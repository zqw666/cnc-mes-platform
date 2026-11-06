using DataCommonOperate;

namespace MachineMonitor
{
    public static class ConfigController
    {
        public static string GetConfigRaw() { return ConfigService.GetConfigRaw(); }
        public static string GetConfig() { return ConfigService.GetConfig(); }
        public static string SaveConfig(string raw) { return ConfigService.SaveConfig(raw); }

        /// <summary>系统参数页：原样保存整份 config.json，不做白名单重拼，不丢字段</summary>
        public static string SaveConfigRaw(string raw) { return ConfigService.SaveConfigRaw(raw); }
        public static string Debug(string raw)
        {
            LogHelper.Debug("[DEBUG] /api/config/debug 收到数据: " + (raw.Length > 500 ? raw.Substring(0, 500) + "..." : raw));
            return AppState.Json(new { success = true, received = raw });
        }
    }
}
