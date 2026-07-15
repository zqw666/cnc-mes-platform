using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using DataCommonOperate;
using PLC;

namespace change_knife
{
    class Program
    {
        // 配置
        static string ConfigPath = ResolveConfigPath();
        static string ResolveConfigPath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                string c = Path.Combine(dir.FullName, "config.json");
                if (File.Exists(c)) return c;
                dir = dir.Parent;
            }
            return Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).FullName, "config.json");
        }

        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "change_knife";
        static readonly string DisplayName = "换刀流程";

        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;

        // ==================== MC1刀库旋转控制信号 ====================
        static readonly string MC1DoorClosed  = "101.01";  // MC1刀库门关闭信号
        static readonly int    MC1TaskDM      = 200;       // MC1刀库任务信号（DM区）
        static readonly string MC1RotateDone  = "105.00";  // MC1刀库转到位信号
        static readonly string MC1RotateStart = "104.01";  // MC1刀库旋转开始信号

        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharSet=utf8;";
        static string DbType = "mysql";

        static void Main(string[] args)
        {
            Log("INFO", "【" + DisplayName + "】分包启动");

            try
            {
                if (args != null && args.Length > 0 && File.Exists(args[0]))
                    ConfigPath = args[0];

                LoadConfig();

                var plc = new plc();
                if (!plc.Connect(PlcIp, PlcPort))
                {
                    Log("ERROR", "PLC 连接失败: " + PlcIp + ":" + PlcPort);
                    return;
                }
                Log("INFO", "PLC 连接成功: " + PlcIp);

                // 刀库旋转：依次旋转1~10号刀
                for (int t = 1; t <= 10; t++)
                    RunMC1Magazine(plc, t);

                Log("SUCCESS", "刀库旋转完成");
            }
            catch (Exception ex)
            {
                Log("ERROR", "执行异常: " + ex.Message);
            }

            Log("INFO", "【" + DisplayName + "】分包结束");
        }

        static void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));

                if (cfg.ContainsKey("database"))
                {
                    var db = GetDict(cfg, "database");
                    string host = GetStr(db, "host", "localhost");
                    string dbname = GetStr(db, "database", "qianduantest");
                    string user = GetStr(db, "user", "root");
                    string pwd = GetStr(db, "password", "123456");
                    DbType = GetStr(db, "type", "mysql").ToLower();
                    if (DbType == "sqlserver")
                        ConnStr = $"Server={host},{GetInt(db, "port", 1433)};Database={dbname};User Id={user};Password={pwd};";
                    else
                        ConnStr = $"Server={host};Port={GetInt(db, "port", 3306)};Database={dbname};Uid={user};Pwd={pwd};CharSet=utf8;";
                }

                if (cfg.ContainsKey("plc"))
                {
                    var plccfg = GetDict(cfg, "plc");
                    PlcIp = GetStr(plccfg, "ip", PlcIp);
                    PlcPort = GetInt(plccfg, "port", PlcPort);
                }
            }
            catch (Exception ex) { Log("WARN", "加载配置失败: " + ex.Message); }
        }

        // ==================== MC1刀库旋转控制 ====================
        /// <summary>
        /// MC1刀库单次旋转控制：检测门关闭后，旋转到指定刀号
        /// </summary>
        static bool RunMC1Magazine(plc plc, int toolNo)
        {
            Log("INFO", "【MC1刀库】检查门关闭信号 " + MC1DoorClosed);
            if (plc.ReadWR(MC1DoorClosed))
            {
                Log("WARN", "【MC1刀库】刀库门未关闭(信号=1)，旋转失败");
                return false;
            }
            plc.WriteWR(MC1RotateStart, 0);
            Log("INFO", "【MC1刀库】旋转至 " + toolNo + " 号刀");
            plc.WriteDM(MC1TaskDM, toolNo);
            plc.WriteWR(MC1RotateStart, 1);

            Log("INFO", "【MC1刀库】等待转到位信号 " + MC1RotateDone);
            while (!plc.ReadWR(MC1RotateDone))
                Thread.Sleep(200);
            plc.WriteWR(MC1RotateStart, 0);
            Log("SUCCESS", "【MC1刀库】" + toolNo + " 号刀旋转到位");
            return true;
        }

        // ==================== 日志 ====================
        static void Log(string level, string content)
        {
            string msg = "[" + level + "] " + content;
            Console.WriteLine(msg);
            try
            {
                var body = Js.Serialize(new { step = StepName, displayName = DisplayName, level, content = msg });
                var bytes = Encoding.UTF8.GetBytes(body);
                var req = WebRequest.CreateHttp(ApiBase + "/api/sub/log");
                req.Method = "POST"; req.ContentType = "application/json"; req.Timeout = 3000;
                using (var rs = req.GetRequestStream()) rs.Write(bytes, 0, bytes.Length);
                try { req.GetResponse().Close(); } catch { }
            }
            catch { }
        }

        // ==================== 辅助 ====================
        static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key) || d[key] == null) return new Dictionary<string, object>();
            if (d[key] is Dictionary<string, object> n) return n;
            try { return Js.Deserialize<Dictionary<string, object>>(d[key].ToString()) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }
        static string GetStr(Dictionary<string, object> d, string key, string def = "") =>
            d.ContainsKey(key) && d[key] != null ? d[key].ToString() : def;
        static int GetInt(Dictionary<string, object> d, string key, int def = 0) =>
            d.ContainsKey(key) && d[key] != null ? Convert.ToInt32(d[key]) : def;
    }
}
