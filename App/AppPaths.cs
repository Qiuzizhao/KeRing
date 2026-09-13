using System;
using System.IO;
using KeRing.App;

namespace KeRing.App
{
    /// <summary>
    /// 路径约定。
    ///
    /// 程序本身是**单个 exe**（依赖和铃声都内嵌），所以运行时产生的文件不能再往 exe 旁边丢，
    /// 统一放在数据目录里：
    ///   - 首选 %ProgramData%\KeRing —— 整机一份，位置好找，远程维护时方便
    ///   - 没有写权限就退回 %LocalAppData%\KeRing —— 当前用户目录，一定有写权限
    /// 两种情况都会写进日志，自检里也会打印实际用的目录。
    /// </summary>
    internal static class AppPaths
    {
        private const string FolderName = "KeRing";
        private static string _dataDirectory;

        /// <summary>exe 所在目录（只读使用，不往里写东西）。</summary>
        public static string BaseDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string DataDirectory
        {
            get
            {
                if (_dataDirectory == null) { _dataDirectory = ResolveDataDirectory(); }
                return _dataDirectory;
            }
        }

        public static string ConfigFile { get { return Path.Combine(DataDirectory, "kering.config.json"); } }
        public static string LogDirectory { get { return Path.Combine(DataDirectory, "logs"); } }
        public static string AudioDirectory { get { return Path.Combine(DataDirectory, "audio"); } }

        /// <summary>现场替换铃声的位置：数据目录下的 audio\ding.wav。没有就用内嵌的那份。</summary>
        public static string BellFile { get { return Path.Combine(AudioDirectory, "ding.wav"); } }

        /// <summary>相对路径按数据目录解析（课表文件也放数据目录，跟 exe 分开）。</summary>
        public static string Resolve(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName)) { return DataDirectory; }
            return Path.IsPathRooted(pathOrName) ? pathOrName : Path.Combine(DataDirectory, pathOrName);
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(AudioDirectory);
        }

        private static string ResolveDataDirectory()
        {
            var candidates = new[]
            {
                Path.Combine(SafeFolder(Environment.SpecialFolder.CommonApplicationData), FolderName),
                Path.Combine(SafeFolder(Environment.SpecialFolder.LocalApplicationData), FolderName),
            };

            foreach (var candidate in candidates)
            {
                if (IsWritable(candidate)) { return candidate; }
            }

            // 两个都不行（极少见），退回 exe 同目录下的 data 子目录
            return Path.Combine(BaseDirectory, "data");
        }

        private static string SafeFolder(Environment.SpecialFolder folder)
        {
            var path = Environment.GetFolderPath(folder);
            return string.IsNullOrEmpty(path) ? BaseDirectory : path;
        }

        private static bool IsWritable(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, ".write-test.tmp");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
