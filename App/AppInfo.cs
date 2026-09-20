using System;
using System.Reflection;

namespace KeRing.App
{
    /// <summary>
    /// 版本号：**唯一来源是 csproj 里的 `&lt;AppVersion&gt;`**（它会同时决定 exe 文件名、程序集版本、
    /// 文件属性里看到的版本）。界面和自检都从这里读，别在代码里写死版本号。
    ///
    /// 使用方 2026-09-20 要求：**从 exe 文件名到窗口顶部栏都要能看见版本号**，当前定为 1.5.0。
    /// </summary>
    internal static class AppInfo
    {
        /// <summary>读不到就退回这个（正常情况下永远读得到）。</summary>
        private const string FallbackVersion = "1.5.0";

        private static string _version;

        /// <summary>形如 "1.5.0"（末尾多余的 .0 会去掉：程序集版本是 1.5.0.0）。</summary>
        public static string Version
        {
            get
            {
                if (_version != null) { return _version; }

                try
                {
                    var assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

                    // SDK 会把 csproj 的 <InformationalVersion> 原样写进程序集，优先用它（"1.5.0"）
                    var informational = assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    var text = informational == null ? null : informational.InformationalVersion;

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        var version = assembly.GetName().Version;
                        text = version == null ? FallbackVersion : version.ToString();
                    }

                    // 有些构建会在后面缀上 "+<commit>"，截掉
                    var plus = text.IndexOf('+');
                    if (plus > 0) { text = text.Substring(0, plus); }

                    // "1.5.0.0" → "1.5.0"（只去末尾的 .0，别把 "1.5.0" 变成 "1.5"）
                    while (text.EndsWith(".0", StringComparison.Ordinal) &&
                           text.Split('.').Length > 3)
                    {
                        text = text.Substring(0, text.Length - 2);
                    }

                    _version = string.IsNullOrWhiteSpace(text) ? FallbackVersion : text.Trim();
                }
                catch (Exception)
                {
                    _version = FallbackVersion;
                }

                return _version;
            }
        }

        /// <summary>窗口标题 / 托盘提示用："智能课表打铃 v1.5.0"。</summary>
        public static string Title(string product)
        {
            return product + " v" + Version;
        }
    }
}
