using System;
using System.Collections.Generic;
using System.Data;

namespace MachineMonitor
{
    public static class DashboardController
    {
        public static string GetDashboard()
        {
            var machines = new List<object>();
            var db = DatabaseRepository.CreateDb();
            var dt = db.ExeForDtl("SELECT id,machinename,machineip,workstate,status,error_message FROM macmessage");
            if (dt != null && dt.Rows.Count > 0)
            {
                foreach (DataRow dr in dt.Rows)
                {
                    machines.Add(new
                    {
                        id = AppState.SafeInt(dr["id"]),
                        machinename = AppState.SafeStr(dr["machinename"]),
                        machineip = AppState.SafeStr(dr["machineip"]),
                        workstate = AppState.SafeInt(dr["workstate"]),
                        status = AppState.SafeInt(dr["status"]),
                        error_message = AppState.SafeStr(dr["error_message"])
                    });
                }
            }

            string today = DateTime.Now.ToString("yyyy-MM-dd");
            long processed = 0, unprocessed = 0;
            string dateCondition = AppState.DbType == "sqlserver"
                ? "CONVERT(date, process_start_time)='" + today + "'"
                : "DATE(process_start_time)='" + today + "'";

            var dt2 = db.ExeForDtl("SELECT process_status,COUNT(*) as cnt FROM process_status WHERE " + dateCondition + " GROUP BY process_status");
            if (dt2 != null && dt2.Rows.Count > 0)
            {
                foreach (DataRow dr in dt2.Rows)
                {
                    var st = AppState.SafeInt(dr["process_status"]);
                    if (st == 2) processed = AppState.SafeLong(dr["cnt"]);
                    else if (st == 0) unprocessed = AppState.SafeLong(dr["cnt"]);
                }
            }

            return AppState.Json(new
            {
                success = true,
                data = new { machines, today_processed = processed, today_unprocessed = unprocessed, today, workflow_running = AppState.WorkflowRunning }
            });
        }
    }
}
