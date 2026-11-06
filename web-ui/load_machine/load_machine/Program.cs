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
using MacCommunicate;

namespace load_machine
{
    class Program
    {
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "load_machine";
        static readonly string DisplayName = "上机流程";

        // 数据库
        static string ConnStr;
        static string DbType;
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
        static string TargetGripperType = "steel"; // 本流程需要的夹爪类型
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

        // PLC 配置
        static string PlcIp;
        static int PlcPort;
        static int PlcDm;
        static string TaskStartAddr;   // 任务开始信号
        static string TaskFinishAddr;   // 任务完成信号
        static string RobotEleAddr;
        static int MachineStartTaskNo;  // 西门子启动加工任务号（0=未配置，回退机床通讯启动）
        static CommonInterface Mysql;
        static void Main(string[] args)
        {
            LogHelper.Info( "【" + DisplayName + "】分包启动");
            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败，配置来源: " + ConfigClient.LastSourceText); return; }
                // 从配置中提取字段（统一配置管理）
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

                List<string> waitmaclist = Waitmaclist();
                if (waitmaclist.Count == 0)
                {
                    LogHelper.Info( "无待机机床，退出上机流程");
                    return;
                }

                // 确定工件来源：有随行台 → 从随行台取料，无随行台 → 从机器人夹爪取料
                bool hasShuttle = HasShuttle(cfg);
                List<string> followpiecelist = null;
                string robotPieceName = null;

                if (hasShuttle)
                {
                    followpiecelist = FollowHavePieceList();
                    if (followpiecelist.Count == 0)
                    {
                        LogHelper.Info( "有随行台但无待上机工件，退出上机流程");
                        return;
                    }
                    LogHelper.Info( "随行台模式：共 " + followpiecelist.Count + " 个待上机工件");
                }
                else
                {
                    robotPieceName = GetRobotMessagePieceMessage();
                    if (string.IsNullOrEmpty(robotPieceName))
                    {
                        LogHelper.Info( "无随行台且机器人夹爪无工件，检查是否有待启动加工的机床");
                        // 手爪无工件：查找"待机 + 无空工位 + 工件待加工"的机床，直接补启动加工
                        int readyMac = FindMacReadyToStart();
                        if (readyMac > 0)
                        {
                            LogHelper.Info( "发现机床" + readyMac + "工位已满且工件待加工，直接执行程序合并并启动加工");
                            string readyIp = GetMachineIp(readyMac);
                            string readyPiece = "";
                            var filled = GetMacFilledWorkpieces(readyMac);
                            if (filled.Count > 0) readyPiece = filled[0];
                            if (!HandleUploadProgram(readyMac, readyIp, readyPiece))
                            {
                                LogHelper.Error( "机床" + readyMac + "上传程序/启动加工失败，需人工处理");
                            }
                            else
                            {
                                // 启动成功后更新该机床所有有件工位为加工中
                                if (!UpdateMacWorkPosStatusAll(readyMac, 1))
                                    LogHelper.Error( "更新机床" + readyMac + "工位加工状态失败！");
                                foreach (string wpn in GetMacFilledWorkpieces(readyMac))
                                    if (!Updaterack_administrate(wpn, 1))
                                        LogHelper.Error( "更新料架" + wpn + "工件加工状态失败！");
                                LogHelper.Success( "机床" + readyMac + " 补启动加工成功");
                            }
                        }
                        else
                        {
                            LogHelper.Info( "无待启动加工的机床，退出上机流程");
                        }
                        return;
                    }
                    LogHelper.Info( "无随行台，机器人夹爪有工件: " + robotPieceName);
                }

                // 0. 目标夹爪类型 = 本件待上机工件所在料架的类型（快换爪模式）
                if (!IsFixedGripper)
                {
                    string firstPiece = hasShuttle ? followpiecelist[0].Split(',')[1] : robotPieceName;
                    TargetGripperType = RackTypeToGripper(GetRackTypeByWorkpiece(firstPiece));
                    LogHelper.Info( "本件工件 " + firstPiece + " → 目标夹爪类型 " + TargetGripperType);
                    if (string.IsNullOrEmpty(TargetGripperType))
                    {
                        LogHelper.Error( "工件 " + firstPiece + " 料架类型无法识别，无法确定目标夹爪类型，退出上机流程");
                        ProcessTermination = true;
                        return;
                    }
                }

                // 1. 执行置换夹爪流程
                if (!ChangeGripper())
                {
                    LogHelper.Error( "置换夹爪失败，退出上机流程");
                    ProcessTermination = true;
                    return;
                }

                // 2. 选择第一台待机机床（一次流程只上 1 件）
                int macnum = Convert.ToInt16(waitmaclist[0].Split(',')[0]);
                string ip = waitmaclist[0].Split(',')[1];

                // 3. 确定本件工件来源（先确定工件名，再根据料架类型确定工位）
                string workpiecename = null;
                int followpos = 0;
                if (hasShuttle)
                {
                    followpos = Convert.ToInt16(followpiecelist[0].Split(',')[0]);
                    workpiecename = followpiecelist[0].Split(',')[1];
                }
                else
                {
                    workpiecename = robotPieceName;
                }

                // 4. 根据工件所在料架类型确定工位编号（racktype: 1=电极 2=钢件 3=刀具 4=托盘）
                int rackType = GetRackTypeByWorkpiece(workpiecename);
                bool isPalletRack = rackType == 4;
                int worknum;
                if (isPalletRack)
                {
                    // 托盘料架：不管机床几个工位，只上一个料，工位编号由机床配置指定
                    worknum = GetMachinePalletWorkNum(macnum);
                    LogHelper.Info( "托盘料架：机床" + macnum + " 固定放置工位 " + worknum);
                }
                else
                {
                    // 其他料架（电极等）：按现有逻辑，放第一个空工位
                    worknum = GetMacFirstEmptyWorknum(macnum);
                    if (worknum == 0)
                    {
                        LogHelper.Info( "机床" + macnum + " 工位已满，退出上机流程");
                        return;
                    }
                }

                // 5. 抓取工件（从随行台）或直接用夹爪工件
                if (hasShuttle)
                {
                    LogHelper.Info( "上机: 随行台" + followpos + "(" + workpiecename + ") → 机床" + macnum + " 工位" + worknum);
                    if (!ExecuteTask(followpos))
                    {
                        LogHelper.Error( "机器人执行从随行台" + followpos + "料位抓取任务失败，退出上机流程！");
                        ProcessTermination = true;
                        return;
                    }
                    if (!Updatefollowing_Rack(followpos, null, 1))
                        LogHelper.Error( "更新随行台" + followpos + "料位信息失败！");
                    if (!Updaterobotmessage(workpiecename, 0))
                        LogHelper.Error( "更新机器人手爪信息失败！");
                }
                else
                {
                    LogHelper.Info( "上机: 机器人工件(" + workpiecename + ") → 机床" + macnum + " 工位" + worknum);
                }

                // 5. 放置工件到机床（任务号规则待定，收敛为 GetPlaceTaskNo）
                if (!OpenMachineDoorIfRoders(macnum))
                {
                    ProcessTermination = true;
                    return;
                }
                int placeTaskNo = GetPlaceTaskNo(macnum, worknum);
                if (!ExecuteTask(placeTaskNo))
                {
                    LogHelper.Error( "机器人执行放置工件到" + macnum + "任务失败，退出上机流程！");
                    ProcessTermination = true;
                    return;
                }
                LogHelper.Info( "机器人执行放置工件到" + macnum + "任务成功！");
                CloseMachineDoorIfRoders(macnum);

                // 6. 工位表记录：从夹爪读取工件工作状态
                int pieceStatus = GetRobotWorkpieceStatus();
                if (!UpdateMacWorkPosStatus(macnum, worknum, workpiecename, pieceStatus))
                    LogHelper.Error( "更新机床工位表" + macnum + "工件信息失败！");
                if (!Updaterobotmessage(null, 0))
                    LogHelper.Error( "更新机器人手爪信息失败！");

                // 7. 判断是否已放满：未满则不启动加工，等待后续上机（托盘料架只上一个料，跳过此判断）
                if (!isPalletRack)
                {
                    int emptyPos = GetMacEmptyPositions(macnum).Count;
                    int totalPos = GetMacTotalPositions(macnum);
                    if (emptyPos > 0)
                    {
                        LogHelper.Info( "机床" + macnum + " 已上" + (totalPos - emptyPos) + "/" + totalPos + "件，未满" + totalPos + "件，不启动加工，等待下次上机");
                        LogHelper.Info( "【" + DisplayName + "】分包结束（待放满后启动加工）");
                        return;
                    }
                }

                // 8. 已放满：上传合并程序 + 启动加工
                bool uploaded = HandleUploadProgram(macnum, ip, workpiecename);
                if (!uploaded)
                {
                    LogHelper.Error( "上传程序/启动加工失败，工件保持待加工状态，需人工处理");
                    return;
                }

                // 9. 启动成功后更新该机床所有有件工位为加工中
                if (!UpdateMacWorkPosStatusAll(macnum, 1))
                    LogHelper.Error( "更新机床" + macnum + "工位加工状态失败！");
                foreach (string wpn in GetMacFilledWorkpieces(macnum))
                    if (!Updaterack_administrate(wpn, 1))
                        LogHelper.Error( "更新料架" + wpn + "工件加工状态失败！");

                LogHelper.Info( "【" + DisplayName + "】分包结束");
            }
            catch (Exception ex)
            { }
        }

       
        /// <summary>
        /// 获取随行台有工件料位列表
        /// </summary>
        /// <returns></returns>
        public static List<string>FollowHavePieceList()
        {
            List<string> piecelist = new List<string>();
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string str = "select * from following_rack where posstatus=2 and workpieceworkstate=0";
                DataTable dt = Db.ExeForDtl(str);
                foreach(DataRow r in dt.Rows)
                {
                    string message = r["id"].ToString() + "," + r["workpiecename"].ToString();
                    piecelist.Add(message);
                }
            }
            catch(Exception ex)
            {

            }
            return piecelist;
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
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string str = "select * from macmessage where status!=0";
                DataTable dt = Db.ExeForDtl(str);
                foreach (DataRow r in dt.Rows)
                {
                    int macnum = Convert.ToInt16(r["id"].ToString());
                    string status = GetMacStatusSafe(macnum);
                    string ip = r["machineip"].ToString();
                    if (status == "待机")
                    {
                        // 机床待机时，将加工中的工位更新为加工完成
                        UpdateMacWorkPosStatusToDone(macnum);
                        var emptyPositions = GetMacEmptyPositions(macnum);
                        // 有空工位可上机；或所有工位已放满待加工电极（无空工位且全部待加工），也视为待机机床
                        if (emptyPositions.Count > 0 || (emptyPositions.Count == 0 && IsMacAllPending(macnum)))
                            macmessage.Add(macnum.ToString() + "," + ip);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return macmessage;
        }

        /// <summary>
        /// 更新随行台指定料位信息
        /// </summary>
        /// <param name="pos">指定料位</param>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="posstatus">料位状态</param>
        /// <returns></returns>
        static bool Updatefollowing_Rack(int pos, string workpiecename, int posstatus)
        {
            bool flag = false;
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "''" : "'" + workpiecename + "'";
                string str = "update following_rack set workpiecename=" + workpieceStr + ",posstatus=" + posstatus + " where id=" + pos;
                if (Db.ExecuteSql(str))
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
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                // 真实库 rack_administrate 无 workpiecename 列，工件名存于 rfid（scan 时写入）
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "''" : "'" + workpiecename + "'";
                string str = "update rack_administrate set workstate=" + workstate + " where rfid=" + workpieceStr;
                if (Db.ExecuteSql(str))
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

        /// <summary>
        /// 执行任务
        /// </summary>
        /// <param name="tasknum">任务号</param>
        /// <returns></returns>
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
                LogHelper.Error( "发送任务号失败: " + tasknum);
                return false;
            }
            LogHelper.Info( "写入任务号 " + tasknum + " 至 DM" + PlcDm);

            // 3. 发送任务开始信号
            if (!string.IsNullOrEmpty(TaskStartAddr))
            {
                if (!Plc.WriteWR(TaskStartAddr, 1))
                {
                    LogHelper.Error( "发送任务开始信号失败: " + TaskStartAddr);
                    return false;
                }
                LogHelper.Info( "发送任务开始信号: " + TaskStartAddr);
                Thread.Sleep(300);
                Plc.WriteWR(TaskStartAddr, 0);
                LogHelper.Info( "复位任务开始信号: " + TaskStartAddr);
            }
            else
            {
                LogHelper.Warn( "未配置任务开始信号，跳过");
            }

            // 4. 等待任务完成信号
            if (!string.IsNullOrEmpty(TaskFinishAddr))
            {
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
                LogHelper.Info( "任务完成信号已收到: " + TaskFinishAddr);
            }

            LogHelper.Success( "执行任务 " + tasknum + " 完成");
            return true;
        }

        /// <summary>
        /// 更新机床主表状态（macmessage：仅更新 workstate）
        /// </summary>
        static bool UpdateMacMessage(int macnum, int workstate)
        {
            bool flag = false;
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string str = $"update macmessage set workstate={workstate} where id={macnum}";
                if (Db.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"更新机床主表状态异常: {ex.Message}");
            }
            return flag;
        }

        /// <summary>
        /// 更新机床指定工位（macworkposstatus）
        /// </summary>
        /// <param name="macnum">机床编号</param>
        /// <param name="worknum">工位号</param>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="workpiecestatus">工件状态 0=待加工 1=加工中</param>
        static bool UpdateMacWorkPosStatus(int macnum, int worknum, string workpiecename, int workpiecestatus)
        {
            bool flag = false;
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string macName = GetMacName(macnum);
                string nameStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                // 真实结构: mac_name / worknum / workpiece_name / workpiece_status
                string str = $"update macworkposstatus set workpiece_name={nameStr}, workpiece_status={workpiecestatus} " +
                             $"where mac_name='{macName}' and worknum={worknum}";
                if (Db.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"更新机床工位表异常: {ex.Message}");
            }
            return flag;
        }

        /// <summary>
        /// 将该机床所有有件工位更新为指定状态（0=待加工 1=加工中）
        /// </summary>
        static bool UpdateMacWorkPosStatusAll(int macnum, int workpiecestatus)
        {
            bool flag = false;
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string macName = GetMacName(macnum);
                string str = "update macworkposstatus set workpiece_status=" + workpiecestatus +
                             " where mac_name='" + macName + "' and workpiece_name is not null and workpiece_name<>''";
                if (Db.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                LogHelper.Error( "更新机床 " + macnum + " 工位状态异常: " + ex.Message);
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
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string macName = GetMacName(macnum);
                Db.ExecuteSql("update macworkposstatus set workpiece_status=2 where mac_name='" + macName + "' and workpiece_status=1");
            }
            catch (Exception ex)
            {
                LogHelper.Warn( "更新机床" + macnum + "工位加工完成状态失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 空夹具占位判定：机床工位读到的 rfid/工件名含 "EMPTY"（如 EMPTY1~EMPTY4）即视为空夹具，
        /// 空夹具不参与程序合并
        /// </summary>
        static bool IsEmptyFixtureStation(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOf("EMPTY", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 获取机床所有有件工位的工件名列表（空夹具占位不算有件工件，已过滤）
        /// </summary>
        static List<string> GetMacFilledWorkpieces(int macnum)
        {
            var list = new List<string>();
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string macName = GetMacName(macnum);
                string str = "select workpiece_name from macworkposstatus where mac_name='" + macName +
                             "' and workpiece_name is not null and workpiece_name<>''";
                var dt = Db.ExeForDtl(str);
                foreach (DataRow r in dt.Rows)
                {
                    string name = r["workpiece_name"].ToString();
                    if (IsEmptyFixtureStation(name)) continue;
                    list.Add(name);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "获取机床 " + macnum + " 工件列表异常: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// 按工位号顺序获取参与合并程序的工件名与对应坐标系（工位1在前）
        /// 空夹具占位工位（rfid/工件名含 EMPTY）不参与合并，其所在工位对应的坐标系编号也不参与合并，
        /// 不影响其他工位对应的坐标系编号。
        /// 工位与坐标系对应关系：读取 config.json machines 节点中该机床的 workCoords 配置项，
        /// 第 (worknum-1) 个元素即该工位的坐标系编号（非固定 G54~G59 顺序）。
        /// 真实工件所在工位未配置坐标系时返回 false，由调用方中止合并，避免按错误坐标系加工。
        /// </summary>
        static bool GetMacMergeWorkpiecesAndCoords(int macnum, out List<string> workpieces, out List<string> coords)
        {
            workpieces = new List<string>();
            coords = new List<string>();
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string macName = GetMacName(macnum);
                string str = $"select worknum, workpiece_name from macworkposstatus where mac_name='{macName}' order by worknum";
                var dt = Db.ExeForDtl(str);
                var coordConfig = GetMachineWorkCoords(macnum);
                foreach (DataRow r in dt.Rows)
                {
                    string name = r["workpiece_name"] == null ? "" : r["workpiece_name"].ToString();
                    if (string.IsNullOrEmpty(name) || IsEmptyFixtureStation(name))
                        continue; // 空工位或空夹具占位：不参与合并
                    int worknum = Convert.ToInt32(r["worknum"]);
                    int coordIndex = worknum - 1; // 配置项按工位号 1 起对应
                    if (coordConfig == null || coordIndex < 0 || coordIndex >= coordConfig.Count || string.IsNullOrEmpty(coordConfig[coordIndex]))
                    {
                        LogHelper.Error( $"机床 {macnum} 工位 {worknum} 未在 config.json 的 machines.{macnum}.workCoords 中配置坐标系，中止合并程序，需人工处理");
                        return false;
                    }
                    workpieces.Add(name);
                    coords.Add(coordConfig[coordIndex].Trim());
                }
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"获取机床 {macnum} 合并工件及坐标系异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取机床工位对应的坐标系编号列表（配置 workCoords，逗号分隔，如 "G54,G55,G56,G57"）
        /// 第 i 个元素对应工位 i
        /// </summary>
        static List<string> GetMachineWorkCoords(int macnum)
        {
            var list = new List<string>();
            try
            {
                if (MachineConfigs.ContainsKey(macnum) && MachineConfigs[macnum].ContainsKey("workCoords"))
                {
                    string raw = MachineConfigs[macnum]["workCoords"];
                    if (!string.IsNullOrEmpty(raw))
                    {
                        foreach (var s in raw.Split(','))
                        {
                            string t = s.Trim();
                            if (!string.IsNullOrEmpty(t)) list.Add(t);
                        }
                    }
                }
            }
            catch (Exception ex) { LogHelper.Error( $"获取机床 {macnum} 工位坐标系异常: {ex.Message}"); }
            return list;
        }

        /// <summary>
        /// 获取机床空工位列表（workpiece_name 为空）
        /// </summary>
        static List<int> GetMacEmptyPositions(int macnum)
        {
            var list = new List<int>();
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string macName = GetMacName(macnum);
                string str = $"select worknum from macworkposstatus where mac_name='{macName}' and (workpiece_name is null or workpiece_name='')";
                var dt = Db.ExeForDtl(str);
                foreach (DataRow r in dt.Rows)
                    list.Add(Convert.ToInt32(r["worknum"]));
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"获取机床空工位异常: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// 获取机床总工位数
        /// </summary>
        static int GetMacTotalPositions(int macnum)
        {
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string macName = GetMacName(macnum);
                string str = $"select count(*) as cnt from macworkposstatus where mac_name='{macName}'";
                var dt = Db.ExeForDtl(str);
                if (dt.Rows.Count > 0)
                    return Convert.ToInt32(dt.Rows[0]["cnt"]);
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"获取机床总工位数异常: {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// 处理上传程序前的空工位判断与程序合并逻辑
        /// 返回 true=执行了上传，false=跳过上传
        /// </summary>
        static bool HandleUploadProgram(int macnum, string ip, string workpiecename)
        {
            var emptyList = GetMacEmptyPositions(macnum);
            int totalPos = GetMacTotalPositions(macnum);

            // 判定是否需要合并程序（总工位>1则需要合并）
            bool needMerge = totalPos > 1;
            if (needMerge)
                LogHelper.Info( $"机床 {macnum} 有 {totalPos} 个工位，需合并程序后再上传");
            else
                LogHelper.Info( $"机床 {macnum} 只有1个工位，无需合并，直接上传");

            // 无空工位：仍需上传程序并启动加工
            if (emptyList.Count == 0)
            {
                LogHelper.Info( $"机床 {macnum} 无空工位，上传程序并启动加工");
            }
            // 有空工位：先更新工位表，再上传
            else
            {
                LogHelper.Info( $"机床 {macnum} 有 {emptyList.Count} 个空工位，上传程序并启动加工");
            }

            if (needMerge)
            {
                // 合并上传：按工位顺序获取工件与对应坐标系（跳过空夹具占位工位），先对单个程序替换坐标系，再合并
                List<string> workpieces;
                List<string> coords;
                if (!GetMacMergeWorkpiecesAndCoords(macnum, out workpieces, out coords) || workpieces.Count == 0)
                {
                    LogHelper.Error( $"获取机床 {macnum} 合并工件/坐标系失败或工件列表为空，中止合并程序");
                    return false;
                }
                if (!transferprogram.MergeAndUpload(macnum, ip, workpieces, coords))
                {
                    LogHelper.Error( $"合并上传程序到机床 {macnum} 失败！");
                    return false;
                }
                LogHelper.Info( $"合并上传程序到机床 {macnum} 成功");
            }
            else
            {
                if (!transferprogram.RunTransferProcess(macnum, ip, workpiecename))
                {
                    LogHelper.Error( $"上传程序到机床 {macnum} 失败！");
                    return false;
                }
                LogHelper.Info( $"上传程序到机床 {macnum} 成功");
            }

            // 启动加工：通过 PLC 置位启动加工信号并等待机床进入运行状态
            bool started = MacStartProcessSafe(macnum);
            if (!started)
            {
                LogHelper.Error( $"启动机床 {macnum} 加工失败！");
                return false;
            }
            LogHelper.Info( $"机床 {macnum} 启动加工成功！");
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
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                // 由于id不自增，使用固定值1
                int taskId = 1;
                string clampNameStr = string.IsNullOrEmpty(clampName) ? "NULL" : $"'{clampName}'";
                string workpieceNameStr = string.IsNullOrEmpty(workpieceName) ? "NULL" : $"'{workpieceName}'";

                string sql = UpsertSql("robotmessage",
                    "id, clampname, clampstatus, clampposstatus, workpiecename, workpiecestatus",
                    $"{taskId}, {clampNameStr}, {clampStatus}, {clampPosStatus}, {workpieceNameStr}, {workpieceStatus}",
                    $"clampname = {clampNameStr}, clampstatus = {clampStatus}, clampposstatus = {clampPosStatus}, workpiecename = {workpieceNameStr}, workpiecestatus = {workpieceStatus}",
                    "id", taskId.ToString());

                if (Db.ExecuteSql(sql))
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
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                if (Db.ExecuteSql(sql))
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
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string str = "select workpiecename from robotmessage where id=1 and (workpiecename is not null and workpiecename <> '')";
                object sb =Db.GetValue(str);
                if (sb != null)
                    piecemessage = sb.ToString();
            }
            catch(Exception ex)
            {

            }
            return piecemessage;
        }

        /// <summary>
        /// 获取夹爪上工件的工件工作状态（workpiecestatus）
        /// </summary>
        static int GetRobotWorkpieceStatus()
        {
            int status = 0;
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                object sb = Db.GetValue("select workpiecestatus from robotmessage where id=1");
                if (sb != null && sb != DBNull.Value)
                    status = Convert.ToInt32(sb);
            }
            catch (Exception ex) { }
            return status;
        }

        /// <summary>
        /// 判断系统是否配置了随行台
        /// </summary>
        static bool HasShuttle(Dictionary<string, object> cfg)
        {
            try
            {
                if (cfg != null && cfg.Count > 0)
                {
                    var line = ConfigClient.GetDict(cfg, "line");
                    string val = ConfigClient.GetStr(line, "shuttleExists", "");
                    return !string.IsNullOrEmpty(val);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 机床编号 → 机床名称（macmessage.machinename，与 macworkposstatus.mac_name 一致）
        /// </summary>
        static readonly Dictionary<int, string> MacNameCache = new Dictionary<int, string>();
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
            catch (Exception ex) { LogHelper.Error( $"获取机床 {macnum} 名称异常: {ex.Message}"); }
            return macnum.ToString();
        }

        /// <summary>
        /// 机床编号 → 机床 IP（macmessage.machineip）
        /// </summary>
        static string GetMachineIp(int macnum)
        {
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                object val = Db.GetValue("select machineip from macmessage where id=" + macnum);
                if (val != null && val != DBNull.Value && !string.IsNullOrEmpty(val.ToString()))
                    return val.ToString();
            }
            catch (Exception ex) { LogHelper.Error( $"获取机床 {macnum} IP异常: {ex.Message}"); }
            return "";
        }

        /// <summary>
        /// 判断机床所有有件工位是否均为"待加工"状态
        /// </summary>
        static bool IsMacAllPending(int macnum)
        {
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string macName = GetMacName(macnum);
                object val = Db.GetValue($"select count(*) from macworkposstatus where mac_name='{macName}' and workpiece_name is not null and workpiece_name<>'' and workpiece_status<>0");
                int cnt = val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);
                return cnt == 0;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 查找"待机 + 无空工位 + 工件待加工"的机床（用于补启动加工），找不到返回 0
        /// </summary>
        static int FindMacReadyToStart()
        {
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string str = "select id from macmessage where status!=0";
                DataTable dt = Db.ExeForDtl(str);
                foreach (DataRow r in dt.Rows)
                {
                    int macnum = Convert.ToInt16(r["id"].ToString());
                    string status = GetMacStatusSafe(macnum);
                    if (status != "待机") continue;
                    // 无空工位
                    if (GetMacEmptyPositions(macnum).Count > 0) continue;
                    // 工件均为待加工状态
                    if (IsMacAllPending(macnum))
                        return macnum;
                }
            }
            catch (Exception ex) { LogHelper.Error("查找待启动机床异常: " + ex.Message); }
            return 0;
        }

        /// <summary>
        /// 通过 maccommunicate 上传程序到机床（各品牌机床统一接口 uploadprogram）
        /// </summary>
        internal static bool UploadProgramToMac(int macnum, string localPath, string remotePath)
        {
            try
            {
                if (!MacList.ContainsKey(macnum))
                {
                    LogHelper.Error("机床" + macnum + "无通信实例");
                    return false;
                }
                return MacList[macnum].uploadprogram(localPath, remotePath);
            }
            catch (Exception ex)
            {
                LogHelper.Error("上传程序到机床" + macnum + "异常: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 获取机床 NC 远程目录（config.json machines 节点的 ncPath）
        /// </summary>
        internal static string GetMachineNcPath(int macnum)
        {
            try
            {
                if (MachineConfigs.ContainsKey(macnum) && MachineConfigs[macnum].ContainsKey("ncPath"))
                    return MachineConfigs[macnum]["ncPath"];
            }
            catch (Exception ex) { LogHelper.Error("获取机床" + macnum + "NC路径异常: " + ex.Message); }
            return "";
        }

        /// <summary>
        /// 确保 macworkposstatus 中存在该机床的工位行（按配置 stationCount 补齐缺失行）
        /// </summary>
        static void EnsureMacWorkPosRows(int macnum)
        {
            try
            {
                string macName = GetMacName(macnum);
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                object cnt = Db.GetValue($"select count(*) from macworkposstatus where mac_name='{macName}'");
                int existing = cnt == null || cnt == DBNull.Value ? 0 : Convert.ToInt32(cnt);
                if (existing > 0) return;
                int stationCount = 1;
                if (MachineConfigs.ContainsKey(macnum) && MachineConfigs[macnum].ContainsKey("stationCount"))
                    int.TryParse(MachineConfigs[macnum]["stationCount"], out stationCount);
                for (int w = 1; w <= stationCount; w++)
                    Db.ExecuteSql($"insert into macworkposstatus (mac_name, worknum, workpiece_name, workpiece_status) values ('{macName}', {w}, NULL, 0)");
                LogHelper.Info( $"机床 {macnum} 工位表为空，已按配置补齐 {stationCount} 个工位行");
            }
            catch (Exception ex) { LogHelper.Error( $"补齐机床 {macnum} 工位行异常: {ex.Message}"); }
        }

        /// <summary>
        /// 获取机床第一个空工位号（workpiece_name 为空），无空工位返回 0
        /// </summary>
        static int GetMacFirstEmptyWorknum(int macnum)
        {
            try
            {
                EnsureMacWorkPosRows(macnum);
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string macName = GetMacName(macnum);
                string str = $"select worknum from macworkposstatus where mac_name='{macName}' and (workpiece_name is null or workpiece_name='') order by worknum";
                object val = Db.GetValue(str);
                if (val != null && val != DBNull.Value)
                    return Convert.ToInt32(val);
            }
            catch (Exception ex) { LogHelper.Error( $"获取机床 {macnum} 空工位异常: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// 根据工件名获取其所在料架类型（rack_administrate.racktype）
        /// 1=电极 2=钢件 3=刀具 4=托盘；工件名存于 rfid 字段
        /// </summary>
        static int GetRackTypeByWorkpiece(string workpiecename)
        {
            try
            {
                if (string.IsNullOrEmpty(workpiecename)) return 0;
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string str = $"select racktype from rack_administrate where rfid='{workpiecename}' or workpiecename='{workpiecename}' order by id";
                object val = Db.GetValue(str);
                if (val != null && val != DBNull.Value)
                    return Convert.ToInt32(val);
            }
            catch (Exception ex) { LogHelper.Error( $"获取工件 {workpiecename} 料架类型异常: {ex.Message}"); }
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

        /// <summary>
        /// 获取托盘料架在机床上的固定放置工位编号（机床配置 palletWorkNum），未配置返回 1
        /// </summary>
        static int GetMachinePalletWorkNum(int macnum)
        {
            try
            {
                if (MachineConfigs.ContainsKey(macnum) && MachineConfigs[macnum].ContainsKey("palletWorkNum"))
                {
                    int w;
                    if (int.TryParse(MachineConfigs[macnum]["palletWorkNum"], out w) && w > 0)
                        return w;
                }
            }
            catch (Exception ex) { LogHelper.Error( $"获取机床 {macnum} 托盘工位号异常: {ex.Message}"); }
            return 1;
        }

        /// <summary>
        /// 获取放置工件到机床的任务号：机床放置开始任务号 + 工位号
        /// </summary>
        static int GetPlaceTaskNo(int macnum, int worknum)
        {
            return GetMachineStartNo(macnum, "place") + worknum;
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
            catch (Exception ex) { LogHelper.Error($"获取机床 {macnum} 开始任务号异常: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// 更新机器人手爪上的工件信息和料位状态
        /// </summary>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="workpiecestatus">工件工作状态（0=待加工，1=加工中，2=加工完成）</param>
        /// <returns></returns>
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
            catch (Exception ex)
            {
                LogHelper.Error( "更新机器人手爪信息异常: " + ex.Message);
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
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                if (Db.ExecuteSql(sql))
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
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                if (Db.ExecuteSql(sql))
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
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                if (Db.ExecuteSql(sql))
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
        /// 机床通讯配置（从 config.json 的 machines 节点读取）
        /// </summary>
        static Dictionary<int, Dictionary<string, string>> MachineConfigs = new Dictionary<int, Dictionary<string, string>>();

        /// <summary>
        /// 初始化机床通讯实例（从配置文件读取并创建）
        /// </summary>
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

        static void AddGripperConfig(object item) { var g = item as Dictionary<string,object>; if (g==null) return; Grippers.Add(new GripperConfig{Id=ConfigClient.GetStr(g,"id",""),Type=ConfigClient.GetStr(g,"type","steel"),Name=ConfigClient.GetStr(g,"name",""),Number=ConfigClient.GetStr(g,"number",""),SlotPhotoSignal=ConfigClient.GetStr(g,"slotPhotoSignal",""),GrabTaskNo=ConfigClient.GetStr(g,"grabTaskNo",""),PlaceTaskNo=ConfigClient.GetStr(g,"placeTaskNo","")}); }

        static void InitMachines(Dictionary<string, object> cfg)
        {
            try
            {
                MachineConfigs.Clear();
                if (cfg == null || cfg.Count == 0)
                {
                    LogHelper.Warn( "配置为空，跳过机床初始化");
                    return;
                }
                if (!cfg.ContainsKey("machines"))
                {
                    LogHelper.Warn( "配置文件中没有 machines 节点，跳过机床初始化");
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
                    string ncPath = m.ContainsKey("ncPath") ? m["ncPath"].ToString() : "";
                    string sessionUrl = m.ContainsKey("sessionUrl") ? m["sessionUrl"].ToString() : "";
                    string username = m.ContainsKey("username") ? m["username"].ToString() : "";
                    string password = m.ContainsKey("password") ? m["password"].ToString() : "";
                    string hostKey = m.ContainsKey("hostKey") ? m["hostKey"].ToString() : "";
                    string stationCount = m.ContainsKey("stationCount") ? m["stationCount"].ToString() : "1";
                    string palletWorkNum = m.ContainsKey("palletWorkNum") ? m["palletWorkNum"].ToString() : "1";
                    string workCoords = m.ContainsKey("workCoords") ? m["workCoords"].ToString() : "";
                    string grabStartTaskNo = m.ContainsKey("grabStartTaskNo") ? m["grabStartTaskNo"].ToString() : "";
                    string placeStartTaskNo = m.ContainsKey("placeStartTaskNo") ? m["placeStartTaskNo"].ToString() : "";
                    string startProcessSignal = m.ContainsKey("startProcessSignal") ? m["startProcessSignal"].ToString() : "";
                    MachineConfigs[macId] = new Dictionary<string, string>() { { "type", typeStr }, { "ip", ip }, { "ncPath", ncPath }, { "stationCount", stationCount }, { "palletWorkNum", palletWorkNum }, { "workCoords", workCoords }, { "grabStartTaskNo", grabStartTaskNo }, { "placeStartTaskNo", placeStartTaskNo }, { "startProcessSignal", startProcessSignal } };

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
                        LogHelper.Info( $"机床 {macId} ({typeStr}) 已创建实例，IP: {ip}" + (string.IsNullOrEmpty(sessionUrl) ? "" : $"，会话: {sessionUrl}"));
                    }
                    else
                    {
                        LogHelper.Error( $"机床 {macId} 类型 {typeStr} 创建实例失败");
                    }
                }
                LogHelper.Info( $"机床通讯初始化完成，共 {MacList.Count} 台");
            }
            catch (Exception ex)
            {
                LogHelper.Error( "InitMachines 异常: " + ex.Message);
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
                LogHelper.Error( $"获取机床 {macId} 状态异常: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 获取机床启动加工信号地址（config.json machines 节点的 startProcessSignal）
        /// </summary>
        static string GetMachineStartProcessSignal(int macnum)
        {
            try
            {
                if (MachineConfigs.ContainsKey(macnum) && MachineConfigs[macnum].ContainsKey("startProcessSignal"))
                    return MachineConfigs[macnum]["startProcessSignal"];
            }
            catch (Exception ex) { LogHelper.Error( $"获取机床 {macnum} 启动加工信号异常: {ex.Message}"); }
            return "";
        }

        /// <summary>
        /// 安全启动机床加工：通过 PLC 置位启动加工信号，等待机床状态变为运行中
        /// </summary>
        static bool MacStartProcessSafe(int macId)
        {
            try
            {
                // 获取机床启动加工信号地址
                string startSignal = GetMachineStartProcessSignal(macId);
                if (string.IsNullOrEmpty(startSignal))
                {
                    LogHelper.Error( $"机床 {macId} 未配置启动加工信号，无法启动加工");
                    return false;
                }

                // 通过 PLC 置位启动加工信号
                if (!Plc.WriteWR(startSignal, 1))
                {
                    LogHelper.Error( $"机床 {macId} 置位启动加工信号失败: " + startSignal);
                    return false;
                }
                LogHelper.Info( $"机床 {macId} 已置位启动加工信号: " + startSignal);

                // 等待机床状态变为"运行中"
                int wait = 0;
                string status = "";
                while (wait < 100)   // 最多等待 10 秒
                {
                    if (IsStopRequested()) { LogHelper.Warn("收到停止信号，启动加工判定失败"); return false; }
                    status = GetMacStatusSafe(macId);
                    if (status == "运行")
                    {
                        LogHelper.Info( $"机床 {macId} 状态为运行中，启动加工成功");
                        return true;
                    }
                    Thread.Sleep(100);
                    wait++;
                }

                LogHelper.Error( $"机床 {macId} 启动加工后未进入运行状态(当前: {status})");
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"启动机床 {macId} 加工异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 若机台为roders则：创建TCP服务器 → 等待机床连接 → 确认空闲 → 开门
        /// </summary>
        static bool OpenMachineDoorIfRoders(int macId)
        {
            if (!MachineConfigs.ContainsKey(macId) ||
                !MachineConfigs[macId].ContainsKey("type") ||
                MachineConfigs[macId]["type"] != "roders")
                return true; // 非roders无需开门

            LogHelper.Info( "机床" + macId + " 为roders，准备连接并开门");

            // 1. 获取机床通讯实例并连接（创建TCP服务器:20200，等待机床主动连接）
            if (!MacList.ContainsKey(macId) || MacList[macId] == null)
            {
                LogHelper.Error( "机床" + macId + " 通讯实例不存在");
                return false;
            }
            if (!MacList[macId].ConnectMac())
            {
                LogHelper.Error( "机床" + macId + " 连接失败（TCP服务器创建或机床未连接）");
                return false;
            }
            LogHelper.Info( "机床" + macId + " 已连接");

            // 2. 检查机床状态为空闲("待机")时才允许开门
            string status = MacList[macId].GetMacStatus();
            LogHelper.Info( "机床" + macId + " 当前状态: " + (status ?? "未知"));
            if (status != "待机")
            {
                LogHelper.Warn( "机床" + macId + " 非空闲状态(" + status + ")，等待变为空闲...");
                int wait = 0;
                while (wait < 300) // 最多等30秒
                {
                    if (IsStopRequested()) { LogHelper.Warn("收到停止信号，终止机床状态等待"); return false; }
                    Thread.Sleep(100);
                    status = MacList[macId].GetMacStatus();
                    if (status == "待机") break;
                    wait++;
                }
                if (status != "待机")
                {
                    LogHelper.Error( "机床" + macId + " 等待空闲超时，当前状态: " + (status ?? "未知"));
                    return false;
                }
            }
            LogHelper.Info( "机床" + macId + " 空闲状态，可以开门");

            // 3. 开门
            if (!MacList[macId].OpenDoor(macId))
            {
                LogHelper.Error( "机床" + macId + " 开门失败");
                return false;
            }
            LogHelper.Info( "机床" + macId + " 门已打开");
            return true;
        }

        /// <summary>
        /// 机床开门（容错包装），返回 true=成功, false=失败
        /// </summary>
        static bool MacOpenDoor(int macId)
        {
            try
            {
                if (MacList.ContainsKey(macId) && MacList[macId] != null)
                    return MacList[macId].OpenDoor(macId);
                LogHelper.Warn( $"机床 {macId} 不在MacList中，无法开门");
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"机床 {macId} 开门异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 机床关门（容错包装），返回 true=成功, false=失败
        /// </summary>
        static bool MacCloseDoor(int macId)
        {
            try
            {
                if (MacList.ContainsKey(macId) && MacList[macId] != null)
                    return MacList[macId].closeDoor(macId);
                LogHelper.Warn( $"机床 {macId} 不在MacList中，无法关门");
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"机床 {macId} 关门异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 若机台为roders则在放工件完成后关门
        /// </summary>
        static bool CloseMachineDoorIfRoders(int macId)
        {
            if (!MachineConfigs.ContainsKey(macId) ||
                !MachineConfigs[macId].ContainsKey("type") ||
                MachineConfigs[macId]["type"] != "roders")
                return true; // 非roders无需关门

            LogHelper.Info( "机床" + macId + " 为roders，放置工件完成后关门");
            if (!MacCloseDoor(macId))
            {
                LogHelper.Warn( "机床" + macId + " 关门失败，但不影响流程继续");
            }
            return true;
        }

        /// <summary>
        /// 更新UI界面日志
        /// </summary>
        /// <param name="level"></param>

        static string SafeStr(object val) => val == null || val == DBNull.Value ? "" : val.ToString();
        static int SafeInt(object val) => val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);

        static void SendTaskStart() { if (!string.IsNullOrEmpty(TaskStartAddr)) { Plc.WriteWR(TaskStartAddr, 1); Thread.Sleep(300); Plc.WriteWR(TaskStartAddr, 0); } }
        static bool WaitForTaskFinish(int maxTicks) { int w = 0; while (w < maxTicks) { if (IsStopRequested()) { LogHelper.Warn("收到停止信号，机器人任务执行失败"); return false; } if (Plc.ReadWR(TaskFinishAddr)) return true; Thread.Sleep(100); w++; } return Plc.ReadWR(TaskFinishAddr); }
        static void ClearRobotWorkpiece() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0"); } catch { } }
        static void ClearRobotClamp() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0"); } catch { } }
        static int GetEmptyRackSlot() { try { using (DataTable dt = Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0")) { if (dt!=null && dt.Rows.Count>0) return Convert.ToInt32(dt.Rows[0]["id"]); } } catch { } return 0; }
        static int ParseTaskNoBase(string s) { if (string.IsNullOrEmpty(s)) return 0; int.TryParse(s.ToUpper().Replace("D","").Replace("DM","").Trim(), out int r); return r; }
    }
}



