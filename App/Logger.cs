using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeRing.App
{
    /// <summary>
    /// 极简文本日志：按天一个文件，只保留最近若干天。
    /// 日志写失败不能影响打铃。
    /// </summary>
    internal static class Logger
    {
        private static readonly object Gate = new object();
        private const int RetainDays = 30;

        public static void Info(string message) { Write("INFO ", message); }
        public static void Warn(string message) { Write("WARN ", message); }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", ex == null ? message : message + " | " + ex);
        }

        private static void Write(string level, string message)
        {
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}",
                DateTime.Now,
                level,
                message);

            lock (Gate)
            {
                try
                {
                    AppPaths.EnsureDirectories();
                    File.AppendAllText(CurrentLogFile(), line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // 忽略：日志失败不能拖垮主流程
                }
            }
        }

        private static string CurrentLogFile()
        {
            var name = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";
            return Path.Combine(AppPaths.LogDirectory, name);
        }

        public static void Cleanup()
        {
            try
            {
                if (!Directory.Exists(AppPaths.LogDirectory)) { return; }
                var limit = DateTime.Now.AddDays(-RetainDays);
                foreach (var file in Directory.GetFiles(AppPaths.LogDirectory, "*.log"))
                {
                    if (File.GetLastWriteTime(file) < limit)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Warn("清理旧日志失败：" + ex.Message);
            }
        }
    }
}
