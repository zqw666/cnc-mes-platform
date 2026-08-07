using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataCommonOperate;

namespace MachineMonitor
{
    public static class WorkflowService
    {
        public static string GetStatus()
        {
            var steps = GetEnabledSteps();
            return AppState.Json(new
            {
                success = true,
                running = AppState.WorkflowRunning,
                paused = AppState.WorkflowPaused,
                steps = steps.Select(s => new
                {
                    id = s.Id,
                    name = s.Name,
                    displayName = s.DisplayName,
                    status = s.Status,
                    priority = s.Priority
                })
            });
        }

        public static string Start(string raw)
        {
            if (AppState.WorkflowRunning)
                return AppState.Json(new { success = false, message = "工作流正在运行中" });

            AppState.WorkflowRunning = true;
            AppState.WorkflowPaused = false;
            AppState.WorkflowCts = new CancellationTokenSource();
            var ct = AppState.WorkflowCts.Token;

            while (AppState.AllLogs.Count > 0)
                AppState.AllLogs.TryDequeue(out _);

            Task.Run(async () =>
            {
                try
                {
                    await RunWorkflow(ct);
                }
                catch (OperationCanceledException)
                {
                    LogHelper.Info("[Workflow] 工作流已取消");
                }
                catch (Exception ex)
                {
                    LogHelper.Error("[Workflow] 工作流异常: " + ex.Message);
                }
                finally
                {
                    AppState.WorkflowRunning = false;
                    AppState.WorkflowPaused = false;
                    LogHelper.Info("[Workflow] 工作流已结束");
                }
            });

            return AppState.Json(new { success = true, message = "工作流已启动" });
        }

        public static string Stop()
        {
            if (!AppState.WorkflowRunning)
                return AppState.Json(new { success = false, message = "工作流未运行" });
            if (AppState.WorkflowCts != null)
                AppState.WorkflowCts.Cancel();
            AppState.WorkflowRunning = false;
            AppState.WorkflowPaused = false;
            return AppState.Json(new { success = true, message = "工作流已停止" });
        }

        public static string Pause()
        {
            if (!AppState.WorkflowRunning)
                return AppState.Json(new { success = false, message = "工作流未运行" });
            AppState.WorkflowPaused = true;
            return AppState.Json(new { success = true, message = "工作流已暂停" });
        }

        public static string Resume()
        {
            if (!AppState.WorkflowRunning)
                return AppState.Json(new { success = false, message = "工作流未运行" });
            AppState.WorkflowPaused = false;
            return AppState.Json(new { success = true, message = "工作流已恢复" });
        }

        public static void EnsureSteps()
        {
            try
            {
                var db = DatabaseRepository.CreateDb();
                foreach (var kv in AppState.StepDisplayNames)
                {
                    db.ExecuteSql(GetUpsertSql(kv.Key));
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Workflow] 确保工作流步骤失败: " + ex.Message);
            }
        }

        static string GetUpsertSql(string processName)
        {
            if (AppState.DbType == "sqlserver")
            {
                return "IF NOT EXISTS (SELECT 1 FROM work_flow WHERE processname='" + processName + "') "
                    + "INSERT INTO work_flow (processname, status, priority) VALUES ('" + processName + "', 1, 0)";
            }
            else
            {
                return "INSERT INTO work_flow (processname, status, priority) VALUES ('" + processName + "', 1, 0) "
                    + "ON DUPLICATE KEY UPDATE status=1";
            }
        }

        static async Task RunWorkflow(CancellationToken ct)
        {
            LogHelper.Info("[Workflow] 工作流开始执行");
            var steps = GetEnabledSteps();
            if (steps.Count == 0)
            {
                LogHelper.Warn("[Workflow] 没有启用的工作流步骤");
                return;
            }

            steps = steps.OrderByDescending(s => s.Priority).ThenBy(s => s.Id).ToList();

            while (!ct.IsCancellationRequested)
            {
                foreach (var step in steps)
                {
                    if (ct.IsCancellationRequested) break;
                    if (AppState.WorkflowPaused)
                    {
                        LogHelper.Info("[Workflow] 工作流已暂停，等待恢复...");
                        while (AppState.WorkflowPaused && !ct.IsCancellationRequested)
                            await Task.Delay(500, ct);
                    }

                    LogHelper.Info("[Workflow] 开始执行步骤: " + step.DisplayName);
                    await RunSubProcess(step.Name, step.DisplayName, ct);
                    LogHelper.Info("[Workflow] 步骤完成: " + step.DisplayName);
                }

                await Task.Delay(1000, ct);
            }
        }

        static async Task RunSubProcess(string name, string displayName, CancellationToken ct)
        {
            string exePath = Path.Combine(AppState.SubExeDir, name + ".exe");
            if (!File.Exists(exePath))
            {
                LogHelper.Warn("[SubProcess] " + displayName + " 程序不存在: " + exePath);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                var process = Process.Start(psi);
                if (process != null)
                {
                    LogHelper.Info("[SubProcess] " + displayName + " 已启动 (PID: " + process.Id + ")");
                    await Task.Run(() => process.WaitForExit(), ct);
                    LogHelper.Info("[SubProcess] " + displayName + " 已退出 (PID: " + process.Id + ")");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("[SubProcess] " + displayName + " 执行失败: " + ex.Message);
            }
        }

        public static List<ProcessFlowStep> GetEnabledSteps()
        {
            var steps = new List<ProcessFlowStep>();
            try
            {
                var db = DatabaseRepository.CreateDb();
                var dt = db.ExeForDtl("SELECT id, processname, status, priority FROM work_flow WHERE status=1 ORDER BY priority DESC, id ASC");
                if (dt != null && dt.Rows.Count > 0)
                {
                    foreach (DataRow dr in dt.Rows)
                    {
                        string name = AppState.SafeStr(dr["processname"]).ToLower();
                        string display = AppState.StepDisplayNames.ContainsKey(name) ? AppState.StepDisplayNames[name] : name;
                        steps.Add(new ProcessFlowStep
                        {
                            Id = AppState.SafeInt(dr["id"]),
                            Name = name,
                            DisplayName = display,
                            Status = AppState.SafeInt(dr["status"]),
                            Priority = AppState.SafeInt(dr["priority"])
                        });
                    }
                    return steps;
                }

                var dt2 = db.ExeForDtl("SELECT id, processname, status FROM process_flow WHERE status=1 ORDER BY id");
                if (dt2 != null)
                {
                    foreach (DataRow dr in dt2.Rows)
                    {
                        string name = AppState.SafeStr(dr["processname"]).ToLower();
                        string display = AppState.StepDisplayNames.ContainsKey(name) ? AppState.StepDisplayNames[name] : name;
                        steps.Add(new ProcessFlowStep
                        {
                            Id = AppState.SafeInt(dr["id"]),
                            Name = name,
                            DisplayName = display,
                            Status = AppState.SafeInt(dr["status"]),
                            Priority = 0
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("[Workflow] 读取流程失败: " + ex.Message);
            }
            return steps;
        }

        public static void WriteStepLog(string step, string display, string level, string content)
        {
            var entry = new LogEntry
            {
                Step = step,
                Display = display,
                Level = level,
                Content = content,
                Time = DateTime.Now
            };
            AppState.AllLogs.Enqueue(entry);
            while (AppState.AllLogs.Count > 1000)
                AppState.AllLogs.TryDequeue(out _);

            try
            {
                var db = DatabaseRepository.CreateDb();
                string sql = "INSERT INTO workflow_log (step_name, level, content, created_at) VALUES ("
                    + SqlStr(step) + ", " + SqlStr(level) + ", " + SqlStr(content) + ", " + SqlDate(DateTime.Now) + ")";
                db.ExecuteSql(sql);
            }
            catch { }
        }

        public static void UpdateStepStatus(string step, string status, string message)
        {
            try
            {
                var db = DatabaseRepository.CreateDb();
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string sql = "INSERT INTO process_status (workpiece_name, process_status, process_start_time) VALUES ("
                    + SqlStr(step) + ", " + (status == "running" ? 1 : 2) + ", " + SqlStr(now) + ") "
                    + "ON DUPLICATE KEY UPDATE process_status=" + (status == "running" ? 1 : 2)
                    + ", process_end_time=" + (status == "completed" ? SqlStr(now) : "NULL");
                db.ExecuteSql(sql);
            }
            catch { }
        }

        static string SqlStr(string val)
        {
            if (val == null) return "NULL";
            return "'" + val.Replace("'", "''") + "'";
        }

        static string SqlDate(DateTime dt)
        {
            return SqlStr(dt.ToString("yyyy-MM-dd HH:mm:ss"));
        }
    }
}
