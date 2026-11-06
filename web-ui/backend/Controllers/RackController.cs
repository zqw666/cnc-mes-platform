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
            var dt2 = db.ExeForDtl("SELECT id,racktype,rfid,posstatus,workstate,scanstatus,workpiecename,materialname FROM rack_administrate");
            if (dt2 != null)
            {
                foreach (DataRow dr in dt2.Rows)
                    rackAdmin.Add(new
                    {
                        id = AppState.SafeInt(dr["id"]),
                        racktype = AppState.SafeStr(dr["racktype"]),
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
                version = AppState.RackDataVersion,
                data = new { rack_administrate = rackAdmin, knife_rack = knifeRack, rackUpdateCount }
            });
        }

        /// <summary>
        /// 子程序更新料位后通知：递增料架数据版本号，前端据此刷新页面图标
        /// </summary>
        public static string NotifyRackChanged(string raw)
        {
            AppState.NotifyRackDataChanged();
            return AppState.Json(new { success = true, message = "料架数据已更新", version = AppState.RackDataVersion });
        }

        public static string UpdateRackStatus(string raw)
        {
            try
            {
                var body = AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                if (body == null)
                    return AppState.Json(new { success = false, message = "无效的 JSON 数据" });

                string type = body.ContainsKey("type") ? body["type"].ToString() : "";
                string status = body.ContainsKey("status") ? body["status"].ToString() : "";

                // 解析选中的料位编号列表
                var ids = new List<int>();
                if (body.ContainsKey("ids") && body["ids"] != null)
                {
                    object idsRaw = body["ids"];
                    if (idsRaw is ArrayList al)
                    {
                        foreach (var item in al) ids.Add(Convert.ToInt32(item));
                    }
                    else if (idsRaw is object[] arr)
                    {
                        foreach (var item in arr) ids.Add(Convert.ToInt32(item));
                    }
                    else
                    {
                        ids.Add(Convert.ToInt32(idsRaw));
                    }
                }

                if (ids.Count == 0)
                    return AppState.Json(new { success = false, message = "未选中料位" });

                // 前端分页 type -> racktype 映射（1=电极 2=钢件 3=刀具 4=托盘 5=铝板电极）
                int racktype = 0;
                switch (type)
                {
                    case "electrode": racktype = 1; break;
                    case "steel": racktype = 2; break;
                    case "tool": racktype = 3; break;
                    case "pallet": racktype = 4; break;
                    case "aluminum": racktype = 5; break;
                    default: racktype = 0; break;
                }

                string idList = string.Join(",", ids);

                var db = DatabaseRepository.CreateDb();
                string sql = "";
                if (status == "disable")
                    // 禁用：料位状态=0
                    sql = "UPDATE rack_administrate SET posstatus=0 WHERE racktype=" + racktype + " AND id IN (" + idList + ")";
                else if (status == "no-material")
                    // 无料：料位状态=1
                    sql = "UPDATE rack_administrate SET posstatus=1 WHERE racktype=" + racktype + " AND id IN (" + idList + ")";
                else if (status == "to-scan")
                    sql = "UPDATE rack_administrate SET scanstatus=0 WHERE racktype=" + racktype + " AND id IN (" + idList + ")";
                else if (status == "scanned")
                    sql = "UPDATE rack_administrate SET scanstatus=1 WHERE racktype=" + racktype + " AND id IN (" + idList + ")";
                else if (status == "occupied")
                    sql = "UPDATE rack_administrate SET workstate=1 WHERE racktype=" + racktype + " AND id IN (" + idList + ")";
                else if (status == "empty")
                    sql = "UPDATE rack_administrate SET workstate=0,workpiecename=NULL WHERE racktype=" + racktype + " AND id IN (" + idList + ")";

                if (!string.IsNullOrEmpty(sql))
                    db.ExecuteSql(sql);

                AppState.NotifyRackDataChanged();
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
                    { "photoStartSignal", body.ContainsKey("photoStartSignal") ? body["photoStartSignal"].ToString() : "" },
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
