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
    /// 用哪一套**由班级决定**（见 SchemeForClass）：一二年级用低年级表，三到六年级用高年级表。
    /// 时间取自学校给的《作息时间表（2026 年秋季学期）》，
    /// **只取第 1~6 节正课**：体育活动、眼保健操、午间管理、课后服务都不算。
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

        /// <summary>
        /// 由班级名推出该用哪套作息。学校的班级名形如"一(1)""三(4)"，
        /// 一二年级是低年级，三到六年级是高年级。
        /// **认不出来返回 null**，由调用方保持原样——宁可不动，也别瞎猜。
        /// </summary>
        public static string SchemeForClass(string className)
        {
            if (string.IsNullOrWhiteSpace(className)) { return null; }

            var name = className.Trim();
            switch (name[0])
            {
                case '一':
                case '二':
                    return Junior;
                case '三':
                case '四':
                case '五':
                case '六':
                    return Senior;
            }

            // 兼容写成阿拉伯数字的（"1年级"），不过学校现在用的是中文数字
            if (name[0] >= '1' && name[0] <= '6')
            {
                return name[0] <= '2' ? Junior : Senior;
            }

            return null;
        }

        public static List<Period> Create(string scheme)
        {
            return string.Equals(Normalize(scheme), Junior, StringComparison.Ordinal)
                ? Build(JuniorTimes)
                : Build(SeniorTimes);
        }

        // 高年级（三、四、五、六年级）：每节 40 分钟
        private static readonly string[][] SeniorTimes =
        {
            new[] { "08:50", "09:30" },
            new[] { "09:40", "10:20" },
            new[] { "10:35", "11:15" },
            new[] { "11:25", "12:05" },
            new[] { "14:20", "14:55" },
            new[] { "15:05", "15:40" },
        };

        // 低年级（一、二年级）：每节 35 分钟
        private static readonly string[][] JuniorTimes =
        {
            new[] { "08:50", "09:25" },
            new[] { "09:40", "10:15" },
            new[] { "10:35", "11:10" },
            new[] { "11:25", "12:00" },
            new[] { "14:20", "14:55" },
            new[] { "15:05", "15:40" },
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
