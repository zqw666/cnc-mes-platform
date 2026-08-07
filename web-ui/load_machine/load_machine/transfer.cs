using DataCommonOperate;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml.Linq;

namespace load_machine
{
    public class transfer
    {
        // 共享服务器配置
        static string ShareServerPath = @"\\192.168.1.100\share";  // 共享服务器路径
        static string LocalTempPath = @"D:\nc_files";               // 本地临时目录
        static string MachineNcPath = @"C:\NCFiles";                // 机床NC文件目录

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

        // 数据库
        static string ConnStr = "Server=localhost;Database=qianduantest;Uid=root;Pwd=123456;CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
        static CommonInterface Mysql;

        /// <summary>
        /// 执行传输程序流程
        /// </summary>
       public bool RunTransferProcess(string ip, string workpiecename)
        {
            bool flag = false;
            LoadConfig();
            LogHelper.Info( "开始执行传输程序流程...");
            LogHelper.Info( $"获取到待上传工件: {workpiecename}");

            // 从共享服务器搜索名称为"工件名称后缀.nc"的文件
            string ncFileName = workpiecename + ".nc";
            string shareFilePath = Path.Combine(ShareServerPath, ncFileName);
            string localFilePath = Path.Combine(LocalTempPath, ncFileName);
            string machineFilePath = Path.Combine(MachineNcPath, ncFileName);

            LogHelper.Info( $"共享服务器文件路径: {shareFilePath}");

            // 检查共享服务器文件是否存在
            if (!File.Exists(shareFilePath))
            {
                LogHelper.Error( $"共享服务器文件不存在: {shareFilePath}");
                return false;
            }

            // 用文件流复制到本地指定路径
            try
            {
                File.Copy(shareFilePath, localFilePath, true);
                LogHelper.Info( $"文件已复制到本地: {localFilePath}");
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"复制文件到本地失败: {ex.Message}");
            }

            // 复制成功后用文件流复制到机床指定路径
            try
            {
                // 通过SMB协议复制到机床
                string machineSharePath = $@"\\{ip}\{MachineNcPath.Replace(":", "")}";

                // 确保机床目标目录存在（如果可以访问）
                try
                {
                    if (!Directory.Exists(machineSharePath))
                    {
                        // 尝试创建目录
                        try
                        {
                            Directory.CreateDirectory(machineSharePath);
                        }
                        catch
                        {
                            // 目录可能无法创建，继续尝试复制文件
                        }
                    }
                }
                catch
                {
                    // 忽略目录检查错误
                }

                // 复制文件到机床
                string machineFileFullPath = $@"\\{ip}\{MachineNcPath.Replace(":", "")}\{ncFileName}";
                File.Copy(localFilePath, machineFileFullPath, true);
                if(File.Exists(machineFileFullPath))
                {
                    flag = true;
                    LogHelper.Info( $"文件已复制到机床: {machineFileFullPath}");
                }
                else
                {
                    flag = false;
                    LogHelper.Error( $"复制文件到机床失败");
                }
            }
            catch (Exception ex)
            {
                flag = false;
                LogHelper.Error( $"复制文件到机床失败: {ex.Message}");
                // 即使复制到机床失败，本地文件已存在，可以手动上传
            }

            LogHelper.Info( "传输程序流程结束");
            return flag;
        }

        /// <summary>
        /// 更新rack_administrate表的workstate状态
        /// </summary>
        /// <param name="workpieceName">工件名称</param>
        /// <param name="workstate">工作状态 (0: 待加工, 1: 上传程序成功)</param>
        /// <returns>是否更新成功</returns>
        static bool UpdateRackAdministrateWorkstate(string workpieceName, int workstate)
        {
            try
            {
                string sql = $"UPDATE rack_administrate SET workstate = {workstate} WHERE workpiecename = '{workpieceName}'";
                bool result = Mysql.ExecuteSql(sql);

                if (result)
                {
                    LogHelper.Info( $"更新工件 {workpieceName} 的workstate为 {workstate} 成功");
                }
                else
                {
                    LogHelper.Error( $"更新工件 {workpieceName} 的workstate失败");
                }
                return result;
            }
            catch (Exception ex)
            {
                LogHelper.Error( $"更新工件状态异常: {ex.Message}");
                return false;
            }
        }

        public static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));

                    if (cfg.ContainsKey("database"))
                    {
                        var dbRaw = cfg["database"];
                        Dictionary<string, object> db = dbRaw is Dictionary<string, object> dDict
                            ? dDict
                            : Js.Deserialize<Dictionary<string, object>>(dbRaw.ToString());
                        string host = db.ContainsKey("host") ? db["host"].ToString() : "localhost";
                        string dbname = db.ContainsKey("database") ? db["database"].ToString() : "qianduantest";
                        string user = db.ContainsKey("user") ? db["user"].ToString() : "root";
                        string pwd = db.ContainsKey("password") ? db["password"].ToString() : "123456";
                        ConnStr = $"Server={host};Database={dbname};Uid={user};Pwd={pwd};CharacterSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=True;ConnectionReset=False;";
                    }

                    // 加载共享服务器配置
                    if (cfg.ContainsKey("share_server"))
                    {
                        var shareRaw = cfg["share_server"];
                        Dictionary<string, object> share = shareRaw is Dictionary<string, object> sDict
                            ? sDict
                            : Js.Deserialize<Dictionary<string, object>>(shareRaw.ToString());
                        if (share.ContainsKey("path")) ShareServerPath = share["path"].ToString();
                        if (share.ContainsKey("local_temp")) LocalTempPath = share["local_temp"].ToString();
                        if (share.ContainsKey("machine_nc_path")) MachineNcPath = share["machine_nc_path"].ToString();
                    }
                }

                // 初始化数据库连接
                Mysql = CommonFactory.CreateInstance(CommonData.mysql, ConnStr);
            }
            catch (Exception ex)
            {
                LogHelper.Warn( "加载配置失败，使用默认配置: " + ex.Message);
            }
        }

        /// <summary>
        /// 更新UI界面日志
        /// </summary>
        /// <param name="level"></param>

        /// <summary>
        /// 读取 Excel(.xlsx) 指定单元格内容
        /// 基于 Open XML 格式，无需外部依赖
        /// </summary>
        /// <param name="filePath">.xlsx 文件路径</param>
        /// <param name="cellRef">单元格编号，如 "A1"、"B5"、"AA10"</param>
        /// <returns>单元格文本内容，失败返回空字符串</returns>
        public static string ReadExcelCell(string filePath, string cellRef)
        {
            try
            {
                if (!File.Exists(filePath))
                    return "";

                // 解析单元格编号: 列字母 → 数字
                string colLetters = "";
                string rowStr = "";
                foreach (char c in cellRef.ToUpper())
                {
                    if (c >= 'A' && c <= 'Z') colLetters += c;
                    else rowStr += c;
                }
                int row = int.Parse(rowStr);
                string colRef = colLetters; // 保留原样用于匹配

                using (var zip = ZipFile.OpenRead(filePath))
                {
                    // 读取共享字符串表
                    var sstEntry = zip.GetEntry("xl/sharedStrings.xml");
                    List<string> sharedStrings = new List<string>();
                    if (sstEntry != null)
                    {
                        using (var stream = sstEntry.Open())
                        {
                            var sstDoc = XDocument.Load(stream);
                            sharedStrings = sstDoc.Descendants()
                                .Where(e => e.Name.LocalName == "t")
                                .Select(e => e.Value)
                                .ToList();
                        }
                    }

                    // 读取工作表
                    var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
                    if (sheetEntry == null)
                        return "";
                    using (var stream = sheetEntry.Open())
                    {
                        var sheetDoc = XDocument.Load(stream);
                        var ns = sheetDoc.Root.Name.Namespace;
                        // 查找指定行
                        var rowElems = sheetDoc.Descendants(ns + "row")
                            .Where(r => (string)r.Attribute("r") == row.ToString());
                        foreach (var rowElem in rowElems)
                        {
                            // 查找指定列
                            var cell = rowElem.Elements(ns + "c")
                                .FirstOrDefault(c => (string)c.Attribute("r") == cellRef);
                            if (cell != null)
                            {
                                var valElem = cell.Element(ns + "v");
                                if (valElem == null) return "";
                                string val = valElem.Value;
                                // 共享字符串类型
                                string type = (string)cell.Attribute("t");
                                if (type == "s" && int.TryParse(val, out int idx) && idx < sharedStrings.Count)
                                    return sharedStrings[idx];
                                return val;
                            }
                        }
                    }
                }
                return "";
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[ReadExcelCell] 异常: " + ex.Message);
                return "";
            }
        }
    }
}
