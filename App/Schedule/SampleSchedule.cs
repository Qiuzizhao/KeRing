using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace KeRing.App.Schedule
{
    /// <summary>
    /// 演示数据：一份普通的中学周课表。正式接入接口后这个类只在首次运行时兜底用。
    /// </summary>
    internal static class SampleSchedule
    {
        // 行 = 第 1..6 节，列 = 周一..周五（空字符串表示空堂）
        private static readonly string[][] Grid =
        {
            new[] { "语文", "数学", "英语", "语文", "数学" },
            new[] { "数学", "语文", "数学", "英语", "语文" },
            new[] { "英语", "英语", "语文", "数学", "物理" },
            new[] { "物理", "化学", "化学", "物理", "化学" },
            new[] { "语文", "数学", "英语", "语文", "体育" },
            new[] { "数学", "英语", "语文", "历史", "音乐" },
        };

        private static readonly string[][] PeriodTimes =
        {
            new[] { "08:00", "08:45" },
            new[] { "08:55", "09:40" },
            new[] { "10:00", "10:45" },
            new[] { "10:55", "11:40" },
            new[] { "14:00", "14:45" },
            new[] { "14:55", "15:40" },
        };

        public static void WriteTo(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, BuildJson(), new UTF8Encoding(false));
        }

        public static string BuildJson()
        {
            var periods = new List<Period>();
            for (var i = 0; i < PeriodTimes.Length; i++)
            {
                periods.Add(new Period
                {
                    Index = i + 1,
                    Start = PeriodTimes[i][0],
                    End = PeriodTimes[i][1],
                });
            }

            var entries = new List<CourseEntry>();
            for (var period = 0; period < Grid.Length; period++)
            {
                for (var day = 0; day < Grid[period].Length; day++)
                {
                    var course = Grid[period][day];
                    if (string.IsNullOrWhiteSpace(course)) { continue; }

                    entries.Add(new CourseEntry
                    {
                        Weekday = day + 1,
                        Period = period + 1,
                        Course = course,
                    });
                }
            }

            var dto = new ScheduleFileDto
            {
                GeneratedAt = DateTime.Now,
                Periods = periods,
                Entries = entries,
            };

            return JsonConvert.SerializeObject(dto, Formatting.Indented);
        }
    }
}
