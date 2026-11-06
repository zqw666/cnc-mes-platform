using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
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
                var scannerSrc = AppState.GetDict(cfg, "scanner");

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
                        password = AppState.GetStr(m, "password", ""),
                        hostKey = AppState.GetStr(m, "hostKey", ""),
                        grabStartTaskNo = AppState.GetStr(m, "grabStartTaskNo", ""),
                        placeStartTaskNo = AppState.GetStr(m, "placeStartTaskNo", ""),
                        palletWorkNum = AppState.GetStr(m, "palletWorkNum", "1"),
                        workCoords = AppState.GetStr(m, "workCoords", ""),
                        startProcessSignal = AppState.GetStr(m, "startProcessSignal", "")
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
                            number = AppState.GetStr(rd, "number", "1"),
                            rows = AppState.GetStr(rd, "rows", "3"),
                            cols = AppState.GetStr(rd, "cols", "6"),
                            photoStartSignal = AppState.GetStr(rd, "photoStartSignal", ""),
                            photoResetSignal = AppState.GetStr(rd, "photoResetSignal", ""),
                            openRequest = AppState.GetStr(rd, "openRequest", ""),
                            openSignal = AppState.GetStr(rd, "openSignal", ""),
                            doorClosed = AppState.GetStr(rd, "doorClosed", ""),
                            doorFlag = AppState.GetStr(rd, "doorFlag", ""),
                            grabStartTaskNo = AppState.GetStr(rd, "grabStartTaskNo", ""),
                            placeStartTaskNo = AppState.GetStr(rd, "placeStartTaskNo", ""),
                            scanStartTaskNo = AppState.GetStr(rd, "scanStartTaskNo", "")
                        });
                }

                var gripperList = new List<object>();
                var grippers = AppState.GetJsonArray(cfg, "grippers");
                foreach (var g in grippers)
                {
                    if (g is Dictionary<string, object> gd)
                    {
                        var rackIds = new List<string>();
                        foreach (var rid in AppState.GetJsonArray(gd, "rackIds"))
                        {
                            if (rid != null) rackIds.Add(rid.ToString());
                        }
                        gripperList.Add(new
                        {
                            id = AppState.GetStr(gd, "id"),
                            type = AppState.GetStr(gd, "type", "steel"),
                            name = AppState.GetStr(gd, "name", ""),
                            slotPhotoSignal = AppState.GetStr(gd, "slotPhotoSignal", ""),
                            grabTaskNo = AppState.GetStr(gd, "grabTaskNo", ""),
                            placeTaskNo = AppState.GetStr(gd, "placeTaskNo", ""),
                            rackIds = rackIds
                        });
                    }
                }

                var workflowSteps = GetWorkflowStepsForConfig();

                var lineSrc = AppState.GetDict(cfg, "line");
                return AppState.Json(new
                {
                    success = true,
                    data = new
                    {
                        line = new
                        {
                            // 不能给默认值：配置里没写夹爪类型时必须暴露为「未配置」，
                            // 否则页面回填成 fixed-single、用户一保存就把钢件夹爪固化进配置。
                            gripperType = AppState.GetStr(lineSrc, "gripperType", ""),
                            grabMode = AppState.GetStr(lineSrc, "grabMode", "A-grab-B-place"),
                            palletEnabled = AppState.GetStr(lineSrc, "palletEnabled", ""),
                            palletSlots = AppState.GetStr(lineSrc, "palletSlots", "4"),
                            shuttleExists = AppState.GetStr(lineSrc, "shuttleExists", ""),
                            shuttleSlots = AppState.GetStr(lineSrc, "shuttleSlots", ""),
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
                                    scanTaskNo = AppState.GetStr(presetSrc, "scanTaskNo", ""),
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
                        scanner = new
                        {
                            type = AppState.GetStr(scannerSrc, "type", "rcd"),
                            rfidCom = AppState.GetStr(scannerSrc, "rfidCom", "COM3"),
                            rcdIp = AppState.GetStr(scannerSrc, "rcdIp", "192.168.101.71"),
                            rcdPort = AppState.GetStr(scannerSrc, "rcdPort", "5000"),
                            heartbeatInterval = AppState.GetStr(scannerSrc, "heartbeatInterval", "60"),
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
                {
                    var lineObj = AppState.GetDict(body, "line");
                    // 剥离 racks/machines/grippers，避免与顶层配置重复
                    lineObj.Remove("racks");
                    lineObj.Remove("machines");
                    lineObj.Remove("grippers");
                    cfg["line"] = lineObj;
                }
                if (body.ContainsKey("racks"))
                    cfg["racks"] = body["racks"];
                if (body.ContainsKey("grippers"))
                    cfg["grippers"] = body["grippers"];
                if (body.ContainsKey("scanner"))
                    cfg["scanner"] = body["scanner"];

                if (body.ContainsKey("machines"))
                {
                    var machines = new Dictionary<string, object>();
                    var rawMachines = body["machines"];
                    // JavaScriptSerializer 反序列化 JSON 数组可能得到 ArrayList 或 object[]，两者都兼容
                    var machineItems = new List<object>();
                    if (rawMachines is ArrayList al)
                    {
                        foreach (var item in al) machineItems.Add(item);
                    }
                    else if (rawMachines is object[] arr)
                    {
                        foreach (var item in arr) machineItems.Add(item);
                    }
                    foreach (var item in machineItems)
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
                                { "hostKey", AppState.GetStr(m, "hostKey", "") },
                                { "grabStartTaskNo", AppState.GetStr(m, "grabStartTaskNo", "") },
                                { "placeStartTaskNo", AppState.GetStr(m, "placeStartTaskNo", "") },
                                // 铝板电极上下机专用任务号，漏了这两个会导致 load_aluminum / unload_aluminum 用错任务号
                                { "aluminumGrabTaskNo", AppState.GetStr(m, "aluminumGrabTaskNo", "") },
                                { "aluminumPlaceTaskNo", AppState.GetStr(m, "aluminumPlaceTaskNo", "") },
                                { "palletWorkNum", AppState.GetStr(m, "palletWorkNum", "1") },
                                { "workCoords", AppState.GetStr(m, "workCoords", "") },
                                { "startProcessSignal", AppState.GetStr(m, "startProcessSignal", "") },
                            };
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
                        // 收集有效步骤（name 非空）
                        var validSteps = new List<Dictionary<string, object>>();
                        foreach (var step in steps)
                        {
                            if (step is Dictionary<string, object> s)
                            {
                                var name = AppState.GetStr(s, "name", "");
                                if (!string.IsNullOrEmpty(name))
                                    validSteps.Add(s);
                            }
                        }
                        // 清空旧工作流，重新按顺序插入（实现"保存为新工作流"）
                        db.ExecuteSql("DELETE FROM work_flow");
                        int priority = validSteps.Count;
                        foreach (var s in validSteps)
                        {
                            var name = AppState.GetStr(s, "name", "");
                            var enabled = s.ContainsKey("enabled") && Convert.ToBoolean(s["enabled"]);
                            string sql = "INSERT INTO work_flow (processname, status, priority) VALUES ("
                                + SqlStr(name) + ", " + (enabled ? 1 : 0) + ", " + priority-- + ")";
                            db.ExecuteSql(sql);
                        }
                        LogHelper.Info("[Config] 工作流已保存，共 " + validSteps.Count + " 个步骤");
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

        /// <summary>
        /// 原样保存整份 config.json（系统参数页用）。
        ///
        /// 与 SaveConfig 的根本区别：不做白名单重拼，因此不会丢字段。
        /// SaveConfig 里 machines 只保留 15 个键、顶层只认 7 个键，
        /// 会把 aluminumGrabTaskNo / aluminumPlaceTaskNo / eman / nc_server / serverIp / log 全部丢掉。
        ///
        /// 请求体：{ "json": "<格式化好的完整 config>", "syncSub": true }
        /// </summary>
        public static string SaveConfigRaw(string raw)
        {
            try
            {
                var body = AppState.Js.Deserialize<Dictionary<string, object>>(raw);
                if (body == null)
                    return AppState.Json(new { success = false, message = "请求体格式错误" });

                string json = AppState.GetStr(body, "json", "");
                if (string.IsNullOrWhiteSpace(json))
                    return AppState.Json(new { success = false, message = "缺少 json 字段" });

                Dictionary<string, object> check;
                try { check = AppState.Js.Deserialize<Dictionary<string, object>>(json); }
                catch (Exception pex)
                {
                    return AppState.Json(new { success = false, message = "配置不是合法 JSON：" + pex.Message });
                }
                if (check == null)
                    return AppState.Json(new { success = false, message = "配置不是合法 JSON 对象" });
                if (!check.ContainsKey("plc"))
                    return AppState.Json(new { success = false, message = "缺少 plc 节点，已拒绝保存（防止写空配置）" });
                if (!check.ContainsKey("database"))
                    return AppState.Json(new { success = false, message = "缺少 database 节点，已拒绝保存" });

                if (File.Exists(AppState.ConfigPath))
                {
                    try { File.Copy(AppState.ConfigPath, AppState.ConfigPath + ".bak", true); }
                    catch (Exception bex) { LogHelper.Warn("[Config] 备份失败: " + bex.Message); }
                }

                File.WriteAllText(AppState.ConfigPath, json, new UTF8Encoding(false));

                // 同步 subs/config.json —— 两份不一致时，子程序一旦拿不到后端配置
                // 就会回退到 subs 那份，行为会变（曾经导致夹爪类型读成钢件）。
                bool syncedSub = false;
                if (body.ContainsKey("syncSub") && body["syncSub"] != null && Convert.ToBoolean(body["syncSub"]))
                {
                    try
                    {
                        string sub = Path.Combine(AppState.SubExeDir, "config.json");
                        if (File.Exists(sub)) File.Copy(sub, sub + ".bak", true);
                        File.WriteAllText(sub, json, new UTF8Encoding(false));
                        syncedSub = true;
                    }
                    catch (Exception sex)
                    {
                        LogHelper.Warn("[Config] 同步 subs/config.json 失败: " + sex.Message);
                    }
                }

                LogHelper.Info("[Config] 配置已原样保存（" + json.Length + " 字符）" +
                               (syncedSub ? "，已同步 subs/config.json" : ""));

                return AppState.Json(new
                {
                    success = true,
                    syncedSub = syncedSub,
                    message = syncedSub ? "已保存，并同步到 subs/config.json" : "已保存"
                });
            }
            catch (Exception ex)
            {
                LogHelper.Error("[Config] 原样保存失败: " + ex.Message);
                return AppState.Json(new { success = false, message = "保存失败: " + ex.Message });
            }
        }

        static List<object> GetWorkflowStepsForConfig()
        {
            var steps = new List<object>();
            try
            {
                var db = DatabaseRepository.CreateDb();
                var dt = db.ExeForDtl("SELECT id, processname, status, priority FROM work_flow ORDER BY priority DESC, id ASC");
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

                // work_flow 为空：返回默认工作流（按默认顺序）
                int priority = AppState.DefaultWorkflowOrder.Count;
                int seqId = 0;
                foreach (var name in AppState.DefaultWorkflowOrder)
                {
                    steps.Add(new
                    {
                        id = seqId++,
                        name = name,
                        enabled = true,
                        priority = priority--
                    });
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
