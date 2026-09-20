using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KeRing.App;
using KeRing.App.Audio;
using KeRing.App.Schedule;

namespace KeRing.UI
{
    internal sealed class MainForm : Form
    {
        private readonly AppConfig _config;
        private readonly EventWaitHandle _showSignal;
        private readonly Scheduler _scheduler;
        private readonly Announcer _announcer = new Announcer();
        private readonly Dictionary<int, int> _rowByPeriod = new Dictionary<int, int>();

        private readonly float _scale;

        // 表格与工具栏尺寸（96 DPI 逻辑值）
        private const int ToolbarHeight = 60;
        private const int ButtonWidth = 120;
        private const int ButtonHeight = 40;
        private const int GridColumnHeaderHeight = 38;
        private const int GridPreferredRowHeight = 56;
        private const int GridMinRowHeight = 44;
        private const int GridLabelColumnWidth = 132;
        private const int GridDayColumnWidth = 124;
        private const int SplitBandHeight = 18;

        /// <summary>上下午分割色带（深蓝）。</summary>
        private static readonly Color SplitBandColor = Color.FromArgb(36, 69, 127);

        /// <summary>
        /// 调课调过来的格子：蓝色字体，跟基础课表区分开。
        /// 跟另外两种标记并列——红底=正在上的课，绿底=下一个提醒点。
        /// </summary>
        private static readonly Color AdjustedCourseColor = Color.FromArgb(21, 101, 192);

        private DataGridView _grid;
        private Button _btnRefresh;
        private Button _btnTest;
        private Button _btnSettings;
        private Button _btnMinimize;
        private Button _btnMute;
        private ToolStripMenuItem _muteMenuItem;
        private ToolStripMenuItem _floatingMenuItem;
        private StatusStrip _statusBar;
        private StatusStrip _statusBarBottom;
        private ToolStripStatusLabel _lblClock;
        private ToolStripStatusLabel _lblClass;
        private ToolStripStatusLabel _lblNext;
        private ToolStripStatusLabel _lblData;
        private ToolStripStatusLabel _lblAudio;
        private ToolStripStatusLabel _lblLastAnnounce;
        private NotifyIcon _tray;
        /// <summary>悬浮窗（今天的课，鼠标移上去看全周）。默认建出来（见 AppConfig.FloatingEnabled）。</summary>
        private FloatingForm _floating;
        private System.Windows.Forms.Timer _uiTimer;

        private WeekSchedule _schedule;
        private SchoolSchedule _school;
        private string _className = string.Empty;
        private bool _needsClassChoice;
        private bool _classChooserOpen;

        /// <summary>
        /// 后台刷新进行中。取数改成走 HTTP 之后不能再在 UI 线程上同步跑——
        /// 接口慢或不通的时候窗口会僵住，一体机上看起来就像死机。
        /// </summary>
        private bool _refreshRunning;

        /// <summary>
        /// 连续失败次数。决定下一次隔多久重试——失败后不能等满整个刷新间隔，
        /// 否则开机撞上网络还没起来，第一节课就漏了（见 RefreshDueMinutes）。
        /// </summary>
        private int _consecutiveFailures;

        /// <summary>当前显示的是不是"上次成功的缓存"；接口通了会被新数据替换掉。</summary>
        private bool _usingCache;
        private DateTime _cacheSavedAt = DateTime.MinValue;

        /// <summary>最近一次成功的时刻。状态栏常显它，好让"数据已经陈旧很久"看得见。</summary>
        private DateTime _lastSuccess = DateTime.MinValue;

        private DataGridViewCell _currentCell;
        private DataGridViewCell _nextCell;
        private string _dataStatus = "尚未加载";
        private DateTime _lastRefresh = DateTime.MinValue;
        private DateTime _lastAudioCheck = DateTime.MinValue;
        private bool _exiting;
        private bool _announcing;
        private bool _sizedToContent;
        private bool _fillingRows;
        private bool _trayHintShown;
        private bool _muted;
        private int _splitRowIndex = -1;
        private int _appliedWidth;

        public MainForm(AppConfig config, EventWaitHandle showSignal)
        {
            _config = config;
            _showSignal = showSignal;
            _scale = DetectScale();
            _scheduler = new Scheduler(config.RemindAheadList);
            _scheduler.ReminderDue += OnReminderDue;

            BuildUi();
            StartShowSignalListener();
        }

        /// <summary>
        /// 界面按 96 DPI 设计，在高 DPI 屏上整体放大。
        /// 不走 WinForms 的自动缩放：实测 AutoScaleDimensions 会被重置成当前 DPI，缩放比例恒为 1，
        /// 结果是在 200% 缩放的 4K 屏上窗口只有预期物理尺寸的一半，行高不够把课表内容裁掉了。
        /// </summary>
        private static float DetectScale()
        {
            return UiScale.Factor;
        }

        private int S(int value)
        {
            return UiScale.S(value);
        }

        private Size S(int width, int height)
        {
            return UiScale.S(width, height);
        }

        private Point P(int x, int y)
        {
            return UiScale.P(x, y);
        }

        /// <summary>取嵌入的程序图标；失败就退回系统默认图标，不影响打铃。</summary>
        private static Icon LoadAppIcon(Size size)
        {
            try
            {
                using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("KeRing.Assets.app.ico"))
                {
                    if (stream != null) { return new Icon(stream, size); }
                }

                Logger.Warn("找不到嵌入的程序图标，改用系统默认图标");
            }
            catch (Exception ex)
            {
                Logger.Warn("加载程序图标失败：" + ex.Message);
            }

            return SystemIcons.Application;
        }

        // ---------- 界面搭建 ----------

        private void BuildUi()
        {
            AutoScaleMode = AutoScaleMode.None; // 缩放由 _scale 自己做

            Text = "智能课表打铃";
            Font = UiFont.Body;
            Icon = LoadAppIcon(SystemInformation.IconSize);
            // 打开时的尺寸先占位，真正的宽高在课表加载完后按内容算
            // （见 ApplyContentWidth / FitWindowToContent）
            ClientSize = S(1000, 520);
            MinimumSize = S(680, 420);
            StartPosition = FormStartPosition.CenterScreen;

            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = S(ToolbarHeight),
                Padding = new Padding(S(12), S(10), S(12), 0),
            };

            _btnRefresh = MakeButton("更新课表", 12,
                Color.FromArgb(46, 107, 230), Color.FromArgb(74, 130, 240), Color.FromArgb(34, 84, 186));
            _btnTest = MakeButton("测试播报", 140,
                Color.FromArgb(40, 154, 89), Color.FromArgb(58, 178, 108), Color.FromArgb(28, 126, 70));
            // 深紫罗兰：跟蓝/绿/灰同属冷色调，一眼分得清，又不像黄色那样抢眼
            _btnMinimize = MakeButton("最小化", 268,
                Color.FromArgb(106, 69, 168), Color.FromArgb(126, 87, 194), Color.FromArgb(86, 55, 137));
            _btnMute = MakeButton("静音", 396,
                Color.FromArgb(90, 100, 112), Color.FromArgb(112, 122, 136), Color.FromArgb(68, 76, 86));

            // 设置单独放最右边，跟左边那组隔开；位置跟着工具栏宽度走（窗口宽度会随周末列变），
            // 所以不能写死 x，见 PositionSettingsButton。
            _btnSettings = MakeButton("设置", 0,
                Color.FromArgb(90, 100, 112), Color.FromArgb(112, 122, 136), Color.FromArgb(68, 76, 86));
            // 红边凸显一下——这是管理员最常点的按钮（换班级、改作息方案都在里面）
            _btnSettings.FlatAppearance.BorderSize = S(2);
            _btnSettings.FlatAppearance.BorderColor = Color.FromArgb(206, 66, 62);
            toolbar.Resize += (sender, args) => PositionSettingsButton(toolbar);
            _btnRefresh.Click += (sender, args) => RefreshSchedule();
            _btnTest.Click += (sender, args) => AnnounceNextOrSample();
            _btnSettings.Click += (sender, args) => OpenSettings();
            _btnMinimize.Click += (sender, args) => HideToTray();
            _btnMute.Click += (sender, args) => ToggleMute();
            toolbar.Controls.Add(_btnRefresh);
            toolbar.Controls.Add(_btnTest);
            toolbar.Controls.Add(_btnMinimize);
            toolbar.Controls.Add(_btnMute);
            toolbar.Controls.Add(_btnSettings);

            BuildGrid();
            BuildStatusBar();
            BuildTray();

            Controls.Add(_grid);
            Controls.Add(toolbar);
            Controls.Add(_statusBar);
            Controls.Add(_statusBarBottom);

            PositionSettingsButton(toolbar);

            _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _uiTimer.Tick += OnUiTick;
        }

        /// <summary>带配色的扁平按钮：常态、悬停、按下三种颜色，白字加粗。</summary>
        /// <summary>带配色的扁平按钮：常态、悬停、按下三种底色，白字加粗。</summary>
        private Button MakeButton(string text, int x, Color back, Color hover, Color pressed)
        {
            var button = new Button
            {
                Text = text,
                Size = S(ButtonWidth, ButtonHeight),
                Location = P(x, 10),
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Font = UiFont.Button,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = pressed;
            return button;
        }

        /// <summary>
        /// 把"设置"贴到工具栏最右边。工具栏宽度会随窗口宽度变（周末两列隐藏时窗口会变窄），
        /// 所以每次 Resize 都重算，不能写死坐标。
        /// </summary>
        private void PositionSettingsButton(Panel toolbar)
        {
            if (_btnSettings == null || toolbar == null) { return; }

            // 这里不能用 P(x, y)：它会把 x 再乘一次缩放系数，而 ClientSize 已经是实际像素
            var left = toolbar.ClientSize.Width - S(12) - _btnSettings.Width;
            _btnSettings.Location = new Point(left, S(10));
        }

        private void BuildGrid()
        {
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                GridColor = Color.FromArgb(222, 226, 230),
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = S(GridColumnHeaderHeight),
            };

            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 248);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 64, 68);
            _grid.ColumnHeadersDefaultCellStyle.Font = UiFont.Header;
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            _grid.DefaultCellStyle.Font = UiFont.Course;
            _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.DefaultCellStyle.SelectionBackColor = Color.White;
            _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.RowTemplate.Height = S(GridPreferredRowHeight);
            // 行高自己算：窗口拉高时把行撑开填满，不留空白；窗口太矮就压到最小行高再出滚动条
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _grid.Resize += (sender, args) => FillRows();
            _grid.SelectionChanged += (sender, args) => _grid.ClearSelection();
        }

        /// <summary>
        /// 状态栏分两行：**上行**放"时间 / 班级 / 下一个提醒点"，**下行**放"数据 / 音量 / 播报"。
        /// 原来六项挤在一行里，最右边的"播报"经常被挤掉；而"下一个提醒点"和"数据"
        /// 恰恰是文本最长的两项，分开以后各自有整行可用。
        /// </summary>
        private void BuildStatusBar()
        {
            _statusBar = MakeStatusStrip();
            _statusBarBottom = MakeStatusStrip();

            _lblClock = new ToolStripStatusLabel
            {
                Text = "--:--:--",
                Font = UiFont.Clock,
                AutoSize = true,
            };
            _lblNext = new ToolStripStatusLabel
            {
                Text = "下一个提醒点：--",
                // AutoSize=false + Spring：占满剩余宽度，窗口变窄时从尾部截断，
                // 而不是整块被其他标签挤成一个残字。
                AutoSize = false,
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            _lblClass = new ToolStripStatusLabel { Text = "班级：--", AutoSize = true };
            _lblData = new ToolStripStatusLabel { Text = "数据：--", AutoSize = true };
            _lblAudio = new ToolStripStatusLabel { Text = "音量：--", AutoSize = true };
            _lblLastAnnounce = new ToolStripStatusLabel
            {
                Text = "播报：--",
                // 这一项最长，让它吃掉整行剩余宽度，窗口窄时从尾部截断，不挤掉左边的"数据"
                AutoSize = false,
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _statusBar.Items.AddRange(new ToolStripItem[]
            {
                _lblClock, _lblClass, _lblNext,
            });

            _statusBarBottom.Items.AddRange(new ToolStripItem[]
            {
                _lblData, _lblAudio, _lblLastAnnounce,
            });
        }

        private static StatusStrip MakeStatusStrip()
        {
            return new StatusStrip
            {
                SizingGrip = false,
                ShowItemToolTips = true,
                Font = UiFont.Body,
            };
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon
            {
                Icon = LoadAppIcon(SystemInformation.SmallIconSize),
                Text = "智能课表打铃",
                Visible = true,
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (sender, args) => ShowWindowNow());
            menu.Items.Add("立即更新课表", null, (sender, args) => RefreshSchedule());
            menu.Items.Add("测试播报", null, (sender, args) => AnnounceNextOrSample());
            _muteMenuItem = new ToolStripMenuItem("静音");
            _muteMenuItem.Click += (sender, args) => ToggleMute();
            menu.Items.Add(_muteMenuItem);
            _floatingMenuItem = new ToolStripMenuItem("显示悬浮窗");
            _floatingMenuItem.Checked = _config.FloatingEnabled;
            _floatingMenuItem.Click += (sender, args) => ToggleFloatingFromTray();
            menu.Items.Add(_floatingMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出（不再自动重启）", null, (sender, args) => ExitApplication());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (sender, args) => ShowWindowNow();
        }

        // ---------- 生命周期 ----------

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            ApplyAutoStartFromConfig();
            ApplyWatchdogFromConfig();
            ApplyMuteState();

            // 配置里没有班级 = 第一次运行，稍后取到课表要弹一次选择框
            _needsClassChoice = string.IsNullOrWhiteSpace(_config.SelectedClassId);

            Logger.Info("界面已加载，课表来源：" + ScheduleSourceFactory.Create(_config).Description);
            Logger.Info(string.Format(
                "窗口客户区 {0} x {1}，设备 DPI {2}，界面缩放 {3:P0}",
                ClientSize.Width,
                ClientSize.Height,
                DeviceDpi,
                _scale));

            // 时间和课表都可能是错的，启动时先把这两件事的现状写进日志，出问题好对照
            AppClock.CheckTimeZone();
            AppClock.LogCurrentSource();

            // 先用上次成功的课表顶上，别让窗口空着、也别让铃在取到数据之前干瞪眼。
            // 接口通了以后会被新数据替换掉。
            ApplyCachedScheduleIfAny();

            RefreshSchedule();
            _scheduler.Start();
            UpdateAudioStatus();
            UpdateNextReminderLabel(AppClock.Now);
            _uiTimer.Start();

            if (_config.StartMinimized) { Hide(); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 首次运行要选班级。放在窗口显示之后弹：
            // 一方面用户先看到主界面不会觉得程序没反应，
            // 另一方面在 OnLoad 里开模态框会把主窗口的显示流程挡住。
            if (_needsClassChoice) { BeginInvoke((Action)EnsureClassChosen); }

            // 悬浮窗放这儿建（不放 OnLoad）：**OnLoad 时主窗口还没真正显示**，
            // 而 Windows 不会显示"被拥有/父窗口不可见"的窗口——实测踩过（建了但不可见）。
            ApplyFloatingFromConfig();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && _config.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _uiTimer.Stop();
            _uiTimer.Dispose();
            _scheduler.Dispose();

            if (_floating != null && !_floating.IsDisposed)
            {
                _floating.Dispose();
                _floating = null;
            }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }

            base.OnFormClosed(e);
        }

        private void OnUiTick(object sender, EventArgs e)
        {
            var now = AppClock.Now;
            _lblClock.Text = now.ToString("HH:mm:ss");

            // 日期变了（跨天/跨周）强制刷一次：调课是按周的，跨周后必须换新课表
            if (now.Date != _lastRefresh.Date)
            {
                RefreshSchedule();
            }
            else if ((now - _lastRefresh).TotalMinutes >= RefreshDueMinutes())
            {
                RefreshSchedule();
            }

            if ((now - _lastAudioCheck).TotalSeconds >= 30)
            {
                UpdateAudioStatus();
            }

            UpdateNextReminderLabel(now);
            UpdateHighlights(now);
            PushFloating(now);
        }

        // ---------- 悬浮窗 ----------

        /// <summary>
        /// 按配置建/关悬浮窗。它是**同一份课表的另一个视图**——数据由 `PushFloating` 推过去，
        /// 它自己不取数、不自己算提醒（否则会出现"主窗口和悬浮窗显示不一致"这种最难查的问题）。
        /// </summary>
        private void ApplyFloatingFromConfig()
        {
            if (_config.FloatingEnabled)
            {
                if (_floating == null || _floating.IsDisposed)
                {
                    _floating = new FloatingForm(_config, this);
                }

                _floating.SetData(_schedule, _scheduler.NextReminder, AppClock.Now);
                _floating.ApplyPosition();   // 先按内容定好尺寸，再摆位置（否则按旧高度算会顶出屏幕）
                if (!_floating.Visible) { _floating.Show(); }
                Logger.Info("悬浮窗已显示");
            }
            else if (_floating != null && !_floating.IsDisposed)
            {
                _floating.Hide();
                _floating.Dispose();
                _floating = null;
                Logger.Info("悬浮窗已关闭");
            }

            if (_floatingMenuItem != null) { _floatingMenuItem.Checked = _config.FloatingEnabled; }
        }

        /// <summary>把当前课表和"下一个提醒点"推给悬浮窗（每秒跟着界面一起刷）。</summary>
        private void PushFloating(DateTime now)
        {
            if (_floating == null || _floating.IsDisposed) { return; }

            try
            {
                _floating.SetData(_schedule, _scheduler.NextReminder, now);
            }
            catch (Exception ex)
            {
                // 悬浮窗属于"锦上添花"，它出问题不能影响打铃和主界面
                Logger.Warn("刷新悬浮窗失败：" + ex.Message);
            }
        }

        /// <summary>托盘菜单里开关悬浮窗（跟设置里那个复选框是同一个配置项）。</summary>
        private void ToggleFloatingFromTray()
        {
            _config.FloatingEnabled = !_config.FloatingEnabled;
            _config.Save();
            ApplyFloatingFromConfig();
        }

        // ---------- 课表 ----------

        /// <summary>
        /// 发起一次课表刷新。**取数在后台线程做，结果回到 UI 线程应用**（见 ApplyScheduleResult）。
        /// 按钮、托盘菜单、定时刷新三个入口都走这里，靠 _refreshRunning 防重入。
        /// </summary>
        private void RefreshSchedule()
        {
            if (_refreshRunning) { return; }
            _refreshRunning = true;

            _lastRefresh = AppClock.Now;
            UpdateDataStatusLabel();   // 立刻显示"更新中…"，不然点了按钮像没反应

            var source = ScheduleSourceFactory.Create(_config);
            var worker = new Thread(() =>
            {
                // 先对时间、再取课表：打铃点是从"现在"算出来的，钟差几分钟铃就早/晚几分钟
                try
                {
                    AppClock.SyncNtpIfDue(_config.NtpServers, _config.NtpTimeoutSeconds);
                }
                catch (Exception ex)
                {
                    Logger.Warn("NTP 校时异常：" + ex.Message);
                }

                ScheduleLoadResult load;
                try
                {
                    load = source.Load();
                }
                catch (Exception ex)
                {
                    // 数据源自己会兜异常，这里再兜一层，保证后台线程不会把进程带崩
                    load = new ScheduleLoadResult { Success = false, Message = "更新失败：" + ex.Message };
                }

                try
                {
                    BeginInvoke(new Action(() => ApplyScheduleResult(load)));
                }
                catch (Exception)
                {
                    // 窗口已经关了，没人接这个结果，把标志位放掉就行
                    _refreshRunning = false;
                }
            });

            worker.IsBackground = true;
            worker.Start();
        }

        /// <summary>取数结果回到 UI 线程后应用。失败时保留上一次的课表不动，只更新状态栏和日志。</summary>
        private void ApplyScheduleResult(ScheduleLoadResult result)
        {
            _refreshRunning = false;
            if (IsDisposed) { return; }

            try
            {
                _dataStatus = result.Message;

                if (result.Success)
                {
                    // 周一到周五不该没有课。新数据里这个班工作日一节都没有、而现有课表是有课的，
                    // 那多半是接口抽风，**保留现有课表**，别把有效的换掉。
                    var freshHasWeekday = HasWeekdayCourses(result.Schedule, _config.SelectedClassId);
                    var oldHasWeekday = HasWeekdayCourses(_school, _config.SelectedClassId);

                    if (!freshHasWeekday && oldHasWeekday)
                    {
                        _dataStatus = "接口返回的课表里这个班周一到周五一节都没有，已忽略（保留现有课表）";
                        Logger.Warn(_dataStatus);
                        _consecutiveFailures++;   // 当成失败，下一轮提早重试
                    }
                    else
                    {
                        if (!freshHasWeekday)
                        {
                            Logger.Warn("接口返回的课表周一到周五没有课，先按原样显示（手上也没有更可信的）");
                        }

                        _consecutiveFailures = 0;
                        _usingCache = false;
                        _lastSuccess = AppClock.Now;
                        _school = result.Schedule;

                        BuildScheduleView();
                        BuildRows();

                        if (!_sizedToContent)
                        {
                            _sizedToContent = true;
                            FitWindowToContent();
                        }

                        FillRows();
                        _scheduler.UpdateSchedule(_schedule);
                        UpdateHighlights(AppClock.Now);

                        // 存一份，供下次开机（或接口不通时）顶上
                        ScheduleCache.Save(_school, result.Week);
                        Logger.Info("课表已更新：" + result.Message);
                    }
                }
                else
                {
                    _consecutiveFailures++;
                    Logger.Warn("课表加载失败：" + result.Message);
                }
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _dataStatus = "更新失败：" + ex.Message;
                Logger.Error("更新课表异常", ex);
            }

            UpdateDataStatusLabel();

            // 首次运行（配置里还没班级）时在这里弹一次"选择班级"；
            // 数据第一次没取到也没关系，后续刷新到数据后会补上
            if (_needsClassChoice) { EnsureClassChosen(); }
        }

        /// <summary>
        /// 开机先拿上次成功的课表顶上，取不到就什么都不做（等接口）。
        /// 标成"缓存"状态，接口通了以后 ApplyScheduleResult 会把它换掉。
        /// </summary>
        private void ApplyCachedScheduleIfAny()
        {
            DateTime savedAt;
            int week;
            var cached = ScheduleCache.Load(out savedAt, out week);
            if (cached == null) { return; }

            _school = cached;
            BuildScheduleView();
            BuildRows();

            if (!_sizedToContent)
            {
                _sizedToContent = true;
                FitWindowToContent();
            }

            FillRows();
            _scheduler.UpdateSchedule(_schedule);

            _usingCache = true;
            _cacheSavedAt = savedAt;
            _lastSuccess = savedAt;   // 这份缓存就是那次成功抓下来的，状态栏的"上次成功"按它算
            _dataStatus = string.Format(
                "用的是缓存（{0:MM-dd HH:mm} 取到的第 {1} 周课表），正在尝试更新",
                savedAt,
                week);

            UpdateDataStatusLabel();
            UpdateHighlights(AppClock.Now);
            Logger.Info("已载入课表缓存：" + _dataStatus);
        }

        /// <summary>
        /// 状态栏"数据"那一栏：更新中 / 正常 / 缓存 / 失败，完整原因放悬停提示。
        /// 里面的时刻一律是**上次成功**的时刻——这样"数据已经很久没更新"一眼能看出来。
        /// </summary>
        private void UpdateDataStatusLabel()
        {
            string text;
            if (_refreshRunning)
            {
                text = "数据：更新中…";
            }
            else if (_usingCache)
            {
                text = "数据：缓存 " + _cacheSavedAt.ToString("MM-dd HH:mm");
            }
            else if (_lastSuccess == DateTime.MinValue)
            {
                text = "数据：失败";
            }
            else if (_consecutiveFailures == 0)
            {
                text = "数据：正常 " + _lastSuccess.ToString("HH:mm");
            }
            else
            {
                text = "数据：失败（上次 " + _lastSuccess.ToString("HH:mm") + "）";
            }

            if (_lblData == null) { return; }

            _lblData.Text = text;
            _lblData.ToolTipText = string.Format(
                "上次成功：{0}\n本次：{1}",
                _lastSuccess == DateTime.MinValue
                    ? "（还没有成功过）"
                    : _lastSuccess.ToString("yyyy-MM-dd HH:mm:ss"),
                _dataStatus);
        }

        /// <summary>
        /// 距上次刷新多久才该再刷一次。失败后走短退避：1 → 2 → 5 → 10 分钟，再不成就回到正常间隔。
        /// 开机时网络还没起来，就靠这个抢在第一节课打铃之前把数据拿到。
        /// </summary>
        private int RefreshDueMinutes()
        {
            switch (_consecutiveFailures)
            {
                case 1: return 1;
                case 2: return 2;
                case 3: return 5;
                case 4: return 10;
                default: return _config.RefreshIntervalMinutes;   // 正常，或失败次数已经太多
            }
        }

        /// <summary>某个班在周一~周五有没有课。用来挡住"空课表把有效数据换掉"。</summary>
        private static bool HasWeekdayCourses(SchoolSchedule school, string classId)
        {
            if (school == null || school.Classes == null || school.Classes.Count == 0) { return false; }

            var target = school.FindClass(classId) ?? school.Classes[0];
            if (target == null || target.Entries == null) { return false; }

            foreach (var entry in target.Entries)
            {
                if (entry.Weekday >= 1 && entry.Weekday <= 5 && !string.IsNullOrWhiteSpace(entry.Course))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>按当前课表重建表格的行（列固定为 7 天）。</summary>
        private void BuildRows()
        {
            _rowByPeriod.Clear();
            _currentCell = null;
            _nextCell = null;
            _splitRowIndex = -1;
            _grid.Rows.Clear();

            if (_grid.Columns.Count == 0)
            {
                _grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = "节次",
                    Width = S(GridLabelColumnWidth),
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                });

                for (var day = 1; day <= 7; day++)
                {
                    _grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        HeaderText = ReminderPlanner.WeekdayName(day),
                        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                        SortMode = DataGridViewColumnSortMode.NotSortable,
                    });
                }
            }

            if (_schedule == null) { return; }

            // 周六周日作为一组：都没有课就一起藏起来，任一天有课就一起显示。
            // 放在这里而不是建列的时候，是为了每次"更新课表"都能重新判定。
            var showWeekend = HasWeekendCourses(_schedule);
            if (_grid.Columns.Count >= 8)
            {
                _grid.Columns[6].Visible = showWeekend;
                _grid.Columns[7].Visible = showWeekend;
            }

            ApplyContentWidth();

            var periods = new List<Period>(_schedule.Periods);
            periods.Sort((a, b) => a.Index.CompareTo(b.Index));
            var splitAfter = _config.MorningSplitAfterPeriod;

            for (var i = 0; i < periods.Count; i++)
            {
                var period = periods[i];
                var cells = new object[8];
                var adjusted = new bool[8];
                cells[0] = string.Format("第 {0} 节{1}{2} – {3}", period.Index, Environment.NewLine, period.Start, period.End);

                for (var day = 1; day <= 7; day++)
                {
                    var entry = _schedule.FindCourse(day, period.Index);
                    cells[day] = entry == null ? string.Empty : entry.Course ?? string.Empty;
                    adjusted[day] = entry != null && entry.Adjusted;
                }

                var rowIndex = _grid.Rows.Add(cells);
                _rowByPeriod[period.Index] = rowIndex;
                _grid.Rows[rowIndex].Cells[0].Style.ForeColor = Color.FromArgb(110, 116, 122);
                _grid.Rows[rowIndex].Cells[0].Style.Font = UiFont.Body;

                // 调课调过来的格子用蓝字
                for (var day = 1; day <= 7; day++)
                {
                    if (adjusted[day])
                    {
                        _grid.Rows[rowIndex].Cells[day].Style.ForeColor = AdjustedCourseColor;
                    }
                }

                // 上午最后一节后面插一条色带，把上下午分开
                if (splitAfter > 0 && period.Index == splitAfter && i < periods.Count - 1)
                {
                    _splitRowIndex = AddSplitBand();
                }
            }

            _grid.ClearSelection();
        }

        /// <summary>周六或周日只要有课，两列就都显示；都没有就一起隐藏。拿不到课表时不擅自藏列。</summary>
        private static bool HasWeekendCourses(WeekSchedule schedule)
        {
            if (schedule == null) { return true; }

            foreach (var entry in schedule.Entries)
            {
                if (entry.Weekday >= 6 && !string.IsNullOrWhiteSpace(entry.Course)) { return true; }
            }

            return false;
        }

        /// <summary>上下午之间的分隔色带：一条横贯整表的琥珀黄粗线。</summary>
        private int AddSplitBand()
        {
            var index = _grid.Rows.Add();
            var row = _grid.Rows[index];
            row.Height = S(SplitBandHeight);
            row.DefaultCellStyle.BackColor = SplitBandColor;
            row.DefaultCellStyle.SelectionBackColor = SplitBandColor;

            foreach (DataGridViewCell cell in row.Cells)
            {
                cell.Value = string.Empty;
            }

            return index;
        }

        /// <summary>
        /// 按"选定的班级 + 作息方案"重建当前显示的周课表。
        /// 数据源给的是全校课表，这里挑出本机要用的那一个班；
        /// 时刻不用数据里的，用方案表（见 GradeSchemes）。
        /// 选定的班级在数据里找不到时退回第一个班（不弹窗，避免后台刷新时打断），并记日志。
        /// </summary>
        private void BuildScheduleView()
        {
            _schedule = null;
            _className = string.Empty;

            if (_school == null || _school.Classes.Count == 0) { return; }

            var target = _school.FindClass(_config.SelectedClassId) ?? _school.Classes[0];
            if (!string.Equals(target.Id, _config.SelectedClassId, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(_config.SelectedClassId))
                {
                    Logger.Warn("配置的班级 " + _config.SelectedClassId + " 不在课表里，暂用 " + target.DisplayName);
                }

                _config.SelectedClassId = target.Id;
            }

            // 作息方案由班级决定：一二年级用低年级表，三到六年级用高年级表。
            // 设置里那个"时段方案"选项已经去掉，免得出现"班级是三年级、方案却选了低年级"这种自相矛盾的配置。
            // 班名认不出年级时（学校改名等）保持原样，不乱猜。
            var scheme = GradeSchemes.SchemeForClass(target.DisplayName) ?? _config.GradeScheme;
            if (!string.Equals(scheme, _config.GradeScheme, StringComparison.Ordinal))
            {
                Logger.Info("作息方案随班级自动切换：" + _config.GradeScheme + " → " + scheme);
                _config.GradeScheme = scheme;
                _config.Save();
            }

            var periods = GradeSchemes.Create(scheme);
            _schedule = new WeekSchedule
            {
                GeneratedAt = _school.GeneratedAt,
                Periods = periods,
                Entries = target.Entries,
            };

            _className = target.DisplayName;
            if (_lblClass != null) { _lblClass.Text = "班级：" + _className; }

            Logger.Info(string.Format(
                "当前班级：{0}；作息方案：{1}（{2} 节）",
                _className,
                _config.GradeScheme,
                periods.Count));
        }

        /// <summary>首次运行：让用户选一次班级，选完存进配置，以后不再问。</summary>
        private void EnsureClassChosen()
        {
            if (!_needsClassChoice || _classChooserOpen) { return; }
            if (!Visible) { return; }   // 窗口还没显示，等 OnShown 再弹
            if (_school == null || _school.Classes.Count == 0) { return; }

            _classChooserOpen = true;
            try
            {
                string picked;
                using (var picker = new ClassPickerForm(_school.Classes, _config.SelectedClassId, false))
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) { return; }
                    picked = picker.SelectedClassId;
                }

                _config.SelectedClassId = picked;
                _config.Save();
                _needsClassChoice = false;

                BuildScheduleView();
                BuildRows();
                _scheduler.UpdateSchedule(_schedule);
                UpdateNextReminderLabel(AppClock.Now);
                UpdateHighlights(AppClock.Now);
                Logger.Info("已完成首次班级选择：" + _className);
            }
            finally
            {
                _classChooserOpen = false;
            }
        }

        /// <summary>
        /// 窗口宽度跟着"可见的天数"走：周末两列藏起来时窗口一起变窄，
        /// 而不是把周一到周五拉宽去填空出来的位置。
        /// 只在可见天数变化时改，避免跟用户手动拖过的尺寸打架。
        /// </summary>
        private void ApplyContentWidth()
        {
            var days = 0;
            for (var i = 1; i < _grid.Columns.Count; i++)
            {
                if (_grid.Columns[i].Visible) { days++; }
            }

            if (days <= 0) { return; }

            var wanted = S(GridLabelColumnWidth) + days * S(GridDayColumnWidth);
            var working = Screen.FromControl(this).WorkingArea.Width;
            wanted = Math.Min(wanted, working - S(80));

            if (wanted == _appliedWidth) { return; }

            _appliedWidth = wanted;
            ClientSize = new Size(wanted, ClientSize.Height);
        }

        /// <summary>
        /// 首次加载课表后，把窗口高度收到"刚好装下课表"：
        /// 工具栏 + 表头 + 行数 × 行高 + 状态栏，不再留一大片空白。
        /// 之后窗口大小由用户自己控制，不再自动改。
        /// </summary>
        private void FitWindowToContent()
        {
            var periodRows = _grid.Rows.Count - (_splitRowIndex >= 0 ? 1 : 0);
            if (periodRows <= 0) { return; }

            var statusHeight = Math.Max(_statusBar.Height, S(22)) + Math.Max(_statusBarBottom.Height, S(22));
            var bandHeight = _splitRowIndex >= 0 ? S(SplitBandHeight) : 0;
            var wanted = S(ToolbarHeight) + S(GridColumnHeaderHeight) +
                         periodRows * S(GridPreferredRowHeight) + bandHeight + statusHeight;

            var working = Screen.FromControl(this).WorkingArea.Height;
            var maxHeight = Math.Max(working - S(60), MinimumSize.Height);
            ClientSize = new Size(ClientSize.Width, Math.Min(wanted, maxHeight));
        }

        /// <summary>把多余的高度摊到每一行上，让课表填满窗口。</summary>
        private void FillRows()
        {
            if (_fillingRows) { return; }

            var periodRows = _grid.Rows.Count - (_splitRowIndex >= 0 ? 1 : 0);
            if (periodRows <= 0) { return; }

            var bandHeight = _splitRowIndex >= 0 ? S(SplitBandHeight) : 0;
            var available = _grid.ClientSize.Height - _grid.ColumnHeadersHeight - bandHeight;
            if (available <= 0) { return; }

            var height = Math.Max(S(GridMinRowHeight), available / periodRows);
            if (_grid.Rows[0].Height == height) { return; }

            _fillingRows = true;
            try
            {
                _grid.RowTemplate.Height = height;
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.Index == _splitRowIndex) { continue; }
                    row.Height = height;
                }
            }
            finally
            {
                _fillingRows = false;
            }
        }

        /// <summary>正在上的那节标红，下一个提醒点那节标绿。</summary>
        private void UpdateHighlights(DateTime now)
        {
            DataGridViewCell current = null;
            DataGridViewCell next = null;

            if (_schedule != null)
            {
                var weekday = ReminderPlanner.ToWeekday(now.DayOfWeek);
                var timeOfDay = now.TimeOfDay;

                foreach (var period in _schedule.Periods)
                {
                    if (period.EndTime <= period.StartTime) { continue; }
                    if (timeOfDay < period.StartTime || timeOfDay >= period.EndTime) { continue; }

                    var entry = _schedule.FindCourse(weekday, period.Index);
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.Course))
                    {
                        current = CellOf(weekday, period.Index);
                    }

                    break;
                }

                var point = _scheduler.NextReminder;
                if (point != null)
                {
                    next = CellOf(point.Weekday, point.PeriodIndex);
                }
            }

            if (!ReferenceEquals(_currentCell, current))
            {
                ResetCell(_currentCell);
                HighlightCell(current, Color.FromArgb(198, 40, 40), Color.White);
                _currentCell = current;
            }

            if (!ReferenceEquals(_nextCell, next))
            {
                ResetCell(_nextCell);
                HighlightCell(next, Color.FromArgb(46, 139, 87), Color.White);
                _nextCell = next;
            }
        }

        private DataGridViewCell CellOf(int weekday, int periodIndex)
        {
            if (weekday < 1 || weekday > 7) { return null; }

            int rowIndex;
            if (!_rowByPeriod.TryGetValue(periodIndex, out rowIndex)) { return null; }
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) { return null; }

            return _grid.Rows[rowIndex].Cells[weekday];
        }

        private void HighlightCell(DataGridViewCell cell, Color backColor, Color foreColor)
        {
            if (cell == null) { return; }
            cell.Style.BackColor = backColor;
            cell.Style.ForeColor = foreColor;
            cell.Style.SelectionBackColor = backColor;
            cell.Style.Font = UiFont.CourseBold;
        }

        private void ResetCell(DataGridViewCell cell)
        {
            if (cell == null) { return; }
            cell.Style.BackColor = Color.Empty;
            // 调课的格子本身就是蓝字，高亮撤掉时要恢复成蓝的，不能一律清空
            cell.Style.ForeColor = IsAdjustedCell(cell) ? AdjustedCourseColor : Color.Empty;
            cell.Style.SelectionBackColor = Color.Empty;
            cell.Style.Font = UiFont.Course;
        }

        /// <summary>这个格子里的课是不是调课调过来的（高亮撤掉后要靠它把蓝字恢复回来）。</summary>
        private bool IsAdjustedCell(DataGridViewCell cell)
        {
            if (_schedule == null) { return false; }

            var weekday = cell.ColumnIndex;
            if (weekday < 1 || weekday > 7) { return false; }

            var period = PeriodOfRow(cell.RowIndex);
            if (period <= 0) { return false; }

            var entry = _schedule.FindCourse(weekday, period);
            return entry != null && entry.Adjusted;
        }

        /// <summary>行号 → 节次。_rowByPeriod 是反过来的映射，最多 8 项，直接扫。</summary>
        private int PeriodOfRow(int rowIndex)
        {
            foreach (var pair in _rowByPeriod)
            {
                if (pair.Value == rowIndex) { return pair.Key; }
            }

            return 0;
        }

        // ---------- 播报 ----------

        private void AnnounceNextOrSample()
        {
            var point = _scheduler.NextReminder;
            StartAnnouncement(point == null ? "语文" : point.Course);
        }

        private void OnReminderDue(object sender, ReminderEventArgs e)
        {
            if (e == null || e.Point == null) { return; }
            var course = e.Point.Course;
            Logger.Info("到点播报：" + e.Point.Describe());

            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((Action)(() => StartAnnouncement(course)));
                }
            }
            catch (Exception ex)
            {
                Logger.Error("派发播报失败", ex);
            }
        }

        private void StartAnnouncement(string course)
        {
            if (_muted)
            {
                Logger.Info("已静音，跳过播报：" + course);
                SetLastAnnounce(AppClock.Now.ToString("HH:mm:ss") + " " + course + "（已静音，未播放）");
                return;
            }

            if (_announcing)
            {
                Logger.Warn("上一次播报还没结束，忽略这次：" + course);
                return;
            }

            _announcing = true;
            var name = course;

            Task.Run(() =>
                        {
                            try
                            {
                                var result = _announcer.Announce(name, _config);
                                Logger.Info(result.Message);
                                SetLastAnnounce(AppClock.Now.ToString("HH:mm:ss") + " " + name + (result.Ok ? "（成功）" : "（失败）"));
                            }
                            catch (Exception ex)
                            {
                                Logger.Error("播报线程异常", ex);
                                SetLastAnnounce("播报异常：" + ex.Message);
                            }
                            finally
                            {
                                _announcing = false;
                            }
                        });
        }

        private void SetLastAnnounce(string text)
        {
            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((Action)(() =>
                    {
                        _lblLastAnnounce.Text = "播报：" + text;
                        // 窗口窄的时候这一项会被截断，悬停能看到全文
                        _lblLastAnnounce.ToolTipText = text;
                    }));
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("刷新播报状态失败：" + ex.Message);
            }
        }

        // ---------- 状态 ----------

        private void UpdateNextReminderLabel(DateTime now)
        {
            var point = _scheduler.NextReminder;
            if (point == null)
            {
                _lblNext.Text = "下一个提醒点：无";
                return;
            }

            _lblNext.Text = string.Format(
                "下一个提醒点：{0:MM-dd HH:mm}（{1}后） 第{2}节 {3}",
                point.Time,
                FormatSpan(point.Time - now),
                point.PeriodIndex,
                point.Course);
        }

        private void UpdateAudioStatus()
        {
            _lastAudioCheck = AppClock.Now;
            _lblAudio.Text = "音量：" + AudioController.DescribeVolumeShort();
            _lblAudio.ToolTipText = AudioController.DescribeDefaultDevice();
        }

        private static string FormatSpan(TimeSpan span)
        {
            if (span < TimeSpan.Zero) { span = TimeSpan.Zero; }
            if (span.TotalHours >= 1) { return string.Format("{0} 小时 {1} 分", (int)span.TotalHours, span.Minutes); }
            if (span.TotalMinutes >= 1) { return string.Format("{0} 分 {1} 秒", (int)span.TotalMinutes, span.Seconds); }
            return string.Format("{0} 秒", (int)span.TotalSeconds);
        }

        /// <summary>提前提醒档位 → 日志里好看的样子（"7、5 分钟"）；一档都没有时说明白。</summary>
        private static string DescribeAheadList(IList<int> list)
        {
            if (list == null || list.Count == 0) { return "（一档都没设，不打铃）"; }

            var parts = new List<string>();
            foreach (var value in list) { parts.Add(value.ToString()); }
            return string.Join("、", parts.ToArray()) + " 分钟";
        }

        // ---------- 托盘 / 自启 ----------

        /// <summary>
        /// 配置是自启状态的来源：默认开启，启动时确保注册表里的登记与配置一致。
        /// 顺带修正"程序被挪到别的目录后注册表仍指向旧路径"的情况。
        /// </summary>
        private void ApplyAutoStartFromConfig()
        {
            var registered = AutoStart.GetRegisteredCommand();

            if (_config.AutoStart)
            {
                if (!string.Equals(registered, AutoStart.BuildCommand(), StringComparison.OrdinalIgnoreCase))
                {
                    if (!AutoStart.SetEnabled(true))
                    {
                        // 写不进去（权限、组策略）就以实际状态为准，别让界面说谎
                        _config.AutoStart = AutoStart.IsEnabled();
                    }
                }
            }
            else if (registered != null)
            {
                AutoStart.SetEnabled(false);
            }

        }

        /// <summary>
        /// 按配置登记 / 注销看门狗计划任务（见 App/Watchdog.cs）。
        /// **放后台线程**：要起 schtasks.exe（几十到几百毫秒），别卡住启动。
        /// </summary>
        private void ApplyWatchdogFromConfig()
        {
            var config = _config;
            Task.Run(() =>
            {
                try
                {
                    Watchdog.Apply(config);
                }
                catch (Exception ex)
                {
                    Logger.Warn("看门狗：登记任务时出错：" + ex.Message);
                }
            });
        }

        /// <summary>打开设置对话框，确认后立即生效并落盘。</summary>
        private void OpenSettings()
        {
            using (var dialog = new SettingsForm(
                _config.AutoStart,
                _config.FloatingEnabled,
                _config.WatchdogEnabled,
                _school == null ? null : _school.Classes,
                _config.SelectedClassId,
                _config.GradeScheme,
                _config.RemindAheadList,
                _config.AnnounceVolumePercent,
                _config.AnnounceRepeatCount,
                _config.RefreshIntervalMinutes))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }

                var schemeChanged = !string.Equals(_config.GradeScheme, dialog.GradeScheme, StringComparison.Ordinal);
                var classChanged = !string.Equals(_config.SelectedClassId, dialog.SelectedClassId, StringComparison.Ordinal);

                _config.SelectedClassId = dialog.SelectedClassId;
                _config.RemindAheadList = dialog.RemindAheadList;
                _config.AnnounceVolumePercent = dialog.AnnounceVolumePercent;
                _config.AnnounceRepeatCount = dialog.AnnounceRepeatCount;
                _config.RefreshIntervalMinutes = dialog.RefreshIntervalMinutes;
                _config.GradeScheme = dialog.GradeScheme;
                _config.AutoStart = dialog.AutoStartEnabled;
                var floatingChanged = _config.FloatingEnabled != dialog.FloatingEnabled;
                _config.FloatingEnabled = dialog.FloatingEnabled;
                var watchdogChanged = _config.WatchdogEnabled != dialog.WatchdogEnabled;
                _config.WatchdogEnabled = dialog.WatchdogEnabled;
                _config.Save();

                _scheduler.UpdateAheadList(_config.RemindAheadList);
                _lastRefresh = AppClock.Now; // 刚改完刷新间隔，别马上又刷一次

                // 换了班级或作息方案：课表内容/时刻都变了，重建界面与打铃点
                if ((classChanged || schemeChanged) && _school != null)
                {
                    BuildScheduleView();
                    BuildRows();
                    _scheduler.UpdateSchedule(_schedule);
                    FillRows();
                }

                if (_config.AutoStart != AutoStart.IsEnabled() && !AutoStart.SetEnabled(_config.AutoStart))
                {
                    MessageBox.Show(this, "设置开机自启失败，详情见日志。", "智能课表打铃", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _config.AutoStart = AutoStart.IsEnabled();
                    _config.Save();
                }

                Logger.Info(string.Format(
                    "设置已更新：自启={0}，悬浮窗={1}，看门狗={2}，方案={3}，提前={4}，播报音量={5}%，播报次数={6} 遍，刷新间隔={7} 分钟",
                    _config.AutoStart,
                    _config.FloatingEnabled,
                    _config.WatchdogEnabled,
                    _config.GradeScheme,
                    DescribeAheadList(_config.RemindAheadList),
                    _config.AnnounceVolumePercent,
                    _config.AnnounceRepeatCount,
                    _config.RefreshIntervalMinutes));

                if (floatingChanged) { ApplyFloatingFromConfig(); }
                if (watchdogChanged) { ApplyWatchdogFromConfig(); }

                UpdateNextReminderLabel(AppClock.Now);
                UpdateHighlights(AppClock.Now);
            }
        }

        /// <summary>
        /// 静音开关：开启后不再出声，打铃点照常标记为已触发（不会因为解静音又补响）。
        /// 只在本次运行有效，重启后自动恢复不静音——避免有人静音后忘了，铃一直不响还没人发现。
        /// </summary>
        private void ToggleMute()
        {
            _muted = !_muted;
            ApplyMuteState();
            Logger.Info(_muted ? "已开启静音：播报不再出声" : "已取消静音：恢复正常播报");
        }

        private void ApplyMuteState()
        {
            _btnMute.Text = _muted ? "已静音" : "静音";
            _btnMute.BackColor = _muted ? Color.FromArgb(206, 66, 62) : Color.FromArgb(90, 100, 112);
            _btnMute.FlatAppearance.MouseOverBackColor = _muted
                ? Color.FromArgb(222, 88, 84)
                : Color.FromArgb(112, 122, 136);
            _btnMute.FlatAppearance.MouseDownBackColor = _muted
                ? Color.FromArgb(170, 50, 47)
                : Color.FromArgb(68, 76, 86);

            if (_muteMenuItem != null) { _muteMenuItem.Checked = _muted; }
            if (_tray != null)
            {
                _tray.Text = _muted ? "智能课表打铃 · 已静音" : "智能课表打铃";
            }
        }

        /// <summary>
        /// 收进托盘：窗口隐藏，程序继续跑，打铃不受影响。
        /// 每次运行只在第一次弹一条托盘气泡，免得用户以为程序被关掉了。
        /// </summary>
        private void HideToTray()
        {
            Hide();

            if (_trayHintShown) { return; }
            _trayHintShown = true;

            try
            {
                _tray.ShowBalloonTip(
                    4000,
                    "智能课表打铃",
                    "程序仍在后台运行，双击托盘图标可以重新打开。",
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Logger.Warn("托盘气泡提示失败：" + ex.Message);
            }
        }

        /// <summary>把主窗口显示出来并提到前面（托盘双击、第二个实例的唤出信号、悬浮窗双击都用它）。</summary>
        internal void ShowWindowNow()
        {
            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
            BringToFront();
        }

        private void ExitApplication()
        {
            _exiting = true;
            // 使用者主动退出：先给看门狗留个"别拉我"的标记（下次正常启动会自动清掉）
            Watchdog.Pause("从托盘菜单退出");
            if (_tray != null) { _tray.Visible = false; }
            Close();
        }

        private void StartShowSignalListener()
        {
            var thread = new Thread(() =>
            {
                while (!_exiting)
                {
                    try
                    {
                        if (!_showSignal.WaitOne(500)) { continue; }
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    if (_exiting || !IsHandleCreated) { continue; }

                    try
                    {
                        BeginInvoke((Action)ShowWindowNow);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }
    }
}
