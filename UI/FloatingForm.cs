using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeRing.App;
using KeRing.App.Schedule;

namespace KeRing.UI
{
    /// <summary>
    /// 悬浮窗：平时只显示**今天**的几节课——小窗、半透明、可拖、**不抢焦点**；
    /// 鼠标移上去（或点一下）在旁边弹出**全周预览**，离开就收回。
    ///
    /// 设计稿与取舍见 docs/开发交接.md 7.12，几个关键决定：
    ///   · **小窗不动、预览在旁边弹**——如果把小窗本身撑大，窗口在鼠标底下变大，
    ///     MouseEnter/Leave 会来回打架，视觉上疯狂闪烁；
    ///   · **不抢焦点**（WS_EX_NOACTIVATE）：正在上课时不能被打断；
    ///   · **默认不置顶（沉在下面）**：使用方 2026-09-16 定的——一体机上要留出全屏课件的位置，
    ///     需要常显时点表头那个**图钉**把它钉在最上面；
    ///   · 位置记进配置，换屏幕/改分辨率后如果跑到屏幕外，自动回到默认位置；
    ///   · 数据由主窗口推给它（**它不自己取数、不自己算提醒**），免得两处显示不一致。
    ///
    /// 班级版跟个人版（KeRing_SOLO）的差别只有一处：**这里没有晨读/课1/课2 值班行**——
    /// 班级版整张表就是一个班的课，值班是老师个人才有的东西。
    /// </summary>
    internal sealed class FloatingForm : Form
    {
        // 逻辑尺寸（都过 UiScale）
        /// <summary>
        /// 小窗宽度。**比个人版（210）窄得多**：班级版只有"学科名"（两个字），个人版要写
        /// "三(13)电"这种"班级+科目"；而且使用方 2026-09-16 又要求去掉脚注和右上角时钟，
        /// 于是小窗里只剩"表头（今天 周三 + 图钉）+ 每行（时刻 + 学科）"，宽度就能压到 124。
        ///
        /// 底数是量出来的（微软雅黑、96 DPI）：
        ///   表头 10 + 73（"今天 周三" 11 磅粗体）+ 4 + 18（图钉）+ 10 = **115 ← 瓶颈**
        ///   一行 12 + 46（08:50）+ 4 + 37（两字学科）+ 10 = 109
        /// 也就是说小窗现在比一行课还窄不了——**是表头在撑着**，想再窄就得先动标题或图钉。
        /// 124 = 表头 115 留 9 像素余量，顺带让**三个字**的学科名（51）也放得下。
        /// 使用方 2026-09-16 明确"按两个字就行"；真要出现**四个字**的学科名，那一格会变省略号，
        /// 到时候回来改这里，别在别处改。各段宽度见 docs/开发交接.md 的悬浮窗一节。
        /// </summary>
        private const int PanelWidth = 124;
        private const int HeaderHeight = 34;
        private const int RowHeight = 30;
        private const int EdgeMargin = 20;         // 首次摆位时离屏幕边缘留多少
        private const int HoverDelayMs = 250;      // 进小窗后等这么久才弹预览（防路过误触）
        private const int LeaveDelayMs = 300;      // 离开两者多久后收回（防两个窗口之间移动时闪）

        private static readonly Color HeaderBack = Color.FromArgb(244, 246, 248);
        private static readonly Color LineColor = Color.FromArgb(222, 226, 230);
        private static readonly Color MutedText = Color.FromArgb(110, 116, 122);
        private static readonly Color BodyText = Color.FromArgb(38, 42, 48);
        /// <summary>图钉"已置顶"时的颜色（跟界面上那几个主操作按钮同色系）。</summary>
        private static readonly Color PinnedColor = Color.FromArgb(46, 107, 230);
        private static readonly Color PinHoverBack = Color.FromArgb(230, 234, 240);

        private readonly AppConfig _config;
        private readonly MainForm _main;
        private readonly Timer _hoverTimer;
        private readonly Timer _leaveTimer;
        private readonly ToolTip _tip = new ToolTip();
        private WeekPreviewForm _preview;

        private WeekSchedule _view;
        private ReminderPoint _next;
        private LessonRef _current = LessonRef.None;
        private DateTime _now = DateTime.MinValue;
        private bool _dragging;
        private bool _moved;
        private bool _pinHover;

        public FloatingForm(AppConfig config, MainForm main)
        {
            _config = config;
            _main = main;

            // 刻意**不设 Owner**：设了之后，主窗口一旦不可见（收进托盘、启动就最小化），
            // Windows 会把这个"被拥有的窗口"一起藏起来——实测踩过（窗口建了但 IsWindowVisible=false）。
            // 需要"主窗口关了我也走"这件事，由 MainForm.OnFormClosed 里显式 Dispose 保证。

            FormBorderStyle = FormBorderStyle.None;
            Text = "悬浮窗";   // 没有标题栏看不见，但排查问题时靠它认窗口
            ShowInTaskbar = false;
            TopMost = config == null || config.FloatingTopMost;   // 表头上那个图钉切的就是它
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            Font = UiFont.Body;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            // 半透明：既能看见，又不至于挡死底下的东西（配置里可调，默认 92%）
            var opacity = config == null ? 92 : config.FloatingOpacity;
            if (opacity < 40) { opacity = 40; }
            if (opacity > 100) { opacity = 100; }
            Opacity = opacity / 100.0;

            ClientSize = new Size(UiScale.S(PanelWidth), UiScale.S(HeaderHeight + RowHeight));

            _hoverTimer = new Timer { Interval = HoverDelayMs };
            _hoverTimer.Tick += (sender, args) => { _hoverTimer.Stop(); ShowPreview(); };

            _leaveTimer = new Timer { Interval = LeaveDelayMs };
            _leaveTimer.Tick += (sender, args) =>
            {
                _leaveTimer.Stop();
                if (PointerOverUs()) { return; }   // 鼠标还在小窗或预览上，别收
                HidePreview();
            };

            MouseEnter += (sender, args) => { _leaveTimer.Stop(); _hoverTimer.Start(); };
            MouseLeave += (sender, args) => { _hoverTimer.Stop(); _leaveTimer.Start(); };

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseClick += (sender, args) =>
            {
                // 点的是图钉 → 切置顶；否则（没拖动过）→ 展开/收起全周
                // 注意：**判断就放在 Click 里**，别指望 MouseUp 的顺序——
                // 实测 WinForms 是 MouseDown → MouseClick → MouseUp，用标志位在 MouseUp 里判断会永远不触发。
                if (PinRect().Contains(args.Location)) { ToggleTopMost(); return; }

                // 不置顶时，点一下把它抬到普通窗口上面（像正常窗口那样），但不抢键盘焦点
                if (!TopMost) { BringToFrontWithoutActivating(); }
                if (!_moved) { TogglePreview(); }
            };
            MouseDoubleClick += (sender, args) => OpenMainWindow();
            UpdatePinTip();
        }

        /// <summary>点了它不抢焦点（否则全屏上课时会被打断）。</summary>
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW：不出现在 Alt+Tab
                return cp;
            }
        }

        /// <summary>
        /// 表头右侧那个图钉的位置（画它、点它都按这个矩形算）。
        /// </summary>
        private Rectangle PinRect()
        {
            var side = UiScale.S(18);
            var pad = UiScale.S(10);

            // 表头右边只剩图钉（时钟按使用方 2026-09-16 要求去掉了），它就贴右边距
            return new Rectangle(Width - pad - side, UiScale.S(8), side, side);
        }

        /// <summary>
        /// 图钉的提示语：说清楚"点一下会怎么样"。
        /// **只在鼠标停到图钉上时才挂提示**——挂在整个窗体上的话，鼠标只要移进来就弹一条长提示，
        /// 正好盖住今天要看的几节课（实测踩过）。
        /// </summary>
        private void UpdatePinTip()
        {
            try
            {
                _tip.SetToolTip(this, _pinHover
                    ? (TopMost
                        ? "已置顶（不会被别的窗口盖住）· 点一下改为不置顶"
                        : "不置顶（会被别的窗口盖住）· 点一下改为置顶")
                    : string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Warn("设置悬浮窗提示失败：" + ex.Message);
            }
        }

        /// <summary>点图钉：置顶 ↔ 不置顶，并记进配置。</summary>
        private void ToggleTopMost()
        {
            TopMost = !TopMost;

            if (!TopMost) { SendToBottom(); }   // 光清 TopMost 位不够，得真把它沉下去

            if (_config != null)
            {
                _config.FloatingTopMost = TopMost;
                _config.Save();
            }

            if (_preview != null && !_preview.IsDisposed) { _preview.TopMost = TopMost; }

            UpdatePinTip();
            Invalidate(PinRect());
            Logger.Info(TopMost ? "悬浮窗：已置顶" : "悬浮窗：已取消置顶（沉到最下面，会被别的窗口盖住）");
        }

        /// <summary>
        /// 主窗口把"同一份课表"推过来（悬浮窗自己不取数）。
        /// 注意：班級版的小窗**不显示脚注**（使用方 2026-09-16 要求），所以这里没有 muted 参数
        /// ——个人版那边有，"已静音"那句是写在脚注里的。
        /// </summary>
        public void SetData(WeekSchedule view, ReminderPoint next, DateTime now)
        {
            _view = view;
            _next = next;
            _now = now;
            _current = ScheduleRender.CurrentLesson(view, now);

            var rows = BuildTodayRows();
            // 没课时也要留一行——那一行写"今天没有课"
            var lines = rows.Count == 0 ? 1 : rows.Count;
            var wanted = UiScale.S(HeaderHeight) + lines * UiScale.S(RowHeight);
            if (Height != wanted)
            {
                Height = wanted;
                ClampToScreen();   // 高度是"往下长"的，不拉回来就会顶出屏幕底边
            }

            Invalidate();

            if (_preview != null && _preview.Visible)
            {
                _preview.SetData(_view, _next, _config == null ? 4 : _config.MorningSplitAfterPeriod, now);
                PlacePreview();
            }
        }

        /// <summary>
        /// 按配置把窗口摆到该在的位置：**首次贴在屏幕右侧、垂直居中**（使用方 2026-09-16 定的，
        /// 比右下角更顺手——右下角常被输入法/通知区压着），之后用你拖过的位置。
        /// </summary>
        public void ApplyPosition()
        {
            var left = _config == null ? int.MinValue : _config.FloatingLeft;
            var top = _config == null ? int.MinValue : _config.FloatingTop;

            if (left == int.MinValue || top == int.MinValue || !IsOnAnyScreen(left, top))
            {
                var area = Screen.PrimaryScreen.WorkingArea;
                left = area.Right - Width - UiScale.S(EdgeMargin);
                top = area.Top + Math.Max(0, (area.Height - Height) / 2);
            }

            Location = new Point(left, top);
        }

        /// <summary>窗口是不是还在某个屏幕里（换显示器、改分辨率之后可能跑到屏幕外）。</summary>
        private bool IsOnAnyScreen(int left, int top)
        {
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(new Rectangle(left, top, 60, 60))) { return true; }
            }

            return false;
        }

        /// <summary>
        /// 别让窗口跑出屏幕：改尺寸（往下长）和拖动之后都过一遍。
        /// 默认位置是按"当时的高度"算的，一旦课表行数变了、窗口长高，底边就会顶出屏幕——
        /// 那几行课就永远看不见了（实测踩过）。
        /// </summary>
        private void ClampToScreen()
        {
            var area = Screen.FromControl(this).WorkingArea;

            var left = Math.Min(Math.Max(Left, area.Left), Math.Max(area.Left, area.Right - Width));
            var top = Math.Min(Math.Max(Top, area.Top), Math.Max(area.Top, area.Bottom - Height));

            if (left != Left || top != Top) { Location = new Point(left, top); }
        }

        private void SavePosition()
        {
            if (_config == null) { return; }
            if (_config.FloatingLeft == Left && _config.FloatingTop == Top) { return; }

            _config.FloatingLeft = Left;
            _config.FloatingTop = Top;
            _config.Save();
        }

        // ---------- 今天的几节课 ----------

        private sealed class TodayRow
        {
            public TimeSpan Start;
            public string Text = string.Empty;
            public CourseEntry Entry;
        }

        /// <summary>今天要上的课，按上课时刻排。</summary>
        private List<TodayRow> BuildTodayRows()
        {
            var rows = new List<TodayRow>();
            if (_view == null) { return rows; }

            var weekday = ReminderPlanner.ToWeekday((_now == DateTime.MinValue ? AppClock.Now : _now).DayOfWeek);

            foreach (var entry in _view.EntriesOfDay(weekday))
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Course)) { continue; }

                var period = _view.FindPeriod(entry.Period);
                rows.Add(new TodayRow
                {
                    Start = period == null ? TimeSpan.Zero : period.StartTime,
                    Text = ScheduleRender.CellText(entry),
                    Entry = entry,
                });
            }

            rows.Sort((a, b) => a.Start.CompareTo(b.Start));
            return rows;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);

            var headerH = UiScale.S(HeaderHeight);
            var rowH = UiScale.S(RowHeight);
            var pad = UiScale.S(10);

            // 表头：左边"今天 周三"，右边只有图钉——**时钟按使用方 2026-09-16 要求去掉了**
            // （要看时间看系统右下角就行，小窗越简单越不挡人）
            using (var back = new SolidBrush(HeaderBack)) { g.FillRectangle(back, 0, 0, Width, headerH); }
            var weekday = ReminderPlanner.ToWeekday((_now == DateTime.MinValue ? AppClock.Now : _now).DayOfWeek);
            var pin = PinRect();
            TextRenderer.DrawText(g, "今天 " + ReminderPlanner.WeekdayName(weekday), UiFont.Button,
                new Rectangle(pad, 0, pin.Left - pad - UiScale.S(4), headerH), BodyText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            DrawPin(g, pin);

            using (var pen = new Pen(LineColor)) { g.DrawLine(pen, 0, headerH, Width, headerH); }

            var rows = BuildTodayRows();
            var y = headerH;

            if (rows.Count == 0)
            {
                TextRenderer.DrawText(g, "今天没有课", Font,
                    new Rectangle(0, y, Width, rowH), MutedText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                y += rowH;
            }

            foreach (var row in rows)
            {
                var isCurrent = _current.IsValid && _current.Weekday == weekday &&
                                row.Entry != null && _current.Period == row.Entry.Period;
                var isNext = _next != null && _next.Weekday == weekday &&
                             row.Entry != null && _next.PeriodIndex == row.Entry.Period;

                if (isCurrent || isNext)
                {
                    using (var brush = new SolidBrush(isCurrent ? ScheduleRender.CurrentBack : ScheduleRender.NextBack))
                    {
                        g.FillRectangle(brush, UiScale.S(4), y + 1, Width - UiScale.S(8), rowH - 2);
                    }
                }

                var timeColor = (isCurrent || isNext) ? Color.White : MutedText;
                var textColor = (isCurrent || isNext) ? Color.White : (ScheduleRender.TextColor(row.Entry) ?? BodyText);

                TextRenderer.DrawText(g, row.Start.ToString(@"hh\:mm"), UiFont.Body,
                    new Rectangle(pad + UiScale.S(2), y, UiScale.S(46), rowH), timeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, row.Text, Font,
                    new Rectangle(UiScale.S(62), y, Width - UiScale.S(70), rowH), textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                y += rowH;
            }
        }

        /// <summary>
        /// 画那个图钉：置顶时是**实心蓝**（一眼看出"钉住了"），不置顶时是**灰色空心、还斜 45°**
        /// （跟 Windows 里"取消固定"的习惯一致）。鼠标移上去加一层浅灰底。
        /// </summary>
        private void DrawPin(Graphics g, Rectangle rect)
        {
            if (_pinHover)
            {
                using (var back = new SolidBrush(PinHoverBack))
                {
                    g.FillRectangle(back, rect.X - UiScale.S(2), rect.Y - UiScale.S(2),
                        rect.Width + UiScale.S(4), rect.Height + UiScale.S(4));
                }
            }

            var color = TopMost ? PinnedColor : MutedText;
            var cx = rect.X + rect.Width / 2f;

            var state = g.Save();
            if (!TopMost)
            {
                // 没置顶：斜着画（转 45°），跟"钉住"区分开
                g.TranslateTransform(cx, rect.Y + rect.Height / 2f);
                g.RotateTransform(45f);
                g.TranslateTransform(-cx, -(rect.Y + rect.Height / 2f));
            }

            var width = Math.Max(1f, rect.Width / 11f);
            var top = rect.Y + rect.Height * 0.22f;
            var bodyHeight = rect.Height * 0.34f;
            var halfBody = rect.Width * 0.24f;

            using (var pen = new Pen(color, width))
            using (var brush = new SolidBrush(color))
            using (var body = new GraphicsPath())
            {
                // 针（下半截那根细线）
                g.DrawLine(pen, cx, top + bodyHeight, cx, rect.Bottom - rect.Height * 0.12f);

                // 钉身（上宽下窄的梯形）
                body.AddPolygon(new[]
                {
                    new PointF(cx - halfBody * 0.55f, top + bodyHeight * 0.35f),
                    new PointF(cx + halfBody * 0.55f, top + bodyHeight * 0.35f),
                    new PointF(cx + halfBody, top + bodyHeight),
                    new PointF(cx - halfBody, top + bodyHeight),
                });
                if (TopMost) { g.FillPath(brush, body); }
                g.DrawPath(pen, body);

                // 钉帽（横着的一小段）
                var capRect = new RectangleF(cx - halfBody * 0.8f, top, halfBody * 1.6f, bodyHeight * 0.45f);
                if (TopMost) { g.FillEllipse(brush, capRect); }
                g.DrawEllipse(pen, capRect);
            }

            g.Restore(state);
        }

        // ---------- 预览 ----------

        private void TogglePreview()
        {
            if (_preview != null && _preview.Visible) { HidePreview(); }
            else { ShowPreview(); }
        }

        private void ShowPreview()
        {
            if (!Visible || IsDisposed) { return; }
            if (_config != null && !_config.FloatingShowWeekOnHover) { return; }

            if (_preview == null || _preview.IsDisposed)
            {
                _preview = new WeekPreviewForm();
                _preview.MouseEnter += (sender, args) => { _leaveTimer.Stop(); };
                _preview.MouseLeave += (sender, args) => { _leaveTimer.Start(); };
            }

            _preview.TopMost = TopMost;   // 小窗不置顶时，预览也别钉在最上面
            _preview.SetData(_view, _next, _config == null ? 4 : _config.MorningSplitAfterPeriod,
                _now == DateTime.MinValue ? AppClock.Now : _now);
            PlacePreview();
            _preview.Show();

            // 小窗沉在下面时，预览是**新弹出来给你看的那一个**，必须保证它露得出来
            // （否则一片"看了没反应"）。只抬到普通窗口的最上面，不抢焦点。
            if (!TopMost) { RaiseWithoutActivating(_preview.Handle); }
        }

        private void HidePreview()
        {
            if (_preview == null || _preview.IsDisposed) { return; }
            _preview.Hide();
        }

        /// <summary>预览摆在小窗旁边：优先右侧，右边放不下就左侧；下面空间不够就往上顶。</summary>
        private void PlacePreview()
        {
            if (_preview == null || _preview.IsDisposed) { return; }

            var screen = Screen.FromControl(this).WorkingArea;
            var gap = UiScale.S(6);

            var left = Right + gap;
            if (left + _preview.Width > screen.Right) { left = Left - gap - _preview.Width; }
            if (left < screen.Left) { left = screen.Left; }

            var top = Top;
            if (top + _preview.Height > screen.Bottom) { top = screen.Bottom - _preview.Height; }
            if (top < screen.Top) { top = screen.Top; }

            _preview.Location = new Point(left, top);
        }

        private bool PointerOverUs()
        {
            var pos = Cursor.Position;
            if (Bounds.Contains(pos)) { return true; }
            return _preview != null && !_preview.IsDisposed && _preview.Visible && _preview.Bounds.Contains(pos);
        }

        private void OpenMainWindow()
        {
            if (_main != null && !_main.IsDisposed) { _main.ShowWindowNow(); }
        }

        // ---------- 拖动（无边框窗口要自己实现）----------

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        /// <summary>
        /// **默认不置顶 = 沉到最下面**。窗口刚建出来时 Windows 会把它摆在最上面，
        /// 光把 `TopMost` 设成 false 不够——必须主动沉一次，不然使用者看到的还是"没置顶却盖着别人"。
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (!TopMost) { SendToBottom(); }
        }

        private void SendToBottom()
        {
            try
            {
                SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch (Exception ex)
            {
                Logger.Warn("把悬浮窗沉下去失败：" + ex.Message);
            }
        }

        /// <summary>不置顶状态下点它：抬到普通窗口的最上面（不抢焦点）。</summary>
        private void BringToFrontWithoutActivating()
        {
            RaiseWithoutActivating(Handle);
        }

        private static void RaiseWithoutActivating(IntPtr handle)
        {
            try
            {
                SetWindowPos(handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch (Exception ex)
            {
                Logger.Warn("抬起悬浮窗失败：" + ex.Message);
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { return; }

            // 点的是图钉：只切换置顶，不要拖着窗口跑
            if (PinRect().Contains(e.Location)) { return; }

            _dragging = true;
            _moved = false;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);   // 交给系统去拖，省得自己算偏移
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var hover = PinRect().Contains(e.Location);
            if (hover != _pinHover)
            {
                _pinHover = hover;
                UpdatePinTip();
                Invalidate(PinRect());
            }

            if (!_dragging) { return; }
            _moved = true;
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (!_dragging) { return; }

            _dragging = false;
            if (_moved)
            {
                ClampToScreen();   // 别被拖到屏幕外面去
                SavePosition();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_preview != null && !_preview.IsDisposed) { _preview.Close(); }
            base.OnFormClosed(e);
        }
    }
}
