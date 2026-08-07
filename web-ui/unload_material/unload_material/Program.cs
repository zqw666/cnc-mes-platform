using DataCommonOperate;
using PLC;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace unload_material
{
    class Program
    {
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "unload_material";
        static readonly string DisplayName = "下料程序";
        static bool ProcessTermination=false;

        // 数据库
        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
        static string DbType = "mysql";
        static CommonInterface Mysql;
        static plc Plc;

        // 夹爪名称配置（从配置文件读取或使用默认值）
        // 夹爪配置
        static string GripperType = "fixed-single";
        static bool IsFixedGripper = true;
        static string TargetGripperType = "steel";
        static List<GripperConfig> Grippers = new List<GripperConfig>();
        class GripperConfig { public string Id, Type, Name, Number; public string SlotPhotoSignal, GrabTaskNo, PlaceTaskNo; }

        // PLC 配置
        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
        static int PlcDm = 100;
        static string TaskFinishAddr = "205.00";
        static string RobotEleAddr = "206.00";

        // ==================== 从总包配置文件中读取 ====================
        
        
        static void Main(string[] args)
        {
            LogHelper.Info( "【" + DisplayName + "】分包启动");
            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败"); return; }
                ExtractConfig(cfg);
                Mysql = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                Plc = new plc();
                if (!Plc.Connect(PlcIp, PlcPort))
                {
                    LogHelper.Error( "PLC 连接失败: " + PlcIp);
                    return;
                }
                LogHelper.Info( "PLC 连接成功");
                LogHelper.Info( "PLC 连接成功");

                List<string> followpiecelist = FollowpieceiList();
                List<int> rackkongweilist = GetRack_administrateKongWeiList();
                if (followpiecelist.Count > 0 && rackkongweilist.Count > 0)
                {
                    // 1. 执行置换夹爪流程
                    if (!ChangeGripper())
                    {
                        LogHelper.Error( "置换夹爪失败，退出下料流程");
                        return;
                    }


                    for (int i = 0; i < followpiecelist.Count; i++)
                    {
                        if (ProcessTermination)
                            break;
                        for (int j = 0; j < rackkongweilist.Count; j++)
                        {
                            if (ProcessTermination)
                                break;
                            int followpos = Convert.ToInt16(followpiecelist[i].Split(',')[0]);
                            string workpiecename = followpiecelist[i].Split(',')[1];
                            int origionpos = GetOrigionPosStatus(workpiecename);
                            if (origionpos != 0)
                            {
                                if (!ExecuteTask(followpos))
                                {
                                    LogHelper.Error( "机器人执行" + followpos + "料位抓取任务失败，终止该流程");
                                    ProcessTermination = true;
                                    break;
                                }
                                if (!Updatefollowing_Rack(followpos, null, 1))
                                    LogHelper.Error( "更新随行台" + followpos + "料位状态失败！");
                                if (!Updaterobotmessage(workpiecename, 2))
                                    LogHelper.Error( "更新机器人夹爪工件信息失败");
                                if (!ExecuteTask(origionpos))
                                {
                                    LogHelper.Error( "机器人执行" + followpos + "料位抓取任务失败，终止该流程");
                                    ProcessTermination = true;
                                    break;
                                }
                                if (!Updaterobotmessage(null, 1))
                                    LogHelper.Error( "更新机器人夹爪工件信息失败");
                                if (!Updaterack_administrate(workpiecename, 2))
                                    LogHelper.Error( "更新料架" + origionpos + "料位状态失败！");
                            }
                            else
                            {
                                int rackpos = rackkongweilist[j];
                                if (!ExecuteTask(followpos))
                                {
                                    LogHelper.Error( "机器人执行" + followpos + "料位抓取任务失败，终止该流程");
                                    ProcessTermination = true;
                                    break;
                                }
                                if (!Updatefollowing_Rack(followpos, null, 1))
                                    LogHelper.Error( "更新随行台" + followpos + "料位状态失败！");
                                if (!Updaterobotmessage(workpiecename, 2))
                                    LogHelper.Error( "更新机器人夹爪工件信息失败");
                                if (!ExecuteTask(rackpos))
                                {
                                    LogHelper.Error( "机器人执行" + followpos + "料位抓取任务失败，终止该流程");
                                    ProcessTermination = true;
                                    break;
                                }
                                if (!Updaterobotmessage(null, 1))
                                    LogHelper.Error( "更新机器人夹爪工件信息失败");
                                if (!Updaterack_administrate(workpiecename, 2))
                                    LogHelper.Error( "更新料架" + rackpos + "料位状态失败！");
                            }
                        }
                    }
                }
                else if (followpiecelist.Count > 0 && !(rackkongweilist.Count > 0))
                {
                    LogHelper.Warn( "检测到料架无空料位，请人工处理后再开启！");
                }
                LogHelper.Info( "【" + DisplayName + "】分包结束");
            }
            catch (Exception ex)
            { }
        }



        /// <summary>
        /// 置换夹爪流程
        /// </summary>
        static bool ChangeGripper()
        {
            LogHelper.Info( "开始置换夹爪流程...目标类型: " + TargetGripperType);
            if (IsFixedGripper) { LogHelper.Info( "固定爪模式，无需置换"); return true; }
            string cn="",ct="",cw=""; int cp=0;
            try{using(DataTable dt=Mysql.ExeForDtl("SELECT clampname,clampstatus,clampposstatus,workpiecename FROM robotmessage LIMIT 1")){if(dt!=null&&dt.Rows.Count>0){cn=SafeStr(dt.Rows[0]["clampname"]);ct=SafeStr(dt.Rows[0]["clampstatus"]);cp=SafeInt(dt.Rows[0]["clampposstatus"]);cw=SafeStr(dt.Rows[0]["workpiecename"]);}}}catch(Exception ex){LogHelper.Error("读取robotmessage失败:"+ex.Message);return false;}
            if(cp==0){LogHelper.Info("手上无夹爪");return PickTargetGripper();}
            if(IsT(ct)){LogHelper.Info("已为目标夹爪:"+cn);return true;}
            LogHelper.Info("清空当前夹爪("+cn+")...");
            if(!string.IsNullOrEmpty(cw)&&cw!="NULL"){int s=GetEmptyRackSlot();if(s==0){LogHelper.Error("无料架空位");return false;}Plc.WriteDM(PlcDm,s);if(!WaitTask(300))return false;try{Mysql.ExecuteSql("UPDATE rack_administrate SET rfid='"+cw+"',scanstatus=1,posstatus=2 WHERE id="+s);}catch{}ClearRobotWp();}
            if(!ClearCurGrp(ct))return false;
            return PickTargetGripper();
        }
        static bool PickTargetGripper(){return PickGrp(TargetGripperType);}
        static bool ClearCurGrp(string t){var g=FG(t);if(g==null)return true;if(!string.IsNullOrEmpty(g.SlotPhotoSignal)&&Plc.ReadWR(g.SlotPhotoSignal)){LogHelper.Error("夹爪料位有异物");return false;}if(!string.IsNullOrEmpty(g.PlaceTaskNo)){int tn=ParseTaskNo(g.PlaceTaskNo)+(string.IsNullOrEmpty(g.Number)?0:int.Parse(g.Number));Plc.WriteDM(PlcDm,tn);if(!WaitTask(300))return false;}ClearRobotClamp();return true;}
        static bool PickGrp(string t){var g=FG(t);if(g==null){LogHelper.Error("未找到目标夹爪");return false;}if(!string.IsNullOrEmpty(g.GrabTaskNo)){int tn=ParseTaskNo(g.GrabTaskNo)+(string.IsNullOrEmpty(g.Number)?0:int.Parse(g.Number));Plc.WriteDM(PlcDm,tn);if(!WaitTask(300))return false;}try{Mysql.ExecuteSql("UPDATE robotmessage SET clampname='"+g.Name+"',clampstatus="+(t=="steel"?1:t=="tool"?2:t=="electrode"?3:4)+",clampposstatus=1,workpiecename=NULL,workpiecestatus=0");}catch{}LogHelper.Success("已抓取:"+g.Name);return true;}
        static bool IsT(string t)=>string.Equals(t,TargetGripperType,StringComparison.OrdinalIgnoreCase)||(t=="1"&&TargetGripperType=="steel")||(t=="2"&&TargetGripperType=="tool")||(t=="3"&&TargetGripperType=="electrode")||(t=="4"&&TargetGripperType=="pallet");
        static GripperConfig FG(string t){foreach(var g in Grippers)if(string.Equals(g.Type,t,StringComparison.OrdinalIgnoreCase)||(t=="1"&&g.Type=="steel")||(t=="2"&&g.Type=="tool")||(t=="3"&&g.Type=="electrode")||(t=="4"&&g.Type=="pallet"))return g;return Grippers.Count>0?Grippers[0]:null;}

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
                LogHelper.Error( "发送任务失败，料位: " + tasknum);
                flag = false;
            }

            LogHelper.Info( "发送任务 " + tasknum + " 成功");

            // 等待任务完成
            int waitCount = 0;
            while (!Plc.ReadWR(TaskFinishAddr) && waitCount < 300)
            {
                Thread.Sleep(100);
                waitCount++;
            }

            if (!Plc.ReadWR(TaskFinishAddr))
            {
                LogHelper.Warn( "等待任务完成超时，任务: " + tasknum);
                flag = false;
            }
            LogHelper.Info( "执行任务 " + tasknum + " 成功");
            return flag;
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
                string workpieceStr = string.IsNullOrEmpty(workpiecename) ? "NULL" : "'" + workpiecename + "'";
                string str = "update following_rack set workpiecename=" + workpieceStr + ",posstatus=" + posstatus + " where id=" + pos;
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
                    flag = true;
            }
            catch (Exception ex)
            {
                LogHelper.Error( "更新料架信息异常: " + ex.Message);
            }
            return flag;
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
        /// 获取工件原始料位
        /// </summary>
        /// <param name="workpiecename">工件名称</param>
        /// <returns></returns>
        public static int GetOrigionPosStatus(string workpiecename)
        {
            int origionpos = 0;
            try
            {
                string str = "select id from rack_administrate where workpiecename='" + workpiecename + "' and posstatus=1";
                origionpos = Convert.ToInt16(Mysql.GetValue(str).ToString());
            }
            catch(Exception ex)
            {
                LogHelper.Error( "获取工件原始料位异常: " + ex.Message);
            }
            return origionpos;
        }

        /// <summary>
        /// 获取料架空位
        /// </summary>
        /// <returns></returns>
        public static List<int> GetRack_administrateKongWeiList()
        {
            List<int> kongweilist = new List<int>();
            try
            {
                string str = "select id from rack_administrate where posstatus=1";
                DataTable dt =Mysql.ExeForDtl(str);
                if(dt.Rows.Count>0)
                {
                    foreach(DataRow r in dt.Rows)
                    {
                        kongweilist.Add(Convert.ToInt16(r["id"].ToString()));
                    }
                }
            }
            catch(Exception ex)
            {

            }
            return kongweilist;
        }

        /// <summary>
        /// 获取随行台工件列表
        /// </summary>
        /// <returns></returns>
        public static List<string> FollowpieceiList()
        {
            List<string> piecelist = new List<string>();
            try
            {
                string str = "select * from following_rack where workstate=2 and posstatus=2";
                DataTable dt = Mysql.ExeForDtl(str);
                if (dt.Rows.Count > 0)
                {
                    foreach (DataRow r in dt.Rows)
                    {
                        int pos = Convert.ToInt16(r["id"].ToString());
                        string workpiecename = r["workpiecename"].ToString();
                        piecelist.Add(pos.ToString()+","+workpiecename);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return piecelist;
        }

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
        /// 更新UI界面日志
        /// </summary>
        /// <param name="level"></param>
        static string SafeStr(object v)=>v==DBNull.Value?"":v?.ToString()??"";
        static int SafeInt(object v)=>v==DBNull.Value?0:Convert.ToInt32(v);
        static bool WaitTask(int m){int w=0;while(w<m){if(Plc.ReadWR(TaskFinishAddr))return true;Thread.Sleep(100);w++;}return Plc.ReadWR(TaskFinishAddr);}
        static void ClearRobotWp(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0");}catch{}}
        static void ClearRobotClamp(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0");}catch{}}
        static int GetEmptyRackSlot(){try{using(DataTable dt=Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0 LIMIT 1")){if(dt!=null&&dt.Rows.Count>0)return Convert.ToInt32(dt.Rows[0]["id"]);}}catch{}return 0;}
        static int ParseTaskNo(string s){if(string.IsNullOrEmpty(s))return 0;int.TryParse(s.ToUpper().Replace("D","").Replace("DM","").Trim(),out int r);return r;}
        
        static void ExtractConfig(Dictionary<string, object> cfg)
        {
            try
            {
                if (cfg == null || cfg.Count == 0) return;
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
                        TaskFinishAddr = ConfigClient.GetStr(robotSig, "taskDone", "");
                        RobotEleAddr = ConfigClient.GetStr(robotSig, "photo", "");
                    }
                    var machineSig = ConfigClient.GetDict(signals, "machine");
                    var addrs = ConfigClient.GetDict(plcCfg, "addresses");
                    if (string.IsNullOrEmpty(TaskFinishAddr) && addrs.Count > 0)
                        TaskFinishAddr = ConfigClient.GetStr(addrs, "task_finish", "");
                    if (string.IsNullOrEmpty(RobotEleAddr) && addrs.Count > 0)
                        RobotEleAddr = ConfigClient.GetStr(addrs, "robot_ele", "");
                }

                var line = ConfigClient.GetDict(cfg, "line");
                if (line.Count > 0)
                {
                    GripperType = ConfigClient.GetStr(line, "gripperType", "fixed-single");
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
            catch (Exception ex)
            {
                LogHelper.Warn("提取配置失败，使用默认配置: " + ex.Message);
            }
        }

static void AddGripper(object item){var g=item as Dictionary<string,object>;if(g==null)return;Grippers.Add(new GripperConfig{Id=ConfigClient.GetStr(g,"id",""),Type=ConfigClient.GetStr(g,"type","steel"),Name=ConfigClient.GetStr(g,"name",""),Number=ConfigClient.GetStr(g,"number",""),SlotPhotoSignal=ConfigClient.GetStr(g,"slotPhotoSignal",""),GrabTaskNo=ConfigClient.GetStr(g,"grabTaskNo",""),PlaceTaskNo=ConfigClient.GetStr(g,"placeTaskNo","")});}
    }
}

