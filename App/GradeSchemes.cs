using System;
using System.Collections.Generic;
using KeRing.App.Schedule;

namespace KeRing.App
{
    /// <summary>
    /// 作息时间方案。低年级和高年级的上课时间不同（每节时长、上下课时刻都不一样），
    /// 但节数相同——都是 6 节。
    ///
    /// 关键约定：**具体时刻以这份表为准**，课表数据只负责"第几节上什么课"。
    /// 因为数据源不知道这台机器是低年级还是高年级。
    ///
    /// ⚠ 下面的时间是占位值，等使用方给出实际作息后替换即可，程序逻辑不用动。
    /// </summary>
    internal static class GradeSchemes
    {
        public const string Senior = "高年级";
        public const string Junior = "低年级";

        /// <summary>认不出来的值一律当高年级（默认方案）。</summary>
        public static string Normalize(string value)
        {
            return string.Equals(value, Junior, StringComparison.Ordinal) ? Junior : Senior;
        }

        public static List<Period> Create(string scheme)
        {
            return string.Equals(Normalize(scheme), Junior, StringComparison.Ordinal)
                ? Build(JuniorTimes)
                : Build(SeniorTimes);
        }

        // 高年级：每节 45 分钟
        private static readonly string[][] SeniorTimes =
        {
            new[] { "08:00", "08:45" },
            new[] { "08:55", "09:40" },
            new[] { "10:00", "10:45" },
            new[] { "10:55", "11:40" },
            new[] { "14:00", "14:45" },
            new[] { "14:55", "15:40" },
        };

        // 低年级：每节 40 分钟（占位时间，待使用方提供后替换）
        private static readonly string[][] JuniorTimes =
        {
            new[] { "08:10", "08:50" },
            new[] { "09:00", "09:40" },
            new[] { "10:00", "10:40" },
            new[] { "10:50", "11:30" },
            new[] { "14:10", "14:50" },
            new[] { "15:00", "15:40" },
        };

        private static List<Period> Build(string[][] times)
        {
            var periods = new List<Period>();
            for (var i = 0; i < times.Length; i++)
            {
                periods.Add(new Period
                {
                    Index = i + 1,
                    Start = times[i][0],
                    End = times[i][1],
                });
            }

            return periods;
        }
    }
}
