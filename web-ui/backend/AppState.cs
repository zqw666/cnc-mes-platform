using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace MachineMonitor
{
    public static class AppState
    {
        public static readonly string ExeDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
        public static readonly string ConfigPath;
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

        public static readonly Dictionary<string, string> StepDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "rackoperate", "料架操作"},
            { "scan", "扫描"},
            { "preparematerial", "备料"},
            { "loadmachine", "上机"},
            { "unloadmachine", "下机"},
            { "unloadmaterial", "下料"},
            { "materialknife", "备刀"},
            { "changemachine", "换机"},
            { "changeknife", "换刀"}
        };

        static AppState()
        {
            var p = Path.Combine(ExeDir, "config.json");
            ConfigPath = File.Exists(p) ? p : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            SubExeDir = Path.Combine(ExeDir, "subs");

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

