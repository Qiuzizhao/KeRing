using System;
using System.Drawing;
using KeRing.App.Schedule;

namespace KeRing.UI
{
    /// <summary>指向课表里的"某一格"：星期几 + 第几节。</summary>
    internal sealed class LessonRef
    {
        public int Weekday;   // 1 = 周一 … 7 = 周日；0 = 没有
        public int Period;    // 0 = 没有

        public bool IsValid { get { return Weekday >= 1 && Weekday <= 7 && Period > 0; } }

        public static readonly LessonRef None = new LessonRef();
    }

    /// <summary>
    /// "这一格显示成什么样"的共用规则：**主窗口的周课表**和**悬浮窗的全周预览**都用它。
    ///
    /// 抽出来的唯一目的是防止两边各写一套、慢慢长歪——比如主窗口把调课标成蓝字、
    /// 预览忘了标，使用者立刻就会发现"两个地方不一致"，那是最难查的一类问题。
    /// （个人版 KeRing_SOLO 里也有同名的一份，两边结构一样，只是那边多了"班级/教师/值班"几项。）
    /// </summary>
    internal static class ScheduleRender
    {
        /// <summary>调课调过来的格子：蓝字。</summary>
        public static readonly Color AdjustedColor = Color.FromArgb(21, 101, 192);

        /// <summary>正在上的课：红底白字。</summary>
        public static readonly Color CurrentBack = Color.FromArgb(198, 40, 40);

        /// <summary>下一个提醒点：绿底白字。</summary>
        public static readonly Color NextBack = Color.FromArgb(46, 139, 87);

        /// <summary>上下午之间的分隔色带（深蓝）。</summary>
        public static readonly Color SplitBand = Color.FromArgb(36, 69, 127);

        /// <summary>班级版的格子里就是课程名（班级版整张表就是一个班，不用写班名）。</summary>
        public static string CellText(CourseEntry entry)
        {
            return entry == null ? string.Empty : (entry.Course ?? string.Empty);
        }

        /// <summary>这一格的文字颜色；null 表示用默认黑色。</summary>
        public static Color? TextColor(CourseEntry entry)
        {
            if (entry == null) { return null; }
            return entry.Adjusted ? AdjustedColor : (Color?)null;
        }

        /// <summary>周六周日有没有课——决定要不要显示那两列（都没有就一起藏起来）。</summary>
        public static bool HasWeekendCourses(WeekSchedule view)
        {
            if (view == null) { return true; }   // 拿不到课表时不擅自藏列

            foreach (var entry in view.Entries)
            {
                if (entry.Weekday >= 6 && !string.IsNullOrWhiteSpace(entry.Course)) { return true; }
            }

            return false;
        }

        /// <summary>
        /// 现在**正在上的**那一节。主窗口标红和悬浮窗标红都走它，规则必须一致
        /// （下课时刻以作息方案表为准；班级版一套作息，不存在按班切换的问题）。
        /// </summary>
        public static LessonRef CurrentLesson(WeekSchedule view, DateTime now)
        {
            if (view == null) { return LessonRef.None; }

            var weekday = ReminderPlanner.ToWeekday(now.DayOfWeek);
            var timeOfDay = now.TimeOfDay;

            foreach (var period in view.Periods)
            {
                if (period.EndTime <= period.StartTime) { continue; }
                if (timeOfDay < period.StartTime) { break; }   // 作息表按节次升序，后面更晚
                if (timeOfDay >= period.EndTime) { continue; } // 这节已经下课了，看下一节

                var entry = view.FindCourse(weekday, period.Index);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Course))
                {
                    return new LessonRef { Weekday = weekday, Period = period.Index };
                }

                break;
            }

            return LessonRef.None;
        }
    }
}
