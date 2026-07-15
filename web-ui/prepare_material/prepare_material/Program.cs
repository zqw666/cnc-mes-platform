using DataCommonOperate;
using System;
using System.Collections.Generic;
using System.Data;
using PLC;
using MacCommunicate;
using System.Web.Script.Serialization;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.IO;
using System.Xml.Linq;

namespace prepare_material
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
        static readonly string StepName = "transfer";
        static readonly string DisplayName = "传输程序";
        static bool ProcessTermination = false;

        // 数据库
        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharSet=utf8;";
        static string DbType = "mysql";
        static CommonInterface Mysql;
        static Dictionary<int, macinterface> MacList = new Dictionary<int, macinterface>();
        static plc Plc;

        // PLC 配置
        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
        static int PlcDm = 100;
        static string TaskFinishAddr = "205.00";
        static string RobotEleAddr = "206.00";

        // ==================== 转台运动控制信号 ====================
        static readonly string SignalReturnZero = "120.00";
        static readonly string SignalDegree     = "110";
        static readonly string SignalAllowMove  = "108.00";
        static readonly string SignalStartMove  = "108.01";
        static readonly string SignalMoveDone   = "108.04";
        static readonly string SignalAtZero     = "108.05";
        static readonly int[] AngleList = { 90, 180, 270, 360 };

        // eman 数据库（SQL Server，生产数据）
        static string EmanConnStr = "";
        static bool EmanEnabled = false;

        // eman 生产数据查询语句（{0}=监控编号/RFID）
        static readonly string SqlSteelInfo     = "SELECT * FROM steel_part_info WHERE monitor_no = '{0}'";
        static readonly string SqlElectrodeInfo = "SELECT * FROM electrode_part_info WHERE monitor_no = '{0}'";

        // 随行台配置
        static bool PalletEnabled = true;      // 随行台有无

        // NC 服务器配置
        static string NcServerRoot = "";       // 服务器NC根目录，如 \\192.168.1.100\share
        static string NcLocalTemp  = "";       // 本地临时目录，如 D:\nc_temp
        static int ProcessCardNcLine = 3;      // 工艺卡中NC路径所在行号（1-based，旧方式）
        static string ProcessCardCell = "A1";  // Excel 工艺卡中NC路径单元格编号（新方式）

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

                    // eman 数据库配置
                    if (cfg.ContainsKey("eman"))
                    {
                        var eman = GetDict(cfg, "eman");
                        string ehost = GetStr(eman, "host", "172.16.128.243");
                        int eport = GetInt(eman, "port", 1433);
                        string edb = GetStr(eman, "database", "eman");
                        string euser = GetStr(eman, "user", "sa");
                        string epwd = GetStr(eman, "password", "19830617");
                        EmanConnStr = $"Server={ehost},{eport};Database={edb};User Id={euser};Password={epwd};";
                        EmanEnabled = true;
                        Log("INFO", "Eman数据库已配置: " + ehost);
                    }

                    // NC 服务器配置
                    if (cfg.ContainsKey("nc_server"))
                    {
                        var ncs = GetDict(cfg, "nc_server");
                        NcServerRoot = GetStr(ncs, "root_path", "");
                        NcLocalTemp = GetStr(ncs, "local_temp", "");
                        ProcessCardNcLine = GetInt(ncs, "process_card_nc_line", 3);
                        ProcessCardCell = GetStr(ncs, "process_card_nc_cell", "A1");
                        if (!string.IsNullOrEmpty(NcLocalTemp) && !Directory.Exists(NcLocalTemp))
                            Directory.CreateDirectory(NcLocalTemp);
                        Log("INFO", "NC服务器配置: root=" + NcServerRoot + " local=" + NcLocalTemp);
                    }

                    // PLC 配置
                    if (cfg.ContainsKey("plc"))
                    {
                        var plc = GetDict(cfg, "plc");
                        if (plc.ContainsKey("ip")) PlcIp = plc["ip"].ToString();
                        if (plc.ContainsKey("port")) PlcPort = Convert.ToInt32(plc["port"]);

                        if (plc.ContainsKey("addresses"))
                        {
                            var addrs = GetDict(plc, "addresses");
                            if (addrs.ContainsKey("task_finish")) TaskFinishAddr = addrs["task_finish"].ToString();
                            if (addrs.ContainsKey("robot_ele")) RobotEleAddr = addrs["robot_ele"].ToString();
                        }
                    }

                    // 加载夹爪配置 + 随行台
                    if (cfg.ContainsKey("line"))
                    {
                        var line = GetDict(cfg, "line");
                        GripperType = GetStr(line, "gripperType", "fixed-single");
                        IsFixedGripper = GripperType == "fixed-single" || GripperType == "fixed-double";
                        // 随行台有无：当值为 "no" 或 false 时为无
                        string palletStr = GetStr(line, "palletEnabled", "yes").ToLower();
                        PalletEnabled = !(palletStr == "no" || palletStr == "false" || palletStr == "0");
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

        /// <summary>根据料位号获取 RFID</summary>
        static string GetRackRfid(int pos)
        {
            try { using (DataTable dt = Mysql.ExeForDtl("SELECT rfid FROM rack_administrate WHERE id=" + pos))
                if (dt != null && dt.Rows.Count > 0) return SafeStr(dt.Rows[0]["rfid"]); }
            catch { }
            return "";
        }

        /// <summary>根据料位号获取料架类型</summary>
        static string GetRackTypeByPos(int pos)
        {
            try { using (DataTable dt = Mysql.ExeForDtl("SELECT racktype FROM rack_administrate WHERE id=" + pos))
                if (dt != null && dt.Rows.Count > 0)
                { int rt = SafeInt(dt.Rows[0]["racktype"]); return rt == 1 ? "electrode" : "steel"; } }
            catch { }
            return "steel";
        }


        static void Main(string[] args)
        {
            Log("INFO", "【" + DisplayName + "】分包启动");
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

                List<int> waitmaclist = Waitmaclist();

                if (waitmaclist.Count == 0)
                {
                    Log("INFO", "无待加工料位，备料流程结束");
                    return;
                }

                // 随行台判定
                List<int> followkonglist = new List<int>();
                if (PalletEnabled)
                {
                    followkonglist = FollowKongWeiList();
                    if (followkonglist.Count == 0)
                    {
                        Log("INFO", "有随行台但无空位，备料流程结束");
                        return;
                    }
                    Log("INFO", "随行台空位数: " + followkonglist.Count);
                }
                else
                {
                    Log("INFO", "未配置随行台，仅执行抓取→结束");
                }

                // 1. 执行置换夹爪流程
                if (!ChangeGripper())
                {
                    Log("ERROR", "置换夹爪失败，退出备料流程");
                    return;
                }

                if (ProcessTermination)
                    return;

                if (PalletEnabled)
                {
                    // ===== 有随行台：抓→放 =====
                    for (int i = 0; i < waitmaclist.Count; i++)
                    {
                        if (ProcessTermination) break;
                        for (int j = 0; j < followkonglist.Count; j++)
                        {
                            if (ProcessTermination) break;
                            List<string> message = GetFirstWaitProcessPieceMessage();
                            int rackpos = Convert.ToInt16(message[0]);
                            string workpiecename = message[1];
                            // NC 程序下载
                            string rfid = GetRackRfid(rackpos);
                            string rackType = GetRackTypeByPos(rackpos);
                            if (!string.IsNullOrEmpty(rfid) && !DownloadNCProgram(rfid, rackType))
                            {
                                Log("ERROR", "NC程序下载失败，备料流程终止！");
                                ProcessTermination = true;
                                break;
                            }
                            if (!ExecuteTask(rackpos))
                            {
                                Log("ERROR", "机器人执行" + rackpos + "料位抓取任务失败，备料流程终止！");
                                ProcessTermination = true;
                                break;
                            }
                            if (!Updaterobotmessage(workpiecename, 2))
                                Log("ERROR", "更新机器人夹爪信息失败！");
                            if (!Updaterack_administrate(rackpos, null, 1))
                                Log("ERROR", "更新料架信息失败！");

                            int followpos = followkonglist[j];
                            if (!ExecuteTask(followpos))
                            {
                                Log("ERROR", "机器人执行" + followpos + "料位放置任务失败，备料流程终止！");
                                ProcessTermination = true;
                                break;
                            }
                            if (!Updatefollowing_Rack(rackpos, workpiecename, 2))
                                Log("ERROR", "更新随行台料位信息失败！");
                            if (Updaterobotmessage(null, 1))
                                Log("ERROR", "更新机器人夹爪信息失败！");
                        }
                    }
                }
                else
                {
                    // ===== 无随行台：只抓取 → 结束 =====
                    List<string> message = GetFirstWaitProcessPieceMessage();
                    int rackpos = Convert.ToInt16(message[0]);
                    string workpiecename = message[1];
                    Log("INFO", "待加工料位: " + rackpos + " 工件: " + workpiecename);

                    // NC 程序下载
                    string rfid = GetRackRfid(rackpos);
                    string rackType = GetRackTypeByPos(rackpos);
                    if (!string.IsNullOrEmpty(rfid) && !DownloadNCProgram(rfid, rackType))
                    {
                        Log("ERROR", "NC程序下载失败，备料流程终止！");
                        return;
                    }

                    // 抓取
                    if (!ExecuteTask(rackpos))
                    {
                        Log("ERROR", "机器人执行" + rackpos + "料位抓取任务失败，备料流程终止！");
                        return;
                    }
                    Log("INFO", "机器人执行" + rackpos + "料位抓取任务成功！");

                    if (!Updaterobotmessage(workpiecename, 2))
                        Log("ERROR", "更新机器人夹爪信息失败！");
                    if (!Updaterack_administrate(rackpos, workpiecename, 1))
                        Log("ERROR", "更新料架" + rackpos + "料位信息失败！");

                    Log("SUCCESS", "备料流程完成（无随行台）");
                }
                Log("INFO", "【" + DisplayName + "】分包结束");
            }
            catch(Exception ex)
            { }
        }

        /// <summary>
        /// 执行任务
        /// </summary>
        /// <param name="tasknum">任务号</param>
        /// <returns></returns>
        public static bool ExecuteTask(int tasknum)
        {
            bool flag = true;
            if (!Plc.WriteDM(PlcDm, tasknum))
            {
                Log("ERROR", "发送任务失败，料位: " + tasknum);
                flag=false;
            }

            Log("INFO", "发送任务 " + tasknum + " 成功");

            // 等待任务完成
            int waitCount = 0;
            while (!Plc.ReadWR(TaskFinishAddr) && waitCount < 300)
            {
                Thread.Sleep(100);
                waitCount++;
            }

            if (!Plc.ReadWR(TaskFinishAddr))
            {
                Log("WARN", "等待任务完成超时，任务: " + tasknum);
                flag = false ;
            }
            Log("INFO", "执行任务 " + tasknum + " 成功");
            return flag;
        }

        /// <summary>
        /// 更新随行台指定料位信息
        /// </summary>
        /// <param name="pos">指定料位</param>
        /// <param name="workpiecename">工件名称</param>
        /// <param name="posstatus">料位状态</param>
        /// <returns></returns>
        static bool Updatefollowing_Rack(int pos,string workpiecename,int posstatus)
        {
            bool flag = false;
            try
            {
                string str = "update following_rack set workpiecename=" + workpiecename + ",posstatus=" + posstatus + " where id=" + pos + "";
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch(Exception ex)
            {

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
        static bool Updaterack_administrate(int pos,string workpiecename,int posstatus)
        {
            bool flag = false;
            try
            {
                string str = "update rack_administrate set workpiecename=" + workpiecename + ",posstatus=" + posstatus + " where id=" + pos + "";
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch(Exception ex)
            {

            }
            return flag;
        }

        /// <summary>
        /// 置换夹爪流程
        /// </summary>
        static bool ChangeGripper()
        {
            Log("INFO", "开始置换夹爪流程...目标类型: " + TargetGripperType);
            if (IsFixedGripper) { Log("INFO", "固定爪模式，无需置换"); return true; }
            string curName="",curType="",curWp=""; int curPos=0;
            try {
                using(DataTable dt=Mysql.ExeForDtl("SELECT clampname,clampstatus,clampposstatus,workpiecename FROM robotmessage LIMIT 1")){
                    if(dt!=null&&dt.Rows.Count>0){curName=SafeStr(dt.Rows[0]["clampname"]);curType=SafeStr(dt.Rows[0]["clampstatus"]);curPos=SafeInt(dt.Rows[0]["clampposstatus"]);curWp=SafeStr(dt.Rows[0]["workpiecename"]);}
                }
            }
            catch(Exception ex)
            {
                Log("ERROR","读取robotmessage失败:"+ex.Message);
                return false;
            }
            if(curPos==0){Log("INFO","手上无夹爪，直接抓取目标");return PickTargetGripper();}
            if(IsTargetGrip(curType)){Log("INFO","手上已为目标夹爪:"+curName);return true;}
            Log("INFO","手上有夹爪("+curName+")，先清空...");
            if(!string.IsNullOrEmpty(curWp)&&curWp!="NULL"){
                int s=GetEmptyRackSlot();if(s==0){Log("ERROR","无料架空位");return false;}
                Plc.WriteDM(PlcDm,s);if(!WaitTask(300)){Log("ERROR","放工件超时");return false;}
                try{Mysql.ExecuteSql("UPDATE rack_administrate SET rfid='"+curWp+"',scanstatus=1,posstatus=2 WHERE id="+s);}catch{}
                ClearRobotWp();
            }
            if(!ClearCurGripper(curType))return false;
            return PickTargetGripper();
        }
        static bool PickTargetGripper(){return PickGripper(TargetGripperType);}
        static bool ClearCurGripper(string t){var g=FindGrip(t);if(g==null)return true;if(!string.IsNullOrEmpty(g.SlotPhotoSignal)&&Plc.ReadWR(g.SlotPhotoSignal)){Log("ERROR","夹爪料位有异物");return false;}if(!string.IsNullOrEmpty(g.PlaceTaskNo)){int tn=ParseTaskNo(g.PlaceTaskNo)+(string.IsNullOrEmpty(g.Number)?0:int.Parse(g.Number));Plc.WriteDM(PlcDm,tn);if(!WaitTask(300)){Log("ERROR","放夹爪超时");return false;}}ClearRobotClamp();return true;}
        static bool PickGripper(string t){var g=FindGrip(t);if(g==null){Log("ERROR","未找到目标夹爪");return false;}if(!string.IsNullOrEmpty(g.GrabTaskNo)){int tn=ParseTaskNo(g.GrabTaskNo)+(string.IsNullOrEmpty(g.Number)?0:int.Parse(g.Number));Plc.WriteDM(PlcDm,tn);if(!WaitTask(300)){Log("ERROR","抓夹爪超时");return false;}}try{Mysql.ExecuteSql("UPDATE robotmessage SET clampname='"+g.Name+"',clampstatus="+(t=="steel"?1:t=="tool"?2:t=="electrode"?3:4)+",clampposstatus=1,workpiecename=NULL,workpiecestatus=0");}catch{}Log("SUCCESS","已抓取:"+g.Name);return true;}
        static bool IsTargetGrip(string t)=>string.Equals(t,TargetGripperType,StringComparison.OrdinalIgnoreCase)||(t=="1"&&TargetGripperType=="steel")||(t=="2"&&TargetGripperType=="tool")||(t=="3"&&TargetGripperType=="electrode")||(t=="4"&&TargetGripperType=="pallet");
        static GripperConfig FindGrip(string t){foreach(var g in Grippers)if(string.Equals(g.Type,t,StringComparison.OrdinalIgnoreCase)||(t=="1"&&g.Type=="steel")||(t=="2"&&g.Type=="tool")||(t=="3"&&g.Type=="electrode")||(t=="4"&&g.Type=="pallet"))return g;return Grippers.Count>0?Grippers[0]:null;}

        #region RobotMessage表更新方法

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
                string str = "update robotmessage set workpiecename=" + workpiecename + ",workpiecestatus=" + workpiecestatus + " where id=1";
                if (Mysql.ExecuteSql(str))
                    flag = true;
            }
            catch (Exception ex)
            {

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

        // ==================== NC 程序下载 ====================

        /// <summary>
        /// 根据 RFID 下载 NC 程序（钢件/电极分流）
        /// </summary>
        /// <param name="rfid">监控编号</param>
        /// <param name="rackType">料架类型: steel/electrode</param>
        /// <returns>true=下载成功, false=下载失败</returns>
        static bool DownloadNCProgram(string rfid, string rackType)
        {
            if (!EmanEnabled || string.IsNullOrEmpty(EmanConnStr))
            {
                Log("ERROR", "Eman数据库未配置，无法下载NC程序");
                return false;
            }
            if (string.IsNullOrEmpty(NcServerRoot))
            {
                Log("ERROR", "NC服务器根目录未配置");
                return false;
            }

            var emanData = QueryEmanProduction(rfid, rackType);
            if (emanData == null)
            {
                Log("ERROR", "从Eman获取生产数据失败，无法下载NC程序");
                return false;
            }

            if (rackType == "electrode")
                return DownloadNCForElectrode(rfid, emanData);
            else if (rackType == "steel")
                return DownloadNCForSteel(rfid, emanData);

            Log("WARN", "未知料架类型: " + rackType + "，跳过NC下载");
            return true;
        }

        /// <summary>
        /// 电极 NC 下载：从 eman 获取生产信息 → 拼接 NC 路径 → 下载
        /// 路径规则: \\服务器根目录\工装编号\模号\电极名Part1\Part2\small\Part3\电极名称.nc
        /// 电极名称按 '-' 分割
        /// </summary>
        static bool DownloadNCForElectrode(string rfid, Dictionary<string, string> emanData)
        {
            try
            {
                // 读取 eman 数据中的关键字段
                string toolingNo  = GetEmanField(emanData, "tooling_no");    // 工装编号
                string moldNo     = GetEmanField(emanData, "mold_no");       // 模号
                string elecName   = GetEmanField(emanData, "electrode_name"); // 电极名称

                if (string.IsNullOrEmpty(elecName))
                {
                    Log("ERROR", "Eman数据中缺少电极名称");
                    return false;
                }

                // 电极名称按 '-' 分割为 3 部分
                string[] parts = elecName.Split('-');
                if (parts.Length < 3)
                {
                    Log("ERROR", "电极名称格式异常(需3段'-'分隔): " + elecName);
                    return false;
                }
                string part1 = parts[0];
                string part2 = parts[1];
                string part3 = parts[2];

                // 拼接远程 NC 路径
                string remoteNcPath = Path.Combine(NcServerRoot, toolingNo, moldNo,
                    part1, part2, "small", part3, elecName + ".nc");
                Log("INFO", "【电极NC】远程路径: " + remoteNcPath);

                return DownloadFile(remoteNcPath, elecName + ".nc");
            }
            catch (Exception ex)
            {
                Log("ERROR", "电极NC下载异常: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 钢件 NC 下载：从 eman 获取工艺卡路径 → 下载工艺卡 → 读 NC 行 → 下载 NC
        /// </summary>
        static bool DownloadNCForSteel(string rfid, Dictionary<string, string> emanData)
        {
            try
            {
                // 读取 eman 数据中的工艺卡路径
                string cardPath = GetEmanField(emanData, "process_card_path");
                if (string.IsNullOrEmpty(cardPath))
                {
                    Log("ERROR", "Eman数据中缺少工艺卡路径");
                    return false;
                }

                // 1. 下载工艺卡到本地
                string cardFileName = Path.GetFileName(cardPath);
                if (string.IsNullOrEmpty(cardFileName))
                    cardFileName = "steel_card_" + rfid + ".txt";

                Log("INFO", "【钢件NC】下载工艺卡: " + cardPath);
                if (!DownloadFile(cardPath, cardFileName))
                {
                    Log("ERROR", "下载工艺卡失败: " + cardPath);
                    return false;
                }

                // 2. 读取 Excel 工艺卡指定单元格获取 NC 路径
                string localCardPath = Path.Combine(NcLocalTemp, cardFileName);
                string ncPathLine = ReadExcelCell(localCardPath, ProcessCardCell);
                if (string.IsNullOrEmpty(ncPathLine))
                {
                    Log("ERROR", "工艺卡中未找到NC路径（单元格: " + ProcessCardCell + "）");
                    return false;
                }
                Log("INFO", "【钢件NC】工艺卡NC路径: " + ncPathLine);

                // 3. 下载 NC 程序
                string ncFileName = Path.GetFileName(ncPathLine);
                if (string.IsNullOrEmpty(ncFileName))
                    ncFileName = "steel_" + rfid + ".nc";

                Log("INFO", "【钢件NC】下载NC程序: " + ncPathLine);
                if (!DownloadFile(ncPathLine, ncFileName))
                {
                    Log("ERROR", "下载NC程序失败: " + ncPathLine);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log("ERROR", "钢件NC下载异常: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 从 eman 获取的字典中安全取值
        /// </summary>
        static string GetEmanField(Dictionary<string, string> data, string key)
        {
            if (data != null && data.ContainsKey(key))
                return data[key] ?? "";
            return "";
        }

        /// <summary>
        /// 从 eman 数据库获取生产数据（与 scan 一致）
        /// </summary>
        static Dictionary<string, string> QueryEmanProduction(string rfid, string rackType)
        {
            if (!EmanEnabled || string.IsNullOrEmpty(EmanConnStr))
                return null;
            try
            {
                var emanDb = CommonFactory.CreateInstance(CommonData.sql, EmanConnStr);
                string sql;
                if (rackType == "steel")
                    sql = string.Format(SqlSteelInfo, rfid);
                else if (rackType == "electrode")
                    sql = string.Format(SqlElectrodeInfo, rfid);
                else
                    return null;

                Log("INFO", "查询Eman(" + rackType + "): " + rfid);
                using (DataTable dt = emanDb.ExeForDtl(sql))
                {
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        var result = new Dictionary<string, string>();
                        foreach (DataColumn col in dt.Columns)
                            result[col.ColumnName] = SafeStr(dt.Rows[0][col.ColumnName]);
                        return result;
                    }
                }
                Log("WARN", "Eman未找到记录: " + rfid);
            }
            catch (Exception ex) { Log("ERROR", "Eman查询失败: " + ex.Message); }
            return null;
        }

        /// <summary>
        /// 从远程路径下载文件到本地临时目录
        /// </summary>
        static bool DownloadFile(string remotePath, string fileName)
        {
            try
            {
                if (!File.Exists(remotePath))
                {
                    Log("ERROR", "远程文件不存在: " + remotePath);
                    return false;
                }

                string localPath = Path.Combine(NcLocalTemp, fileName);
                File.Copy(remotePath, localPath, true);
                Log("SUCCESS", "文件下载成功: " + localPath);
                return File.Exists(localPath);
            }
            catch (Exception ex)
            {
                Log("ERROR", "文件下载失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 读取 Excel(.xlsx) 指定单元格内容
        /// 基于 Open XML 格式解析，无需外部依赖
        /// </summary>
        /// <param name="filePath">.xlsx 文件路径</param>
        /// <param name="cellRef">单元格编号，如 "A1"、"B5"、"C10"</param>
        /// <returns>单元格文本内容，失败返回空字符串</returns>
        static string ReadExcelCell(string filePath, string cellRef)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Log("ERROR", "Excel文件不存在: " + filePath);
                    return "";
                }

                // 解析单元格编号
                string colLetters = "", rowStr = "";
                foreach (char c in cellRef.ToUpper())
                {
                    if (c >= 'A' && c <= 'Z') colLetters += c;
                    else rowStr += c;
                }
                int row = int.Parse(rowStr);

                using (var zip = ZipFile.OpenRead(filePath))
                {
                    // 读取共享字符串表
                    var sstEntry = zip.GetEntry("xl/sharedStrings.xml");
                    List<string> sharedStrings = new List<string>();
                    if (sstEntry != null)
                    {
                        using (var s = sstEntry.Open())
                        {
                            var sstDoc = XDocument.Load(s);
                            sharedStrings = sstDoc.Descendants()
                                .Where(e => e.Name.LocalName == "t")
                                .Select(e => e.Value).ToList();
                        }
                    }

                    // 读取 sheet1
                    var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
                    if (sheetEntry == null) return "";
                    using (var s = sheetEntry.Open())
                    {
                        var sheetDoc = XDocument.Load(s);
                        var ns = sheetDoc.Root.Name.Namespace;
                        foreach (var rowElem in sheetDoc.Descendants(ns + "row"))
                        {
                            if ((string)rowElem.Attribute("r") != row.ToString()) continue;
                            var cell = rowElem.Elements(ns + "c")
                                .FirstOrDefault(c => (string)c.Attribute("r") == cellRef.ToUpper());
                            if (cell == null) continue;
                            var valElem = cell.Element(ns + "v");
                            if (valElem == null) return "";
                            string val = valElem.Value;
                            string type = (string)cell.Attribute("t");
                            if (type == "s" && int.TryParse(val, out int idx) && idx < sharedStrings.Count)
                                return sharedStrings[idx];
                            return val;
                        }
                    }
                }
                return "";
            }
            catch (Exception ex)
            {
                Log("ERROR", "读取Excel单元格失败(" + cellRef + "): " + ex.Message);
                return "";
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

        /// <summary>
        ///获取料架第一个待加工工件信息 
        /// </summary>
        /// <returns></returns>
        public static List<string> GetFirstWaitProcessPieceMessage()
        {
            List<string> message = new List<string>();
            try
            {
                string str = "select * from rack_administrate where workstate=0 and posstatus=2 order by id desc";
                DataTable dt = Mysql.ExeForDtl(str);
                foreach(DataRow r in dt.Rows)
                {
                    message.Add(r["id"].ToString());
                    message.Add(r["workpiecename"].ToString());
                }
            }
            catch(Exception ex)
            {

            }
            return message;
        }

        /// <summary>
        /// 获取随行台空位列表
        /// </summary>
        /// <returns></returns>
        public static List<int>FollowKongWeiList()
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
            catch(Exception ex)
            {

            }
            return kongweilist;
        }

        /// <summary>
        /// 获取待机机床列表
        /// </summary>
        /// <returns></returns>
        public static List<int>Waitmaclist()
        {
            List<int> mac = new List<int>();
            try
            {
                string str = "select * from macmessage where status!=0";
                DataTable dt = Mysql.ExeForDtl(str);
                foreach(DataRow r in dt.Rows)
                {
                    string status = GetMacStatusSafe(Convert.ToInt16(r["id"].ToString()));
                    string workpiecename = r["workpiecename"].ToString();
                    string workpieceworkstate = r["workpieceworkstate"].ToString();
                    if (status == "reset"&&workpiecename==""&&workpieceworkstate=="0")
                        mac.Add(Convert.ToInt16(r["id"].ToString()));
                }
            }
            catch(Exception ex)
            {

            }
            return mac;
        }
        static string SafeStr(object val)=>val==null||val==DBNull.Value?"":val.ToString();
        static int SafeInt(object val)=>val==null||val==DBNull.Value?0:Convert.ToInt32(val);
        static bool WaitTask(int max){int w=0;while(w<max){if(Plc.ReadWR(TaskFinishAddr))return true;Thread.Sleep(100);w++;}return Plc.ReadWR(TaskFinishAddr);}
        static void ClearRobotWp(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0");}catch{}}
        static void ClearRobotClamp(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0");}catch{}}
        static int GetEmptyRackSlot(){try{using(DataTable dt=Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0 LIMIT 1")){if(dt!=null&&dt.Rows.Count>0)return Convert.ToInt32(dt.Rows[0]["id"]);}}catch{}return 0;}
        static int ParseTaskNo(string s){if(string.IsNullOrEmpty(s))return 0;int.TryParse(s.ToUpper().Replace("D","").Replace("DM","").Trim(),out int r);return r;}
        static void AddGripper(object item){var g=item as Dictionary<string,object>;if(g==null)return;Grippers.Add(new GripperConfig{Id=GetStr(g,"id",""),Type=GetStr(g,"type","steel"),Name=GetStr(g,"name",""),Number=GetStr(g,"number",""),SlotPhotoSignal=GetStr(g,"slotPhotoSignal",""),GrabTaskNo=GetStr(g,"grabTaskNo",""),PlaceTaskNo=GetStr(g,"placeTaskNo","")});}

        // ==================== 转台运动控制 ====================
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
    }
}
