// ============================================================================
// 扫描流程 - SubProcess
// 1. 置换夹爪流程
//    - 判定机器人是固定爪还是快换爪
//    - 确认夹爪上被清空
//    - 获取夹爪放置位状态
//    - 若夹爪放置位为空，发送任务将夹爪放置到夹爪放置位
// 2. 获取料架待扫描料位列表
// 3. 循环扫描 - 发送扫描任务给机器人
// 4. 控制扫码枪并输出扫描结果
// 5. 更新料位扫描状态
// 6. 通知总包程序更新前端UI
// 7. 更新robotmessage表夹爪状态
// ============================================================================
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using DataCommonOperate;
using PLC;
using Serialoperate;
using Rcd;

namespace scan
{
    class Program
    {
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "scan";
        static readonly string DisplayName = "扫描流程";

        // PLC 配置
        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
        static int PlcDm = 100;
        static string TaskStartAddr = "204.00";
        static string TaskFinishAddr = "205.00";
        static string RobotEleAddr = "206.00";

        // 夹爪配置（从 config.json line/grippers 读取）
        static string GripperType = "";   // fixed-single/fixed-double/quick-electrode/quick-tool/quick-pallet
        static bool IsFixedGripper = true;            // 是否固定爪
        static List<GripperConfig> Grippers = new List<GripperConfig>();
        static List<RackConfig> Racks = new List<RackConfig>();

        class GripperConfig
        {
            public string Id;
            public string Type;           // steel/tool/electrode/pallet
            public string Name;
            public string Number;
            public string SlotPhotoSignal;
            public string GrabTaskNo;
            public string PlaceTaskNo;
        }

        class RackConfig
        {
            public string Id;
            public string Type;           // steel/tool/electrode/pallet
            public string Name;
            public string Number;
            public string GrabStartTaskNo;
            public string PlaceStartTaskNo;
            public string ScanStartTaskNo;
        }

        // 扫描器配置
        static string ScannerType = "rcd"; // "rfid" 或 "rcd"
        static string RfidCom = "COM3";
        static string RcdIp = "192.168.101.71";
        static string RcdPort = "5000";
        static int RcdHeartbeatInterval = 60;

        // 数据库
        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
        static string DbType = "mysql";
        static CommonInterface Mysql;

        // eman 数据库（SQL Server，生产数据）
        static string EmanConnStr = "";
        static bool EmanEnabled = false;

        // eman 生产数据查询语句（{0}=监控编号/RFID）
        static readonly string SqlSteelInfo = "SELECT actualResourceID,operationNameID,ID,mouldName,operationName from Mould INNER JOIN MouldPartTechnics ON Mould.mouldID=MouldPartTechnics.mouldID where MouldPartTechnics.partMnoitorID='{0}' AND operationName='CNC-2'";
        static readonly string SqlElectrodeInfo = "SELECT MouldPart.verifyID FROM Mould INNER JOIN MouldPart ON Mould.mouldID=MouldPart.mouldID WHERE MouldPart.ID='{0}'";

        static plc Plc;

        // 停止标志位：前端停止按钮触发后，后端创建 stop.flag，子程序检测到即终止
        static readonly string StopFlagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stop.flag");
        static bool IsStopRequested() { return File.Exists(StopFlagFile); }

        static void Main(string[] args)
        {
            LogHelper.Info( "【" + DisplayName + "】分包启动");

            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败，配置来源: " + ConfigClient.LastSourceText); return; }
                if (!ExtractConfig(cfg)) return;
                Mysql = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);

                // 连接 PLC
                Plc = new plc();
                if (!Plc.Connect(PlcIp, PlcPort))
                {
                    LogHelper.Error( "PLC 连接失败: " + PlcIp);
                    return;
                }
                LogHelper.Info( "PLC 连接成功");

                // 1. 执行置换夹爪流程
                if (!ChangeGripper())
                {
                    LogHelper.Error( "置换夹爪失败，退出扫描流程");
                    return;
                }

                // 2. 获取料架待扫描料位列表
                var scanList = GetScanList();
                if (scanList.Count == 0)
                {
                    LogHelper.Info( "无待扫描料位，退出扫描流程");
                    return;
                }
                LogHelper.Info( "获取到 " + scanList.Count + " 个待扫描料位");

                // 3. 循环扫描
                foreach (int pos in scanList)
                {
                    if (IsStopRequested()) { LogHelper.Warn("收到停止信号，终止扫描流程"); return; }
                    LogHelper.Info( "开始扫描料位: " + pos);

                    // 发送扫描任务：任务号 = 料架扫描开始任务号 + 料位
                    var scanRack = GetRackByType(GetRackType(pos));
                    int scanStartNo = 400;
                    if (scanRack != null && !string.IsNullOrEmpty(scanRack.ScanStartTaskNo))
                        scanStartNo = ParseTaskNoBase(scanRack.ScanStartTaskNo);
                    if (!Plc.WriteDM(PlcDm, scanStartNo + pos))
                    {
                        LogHelper.Error("发送任务失败，料位: " + pos);
                        continue;
                    }
                    LogHelper.Info( "发送任务 "+ pos + " 成功");
                    // 发送扫描任务
                    if (!Plc.WriteWR(TaskStartAddr, 1))
                    {
                        LogHelper.Error("发送任务开始失败，料位: " + pos);
                        continue;
                    }
                    Thread.Sleep(300);
                    Plc.WriteWR(TaskStartAddr, 0);
                    // 等待任务完成
                    int waitCount = 0;
                    while (!Plc.ReadWR(TaskFinishAddr) && waitCount < 300)
                    {
                        if (IsStopRequested()) { LogHelper.Warn("收到停止信号，机器人任务执行失败"); return; }
                        Thread.Sleep(100);
                        waitCount++;
                    }

                    if (!Plc.ReadWR(TaskFinishAddr))
                    {
                        LogHelper.Warn( "等待任务完成超时，料位: " + pos);
                        continue;
                    }
                    LogHelper.Info( "执行任务 " + pos + " 成功");

                    // 4. 扫描并识别 RFID
                    ScanPos(pos);
                }

                LogHelper.Success( "扫描流程完成");
            }
            catch (Exception ex)
            {
                LogHelper.Error( "执行异常: " + ex.Message);
            }
            finally
            {
                if (Plc != null)
                    Plc.close();
            }

            LogHelper.Info( "【" + DisplayName + "】分包结束");
        }

        
        
        static bool ChangeGripper()
        {
            LogHelper.Info( "开始置换夹爪流程...");

            if (IsFixedGripper)
            {
                LogHelper.Info( "固定爪模式，读取 robotmessage 判定工件...");
                string fixedWorkpiece = "";
                try
                {
                    using (DataTable dt = Mysql.ExeForDtl("SELECT workpiecename FROM robotmessage where id =1"))
                        if (dt != null && dt.Rows.Count > 0)
                            fixedWorkpiece = SafeStr(dt.Rows[0]["workpiecename"]);
                }
                catch (Exception ex) { LogHelper.Error( "读取 robotmessage 失败: " + ex.Message); return false; }

                bool fixedHasWorkpiece = !string.IsNullOrEmpty(fixedWorkpiece) && fixedWorkpiece != "NULL";

                if (!fixedHasWorkpiece)
                {
                    LogHelper.Info( "固定爪无工件，置换夹爪完成");
                    UpdateRobotMessage_ChangeGripper("固定夹爪", 1);
                    return true;
                }

                // 有工件：放回料架空位
                LogHelper.Info( "固定爪有工件: " + fixedWorkpiece + "，放置回料架...");
                int emptySlot = GetEmptyRackSlot();
                if (emptySlot == 0)
                {
                    LogHelper.Error( "没有可用的料架空位");
                    return false;
                }

                var placeRack = GetRackByType(GetRackType(emptySlot));
                int placeStartNo = 0;
                if (placeRack != null && !string.IsNullOrEmpty(placeRack.PlaceStartTaskNo))
                    placeStartNo = ParseTaskNoBase(placeRack.PlaceStartTaskNo);
                if (!Plc.WriteDM(PlcDm, placeStartNo + emptySlot))
                {
                    LogHelper.Error( "发送放置工件任务失败: " + emptySlot);
                    return false;
                }

                if (!Plc.WriteWR(TaskStartAddr,1))
                {
                    LogHelper.Error("发送任务开始失败: " + emptySlot);
                    return false;
                }
                LogHelper.Info( "发送放置工件任务: " + emptySlot);
                Thread.Sleep(300);
                Plc.WriteWR(TaskStartAddr, 0);
                if (!WaitForTaskFinish(300))
                {
                    LogHelper.Error( "放置工件任务超时");
                    return false;
                }

                try
                {
                    Mysql.ExecuteSql("UPDATE rack_administrate SET rfid='" + fixedWorkpiece + "', scanstatus=1, workstate=0, posstatus=2 WHERE id=" + emptySlot);
                    LogHelper.Info( "更新料位 " + emptySlot + " 为有料: " + fixedWorkpiece);
                    LogHelper.NotifyRackChanged();
                }
                catch (Exception ex) { LogHelper.Warn( "更新料架失败: " + ex.Message); }

                ClearRobotWorkpiece();
                UpdateRobotMessage_ChangeGripper("固定夹爪", 1);
                LogHelper.Success( "固定爪置换夹爪完成");
                return true;
            }

            // 快换爪：读取 robotmessage 表
            LogHelper.Info( "快换爪模式，读取 robotmessage 表...");
            int clampposstatus = 0;
            string clampName = "";
            string clampType = "";
            string workpieceName = "";
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT clampname,clampstatus,clampposstatus,workpiecename FROM robotmessage where id =1"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        clampName = SafeStr(dt.Rows[0]["clampname"]);
                        clampType = SafeStr(dt.Rows[0]["clampstatus"]);
                        clampposstatus = SafeInt(dt.Rows[0]["clampposstatus"]);
                        workpieceName = SafeStr(dt.Rows[0]["workpiecename"]);
                    }
                }
            }
            catch (Exception ex) { LogHelper.Error( "读取 robotmessage 失败: " + ex.Message); return false; }

            LogHelper.Info( string.Format("robotmessage: clampname={0}, clampposstatus={1}, workpiece={2}",
                clampName, clampposstatus, string.IsNullOrEmpty(workpieceName) ? "(空)" : workpieceName));

            // 手上无夹爪
            if (clampposstatus == 0)
            {
                LogHelper.Info( "机器人手上无夹爪，置换夹爪流程结束");
                return true;
            }

            // 手上有夹爪
            bool hasWorkpiece = !string.IsNullOrEmpty(workpieceName) && workpieceName != "NULL";

            if (!hasWorkpiece)
            {
                // 无工件：直接放置夹爪
                LogHelper.Info( "夹爪上无工件，准备放置夹爪...");
                return PlaceGripperToSlot(clampType);
            }
            else
            {
                // 有工件：先放工件到料架，再放夹爪
                LogHelper.Info( "夹爪上有工件: " + workpieceName + "，先放置工件到料架...");

                // 获取料架空位
                int emptySlot = GetEmptyRackSlot();
                if (emptySlot == 0)
                {
                    LogHelper.Error( "没有可用的料架空位");
                    return false;
                }

                // 发送放置工件到料架任务：任务号 = 料架放置开始任务号 + 料位
                var placeRack2 = GetRackByType(GetRackType(emptySlot));
                int placeStartNo2 = 0;
                if (placeRack2 != null && !string.IsNullOrEmpty(placeRack2.PlaceStartTaskNo))
                    placeStartNo2 = ParseTaskNoBase(placeRack2.PlaceStartTaskNo);
                int placeTask = placeStartNo2 + emptySlot;
                if (!Plc.WriteDM(PlcDm, placeTask))
                {
                    LogHelper.Error( "发送放置工件任务失败: " + placeTask);
                    return false;
                }
                LogHelper.Info( "发送放置工件任务: " + placeTask);

                // 等待任务完成
                if (!WaitForTaskFinish(300))
                {
                    LogHelper.Error( "放置工件任务超时");
                    return false;
                }

                // 更新料架信息
                try
                {
                    string updateRack = "UPDATE rack_administrate SET rfid='" + workpieceName + "', scanstatus=1, workstate=0, posstatus=2 WHERE id=" + emptySlot;
                    Mysql.ExecuteSql(updateRack);
                    LogHelper.Info( "更新料位 " + emptySlot + " 为有料: " + workpieceName);
                    LogHelper.NotifyRackChanged();
                }
                catch (Exception ex) { LogHelper.Warn( "更新料架失败: " + ex.Message); }

                // 清空机器人夹爪工件信息
                ClearRobotWorkpiece();

                // 再放置夹爪
                LogHelper.Info( "工件已放置，准备放置夹爪...");
                return PlaceGripperToSlot(clampType);
            }
        }

        /// <summary>
        /// 放置夹爪到对应料位
        /// </summary>
        static bool PlaceGripperToSlot(string clampType)
        {
            // 找到匹配类型的夹爪配置
            GripperConfig gripper = null;
            foreach (var g in Grippers)
            {
                if (string.Equals(g.Type, clampType, StringComparison.OrdinalIgnoreCase) ||
                    (clampType == "1" && g.Type == "electrode") ||
                    (clampType == "2" && g.Type == "steel") ||
                    (clampType == "3" && g.Type == "tool") ||
                    (clampType == "4" && g.Type == "pallet"))
                {
                    gripper = g; break;
                }
            }
            if (gripper == null)
            {
                // 禁止兜底使用第一个夹爪：否则会把当前夹爪放错料位，机器人后续抓取错误
                LogHelper.Error( "未找到类型为 " + clampType + " 的夹爪配置，无法放置当前夹爪，请人工处理（已配置: " + string.Join(",", Grippers.ConvertAll(x => x.Type + "/" + x.Name).ToArray()) + "）");
                return false;
            }

            LogHelper.Info( "夹爪: " + gripper.Name + " (类型=" + gripper.Type + ", 光电=" + gripper.SlotPhotoSignal + ")");

            // 读取料位光电信号
            if (!string.IsNullOrEmpty(gripper.SlotPhotoSignal))
            {
                bool slotOccupied = Plc.ReadWR(gripper.SlotPhotoSignal);
                LogHelper.Info( "夹爪料位光电信号: " + (slotOccupied ? "有异物" : "空"));

                if (slotOccupied)
                {
                    LogHelper.Error( "夹爪料位有异物，无法放置夹爪，需人工处理");
                    return false;
                }
            }

            // 发送放置夹爪任务
            if (!string.IsNullOrEmpty(gripper.PlaceTaskNo))
            {
                int taskNo = ParseTaskNoBase(gripper.PlaceTaskNo);
                LogHelper.Info( "发送放置夹爪任务: " + taskNo);
                if (!Plc.WriteDM(PlcDm, taskNo))
                {
                    LogHelper.Error( "发送放置夹爪任务失败");
                    return false;
                }
                if (!Plc.WriteWR(TaskStartAddr, 1))
                {
                    LogHelper.Error("发送任务开始失败");
                    return false;
                }
                Thread.Sleep(300);
                Plc.WriteWR(TaskStartAddr, 0);
                if (!WaitForTaskFinish(300))
                {
                    LogHelper.Error( "放置夹爪任务超时");
                    return false;
                }
            }
            else
            {
                LogHelper.Warn( "未配置放置夹爪任务号，跳过发送任务");
            }

            // 更新 robotmessage：手上为空
            ClearRobotClamp();
            LogHelper.Success( "置换夹爪流程完成，机器人手上已清空");
            return true;
        }

        static bool WaitForTaskFinish(int maxTicks)
        {
            int waited = 0;
            while (waited < maxTicks)
            {
                if (IsStopRequested()) { LogHelper.Warn("收到停止信号，机器人任务执行失败"); return false; }
                if (Plc.ReadWR(TaskFinishAddr)) return true;
                Thread.Sleep(100);
                waited++;
            }
            return Plc.ReadWR(TaskFinishAddr);
        }

        static int GetEmptyRackSlot()
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return Convert.ToInt32(dt.Rows[0]["id"]);
                }
            }
            catch { }
            return 0;
        }

        static void ClearRobotWorkpiece()
        {
            try { Mysql.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL, workpiecestatus=0"); }
            catch (Exception ex) { LogHelper.Warn( "清空工件信息失败: " + ex.Message); }
        }

        static void ClearRobotClamp()
        {
            try { Mysql.ExecuteSql("UPDATE robotmessage SET clampname=NULL, clampposstatus=0, workpiecename=NULL, workpiecestatus=0"); }
            catch (Exception ex) { LogHelper.Warn( "清空夹爪信息失败: " + ex.Message); }
        }

        static int ParseTaskNoBase(string taskNo)
        {
            if (string.IsNullOrEmpty(taskNo)) return 0;
            string s = taskNo.ToUpper().Replace("D", "").Replace("DM", "").Trim();
            int.TryParse(s, out int r);
            return r;
        }

        /// <summary>
        /// 根据料位号获取料架类型 (steel=钢件 / electrode=电极)
        /// </summary>
        static string GetRackType(int pos)
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT racktype FROM rack_administrate WHERE id=" + pos))
                {
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        int rt = SafeInt(dt.Rows[0]["racktype"]);
                        if (rt == 1) return "electrode";
                        if (rt == 2) return "steel";
                        return "steel"; // 默认钢件
                    }
                }
            }
            catch (Exception ex) { LogHelper.Warn( "获取料架类型失败: " + ex.Message); }
            return "steel";
        }

        /// <summary>
        /// 从 config.json 读取 eman 数据库配置
        /// </summary>
        static void LoadEmanConfig(Dictionary<string, object> cfg)
        {
            try
            {
                var emanCfg = ConfigClient.GetDict(cfg, "eman");
                if (emanCfg.Count > 0)
                {
                    string emanHost = ConfigClient.GetStr(emanCfg, "host", "");
                    string emanDbname = ConfigClient.GetStr(emanCfg, "database", "eman");
                    string emanUser = ConfigClient.GetStr(emanCfg, "user", "sa");
                    string emanPwd = ConfigClient.GetStr(emanCfg, "password", "");
                    string emanType = ConfigClient.GetStr(emanCfg, "type", "sqlserver").ToLower();
                    int emanPort = ConfigClient.GetInt(emanCfg, "port", 1433);
                    if (emanType == "sqlserver")
                        EmanConnStr = $"Server={emanHost},{emanPort};Database={emanDbname};User Id={emanUser};Password={emanPwd};";
                    else
                        EmanConnStr = $"Server={emanHost};Database={emanDbname};Uid={emanUser};Pwd={emanPwd};CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
                    EmanEnabled = !string.IsNullOrEmpty(emanHost);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn( "读取 eman 数据库配置失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 从 eman 数据库获取生产数据
        /// </summary>
        /// <param name="rfid">监控编号/RFID</param>
        /// <param name="rackType">料架类型: steel(钢件) / electrode(电极)</param>
        /// <returns>生产数据字典，失败返回null</returns>
        static Dictionary<string, string> QueryEmanProduction(string rfid, string rackType)
        {
            if (!EmanEnabled || string.IsNullOrEmpty(EmanConnStr))
            {
                LogHelper.Warn( "Eman数据库未配置，跳过生产数据查询");
                return null;
            }
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
                        LogHelper.Success( "Eman查询成功，字段数: " + result.Count);
                        return result;
                    }
                }
                LogHelper.Warn( "Eman未找到记录: " + rfid);
                return null;
            }
            catch (Exception ex)
            {
                LogHelper.Error( "Eman查询失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 获取待扫描料位列表
        /// </summary>
        static List<int> GetScanList()
        {
            var list = new List<int>();
            try
            {
                // scanstatus=0 (未扫描) 且 posstatus!=0 (有料)
                string sql = "SELECT id FROM rack_administrate WHERE scanstatus=0 AND posstatus!=0 ORDER BY id";
                DataTable dt = Mysql.ExeForDtl(sql);
                foreach (DataRow r in dt.Rows)
                {
                    list.Add(Convert.ToInt32(r["id"]));
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "获取待扫描列表失败: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// 扫描料位
        /// </summary>
        static bool ScanPos(int pos)
        {
            bool flag = false;
            string scanResult = "";
            string status = "";

            try
            {
                // 判定机器人光电信号（0=有料，1=无料）
                if (!Plc.ReadWR(RobotEleAddr))
                {
                    // 有料，执行扫描
                    if (ScannerType == "rcd")
                    {
                        scanResult = ScanByRCD();
                    }
                    else
                    {
                        scanResult = ScanByRFID();
                    }

                    if (!string.IsNullOrEmpty(scanResult) && scanResult != "null")
                    {
                        flag = true;
                        status = "pending"; // 扫描成功，待加工
                        LogHelper.Info( "识别料位 " + pos + " 扫描结果: " + scanResult);

                        // 空夹具识别：扫描到的 rfid 含 EMPTY（如 EMPTY1~EMPTY4）即视为空夹具
                        bool isEmptyFixture = scanResult.IndexOf("EMPTY", StringComparison.OrdinalIgnoreCase) >= 0;
                        string workpieceVal = "";
                        if (isEmptyFixture)
                        {
                            // 空夹具：跳过 eman 检索，workpiecename 与 rfid 保持一致
                            LogHelper.Info( "识别料位 " + pos + " 为空夹具 " + scanResult + "，跳过 eman 查询");
                            workpieceVal = scanResult;
                        }
                        else
                        {
                            // 非空夹具：判定料架类型，连接 eman 获取生产数据
                            string rackType = GetRackType(pos);
                            string verifyId = "";
                            if (!string.IsNullOrEmpty(rackType))
                            {
                                var emanData = QueryEmanProduction(scanResult, rackType);
                                if (emanData != null)
                                {
                                    LogHelper.Info( "获取eman生产数据成功: " + scanResult);
                                    foreach (var kv in emanData)
                                        if (kv.Key.Equals("verifyID", StringComparison.OrdinalIgnoreCase))
                                        { verifyId = kv.Value; break; }
                                }
                            }
                            workpieceVal = verifyId;
                        }

                        // 更新料位信息 - 扫描成功，workpiecename 来自 eman 的 verifyID（空夹具时与 rfid 一致）
                        string workpieceStr = string.IsNullOrEmpty(workpieceVal) ? "NULL" : "'" + workpieceVal.Replace("'", "''") + "'";
                        string sql = $"UPDATE rack_administrate SET rfid='{scanResult}', workpiecename={workpieceStr}, scanstatus=1, workstate=0, posstatus=2 WHERE id={pos}";
                        if (!Mysql.ExecuteSql(sql))
                        {
                            LogHelper.Error( "更新料位 " + pos + " 信息失败");
                        }

                        // 通知总包程序更新前端UI - 扫描成功
                        NotifyRackUpdate(pos, "steel", status, scanResult, $"料位{pos}扫描成功，RFID:{scanResult}");
                    }
                    else
                    {
                        // 扫描失败
                        status = "scan-failed";
                        scanResult = "";
                        LogHelper.Warn( "识别料位 " + pos + " 扫描失败");

                        // 标记为扫描失败
                        string sql = $"UPDATE rack_administrate SET scanstatus=2, workstate=0, posstatus=2 WHERE id={pos}";
                        Mysql.ExecuteSql(sql);

                        // 通知总包程序更新前端UI - 扫描失败
                        NotifyRackUpdate(pos, "steel", status, "", $"料位{pos}扫描失败，未识别到RFID");
                    }
                }
                else
                {
                    // 无料
                    status = "no-material";
                    scanResult = "";
                    LogHelper.Info( "料位 " + pos + " 无料");

                    string sql = $"UPDATE rack_administrate SET rfid='', workpiecename='', scanstatus=1, workstate=0, posstatus=1 WHERE id={pos}";
                    Mysql.ExecuteSql(sql);

                    // 通知总包程序更新前端UI - 无料
                    NotifyRackUpdate(pos, "steel", status, "", $"料位{pos}无料");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "扫描料位 " + pos + " 失败: " + ex.Message);
            }
            return flag;
        }

        /// <summary>
        /// 使用串口 RFID 扫描
        /// </summary>
        static string ScanByRFID()
        {
            string rfidName = "";

            var rfid = new ReadRfid();
            rfid.Send(RfidCom);
            Thread.Sleep(500);
            rfidName = rfid.ReceiveData;

            // 重试机制（最多2次）
            if (string.IsNullOrEmpty(rfidName) || rfidName == "null")
            {
                for (int i = 0; i < 2; i++)
                {
                    rfid.Send(RfidCom);
                    Thread.Sleep(500);
                    rfidName = rfid.ReceiveData;
                    if (!string.IsNullOrEmpty(rfidName) && rfidName != "null")
                        break;
                }
            }

            return rfidName;
        }

        /// <summary>
        /// 使用 RCD 扫码枪扫描（TCP连接）
        /// </summary>
        static string ScanByRCD()
        {
            string scanResult = "";
            RCD_100 rcd = null;
            var resetEvent = new ManualResetEvent(false);
            string receivedData = "";

            try
            {
                rcd = new RCD_100(RcdIp, RcdPort, RcdHeartbeatInterval);

                // 连接 RCD 扫码枪
                if (!rcd.Connect())
                {
                    LogHelper.Error( "RCD 扫码枪连接失败: " + RcdIp + ":" + RcdPort);
                    return "";
                }
                LogHelper.Info( "RCD 扫码枪连接成功");
                rcd.MessageSent += rcd.returnMsg;
                rcd.SendMess("start");
                scanResult = WaitMsg(rcd);
                if (string.IsNullOrEmpty(scanResult))
                {
                    // 重试机制：重新发送心跳触发扫码（最多2次）
                    for (int i = 0; i < 2; i++)
                    {
                        resetEvent.Reset();
                        receivedData = "";
                        rcd.PauseHeart();
                        rcd.SendMess("start");
                        rcd.ContinueHeart();
                        scanResult = WaitMsg(rcd);
                        if (!string.IsNullOrEmpty(scanResult) && scanResult != "heartbeat")
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error( "RCD 扫描异常: " + ex.Message);
            }
            finally
            {
                if (rcd != null)
                {
                    try { rcd.Disconnect(); } catch { }
                }
            }

            return scanResult;
        }

        /// <summary>
        /// 等待返回信息
        /// </summary>
        /// <returns></returns>
        static public string WaitMsg(RCD_100 rcd)
        {
            DateTime startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < 3000) // 超时时间设置为1秒
            {
                // 读取扫码枪返回的数据
                if (!string.IsNullOrEmpty(rcd.DJId))
                {
                    break;
                }
            }
            return rcd.DJId;
        }

        #region RobotMessage表更新方法

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
        /// MySQL: INSERT ... ON DUPLICATE KEY UPDATE ...
        /// SQL Server: MERGE ...
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
        /// 通知总包程序更新料架UI
        /// </summary>
        /// <param name="position">料位位置</param>
        /// <param name="rackType">料架类型: tool(刀具) 或 steel(钢件)</param>
        /// <param name="status">状态: no-material, to-scan, pending, urgent, processing, completed, scan-failed</param>
        /// <param name="rfid">RFID编号</param>
        /// <param name="message">消息</param>
        
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

                // eman 数据库（生产数据）
                LoadEmanConfig(cfg);

                var plcCfg = ConfigClient.GetDict(cfg, "plc");
                if (plcCfg.Count > 0)
                {
                    PlcIp = ConfigClient.GetStr(plcCfg, "ip", "192.168.1.1");
                    PlcPort = ConfigClient.GetInt(plcCfg, "port", 9600);
                    var signals = ConfigClient.GetDict(plcCfg, "signals");
                    var robotSig = ConfigClient.GetDict(signals, "robot");
                    if (robotSig.Count > 0)
                    {
                        TaskStartAddr = ConfigClient.GetStr(robotSig, "taskStart", "");
                        TaskFinishAddr = ConfigClient.GetStr(robotSig, "taskDone", "");
                        RobotEleAddr = ConfigClient.GetStr(robotSig, "photo", "");
                        string taskNoStr = ConfigClient.GetStr(robotSig, "taskNumber", "D100");
                        int.TryParse(taskNoStr.Replace("D", "").Replace("DM", "").Trim(), out PlcDm);
                    }
                    var machineSig = ConfigClient.GetDict(signals, "machine");
                    if (machineSig.Count > 0)
                    {
                        string startNoStr = ConfigClient.GetStr(machineSig, "startTaskNo", "0");
                        // MachineStartTaskNo not used in scan
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

                var scanner = ConfigClient.GetDict(cfg, "scanner");
                if (scanner.Count > 0)
                {
                    ScannerType = ConfigClient.GetStr(scanner, "type", "rcd").ToLower();
                    RfidCom = ConfigClient.GetStr(scanner, "rfidCom", "COM3");
                    RcdIp = ConfigClient.GetStr(scanner, "rcdIp", "192.168.101.71");
                    RcdPort = ConfigClient.GetStr(scanner, "rcdPort", "5000");
                    RcdHeartbeatInterval = ConfigClient.GetInt(scanner, "heartbeatInterval", 60);
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

                LoadRacks(cfg);
            }
            catch (Exception ex)
            {
                LogHelper.Error("提取配置失败，终止运行（不再使用默认配置兜底）: " + ex.Message);
                return false;
            }
            return true;
        }

        static void LoadRacks(Dictionary<string, object> cfg)
        {
            try
            {
                Racks.Clear();
                if (cfg == null || !cfg.ContainsKey("racks") || cfg["racks"] == null) return;
                var racksRaw = cfg["racks"];
                if (racksRaw is System.Collections.ArrayList al)
                {
                    foreach (var it in al)
                        if (it is Dictionary<string, object> rk) Racks.Add(ParseRack(rk));
                }
                else if (racksRaw is object[] arr)
                {
                    foreach (var it in arr)
                        if (it is Dictionary<string, object> rk) Racks.Add(ParseRack(rk));
                }
            }
            catch (Exception ex) { LogHelper.Warn("加载料架配置失败: " + ex.Message); }
        }

        static RackConfig ParseRack(Dictionary<string, object> r)
        {
            return new RackConfig
            {
                Id = ConfigClient.GetStr(r, "id", ""),
                Type = ConfigClient.GetStr(r, "type", "steel"),
                Name = ConfigClient.GetStr(r, "name", ""),
                Number = ConfigClient.GetStr(r, "number", ""),
                GrabStartTaskNo = ConfigClient.GetStr(r, "grabStartTaskNo", ""),
                PlaceStartTaskNo = ConfigClient.GetStr(r, "placeStartTaskNo", ""),
                ScanStartTaskNo = ConfigClient.GetStr(r, "scanStartTaskNo", "")
            };
        }

        static RackConfig GetRackByType(string rackType)
        {
            foreach (var r in Racks)
                if (string.Equals(r.Type, rackType, StringComparison.OrdinalIgnoreCase)) return r;
            return Racks.Count > 0 ? Racks[0] : null;
        }

        static void AddGripper(object item) { var g = item as Dictionary<string,object>; if (g==null) return; Grippers.Add(new GripperConfig{Id=ConfigClient.GetStr(g,"id",""),Type=ConfigClient.GetStr(g,"type","steel"),Name=ConfigClient.GetStr(g,"name",""),Number=ConfigClient.GetStr(g,"number",""),SlotPhotoSignal=ConfigClient.GetStr(g,"slotPhotoSignal",""),GrabTaskNo=ConfigClient.GetStr(g,"grabTaskNo",""),PlaceTaskNo=ConfigClient.GetStr(g,"placeTaskNo","")}); }

        static void NotifyRackUpdate(int position, string rackType, string status, string rfid, string message)
        {
            LogHelper.Info("UI更新通知: 料位" + position + ", 状态" + status + ", " + message);
            // 通知主控程序递增料架数据版本号，主控程序向前端推送更新
            LogHelper.NotifyRackChanged();
        }

        static string SafeStr(object val) => val == null || val == DBNull.Value ? "" : val.ToString();
        static int SafeInt(object val) => val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);
    }
}
