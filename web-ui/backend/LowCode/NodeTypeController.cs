using System;
using System.IO;
using System.Text;
using DataCommonOperate;

namespace MachineMonitor
{
    /// <summary>
    /// 环节元数据接口。前端环节库与属性面板都从这里取定义。
    /// 数据源：ExeDir/node-types.json（单一真源，改这个文件即可增删环节，不用改代码）
    /// </summary>
    public static class NodeTypeController
    {
        const string FileName = "node-types.json";

        static string FilePath()
        {
            return Path.Combine(AppState.ExeDir, FileName);
        }

        /// <summary>
        /// 找源码目录里的那份 node-types.json（开发机才有）。
        /// 做法是从 ExeDir 往上找含 MachineMonitor.csproj 的目录 —— 找到就是工程根。
        /// 现场部署时 bin 被单独拷到工控机，往上找不到 csproj，返回 null，只改运行时文件。
        /// </summary>
        static string FindSourcePath()
        {
            try
            {
                var dir = new DirectoryInfo(AppState.ExeDir);
                for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "MachineMonitor.csproj")))
                        return Path.Combine(dir.FullName, FileName);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[NodeType] 查找源码目录失败: " + ex.Message);
            }
            return null;
        }

        /// <summary>返回 node-types.json 原文，前端直接 JSON.parse</summary>
        public static string GetRaw()
        {
            try
            {
                var p = FilePath();
                if (!File.Exists(p))
                {
                    LogHelper.Warn("[NodeType] 文件不存在: " + p);
                    return AppState.Json(new { success = false, message = "node-types.json 不存在: " + p });
                }

                var text = File.ReadAllText(p, Encoding.UTF8).TrimStart('\uFEFF');
                return text;
            }
            catch (Exception ex)
            {
                LogHelper.Error("[NodeType] 读取失败: " + ex.Message);
                return AppState.Json(new { success = false, message = "读取失败: " + ex.Message });
            }
        }

        /// <summary>保存节点定义。写之前先校验 JSON 合法性，并保留一份 .bak</summary>
        public static string Save(string raw)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return AppState.Json(new { success = false, message = "内容为空" });

                // 校验：必须能反序列化成字典，且含 nodes 数组
                var dict = AppState.Js.Deserialize<System.Collections.Generic.Dictionary<string, object>>(raw);
                if (dict == null)
                    return AppState.Json(new { success = false, message = "不是合法的 JSON 对象" });
                if (!dict.ContainsKey("nodes"))
                    return AppState.Json(new { success = false, message = "缺少 nodes 字段" });

                var p = FilePath();
                if (File.Exists(p))
                {
                    try { File.Copy(p, p + ".bak", true); }
                    catch (Exception bex) { LogHelper.Warn("[NodeType] 备份失败: " + bex.Message); }
                }

                File.WriteAllText(p, raw, new UTF8Encoding(false));

                // 开发机上顺手把源码里的那份也写掉，否则下次 dotnet build 会把界面上的改动
                // 用旧源文件覆盖回去（PreserveNewest 在源文件更新时会覆盖输出目录）。
                bool syncedSource = false;
                string src = FindSourcePath();
                if (src != null && !string.Equals(src, p, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.WriteAllText(src, raw, new UTF8Encoding(false));
                        syncedSource = true;
                    }
                    catch (Exception sex)
                    {
                        LogHelper.Warn("[NodeType] 同步源码副本失败: " + sex.Message);
                    }
                }

                LogHelper.Info("[NodeType] 已保存，长度 " + raw.Length + (syncedSource ? "（已同步源码副本）" : ""));

                return AppState.Json(new
                {
                    success = true,
                    syncedSource = syncedSource,
                    message = syncedSource
                        ? "已保存，并同步到源码 backend/node-types.json"
                        : "已保存（仅运行时文件）"
                });
            }
            catch (Exception ex)
            {
                LogHelper.Error("[NodeType] 保存失败: " + ex.Message);
                return AppState.Json(new { success = false, message = "保存失败: " + ex.Message });
            }
        }
    }
}
