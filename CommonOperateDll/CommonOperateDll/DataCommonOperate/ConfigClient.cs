using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Web.Script.Serialization;

namespace DataCommonOperate
{
    /// <summary>
    /// 配置来源。子程序据此判断「我拿到的配置到底可不可信」。
    /// </summary>
    public enum ConfigSource
    {
        /// <summary>所有来源均失败，FetchConfig 返回空字典</summary>
        None = 0,

        /// <summary>后端 API /api/config/raw —— 唯一权威来源</summary>
        Api = 1,

        /// <summary>调用方显式指定的回退文件</summary>
        FallbackFile = 2,

        /// <summary>从 exe 目录向上查找命中的 config.json —— 最容易读到别的产线的路径</summary>
        UpwardFile = 3
    }

    /// <summary>
    /// 统一配置管理客户端
    /// 优先从后端 API 获取配置，若后端不可达则回退到本地文件读取
    ///
    /// 【重要】回退到本地文件时，拿到的可能是过期配置或其它产线的配置。
    /// 每次 FetchConfig 都会记录来源，调用方应把 ConfigClient.LastSourceText 打进日志，
    /// 并在关键字段（如 line.gripperType）缺失时直接拒绝运行，而不是用默认值兜底。
    /// </summary>
    public static class ConfigClient
    {
        private static readonly JavaScriptSerializer Js = new JavaScriptSerializer();

        // ==================== 配置来源追踪 ====================

        static ConfigSource _lastSource = ConfigSource.None;
        static string _lastSourcePath = "";
        static string _lastError = "";

        /// <summary>本次配置的来源</summary>
        public static ConfigSource LastSource { get { return _lastSource; } }

        /// <summary>来源明细（API 地址或文件路径）</summary>
        public static string LastSourcePath { get { return _lastSourcePath; } }

        /// <summary>取配置过程中发生的错误（后端连不上时的主要原因）</summary>
        public static string LastError { get { return _lastError; } }

        /// <summary>配置是否来自后端 API（最可信）</summary>
        public static bool IsFromApi { get { return _lastSource == ConfigSource.Api; } }

        /// <summary>配置是否来自本地文件回退（可能过期 / 可能不是当前产线）</summary>
        public static bool IsFallback { get { return _lastSource == ConfigSource.FallbackFile || _lastSource == ConfigSource.UpwardFile; } }

        /// <summary>人类可读的来源描述，用于日志</summary>
        public static string LastSourceText
        {
            get
            {
                switch (_lastSource)
                {
                    case ConfigSource.Api:
                        return "后端 API [" + _lastSourcePath + "]";
                    case ConfigSource.FallbackFile:
                        return "本地回退文件 [" + _lastSourcePath + "]（可能已过期）";
                    case ConfigSource.UpwardFile:
                        return "向上查找到 [" + _lastSourcePath + "]（可能不是当前产线）";
                    default:
                        return "无（所有来源均失败" + (string.IsNullOrEmpty(_lastError) ? "" : "，" + _lastError) + "）";
                }
            }
        }

        static void ResetSource()
        {
            _lastSource = ConfigSource.None;
            _lastSourcePath = "";
            _lastError = "";
        }

        /// <summary>
        /// 从后端 API 获取配置，若后端不可达则回退到本地文件读取
        /// </summary>
        /// <param name="apiBase">后端 API 基础地址，如 "http://localhost:8888"</param>
        /// <param name="fallbackConfigPath">回退到本地文件时的 config.json 路径（可选）</param>
        /// <returns>配置字典，失败时返回空字典。来源见 LastSource</returns>
        public static Dictionary<string, object> FetchConfig(string apiBase, string fallbackConfigPath = null)
        {
            ResetSource();

            // ---------- 第 1 层：后端 API（权威来源） ----------
            if (!string.IsNullOrEmpty(apiBase))
            {
                string url = apiBase.TrimEnd('/') + "/api/config/raw";
                try
                {
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
                                {
                                    _lastSource = ConfigSource.Api;
                                    _lastSourcePath = url;
                                    return dict;
                                }
                            }
                            else
                            {
                                _lastError = "后端返回 success=false";
                            }
                        }
                        else
                        {
                            _lastError = "后端返回内容不是预期格式";
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 后端不可达 / 超时 —— 记录原因，不再静默吞掉
                    _lastError = "后端不可达: " + ex.Message;
                }
            }
            else
            {
                _lastError = "未指定 apiBase";
            }

            // ---------- 第 2 层：本地文件回退（可能过期） ----------
            try
            {
                if (!string.IsNullOrEmpty(fallbackConfigPath) && File.Exists(fallbackConfigPath))
                {
                    var dict = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(fallbackConfigPath));
                    if (dict != null)
                    {
                        _lastSource = ConfigSource.FallbackFile;
                        _lastSourcePath = fallbackConfigPath;
                        WarnFallback();
                        return dict;
                    }
                }

                // 尝试从当前目录向上查找
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "config.json");
                    if (File.Exists(candidate))
                    {
                        var dict = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(candidate));
                        if (dict != null)
                        {
                            _lastSource = ConfigSource.UpwardFile;
                            _lastSourcePath = candidate;
                            WarnFallback();
                            return dict;
                        }
                    }
                    dir = dir.Parent;
                }
            }
            catch (Exception ex)
            {
                // 文件读取或 JSON 反序列化失败
                _lastError = (_lastError + "；本地配置读取失败: " + ex.Message).TrimStart('；', ' ');
            }

            return new Dictionary<string, object>();
        }

        /// <summary>
        /// 回退到本地文件时强制留痕。
        /// 这一条日志是排查「用了错的夹爪 / 错的产线配置」时唯一的线索，不能省。
        /// </summary>
        static void WarnFallback()
        {
            try
            {
                LogHelper.Warn("配置未从后端取得（" + (string.IsNullOrEmpty(_lastError) ? "原因未知" : _lastError)
                    + "），已回退到 " + LastSourceText
                    + "。注意：该文件可能已过期或属于其它产线，夹爪类型/任务号/机床列表未必正确。");
            }
            catch
            {
                // 日志本身失败不能影响取配置
            }
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
