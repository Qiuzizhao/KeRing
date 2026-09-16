using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using KeRing.App.Schedule;

namespace KeRing.UI
{
    /// <summary>
    /// 只读的"全周课表"控件——悬浮窗放大预览用。
    ///
    /// **为什么不用 DataGridView**：预览要的是"紧凑 + 一次画完 + 没有任何交互痕迹"
    /// （不要选中框、不要滚动条）。自己画反倒更短、更可控。
    ///
    /// **文字和颜色规则全部走 `ScheduleRender`**，跟主窗口共用一套——否则两边会慢慢长歪。
    /// </summary>
    internal sealed class WeekGridView : Control
    {
        // 逻辑尺寸（都过 UiScale）
        private const int LabelWidth = 96;
        private const int ColWidth = 96;
        private const int HeaderHeight = 30;
        private const int RowHeight = 38;
        private const int BandHeight = 10;

        private static readonly Color LineColor = Color.FromArgb(222, 226, 230);
        private static readonly Color HeaderBack = Color.FromArgb(244, 246, 248);
        private static readonly Color HeaderText = Color.FromArgb(60, 64, 68);
        private static readonly Color MutedText = Color.FromArgb(110, 116, 122);

        private WeekSchedule _view;
        private ReminderPoint _next;
        private LessonRef _current = LessonRef.None;
        private int _splitAfter;

        public WeekGridView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.White;
            Font = UiFont.Body;
        }

        /// <summary>喂数据。now 用来判断"正在上的是哪一节"。</summary>
        public void SetData(WeekSchedule view, ReminderPoint next, int splitAfterPeriod, DateTime now)
        {
            _view = view;
            _next = next;
            _splitAfter = splitAfterPeriod;
            _current = ScheduleRender.CurrentLesson(view, now);
            Invalidate();
        }

        public int MeasureHeight()
        {
            var rows = BuildRows();
            var height = HeaderHeight + rows.Count * RowHeight;
            if (HasSplitBand()) { height += BandHeight; }

            return Scale(height) + 1;
        }

        public int MeasureWidth()
        {
            return Scale(LabelWidth + VisibleDays() * ColWidth) + 1;
        }

        private int VisibleDays()
        {
            if (_view == null) { return 5; }
            return ScheduleRender.HasWeekendCourses(_view) ? 7 : 5;
        }

        private static int Scale(int value) { return UiScale.S(value); }

        private List<Period> BuildRows()
        {
            var rows = new List<Period>();
            if (_view == null) { return rows; }

            rows.AddRange(_view.Periods);
            rows.Sort((a, b) => a.Index.CompareTo(b.Index));
            return rows;
        }

        private bool HasSplitBand()
        {
            if (_view == null || _splitAfter <= 0) { return false; }

            var rows = BuildRows();
            for (var i = 0; i < rows.Count - 1; i++)
            {
                if (rows[i].Index == _splitAfter) { return true; }
            }

            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);

            if (_view == null)
            {
                TextRenderer.DrawText(g, "（还没有课表）", Font,
                    new Rectangle(0, 0, Width, Height), MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var days = VisibleDays();
            var labelW = Scale(LabelWidth);
            var colW = Scale(ColWidth);
            var headerH = Scale(HeaderHeight);
            var rowH = Scale(RowHeight);
            var bandH = Scale(BandHeight);

            using (var back = new SolidBrush(HeaderBack)) { g.FillRectangle(back, 0, 0, labelW + colW * days, headerH); }
            // 第一列（节次）的文字**居中**：矩形要左右对称，不然"居中"会偏
            TextRenderer.DrawText(g, "节次", Font, new Rectangle(0, 0, labelW, headerH), HeaderText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            for (var day = 1; day <= days; day++)
            {
                var rect = new Rectangle(labelW + colW * (day - 1), 0, colW, headerH);
                TextRenderer.DrawText(g, ReminderPlanner.WeekdayName(day), UiFont.Header, rect, HeaderText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }

            var y = headerH;
            var rows = BuildRows();
            var rowLines = new List<int>();   // 每行下面那条横线画在哪个 y
            for (var i = 0; i < rows.Count; i++)
            {
                DrawRow(g, rows[i], y, labelW, colW, rowH, days);
                y += rowH;
                rowLines.Add(y);

                if (rows[i].Index == _splitAfter && i < rows.Count - 1)
                {
                    using (var band = new SolidBrush(ScheduleRender.SplitBand)) { g.FillRectangle(band, 0, y, labelW + colW * days, bandH); }
                    y += bandH;
                    rowLines.Add(y);
                }
            }

            using (var pen = new Pen(LineColor))
            {
                // 横线：一行一条。**最后一行那条跳过**——它就是控件底边，交给下面的外框画，
                // 否则底边会变成两像素粗的一条。
                // （这一条原来漏了，界面上看着"只有竖线、没有网格"，2026-09-16 使用方反馈后补上。）
                for (var i = 0; i < rowLines.Count - 1; i++)
                {
                    g.DrawLine(pen, 0, rowLines[i], labelW + colW * days, rowLines[i]);
                }

                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                g.DrawLine(pen, 0, headerH, labelW + colW * days, headerH);
                for (var day = 0; day <= days; day++)
                {
                    var x = labelW + colW * day;
                    g.DrawLine(pen, x, 0, x, y);
                }
            }
        }

        private void DrawRow(Graphics g, Period period, int y, int labelW, int colW, int rowH, int days)
        {
            TextRenderer.DrawText(g, "第" + period.Index + "节", Font,
                new Rectangle(Scale(4), y + Scale(3), labelW - Scale(8), Scale(17)), HeaderText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, period.Start, UiFont.Small,
                new Rectangle(Scale(4), y + Scale(21), labelW - Scale(8), Scale(15)), MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);

            for (var day = 1; day <= days; day++)
            {
                var rect = new Rectangle(labelW + colW * (day - 1), y, colW, rowH);
                var entry = _view.FindCourse(day, period.Index);
                if (entry == null || string.IsNullOrWhiteSpace(entry.Course)) { continue; }

                var text = ScheduleRender.CellText(entry);
                var isCurrent = _current.IsValid && _current.Weekday == day && _current.Period == period.Index;
                var isNext = _next != null && _next.Weekday == day && _next.PeriodIndex == period.Index;

                if (isCurrent || isNext)
                {
                    using (var brush = new SolidBrush(isCurrent ? ScheduleRender.CurrentBack : ScheduleRender.NextBack))
                    {
                        g.FillRectangle(brush, rect.X + Scale(3), rect.Y + Scale(3), rect.Width - Scale(6), rect.Height - Scale(6));
                    }

                    TextRenderer.DrawText(g, text, UiFont.CourseBold, rect, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                else
                {
                    TextRenderer.DrawText(g, text, UiFont.Body, rect, ScheduleRender.TextColor(entry) ?? Color.FromArgb(38, 42, 48),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}
