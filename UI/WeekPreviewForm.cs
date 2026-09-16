using System;
using System.Drawing;
using System.Windows.Forms;
using KeRing.App.Schedule;

namespace KeRing.UI
{
    /// <summary>
    /// 悬浮窗的"全周预览"：鼠标移到小窗上时在旁边弹出，离开就收回。
    ///
    /// 跟小窗一样是**无边框 + 不抢焦点**，并且**只读**（上面什么都不用点）。
    /// 课表内容由 `WeekGridView` 画，文字/颜色规则跟主窗口共用（见 ScheduleRender）。
    /// </summary>
    internal sealed class WeekPreviewForm : Form
    {
        private const int HeaderHeight = 34;
        private const int PaddingSize = 6;

        private static readonly Color HeaderBack = Color.FromArgb(244, 246, 248);
        private static readonly Color LineColor = Color.FromArgb(222, 226, 230);
        private static readonly Color MutedText = Color.FromArgb(110, 116, 122);

        private readonly WeekGridView _grid;
        private string _title = "本周课表";

        public WeekPreviewForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            Text = "本周课表预览";
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            Font = UiFont.Body;
            DoubleBuffered = true;

            _grid = new WeekGridView { Location = new Point(UiScale.S(PaddingSize), UiScale.S(HeaderHeight + PaddingSize)) };
            Controls.Add(_grid);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public void SetData(WeekSchedule view, ReminderPoint next, int splitAfterPeriod, DateTime now)
        {
            _grid.SetData(view, next, splitAfterPeriod, now);
            _grid.Size = new Size(_grid.MeasureWidth(), _grid.MeasureHeight());

            _title = "本周课表 · 鼠标移开就收回";
            ClientSize = new Size(
                _grid.Width + UiScale.S(PaddingSize * 2),
                UiScale.S(HeaderHeight) + _grid.Height + UiScale.S(PaddingSize * 2));

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var headerH = UiScale.S(HeaderHeight);
            var pad = UiScale.S(PaddingSize);

            using (var back = new SolidBrush(HeaderBack)) { g.FillRectangle(back, 0, 0, Width, headerH); }
            TextRenderer.DrawText(g, _title, UiFont.Button,
                new Rectangle(pad + UiScale.S(4), 0, Width - pad * 2, headerH), MutedText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            using (var pen = new Pen(LineColor))
            {
                g.DrawLine(pen, 0, headerH, Width, headerH);
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
