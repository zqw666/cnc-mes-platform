// ============================================================================
// 空夹具补充 - SubProcess
// 功能：当 料架上没有待加工工件 且 待机机床还有空位 时，
//       从料架上抓取"空夹具"放置到机床空位（占位，等待后续工件上机）。
//
// 判定条件（数据库 rack_administrate）：
//   ① 待加工工件：posstatus=2 AND workstate=0 AND workpiecename 非空
//      且非 EMPTY 空夹具（rfid/workpiecename 含 EMPTY 视为空夹具）
//   ② 空夹具（本流程抓取对象）：
//        posstatus=2 AND rfid 含 EMPTY（如 EMPTY1），
//        或兼容历史数据：rfid 有夹具编号(非空) AND workpiecename 为空
//        （即该料位有夹具、但未装工件）
//
// 执行动作：
//   ① 置换夹爪（如需）→ ② 机器人从料架空夹具格位抓取夹具
//   → ③ 放置到待机机床空工位 → ④ 更新 macworkposstatus 占位
//   → ⑤ 料架该格位置空（待后续扫描确认）
//
// 若无空夹具可补：打印报错日志并退出本流程。
// 备注：空夹具识别 SQL 集中在 IsEmptyFixture()/GetFirstFixtureSlot()
//       便于现场按实际料架数据结构调整。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using DataCommonOperate;
using PLC;
using MacCommunicate;

namespace fill_fixture
{
    class Program
    {
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "fill_fixture";
        static readonly string DisplayName = "空夹具补充";

        // ==================== 配置 ====================
        static string ConnStr;
        static string DbType;
        static CommonInterface Mysql;

        static string PlcIp;
        static int PlcPort;
        static int PlcDm;            // 机器人任务号 DM 区
        static string TaskStartAddr; // 任务开始信号
        static string TaskFinishAddr;// 任务完成信号
        static plc Plc;

        // 停止标志位：前端停止按钮触发后，后端创建 stop.flag，子程序检测到即终止
        static readonly string StopFlagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stop.flag");
        static bool IsStopRequested() { return File.Exists(StopFlagFile); }

        // 夹爪配置（快换模式需要）
        static string GripperType = "";
        static bool IsFixedGripper = true;
        static string TargetGripperType = "steel"; // 由空夹具所在料架类型决定
        static List<GripperConfig> Grippers = new List<GripperConfig>();
        class GripperConfig
        {
            public string Id;
            public string Type;
            public string Name;
            public string Number;
            public string SlotPhotoSignal;
            public string GrabTaskNo;
            public string PlaceTaskNo;
        }

        // 料架配置
        static readonly List<RackConfig> Racks = new List<RackConfig>();
        class RackConfig
        {
            public string Id;
            public string Type;
            public string Name;
            public string Number;
            public string GrabStartTaskNo;
            public string PlaceStartTaskNo;
        }

        // 机床配置
        static Dictionary<int, Dictionary<string, string>> MachineConfigs = new Dictionary<int, Dictionary<string, string>>();
        static Dictionary<int, macinterface> MacList = new Dictionary<int, macinterface>();
        static readonly Dictionary<int, string> MacNameCache = new Dictionary<int, string>();

        /// <summary>空夹具料位信息</summary>
        class FixtureSlot
        {
            public int Id;
            public string Rfid;
            public string Racknum;
        }

        // ==================== Main ====================
        static void Main(string[] args)
        {
            LogHelper.Info( "【" + DisplayName + "】分包启动");
            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败，退出空夹具补充流程，配置来源: " + ConfigClient.LastSourceText); return; }

                if (!ExtractConfig(cfg)) return;
                LoadRacks(cfg);
                InitMachines(cfg);
                LogHelper.Configure(StepName, DisplayName, ApiBase);

                Mysql = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                Plc = new plc();
                if (!Plc.Connect(PlcIp, PlcPort))
                {
                    LogHelper.Error( "PLC 连接失败: " + PlcIp);
                    return;
                }
                LogHelper.Info( "PLC 连接成功");

                // ========== ① 判定：料架上没有待加工的工件 ==========
                int pending = CountPendingPieces();
                if (pending < 0)
                {
                    LogHelper.Error( "查询料架待加工工件失败，终止空夹具补充流程，需人工排查");
                    return;
                }
                if (pending > 0)
                {
                    LogHelper.Info( "料架上仍有 " + pending + " 个待加工工件，应由备料/上机流程处理，空夹具补充流程退出");
                    return;
                }
                LogHelper.Info( "料架上没有待加工工件，进入空夹具补充判定");

                // ========== ② 判定：存在待机且有空位的机床 ==========
                List<string> waitmaclist = WaitEmptyMaclist();
                if (waitmaclist.Count == 0)
                {
                    LogHelper.Info( "没有“待机且有工位空位”的机床，空夹具补充流程退出");
                    return;
                }
                int macnum = Convert.ToInt16(waitmaclist[0].Split(',')[0]);
                string ip = waitmaclist[0].Split(',')[1];
                int worknum = GetMacFirstEmptyWorknum(macnum);
                if (worknum == 0)
                {
                    LogHelper.Error( "机床" + macnum + " 没有可用空工位，退出空夹具补充流程");
                    return;
                }
                LogHelper.Info( "选择待机机床" + macnum + "（IP:" + ip + "）空工位 " + worknum);

                // ========== ③ 从料架获取空夹具位置 ==========
                FixtureSlot fixture = GetFirstFixtureSlot();
                if (fixture == null)
                {
                    LogHelper.Error( "料架上没有空夹具（无 posstatus=2 且 rfid 有夹具编号、未装工件的料位），无法补充空夹具，请人工放置空夹具后再触发本流程！");
                    return;
                }
                LogHelper.Info( "找到空夹具：料架格位 " + fixture.Id + "，夹具编号 " + fixture.Rfid + "（料架号 " + fixture.Racknum + "）");

                // ========== ④ 置换夹爪（快换模式，目标类型=夹具所在料架类型） ==========
                TargetGripperType = RackTypeToGripper(GetRackTypeByPos(fixture.Id));
                if (string.IsNullOrEmpty(TargetGripperType))
                {
                    LogHelper.Error( "料位 " + fixture.Id + " 料架类型无法识别，无法确定目标夹爪类型，终止空夹具补充流程");
                    return;
                }
                if (!ChangeGripper())
                {
                    LogHelper.Error( "置换夹爪失败，终止空夹具补充流程");
                    return;
                }

                // ========== ⑤ 机台为 roders 时开门 ==========
                if (!OpenMachineDoorIfRoders(macnum))
                {
                    LogHelper.Error( "机台开门失败，终止空夹具补充流程");
                    return;
                }

                // ========== ⑥ 抓取空夹具（任务号 = 料架抓取开始任务号 + 料位号） ==========
                int grabStartNo = GetRackGrabStartNoByPos(fixture.Id);
                if (grabStartNo <= 0)
                {
                    LogHelper.Error( "料架" + fixture.Racknum + " 未配置抓取开始任务号(grabStartTaskNo)，无法抓取空夹具，请检查 config.json");
                    return;
                }
                LogHelper.Info( "机器人抓取料架 " + fixture.Id + " 位空夹具（任务号 " + (grabStartNo + fixture.Id) + "）...");
                if (!ExecuteTask(grabStartNo + fixture.Id))
                {
                    LogHelper.Error( "机器人抓取料架" + fixture.Id + "位空夹具失败，终止空夹具补充流程");
                    return;
                }
                LogHelper.Info( "机器人已抓取空夹具[" + fixture.Rfid + "]");

                // 清空料架该格位（夹具已取走 → 空位待扫描）
                if (!ClearRackSlot(fixture.Id))
                    LogHelper.Error( "更新料架格位 " + fixture.Id + " 为空失败！");
                // 机器人手爪临时标记携带夹具（放完后清空）
                if (!Updaterobotmessage(fixture.Rfid, 0))
                    LogHelper.Error( "更新机器人手爪信息失败！");

                // ========== ⑦ 放置空夹具到机床空位（任务号 = 机床放置开始任务号 + 工位号） ==========
                int placeStartNo = GetMachineStartNo(macnum, "place");
                if (placeStartNo <= 0)
                {
                    LogHelper.Error( "机床" + macnum + " 未配置放置开始任务号(placeStartTaskNo)，无法放置空夹具，请检查 config.json");
                    return;
                }
                LogHelper.Info( "机器人放置空夹具到机床" + macnum + " 工位" + worknum + "（任务号 " + (placeStartNo + worknum) + "）...");
                if (!ExecuteTask(placeStartNo + worknum))
                {
                    LogHelper.Error( "机器人放置空夹具到机床" + macnum + " 工位" + worknum + "失败（机器人仍持有夹具），需人工处理！");
                    return;
                }
                LogHelper.Info( "机器人已将空夹具[" + fixture.Rfid + "]放置到机床" + macnum + " 工位" + worknum);

                // 机台为 roders 时关门
                CloseMachineDoorIfRoders(macnum);

                // ========== ⑧ 更新数据库：机床工位占位 ==========
                if (!UpdateMacWorkPosStatus(macnum, worknum, fixture.Rfid, 0))
                    LogHelper.Error( "更新机床" + macnum + " 工位" + worknum + "（空夹具占位）失败，需人工确认！");
                if (!Updaterobotmessage(null, 0))
                    LogHelper.Error( "更新机器人手爪信息失败！");

                LogHelper.Success( "空夹具补充完成：料架" + fixture.Id + "位夹具[" + fixture.Rfid + "] → 机床" + macnum + " 工位" + worknum);
            }
            catch (Exception ex)
            {
                LogHelper.Error( "执行异常: " + ex.Message);
            }

            LogHelper.Info( "【" + DisplayName + "】分包结束");
        }

        // ==================== 配置加载 ====================
        static bool ExtractConfig(Dictionary<string, object> cfg)
        {
            try
            {
                if (cfg == null || cfg.Count == 0) return false;
                var db = ConfigClient.GetDict(cfg, "database");
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

                var plcCfg = ConfigClient.GetDict(cfg, "plc");
                if (plcCfg.Count > 0)
                {
                    PlcIp = ConfigClient.GetStr(plcCfg, "ip", "192.168.1.1");
                    PlcPort = ConfigClient.GetInt(plcCfg, "port", 9600);
                    var signals = ConfigClient.GetDict(plcCfg, "signals");
                    var robotSig = ConfigClient.GetDict(signals, "robot");
                    if (robotSig.Count > 0)
                    {
                        string taskNoStr = ConfigClient.GetStr(robotSig, "taskNumber", "D100");
                        int.TryParse(taskNoStr.Replace("D", "").Replace("DM", "").Trim(), out PlcDm);
                        TaskStartAddr = ConfigClient.GetStr(robotSig, "taskStart", "");
                        TaskFinishAddr = ConfigClient.GetStr(robotSig, "taskDone", "");
                    }
                }

                var line = ConfigClient.GetDict(cfg, "line");
                // 夹爪类型决定后续用哪套任务号 / 哪组夹爪对象，取不到时必须终止：
                // 旧逻辑用 "fixed-single" 兜底，电极线一旦读不到配置就会静默用钢件夹爪抓料，日志查不出原因。
                GripperType = ConfigClient.GetStr(line, "gripperType", "").Trim();
                if (string.IsNullOrEmpty(GripperType))
                {
                    LogHelper.Error("配置缺少 line.gripperType，无法判定夹爪类型，拒绝以默认值继续运行。配置来源: "
                        + ConfigClient.LastSourceText);
                    return false;
                }
                LogHelper.Info("夹爪类型: " + GripperType + "（配置来源: " + ConfigClient.LastSourceText + "）");
                IsFixedGripper = GripperType == "fixed-single" || GripperType == "fixed-double";

                // 快换模式加载夹爪配置
                Grippers.Clear();
                if (!IsFixedGripper && cfg.ContainsKey("grippers") && cfg["grippers"] != null)
                {
                    var glist = cfg["grippers"];
                    if (glist is System.Collections.ArrayList al)
                        foreach (var item in al) AddGripperConfig(item);
                    else if (glist is object[] arr)
                        foreach (var item in arr) AddGripperConfig(item);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("提取配置失败，终止运行（不再使用默认配置兜底）: " + ex.Message);
                return false;
            }
            return true;
        }

        static void AddGripperConfig(object item)
        {
            var g = item as Dictionary<string, object>;
            if (g == null) return;
            Grippers.Add(new GripperConfig
            {
                Id = ConfigClient.GetStr(g, "id", ""),
                Type = ConfigClient.GetStr(g, "type", "steel"),
                Name = ConfigClient.GetStr(g, "name", ""),
                Number = ConfigClient.GetStr(g, "number", ""),
                SlotPhotoSignal = ConfigClient.GetStr(g, "slotPhotoSignal", ""),
                GrabTaskNo = ConfigClient.GetStr(g, "grabTaskNo", ""),
                PlaceTaskNo = ConfigClient.GetStr(g, "placeTaskNo", "")
            });
        }

        static void LoadRacks(Dictionary<string, object> cfg)
        {
            try
            {
                Racks.Clear();
                if (cfg == null || !cfg.ContainsKey("racks") || cfg["racks"] == null)
                {
                    LogHelper.Warn("配置文件中没有 racks 节点");
                    return;
                }
                var racksRaw = cfg["racks"];
                var racksList = new List<object>();
                if (racksRaw is System.Collections.ArrayList al)
                {
                    foreach (var item in al) racksList.Add(item);
                }
                else if (racksRaw is object[] arr)
                {
                    foreach (var item in arr) racksList.Add(item);
                }
                else
                {
                    LogHelper.Warn("racks 配置格式错误，跳过");
                    return;
                }
                foreach (var item in racksList)
                {
                    if (item is Dictionary<string, object> r)
                    {
                        Racks.Add(new RackConfig
                        {
                            Id = ConfigClient.GetStr(r, "id", ""),
                            Type = ConfigClient.GetStr(r, "type", "steel"),
                            Name = ConfigClient.GetStr(r, "name", "未知料架"),
                            Number = ConfigClient.GetStr(r, "number", ""),
                            GrabStartTaskNo = ConfigClient.GetStr(r, "grabStartTaskNo", ""),
                            PlaceStartTaskNo = ConfigClient.GetStr(r, "placeStartTaskNo", "")
                        });
                    }
                }
                LogHelper.Info("加载料架配置完成，共 " + Racks.Count + " 个料架");
            }
            catch (Exception ex)
            {
                LogHelper.Warn("加载料架配置失败: " + ex.Message);
            }
        }

        static void InitMachines(Dictionary<string, object> cfg)
        {
            try
            {
                MachineConfigs.Clear();
                if (cfg == null || !cfg.ContainsKey("machines") || cfg["machines"] == null)
                {
                    LogHelper.Warn("配置文件中没有 machines 节点，跳过机床初始化");
                    return;
                }
                var machinesRaw = cfg["machines"];
                Dictionary<string, object> machines;
                if (machinesRaw is Dictionary<string, object> mDict)
                    machines = mDict;
                else
                    machines = Js.Deserialize<Dictionary<string, object>>(machinesRaw.ToString());

                foreach (var kv in machines)
                {
                    int macId = Convert.ToInt32(kv.Key);
                    var mVal = kv.Value;
                    Dictionary<string, object> m;
                    if (mVal is Dictionary<string, object> macDict)
                        m = macDict;
                    else
                        m = Js.Deserialize<Dictionary<string, object>>(mVal.ToString());

                    string typeStr = m.ContainsKey("type") ? m["type"].ToString().ToLower() : "roders";
                    string ip = m.ContainsKey("ip") ? m["ip"].ToString() : "";
                    string sessionUrl = m.ContainsKey("sessionUrl") ? m["sessionUrl"].ToString() : "";
                    string username = m.ContainsKey("username") ? m["username"].ToString() : "";
                    string password = m.ContainsKey("password") ? m["password"].ToString() : "";
                    string hostKey = m.ContainsKey("hostKey") ? m["hostKey"].ToString() : "";
                    string stationCount = m.ContainsKey("stationCount") ? m["stationCount"].ToString() : "1";
                    string palletWorkNum = m.ContainsKey("palletWorkNum") ? m["palletWorkNum"].ToString() : "1";
                    string grabStartTaskNo = m.ContainsKey("grabStartTaskNo") ? m["grabStartTaskNo"].ToString() : "";
                    string placeStartTaskNo = m.ContainsKey("placeStartTaskNo") ? m["placeStartTaskNo"].ToString() : "";

                    MachineConfigs[macId] = new Dictionary<string, string>()
                    {
                        { "type", typeStr }, { "ip", ip }, { "stationCount", stationCount },
                        { "palletWorkNum", palletWorkNum },
                        { "grabStartTaskNo", grabStartTaskNo }, { "placeStartTaskNo", placeStartTaskNo }
                    };

                    MacCommunicate.Macfactory.MacType macType;
                    switch (typeStr)
                    {
                        case "fanuc": macType = MacCommunicate.Macfactory.MacType.fanuc; break;
                        case "siemense": macType = MacCommunicate.Macfactory.MacType.siemense; break;
                        case "makino": macType = MacCommunicate.Macfactory.MacType.makino; break;
                        default: macType = MacCommunicate.Macfactory.MacType.roders; break;
                    }
                    var mac = MacCommunicate.Macfactory.MacFactory.CreateInstance(macType, ip, sessionUrl, username, password, hostKey);
                    if (mac != null)
                    {
                        MacList[macId] = mac;
                        LogHelper.Info("机床 " + macId + " (" + typeStr + ") 已创建实例，IP: " + ip);
                    }
                    else
                    {
                        LogHelper.Error("机床 " + macId + " 类型 " + typeStr + " 创建实例失败");
                    }
                }
                LogHelper.Info("机床通讯初始化完成，共 " + MacList.Count + " 台");
            }
            catch (Exception ex)
            {
                LogHelper.Error("InitMachines 异常: " + ex.Message);
            }
        }

        // ==================== ① 料架待加工工件判定 ====================
        /// <summary>
        /// 统计料架上待加工工件数量：
        /// posstatus=2 有料 + workstate=0 待加工 + workpiecename 非空，
        /// 且排除 EMPTY 空夹具（rfid/workpiecename 含 EMPTY 视为空夹具）
        /// 返回 -1 表示查询异常
        /// </summary>
        static int CountPendingPieces()
        {
            try
            {
                object val = Mysql.GetValue(
                    "SELECT COUNT(*) FROM rack_administrate WHERE posstatus=2 AND workstate=0 " +
                    "AND workpiecename IS NOT NULL AND workpiecename<>'' " +
                    "AND rfid NOT LIKE '%EMPTY%' AND workpiecename NOT LIKE '%EMPTY%'");
                return val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);
            }
            catch (Exception ex)
            {
                LogHelper.Error("查询料架待加工工件数量异常: " + ex.Message);
                return -1;
            }
        }

        // ==================== ② 待机机床 + 空位 ====================
        /// <summary>
        /// 获取“待机且有空工位”的机床列表（macmessage.status!=0 表示已启用/在线）
        /// </summary>
        static List<string> WaitEmptyMaclist()
        {
            List<string> macmessage = new List<string>();
            try
            {
                DataTable dt = Mysql.ExeForDtl("select * from macmessage where status!=0");
                foreach (DataRow r in dt.Rows)
                {
                    int macnum = Convert.ToInt16(r["id"].ToString());
                    string ip = r["machineip"] == null ? "" : r["machineip"].ToString();
                    // 确保工位行存在（工位表为空时按配置补齐）
                    EnsureMacWorkPosRows(macnum);
                    string status = GetMacStatusSafe(macnum);
                    if (status == "待机")
                    {
                        // 机床待机时，将加工中的工位更新为加工完成
                        UpdateMacWorkPosStatusToDone(macnum);
                        var emptyPositions = GetMacEmptyPositions(macnum);
                        if (emptyPositions.Count > 0)
                        {
                            macmessage.Add(macnum.ToString() + "," + ip);
                            LogHelper.Info("机床" + macnum + " 待机且有空位(" + string.Join(",", emptyPositions) + ")");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("获取待机机床列表异常: " + ex.Message);
            }
            return macmessage;
        }

        // ==================== ③ 空夹具查询 ====================
        /// <summary>
        /// 空夹具识别条件（集中封装，便于现场调整）：
        /// posstatus=2（有夹具在位）+ rfid 含 EMPTY（如 EMPTY1，扫描回写 workpiecename 与 rfid 一致）
        /// 或兼容历史数据：rfid 有夹具编号(非空) + workpiecename 为空（未装工件）
        /// </summary>
        static bool IsEmptyFixtureRow(DataRow r)
        {
            try
            {
                int posstatus = SafeInt(r["posstatus"]);
                string rfid = SafeStr(r["rfid"]);
                string workpiecename = SafeStr(r["workpiecename"]);
                if (posstatus != 2 || string.IsNullOrEmpty(rfid)) return false;
                // 新口径：rfid 含 EMPTY 即为空夹具
                if (rfid.IndexOf("EMPTY", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                // 兼容旧数据：有夹具编号但未装工件（workpiecename 为空）
                return string.IsNullOrEmpty(workpiecename);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从料架获取第一个空夹具位置（按料位号升序），没有则返回 null
        /// </summary>
        static FixtureSlot GetFirstFixtureSlot()
        {
            try
            {
                DataTable dt = Mysql.ExeForDtl(
                    "SELECT id, rfid, racknum, posstatus, workpiecename FROM rack_administrate ORDER BY id");
                if (dt != null)
                {
                    foreach (DataRow r in dt.Rows)
                    {
                        if (IsEmptyFixtureRow(r))
                        {
                            return new FixtureSlot
                            {
                                Id = SafeInt(r["id"]),
                                Rfid = SafeStr(r["rfid"]),
                                Racknum = SafeStr(r["racknum"])
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("查询空夹具料位异常: " + ex.Message);
            }
            return null;
        }

        // ==================== 料架辅助 ====================
        /// <summary>
        /// 根据料位号获取料架类型字符串（steel/electrode/tool/pallet）
        /// 优先按 racknum 匹配 config racks[]，兜底按 rack_administrate.racktype 数字映射
        /// </summary>
        static string GetRackTypeByPos(int pos)
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT racknum, racktype FROM rack_administrate WHERE id=" + pos))
                {
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        string racknum = SafeStr(dt.Rows[0]["racknum"]);
                        // 优先按 racknum 找 config 料架
                        foreach (var rack in Racks)
                        {
                            if (string.Equals(rack.Number, racknum, StringComparison.OrdinalIgnoreCase))
                                return rack.Type;
                        }
                        // 兜底：racktype 数字映射（1=电极 2=钢件 3=刀具 4=托盘 5=铝板电极）
                        int rt = SafeInt(dt.Rows[0]["racktype"]);
                        if (rt == 1) return "electrode";
                        if (rt == 3) return "tool";
                        if (rt == 4) return "pallet";
                        if (rt == 5) return "aluminum";
                        return "steel";
                    }
                }
            }
            catch (Exception ex) { LogHelper.Error("获取料位 " + pos + " 料架类型异常: " + ex.Message); }
            return "steel";
        }

        /// <summary>料架类型 → 夹爪类型（config grippers[].type 仅支持 steel/electrode/tool/pallet，铝板电极共用电极夹爪）；无法识别返回空串</summary>
        static string RackTypeToGripper(string rackType)
        {
            switch ((rackType ?? "").Trim().ToLower())
            {
                case "electrode": return "electrode";
                case "aluminum": return "electrode";
                case "steel": return "steel";
                case "tool": return "tool";
                case "pallet": return "pallet";
                default: return "";
            }
        }

        /// <summary>
        /// 获取料位所属料架的抓取开始任务号（config racks[].grabStartTaskNo 解析后的数字），未配置返回 0
        /// </summary>
        static int GetRackGrabStartNoByPos(int pos)
        {
            try
            {
                string racknum = "";
                using (DataTable dt = Mysql.ExeForDtl("SELECT racknum FROM rack_administrate WHERE id=" + pos))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        racknum = SafeStr(dt.Rows[0]["racknum"]);
                }
                foreach (var rack in Racks)
                {
                    if (string.Equals(rack.Number, racknum, StringComparison.OrdinalIgnoreCase))
                        return ParseTaskNoBase(rack.GrabStartTaskNo);
                }
                // 兜底按类型
                string rackType = GetRackTypeByPos(pos);
                foreach (var rack in Racks)
                {
                    if (string.Equals(rack.Type, rackType, StringComparison.OrdinalIgnoreCase))
                        return ParseTaskNoBase(rack.GrabStartTaskNo);
                }
            }
            catch (Exception ex) { LogHelper.Error("获取料位 " + pos + " 抓取任务号异常: " + ex.Message); }
            return 0;
        }

        /// <summary>
        /// 清空料架指定格位（夹具取走后置空位，等待后续扫描）
        /// </summary>
        static bool ClearRackSlot(int pos)
        {
            bool flag = false;
            try
            {
                string str = "UPDATE rack_administrate SET rfid='', workpiecename=NULL, workstate=0, posstatus=1, scanstatus=1 WHERE id=" + pos;
                if (Mysql.ExecuteSql(str))
                {
                    flag = true;
                    LogHelper.NotifyRackChanged();
                    LogHelper.Info("料架格位 " + pos + " 已清空（空夹具取走）");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("清空料架格位 " + pos + " 异常: " + ex.Message);
            }
            return flag;
        }

        // ==================== 机床辅助 ====================
        /// <summary>
        /// 机床编号 → 机床名称（macmessage.machinename，与 macworkposstatus.mac_name 一致）
        /// </summary>
        static string GetMacName(int macnum)
        {
            try
            {
                lock (MacNameCache)
                {
                    if (MacNameCache.ContainsKey(macnum)) return MacNameCache[macnum];
                }
                object val = Mysql.GetValue("select machinename from macmessage where id=" + macnum);
                if (val != null && val != DBNull.Value && !string.IsNullOrEmpty(val.ToString()))
                {
                    lock (MacNameCache) { MacNameCache[macnum] = val.ToString(); }
                    return val.ToString();
                }
            }
            catch (Exception ex) { LogHelper.Error("获取机床 " + macnum + " 名称异常: " + ex.Message); }
            return macnum.ToString();
        }

        /// <summary>
        /// 确保 macworkposstatus 中存在该机床的工位行（按配置 stationCount 补齐缺失行）
        /// </summary>
        static void EnsureMacWorkPosRows(int macnum)
        {
            try
            {
                string macName = GetMacName(macnum);
                object cnt = Mysql.GetValue("select count(*) from macworkposstatus where mac_name='" + macName + "'");
                int existing = cnt == null || cnt == DBNull.Value ? 0 : Convert.ToInt32(cnt);
                if (existing > 0) return;
                int stationCount = 1;
                if (MachineConfigs.ContainsKey(macnum) && MachineConfigs[macnum].ContainsKey("stationCount"))
                    int.TryParse(MachineConfigs[macnum]["stationCount"], out stationCount);
                for (int w = 1; w <= stationCount; w++)
                    Mysql.ExecuteSql("insert into macworkposstatus (mac_name, worknum, workpiece_name, workpiece_status) values ('" + macName + "', " + w + ", NULL, 0)");
                LogHelper.Info("机床 " + macnum + " 工位表为空，已按配置补齐 " + stationCount + " 个工位行");
            }
            catch (Exception ex) { LogHelper.Error("补齐机床 " + macnum + " 工位行异常: " + ex.Message); }
        }

        /// <summary>
        /// 获取机床空工位列表（workpiece_name 为空）
        /// </summary>
        static List<int> GetMacEmptyPositions(int macnum)
        {
            var list = new List<int>();
            try
            {
                string macName = GetMacName(macnum);
                DataTable dt = Mysql.ExeForDtl("select worknum from macworkposstatus where mac_name='" + macName + "' and (workpiece_name is null or workpiece_name='') order by worknum");
                foreach (DataRow r in dt.Rows)
                    list.Add(Convert.ToInt32(r["worknum"]));
            }
            catch (Exception ex) { LogHelper.Error("获取机床空工位异常: " + ex.Message); }
            return list;
        }

        /// <summary>
        /// 获取机床第一个空工位号（workpiece_name 为空），无空工位返回 0
        /// </summary>
        static int GetMacFirstEmptyWorknum(int macnum)
        {
            try
            {
                EnsureMacWorkPosRows(macnum);
                var list = GetMacEmptyPositions(macnum);
                return list.Count > 0 ? list[0] : 0;
            }
            catch (Exception ex) { LogHelper.Error("获取机床 " + macnum + " 空工位异常: " + ex.Message); }
            return 0;
        }

        /// <summary>
        /// 更新机床指定工位（macworkposstatus）：占位写入夹具编号
        /// workpiecestatus: 0=待加工/待上料 1=加工中 2=加工完成
        /// </summary>
        static bool UpdateMacWorkPosStatus(int macnum, int worknum, string workpieceName, int workpiecestatus)
        {
            bool flag = false;
            try
            {
                string macName = GetMacName(macnum);
                string nameStr = string.IsNullOrEmpty(workpieceName) ? "NULL" : "'" + workpieceName + "'";
                string str = "update macworkposstatus set workpiece_name=" + nameStr + ", workpiece_status=" + workpiecestatus +
                             " where mac_name='" + macName + "' and worknum=" + worknum;
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                LogHelper.Error("更新机床工位表异常: " + ex.Message);
            }
            return flag;
        }

        /// <summary>
        /// 机床待机时，将该机床所有"加工中"的工位更新为"加工完成"
        /// </summary>
        static void UpdateMacWorkPosStatusToDone(int macnum)
        {
            try
            {
                string macName = GetMacName(macnum);
                Mysql.ExecuteSql("update macworkposstatus set workpiece_status=2 where mac_name='" + macName + "' and workpiece_status=1");
            }
            catch (Exception ex)
            {
                LogHelper.Warn("更新机床" + macnum + "工位加工完成状态失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 安全获取机床状态（容错包装）
        /// </summary>
        static string GetMacStatusSafe(int macId)
        {
            try
            {
                if (MacList.ContainsKey(macId) && MacList[macId] != null)
                    return MacList[macId].GetMacStatus();
            }
            catch (Exception ex)
            {
                LogHelper.Error("获取机床 " + macId + " 状态异常: " + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 根据机床编号和动作获取开始任务号（grab=抓取/取料，place=放置/放料）
        /// </summary>
        static int GetMachineStartNo(int macnum, string action)
        {
            try
            {
                if (MachineConfigs.ContainsKey(macnum))
                {
                    string key = action == "grab" ? "grabStartTaskNo" : "placeStartTaskNo";
                    if (MachineConfigs[macnum].ContainsKey(key))
                        return ParseTaskNoBase(MachineConfigs[macnum][key]);
                }
            }
            catch (Exception ex) { LogHelper.Error("获取机床 " + macnum + " 开始任务号异常: " + ex.Message); }
            return 0;
        }

        // ==================== 机台门控制（roders 机型） ====================
        /// <summary>
        /// 若机台为roders则：创建TCP服务器 → 等待机床连接 → 确认空闲 → 开门
        /// </summary>
        static bool OpenMachineDoorIfRoders(int macId)
        {
            if (!MachineConfigs.ContainsKey(macId) ||
                !MachineConfigs[macId].ContainsKey("type") ||
                MachineConfigs[macId]["type"] != "roders")
                return true; // 非roders无需开门

            LogHelper.Info("机床" + macId + " 为roders，准备连接并开门");
            if (!MacList.ContainsKey(macId) || MacList[macId] == null)
            {
                LogHelper.Error("机床" + macId + " 通讯实例不存在");
                return false;
            }
            if (!MacList[macId].ConnectMac())
            {
                LogHelper.Error("机床" + macId + " 连接失败（TCP服务器创建或机床未连接）");
                return false;
            }
            LogHelper.Info("机床" + macId + " 已连接");

            string status = GetMacStatusSafe(macId);
            LogHelper.Info("机床" + macId + " 当前状态: " + (status ?? "未知"));
            if (status != "待机")
            {
                LogHelper.Warn("机床" + macId + " 非空闲状态(" + status + ")，等待变为空闲...");
                int wait = 0;
                while (wait < 300)
                {
                    if (IsStopRequested()) { LogHelper.Warn("收到停止信号，终止机床状态等待"); return false; }
                    Thread.Sleep(100);
                    status = MacList[macId].GetMacStatus();
                    if (status == "待机") break;
                    wait++;
                }
                if (status != "待机")
                {
                    LogHelper.Error("机床" + macId + " 等待空闲超时，当前状态: " + (status ?? "未知"));
                    return false;
                }
            }
            LogHelper.Info("机床" + macId + " 空闲状态，可以开门");
            if (!MacList[macId].OpenDoor(macId))
            {
                LogHelper.Error("机床" + macId + " 开门失败");
                return false;
            }
            LogHelper.Info("机床" + macId + " 门已打开");
            return true;
        }

        /// <summary>
        /// 若机台为roders则在放置夹具完成后关门
        /// </summary>
        static void CloseMachineDoorIfRoders(int macId)
        {
            if (!MachineConfigs.ContainsKey(macId) ||
                !MachineConfigs[macId].ContainsKey("type") ||
                MachineConfigs[macId]["type"] != "roders")
                return; // 非roders无需关门

            LogHelper.Info("机床" + macId + " 为roders，放置空夹具完成后关门");
            try
            {
                if (MacList.ContainsKey(macId) && MacList[macId] != null)
                    MacList[macId].closeDoor(macId);
            }
            catch (Exception ex)
            {
                LogHelper.Warn("机床" + macId + " 关门失败，但不影响流程继续: " + ex.Message);
            }
        }

        // ==================== 机器人任务 ====================
        /// <summary>
        /// 执行机器人任务：写任务号 → 置位任务开始信号 → 等待任务完成信号
        /// </summary>
        public static bool ExecuteTask(int tasknum)
        {
            // 1. 先复位任务开始信号
            if (!string.IsNullOrEmpty(TaskStartAddr))
            {
                Plc.WriteWR(TaskStartAddr, 0);
                LogHelper.Info("复位任务开始信号: " + TaskStartAddr);
            }

            // 2. 写入任务号
            if (!Plc.WriteDM(PlcDm, tasknum))
            {
                LogHelper.Error("发送任务号失败: " + tasknum);
                return false;
            }
            LogHelper.Info("写入任务号 " + tasknum + " 至 DM" + PlcDm);

            // 3. 发送任务开始信号
            if (!string.IsNullOrEmpty(TaskStartAddr))
            {
                if (!Plc.WriteWR(TaskStartAddr, 1))
                {
                    LogHelper.Error("发送任务开始信号失败: " + TaskStartAddr);
                    return false;
                }
                LogHelper.Info("发送任务开始信号: " + TaskStartAddr);
                Thread.Sleep(300);
                Plc.WriteWR(TaskStartAddr, 0);
                LogHelper.Info("复位任务开始信号: " + TaskStartAddr);
            }
            else
            {
                LogHelper.Warn("未配置任务开始信号，跳过");
            }

            // 4. 等待任务完成信号
            if (!string.IsNullOrEmpty(TaskFinishAddr))
            {
                LogHelper.Info("等待任务完成信号 " + TaskFinishAddr + " = 1...");
                int waitCount = 0;
                while (!Plc.ReadWR(TaskFinishAddr) && waitCount < 3000)
                {
                    if (IsStopRequested()) { LogHelper.Warn("收到停止信号，机器人任务执行失败"); return false; }
                    Thread.Sleep(100);
                    waitCount++;
                }
                if (!Plc.ReadWR(TaskFinishAddr))
                {
                    LogHelper.Error("等待任务完成超时，任务: " + tasknum);
                    return false;
                }
                LogHelper.Info("任务完成信号已收到: " + TaskFinishAddr);
            }

            LogHelper.Success("执行任务 " + tasknum + " 完成");
            return true;
        }

        // ==================== 夹爪置换（快换模式） ====================
        static bool ChangeGripper()
        {
            if (IsFixedGripper)
            {
                LogHelper.Info("固定爪模式，无需置换");
                return true;
            }
            LogHelper.Info("开始置换夹爪流程...目标类型: " + TargetGripperType);

            // 读取当前 robotmessage 状态
            string curName = "", curType = "", curWorkpiece = "";
            int curPosStatus = 0;
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT clampname,clampstatus,clampposstatus,workpiecename FROM robotmessage WHERE id = 1"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        curName = SafeStr(dt.Rows[0]["clampname"]);
                        curType = SafeStr(dt.Rows[0]["clampstatus"]);
                        curPosStatus = SafeInt(dt.Rows[0]["clampposstatus"]);
                        curWorkpiece = SafeStr(dt.Rows[0]["workpiecename"]);
                    }
                }
            }
            catch (Exception ex) { LogHelper.Error("读取 robotmessage 失败: " + ex.Message); return false; }

            // 手上无夹爪 → 直接抓取目标夹爪
            if (curPosStatus == 0)
            {
                LogHelper.Info("手上无夹爪，直接抓取目标夹爪");
                return PickTargetGripper();
            }

            // 手上已有目标夹爪 → 完成
            // clampstatus 数值码约定：electrode=1 steel=2 tool=3 pallet=4（与 racktype 一致）
            if (string.Equals(curType, TargetGripperType, StringComparison.OrdinalIgnoreCase) ||
                (curType == "1" && TargetGripperType == "electrode") ||
                (curType == "2" && TargetGripperType == "steel") ||
                (curType == "3" && TargetGripperType == "tool") ||
                (curType == "4" && TargetGripperType == "pallet"))
            {
                LogHelper.Info("手上已为目标夹爪: " + curName);
                return true;
            }

            // 手上有其他快换爪 → 先清空再抓取目标
            LogHelper.Info("手上有夹爪(" + curName + ")，先清空...");
            bool hasWorkpiece = !string.IsNullOrEmpty(curWorkpiece) && curWorkpiece != "NULL";

            if (hasWorkpiece)
            {
                // 先放工件（空夹具补充原则上在料架无待加工工件时执行，此处兜底）
                int emptySlot = GetEmptyRackSlot();
                if (emptySlot == 0) { LogHelper.Error("无料架空位，无法放置手上工件"); return false; }
                Plc.WriteDM(PlcDm, emptySlot);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { LogHelper.Error("放工件超时"); return false; }
                try { Mysql.ExecuteSql("UPDATE rack_administrate SET rfid='" + curWorkpiece + "',scanstatus=1,posstatus=2 WHERE id=" + emptySlot); }
                catch { }
                ClearRobotWorkpiece();
            }

            // 放置当前夹爪
            if (!ClearCurrentGripper(curType))
                return false;

            // 抓取目标夹爪
            return PickTargetGripper();
        }

        static bool ClearCurrentGripper(string gripperType)
        {
            var cur = FindGripperByType(gripperType);
            if (cur == null) { LogHelper.Warn("未找到匹配夹爪配置，跳过放置"); return true; }
            if (!string.IsNullOrEmpty(cur.SlotPhotoSignal) && Plc.ReadWR(cur.SlotPhotoSignal))
            { LogHelper.Error("夹爪料位有异物"); return false; }
            if (!string.IsNullOrEmpty(cur.PlaceTaskNo))
            {
                int taskNo = ParseTaskNoBase(cur.PlaceTaskNo);
                Plc.WriteDM(PlcDm, taskNo);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { LogHelper.Error("放夹爪超时"); return false; }
            }
            ClearRobotClamp();
            LogHelper.Info("当前夹爪已清空");
            return true;
        }

        static bool PickTargetGripper()
        {
            var target = FindGripperByType(TargetGripperType);
            if (target == null) { LogHelper.Error("未找到目标夹爪配置"); return false; }
            if (!string.IsNullOrEmpty(target.SlotPhotoSignal) && !Plc.ReadWR(target.SlotPhotoSignal))
            { LogHelper.Warn("目标夹爪料位光电无信号，可能无夹爪"); }
            if (!string.IsNullOrEmpty(target.GrabTaskNo))
            {
                int taskNo = ParseTaskNoBase(target.GrabTaskNo);
                Plc.WriteDM(PlcDm, taskNo);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { LogHelper.Error("抓夹爪超时"); return false; }
            }
            // 更新 DB
            try
            {
                Mysql.ExecuteSql("UPDATE robotmessage SET clampname='" + target.Name + "',clampstatus=" +
                    (TargetGripperType == "electrode" ? 1 : TargetGripperType == "steel" ? 2 : TargetGripperType == "tool" ? 3 : 4) +
                    ",clampposstatus=1,workpiecename=NULL,workpiecestatus=0");
            }
            catch { }
            LogHelper.Success("已抓取目标夹爪: " + target.Name);
            return true;
        }

        static GripperConfig FindGripperByType(string type)
        {
            foreach (var g in Grippers)
                if (string.Equals(g.Type, type, StringComparison.OrdinalIgnoreCase) ||
                    (type == "1" && g.Type == "electrode") || (type == "2" && g.Type == "steel") ||
                    (type == "3" && g.Type == "tool") || (type == "4" && g.Type == "pallet"))
                    return g;
            // 禁止兜底返回首个夹爪：否则会静默抓错夹爪，并把 robotmessage.clampstatus 写成错误类型
            LogHelper.Error("未找到类型为 " + type + " 的夹爪配置（已配置: " + string.Join(",", Grippers.ConvertAll(x => x.Type + "/" + x.Name).ToArray()) + "）");
            return null;
        }

        // ==================== robotmessage ====================
        /// <summary>
        /// 更新机器人手爪上的工件信息
        /// </summary>
        static bool Updaterobotmessage(string workpiecename, int workpiecestatus)
        {
            bool flag = false;
            try
            {
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update robotmessage set workpiecename=" + workpieceStr + ",workpiecestatus=" + workpiecestatus + " where id=1";
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                LogHelper.Error("更新机器人手爪信息异常: " + ex.Message);
            }
            return flag;
        }

        // ==================== 辅助 ====================
        static string SafeStr(object val) => val == null || val == DBNull.Value ? "" : val.ToString();
        static int SafeInt(object val) => val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);

        static int ParseTaskNoBase(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int.TryParse(s.ToUpper().Replace("D", "").Replace("DM", "").Trim(), out int r);
            return r;
        }

        static void SendTaskStart()
        {
            if (!string.IsNullOrEmpty(TaskStartAddr)) { Plc.WriteWR(TaskStartAddr, 1); Thread.Sleep(300); Plc.WriteWR(TaskStartAddr, 0); }
        }

        static bool WaitForTaskFinish(int maxTicks)
        {
            int w = 0;
            while (w < maxTicks)
            {
                if (IsStopRequested()) { LogHelper.Warn("收到停止信号，机器人任务执行失败"); return false; }
                if (Plc.ReadWR(TaskFinishAddr)) return true;
                Thread.Sleep(100);
                w++;
            }
            return Plc.ReadWR(TaskFinishAddr);
        }

        static void ClearRobotWorkpiece() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0"); } catch { } }
        static void ClearRobotClamp() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0"); } catch { } }

        static int GetEmptyRackSlot()
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0"))
                {
                    if (dt != null && dt.Rows.Count > 0) return Convert.ToInt32(dt.Rows[0]["id"]);
                }
            }
            catch { }
            return 0;
        }
    }
}
