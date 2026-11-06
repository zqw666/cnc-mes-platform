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
            public string PhotoStartSignal;
            public string PhotoResetSignal; // 光电复位开始信号
            public string OpenRequest;     // 人工开门请求信号
            public string OpenAllow;       // 允许开门信号
            public string DoorClosed;      // 门状态信号（1=门关, 0=门开）
            public string DoorFlag;        // 门状态标志信号（置1后500ms复位）
            public string GrabStartTaskNo;
            public string PlaceStartTaskNo;
            public string ScanStartTaskNo;
        }

        // ==================== 配置 ====================
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "rack_operate";
        static readonly string DisplayName = "料架操作";

        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
        static int PlcDm = 100;   // 任务号地址（DM区，从 plc.signals.robot.taskNumber 读取）
        static string RobotTaskDoneAddr = "";
        static readonly List<RackConfig> Racks = new List<RackConfig>();
        static string StopFlagFile;
        static bool IsStopRequested() { return File.Exists(StopFlagFile); }

        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
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
        static readonly string MC1RotateDone    = "104.05";  // MC1刀库转到位信号
        static readonly string MC1RotateStart   = "104.01";  // MC1刀库旋转开始信号

        // ==================== Main ====================
        static void Main(string[] args)
        {
            LogHelper.Info( "【" + DisplayName + "】分包启动");

            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败，配置来源: " + ConfigClient.LastSourceText); return; }
                // 从配置读取 PLC 地址（与 load_machine 一致）
                var plcCfg = ConfigClient.GetDict(cfg, "plc");
                if (plcCfg.Count > 0)
                {
                    PlcIp = ConfigClient.GetStr(plcCfg, "ip", PlcIp);
                    PlcPort = ConfigClient.GetInt(plcCfg, "port", PlcPort);
                    // 读取任务号地址（plc.signals.robot.taskNumber）
                    var signals = ConfigClient.GetDict(plcCfg, "signals");
                    var robotSig = ConfigClient.GetDict(signals, "robot");
                    if (robotSig.Count > 0)
                    {
                        string taskNoStr = ConfigClient.GetStr(robotSig, "taskNumber", "D100");
                        int.TryParse(taskNoStr.Replace("D", "").Replace("DM", "").Trim(), out PlcDm);
                    }
                }
                // 从配置读取数据库配置
                LoadDatabaseConfig(cfg);
                LoadRacks(cfg);

                if (Racks.Count == 0)
                {
                    LogHelper.Warn( "检测到系统没有料架配置，结束料架操作流程");
                    return;
                }
                LogHelper.Info( "加载料架配置完成，共 " + Racks.Count + " 个料架");

                Work();
            }
            catch (Exception ex)
            {
                LogHelper.Error( "执行异常: " + ex.Message);
            }

            LogHelper.Info( "【" + DisplayName + "】分包结束");
        }

        // ==================== 加载配置 ====================
        
        // 从配置读取数据库配置（database 节点）
        static void LoadDatabaseConfig(Dictionary<string, object> cfg)
        {
            try
            {
                if (cfg == null || cfg.Count == 0) return;
                var db = ConfigClient.GetDict(cfg, "database");
                if (db.Count == 0) { LogHelper.Warn( "配置文件中没有 database 节点，使用默认数据库配置"); return; }
                string host = ConfigClient.GetStr(db, "host", "localhost");
                string dbname = ConfigClient.GetStr(db, "database", "qianduantest");
                string user = ConfigClient.GetStr(db, "user", "root");
                string pwd = ConfigClient.GetStr(db, "password", "123456");
                DbType = ConfigClient.GetStr(db, "type", "mysql").ToLower();
                if (DbType == "sqlserver")
                {
                    int dbport = ConfigClient.GetInt(db, "port", 1433);
                    ConnStr = $"Server={host},{dbport};Database={dbname};User Id={user};Password={pwd};";
                }
                else
                {
                    ConnStr = $"Server={host};Database={dbname};Uid={user};Pwd={pwd};CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
                }
                LogHelper.Info( "数据库配置加载完成，类型: " + DbType);
            }
            catch (Exception ex)
            {
                LogHelper.Warn( "加载数据库配置失败，使用默认配置: " + ex.Message);
            }
        }

        static void LoadRacks(Dictionary<string, object> cfg)
        {
            try
            {
                Racks.Clear();
                var racksRaw = cfg.ContainsKey("racks") ? cfg["racks"] : null;
                if (racksRaw == null)
                {
                    LogHelper.Warn("配置文件中没有 racks 节点");
                    return;
                }
                var racksList = new List<object>();
                if (racksRaw is System.Collections.ArrayList al)
                {
                    foreach (var item in al)
                        racksList.Add(item);
                }
                else if (racksRaw is object[] arr)
                {
                    foreach (var item in arr)
                        racksList.Add(item);
                }
                else
                {
                    LogHelper.Warn("racks 配置格式错误，跳过");
                    return;
                }
                foreach (var item in racksList)
                {
                    if (!(item is Dictionary<string, object> r)) continue;
                    var rack = ParseRackConfig(r);
                    // 铝板电极料架(aluminum)：不做门开请求处理、不做扫码、不做 DB 更新，
                    // 从 rackoperate 巡检范围排除（其他子程序仍可正常按 racks 配置使用该料架）
                    if (string.Equals((rack.Type ?? "").Trim(), "aluminum", StringComparison.OrdinalIgnoreCase))
                    {
                        LogHelper.Info( "料架 " + rack.Name + " 为铝板电极料架(aluminum)，已从门开/DB巡检流程排除");
                        continue;
                    }
                    Racks.Add(rack);
                }
                LogHelper.Info("加载料架配置完成，共 " + Racks.Count + " 个料架");
            }
            catch (Exception ex)
            {
                LogHelper.Warn("加载料架配置失败: " + ex.Message);
            }
        }

static RackConfig ParseRackConfig(Dictionary<string, object> r)
        {
            return new RackConfig
            {
                Id = ConfigClient.GetStr(r, "id", ""),
                Type = ConfigClient.GetStr(r, "type", "steel"),
                Name = ConfigClient.GetStr(r, "name", "未知料架"),
                Number = ConfigClient.GetStr(r, "number", ""),
                Rows = ConfigClient.GetInt(r, "rows", 3),
                Cols = ConfigClient.GetInt(r, "cols", 6),
                PhotoStartSignal = ConfigClient.GetStr(r, "photoStartSignal", ""),
                PhotoResetSignal = ConfigClient.GetStr(r, "photoResetSignal", ""),
                OpenRequest = ConfigClient.GetStr(r, "openRequest", ""),
                OpenAllow = ConfigClient.GetStr(r, "openSignal", ""),
                DoorClosed = ConfigClient.GetStr(r, "doorClosed", ""),
                DoorFlag = ConfigClient.GetStr(r, "doorFlag", ""),
                GrabStartTaskNo = ConfigClient.GetStr(r, "grabStartTaskNo", ""),
                PlaceStartTaskNo = ConfigClient.GetStr(r, "placeStartTaskNo", ""),
                ScanStartTaskNo = ConfigClient.GetStr(r, "scanStartTaskNo", "")
            };
        }

        // ==================== 主循环 ====================
        static void Work()
        {
            var plc = new plc();
            if (!plc.Connect(PlcIp, PlcPort))
            {
                LogHelper.Error( "PLC 连接失败: " + PlcIp + ":" + PlcPort);
                return;
            }
            LogHelper.Info( "PLC 连接成功: " + PlcIp);

            // 停止标志文件
            StopFlagFile = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "stop.flag");

            try
            {
                int totalRacks = Racks.Count;
                int maxRounds = totalRacks * 3; // 最多3轮
                int roundsWithoutRequest = 0;    // 连续无请求轮数
                bool anyRequestHandled = false;

                LogHelper.Info( "开始多料架循环巡检 (最多3轮，共 " + maxRounds + " 次判定)");

                for (int idx = 0; idx < maxRounds; idx++)
                {
                    int rackIndex = idx % totalRacks;
                    var rack = Racks[rackIndex];
                    int round = (idx / totalRacks) + 1;

                    LogHelper.Info( string.Format("--- [轮{0}/{2}] 料架: {1} ---",
                        round, rack.Name, totalRacks));

                    try
                    {
                        bool requestHandled = ProcessRack(plc, rack);

                        if (requestHandled)
                        {
                            anyRequestHandled = true;
                            roundsWithoutRequest = 0;
                            // 处理完开门请求后(已等门关+读光电+更新DB)，退出整个流程
                            LogHelper.Info( "料架 " + rack.Name + " 已完成开门处理，结束料架操作流程");
                            break;
                        }
                        else
                        {
                            roundsWithoutRequest++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error( "处理料架 " + rack.Name + " 异常: " + ex.Message);
                        roundsWithoutRequest++;
                    }

                    // 如果已经3整轮没有收到任何请求，退出
                    if (roundsWithoutRequest >= maxRounds)
                    {
                        LogHelper.Info( "连续 " + totalRacks + " 轮未收到开门请求，结束料架操作流程");
                        break;
                    }

                    Thread.Sleep(200);
                }

                if (!anyRequestHandled)
                    LogHelper.Info( "无料架收到开门请求，流程结束");
            }
            finally
            {
                plc.close();
                LogHelper.Info( "PLC 连接关闭");
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
            LogHelper.Info( "【MC1刀库】检查门关闭信号 " + MC1DoorClosed);
            if (plc.ReadWR(MC1DoorClosed))
            {
                LogHelper.Warn( "【MC1刀库】刀库门未关闭(信号=1)，旋转失败");
                return false;
            }
            plc.WriteWR(MC1RotateStart, 0);
            LogHelper.Info( "【MC1刀库】旋转至 " + toolNo + " 号刀");
            plc.WriteDM(PlcDm, toolNo);
            plc.WriteWR(MC1RotateStart, 1);

            LogHelper.Info( "【MC1刀库】等待转到位信号 " + MC1RotateDone);
            while (!plc.ReadWR(MC1RotateDone))
                Thread.Sleep(200);
            plc.WriteWR(MC1RotateStart, 0);
            LogHelper.Success( "【MC1刀库】" + toolNo + " 号刀旋转到位");
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
            LogHelper.Info( "【转台】移动至 " + angle + " 度");
            plc.WriteWr(SignalDegree, (ushort)angle);
            plc.WriteWR(SignalAllowMove, 1);
            plc.WriteWR(SignalStartMove, 1);

            LogHelper.Info( "【转台】等待到位信号 " + SignalMoveDone);
            while (!plc.ReadWR(SignalMoveDone))
                Thread.Sleep(200);

            plc.WriteWR(SignalAllowMove, 0);
            plc.WriteWR(SignalStartMove, 0);
            LogHelper.Success( "【转台】" + angle + " 度移动完成");
            return true;
        }

        // ==================== 转台回零 ====================
        /// <summary>
        /// 转台回零：将转台旋转回零点
        /// </summary>
        static bool TurntableReturnZero(plc plc)
        {
            LogHelper.Info( "【转台】回零开始: " + SignalReturnZero);
            plc.WriteWR(SignalReturnZero, 1);

            LogHelper.Info( "【转台】等待零点信号 " + SignalAtZero);
            while (!plc.ReadWR(SignalAtZero))
                Thread.Sleep(200);

            plc.WriteWR(SignalReturnZero, 0);
            LogHelper.Success( "【转台】已回到零点");
            return true;
        }

        /// <summary>
        /// 处理单个料架门开请求，返回 true 表示已处理(需退出)
        /// 门状态信号（doorClosed 配置，如 103.06）：ReadWR() → 0=门开, 1=门关/门锁到位
        /// </summary>
        static bool ProcessRack(plc plc, RackConfig rack)
        {
            if (string.IsNullOrEmpty(rack.DoorClosed) || string.IsNullOrEmpty(rack.OpenRequest))
            {
                LogHelper.Warn( "料架 " + rack.Name + " 缺少信号配置，跳过");
                return false;
            }

            // 读取门状态: 0=门开，1=门关
            bool doorClosed = plc.ReadWR(rack.DoorClosed);

            // ---- 门关闭：等待人工开门请求 ----
            if (doorClosed)
            {
                LogHelper.Info( "料架 " + rack.Name + " 门已关闭，等待人工开门请求 (10秒超时)...");

                bool requestReceived = false;
                int waitTicks = 0;
                while (waitTicks < 100)
                {
                    if (IsStopRequested()) { LogHelper.Info( "收到停止信号，退出料架操作流程"); return true; }
                    requestReceived = plc.ReadWR(rack.OpenRequest);
                    if (requestReceived) break;
                    Thread.Sleep(100);
                    waitTicks++;
                }

                if (!requestReceived)
                {
                    LogHelper.Info( "料架 " + rack.Name + " 10秒内未收到开门请求");
                    return false;
                }

                LogHelper.Info( "料架 " + rack.Name + " 收到人工开门请求");

                // 允许开门
                if (!string.IsNullOrEmpty(rack.OpenAllow))
                {
                    LogHelper.Info( "发送允许开门信号: " + rack.OpenAllow);
                    plc.WriteWR(rack.OpenAllow, 1);
                }

                // 等待门打开（doorClosed → 0）
                LogHelper.Info( "等待门打开 (" + rack.DoorClosed + "→0)...");
                while (true)
                {
                    if (IsStopRequested()) { LogHelper.Info( "收到停止信号，中断门开请求处理，退出料架操作流程"); return true; }
                    doorClosed = plc.ReadWR(rack.DoorClosed);
                    if (!doorClosed) break;
                    Thread.Sleep(200);
                }
            }

            // ---- 门已打开：复位允许开门信号 + 置位 doorFlag ----
            LogHelper.Info( "门已打开，复位允许开门信号并置位 doorFlag");

            // 复位允许开门信号
            if (!string.IsNullOrEmpty(rack.OpenAllow))
                plc.WriteWR(rack.OpenAllow, 0);

            // 置位 doorFlag（doorFlag 配置值，如 103.05 = 1）
            if (!string.IsNullOrEmpty(rack.DoorFlag))
                plc.WriteWR(rack.DoorFlag, 1);
            else
                LogHelper.Warn( "料架 " + rack.Name + " 未配置 doorFlag 信号地址");

            // 等待门状态信号变为1（门锁到位）
            LogHelper.Info( "等待门锁到位 (" + rack.DoorClosed + "→1)...");
            while (true)
            {
                if (IsStopRequested())
                {
                    LogHelper.Info( "收到停止信号，复位 doorFlag 并退出料架操作流程");
                    if (!string.IsNullOrEmpty(rack.DoorFlag)) plc.WriteWR(rack.DoorFlag, 0);
                    return true;
                }
                doorClosed = plc.ReadWR(rack.DoorClosed);
                if (doorClosed) break;
                Thread.Sleep(200);
            }

            // 门锁到位，等待1秒后复位 doorFlag
            LogHelper.Info( "门锁到位，等待1秒后复位 doorFlag");
            Thread.Sleep(1000);
            if (!string.IsNullOrEmpty(rack.DoorFlag))
                plc.WriteWR(rack.DoorFlag, 0);

            // 读取光电信号，更新料架状态
            LogHelper.Info( "读取光电信号变化...");
            ReadPhotoelectricSignals(plc, rack);

            LogHelper.Success( "料架 " + rack.Name + " 门开请求处理完成");
            return true;
        }

        // ==================== 光电读取 ====================
        static void ReadPhotoelectricSignals(plc plc, RackConfig rack)
        {
            if (string.IsNullOrEmpty(rack.PhotoStartSignal))
            {
                LogHelper.Warn( "料架 " + rack.Name + " 未配置光电开始信号地址");
                return;
            }
            int layerCount = rack.Rows;

            LogHelper.Info( string.Format("读取料架 {0} 光电开始信号: {1} → 共 {2} 层",
                rack.Name, rack.PhotoStartSignal, layerCount));

            var changedLayers = new List<int>();
            for (int i = 1; i <= layerCount; i++)
            {
                try
                {
                    string addr = GetLayerPhotoAddr(rack.PhotoStartSignal, i);
                    if (plc.ReadWR(addr))
                        changedLayers.Add(i);
                }
                catch { }
            }

            if (changedLayers.Count > 0)
            {
                LogHelper.Info( "料架 " + rack.Name + " 变化层: " + string.Join(",", changedLayers));
                UpdateScanStatus(rack, changedLayers);
                ResetPhotoSignals(plc, rack, changedLayers);
            }
            else
                LogHelper.Info( "料架 " + rack.Name + " 无光电变化");
        }

        // 复位对应层的光电复位信号（地址 = 开始复位信号 + 0.01*层数，与读取光电一致）
        static void ResetPhotoSignals(plc plc, RackConfig rack, List<int> layers)
        {
            if (string.IsNullOrEmpty(rack.PhotoResetSignal)) return;
            foreach (int layer in layers)
            {
                try
                {
                    string addr = GetLayerPhotoAddr(rack.PhotoResetSignal, layer);
                    plc.WriteWR(addr, 0);
                    LogHelper.Info( "料架 " + rack.Name + " 复位第" + layer + "层光电复位信号: " + addr);
                }
                catch { }
            }
        }

        // 根据光电开始信号地址 + 层号计算每层光电地址
        // 例：开始信号 500.00，第1层 = 500.00 + 0.01 = 500.01，第2层 = 500.02 ...
        static string GetLayerPhotoAddr(string baseAddr, int layer)
        {
            int intPart = 0, decPart = 0;
            if (!string.IsNullOrEmpty(baseAddr))
            {
                var parts = baseAddr.Trim().Split('.');
                int.TryParse(parts[0], out intPart);
                if (parts.Length > 1)
                {
                    string decStr = parts[1];
                    if (decStr.Length == 1) decStr += "0";
                    int.TryParse(decStr, out decPart);
                }
            }
            int total = decPart + layer;   // layer 从 1 开始，1 对应 +0.01
            intPart += total / 100;
            int dec = total % 100;
            return intPart.ToString() + "." + dec.ToString("D2");
        }

        // ==================== DB更新 ====================

        // 料架类型(type) → DB racktype 数字（与前端 RACKTYPE_MAP / 后端 RackController 映射一致）
        // 1=电极 2=钢件 3=刀具 4=托盘 5=铝板电极
        static int GetRackTypeCode(string type)
        {
            switch ((type ?? "").Trim().ToLower())
            {
                case "electrode": return 1;
                case "steel": return 2;
                case "tool": return 3;
                case "pallet": return 4;
                case "aluminum": return 5;
                default: return 0;
            }
        }

        static void UpdateScanStatus(RackConfig rack, List<int> layers)
        {
            try
            {
                var db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                if (db == null) { LogHelper.Error( "创建数据库实例失败"); return; }

                int updatedCount = 0;
                int rackNum = 0;
                int.TryParse(rack.Number, out rackNum);
                int slotsPerLayer = rack.Cols > 0 ? rack.Cols : 10;  // 每层料位数
                // 匹配条件按 racknum+id 定位本料架槽位；再追加该料架类型的 racktype，
                // 避免不同 racktype 的料架在 racknum/id 相同（料位号有重叠）时误更新
                int rackTypeCode = GetRackTypeCode(rack.Type);
                string rackTypeCond = rackTypeCode > 0 ? " AND racktype = " + rackTypeCode : "";

                foreach (int layer in layers)
                {
                    // 根据层号计算该层料位号范围：第N层 = [(N-1)*每层料位数 + 1, N*每层料位数]
                    // 例：每层10个料位，第3层=21-30，第4层=31-40
                    int startId = (layer - 1) * slotsPerLayer + 1;
                    int endId = layer * slotsPerLayer;

                    int layerUpdated = 0;
                    for (int id = startId; id <= endId; id++)
                    {
                        // 先确认该料位是否被禁用（posstatus=0），被禁用则不更新
                        try
                        {
                            object ps = db.GetValue("SELECT posstatus FROM rack_administrate WHERE racknum = " + rackNum + " AND id = " + id + rackTypeCond);
                            int pos = (ps == null || ps == DBNull.Value) ? -1 : Convert.ToInt32(ps);
                            if (pos == 0)
                            {
                                LogHelper.Info( "料架 " + rack.Name + " 第" + layer + "层料位 " + id + " 已禁用，跳过更新");
                                continue;
                            }
                        }
                        catch (Exception ex) { LogHelper.Warn( "查询料位 " + id + " 状态异常: " + ex.Message); }

                        // 一个料位一个料位更新：扫描状态=待扫描(0)，料位状态=有料(2)，工件状态=待加工(0)
                        string sql = "UPDATE rack_administrate SET scanstatus = 0, posstatus = 2, workstate = 0 WHERE racknum = " + rackNum +
                                     " AND id = " + id + rackTypeCond;
                        bool ok = false;
                        try { ok = db.ExecuteSql(sql); } catch (Exception ex) { LogHelper.Warn( "SQL异常: " + ex.Message); }
                        if (ok) layerUpdated++;
                    }

                    if (layerUpdated > 0) { updatedCount += layerUpdated; NotifyLayerToScan(rack, layer, layerUpdated); }
                    LogHelper.Info( "更新料架 " + rack.Name + " 第" + layer + "层 (料位 " + startId + "-" + endId + "): " + layerUpdated + " 个料位更新成功");
                }

                LogHelper.Success( "料架 " + rack.Name + " 共更新 " + updatedCount + " 个料位为待扫描");
                if (updatedCount > 0) TriggerRackRefresh();
            }
            catch (Exception ex) { LogHelper.Error( "更新扫描状态失败: " + ex.Message); }
        }

        static void NotifyLayerToScan(RackConfig rack, int layer, int count)
        {
            LogHelper.Info("料架 " + rack.Name + " 第" + layer + "层→待扫描");
            LogHelper.NotifyRackChanged();
        }

        static void TriggerRackRefresh()
        {
            LogHelper.NotifyRackChanged();
        }


        // ==================== 辅助 ====================
        
}
}
