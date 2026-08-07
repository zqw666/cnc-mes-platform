using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;

namespace MachineMonitor
{
    public static class RackController
    {
        public static string GetRackStatus()
        {
            var knifeRack = new List<object>();
            var db = DatabaseRepository.CreateDb();
            var dt1 = db.ExeForDtl("SELECT id,rfid,posstatus,workstate FROM knife_rack");
            if (dt1 != null)
            {
                foreach (DataRow dr in dt1.Rows)
                    knifeRack.Add(new
                    {
                        id = AppState.SafeInt(dr["id"]),
                        rfid = AppState.SafeStr(dr["rfid"]),
                        posstatus = AppState.SafeInt(dr["posstatus"]),
                        workstate = AppState.SafeInt(dr["workstate"])
                    });
            }

            var rackAdmin = new List<object>();
            var dt2 = db.ExeForDtl("SELECT id,rfid,posstatus,workstate,scanstatus,workpiecename,materialname FROM rack_administrate");
            if (dt2 != null)
            {
                foreach (DataRow dr in dt2.Rows)
                    rackAdmin.Add(new
                    {
                        id = AppState.SafeInt(dr["id"]),
                        rfid = AppState.SafeStr(dr["rfid"]),
                        posstatus = AppState.SafeInt(dr["posstatus"]),
                        workstate = AppState.SafeInt(dr["workstate"]),
                        scanstatus = AppState.SafeInt(dr["scanstatus"]),
                        workpiecename = AppState.SafeStr(dr["workpiecename"]),
                        materialname = AppState.SafeStr(dr["materialname"])
                    });
            }

            int rackUpdateCount = 0;
            try
            {
                var dt3 = db.ExeForDtl("SELECT COUNT(*) as cnt FROM rack_administrate WHERE scanstatus=0 AND posstatus=1");
                if (dt3 != null && dt3.Rows.Count > 0)
                    rackUpdateCount = AppState.SafeInt(dt3.Rows[0]["cnt"]);
            }
            catch { }

            return AppState.Json(new
            {
                success = true,
                data = new { knifeRack, rackAdmin, rackUpdateCount }
            });
        }

        public static string UpdateRackStatus(string raw)
        {
            try
            {
                var body = AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                if (body == null)
                    return AppState.Json(new { success = false, message = "无效的 JSON 数据" });

                string rackId = body.ContainsKey("rackId") ? body["rackId"].ToString() : "";
                int position = body.ContainsKey("position") ? Convert.ToInt32(body["position"]) : 0;
                string status = body.ContainsKey("status") ? body["status"].ToString() : "";

                var db = DatabaseRepository.CreateDb();
                string sql = "";
                if (status == "to-scan")
                    sql = "UPDATE rack_administrate SET scanstatus=0 WHERE id LIKE '" + position + "%' AND posstatus=1";
                else if (status == "scanned")
                    sql = "UPDATE rack_administrate SET scanstatus=1 WHERE id=" + position;
                else if (status == "occupied")
                    sql = "UPDATE rack_administrate SET workstate=1 WHERE id='" + rackId + "'";
                else if (status == "empty")
                    sql = "UPDATE rack_administrate SET workstate=0,workpiecename=NULL WHERE id='" + rackId + "'";

                if (!string.IsNullOrEmpty(sql))
                    db.ExecuteSql(sql);

                return AppState.Json(new { success = true, message = "料架状态已更新" });
            }
            catch (Exception ex)
            {
                return AppState.Json(new { success = false, message = ex.Message });
            }
        }

        public static string CreateRack(string raw)
        {
            try
            {
                var body = AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                if (body == null)
                    return AppState.Json(new { success = false, message = "无效的 JSON 数据" });

                var cfg = AppState.Js.Deserialize<Dictionary<string, object>>(System.IO.File.ReadAllText(AppState.ConfigPath));
                if (cfg == null)
                    return AppState.Json(new { success = false, message = "读取配置文件失败" });

                var racks = new List<object>();
                if (cfg.ContainsKey("racks") && cfg["racks"] != null)
                {
                    if (cfg["racks"] is ArrayList al)
                    {
                        foreach (var item in al) racks.Add(item);
                    }
                    else if (cfg["racks"] is object[] arr)
                    {
                        foreach (var item in arr) racks.Add(item);
                    }
                }

                var newRack = new Dictionary<string, object>
                {
                    { "id", body.ContainsKey("id") ? body["id"].ToString() : DateTime.Now.Ticks.ToString() },
                    { "type", body.ContainsKey("type") ? body["type"].ToString() : "steel" },
                    { "name", body.ContainsKey("name") ? body["name"].ToString() : "新料架" },
                    { "number", body.ContainsKey("number") ? body["number"].ToString() : (racks.Count + 1).ToString() },
                    { "rows", body.ContainsKey("rows") ? body["rows"].ToString() : "3" },
                    { "cols", body.ContainsKey("cols") ? body["cols"].ToString() : "6" },
                    { "photoSignal", body.ContainsKey("photoSignal") ? body["photoSignal"].ToString() : "" },
                    { "openRequest", body.ContainsKey("openRequest") ? body["openRequest"].ToString() : "" },
                    { "openSignal", body.ContainsKey("openSignal") ? body["openSignal"].ToString() : "" },
                    { "doorClosed", body.ContainsKey("doorClosed") ? body["doorClosed"].ToString() : "" },
                    { "doorFlag", body.ContainsKey("doorFlag") ? body["doorFlag"].ToString() : "" },
                    { "grabStartTaskNo", body.ContainsKey("grabStartTaskNo") ? body["grabStartTaskNo"].ToString() : "" },
                    { "placeStartTaskNo", body.ContainsKey("placeStartTaskNo") ? body["placeStartTaskNo"].ToString() : "" },
                    { "scanStartTaskNo", body.ContainsKey("scanStartTaskNo") ? body["scanStartTaskNo"].ToString() : "" },
                };

                racks.Add(newRack);
                cfg["racks"] = racks;

                string json = AppState.Js.Serialize(cfg);
                System.IO.File.WriteAllText(AppState.ConfigPath, json);

                return AppState.Json(new { success = true, message = "料架已添加" });
            }
            catch (Exception ex)
            {
                return AppState.Json(new { success = false, message = "添加料架失败: " + ex.Message });
            }
        }
    }
}
