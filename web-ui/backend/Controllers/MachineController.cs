using System;
using System.Collections.Generic;
using System.IO;

namespace MachineMonitor
{
    public static class MachineController
    {
        public static string ControlMachine(string action, string raw)
        {
            int id = 1;
            try
            {
                if (!string.IsNullOrEmpty(raw))
                {
                    var dict = AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                    if (dict != null && dict.ContainsKey("id"))
                        id = Convert.ToInt32(dict["id"]);
                }
            }
            catch { }

            var db = DatabaseRepository.CreateDb();
            if (action == "start")
                db.ExecuteSql("UPDATE macmessage SET workstate=1 WHERE id=" + id);
            else if (action == "pause")
                db.ExecuteSql("UPDATE macmessage SET workstate=0 WHERE id=" + id);
            else if (action == "stop")
                db.ExecuteSql("UPDATE macmessage SET workstate=0,workpiecename=NULL,workpieceworkstate=NULL,processing_duration=0 WHERE id=" + id);

            string msg = action == "start" ? "已发送启动命令" : action == "pause" ? "已发送暂停命令" : "已发送停止命令";
            return AppState.Json(new { success = true, message = msg, action });
        }

        public static string CreateMachine(string raw)
        {
            try
            {
                var body = AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                if (body == null)
                    return AppState.Json(new { success = false, message = "无效的 JSON 数据" });

                string macIp = body.ContainsKey("ip") ? body["ip"].ToString() : "";
                string brand = body.ContainsKey("brand") ? body["brand"].ToString().ToLower() : "roders";
                string ncpath = body.ContainsKey("ncpath") ? body["ncpath"].ToString() : "";
                int slots = body.ContainsKey("slots") ? Convert.ToInt32(body["slots"]) : 4;
                string opcUsername = body.ContainsKey("username") ? body["username"].ToString() : "";
                string opcPassword = body.ContainsKey("password") ? body["password"].ToString() : "";

                var cfg = AppState.Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(AppState.ConfigPath));
                if (cfg == null)
                    return AppState.Json(new { success = false, message = "读取配置文件失败" });

                if (!cfg.ContainsKey("machines") || cfg["machines"] == null)
                    cfg["machines"] = new Dictionary<string, object>();

                var machines = AppState.GetDict(cfg, "machines");
                long maxId = 0;
                foreach (var kv in machines)
                {
                    if (long.TryParse(kv.Key, out long mid) && mid > maxId)
                        maxId = mid;
                }
                string macId = (maxId + 1).ToString();

                machines[macId] = new Dictionary<string, object>
                {
                    { "type", brand },
                    { "ip", macIp },
                    { "ncPath", ncpath },
                    { "slots", slots },
                    { "username", opcUsername },
                    { "password", opcPassword }
                };

                cfg["machines"] = machines;
                string json = AppState.Js.Serialize(cfg);
                File.WriteAllText(AppState.ConfigPath, json);

                return AppState.Json(new { success = true, message = "机床已添加", id = macId });
            }
            catch (Exception ex)
            {
                return AppState.Json(new { success = false, message = "添加机床失败: " + ex.Message });
            }
        }
    }
}
