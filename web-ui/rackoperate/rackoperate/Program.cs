// ============================================================================
// 料架操作 - SubProcess（多料架循环巡检版）
// 1. 读取 config.json 中的 racks[] 料架配置
// 2. 循环遍历每个料架（最多3轮）：
//    a. doorClosed=1(门关) → 等5s看是否有人工开门请求
//       - 收到请求且机器人任务完成 → 允许开门 → 等门开(doorClosed→0) → 脉冲doorFlag
//       - 等门关(doorClosed→1) → 读光电 → 更新DB → 退出
//    b. doorClosed=0(门开) → 等5s看门是否关闭
//       - 门关 → 读取光电信号变化 → 更新DB
// 3. 3轮无任何开门请求 → 退出
// ============================================================================
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using DataCommonOperate;
using PLC;

namespace rackoperate
{
    class Program
    {
        // ==================== 料架配置数据结构 ====================
        class RackConfig
        {
            public string Id;
            public string Type;
            public string Name;
            public string Number;
            public int Rows;
            public int Cols;
            public string PhotoSignal;
            public string OpenRequest;     // 人工开门请求信号
            public string OpenAllow;       // 允许开门信号
            public string DoorClosed;      // 门状态信号（1=门关, 0=门开）
            public string DoorFlag;        // 门状态标志信号（置1后500ms复位）
            public string GrabStartTaskNo;
            public string PlaceStartTaskNo;
            public string ScanStartTaskNo;
        }

        // ==================== 配置 ====================
        static string ConfigPath = ResolveConfigPath();

        static string ResolveConfigPath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "config.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).FullName, "config.json");
        }

        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "rack_operate";
        static readonly string DisplayName = "料架操作";

        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
        static string RobotTaskDoneAddr = "";
        static readonly List<RackConfig> Racks = new List<RackConfig>();
        static string StopFlagFile;
        static bool IsStopRequested() { return File.Exists(StopFlagFile); }

        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharSet=utf8;";
        static string DbType = "mysql";

        // ==================== 转台运动控制信号 ====================
        static readonly string SignalReturnZero    = "120.00";  // 回到零点信号
        static readonly string SignalDegree        = "110";     // 移动度数信号（WR字）
        static readonly string SignalAllowMove     = "108.00";  // 允许移动信号
        static readonly string SignalStartMove     = "108.01";  // 开始移动信号
        static readonly string SignalMoveDone      = "108.04";  // 移动到位信号
        static readonly string SignalAtZero        = "108.05";  // 在0度标志位信号
        static readonly int[] AngleList = { 90, 180, 270, 360 }; // 移动角度列表

        // ==================== MC1刀库旋转控制信号 ====================
        static readonly string MC1DoorClosed    = "101.00";  // MC1刀库门关闭信号
        static readonly int    MC1TaskDM        = 200;       // MC1刀库任务信号（DM区）
        static readonly string MC1RotateDone    = "104.05";  // MC1刀库转到位信号
        static readonly string MC1RotateStart   = "104.01";  // MC1刀库旋转开始信号

        // ==================== Main ====================
        static void Main(string[] args)
        {
            Log("INFO", "【" + DisplayName + "】分包启动");

            try
            {
                if (args != null && args.Length > 0 && File.Exists(args[0]))
                    ConfigPath = args[0];

                LoadConfig();

                if (Racks.Count == 0)
                {
                    Log("WARN", "检测到系统没有料架配置，结束料架操作流程");
                    return;
                }
                Log("INFO", "加载料架配置完成，共 " + Racks.Count + " 个料架");

                Work();
            }
            catch (Exception ex)
            {
                Log("ERROR", "执行异常: " + ex.Message);
            }

            Log("INFO", "【" + DisplayName + "】分包结束");
        }

        // ==================== 加载配置 ====================
        static void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) { Log("WARN", "配置文件不存在: " + ConfigPath); return; }
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
                    var plc = GetDict(cfg, "plc");
                    PlcIp = GetStr(plc, "ip", PlcIp);
                    PlcPort = GetInt(plc, "port", PlcPort);
                    var signalsSrc = GetDict(plc, "signals");
                    var addrFallback = GetDict(plc, "addresses");
                    var hasSignals = signalsSrc.Count > 0;
                    if (hasSignals)
                        RobotTaskDoneAddr = GetStr(GetDict(signalsSrc, "robot"), "taskDone", "");
                    else
                        RobotTaskDoneAddr = GetStr(addrFallback, "task_complete_signal", "");
                }

                Racks.Clear();
                if (cfg.ContainsKey("racks") && cfg["racks"] != null)
                {
                    var racksList = cfg["racks"];
                    if (racksList is System.Collections.ArrayList al)
                        foreach (var item in al) { var r = item as Dictionary<string, object>; if (r != null) Racks.Add(ParseRackConfig(r)); }
                    else if (racksList is object[] arr)
                        foreach (var item in arr) { var r = item as Dictionary<string, object>; if (r != null) Racks.Add(ParseRackConfig(r)); }
                    else if (racksList is IEnumerable<object> ie)
                        foreach (var item in ie) { var r = item as Dictionary<string, object>; if (r != null) Racks.Add(ParseRackConfig(r)); }
                }

                Log("INFO", string.Format("配置加载完成: PLC={0}:{1}, 料架数={2}, 机器人任务完成信号={3}",
                    PlcIp, PlcPort, Racks.Count, RobotTaskDoneAddr));
            }
            catch (Exception ex) { Log("WARN", "加载配置失败: " + ex.Message); }
        }

        static RackConfig ParseRackConfig(Dictionary<string, object> r)
        {
            return new RackConfig
            {
                Id = GetStr(r, "id", ""),
                Type = GetStr(r, "type", "steel"),
                Name = GetStr(r, "name", "未知料架"),
                Number = GetStr(r, "number", ""),
                Rows = GetInt(r, "rows", 3),
                Cols = GetInt(r, "cols", 6),
                PhotoSignal = GetStr(r, "photoSignal", ""),
                OpenRequest = GetStr(r, "openRequest", ""),
                OpenAllow = GetStr(r, "openSignal", ""),
                DoorClosed = GetStr(r, "doorClosed", ""),
                DoorFlag = GetStr(r, "doorFlag", ""),
                GrabStartTaskNo = GetStr(r, "grabStartTaskNo", ""),
                PlaceStartTaskNo = GetStr(r, "placeStartTaskNo", ""),
                ScanStartTaskNo = GetStr(r, "scanStartTaskNo", "")
            };
        }

        // ==================== 主循环 ====================
        static void Work()
        {
            var plc = new plc();
            if (!plc.Connect(PlcIp, PlcPort))
            {
                Log("ERROR", "PLC 连接失败: " + PlcIp + ":" + PlcPort);
                return;
            }
            Log("INFO", "PLC 连接成功: " + PlcIp);

            // 停止标志文件
            StopFlagFile = Path.Combine(
                Path.GetDirectoryName(ConfigPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                "stop_rackoperate.flag");

            try
            {
                int totalRacks = Racks.Count;
                int maxRounds = totalRacks * 3; // 最多3轮
                int roundsWithoutRequest = 0;    // 连续无请求轮数
                bool anyRequestHandled = false;

                Log("INFO", "开始多料架循环巡检 (最多3轮，共 " + maxRounds + " 次判定)");

                for (int idx = 0; idx < maxRounds; idx++)
                {
                    int rackIndex = idx % totalRacks;
                    var rack = Racks[rackIndex];
                    int round = (idx / totalRacks) + 1;

                    Log("INFO", string.Format("--- [轮{0}/{2}] 料架: {1} ---",
                        round, rack.Name, totalRacks));

                    try
                    {
                        bool requestHandled = ProcessRack(plc, rack);

                        if (requestHandled)
                        {
                            anyRequestHandled = true;
                            roundsWithoutRequest = 0;
                            // 处理完开门请求后(已等门关+读光电+更新DB)，退出整个流程
                            Log("INFO", "料架 " + rack.Name + " 已完成开门处理，结束料架操作流程");
                            break;
                        }
                        else
                        {
                            roundsWithoutRequest++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("ERROR", "处理料架 " + rack.Name + " 异常: " + ex.Message);
                        roundsWithoutRequest++;
                    }

                    // 如果已经3整轮没有收到任何请求，退出
                    if (roundsWithoutRequest >= maxRounds)
                    {
                        Log("INFO", "连续 " + totalRacks + " 轮未收到开门请求，结束料架操作流程");
                        break;
                    }

                    Thread.Sleep(200);
                }

                if (!anyRequestHandled)
                    Log("INFO", "无料架收到开门请求，流程结束");
            }
            finally
            {
                plc.close();
                Log("INFO", "PLC 连接关闭");
            }
        }

        // ==================== MC1刀库旋转控制 ====================
        /// <summary>
        /// MC1刀库单次旋转控制：检测门关闭后，旋转到指定刀号
        /// </summary>
        /// <param name="plc">PLC实例</param>
        /// <param name="toolNo">目标刀号</param>
        /// <returns>true=执行成功, false=执行失败</returns>
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

        // ==================== 转台运动控制 ====================
        /// <summary>
        /// 转台单次运动控制：旋转到指定角度
        /// </summary>
        /// <param name="plc">PLC实例</param>
        /// <param name="angle">目标角度</param>
        /// <returns>true=执行成功, false=执行失败</returns>
        static bool RunTurntable(plc plc, int angle)
        {
            Log("INFO", "【转台】移动至 " + angle + " 度");
            plc.WriteWr(SignalDegree, (ushort)angle);
            plc.WriteWR(SignalAllowMove, 1);
            plc.WriteWR(SignalStartMove, 1);

            Log("INFO", "【转台】等待到位信号 " + SignalMoveDone);
            while (!plc.ReadWR(SignalMoveDone))
                Thread.Sleep(200);

            plc.WriteWR(SignalAllowMove, 0);
            plc.WriteWR(SignalStartMove, 0);
            Log("SUCCESS", "【转台】" + angle + " 度移动完成");
            return true;
        }

        // ==================== 转台回零 ====================
        /// <summary>
        /// 转台回零：将转台旋转回零点
        /// </summary>
        static bool TurntableReturnZero(plc plc)
        {
            Log("INFO", "【转台】回零开始: " + SignalReturnZero);
            plc.WriteWR(SignalReturnZero, 1);

            Log("INFO", "【转台】等待零点信号 " + SignalAtZero);
            while (!plc.ReadWR(SignalAtZero))
                Thread.Sleep(200);

            plc.WriteWR(SignalReturnZero, 0);
            Log("SUCCESS", "【转台】已回到零点");
            return true;
        }

        /// <summary>
        /// 处理单个料架巡检，返回 true 表示处理了开门请求(需退出)
        /// doorClosed: ReadWR() → 1=门关, 0=门开
        /// </summary>
        static bool ProcessRack(plc plc, RackConfig rack)
        {
            if (string.IsNullOrEmpty(rack.DoorClosed) || string.IsNullOrEmpty(rack.OpenRequest))
            {
                Log("WARN", "料架 " + rack.Name + " 缺少信号配置，跳过");
                return false;
            }

            // 读取门状态: 1=门关, 0=门开
            bool doorClosed = plc.ReadWR(rack.DoorClosed);

            // ---- 场景A: 门关闭 ---- 
            if (doorClosed)
            {
                Log("INFO", "料架 " + rack.Name + " 门已关闭，等待人工开门请求 (5秒超时)...");

                bool requestReceived = false;
                int waitTicks = 0;
                while (waitTicks < 50)
                {
                    if (IsStopRequested()) { Log("INFO", "收到停止信号"); return false; }
                    requestReceived = plc.ReadWR(rack.OpenRequest);
                    if (requestReceived) break;
                    Thread.Sleep(100);
                    waitTicks++;
                }

                if (!requestReceived)
                {
                    Log("INFO", "料架 " + rack.Name + " 5秒内未收到开门请求");
                    return false;
                }

                Log("INFO", "料架 " + rack.Name + " 收到人工开门请求");

                // 检查机器人任务完成信号
                bool robotTaskDone = false;
                if (!string.IsNullOrEmpty(RobotTaskDoneAddr))
                {
                    robotTaskDone = plc.ReadWR(RobotTaskDoneAddr);
                    Log("INFO", "机器人任务完成信号: " + (robotTaskDone ? "已置位" : "未置位"));
                }
                else { robotTaskDone = true; }

                if (!robotTaskDone)
                {
                    Log("WARN", "机器人任务未完成，拒绝开门请求");
                    return false;
                }

                // 允许开门
                if (!string.IsNullOrEmpty(rack.OpenAllow))
                {
                    Log("INFO", "发送允许开门信号: " + rack.OpenAllow);
                    plc.WriteWR(rack.OpenAllow, 1);
                    Thread.Sleep(500);
                    plc.WriteWR(rack.OpenAllow, 0);
                }

                // 等待门打开（doorClosed 变为 0），直到停止信号
                Log("INFO", "等待门打开 (doorClosed→0)...");
                while (true)
                {
                    if (IsStopRequested()) { Log("INFO", "收到停止信号"); return true; }
                    doorClosed = plc.ReadWR(rack.DoorClosed);
                    if (!doorClosed) break;
                    Thread.Sleep(500);
                }
                Log("INFO", "门已打开，置位 doorFlag = 1 (等待门关闭后复位)");

                // 置位 doorFlag
                if (!string.IsNullOrEmpty(rack.DoorFlag))
                    plc.WriteWR(rack.DoorFlag, 1);
                else
                    Log("WARN", "料架 " + rack.Name + " 未配置 doorFlag 信号地址");

                // 等待门再次关闭（doorClosed 变为 1），直到停止信号
                Log("INFO", "等待门关闭 (doorClosed→1)...");
                while (true)
                {
                    if (IsStopRequested()) {
                        Log("INFO", "收到停止信号，复位 doorFlag");
                        if (!string.IsNullOrEmpty(rack.DoorFlag)) plc.WriteWR(rack.DoorFlag, 0);
                        return true;
                    }
                    doorClosed = plc.ReadWR(rack.DoorClosed);
                    if (doorClosed) break;
                    Thread.Sleep(500);
                }

                // 复位 doorFlag
                if (!string.IsNullOrEmpty(rack.DoorFlag))
                    plc.WriteWR(rack.DoorFlag, 0);

                Log("INFO", "门已关闭，读取光电信号变化...");
                ReadPhotoelectricSignals(plc, rack);

                Log("SUCCESS", "料架 " + rack.Name + " 开门处理完成");
                return true;
            }

            // ---- 场景B: 门打开 ----
            // 门已打开说明有人在操作，持续等待门关闭直到收到停止信号或进程被终止
            Log("INFO", "料架 " + rack.Name + " 门当前打开，持续等待门关闭...");

            while (true)
            {
                // 检查停止标志
                if (IsStopRequested())
                {
                    Log("INFO", "收到流程终止信号，结束等待");
                    try { File.Delete(StopFlagFile); } catch { }
                    return false;
                }

                doorClosed = plc.ReadWR(rack.DoorClosed);
                if (doorClosed)
                {
                    Log("INFO", "料架 " + rack.Name + " 门已关闭，读取光电信号变化...");
                    ReadPhotoelectricSignals(plc, rack);
                    Log("SUCCESS", "料架 " + rack.Name + " 门关闭处理完成，结束料架操作流程");
                    return true;
                }

                Thread.Sleep(500); // 每0.5秒检查一次
            }
        }

        // ==================== 光电读取 ====================
        static void ReadPhotoelectricSignals(plc plc, RackConfig rack)
        {
            if (string.IsNullOrEmpty(rack.PhotoSignal))
            {
                Log("WARN", "料架 " + rack.Name + " 未配置光电信号地址");
                return;
            }
            int baseAddr = ParseDMAddress(rack.PhotoSignal);
            int layerCount = rack.Rows;

            Log("INFO", string.Format("读取料架 {0} 光电: DM{1}→DM{2}", rack.Name, baseAddr, baseAddr + layerCount - 1));

            var changedLayers = new List<int>();
            for (int i = 0; i < layerCount; i++)
            {
                try
                {
                    if (plc.ReadDM(baseAddr + i))
                        changedLayers.Add(i + 1);
                }
                catch { }
            }

            if (changedLayers.Count > 0)
            {
                Log("INFO", "料架 " + rack.Name + " 变化层: " + string.Join(",", changedLayers));
                UpdateScanStatus(rack, changedLayers);
            }
            else
                Log("INFO", "料架 " + rack.Name + " 无光电变化");
        }

        static int ParseDMAddress(string addr)
        {
            if (string.IsNullOrEmpty(addr)) return 0;
            int.TryParse(addr.ToUpper().Replace("DM", "").Trim(), out int r);
            return r;
        }

        // ==================== DB更新 ====================
        static void UpdateScanStatus(RackConfig rack, List<int> layers)
        {
            try
            {
                var db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                if (db == null) { Log("ERROR", "创建数据库实例失败"); return; }

                int updatedCount = 0;
                int rackNum = 0;
                int.TryParse(rack.Number, out rackNum);

                foreach (int layer in layers)
                {
                    string sql = "UPDATE rack_administrate SET scanstatus = 0 WHERE racknum = " + rackNum +
                                 " AND id LIKE '" + layer + "%' AND posstatus = 1";
                    bool ok = false;
                    try { ok = db.ExecuteSql(sql); } catch (Exception ex) { Log("WARN", "SQL异常: " + ex.Message); }
                    if (ok) { updatedCount++; NotifyLayerToScan(rack, layer, 1); }
                    Log("INFO", "更新料架 " + rack.Name + " 第" + layer + "层: " + (ok ? "成功" : "失败"));
                }

                Log("SUCCESS", "料架 " + rack.Name + " 共更新 " + updatedCount + " 层为待扫描");
                if (updatedCount > 0) TriggerRackRefresh();
            }
            catch (Exception ex) { Log("ERROR", "更新扫描状态失败: " + ex.Message); }
        }

        static void NotifyLayerToScan(RackConfig rack, int layer, int count)
        {
            try
            {
                var data = new { rackId = rack.Id, rackName = rack.Name, position = layer,
                    rackType = rack.Type, status = "to-scan",
                    message = "料架 " + rack.Name + " 第" + layer + "层→待扫描" };
                var req = WebRequest.CreateHttp(ApiBase + "/api/rack/update");
                req.Method = "POST"; req.ContentType = "application/json"; req.Timeout = 3000;
                byte[] bytes = Encoding.UTF8.GetBytes(Js.Serialize(data));
                using (var rs = req.GetRequestStream()) rs.Write(bytes, 0, bytes.Length);
                using (var resp = req.GetResponse()) { }
            }
            catch { }
        }

        static void TriggerRackRefresh()
        {
            try
            {
                var req = WebRequest.CreateHttp(ApiBase + "/api/rack/refresh");
                req.Method = "POST"; req.ContentType = "application/json"; req.Timeout = 3000;
                byte[] bytes = Encoding.UTF8.GetBytes("{}");
                using (var rs = req.GetRequestStream()) rs.Write(bytes, 0, bytes.Length);
                using (var resp = req.GetResponse()) { }
            }
            catch { }
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
