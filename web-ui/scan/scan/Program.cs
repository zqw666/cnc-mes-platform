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
        static string GripperType = "fixed-single";   // fixed-single/fixed-double/quick-electrode/quick-tool/quick-pallet
        static bool IsFixedGripper = true;            // 是否固定爪
        static List<GripperConfig> Grippers = new List<GripperConfig>();

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

        // 扫描器配置
        static string ScannerType = "rfid"; // "rfid" 或 "rcd"
        static string RfidCom = "COM3";
        static string RcdIp = "192.168.1.100";
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
        static readonly string SqlSteelInfo     = "SELECT * FROM steel_part_info WHERE monitor_no = '{0}'";
        static readonly string SqlElectrodeInfo = "SELECT * FROM electrode_part_info WHERE monitor_no = '{0}'";

        static plc Plc;

        static void Main(string[] args)
        {
            LogHelper.Info( "【" + DisplayName + "】分包启动");

            try
            {
                LoadConfig();
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
                    LogHelper.Info( "开始扫描料位: " + pos);

                    // 发送扫描任务
                    if (!Plc.WriteDM(PlcDm, pos))
                    {
                        LogHelper.Error( "发送任务失败，料位: " + pos);
                        continue;
                    }
                    LogHelper.Info( "发送任务 " + pos + " 成功");

                    // 等待任务完成
                    int waitCount = 0;
                    while (!Plc.ReadWR(TaskFinishAddr) && waitCount < 300)
                    {
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

        static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));

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
                        LogHelper.Info( "Eman数据库配置: " + ehost + ":" + eport);
                    }

                    if (cfg.ContainsKey("database"))
                    {
                        var db = GetDict(cfg, "database");
                        string host = GetStr(db, "host", "localhost");
                        string dbname = GetStr(db, "database", "qianduantest");
                        string user = GetStr(db, "user", "root");
                        string pwd = GetStr(db, "password", "123456");
                        DbType = GetStr(db, "type", "mysql").ToLower();
                        if (DbType == "sqlserver")
                        {
                            int dbport = GetInt(db, "port", 1433);
                            ConnStr = $"Server={host},{dbport};Database={dbname};User Id={user};Password={pwd};";
                        }
                        else
                        {
                            int dbport = GetInt(db, "port", 3306);
                            ConnStr = $"Server={host};Port={dbport};Database={dbname};Uid={user};Pwd={pwd};CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
                        }
                    }

                    if (cfg.ContainsKey("plc"))
                    {
                        var plc = GetDict(cfg, "plc");
                        if (plc.ContainsKey("ip")) PlcIp = GetStr(plc, "ip", PlcIp);
                        if (plc.ContainsKey("port")) PlcPort = GetInt(plc, "port", PlcPort);
                        if (plc.ContainsKey("dm"))
                        {
                            var dm = GetDict(plc, "dm");
                            if (dm.ContainsKey("task_number")) PlcDm = GetInt(dm, "task_number", PlcDm);
                        }
                    }

                    // 夹爪类型（从 line.gripperType 读取）
                    if (cfg.ContainsKey("line"))
                    {
                        var line = GetDict(cfg, "line");
                        GripperType = GetStr(line, "gripperType", "fixed-single");
                        IsFixedGripper = GripperType == "fixed-single" || GripperType == "fixed-double";
                    }

                    // 快换夹爪列表
                    Grippers.Clear();
                    if (!IsFixedGripper && cfg.ContainsKey("grippers") && cfg["grippers"] != null)
                    {
                        var glist = cfg["grippers"];
                        IEnumerable<object> enumerable = null;
                        if (glist is System.Collections.ArrayList al) enumerable = al.Cast<object>();
                        else if (glist is object[] arr) enumerable = arr;
                        else if (glist is IEnumerable<object> ie) enumerable = ie;
                        if (enumerable != null)
                            foreach (var item in enumerable)
                            {
                                var g = item as Dictionary<string, object>;
                                if (g == null) continue;
                                Grippers.Add(new GripperConfig
                                {
                                    Id = GetStr(g, "id", ""),
                                    Type = GetStr(g, "type", "steel"),
                                    Name = GetStr(g, "name", ""),
                                    Number = GetStr(g, "number", ""),
                                    SlotPhotoSignal = GetStr(g, "slotPhotoSignal", ""),
                                    GrabTaskNo = GetStr(g, "grabTaskNo", ""),
                                    PlaceTaskNo = GetStr(g, "placeTaskNo", "")
                                });
                            }
                    }



                    if (cfg.ContainsKey("rfid_port"))
                        RfidCom = GetStr(cfg, "rfid_port", RfidCom);

                    // 扫码器配置
                    if (cfg.ContainsKey("scanner"))
                    {
                        var scanner = GetDict(cfg, "scanner");
                        if (scanner.ContainsKey("type"))
                            ScannerType = GetStr(scanner, "type", ScannerType).ToLower();
                        if (scanner.ContainsKey("rcd"))
                        {
                            var rcdCfg = GetDict(scanner, "rcd");
                            if (rcdCfg.ContainsKey("ip")) RcdIp = GetStr(rcdCfg, "ip", RcdIp);
                            if (rcdCfg.ContainsKey("port")) RcdPort = GetStr(rcdCfg, "port", RcdPort);
                            if (rcdCfg.ContainsKey("heartbeatInterval")) RcdHeartbeatInterval = GetInt(rcdCfg, "heartbeatInterval", RcdHeartbeatInterval);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn( "加载配置失败，使用默认配置: " + ex.Message);
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


        /// <summary>
        /// 置换夹爪流程（重写版）
        /// 1. 固定爪：判定无工件 → 完成
        /// 2. 快换爪：读取 DB robotmessage.clampposstatus
        ///    0→手上无夹爪→完成
        ///    1→手上有夹爪→判定是否有工件→无工件则放置夹爪→有工件则先放工件到料架再放夹爪
        /// </summary>
        static bool ChangeGripper()
        {
            LogHelper.Info( "开始置换夹爪流程...");

            if (IsFixedGripper)
            {
                LogHelper.Info( "固定爪模式，读取 robotmessage 判定工件...");
                string fixedWorkpiece = "";
                try
                {
                    using (DataTable dt = Mysql.ExeForDtl("SELECT workpiecename FROM robotmessage LIMIT 1"))
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

                if (!Plc.WriteDM(PlcDm, emptySlot))
                {
                    LogHelper.Error( "发送放置工件任务失败: " + emptySlot);
                    return false;
                }
                LogHelper.Info( "发送放置工件任务: " + emptySlot);
                if (!WaitForTaskFinish(300))
                {
                    LogHelper.Error( "放置工件任务超时");
                    return false;
                }

                try
                {
                    Mysql.ExecuteSql("UPDATE rack_administrate SET rfid='" + fixedWorkpiece + "', scanstatus=1, workstate=0, posstatus=2 WHERE id=" + emptySlot);
                    LogHelper.Info( "更新料位 " + emptySlot + " 为有料: " + fixedWorkpiece);
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
                using (DataTable dt = Mysql.ExeForDtl("SELECT clampname,clampstatus,clampposstatus,workpiecename FROM robotmessage LIMIT 1"))
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

                // 发送放置工件到料架任务
                int placeTask = emptySlot + 0; // 任务号 = 料位号
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
                    (clampType == "1" && g.Type == "steel") ||
                    (clampType == "2" && g.Type == "tool") ||
                    (clampType == "3" && g.Type == "electrode") ||
                    (clampType == "4" && g.Type == "pallet"))
                {
                    gripper = g; break;
                }
            }
            if (gripper == null)
            {
                LogHelper.Warn( "未找到类型为 " + clampType + " 的夹爪配置，使用第一个");
                if (Grippers.Count > 0) gripper = Grippers[0];
                else { LogHelper.Error( "无夹爪配置"); return false; }
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
                // 任务号 = 放置任务号 + 料位号
                int taskNo = ParseTaskNoBase(gripper.PlaceTaskNo) + (string.IsNullOrEmpty(gripper.Number) ? 0 : int.Parse(gripper.Number));
                LogHelper.Info( "发送放置夹爪任务: " + taskNo);
                if (!Plc.WriteDM(PlcDm, taskNo))
                {
                    LogHelper.Error( "发送放置夹爪任务失败");
                    return false;
                }
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
                using (DataTable dt = Mysql.ExeForDtl("SELECT id FROM rack_administrate WHERE posstatus=1 AND scanstatus=0 LIMIT 1"))
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
                // 判定机器人光电信号 - 抓取夹爪成功后更新robotmessage表
                if (Plc.ReadWR(RobotEleAddr))
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

                        // 判定料架类型，连接 eman 获取生产数据
                        string rackType = GetRackType(pos);
                        if (!string.IsNullOrEmpty(rackType))
                        {
                            var emanData = QueryEmanProduction(scanResult, rackType);
                            if (emanData != null)
                                LogHelper.Info( "获取eman生产数据成功: " + scanResult);
                        }

                        // 更新料位信息 - 扫描成功
                        string sql = $"UPDATE rack_administrate SET rfid='{scanResult}', scanstatus=1, workstate=0, posstatus=2 WHERE id={pos}";
                        if (!Mysql.ExecuteSql(sql))
                        {
                            LogHelper.Error( "更新料位 " + pos + " 信息失败");
                        }

                        // 抓取夹爪成功后，根据任务号更新robotmessage表
                        UpdateRobotMessage_GrabClamp(pos);

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

                    string sql = $"UPDATE rack_administrate SET rfid='', verifyid='', scanstatus=1, workstate=0, posstatus=1 WHERE id={pos}";
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

                // 订阅消息接收事件
                rcd.MessageSent += (sender, msg) =>
                {
                    receivedData = msg;
                    resetEvent.Set();
                };

                // 连接 RCD 扫码枪
                if (!rcd.Connect())
                {
                    LogHelper.Error( "RCD 扫码枪连接失败: " + RcdIp + ":" + RcdPort);
                    return "";
                }
                LogHelper.Info( "RCD 扫码枪连接成功");

                // 等待扫码结果，最多等待 5 秒
                bool gotResult = resetEvent.WaitOne(5000);
                if (gotResult && !string.IsNullOrEmpty(receivedData) && receivedData != "heartbeat")
                {
                    scanResult = receivedData.Trim();
                }
                else
                {
                    // 重试机制：重新发送心跳触发扫码（最多2次）
                    for (int i = 0; i < 2; i++)
                    {
                        resetEvent.Reset();
                        receivedData = "";
                        rcd.PauseHeart();
                        rcd.SendMess("scan");
                        rcd.ContinueHeart();
                        gotResult = resetEvent.WaitOne(3000);
                        if (gotResult && !string.IsNullOrEmpty(receivedData) && receivedData != "heartbeat")
                        {
                            scanResult = receivedData.Trim();
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
        static void NotifyRackUpdate(int position, string rackType, string status, string rfid, string message)
        {
            try
            {
                var data = new
                {
                    position = position,
                    rackType = rackType,
                    status = status,
                    rfid = rfid,
                    message = message
                };

                string body = Js.Serialize(data);
                byte[] bytes = Encoding.UTF8.GetBytes(body);

                var req = WebRequest.CreateHttp(ApiBase + "/api/rack/update");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 3000;

                using (var rs = req.GetRequestStream())
                    rs.Write(bytes, 0, bytes.Length);

                using (var resp = req.GetResponse())
                {
                    LogHelper.Info( $"UI更新通知已发送: 料位{position}, 状态{status}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn( "UI更新通知发送失败: " + ex.Message);
            }
        }

        static string SafeStr(object val) => val == null || val == DBNull.Value ? "" : val.ToString();
        static int SafeInt(object val) => val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);
    }
}
