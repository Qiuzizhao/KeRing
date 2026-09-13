using System;
using System.Globalization;
using System.Text;
using System.Threading;
using KeRing.App.Audio;
using KeRing.App.Schedule;

namespace KeRing.App
{
    /// <summary>
    /// 无界面自检：KeRing.exe --selftest
    /// 用来在目标机上快速确认"配置能读、课表能解析、班级选没选、音频设备和中文音色在不在"。
    /// 返回码 = 问题数，0 表示全过。
    /// </summary>
    internal static class SelfTest
    {
        public static int Run()
        {
            var report = new StringBuilder();
            var failures = 0;

            report.AppendLine("KeRing 自检报告  " + AppClock.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("程序目录：" + AppPaths.BaseDirectory);
            report.AppendLine("数据目录：" + AppPaths.DataDirectory);
            report.AppendLine();

            var config = AppConfig.Load();
            var source = ScheduleSourceFactory.Create(config);

            // 先把时间问准：下面算出来的打铃点、下一个提醒点全靠"现在几点"，
            // 本机钟不准的话自检报告本身就是错的。查不到 NTP 也不要紧，会自动退到下一级。
            var timeZoneOk = AppClock.CheckTimeZone();
            AppClock.SyncNtp(config.NtpServers, config.NtpTimeoutSeconds);

            report.AppendLine("[1] 配置");
            report.AppendLine("    文件：" + AppPaths.ConfigFile);
            report.AppendLine("    班级：" + (string.IsNullOrWhiteSpace(config.SelectedClassId)
                ? "（尚未选择，首次启动会弹选择框）"
                : config.SelectedClassId));
            report.AppendLine("    作息方案：" + config.GradeScheme);
            report.AppendLine("    提前提醒：" + config.RemindAheadMinutes + " 分钟");
            report.AppendLine("    播报音量：" + config.AnnounceVolumePercent + "%（播完恢复原值）");
            report.AppendLine("    刷新间隔：" + config.RefreshIntervalMinutes + " 分钟");
            report.AppendLine("    数据源：" + source.Description);
            report.AppendLine();

            report.AppendLine("[2] 课表加载");
            var load = source.Load();
            report.AppendLine("    结果：" + (load.Success ? "成功" : "失败") + " - " + load.Message);
            if (!load.Success) { failures++; }
            report.AppendLine();

            if (load.Success)
            {
                var school = load.Schedule;
                var target = school.FindClass(config.SelectedClassId) ?? school.Classes[0];

                report.AppendLine("[3] 班级");
                report.AppendLine("    数据源共 " + school.Classes.Count + " 个班：");
                foreach (var item in school.Classes)
                {
                    report.AppendLine("      · " + item.DisplayName + "（" + item.Entries.Count + " 条课程）");
                }
                report.AppendLine("    本机使用：" + target.DisplayName);

                // 和主界面用同一套推导：作息方案由班级决定，不是配置里那个值
                var scheme = GradeSchemes.SchemeForClass(target.DisplayName) ?? config.GradeScheme;
                report.AppendLine("    作息方案：" + scheme + "（按班级自动；一二年级低年级、三到六年级高年级）");
                report.AppendLine();

                // 和主界面一样：课表内容取选定班级，时刻取作息方案
                var view = new WeekSchedule
                {
                    GeneratedAt = school.GeneratedAt,
                    Periods = GradeSchemes.Create(scheme),
                    Entries = target.Entries,
                };

                var weekday = ReminderPlanner.ToWeekday(AppClock.Now.DayOfWeek);
                report.AppendLine("[4] 今天（" + ReminderPlanner.WeekdayName(weekday) + "）的打铃点");
                var today = ReminderPlanner.BuildForDay(view, AppClock.Now, config.RemindAheadMinutes);
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
                var next = ReminderPlanner.FindNext(view, AppClock.Now, config.RemindAheadMinutes, out fireTime);
                report.AppendLine("[5] 下一个提醒点");
                report.AppendLine("    " + (next == null
                    ? "找不到"
                    : next.Describe() + "，还有 " + FormatSpan(fireTime - AppClock.Now)));
                report.AppendLine();
            }

            report.AppendLine("[6] 音频设备");
            var device = AudioController.DescribeDefaultDevice();
            report.AppendLine("    " + device);
            if (device.StartsWith("无可用", StringComparison.Ordinal)) { failures++; }
            report.AppendLine();

            report.AppendLine("[7] 中文语音");
            var voices = Announcer.CountChineseVoices();
            report.AppendLine("    可用中文音色：" + voices + " 个");
            if (voices == 0)
            {
                report.AppendLine("    !! 没有中文音色，播报会没声音，需要在目标机装中文语音包");
                failures++;
            }
            report.AppendLine();

            report.AppendLine("[8] 提示音");
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

            report.AppendLine("[9] 开机自启");
            report.AppendLine("    当前状态：" + (AutoStart.IsEnabled() ? "已开启" : "未开启"));
            report.AppendLine();

            report.AppendLine("[10] 时间");
            report.AppendLine("    本机时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                              "；时区 " + AppClock.TimeZoneDescription +
                              (timeZoneOk ? "" : "  ← 不是北京时间，建议改成 UTC+08:00"));
            var ntpSkew = AppClock.NtpSkew;
            report.AppendLine("    NTP：" + (ntpSkew.HasValue
                ? AppClock.NtpServer + "，本机时间比标准时间" +
                  (ntpSkew.Value < TimeSpan.Zero ? "快 " : "慢 ") +
                  Math.Abs(ntpSkew.Value.TotalSeconds).ToString("0.00", CultureInfo.InvariantCulture) + " 秒"
                : "全部不可达（配置：" + config.NtpServers + "）"));
            var serverSkew = AppClock.ServerSkew;
            report.AppendLine("    教务接口：" + (!serverSkew.HasValue
                ? "没取到"
                : "本机时间比服务器" +
                  (serverSkew.Value < TimeSpan.Zero ? "快 " : "慢 ") +
                  Math.Abs(serverSkew.Value.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture) +
                  " 秒（服务器自身不准，只在偏差超过 1 分钟时才采用）"));
            report.AppendLine("    采用：" + AppClock.Source + "，修正 " +
                              AppClock.Offset.TotalSeconds.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " 秒");
            report.AppendLine("    打铃用的时间：" + AppClock.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (!timeZoneOk)
            {
                report.AppendLine("    （时区不影响打铃——程序一律按北京时间算——但建议把系统时区也改对）");
            }

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
