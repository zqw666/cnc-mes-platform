using DataCommonOperate;
using PLC;
using MacCommunicate;
using System.Web.Script.Serialization;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace change_machine
{
    class Program
    {
        // 配置：从当前运行目录向上逐级查找 config.json
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "change_machine";
        static readonly string DisplayName = "换料程序";

        // 数据库
        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
        static string DbType = "mysql";
        static CommonInterface Mysql;
        static Dictionary<int, macinterface> MacList = new Dictionary<int, macinterface>();
        static transfer transferprogram = new transfer();
        static bool ProcessTermination = false;
        static plc Plc;

        // 停止标志位：前端停止按钮触发后，后端创建 stop.flag，子程序检测到即终止
        static readonly string StopFlagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stop.flag");
        static bool IsStopRequested() { return File.Exists(StopFlagFile); }

        // 夹爪配置
        static string GripperType = "";
        static bool IsFixedGripper = true;
        static string TargetGripperType = "steel";
        static bool PalletEnabled = true;  // 随行台有无
        static List<GripperConfig> Grippers = new List<GripperConfig>();
        class GripperConfig {
            public string Id, Type, Name, Number;
            public string SlotPhotoSignal, GrabTaskNo, PlaceTaskNo;
        }

        // ==================== 从总包配置文件中读取 ====================
        
        
        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
        static int MachineStartTaskNo;
        static int PlcDm = 100;                   // 任务号 DM 地址
        static string TaskStartAddr = "";          // 任务开始信号
        static string TaskFinishAddr = "205.00";   // 任务完成信号
        static string RobotEleAddr = "";            // 机器人光电信号

        // 转台运动控制信号（来自 rackoperate）
        static readonly string SignalReturnZero = "120.00";   // 回到零点信号
        static readonly string SignalDegree = "110";           // 移动度数信号（WR字）
        static readonly string SignalAllowMove = "108.00";     // 允许移动信号
        static readonly string SignalStartMove = "108.01";     // 开始移动信号
        static readonly string SignalMoveDone = "108.04";      // 移动到位信号
        static readonly string SignalAtZero = "108.05";        // 在0度标志位信号

        static void Main(string[] args)
        {
            LogHelper.Info( "开始执行" + DisplayName);
            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败，配置来源: " + ConfigClient.LastSourceText); return; }
                if (!ExtractConfig(cfg)) return;
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

                // 快换夹爪 + 无随行台：先判定同一台空闲机床有加工完成工位（需下机） + 待加工工件
                if (!IsFixedGripper && !PalletEnabled)
                {
                    if (!HasIdleMachineNeedUnload() || !HasPendingWorkpiece())
                    {
                        LogHelper.Info( "不满足换料流程前置条件（同一台空闲机床需下机/待加工工件），结束换料流程");
                        return;
                    }
                    LogHelper.Info( "换料流程前置条件满足（同一台空闲机床需下机+待加工工件）");
                }

                // 本次要处理的机床队列（只查一次：夹爪类型的判定依据与实际换料对象同源）
                var waitmaclist = Waitmaclist();
                if (waitmaclist.Count == 0)
                {
                    LogHelper.Info( "无待机且有加工完成工件的机床");
                    return;
                }

                // 目标夹爪类型 = 该队列首台在机工件所在料架的类型（快换爪模式）
                if (!IsFixedGripper)
                {
                    TargetGripperType = ResolveTargetGripperType(waitmaclist);
                    if (string.IsNullOrEmpty(TargetGripperType))
                    {
                        LogHelper.Error("无法确定目标夹爪类型，退出换料流程");
                        return;
                    }
                }

                // 执行置换夹爪流程（必须在确定本次目标工件之后执行）
                if (!ChangeGripper())
                {
                    LogHelper.Error( "置换夹爪失败，退出换料流程");
                    return;
                }

                // 执行换料主流程
                RunChangePiece(waitmaclist);
            }
            catch (Exception ex)
            {
                LogHelper.Error( "执行异常: " + ex.Message);
            }
            finally
            {
                LogHelper.Info( "结束执行" + DisplayName);
            }
        }

        /// <summary>
        /// 换料主流程：钢件 + 无随行台
        /// 固定双爪：一次任务完成取旧放新
        /// 快换爪+多次旋转：取料→转台旋转→放回→加工
        /// </summary>
        static void RunChangePiece(List<string> waitmaclist)
        {
            if (waitmaclist == null || waitmaclist.Count == 0)
            {
                LogHelper.Info( "无待机且有加工完成工件的机床");
                return;
            }

            foreach (var macinfo in waitmaclist)
            {
                if (ProcessTermination) break;

                int macnum = Convert.ToInt16(macinfo.Split(',')[0]);
                string ip = macinfo.Split(',')[1];
                bool isRoders = IsMachineRoders(macnum);

                LogHelper.Info( "开始处理机床" + macnum);

                // Roders: 连接并开门
                if (isRoders && !OpenRodersDoor(macnum))
                {
                    ProcessTermination = true;
                    break;
                }

                // 读取机床上"加工中"的工位
                var wp = GetMacProcessingWorkpiece(macnum);
                if (wp == null || string.IsNullOrEmpty(wp.Item2))
                {
                    LogHelper.Info( "机床" + macnum + " 无加工中工位，跳过");
                    if (isRoders) MacCloseDoor(macnum);
                    continue;
                }
                int worknum = wp.Item1;
                string workpiecename = wp.Item2;

                // 夹爪类型跟随本次实际搬运的工件；本流程内若料架类型变化则再置换一次夹爪
                if (!IsFixedGripper)
                {
                    int needRt = GetRackTypeNumByWorkpiece(workpiecename);
                    string needType = RackTypeToGripper(needRt);
                    if (string.IsNullOrEmpty(needType))
                    {
                        LogHelper.Error( "工件 " + workpiecename + " 料架类型无法识别（racktype=" + needRt + "），换料流程终止！");
                        if (isRoders) MacCloseDoor(macnum);
                        ProcessTermination = true;
                        break;
                    }
                    if (!string.Equals(needType, TargetGripperType, StringComparison.OrdinalIgnoreCase))
                    {
                        LogHelper.Info( "工件 " + workpiecename + " 需要夹爪 " + needType
                            + "，当前为 " + TargetGripperType + "，重新置换夹爪");
                        TargetGripperType = needType;
                        if (!ChangeGripper())
                        {
                            LogHelper.Error( "置换夹爪失败，换料流程终止！");
                            if (isRoders) MacCloseDoor(macnum);
                            ProcessTermination = true;
                            break;
                        }
                    }
                }

                // 更新工位状态为加工完成
                if (!UpdateMacWorkPosStatus(macnum, worknum, 2))
                {
                    LogHelper.Error( "更新机床" + macnum + "工位" + worknum + "加工完成状态失败");
                    ProcessTermination = true;
                    break;
                }

                // 固定双爪：一次任务完成取旧放新
                if (IsFixedGripper && GripperType == "fixed-double")
                {
                    if (!ExecuteTask(GetMachineStartNo(macnum, "grab") + macnum))
                    {
                        LogHelper.Error( "机器人执行换料任务失败(取旧+放新)，机床: " + macnum);
                        ProcessTermination = true;
                        break;
                    }
                    LogHelper.Info( "换料任务完成: 取旧(" + workpiecename + ") + 放新，机床: " + macnum);
                    UpdateMachinePieceOnChangePair(macnum, worknum, workpiecename);
                }
                // 快换爪 + 多次旋转加工
                else if (!IsFixedGripper)
                {
                    // 发送取件任务
                    if (!ExecuteTask(GetMachineStartNo(macnum, "grab") + macnum))
                    {
                        LogHelper.Error( "机器人从机床" + macnum + "取料失败");
                        ProcessTermination = true;
                        break;
                    }
                    LogHelper.Info( "从机床" + macnum + "取料完成: " + workpiecename);
                    // 清空机床工位
                    ClearMacWorkPos(macnum, worknum);
                    // 更新夹爪（加工完成状态）
                    Updaterobotmessage(workpiecename, 2, 2);

                    // 放置到旋转台
                    int turntableTask = macnum; // 旋转台任务号(与机床号对应)
                    if (!ExecuteTask(turntableTask + 100))
                    {
                        LogHelper.Error( "放置工件到旋转台失败");
                        ProcessTermination = true;
                        break;
                    }
                    Updaterobotmessage(null, 0, 0);

                    // 旋转台旋转(从加工卡读取角度 + 调用 rackoperate 的 TurntableReturnZero/RunTurntable)
                    TurntableReturnZero();
                    int angle = ReadRotationAngle(workpiecename);
                    if (angle > 0)
                    {
                        if (!RunTurntable(angle))
                        {
                            LogHelper.Error( "旋转台旋转 " + angle + " 度失败");
                            ProcessTermination = true;
                            break;
                        }
                    }

                    // 从旋转台取料（旋转后工件转为待加工）
                    if (!ExecuteTask(turntableTask + 101))
                    {
                        LogHelper.Error( "从旋转台取料失败");
                        ProcessTermination = true;
                        break;
                    }
                    Updaterobotmessage(workpiecename, 0, 0);

                    // 确认夹爪工件为待加工才放置
                    int pieceStatus = GetRobotWorkpieceStatus();
                    if (pieceStatus != 0)
                    {
                        LogHelper.Warn( "夹爪工件未待加工，无法放置");
                        ProcessTermination = true;
                        break;
                    }
                    // 放回机床
                    if (!ExecuteTask(GetMachineStartNo(macnum, "place") + macnum))
                    {
                        LogHelper.Error( "放置工件回机床" + macnum + "失败");
                        ProcessTermination = true;
                        break;
                    }
                    Updaterobotmessage(null, 0, 0);
                    // 更新机床工位（待加工）
                    SetMacWorkPos(macnum, worknum, workpiecename, 0);
                }

                // Roders: 关门
                if (isRoders) MacCloseDoor(macnum);

                // 传输NC程序 + 启动加工
                string nextPgm = GetNextProgramName(workpiecename);
                if (!transferprogram.RunTransferProcess(ip, nextPgm))
                {
                    LogHelper.Warn( "传输程序" + nextPgm + "到机床" + macnum + "失败");
                }
                if (!MacStartProcessSafe(macnum))
                {
                    LogHelper.Error( "启动机床" + macnum + "加工失败");
                    ProcessTermination = true;
                    break;
                }

                // 更新工位状态为加工中
                UpdateMacWorkPosStatus(macnum, worknum, 1);
                Updaterack_administrate(workpiecename, 1);
                LogHelper.Success( "机床" + macnum + " 换料完成，工件" + workpiecename + "加工中");
            }
        }

        /// <summary>
        /// 固定双爪换料：取旧爪有工件(完成)，放新爪为空，机床有新工件(待加工)
        /// </summary>
        static void UpdateMachinePieceOnChangePair(int macnum, int worknum, string oldPieceName)
        {
            // 取旧爪 → 有旧工件（加工完成）
            Updaterobotmessage(oldPieceName, 2, 2);
            // 机床工位 → 清空
            ClearMacWorkPos(macnum, worknum);
            // 从料架获取下一个待加工工件名称
            string newPiece = GetNextWorkpieceForMachine(macnum);
            if (!string.IsNullOrEmpty(newPiece))
            {
                // 机床工位 → 放新工件（待加工）
                SetMacWorkPos(macnum, worknum, newPiece, 0);
                Updaterobotmessage(null, 0, 0);
            }
        }

        /// <summary>
        /// 判断机床是否为 Roders 品牌
        /// </summary>
        static bool IsMachineRoders(int macId)
        {
            if (!MachineConfigs.ContainsKey(macId)) return false;
            if (!MachineConfigs[macId].ContainsKey("type")) return false;
            return MachineConfigs[macId]["type"] == "roders";
        }

        /// <summary>
        /// Roders 机床开门：连接 + 确认空闲 + 开门
        /// </summary>
        static bool OpenRodersDoor(int macId)
        {
            if (!MacList.ContainsKey(macId) || MacList[macId] == null) return false;
            LogHelper.Info( "机床" + macId + " 为 Roders，准备连接并开门");
            if (!MacList[macId].ConnectMac())
            {
                LogHelper.Error( "机床" + macId + " Roders 连接失败");
                return false;
            }
            string status = MacList[macId].GetMacStatus();
            int wait = 0;
            while (status != "待机" && wait < 300)
            {
                if (IsStopRequested()) { LogHelper.Warn("收到停止信号，终止机床状态等待"); return false; }
                Thread.Sleep(100);
                status = MacList[macId].GetMacStatus();
                wait++;
            }
            if (status != "待机") { LogHelper.Error( "机床" + macId + " 非空闲状态"); return false; }
            return MacList[macId].OpenDoor(macId);
        }

        /// <summary>
        /// 获取工件下一步加工的程序名称
        /// </summary>
        static string GetNextProgramName(string workpiecename)
        {
            try
            {
                string str = "select programname from rack_administrate where workpiecename='" + workpiecename + "'";
                object val = Mysql.GetValue(str);
                return val != null && val != DBNull.Value ? val.ToString() : workpiecename;
            }
            catch { return workpiecename; }
        }

        /// <summary>
        /// 获取机床下一个待加工工件名称
        /// </summary>
        static string GetNextWorkpieceForMachine(int macnum)
        {
            try
            {
                string str = "select workpiecename from macworkposstatus where machinename='" + macnum + "' and posstatus=0";
                object val = Mysql.GetValue(str);
                return val != null && val != DBNull.Value ? val.ToString() : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 从加工卡读取旋转角度（xlsx 指定单元格）
        /// </summary>
        static int ReadRotationAngle(string workpiecename)
        {
            try
            {
                string filePath = Path.Combine(@"D:\process_cards", workpiecename + ".xlsx");
                if (!File.Exists(filePath)) { LogHelper.Warn( "加工卡不存在: " + filePath); return 0; }
                string angleStr = transfer.ReadExcelCell(filePath, "A1");
                int angle = 0;
                int.TryParse(angleStr, out angle);
                return angle;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 执行任务：复位开始信号 → 写任务号 → 发送开始信号 → 等待完成
        /// </summary>
        public static bool ExecuteTask(int tasknum)
        {
            // 1. 先复位任务开始信号
            if (!string.IsNullOrEmpty(TaskStartAddr))
            {
                Plc.WriteWR(TaskStartAddr, 0);
                LogHelper.Info( "复位任务开始信号: " + TaskStartAddr);
            }

            // 2. 写入任务号
            if (!Plc.WriteDM(PlcDm, tasknum))
            {
                LogHelper.Error( "发送任务失败，任务: " + tasknum);
                return false;
            }
            LogHelper.Info( "写入任务号 " + tasknum + " 至 DM" + PlcDm);

            // 3. 发送任务开始信号
            if (!string.IsNullOrEmpty(TaskStartAddr))
            {
                Plc.WriteWR(TaskStartAddr, 1);
                LogHelper.Info( "发送任务开始信号: " + TaskStartAddr);
                Thread.Sleep(300);
                Plc.WriteWR(TaskStartAddr, 0);
                LogHelper.Info( "复位任务开始信号: " + TaskStartAddr);
            }

            // 4. 等待任务完成
            LogHelper.Info( "等待任务完成信号 " + TaskFinishAddr + " = 1...");
            int waitCount = 0;
            while (!Plc.ReadWR(TaskFinishAddr) && waitCount < 3000)
            {
                if (IsStopRequested()) { LogHelper.Warn("收到停止信号，机器人任务执行失败"); return false; }
                Thread.Sleep(100);
                waitCount++;
            }

            if (!Plc.ReadWR(TaskFinishAddr))
            {
                LogHelper.Error( "等待任务完成超时，任务: " + tasknum);
                return false;
            }
            LogHelper.Success( "执行任务 " + tasknum + " 完成");
            return true;
        }

        /// <summary>
        /// 获取随行台有工件料位列表
        /// </summary>
        /// <returns></returns>
        public static List<string> FollowHavePieceList()
        {
            List<string> piecelist = new List<string>();
            try
            {
                string str = "select * from following_rack where posstatus=2 and workstate=0";
                DataTable dt = Mysql.ExeForDtl(str);
                foreach (DataRow r in dt.Rows)
                {
                    string message = r["id"].ToString() + "," + r["workpiecename"].ToString();
                    piecelist.Add(message);
                }
            }
            catch (Exception ex)
            {

            }
            return piecelist;
        }

        /// <summary>
        /// 获取随行台空位列表
        /// </summary>
        /// <returns></returns>
        public static List<int> FollowKongWeiList()
        {
            List<int> kongweilist = new List<int>();
            try
            {
                string str = "select id from following_rack where posstatus=1";
                DataTable dt = Mysql.ExeForDtl(str);
                if (dt.Rows.Count > 0)
                {
                    foreach (DataRow r in dt.Rows)
                    {
                        int pos = Convert.ToInt16(r["id"].ToString());
                        kongweilist.Add(pos);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return kongweilist;
        }

        /// <summary>
        /// 获取待机机床列表
        /// </summary>
        /// <returns></returns>
        public static List<string> Waitmaclist()
        {
            List<string> macmessage = new List<string>();
            try
            {
                string str = "select * from macmessage where status!=0";
                DataTable dt = Mysql.ExeForDtl(str);
                foreach (DataRow r in dt.Rows)
                {
                    string status = GetMacStatusSafe(Convert.ToInt16(r["id"].ToString()));
                    string ip = r["machineip"].ToString();
                    if (status == "待机")
                        macmessage.Add(r["id"].ToString() + "," + ip);
                }
            }
            catch (Exception ex)
            {

            }
            return macmessage;
        }

        /// <summary>
        /// 是否有同一台机床同时满足：空闲（待机）且有加工完成工位（需要下机）
        /// </summary>
        static bool HasIdleMachineNeedUnload()
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("select id from macmessage where status!=0"))
                {
                    if (dt != null)
                        foreach (DataRow r in dt.Rows)
                        {
                            int macId = Convert.ToInt32(r["id"]);
                            if (GetMacStatusSafe(macId) != "待机") continue;
                            string macName = GetMacName(macId);
                            using (DataTable dt2 = Mysql.ExeForDtl("select * from macworkposstatus where mac_name='" + macName + "' and workpiece_status=2"))
                            {
                                if (dt2 != null && dt2.Rows.Count > 0) return true;
                            }
                        }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 是否有待加工工件
        /// </summary>
        static bool HasPendingWorkpiece()
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("select * from rack_administrate where workstate=0 and posstatus=2"))
                {
                    return dt != null && dt.Rows.Count > 0;
                }
            }
            catch { }
            return false;
        }

        /// <summary>取首个待加工料位号（workstate=0 且 posstatus=2），无则返回 0</summary>
        static int GetFirstPendingRackPos()
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE workstate=0 AND posstatus=2"))
                    if (dt != null && dt.Rows.Count > 0) return SafeInt(dt.Rows[0]["id"]);
            }
            catch (Exception ex) { LogHelper.Error("获取首个待加工料位异常: " + ex.Message); }
            return 0;
        }

        /// <summary>根据料位号获取料架类型数字（rack_administrate.racktype），异常返回 0</summary>
        static int GetRackTypeNumByPos(int pos)
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT racktype FROM rack_administrate WHERE id=" + pos))
                    if (dt != null && dt.Rows.Count > 0) return SafeInt(dt.Rows[0]["racktype"]);
            }
            catch (Exception ex) { LogHelper.Error("获取料位 " + pos + " 料架类型异常: " + ex.Message); }
            return 0;
        }

        /// <summary>根据工件名获取料架类型数字（rack_administrate.racktype，工件名存于 rfid 字段，兼容 workpiecename），异常返回 0</summary>
        static int GetRackTypeNumByWorkpiece(string workpiecename)
        {
            try
            {
                if (string.IsNullOrEmpty(workpiecename)) return 0;
                object val = Mysql.GetValue("select racktype from rack_administrate where rfid='" + workpiecename
                    + "' or workpiecename='" + workpiecename + "' order by id");
                if (val != null && val != DBNull.Value) return SafeInt(val);
            }
            catch (Exception ex) { LogHelper.Error("获取工件 " + workpiecename + " 料架类型异常: " + ex.Message); }
            return 0;
        }

        /// <summary>料架类型数字 → 夹爪类型（1=电极 2=钢件 3=刀具 4=托盘 5=铝板电极，铝板电极共用电极夹爪）；无法识别返回空串</summary>
        static string RackTypeToGripper(int rackType)
        {
            switch (rackType)
            {
                case 1: return "electrode";
                case 2: return "steel";
                case 3: return "tool";
                case 4: return "pallet";
                case 5: return "electrode";
                default: return "";
            }
        }

        /// <summary>目标夹爪类型 = 指定机床队列中首台在机工件所在料架的类型（快换爪模式）
        /// 入参必须是 RunChangePiece 将要处理的那个机床队列本身（同一份数据），
        /// 保证“判定夹爪用的工件”与“实际搬运的工件”完全同源，不再二次查询。
        /// 无法识别时返回空串，由调用方报错退出（不默认 steel，避免静默抓错夹爪）</summary>
        static string ResolveTargetGripperType(List<string> waitmaclist)
        {
            try
            {
                if (waitmaclist != null)
                {
                    foreach (var macinfo in waitmaclist)
                    {
                        int macnum = Convert.ToInt16(macinfo.Split(',')[0]);
                        var wp = GetMacProcessingWorkpiece(macnum);
                        if (wp == null || string.IsNullOrEmpty(wp.Item2)) continue;
                        int rt = GetRackTypeNumByWorkpiece(wp.Item2);
                        string gtype = RackTypeToGripper(rt);
                        LogHelper.Info("在机工件 " + wp.Item2 + " 料架类型编号 " + rt + " → 目标夹爪类型 "
                            + (string.IsNullOrEmpty(gtype) ? "(未知)" : gtype));
                        if (string.IsNullOrEmpty(gtype))
                            LogHelper.Error("工件 " + wp.Item2 + " 料架类型无法识别（racktype=" + rt + "），无法确定目标夹爪类型");
                        return gtype;
                    }
                }
                // 机床队列取不到在机工件时，按料架待加工队列判定
                int pos = GetFirstPendingRackPos();
                if (pos > 0)
                {
                    int rt = GetRackTypeNumByPos(pos);
                    string gtype = RackTypeToGripper(rt);
                    LogHelper.Info("待加工料位 " + pos + " 料架类型编号 " + rt + " → 目标夹爪类型 "
                        + (string.IsNullOrEmpty(gtype) ? "(未知)" : gtype));
                    if (string.IsNullOrEmpty(gtype))
                        LogHelper.Error("料位 " + pos + " 料架类型无法识别（racktype=" + rt + "），无法确定目标夹爪类型");
                    return gtype;
                }
                LogHelper.Error("未找到在机/待加工工件，无法确定目标夹爪类型");
            }
            catch (Exception ex) { LogHelper.Error("解析目标夹爪类型异常: " + ex.Message); }
            return "";
        }

        /// <summary>
        /// 更新随行台指定料位信息
        /// </summary>
        /// <param name="pos">指定料位</param>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="posstatus">料位状态</param>
        /// <returns></returns>
        static bool Updatefollowing_Rack(int pos, string workpiecename,int workstate, int posstatus)
        {
            bool flag = false;
            try
            {
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update following_rack set workpiecename=" + workpieceStr + ",workpieceworkstate=" + workstate + ", posstatus=" + posstatus + " where id=" + pos;
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                LogHelper.Error( "更新随行台信息异常: " + ex.Message);
            }
            return flag;
        }

        /// <summary>
        /// 更新料架指定料位的工件信息
        /// </summary>
        /// <param name="pos">指定料位</param>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="posstatus">料位状态</param>
        /// <returns></returns>
        static bool Updaterack_administrate(string workpiecename, int workstate)
        {
            bool flag = false;
            try
            {
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update rack_administrate set workstate=" + workstate + " where workpiecename=" + workpieceStr;
                if (Mysql.ExecuteSql(str))
                {
                    flag = true;
                    LogHelper.NotifyRackChanged();
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "更新料架信息异常: " + ex.Message);
            }
            return flag;
        }

        /// <summary>
        /// 置换夹爪流程 - 将机器人手上夹爪更换为本流程需要的夹爪
        /// </summary>
        static bool ChangeGripper()
        {
            if (IsFixedGripper)
            {
                LogHelper.Info( "固定爪模式，无需置换");
                return true;
            }

            LogHelper.Info( "开始置换夹爪流程...目标类型: " + TargetGripperType);

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
            catch (Exception ex) { LogHelper.Error( "读取 robotmessage 失败: " + ex.Message); return false; }

            // 手上无夹爪 → 直接抓取目标夹爪
            if (curPosStatus == 0)
            {
                LogHelper.Info( "手上无夹爪，直接抓取目标夹爪");
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
                LogHelper.Info( "手上已为目标夹爪: " + curName);
                return true;
            }

            // 手上有其他快换爪 → 先清空再抓取目标
            LogHelper.Info( "手上有夹爪(" + curName + ")，先清空...");
            bool hasWorkpiece = !string.IsNullOrEmpty(curWorkpiece) && curWorkpiece != "NULL";

            if (hasWorkpiece)
            {
                // 先放工件
                int emptySlot = GetEmptyRackSlot();
                if (emptySlot == 0) { LogHelper.Error( "无料架空位"); return false; }
                Plc.WriteDM(PlcDm, emptySlot);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { LogHelper.Error( "放工件超时"); return false; }
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
            if (cur == null) { LogHelper.Warn( "未找到匹配夹爪配置，跳过放置"); return true; }
            if (!string.IsNullOrEmpty(cur.SlotPhotoSignal) && Plc.ReadWR(cur.SlotPhotoSignal))
            { LogHelper.Error( "夹爪料位有异物"); return false; }
            if (!string.IsNullOrEmpty(cur.PlaceTaskNo))
            {
                int taskNo = ParseTaskNoBase(cur.PlaceTaskNo);
                Plc.WriteDM(PlcDm, taskNo);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { LogHelper.Error( "放夹爪超时"); return false; }
            }
            ClearRobotClamp();
            LogHelper.Info( "当前夹爪已清空");
            return true;
        }

        static bool PickTargetGripper()
        {
            var target = FindGripperByType(TargetGripperType);
            if (target == null) { LogHelper.Error( "未找到目标夹爪配置"); return false; }
            if (!string.IsNullOrEmpty(target.SlotPhotoSignal) && !Plc.ReadWR(target.SlotPhotoSignal))
            { LogHelper.Warn( "目标夹爪料位光电无信号，可能无夹爪"); }
            if (!string.IsNullOrEmpty(target.GrabTaskNo))
            {
                int taskNo = ParseTaskNoBase(target.GrabTaskNo);
                Plc.WriteDM(PlcDm, taskNo);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { LogHelper.Error( "抓夹爪超时"); return false; }
            }
            // 更新 DB
            try { Mysql.ExecuteSql("UPDATE robotmessage SET clampname='" + target.Name + "',clampstatus=" + (TargetGripperType=="electrode"?1:TargetGripperType=="steel"?2:TargetGripperType=="tool"?3:4) + ",clampposstatus=1,workpiecename=NULL,workpiecestatus=0"); }
            catch { }
            LogHelper.Success( "已抓取目标夹爪: " + target.Name);
            return true;
        }

        static GripperConfig FindGripperByType(string type)
        {
            foreach (var g in Grippers)
                if (string.Equals(g.Type, type, StringComparison.OrdinalIgnoreCase) ||
                    (type=="1" && g.Type=="electrode") || (type=="2" && g.Type=="steel") || (type=="3" && g.Type=="tool") || (type=="4" && g.Type=="pallet"))
                    return g;
            // 禁止兜底返回首个夹爪：否则会静默抓错夹爪，并把 robotmessage.clampstatus 写成错误类型
            LogHelper.Error("未找到类型为 " + type + " 的夹爪配置（已配置: " + string.Join(",", Grippers.ConvertAll(x => x.Type + "/" + x.Name).ToArray()) + "）");
            return null;
        }

        // ==================== 转台运动控制（来自 rackoperate） ====================
        /// <summary>
        /// 转台单次运动控制：旋转到指定角度
        /// </summary>
        static bool RunTurntable(int angle)
        {
            LogHelper.Info( "【转台】移动至 " + angle + " 度");
            Plc.WriteWr(SignalDegree, (ushort)angle);
            Plc.WriteWR(SignalAllowMove, 1);
            Plc.WriteWR(SignalStartMove, 1);

            LogHelper.Info( "【转台】等待到位信号 " + SignalMoveDone);
            while (!Plc.ReadWR(SignalMoveDone))
            {
                if (IsStopRequested()) { LogHelper.Warn("收到停止信号，终止转台移动"); return false; }
                Thread.Sleep(200);
            }

            Plc.WriteWR(SignalAllowMove, 0);
            Plc.WriteWR(SignalStartMove, 0);
            LogHelper.Success( "【转台】" + angle + " 度移动完成");
            return true;
        }

        /// <summary>
        /// 转台回零
        /// </summary>
        static bool TurntableReturnZero()
        {
            LogHelper.Info( "【转台】回零开始: " + SignalReturnZero);
            Plc.WriteWR(SignalReturnZero, 1);

            LogHelper.Info( "【转台】等待零点信号 " + SignalAtZero);
            while (!Plc.ReadWR(SignalAtZero))
            {
                if (IsStopRequested()) { LogHelper.Warn("收到停止信号，终止转台回零"); return false; }
                Thread.Sleep(200);
            }

            Plc.WriteWR(SignalReturnZero, 0);
            LogHelper.Success( "【转台】已回到零点");
            return true;
        }

        #region RobotMessage表更新方法

        /// <summary>
        /// 放置夹爪任务完成后更新robotmessage表
        /// 夹爪任务完成后：clampname为空，clampposstatus为0（无夹爪），workpiecename为空，workpiecestatus为0（无工件）
        /// </summary>
        static void UpdateRobotMessageOnTaskComplete()
        {
            UpdateRobotMessage(null, 0, 0, null, 0);
        }

        /// <summary>
        /// 更新robotmessage表
        /// </summary>
        /// <param name="clampName">夹爪名称</param>
        /// <param name="clampStatus">夹爪类型（0：快换夹爪、1：固定夹爪）</param>
        /// <param name="clampPosStatus">夹爪放置状态（0：无夹爪、1：有夹爪）</param>
        /// <param name="workpieceName">工件名称（无工件时为NULL）</param>
        /// <param name="workpieceStatus">工件工作状态（0=待加工，1=加工中，2=加工完成）</param>
        static void UpdateRobotMessage(string clampName, int clampStatus, int clampPosStatus, string workpieceName, int workpieceStatus)
        {
            try
            {
                // 由于id不自增，使用固定值1
                int taskId = 1;
                string clampNameStr = string.IsNullOrEmpty(clampName) ? "NULL" : $"'{clampName}'";
                string workpieceNameStr = string.IsNullOrEmpty(workpieceName) ? "NULL" : $"'{workpieceName}'";

                string sql = UpsertSql("robotmessage",
                    "id, clampname, clampstatus, clampposstatus, workpiecename, workpiecestatus",
                    $"{taskId}, {clampNameStr}, {clampStatus}, {clampPosStatus}, {workpieceNameStr}, {workpieceStatus}",
                    $"clampname = {clampNameStr}, clampstatus = {clampStatus}, clampposstatus = {clampPosStatus}, workpiecename = {workpieceNameStr}, workpiecestatus = {workpieceStatus}",
                    "id", taskId.ToString());

                if (Mysql.ExecuteSql(sql))
                {
                    LogHelper.Info( $"更新robotmessage表成功: clampname={clampName}, clampstatus={clampStatus}, clampposstatus={clampPosStatus}, workpiecename={workpieceName}, workpiecestatus={workpieceStatus}");
                }
                else
                {
                    LogHelper.Error( "更新robotmessage表失败");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "更新robotmessage表异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 抓取夹爪成功后更新robotmessage表
        /// clampname保持不变，clampposstatus为1（有夹爪），workpiecename为空，workpiecestatus为0（无工件）
        /// </summary>
        static void UpdateRobotMessageOnGrabClamp()
        {
            try
            {
                int taskId = 1;
                string sql = UpsertSql("robotmessage",
                    "id, clampposstatus, workpiecename, workpiecestatus",
                    $"{taskId}, 1, NULL, 0",
                    "clampposstatus = 1, workpiecename = NULL, workpiecestatus = 0",
                    "id", taskId.ToString());

                if (Mysql.ExecuteSql(sql))
                {
                    LogHelper.Info( "更新robotmessage表成功 - 抓取夹爪: clampposstatus=1, workpiecename=NULL, workpiecestatus=0");
                }
                else
                {
                    LogHelper.Error( "更新robotmessage表失败 - 抓取夹爪");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "更新robotmessage表异常: " + ex.Message);
            }
        }

        static string GetRobotMessagePieceMessage()
        {
            string piecemessage = null;
            try
            {
                string str = "select workpiecename from robotmessage where id=1 and (workpiecename is not null and workpiecename <> '')";
                piecemessage = Mysql.GetValue(str).ToString();
            }
            catch (Exception ex)
            {

            }
            return piecemessage;
        }

        /// <summary>
        /// 获取夹爪上工件的工件工作状态（robotmessage.workpiecestatus）
        /// </summary>
        static int GetRobotWorkpieceStatus()
        {
            try
            {
                object val = Mysql.GetValue("select workpiecestatus from robotmessage where id=1");
                if (val != null && val != DBNull.Value) return Convert.ToInt32(val);
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// 机床编号 → 机床名称（macmessage.machinename，与 macworkposstatus.mac_name 一致）
        /// </summary>
        static readonly Dictionary<int, string> MacNameCache = new Dictionary<int, string>();
        static string GetMacName(int macnum)
        {
            lock (MacNameCache)
            {
                if (MacNameCache.TryGetValue(macnum, out var n)) return n;
                string name = macnum.ToString();
                try
                {
                    object val = Mysql.GetValue("select machinename from macmessage where id=" + macnum);
                    if (val != null && val != DBNull.Value && !string.IsNullOrEmpty(val.ToString()))
                        name = val.ToString();
                }
                catch { }
                MacNameCache[macnum] = name;
                return name;
            }
        }

        /// <summary>
        /// 读取机床"加工中"工位的工位号和工件名（workpiece_status=1）
        /// </summary>
        static Tuple<int, string> GetMacProcessingWorkpiece(int macnum)
        {
            try
            {
                string macName = GetMacName(macnum);
                using (DataTable dt = Mysql.ExeForDtl("select worknum, workpiece_name from macworkposstatus where mac_name='" + macName + "' and workpiece_status=1"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return new Tuple<int, string>(SafeInt(dt.Rows[0]["worknum"]), SafeStr(dt.Rows[0]["workpiece_name"]));
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 更新机床工位工件状态
        /// </summary>
        static bool UpdateMacWorkPosStatus(int macnum, int worknum, int status)
        {
            try
            {
                string macName = GetMacName(macnum);
                return Mysql.ExecuteSql("update macworkposstatus set workpiece_status=" + status + " where mac_name='" + macName + "' and worknum=" + worknum);
            }
            catch (Exception ex) { LogHelper.Error("更新机床工位状态异常: " + ex.Message); }
            return false;
        }

        /// <summary>
        /// 清空机床工位工件信息和工件状态
        /// </summary>
        static bool ClearMacWorkPos(int macnum, int worknum)
        {
            try
            {
                string macName = GetMacName(macnum);
                return Mysql.ExecuteSql("update macworkposstatus set workpiece_name=NULL, workpiece_status=0 where mac_name='" + macName + "' and worknum=" + worknum);
            }
            catch (Exception ex) { LogHelper.Error("清空机床工位异常: " + ex.Message); }
            return false;
        }

        /// <summary>
        /// 设置机床工位工件名称和工件状态
        /// </summary>
        static bool SetMacWorkPos(int macnum, int worknum, string workpiecename, int status)
        {
            try
            {
                string macName = GetMacName(macnum);
                string nameStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                return Mysql.ExecuteSql("update macworkposstatus set workpiece_name=" + nameStr + ", workpiece_status=" + status + " where mac_name='" + macName + "' and worknum=" + worknum);
            }
            catch (Exception ex) { LogHelper.Error("设置机床工位异常: " + ex.Message); }
            return false;
        }

        /// <summary>
        /// 更新机器人手爪上的工件信息和料位状态
        /// </summary>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="workpiecestatus">工件工作状态（0=待加工，1=加工中，2=加工完成）</param>
        /// <returns></returns>
        static bool Updaterobotmessage(string workpiecename,int workstate, int workpiecestatus)
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
                LogHelper.Error( "更新机器人夹爪信息异常: " + ex.Message);
            }
            return flag;
        }

        /// <summary>
        /// 更新robotmessage表 - 置换夹爪任务后
        /// 更新clampname为相应夹爪，workpiecename为空，workpiecestatus为0（无工件）
        /// </summary>
        /// <param name="clampName">夹爪名称</param>
        /// <param name="clampStatus">夹爪类型（0：快换夹爪、1：固定夹爪）</param>
        static void UpdateRobotMessage_ChangeGripper(string clampName, int clampStatus)
        {
            try
            {
                // 由于id不自增，使用任务号或固定值1
                int taskId = 1;
                string sql = UpsertSql("robotmessage",
                    "id, clampname, clampstatus, clampposstatus, workpiecename, workpiecestatus",
                    $"{taskId}, '{clampName}', {clampStatus}, 1, NULL, 0",
                    $"clampname = '{clampName}', clampstatus = {clampStatus}, clampposstatus = 1, workpiecename = NULL, workpiecestatus = 0",
                    "id", taskId.ToString());

                if (Mysql.ExecuteSql(sql))
                {
                    LogHelper.Info( $"更新robotmessage表成功 - 置换夹爪: clampname={clampName}, clampstatus={clampStatus}, clampposstatus=1");
                }
                else
                {
                    LogHelper.Error( "更新robotmessage表失败 - 置换夹爪");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "更新robotmessage表异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 更新robotmessage表 - 抓取夹爪成功后
        /// 根据任务号更新clampname为相应夹爪名称，clampposstatus为1（有夹爪），workpiecename为空，workpiecestatus为0（无工件）
        /// </summary>
        /// <param name="taskId">任务号/料位号</param>
        static void UpdateRobotMessage_GrabClamp(int taskId)
        {
            try
            {
                // 抓取夹爪成功后，保持夹爪名称不变，夹爪状态为有夹爪
                string sql = UpsertSql("robotmessage",
                    "id, clampposstatus, workpiecename, workpiecestatus",
                    $"{taskId}, 1, NULL, 0",
                    "clampposstatus = 1, workpiecename = NULL, workpiecestatus = 0",
                    "id", taskId.ToString());

                if (Mysql.ExecuteSql(sql))
                {
                    LogHelper.Info( $"更新robotmessage表成功 - 抓取夹爪: taskId={taskId}, clampposstatus=1, workpiecestatus=0");
                }
                else
                {
                    LogHelper.Error( "更新robotmessage表失败 - 抓取夹爪");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "更新robotmessage表异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 更新robotmessage表 - 放置夹爪任务完成后（扫描流程结束）
        /// 更新clampname为空，clampposstatus为0（无夹爪），workpiecename为空，workpiecestatus为0（无工件）
        /// </summary>
        static void UpdateRobotMessage_PlaceClamp()
        {
            try
            {
                int taskId = 1;
                string sql = UpsertSql("robotmessage",
                    "id, clampname, clampposstatus, workpiecename, workpiecestatus",
                    $"{taskId}, NULL, 0, NULL, 0",
                    "clampname = NULL, clampposstatus = 0, workpiecename = NULL, workpiecestatus = 0",
                    "id", taskId.ToString());

                if (Mysql.ExecuteSql(sql))
                {
                    LogHelper.Info( "更新robotmessage表成功 - 放置夹爪: clampname=NULL, clampposstatus=0, workpiecename=NULL, workpiecestatus=0");
                }
                else
                {
                    LogHelper.Error( "更新robotmessage表失败 - 放置夹爪");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "更新robotmessage表异常: " + ex.Message);
            }
        }

        #endregion

        /// <summary>
        /// 生成兼容 MySQL 和 SQL Server 的 Upsert SQL
        /// </summary>
        static string UpsertSql(string table, string columns, string values, string updateSet, string keyCol, string keyVal)
        {
            if (DbType == "sqlserver")
            {
                return $@"MERGE {table} AS target
USING (SELECT {keyVal} AS {keyCol}) AS source
ON target.{keyCol} = source.{keyCol}
WHEN MATCHED THEN UPDATE SET {updateSet}
WHEN NOT MATCHED THEN INSERT ({columns}) VALUES ({values});";
            }
            else
            {
                return $@"INSERT INTO {table} ({columns}) VALUES ({values})
ON DUPLICATE KEY UPDATE {updateSet}";
            }
        }

        /// <summary>
        /// 机床通讯配置
        /// </summary>
        static Dictionary<int, Dictionary<string, string>> MachineConfigs = new Dictionary<int, Dictionary<string, string>>();

        /// <summary>
        /// 初始化机床通讯实例
        /// </summary>
        static void InitMachines(Dictionary<string, object> cfg)
        {
            try
            {
                MachineConfigs.Clear();
                if (cfg == null || cfg.Count == 0) { LogHelper.Warn( "配置为空，跳过机床初始化"); return; }
                if (!cfg.ContainsKey("machines")) { LogHelper.Warn( "配置文件中没有 machines 节点"); return; }
                var machinesRaw = cfg["machines"];
                Dictionary<string, object> machines;
                if (machinesRaw is Dictionary<string, object> machinesDict)
                    machines = machinesDict;
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
                    string grabStartTaskNo = m.ContainsKey("grabStartTaskNo") ? m["grabStartTaskNo"].ToString() : "";
                    string placeStartTaskNo = m.ContainsKey("placeStartTaskNo") ? m["placeStartTaskNo"].ToString() : "";
                    string startProcessSignal = m.ContainsKey("startProcessSignal") ? m["startProcessSignal"].ToString() : "";
                    MachineConfigs[macId] = new Dictionary<string, string>() { { "type", typeStr }, { "ip", ip }, { "grabStartTaskNo", grabStartTaskNo }, { "placeStartTaskNo", placeStartTaskNo }, { "startProcessSignal", startProcessSignal } };
                    MacCommunicate.Macfactory.MacType macType;
                    switch (typeStr)
                    {
                        case "fanuc": macType = MacCommunicate.Macfactory.MacType.fanuc; break;
                        case "siemense": macType = MacCommunicate.Macfactory.MacType.siemense; break;
                        case "makino": macType = MacCommunicate.Macfactory.MacType.makino; break;
                        default: macType = MacCommunicate.Macfactory.MacType.roders; break;
                    }
                    var mac = MacCommunicate.Macfactory.MacFactory.CreateInstance(macType, ip, sessionUrl, username, password, hostKey);
                    if (mac != null) { MacList[macId] = mac; LogHelper.Info( $"机床 {macId} ({typeStr}) 已创建，IP: {ip}" + (string.IsNullOrEmpty(sessionUrl) ? "" : $"，会话: {sessionUrl}")); }
                    else { LogHelper.Error( $"机床 {macId} 创建实例失败"); }
                }
                LogHelper.Info( $"机床通讯初始化完成，共 {MacList.Count} 台");
            }
            catch (Exception ex) { LogHelper.Error( "InitMachines 异常: " + ex.Message); }
        }

        /// <summary>
        /// 安全获取机床状态
        /// </summary>
        static string GetMacStatusSafe(int macId)
        {
            try { if (MacList.ContainsKey(macId) && MacList[macId] != null) return MacList[macId].GetMacStatus(); }
            catch (Exception ex) { LogHelper.Error( $"获取机床 {macId} 状态异常: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// 安全启动机床加工
        /// </summary>
        static bool MacStartProcessSafe(int macId)
        {
            try { if (MacList.ContainsKey(macId) && MacList[macId] != null) return MacList[macId].MacStartProcess(macId); }
            catch (Exception ex) { LogHelper.Error( $"启动机床 {macId} 加工异常: {ex.Message}"); }
            return false;
        }

        /// <summary>
        /// 机床开门
        /// </summary>
        static void MacOpenDoor(int macId)
        {
            try { if (MacList.ContainsKey(macId) && MacList[macId] != null) MacList[macId].OpenDoor(macId); }
            catch (Exception ex) { LogHelper.Error( $"机床 {macId} 开门异常: {ex.Message}"); }
        }

        /// <summary>
        /// 机床关门
        /// </summary>
        static void MacCloseDoor(int macId)
        {
            try { if (MacList.ContainsKey(macId) && MacList[macId] != null) MacList[macId].closeDoor(macId); }
            catch (Exception ex) { LogHelper.Error( $"机床 {macId} 关门异常: {ex.Message}"); }
        }

        /// <summary>
        /// 更新UI界面日志
        /// </summary>
        /// <param name="level"></param>
        static string SafeStr(object val)=>val==null||val==DBNull.Value?"":val.ToString();
        static int SafeInt(object val)=>val==null||val==DBNull.Value?0:Convert.ToInt32(val);
        static void SendTaskStart() { if (!string.IsNullOrEmpty(TaskStartAddr)) { Plc.WriteWR(TaskStartAddr, 1); Thread.Sleep(300); Plc.WriteWR(TaskStartAddr, 0); } }
        static bool WaitForTaskFinish(int maxTicks) { int w = 0; while (w < maxTicks) { if (IsStopRequested()) { LogHelper.Warn("收到停止信号，机器人任务执行失败"); return false; } if (Plc.ReadWR(TaskFinishAddr)) return true; Thread.Sleep(100); w++; } return Plc.ReadWR(TaskFinishAddr); }
        static void ClearRobotWorkpiece() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0"); } catch { } }
        static void ClearRobotClamp() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0"); } catch { } }
        static int GetEmptyRackSlot() { try { using (DataTable dt = Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0")) { if (dt != null && dt.Rows.Count > 0) return Convert.ToInt32(dt.Rows[0]["id"]); } } catch { } return 0; }
        static int ParseTaskNoBase(string s) { if (string.IsNullOrEmpty(s)) return 0; int.TryParse(s.ToUpper().Replace("D", "").Replace("DM", "").Trim(), out int r); return r; }

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
            catch (Exception ex) { LogHelper.Error($"获取机床 {macnum} 开始任务号异常: {ex.Message}"); }
            return 0;
        }
        
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
                        int.TryParse(taskNoStr.Replace("D","").Replace("DM","").Trim(), out PlcDm);
                        TaskStartAddr = ConfigClient.GetStr(robotSig, "taskStart", "");
                        TaskFinishAddr = ConfigClient.GetStr(robotSig, "taskDone", "");
                        RobotEleAddr = ConfigClient.GetStr(robotSig, "photo", "");
                    }
                    var machineSig = ConfigClient.GetDict(signals, "machine");
                    if (machineSig.Count > 0)
                    {
                        string startNoStr = ConfigClient.GetStr(machineSig, "startTaskNo", "0");
                        int.TryParse(startNoStr.Replace("D","").Replace("DM","").Trim(), out MachineStartTaskNo);
                    }
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
                if (line.Count > 0)
                {
                    if (line.ContainsKey("palletEnabled") && line["palletEnabled"] != null)
                    {
                        object palletObj = line["palletEnabled"];
                        if (palletObj is bool palletBool) PalletEnabled = palletBool;
                        else
                        {
                            string ps = palletObj.ToString().Trim().ToLower();
                            PalletEnabled = ps == "true" || ps == "yes" || ps == "1";
                        }
                    }
                }

                Grippers.Clear();
                if (!IsFixedGripper && cfg.ContainsKey("grippers") && cfg["grippers"] != null)
                {
                    var glist = cfg["grippers"];
                    if (glist is System.Collections.ArrayList al)
                        foreach (var item in al) AddGripper(item);
                    else if (glist is object[] arr)
                        foreach (var item in arr) AddGripper(item);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("提取配置失败，终止运行（不再使用默认配置兜底）: " + ex.Message);
                return false;
            }
            return true;
        }

static void AddGripper(object item){var g=item as Dictionary<string,object>;if(g==null)return;Grippers.Add(new GripperConfig{Id=ConfigClient.GetStr(g,"id",""),Type=ConfigClient.GetStr(g,"type","steel"),Name=ConfigClient.GetStr(g,"name",""),Number=ConfigClient.GetStr(g,"number",""),SlotPhotoSignal=ConfigClient.GetStr(g,"slotPhotoSignal",""),GrabTaskNo=ConfigClient.GetStr(g,"grabTaskNo",""),PlaceTaskNo=ConfigClient.GetStr(g,"placeTaskNo","")});}
    }
}
