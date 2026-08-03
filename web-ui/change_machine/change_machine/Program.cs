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
            return Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).FullName, "config.json");
        }
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "change_machine";
        static readonly string DisplayName = "换料程序";

        // 数据库
        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharSet=utf8;";
        static string DbType = "mysql";
        static CommonInterface Mysql;
        static Dictionary<int, macinterface> MacList = new Dictionary<int, macinterface>();
        static transfer transferprogram = new transfer();
        static bool ProcessTermination = false;
        static plc Plc;

        // 夹爪配置
        static string GripperType = "fixed-single";
        static bool IsFixedGripper = true;
        static string TargetGripperType = "steel";
        static List<GripperConfig> Grippers = new List<GripperConfig>();
        class GripperConfig {
            public string Id, Type, Name, Number;
            public string SlotPhotoSignal, GrabTaskNo, PlaceTaskNo;
        }

        // ==================== 从总包配置文件中读取 ====================
        static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));

                    // 数据库配置
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
                            ConnStr = $"Server={host};Database={dbname};Uid={user};Pwd={pwd};CharSet=utf8;";
                        }
                    }

                    // PLC 配置
                    if (cfg.ContainsKey("plc"))
                    {
                        var plc = GetDict(cfg, "plc");
                        if (plc.ContainsKey("ip")) PlcIp = plc["ip"].ToString();
                        if (plc.ContainsKey("port")) PlcPort = Convert.ToInt32(plc["port"]);

                    // 优先从 signals.robot 读取
                    var signals = GetDict(plc, "signals");
                    var robotSig = GetDict(signals, "robot");
                    if (robotSig.ContainsKey("taskStart"))
                        TaskStartAddr = GetStr(robotSig, "taskStart", "");
                    if (robotSig.ContainsKey("taskDone"))
                        TaskFinishAddr = GetStr(robotSig, "taskDone", TaskFinishAddr);

                    // 兜底：旧格式 addresses
                    var addrs = GetDict(plc, "addresses");
                    if (string.IsNullOrEmpty(TaskStartAddr) && addrs.ContainsKey("task_start"))
                        TaskStartAddr = addrs["task_start"].ToString();
                    if (string.IsNullOrEmpty(TaskFinishAddr) && addrs.ContainsKey("task_finish"))
                        TaskFinishAddr = addrs["task_finish"].ToString();
                    if (addrs.ContainsKey("robot_ele")) RobotEleAddr = addrs["robot_ele"].ToString();
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
                            foreach (var item in al) AddGripper(item);
                        else if (glist is object[] arr)
                            foreach (var item in arr) AddGripper(item);
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


        // PLC 配置
        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
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
            Log("INFO", "开始执行" + DisplayName);
            try
            {
                LoadConfig();
                InitMachines();
                Mysql = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                Plc = new plc();
                if (!Plc.Connect(PlcIp, PlcPort))
                {
                    Log("ERROR", "PLC 连接失败: " + PlcIp);
                    return;
                }
                Log("INFO", "PLC 连接成功");

                // 执行置换夹爪流程
                if (!ChangeGripper())
                {
                    Log("ERROR", "置换夹爪失败，退出换料流程");
                    return;
                }

                // 执行换料主流程
                RunChangePiece();
            }
            catch (Exception ex)
            {
                Log("ERROR", "执行异常: " + ex.Message);
            }
            finally
            {
                Log("INFO", "结束执行" + DisplayName);
            }
        }

        /// <summary>
        /// 换料主流程：钢件 + 无随行台
        /// 固定双爪：一次任务完成取旧放新
        /// 快换爪+多次旋转：取料→转台旋转→放回→加工
        /// </summary>
        static void RunChangePiece()
        {
            var waitmaclist = Waitmaclist();
            if (waitmaclist.Count == 0)
            {
                Log("INFO", "无待机且有加工完成工件的机床");
                return;
            }

            foreach (var macinfo in waitmaclist)
            {
                if (ProcessTermination) break;

                int macnum = Convert.ToInt16(macinfo.Split(',')[0]);
                string ip = macinfo.Split(',')[1];
                string workpiecename = macinfo.Split(',')[2];
                int workstate = Convert.ToInt16(macinfo.Split(',')[3]);
                bool isRoders = IsMachineRoders(macnum);

                if (string.IsNullOrEmpty(workpiecename) || workstate != 1)
                {
                    Log("INFO", "机床" + macnum + " 无加工完成工件，跳过");
                    continue;
                }

                Log("INFO", "开始处理机床" + macnum + "，工件: " + workpiecename);

                // Roders: 连接并开门
                if (isRoders && !OpenRodersDoor(macnum))
                {
                    ProcessTermination = true;
                    break;
                }

                // 固定双爪：一次任务完成取旧放新
                if (IsFixedGripper && GripperType == "fixed-double")
                {
                    if (!ExecuteTask(macnum))
                    {
                        Log("ERROR", "机器人执行换料任务失败(取旧+放新)，机床: " + macnum);
                        ProcessTermination = true;
                        break;
                    }
                    Log("INFO", "换料任务完成: 取旧(" + workpiecename + ") + 放新，机床: " + macnum);

                    // 取旧爪 → 有工件(已完成)
                    // 放新爪 → 空
                    // 机床工位 → 有新工件(待加工)
                    UpdateMachinePieceOnChangePair(macnum, workpiecename);
                }
                // 快换爪 + 多次旋转加工
                else if (!IsFixedGripper)
                {
                    // 1. 从机床取料
                    if (!ExecuteTask(macnum))
                    {
                        Log("ERROR", "机器人从机床" + macnum + "取料失败");
                        ProcessTermination = true;
                        break;
                    }
                    Log("INFO", "从机床" + macnum + "取料完成: " + workpiecename);
                    Updaterobotmessage(workpiecename, 2, 2);

                    // 2. 放置到旋转台
                    int turntableTask = macnum; // 旋转台任务号(与机床号对应)
                    if (!ExecuteTask(turntableTask + 100))
                    {
                        Log("ERROR", "放置工件到旋转台失败");
                        ProcessTermination = true;
                        break;
                    }
                    Updaterobotmessage(null, 0, 1);

                    // 3. 旋转台旋转(从加工卡读取角度 + 调用 rackoperate 的 TurntableReturnZero/RunTurntable)
                    TurntableReturnZero();
                    int angle = ReadRotationAngle(workpiecename);
                    if (angle > 0)
                    {
                        if (!RunTurntable(angle))
                        {
                            Log("ERROR", "旋转台旋转 " + angle + " 度失败");
                            ProcessTermination = true;
                            break;
                        }
                    }

                    // 4. 从旋转台取料
                    if (!ExecuteTask(turntableTask + 101))
                    {
                        Log("ERROR", "从旋转台取料失败");
                        ProcessTermination = true;
                        break;
                    }
                    Updaterobotmessage(workpiecename, 2, 2);

                    // 5. 放回机床
                    if (!ExecuteTask(macnum))
                    {
                        Log("ERROR", "放置工件回机床" + macnum + "失败");
                        ProcessTermination = true;
                        break;
                    }
                    Updaterobotmessage(null, 0, 1);
                    UpdateMacMessage(macnum, workpiecename, 0);
                }

                // Roders: 关门
                if (isRoders) MacCloseDoor(macnum);

                // 传输NC程序 + 启动加工
                string nextPgm = GetNextProgramName(workpiecename);
                if (!transferprogram.RunTransferProcess(ip, nextPgm))
                {
                    Log("WARN", "传输程序" + nextPgm + "到机床" + macnum + "失败");
                }
                if (!MacStartProcessSafe(macnum))
                {
                    Log("ERROR", "启动机床" + macnum + "加工失败");
                    ProcessTermination = true;
                    break;
                }

                // 更新状态为加工中
                UpdateMacMessage(macnum, workpiecename, 1);
                Updaterack_administrate(workpiecename, 1);
                Log("SUCCESS", "机床" + macnum + " 换料完成，工件" + workpiecename + "加工中");
            }
        }

        /// <summary>
        /// 固定双爪换料：取旧爪有工件(完成)，放新爪为空，机床有新工件(待加工)
        /// </summary>
        static void UpdateMachinePieceOnChangePair(int macnum, string oldPieceName)
        {
            // 取旧爪 → 有工件，加工完成
            Updaterobotmessage(oldPieceName, 2, 2);
            // 机床工位 → 清空
            UpdateMacMessage(macnum, null, 0);
            // 从料架获取下一个待加工工件名称
            string newPiece = GetNextWorkpieceForMachine(macnum);
            if (!string.IsNullOrEmpty(newPiece))
            {
                UpdateMacMessage(macnum, newPiece, 0);
                Updaterobotmessage(null, 0, 1);
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
            Log("INFO", "机床" + macId + " 为 Roders，准备连接并开门");
            if (!MacList[macId].ConnectMac())
            {
                Log("ERROR", "机床" + macId + " Roders 连接失败");
                return false;
            }
            string status = MacList[macId].GetMacStatus();
            int wait = 0;
            while (status != "reset" && wait < 300)
            {
                Thread.Sleep(100);
                status = MacList[macId].GetMacStatus();
                wait++;
            }
            if (status != "reset") { Log("ERROR", "机床" + macId + " 非空闲状态"); return false; }
            return MacList[macId].OpenDoor(macId);
        }

        /// <summary>
        /// 获取工件下一步加工的程序名称
        /// </summary>
        static string GetNextProgramName(string workpiecename)
        {
            try
            {
                string str = "select programname from rack_administrate where workpiecename='" + workpiecename + "' limit 1";
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
                string str = "select workpiecename from macworkposstatus where machinename='" + macnum + "' and posstatus=0 limit 1";
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
                if (!File.Exists(filePath)) { Log("WARN", "加工卡不存在: " + filePath); return 0; }
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
                Log("INFO", "复位任务开始信号: " + TaskStartAddr);
            }

            // 2. 写入任务号
            if (!Plc.WriteDM(PlcDm, tasknum))
            {
                Log("ERROR", "发送任务失败，任务: " + tasknum);
                return false;
            }
            Log("INFO", "写入任务号 " + tasknum + " 至 DM" + PlcDm);

            // 3. 发送任务开始信号
            if (!string.IsNullOrEmpty(TaskStartAddr))
            {
                Plc.WriteWR(TaskStartAddr, 1);
                Log("INFO", "发送任务开始信号: " + TaskStartAddr);
            }

            // 4. 等待任务完成
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
            Log("SUCCESS", "执行任务 " + tasknum + " 完成");
            return true;
        }

        /// <summary>
        /// 更新机床信息
        /// </summary>
        /// <param name="macnum">机床编号</param>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="workpieceworkstate">工件加工状态</param>
        /// <returns></returns>
        static bool UpdateMacMessage(int macnum, string workpiecename, int workpieceworkstate)
        {
            bool flag = false;
            try
            {
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update macmessage set workpiecename =" + workpieceStr + ",workpieceworkstate=" + workpieceworkstate + " where id =" + macnum;
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                Log("ERROR", $"更新机床信息异常: {ex.Message}");
            }
            return flag;
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
                    string workpiecename = r["workpiecename"].ToString();
                    string workpieceworkstate = r["workpieceworkstate"].ToString();
                    if (status == "reset")
                        macmessage.Add(r["id"].ToString() + "," + ip + "," + workpiecename + "," + workpieceworkstate);
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
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update rack_administrate set workstate=" + workstate + " where workpiecename=" + workpieceStr;
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                Log("ERROR", "更新料架信息异常: " + ex.Message);
            }
            return flag;
        }

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

        // ==================== 转台运动控制（来自 rackoperate） ====================
        /// <summary>
        /// 转台单次运动控制：旋转到指定角度
        /// </summary>
        static bool RunTurntable(int angle)
        {
            Log("INFO", "【转台】移动至 " + angle + " 度");
            Plc.WriteWr(SignalDegree, (ushort)angle);
            Plc.WriteWR(SignalAllowMove, 1);
            Plc.WriteWR(SignalStartMove, 1);

            Log("INFO", "【转台】等待到位信号 " + SignalMoveDone);
            while (!Plc.ReadWR(SignalMoveDone))
                Thread.Sleep(200);

            Plc.WriteWR(SignalAllowMove, 0);
            Plc.WriteWR(SignalStartMove, 0);
            Log("SUCCESS", "【转台】" + angle + " 度移动完成");
            return true;
        }

        /// <summary>
        /// 转台回零
        /// </summary>
        static bool TurntableReturnZero()
        {
            Log("INFO", "【转台】回零开始: " + SignalReturnZero);
            Plc.WriteWR(SignalReturnZero, 1);

            Log("INFO", "【转台】等待零点信号 " + SignalAtZero);
            while (!Plc.ReadWR(SignalAtZero))
                Thread.Sleep(200);

            Plc.WriteWR(SignalReturnZero, 0);
            Log("SUCCESS", "【转台】已回到零点");
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
        /// <param name="workpieceStatus">工件状态（0：无工件、1：有工件）</param>
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

                if (Mysql.ExecuteSql(sql))
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
                string str = "select workpiecename from robotmessage where id=1 and workpiecestatus=0";
                piecemessage = Mysql.GetValue(str).ToString();
            }
            catch (Exception ex)
            {

            }
            return piecemessage;
        }

        /// <summary>
        /// 更新机器人手爪上的工件信息和料位状态
        /// </summary>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="workpiecestatus">夹爪上工件料位状态</param>
        /// <returns></returns>
        static bool Updaterobotmessage(string workpiecename,int workstate, int workpiecestatus)
        {
            bool flag = false;
            try
            {
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update robotmessage set workpiecename=" + workpieceStr + ",workpieceworkstate=" + workstate + ",workpiecestatus=" + workpiecestatus + " where id=1";
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {
                Log("ERROR", "更新机器人夹爪信息异常: " + ex.Message);
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

                if (Mysql.ExecuteSql(sql))
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

                if (Mysql.ExecuteSql(sql))
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
        /// 机床通讯配置
        /// </summary>
        static Dictionary<int, Dictionary<string, string>> MachineConfigs = new Dictionary<int, Dictionary<string, string>>();

        /// <summary>
        /// 初始化机床通讯实例
        /// </summary>
        static void InitMachines()
        {
            try
            {
                MachineConfigs.Clear();
                if (!File.Exists(ConfigPath)) { Log("WARN", "配置文件不存在: " + ConfigPath); return; }
                var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));
                if (!cfg.ContainsKey("machines")) { Log("WARN", "配置文件中没有 machines 节点"); return; }
                var machines = Js.Deserialize<Dictionary<string, object>>(cfg["machines"].ToString());
                foreach (var kv in machines)
                {
                    int macId = Convert.ToInt32(kv.Key);
                    var m = Js.Deserialize<Dictionary<string, object>>(kv.Value.ToString());
                    string typeStr = m.ContainsKey("type") ? m["type"].ToString().ToLower() : "roders";
                    string ip = m.ContainsKey("ip") ? m["ip"].ToString() : "";
                    MachineConfigs[macId] = new Dictionary<string, string>() { { "type", typeStr }, { "ip", ip } };
                    MacCommunicate.Macfactory.MacType macType;
                    switch (typeStr)
                    {
                        case "fanuc": macType = MacCommunicate.Macfactory.MacType.fanuc; break;
                        case "siemense": macType = MacCommunicate.Macfactory.MacType.siemense; break;
                        case "makino": macType = MacCommunicate.Macfactory.MacType.makino; break;
                        default: macType = MacCommunicate.Macfactory.MacType.roders; break;
                    }
                    var mac = MacCommunicate.Macfactory.MacFactory.CreateInstance(macType, ip);
                    if (mac != null) { MacList[macId] = mac; Log("INFO", $"机床 {macId} ({typeStr}) 已创建，IP: {ip}"); }
                    else { Log("ERROR", $"机床 {macId} 创建实例失败"); }
                }
                Log("INFO", $"机床通讯初始化完成，共 {MacList.Count} 台");
            }
            catch (Exception ex) { Log("ERROR", "InitMachines 异常: " + ex.Message); }
        }

        /// <summary>
        /// 安全获取机床状态
        /// </summary>
        static string GetMacStatusSafe(int macId)
        {
            try { if (MacList.ContainsKey(macId) && MacList[macId] != null) return MacList[macId].GetMacStatus(); }
            catch (Exception ex) { Log("ERROR", $"获取机床 {macId} 状态异常: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// 安全启动机床加工
        /// </summary>
        static bool MacStartProcessSafe(int macId)
        {
            try { if (MacList.ContainsKey(macId) && MacList[macId] != null) return MacList[macId].MacStartProcess(macId); }
            catch (Exception ex) { Log("ERROR", $"启动机床 {macId} 加工异常: {ex.Message}"); }
            return false;
        }

        /// <summary>
        /// 机床开门
        /// </summary>
        static void MacOpenDoor(int macId)
        {
            try { if (MacList.ContainsKey(macId) && MacList[macId] != null) MacList[macId].OpenDoor(macId); }
            catch (Exception ex) { Log("ERROR", $"机床 {macId} 开门异常: {ex.Message}"); }
        }

        /// <summary>
        /// 机床关门
        /// </summary>
        static void MacCloseDoor(int macId)
        {
            try { if (MacList.ContainsKey(macId) && MacList[macId] != null) MacList[macId].closeDoor(macId); }
            catch (Exception ex) { Log("ERROR", $"机床 {macId} 关门异常: {ex.Message}"); }
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
        static string SafeStr(object val)=>val==null||val==DBNull.Value?"":val.ToString();
        static int SafeInt(object val)=>val==null||val==DBNull.Value?0:Convert.ToInt32(val);
        static void SendTaskStart() { if (!string.IsNullOrEmpty(TaskStartAddr)) Plc.WriteWR(TaskStartAddr, 1); }
        static bool WaitForTaskFinish(int maxTicks) { int w = 0; while (w < maxTicks) { if (Plc.ReadWR(TaskFinishAddr)) return true; Thread.Sleep(100); w++; } return Plc.ReadWR(TaskFinishAddr); }
        static void ClearRobotWorkpiece() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0"); } catch { } }
        static void ClearRobotClamp() { try { Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0"); } catch { } }
        static int GetEmptyRackSlot() { try { using (DataTable dt = Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0 LIMIT 1")) { if (dt != null && dt.Rows.Count > 0) return Convert.ToInt32(dt.Rows[0]["id"]); } } catch { } return 0; }
        static int ParseTaskNoBase(string s) { if (string.IsNullOrEmpty(s)) return 0; int.TryParse(s.ToUpper().Replace("D", "").Replace("DM", "").Trim(), out int r); return r; }
        static void AddGripper(object item){var g=item as Dictionary<string,object>;if(g==null)return;Grippers.Add(new GripperConfig{Id=GetStr(g,"id",""),Type=GetStr(g,"type","steel"),Name=GetStr(g,"name",""),Number=GetStr(g,"number",""),SlotPhotoSignal=GetStr(g,"slotPhotoSignal",""),GrabTaskNo=GetStr(g,"grabTaskNo",""),PlaceTaskNo=GetStr(g,"placeTaskNo","")});}
    }
}
