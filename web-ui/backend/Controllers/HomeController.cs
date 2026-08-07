using System;
using System.IO;

namespace MachineMonitor
{
    public static class HomeController
    {
        public static bool TryHandle(string path, out string result, out string contentType, out byte[] fileBytes, out int notFoundStatus)
        {
            result = null;
            contentType = "application/json; charset=utf-8";
            fileBytes = null;
            notFoundStatus = 0;

            if (path == "" || path == "/")
            {
                string indexPath = Path.Combine(AppState.ExeDir, "index.html");
                if (File.Exists(indexPath))
                {
                    fileBytes = File.ReadAllBytes(indexPath);
                    contentType = "text/html; charset=utf-8";
                    return true;
                }
                result = AppState.Json(new { success = false, message = "index.html 不存在" });
                return true;
            }

            if (path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".html") || path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".ico"))
            {
                string filePath = Path.Combine(AppState.ExeDir, path.TrimStart('/').Replace("/", "\\"));
                if (File.Exists(filePath))
                {
                    fileBytes = File.ReadAllBytes(filePath);
                    if (path.EndsWith(".js")) contentType = "application/javascript; charset=utf-8";
                    else if (path.EndsWith(".css")) contentType = "text/css; charset=utf-8";
                    else if (path.EndsWith(".html")) contentType = "text/html; charset=utf-8";
                    else if (path.EndsWith(".png")) contentType = "image/png";
                    else if (path.EndsWith(".jpg") || path.EndsWith(".jpeg")) contentType = "image/jpeg";
                    else if (path.EndsWith(".ico")) contentType = "image/x-icon";
                    return true;
                }
                result = AppState.Json(new { success = false, message = "文件不存在: " + path });
                notFoundStatus = 404;
                return true;
            }

            return false;
        }
    }
}
