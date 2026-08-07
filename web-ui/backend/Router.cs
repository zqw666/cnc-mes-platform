using System;
using System.IO;
using System.Net;
using System.Text;
using DataCommonOperate;

namespace MachineMonitor
{
    public static class Router
    {
        public static void Handle(HttpListenerContext ctx)
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

            LogHelper.Debug("[" + method + "] " + path);

            string result = "";
            string contentType = "application/json; charset=utf-8";
            byte[] fileBytes = null;
            int notFoundStatus = 0;

            try
            {
                if (HomeController.TryHandle(path, out result, out contentType, out fileBytes, out notFoundStatus))
                {
                }
                else if (path == "/api/dashboard")
                    result = DashboardController.GetDashboard();
                else if (path == "/api/logs")
                    result = LogController.GetLogs(raw);
                else if (path == "/api/workflow/status")
                    result = WorkflowController.GetStatus();
                else if (path == "/api/workflow/start")
                    result = WorkflowController.Start(raw);
                else if (path == "/api/workflow/stop")
                    result = WorkflowController.Stop();
                else if (path == "/api/workflow/pause")
                    result = WorkflowController.Pause();
                else if (path == "/api/workflow/resume")
                    result = WorkflowController.Resume();
                else if (path.StartsWith("/api/machine/"))
                    result = MachineController.ControlMachine(path.Substring("/api/machine/".Length), raw);
                else if (path == "/api/rack/status")
                    result = method == "GET" ? RackController.GetRackStatus() : RackController.UpdateRackStatus(raw);
                else if (path == "/api/rack/create")
                    result = RackController.CreateRack(raw);
                else if (path == "/api/machine/create")
                    result = MachineController.CreateMachine(raw);
                else if (path == "/api/sub/log")
                    result = LogController.ReceiveSubLog(raw);
                else if (path == "/api/config")
                    result = method == "GET" ? ConfigController.GetConfig() : ConfigController.SaveConfig(raw);
                else if (path == "/api/config/raw")
                    result = ConfigController.GetConfigRaw();
                else if (path == "/api/config/debug")
                    result = ConfigController.Debug(raw);
                else
                    result = AppState.Json(new { success = false, message = "未找到接口" });
            }
            catch (Exception ex)
            {
                LogHelper.Error("[ERROR] " + ex.ToString());
                result = AppState.Json(new { success = false, message = ex.Message });
            }

            byte[] buf;
            if (fileBytes != null)
            {
                buf = fileBytes;
                resp.ContentType = contentType;
            }
            else
            {
                buf = Encoding.UTF8.GetBytes(result);
                resp.ContentType = contentType;
            }
            if (notFoundStatus > 0)
                resp.StatusCode = notFoundStatus;
            resp.ContentLength64 = buf.Length;
            resp.OutputStream.Write(buf, 0, buf.Length);
            resp.Close();
        }
    }
}
