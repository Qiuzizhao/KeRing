using System;
using System.Collections.Generic;
using System.Globalization;

namespace KeRing.App.Schedule
{
    /// <summary>作息时间表里的一节。</summary>
    internal sealed class Period
    {
        public int Index { get; set; }
        public string Start { get; set; }
        public string End { get; set; }

        public TimeSpan StartTime { get { return ParseTime(Start); } }
        public TimeSpan EndTime { get { return ParseTime(End); } }

        public static TimeSpan ParseTime(string text)
        {
            TimeSpan value;
            if (!string.IsNullOrWhiteSpace(text) &&
                TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return TimeSpan.Zero;
        }
    }

    /// <summary>一门课落在星期几的第几节。</summary>
    internal sealed class CourseEntry
    {
        /// <summary>1 = 周一 … 7 = 周日</summary>
        public int Weekday { get; set; }

        /// <summary>对应 Period.Index</summary>
        public int Period { get; set; }

        public string Course { get; set; }
    }

    internal sealed class WeekSchedule
    {
        public DateTime? GeneratedAt { get; set; }
        public List<Period> Periods { get; set; } = new List<Period>();
        public List<CourseEntry> Entries { get; set; } = new List<CourseEntry>();

        public Period FindPeriod(int index)
        {
            foreach (var period in Periods)
            {
                if (period.Index == index) { return period; }
            }

            return null;
        }

        public CourseEntry FindCourse(int weekday, int periodIndex)
        {
            foreach (var entry in Entries)
            {
                if (entry.Weekday == weekday && entry.Period == periodIndex) { return entry; }
            }

            return null;
        }

        public List<CourseEntry> EntriesOfDay(int weekday)
        {
            var result = new List<CourseEntry>();
            foreach (var entry in Entries)
            {
                if (entry.Weekday == weekday) { result.Add(entry); }
            }

            return result;
        }
    }
}
