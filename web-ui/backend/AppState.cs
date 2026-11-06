using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using DataCommonOperate;

namespace MachineMonitor
{
    public static class AppState
    {
        public static readonly string ExeDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
        public static readonly string ConfigPath;
        /// <summary>流程模板目录（ExeDir/flows），每个模板一个 &lt;name&gt;.json</summary>
        public static readonly string FlowsDir;
        /// <summary>旧版单文件流程配置（ExeDir/flow.json），仅作首次迁移的种子</summary>
        public static readonly string LegacyFlowPath;
        /// <summary>默认模板名</summary>
        public const string DefaultFlowName = "default";
        /// <summary>当前生效的模板文件名（不含扩展名）</summary>
        static volatile string _currentFlow = DefaultFlowName;
        /// <summary>工序编排配置文件（唯一真源，替代原 work_flow 表）。随当前模板动态解析。</summary>
        public static string FlowConfigPath { get { return FlowPath(_currentFlow); } }
        public static string CurrentFlowName { get { return _currentFlow; } }
        public static readonly string SubExeDir;
        public static readonly string ConnStr;
        public static readonly string DbType;
        public static readonly int Port;
        public static readonly int LogCapacity;
        public static readonly JavaScriptSerializer Js = new JavaScriptSerializer();

        public static readonly ConcurrentQueue<LogEntry> AllLogs = new ConcurrentQueue<LogEntry>();
        public static volatile bool WorkflowRunning = false;
        public static volatile bool WorkflowPaused = false;
        public static System.Threading.CancellationTokenSource WorkflowCts;
        public static readonly object LogLock = new object();

        /// <summary>当前正在运行的子程序进程，用于停止时强制结束</summary>
        public static volatile System.Diagnostics.Process CurrentProcess;

        /// <summary>各步骤最近一次运行状态：idle/running/completed/failed/skipped</summary>
        public static readonly ConcurrentDictionary<string, string> StepRuntimeStatus =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>料架数据版本号，子程序更新料位后递增，前端据此刷新</summary>
        public static volatile int RackDataVersion = 0;
        public static void NotifyRackDataChanged() { System.Threading.Interlocked.Increment(ref RackDataVersion); }

        /// <summary>日志自增序号，用于前端识别新增日志</summary>
        public static long LogSeq = 0;

        public static readonly Dictionary<string, string> StepDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "rackoperate", "料架操作"},
            { "scan", "扫描流程"},
            { "scan_preset", "扫描预设台流程"},
            { "prepare_material", "备料流程"},
            { "load_machine", "上机流程"},
            { "change_machine", "换料流程"},
            { "unload_machine", "下机流程"},
            { "unload_material", "下料流程"},
            { "load_aluminum", "铝板电极上机流程"},
            { "unload_aluminum", "铝板电极下机流程"},
            { "fill_fixture", "空夹具补充"},
            { "prepare_knife", "备刀"},
            { "change_knife", "换刀"},
            { "sendkniferequest", "发送换刀请求"}
        };

        /// <summary>
        /// 默认工作流顺序（料架操作 → 扫描 → 扫描预设台 → 备料 → 上机 → 空夹具补充 → 换料 → 下机 → 下料 → 铝板电极上机 → 铝板电极下机）
        /// processname 与 subs 目录下的 exe 文件名一致。
        /// 注意：自 flow.json 生效后，该列表仅作为「首次生成配置的种子」和「补充缺失步骤的依据」，
        /// 实际执行顺序以 ExeDir/flow.json 为准。
        /// </summary>
        public static readonly List<string> DefaultWorkflowOrder = new List<string>
        {
            "rackoperate", "scan", "scan_preset", "prepare_material", "load_machine", "fill_fixture", "change_machine", "unload_machine", "unload_material", "load_aluminum", "unload_aluminum"
        };

        static AppState()
        {
            var p = Path.Combine(ExeDir, "config.json");
            ConfigPath = File.Exists(p) ? p : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            SubExeDir = Path.Combine(ExeDir, "subs");
            FlowsDir = Path.Combine(ExeDir, "flows");
            LegacyFlowPath = Path.Combine(ExeDir, "flow.json");
            InitFlows();

            var cfg = File.Exists(ConfigPath)
                ? Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath))
                : new Dictionary<string, object>();

            var svr = GetDict(cfg, "server");
            Port = GetInt(svr, "port", 8888);
            LogCapacity = GetInt(svr, "logCapacity", 1000);

            var db = GetDict(cfg, "database");
            DbType = GetStr(db, "type", "mysql").ToLower();
            string dbName = GetStr(db, "database", "qianduantest");

            if (DbType == "sqlserver")
            {
                ConnStr = "Server=" + GetStr(db, "host", "localhost") + "," + GetInt(db, "port", 1433) + ";"
                    + "Database=" + dbName + ";"
                    + "User Id=" + GetStr(db, "user", "sa") + ";"
                    + "Password=" + GetStr(db, "password", "123456") + ";";
            }
            else
            {
                ConnStr = "Server=" + GetStr(db, "host", "localhost") + ";"
                    + "Port=" + GetInt(db, "port", 3306) + ";"
                    + "Database=" + dbName + ";"
                    + "Uid=" + GetStr(db, "user", "root") + ";"
                    + "Pwd=" + GetStr(db, "password", "123456") + ";"
                    + "CharacterSet=utf8mb4;"
                    + "AllowPublicKeyRetrieval=True;"
                    + "SslMode=None;";
            }
        }

        // ==================== 流程模板 ====================

        /// <summary>模板名 → 配置文件绝对路径</summary>
        public static string FlowPath(string name)
        {
            if (string.IsNullOrEmpty(name)) name = DefaultFlowName;
            return Path.Combine(FlowsDir, name + ".json");
        }

        public static string CurrentMarkerPath { get { return Path.Combine(FlowsDir, ".current"); } }
        public static string MetaPath { get { return Path.Combine(FlowsDir, "_meta.json"); } }

        /// <summary>模板名只允许字母数字下划线连字符，长度 1-32，避免路径穿越与中文文件名问题</summary>
        public static bool IsValidFlowName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 32) return false;
            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                          || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>切换当前模板并落盘记忆。调用方需自行保证非运行中。</summary>
        public static bool SetCurrentFlow(string name)
        {
            if (!IsValidFlowName(name)) return false;
            _currentFlow = name;
            try
            {
                File.WriteAllText(CurrentMarkerPath, name, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Flow] 记录当前模板失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 初始化模板目录：
        /// 1) 目录不存在则创建；
        /// 2) flows/default.json 不存在而旧的 ExeDir/flow.json 存在 → 复制过来（旧文件保留，可回滚）；
        /// 3) 从 .current 恢复上次使用的模板，失效则回落 default。
        /// </summary>
        static void InitFlows()
        {
            try
            {
                if (!Directory.Exists(FlowsDir)) Directory.CreateDirectory(FlowsDir);

                string def = FlowPath(DefaultFlowName);
                if (!File.Exists(def) && File.Exists(LegacyFlowPath))
                {
                    File.Copy(LegacyFlowPath, def, false);
                    LogHelper.Info("[Flow] 已把 flow.json 复制为模板 flows/default.json（原文件保留）");
                }

                string cur = "";
                if (File.Exists(CurrentMarkerPath))
                    cur = File.ReadAllText(CurrentMarkerPath, Encoding.UTF8).Trim();

                _currentFlow = (IsValidFlowName(cur) && File.Exists(FlowPath(cur))) ? cur : DefaultFlowName;
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Flow] 初始化模板目录失败，使用 default: " + ex.Message);
                _currentFlow = DefaultFlowName;
            }
        }

        public static string Json(object obj) { return Js.Serialize(obj); }
        public static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key) || d[key] == null) return new Dictionary<string, object>();
            if (d[key] is Dictionary<string, object> nested) return nested;
            try { return Js.Deserialize<Dictionary<string, object>>(d[key].ToString()) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }
        public static string GetStr(Dictionary<string, object> d, string key, string def = "")
        {
            return d != null && d.ContainsKey(key) && d[key] != null ? d[key].ToString() : def;
        }
        public static int GetInt(Dictionary<string, object> d, string key, int def = 0)
        {
            return d != null && d.ContainsKey(key) && d[key] != null ? Convert.ToInt32(d[key]) : def;
        }
        public static string SafeStr(object val) { return val == null || val == DBNull.Value ? "" : val.ToString(); }
        public static int SafeInt(object val) { return val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val); }
        public static long SafeLong(object val) { return val == null || val == DBNull.Value ? 0 : Convert.ToInt64(val); }
        public static string SafeDateTime(object val)
        {
            return val == null || val == DBNull.Value ? "" : Convert.ToDateTime(val).ToString("yyyy-MM-dd HH:mm:ss");
        }
        public static List<object> GetJsonArray(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key) || d[key] == null) return new List<object>();
            var arr = d[key];
            if (arr is ArrayList al) { var list = new List<object>(); foreach (var item in al) list.Add(item); return list; }
            if (arr is object[] oa) { var list = new List<object>(); foreach (var item in oa) list.Add(item); return list; }
            return new List<object>();
        }
    }
}

