// ============================================================================
// 扫描预设台流程 - SubProcess
// 1. 读取预设台光电信号，确认有铝板电极
// 2. 执行置换夹爪流程（与 scan 一致：固定爪清空工件 / 快换爪清空夹爪与工件）
// 3. 控制机器人去预设台进行扫描（扫描配置/扫描方法/读取方法与 scan 一致）
// 4. 更新 prerack 表（id=1）：rfid、workpiecename、workstate、posstatus
// 5. 获取铝板电极料架（racktype=5）空料位编号
// 6. 发送"从预设台抓取铝板电极"任务（plc.signals.preset.grabTaskNo）
// 7. 执行成功后更新 robotmessage 表工件信息
// 8. 清空 prerack 表工件信息和料位状态
// 9. 发送"放置铝板电极料架空料位"任务（racks[]aluminum.placeStartTaskNo + 料位号）
// 10. 执行成功后更新铝板电极料架对应料位工件信息/料位信息/工作状态
// 11. 清空 robotmessage 表工件信息
// ============================================================================
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
using Serialoperate;
using Rcd;

namespace scan_preset
{
    class Program
    {
        static readonly string ApiBase = "http://localhost:8888";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static readonly string StepName = "scan_preset";
        static readonly string DisplayName = "扫描预设台流程";

        // PLC 配置
        static string PlcIp = "192.168.1.1";
        static int PlcPort = 9600;
        static int PlcDm = 100;
        static string TaskStartAddr = "204.00";
        static string TaskFinishAddr = "205.00";
        static string RobotEleAddr = "206.00";

        // 预设台信号配置（plc.signals.preset）
        static string PresetPhotoAddr = "502.00";
        static int PresetScanTaskNo = 0;   // 机器人去预设台扫描任务号
        static int PresetGrabTaskNo = 0;   // 机器人从预设台抓取任务号
        static int PresetReturnHomeTaskNo = 0;   // 机器人回原点任务号

        // 铝板电极料架配置（racks[] type=aluminum）
        static int AluminumPlaceBase = 0;  // 铝板电极料架放置开始任务号（放置任务号 = base + 料位号）
        static bool HasAluminumRackConfig = false;

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
            public string Type;           // steel/tool/electrode/pallet/aluminum
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

        // eman 生产数据查询语句（{0}=监控编号/RFID）—— 铝板电极按电极方式查询
        static readonly string SqlElectrodeInfo = "SELECT MouldPart.verifyID FROM Mould INNER JOIN MouldPart ON Mould.mouldID=MouldPart.mouldID WHERE MouldPart.ID='{0}'";

        static plc Plc;

        // 停止标志位：前端停止按钮触发后，后端创建 stop.flag，子程序检测到即终止
        static readonly string StopFlagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stop.flag");
        static bool IsStopRequested() { return File.Exists(StopFlagFile); }

        static void Main(string[] args)
        {
            LogHelper.Info("【" + StepName + "/" + DisplayName + "】分包启动");

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
                    LogHelper.Error("PLC 连接失败: " + PlcIp);
                    return;
                }
                LogHelper.Info("PLC 连接成功");

                // 1. 判断预设台光电信号（有料才继续，无料直接退出，等待下轮）
                if (!WaitPresetPhotoOn())
                {
                    LogHelper.Info("预设台无料，退出扫描预设台流程");
                    return;
                }

                // 2. 扫描铝板电极前执行置换夹爪流程（与 scan 相同）
                //    确保机器人没有夹爪/工件才能执行扫描预设台、抓取预设台、放置铝板电极料架任务
                if (!ChangeGripper())
                {
                    LogHelper.Error("置换夹爪失败，退出扫描预设台流程");
                    return;
                }

                // 置换夹爪期间料可能被取走，再次确认
                if (!WaitPresetPhotoOn())
                {
                    LogHelper.Warn("预设台料已被取走，退出扫描预设台流程");
                    return;
                }

                // 3. 控制机器人去预设台进行扫描
                if (PresetScanTaskNo <= 0)
                {
                    LogHelper.Error("预设台扫描任务号未配置 (plc.signals.preset.scanTaskNo)，退出");
                    return;
                }
                LogHelper.Info("发送预设台扫描任务: " + PresetScanTaskNo + "（超时 40 秒）");
                if (!SendRobotTask(PresetScanTaskNo, 400))
                {
                    LogHelper.Error("预设台扫描任务执行失败，回原点后退出");
                    ReturnRobotHome();
                    return;
                }

                // 4. 与 scan 相同：判定机器人光电判断预设台有无铝板电极，有料则扫码识别
                string rfid = "";
                string workpieceName = "";
                if (!ScanPreset(out rfid, out workpieceName))
                {
                    LogHelper.Error("预设台扫描未成功，回原点后退出扫描预设台流程");
                    ReturnRobotHome();
                    return;
                }

                // 5. 扫描成功后获取铝板电极料架空料位编号
                int emptySlot = GetEmptyAluminumSlot();
                if (emptySlot == 0)
                {
                    LogHelper.Error("铝板电极料架无空料位，退出扫描预设台流程");
                    return;
                }
                LogHelper.Info("铝板电极料架空料位: " + emptySlot);

                // 6. 发送"从预设台抓取铝板电极"任务
                if (PresetGrabTaskNo <= 0)
                {
                    LogHelper.Error("预设台抓取任务号未配置 (plc.signals.preset.grabTaskNo)，退出");
                    return;
                }
                LogHelper.Info("发送预设台抓取任务: " + PresetGrabTaskNo);
                if (!SendRobotTask(PresetGrabTaskNo))
                {
                    LogHelper.Error("预设台抓取任务执行失败，退出");
                    return;
                }

                // 7. 执行成功后更新 robotmessage 中的工件信息
                //    robotmessage 无 rfid 列；eman 查无 verifyID 时以 rfid 兜底，便于其他流程判断手上有料
                string robotWp = string.IsNullOrEmpty(workpieceName) ? rfid : workpieceName;
                UpdateRobotWorkpiece(robotWp);
                LogHelper.Success("已从预设台抓取铝板电极: " + rfid);

                // 8. 清空 prerack 表的工件信息和料位状态
                ClearPrerack();

                // 9. 发送"放置铝板电极料架空料位"任务
                int placeTaskNo = AluminumPlaceBase + emptySlot;
                LogHelper.Info("发送放置铝板电极任务: " + placeTaskNo + " (料位 " + emptySlot + ")");
                if (!SendRobotTask(placeTaskNo))
                {
                    LogHelper.Error("放置铝板电极任务执行失败，退出");
                    return;
                }

                // 10. 执行成功后更新铝板电极料架对应料位工件信息/料位信息/工作状态
                UpdateAluminumSlot(emptySlot, rfid, workpieceName);

                // 11. 清空 robotmessage 上的工件信息和料位状态
                ClearRobotWorkpiece();
                LogHelper.Success("扫描预设台流程完成：RFID=" + rfid + "，放入铝板电极料架料位 " + emptySlot);
            }
            catch (Exception ex)
            {
                LogHelper.Error("执行异常: " + ex.Message);
            }
            finally
            {
                if (Plc != null)
                    Plc.close();
            }

            LogHelper.Info("【" + StepName + "/" + DisplayName + "】分包结束");
        }

        // ====================================================================
        // 预设台相关
        // ====================================================================

        /// <summary>等待预设台光电信号为有料（最多约 5 秒），返回是否有料</summary>
        static bool WaitPresetPhotoOn()
        {
            if (string.IsNullOrEmpty(PresetPhotoAddr))
            {
                LogHelper.Error("预设台光电信号地址未配置 (plc.signals.preset.photo)");
                return false;
            }
            int wait = 0;
            while (wait < 25)
            {
                if (IsStopRequested()) { LogHelper.Warn("收到停止信号，退出扫描预设台流程"); return false; }
                try
                {
                    if (Plc.ReadWR(PresetPhotoAddr))
                        return true;
                }
                catch (Exception ex) { LogHelper.Warn("读取预设台光电信号失败: " + ex.Message); }
                Thread.Sleep(200);
                wait++;
            }
            return false;
        }

        /// <summary>
        /// 扫描预设台上的铝板电极（方法与 scan 相同）：
        /// 读机器人光电判断有料 → 按 scanner.type 读 RFID/RCD → 空夹具跳过 eman；
        /// 否则按电极方式查 eman 取 verifyID → 更新 prerack(id=1)
        /// </summary>
        static bool ScanPreset(out string rfid, out string workpieceName)
        {
            rfid = "";
            workpieceName = "";

            if (!string.IsNullOrEmpty(RobotEleAddr) && Plc.ReadWR(RobotEleAddr))
            {
                LogHelper.Warn("预设台未检测到工件（机器人光电无料）");
                return false;
            }

            string scanResult = ScannerType == "rcd" ? ScanByRCD() : ScanByRFID();
            if (string.IsNullOrEmpty(scanResult) || scanResult == "null")
            {
                LogHelper.Error("预设台扫描失败，未识别到RFID，请人工处理");
                return false;
            }

            // 空夹具识别：扫描到的 rfid 含 EMPTY（如 EMPTY1~EMPTY4）即视为空夹具，跳过 eman
            bool isEmptyFixture = scanResult.IndexOf("EMPTY", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isEmptyFixture)
            {
                LogHelper.Info("识别到空夹具 " + scanResult + "，跳过 eman 查询");
                workpieceName = scanResult;
            }
            else
            {
                // 铝板电极按电极方式查询 eman 获取生产数据
                var emanData = QueryEmanProduction(scanResult, "electrode");
                if (emanData != null)
                {
                    foreach (var kv in emanData)
                        if (kv.Key.Equals("verifyID", StringComparison.OrdinalIgnoreCase))
                        { workpieceName = kv.Value; break; }
                    LogHelper.Info("获取eman生产数据成功: " + scanResult);
                }
                else
                {
                    LogHelper.Warn("Eman未查询到生产数据，workpiecename 置空: " + scanResult);
                }
            }

            rfid = scanResult;

            // 更新 prerack 表：rfid、workpiecename、workstate=0（待加工）、posstatus=2（有料）
            if (!UpdatePrerack(rfid, workpieceName, 0, 2))
            {
                LogHelper.Error("更新 prerack 表失败");
                return false;
            }
            LogHelper.Info("更新 prerack 表成功: rfid=" + rfid + ", workpiecename=" +
                (string.IsNullOrEmpty(workpieceName) ? "(空)" : workpieceName) + ", posstatus=2");
            return true;
        }

        // ====================================================================
        // 置换夹爪流程（与 scan 保持一致）
        // ====================================================================

        static bool ChangeGripper()
        {
            LogHelper.Info("开始置换夹爪流程...");

            if (IsFixedGripper)
            {
                LogHelper.Info("固定爪模式，读取 robotmessage 判定工件...");
                string fixedWorkpiece = "";
                try
                {
                    using (DataTable dt = Mysql.ExeForDtl("SELECT workpiecename FROM robotmessage where id =1"))
                        if (dt != null && dt.Rows.Count > 0)
                            fixedWorkpiece = SafeStr(dt.Rows[0]["workpiecename"]);
                }
                catch (Exception ex) { LogHelper.Error("读取 robotmessage 失败: " + ex.Message); return false; }

                bool fixedHasWorkpiece = !string.IsNullOrEmpty(fixedWorkpiece) && fixedWorkpiece != "NULL";

                if (!fixedHasWorkpiece)
                {
                    LogHelper.Info("固定爪无工件，置换夹爪完成");
                    UpdateRobotMessage_ChangeGripper("固定夹爪", 1);
                    return true;
                }

                // 有工件：放回料架空位
                LogHelper.Info("固定爪有工件: " + fixedWorkpiece + "，放置回料架...");
                int emptySlot = GetEmptyRackSlot();
                if (emptySlot == 0)
                {
                    LogHelper.Error("没有可用的料架空位");
                    return false;
                }

                var placeRack = GetRackByType(GetRackType(emptySlot));
                int placeStartNo = 0;
                if (placeRack != null && !string.IsNullOrEmpty(placeRack.PlaceStartTaskNo))
                    placeStartNo = ParseTaskNoBase(placeRack.PlaceStartTaskNo);
                if (!Plc.WriteDM(PlcDm, placeStartNo + emptySlot))
                {
                    LogHelper.Error("发送放置工件任务失败: " + emptySlot);
                    return false;
                }

                if (!Plc.WriteWR(TaskStartAddr, 1))
                {
                    LogHelper.Error("发送任务开始失败: " + emptySlot);
                    return false;
                }
                LogHelper.Info("发送放置工件任务: " + emptySlot);
                Thread.Sleep(300);
                Plc.WriteWR(TaskStartAddr, 0);
                if (!WaitForTaskFinish(300))
                {
                    LogHelper.Error("放置工件任务超时");
                    return false;
                }

                try
                {
                    Mysql.ExecuteSql("UPDATE rack_administrate SET rfid='" + fixedWorkpiece + "', scanstatus=1, workstate=0, posstatus=2 WHERE id=" + emptySlot);
                    LogHelper.Info("更新料位 " + emptySlot + " 为有料: " + fixedWorkpiece);
                    LogHelper.NotifyRackChanged();
                }
                catch (Exception ex) { LogHelper.Warn("更新料架失败: " + ex.Message); }

                ClearRobotWorkpiece();
                UpdateRobotMessage_ChangeGripper("固定夹爪", 1);
                LogHelper.Success("固定爪置换夹爪完成");
                return true;
            }

            // 快换爪：读取 robotmessage 表
            LogHelper.Info("快换爪模式，读取 robotmessage 表...");
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
            catch (Exception ex) { LogHelper.Error("读取 robotmessage 失败: " + ex.Message); return false; }

            LogHelper.Info(string.Format("robotmessage: clampname={0}, clampposstatus={1}, workpiece={2}",
                clampName, clampposstatus, string.IsNullOrEmpty(workpieceName) ? "(空)" : workpieceName));

            // 手上无夹爪
            if (clampposstatus == 0)
            {
                LogHelper.Info("机器人手上无夹爪，置换夹爪流程结束");
                return true;
            }

            // 手上有夹爪
            bool hasWorkpiece = !string.IsNullOrEmpty(workpieceName) && workpieceName != "NULL";

            if (!hasWorkpiece)
            {
                // 无工件：直接放置夹爪
                LogHelper.Info("夹爪上无工件，准备放置夹爪...");
                return PlaceGripperToSlot(clampType);
            }
            else
            {
                // 有工件：先放工件到料架，再放夹爪
                LogHelper.Info("夹爪上有工件: " + workpieceName + "，先放置工件到料架...");

                int emptySlot = GetEmptyRackSlot();
                if (emptySlot == 0)
                {
                    LogHelper.Error("没有可用的料架空位");
                    return false;
                }

                var placeRack2 = GetRackByType(GetRackType(emptySlot));
                int placeStartNo2 = 0;
                if (placeRack2 != null && !string.IsNullOrEmpty(placeRack2.PlaceStartTaskNo))
                    placeStartNo2 = ParseTaskNoBase(placeRack2.PlaceStartTaskNo);
                int placeTask = placeStartNo2 + emptySlot;
                if (!Plc.WriteDM(PlcDm, placeTask))
                {
                    LogHelper.Error("发送放置工件任务失败: " + placeTask);
                    return false;
                }
                LogHelper.Info("发送放置工件任务: " + placeTask);

                if (!WaitForTaskFinish(300))
                {
                    LogHelper.Error("放置工件任务超时");
                    return false;
                }

                try
                {
                    string updateRack = "UPDATE rack_administrate SET rfid='" + workpieceName + "', scanstatus=1, workstate=0, posstatus=2 WHERE id=" + emptySlot;
                    Mysql.ExecuteSql(updateRack);
                    LogHelper.Info("更新料位 " + emptySlot + " 为有料: " + workpieceName);
                    LogHelper.NotifyRackChanged();
                }
                catch (Exception ex) { LogHelper.Warn("更新料架失败: " + ex.Message); }

                ClearRobotWorkpiece();

                LogHelper.Info("工件已放置，准备放置夹爪...");
                return PlaceGripperToSlot(clampType);
            }
        }

        /// <summary>
        /// 放置夹爪到对应料位
        /// </summary>
        static bool PlaceGripperToSlot(string clampType)
        {
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
                LogHelper.Error("未找到类型为 " + clampType + " 的夹爪配置，无法放置当前夹爪，请人工处理（已配置: " + string.Join(",", Grippers.ConvertAll(x => x.Type + "/" + x.Name).ToArray()) + "）");
                return false;
            }

            LogHelper.Info("夹爪: " + gripper.Name + " (类型=" + gripper.Type + ", 光电=" + gripper.SlotPhotoSignal + ")");

            // 读取料位光电信号
            if (!string.IsNullOrEmpty(gripper.SlotPhotoSignal))
            {
                bool slotOccupied = Plc.ReadWR(gripper.SlotPhotoSignal);
                LogHelper.Info("夹爪料位光电信号: " + (slotOccupied ? "有异物" : "空"));
                if (slotOccupied)
                {
                    LogHelper.Error("夹爪料位有异物，无法放置夹爪，需人工处理");
                    return false;
                }
            }

            // 发送放置夹爪任务
            if (!string.IsNullOrEmpty(gripper.PlaceTaskNo))
            {
                int taskNo = ParseTaskNoBase(gripper.PlaceTaskNo);
                LogHelper.Info("发送放置夹爪任务: " + taskNo);
                if (!Plc.WriteDM(PlcDm, taskNo))
                {
                    LogHelper.Error("发送放置夹爪任务失败");
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
                    LogHelper.Error("放置夹爪任务超时");
                    return false;
                }
            }
            else
            {
                LogHelper.Warn("未配置放置夹爪任务号，跳过发送任务");
            }

            ClearRobotClamp();
            LogHelper.Success("置换夹爪流程完成，机器人手上已清空");
            return true;
        }

        // ====================================================================
        // 机器人任务公共方法
        // ====================================================================

        static bool SendRobotTask(int taskNo, int timeoutTicks = 300)
        {
            if (!Plc.WriteDM(PlcDm, taskNo))
            {
                LogHelper.Error("发送任务号失败: " + taskNo);
                return false;
            }
            LogHelper.Info("发送任务号 " + taskNo + " 成功");
            if (!Plc.WriteWR(TaskStartAddr, 1))
            {
                LogHelper.Error("发送任务开始失败: " + taskNo);
                return false;
            }
            Thread.Sleep(300);
            Plc.WriteWR(TaskStartAddr, 0);
            if (!WaitForTaskFinish(timeoutTicks))
            {
                LogHelper.Error("等待任务完成超时，任务号: " + taskNo);
                return false;
            }
            LogHelper.Success("执行任务 " + taskNo + " 成功");
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

        /// <summary>
        /// 机器人回原点（发送回原点任务号）。扫描失败后调用，避免机器人停留在预设台位置与机床干涉。
        /// </summary>
        static bool ReturnRobotHome()
        {
            if (PresetReturnHomeTaskNo <= 0)
            {
                LogHelper.Warn("回原点任务号未配置 (plc.signals.preset.returnHomeTaskNo)，跳过回原点");
                return false;
            }
            LogHelper.Info("发送回原点任务: " + PresetReturnHomeTaskNo);
            if (!SendRobotTask(PresetReturnHomeTaskNo, 400))
            {
                LogHelper.Error("回原点任务执行失败，机器人可能未回到原点，需人工处理！");
                return false;
            }
            LogHelper.Success("机器人已回到原点");
            return true;
        }

        // ====================================================================
        // 扫描方法（与 scan 一致）
        // ====================================================================

        /// <summary>使用串口 RFID 扫描</summary>
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

        /// <summary>使用 RCD 扫码枪扫描（TCP连接）</summary>
        static string ScanByRCD()
        {
            string scanResult = "";
            RCD_100 rcd = null;

            try
            {
                rcd = new RCD_100(RcdIp, RcdPort, RcdHeartbeatInterval);

                // 连接 RCD 扫码枪
                if (!rcd.Connect())
                {
                    LogHelper.Error("RCD 扫码枪连接失败: " + RcdIp + ":" + RcdPort);
                    return "";
                }
                LogHelper.Info("RCD 扫码枪连接成功");
                rcd.MessageSent += rcd.returnMsg;
                rcd.SendMess("start");
                scanResult = WaitMsg(rcd);
                if (string.IsNullOrEmpty(scanResult))
                {
                    // 重试机制：重新发送心跳触发扫码（最多2次）
                    for (int i = 0; i < 2; i++)
                    {
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
                LogHelper.Error("RCD 扫描异常: " + ex.Message);
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

        /// <summary>等待 RCD 返回信息</summary>
        static public string WaitMsg(RCD_100 rcd)
        {
            DateTime startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < 3000)
            {
                if (!string.IsNullOrEmpty(rcd.DJId))
                    break;
                Thread.Sleep(50);
            }
            return rcd.DJId;
        }

        // ====================================================================
        // 数据库更新方法
        // ====================================================================

        /// <summary>
        /// 从 eman 数据库获取生产数据（铝板电极按电极方式查询 verifyID）
        /// </summary>
        static Dictionary<string, string> QueryEmanProduction(string rfid, string rackType)
        {
            if (!EmanEnabled || string.IsNullOrEmpty(EmanConnStr))
            {
                LogHelper.Warn("Eman数据库未配置，跳过生产数据查询");
                return null;
            }
            try
            {
                var emanDb = CommonFactory.CreateInstance(CommonData.sql, EmanConnStr);
                string sql = string.Format(SqlElectrodeInfo, rfid);
                LogHelper.Info("查询Eman(" + rackType + "): " + rfid);
                using (DataTable dt = emanDb.ExeForDtl(sql))
                {
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        var result = new Dictionary<string, string>();
                        foreach (DataColumn col in dt.Columns)
                            result[col.ColumnName] = SafeStr(dt.Rows[0][col.ColumnName]);
                        LogHelper.Success("Eman查询成功，字段数: " + result.Count);
                        return result;
                    }
                }
                LogHelper.Warn("Eman未找到记录: " + rfid);
                return null;
            }
            catch (Exception ex)
            {
                LogHelper.Error("Eman查询失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>更新 prerack 表（单行 id=1，不存在则插入）</summary>
        static bool UpdatePrerack(string rfid, string workpieceName, int workstate, int posstatus)
        {
            try
            {
                string sql = UpsertSql("prerack",
                    "id, rfid, workpiecename, workstate, posstatus",
                    "1, " + SqlStr(rfid) + ", " + SqlStr(workpieceName) + ", " + workstate + ", " + posstatus,
                    "rfid = " + SqlStr(rfid) + ", workpiecename = " + SqlStr(workpieceName) + ", workstate = " + workstate + ", posstatus = " + posstatus,
                    "id", "1");
                return Mysql.ExecuteSql(sql);
            }
            catch (Exception ex)
            {
                LogHelper.Error("更新 prerack 表异常: " + ex.Message);
                return false;
            }
        }

        /// <summary>清空 prerack 表工件信息和料位状态（保留行 id=1）</summary>
        static void ClearPrerack()
        {
            try
            {
                string sql = UpsertSql("prerack",
                    "id, rfid, workpiecename, workstate, posstatus",
                    "1, NULL, NULL, 0, 0",
                    "rfid = NULL, workpiecename = NULL, workstate = 0, posstatus = 0",
                    "id", "1");
                if (Mysql.ExecuteSql(sql))
                    LogHelper.Info("清空 prerack 表成功: rfid=NULL, workpiecename=NULL, posstatus=0");
                else
                    LogHelper.Error("清空 prerack 表失败");
            }
            catch (Exception ex)
            {
                LogHelper.Error("清空 prerack 表异常: " + ex.Message);
            }
        }

        /// <summary>更新 robotmessage 工件信息（手上持有铝板电极）</summary>
        static void UpdateRobotWorkpiece(string workpiecename)
        {
            try
            {
                int taskId = 1;
                string wp = SqlStr(workpiecename);
                string sql = UpsertSql("robotmessage",
                    "id, workpiecename, workpiecestatus",
                    taskId + ", " + wp + ", 0",
                    "workpiecename = " + wp + ", workpiecestatus = 0",
                    "id", taskId.ToString());
                if (Mysql.ExecuteSql(sql))
                    LogHelper.Info("更新robotmessage表成功 - 抓取工件: workpiecename=" + workpiecename + ", workpiecestatus=0");
                else
                    LogHelper.Error("更新robotmessage表失败 - 抓取工件");
            }
            catch (Exception ex)
            {
                LogHelper.Error("更新robotmessage表异常 - 抓取工件: " + ex.Message);
            }
        }

        /// <summary>更新铝板电极料架对应料位：工件信息 + 料位信息 + 工作状态</summary>
        static void UpdateAluminumSlot(int slot, string rfid, string workpieceName)
        {
            try
            {
                string wp = SqlStr(workpieceName);
                string sql = "UPDATE rack_administrate SET rfid = " + SqlStr(rfid) + ", workpiecename = " + wp
                    + ", scanstatus = 1, workstate = 0, posstatus = 2 WHERE id = " + slot + " AND racktype = 5";
                if (Mysql.ExecuteSql(sql))
                {
                    LogHelper.Info("更新铝板电极料位 " + slot + " 为有料: rfid=" + rfid);
                    LogHelper.NotifyRackChanged();
                }
                else
                {
                    LogHelper.Error("更新铝板电极料位 " + slot + " 失败");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("更新铝板电极料位异常: " + ex.Message);
            }
        }

        /// <summary>清空 robotmessage 工件信息与料位状态</summary>
        static void ClearRobotWorkpiece()
        {
            try { Mysql.ExecuteSql("UPDATE robotmessage SET workpiecename=NULL, workpiecestatus=0"); }
            catch (Exception ex) { LogHelper.Warn("清空工件信息失败: " + ex.Message); }
        }

        static void ClearRobotClamp()
        {
            try { Mysql.ExecuteSql("UPDATE robotmessage SET clampname=NULL, clampposstatus=0, workpiecename=NULL, workpiecestatus=0"); }
            catch (Exception ex) { LogHelper.Warn("清空夹爪信息失败: " + ex.Message); }
        }

        // ====================================================================
        // 料位/料架查询
        // ====================================================================

        /// <summary>通用空料位（固定爪/快换爪有工件放回时使用，与 scan 一致）</summary>
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

        /// <summary>获取铝板电极料架空料位（racktype=5 且 posstatus=1 的最小编号）</summary>
        static int GetEmptyAluminumSlot()
        {
            try
            {
                using (DataTable dt = Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE racktype=5 AND posstatus=1 ORDER BY id"))
                {
                    if (dt != null && dt.Rows.Count > 0)
                        return Convert.ToInt32(dt.Rows[0]["id"]);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("查询铝板电极料架空料位失败: " + ex.Message);
            }
            return 0;
        }

        /// <summary>根据料位号获取料架类型（1=electrode, 2=steel, 其余默认 steel）</summary>
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
                        return "steel";
                    }
                }
            }
            catch (Exception ex) { LogHelper.Warn("获取料架类型失败: " + ex.Message); }
            return "steel";
        }

        // ====================================================================
        // 配置读取
        // ====================================================================

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
                    // 预设台信号：光电、扫描任务号、抓取任务号
                    var presetSig = ConfigClient.GetDict(signals, "preset");
                    if (presetSig.Count > 0)
                    {
                        PresetPhotoAddr = ConfigClient.GetStr(presetSig, "photo", "");
                        PresetScanTaskNo = ParseTaskNoBase(ConfigClient.GetStr(presetSig, "scanTaskNo", ""));
                        PresetGrabTaskNo = ParseTaskNoBase(ConfigClient.GetStr(presetSig, "grabTaskNo", ""));
                        PresetReturnHomeTaskNo = ParseTaskNoBase(ConfigClient.GetStr(presetSig, "returnHomeTaskNo", ""));
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

                if (!HasAluminumRackConfig)
                    LogHelper.Warn("配置中未找到铝板电极料架(aluminum)，无法获取放置任务号 (racks[].placeStartTaskNo)，将导致放置阶段失败");
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
                AluminumPlaceBase = 0;
                HasAluminumRackConfig = false;
                if (cfg == null || !cfg.ContainsKey("racks") || cfg["racks"] == null) return;
                var racksRaw = cfg["racks"];
                if (racksRaw is System.Collections.ArrayList al)
                {
                    foreach (var it in al)
                        if (it is Dictionary<string, object> rk) AddRack(ParseRack(rk));
                }
                else if (racksRaw is object[] arr)
                {
                    foreach (var it in arr)
                        if (it is Dictionary<string, object> rk) AddRack(ParseRack(rk));
                }
            }
            catch (Exception ex) { LogHelper.Warn("加载料架配置失败: " + ex.Message); }
        }

        static void AddRack(RackConfig rack)
        {
            Racks.Add(rack);
            // 记录铝板电极料架放置开始任务号
            if (string.Equals((rack.Type ?? "").Trim(), "aluminum", StringComparison.OrdinalIgnoreCase))
            {
                HasAluminumRackConfig = true;
                AluminumPlaceBase = ParseTaskNoBase(rack.PlaceStartTaskNo);
                LogHelper.Info("铝板电极料架配置: name=" + rack.Name + ", placeStartTaskNo=" + rack.PlaceStartTaskNo
                    + " -> 放置基数=" + AluminumPlaceBase);
            }
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

        static void AddGripper(object item)
        {
            var g = item as Dictionary<string, object>;
            if (g == null) return;
            Grippers.Add(new GripperConfig
            {
                Id = ConfigClient.GetStr(g, "id", ""),
                Type = ConfigClient.GetStr(g, "type", "steel"),
                Name = ConfigClient.GetStr(g, "name", ""),
                Number = ConfigClient.GetStr(g, "number", ""),
                SlotPhotoSignal = ConfigClient.GetStr(g, "slotPhotoSignal", ""),
                GrabTaskNo = ConfigClient.GetStr(g, "grabTaskNo", ""),
                PlaceTaskNo = ConfigClient.GetStr(g, "placeTaskNo", "")
            });
        }

        /// <summary>从 config.json 读取 eman 数据库配置</summary>
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
                LogHelper.Warn("读取 eman 数据库配置失败: " + ex.Message);
            }
        }

        // ====================================================================
        // robotmessage 更新方法（置换夹爪，与 scan 一致）
        // ====================================================================

        /// <summary>更新robotmessage表 - 置换夹爪任务后</summary>
        static void UpdateRobotMessage_ChangeGripper(string clampName, int clampStatus)
        {
            try
            {
                int taskId = 1;
                string sql = UpsertSql("robotmessage",
                    "id, clampname, clampstatus, clampposstatus, workpiecename, workpiecestatus",
                    taskId + ", '" + clampName + "', " + clampStatus + ", 1, NULL, 0",
                    "clampname = '" + clampName + "', clampstatus = " + clampStatus + ", clampposstatus = 1, workpiecename = NULL, workpiecestatus = 0",
                    "id", taskId.ToString());
                if (Mysql.ExecuteSql(sql))
                    LogHelper.Info("更新robotmessage表成功 - 置换夹爪: clampname=" + clampName + ", clampstatus=" + clampStatus + ", clampposstatus=1");
                else
                    LogHelper.Error("更新robotmessage表失败 - 置换夹爪");
            }
            catch (Exception ex)
            {
                LogHelper.Error("更新robotmessage表异常: " + ex.Message);
            }
        }

        // ====================================================================
        // 通用工具
        // ====================================================================

        /// <summary>生成兼容 MySQL 和 SQL Server 的 Upsert SQL</summary>
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

        static string SqlStr(string val)
        {
            if (string.IsNullOrEmpty(val)) return "NULL";
            return "'" + val.Replace("'", "''") + "'";
        }

        static int ParseTaskNoBase(string taskNo)
        {
            if (string.IsNullOrEmpty(taskNo)) return 0;
            string s = taskNo.ToUpper().Replace("D", "").Replace("DM", "").Trim();
            int.TryParse(s, out int r);
            return r;
        }

        static string SafeStr(object val) => val == null || val == DBNull.Value ? "" : val.ToString();
        static int SafeInt(object val) => val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);
    }
}
