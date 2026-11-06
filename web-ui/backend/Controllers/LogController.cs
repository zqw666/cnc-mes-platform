using System;
using System.Collections.Generic;
using System.Linq;

namespace MachineMonitor
{
    public static class LogController
    {
        public static string GetLogs(string raw)
        {
            try
            {
                var body = string.IsNullOrEmpty(raw) ? null : AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                int page = body != null && body.ContainsKey("page") ? Convert.ToInt32(body["page"]) : 1;
                int pageSize = body != null && body.ContainsKey("pageSize") ? Convert.ToInt32(body["pageSize"]) : 50;

                var logs = AppState.AllLogs.Reverse().Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(e => new
                    {
                        id = e.Id,
                        step = e.Step,
                        display = e.Display,
                        level = e.Level,
                        content = e.Content,
                        time = e.Time.ToString("yyyy-MM-dd HH:mm:ss")
                    }).ToList();

                return AppState.Json(new
                {
                    success = true,
                    data = new { logs, total = AppState.AllLogs.Count, page, pageSize }
                });
            }
            catch (Exception ex)
            {
                return AppState.Json(new { success = false, message = ex.Message });
            }
        }

        public static string ReceiveSubLog(string raw)
        {
            try
            {
                var body = AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                if (body == null)
                    return AppState.Json(new { success = false, message = "无效的日志数据" });

                string step = body.ContainsKey("step") ? body["step"].ToString() : "";
                string display = body.ContainsKey("display") ? body["display"].ToString() : step;
                string level = body.ContainsKey("level") ? body["level"].ToString() : "info";
                string content = body.ContainsKey("content") ? body["content"].ToString() : "";

                WorkflowService.WriteStepLog(step, display, level, content);
                return AppState.Json(new { success = true });
            }
            catch (Exception ex)
            {
                return AppState.Json(new { success = false, message = ex.Message });
            }
        }
    }
}
