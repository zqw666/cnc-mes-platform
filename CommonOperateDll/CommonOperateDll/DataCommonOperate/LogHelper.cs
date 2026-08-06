using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DataCommonOperate
{
    /// <summary>
    /// 统一日志工具类 — 提供控制台输出、文件日志、HTTP 上报后端（写入 process_log）功能
    /// 使用前需初始化配置（在 Main 开头调用 LogHelper.Configure）
    /// </summary>
    public static class LogHelper
    {
        // ==================== 配置属性 ====================

        /// <summary>后端 API 基地址，用于上报日志到 process_log 表</summary>
        public static string ApiBase { get; set; } = "http://localhost:8888";

        /// <summary>当前流程名称（如 load_machine）</summary>
        public static string StepName { get; set; } = "";

        /// <summary>当前流程显示名称（如上机流程）</summary>
        public static string DisplayName { get; set; } = "";

        /// <summary>日志级别过滤：低于此级别的日志不输出。可选 DEBUG/INFO/WARN/ERROR</summary>
        public static string Level { get; set; } = "INFO";

        /// <summary>是否输出到控制台</summary>
        public static bool ConsoleEnabled { get; set; } = true;

        /// <summary>是否上报到后端（写入 process_log 表）</summary>
        public static bool DatabaseEnabled { get; set; } = true;

        /// <summary>是否启用文件日志</summary>
        public static bool FileEnabled { get; set; } = false;

        /// <summary>文件日志目录（默认 exe 所在目录下的 logs/ 文件夹）</summary>
        public static string FileDirectory { get; set; } = "";

        /// <summary>默认操作人</summary>
        public static string DefaultOperator { get; set; } = "system";

        // ==================== 便捷方法 ====================

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg) => Write("ERROR", msg);
        public static void Debug(string msg) => Write("DEBUG", msg);
        public static void Success(string msg) => Write("SUCCESS", msg);

        // ==================== 核心方法 ====================

        static readonly string[] LevelOrder = { "DEBUG", "INFO", "WARN", "ERROR" };

        /// <summary>一次配置（在 Main 开头调用）</summary>
        public static void Configure(string stepName, string displayName, string apiBase = null,
            string level = null, bool? consoleEnabled = null, bool? databaseEnabled = null,
            bool? fileEnabled = null, string fileDir = null, string operatorName = null)
        {
            StepName = stepName;
            DisplayName = displayName;
            if (apiBase != null) ApiBase = apiBase;
            if (level != null) Level = level;
            if (consoleEnabled.HasValue) ConsoleEnabled = consoleEnabled.Value;
            if (databaseEnabled.HasValue) DatabaseEnabled = databaseEnabled.Value;
            if (fileEnabled.HasValue) FileEnabled = fileEnabled.Value;
            if (fileDir != null) FileDirectory = fileDir;
            if (operatorName != null) DefaultOperator = operatorName;
        }

        /// <summary>配置的快捷方式：从 config.json 字典读取</summary>
        public static void ConfigureFromDict(System.Collections.Generic.Dictionary<string, object> cfg, string stepName, string displayName)
        {
            StepName = stepName;
            DisplayName = displayName;
            try
            {
                if (cfg.ContainsKey("log"))
                {
                    var logCfg = cfg["log"] as System.Collections.Generic.Dictionary<string, object>;
                    if (logCfg != null)
                    {
                        if (logCfg.ContainsKey("level")) Level = logCfg["level"].ToString();
                        if (logCfg.ContainsKey("console")) ConsoleEnabled = Convert.ToBoolean(logCfg["console"]);
                        if (logCfg.ContainsKey("database")) DatabaseEnabled = Convert.ToBoolean(logCfg["database"]);
                        if (logCfg.ContainsKey("file")) FileEnabled = Convert.ToBoolean(logCfg["file"]);
                        if (logCfg.ContainsKey("fileDir")) FileDirectory = logCfg["fileDir"].ToString();
                        if (logCfg.ContainsKey("operator")) DefaultOperator = logCfg["operator"].ToString();
                    }
                }
            }
            catch { }
        }

        /// <summary>核心写入方法</summary>
        static void Write(string level, string content)
        {
            // 1. 级别过滤
            if (!ShouldLog(level)) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string msg = $"[{timestamp}][{level}][{StepName}] {content}";

            // 2. 控制台输出
            if (ConsoleEnabled)
                Console.WriteLine(msg);

            // 3. 文件日志
            if (FileEnabled)
                WriteToFile(level, msg);

            // 4. 上报后端（异步，不阻塞）
            if (DatabaseEnabled && !string.IsNullOrEmpty(ApiBase))
            {
                string body = JsonSerialize(new { step = StepName, displayName = DisplayName, level, content = msg });
                Task.Run(() => HttpPost(body));
            }
        }

        /// <summary>判断是否应输出该级别</summary>
        static bool ShouldLog(string level)
        {
            int currentLevelIndex = Array.IndexOf(LevelOrder, Level.ToUpper());
            int targetLevelIndex = Array.IndexOf(LevelOrder, level.ToUpper());
            if (currentLevelIndex < 0) currentLevelIndex = 1; // 默认 INFO
            if (targetLevelIndex < 0) targetLevelIndex = 1;
            return targetLevelIndex >= currentLevelIndex;
        }

        /// <summary>写入文件日志</summary>
        static void WriteToFile(string level, string msg)
        {
            try
            {
                string dir = string.IsNullOrEmpty(FileDirectory)
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")
                    : FileDirectory;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string date = DateTime.Now.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(dir, $"{date}_{StepName}_{level}.log");
                File.AppendAllText(filePath, msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>HTTP POST 到后端</summary>
        static void HttpPost(string body)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                var req = WebRequest.CreateHttp(ApiBase + "/api/sub/log");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 3000;
                using (var rs = req.GetRequestStream())
                    rs.Write(bytes, 0, bytes.Length);
                try { req.GetResponse().Close(); } catch { }
            }
            catch { }
        }

        /// <summary>简单 JSON 序列化（避免依赖 System.Web.Extensions）</summary>
        static string JsonSerialize(object obj)
        {
            var sb = new StringBuilder();
            var dict = obj as System.Collections.Generic.Dictionary<string, object>;
            if (dict != null)
            {
                sb.Append("{");
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append($"\"{EscapeJson(kv.Key)}\":");
                    if (kv.Value is string s)
                        sb.Append($"\"{EscapeJson(s)}\"");
                    else
                        sb.Append(kv.Value?.ToString() ?? "null");
                }
                sb.Append("}");
                return sb.ToString();
            }
            // 兜底：用属性名反射（简化为已知结构）
            var type = obj.GetType();
            sb.Append("{");
            var props = type.GetProperties();
            for (int i = 0; i < props.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{EscapeJson(props[i].Name)}\":");
                var val = props[i].GetValue(obj);
                if (val is string s)
                    sb.Append($"\"{EscapeJson(s)}\"");
                else
                    sb.Append(val?.ToString() ?? "null");
            }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>JSON 字符串转义</summary>
        static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
