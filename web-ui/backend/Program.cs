using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using DataCommonOperate;

namespace MachineMonitor
{
    class Program
    {
        // ==================== 配置 ====================
        // 自适应：始终以 MachineMonitor.exe 所在目录为基准
        static readonly string ExeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        static readonly string ConfigPath;
        static readonly string SubExeDir;

        static readonly string ConnStr;
        static readonly string DbType; // "mysql" 或 "sqlserver"
        static readonly int Port;
        static readonly int LogCapacity;
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();

        // ====================全局状态====================
        static readonly ConcurrentQueue<LogEntry> AllLogs = new ConcurrentQueue<LogEntry>();
        static volatile bool WorkflowRunning = false;
        static volatile bool WorkflowPaused = false;
        static CancellationTokenSource WorkflowCts;
        static readonly object LogLock = new object();

        // ====================流程显示名映射====================
        static readonly Dictionary<string, string> StepDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "rackoperate", "料架操作"},
            {"scan", "扫描"},
            {"preparematerial", "备料"},
            {"loadmachine", "上机"},
            {"unloadmachine", "下机"},
            {"unloadmaterial", "下料"},
            {"materialknife", "备刀"},
            {"changemachine", "换机"},
            {"changeknife", "换刀"}
        };

        static Program()
        {
            // 自适应：config.json 始终在 exe 同目录查找
            var p = Path.Combine(ExeDir, "config.json");
            ConfigPath = File.Exists(p) ? p : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            SubExeDir = Path.Combine(ExeDir, "subs");

            var cfg = File.Exists(ConfigPath)
                ? Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath))
                : new Dictionary<string, object>();

            var db = GetDict(cfg, "database");
            DbType = GetStr(db, "type", "mysql").ToLower();
            string dbName = GetStr(db, "database", "qianduantest");

            // 根据数据库类型构建连接字符串（不含 Database，用于创建数据库）
            if (DbType == "sqlserver")
            {
                ConnStr = $"Server={GetStr(db, "host", "localhost")},{GetInt(db, "port", 1433)};" +
                          $"Database={dbName};" +
                          $"User Id={GetStr(db, "user", "sa")};" +
                          $"Password={GetStr(db, "password", "123456")};";
                // 连接 master 检查/创建数据库
                string masterConnStr = $"Server={GetStr(db, "host", "localhost")},{GetInt(db, "port", 1433)};" +
                          $"Database=master;" +
                          $"User Id={GetStr(db, "user", "sa")};" +
                          $"Password={GetStr(db, "password", "123456")};";
                EnsureSqlServerDatabase(masterConnStr, dbName);
            }
            else
            {
                string mysqlCharset = GetStr(db, "charset", "utf8mb4");
                // MySql.Data 8.0.20 连接 MySQL 8.0+ 时的 KeyNotFoundException  workaround
                // 使用 CharacterSet 而非 CharSet，并添加 AllowPublicKeyRetrieval
                // MySql.Data 8.0.20 连接 MySQL 8.x 的兼容性修复：
                // 1. SslMode=None 跳过 SSL 协商（避免 KeyNotFoundException）
                // 2. CharacterSet=utf8mb4 强制客户端字符集（避免 utf8mb3 NotSupportedException）
                // 3. ConnectionReset=False 防止连接重置时重新协商字符集
                ConnStr = $"Server={GetStr(db, "host", "localhost")};" +
                          $"Port={GetInt(db, "port", 3306)};" +
                          $"Database={dbName};" +
                          $"Uid={GetStr(db, "user", "root")};" +
                          $"Pwd={GetStr(db, "password", "123456")};" +
                          $"CharacterSet=utf8mb4;" +
                          $"SslMode=None;" +
                          $"AllowPublicKeyRetrieval=True;" +
                          $"ConnectionReset=False;";
                // 连接时不含 Database，检查/创建数据库
                string noDbConnStr = $"Server={GetStr(db, "host", "localhost")};" +
                          $"Port={GetInt(db, "port", 3306)};" +
                          $"Uid={GetStr(db, "user", "root")};" +
                          $"Pwd={GetStr(db, "password", "123456")};" +
                          $"CharacterSet=utf8mb4;" +
                          $"SslMode=None;" +
                          $"AllowPublicKeyRetrieval=True;" +
                          $"ConnectionReset=False;";
                EnsureMySqlDatabase(noDbConnStr, dbName);
            }
            
            Port = GetInt(GetDict(cfg, "api"), "port", 8888);
            LogCapacity = GetInt(GetDict(cfg, "main"), "logCapacity", 500);
        }

        /// <summary>
        /// 确保 MySQL 数据库存在，不存在则创建
        /// </summary>
        static void EnsureMySqlDatabase(string baseConnStr, string dbName)
        {
            try
            {
                Console.WriteLine($"[DB] 尝试连接 MySQL (无数据库) 创建 '{dbName}'...");
                Console.WriteLine($"[DB] 连接字符串: {baseConnStr}");
                var dbServer = CommonFactory.CreateInstance(CommonData.mysql, baseConnStr);
                string sql = $"CREATE DATABASE IF NOT EXISTS `{dbName}` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
                dbServer.ExecuteSql(sql);
                Console.WriteLine($"[DB] MySQL 数据库 '{dbName}' 检查/创建完成");
            }
            catch (Exception ex)
            {
                // MySql.Data 8.0.20 与 MySQL 8.0.45 服务器的字符集协商问题 (utf8mb3)
                // 实际数据库可能已创建成功，仅在 Dispose/NextResult 阶段报 KeyNotFoundException
                // 记录警告后继续，避免阻塞启动
                Console.WriteLine($"[DB] MySQL 数据库检查出现异常 (可能被忽略): {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[DB] 提示: 如果数据库 '{dbName}' 已存在，可安全忽略此异常");
            }
        }

        /// <summary>
        ///  确保 SQL Server 数据库存在，不存在则创建
        /// </summary>
        static void EnsureSqlServerDatabase(string masterConnStr, string dbName)
        {
            try
            {
                var dbServer = CommonFactory.CreateInstance(CommonData.sql, masterConnStr);
                string sql = $"IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = '{dbName}')\n" +
                             $"BEGIN\n" +
                             $"    CREATE DATABASE [{dbName}];\n" +
                             $"END";
                dbServer.ExecuteSql(sql);
                Console.WriteLine($"[DB] SQL Server 数据库 '{dbName}' 检查/创建完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] SQL Server 数据库检查失败: {ex.Message}");
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine($"[MachineMonitor] 启动中... 端口: {Port}");
            Console.WriteLine($"[MachineMonitor] 数据库类型: {(DbType == "sqlserver" ? "SQL Server" : "MySQL")}");
            Console.WriteLine($"[MachineMonitor] 等待前端启动请求... (调用 /api/workflow/start 开始执行分包)");
            EnsureSubDir();
            EnsureTables();
            EnsureDirectories();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Start();
            Console.WriteLine($"[MachineMonitor] HTTP 服务已启动 http://localhost:{Port}/");
            Console.WriteLine($"[MachineMonitor] 当前状态: 等待启动指令 | 数据查询接口已就绪");

            while (true)
            {
                var ctx = listener.GetContext();
                Task.Run(() => Handle(ctx));
            }
        }

        // ==================== SQL 辅助方法 ====================
        /// <summary>
        /// 根据数据库类型生成 UPSERT 语句（MySQL: ON DUPLICATE KEY UPDATE, SQL Server: MERGE）
        /// </summary>
        static string UpsertSql(string table, string columns, string values, string updateSet)
        {
            if (DbType == "sqlserver")
            {
                var colList = columns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim()).ToList();
                var valList = values.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim()).ToList();
                string sourceCols = string.Join(", ", colList.Select((c, i) => $"{valList[i]} AS {c}"));
                string onClause = string.Join(" AND ", colList.Select(c => $"target.{c} = source.{c}"));
                return $@"MERGE {table} AS target
                    USING (SELECT {sourceCols}) AS source
                    ON {onClause}
                    WHEN MATCHED THEN UPDATE SET {updateSet}
                    WHEN NOT MATCHED THEN INSERT ({columns}) VALUES ({values});";
            }
            else
            {
                return $"INSERT INTO {table} ({columns}) VALUES ({values}) ON DUPLICATE KEY UPDATE {updateSet}";
            }
        }

        /// <summary>
        /// 转义 SQL 字符串值，防止 SQL 注入
        /// </summary>
        static string SqlStr(string val)
        {
            if (val == null) return "NULL";
            return "'" + val.Replace("'", "''") + "'";
        }

        /// <summary>
        /// 格式化日期为 SQL 字符串
        /// </summary>
        static string SqlDate(DateTime dt)
        {
            return SqlStr(dt.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        // ====================子目录====================
        static void EnsureSubDir()
        {
            if (!Directory.Exists(SubExeDir))
                Directory.CreateDirectory(SubExeDir);
        }

        static void EnsureDirectories()
        {
            var dirs = new[] {
                Path.Combine(ExeDir, "logs"),
                SubExeDir
            };
            foreach (var d in dirs)
                if (!Directory.Exists(d))
                    Directory.CreateDirectory(d);
        }

        // ==================== 数据库初始化 ====================
        /// <summary>
        /// 检查 MySQL 数据库中是否已存在指定表（通过查询 information_schema）
        /// </summary>
        static bool MySqlTableExists(CommonInterface db, string tableName)
        {
            try
            {
                var result = db.GetValue($"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{tableName}'");
                int count = 0;
                if (result != null && int.TryParse(result.ToString(), out count) && count > 0)
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 检查 SQL Server 中是否已存在指定表
        /// </summary>
        static bool SqlServerTableExists(CommonInterface db, string tableName)
        {
            try
            {
                var result = db.GetValue($"SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'[{tableName}]') AND type in (N'U')");
                int count = 0;
                if (result != null && int.TryParse(result.ToString(), out count) && count > 0)
                    return true;
            }
            catch { }
            return false;
        }

        static void EnsureTables()
        {
            // 外层 try：捕获数据库连接/不存在导致的异常
            try
            {
                // 定义需要检查的所有表名
                var requiredTables = new[] {
                    "process_log", "workflow_step", "work_flow", "process_flow",
                    "following_rack", "knife_rack", "macmessage", "rack_administrate",
                    "robotmessage", "runlog", "processlog", "macworkposstatus"
                };

                CommonInterface Db = null;
                bool dbConnected = false;

                // 尝试连接数据库
                try
                {
                    Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                    // 快速检查：如果所有表都存在，直接跳过
                    bool allExist = true;
                    foreach (var t in requiredTables)
                    {
                        bool exists = false;
                        try
                        {
                            exists = DbType == "sqlserver" ? SqlServerTableExists(Db, t) : MySqlTableExists(Db, t);
                        }
                        catch { exists = false; }
                        if (!exists)
                        {
                            allExist = false;
                            Console.WriteLine($"[DB] 表 '{t}' 不存在，需要创建");
                            break;
                        }
                    }
                    if (allExist)
                    {
                        Console.WriteLine("[DB] 所有表已存在，跳过数据库/表初始化");
                        return;
                    }
                    dbConnected = true;
                }
                catch (Exception connEx)
                {
                    // 数据库连接失败（可能数据库不存在）—— 尝试先用无数据库连接创建数据库
                    Console.WriteLine($"[DB] 数据库连接失败: {connEx.GetType().Name}: {connEx.Message}");
                    Console.WriteLine($"[DB] 尝试强制创建数据库...");

                    if (DbType == "sqlserver")
                    {
                        Console.WriteLine($"[DB] SQL Server 需要手动创建数据库");
                    }
                    else
                    {
                        // MySQL：直接从 config.json 读取数据库配置，构建无 Database 的连接字符串
                        try
                        {
                            string rawJson = File.ReadAllText(ConfigPath);
                            var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                            var root = js.Deserialize<Dictionary<string, object>>(rawJson) ?? new Dictionary<string, object>();
                            var dbSection = root.ContainsKey("database") ? GetDict(root, "database") : new Dictionary<string, object>();
                            string dbHost = GetStr(dbSection, "host", "localhost");
                            int dbPort = GetInt(dbSection, "port", 3306);
                            string dbUser = GetStr(dbSection, "user", "root");
                            string dbPass = GetStr(dbSection, "password", "123456");
                            string dbName = GetStr(dbSection, "database", "qianduantest");

                            string noDbConn = $"Server={dbHost};" +
                                              $"Port={dbPort};" +
                                              $"Uid={dbUser};" +
                                              $"Pwd={dbPass};" +
                                              $"CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
                            var dbServer = CommonFactory.CreateInstance(CommonData.mysql, noDbConn);
                            dbServer.ExecuteSql($"CREATE DATABASE IF NOT EXISTS `{dbName}` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
                            Console.WriteLine($"[DB] MySQL 数据库 '{dbName}' 创建完成");
                            // 重新连接（使用已构建好的 ConnStr）
                            Db = CommonFactory.CreateInstance(CommonData.mysql, ConnStr);
                            dbConnected = true;
                        }
                        catch (Exception createEx)
                        {
                            Console.WriteLine($"[DB] 创建数据库失败 (可能被忽略): {createEx.GetType().Name}: {createEx.Message}");
                            Console.WriteLine($"[DB] 提示: 请手动在 MySQL 中创建数据库: CREATE DATABASE IF NOT EXISTS `qianduantest` DEFAULT CHARACTER SET utf8mb4");
                        }
                    }
                }

                if (!dbConnected || Db == null)
                {
                    Console.WriteLine("[DB] 无法连接数据库，跳过表初始化。后端将正常启动但数据库功能不可用。");
                    return;
                }

                Console.WriteLine("[DB] 开始创建缺失的表...");

                // 每个建表语句独立 try-catch，单个失败不阻塞其他
                Action<string, string> TryCreateTable = (name, sql) =>
                {
                    try { Db.ExecuteSql(sql); Console.WriteLine($"[DB] 表 '{name}' 创建完成"); }
                    catch (Exception ex) { Console.WriteLine($"[DB] 表 '{name}' 创建失败 (继续): {ex.Message}"); }
                };

                if (DbType == "sqlserver")
                {
                    TryCreateTable("process_log", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[process_log]') AND type in (N'U'))
                        CREATE TABLE process_log (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            step_name NVARCHAR(100) NOT NULL,
                            step_display NVARCHAR(200) NOT NULL,
                            log_level NVARCHAR(20) NOT NULL,
                            log_content NVARCHAR(MAX) NOT NULL,
                            operator NVARCHAR(100) DEFAULT '',
                            created_at DATETIME DEFAULT GETDATE()
                        )");
                    TryCreateTable("workflow_step", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[workflow_step]') AND type in (N'U'))
                        CREATE TABLE workflow_step (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            step_name NVARCHAR(100) NOT NULL UNIQUE,
                            display_name NVARCHAR(200) NOT NULL,
                            step_order INT NOT NULL,
                            is_enabled BIT NOT NULL DEFAULT 1,
                            last_status NVARCHAR(20) DEFAULT 'idle',
                            last_message NVARCHAR(MAX) DEFAULT NULL,
                            last_time DATETIME DEFAULT NULL,
                            created_at DATETIME DEFAULT GETDATE()
                        )");
                    TryCreateTable("work_flow", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[work_flow]') AND type in (N'U'))
                        CREATE TABLE work_flow (
                            id INT NOT NULL PRIMARY KEY,
                            processname NVARCHAR(255) NOT NULL,
                            status INT DEFAULT 0,
                            priority INT DEFAULT 0
                        )");
                    TryCreateTable("following_rack", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[following_rack]') AND type in (N'U'))
                        CREATE TABLE following_rack (
                            id INT NOT NULL PRIMARY KEY,
                            workpiecename VARCHAR(255) NOT NULL,
                            workpieceworkstate INT DEFAULT 0,
                            posstatus INT DEFAULT 0
                        )");
                    TryCreateTable("macmessage", @"
                        CREATE TABLE IF NOT EXISTS macmessage (
                            id INT NOT NULL AUTO_INCREMENT PRIMARY KEY COMMENT '机床编号',
                            machinename VARCHAR(255) DEFAULT NULL COMMENT '机床名称',
                            machineip VARCHAR(255) DEFAULT NULL COMMENT '机床IP',
                            workstate INT DEFAULT 0 COMMENT '工作状态：0=空闲，1=加工中，2=已完成',
                            status INT DEFAULT 0 COMMENT '状态：0=禁用，1=启用',
                            workpiecename VARCHAR(255) DEFAULT NULL COMMENT '加工工件名称',
                            workpieceworkstate VARCHAR(255) DEFAULT NULL COMMENT '加工工件状态',
                            processing_duration VARCHAR(255) DEFAULT NULL COMMENT '加工时长(秒)',
                            error_message TEXT DEFAULT NULL COMMENT '报错信息'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("rack_administrate", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[rack_administrate]') AND type in (N'U'))
                        CREATE TABLE rack_administrate (
                            id INT NOT NULL PRIMARY KEY,
                            rfid VARCHAR(255) DEFAULT NULL,
                            workpiecename VARCHAR(255) DEFAULT NULL,
                            posstatus INT DEFAULT 0,
                            workstate INT DEFAULT 0,
                            scanstatus INT DEFAULT 0,
                            priority INT DEFAULT 0,
                            racktype INT DEFAULT 0,
                            racknum INT DEFAULT 0
                        )");
                    TryCreateTable("macworkposstatus", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[macworkposstatus]') AND type in (N'U'))
                        CREATE TABLE macworkposstatus (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            mac_name VARCHAR(255) DEFAULT NULL,
                            worknum INT DEFAULT 0,
                            workpiece_name VARCHAR(255) DEFAULT NULL,
                            workpiece_status INT DEFAULT 0,
                            process_duration INT DEFAULT 0
                        )");
                    TryCreateTable("robotmessage", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[robotmessage]') AND type in (N'U'))
                        CREATE TABLE robotmessage (
                            id INT NOT NULL PRIMARY KEY,
                            clampname VARCHAR(255) DEFAULT NULL,
                            clampstatus INT DEFAULT 0,
                            clampposstatus INT DEFAULT 0,
                            workpiecename VARCHAR DEFAULT NULL,
                            workpiecestatus INT DEFAULT 0
                        )");
                    TryCreateTable("runlog", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[runlog]') AND type in (N'U'))
                        CREATE TABLE runlog (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            occurrence_time DATETIME DEFAULT NULL,
                            log_type VARCHAR(50) DEFAULT NULL,
                            log_content TEXT DEFAULT NULL,
                            operator VARCHAR DEFAULT NULL
                        )");
                    TryCreateTable("knife_rack", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[knife_rack]') AND type in (N'U'))
                        CREATE TABLE knife_rack (
                            id INT NOT NULL PRIMARY KEY,
                            rfid VARCHAR DEFAULT NULL,
                            posstatus INT DEFAULT NULL,
                            workstate INT DEFAULT NULL
                        )");
                    TryCreateTable("processlog", @"
                        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[processlog]') AND type in (N'U'))
                        CREATE TABLE processlog (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            workpiece_name VARCHAR DEFAULT NULL,
                            fixture_no VARCHAR DEFAULT NULL,
                            mold_no VARCHAR DEFAULT NULL,
                            process_start_time DATETIME DEFAULT NULL,
                            process_end_time DATETIME DEFAULT NULL,
                            process_status INT DEFAULT NULL,
                            machine_station VARCHAR DEFAULT NULL
                        )");
                }
                else
                {
                    // MySQL
                    TryCreateTable("process_log", @"
                        CREATE TABLE IF NOT EXISTS process_log (
                            id INT AUTO_INCREMENT PRIMARY KEY,
                            step_name VARCHAR(100) NOT NULL COMMENT '分包名称',
                            step_display VARCHAR(200) NOT NULL COMMENT '显示名称',
                            log_level VARCHAR(20) NOT NULL COMMENT 'INFO/WARN/ERROR/SUCCESS',
                            log_content TEXT NOT NULL COMMENT '日志内容',
                            operator VARCHAR(100) DEFAULT '' COMMENT '操作员',
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT '记录时间'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("workflow_step", @"
                        CREATE TABLE IF NOT EXISTS workflow_step (
                            id INT AUTO_INCREMENT PRIMARY KEY,
                            step_name VARCHAR(100) NOT NULL UNIQUE COMMENT '分包名称',
                            display_name VARCHAR(200) NOT NULL COMMENT '显示名称',
                            step_order INT NOT NULL COMMENT '顺序',
                            is_enabled TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
                            last_status VARCHAR(20) DEFAULT 'idle' COMMENT 'idle/running/completed/error',
                            last_message TEXT DEFAULT NULL COMMENT '最新日志',
                            last_time DATETIME DEFAULT NULL COMMENT '最后执行时间',
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("work_flow", @"
                        CREATE TABLE IF NOT EXISTS work_flow (
                            id INT NOT NULL COMMENT '序号',
                            processname VARCHAR(255) NOT NULL COMMENT '流程名称',
                            status INT DEFAULT 0 COMMENT '0:不执行 1:执行',
                            priority INT DEFAULT 0 COMMENT '优先级，数字越大越优先',
                            PRIMARY KEY (id),
                            INDEX idx_status_priority (status, priority DESC)
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("following_rack", @"
                        CREATE TABLE IF NOT EXISTS following_rack (
                            id INT NOT NULL COMMENT '料位编号',
                            workpiecename VARCHAR(255) NOT NULL COMMENT '工件名称',
                            workpieceworkstate INT DEFAULT 0 COMMENT '工件工作状态（0：待加工、1：加工中、2：加工完成、3：加工失败）',
                            posstatus INT DEFAULT 0 COMMENT '料位状态（0：禁用、1：无料、2：有料）'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("knife_rack", @"
                        CREATE TABLE IF NOT EXISTS knife_rack (
                            id INT NOT NULL COMMENT '料位编号',
                            rfid VARCHAR(255) NOT NULL COMMENT '标签号（TXX）',
                            posstatus INT DEFAULT 0 COMMENT '料位状态：0=禁用，1=空闲，2=占用',
                            workstate INT DEFAULT 0 COMMENT '工作状态：0=未加工，1=加工中，2=加工完成，3=异常'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("macmessage", @"
                        CREATE TABLE IF NOT EXISTS macmessage (
                            id INT NOT NULL AUTO_INCREMENT PRIMARY KEY COMMENT '机床编号',
                            machinename VARCHAR(255) DEFAULT NULL COMMENT '机床名称',
                            machineip VARCHAR(255)  DEFAULT NULL COMMENT '料位状态：0=禁用，1=空闲，2=占用',
                            workstate INT DEFAULT 0 COMMENT '机床工作状态：0=空闲，1=加工中，2=已完成',
                            status INT DEFAULT 0 COMMENT '机床状态：0=禁用，1=启用',
                            workpiecename VARCHAR(255)  DEFAULT NULL COMMENT '加工工件名称',
                            workpieceworkstate VARCHAR(255) DEFAULT NULL COMMENT '加工工件状态',
                            processing_duration VARCHAR(255) DEFAULT NULL COMMENT '该工件加工时长(秒)',
                            error_message TEXT DEFAULT NULL COMMENT '机床报错信息'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("macworkposstatus", @"
                        CREATE TABLE IF NOT EXISTS macworkposstatus (
                            id INT NOT NULL AUTO_INCREMENT PRIMARY KEY COMMENT '记录ID',
                            mac_name VARCHAR(255) DEFAULT NULL COMMENT '机床名称',
                            worknum INT DEFAULT 0 COMMENT '工位号',
                            workpiece_name VARCHAR(255) DEFAULT NULL COMMENT '工件名称',
                            workpiece_status INT DEFAULT 0 COMMENT '工件状态：0=无料，1=待加工，2=加工中，3=加工完成，4=异常',
                            process_duration INT DEFAULT 0 COMMENT '加工时长(秒)'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("processlog", @"
                        CREATE TABLE IF NOT EXISTS processlog (
                            id INT AUTO_INCREMENT PRIMARY KEY COMMENT '记录ID',
                            workpiece_name VARCHAR(255) DEFAULT NULL COMMENT '工件名称',
                            fixture_no VARCHAR(255) DEFAULT NULL COMMENT '夹具编号',
                            mold_no  VARCHAR(255) DEFAULT NULL COMMENT '模具编号',
                            process_start_time  DATETIME DEFAULT NULL COMMENT '加工开始时间',
                            process_end_time  DATETIME DEFAULT NULL COMMENT '加工结束时间',
                            process_status  INT DEFAULT 0 COMMENT '加工状态：0=未加工，1=加工中，2=已完成，3=异常',
                            machine_station  VARCHAR(255) DEFAULT NULL COMMENT '加工机床'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("rack_administrate", @"
                        CREATE TABLE IF NOT EXISTS rack_administrate (
                            id INT NOT NULL COMMENT '料位编号',
                            rfid VARCHAR(255) NOT NULL COMMENT '监控编号',
                            workpiecename VARCHAR(255) DEFAULT NULL COMMENT '工件名称',
                            posstatus INT DEFAULT 0 COMMENT '料位状态：0=禁用，1=空闲，2=占用',
                            workstate INT DEFAULT 0 COMMENT '工作状态：0=未加工，1=加工中，2=加工完成，3=加工异常',
                            scanstatus INT DEFAULT 0 COMMENT '扫描状态：0=未扫描，1=扫描成功，2=扫描失败',
                            priority INT DEFAULT 0 COMMENT '优先级：0=普通，1=紧急',
                            racktype INT DEFAULT 0 COMMENT '料架类型：1=固定电极，2=铝板电极',
                            racknum INT DEFAULT 0 COMMENT '料架编号'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("robotmessage", @"
                        CREATE TABLE IF NOT EXISTS robotmessage (
                            id INT NOT NULL COMMENT '料位编号',
                            clampname VARCHAR(255) DEFAULT NULL COMMENT '夹爪名称',
                            clampstatus INT DEFAULT 0 COMMENT '夹爪类型（0：快换夹爪、1：固定夹爪）',
                            clampposstatus INT DEFAULT 0 COMMENT '夹爪料位状态（0：无夹爪、1：有夹爪）',
                            workpiecename VARCHAR(255) DEFAULT NULL COMMENT '夹爪上工件名称',
                            workpiecestatus VARCHAR(20) DEFAULT NULL COMMENT '夹爪上工件状态（0：待加工、1：加工中、2：加工完成、3：加工错误）'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                    TryCreateTable("runlog", @"
                        CREATE TABLE IF NOT EXISTS runlog (
                            id INT AUTO_INCREMENT PRIMARY KEY COMMENT '日志ID',
                            occurrence_time DATETIME DEFAULT NULL COMMENT '发生时间',
                            log_type VARCHAR(50) DEFAULT NULL COMMENT '日志类型：INFO/WARN/ERROR',
                            log_content TEXT DEFAULT NULL COMMENT '日志内容',
                            operator VARCHAR(255) DEFAULT NULL COMMENT '操作员'
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                }

                Console.WriteLine("[DB] 表检查完成");
            }
            catch (Exception ex)
            {
                // 外层捕获：确保即使完全失败也不崩溃，允许后端启动
                Console.WriteLine($"[DB] 表初始化异常: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[DB] 后端将尝试继续启动，数据库功能可能不可用");
            }
        }

        // ==================== HTTP 处理 ====================
        static void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            resp.Headers.Add("Access-Control-Allow-Origin", "*");
            resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
            if (req.HttpMethod == "OPTIONS") { resp.StatusCode = 200; resp.Close(); return; }

            string path = Uri.UnescapeDataString(req.Url.AbsolutePath).TrimEnd('/');
            string method = req.HttpMethod;
            string raw = "";
            if (method == "POST")
            {
                using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                    raw = reader.ReadToEnd();
            }

            Console.WriteLine($"[{method}] {path}");

            string result = "";
            string contentType = "application/json; charset=utf-8";
            byte[] fileBytes = null;
            int notFoundStatus = 0; // 静态文件 404 时记录
            try
            {
                // 根路径 - 返回 index.html（自适应 exe 同目录）
                if (path == "" || path == "/")
                {
                    string indexPath = Path.Combine(ExeDir, "index.html");

                    if (File.Exists(indexPath))
                    {
                        fileBytes = File.ReadAllBytes(indexPath);
                        contentType = "text/html; charset=utf-8";
                    }
                    else
                    {
                        result = Json(new { success = false, message = "index.html 不存在" });
                    }
                }
                // 静态文件请求 (js, css, html)
                else if (path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".html") || path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".ico"))
                {
                    string filePath = Path.Combine(ExeDir, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                    if (File.Exists(filePath))
                    {
                        fileBytes = File.ReadAllBytes(filePath);
                        if (path.EndsWith(".js")) contentType = "application/javascript; charset=utf-8";
                        else if (path.EndsWith(".css")) contentType = "text/css; charset=utf-8";
                        else if (path.EndsWith(".html")) contentType = "text/html; charset=utf-8";
                        else if (path.EndsWith(".png")) contentType = "image/png";
                        else if (path.EndsWith(".jpg") || path.EndsWith(".jpeg")) contentType = "image/jpeg";
                        else if (path.EndsWith(".ico")) contentType = "image/x-icon";
                    }
                    else
                    {
                        // 静态文件不存在时返回 404，避免浏览器缓存错误响应
                        result = Json(new { success = false, message = "文件不存在: " + path });
                        notFoundStatus = 404;
                    }
                }
                else if (path == "/api/dashboard")
                    result = GetDashboard();
                else if (path == "/api/logs")
                    result = GetLogs(raw);
                else if (path == "/api/workflow/status")
                    result = GetWorkflowStatus();
                else if (path == "/api/workflow/start")
                    result = StartWorkflow(raw);
                else if (path == "/api/workflow/stop")
                    result = StopWorkflow();
                else if (path == "/api/workflow/pause")
                    result = PauseWorkflow();
                else if (path == "/api/workflow/resume")
                    result = ResumeWorkflow();
                else if (path.StartsWith("/api/machine/"))
                    result = ControlMachine(path.Substring("/api/machine/".Length), raw);
                else if (path == "/api/rack/status")
                    result = method == "GET" ? GetRackStatus() : UpdateRackStatus(raw);
                else if (path == "/api/rack/create")
                    result = CreateRack(raw);
                else if (path == "/api/machine/create")
                    result = CreateMachine(raw);
                else if (path == "/api/sub/log")
                    result = ReceiveSubLog(raw);
                else if (path == "/api/config")
                    result = method == "GET" ? GetConfig() : SaveConfig(raw);
                else if (path == "/api/config/debug")
                {
                    // 调试端点：回显前端发送的原始数据
                    Console.WriteLine("[DEBUG] /api/config/debug 收到数据: " + (raw.Length > 500 ? raw.Substring(0, 500) + "..." : raw));
                    result = Json(new { success = true, received = raw });
                }
                else
                    result = Json(new { success = false, message = "未找到接口" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex}");
                result = Json(new { success = false, message = ex.Message });
            }

            byte[] buf;
            if (fileBytes != null)
            {
                // 静态文件响应
                buf = fileBytes;
                resp.ContentType = contentType;
            }
            else
            {
                // JSON API 响应
                buf = Encoding.UTF8.GetBytes(result);
                resp.ContentType = contentType;
            }
            if (notFoundStatus > 0)
                resp.StatusCode = notFoundStatus;
            resp.ContentLength64 = buf.Length;
            resp.OutputStream.Write(buf, 0, buf.Length);
            resp.Close();
        }

        // ====================API: 仪表盘====================
        static string GetDashboard()
        {
            var machines = new List<object>();
            CommonInterface Dbdashboard = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
            using (DataTable dt = Dbdashboard.ExeForDtl("SELECT id,machinename,machineip,workstate,status,error_message FROM macmessage"))
            {
                if (dt != null && dt.Rows.Count > 0)
                foreach (DataRow dr in dt.Rows)
                {
                    machines.Add(new
                    {
                        id = SafeInt(dr["id"]),
                        machinename = SafeStr(dr["machinename"]),
                        machineip = SafeStr(dr["machineip"]),
                        workstate = SafeInt(dr["workstate"]),
                        status = SafeInt(dr["status"]),
                        error_message = SafeStr(dr["error_message"])
                    });
                }
            }
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            long processed = 0, unprocessed = 0;
            
            string dateCondition = DbType == "sqlserver" 
                ? $"CONVERT(date, process_start_time)='{today}'" 
                : $"DATE(process_start_time)='{today}'";
                
            using (DataTable dt2 = Dbdashboard.ExeForDtl($"SELECT process_status,COUNT(*) as cnt FROM processlog WHERE {dateCondition} GROUP BY process_status"))
            {
                if (dt2 != null && dt2.Rows.Count > 0)
                foreach (DataRow dr in dt2.Rows)
                {
                    var st = SafeInt(dr["process_status"]);
                    if (st == 2) processed = SafeLong(dr["cnt"]);
                    else if (st == 0) unprocessed = SafeLong(dr["cnt"]);
                }
            }
            return Json(new
            {
                success = true,
                data = new { machines, today_processed = processed, today_unprocessed = unprocessed, today, workflow_running = WorkflowRunning }
            });
        }

        // ====================API: 获取配置====================
        static string GetConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return Json(new { success = false, message = "配置文件不存在" });

                var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));
                if (cfg == null)
                    return Json(new { success = false, message = "配置文件格式错误" });

                // 读取config.json并映射到前端结构
                var db = GetDict(cfg, "database");
                var plc = GetDict(cfg, "plc");
                // 兼容新旧格式：优先读取 signals（新），兜底 addresses（旧）
                var signalsSrc = GetDict(plc, "signals");
                var addrFallback = GetDict(plc, "addresses");
                var hasSignals = signalsSrc.Count > 0;
                var robotSrc = hasSignals ? GetDict(signalsSrc, "robot") : new Dictionary<string, object>();
                var palletSrc = hasSignals ? GetDict(signalsSrc, "pallet") : new Dictionary<string, object>();
                var presetSrc = hasSignals ? GetDict(signalsSrc, "preset") : new Dictionary<string, object>();
                var cleanerSrc = hasSignals ? GetDict(signalsSrc, "cleaner") : new Dictionary<string, object>();
                var dm = GetDict(plc, "dm");
                var machines = GetDict(cfg, "machines");

                // 机床列表
                var machineList = new List<object>();
                foreach (var kv in machines)
                {
                    var m = GetDict(machines, kv.Key);
                    machineList.Add(new
                    {
                        id = kv.Key,
                        type = GetStr(m, "type", ""),
                        number = GetStr(m, "number", ""),
                        ip = GetStr(m, "ip", ""),
                        port = GetStr(m, "port", ""),
                        stationCount = GetStr(m, "stationCount", "4"),
                        ncPath = GetStr(m, "ncPath", ""),
                        sessionUrl = GetStr(m, "sessionUrl", ""),
                        username = GetStr(m, "username", ""),
                        password = GetStr(m, "password", "")
                    });
                }

                // 读取系统工作流定义（work_flow 表）
                var workflowList = new List<object>();
                try
                {
                    CommonInterface Dbwf = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                    using (DataTable dtwf = Dbwf.ExeForDtl("SELECT id, processname, status, priority FROM work_flow ORDER BY id"))
                    {
                        if (dtwf != null && dtwf.Rows.Count > 0)
                        {
                            foreach (DataRow dr in dtwf.Rows)
                            {
                                workflowList.Add(new
                                {
                                    id = SafeInt(dr["id"]),
                                    name = SafeStr(dr["processname"]),
                                    enabled = SafeInt(dr["status"]) == 1,
                                    priority = SafeInt(dr["priority"])
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[CONFIG] 读取工作流失败: " + ex.Message);
                }

                var result = new
                {
                    success = true,
                    data = new
                    {
                        line = new
                        {
                            rackType = GetStr(cfg.ContainsKey("line") ? GetDict(cfg, "line") : new Dictionary<string, object>(), "rackType", "固定料架"),
                            rackCount = GetStr(cfg.ContainsKey("line") ? GetDict(cfg, "line") : new Dictionary<string, object>(), "rackCount", ""),
                            rackLayers = GetStr(cfg.ContainsKey("line") ? GetDict(cfg, "line") : new Dictionary<string, object>(), "rackLayers", ""),
                            rackSlots = GetStr(cfg.ContainsKey("line") ? GetDict(cfg, "line") : new Dictionary<string, object>(), "rackSlots", ""),
                            gripperType = GetStr(cfg.ContainsKey("line") ? GetDict(cfg, "line") : new Dictionary<string, object>(), "gripperType", "固定单爪"),
                            grabMode = GetStr(cfg.ContainsKey("line") ? GetDict(cfg, "line") : new Dictionary<string, object>(), "grabMode", "A抓B放"),
                            shuttleExists = GetStr(cfg.ContainsKey("line") ? GetDict(cfg, "line") : new Dictionary<string, object>(), "shuttleExists", "无"),
                            shuttleSlots = GetStr(cfg.ContainsKey("line") ? GetDict(cfg, "line") : new Dictionary<string, object>(), "shuttleSlots", ""),
                            racks = cfg.ContainsKey("racks") ? cfg["racks"] : new List<object>(),
                            machines = machineList,
                            grippers = cfg.ContainsKey("grippers") ? cfg["grippers"] : new List<object>()
                        },
                        plc = new
                        {
                            ip = GetStr(plc, "ip", ""),
                            port = GetStr(plc, "port", ""),
                            signals = new
                            {
                                robot = new
                                {
                                    photo = hasSignals ? GetStr(robotSrc, "photo", "") : GetStr(addrFallback, "gripper_has_part", ""),
                                    taskStart = hasSignals ? GetStr(robotSrc, "taskStart", "") : GetStr(addrFallback, "task_start_signal", ""),
                                    taskDone = hasSignals ? GetStr(robotSrc, "taskDone", "") : GetStr(addrFallback, "task_complete_signal", ""),
                                    taskNumber = hasSignals ? GetStr(robotSrc, "taskNumber", "") : GetStr(addrFallback, "task_number_addr", ""),
                                    error = hasSignals ? GetStr(robotSrc, "error", "") : GetStr(addrFallback, "robot_error", ""),
                                    safePos = hasSignals ? GetStr(robotSrc, "safePos", "") : GetStr(addrFallback, "robot_safe", ""),
                                    estop = hasSignals ? GetStr(robotSrc, "estop", "") : GetStr(addrFallback, "emergency_stop", "")
                                },
                                pallet = new
                                {
                                    slotPhoto = hasSignals ? GetStr(palletSrc, "slotPhoto", "") : GetStr(addrFallback, "shuttle_slot_photoelectric", ""),
                                    grabTaskNo = hasSignals ? GetStr(palletSrc, "grabTaskNo", "") : GetStr(addrFallback, "pallet_grab_task", ""),
                                    placeTaskNo = hasSignals ? GetStr(palletSrc, "placeTaskNo", "") : GetStr(addrFallback, "pallet_place_task", ""),
                                    photoStart = hasSignals ? GetStr(palletSrc, "photoStart", "") : GetStr(addrFallback, "pallet_photo_start", "")
                                },
                                preset = new
                                {
                                    photo = hasSignals ? GetStr(presetSrc, "photo", "") : GetStr(addrFallback, "preset_photoelectric", ""),
                                    manual = hasSignals ? GetStr(presetSrc, "manual", "") : GetStr(addrFallback, "preset_manual_signal", ""),
                                    openRequest = hasSignals ? GetStr(presetSrc, "openRequest", "") : "",
                                    openAllow = hasSignals ? GetStr(presetSrc, "openAllow", "") : "",
                                    grabTaskNo = hasSignals ? GetStr(presetSrc, "grabTaskNo", "") : "",
                                    placeTaskNo = hasSignals ? GetStr(presetSrc, "placeTaskNo", "") : "",
                                    doorLock = hasSignals ? GetStr(presetSrc, "doorLock", "") : ""
                                },
                                rotary = hasSignals ? new
                                {
                                    rotate = GetStr(GetDict(signalsSrc, "rotary"), "rotate", ""),
                                    zero = GetStr(GetDict(signalsSrc, "rotary"), "zero", ""),
                                    grabTaskNo = GetStr(GetDict(signalsSrc, "rotary"), "grabTaskNo", ""),
                                    placeTaskNo = GetStr(GetDict(signalsSrc, "rotary"), "placeTaskNo", ""),
                                    arrive = GetStr(GetDict(signalsSrc, "rotary"), "arrive", "")
                                } : new { rotate = "", zero = "", grabTaskNo = "", placeTaskNo = "", arrive = "" },
                                cleaner = new
                                {
                                    photo = hasSignals ? GetStr(cleanerSrc, "photo", "") : GetStr(addrFallback, "clean_photoelectric", ""),
                                    grabTaskNo = hasSignals ? GetStr(cleanerSrc, "grabTaskNo", "") : "",
                                    placeTaskNo = hasSignals ? GetStr(cleanerSrc, "placeTaskNo", "") : ""
                                }
                            },
                            dm = new
                            {
                                task_number = GetStr(dm, "task_number", ""),
                                robot_task = GetStr(dm, "robot_task", ""),
                                rack_layer_photoelectric = GetStr(dm, "rack_layer_photoelectric", ""),
                                gripper_type = GetStr(dm, "gripper_type", ""),
                                fixture_position = GetStr(dm, "fixture_position", ""),
                                shuttle_part_count = GetStr(dm, "shuttle_part_count", "")
                            }
                        },
                        workflow = workflowList
                    }
                };
                return Js.Serialize(result);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "读取配置失败: " + ex.Message });
            }
        }

        // ====================API: 保存配置====================
        static string SaveConfig(string raw)
        {
            try
            {
                if (string.IsNullOrEmpty(raw))
                    return Json(new { success = false, message = "请求数据为空" });

                var input = Js.Deserialize<Dictionary<string, object>>(raw);
                if (input == null)
                    return Json(new { success = false, message = "请求数据格式错误" });

                // 读取当前配置
                var cfg = File.Exists(ConfigPath)
                    ? Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath))
                    : new Dictionary<string, object>();

                // 保留旧 plc 中的 DM 值，防止前端未传时被覆盖为空
                var oldPlc = GetDict(cfg, "plc");
                var oldDm = GetDict(oldPlc, "dm");

                // 更新line配置
                var lineInput = GetDict(input, "line");
                cfg["line"] = new Dictionary<string, object>
                {
                    { "rackType", GetStr(lineInput, "rackType", "") },
                    { "rackCount", GetStr(lineInput, "rackCount", "") },
                    { "rackLayers", GetStr(lineInput, "rackLayers", "") },
                    { "rackSlots", GetStr(lineInput, "rackSlots", "") },
                    { "gripperType", GetStr(lineInput, "gripperType", "") },
                    { "grabMode", GetStr(lineInput, "grabMode", "") },
                    { "shuttleExists", GetStr(lineInput, "shuttleExists", "") },
                    { "shuttleSlots", GetStr(lineInput, "shuttleSlots", "") }
                };

                // 更新PLC配置
                var plcInput = GetDict(input, "plc");
                // 双向兼容：前端可能发送 signals（新格式）或 addresses（旧格式）
                var signalsInput = GetDict(plcInput, "signals");
                var addressesFallback = GetDict(plcInput, "addresses");
                // signals 优先级高于 addresses
                var hasSignals = signalsInput != null && signalsInput.Count > 0;
                var robotSignals = hasSignals ? GetDict(signalsInput, "robot") : GetDict(addressesFallback, "robot") ?? new Dictionary<string, object>();
                var palletSignals = hasSignals ? GetDict(signalsInput, "pallet") : GetDict(addressesFallback, "pallet") ?? new Dictionary<string, object>();
                var presetSignals = hasSignals ? GetDict(signalsInput, "preset") : GetDict(addressesFallback, "preset") ?? new Dictionary<string, object>();
                var cleanerSignals = hasSignals ? GetDict(signalsInput, "cleaner") : GetDict(addressesFallback, "cleaner") ?? new Dictionary<string, object>();
                var rotarySignals = hasSignals ? GetDict(signalsInput, "rotary") : new Dictionary<string, object>();
                var dmInput = GetDict(plcInput, "dm");

                cfg["plc"] = new Dictionary<string, object>
                {
                    { "ip", GetStr(plcInput, "ip", "192.168.1.1") },
                    { "port", GetStr(plcInput, "port", "9600") },
                    { "signals", new Dictionary<string, object>
                        {
                            { "robot", new Dictionary<string, object>
                                {
                                    { "photo", hasSignals ? GetStr(robotSignals, "photo", "") : GetStr(addressesFallback, "gripper_has_part", "") },
                                    { "taskStart", hasSignals ? GetStr(robotSignals, "taskStart", "") : GetStr(addressesFallback, "task_start_signal", "") },
                                    { "taskDone", hasSignals ? GetStr(robotSignals, "taskDone", "") : GetStr(addressesFallback, "task_complete_signal", "") },
                                    { "taskNumber", hasSignals ? GetStr(robotSignals, "taskNumber", "") : GetStr(addressesFallback, "task_number_addr", "") },
                                    { "error", hasSignals ? GetStr(robotSignals, "error", "") : GetStr(addressesFallback, "robot_error", "") },
                                    { "safePos", hasSignals ? GetStr(robotSignals, "safePos", "") : GetStr(addressesFallback, "robot_safe", "") },
                                    { "estop", hasSignals ? GetStr(robotSignals, "estop", "") : GetStr(addressesFallback, "emergency_stop", "") }
                                }
                            },
                            { "pallet", new Dictionary<string, object>
                                {
                                    { "slotPhoto", hasSignals ? GetStr(palletSignals, "slotPhoto", "") : GetStr(addressesFallback, "shuttle_slot_photoelectric", "") },
                                    { "grabTaskNo", hasSignals ? GetStr(palletSignals, "grabTaskNo", "") : "" },
                                    { "placeTaskNo", hasSignals ? GetStr(palletSignals, "placeTaskNo", "") : "" },
                                    { "photoStart", hasSignals ? GetStr(palletSignals, "photoStart", "") : "" }
                                }
                            },
                            { "preset", new Dictionary<string, object>
                                {
                                    { "photo", hasSignals ? GetStr(presetSignals, "photo", "") : GetStr(addressesFallback, "preset_photoelectric", "") },
                                    { "manual", hasSignals ? GetStr(presetSignals, "manual", "") : GetStr(addressesFallback, "preset_manual_signal", "") },
                                    { "openRequest", hasSignals ? GetStr(presetSignals, "openRequest", "") : "" },
                                    { "openAllow", hasSignals ? GetStr(presetSignals, "openAllow", "") : "" },
                                    { "grabTaskNo", hasSignals ? GetStr(presetSignals, "grabTaskNo", "") : "" },
                                    { "placeTaskNo", hasSignals ? GetStr(presetSignals, "placeTaskNo", "") : "" },
                                    { "doorLock", hasSignals ? GetStr(presetSignals, "doorLock", "") : "" }
                                }
                            },
                            { "rotary", new Dictionary<string, object>
                                {
                                    { "rotate", hasSignals ? GetStr(rotarySignals, "rotate", "") : "" },
                                    { "zero", hasSignals ? GetStr(rotarySignals, "zero", "") : "" },
                                    { "grabTaskNo", hasSignals ? GetStr(rotarySignals, "grabTaskNo", "") : "" },
                                    { "placeTaskNo", hasSignals ? GetStr(rotarySignals, "placeTaskNo", "") : "" },
                                    { "arrive", hasSignals ? GetStr(rotarySignals, "arrive", "") : "" }
                                }
                            },
                            { "cleaner", new Dictionary<string, object>
                                {
                                    { "photo", hasSignals ? GetStr(cleanerSignals, "photo", "") : GetStr(addressesFallback, "clean_photoelectric", "") },
                                    { "grabTaskNo", hasSignals ? GetStr(cleanerSignals, "grabTaskNo", "") : "" },
                                    { "placeTaskNo", hasSignals ? GetStr(cleanerSignals, "placeTaskNo", "") : "" }
                                }
                            }
                        }
                    },
                    { "dm", new Dictionary<string, object>
                        {
                            // 优先取前端值，兜底保留旧配置值
                            { "task_number", GetStr(dmInput, "task_number", GetStr(oldDm, "task_number", "")) },
                            { "robot_task", GetStr(dmInput, "robot_task", GetStr(oldDm, "robot_task", "")) },
                            { "rack_layer_photoelectric", GetStr(dmInput, "rack_layer_photoelectric", GetStr(oldDm, "rack_layer_photoelectric", "")) },
                            { "gripper_type", GetStr(dmInput, "gripper_type", GetStr(oldDm, "gripper_type", "")) },
                            { "fixture_position", GetStr(dmInput, "fixture_position", GetStr(oldDm, "fixture_position", "")) },
                            { "shuttle_part_count", GetStr(dmInput, "shuttle_part_count", GetStr(oldDm, "shuttle_part_count", "")) }
                        }
                    }
                };

                // 更新机床配置（只有非空才覆盖，保护已有数据）
                var machineInput = GetDict(input, "line");
                var machineArr = GetJsonArray(machineInput, "machines");
                if (machineArr.Count > 0)
                {
                    var machinesDict = new Dictionary<string, object>();
                    foreach (var item in machineArr)
                    {
                        var m = item as Dictionary<string, object>;
                        if (m == null) continue;
                        string mId = GetStr(m, "id", "");
                        if (string.IsNullOrEmpty(mId)) continue;
                        machinesDict[mId] = new Dictionary<string, object>
                        {
                            { "type", GetStr(m, "type", "") },
                            { "number", GetStr(m, "number", "") },
                            { "ip", GetStr(m, "ip", "") },
                            { "port", GetStr(m, "port", "") },
                            { "stationCount", GetStr(m, "stationCount", "4") },
                            { "ncPath", GetStr(m, "ncPath", "") },
                            { "sessionUrl", GetStr(m, "sessionUrl", "") },
                            { "username", GetStr(m, "username", "") },
                            { "password", GetStr(m, "password", "") }
                        };
                    }
                    cfg["machines"] = machinesDict;
                    Console.WriteLine($"[CONFIG] 机床配置已更新，共 {machinesDict.Count} 台机床");
                }
                else
                {
                    Console.WriteLine("[CONFIG] 前端未发送机床数据，保留现有配置");
                }

                // 更新料架配置（只有非空才覆盖，保护已有数据）
                var rackArr = GetJsonArray(lineInput, "racks");
                if (rackArr.Count > 0)
                {
                    var racksList = new List<object>();
                    foreach (var item in rackArr)
                    {
                        var r = item as Dictionary<string, object>;
                        if (r == null) continue;
                        racksList.Add(new {
                            id = GetStr(r, "id", ""),
                            type = GetStr(r, "type", ""),
                            name = GetStr(r, "name", ""),
                            number = GetStr(r, "number", ""),
                            rows = GetStr(r, "rows", ""),
                            cols = GetStr(r, "cols", ""),
                            photoSignal = GetStr(r, "photoSignal", ""),
                            openRequest = GetStr(r, "openRequest", ""),
                            openSignal = GetStr(r, "openSignal", ""),
                            doorClosed = GetStr(r, "doorClosed", ""),
                            doorFlag = GetStr(r, "doorFlag", ""),
                            grabStartTaskNo = GetStr(r, "grabStartTaskNo", ""),
                            placeStartTaskNo = GetStr(r, "placeStartTaskNo", ""),
                            scanStartTaskNo = GetStr(r, "scanStartTaskNo", "")
                        });
                    }
                    cfg["racks"] = racksList;
                    Console.WriteLine($"[CONFIG] 料架配置已更新，共 {racksList.Count} 个料架");
                }
                else
                {
                    Console.WriteLine("[CONFIG] 前端未发送料架数据，保留现有配置");
                }

                // 更新夹爪配置
                var gripperArr = GetJsonArray(lineInput, "grippers");
                if (gripperArr.Count > 0)
                {
                    var gripperList = new List<object>();
                    foreach (var item in gripperArr)
                    {
                        var g = item as Dictionary<string, object>;
                        if (g == null) continue;
                        gripperList.Add(new {
                            id = GetStr(g, "id", ""),
                            type = GetStr(g, "type", "steel"),
                            name = GetStr(g, "name", ""),
                            number = GetStr(g, "number", ""),
                            slotPhotoSignal = GetStr(g, "slotPhotoSignal", ""),
                            grabTaskNo = GetStr(g, "grabTaskNo", ""),
                            placeTaskNo = GetStr(g, "placeTaskNo", "")
                        });
                    }
                    cfg["grippers"] = gripperList;
                    Console.WriteLine($"[CONFIG] 夹爪配置已更新，共 {gripperList.Count} 个夹爪");

                    // 同步更新 robotmessage 数据库表
                    try
                    {
                        CommonInterface DbGripper = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                        foreach (var g in gripperList)
                        {
                            var gdict = (Dictionary<string, object>)null;
                            try { gdict = Js.Deserialize<Dictionary<string, object>>(Js.Serialize(g)); } catch { continue; }
                            string gname = GetStr(gdict, "name", "");
                            string gtype = GetStr(gdict, "type", "steel");
                            int gid = string.IsNullOrEmpty(GetStr(gdict, "number", "")) ? 0 : Convert.ToInt32(GetStr(gdict, "number", ""));
                            // 检查是否已存在同名夹爪
                            bool exists = false;
                            var checkSql = "SELECT COUNT(*) as cnt FROM robotmessage WHERE clampname = " + SqlStr(gname);
                            using (var cdt = DbGripper.ExeForDtl(checkSql))
                                if (cdt != null && cdt.Rows.Count > 0)
                                    exists = Convert.ToInt32(cdt.Rows[0]["cnt"]) > 0;

                            if (exists)
                            {
                                DbGripper.ExecuteSql("UPDATE robotmessage SET clampstatus = " + (gtype == "steel" ? 1 : gtype == "tool" ? 2 : gtype == "electrode" ? 3 : 4) + " WHERE clampname = " + SqlStr(gname));
                                Console.WriteLine($"[CONFIG] 更新夹爪: {gname}");
                            }
                            else
                            {
                                DbGripper.ExecuteSql("INSERT INTO robotmessage (id, clampname, clampstatus, clampposstatus) VALUES (" + gid + ", " + SqlStr(gname) + ", " + (gtype == "steel" ? 1 : gtype == "tool" ? 2 : gtype == "electrode" ? 3 : 4) + ", 0)");
                                Console.WriteLine($"[CONFIG] 添加夹爪: {gname}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[CONFIG] robotmessage 更新失败: " + ex.Message);
                    }
                }
                else
                {
                    Console.WriteLine("[CONFIG] 前端未发送夹爪数据，保留现有配置");
                }

                // 更新系统工作流定义（写入 work_flow 表）
                if (input.ContainsKey("workflow"))
                {
                    try
                    {
                        var workflowArr = GetJsonArray(input, "workflow");
                        CommonInterface DbwfSave = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                        DbwfSave.ExecuteSql("DELETE FROM work_flow");
                        int seq = 1;
                        foreach (var item in workflowArr)
                        {
                            var w = item as Dictionary<string, object>;
                            if (w == null) continue;
                            string wName = (GetStr(w, "name", "") ?? "").Trim();
                            if (string.IsNullOrEmpty(wName)) continue;
                            int wEnabled = (GetInt(w, "enabled", 0) == 1) ? 1 : 0;
                            int wPriority = GetInt(w, "priority", 0);
                            DbwfSave.ExecuteSql($"INSERT INTO work_flow (id, processname, status, priority) VALUES ({seq}, {SqlStr(wName)}, {wEnabled}, {wPriority})");
                            seq++;
                        }
                        Console.WriteLine($"[CONFIG] 系统工作流已更新，共 {seq - 1} 个步骤");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[CONFIG] 工作流写入失败: " + ex.Message);
                    }
                }

                // 写入文件（格式化JSON）
                string json = Js.Serialize(cfg);
                // 用简单方式格式化JSON
                File.WriteAllText(ConfigPath, FormatJson(json), Encoding.UTF8);

                Console.WriteLine("[CONFIG] 配置已保存");
                return Json(new { success = true, message = "配置保存成功" });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CONFIG] 保存失败: " + ex.Message);
                return Json(new { success = false, message = "保存失败: " + ex.Message });
            }
        }

        // JSON格式化（缩进排版）
        static string FormatJson(string json)
        {
            var sb = new StringBuilder();
            bool inString = false;
            bool escape = false;
            int indent = 0;
            char prev = '\0';
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (escape) { escape = false; sb.Append(c); continue; }
                if (c == '\\' && inString) { escape = true; sb.Append(c); continue; }
                if (c == '"') inString = !inString;
                if (inString) { sb.Append(c); prev = c; continue; }

                if (c == '{' || c == '[')
                {
                    sb.Append(c);
                    indent += 2;
                    if (i + 1 < json.Length && json[i + 1] != '}' && json[i + 1] != ']')
                    { sb.Append('\n'); sb.Append(' ', indent); }
                }
                else if (c == '}' || c == ']')
                {
                    indent -= 2;
                    sb.Append('\n'); sb.Append(' ', indent); sb.Append(c);
                }
                else if (c == ',')
                {
                    sb.Append(c); sb.Append('\n'); sb.Append(' ', indent);
                }
                else if (c == ':')
                {
                    sb.Append(": ");
                }
                else if (!char.IsWhiteSpace(c))
                {
                    sb.Append(c);
                }
                prev = c;
            }
            return sb.ToString();
        }

        // ====================API: 日志====================
        static string GetLogs(string raw)
        {
            var filter = string.IsNullOrEmpty(raw)
                ? new Dictionary<string, object>()
                : Js.Deserialize<Dictionary<string, object>>(raw) ?? new Dictionary<string, object>();

            int page = filter.ContainsKey("page") ? Convert.ToInt32(filter["page"]) : 1;
            int pageSize = filter.ContainsKey("pageSize") ? Convert.ToInt32(filter["pageSize"]) : 50;
            string step = filter.ContainsKey("step") ? filter["step"].ToString() : "";
            string level = filter.ContainsKey("level") ? filter["level"].ToString() : "";
            string keyword = filter.ContainsKey("keyword") ? filter["keyword"].ToString() : "";

            var conditions = new List<string>();
            if (!string.IsNullOrEmpty(step)) conditions.Add($"step_name={SqlStr(step)}");
            if (!string.IsNullOrEmpty(level)) conditions.Add($"log_level={SqlStr(level)}");
            if (!string.IsNullOrEmpty(keyword)) conditions.Add($"log_content LIKE {SqlStr("%" + keyword + "%")}");
            string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

            // 先查总数
            long total = 0;
            object cntObj;
            CommonInterface Dblog = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
            using (DataTable dt0 = Dblog.ExeForDtl($"SELECT COUNT(*) as cnt FROM process_log {where}"))
            {
                cntObj = (dt0 != null && dt0.Rows.Count > 0) ? dt0.Rows[0]["cnt"] : null;
            }
            if (cntObj != null && cntObj != DBNull.Value) total = Convert.ToInt64(cntObj);

            // 分页语法差异
            int offset = (page - 1) * pageSize;
            string orderBy = DbType == "sqlserver" 
                ? $"ORDER BY created_at DESC OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY"
                : $"ORDER BY created_at DESC LIMIT {pageSize} OFFSET {offset}";

            var logs = new List<object>();
            using (DataTable dt = Dblog.ExeForDtl($"SELECT id,step_name,step_display,log_level,log_content,operator,created_at FROM process_log {where} {orderBy}"))
            {
                if (dt != null && dt.Rows.Count > 0)
                foreach (DataRow dr in dt.Rows)
                {
                    logs.Add(new
                    {
                        id = SafeInt(dr["id"]),
                        step = SafeStr(dr["step_name"]),
                        stepDisplay = SafeStr(dr["step_display"]),
                        level = SafeStr(dr["log_level"]),
                        content = SafeStr(dr["log_content"]),
                        operators = SafeStr(dr["operator"]),
                        time = SafeDateTime(dr["created_at"])
                    });
                }
            }
            return Json(new
            {
                success = true,
                data = new { logs, total, page, pageSize, pages = (int)Math.Ceiling((double)total / pageSize) }
            });
        }

        // ==================== API: 工作流状态 ====================
        static string GetWorkflowStatus()
        {
            EnsureWorkflowSteps();
            CommonInterface Dbworkflowstatus = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
            var steps = new List<object>();
            using (DataTable dt = Dbworkflowstatus.ExeForDtl("SELECT step_name,display_name,step_order,is_enabled,last_status,last_message,last_time FROM workflow_step ORDER BY step_order"))
            {
                if (dt != null && dt.Rows.Count > 0)
                foreach (DataRow dr in dt.Rows)
                {
                    steps.Add(new
                    {
                        name = SafeStr(dr["step_name"]),
                        displayName = SafeStr(dr["display_name"]),
                        order = SafeInt(dr["step_order"]),
                        enabled = SafeInt(dr["is_enabled"]) == 1,
                        status = SafeStr(dr["last_status"]),
                        message = SafeStr(dr["last_message"]),
                        lastTime = SafeDateTime(dr["last_time"])
                    });
                }
            }
            return Json(new { success = true, data = new { running = WorkflowRunning, paused = WorkflowPaused, steps } });
        }

        // 从 process_flow 表同步数据到 workflow_step 表
        static void EnsureWorkflowSteps()
        {
            try
            {
                CommonInterface Dbworkflow = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                var allSteps = new List<ProcessFlowStep>();
                using (DataTable dt = Dbworkflow.ExeForDtl("SELECT id, processname, status FROM process_flow ORDER BY id"))
                if (dt != null)
                foreach (DataRow dr in dt.Rows)
                {
                    string name = SafeStr(dr["processname"]).ToLower();
                    string display = StepDisplayNames.ContainsKey(name) ? StepDisplayNames[name] : name;
                    allSteps.Add(new ProcessFlowStep
                    {
                        Id = SafeInt(dr["id"]),
                        Name = name,
                        DisplayName = display,
                        Status = SafeInt(dr["status"])
                    });
                }

                foreach (var s in allSteps)
                {
                    string upsertSql = DbType == "sqlserver"
                        ? $@"MERGE workflow_step AS target
                            USING (SELECT {SqlStr(s.Name)} AS step_name) AS source
                            ON target.step_name = source.step_name
                            WHEN MATCHED THEN UPDATE SET display_name={SqlStr(s.DisplayName)}, step_order={s.Id}, is_enabled={(s.Status == 1 ? 1 : 0)}
                            WHEN NOT MATCHED THEN INSERT (step_name, display_name, step_order, is_enabled) VALUES ({SqlStr(s.Name)}, {SqlStr(s.DisplayName)}, {s.Id}, {(s.Status == 1 ? 1 : 0)});"
                        : $"INSERT INTO workflow_step (step_name,display_name,step_order,is_enabled) VALUES ({SqlStr(s.Name)},{SqlStr(s.DisplayName)},{s.Id},{(s.Status == 1 ? 1 : 0)}) ON DUPLICATE KEY UPDATE display_name={SqlStr(s.DisplayName)}, step_order={s.Id}, is_enabled={(s.Status == 1 ? 1 : 0)}";
                    Dbworkflow.ExecuteSql(upsertSql);
                }
            }
            catch { }
        }

        // ==================== API: 启动工作流 ====================
        static string StartWorkflow(string raw)
        {
            if (WorkflowRunning)
                return Json(new { success = false, message = "工作流已在运行" });

            // 清除上次遗留的停止标志
            try { File.Delete(Path.Combine(ExeDir, "stop_rackoperate.flag")); } catch { }

            // 清理可能残留的子进程，防止启动时报"被另一进程占用"
            foreach (var name in StepDisplayNames.Keys)
            {
                try
                {
                    var procs = Process.GetProcessesByName(name);
                    foreach (var p in procs)
                    {
                        try { p.Kill(); p.WaitForExit(2000); } catch { }
                        Console.WriteLine($"[Workflow] 已终止残留进程: {name}.exe");
                    }
                }
                catch { }
            }

            Console.WriteLine($"[Workflow] 收到启动请求，开始执行分包程序...");
            WorkflowCts = new CancellationTokenSource();
            WorkflowRunning = true;
            WorkflowPaused = false;

            Task.Run(() => RunWorkflow(WorkflowCts.Token));
            Console.WriteLine($"[Workflow] 工作流已启动，正在按顺序执行分包...");
            return Json(new { success = true, message = "工作流已启动" });
        }

        static async Task RunWorkflow(CancellationToken ct)
        {
            try
            {
                var enabledSteps = GetEnabledSteps();

                if (enabledSteps.Count == 0)
                {
                    Console.WriteLine("[Workflow] 没有启用的流程步骤 (work_flow 和 process_flow 表中均无 status=1 的记录)");
                    WriteStepLog("workflow", "总流程", "WARN", "没有启用的流程步骤");
                    return;
                }

                bool fromWorkFlow = enabledSteps.Any(s => s.Priority > 0);
                string sourceInfo = fromWorkFlow ? "work_flow 定制工作流" : "process_flow 普通线体流程";
                Console.WriteLine($"[Workflow] 数据来源: {sourceInfo}，共 {enabledSteps.Count} 个步骤");

                int stepIndex = 0;
                int totalSteps = enabledSteps.Count;
                foreach (var step in enabledSteps)
                {
                    stepIndex++;
                    if (ct.IsCancellationRequested) break;
                    while (WorkflowPaused) { await Task.Delay(500, ct); if (ct.IsCancellationRequested) return; }

                    string name = step.Name;
                    string display = step.DisplayName;
                    Console.WriteLine($"[Workflow] [{stepIndex}/{totalSteps}] 开始执行分包: {display} ({name})");

                    WriteStepLog(name, display, "INFO", $"步骤 [{stepIndex}/{totalSteps}] 开始执行");
                    UpdateStepStatus(name, "running", "执行中");

                    await RunSubProcess(name, display, ct);

                    if (ct.IsCancellationRequested) break;
                    while (WorkflowPaused) { await Task.Delay(500, ct); if (ct.IsCancellationRequested) return; }

                    Console.WriteLine($"[Workflow] 完成: {display}");
                    UpdateStepStatus(name, "completed", "执行完成");
                    WriteStepLog(name, display, "SUCCESS", "步骤执行完成");
                }

                Console.WriteLine("[Workflow] 所有步骤执行完毕");
                WriteStepLog("workflow", "总流程", "INFO", "工作流全部步骤执行完毕");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Workflow] 已取消");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Workflow] 异常: {ex.Message}");
                WriteStepLog("workflow", "总流程", "ERROR", "工作流异常: " + ex.Message);
            }
            finally
            {
                WorkflowRunning = false;
                WorkflowPaused = false;
            }
        }

        static async Task RunSubProcess(string name, string displayName, CancellationToken ct)
        {
            string exePath = Path.Combine(SubExeDir, name + ".exe");
            Console.WriteLine($"[SubProcess] 准备启动: {name}.exe");

            if (!File.Exists(exePath))
            {
                Console.WriteLine($"[SubProcess] [警告] 分包不存在: {exePath}");
                WriteStepLog(name, displayName, "WARN", $"分包程序不存在: {name}.exe");
                await Task.Delay(1000, ct);
                return;
            }

            Console.WriteLine($"[SubProcess] 正在启动: {name}.exe");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "\"" + Path.Combine(ExeDir, "config.json") + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = SubExeDir
                };

                using (var proc = Process.Start(psi))
                {
                    proc.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Console.WriteLine($"  [{name}] {e.Data}");
                            WriteStepLog(name, displayName, "INFO", e.Data);
                        }
                    };
                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Console.WriteLine($"  [{name} ERROR] {e.Data}");
                            WriteStepLog(name, displayName, "ERROR", e.Data);
                        }
                    };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    while (!proc.HasExited)
                    {
                        await Task.Delay(500, ct);
                        if (ct.IsCancellationRequested)
                        {
                            try { File.WriteAllText(Path.Combine(ExeDir, "stop_rackoperate.flag"), "stop"); } catch { }
                            try { proc.Kill(); } catch { }
                            return;
                        }
                    }

                    await Task.Delay(200);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 运行分包 {name} 失败: {ex.Message}");
                WriteStepLog(name, displayName, "ERROR", "分包运行失败: " + ex.Message);
            }
        }

        // ==================== API: 停止/暂停/恢复 ====================
        static string StopWorkflow()
        {
            if (!WorkflowRunning) return Json(new { success = false, message = "工作流未运行" });
            // 创建停止标志文件，供子程序检测
            try { File.WriteAllText(Path.Combine(ExeDir, "stop_rackoperate.flag"), "stop"); } catch { }
            WorkflowCts?.Cancel();
            WorkflowRunning = false;
            return Json(new { success = true, message = "工作流已停止" });
        }

        static string PauseWorkflow()
        {
            if (!WorkflowRunning) return Json(new { success = false, message = "工作流未运行" });
            WorkflowPaused = true;
            return Json(new { success = true, message = "工作流已暂停" });
        }

        static string ResumeWorkflow()
        {
            if (!WorkflowRunning) return Json(new { success = false, message = "工作流未运行" });
            WorkflowPaused = false;
            return Json(new { success = true, message = "工作流已恢复" });
        }

        // ==================== 接收分包上报日志 ====================
        static string ReceiveSubLog(string raw)
        {
            try
            {
                if (string.IsNullOrEmpty(raw)) return Json(new { success = false });
                var body = Js.Deserialize<Dictionary<string, object>>(raw);
                string step = body.ContainsKey("step") ? body["step"].ToString() : "";
                string display = body.ContainsKey("displayName") ? body["displayName"].ToString() : step;
                string level = body.ContainsKey("level") ? body["level"].ToString() : "INFO";
                string content = body.ContainsKey("content") ? body["content"].ToString() : "";
                WriteStepLog(step, display, level, content);
                return Json(new { success = true });
            }
            catch { return Json(new { success = false }); }
        }

        // ==================== API: 机床控制 ====================
        static string ControlMachine(string action, string raw)
        {
            int id = 1;
            try
            {
                if (!string.IsNullOrEmpty(raw))
                {
                    var dict = Js.Deserialize<Dictionary<string, object>>(raw);
                    if (dict != null && dict.ContainsKey("id"))
                        id = Convert.ToInt32(dict["id"]);
                }
            }
            catch { }
            CommonInterface Dbmachine = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
            if (action == "start")
            {
                Dbmachine.ExecuteSql($"UPDATE macmessage SET workstate=1 WHERE id={id}");
            }
            else if (action == "pause")
            {
                Dbmachine.ExecuteSql($"UPDATE macmessage SET workstate=0 WHERE id={id}");
            }
            else if (action == "stop")
            {
                Dbmachine.ExecuteSql($"UPDATE macmessage SET workstate=0,workpiecename=NULL,workpieceworkstate=NULL,processing_duration=0 WHERE id={id}");
            }

            string msg = action == "start" ? "已发送启动命令" : action == "pause" ? "已发送暂停命令" : "已发送停止命令";
            return Json(new { success = true, message = msg, action });
        }

        // ==================== API: 料架状态 ====================
        static string GetRackStatus()
        {
            var knifeRack = new List<object>();
            CommonInterface Dbrackstatus = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
            using (DataTable dt1 = Dbrackstatus.ExeForDtl("SELECT id,rfid,posstatus,workstate FROM knife_rack"))
            if (dt1 != null)
            foreach (DataRow dr in dt1.Rows)
                knifeRack.Add(new { id = SafeInt(dr["id"]), rfid = SafeStr(dr["rfid"]), posstatus = SafeInt(dr["posstatus"]), workstate = SafeInt(dr["workstate"]) });

            var rackAdministrate = new List<object>();
            using (DataTable dt2 = Dbrackstatus.ExeForDtl("SELECT id,rfid,posstatus,workstate,scanstatus,priority,racktype,racknum FROM rack_administrate"))
            if (dt2 != null)
            foreach (DataRow dr in dt2.Rows)
                rackAdministrate.Add(new
                {
                    id = SafeInt(dr["id"]),
                    rfid = SafeStr(dr["rfid"]),
                    posstatus = SafeInt(dr["posstatus"]),
                    workstate = SafeInt(dr["workstate"]),
                    scanstatus = SafeInt(dr["scanstatus"]),
                    priority = SafeInt(dr["priority"]),
                    racktype = SafeInt(dr["racktype"]),
                    racknum = SafeInt(dr["racknum"])
                });

            return Json(new { success = true, data = new { knife_rack = knifeRack, rack_administrate = rackAdministrate } });
        }

        static string UpdateRackStatus(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Json(new { success = false, message = "参数为空" });
            try
            {
                var body = Js.Deserialize<Dictionary<string, object>>(raw);
                string type = body.ContainsKey("type") ? body["type"].ToString() : "";
                var ids = new List<int>();
                if (body.ContainsKey("ids"))
                {
                    var idsObj = body["ids"];
                    if (idsObj is System.Collections.ArrayList arrList)
                    {
                        foreach (var item in arrList)
                            ids.Add(Convert.ToInt32(item));
                    }
                    else if (idsObj is System.Array arr)
                    {
                        foreach (var item in arr)
                            ids.Add(Convert.ToInt32(item));
                    }
                    else if (idsObj is IEnumerable<object> enumerable)
                    {
                        foreach (var item in enumerable)
                            ids.Add(Convert.ToInt32(item));
                    }
                }
                string status = body.ContainsKey("status") ? body["status"].ToString() : "";

                string idList = string.Join(",", ids);
                string sql = "";
                if (type == "tool")
                {
                    if (status == "no-material") sql = $"UPDATE knife_rack SET posstatus=0 WHERE id IN ({idList})";
                    else if (status == "disable") sql = $"UPDATE knife_rack SET posstatus=0,workstate=0 WHERE id IN ({idList})";
                }
                else
                {
                    if (status == "no-material") sql = $"UPDATE rack_administrate SET posstatus=0 WHERE id IN ({idList})";
                    else if (status == "activate") sql = $"UPDATE rack_administrate SET scanstatus=1 WHERE id IN ({idList})";
                    else if (status == "lock") sql = $"UPDATE rack_administrate SET scanstatus=0 WHERE id IN ({idList})";
                    else if (status == "disable") sql = $"UPDATE rack_administrate SET posstatus=0,workstate=0,scanstatus=0 WHERE id IN ({idList})";
                    else if (status == "urgent") sql = $"UPDATE rack_administrate SET priority=1 WHERE id IN ({idList})";
                }
                CommonInterface Dbrack = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                if (!string.IsNullOrEmpty(sql))
                    Dbrack.ExecuteSql(sql);

                return Json(new { success = true, message = "状态已更新", ids, status });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // ==================== 辅助方法：写config.json ====================
        static bool SaveConfigJson(Dictionary<string, object> cfg)
        {
            try
            {
                var json = Js.Serialize(cfg);
                File.WriteAllText(ConfigPath, FormatJson(json), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SaveConfigJson Error] {ex.Message}");
                return false;
            }
        }

        // ==================== 创建料架条目 ====================
        static string CreateRack(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Json(new { success = false, message = "参数为空" });
            try
            {
                var body = Js.Deserialize<Dictionary<string, object>>(raw);
                int rackType = GetInt(body, "racktype", 1);
                int rows = GetInt(body, "rows", 8);
                int cols = GetInt(body, "cols", 10);
                string rackName = body.ContainsKey("rackname") ? body["rackname"].ToString() : "";

                int totalSlots = rows * cols;
                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);

                // 查找当前最大id，往后递增
                int maxId = 0;
                string maxIdSql = DbType == "sqlserver" 
                    ? "SELECT ISNULL(MAX(id), 0) as maxid FROM rack_administrate"
                    : "SELECT IFNULL(MAX(id), 0) as maxid FROM rack_administrate";
                using (var dt = Db.ExeForDtl(maxIdSql))
                    if (dt != null && dt.Rows.Count > 0)
                        maxId = Convert.ToInt32(dt.Rows[0]["maxid"]);

                var insertedIds = new List<int>();
                for (int i = 1; i <= totalSlots; i++)
                {
                    int newId = maxId + i;
                    string rfidVal = $"R{rackType:D1}-{newId:D4}";
                    string sql = $"INSERT INTO rack_administrate (id, rfid, posstatus, workstate, scanstatus, priority, racktype, racknum) " +
                                 $"VALUES ({newId}, '{rfidVal}', 1, 0, 0, 0, {rackType}, {newId})";
                    Db.ExecuteSql(sql);
                    insertedIds.Add(newId);
                }

                // 写config.json（同步racks节）
                try
                {
                    var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath)) ?? new Dictionary<string, object>();
                    if (!cfg.ContainsKey("racks")) cfg["racks"] = new Dictionary<string, object>();
                    var racks = (Dictionary<string, object>)cfg["racks"];
                    racks[$"{rackType}-{racks.Count + 1}"] = new Dictionary<string, object>
                    {
                        { "type", rackType.ToString() },
                        { "rows", rows },
                        { "cols", cols },
                        { "name", rackName },
                        { "totalSlots", totalSlots }
                    };
                    SaveConfigJson(cfg);
                }
                catch (Exception ex) { Console.WriteLine($"[SaveConfigJson Rack Error] {ex.Message}"); }

                return Json(new { success = true, message = $"已创建 {totalSlots} 个料位", ids = insertedIds, racktype = rackType, rows, cols, rackname = rackName });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // ==================== 创建机床条目 ====================
        static string CreateMachine(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Json(new { success = false, message = "参数为空" });
            try
            {
                var body = Js.Deserialize<Dictionary<string, object>>(raw);
                string macName = body.ContainsKey("macname") ? body["macname"].ToString() : "";
                string macIp = body.ContainsKey("macip") ? body["macip"].ToString() : "";
                string brand = body.ContainsKey("brand") ? body["brand"].ToString() : "roders";
                string ncpath = body.ContainsKey("ncpath") ? body["ncpath"].ToString() : "";
                int slots = GetInt(body, "slots", 4);

                CommonInterface Db = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);

                // 查找当前最大macid，往后递增
                int maxMacId = 0;
                string maxMacIdSql = DbType == "sqlserver"
                    ? "SELECT ISNULL(MAX(id), 0) as maxid FROM macmessage"
                    : "SELECT IFNULL(MAX(id), 0) as maxid FROM macmessage";
                using (var dtMax = Db.ExeForDtl(maxMacIdSql))
                    if (dtMax != null && dtMax.Rows.Count > 0)
                        maxMacId = Convert.ToInt32(dtMax.Rows[0]["maxid"]);
                int macId = maxMacId + 1;

                // 插入 macmessage
                Db.ExecuteSql($"INSERT INTO macmessage (id, machinename, machineip, workstate, status) VALUES ({macId}, '{Escape(macName)}', '{Escape(macIp)}', 0, 1)");

                // 根据工位数在 macworkposstatus 创建对应行
                var workIds = new List<int>();
                for (int w = 1; w <= slots; w++)
                {
                    string sql = $"INSERT INTO macworkposstatus (mac_name, worknum, workpiece_status) VALUES ('{Escape(macName)}', {w}, 0)";
                    Db.ExecuteSql(sql);
                }

                // 写config.json（同步machines节）
                try
                {
                    var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath)) ?? new Dictionary<string, object>();
                    if (!cfg.ContainsKey("machines")) cfg["machines"] = new Dictionary<string, object>();
                    var machines = (Dictionary<string, object>)cfg["machines"];
                    machines[$"{macId}"] = new Dictionary<string, object>
                    {
                        { "type", brand },
                        { "ip", macIp },
                        { "ncPath", ncpath },
                        { "slots", slots }
                    };
                    SaveConfigJson(cfg);
                }
                catch (Exception ex) { Console.WriteLine($"[SaveConfigJson Machine Error] {ex.Message}"); }

                return Json(new { success = true, message = $"机床已创建，工位数 {slots}", macid = macId, macname = macName, macip = macIp, slots });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("'", "''");

        // ==================== 写入步骤日志到数据库 ====================
        static void WriteStepLog(string step, string display, string level, string content)
        {
            try
            {
                CommonInterface Dbsteplog = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string sql = $"INSERT INTO process_log (step_name,step_display,log_level,log_content,operator,created_at) " +
                             $"VALUES ({SqlStr(step)},{SqlStr(display)},{SqlStr(level)},{SqlStr(content)},{SqlStr("")},{SqlStr(now)})";
                Dbsteplog.ExecuteSql(sql);

                AllLogs.Enqueue(new LogEntry { Step = step, Display = display, Level = level, Content = content, Time = DateTime.Now });
                while (AllLogs.Count > LogCapacity) AllLogs.TryDequeue(out _);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Log Error] {ex.Message}");
            }
        }

        // 更新步骤状态
        static void UpdateStepStatus(string step, string status, string message)
        {
            try
            {
                CommonInterface Dbstepstatus = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Dbstepstatus.ExecuteSql($"UPDATE workflow_step SET last_status={SqlStr(status)},last_message={SqlStr(message)},last_time={SqlStr(now)} WHERE step_name={SqlStr(step)}");
            }
            catch { }
        }

        // ==================== 从数据库读取启用的流程（优先 work_flow，其次 process_flow） ====================
        static List<ProcessFlowStep> GetEnabledSteps()
        {
            var steps = new List<ProcessFlowStep>();
            try
            {
                CommonInterface Dbsteps = CommonFactory.CreateInstance(DbType == "sqlserver" ? CommonData.sql : CommonData.mysql, ConnStr);
                using (DataTable dt = Dbsteps.ExeForDtl("SELECT id, processname, status, priority FROM work_flow WHERE status=1 ORDER BY priority DESC, id ASC"))
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        foreach (DataRow dr in dt.Rows)
                        {
                            string name = SafeStr(dr["processname"]).ToLower();
                            string display = StepDisplayNames.ContainsKey(name) ? StepDisplayNames[name] : name;
                            steps.Add(new ProcessFlowStep
                            {
                                Id = SafeInt(dr["id"]),
                                Name = name,
                                DisplayName = display,
                                Status = SafeInt(dr["status"]),
                                Priority = SafeInt(dr["priority"])
                            });
                        }
                        Console.WriteLine($"[DB] 读取 work_flow 定制工作流 {steps.Count} 个步骤");
                    }
                    else
                    {
                        Console.WriteLine("[DB] work_flow 表无数据，使用 process_flow 普通流程");
                        using (DataTable dt2 = Dbsteps.ExeForDtl("SELECT id, processname, status FROM process_flow WHERE status=1 ORDER BY id"))
                            if (dt2 != null)
                                foreach (DataRow dr in dt2.Rows)
                                {
                                    string name = SafeStr(dr["processname"]).ToLower();
                                    string display = StepDisplayNames.ContainsKey(name) ? StepDisplayNames[name] : name;
                                    steps.Add(new ProcessFlowStep
                                    {
                                        Id = SafeInt(dr["id"]),
                                        Name = name,
                                        DisplayName = display,
                                        Status = SafeInt(dr["status"]),
                                        Priority = 0
                                    });
                                }
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] 读取流程失败: {ex.Message}");
            }
            return steps;
        }

        // ==================== 工具方法 ====================
        static string Json(object obj) => Js.Serialize(obj);
        static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key) || d[key] == null) return new Dictionary<string, object>();
            if (d[key] is Dictionary<string, object> nested) return nested;
            if (d[key] is System.Collections.Generic.Dictionary<string, object> nested2) return nested2;
            try { return Js.Deserialize<Dictionary<string, object>>(d[key].ToString()) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }
        static string GetStr(Dictionary<string, object> d, string key, string def = "") =>
            d.ContainsKey(key) && d[key] != null ? d[key].ToString() : def;
        static int GetInt(Dictionary<string, object> d, string key, int def = 0) =>
            d.ContainsKey(key) && d[key] != null ? Convert.ToInt32(d[key]) : def;
        static string SafeStr(object val) => val == null || val == DBNull.Value ? "" : val.ToString();
        static int SafeInt(object val) => val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);
        static long SafeLong(object val) => val == null || val == DBNull.Value ? 0 : Convert.ToInt64(val);
        static string SafeDateTime(object val) =>
            val == null || val == DBNull.Value ? "" : Convert.ToDateTime(val).ToString("yyyy-MM-dd HH:mm:ss");

        static List<object> GetJsonArray(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key) || d[key] == null) return new List<object>();
            var arr = d[key];
            if (arr is System.Collections.ArrayList al) return al.Cast<object>().ToList();
            if (arr is object[] oa) return oa.ToList();
            if (arr is IEnumerable<object> ie) return ie.ToList();
            return new List<object>();
        }

        class LogEntry
        {
            public string Step, Display, Level, Content;
            public DateTime Time;
        }

        class ProcessFlowStep
        {
            public int Id;
            public string Name;
            public string DisplayName;
            public int Status;
            public int Priority;
        }

        class WorkFlowStep : ProcessFlowStep
        {
        }
    }
}
