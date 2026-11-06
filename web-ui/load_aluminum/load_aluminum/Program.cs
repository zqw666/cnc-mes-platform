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
        static readonly string StepName = "load_aluminum";
        static readonly string DisplayName = "铝板电极上机流程";

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
        static void LegacyMain(string[] args)
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

            // 手上已有目标夹爪 → 完成（clampstatus 数值码约定：electrode=1 steel=2 tool=3 pallet=4）
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
                string str = $"select racktype from rack_administrate where rfid='{workpiecename}'";
                object val = Db.GetValue(str);
                if (val != null && val != DBNull.Value)
                    return Convert.ToInt32(val);
            }
            catch (Exception ex) { LogHelper.Error( $"获取工件 {workpiecename} 料架类型异常: {ex.Message}"); }
            return 0;
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
        /// 获取铝板电极放置到机床的固定任务号（对应机床 2 号工位，固定值，不加工位号）
        /// </summary>
        static int GetAluminumPlaceTaskNo(int macnum)
        {
            try
            {
                if (MachineConfigs.ContainsKey(macnum) && MachineConfigs[macnum].ContainsKey("aluminumPlaceTaskNo"))
                    return ParseTaskNoBase(MachineConfigs[macnum]["aluminumPlaceTaskNo"]);
            }
            catch (Exception ex) { LogHelper.Error($"获取机床 {macnum} 铝板电极放置任务号异常: {ex.Message}"); }
            return 0;
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
                    string aluminumGrabTaskNo = m.ContainsKey("aluminumGrabTaskNo") ? m["aluminumGrabTaskNo"].ToString() : "";
                    string aluminumPlaceTaskNo = m.ContainsKey("aluminumPlaceTaskNo") ? m["aluminumPlaceTaskNo"].ToString() : "";
                    string startProcessSignal = m.ContainsKey("startProcessSignal") ? m["startProcessSignal"].ToString() : "";
                    MachineConfigs[macId] = new Dictionary<string, string>() { { "type", typeStr }, { "ip", ip }, { "ncPath", ncPath }, { "stationCount", stationCount }, { "palletWorkNum", palletWorkNum }, { "workCoords", workCoords }, { "grabStartTaskNo", grabStartTaskNo }, { "placeStartTaskNo", placeStartTaskNo }, { "aluminumGrabTaskNo", aluminumGrabTaskNo }, { "aluminumPlaceTaskNo", aluminumPlaceTaskNo }, { "startProcessSignal", startProcessSignal } };

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

        // ================================================================
        // 铝板电极上机流程（load_aluminum）
        // 流程条件：①待机机床且所有工位为空  ②铝板电极料架有待加工电极
        //           ③电极料架上有 3 个空夹具
        // 流程：无爪换爪→取铝板电极→放机床2号工位→坐标替换+上传→
        //       换电极夹爪→依次放 3 个空夹具到 1/3/4 号工位→启动加工
        // ================================================================
        const int AluminumWorkNum = 2;                          // 铝板电极固定放机床 2 号工位
        static readonly int[] FixturePlaceOrder = new int[] { 1, 3, 4 };  // 空夹具放入工位顺序

        // 料架配置（config.json racks[]），用于计算铝板电极料架抓取任务号
        static List<RackConfig> Racks = new List<RackConfig>();
        class RackConfig
        {
            public string Id;
            public string Type;
            public string Name;
            public string Number;
            public string GrabStartTaskNo;
            public string PlaceStartTaskNo;
        }
        class FixtureSlot { public int Id; public string Rfid; public string Racknum; }

        /// <summary>铝板电极上机主流程</summary>
        static void Main(string[] args)
        {
            LogHelper.Info("【" + StepName + "/" + DisplayName + "】分包启动");
            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败，退出" + DisplayName + "，配置来源: " + ConfigClient.LastSourceText); return; }
                if (!ExtractConfig(cfg)) return;
                InitMachines(cfg);
                LoadAluminumRacks(cfg);
                LogHelper.Configure(StepName, DisplayName, ApiBase);

                Mysql = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                Plc = new plc();
                if (!Plc.Connect(PlcIp, PlcPort))
                {
                    LogHelper.Error("PLC 连接失败: " + PlcIp);
                    return;
                }
                LogHelper.Info("PLC 连接成功");

                // ===== 条件① 待机机床且所有工位为空（4 工位） =====
                if (!TryPickAluEmptyMachine(out int macnum, out string ip))
                {
                    LogHelper.Info("没有“待机且所有工位为空”的机床，退出" + DisplayName);
                    return;
                }

                // ===== 条件② 铝板电极料架上有待加工电极 =====
                if (!TryGetAluminumJob(out int alSlot, out string alRfid, out string alName))
                {
                    LogHelper.Info("铝板电极料架(racktype=5)上没有待加工铝板电极，退出" + DisplayName);
                    return;
                }

                // ===== 条件③ 电极料架上有 3 个空夹具 =====
                if (CountEmptyFixtures() < 3)
                {
                    LogHelper.Info("电极料架上空夹具不足 3 个（当前 " + CountEmptyFixtures() + "），退出" + DisplayName);
                    return;
                }

                LogHelper.Info("条件满足：机床" + macnum + "(IP:" + ip + ")待机且 4 工位全空；铝板电极["
                    + alName + "]位于料位" + alSlot + "；电极料架空夹具 ≥3，开始" + DisplayName);

                // ===== 第一次置换夹爪：机器人没有夹爪才可执行取铝板电极/放机床任务 =====
                if (!ChangeGripperToEmpty())
                {
                    LogHelper.Error("第一次置换夹爪失败（未能清空机器人手上的夹爪/工件），退出" + DisplayName);
                    return;
                }

                // 置换夹爪后再次确认三项条件仍然成立
                if (!IsAluMachineStillReady(macnum)) { LogHelper.Warn("机床状态已变化，退出" + DisplayName); return; }
                if (!TryGetAluminumJob(out alSlot, out alRfid, out alName)) { LogHelper.Warn("铝板电极已被取走，退出" + DisplayName); return; }
                if (CountEmptyFixtures() < 3) { LogHelper.Warn("空夹具不足 3 个，退出" + DisplayName); return; }

                // ===== 机器人从铝板电极料架抓取铝板电极 =====
                int alGrabBase = GetAluminumGrabBase();
                if (alGrabBase <= 0)
                {
                    LogHelper.Error("未配置铝板电极料架(racks[] type=aluminum)的 grabStartTaskNo，退出" + DisplayName);
                    return;
                }
                LogHelper.Info("机器人抓取铝板电极(料位" + alSlot + "，任务号 " + (alGrabBase + alSlot) + ")...");
                if (!ExecuteTask(alGrabBase + alSlot))
                {
                    LogHelper.Error("抓取铝板电极失败，机器人可能未取到电极，退出" + DisplayName + "，需人工处理");
                    return;
                }
                LogHelper.Success("机器人已抓取铝板电极[" + alName + "]");
                if (!Updaterobotmessage(alName, 0)) LogHelper.Error("更新robotmessage失败(机器人手持铝板电极)");
                // 保留铝板电极料位 rfid/workpiecename 身份（不置空），仅标记为加工中(workstate=1)，
                // 便于下机流程通过 rfid 或 workpiecename 反查料架类型(racktype=5)
                try { Mysql.ExecuteSql("UPDATE rack_administrate SET workstate=1 WHERE id=" + alSlot); }
                catch (Exception ex) { LogHelper.Warn("标记铝板电极料位 " + alSlot + " 加工中失败: " + ex.Message); }
                LogHelper.Info("铝板电极料位 " + alSlot + " 保留工件身份并标记为加工中(workstate=1)");

                // ===== 放置铝板电极到机床 2 号工位 =====
                if (!OpenMachineDoorIfRoders(macnum))
                {
                    LogHelper.Error("机台开门失败（机器人手持铝板电极），需人工处理，退出" + DisplayName);
                    return;
                }
                int alPlaceTask = GetAluminumPlaceTaskNo(macnum);
                LogHelper.Info("机器人放置铝板电极到机床" + macnum + " 2号工位(任务号 " + alPlaceTask + ")...");
                if (!ExecuteTask(alPlaceTask))
                {
                    LogHelper.Error("放置铝板电极到机床" + macnum + " 2号工位失败（机器人仍持有电极），需人工处理！");
                    return;
                }
                CloseMachineDoorIfRoders(macnum);
                LogHelper.Success("铝板电极[" + alName + "]已放入机床" + macnum + " 2号工位");
                if (!UpdateMacWorkPosStatus(macnum, AluminumWorkNum, alName, 0))
                    LogHelper.Error("更新机床" + macnum + " 2号工位工件信息失败，需人工确认！");
                ClearRobotWorkpiece();

                // ===== 坐标替换：机床配置中 2 号工位对应坐标系 + 上传到机床 =====
                var coords = GetMachineWorkCoords(macnum);
                if (coords == null || coords.Count < AluminumWorkNum || string.IsNullOrEmpty(coords[AluminumWorkNum - 1]))
                {
                    LogHelper.Error("机床" + macnum + " 未配置 2 号工位坐标系(workCoords 第2个)，无法处理铝板电极程序，退出" + DisplayName);
                    return;
                }
                LogHelper.Info("铝板电极程序坐标替换为 2 号工位坐标系 " + coords[AluminumWorkNum - 1] + " 并上传到机床" + macnum + "...");
                if (!transferprogram.MergeAndUpload(macnum, ip, new List<string> { alName }, new List<string> { coords[AluminumWorkNum - 1] }))
                {
                    LogHelper.Error("铝板电极程序坐标替换/上传失败，需人工处理，退出" + DisplayName);
                    return;
                }
                LogHelper.Success("铝板电极程序已上传机床" + macnum);

                // ===== 第二次置换夹爪：机器人有电极夹爪才可抓取/放置空夹具 =====
                TargetGripperType = "electrode";
                if (!ChangeGripper())
                {
                    LogHelper.Error("第二次置换夹爪(电极夹爪)失败，退出" + DisplayName);
                    return;
                }

                // ===== 依次抓空夹具→放机床 1、3、4 号工位（顺序固定） =====
                foreach (int worknum in FixturePlaceOrder)
                {
                    FixtureSlot fixture = GetFirstFixtureSlot();
                    if (fixture == null)
                    {
                        LogHelper.Error("料架空夹具不足（缺少放入 " + worknum + " 号工位的空夹具），机床已放入部分夹具，需人工处理！");
                        return;
                    }
                    int grabBase = GetRackGrabStartNoByPos(fixture.Id);
                    if (grabBase <= 0)
                    {
                        LogHelper.Error("料架" + fixture.Racknum + " 未配置 grabStartTaskNo，无法抓取空夹具，需人工处理！");
                        return;
                    }
                    LogHelper.Info("抓取空夹具[" + fixture.Rfid + "](料位" + fixture.Id + "，任务号 " + (grabBase + fixture.Id) + ")...");
                    if (!ExecuteTask(grabBase + fixture.Id))
                    {
                        LogHelper.Error("抓取空夹具[" + fixture.Rfid + "]失败（机器人可能仍持有夹具），需人工处理！");
                        return;
                    }
                    if (!ClearRackSlot(fixture.Id)) LogHelper.Error("更新料架格位 " + fixture.Id + " 为空失败！");
                    if (!Updaterobotmessage(fixture.Rfid, 0)) LogHelper.Error("更新robotmessage失败(机器人手持空夹具)");

                    if (!OpenMachineDoorIfRoders(macnum))
                    {
                        LogHelper.Error("机台开门失败，需人工处理！");
                        return;
                    }
                    int placeTask = GetMachineStartNo(macnum, "place") + worknum;
                    LogHelper.Info("放置空夹具[" + fixture.Rfid + "]到机床" + macnum + " " + worknum + "号工位(任务号 " + placeTask + ")...");
                    if (!ExecuteTask(placeTask))
                    {
                        LogHelper.Error("放置空夹具到机床" + macnum + " " + worknum + "号工位失败（机器人仍持有夹具），需人工处理！");
                        return;
                    }
                    CloseMachineDoorIfRoders(macnum);
                    LogHelper.Success("空夹具[" + fixture.Rfid + "]已放入机床" + macnum + " " + worknum + "号工位");
                    if (!UpdateMacWorkPosStatus(macnum, worknum, fixture.Rfid, 0))
                        LogHelper.Error("更新机床" + macnum + " " + worknum + "号工位失败，需人工确认！");
                    ClearRobotWorkpiece();
                }

                // ===== 启动加工，成功后更新机床所有工位工作状态为加工中 =====
                LogHelper.Info("机床" + macnum + " 工位已装满（2号位铝板电极 + 1/3/4号位空夹具），启动加工...");
                if (!MacStartProcessSafe(macnum))
                {
                    LogHelper.Error("启动加工失败，机床" + macnum + "已装满工件，需人工处理");
                    return;
                }
                if (!UpdateMacWorkPosStatusAll(macnum, 1))
                    LogHelper.Error("更新机床" + macnum + " 工位加工状态失败！");
                LogHelper.Success(DisplayName + "完成：机床" + macnum + " 2号位铝板电极 + 1/3/4号位空夹具，已启动加工");
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

        // ==================== 铝板电极料架（racks[] type=aluminum） ====================
        static void LoadAluminumRacks(Dictionary<string, object> cfg)
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

        /// <summary>铝板电极料架(racks[] type=aluminum)抓取开始任务号，未配置返回0</summary>
        static int GetAluminumGrabBase()
        {
            foreach (var rack in Racks)
                if (string.Equals(rack.Type, "aluminum", StringComparison.OrdinalIgnoreCase))
                    return ParseTaskNoBase(rack.GrabStartTaskNo);
            return 0;
        }

        // ==================== 铝板电极料架待加工工件（racktype=5） ====================
        /// <summary>获取铝板电极料架(racktype=5)第一个待加工铝板电极</summary>
        static bool TryGetAluminumJob(out int slot, out string rfid, out string jobName)
        {
            slot = 0; rfid = ""; jobName = "";
            try
            {
                DataTable dt = Mysql.ExeForDtl(
                    "SELECT id, rfid, workpiecename FROM rack_administrate WHERE racktype=5 AND posstatus=2 AND workstate=0 " +
                    "AND rfid IS NOT NULL AND rfid<>'' AND rfid NOT LIKE '%EMPTY%' ORDER BY id");
                if (dt != null && dt.Rows.Count > 0)
                {
                    slot = SafeInt(dt.Rows[0]["id"]);
                    rfid = SafeStr(dt.Rows[0]["rfid"]);
                    string wp = SafeStr(dt.Rows[0]["workpiecename"]);
                    jobName = string.IsNullOrEmpty(wp) ? rfid : wp; // 程序名/工件名：workpiecename 优先，空则回退 rfid
                    return !string.IsNullOrEmpty(jobName);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("查询铝板电极料架待加工电极异常: " + ex.Message);
            }
            return false;
        }

        // ==================== 空夹具（EMPTY）统计/获取（与 fill_fixture 口径一致） ====================
        /// <summary>统计料架上可用空夹具数量（posstatus=2 + rfid含EMPTY，或旧数据 rfid有编号+workpiecename空）</summary>
        static int CountEmptyFixtures()
        {
            try
            {
                int cnt = 0;
                DataTable dt = Mysql.ExeForDtl("SELECT id, rfid, workpiecename, posstatus FROM rack_administrate");
                if (dt != null)
                    foreach (DataRow r in dt.Rows)
                        if (IsEmptyFixtureRow(r)) cnt++;
                return cnt;
            }
            catch (Exception ex)
            {
                LogHelper.Error("统计空夹具数量异常: " + ex.Message);
                return 0;
            }
        }

        /// <summary>空夹具识别：posstatus=2 且 rfid 含 EMPTY，或兼容历史数据 rfid 有编号但 workpiecename 为空</summary>
        static bool IsEmptyFixtureRow(DataRow r)
        {
            try
            {
                int posstatus = SafeInt(r["posstatus"]);
                string rfid = SafeStr(r["rfid"]);
                string workpiecename = SafeStr(r["workpiecename"]);
                if (posstatus != 2 || string.IsNullOrEmpty(rfid)) return false;
                if (rfid.IndexOf("EMPTY", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return string.IsNullOrEmpty(workpiecename);
            }
            catch { return false; }
        }

        /// <summary>从料架获取第一个空夹具位置（按料位号升序），没有返回 null</summary>
        static FixtureSlot GetFirstFixtureSlot()
        {
            try
            {
                DataTable dt = Mysql.ExeForDtl(
                    "SELECT id, rfid, racknum, posstatus, workpiecename FROM rack_administrate ORDER BY id");
                if (dt != null)
                    foreach (DataRow r in dt.Rows)
                        if (IsEmptyFixtureRow(r))
                            return new FixtureSlot
                            {
                                Id = SafeInt(r["id"]),
                                Rfid = SafeStr(r["rfid"]),
                                Racknum = SafeStr(r["racknum"])
                            };
            }
            catch (Exception ex)
            {
                LogHelper.Error("查询空夹具料位异常: " + ex.Message);
            }
            return null;
        }

        /// <summary>根据料位号获取所属料架的抓取开始任务号（优先 racknum 匹配 config racks[]，兜底按类型）</summary>
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
                    if (string.Equals(rack.Number, racknum, StringComparison.OrdinalIgnoreCase))
                        return ParseTaskNoBase(rack.GrabStartTaskNo);
                string rackType = GetRackTypeByPos(pos);
                foreach (var rack in Racks)
                    if (string.Equals(rack.Type, rackType, StringComparison.OrdinalIgnoreCase))
                        return ParseTaskNoBase(rack.GrabStartTaskNo);
            }
            catch (Exception ex) { LogHelper.Error("获取料位 " + pos + " 抓取任务号异常: " + ex.Message); }
            return 0;
        }

        /// <summary>根据料位号获取料架类型字符串（优先 racknum 匹配 config racks[]，兜底 racktype 数字映射）</summary>
        static string GetRackTypeByPos(int pos)
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT racknum, racktype FROM rack_administrate WHERE id=" + pos))
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
                        return "steel";
                    }
                }
            }
            catch (Exception ex) { LogHelper.Error("获取料位 " + pos + " 料架类型异常: " + ex.Message); }
            return "steel";
        }

        /// <summary>清空料架指定格位（铝板电极/空夹具取走后置空位）</summary>
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
                    LogHelper.Info("料架格位 " + pos + " 已清空置为无料");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("清空料架格位 " + pos + " 异常: " + ex.Message);
            }
            return flag;
        }

        // ==================== 待机且 4 工位全空机床选择 ====================
        /// <summary>获取“待机且所有工位为空(4工位)+已配置2号工位坐标系+放置任务号”的机床</summary>
        static bool TryPickAluEmptyMachine(out int macnum, out string ip)
        {
            macnum = 0; ip = "";
            try
            {
                DataTable dt = Mysql.ExeForDtl("select * from macmessage where status!=0 order by id");
                if (dt == null) return false;
                foreach (DataRow r in dt.Rows)
                {
                    int mid = SafeInt(r["id"]);
                    if (!MachineConfigs.ContainsKey(mid)) continue;
                    ip = MachineConfigs[mid].ContainsKey("ip") ? MachineConfigs[mid]["ip"] : "";
                    if (string.IsNullOrEmpty(ip) && r.Table.Columns.Contains("machineip"))
                        ip = SafeStr(r["machineip"]);

                    EnsureMacWorkPosRows(mid);
                    string st = GetMacStatusSafe(mid);
                    if (st != "待机") continue;
                    var empty = GetMacEmptyPositions(mid);
                    int total = GetMacTotalPositions(mid);
                    if (total != 4 || empty.Count != total) continue;
                    var coords = GetMachineWorkCoords(mid);
                    if (coords == null || coords.Count < AluminumWorkNum || string.IsNullOrEmpty(coords[AluminumWorkNum - 1])) continue;
                    if (GetMachineStartNo(mid, "place") <= 0) continue;

                    macnum = mid;
                    LogHelper.Info("选择待机机床" + mid + "(IP:" + ip + ")，4工位全空");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("选择铝板电极上机机床异常: " + ex.Message);
            }
            return false;
        }

        /// <summary>再次确认机床仍“待机且4工位全空”</summary>
        static bool IsAluMachineStillReady(int macnum)
        {
            try
            {
                if (!MachineConfigs.ContainsKey(macnum)) return false;
                string st = GetMacStatusSafe(macnum);
                if (st != "待机") return false;
                var empty = GetMacEmptyPositions(macnum);
                int total = GetMacTotalPositions(macnum);
                return total == 4 && empty.Count == total;
            }
            catch { return false; }
        }

        // ==================== 置换夹爪：机器人空手（第一次） ====================
        /// <summary>第一次置换夹爪：清空机器人手上的夹爪与工件，使机器人处于无夹爪状态</summary>
        static bool ChangeGripperToEmpty()
        {
            LogHelper.Info("第一次置换夹爪：要求机器人没有夹爪才可以执行取铝板电极/放机床任务");

            if (IsFixedGripper)
            {
                // 固定爪：无需换爪，但必须确认手上没有工件
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

        /// <summary>把机器人手上的工件放到料架空位（任务号=空料位号）</summary>
        static bool StashRobotPiece(string piece)
        {
            int emptySlot = GetEmptyRackSlot();
            if (emptySlot == 0)
            {
                LogHelper.Error("没有可用的料架空位存放机器人手上的工件[" + piece + "]，需人工处理");
                return false;
            }
            Plc.WriteDM(PlcDm, emptySlot);
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
    }
}



