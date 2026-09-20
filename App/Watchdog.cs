using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace KeRing.App
{
    /// <summary>
    /// 看门狗：程序被"外面"干掉时（杀软、清理软件、任务管理器、崩溃）自动把它拉回来。
    ///
    /// **交付物还是一个 exe**：看门狗不是额外的脚本，而是**程序自己的一个命令行开关**——
    ///   · 程序启动时由它自己登记一个计划任务：每 5 分钟跑一次 `KeRing.exe --watchdog`；
    ///   · `--watchdog` 跑起来不显示界面，只做一件事：主程序在不在？在就悄悄退出，不在就拉起来并记日志。
    ///
    /// **手动退出照样管用**：从托盘菜单"退出"时先写一个标记文件（watchdog.pause），
    /// 看门狗看到这个标记就**不拉**；下次程序正常启动（双击 / 开机自启 / 看门狗拉起）会把它删掉。
    ///
    /// 详见 docs/开发交接.md 的"看门狗"一节（含为什么不用外部脚本、为什么不用第二个进程）。
    /// </summary>
    internal static class Watchdog
    {
        /// <summary>计划任务名（在"任务计划程序"里看到的就是它）。</summary>
        public const string TaskName = "KeRing 看门狗";

        /// <summary>多久检查一次（分钟）。</summary>
        private const int IntervalMinutes = 5;

        /// <summary>标记文件：存在 = 使用者主动退出了，别把我们拉回来。</summary>
        private const string PauseFileName = "watchdog.pause";

        /// <summary>最近一次 schtasks 失败的原因（登记失败时写进配置，自检里能看到）。</summary>
        private static string _lastSchtasksError;

        /// <summary>当前 exe 的完整路径（计划任务要登记的、重启时要拉起的都是它）。</summary>
        public static string ExePath
        {
            get
            {
                try
                {
                    return Process.GetCurrentProcess().MainModule.FileName;
                }
                catch (Exception)
                {
                    return Assembly.GetEntryAssembly().Location;
                }
            }
        }

        private static string PauseFile
        {
            get { return Path.Combine(AppPaths.DataDirectory, PauseFileName); }
        }

        /// <summary>是不是"使用者主动退出、让我们别管"的状态。</summary>
        public static bool IsPaused
        {
            get
            {
                try { return File.Exists(PauseFile); }
                catch (Exception) { return false; }
            }
        }

        /// <summary>程序正常启动时调（不管是谁把它拉起来的）：清掉"暂停"标记，恢复守护。</summary>
        public static void ClearPause()
        {
            try
            {
                if (!File.Exists(PauseFile)) { return; }
                File.Delete(PauseFile);
                Logger.Info("看门狗：已恢复守护（清掉上次「手动退出」留下的标记）");
            }
            catch (Exception ex)
            {
                Logger.Warn("看门狗：清理暂停标记失败：" + ex.Message);
            }
        }

        /// <summary>使用者主动退出时调：写标记，让看门狗别把我们拉回来。</summary>
        public static void Pause(string reason)
        {
            try
            {
                File.WriteAllText(PauseFile, string.Format(
                    "{0:yyyy-MM-dd HH:mm:ss} 手动退出：{1}{2}",
                    AppClock.Now,
                    reason ?? string.Empty,
                    Environment.NewLine));
                Logger.Info("看门狗：已暂停（" + (reason ?? string.Empty) + "）——这次不会再自动拉起");
            }
            catch (Exception ex)
            {
                Logger.Warn("看门狗：写暂停标记失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 按配置登记 / 更新 / 注销计划任务。**放在后台线程调**（要起 schtasks.exe，别卡住界面）。
        /// 幂等：已经登记好且就是现在这个 exe 时什么都不做；程序换目录了会自动改成新路径。
        /// </summary>
        public static void Apply(AppConfig config)
        {
            if (config == null) { return; }

            try
            {
                var exe = ExePath;

                if (!config.WatchdogEnabled)
                {
                    if (TaskExists())
                    {
                        if (DeleteTask()) { Logger.Info("看门狗：已关闭（计划任务已删除）"); }
                    }

                    if (!string.IsNullOrEmpty(config.WatchdogTaskPath))
                    {
                        config.WatchdogTaskPath = string.Empty;
                        config.Save();
                    }

                    return;
                }

                // 已经登记过、而且登记的就是现在这个 exe → 不用动
                if (string.Equals(config.WatchdogTaskPath, exe, StringComparison.OrdinalIgnoreCase) && TaskExists())
                {
                    return;
                }

                if (CreateTask(exe))
                {
                    config.WatchdogTaskPath = exe;
                    config.WatchdogLastError = string.Empty;
                    config.Save();
                    Logger.Info(string.Format(
                        "看门狗：已登记计划任务（每 {0} 分钟检查一次，任务名「{1}」）：{2}",
                        IntervalMinutes,
                        TaskName,
                        exe));
                }
                else
                {
                    config.WatchdogTaskPath = string.Empty;
                    config.WatchdogLastError = _lastSchtasksError ?? "schtasks 执行失败";
                    config.Save();
                    Logger.Warn("看门狗：登记失败（可能是系统策略不允许建计划任务）——这台机器被强杀后不会自动拉起");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("看门狗：登记/注销出错", ex);
            }
        }

        /// <summary>
        /// `KeRing.exe --watchdog` 的入口：主程序在运行就悄悄退出，不在就把它拉起来。
        /// 计划任务每 5 分钟调它一次；跑的是 WinExe，所以不会闪黑窗。
        /// </summary>
        public static int Run()
        {
            try
            {
                if (IsPaused) { return 0; }         // 使用者主动退出的，别拉
                if (IsMainRunning()) { return 0; }  // 好好跑着呢，什么都不做

                var exe = ExePath;
                Logger.Warn("看门狗：发现程序不在运行，正在重新启动：" + exe);

                var start = new ProcessStartInfo(exe)
                {
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                };
                Process.Start(start);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("看门狗：重新启动失败", ex);
                return 1;
            }
        }

        /// <summary>主程序（不是我自己）在不在运行。</summary>
        private static bool IsMainRunning()
        {
            var self = Process.GetCurrentProcess().Id;
            var name = Path.GetFileNameWithoutExtension(ExePath);

            Process[] all = null;
            try
            {
                all = Process.GetProcessesByName(name);
                foreach (var process in all)
                {
                    try
                    {
                        if (process.Id != self) { return true; }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("看门狗：枚举进程失败：" + ex.Message);
            }

            return false;
        }

        // ---------- 计划任务（都用 schtasks.exe，当前用户级、不需要管理员）----------

        private static bool TaskExists()
        {
            return RunSchtasks(string.Format("/query /tn \"{0}\"", TaskName)) == 0;
        }

        private static bool CreateTask(string exe)
        {
            // /tr 的值里要带引号（路径可能含空格）再跟参数，所以这里得嵌一层转义
            var arguments = string.Format(
                "/create /tn \"{0}\" /tr \"\\\"{1}\\\" --watchdog\" /sc minute /mo {2} /f",
                TaskName,
                exe,
                IntervalMinutes);

            return RunSchtasks(arguments) == 0;
        }

        private static bool DeleteTask()
        {
            return RunSchtasks(string.Format("/delete /tn \"{0}\" /f", TaskName)) == 0;
        }

        private static int RunSchtasks(string arguments)
        {
            var start = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var process = Process.Start(start))
            {
                if (process == null) { return -1; }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(15000))
                {
                    Logger.Warn("看门狗：schtasks 超时（" + arguments + "）");
                    try { process.Kill(); } catch (Exception) { }
                    return -1;
                }

                if (process.ExitCode != 0)
                {
                    var detail = ((error ?? string.Empty) + " " + (output ?? string.Empty)).Trim();
                    if (detail.Length > 200) { detail = detail.Substring(0, 200); }
                    _lastSchtasksError = string.Format("schtasks 返回 {0}：{1}", process.ExitCode, detail);

                    Logger.Warn(string.Format(
                        "看门狗：schtasks 返回 {0}（{1}）{2} {3}",
                        process.ExitCode,
                        arguments,
                        output == null ? string.Empty : output.Trim(),
                        error == null ? string.Empty : error.Trim()));
                }
                else
                {
                    _lastSchtasksError = null;
                }

                return process.ExitCode;
            }
        }
    }
}
