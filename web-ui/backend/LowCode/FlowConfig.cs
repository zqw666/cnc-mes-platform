using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataCommonOperate;

namespace MachineMonitor
{
    /// <summary>单个工序步骤的配置（对应 flow.json 里 steps 数组的一项）</summary>
    public class FlowStepConfig
    {
        /// <summary>子程序名，与 subs 目录下 exe 文件名一致（不带扩展名）</summary>
        public string name { get; set; }
        /// <summary>界面显示名</summary>
        public string display { get; set; }
        /// <summary>是否参与执行（对应"勾选启用"）</summary>
        public bool enabled { get; set; }
        /// <summary>单步超时秒数，超时强制结束进程</summary>
        public int timeoutSec { get; set; }
        /// <summary>失败策略：stop=停在失败步，skip=跳过继续</summary>
        public string onFail { get; set; }
        /// <summary>仿真模式下该步骤的模拟耗时（毫秒）</summary>
        public int simMs { get; set; }
        /// <summary>仿真模式下是否模拟失败（用于验证失败分支）</summary>
        public bool simFail { get; set; }

        public FlowStepConfig Clone()
        {
            return new FlowStepConfig
            {
                name = this.name,
                display = this.display,
                enabled = this.enabled,
                timeoutSec = this.timeoutSec,
                onFail = this.onFail,
                simMs = this.simMs,
                simFail = this.simFail
            };
        }
    }

    /// <summary>
    /// 工序编排配置。唯一真源是 ExeDir/flow.json；
    /// 首次启动时从 work_flow 表迁移生成，之后数据库表只读不写。
    /// </summary>
    public class FlowConfig
    {
        public int version { get; set; }
        /// <summary>执行模式：once=跑一遍就停，loop=跑完一轮继续</summary>
        public string mode { get; set; }
        /// <summary>轮播模式下两轮之间的间隔（毫秒）</summary>
        public int loopIntervalMs { get; set; }
        /// <summary>仿真模式：不启动真实子程序，只按 simMs 等待</summary>
        public bool simulate { get; set; }
        public List<FlowStepConfig> steps { get; set; }

        public const string ModeOnce = "once";
        public const string ModeLoop = "loop";
        public const string FailStop = "stop";
        public const string FailSkip = "skip";

        // ==================== 默认 / 归一化 ====================

        public static FlowConfig CreateDefault()
        {
            var cfg = new FlowConfig
            {
                version = 1,
                mode = ModeOnce,
                loopIntervalMs = 1000,
                simulate = true,
                steps = new List<FlowStepConfig>()
            };

            foreach (var name in AppState.DefaultWorkflowOrder)
                cfg.steps.Add(NewStep(name));

            return cfg;
        }

        public static FlowStepConfig NewStep(string name)
        {
            string display;
            if (!AppState.StepDisplayNames.TryGetValue(name, out display) || string.IsNullOrEmpty(display))
                display = name;

            return new FlowStepConfig
            {
                name = name,
                display = display,
                enabled = true,
                timeoutSec = 300,
                onFail = FailStop,
                simMs = 3000,
                simFail = false
            };
        }

        /// <summary>把外部传入（含手工编辑）的配置修正到合法状态</summary>
        public FlowConfig Normalize()
        {
            if (version <= 0) version = 1;

            mode = string.Equals(mode, ModeLoop, StringComparison.OrdinalIgnoreCase) ? ModeLoop : ModeOnce;
            if (loopIntervalMs <= 0) loopIntervalMs = 1000;

            if (steps == null) steps = new List<FlowStepConfig>();

            // 去掉无效项、重复项
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = new List<FlowStepConfig>();
            foreach (var s in steps)
            {
                if (s == null) continue;
                if (string.IsNullOrWhiteSpace(s.name)) continue;
                s.name = s.name.Trim().ToLowerInvariant();
                if (!seen.Add(s.name)) continue;

                s.onFail = string.Equals(s.onFail, FailSkip, StringComparison.OrdinalIgnoreCase) ? FailSkip : FailStop;
                if (s.timeoutSec <= 0) s.timeoutSec = 300;
                if (s.simMs <= 0) s.simMs = 3000;

                if (string.IsNullOrWhiteSpace(s.display))
                {
                    string display;
                    s.display = AppState.StepDisplayNames.TryGetValue(s.name, out display) ? display : s.name;
                }

                cleaned.Add(s);
            }
            steps = cleaned;

            return this;
        }

        /// <summary>
        /// 补齐已知但配置里缺失的步骤（追加到末尾并默认停用，避免误跑）。
        /// 来源：StepDisplayNames（人工维护的显示名）+ subs 目录下实际存在的 exe。
        /// 返回补进来的步骤名列表。
        /// </summary>
        public List<string> MergeKnownSteps()
        {
            var added = new List<string>();
            if (steps == null) steps = new List<FlowStepConfig>();

            var existing = new HashSet<string>(steps.Select(s => s.name), StringComparer.OrdinalIgnoreCase);

            // 1. 优先按默认顺序补，保持业务语义顺序
            foreach (var name in AppState.DefaultWorkflowOrder)
            {
                if (existing.Contains(name)) continue;
                var step = NewStep(name);
                step.enabled = false;          // 新增项默认不启用
                steps.Add(step);
                existing.Add(name);
                added.Add(name);
            }

            // 2. 目录里存在但配置里没有的 exe，也补进来
            try
            {
                if (Directory.Exists(AppState.SubExeDir))
                {
                    foreach (var exe in Directory.GetFiles(AppState.SubExeDir, "*.exe"))
                    {
                        string name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                        if (existing.Contains(name)) continue;
                        var step = NewStep(name);
                        step.enabled = false;
                        steps.Add(step);
                        existing.Add(name);
                        added.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Flow] 扫描 subs 目录失败: " + ex.Message);
            }

            return added;
        }

        // ==================== 读写 ====================

        public static FlowConfig Load()
        {
            FlowConfig cfg;
            if (TryLoadFrom(AppState.FlowConfigPath, out cfg))
                return cfg;

            return CreateDefault();
        }

        /// <summary>
        /// 从指定文件读取。失败返回 false 且不静默伪造配置——
        /// 模板列表要靠这个区分"文件坏了"和"文件是空的"。
        /// </summary>
        public static bool TryLoadFrom(string path, out FlowConfig cfg)
        {
            cfg = null;
            try
            {
                if (!File.Exists(path)) return false;
                string text = File.ReadAllText(path, Encoding.UTF8);
                var c = AppState.Js.Deserialize<FlowConfig>(text);
                if (c == null) return false;
                cfg = c.Normalize();
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Flow] 读取 " + Path.GetFileName(path) + " 失败: " + ex.Message);
                return false;
            }
        }

        public static bool Save(FlowConfig cfg)
        {
            return SaveTo(cfg, AppState.FlowConfigPath);
        }

        /// <summary>写入指定文件（先写 .tmp 再替换，避免断电写坏）</summary>
        public static bool SaveTo(FlowConfig cfg, string path)
        {
            try
            {
                cfg = cfg.Normalize();
                string json = ToJson(cfg);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error("[Flow] 保存 " + Path.GetFileName(path) + " 失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>手写序列化，保证输出带缩进、人能直接编辑</summary>
        public static string ToJson(FlowConfig c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"version\": " + c.version + ",");
            sb.AppendLine("  \"mode\": \"" + Esc(c.mode) + "\",");
            sb.AppendLine("  \"loopIntervalMs\": " + c.loopIntervalMs + ",");
            sb.AppendLine("  \"simulate\": " + (c.simulate ? "true" : "false") + ",");
            sb.AppendLine("  \"steps\": [");

            for (int i = 0; i < c.steps.Count; i++)
            {
                var s = c.steps[i];
                sb.Append("    { \"name\": \"" + Esc(s.name) + "\"");
                sb.Append(", \"display\": \"" + Esc(s.display) + "\"");
                sb.Append(", \"enabled\": " + (s.enabled ? "true" : "false"));
                sb.Append(", \"timeoutSec\": " + s.timeoutSec);
                sb.Append(", \"onFail\": \"" + Esc(s.onFail) + "\"");
                sb.Append(", \"simMs\": " + s.simMs);
                sb.Append(", \"simFail\": " + (s.simFail ? "true" : "false") + " }");
                if (i < c.steps.Count - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        static string Esc(string v)
        {
            if (v == null) return "";
            return v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        /// <summary>
        /// 首次启动迁移：flow.json 不存在时，
        /// 先尝试从 work_flow 表按 priority 还原顺序，再补齐已知步骤，最后落盘。
        /// </summary>
        public static FlowConfig EnsureExists()
        {
            if (File.Exists(AppState.FlowConfigPath))
            {
                var cfg = Load();
                var added = cfg.MergeKnownSteps();
                if (added.Count > 0)
                {
                    Save(cfg);
                    LogHelper.Info("[Flow] 已补充步骤到 flow.json: " + string.Join(", ", added.ToArray()));
                }
                return cfg;
            }

            FlowConfig created = null;

            // 从旧表迁移
            try
            {
                var db = DatabaseRepository.CreateDb();
                var dt = db.ExeForDtl("SELECT processname, status, priority FROM work_flow ORDER BY priority DESC, id ASC");
                if (dt != null && dt.Rows.Count > 0)
                {
                    created = new FlowConfig
                    {
                        version = 1,
                        mode = ModeOnce,
                        loopIntervalMs = 1000,
                        simulate = true,
                        steps = new List<FlowStepConfig>()
                    };

                    foreach (System.Data.DataRow dr in dt.Rows)
                    {
                        string name = AppState.SafeStr(dr["processname"]).Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(name)) continue;
                        if (created.steps.Any(s => string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase))) continue;

                        var step = NewStep(name);
                        step.enabled = AppState.SafeInt(dr["status"]) == 1;
                        created.steps.Add(step);
                    }

                    if (created.steps.Count > 0)
                        LogHelper.Info("[Flow] 已从 work_flow 表迁移 " + created.steps.Count + " 个步骤");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Flow] 从 work_flow 迁移失败，使用默认顺序: " + ex.Message);
            }

            if (created == null || created.steps.Count == 0)
                created = CreateDefault();

            created.Normalize();
            created.MergeKnownSteps();
            Save(created);
            LogHelper.Info("[Flow] 已生成编排配置: " + AppState.FlowConfigPath + "（共 " + created.steps.Count + " 步）");
            return created;
        }
    }
}
