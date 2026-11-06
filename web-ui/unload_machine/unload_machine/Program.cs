using DataCommonOperate;
using PLC;
using MacCommunicate;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace unload_machine
{
    class Program
    {
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "unload_machine";
        static readonly string DisplayName = "下料程序";

        // 数据库
        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
        static string DbType = "mysql";
        static CommonInterface Mysql;
        static Dictionary<int, macinterface> MacList = new Dictionary<int, macinterface>();
        static plc Plc;

        // 停止标志位：前端停止按钮触发后，后端创建 stop.flag，子程序检测到即终止
        static readonly string StopFlagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stop.flag");
        static bool IsStopRequested() { return File.Exists(StopFlagFile); }

        // 夹爪配置
        static string GripperType = "";
        static bool IsFixedGripper = true;
        static string TargetGripperType = "steel";
        static List<GripperConfig> Grippers = new List<GripperConfig>();
        class GripperConfig { public string Id, Type, Name, Number; public string SlotPhotoSignal, GrabTaskNo, PlaceTaskNo; }

        // PLC 配置
        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
        static int MachineStartTaskNo;
        static int PlcDm = 100;
        static string TaskStartAddr = "";
        static string TaskFinishAddr = "205.00";
        static string RobotEleAddr = "206.00";

        // ==================== 从总包配置文件中读取 ====================
        
        
        static void Main(string[] args)
        {
            LogHelper.Info( "【" + DisplayName + "】分包启动");
            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败，配置来源: " + ConfigClient.LastSourceText); return; }
                if (!ExtractConfig(cfg)) return;
                InitMachines(cfg);
                LogHelper.Configure(StepName, DisplayName, ApiBase);
                // OPC UA 连接测试
                foreach (var kv in MacList)
                {
                    var m = MachineConfigs.ContainsKey(kv.Key) ? MachineConfigs[kv.Key] : null;
                    string typeStr = m != null && m.ContainsKey("type") ? m["type"] : "";
                    if (typeStr == "siemense")
                    {
                        LogHelper.Info( string.Format("机床 {0} (siemense) 正在测试 OPC UA 连接 {1}:4840 (等待最多 10 秒)...", kv.Key, m["ip"]));
                        try
                        {
                            var task = Task.Run(() => kv.Value.ConnectMac());
                            if (task.Wait(TimeSpan.FromSeconds(10)))
                            {
                                if (task.Result) LogHelper.Success( string.Format("机床 {0} OPC UA 连接成功！", kv.Key));
                                else LogHelper.Error( string.Format("机床 {0} OPC UA 连接失败", kv.Key));
                            }
                            else
                            {
                                LogHelper.Error( string.Format("机床 {0} OPC UA 连接超时（10 秒）", kv.Key));
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Error( string.Format("机床 {0} OPC UA 连接异常: {1}", kv.Key, ex.Message));
                        }
                    }
                }
                Mysql = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                Plc = new plc();
                if (!Plc.Connect(PlcIp, PlcPort))
                {
                    LogHelper.Error( "PLC 连接失败: " + PlcIp);
                    return;
                }
                LogHelper.Info( "PLC 连接成功");
                List<string> waitmaclist = Waitmaclist();
                List<string> followingkonglist = FollowKongWeiList();
                if (waitmaclist.Count > 0 && followingkonglist.Count > 0)
                {
                    // 0. 目标夹爪类型 = 本次下机工件所在料架的类型（快换爪模式）
                    if (!IsFixedGripper)
                    {
                        var preWp = GetMacDoneWorkpiece(Convert.ToInt16(waitmaclist[0].Split(',')[0]));
                        if (preWp == null || string.IsNullOrEmpty(preWp.Item2))
                        {
                            LogHelper.Error( "未读取到加工完成工件，无法确定目标夹爪类型，退出扫描流程");
                            return;
                        }
                        TargetGripperType = RackTypeToGripper(GetRackTypeByWorkpiece(preWp.Item2));
                        LogHelper.Info( "下机工件 " + preWp.Item2 + " → 目标夹爪类型 " + TargetGripperType);
                        if (string.IsNullOrEmpty(TargetGripperType))
                        {
                            LogHelper.Error( "工件 " + preWp.Item2 + " 料架类型无法识别，无法确定目标夹爪类型，退出扫描流程");
                            return;
                        }
                    }

                    // 1. 执行置换夹爪流程
                    if (!ChangeGripper())
                    {
                        LogHelper.Error( "置换夹爪失败，退出扫描流程");
                        return;
                    }


                    for (int i = 0; i < waitmaclist.Count; i++)
                    {
                        int macnum = Convert.ToInt16(waitmaclist[i].Split(',')[0]);
                        // 读取机床"加工完成"的工位
                        var wp = GetMacDoneWorkpiece(macnum);
                        if (wp == null || string.IsNullOrEmpty(wp.Item2))
                            continue;
                        int worknum = wp.Item1;
                        string workpiecename = wp.Item2;

                        // 夹爪类型跟随本次实际搬运的工件；本流程内若料架类型变化则再置换一次夹爪
                        if (!IsFixedGripper)
                        {
                            string needType = RackTypeToGripper(GetRackTypeByWorkpiece(workpiecename));
                            if (string.IsNullOrEmpty(needType))
                            {
                                LogHelper.Error( "工件 " + workpiecename + " 料架类型无法识别，无法确定目标夹爪类型，跳过机床" + macnum);
                                continue;
                            }
                            if (!string.Equals(needType, TargetGripperType, StringComparison.OrdinalIgnoreCase))
                            {
                                LogHelper.Info( "工件 " + workpiecename + " 需要夹爪 " + needType
                                    + "，当前为 " + TargetGripperType + "，重新置换夹爪");
                                TargetGripperType = needType;
                                if (!ChangeGripper())
                                {
                                    LogHelper.Error( "置换夹爪失败，扫描流程终止！");
                                    return;
                                }
                            }
                        }

                        for (int j = 0; j < followingkonglist.Count; j++)
                        {
                            int followpos = Convert.ToInt16(followingkonglist[j].Split(',')[0]);
                            if (ExecuteTask(GetMachineStartNo(macnum, "grab") + macnum))
                            {
                                // 清空机床工位
                                ClearMacWorkPos(macnum, worknum);
                                // 更新夹爪（加工完成状态）
                                if (Updaterobotmessage(workpiecename, 2))
                                {
                                    if (ExecuteTask(followpos))
                                    {
                                        if (Updaterobotmessage(null, 0))
                                        {
                                            if (Updatefollowing_Rack(followpos, workpiecename, 2))
                                            {

                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (waitmaclist.Count > 0 && !(followingkonglist.Count > 0))
                {
                    int macnum = Convert.ToInt16(waitmaclist[0].Split(',')[0]);
                    var wp = GetMacDoneWorkpiece(macnum);
                    if (wp != null && !string.IsNullOrEmpty(wp.Item2))
                    {
                        int worknum = wp.Item1;
                        string workpiecename = wp.Item2;
                        if (ExecuteTask(GetMachineStartNo(macnum, "grab") + macnum))
                        {
                            ClearMacWorkPos(macnum, worknum);
                            Updaterobotmessage(workpiecename, 2);
                        }
                    }
                }
                LogHelper.Info( "【" + DisplayName + "】分包结束");
            }
            catch (Exception ex)
            {

            }
        }

        static bool ChangeGripper()
        {
            if (IsFixedGripper) { LogHelper.Info( "固定爪模式，无需置换"); return true; }
            LogHelper.Info( "开始置换夹爪流程...目标类型: " + TargetGripperType);
            string cn="",ct="",cw=""; int cp=0;
            try{using(DataTable dt=Mysql.ExeForDtl("SELECT clampname,clampstatus,clampposstatus,workpiecename FROM robotmessage WHERE id = 1")){if(dt!=null&&dt.Rows.Count>0){cn=SafeStr(dt.Rows[0]["clampname"]);ct=SafeStr(dt.Rows[0]["clampstatus"]);cp=SafeInt(dt.Rows[0]["clampposstatus"]);cw=SafeStr(dt.Rows[0]["workpiecename"]);}}}catch(Exception ex){LogHelper.Error("读取robotmessage失败:"+ex.Message);return false;}
            if(cp==0){LogHelper.Info("手上无夹爪");return PickTargetGripper();}
            if(IsT(ct)){LogHelper.Info("已为目标夹爪:"+cn);return true;}
            LogHelper.Info("清空当前夹爪("+cn+")...");
            if(!string.IsNullOrEmpty(cw)&&cw!="NULL"){int s=GetEmptyRackSlot();if(s==0){LogHelper.Error("无料架空位");return false;}Plc.WriteDM(PlcDm,s);if(!string.IsNullOrEmpty(TaskStartAddr)){Plc.WriteWR(TaskStartAddr,1);Thread.Sleep(300);Plc.WriteWR(TaskStartAddr,0);}if(!WaitForTaskFinish(3000))return false;try{Mysql.ExecuteSql("UPDATE rack_administrate SET rfid='"+cw+"',scanstatus=1,posstatus=2 WHERE id="+s);}catch{}ClearRobotWp();}
            if(!ClearCurGrp(ct))return false;
            return PickTargetGripper();
        }
        static bool PickTargetGripper(){return PickGrp(TargetGripperType);}
        static bool ClearCurGrp(string t){var g=FG(t);if(g==null)return true;if(!string.IsNullOrEmpty(g.SlotPhotoSignal)&&Plc.ReadWR(g.SlotPhotoSignal)){LogHelper.Error("夹爪料位有异物");return false;}if(!string.IsNullOrEmpty(g.PlaceTaskNo)){int tn=ParseTaskNo(g.PlaceTaskNo);Plc.WriteDM(PlcDm,tn);if(!string.IsNullOrEmpty(TaskStartAddr)){Plc.WriteWR(TaskStartAddr,1);Thread.Sleep(300);Plc.WriteWR(TaskStartAddr,0);}if(!WaitForTaskFinish(3000))return false;}ClearRobotClamp();return true;}
        static bool PickGrp(string t){var g=FG(t);if(g==null){LogHelper.Error("未找到目标夹爪");return false;}if(!string.IsNullOrEmpty(g.GrabTaskNo)){int tn=ParseTaskNo(g.GrabTaskNo);Plc.WriteDM(PlcDm,tn);if(!string.IsNullOrEmpty(TaskStartAddr)){Plc.WriteWR(TaskStartAddr,1);Thread.Sleep(300);Plc.WriteWR(TaskStartAddr,0);}if(!WaitForTaskFinish(3000))return false;}try{Mysql.ExecuteSql("UPDATE robotmessage SET clampname='"+g.Name+"',clampstatus="+(t=="electrode"?1:t=="steel"?2:t=="tool"?3:4)+",clampposstatus=1,workpiecename=NULL,workpiecestatus=0");}catch{}LogHelper.Success("已抓取:"+g.Name);return true;}
        static bool IsT(string t)=>string.Equals(t,TargetGripperType,StringComparison.OrdinalIgnoreCase)||(t=="1"&&TargetGripperType=="electrode")||(t=="2"&&TargetGripperType=="steel")||(t=="3"&&TargetGripperType=="tool")||(t=="4"&&TargetGripperType=="pallet");
        static GripperConfig FG(string t){foreach(var g in Grippers)if(string.Equals(g.Type,t,StringComparison.OrdinalIgnoreCase)||(t=="1"&&g.Type=="electrode")||(t=="2"&&g.Type=="steel")||(t=="3"&&g.Type=="tool")||(t=="4"&&g.Type=="pallet"))return g;LogHelper.Error("未找到类型为 "+t+" 的夹爪配置（已配置: "+string.Join(",",Grippers.ConvertAll(x=>x.Type+"/"+x.Name).ToArray())+"）");return null;}

        /// <summary>根据工件名（rfid，兼容 workpiecename）获取料架类型数字（1=电极 2=钢件 3=刀具 4=托盘 5=铝板电极），查不到返回 0</summary>
        static int GetRackTypeByWorkpiece(string workpiecename)
        {
            try
            {
                if (string.IsNullOrEmpty(workpiecename)) return 0;
                object val = Mysql.GetValue("select racktype from rack_administrate where rfid='" + workpiecename
                    + "' or workpiecename='" + workpiecename + "' order by id");
                if (val != null && val != DBNull.Value) return Convert.ToInt32(val);
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
        /// 机床编号 → 机床名称
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
        /// 读取机床"加工完成"工位的工位号和工件名（workpiece_status=2）
        /// 多工位下机：按工位号降序，从最后一个工位开始下机
        /// </summary>
        static Tuple<int, string> GetMacDoneWorkpiece(int macnum)
        {
            try
            {
                string macName = GetMacName(macnum);
                using (DataTable dt = Mysql.ExeForDtl("select worknum, workpiece_name from macworkposstatus where mac_name='" + macName + "' and workpiece_status=2 order by worknum desc"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return new Tuple<int, string>(SafeInt(dt.Rows[0]["worknum"]), SafeStr(dt.Rows[0]["workpiece_name"]));
                }
            }
            catch { }
            return null;
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
                    int macnum = Convert.ToInt16(r["id"].ToString());
                    string status = GetMacStatusSafe(macnum);
                    string ip = r["machineip"].ToString();
                    if (status == "待机")
                    {
                        // 有加工完成工位才可下机
                        var wp = GetMacDoneWorkpiece(macnum);
                        if (wp != null && !string.IsNullOrEmpty(wp.Item2))
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
        /// 获取随行台有工件料位列表
        /// </summary>
        /// <returns></returns>
        public static List<string> FollowKongWeiList()
        {
            List<string> piecelist = new List<string>();
            try
            {
                string str = "select * from following_rack where posstatus=1 and workstate=0";
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
        /// 初始化机床通讯实例
        /// </summary>
        static void InitMachines(Dictionary<string, object> cfg)
        {
            try
            {
                MachineConfigs.Clear();
                if (cfg == null || cfg.Count == 0) { LogHelper.Warn( "配置为空，跳过机床初始化"); return; }
                if (!cfg.ContainsKey("machines")) { LogHelper.Warn( "配置文件中没有 machines 节点"); return; }
                var machines = cfg["machines"] as Dictionary<string, object> ?? new Dictionary<string, object>();
                foreach (var kv in machines)
                {
                    int macId = Convert.ToInt32(kv.Key);
                    var m = kv.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
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

        static string SafeStr(object v)=>v==DBNull.Value?"":v?.ToString()??"";
        static int SafeInt(object v)=>v==DBNull.Value?0:Convert.ToInt32(v);
        static bool WaitForTaskFinish(int maxTicks){int w=0;while(w<maxTicks){if(IsStopRequested()){LogHelper.Warn("收到停止信号，机器人任务执行失败");return false;}if(Plc.ReadWR(TaskFinishAddr))return true;Thread.Sleep(100);w++;}return Plc.ReadWR(TaskFinishAddr);}
        static void ClearRobotWp(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0");}catch{}}
        static void ClearRobotClamp(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0");}catch{}}
        static int GetEmptyRackSlot(){try{using(DataTable dt=Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0")){if(dt!=null&&dt.Rows.Count>0)return Convert.ToInt32(dt.Rows[0]["id"]);}}catch{}return 0;}
        static int ParseTaskNo(string s){if(string.IsNullOrEmpty(s))return 0;int.TryParse(s.ToUpper().Replace("D","").Replace("DM","").Trim(),out int r);return r;}

        static int GetMachineStartNo(int macnum, string action)
        {
            try
            {
                if (MachineConfigs.ContainsKey(macnum))
                {
                    string key = action == "grab" ? "grabStartTaskNo" : "placeStartTaskNo";
                    if (MachineConfigs[macnum].ContainsKey(key))
                        return ParseTaskNo(MachineConfigs[macnum][key]);
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
