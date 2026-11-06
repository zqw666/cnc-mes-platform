using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataCommonOperate;

namespace MachineMonitor
{
    /// <summary>
    /// 流程模板管理。
    /// 目录结构（均在 ExeDir/flows 下）：
    ///   &lt;name&gt;.json   每个模板一个文件，内容就是原来的 flow.json 格式
    ///   _meta.json      模板的显示名/备注（FlowConfig.ToJson 会丢未知字段，所以单独存）
    ///   .current        当前生效的模板名，重启后恢复
    /// 设计原则：模板文件格式与旧 flow.json 完全一致，运维可以脱离界面直接改。
    /// </summary>
    public static class FlowTemplateService
    {
        // ==================== 元数据 ====================

        static Dictionary<string, object> LoadMeta()
        {
            try
            {
                if (!File.Exists(AppState.MetaPath)) return new Dictionary<string, object>();
                var d = AppState.Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(AppState.MetaPath, Encoding.UTF8));
                return d ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Flow] 读取 _meta.json 失败: " + ex.Message);
                return new Dictionary<string, object>();
            }
        }

        static void SaveMeta(Dictionary<string, object> meta)
        {
            try
            {
                File.WriteAllText(AppState.MetaPath, AppState.Js.Serialize(meta), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Flow] 写入 _meta.json 失败: " + ex.Message);
            }
        }

        static string MetaStr(Dictionary<string, object> meta, string name, string key)
        {
            object o;
            if (!meta.TryGetValue(name, out o)) return "";
            var d = o as Dictionary<string, object>;
            if (d == null) return "";
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        static void SetMeta(Dictionary<string, object> meta, string name, string display, string note, string createdAt)
        {
            if (string.IsNullOrEmpty(createdAt)) createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            meta[name] = new Dictionary<string, object>
            {
                { "display", display ?? "" },
                { "note", note ?? "" },
                { "createdAt", createdAt }
            };
        }

        // ==================== 工具 ====================

        static string Err(string msg) { return AppState.Json(new { success = false, message = msg }); }

        static Dictionary<string, object> Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try { return AppState.Js.Deserialize<Dictionary<string, object>>(raw); }
            catch { return null; }
        }

        static List<string> TemplateNames()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(AppState.FlowsDir)) return list;
                foreach (var f in Directory.GetFiles(AppState.FlowsDir, "*.json"))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (n.StartsWith("_") || n.StartsWith(".")) continue;
                    if (!AppState.IsValidFlowName(n)) continue;
                    list.Add(n);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Flow] 扫描模板目录失败: " + ex.Message);
            }
            return list;
        }

        // ==================== 接口实现 ====================

        /// <summary>模板列表 + 当前模板 + 运行状态</summary>
        public static string List()
        {
            var meta = LoadMeta();
            var items = new List<object>();

            foreach (var name in TemplateNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string path = AppState.FlowPath(name);
                FlowConfig cfg;
                bool ok = FlowConfig.TryLoadFrom(path, out cfg);

                int total = 0, enabled = 0;
                string mode = "";
                bool simulate = false;
                if (ok && cfg.steps != null)
                {
                    total = cfg.steps.Count;
                    enabled = cfg.steps.Count(s => s.enabled);
                    mode = cfg.mode;
                    simulate = cfg.simulate;
                }

                string display = MetaStr(meta, name, "display");

                items.Add(new
                {
                    name = name,
                    display = string.IsNullOrEmpty(display) ? name : display,
                    note = MetaStr(meta, name, "note"),
                    stepCount = total,
                    enabledCount = enabled,
                    mode = mode,
                    simulate = simulate,
                    broken = !ok,
                    current = string.Equals(name, AppState.CurrentFlowName, StringComparison.OrdinalIgnoreCase),
                    mtime = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss")
                });
            }

            return AppState.Json(new
            {
                success = true,
                current = AppState.CurrentFlowName,
                running = AppState.WorkflowRunning,
                items = items
            });
        }

        /// <summary>
        /// 新建模板。body: { name, display?, note?, from? }
        /// from 省略 → 复制当前模板；from 传空串 → 从空白模板开始（全部环节停用，强制仿真）。
        /// </summary>
        public static string Create(string raw)
        {
            if (AppState.WorkflowRunning) return Err("工作流运行中，请先停止再新建模板");

            var d = Parse(raw);
            if (d == null) return Err("请求体格式错误");

            string name = AppState.GetStr(d, "name", "").Trim();
            string display = AppState.GetStr(d, "display", "").Trim();
            string note = AppState.GetStr(d, "note", "").Trim();
            string from = d.ContainsKey("from")
                ? AppState.GetStr(d, "from", "").Trim()
                : AppState.CurrentFlowName;

            if (!AppState.IsValidFlowName(name))
                return Err("模板名只能用字母、数字、下划线、连字符，长度 1-32（中文名请填在「显示名」里）");

            string path = AppState.FlowPath(name);
            if (File.Exists(path))
                return Err("模板 " + name + " 已存在");

            FlowConfig cfg;
            if (string.IsNullOrEmpty(from))
            {
                // 空白模板：全部停用 + 仿真，避免"新建完点启动就跑真机"
                cfg = FlowConfig.CreateDefault();
                foreach (var s in cfg.steps) s.enabled = false;
                cfg.simulate = true;
                cfg.mode = FlowConfig.ModeOnce;
            }
            else
            {
                if (!AppState.IsValidFlowName(from))
                    return Err("源模板名不合法");
                if (!FlowConfig.TryLoadFrom(AppState.FlowPath(from), out cfg))
                    return Err("源模板 " + from + " 不存在或读取失败");
            }

            if (!FlowConfig.SaveTo(cfg, path))
                return Err("写入模板失败，请检查目录权限");

            var meta = LoadMeta();
            SetMeta(meta, name, display, note, null);
            SaveMeta(meta);

            LogHelper.Info("[Flow] 新建模板 " + name +
                           (string.IsNullOrEmpty(from) ? "（空白）" : "（复制自 " + from + "）") +
                           "，共 " + (cfg.steps == null ? 0 : cfg.steps.Count) + " 步");

            return AppState.Json(new { success = true, message = "已创建模板 " + name, name = name });
        }

        /// <summary>切换当前模板。body: { name }</summary>
        public static string Switch(string raw)
        {
            if (AppState.WorkflowRunning)
                return Err("工作流运行中，切换模板会改变执行内容，请先停止");

            var d = Parse(raw);
            if (d == null) return Err("请求体格式错误");

            string name = AppState.GetStr(d, "name", "").Trim();
            if (!AppState.IsValidFlowName(name))
                return Err("模板名不合法");
            if (!File.Exists(AppState.FlowPath(name)))
                return Err("模板 " + name + " 不存在");

            if (string.Equals(name, AppState.CurrentFlowName, StringComparison.OrdinalIgnoreCase))
                return AppState.Json(new { success = true, message = "已是当前模板", current = name });

            if (!AppState.SetCurrentFlow(name))
                return Err("记录当前模板失败，请检查目录权限");

            LogHelper.Info("[Flow] 已切换模板 → " + name);
            return AppState.Json(new { success = true, message = "已切换到 " + name, current = name });
        }

        /// <summary>删除模板。body: { name }</summary>
        public static string Delete(string raw)
        {
            if (AppState.WorkflowRunning) return Err("工作流运行中，请先停止再删除模板");

            var d = Parse(raw);
            if (d == null) return Err("请求体格式错误");

            string name = AppState.GetStr(d, "name", "").Trim();
            if (!AppState.IsValidFlowName(name)) return Err("模板名不合法");
            if (string.Equals(name, AppState.CurrentFlowName, StringComparison.OrdinalIgnoreCase))
                return Err("不能删除正在使用的模板，请先切换到其它模板");
            if (!File.Exists(AppState.FlowPath(name)))
                return Err("模板 " + name + " 不存在");
            if (TemplateNames().Count <= 1)
                return Err("至少要保留一个模板");

            try
            {
                File.Delete(AppState.FlowPath(name));
            }
            catch (Exception ex)
            {
                return Err("删除失败: " + ex.Message);
            }

            var meta = LoadMeta();
            meta.Remove(name);
            SaveMeta(meta);

            LogHelper.Info("[Flow] 已删除模板 " + name);
            return AppState.Json(new { success = true, message = "已删除 " + name });
        }

        /// <summary>
        /// 重命名 / 改显示名。body: { name, newName?, display?, note? }
        /// newName 省略或留空 → 只改显示名与备注，不动文件。
        /// </summary>
        public static string Rename(string raw)
        {
            if (AppState.WorkflowRunning) return Err("工作流运行中，请先停止再修改模板");

            var d = Parse(raw);
            if (d == null) return Err("请求体格式错误");

            string name = AppState.GetStr(d, "name", "").Trim();
            string newName = AppState.GetStr(d, "newName", "").Trim();
            string display = AppState.GetStr(d, "display", "").Trim();
            string note = AppState.GetStr(d, "note", "").Trim();

            if (!AppState.IsValidFlowName(name)) return Err("模板名不合法");
            if (!File.Exists(AppState.FlowPath(name))) return Err("模板 " + name + " 不存在");

            var meta = LoadMeta();
            string createdAt = MetaStr(meta, name, "createdAt");

            // 只改显示信息
            if (string.IsNullOrEmpty(newName) || string.Equals(newName, name, StringComparison.OrdinalIgnoreCase))
            {
                SetMeta(meta, name, display, note, createdAt);
                SaveMeta(meta);
                return AppState.Json(new { success = true, message = "已更新模板信息", name = name });
            }

            if (!AppState.IsValidFlowName(newName)) return Err("新模板名不合法");
            if (File.Exists(AppState.FlowPath(newName))) return Err("模板 " + newName + " 已存在");

            try
            {
                File.Move(AppState.FlowPath(name), AppState.FlowPath(newName));
            }
            catch (Exception ex)
            {
                return Err("重命名失败: " + ex.Message);
            }

            if (string.Equals(name, AppState.CurrentFlowName, StringComparison.OrdinalIgnoreCase))
                AppState.SetCurrentFlow(newName);

            string oldDisplay = MetaStr(meta, name, "display");
            meta.Remove(name);
            SetMeta(meta, newName, string.IsNullOrEmpty(display) ? oldDisplay : display, note, createdAt);
            SaveMeta(meta);

            LogHelper.Info("[Flow] 模板重命名 " + name + " → " + newName);
            return AppState.Json(new { success = true, message = "已重命名为 " + newName, name = newName });
        }
    }
}
