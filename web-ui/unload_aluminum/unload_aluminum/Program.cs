using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using DataCommonOperate;
using PLC;
using MacCommunicate;

namespace unload_aluminum
{
    class Program
    {
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "unload_aluminum";
        static readonly string DisplayName = "铝板电极下机流程";

        // 数据库
        static string ConnStr;
        static string DbType;
        static Dictionary<int, macinterface> MacList = new Dictionary<int, macinterface>();
        static plc Plc;

        // 停止标志位：前端停止按钮触发后，后端创建 stop.flag，子程序检测到即终止
        static readonly string StopFlagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stop.flag");
        static bool IsStopRequested() { return File.Exists(StopFlagFile); }

        // 夹爪配置
        static string GripperType = "";
        static bool IsFixedGripper = true;
        static string TargetGripperType = "electrode"; // 本流程需要的夹爪类型
        static List<GripperConfig> Grippers = new List<GripperConfig>();
        class GripperConfig
        {
            public string Id;
            public string Type;      // 取值: steel / electrode / tool / pallet（与配置字符串一致）
            public string Name;
            public string Number;
            public string SlotPhotoSignal;
            public string GrabTaskNo;
            public string PlaceTaskNo;
        }

        // PLC 配置
        static string PlcIp;
        static int PlcPort;
        static int PlcDm;
        static string TaskStartAddr;   // 任务开始信号
        static string TaskFinishAddr;   // 任务完成信号
        static string RobotEleAddr;
        static CommonInterface Mysql;

        // ============ 铝板电极下机流程专用常量/配置 ============
        const int AluminumWorkNum = 2;                          // 铝板电极固定在机床 2 号工位（与上机流程 load_aluminum 一致）

        // 料架配置（config.json racks[]），用于计算铝板电极料架/电极料架抓放任务号
        static List<RackConfig> Racks = new List<RackConfig>();
        class RackConfig
        {
            public string Id;
            public string Type;      // "steel"/"electrode"/"tool"/"pallet"/"aluminum"
            public string Name;
            public string Number;
            public string GrabStartTaskNo;
            public string PlaceStartTaskNo;
        }

        // 机床配置（config.json machines{}）
        static Dictionary<int, Dictionary<string, string>> MachineConfigs = new Dictionary<int, Dictionary<string, string>>();
        static Dictionary<int, string> MacNameCache = new Dictionary<int, string>();

        class MacStation { public int Worknum; public string Name; public int Status; }

        static void Main(string[] args)
        {
            LogHelper.Info("【" + StepName + "/" + DisplayName + "】分包启动");
            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败，退出" + DisplayName + "，配置来源: " + ConfigClient.LastSourceText); return; }
                if (!ExtractConfig(cfg)) return;
                InitMachines(cfg);
                LogHelper.Configure(StepName, DisplayName, ApiBase);

                // 西门子机台先做 OPC UA 连接测试（等待最多 10 秒），否则 GetMacStatus 拿不到机台状态
                foreach (var kv in MacList)
                {
                    var m = MachineConfigs.ContainsKey(kv.Key) ? MachineConfigs[kv.Key] : null;
                    string typeStr = m != null && m.ContainsKey("type") ? m["type"] : "";
                    if (typeStr == "siemense")
                    {
                        LogHelper.Info(string.Format("机床 {0} (siemense) 正在测试 OPC UA 连接 {1}:4840 (等待最多 10 秒)...", kv.Key, m.ContainsKey("ip") ? m["ip"] : ""));
                        try
                        {
                            var task = Task.Run(() => kv.Value.ConnectMac());
                            if (task.Wait(TimeSpan.FromSeconds(10)))
                            {
                                if (task.Result) LogHelper.Success(string.Format("机床 {0} OPC UA 连接成功！", kv.Key));
                                else LogHelper.Error(string.Format("机床 {0} OPC UA 连接失败", kv.Key));
                            }
                            else
                            {
                                LogHelper.Error(string.Format("机床 {0} OPC UA 连接超时（10 秒）", kv.Key));
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Error(string.Format("机床 {0} OPC UA 连接异常: {1}", kv.Key, ex.Message));
                        }
                    }
                }

                LoadRackConfigs(cfg);
                Mysql = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                Plc = new plc();
                if (!Plc.Connect(PlcIp, PlcPort))
                {
                    LogHelper.Error("PLC 连接失败: " + PlcIp);
                    return;
                }
                LogHelper.Info("PLC 连接成功");

                // ===== 选择一台“机床上是铝板电极”的机床 =====
                // 判定规则：通过 rfid 或 workpiecename 反查该工件所在料架类型；
                //          料架类型=电极料架 → 走正常下机流程（本流程不处理）；
                //          料架类型=铝板电极料架(racktype=5) → 执行本流程。
                if (!TryPickAluUnloadMachine(out int macnum, out string ip, out int alWorknum,
                                             out string alName, out List<int> fixtureWorknums))
                {
                    LogHelper.Info("没有需要下机的铝板电极机床（机台上无 racktype=5 铝板电极），退出" + DisplayName);
                    return;
                }

                LogHelper.Info("判定通过：机床" + macnum + "(IP:" + ip + ")2号工位上为铝板电极[" + alName + "]（加工完成），"
                    + "其余" + fixtureWorknums.Count + "个工位为空夹具，开始" + DisplayName);

                // ===== 第一次置换夹爪：取空夹具前必须先换为电极夹爪 =====
                TargetGripperType = "electrode";
                if (!ChangeGripper())
                {
                    LogHelper.Error("第一次置换夹爪(电极夹爪)失败，无法执行空夹具下机任务，退出" + DisplayName);
                    return;
                }
                if (!IsAluMachineStillReady(macnum, alWorknum, alName, fixtureWorknums))
                {
                    LogHelper.Warn("机床状态已变化，退出" + DisplayName);
                    return;
                }

                // ===== 第一步：按 4、3、1（工位号降序）将空夹具依次取出，放回电极料架空位 =====
                foreach (int worknum in fixtureWorknums)
                {
                    // 从机床取空夹具
                    if (!OpenMachineDoorIfRoders(macnum))
                    {
                        LogHelper.Error("机台开门失败（取空夹具前），需人工处理，退出" + DisplayName);
                        return;
                    }
                    int fixtureGrabTask = GetMachineStartNo(macnum, "grab") + worknum;
                    LogHelper.Info("机器人从机床" + macnum + " " + worknum + "号工位取空夹具(任务号 " + fixtureGrabTask + ")...");
                    if (!ExecuteTask(fixtureGrabTask))
                    {
                        LogHelper.Error("从机床" + macnum + " " + worknum + "号工位取空夹具失败（机器人可能仍持有夹具），需人工处理！");
                        return;
                    }
                    CloseMachineDoorIfRoders(macnum);

                    string fixtureName = GetMacStationName(macnum, worknum);
                    if (!IsEmptyFixtureStation(fixtureName))
                    {
                        LogHelper.Error("机床" + macnum + " " + worknum + "号工位内容[" + fixtureName + "]不是空夹具，与预期不符，需人工处理！");
                        return;
                    }
                    LogHelper.Success("机器人已从机床" + macnum + " " + worknum + "号工位取出空夹具[" + fixtureName + "]");
                    if (!ClearMacWorkPos(macnum, worknum)) LogHelper.Error("更新机床" + macnum + " " + worknum + "号工位为空失败！");
                    if (!Updaterobotmessage(fixtureName, 0)) LogHelper.Error("更新robotmessage失败(机器人手持空夹具)");

                    // 放到电极料架的空位上
                    int fixtureSlot = GetFirstEmptySlotByRackType(1);   // 电极料架 racktype=1
                    if (fixtureSlot == 0)
                    {
                        LogHelper.Error("电极料架(racktype=1)没有空位放回空夹具[" + fixtureName + "]，机器人仍持有夹具，需人工处理！");
                        return;
                    }
                    int fixturePlaceBase = GetRackStartNoByPos(fixtureSlot, "place");
                    if (fixturePlaceBase <= 0)
                    {
                        LogHelper.Error("料位" + fixtureSlot + "所属料架未配置 placeStartTaskNo，无法放回空夹具，需人工处理！");
                        return;
                    }
                    LogHelper.Info("机器人将空夹具[" + fixtureName + "]放回电极料架空位" + fixtureSlot + "(任务号 " + (fixturePlaceBase + fixtureSlot) + ")...");
                    if (!ExecuteTask(fixturePlaceBase + fixtureSlot))
                    {
                        LogHelper.Error("放回空夹具[" + fixtureName + "]到电极料架空位" + fixtureSlot + "失败（机器人仍持有夹具），需人工处理！");
                        return;
                    }
                    if (!SetRackSlotFixture(fixtureSlot, fixtureName)) LogHelper.Error("更新电极料架空位" + fixtureSlot + "为空夹具失败！");
                    ClearRobotWorkpiece();
                    LogHelper.Success("空夹具[" + fixtureName + "]已放回电极料架空位" + fixtureSlot);
                }

                // ===== 第二步：第二次置换夹爪：取铝板电极前必须确认机器人上无夹爪 =====
                if (!ChangeGripperToEmpty())
                {
                    LogHelper.Error("第二次置换夹爪失败（未能清空机器人手上的夹爪），无法执行铝板电极下机流程，退出" + DisplayName);
                    return;
                }
                if (!IsAluMachineStillReady(macnum, alWorknum, alName, new List<int>()))
                {
                    LogHelper.Warn("机床状态已变化（或铝板电极已不在2号工位），退出" + DisplayName);
                    return;
                }

                // ===== 第三步：从机床抓取铝板电极 =====
                if (!OpenMachineDoorIfRoders(macnum))
                {
                    LogHelper.Error("机台开门失败（取铝板电极前），需人工处理，退出" + DisplayName);
                    return;
                }
                int alGrabTask = GetAluminumGrabTaskNo(macnum);
                LogHelper.Info("机器人从机床" + macnum + " " + alWorknum + "号工位抓取铝板电极[" + alName + "](任务号 " + alGrabTask + ")...");
                if (!ExecuteTask(alGrabTask))
                {
                    LogHelper.Error("抓取铝板电极[" + alName + "]失败，机器人可能未取到电极，退出" + DisplayName + "，需人工处理");
                    return;
                }
                CloseMachineDoorIfRoders(macnum);
                LogHelper.Success("机器人已从机床" + macnum + "抓取铝板电极[" + alName + "]");
                if (!ClearMacWorkPos(macnum, alWorknum)) LogHelper.Error("更新机床" + macnum + " " + alWorknum + "号工位为空失败！");
                if (!Updaterobotmessage(alName, 2)) LogHelper.Error("更新robotmessage失败(机器人手持铝板电极,加工完成)");

                // ===== 第四步：放回铝板电极料架(racktype=5) =====
                // 铝板电极身份在上机时保留在料架中（仅标记加工中），下机优先放回原料位；找不到原料位再找空位
                int alSlot = GetAluminumHomeSlot(alName);
                bool isHomeSlot = alSlot != 0;
                if (!isHomeSlot)
                    alSlot = GetFirstEmptySlotByRackType(5);
                if (alSlot == 0)
                {
                    LogHelper.Error("铝板电极料架(racktype=5)没有可放回铝板电极[" + alName + "]的料位，机器人仍持有电极，需人工处理！");
                    return;
                }
                int alPlaceBase = GetRackStartNoByPos(alSlot, "place");
                if (alPlaceBase <= 0)
                {
                    LogHelper.Error("料位" + alSlot + "所属料架未配置 placeStartTaskNo，无法放回铝板电极，需人工处理！");
                    return;
                }
                LogHelper.Info("机器人将铝板电极[" + alName + "]放回铝板电极料架" + (isHomeSlot ? "原料位" : "空位") + alSlot + "(任务号 " + (alPlaceBase + alSlot) + ")...");
                if (!ExecuteTask(alPlaceBase + alSlot))
                {
                    LogHelper.Error("放回铝板电极[" + alName + "]到铝板电极料架料位" + alSlot + "失败（机器人仍持有电极），需人工处理！");
                    return;
                }
                if (isHomeSlot)
                {
                    if (!MarkRackSlotFinished(alSlot)) LogHelper.Error("更新铝板电极原料位" + alSlot + "为加工完成失败！");
                }
                else
                {
                    if (!SetRackSlotFinishedPiece(alSlot, alName)) LogHelper.Error("更新铝板电极料架空位" + alSlot + "为加工完成件失败！");
                }
                ClearRobotWorkpiece();
                LogHelper.Success(DisplayName + "完成：机床" + macnum + " 空夹具已放回电极料架，铝板电极[" + alName + "]已放回铝板电极料架料位" + alSlot);
            }
            catch (Exception ex)
            {
                LogHelper.Error("【" + DisplayName + "】执行异常: " + ex.Message);
            }
            finally
            {
                if (Plc != null) Plc.close();
            }
            LogHelper.Info("【" + StepName + "/" + DisplayName + "】分包结束");
        }

        // ==================== 选择需要下机的“铝板电极”机床 ====================

        /// <summary>
        /// 判定机台上工件是否为铝板电极：通过 rfid 或 workpiecename 反查该工件所在料架类型。
        /// racktype=1(电极料架)→走正常下机流程；racktype=5(铝板电极料架)→走铝板电极下机流程。
        /// 反查不到记录时兜底：铝板电极上机时固定放在机床 2 号工位，且其余占用工位为空夹具。
        /// </summary>
        static bool TryPickAluUnloadMachine(out int macnum, out string ip, out int alWorknum,
                                            out string alName, out List<int> fixtureWorknums)
        {
            macnum = 0; ip = ""; alWorknum = 0; alName = ""; fixtureWorknums = new List<int>();
            try
            {
                DataTable dt = Mysql.ExeForDtl("select * from macmessage where status!=0 order by id");
                if (dt == null) return false;
                foreach (DataRow r in dt.Rows)
                {
                    int mid = SafeInt(r["id"]);
                    if (!MachineConfigs.ContainsKey(mid)) continue;
                    ip = MachineConfigs[mid].ContainsKey("ip") ? MachineConfigs[mid]["ip"] : "";
                    EnsureMacWorkPosRows(mid);

                    string st = GetMacStatusSafe(mid);
                    if (st != "待机") continue;
                    // 机床已回待机：加工中的工位置为加工完成
                    UpdateMacWorkPosStatusToDone(mid);

                    var stations = GetMacStations(mid);
                    var realPieces = new List<MacStation>();
                    var fixtures = new List<MacStation>();
                    foreach (var s in stations)
                    {
                        if (string.IsNullOrEmpty(s.Name) || s.Name == "NULL") continue;
                        if (IsEmptyFixtureStation(s.Name)) fixtures.Add(s);
                        else realPieces.Add(s);
                    }

                    // 正常电极批量（多件真实工件）交给正常下机流程，不在此处理
                    if (realPieces.Count != 1) continue;
                    MacStation plate = realPieces[0];
                    if (plate.Status != 2) continue;    // 未加工完成

                    // 判定该工件料架类型：仅当工件属于铝板电极料架(racktype=5)才由本流程处理。
                    // 空夹具也会用于填补正常电极的空工位，因此不能以“存在空夹具”判断是否为铝板电极。
                    int rtype = GetRackTypeByWorkpiece(plate.Name);
                    if (rtype != 5)
                    {
                        LogHelper.Info("机床" + mid + " " + plate.Worknum + "号工位工件[" + plate.Name + "]料架类型为"
                            + RackTypeToText(rtype) + "，走正常下机流程（本流程不处理）");
                        continue;
                    }
                    LogHelper.Info("机床" + mid + " " + plate.Worknum + "号工位工件[" + plate.Name + "]料架类型为铝板电极料架(racktype=5)");

                    // 其余占用工位必须是空夹具，按工位号降序取出（4、3、1）
                    fixtureWorknums.Clear();
                    foreach (var f in fixtures) fixtureWorknums.Add(f.Worknum);
                    fixtureWorknums.Sort((a, b) => b.CompareTo(a));

                    macnum = mid;
                    alWorknum = plate.Worknum;
                    alName = plate.Name;
                    LogHelper.Info("选中机床" + mid + "(IP:" + ip + ")：铝板电极[" + alName + "]位于" + alWorknum
                        + "号工位，空夹具工位：" + string.Join(",", fixtureWorknums));
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("选择铝板电极下机机床异常: " + ex.Message);
            }
            return false;
        }

        /// <summary>再次确认机床状态未变化（仍待机、铝板电极仍在指定工位；夹具为空状态允许已取完）</summary>
        static bool IsAluMachineStillReady(int macnum, int alWorknum, string alName, List<int> fixtureWorknums)
        {
            try
            {
                if (!MachineConfigs.ContainsKey(macnum)) return false;
                string st = GetMacStatusSafe(macnum);
                if (st != "待机") return false;
                // 确认铝板电极还在指定工位
                var plate = GetMacStation(macnum, alWorknum);
                if (plate == null) return false;
                if (string.IsNullOrEmpty(plate.Name) || plate.Name == "NULL") return false;
                if (IsEmptyFixtureStation(plate.Name)) return false;
                if (!string.Equals(plate.Name, alName, StringComparison.OrdinalIgnoreCase)) return false;
                // 确认需要取走的空夹具仍在对应工位
                foreach (int worknum in fixtureWorknums)
                {
                    var s = GetMacStation(macnum, worknum);
                    if (s == null) return false;
                    if (!IsEmptyFixtureStation(s.Name)) return false;
                }
                return true;
            }
            catch { return false; }
        }

        // ==================== 机床工位/任务号 ====================

        static List<MacStation> GetMacStations(int macnum)
        {
            var list = new List<MacStation>();
            try
            {
                string macName = GetMacName(macnum);
                DataTable dt = Mysql.ExeForDtl("select worknum, workpiece_name, workpiece_status from macworkposstatus where mac_name='" + macName + "' order by worknum");
                if (dt != null)
                    foreach (DataRow r in dt.Rows)
                        list.Add(new MacStation
                        {
                            Worknum = SafeInt(r["worknum"]),
                            Name = SafeStr(r["workpiece_name"]),
                            Status = SafeInt(r["workpiece_status"])
                        });
            }
            catch (Exception ex) { LogHelper.Error("获取机床" + macnum + "工位异常: " + ex.Message); }
            return list;
        }

        static MacStation GetMacStation(int macnum, int worknum)
        {
            try
            {
                string macName = GetMacName(macnum);
                using (DataTable dt = Mysql.ExeForDtl("select worknum, workpiece_name, workpiece_status from macworkposstatus where mac_name='" + macName + "' and worknum=" + worknum))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return new MacStation
                        {
                            Worknum = SafeInt(dt.Rows[0]["worknum"]),
                            Name = SafeStr(dt.Rows[0]["workpiece_name"]),
                            Status = SafeInt(dt.Rows[0]["workpiece_status"])
                        };
                }
            }
            catch (Exception ex) { LogHelper.Error("获取机床" + macnum + "工位" + worknum + "异常: " + ex.Message); }
            return null;
        }

        static string GetMacStationName(int macnum, int worknum)
        {
            var s = GetMacStation(macnum, worknum);
            return s == null ? "" : s.Name;
        }

        static int GetMacTotalPositions(int macnum)
        {
            try
            {
                string macName = GetMacName(macnum);
                using (DataTable dt = Mysql.ExeForDtl("select count(*) as cnt from macworkposstatus where mac_name='" + macName + "'"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return SafeInt(dt.Rows[0]["cnt"]);
                }
            }
            catch (Exception ex) { LogHelper.Error("获取机床总工位数异常: " + ex.Message); }
            return 0;
        }

        // 确保 macworkposstatus 中有该机床的工位行（按配置 stationCount 补齐缺失行）
        static void EnsureMacWorkPosRows(int macnum)
        {
            try
            {
                string macName = GetMacName(macnum);
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                object cnt = Db.GetValue("select count(*) from macworkposstatus where mac_name='" + macName + "'");
                int existing = cnt == null || cnt == DBNull.Value ? 0 : Convert.ToInt32(cnt);
                if (existing > 0) return;
                int stationCount = 1;
                if (MachineConfigs.ContainsKey(macnum) && MachineConfigs[macnum].ContainsKey("stationCount"))
                    int.TryParse(MachineConfigs[macnum]["stationCount"], out stationCount);
                for (int w = 1; w <= stationCount; w++)
                    Db.ExecuteSql("insert into macworkposstatus (mac_name, worknum, workpiece_name, workpiece_status) values ('" + macName + "', " + w + ", NULL, 0)");
                LogHelper.Info("机床 " + macnum + " 工位表为空，已按配置补齐 " + stationCount + " 个工位行");
            }
            catch (Exception ex) { LogHelper.Error("补齐机床 " + macnum + " 工位行异常: " + ex.Message); }
        }

        // 根据机床编号和动作获取开始任务号基址（grab=取料，place=放料）——调用处再加工位号
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

        /// <summary>获取铝板电极从机床抓取的固定任务号（对应机床 2 号工位，固定值，不加工位号）</summary>
        static int GetAluminumGrabTaskNo(int macnum)
        {
            try
            {
                if (MachineConfigs.ContainsKey(macnum) && MachineConfigs[macnum].ContainsKey("aluminumGrabTaskNo"))
                    return ParseTaskNoBase(MachineConfigs[macnum]["aluminumGrabTaskNo"]);
            }
            catch (Exception ex) { LogHelper.Error("获取机床 " + macnum + " 铝板电极抓取任务号异常: " + ex.Message); }
            return 0;
        }

        static string GetMacName(int macnum)
        {
            lock (MacNameCache)
            {
                if (MacNameCache.ContainsKey(macnum))
                    return MacNameCache[macnum];
            }
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                object val = Db.GetValue("select machinename from macmessage where id=" + macnum);
                if (val != null && val != DBNull.Value && !string.IsNullOrEmpty(val.ToString()))
                {
                    lock (MacNameCache) { MacNameCache[macnum] = val.ToString(); }
                    return val.ToString();
                }
            }
            catch (Exception ex) { LogHelper.Error("获取机床 " + macnum + " 名称异常: " + ex.Message); }
            return macnum.ToString();
        }

        // 安全获取机床状态（容错包装）
        static string GetMacStatusSafe(int macId)
        {
            try
            {
                if (MacList.ContainsKey(macId) && MacList[macId] != null)
                    return MacList[macId].GetMacStatus();
            }
            catch (Exception ex) { LogHelper.Error("获取机床 " + macId + " 状态异常: " + ex.Message); }
            return null;
        }

        // 更新机床指定工位（workpiecename 传空字符串表示清空工位）
        static bool UpdateMacWorkPosStatus(int macnum, int worknum, string workpiecename, int workpiecestatus)
        {
            bool flag = false;
            try
            {
                string macName = GetMacName(macnum);
                string nameStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update macworkposstatus set workpiece_name=" + nameStr + ", workpiece_status=" + workpiecestatus +
                             " where mac_name='" + macName + "' and worknum=" + worknum;
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex) { LogHelper.Error("更新机床工位表异常: " + ex.Message); }
            return flag;
        }

        // 清空机床工位工件信息和工件状态
        static bool ClearMacWorkPos(int macnum, int worknum)
        {
            return UpdateMacWorkPosStatus(macnum, worknum, "", 0);
        }

        // 机床待机时，将该机床所有"加工中"的工位更新为"加工完成"
        static void UpdateMacWorkPosStatusToDone(int macnum)
        {
            try
            {
                string macName = GetMacName(macnum);
                Mysql.ExecuteSql("update macworkposstatus set workpiece_status=2 where mac_name='" + macName + "' and workpiece_status=1");
            }
            catch (Exception ex) { LogHelper.Warn("更新机床" + macnum + "工位加工完成状态失败: " + ex.Message); }
        }

        // 空夹具占位判定：机床工位读到的 rfid/工件名含 "EMPTY"（如 EMPTY1~EMPTY4）即视为空夹具
        static bool IsEmptyFixtureStation(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOf("EMPTY", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ==================== 工件料架类型判定 ====================

        /// <summary>通过 rfid 或 workpiecename 反查工件所在料架类型（返回 racktype 数字，查不到返回 0）</summary>
        static int GetRackTypeByWorkpiece(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("select racktype from rack_administrate where rfid='" + name + "' or workpiecename='" + name + "' order by id"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return SafeInt(dt.Rows[0]["racktype"]);
                }
            }
            catch (Exception ex) { LogHelper.Error("反查工件料架类型异常: " + ex.Message); }
            return 0;
        }

        static string RackTypeToText(int racktype)
        {
            if (racktype == 1) return "电极料架";
            if (racktype == 2) return "钢件料架";
            if (racktype == 3) return "刀具料架";
            if (racktype == 4) return "托盘料架";
            if (racktype == 5) return "铝板电极料架";
            return "未知料架(" + racktype + ")";
        }

        // 料位号 → 料架类型字符串（DB racktype 数字映射 config 字符串）
        static string GetRackTypeByPos(int pos)
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("select racknum, racktype from rack_administrate where id=" + pos))
                {
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        string racknum = SafeStr(dt.Rows[0]["racknum"]);
                        foreach (var rack in Racks)
                            if (string.Equals(rack.Number, racknum, StringComparison.OrdinalIgnoreCase))
                                return rack.Type;
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

        // 料架按类型获取抓/放开始任务号基址
        static int GetRackStartNoByType(string typeStr, string action)
        {
            foreach (var rack in Racks)
                if (string.Equals(rack.Type, typeStr, StringComparison.OrdinalIgnoreCase))
                    return action == "grab" ? ParseTaskNoBase(rack.GrabStartTaskNo) : ParseTaskNoBase(rack.PlaceStartTaskNo);
            return 0;
        }

        // 料位号所属料架抓/放开始任务号（优先 racknum 匹配 config racks[]，兜底按料架类型）
        static int GetRackStartNoByPos(int pos, string action)
        {
            try
            {
                string racknum = "";
                using (DataTable dt = Mysql.ExeForDtl("select racknum from rack_administrate where id=" + pos))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        racknum = SafeStr(dt.Rows[0]["racknum"]);
                }
                foreach (var rack in Racks)
                    if (string.Equals(rack.Number, racknum, StringComparison.OrdinalIgnoreCase))
                        return action == "grab" ? ParseTaskNoBase(rack.GrabStartTaskNo) : ParseTaskNoBase(rack.PlaceStartTaskNo);
                string rackType = GetRackTypeByPos(pos);
                return GetRackStartNoByType(rackType, action);
            }
            catch (Exception ex) { LogHelper.Error("获取料位 " + pos + " 任务号异常: " + ex.Message); }
            return 0;
        }

        // ==================== 料架料位 ====================

        /// <summary>获取指定 racktype 料架的第一个空位（posstatus=1 且无 rfid），没有返回 0</summary>
        static int GetFirstEmptySlotByRackType(int racktype)
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("select id from rack_administrate where racktype=" + racktype +
                    " and posstatus=1 and (rfid is null or rfid='') order by id"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return SafeInt(dt.Rows[0]["id"]);
                }
            }
            catch (Exception ex) { LogHelper.Error("查询 racktype=" + racktype + " 料架空位异常: " + ex.Message); }
            return 0;
        }

        /// <summary>铝板电极下机时，按 rfid/workpiecename 反查其在铝板电极料架(racktype=5)的原料位</summary>
        static int GetAluminumHomeSlot(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("select id from rack_administrate where racktype=5 and (rfid='" + name + "' or workpiecename='" + name + "') order by id"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return SafeInt(dt.Rows[0]["id"]);
                }
            }
            catch (Exception ex) { LogHelper.Error("反查铝板电极原料位异常: " + ex.Message); }
            return 0;
        }

        /// <summary>成品放回原料位：保留 rfid/workpiecename 身份，仅标记加工完成(workstate=2)</summary>
        static bool MarkRackSlotFinished(int pos)
        {
            try
            {
                string str = "UPDATE rack_administrate SET workstate=2, scanstatus=1, posstatus=2 WHERE id=" + pos;
                if (Mysql.ExecuteSql(str))
                {
                    LogHelper.NotifyRackChanged();
                    return true;
                }
            }
            catch (Exception ex) { LogHelper.Error("更新料位 " + pos + " 为加工完成异常: " + ex.Message); }
            return false;
        }

        // 找可用的通用料架空位（可接受放件）: posstatus=1(空位) && scanstatus=0
        static int GetEmptyRackSlot()
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("select id from rack_administrate where posstatus=1 and scanstatus=0"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return SafeInt(dt.Rows[0]["id"]);
                }
            }
            catch { }
            return 0;
        }

        /// <summary>放回空夹具到料位：占位 + rfid=空夹具号</summary>
        static bool SetRackSlotFixture(int pos, string fixtureRfid)
        {
            try
            {
                string str = "UPDATE rack_administrate SET rfid='" + fixtureRfid + "', workpiecename=NULL, workstate=0, scanstatus=1, posstatus=2 WHERE id=" + pos;
                if (Mysql.ExecuteSql(str))
                {
                    LogHelper.NotifyRackChanged();
                    return true;
                }
            }
            catch (Exception ex) { LogHelper.Error("更新料位 " + pos + " 为空夹具异常: " + ex.Message); }
            return false;
        }

        /// <summary>放回加工完成的铝板电极到料位：workstate=2 表示加工完成，避免被当作待加工件重复上机</summary>
        static bool SetRackSlotFinishedPiece(int pos, string name)
        {
            try
            {
                string str = "UPDATE rack_administrate SET rfid='" + name + "', workpiecename='" + name + "', workstate=2, scanstatus=1, posstatus=2 WHERE id=" + pos;
                if (Mysql.ExecuteSql(str))
                {
                    LogHelper.NotifyRackChanged();
                    return true;
                }
            }
            catch (Exception ex) { LogHelper.Error("更新料位 " + pos + " 为加工完成件异常: " + ex.Message); }
            return false;
        }

        // 清空料架指定格位（取走后置空位）
        static bool ClearRackSlot(int pos)
        {
            try
            {
                string str = "UPDATE rack_administrate SET rfid='', workpiecename=NULL, workstate=0, posstatus=1, scanstatus=1 WHERE id=" + pos;
                if (Mysql.ExecuteSql(str))
                {
                    LogHelper.NotifyRackChanged();
                    return true;
                }
            }
            catch (Exception ex) { LogHelper.Error("清空料架格位 " + pos + " 异常: " + ex.Message); }
            return false;
        }

        // ==================== robotmessage ====================

        // 更新机器人手爪上的工件信息和料位状态
        static bool Updaterobotmessage(string workpiecename, int workpiecestatus)
        {
            bool flag = false;
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update robotmessage set workpiecename=" + workpieceStr + ",workpiecestatus=" + workpiecestatus + " where id=1";
                if (Db.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex) { LogHelper.Error("更新机器人手爪信息异常: " + ex.Message); }
            return flag;
        }

        static void ClearRobotWorkpiece()
        {
            try { Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0"); } catch { }
        }

        static void ClearRobotClamp()
        {
            try { Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0"); } catch { }
        }

        // ==================== 夹爪置换 ====================

        /// <summary>置换夹爪到 TargetGripperType（快换爪模式：清理当前爪→抓目标爪；固定爪模式：仅确认手上无工件）</summary>
        static bool ChangeGripper()
        {
            if (IsFixedGripper)
            {
                LogHelper.Info("固定爪模式，无需置换夹爪");
                return true;
            }

            LogHelper.Info("开始置换夹爪流程...目标类型: " + TargetGripperType);

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

            // 手上已是目标夹爪 → 完成
            if (TypeMatches(curType, TargetGripperType))
            {
                LogHelper.Info("手上已为目标夹爪: " + curName);
                return true;
            }

            // 手上有其他夹爪：先放工件、再清空当前夹爪、最后抓目标夹爪
            LogHelper.Info("手上有夹爪(" + curName + ")，先清空再抓取目标夹爪...");
            bool hasWorkpiece = !string.IsNullOrEmpty(curWorkpiece) && curWorkpiece != "NULL";
            if (hasWorkpiece)
            {
                if (!StashRobotPiece(curWorkpiece)) return false;
            }
            if (!ClearCurrentGripper(curType)) return false;
            return PickTargetGripper();
        }

        /// <summary>清空机器人手上的夹爪与工件（clampposstatus=0），用于无夹爪执行铝板电极任务</summary>
        static bool ChangeGripperToEmpty()
        {
            LogHelper.Info("换夹爪(清空)：要求机器人手上无夹爪才可以执行取铝板电极/放机床任务");

            if (IsFixedGripper)
            {
                // 固定爪：无需卸爪，但必须确认手上没有工件
                string piece = "";
                try
                {
                    using (DataTable dt = Mysql.ExeForDtl("SELECT workpiecename FROM robotmessage WHERE id = 1"))
                        if (dt != null && dt.Rows.Count > 0)
                            piece = SafeStr(dt.Rows[0]["workpiecename"]);
                }
                catch (Exception ex) { LogHelper.Error("读取 robotmessage 失败: " + ex.Message); return false; }
                if (!string.IsNullOrEmpty(piece) && piece != "NULL")
                {
                    LogHelper.Error("固定爪模式：机器人手上仍有工件[" + piece + "]，无法执行铝板电极抓取任务，需人工处理");
                    return false;
                }
                LogHelper.Info("固定爪模式确认机器人手上无工件，可执行铝板电极任务");
                return true;
            }

            // 快换爪模式
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

            bool hasWorkpiece = !string.IsNullOrEmpty(curWorkpiece) && curWorkpiece != "NULL";

            // 手上无夹爪
            if (curPosStatus == 0)
            {
                if (hasWorkpiece)
                {
                    LogHelper.Warn("机器人无夹爪但记录持有工件[" + curWorkpiece + "]，先放回料架");
                    if (!StashRobotPiece(curWorkpiece)) return false;
                }
                LogHelper.Info("机器人手上无夹爪，可执行铝板电极任务");
                return true;
            }

            // 手上有工件：先放回料架空位
            if (hasWorkpiece)
            {
                LogHelper.Info("机器人夹爪上有工件[" + curWorkpiece + "]，先放回料架空位...");
                if (!StashRobotPiece(curWorkpiece)) return false;
            }

            // 放置当前夹爪到其夹具位 → 机器人空手
            LogHelper.Info("放置当前夹爪[" + curName + "](类型码 " + curType + ")回夹具位...");
            return ClearCurrentGripper(curType);
        }

        // 当前夹爪 clampstatus 与目标类型是否一致（兼容存数值码：electrode=1 steel=2 tool=3 pallet=4）
        static bool TypeMatches(string curType, string target)
        {
            if (string.IsNullOrEmpty(curType)) return false;
            if (string.Equals(curType, target, StringComparison.OrdinalIgnoreCase)) return true;
            if (curType == "1" && target == "electrode") return true;
            if (curType == "2" && target == "steel") return true;
            if (curType == "3" && target == "tool") return true;
            if (curType == "4" && target == "pallet") return true;
            return false;
        }

        // 把机器人手上的工件放到料架空位（任务号=料架放料基址+空料位号）
        static bool StashRobotPiece(string piece)
        {
            int emptySlot = GetEmptyRackSlot();
            if (emptySlot == 0)
            {
                LogHelper.Error("没有可用的料架空位存放机器人手上的工件[" + piece + "]，需人工处理");
                return false;
            }
            int placeBase = GetRackStartNoByPos(emptySlot, "place");
            if (placeBase <= 0) placeBase = emptySlot;   // 兼容历史：个别料架未配放料任务号时直接使用料位号
            Plc.WriteDM(PlcDm, placeBase + (placeBase == emptySlot ? 0 : emptySlot));
            SendTaskStart();
            if (!WaitForTaskFinish(3000))
            {
                LogHelper.Error("放置工件[" + piece + "]到料位" + emptySlot + "超时");
                return false;
            }
            try
            {
                Mysql.ExecuteSql("UPDATE rack_administrate SET rfid='" + piece + "',scanstatus=1,workstate=0,posstatus=2 WHERE id=" + emptySlot);
                LogHelper.NotifyRackChanged();
            }
            catch (Exception ex) { LogHelper.Warn("更新料架失败: " + ex.Message); }
            ClearRobotWorkpiece();
            LogHelper.Success("机器人手上工件[" + piece + "]已放回料位" + emptySlot);
            return true;
        }

        // 把当前夹爪放回其夹具位（SlotPhotoSignal 光电有料则不允许放）
        static bool ClearCurrentGripper(string gripperType)
        {
            var cur = FindGripperByType(gripperType);
            if (cur == null) { LogHelper.Warn("未找到匹配夹爪配置，跳过放置"); return true; }
            if (!string.IsNullOrEmpty(cur.SlotPhotoSignal) && Plc.ReadWR(cur.SlotPhotoSignal))
            {
                LogHelper.Error("夹爪料位有异物，无法放置当前夹爪");
                return false;
            }
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
            return PickGrp(TargetGripperType);
        }

        // 抓目标夹爪并写 robotmessage（clampstatus 数值码：electrode=1 steel=2 tool=3 pallet=4）
        static bool PickGrp(string typeStr)
        {
            var g = FindGripperByType(typeStr);
            if (g == null)
            {
                LogHelper.Error("未找到目标夹爪配置: " + typeStr);
                return false;
            }
            if (!string.IsNullOrEmpty(g.SlotPhotoSignal) && !Plc.ReadWR(g.SlotPhotoSignal))
                LogHelper.Warn("目标夹爪料位光电无信号，可能无夹爪");
            if (!string.IsNullOrEmpty(g.GrabTaskNo))
            {
                int taskNo = ParseTaskNoBase(g.GrabTaskNo);
                Plc.WriteDM(PlcDm, taskNo);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { LogHelper.Error("抓夹爪超时"); return false; }
            }
            try
            {
                int code = typeStr == "electrode" ? 1 : typeStr == "steel" ? 2 : typeStr == "tool" ? 3 : 4;
                Mysql.ExecuteSql("UPDATE robotmessage SET clampname='" + g.Name + "',clampstatus=" + code + ",clampposstatus=1,workpiecename=NULL,workpiecestatus=0");
            }
            catch { }
            LogHelper.Success("已抓取目标夹爪: " + g.Name);
            return true;
        }

        // 按类型查找夹爪配置（兼容数值码：electrode=1 steel=2 tool=3 pallet=4）
        static GripperConfig FindGripperByType(string typeStr)
        {
            foreach (var g in Grippers)
            {
                if (string.Equals(g.Type, typeStr, StringComparison.OrdinalIgnoreCase)) return g;
                if (typeStr == "1" && g.Type == "electrode") return g;
                if (typeStr == "2" && g.Type == "steel") return g;
                if (typeStr == "3" && g.Type == "tool") return g;
                if (typeStr == "4" && g.Type == "pallet") return g;
            }
            // 禁止兜底返回首个夹爪：否则会静默抓错夹爪，并把 robotmessage.clampstatus 写成错误类型
            LogHelper.Error("未找到类型为 " + typeStr + " 的夹爪配置（已配置: " + string.Join(",", Grippers.ConvertAll(x => x.Type + "/" + x.Name).ToArray()) + "）");
            return null;
        }

        // ==================== 任务执行原语 ====================

        static void SendTaskStart()
        {
            if (!string.IsNullOrEmpty(TaskStartAddr))
            {
                Plc.WriteWR(TaskStartAddr, 1);
                Thread.Sleep(300);
                Plc.WriteWR(TaskStartAddr, 0);
            }
        }

        static bool WaitForTaskFinish(int maxTicks)
        {
            int w = 0;
            while (w < maxTicks)
            {
                if (IsStopRequested())
                {
                    LogHelper.Warn("收到停止信号，机器人任务执行失败");
                    return false;
                }
                if (Plc.ReadWR(TaskFinishAddr)) return true;
                Thread.Sleep(100);
                w++;
            }
            return Plc.ReadWR(TaskFinishAddr);
        }

        public static bool ExecuteTask(int tasknum)
        {
            if (!string.IsNullOrEmpty(TaskStartAddr))
                Plc.WriteWR(TaskStartAddr, 0);

            if (!Plc.WriteDM(PlcDm, tasknum))
            {
                LogHelper.Error("发送任务号失败: " + tasknum);
                return false;
            }
            LogHelper.Info("写入任务号 " + tasknum + " 至 DM" + PlcDm);

            if (!string.IsNullOrEmpty(TaskStartAddr))
            {
                if (!Plc.WriteWR(TaskStartAddr, 1))
                {
                    LogHelper.Error("发送任务开始信号失败: " + TaskStartAddr);
                    return false;
                }
                Thread.Sleep(300);
                Plc.WriteWR(TaskStartAddr, 0);
            }

            if (!string.IsNullOrEmpty(TaskFinishAddr))
            {
                int waitCount = 0;
                while (!Plc.ReadWR(TaskFinishAddr) && waitCount < 3000)
                {
                    if (IsStopRequested())
                    {
                        LogHelper.Warn("收到停止信号，机器人任务执行失败");
                        return false;
                    }
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

        static int ParseTaskNoBase(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int.TryParse(s.ToUpper().Replace("D", "").Replace("DM", "").Trim(), out int r);
            return r;
        }

        // ==================== 机台开门/关门 ====================

        // 若机台为roders则：创建TCP服务器 → 等待机床连接 → 确认空闲 → 开门
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
            string status = MacList[macId].GetMacStatus();
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

        // 若机台为roders则在取/放工件完成后关门（关门失败不阻断流程）
        static bool CloseMachineDoorIfRoders(int macId)
        {
            if (!MachineConfigs.ContainsKey(macId) ||
                !MachineConfigs[macId].ContainsKey("type") ||
                MachineConfigs[macId]["type"] != "roders")
                return true;
            LogHelper.Info("机床" + macId + " 为roders，任务完成后关门");
            try
            {
                if (MacList.ContainsKey(macId) && MacList[macId] != null)
                {
                    if (!MacList[macId].closeDoor(macId))
                        LogHelper.Warn("机床" + macId + " 关门失败，但不影响流程继续");
                }
            }
            catch (Exception ex) { LogHelper.Warn("机床" + macId + " 关门异常: " + ex.Message); }
            return true;
        }

        // ==================== 配置读取 ====================

        // ---------- ExtractConfig（database/plc/line/grippers 节点） ----------
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
                        RobotEleAddr = ConfigClient.GetStr(robotSig, "photo", "");
                    }
                    // 兜底旧格式
                    var addrs = ConfigClient.GetDict(plcCfg, "addresses");
                    if (string.IsNullOrEmpty(TaskFinishAddr) && addrs.Count > 0)
                        TaskFinishAddr = ConfigClient.GetStr(addrs, "task_finish", "");
                    if (string.IsNullOrEmpty(RobotEleAddr) && addrs.Count > 0)
                        RobotEleAddr = ConfigClient.GetStr(addrs, "robot_ele", "");
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

                // 加载夹爪配置
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

        // ---------- InitMachines（machines 节点 → MachineConfigs + MacList） ----------
        static void InitMachines(Dictionary<string, object> cfg)
        {
            try
            {
                MachineConfigs.Clear();
                if (cfg == null || cfg.Count == 0 || !cfg.ContainsKey("machines"))
                {
                    LogHelper.Warn("配置为空或无 machines 节点，跳过机床初始化");
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
                    string workCoords = m.ContainsKey("workCoords") ? m["workCoords"].ToString() : "";
                    string grabStartTaskNo = m.ContainsKey("grabStartTaskNo") ? m["grabStartTaskNo"].ToString() : "";
                    string placeStartTaskNo = m.ContainsKey("placeStartTaskNo") ? m["placeStartTaskNo"].ToString() : "";
                    string aluminumGrabTaskNo = m.ContainsKey("aluminumGrabTaskNo") ? m["aluminumGrabTaskNo"].ToString() : "";
                    string aluminumPlaceTaskNo = m.ContainsKey("aluminumPlaceTaskNo") ? m["aluminumPlaceTaskNo"].ToString() : "";
                    MachineConfigs[macId] = new Dictionary<string, string>() {
                        { "type", typeStr }, { "ip", ip }, { "stationCount", stationCount },
                        { "workCoords", workCoords }, { "grabStartTaskNo", grabStartTaskNo }, { "placeStartTaskNo", placeStartTaskNo },
                        { "aluminumGrabTaskNo", aluminumGrabTaskNo }, { "aluminumPlaceTaskNo", aluminumPlaceTaskNo }
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
                        LogHelper.Info("机床 " + macId + " (" + typeStr + ") 已创建实例，IP: " + ip + (string.IsNullOrEmpty(sessionUrl) ? "" : "，会话: " + sessionUrl));
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

        // ---------- LoadRackConfigs（racks[] 节点，type 含 electrode/aluminum） ----------
        static void LoadRackConfigs(Dictionary<string, object> cfg)
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
                    foreach (var item in al) racksList.Add(item);
                else if (racksRaw is object[] arr)
                    foreach (var item in arr) racksList.Add(item);
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

        // ==================== 通用工具 ====================
        static string SafeStr(object val) => val == null || val == DBNull.Value ? "" : val.ToString();
        static int SafeInt(object val)
        {
            if (val == null || val == DBNull.Value) return 0;
            if (val is int) return (int)val;
            int.TryParse(val.ToString().Trim(), out int r);
            return r;
        }
    }
}
