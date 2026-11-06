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
    /// <summary>
    /// 工序编排执行服务。
    /// 顺序与参数来自 ExeDir/flow.json（FlowConfig），数据库仅用于写运行日志与状态。
    /// 执行语义：once=按顺序跑一遍；loop=跑完一轮按间隔继续，且失败即停（避免"一直跳过假装正常"）。
    /// </summary>
    public static class WorkflowService
    {
        /// <summary>停止标志文件：子程序检测到此文件存在即终止流程</summary>
        static readonly string StopFlagFile = Path.Combine(AppState.SubExeDir, "stop.flag");

        // 退出码约定（与子程序侧保持一致）
        const int ExitOk = 0;
        const int ExitNoExe = 2;
        const int ExitStartFail = 3;
        const int ExitTimeout = 4;
        const int ExitException = 5;
        const int ExitSimFail = 99;

        // ==================== 查询 ====================

        /// <summary>返回完整编排配置 + 运行状态，前端编排页直接消费这个结构</summary>
        public static string GetStatus()
        {
            var cfg = FlowConfig.Load();
            return AppState.Json(new
            {
                success = true,
                running = AppState.WorkflowRunning,
                paused = AppState.WorkflowPaused,
                mode = cfg.mode,
                simulate = cfg.simulate,
                loopIntervalMs = cfg.loopIntervalMs,
                steps = cfg.steps.Select(s => new
                {
                    name = s.name,
                    display = s.display,
                    enabled = s.enabled,
                    timeoutSec = s.timeoutSec,
                    onFail = s.onFail,
                    simMs = s.simMs,
                    simFail = s.simFail,
                    status = GetRuntimeStatus(s.name)
                })
            });
        }

        static string GetRuntimeStatus(string name)
        {
            string st;
            return AppState.StepRuntimeStatus.TryGetValue(name, out st) ? st : "idle";
        }

        // ==================== 可挑选编排（amis 页使用） ====================

        /// <summary>运行状态码转中文，供 amis 表格直接显示（input-table 的 mapping 列在该版本不生效）。</summary>
        static string StatusText(string code)
        {
            switch (code)
            {
                case "running": return "执行中";
                case "completed": return "已完成";
                case "failed": return "失败";
                case "skipped": return "已跳过";
                default: return "待执行";
            }
        }

        static object MapStep(FlowStepConfig s)
        {
            string st = GetRuntimeStatus(s.name);
            return new
            {
                name = s.name,
                display = s.display,
                enabled = s.enabled,
                timeoutSec = s.timeoutSec,
                onFail = s.onFail,
                simMs = s.simMs,
                simFail = s.simFail,
                status = st,
                statusText = StatusText(st)
            };
        }

        /// <summary>
        /// 编排计划：steps=流程中的环节（按执行顺序），available=池子里未启用的环节。
        /// amis 页两个表格各取一段。
        /// </summary>
        public static string GetPlan()
        {
            var cfg = FlowConfig.Load();
            return AppState.Json(new
            {
                success = true,
                running = AppState.WorkflowRunning,
                paused = AppState.WorkflowPaused,
                mode = cfg.mode,
                simulate = cfg.simulate,
                loopIntervalMs = cfg.loopIntervalMs,
                steps = cfg.steps.Where(s => s.enabled).Select(s => MapStep(s)).ToList(),
                available = cfg.steps.Where(s => !s.enabled).Select(s => MapStep(s)).ToList()
            });
        }

        /// <summary>
        /// 保存编排计划：提交上来的视为"在流程中"；
        /// 旧配置里被删掉的环节不丢弃，降级为未启用放回池子，避免配置真丢。
        /// </summary>
        public static string SavePlan(string raw, bool andStart)
        {
            if (AppState.WorkflowRunning)
                return AppState.Json(new { success = false, message = "工作流运行中，请先停止再修改编排" });

            if (string.IsNullOrWhiteSpace(raw))
                return AppState.Json(new { success = false, message = "请求体为空" });

            FlowConfig incoming;
            try { incoming = AppState.Js.Deserialize<FlowConfig>(raw); }
            catch (Exception ex) { return AppState.Json(new { success = false, message = "配置解析失败: " + ex.Message }); }

            if (incoming == null)
                return AppState.Json(new { success = false, message = "配置解析失败" });

            LogHelper.Debug("[Flow] SavePlan 收到: " +
                (string.IsNullOrEmpty(raw) ? "(空)" : (raw.Length > 160 ? raw.Substring(0, 160) + "…" : raw)));

            // 是否携带了步骤数组：没带就只更新运行参数，避免误清空整个流程
            bool hasSteps = incoming.steps != null && incoming.steps.Count > 0;

            incoming.Normalize();

            if (!hasSteps)
            {
                var cur = FlowConfig.Load();
                cur.mode = incoming.mode;
                cur.simulate = incoming.simulate;
                cur.loopIntervalMs = incoming.loopIntervalMs;

                if (!FlowConfig.Save(cur))
                    return AppState.Json(new { success = false, message = "写入 flow.json 失败，请检查目录权限" });

                LogHelper.Info("[Flow] 仅更新运行参数：模式=" + cur.mode + (cur.simulate ? "（仿真）" : "（真实）"));

                return andStart
                    ? Start(null)
                    : AppState.Json(new { success = true, message = "运行参数已保存" });
            }

            if (incoming.steps.Count == 0)
                return AppState.Json(new { success = false, message = "至少保留一个环节" });

            var old = FlowConfig.Load();
            var submitted = new HashSet<string>(incoming.steps.Select(s => s.name), StringComparer.OrdinalIgnoreCase);

            // 提交上来的都算"在流程中"
            foreach (var s in incoming.steps) s.enabled = true;

            // 旧配置里被移除的，保留为未启用，放回池子
            foreach (var o in old.steps)
            {
                if (submitted.Contains(o.name)) continue;
                var c = o.Clone();
                c.enabled = false;
                incoming.steps.Add(c);
            }

            // 从未出现过的已知环节也补进池子
            incoming.MergeKnownSteps();

            if (!FlowConfig.Save(incoming))
                return AppState.Json(new { success = false, message = "写入 flow.json 失败，请检查目录权限" });

            LogHelper.Info("[Flow] 编排计划已保存：流程中 " + incoming.steps.Count(s => s.enabled) +
                           " 步，池中 " + incoming.steps.Count(s => !s.enabled) + " 步");

            if (!andStart)
                return AppState.Json(new { success = true, message = "已保存" });

            return Start(null);
        }

        // ==================== 配置保存 ====================

        /// <summary>整表覆盖保存：前端把拖拽后的完整数组发回来即可</summary>
        public static string SaveFlow(string raw)
        {
            if (AppState.WorkflowRunning)
                return AppState.Json(new { success = false, message = "工作流运行中，请先停止再修改编排" });

            if (string.IsNullOrWhiteSpace(raw))
                return AppState.Json(new { success = false, message = "请求体为空" });

            try
            {
                var cfg = AppState.Js.Deserialize<FlowConfig>(raw);
                if (cfg == null)
                    return AppState.Json(new { success = false, message = "配置解析失败" });

                cfg.Normalize();
                if (cfg.steps.Count == 0)
                    return AppState.Json(new { success = false, message = "至少保留一个步骤" });

                if (!FlowConfig.Save(cfg))
                    return AppState.Json(new { success = false, message = "写入 flow.json 失败，请检查目录权限" });

                LogHelper.Info("[Flow] 编排已更新，共 " + cfg.steps.Count + " 步，启用 " +
                               cfg.steps.Count(s => s.enabled) + " 步，模式=" + cfg.mode +
                               (cfg.simulate ? "（仿真）" : "（真实）"));

                return AppState.Json(new { success = true, message = "已保存" });
            }
            catch (Exception ex)
            {
                LogHelper.Error("[Flow] 保存编排异常: " + ex.Message);
                return AppState.Json(new { success = false, message = "保存失败: " + ex.Message });
            }
        }

        // ==================== 启动 / 停止 / 暂停 ====================

        public static string Start(string raw)
        {
            if (AppState.WorkflowRunning)
                return AppState.Json(new { success = false, message = "工作流正在运行中" });

            var cfg = FlowConfig.Load();
            ApplyOverrides(cfg, raw);
            FlowConfig.Save(cfg);

            if (cfg.steps.Count(s => s.enabled) == 0)
                return AppState.Json(new { success = false, message = "没有启用的步骤，请先在编排页勾选" });

            AppState.WorkflowRunning = true;
            AppState.WorkflowPaused = false;
            AppState.WorkflowCts = new CancellationTokenSource();
            var ct = AppState.WorkflowCts.Token;

            // 清理上次的停止标志
            try { if (File.Exists(StopFlagFile)) File.Delete(StopFlagFile); } catch { }

            while (AppState.AllLogs.Count > 0)
                AppState.AllLogs.TryDequeue(out _);

            // 重置运行时状态
            foreach (var step in cfg.steps)
                AppState.StepRuntimeStatus[step.name] = "idle";

            Task.Run(async () =>
            {
                try
                {
                    await RunWorkflow(cfg, ct);
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

            return AppState.Json(new
            {
                success = true,
                message = "工作流已启动（" + (cfg.mode == FlowConfig.ModeLoop ? "轮播" : "单次") +
                          (cfg.simulate ? " / 仿真" : " / 真实") + "）"
            });
        }

        /// <summary>启动时允许界面覆盖模式与仿真开关，并落盘保持一致</summary>
        static void ApplyOverrides(FlowConfig cfg, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            try
            {
                var d = AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                if (d == null) return;

                if (d.ContainsKey("mode") && d["mode"] != null)
                    cfg.mode = string.Equals(d["mode"].ToString(), FlowConfig.ModeLoop, StringComparison.OrdinalIgnoreCase)
                        ? FlowConfig.ModeLoop : FlowConfig.ModeOnce;

                if (d.ContainsKey("simulate") && d["simulate"] != null)
                    cfg.simulate = Convert.ToBoolean(d["simulate"]);

                if (d.ContainsKey("loopIntervalMs") && d["loopIntervalMs"] != null)
                    cfg.loopIntervalMs = Convert.ToInt32(d["loopIntervalMs"]);
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Workflow] 启动参数解析失败，沿用 flow.json: " + ex.Message);
            }
        }

        public static string Stop()
        {
            if (!AppState.WorkflowRunning)
                return AppState.Json(new { success = false, message = "工作流未运行" });

            // 1. 先写停止标志，让子程序有机会自行收尾
            try { File.WriteAllText(StopFlagFile, "stop"); } catch { }

            // 2. 取消任务：RunOneStep 里注册的回调会结束当前子进程
            try { if (AppState.WorkflowCts != null) AppState.WorkflowCts.Cancel(); } catch { }

            // 3. 兜底强杀：子程序可能卡在 PLC 读取里，轮询不到 stop.flag
            var p = AppState.CurrentProcess;
            if (p != null) TryKill(p);

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

        // ==================== 初始化 ====================

        /// <summary>
        /// 启动时确保 flow.json 存在。方法名沿用旧名，避免改动 Program.cs 调用点。
        /// 不存在时从 work_flow 表迁移；存在时补齐新出现的子程序（默认停用）。
        /// </summary>
        public static void EnsureSteps()
        {
            try
            {
                FlowConfig.EnsureExists();
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Workflow] 初始化编排配置失败: " + ex.Message);
            }
        }

        // ==================== 执行引擎 ====================

        static async Task RunWorkflow(FlowConfig cfg, CancellationToken ct)
        {
            var enabled = cfg.steps.Where(s => s.enabled).ToList();

            WriteStepLog("workflow", "主控程序", "INFO",
                "工作流开始（" + (cfg.mode == FlowConfig.ModeLoop ? "轮播" : "单次") +
                " / " + (cfg.simulate ? "仿真" : "真实") + "，共 " + enabled.Count + " 步）");

            if (enabled.Count == 0)
            {
                WriteStepLog("workflow", "主控程序", "WARN", "没有启用的步骤");
                return;
            }

            int round = 0;

            while (!ct.IsCancellationRequested)
            {
                round++;
                if (cfg.mode == FlowConfig.ModeLoop && round > 1)
                    WriteStepLog("workflow", "主控程序", "INFO", "—— 第 " + round + " 轮 ——");

                bool abort = false;

                foreach (var step in enabled)
                {
                    if (ct.IsCancellationRequested) { abort = true; break; }

                    await WaitWhilePaused(ct);
                    if (ct.IsCancellationRequested) { abort = true; break; }

                    WriteStepLog(step.name, step.display, "INFO", "开始执行");
                    SetStepStatus(step.name, step.display, "running", "执行中");

                    int code = await RunOneStep(step, cfg.simulate, ct);

                    if (ct.IsCancellationRequested)
                    {
                        SetStepStatus(step.name, step.display, "idle", "已中断");
                        abort = true;
                        break;
                    }

                    if (code == ExitOk)
                    {
                        SetStepStatus(step.name, step.display, "completed", "执行完成");
                        WriteStepLog(step.name, step.display, "INFO", "完成");
                        continue;
                    }

                    // 失败分支
                    string reason = DescribeExit(code);
                    SetStepStatus(step.name, step.display, "failed", reason);
                    WriteStepLog(step.name, step.display, "ERROR", "失败：" + reason);

                    // 轮播模式强制失败即停，否则产线会"一直跳过、看起来正常"
                    bool mustStop = cfg.mode == FlowConfig.ModeLoop || step.onFail == FlowConfig.FailStop;
                    if (mustStop)
                    {
                        WriteStepLog("workflow", "主控程序", "ERROR",
                            "已在【" + step.display + "】中止（" +
                            (cfg.mode == FlowConfig.ModeLoop ? "轮播模式强制失败即停" : "失败策略=停在失败步") + "）");
                        abort = true;
                        break;
                    }

                    SetStepStatus(step.name, step.display, "skipped", "已跳过");
                    WriteStepLog(step.name, step.display, "WARN", "按配置跳过，继续下一步");
                }

                if (abort) break;

                if (cfg.mode != FlowConfig.ModeLoop)
                {
                    WriteStepLog("workflow", "主控程序", "INFO", "单次执行完毕");
                    break;
                }

                await Task.Delay(Math.Max(100, cfg.loopIntervalMs), ct);
            }
        }

        static async Task WaitWhilePaused(CancellationToken ct)
        {
            if (!AppState.WorkflowPaused) return;

            WriteStepLog("workflow", "主控程序", "INFO", "已暂停，等待恢复…");
            while (AppState.WorkflowPaused && !ct.IsCancellationRequested)
                await Task.Delay(300, ct);

            if (!ct.IsCancellationRequested)
                WriteStepLog("workflow", "主控程序", "INFO", "已恢复");
        }

        /// <summary>执行单个步骤，返回退出码。0 表示成功。</summary>
        static async Task<int> RunOneStep(FlowStepConfig step, bool simulate, CancellationToken ct)
        {
            if (simulate)
                return await RunSimulated(step, ct);

            string exePath = Path.Combine(AppState.SubExeDir, step.name + ".exe");
            if (!File.Exists(exePath))
            {
                WriteStepLog(step.name, step.display, "ERROR", "程序不存在: " + exePath);
                return ExitNoExe;
            }

            Process process = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process = Process.Start(psi);
                if (process == null)
                {
                    WriteStepLog(step.name, step.display, "ERROR", "进程启动失败");
                    return ExitStartFail;
                }

                AppState.CurrentProcess = process;
                int timeoutMs = Math.Max(1, step.timeoutSec) * 1000;
                WriteStepLog(step.name, step.display, "INFO",
                    "已启动 (PID " + process.Id + ")，超时 " + step.timeoutSec + "s");

                // 取消时立刻结束进程，避免卡在 WaitForExit 上
                using (ct.Register(() => TryKill(process)))
                {
                    bool exited = await Task.Run(() => WaitExit(process, timeoutMs), ct);
                    if (!exited)
                    {
                        TryKill(process);
                        WriteStepLog(step.name, step.display, "ERROR",
                            "超时 " + step.timeoutSec + "s，已强制结束");
                        return ExitTimeout;
                    }
                }

                int code = process.ExitCode;
                WriteStepLog(step.name, step.display, "INFO",
                    "已退出 (PID " + process.Id + "，退出码 " + code + ")");
                return code;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteStepLog(step.name, step.display, "ERROR", "执行异常: " + ex.Message);
                TryKill(process);
                return ExitException;
            }
            finally
            {
                AppState.CurrentProcess = null;
                try { if (process != null) process.Dispose(); } catch { }
            }
        }

        static async Task<int> RunSimulated(FlowStepConfig step, CancellationToken ct)
        {
            int ms = Math.Max(100, step.simMs);
            WriteStepLog(step.name, step.display, "INFO",
                "仿真执行，预计 " + ms + "ms" + (step.simFail ? "（配置为失败）" : ""));

            await Task.Delay(ms, ct);
            return step.simFail ? ExitSimFail : ExitOk;
        }

        static bool WaitExit(Process p, int timeoutMs)
        {
            try { return p.WaitForExit(timeoutMs); }
            catch { return true; }   // 进程已被取消回调杀掉
        }

        static void TryKill(Process p)
        {
            try
            {
                if (p != null && !p.HasExited) p.Kill();
            }
            catch { }
        }

        static string DescribeExit(int code)
        {
            switch (code)
            {
                case ExitNoExe: return "子程序文件不存在";
                case ExitStartFail: return "进程启动失败";
                case ExitTimeout: return "执行超时被强制结束";
                case ExitException: return "启动异常";
                case ExitSimFail: return "仿真失败（simFail=true）";
                default: return "退出码 " + code;
            }
        }

        // ==================== 日志与状态 ====================

        public static void WriteStepLog(string step, string display, string level, string content)
        {
            var entry = new LogEntry
            {
                Id = Interlocked.Increment(ref AppState.LogSeq),
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

        public static void UpdateStepStatus(string step, string display, string status, string message)
        {
            try
            {
                var db = DatabaseRepository.CreateDb();
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                int stepOrder = GetStepOrder(step);
                if (AppState.DbType == "sqlserver")
                {
                    string sql = "IF NOT EXISTS (SELECT 1 FROM workflow_step WHERE step_name=" + SqlStr(step) + ") "
                        + "INSERT INTO workflow_step (step_name, display_name, last_status, last_message, last_time, step_order) VALUES ("
                        + SqlStr(step) + ", " + SqlStr(display) + ", " + SqlStr(status) + ", " + SqlStr(message) + ", " + SqlStr(now) + ", " + stepOrder + ") "
                        + "ELSE UPDATE workflow_step SET display_name=" + SqlStr(display) + ", last_status=" + SqlStr(status)
                        + ", last_message=" + SqlStr(message) + ", last_time=" + SqlStr(now) + " WHERE step_name=" + SqlStr(step);
                    db.ExecuteSql(sql);
                }
                else
                {
                    string sql = "INSERT INTO workflow_step (step_name, display_name, last_status, last_message, last_time, step_order) VALUES ("
                        + SqlStr(step) + ", " + SqlStr(display) + ", " + SqlStr(status) + ", " + SqlStr(message) + ", " + SqlStr(now) + ", " + stepOrder + ") "
                        + "ON DUPLICATE KEY UPDATE display_name=" + SqlStr(display) + ", last_status=" + SqlStr(status)
                        + ", last_message=" + SqlStr(message) + ", last_time=" + SqlStr(now);
                    db.ExecuteSql(sql);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn("[Workflow] 更新步骤状态失败: " + ex.Message);
            }
        }

        /// <summary>同时更新内存状态（前端编排页实时读这个）与数据库</summary>
        static void SetStepStatus(string step, string display, string status, string message)
        {
            AppState.StepRuntimeStatus[step] = status;
            UpdateStepStatus(step, display, status, message);
        }

        /// <summary>步骤在 flow.json 中的位置，作为 step_order 落库</summary>
        static int GetStepOrder(string step)
        {
            try
            {
                var cfg = FlowConfig.Load();
                for (int i = 0; i < cfg.steps.Count; i++)
                {
                    if (string.Equals(cfg.steps[i].name, step, StringComparison.OrdinalIgnoreCase))
                        return i + 1;
                }
            }
            catch { }
            return 0;
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
