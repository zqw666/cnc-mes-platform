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
        // 配置
        static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "backend", "config.json");
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

                        if (plc.ContainsKey("addresses"))
                        {
                            var addrs = GetDict(cfg, "addresses");
                            if (addrs.ContainsKey("task_finish")) TaskFinishAddr = addrs["task_finish"].ToString();
                            if (addrs.ContainsKey("robot_ele")) RobotEleAddr = addrs["robot_ele"].ToString();
                        }
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
        static int PlcDm = 100;              // 任务号 DM 地址
        static string TaskFinishAddr = "205.00";  // 任务完成信号
        static string RobotEleAddr = "206.00";    // 机器人光电信号
        static string GripperTypeAddr = "300";    // 夹爪类型 DM 地址 (0=固定爪, 1=快换爪)
        static string FixturePosAddr = "301";     // 夹爪放置位状态 DM 地址
        static string GripperHasPartAddr = "206.00"; // 夹爪上有料信号
        static string GripperEmptyAddr = "207.00";   // 夹爪清空信号
        static string ChangeGripperTaskAddr = "208.00"; // 置换夹爪任务信号
        static int ModeChoise = 0;
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

                //区分单夹爪和双夹爪流程
               switch(ModeChoise)
                {
                    case 1:
                        OneClampChangPiece();
                        break;
                    case 2:
                        TwoClampChangPiece();
                        break;
                }
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
        /// 双夹爪换料流程
        /// </summary>
        public static void TwoClampChangPiece()
        {

        }

        /// <summary>
        /// 单夹爪换料流程
        /// </summary>
        public static void OneClampChangPiece()
        {
            //区分单夹爪和双夹爪流程
            List<string> waitmaclist = Waitmaclist();
            List<int> followkongweilist = FollowKongWeiList();
            List<string> followpiecelist = FollowHavePieceList();
            if (waitmaclist.Count > 0 && followkongweilist.Count > 0 && followpiecelist.Count > 0)
            {
                // 1. 执行置换夹爪流程
                if (!ChangeGripper())
                {
                    Log("ERROR", "置换夹爪失败，退出扫描流程");
                    ProcessTermination = true;
                    return;
                }

                for (int i = 0; i < waitmaclist.Count; i++)
                {
                    if (ProcessTermination)
                        break;
                    for (int j = 0; j < followkongweilist.Count; j++)
                    {
                        if (ProcessTermination)
                            break;
                        for (int z = 0; z < followpiecelist.Count; z++)
                        {
                            if (ProcessTermination)
                                break;
                            int macnum = Convert.ToInt16(waitmaclist[i].Split(',')[0]);
                            string ip = waitmaclist[i].Split(',')[1];
                            string workpiecename = waitmaclist[i].Split(',')[2];
                            int workstate = Convert.ToInt16(waitmaclist[i].Split(',')[3]);
                            int followkongpos = followkongweilist[j];
                            int followpiecepos = Convert.ToInt16(followpiecelist[z].Split(',')[0]);
                            string waitprocesspiecename = followpiecelist[z].Split(',')[1];
                            if (workpiecename != null && workstate == 1)
                            {
                                if (!ExecuteTask(macnum))
                                {
                                    Log("ERROR", "机器人执行从机台" + macnum + "取工件任务失败，退出换料流程！");
                                    ProcessTermination = true;
                                    break;
                                }
                                Log("INFO", "机器人执行从机台" + macnum + "取工件任务成功");
                                if (!UpdateMacMessage(macnum, null, 0))
                                    Log("ERROR", "更新机台" + macnum + "工件信息失败！");
                                if (!Updaterobotmessage(workpiecename, 2, 2))
                                    Log("ERROR", "更新机器人夹爪信息失败！");
                                if (!ExecuteTask(followkongpos))
                                {
                                    Log("ERROR", "机器人执行随行台" + followkongpos + "放置任务失败，退出换料流程！");
                                    ProcessTermination = true;
                                    break;
                                }
                                Log("INFO", "机器人执行随行台" + followkongpos + "放置任务成功！");
                                if (!Updatefollowing_Rack(followkongpos, workpiecename, 2, 2))
                                    Log("ERROR", "更新随行台" + followkongpos + "料位工件信息失败！");
                                if (!Updaterobotmessage(null, 0, 1))
                                    Log("ERROR", "更新机器人夹爪工件信息失败！");
                                if (!ExecuteTask(followpiecepos))
                                {
                                    Log("ERROR", "机器人执行随行台" + followpiecepos + "抓取任务失败，退出换料流程！");
                                    ProcessTermination = true;
                                    break;
                                }
                                Log("INFO", "机器人执行随行台" + followpiecepos + "抓取任务成功");
                                if (!Updatefollowing_Rack(followpiecepos, null, 0, 1))
                                    Log("ERROR", "更新随行台" + followpiecepos + "料位工件信息失败！");
                                if (!Updaterobotmessage(waitprocesspiecename, 0, 2))
                                    Log("ERROR", "更新机器人夹爪工件信息失败！");
                                if (!ExecuteTask(macnum))
                                {
                                    Log("ERROR", "机器人执行机台" + macnum + "放置工件任务失败，退出换料流程！");
                                    ProcessTermination = true;
                                    break;
                                }
                                Log("ERROR", "机器人执行机台" + macnum + "放置工件任务成功！");
                                if (!UpdateMacMessage(macnum, waitprocesspiecename, 0))
                                    Log("ERROR", "更新机台" + macnum + "工件信息失败！");
                                if (!Updaterobotmessage(null, 0, 1))
                                    Log("ERROR", "更新机器人夹爪工件信息失败！");
                                if (!transferprogram.RunTransferProcess(ip, waitprocesspiecename))
                                    Log("ERROR", "给机台" + macnum + "传输工件" + waitprocesspiecename + "程序失败！");
                                Log("INFO", "给机台" + macnum + "传输工件" + waitprocesspiecename + "程序成功！");
                                if (!UpdateMacMessage(macnum, waitprocesspiecename, 0))
                                    Log("ERROR", "更新机台" + macnum + "工件信息失败！");
                                if (!MacStartProcessSafe(macnum))
                                    Log("ERROR", "启动机台" + macnum + "加工失败！");
                                Log("INFO", "启动机台" + macnum + "加工成功！");
                                if (!UpdateMacMessage(macnum, waitprocesspiecename, 1))
                                    Log("ERROR", "更新机台" + macnum + "工件信息失败！");
                                if (!Updaterack_administrate(waitprocesspiecename, 1))
                                    Log("ERROR", "更新料架" + waitprocesspiecename + "工件状态为加工中失败！");
                            }
                        }
                    }
                }
            }
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
                flag = false;
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
                flag = false;
            }
            Log("INFO", "执行任务 " + tasknum + " 成功");
            return flag;
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
            if (IsFixedGripper) { Log("INFO", "固定爪模式，无需置换"); return true; }
            string curName="",curType="",curWp=""; int curPos=0;
            try {
                using(DataTable dt=Mysql.ExeForDtl("SELECT clampname,clampstatus,clampposstatus,workpiecename FROM robotmessage LIMIT 1")){
                    if(dt!=null&&dt.Rows.Count>0){curName=SafeStr(dt.Rows[0]["clampname"]);curType=SafeStr(dt.Rows[0]["clampstatus"]);curPos=SafeInt(dt.Rows[0]["clampposstatus"]);curWp=SafeStr(dt.Rows[0]["workpiecename"]);}
                }
            }catch(Exception ex){Log("ERROR","读取robotmessage失败:"+ex.Message);return false;}
            if(curPos==0){Log("INFO","手上无夹爪，直接抓取目标");return PickTargetGripper();}
            if(IsTargetGripper(curType)){Log("INFO","手上已为目标夹爪:"+curName);return true;}
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
        static bool ClearCurGripper(string t){var g=FindGripper(t);if(g==null)return true;if(!string.IsNullOrEmpty(g.SlotPhotoSignal)&&Plc.ReadWR(g.SlotPhotoSignal)){Log("ERROR","夹爪料位有异物");return false;}if(!string.IsNullOrEmpty(g.PlaceTaskNo)){int tn=ParseTaskNo(g.PlaceTaskNo)+(string.IsNullOrEmpty(g.Number)?0:int.Parse(g.Number));Plc.WriteDM(PlcDm,tn);if(!WaitTask(300)){Log("ERROR","放夹爪超时");return false;}}ClearRobotClamp();return true;}
        static bool PickGripper(string t){var g=FindGripper(t);if(g==null){Log("ERROR","未找到目标夹爪");return false;}if(!string.IsNullOrEmpty(g.GrabTaskNo)){int tn=ParseTaskNo(g.GrabTaskNo)+(string.IsNullOrEmpty(g.Number)?0:int.Parse(g.Number));Plc.WriteDM(PlcDm,tn);if(!WaitTask(300)){Log("ERROR","抓夹爪超时");return false;}}try{Mysql.ExecuteSql("UPDATE robotmessage SET clampname='"+g.Name+"',clampstatus="+(t=="steel"?1:t=="tool"?2:t=="electrode"?3:4)+",clampposstatus=1,workpiecename=NULL,workpiecestatus=0");}catch{}Log("SUCCESS","已抓取:"+g.Name);return true;}
        static bool IsTargetGripper(string t)=>string.Equals(t,TargetGripperType,StringComparison.OrdinalIgnoreCase)||(t=="1"&&TargetGripperType=="steel")||(t=="2"&&TargetGripperType=="tool")||(t=="3"&&TargetGripperType=="electrode")||(t=="4"&&TargetGripperType=="pallet");
        static GripperConfig FindGripper(string t){foreach(var g in Grippers)if(IsTargetGripperType(g.Type,t))return g;return Grippers.Count>0?Grippers[0]:null;}
        static bool IsTargetGripperType(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase)||(b=="1"&&a=="steel")||(b=="2"&&a=="tool")||(b=="3"&&a=="electrode")||(b=="4"&&a=="pallet");

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
        static bool WaitTask(int max){int w=0;while(w<max){if(Plc.ReadWR(TaskFinishAddr))return true;Thread.Sleep(100);w++;}return Plc.ReadWR(TaskFinishAddr);}
        static void ClearRobotWp(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0");}catch{}}
        static void ClearRobotClamp(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0");}catch{}}
        static int GetEmptyRackSlot(){try{using(DataTable dt=Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0 LIMIT 1")){if(dt!=null&&dt.Rows.Count>0)return Convert.ToInt32(dt.Rows[0]["id"]);}}catch{}return 0;}
        static int ParseTaskNo(string s){if(string.IsNullOrEmpty(s))return 0;int.TryParse(s.ToUpper().Replace("D","").Replace("DM","").Trim(),out int r);return r;}
        static void AddGripper(object item){var g=item as Dictionary<string,object>;if(g==null)return;Grippers.Add(new GripperConfig{Id=GetStr(g,"id",""),Type=GetStr(g,"type","steel"),Name=GetStr(g,"name",""),Number=GetStr(g,"number",""),SlotPhotoSignal=GetStr(g,"slotPhotoSignal",""),GrabTaskNo=GetStr(g,"grabTaskNo",""),PlaceTaskNo=GetStr(g,"placeTaskNo","")});}
    }
}
