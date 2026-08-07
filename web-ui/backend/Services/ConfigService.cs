using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Web.Script.Serialization;
using DataCommonOperate;

namespace MachineMonitor
{
    public static class ConfigService
    {
        public static string GetConfigRaw()
        {
            try
            {
                if (!File.Exists(AppState.ConfigPath))
                    return AppState.Json(new { success = false, message = "配置文件不存在" });
                var cfg = AppState.Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(AppState.ConfigPath));
                if (cfg == null)
                    return AppState.Json(new { success = false, message = "配置文件格式错误" });
                return AppState.Json(new { success = true, data = cfg });
            }
            catch (Exception ex)
            {
                return AppState.Json(new { success = false, message = "读取配置失败: " + ex.Message });
            }
        }

        public static string GetConfig()
        {
            try
            {
                if (!File.Exists(AppState.ConfigPath))
                    return AppState.Json(new { success = false, message = "配置文件不存在" });

                var cfg = AppState.Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(AppState.ConfigPath));
                if (cfg == null)
                    return AppState.Json(new { success = false, message = "配置文件格式错误" });

                var db = AppState.GetDict(cfg, "database");
                var plc = AppState.GetDict(cfg, "plc");
                var signalsSrc = AppState.GetDict(plc, "signals");
                var addrFallback = AppState.GetDict(plc, "addresses");
                var hasSignals = signalsSrc.Count > 0;
                var robotSrc = hasSignals ? AppState.GetDict(signalsSrc, "robot") : new Dictionary<string, object>();
                var palletSrc = hasSignals ? AppState.GetDict(signalsSrc, "pallet") : new Dictionary<string, object>();
                var presetSrc = hasSignals ? AppState.GetDict(signalsSrc, "preset") : new Dictionary<string, object>();
                var cleanerSrc = hasSignals ? AppState.GetDict(signalsSrc, "cleaner") : new Dictionary<string, object>();
                var dm = AppState.GetDict(plc, "dm");
                var machines = AppState.GetDict(cfg, "machines");

                var machineList = new List<object>();
                foreach (var kv in machines)
                {
                    var m = AppState.GetDict(machines, kv.Key);
                    machineList.Add(new
                    {
                        id = kv.Key,
                        type = AppState.GetStr(m, "type", "roders"),
                        number = AppState.GetStr(m, "number", "1"),
                        ip = AppState.GetStr(m, "ip", ""),
                        port = AppState.GetStr(m, "port", ""),
                        stationCount = AppState.GetStr(m, "stationCount", "4"),
                        ncPath = AppState.GetStr(m, "ncPath", ""),
                        sessionUrl = AppState.GetStr(m, "sessionUrl", ""),
                        username = AppState.GetStr(m, "username", ""),
                        password = AppState.GetStr(m, "password", "")
                    });
                }

                var rackList = new List<object>();
                var racks = AppState.GetJsonArray(cfg, "racks");
                foreach (var r in racks)
                {
                    if (r is Dictionary<string, object> rd)
                        rackList.Add(new
                        {
                            id = AppState.GetStr(rd, "id"),
                            type = AppState.GetStr(rd, "type", "steel"),
                            name = AppState.GetStr(rd, "name", ""),
                            rows = AppState.GetStr(rd, "rows", "3"),
                            cols = AppState.GetStr(rd, "cols", "6"),
                            photoSignal = AppState.GetStr(rd, "photoSignal", ""),
                        });
                }

                var gripperList = new List<object>();
                var grippers = AppState.GetJsonArray(cfg, "grippers");
                foreach (var g in grippers)
                {
                    if (g is Dictionary<string, object> gd)
                        gripperList.Add(new
                        {
                            id = AppState.GetStr(gd, "id"),
                            type = AppState.GetStr(gd, "type", "steel"),
                            name = AppState.GetStr(gd, "name", ""),
                            slotPhotoSignal = AppState.GetStr(gd, "slotPhotoSignal", ""),
                            grabTaskNo = AppState.GetStr(gd, "grabTaskNo", ""),
                            placeTaskNo = AppState.GetStr(gd, "placeTaskNo", "")
                        });
                }

                var workflowSteps = GetWorkflowStepsForConfig();

                return AppState.Json(new
                {
                    success = true,
                    data = new
                    {
                        line = new
                        {
                            rackType = AppState.GetStr(cfg, "rackType", "fixed"),
                            rackCount = AppState.GetStr(cfg, "rackCount", "1"),
                            rackLayers = AppState.GetStr(cfg, "rackLayers", "3"),
                            rackSlots = AppState.GetStr(cfg, "rackSlots", "6"),
                            gripperType = AppState.GetStr(AppState.GetDict(cfg, "line"), "gripperType", "fixed-single"),
                            grabMode = AppState.GetStr(AppState.GetDict(cfg, "line"), "grabMode", "A-grab-B-place"),
                            shuttleExists = AppState.GetStr(AppState.GetDict(cfg, "line"), "shuttleExists", ""),
                            shuttleSlots = AppState.GetStr(AppState.GetDict(cfg, "line"), "shuttleSlots", ""),
                            racks = rackList,
                            machines = machineList,
                            grippers = gripperList,
                        },
                        plc = new
                        {
                            ip = AppState.GetStr(plc, "ip", "192.168.1.1"),
                            port = AppState.GetStr(plc, "port", "9600"),
                            signals = new
                            {
                                robot = new
                                {
                                    photo = AppState.GetStr(robotSrc, "photo", ""),
                                    taskStart = AppState.GetStr(robotSrc, "taskStart", ""),
                                    taskDone = AppState.GetStr(robotSrc, "taskDone", ""),
                                    taskNumber = AppState.GetStr(robotSrc, "taskNumber", ""),
                                    error = AppState.GetStr(robotSrc, "error", ""),
                                    safePos = AppState.GetStr(robotSrc, "safePos", ""),
                                    estop = AppState.GetStr(robotSrc, "estop", ""),
                                },
                                pallet = new
                                {
                                    slotPhoto = AppState.GetStr(palletSrc, "slotPhoto", ""),
                                    grabTaskNo = AppState.GetStr(palletSrc, "grabTaskNo", ""),
                                    placeTaskNo = AppState.GetStr(palletSrc, "placeTaskNo", ""),
                                    photoStart = AppState.GetStr(palletSrc, "photoStart", ""),
                                },
                                preset = new
                                {
                                    photo = AppState.GetStr(presetSrc, "photo", ""),
                                    manual = AppState.GetStr(presetSrc, "manual", ""),
                                    openRequest = AppState.GetStr(presetSrc, "openRequest", ""),
                                    openAllow = AppState.GetStr(presetSrc, "openAllow", ""),
                                    grabTaskNo = AppState.GetStr(presetSrc, "grabTaskNo", ""),
                                    placeTaskNo = AppState.GetStr(presetSrc, "placeTaskNo", ""),
                                    doorLock = AppState.GetStr(presetSrc, "doorLock", ""),
                                },
                                rotary = new
                                {
                                    rotate = AppState.GetStr(AppState.GetDict(signalsSrc, "rotary"), "rotate", ""),
                                    zero = AppState.GetStr(AppState.GetDict(signalsSrc, "rotary"), "zero", ""),
                                    grabTaskNo = AppState.GetStr(AppState.GetDict(signalsSrc, "rotary"), "grabTaskNo", ""),
                                    placeTaskNo = AppState.GetStr(AppState.GetDict(signalsSrc, "rotary"), "placeTaskNo", ""),
                                    arrive = AppState.GetStr(AppState.GetDict(signalsSrc, "rotary"), "arrive", ""),
                                },
                                cleaner = new
                                {
                                    photo = AppState.GetStr(cleanerSrc, "photo", ""),
                                    grabTaskNo = AppState.GetStr(cleanerSrc, "grabTaskNo", ""),
                                    placeTaskNo = AppState.GetStr(cleanerSrc, "placeTaskNo", ""),
                                },
                            },
                            dm = new
                            {
                                task_number = AppState.GetStr(dm, "task_number", ""),
                                robot_task = AppState.GetStr(dm, "robot_task", ""),
                                rack_layer_photoelectric = AppState.GetStr(dm, "rack_layer_photoelectric", ""),
                                gripper_type = AppState.GetStr(dm, "gripper_type", ""),
                                fixture_position = AppState.GetStr(dm, "fixture_position", ""),
                                shuttle_part_count = AppState.GetStr(dm, "shuttle_part_count", ""),
                            },
                        },
                        workflow = workflowSteps,
                    }
                });
            }
            catch (Exception ex)
            {
                return AppState.Json(new { success = false, message = "读取配置失败: " + ex.Message });
            }
        }

        public static string SaveConfig(string raw)
        {
            try
            {
                var body = AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                if (body == null)
                    return AppState.Json(new { success = false, message = "无效的 JSON 数据" });

                var cfg = File.Exists(AppState.ConfigPath)
                    ? AppState.Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(AppState.ConfigPath))
                    : new Dictionary<string, object>();

                if (body.ContainsKey("database"))
                    cfg["database"] = body["database"];
                if (body.ContainsKey("plc"))
                    cfg["plc"] = body["plc"];
                if (body.ContainsKey("line"))
                    cfg["line"] = body["line"];
                if (body.ContainsKey("racks"))
                    cfg["racks"] = body["racks"];
                if (body.ContainsKey("grippers"))
                    cfg["grippers"] = body["grippers"];

                if (body.ContainsKey("machines"))
                {
                    var machines = new Dictionary<string, object>();
                    var rawMachines = body["machines"];
                    if (rawMachines is ArrayList al)
                    {
                        foreach (var item in al)
                        {
                            if (item is Dictionary<string, object> m)
                            {
                                string id = AppState.GetStr(m, "id", DateTime.Now.Ticks.ToString());
                                machines[id] = new Dictionary<string, object>
                                {
                                    { "type", AppState.GetStr(m, "type", "roders") },
                                    { "number", AppState.GetStr(m, "number", "1") },
                                    { "ip", AppState.GetStr(m, "ip", "") },
                                    { "port", AppState.GetStr(m, "port", "") },
                                    { "stationCount", AppState.GetStr(m, "stationCount", "4") },
                                    { "ncPath", AppState.GetStr(m, "ncPath", "") },
                                    { "sessionUrl", AppState.GetStr(m, "sessionUrl", "") },
                                    { "username", AppState.GetStr(m, "username", "") },
                                    { "password", AppState.GetStr(m, "password", "") },
                                };
                            }
                        }
                    }
                    cfg["machines"] = machines;
                }

                if (body.ContainsKey("workflow"))
                {
                    var steps = body["workflow"] as ArrayList;
                    if (steps != null)
                    {
                        var db = DatabaseRepository.CreateDb();
                        foreach (var step in steps)
                        {
                            if (step is Dictionary<string, object> s)
                            {
                                var name = AppState.GetStr(s, "name", "");
                                var enabled = s.ContainsKey("enabled") && Convert.ToBoolean(s["enabled"]);
                                var priority = s.ContainsKey("priority") ? Convert.ToInt32(s["priority"]) : 0;
                                string sql = "INSERT INTO work_flow (processname, status, priority) VALUES ("
                                    + SqlStr(name) + ", " + (enabled ? 1 : 0) + ", " + priority + ") "
                                    + "ON DUPLICATE KEY UPDATE status=" + (enabled ? 1 : 0) + ", priority=" + priority;
                                db.ExecuteSql(sql);
                            }
                        }
                    }
                }

                if (SaveConfigJson(cfg))
                    return AppState.Json(new { success = true, message = "配置已保存" });
                else
                    return AppState.Json(new { success = false, message = "保存配置文件失败" });
            }
            catch (Exception ex)
            {
                return AppState.Json(new { success = false, message = "保存配置失败: " + ex.Message });
            }
        }

        static bool SaveConfigJson(Dictionary<string, object> cfg)
        {
            try
            {
                string json = AppState.Js.Serialize(cfg);
                File.WriteAllText(AppState.ConfigPath, json);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error("[Config] 保存配置文件失败: " + ex.Message);
                return false;
            }
        }

        static List<object> GetWorkflowStepsForConfig()
        {
            var steps = new List<object>();
            try
            {
                var db = DatabaseRepository.CreateDb();
                var dt = db.ExeForDtl("SELECT id, processname, status, priority FROM work_flow WHERE status=1 ORDER BY priority DESC, id ASC");
                if (dt != null && dt.Rows.Count > 0)
                {
                    foreach (DataRow dr in dt.Rows)
                    {
                        string name = AppState.SafeStr(dr["processname"]).ToLower();
                        steps.Add(new
                        {
                            id = AppState.SafeInt(dr["id"]),
                            name = name,
                            enabled = AppState.SafeInt(dr["status"]) == 1,
                            priority = AppState.SafeInt(dr["priority"])
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
                        steps.Add(new
                        {
                            id = AppState.SafeInt(dr["id"]),
                            name = name,
                            enabled = AppState.SafeInt(dr["status"]) == 1,
                            priority = 0
                        });
                    }
                }
            }
            catch { }
            return steps;
        }

        static string SqlStr(string val)
        {
            if (val == null) return "NULL";
            return "'" + val.Replace("'", "''") + "'";
        }
    }
}
