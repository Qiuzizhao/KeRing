using System;
using System.Text;
using System.Threading;
using KeRing.App.Audio;
using KeRing.App.Schedule;

namespace KeRing.App
{
    /// <summary>
    /// 无界面自检：KeRing.exe --selftest
    /// 用来在目标机上快速确认"配置能读、课表能解析、音频设备和中文音色在不在"。
    /// </summary>
    internal static class SelfTest
    {
        public static int Run()
        {
            var report = new StringBuilder();
            var failures = 0;

            report.AppendLine("KeRing 自检报告  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("程序目录：" + AppPaths.BaseDirectory);
            report.AppendLine("数据目录：" + AppPaths.DataDirectory);
            report.AppendLine();

            var config = AppConfig.Load();
            report.AppendLine("[1] 配置");
            report.AppendLine("    文件：" + AppPaths.ConfigFile);
            report.AppendLine("    提前提醒：" + config.RemindAheadMinutes + " 分钟");
            report.AppendLine("    播报音量：" + config.AnnounceVolumePercent + "%（播完恢复原值）");
            report.AppendLine("    刷新间隔：" + config.RefreshIntervalMinutes + " 分钟");
            report.AppendLine("    数据源：" + config.ScheduleFilePath);
            report.AppendLine();

            report.AppendLine("[2] 课表加载");
            var source = new LocalFileScheduleSource(config.ScheduleFilePath);
            var load = source.Load();
            report.AppendLine("    结果：" + (load.Success ? "成功" : "失败") + " - " + load.Message);
            if (!load.Success) { failures++; }
            report.AppendLine();

            if (load.Success)
            {
                var weekday = ReminderPlanner.ToWeekday(DateTime.Now.DayOfWeek);
                report.AppendLine("[3] 今天（" + ReminderPlanner.WeekdayName(weekday) + "）的打铃点");
                var today = ReminderPlanner.BuildForDay(load.Schedule, DateTime.Now, config.RemindAheadMinutes);
                if (today.Count == 0)
                {
                    report.AppendLine("    （今天没有课）");
                }

                foreach (var point in today)
                {
                    report.AppendLine("    " + point.Describe());
                }
                report.AppendLine();

                DateTime fireTime;
                var next = ReminderPlanner.FindNext(load.Schedule, DateTime.Now, config.RemindAheadMinutes, out fireTime);
                report.AppendLine("[4] 下一个提醒点");
                report.AppendLine("    " + (next == null
                    ? "找不到"
                    : next.Describe() + "，还有 " + FormatSpan(fireTime - DateTime.Now)));
                report.AppendLine();
            }

            report.AppendLine("[5] 音频设备");
            var device = AudioController.DescribeDefaultDevice();
            report.AppendLine("    " + device);
            if (device.StartsWith("无可用", StringComparison.Ordinal)) { failures++; }
            report.AppendLine();

            report.AppendLine("[6] 中文语音");
            var voices = Announcer.CountChineseVoices();
            report.AppendLine("    可用中文音色：" + voices + " 个");
            if (voices == 0)
            {
                report.AppendLine("    !! 没有中文音色，播报会没声音，需要在目标机装中文语音包");
                failures++;
            }
            report.AppendLine();

            report.AppendLine("[7] 提示音");
            report.AppendLine("    来源：" + BellTone.DescribeSource());
            try
            {
                using (var bell = BellTone.OpenEmbedded())
                {
                    report.AppendLine("    可读取，大小 " + bell.Length + " 字节");
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("    !! 打不开：" + ex.Message);
                failures++;
            }
            report.AppendLine();

            report.AppendLine("[8] 开机自启");
            report.AppendLine("    当前状态：" + (AutoStart.IsEnabled() ? "已开启" : "未开启"));
            report.AppendLine();

            report.AppendLine(failures == 0 ? "自检通过。" : "自检发现 " + failures + " 个问题。");

            Console.WriteLine(report.ToString());
            Logger.Info("自检完成，问题数：" + failures);
            return failures;
        }

        private static string FormatSpan(TimeSpan span)
        {
            if (span < TimeSpan.Zero) { span = TimeSpan.Zero; }
            if (span.TotalHours >= 1) { return string.Format("{0} 小时 {1} 分", (int)span.TotalHours, span.Minutes); }
            if (span.TotalMinutes >= 1) { return string.Format("{0} 分 {1} 秒", (int)span.TotalMinutes, span.Seconds); }
            return string.Format("{0} 秒", (int)span.TotalSeconds);
        }

        /// <summary>
        /// 只播报一次，用于在目标机上确认"真的能出声"：KeRing.exe --say 语文
        /// 会打印播报前后的系统音量，确认临时提升后已恢复原值。
        /// </summary>
        public static int Say(string course)
        {
            var config = AppConfig.Load();
            Console.WriteLine("播报前：" + AudioController.DescribeDefaultDevice());
            Console.WriteLine("中文音色：" + Announcer.CountChineseVoices() + " 个");

            var result = new Announcer().Announce(course, config);
            Console.WriteLine(result.Message);

            Thread.Sleep(500); // 等系统音量写回
            Console.WriteLine("播报后：" + AudioController.DescribeDefaultDevice());

            return result.Ok ? 0 : 1;
        }
    }
}
