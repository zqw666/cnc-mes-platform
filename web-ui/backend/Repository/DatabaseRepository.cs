using System;
using System.Collections.Generic;
using System.Data;
using DataCommonOperate;

namespace MachineMonitor
{
    public static class DatabaseRepository
    {
        public static CommonInterface CreateDb()
        {
            return CommonFactory.CreateInstance(
                AppState.DbType == "sqlserver" ? CommonData.sql : CommonData.mysql,
                AppState.ConnStr);
        }

        public static void EnsureDatabaseAndTables()
        {
            try
            {
                if (AppState.DbType == "sqlserver")
                {
                    EnsureSqlServerDatabase(AppState.ConnStr, GetDbName());
                }
                else
                {
                    string noDbConnStr = RemoveDbName(AppState.ConnStr);
                    EnsureMySqlDatabase(noDbConnStr, GetDbName());
                }

                var db = CreateDb();
                string[] requiredTables = {
                    "macmessage", "macworkposstatus", "process_flow", "process_status",
                    "work_flow", "robotmessage", "following_rack", "rack_administrate",
                    "grippers", "knife_rack", "workflow_log"
                };
                foreach (var table in requiredTables)
                {
                    bool exists = AppState.DbType == "sqlserver"
                        ? SqlServerTableExists(db, table)
                        : MySqlTableExists(db, table);
                    if (!exists)
                    {
                        db.ExecuteSql(GetCreateTableSql(table));
                        LogHelper.Info("[DB] 创建表 " + table);
                    }
                }
                EnsureRackAdministrateColumns(db);
            }
            catch (Exception ex)
            {
                LogHelper.Error("[DB] 初始化数据库失败: " + ex.Message);
            }
        }

        static string GetDbName()
        {
            var parts = AppState.ConnStr.Split(';');
            foreach (var p in parts)
            {
                var kv = p.Split('=');
                if (kv.Length == 2 && kv[0].ToLower() == "database")
                    return kv[1];
            }
            return "qianduantest";
        }

        static string RemoveDbName(string connStr)
        {
            var parts = connStr.Split(';');
            var result = new List<string>();
            foreach (var p in parts)
            {
                var kv = p.Split('=');
                if (kv.Length == 2 && kv[0].ToLower() != "database")
                    result.Add(p);
            }
            return string.Join(";", result) + ";";
        }

        static void EnsureMySqlDatabase(string baseConnStr, string dbName)
        {
            try
            {
                var db = CommonFactory.CreateInstance(CommonData.mysql, baseConnStr);
                db.ExecuteSql("CREATE DATABASE IF NOT EXISTS `" + dbName + "` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[DB] 创建MySQL数据库失败: " + ex.Message);
            }
        }

        static void EnsureSqlServerDatabase(string connStr, string dbName)
        {
            try
            {
                var db = CommonFactory.CreateInstance(CommonData.sql, connStr);
                db.ExecuteSql("IF NOT EXISTS (SELECT name FROM sys.databases WHERE name=N'" + dbName + "') CREATE DATABASE [" + dbName + "]");
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[DB] 创建SQL Server数据库失败: " + ex.Message);
            }
        }

        static bool MySqlTableExists(CommonInterface db, string tableName)
        {
            var dt = db.ExeForDtl("SHOW TABLES LIKE '" + tableName + "'");
            return dt != null && dt.Rows.Count > 0;
        }

        static bool SqlServerTableExists(CommonInterface db, string tableName)
        {
            var dt = db.ExeForDtl("SELECT COUNT(*) as cnt FROM sys.tables WHERE name='" + tableName + "'");
            return dt != null && dt.Rows.Count > 0 && Convert.ToInt32(dt.Rows[0]["cnt"]) > 0;
        }

        static void EnsureRackAdministrateColumns(CommonInterface db)
        {
            try
            {
                var dt = db.ExeForDtl("SHOW COLUMNS FROM rack_administrate");
                if (dt == null) return;
                var cols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataRow dr in dt.Rows)
                    cols.Add(dr["Field"].ToString());
                if (!cols.Contains("materialname"))
                {
                    db.ExecuteSql("ALTER TABLE rack_administrate ADD COLUMN materialname VARCHAR(255) DEFAULT NULL");
                    LogHelper.Info("[DB] rack_administrate 补充列 materialname");
                }
                if (!cols.Contains("programname"))
                {
                    db.ExecuteSql("ALTER TABLE rack_administrate ADD COLUMN programname VARCHAR(255) DEFAULT NULL");
                    LogHelper.Info("[DB] rack_administrate 补充列 programname");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[DB] 检查 rack_administrate 列失败: " + ex.Message);
            }
        }

        static string GetCreateTableSql(string tableName)
        {
            if (AppState.DbType == "sqlserver")
            {
                if (tableName == "macmessage") return "CREATE TABLE macmessage (id INT IDENTITY(1,1) PRIMARY KEY, machinename NVARCHAR(100), machineip NVARCHAR(50), workstate INT DEFAULT 0, status INT DEFAULT 0, error_message NVARCHAR(500) NULL)";
                if (tableName == "process_flow") return "CREATE TABLE process_flow (id INT IDENTITY(1,1) PRIMARY KEY, processname NVARCHAR(100), status INT DEFAULT 1)";
                if (tableName == "work_flow") return "CREATE TABLE work_flow (id INT IDENTITY(1,1) PRIMARY KEY, processname NVARCHAR(100), status INT DEFAULT 1, priority INT DEFAULT 0)";
                if (tableName == "process_status") return "CREATE TABLE process_status (id INT IDENTITY(1,1) PRIMARY KEY, process_name NVARCHAR(100), process_status INT, process_start_time DATETIME, process_end_time DATETIME NULL)";
                if (tableName == "workflow_log") return "CREATE TABLE workflow_log (id INT IDENTITY(1,1) PRIMARY KEY, step_name NVARCHAR(100), level NVARCHAR(20), content NVARCHAR(MAX), created_at DATETIME DEFAULT GETDATE())";
            }
            else
            {
                if (tableName == "macmessage") return "CREATE TABLE IF NOT EXISTS macmessage (id INT AUTO_INCREMENT PRIMARY KEY, machinename VARCHAR(100), machineip VARCHAR(50), workstate INT DEFAULT 0, status INT DEFAULT 0, error_message VARCHAR(500) NULL)";
                if (tableName == "process_flow") return "CREATE TABLE IF NOT EXISTS process_flow (id INT AUTO_INCREMENT PRIMARY KEY, processname VARCHAR(100), status INT DEFAULT 1)";
                if (tableName == "work_flow") return "CREATE TABLE IF NOT EXISTS work_flow (id INT AUTO_INCREMENT PRIMARY KEY, processname VARCHAR(100), status INT DEFAULT 1, priority INT DEFAULT 0)";
                if (tableName == "process_status") return "CREATE TABLE IF NOT EXISTS process_status (id INT AUTO_INCREMENT PRIMARY KEY, process_name VARCHAR(100), process_status INT, process_start_time DATETIME, process_end_time DATETIME NULL)";
                if (tableName == "workflow_log") return "CREATE TABLE IF NOT EXISTS workflow_log (id INT AUTO_INCREMENT PRIMARY KEY, step_name VARCHAR(100), level VARCHAR(20), content TEXT, created_at DATETIME DEFAULT CURRENT_TIMESTAMP)";
                if (tableName == "rack_administrate") return "CREATE TABLE IF NOT EXISTS rack_administrate (id INT PRIMARY KEY, racknum INT DEFAULT 1, racktype VARCHAR(50) DEFAULT 'steel', name VARCHAR(100) DEFAULT '', rows INT DEFAULT 0, cols INT DEFAULT 0, scanstatus INT DEFAULT 0, posstatus INT DEFAULT 1, workstate INT DEFAULT 0, rfid VARCHAR(100) DEFAULT '', verifyid VARCHAR(100) DEFAULT '', priority INT DEFAULT 0, workpiecename VARCHAR(255) NULL, materialname VARCHAR(255) NULL, programname VARCHAR(255) NULL)";
                if (tableName == "knife_rack") return "CREATE TABLE IF NOT EXISTS knife_rack (id INT PRIMARY KEY, rfid VARCHAR(100) DEFAULT '', posstatus INT DEFAULT 0, workstate INT DEFAULT 0)";
                if (tableName == "grippers") return "CREATE TABLE IF NOT EXISTS grippers (id VARCHAR(100) PRIMARY KEY, type VARCHAR(50) DEFAULT 'steel', name VARCHAR(100) DEFAULT '', number VARCHAR(10) DEFAULT '1', slotPhotoSignal VARCHAR(50) DEFAULT '', grabTaskNo VARCHAR(50) DEFAULT '', placeTaskNo VARCHAR(50) DEFAULT '')";
            }
            return "";
        }
    }
}
