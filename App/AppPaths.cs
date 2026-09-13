using System;
using System.IO;

namespace KeRing.App
{
    /// <summary>
    /// 绿色部署：配置、课表缓存、日志、音频都在 exe 同目录，拷走整个文件夹即可。
    /// </summary>
    internal static class AppPaths
    {
        public static string BaseDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string ConfigFile { get { return Path.Combine(BaseDirectory, "kering.config.json"); } }
        public static string LogDirectory { get { return Path.Combine(BaseDirectory, "logs"); } }
        public static string AudioDirectory { get { return Path.Combine(BaseDirectory, "audio"); } }
        public static string BellFile { get { return Path.Combine(AudioDirectory, "ding.wav"); } }

        public static string Resolve(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName)) { return BaseDirectory; }
            return Path.IsPathRooted(pathOrName) ? pathOrName : Path.Combine(BaseDirectory, pathOrName);
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(AudioDirectory);
        }
    }
}
