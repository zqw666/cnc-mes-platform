using System;
using System.Net;
using System.Threading.Tasks;
using DataCommonOperate;

namespace MachineMonitor
{
    class Program
    {
        static void Main(string[] args)
        {
            LogHelper.Info($"[MachineMonitor] 启动中... 端口: {AppState.Port}");
            LogHelper.Info($"[MachineMonitor] 数据库类型: {(AppState.DbType == "sqlserver" ? "SQL Server" : "MySQL")}");
            LogHelper.Info($"[MachineMonitor] 等待前端启动请求... (调用 /api/workflow/start 开始执行分包)");

            // 初始化
            EnsureSubDir();
            DatabaseRepository.EnsureDatabaseAndTables();
            EnsureDirectories();
            WorkflowService.EnsureSteps();

            // 启动 HTTP 服务（同时监听 localhost 与 127.0.0.1，避免子程序/自身日志上报 localhost 时解析为 IPv6 ::1 而连接失败）
            var listener = new HttpListener();
            try { listener.Prefixes.Add($"http://localhost:{AppState.Port}/"); }
            catch (Exception ex) { LogHelper.Warn("[HTTP] localhost 前缀注册失败: " + ex.Message); }
            try { listener.Prefixes.Add($"http://127.0.0.1:{AppState.Port}/"); }
            catch (Exception ex) { LogHelper.Warn("[HTTP] 127.0.0.1 前缀注册失败: " + ex.Message); }
            listener.Start();
            LogHelper.Info($"[MachineMonitor] HTTP 服务已启动 http://localhost:{AppState.Port}/");
            LogHelper.Info($"[MachineMonitor] 当前状态: 等待启动指令 | 数据查询接口已就绪");

            while (true)
            {
                var ctx = listener.GetContext();
                Task.Run(() => Router.Handle(ctx));
            }
        }

        // ==================== 初始化方法 ====================

        static void EnsureSubDir()
        {
            if (!System.IO.Directory.Exists(AppState.SubExeDir))
                System.IO.Directory.CreateDirectory(AppState.SubExeDir);
        }

        static void EnsureDirectories()
        {
            try
            {
                // 确保日志目录存在
                string logDir = System.IO.Path.Combine(AppState.ExeDir, "logs");
                if (!System.IO.Directory.Exists(logDir))
                    System.IO.Directory.CreateDirectory(logDir);
            }
            catch (Exception ex)
            {
                LogHelper.Warn($"[Init] 创建目录失败: {ex.Message}");
            }
        }
    }
}
