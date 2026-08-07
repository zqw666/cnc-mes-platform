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
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "prepare_material";
        static readonly string DisplayName = "备料流程";
        static bool ProcessTermination = false;

        // 数据库
        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
        static string DbType = "mysql";
        static CommonInterface Mysql;
        static Dictionary<int, macinterface> MacList = new Dictionary<int, macinterface>();
        static plc Plc;

        // PLC 配置
        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
        static int MachineStartTaskNo;
        static int PlcDm = 100;
        static string TaskStartAddr = "";
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
            LogHelper.Info( "【" + DisplayName + "】分包启动");
            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败"); return; }
                ExtractConfig(cfg);
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

                List<int> waitmaclist = Waitmaclist();

                if (waitmaclist.Count == 0)
                {
                    LogHelper.Info( "无待加工料位，备料流程结束");
                    return;
                }

                // 随行台判定
                List<int> followkonglist = new List<int>();
                if (PalletEnabled)
                {
                    followkonglist = FollowKongWeiList();
                    if (followkonglist.Count == 0)
                    {
                        LogHelper.Info( "有随行台但无空位，备料流程结束");
                        return;
                    }
                    LogHelper.Info( "随行台空位数: " + followkonglist.Count);
                }
                else
                {
                    LogHelper.Info( "未配置随行台，仅执行抓取→结束");
                }

                // 1. 执行置换夹爪流程
                if (!ChangeGripper())
                {
                    LogHelper.Error( "置换夹爪失败，退出备料流程");
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
                                LogHelper.Error( "NC程序下载失败，备料流程终止！");
                                ProcessTermination = true;
                                break;
                            }
                            if (!ExecuteTask(rackpos))
                            {
                                LogHelper.Error( "机器人执行" + rackpos + "料位抓取任务失败，备料流程终止！");
                                ProcessTermination = true;
                                break;
                            }
                            if (!Updaterobotmessage(workpiecename, 2))
                                LogHelper.Error( "更新机器人夹爪信息失败！");
                            if (!Updaterack_administrate(rackpos, null, 1))
                                LogHelper.Error( "更新料架信息失败！");

                            int followpos = followkonglist[j];
                            if (!ExecuteTask(followpos))
                            {
                                LogHelper.Error( "机器人执行" + followpos + "料位放置任务失败，备料流程终止！");
                                ProcessTermination = true;
                                break;
                            }
                            if (!Updatefollowing_Rack(rackpos, workpiecename, 2))
                                LogHelper.Error( "更新随行台料位信息失败！");
                            if (Updaterobotmessage(null, 1))
                                LogHelper.Error( "更新机器人夹爪信息失败！");
                        }
                    }
                }
                else
                {
                    // ===== 无随行台：只抓取 → 结束 =====
                    List<string> message = GetFirstWaitProcessPieceMessage();
                    int rackpos = Convert.ToInt16(message[0]);
                    string workpiecename = message[1];
                    LogHelper.Info( "待加工料位: " + rackpos + " 工件: " + workpiecename);

                    // NC 程序下载
                    string rfid = GetRackRfid(rackpos);
                    string rackType = GetRackTypeByPos(rackpos);
                    if (!string.IsNullOrEmpty(rfid) && !DownloadNCProgram(rfid, rackType))
                    {
                        LogHelper.Error( "NC程序下载失败，备料流程终止！");
                        return;
                    }

                    // 抓取
                    if (!ExecuteTask(rackpos))
                    {
                        LogHelper.Error( "机器人执行" + rackpos + "料位抓取任务失败，备料流程终止！");
                        return;
                    }
                    LogHelper.Info( "机器人执行" + rackpos + "料位抓取任务成功！");

                    if (!Updaterobotmessage(workpiecename, 2))
                        LogHelper.Error( "更新机器人夹爪信息失败！");
                    if (!Updaterack_administrate(rackpos, workpiecename, 1))
                        LogHelper.Error( "更新料架" + rackpos + "料位信息失败！");

                    LogHelper.Success( "备料流程完成（无随行台）");
                }
                LogHelper.Info( "【" + DisplayName + "】分包结束");
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
            LogHelper.Info( "开始置换夹爪流程...目标类型: " + TargetGripperType);
            if (IsFixedGripper) { LogHelper.Info( "固定爪模式，无需置换"); return true; }

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
            catch (Exception ex) { LogHelper.Error( "读取 robotmessage 失败: " + ex.Message); return false; }

            if (curPosStatus == 0) { LogHelper.Info( "手上无夹爪，直接抓取目标夹爪"); return PickTargetGripper(); }

            if (string.Equals(curType, TargetGripperType, StringComparison.OrdinalIgnoreCase) ||
                (curType == "1" && TargetGripperType == "steel") ||
                (curType == "2" && TargetGripperType == "tool") ||
                (curType == "3" && TargetGripperType == "electrode") ||
                (curType == "4" && TargetGripperType == "pallet"))
            { LogHelper.Info( "手上已为目标夹爪: " + curName); return true; }

            LogHelper.Info( "手上有夹爪(" + curName + ")，先清空...");
            bool hasWorkpiece = !string.IsNullOrEmpty(curWorkpiece) && curWorkpiece != "NULL";

            if (hasWorkpiece)
            {
                int emptySlot = GetEmptyRackSlot();
                if (emptySlot == 0) { LogHelper.Error( "无料架空位"); return false; }
                Plc.WriteDM(PlcDm, emptySlot);
                if (!WaitTask(3000)) { LogHelper.Error( "放工件超时"); return false; }
                try { Mysql.ExecuteSql("UPDATE rack_administrate SET rfid='" + curWorkpiece + "',scanstatus=1,posstatus=2 WHERE id=" + emptySlot); }
                catch { }
                ClearRobotWorkpiece();
            }

            if (!ClearCurrentGripper(curType)) return false;
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
                int taskNo = ParseTaskNoBase(cur.PlaceTaskNo) + (string.IsNullOrEmpty(cur.Number) ? 0 : int.Parse(cur.Number));
                Plc.WriteDM(PlcDm, taskNo);
                if (!WaitTask(3000)) { LogHelper.Error( "放夹爪超时"); return false; }
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
                int taskNo = ParseTaskNoBase(target.GrabTaskNo) + (string.IsNullOrEmpty(target.Number) ? 0 : int.Parse(target.Number));
                Plc.WriteDM(PlcDm, taskNo);
                if (!WaitTask(3000)) { LogHelper.Error( "抓夹爪超时"); return false; }
            }
            try { Mysql.ExecuteSql("UPDATE robotmessage SET clampname='" + target.Name + "',clampstatus=" + (TargetGripperType=="steel"?1:TargetGripperType=="tool"?2:TargetGripperType=="electrode"?3:4) + ",clampposstatus=1,workpiecename=NULL,workpiecestatus=0"); }
            catch { }
            LogHelper.Success( "已抓取目标夹爪: " + target.Name);
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
                LogHelper.Error( "Eman数据库未配置，无法下载NC程序");
                return false;
            }
            if (string.IsNullOrEmpty(NcServerRoot))
            {
                LogHelper.Error( "NC服务器根目录未配置");
                return false;
            }

            var emanData = QueryEmanProduction(rfid, rackType);
            if (emanData == null)
            {
                LogHelper.Error( "从Eman获取生产数据失败，无法下载NC程序");
                return false;
            }

            if (rackType == "electrode")
                return DownloadNCForElectrode(rfid, emanData);
            else if (rackType == "steel")
                return DownloadNCForSteel(rfid, emanData);

            LogHelper.Warn( "未知料架类型: " + rackType + "，跳过NC下载");
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
                    LogHelper.Error( "Eman数据中缺少电极名称");
                    return false;
                }

                // 电极名称按 '-' 分割为 3 部分
                string[] parts = elecName.Split('-');
                if (parts.Length < 3)
                {
                    LogHelper.Error( "电极名称格式异常(需3段'-'分隔): " + elecName);
                    return false;
                }
                string part1 = parts[0];
                string part2 = parts[1];
                string part3 = parts[2];

                // 拼接远程 NC 路径
                string remoteNcPath = Path.Combine(NcServerRoot, toolingNo, moldNo,
                    part1, part2, "small", part3, elecName + ".nc");
                LogHelper.Info( "【电极NC】远程路径: " + remoteNcPath);

                return DownloadFile(remoteNcPath, elecName + ".nc");
            }
            catch (Exception ex)
            {
                LogHelper.Error( "电极NC下载异常: " + ex.Message);
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
                    LogHelper.Error( "Eman数据中缺少工艺卡路径");
                    return false;
                }

                // 1. 下载工艺卡到本地
                string cardFileName = Path.GetFileName(cardPath);
                if (string.IsNullOrEmpty(cardFileName))
                    cardFileName = "steel_card_" + rfid + ".txt";

                LogHelper.Info( "【钢件NC】下载工艺卡: " + cardPath);
                if (!DownloadFile(cardPath, cardFileName))
                {
                    LogHelper.Error( "下载工艺卡失败: " + cardPath);
                    return false;
                }

                // 2. 读取 Excel 工艺卡指定单元格获取 NC 路径
                string localCardPath = Path.Combine(NcLocalTemp, cardFileName);
                string ncPathLine = ReadExcelCell(localCardPath, ProcessCardCell);
                if (string.IsNullOrEmpty(ncPathLine))
                {
                    LogHelper.Error( "工艺卡中未找到NC路径（单元格: " + ProcessCardCell + "）");
                    return false;
                }
                LogHelper.Info( "【钢件NC】工艺卡NC路径: " + ncPathLine);

                // 3. 下载 NC 程序
                string ncFileName = Path.GetFileName(ncPathLine);
                if (string.IsNullOrEmpty(ncFileName))
                    ncFileName = "steel_" + rfid + ".nc";

                LogHelper.Info( "【钢件NC】下载NC程序: " + ncPathLine);
                if (!DownloadFile(ncPathLine, ncFileName))
                {
                    LogHelper.Error( "下载NC程序失败: " + ncPathLine);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error( "钢件NC下载异常: " + ex.Message);
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

                LogHelper.Info( "查询Eman(" + rackType + "): " + rfid);
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
                LogHelper.Warn( "Eman未找到记录: " + rfid);
            }
            catch (Exception ex) { LogHelper.Error( "Eman查询失败: " + ex.Message); }
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
                    LogHelper.Error( "远程文件不存在: " + remotePath);
                    return false;
                }

                string localPath = Path.Combine(NcLocalTemp, fileName);
                File.Copy(remotePath, localPath, true);
                LogHelper.Success( "文件下载成功: " + localPath);
                return File.Exists(localPath);
            }
            catch (Exception ex)
            {
                LogHelper.Error( "文件下载失败: " + ex.Message);
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
                    LogHelper.Error( "Excel文件不存在: " + filePath);
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
                LogHelper.Error( "读取Excel单元格失败(" + cellRef + "): " + ex.Message);
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
        static void InitMachines(Dictionary<string, object> cfg)
        {
            try
            {
                MachineConfigs.Clear();
                if (cfg == null || cfg.Count == 0) { LogHelper.Warn( "配置为空，跳过机床初始化"); return; }
                if (!cfg.ContainsKey("machines")) { LogHelper.Warn( "配置文件中没有 machines 节点"); return; }
                var machines = Js.Deserialize<Dictionary<string, object>>(cfg["machines"].ToString());
                foreach (var kv in machines)
                {
                    int macId = Convert.ToInt32(kv.Key);
                    var m = Js.Deserialize<Dictionary<string, object>>(kv.Value.ToString());
                    string typeStr = m.ContainsKey("type") ? m["type"].ToString().ToLower() : "roders";
                    string ip = m.ContainsKey("ip") ? m["ip"].ToString() : "";
                    string sessionUrl = m.ContainsKey("sessionUrl") ? m["sessionUrl"].ToString() : "";
                    string username = m.ContainsKey("username") ? m["username"].ToString() : "";
                    string password = m.ContainsKey("password") ? m["password"].ToString() : "";
                    MachineConfigs[macId] = new Dictionary<string, string>() { { "type", typeStr }, { "ip", ip } };
                    MacCommunicate.Macfactory.MacType macType;
                    switch (typeStr)
                    {
                        case "fanuc": macType = MacCommunicate.Macfactory.MacType.fanuc; break;
                        case "siemense": macType = MacCommunicate.Macfactory.MacType.siemense; break;
                        case "makino": macType = MacCommunicate.Macfactory.MacType.makino; break;
                        default: macType = MacCommunicate.Macfactory.MacType.roders; break;
                    }
                    var mac = MacCommunicate.Macfactory.MacFactory.CreateInstance(macType, ip, sessionUrl, username, password);
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
        static void ClearRobotWorkpiece(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL,workpiecestatus=0");}catch{}}
        static void ClearRobotClamp(){try{Mysql?.ExecuteSql("UPDATE robotmessage SET clampname=NULL,clampposstatus=0,workpiecename=NULL,workpiecestatus=0");}catch{}}
        static int GetEmptyRackSlot(){try{using(DataTable dt=Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0 LIMIT 1")){if(dt!=null&&dt.Rows.Count>0)return Convert.ToInt32(dt.Rows[0]["id"]);}}catch{}return 0;}
        static int ParseTaskNoBase(string s){if(string.IsNullOrEmpty(s))return 0;int.TryParse(s.ToUpper().Replace("D","").Replace("DM","").Trim(),out int r);return r;}
        
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

        // ==================== 转台运动控制 ====================
        static bool RunTurntable(plc plc, int angle)
        {
            LogHelper.Info( "【转台】移动至 " + angle + " 度");
            plc.WriteWr(SignalDegree, (ushort)angle);
            plc.WriteWR(SignalAllowMove, 1);
            plc.WriteWR(SignalStartMove, 1);

            LogHelper.Info( "【转台】等待到位信号 " + SignalMoveDone);
            while (!plc.ReadWR(SignalMoveDone))
                Thread.Sleep(200);

            plc.WriteWR(SignalAllowMove, 0);
            plc.WriteWR(SignalStartMove, 0);
            LogHelper.Success( "【转台】" + angle + " 度移动完成");
            return true;
        }

        static bool TurntableReturnZero(plc plc)
        {
            LogHelper.Info( "【转台】回零开始: " + SignalReturnZero);
            plc.WriteWR(SignalReturnZero, 1);

            LogHelper.Info( "【转台】等待零点信号 " + SignalAtZero);
            while (!plc.ReadWR(SignalAtZero))
                Thread.Sleep(200);

            plc.WriteWR(SignalReturnZero, 0);
            LogHelper.Success( "【转台】已回到零点");
            return true;
        }
    }
}
