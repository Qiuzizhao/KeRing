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
        public bool Fired;

        public string Describe()
        {
            return string.Format(
                "{0:MM-dd HH:mm} 第{1}节 {2}",
                Time,
                PeriodIndex,
                Course);
        }
    }

    internal static class ReminderPlanner
    {
        /// <summary>生成某一天的打铃点：每节课开始时间往前推 N 分钟。</summary>
        public static List<ReminderPoint> BuildForDay(WeekSchedule schedule, DateTime day, int aheadMinutes)
        {
            var points = new List<ReminderPoint>();
            if (schedule == null) { return points; }

            var weekday = ToWeekday(day.DayOfWeek);
            foreach (var entry in schedule.EntriesOfDay(weekday))
            {
                if (string.IsNullOrWhiteSpace(entry.Course)) { continue; } // 空堂不提醒

                var period = schedule.FindPeriod(entry.Period);
                if (period == null) { continue; }

                points.Add(new ReminderPoint
                {
                    Time = day.Date + period.StartTime - TimeSpan.FromMinutes(aheadMinutes),
                    Weekday = weekday,
                    PeriodIndex = entry.Period,
                    Course = entry.Course,
                });
            }

            points.Sort((a, b) => a.Time.CompareTo(b.Time));
            return points;
        }

        /// <summary>找出下一个还没到的打铃点，今天没有就往后找最多 7 天。</summary>
        public static ReminderPoint FindNext(WeekSchedule schedule, DateTime now, int aheadMinutes, out DateTime fireTime)
        {
            fireTime = default(DateTime);
            if (schedule == null) { return null; }

            for (var offset = 0; offset < 8; offset++)
            {
                var day = now.Date.AddDays(offset);
                foreach (var point in BuildForDay(schedule, day, aheadMinutes))
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
