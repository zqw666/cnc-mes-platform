using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Web.Script.Serialization;

namespace DataCommonOperate
{
    /// <summary>
    /// 统一配置管理客户端
    /// 优先从后端 API 获取配置，若后端不可达则回退到本地文件读取
    /// </summary>
    public static class ConfigClient
    {
        private static readonly JavaScriptSerializer Js = new JavaScriptSerializer();

        /// <summary>
        /// 从后端 API 获取配置，若后端不可达则回退到本地文件读取
        /// </summary>
        /// <param name="apiBase">后端 API 基础地址，如 "http://localhost:8888"</param>
        /// <param name="fallbackConfigPath">回退到本地文件时的 config.json 路径（可选）</param>
        /// <returns>配置字典，失败时返回空字典</returns>
        public static Dictionary<string, object> FetchConfig(string apiBase, string fallbackConfigPath = null)
        {
            // 先尝试从后端 API 获取
            try
            {
                string url = apiBase.TrimEnd('/') + "/api/config/raw";
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 3000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    var result = Js.Deserialize<Dictionary<string, object>>(json);
                    if (result != null && result.ContainsKey("success"))
                    {
                        if (Convert.ToBoolean(result["success"]) && result.ContainsKey("data"))
                        {
                            var data = result["data"];
                            if (data is Dictionary<string, object> dict)
                                return dict;
                        }
                    }
                }
            }
            catch
            {
                // 后端不可达，静默回退到文件
            }

            // 回退：从文件读取
            if (!string.IsNullOrEmpty(fallbackConfigPath) && File.Exists(fallbackConfigPath))
            {
                return Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(fallbackConfigPath));
            }

            // 尝试从当前目录向上查找
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "config.json");
                if (File.Exists(candidate))
                    return Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(candidate));
                dir = dir.Parent;
            }

            return new Dictionary<string, object>();
        }

        /// <summary>
        /// 安全获取嵌套字典
        /// </summary>
        public static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return new Dictionary<string, object>();
            if (d[key] is Dictionary<string, object> nested) return nested;
            try { return Js.Deserialize<Dictionary<string, object>>(d[key].ToString()) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        /// <summary>
        /// 安全获取字符串值
        /// </summary>
        public static string GetStr(Dictionary<string, object> d, string key, string def = "")
        {
            return d != null && d.ContainsKey(key) && d[key] != null ? d[key].ToString() : def;
        }

        /// <summary>
        /// 安全获取整数值
        /// </summary>
        public static int GetInt(Dictionary<string, object> d, string key, int def = 0)
        {
            return d != null && d.ContainsKey(key) && d[key] != null ? Convert.ToInt32(d[key]) : def;
        }
    }
}
