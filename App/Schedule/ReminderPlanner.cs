using System;
using System.Collections.Generic;

namespace KeRing.App.Schedule
{
    /// <summary>一个打铃点：几点几分、提醒哪门课。</summary>
    internal sealed class ReminderPoint
    {
        public DateTime Time;
        public int Weekday;
        public int PeriodIndex;
        public string Course;

        /// <summary>
        /// 这个点是"提前几分钟"那一档打出来的（0 = 正点）。
        /// 一节课设了几档就有几个点，**日志/自检里靠它区分**（否则同一节课出现三行，看不懂谁是谁）。
        /// </summary>
        public int AheadMinutes;

        public bool Fired;

        public string Describe()
        {
            return string.Format(
                "{0:MM-dd HH:mm} 提前{1}分 第{2}节 {3}",
                Time,
                AheadMinutes,
                PeriodIndex,
                Course);
        }
    }

    internal static class ReminderPlanner
    {
        /// <summary>
        /// 生成某一天的打铃点：每节课开始时间往前推 N 分钟——**N 取遍所有档位**
        /// （设了 [7,5]，这节课就会有 7 分钟前、5 分钟前两个点）。
        /// </summary>
        public static List<ReminderPoint> BuildForDay(WeekSchedule schedule, DateTime day, IList<int> aheadMinutesList)
        {
            var points = new List<ReminderPoint>();
            if (schedule == null) { return points; }

            var ahead = NormalizeAhead(aheadMinutesList);
            if (ahead.Count == 0) { return points; }   // 全关了 = 这天不打铃

            var weekday = ToWeekday(day.DayOfWeek);
            foreach (var entry in schedule.EntriesOfDay(weekday))
            {
                if (string.IsNullOrWhiteSpace(entry.Course)) { continue; } // 空堂不提醒

                var period = schedule.FindPeriod(entry.Period);
                if (period == null) { continue; }

                foreach (var minutes in ahead)
                {
                    points.Add(new ReminderPoint
                    {
                        Time = day.Date + period.StartTime - TimeSpan.FromMinutes(minutes),
                        Weekday = weekday,
                        PeriodIndex = entry.Period,
                        Course = entry.Course,
                        AheadMinutes = minutes,
                    });
                }
            }

            points.Sort((a, b) => a.Time.CompareTo(b.Time));
            return points;
        }

        /// <summary>找出下一个还没到的打铃点，今天没有就往后找最多 7 天。</summary>
        public static ReminderPoint FindNext(WeekSchedule schedule, DateTime now, IList<int> aheadMinutesList, out DateTime fireTime)
        {
            fireTime = default(DateTime);
            if (schedule == null) { return null; }

            for (var offset = 0; offset < 8; offset++)
            {
                var day = now.Date.AddDays(offset);
                foreach (var point in BuildForDay(schedule, day, aheadMinutesList))
                {
                    if (point.Time > now)
                    {
                        fireTime = point.Time;
                        return point;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 档位列表的兜底整理（配置加载时已经理过一遍，这里再挡一次传给我们的脏数据）：
        /// 去重、**大的在前**、越界的丢掉。
        /// </summary>
        private static List<int> NormalizeAhead(IList<int> source)
        {
            var result = new List<int>();
            if (source == null) { return result; }

            foreach (var value in source)
            {
                if (value < 0 || value > 60) { continue; }
                if (result.Contains(value)) { continue; }
                result.Add(value);
            }

            result.Sort((a, b) => b.CompareTo(a));
            return result;
        }

        public static int ToWeekday(DayOfWeek day)
        {
            return day == DayOfWeek.Sunday ? 7 : (int)day;
        }

        public static string WeekdayName(int weekday)
        {
            switch (weekday)
            {
                case 1: return "周一";
                case 2: return "周二";
                case 3: return "周三";
                case 4: return "周四";
                case 5: return "周五";
                case 6: return "周六";
                case 7: return "周日";
                default: return "?";
            }
        }
    }
}
