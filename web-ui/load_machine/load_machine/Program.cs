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
        // 配置：始终与 MachineMonitor.exe 执行路径一致
        // 从当前运行目录向上逐级查找 config.json，避免受 subs/ 嵌套层级影响
        static string ConfigPath = ResolveConfigPath();

        static string ResolveConfigPath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "config.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            // 兜底：回到 MachineMonitor.exe 同级目录（默认退一级）
            return Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).FullName, "config.json");
        }
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


        // 夹爪配置
        static string GripperType = "fixed-single";
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
        static CommonInterface Mysql;
        static void Main(string[] args)
        {
            Log("INFO", "【" + DisplayName + "】分包启动");
            try
            {
                LoadConfig();
                InitMachines();
                CommonInterface Mysql = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                Plc = new plc();
                if (!Plc.Connect(PlcIp, PlcPort))
                {
                    Log("ERROR", "PLC 连接失败: " + PlcIp);
                    return;
                }
                Log("INFO", "PLC 连接成功");

                List<string> waitmaclist = Waitmaclist();
                if (waitmaclist.Count == 0)
                {
                    Log("INFO", "无待机机床，退出上机流程");
                    return;
                }

                // 确定工件来源：有随行台 → 从随行台取料，无随行台 → 从机器人夹爪取料
                bool hasShuttle = HasShuttle();
                List<string> followpiecelist = null;
                string robotPieceName = null;

                if (hasShuttle)
                {
                    followpiecelist = FollowHavePieceList();
                    if (followpiecelist.Count == 0)
                    {
                        Log("INFO", "有随行台但无待上机工件，退出上机流程");
                        return;
                    }
                    Log("INFO", "随行台模式：共 " + followpiecelist.Count + " 个待上机工件");
                }
                else
                {
                    robotPieceName = GetRobotMessagePieceMessage();
                    if (string.IsNullOrEmpty(robotPieceName))
                    {
                        Log("INFO", "无随行台且机器人夹爪无工件，退出上机流程");
                        return;
                    }
                    Log("INFO", "无随行台，机器人夹爪有工件: " + robotPieceName);
                }

                // 1. 执行置换夹爪流程
                if (!ChangeGripper())
                {
                    Log("ERROR", "置换夹爪失败，退出上机流程");
                    ProcessTermination = true;
                    return;
                }

                if (hasShuttle)
                {
                    // ===== 有随行台：从随行台取料 → 放机床 =====
                    for (int i = 0; i < waitmaclist.Count; i++)
                    {
                        if (ProcessTermination) break;
                        for (int j = 0; j < followpiecelist.Count; j++)
                        {
                            if (ProcessTermination) break;
                            int macnum = Convert.ToInt16(waitmaclist[i].Split(',')[0]);
                            string ip = waitmaclist[i].Split(',')[1];

                            // 检查机床上是否已有工件，有则跳过
                            if (MachineHasPiece(macnum))
                            {
                                Log("INFO", "机床" + macnum + " 上已有工件，跳过该机床");
                                break;
                            }

                            int followpos = Convert.ToInt16(followpiecelist[j].Split(',')[0]);
                            string workpiecename = followpiecelist[j].Split(',')[1];

                            if (!ExecuteTask(followpos))
                            {
                                Log("ERROR", "机器人执行从随行台" + followpos + "料位抓取任务失败，退出上机流程！");
                                ProcessTermination = true;
                                break;
                            }
                            if (!Updatefollowing_Rack(followpos, null, 1))
                                Log("ERROR", "更新随行台" + followpos + "料位信息失败！");
                            if (!Updaterobotmessage(workpiecename, 2))
                                Log("ERROR", "更新机器人手爪信息失败！");
                            if (!OpenMachineDoorIfRoders(macnum))
                            {
                                ProcessTermination = true;
                                break;
                            }
                            if (!ExecuteTask(macnum))
                            {
                                Log("ERROR", "机器人执行放置工件到" + macnum + "任务失败，退出上机流程！");
                                ProcessTermination = true;
                                break;
                            }
                            CloseMachineDoorIfRoders(macnum);
                            if (!Updaterobotmessage(null, 1))
                                Log("ERROR", "更新机器人手爪信息失败！");
                            if (!UpdateMacWorkPosStatus(macnum, workpiecename, 0))
                                Log("ERROR", "更新机床工位表" + macnum + "工件信息失败！");
                            bool uploaded = HandleUploadProgram(macnum, ip, workpiecename);
                            if (!uploaded && GetMacEmptyPositions(macnum).Count == 0)
                            {
                                Log("INFO", "机床" + macnum + "无空工位，跳过上机流程！");
                                break;
                            }
                            if (uploaded && !UpdateMacWorkPosStatus(macnum, workpiecename, 1))
                                Log("ERROR", "更新机床工位" + macnum + "工件状态失败！");
                            if (!Updaterack_administrate(workpiecename, 1))
                                Log("ERROR", "更新料架" + workpiecename + "工件加工状态失败！");
                        }
                    }
                }
                else
                {
                    // ===== 无随行台：机器人夹爪已有工件 → 直接放机床 =====
                    int macnum = Convert.ToInt16(waitmaclist[0].Split(',')[0]);
                    string ip = waitmaclist[0].Split(',')[1];

                    // 机床上已有工件 → 退出
                    if (MachineHasPiece(macnum))
                    {
                        Log("INFO", "机床" + macnum + " 已有工件，退出上机流程");
                        return;
                    }

                    // 机床上无工件 且 机器人有工件 → 执行上机
                    if (!MachineHasPiece(macnum) && !string.IsNullOrEmpty(robotPieceName))
                    {
                        if (!OpenMachineDoorIfRoders(macnum))
                        {
                            ProcessTermination = true;
                            return;
                        }
                        if (!ExecuteTask(macnum))
                        {
                            Log("ERROR", "机器人执行放置工件到" + macnum + "任务失败，退出上机流程！");
                            ProcessTermination = true;
                            return;
                        }
                        Log("INFO", "机器人执行放置工件到" + macnum + "任务成功！");
                        CloseMachineDoorIfRoders(macnum);
                        if (!Updaterobotmessage(null, 1))
                            Log("ERROR", "更新机器人手爪信息失败！");
                        if (!UpdateMacWorkPosStatus(macnum, robotPieceName, 0))
                            Log("ERROR", "更新机床工位表" + macnum + "工件信息失败！");
                        bool uploaded = HandleUploadProgram(macnum, ip, robotPieceName);
                        if (!uploaded && GetMacEmptyPositions(macnum).Count == 0)
                        {
                            Log("INFO", "机床" + macnum + "无空工位，跳过上机流程！");
                            return;
                        }
                        if (uploaded && !UpdateMacWorkPosStatus(macnum, robotPieceName, 1))
                            Log("ERROR", "更新机床工位" + macnum + "工件状态失败！");
                        if (!Updaterack_administrate(robotPieceName, 1))
                            Log("ERROR", "更新料架" + robotPieceName + "工件加工状态失败！");
                    }
                }
                Log("INFO", "【" + DisplayName + "】分包结束");
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
                string str = "select * from following_rack where posstatus=2 and workstate=0";
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
                    string status = GetMacStatusSafe(Convert.ToInt16(r["id"].ToString()));
                    string ip = r["machineip"].ToString();
                    string workpiecename = r["workpiecename"].ToString();
                    string workpieceworkstate = r["workpieceworkstate"].ToString();
                    if (status == "reset" && workpiecename == "" && workpieceworkstate == "0")
                        macmessage.Add(r["id"].ToString() + "," + ip);
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
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update following_rack set workpiecename=" + workpieceStr + ",posstatus=" + posstatus + " where id=" + pos;
                if (Db.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                Log("ERROR", "更新随行台信息异常: " + ex.Message);
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
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update rack_administrate set workstate=" + workstate + " where workpiecename=" + workpieceStr;
                if (Db.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                Log("ERROR", "更新料架信息异常: " + ex.Message);
            }
            return flag;
        }

        /// <summary>
        /// <summary>
        /// 置换夹爪流程 - 将机器人手上夹爪更换为本流程需要的夹爪
        /// </summary>
        static bool ChangeGripper()
        {
            Log("INFO", "开始置换夹爪流程...目标类型: " + TargetGripperType);

            if (IsFixedGripper)
            {
                Log("INFO", "固定爪模式，无需置换");
                return true;
            }

            // 读取当前 robotmessage 状态
            string curName = "", curType = "", curWorkpiece = "";
            int curPosStatus = 0;
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT clampname,clampstatus,clampposstatus,workpiecename FROM robotmessage LIMIT 1"))
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
            catch (Exception ex) { Log("ERROR", "读取 robotmessage 失败: " + ex.Message); return false; }

            // 手上无夹爪 → 直接抓取目标夹爪
            if (curPosStatus == 0)
            {
                Log("INFO", "手上无夹爪，直接抓取目标夹爪");
                return PickTargetGripper();
            }

            // 手上已有目标夹爪 → 完成
            if (string.Equals(curType, TargetGripperType, StringComparison.OrdinalIgnoreCase) ||
                (curType == "1" && TargetGripperType == "steel") ||
                (curType == "2" && TargetGripperType == "tool") ||
                (curType == "3" && TargetGripperType == "electrode") ||
                (curType == "4" && TargetGripperType == "pallet"))
            {
                Log("INFO", "手上已为目标夹爪: " + curName);
                return true;
            }

            // 手上有其他快换爪 → 先清空再抓取目标
            Log("INFO", "手上有夹爪(" + curName + ")，先清空...");
            bool hasWorkpiece = !string.IsNullOrEmpty(curWorkpiece) && curWorkpiece != "NULL";

            if (hasWorkpiece)
            {
                // 先放工件
                int emptySlot = GetEmptyRackSlot();
                if (emptySlot == 0) { Log("ERROR", "无料架空位"); return false; }
                Plc.WriteDM(PlcDm, emptySlot);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { Log("ERROR", "放工件超时"); return false; }
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
            if (cur == null) { Log("WARN", "未找到匹配夹爪配置，跳过放置"); return true; }
            if (!string.IsNullOrEmpty(cur.SlotPhotoSignal) && Plc.ReadWR(cur.SlotPhotoSignal))
            { Log("ERROR", "夹爪料位有异物"); return false; }
            if (!string.IsNullOrEmpty(cur.PlaceTaskNo))
            {
                int taskNo = ParseTaskNoBase(cur.PlaceTaskNo) + (string.IsNullOrEmpty(cur.Number) ? 0 : int.Parse(cur.Number));
                Plc.WriteDM(PlcDm, taskNo);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { Log("ERROR", "放夹爪超时"); return false; }
            }
            ClearRobotClamp();
            Log("INFO", "当前夹爪已清空");
            return true;
        }

        static bool PickTargetGripper()
        {
            var target = FindGripperByType(TargetGripperType);
            if (target == null) { Log("ERROR", "未找到目标夹爪配置"); return false; }
            if (!string.IsNullOrEmpty(target.SlotPhotoSignal) && !Plc.ReadWR(target.SlotPhotoSignal))
            { Log("WARN", "目标夹爪料位光电无信号，可能无夹爪"); }
            if (!string.IsNullOrEmpty(target.GrabTaskNo))
            {
                int taskNo = ParseTaskNoBase(target.GrabTaskNo) + (string.IsNullOrEmpty(target.Number) ? 0 : int.Parse(target.Number));
                Plc.WriteDM(PlcDm, taskNo);
                SendTaskStart();
                if (!WaitForTaskFinish(3000)) { Log("ERROR", "抓夹爪超时"); return false; }
            }
            // 更新 DB
            try { Mysql.ExecuteSql("UPDATE robotmessage SET clampname='" + target.Name + "',clampstatus=" + (TargetGripperType=="steel"?1:TargetGripperType=="tool"?2:TargetGripperType=="electrode"?3:4) + ",clampposstatus=1,workpiecename=NULL,workpiecestatus=0"); }
            catch { }
            Log("SUCCESS", "已抓取目标夹爪: " + target.Name);
            return true;
        }

        static GripperConfig FindGripperByType(string type)
        {
            foreach (var g in Grippers)
                if (string.Equals(g.Type, type, StringComparison.OrdinalIgnoreCase) ||
                    (type=="1" && g.Type=="steel") || (type=="2" && g.Type=="tool") || (type=="3" && g.Type=="electrode") || (type=="4" && g.Type=="pallet"))
                    return g;
            return Grippers.Count > 0 ? Grippers[0] : null;
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
                Log("INFO", "复位任务开始信号: " + TaskStartAddr);
            }

            // 2. 写入任务号
            if (!Plc.WriteDM(PlcDm, tasknum))
            {
                Log("ERROR", "发送任务号失败: " + tasknum);
                return false;
            }
            Log("INFO", "写入任务号 " + tasknum + " 至 DM" + PlcDm);

            // 3. 发送任务开始信号
            if (!string.IsNullOrEmpty(TaskStartAddr))
            {
                if (!Plc.WriteWR(TaskStartAddr, 1))
                {
                    Log("ERROR", "发送任务开始信号失败: " + TaskStartAddr);
                    return false;
                }
                Log("INFO", "发送任务开始信号: " + TaskStartAddr);
            }
            else
            {
                Log("WARN", "未配置任务开始信号，跳过");
            }

            // 4. 等待任务完成信号
            if (!string.IsNullOrEmpty(TaskFinishAddr))
            {
                Log("INFO", "等待任务完成信号 " + TaskFinishAddr + " = 1...");
                int waitCount = 0;
                while (!Plc.ReadWR(TaskFinishAddr) && waitCount < 3000)
                {
                    Thread.Sleep(100);
                    waitCount++;
                }

                if (!Plc.ReadWR(TaskFinishAddr))
                {
                    Log("ERROR", "等待任务完成超时，任务: " + tasknum);
                    return false;
                }
                Log("INFO", "任务完成信号已收到: " + TaskFinishAddr);
            }

            Log("SUCCESS", "执行任务 " + tasknum + " 完成");
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
                Log("ERROR", $"更新机床主表状态异常: {ex.Message}");
            }
            return flag;
        }

        /// <summary>
        /// 更新机床工位表（macworkposstatus）
        /// </summary>
        /// <param name="macnum">机床编号</param>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="workpiecestatus">工件状态 0=待加工 1=加工中</param>
        static bool UpdateMacWorkPosStatus(int macnum, string workpiecename, int workpiecestatus)
        {
            bool flag = false;
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string nameStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                // 找到该机床第一个 posstatus=0 的空工位，填入工件
                string str = $"update macworkposstatus set workpiecename={nameStr}, workpiecestatus={workpiecestatus} " +
                             $"where id=(select id from (select id from macworkposstatus where machinename='{macnum}' and posstatus=0 limit 1) t) ";
                if (Db.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                Log("ERROR", $"更新机床工位表异常: {ex.Message}");
            }
            return flag;
        }

        /// <summary>
        /// 获取机床空工位列表（posstatus=0）
        /// </summary>
        static List<int> GetMacEmptyPositions(int macnum)
        {
            var list = new List<int>();
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string str = $"select worknum from macworkposstatus where machinename='{macnum}' and posstatus=0";
                var dt = Db.ExeForDtl(str);
                foreach (DataRow r in dt.Rows)
                    list.Add(Convert.ToInt32(r["worknum"]));
            }
            catch (Exception ex)
            {
                Log("ERROR", $"获取机床空工位异常: {ex.Message}");
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
                string str = $"select count(*) as cnt from macworkposstatus where machinename='{macnum}'";
                var dt = Db.ExeForDtl(str);
                if (dt.Rows.Count > 0)
                    return Convert.ToInt32(dt.Rows[0]["cnt"]);
            }
            catch (Exception ex)
            {
                Log("ERROR", $"获取机床总工位数异常: {ex.Message}");
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
                Log("INFO", $"机床 {macnum} 有 {totalPos} 个工位，需合并程序后再上传");
            else
                Log("INFO", $"机床 {macnum} 只有1个工位，无需合并，直接上传");

            // 无空工位：仍需上传程序并启动加工
            if (emptyList.Count == 0)
            {
                Log("INFO", $"机床 {macnum} 无空工位，上传程序并启动加工");
            }
            // 有空工位：先更新工位表，再上传
            else
            {
                Log("INFO", $"机床 {macnum} 有 {emptyList.Count} 个空工位，上传程序并启动加工");
            }

            // TODO: 将 transferprogram.RunTransferProcess(ip, workpiecename)
            // 替换为实际的合并上传方法（transfer 类需要先实现合并逻辑接口）
            if (!transferprogram.RunTransferProcess(ip, workpiecename))
            {
                Log("ERROR", $"上传程序到机床 {macnum} 失败！");
                return false;
            }
            Log("INFO", $"上传程序到机床 {macnum} 成功" + (needMerge ? "（已合并）" : ""));

            // 启动加工
            if (!MacStartProcessSafe(macnum))
            {
                Log("ERROR", $"启动机床 {macnum} 加工失败！");
                return false;
            }
            Log("INFO", $"机床 {macnum} 启动加工成功！");
            return true;
        }

        static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));

                    if (cfg.ContainsKey("database"))
                    {
                        var db = GetDict(cfg, "database");
                        string host = db.ContainsKey("host") ? db["host"].ToString() : "localhost";
                        string dbname = db.ContainsKey("database") ? db["database"].ToString() : "qianduantest";
                        string user = db.ContainsKey("user") ? db["user"].ToString() : "root";
                        string pwd = db.ContainsKey("password") ? db["password"].ToString() : "123456";
                        DbType = db.ContainsKey("type") ? db["type"].ToString().ToLower() : "mysql";
                        if (DbType == "sqlserver")
                        {
                            int dbport = db.ContainsKey("port") ? Convert.ToInt32(db["port"]) : 1433;
                            ConnStr = $"Server={host},{dbport};Database={dbname};User Id={user};Password={pwd};";
                        }
                        else
                        {
                            ConnStr = $"Server={host};Database={dbname};Uid={user};Pwd={pwd};CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
                        }
                    }

                    if (cfg.ContainsKey("plc"))
                    {
                        var plcCfg = GetDict(cfg, "plc");
                        if (plcCfg.ContainsKey("ip")) PlcIp = plcCfg["ip"].ToString();
                        if (plcCfg.ContainsKey("port")) PlcPort = Convert.ToInt32(plcCfg["port"]);

                        // 从 plc.signals.robot 读取任务号地址、开始/完成信号
                        var signals = GetDict(plcCfg, "signals");
                        var robotSig = GetDict(signals, "robot");
                        if (robotSig.ContainsKey("taskNumber"))
                            int.TryParse(robotSig["taskNumber"].ToString().Replace("D","").Replace("DM","").Trim(), out PlcDm);
                        TaskStartAddr  = GetStr(robotSig, "taskStart", "");
                        TaskFinishAddr = GetStr(robotSig, "taskDone", "");
                        RobotEleAddr   = GetStr(robotSig, "photo", "");

                        // 兜底：旧格式 addresses
                        var addrs = GetDict(plcCfg, "addresses");
                        if (string.IsNullOrEmpty(TaskFinishAddr) && addrs.ContainsKey("task_finish"))
                            TaskFinishAddr = addrs["task_finish"].ToString();
                        if (string.IsNullOrEmpty(RobotEleAddr) && addrs.ContainsKey("robot_ele"))
                            RobotEleAddr = addrs["robot_ele"].ToString();
                    }

                    // 加载夹爪配置
                    if (cfg.ContainsKey("line"))
                    {
                        var line = GetDict(cfg, "line");
                        GripperType = GetStr(line, "gripperType", "fixed-single");
                        IsFixedGripper = GripperType == "fixed-single" || GripperType == "fixed-double";
                    }
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
            }
            catch (Exception ex)
            {
                Log("WARN", "加载配置失败，使用默认配置: " + ex.Message);
            }
        }


        // 辅助方法：安全获取嵌套字典
        static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key) || d[key] == null) return new Dictionary<string, object>();
            if (d[key] is Dictionary<string, object> nested) return nested;
            if (d[key] is System.Collections.Generic.Dictionary<string, object> nested2) return nested2;
            try { return Js.Deserialize<Dictionary<string, object>>(d[key].ToString()) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        static string GetStr(Dictionary<string, object> d, string key, string def = "")
        {
            return d.ContainsKey(key) && d[key] != null ? d[key].ToString() : def;
        }

        static int GetInt(Dictionary<string, object> d, string key, int def = 0)
        {
            return d.ContainsKey(key) && d[key] != null ? Convert.ToInt32(d[key]) : def;
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
        /// <param name="workpieceStatus">工件状态（0：无工件、1：有工件）</param>
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
                    Log("INFO", $"更新robotmessage表成功: clampname={clampName}, clampstatus={clampStatus}, clampposstatus={clampPosStatus}, workpiecename={workpieceName}, workpiecestatus={workpieceStatus}");
                }
                else
                {
                    Log("ERROR", "更新robotmessage表失败");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", "更新robotmessage表异常: " + ex.Message);
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
                    Log("INFO", "更新robotmessage表成功 - 抓取夹爪: clampposstatus=1, workpiecename=NULL, workpiecestatus=0");
                }
                else
                {
                    Log("ERROR", "更新robotmessage表失败 - 抓取夹爪");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", "更新robotmessage表异常: " + ex.Message);
            }
        }

        static string GetRobotMessagePieceMessage()
        {
            string piecemessage = null;
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string str = "select workpiecename from robotmessage where id=1 and workpiecestatus=0";
                piecemessage = Db.GetValue(str).ToString(); 
            }
            catch(Exception ex)
            {

            }
            return piecemessage;
        }

        /// <summary>
        /// 判断系统是否配置了随行台
        /// </summary>
        static bool HasShuttle()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));
                    var line = GetDict(cfg, "line");
                    string val = GetStr(line, "shuttleExists", "");
                    return !string.IsNullOrEmpty(val);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 判断机床上是否已有工件（加工完成等待换料）
        /// </summary>
        static bool MachineHasPiece(int macnum)
        {
            try
            {
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string str = "select COUNT(*) from macmessage where id=" + macnum + " and workpiecename IS NOT NULL and workpiecename != ''";
                object val = Db.GetValue(str);
                return val != null && Convert.ToInt32(val) > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 更新机器人手爪上的工件信息和料位状态
        /// </summary>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="workpiecestatus">夹爪上工件料位状态</param>
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
                Log("ERROR", "更新机器人手爪信息异常: " + ex.Message);
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
                    Log("INFO", $"更新robotmessage表成功 - 置换夹爪: clampname={clampName}, clampstatus={clampStatus}, clampposstatus=1");
                }
                else
                {
                    Log("ERROR", "更新robotmessage表失败 - 置换夹爪");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", "更新robotmessage表异常: " + ex.Message);
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
                    Log("INFO", $"更新robotmessage表成功 - 抓取夹爪: taskId={taskId}, clampposstatus=1, workpiecestatus=0");
                }
                else
                {
                    Log("ERROR", "更新robotmessage表失败 - 抓取夹爪");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", "更新robotmessage表异常: " + ex.Message);
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
                    Log("INFO", "更新robotmessage表成功 - 放置夹爪: clampname=NULL, clampposstatus=0, workpiecename=NULL, workpiecestatus=0");
                }
                else
                {
                    Log("ERROR", "更新robotmessage表失败 - 放置夹爪");
}
            }
            catch (Exception ex)
            {
                Log("ERROR", "更新robotmessage表异常: " + ex.Message);
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
        static void InitMachines()
        {
            try
            {
                MachineConfigs.Clear();
                if (!File.Exists(ConfigPath))
                {
                    Log("WARN", "配置文件不存在，跳过机床初始化: " + ConfigPath);
                    return;
                }
                var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));
                if (!cfg.ContainsKey("machines"))
                {
                    Log("WARN", "配置文件中没有 machines 节点，跳过机床初始化");
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
                    MachineConfigs[macId] = new Dictionary<string, string>() { { "type", typeStr }, { "ip", ip }, { "ncPath", ncPath } };

                    MacCommunicate.Macfactory.MacType macType;
                    switch (typeStr)
                    {
                        case "fanuc": macType = MacCommunicate.Macfactory.MacType.fanuc; break;
                        case "siemense": macType = MacCommunicate.Macfactory.MacType.siemense; break;
                        case "makino": macType = MacCommunicate.Macfactory.MacType.makino; break;
                        default: macType = MacCommunicate.Macfactory.MacType.roders; break;
                    }
                    var mac = MacCommunicate.Macfactory.MacFactory.CreateInstance(macType, ip, sessionUrl, username, password);
                    if (mac != null)
                    {
                        MacList[macId] = mac;
                        Log("INFO", $"机床 {macId} ({typeStr}) 已创建实例，IP: {ip}" + (string.IsNullOrEmpty(sessionUrl) ? "" : $"，会话: {sessionUrl}"));
                    }
                    else
                    {
                        Log("ERROR", $"机床 {macId} 类型 {typeStr} 创建实例失败");
                    }
                }
                Log("INFO", $"机床通讯初始化完成，共 {MacList.Count} 台");
            }
            catch (Exception ex)
            {
                Log("ERROR", "InitMachines 异常: " + ex.Message);
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
                Log("ERROR", $"获取机床 {macId} 状态异常: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 安全启动机床加工（容错包装）
        /// </summary>
        static bool MacStartProcessSafe(int macId)
        {
            try
            {
                if (MacList.ContainsKey(macId) && MacList[macId] != null)
                    return MacList[macId].MacStartProcess(macId);
            }
            catch (Exception ex)
            {
                Log("ERROR", $"启动机床 {macId} 加工异常: {ex.Message}");
            }
            return false;
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

            Log("INFO", "机床" + macId + " 为roders，准备连接并开门");

            // 1. 获取机床通讯实例并连接（创建TCP服务器:20200，等待机床主动连接）
            if (!MacList.ContainsKey(macId) || MacList[macId] == null)
            {
                Log("ERROR", "机床" + macId + " 通讯实例不存在");
                return false;
            }
            if (!MacList[macId].ConnectMac())
            {
                Log("ERROR", "机床" + macId + " 连接失败（TCP服务器创建或机床未连接）");
                return false;
            }
            Log("INFO", "机床" + macId + " 已连接");

            // 2. 检查机床状态为空闲("reset")时才允许开门
            string status = MacList[macId].GetMacStatus();
            Log("INFO", "机床" + macId + " 当前状态: " + (status ?? "未知"));
            if (status != "reset")
            {
                Log("WARN", "机床" + macId + " 非空闲状态(" + status + ")，等待变为空闲...");
                int wait = 0;
                while (wait < 300) // 最多等30秒
                {
                    Thread.Sleep(100);
                    status = MacList[macId].GetMacStatus();
                    if (status == "reset") break;
                    wait++;
                }
                if (status != "reset")
                {
                    Log("ERROR", "机床" + macId + " 等待空闲超时，当前状态: " + (status ?? "未知"));
                    return false;
                }
            }
            Log("INFO", "机床" + macId + " 空闲状态，可以开门");

            // 3. 开门
            if (!MacList[macId].OpenDoor(macId))
            {
                Log("ERROR", "机床" + macId + " 开门失败");
                return false;
            }
            Log("INFO", "机床" + macId + " 门已打开");
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
                Log("WARN", $"机床 {macId} 不在MacList中，无法开门");
                return false;
            }
            catch (Exception ex)
            {
                Log("ERROR", $"机床 {macId} 开门异常: {ex.Message}");
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
                Log("WARN", $"机床 {macId} 不在MacList中，无法关门");
                return false;
            }
            catch (Exception ex)
            {
                Log("ERROR", $"机床 {macId} 关门异常: {ex.Message}");
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

            Log("INFO", "机床" + macId + " 为roders，放置工件完成后关门");
            if (!MacCloseDoor(macId))
            {
                Log("WARN", "机床" + macId + " 关门失败，但不影响流程继续");
            }
            return true;
        }

        /// <summary>
        /// 更新UI界面日志
        /// </summary>
        /// <param name="level"></param>
        /// <param name="content">日志内容</param>
        static void Log(string level, string content)
        {
            string msg = "[" + level + "] " + content;
            Console.WriteLine(msg);

            try
            {
                var body = Js.Serialize(new { step = StepName, displayName = DisplayName, level, content = msg });
                var bytes = Encoding.UTF8.GetBytes(body);
                var req = WebRequest.CreateHttp(ApiBase + "/api/sub/log");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 3000;
                using (var rs = req.GetRequestStream())
                    rs.Write(bytes, 0, bytes.Length);
                try { req.GetResponse().Close(); } catch { }
            }
            catch { }
        }

        static string SafeStr(object val) => val == null || val == DBNull.Value ? "" : val.ToString();
        static int SafeInt(object val) => val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);

        static void SendTaskStart() { if (!string.IsNullOrEmpty(TaskStartAddr)) Plc.WriteWR(TaskStartAddr, 1); }
        static bool WaitForTaskFinish(int maxTicks) { int w = 0; while (w < maxTicks) { if (Plc.ReadWR(TaskFinishAddr)) return true; Thread.Sleep(100); w++; } return Plc.ReadWR(TaskFinishAddr); }
        static void ClearRobotWorkpiece() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0"); } catch { } }
        static void ClearRobotClamp() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0"); } catch { } }
        static int GetEmptyRackSlot() { try { using (DataTable dt = Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0 LIMIT 1")) { if (dt!=null && dt.Rows.Count>0) return Convert.ToInt32(dt.Rows[0]["id"]); } } catch { } return 0; }
        static int ParseTaskNoBase(string s) { if (string.IsNullOrEmpty(s)) return 0; int.TryParse(s.ToUpper().Replace("D","").Replace("DM","").Trim(), out int r); return r; }
        static void AddGripperConfig(object item) { var g = item as Dictionary<string,object>; if (g==null) return; Grippers.Add(new GripperConfig{Id=GetStr(g,"id",""),Type=GetStr(g,"type","steel"),Name=GetStr(g,"name",""),Number=GetStr(g,"number",""),SlotPhotoSignal=GetStr(g,"slotPhotoSignal",""),GrabTaskNo=GetStr(g,"grabTaskNo",""),PlaceTaskNo=GetStr(g,"placeTaskNo","")}); }
    }
}
