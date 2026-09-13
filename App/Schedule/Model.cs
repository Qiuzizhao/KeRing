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

    /// <summary>
    /// 一个课表单位（一个班级）。数据源给的是全校课表，程序从中挑一个来用。
    /// </summary>
    internal sealed class SchoolClass
    {
        /// <summary>唯一标识，程序用它记住"这台机器是哪一班"。改名不影响，换了 id 才算换班。</summary>
        public string Id { get; set; }

        /// <summary>界面上显示的名字，例如"一(1)班"。</summary>
        public string Name { get; set; }

        public List<CourseEntry> Entries { get; set; } = new List<CourseEntry>();

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name)) { return Name.Trim(); }
                return string.IsNullOrWhiteSpace(Id) ? "未命名班级" : Id.Trim();
            }
        }
    }

    /// <summary>整所学校的课表：数据源一次返回全校，程序从中挑出当前班级的内容。</summary>
    internal sealed class SchoolSchedule
    {
        public DateTime? GeneratedAt { get; set; }

        /// <summary>节次（时刻会被作息方案表覆盖，见 GradeSchemes）。</summary>
        public List<Period> Periods { get; set; } = new List<Period>();

        public List<SchoolClass> Classes { get; set; } = new List<SchoolClass>();

        public SchoolClass FindClass(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) { return null; }

            foreach (var item in Classes)
            {
                if (string.Equals(item.Id, id, StringComparison.Ordinal)) { return item; }
            }

            return null;
        }
    }
}
