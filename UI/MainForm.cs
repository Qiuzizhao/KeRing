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

        private readonly Font _plainFont = new Font("Microsoft YaHei", 10.5F);
        private readonly Font _boldFont = new Font("Microsoft YaHei", 10.5F, FontStyle.Bold);
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

        private DataGridView _grid;
        private Button _btnRefresh;
        private Button _btnTest;
        private Button _btnSettings;
        private Button _btnMinimize;
        private Button _btnMute;
        private ToolStripMenuItem _muteMenuItem;
        private StatusStrip _statusBar;
        private ToolStripStatusLabel _lblClock;
        private ToolStripStatusLabel _lblClass;
        private ToolStripStatusLabel _lblNext;
        private ToolStripStatusLabel _lblData;
        private ToolStripStatusLabel _lblAudio;
        private ToolStripStatusLabel _lblLastAnnounce;
        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _uiTimer;

        private WeekSchedule _schedule;
        private SchoolSchedule _school;
        private string _className = string.Empty;
        private bool _needsClassChoice;
        private bool _classChooserOpen;
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
            _scheduler = new Scheduler(config.RemindAheadMinutes);
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
            Font = new Font("Microsoft YaHei", 9F);
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
            _btnSettings = MakeButton("设置", 268,
                Color.FromArgb(90, 100, 112), Color.FromArgb(112, 122, 136), Color.FromArgb(68, 76, 86));
            _btnMinimize = MakeButton("最小化", 396,
                Color.FromArgb(90, 100, 112), Color.FromArgb(112, 122, 136), Color.FromArgb(68, 76, 86));
            _btnMute = MakeButton("静音", 524,
                Color.FromArgb(90, 100, 112), Color.FromArgb(112, 122, 136), Color.FromArgb(68, 76, 86));
            _btnRefresh.Click += (sender, args) => RefreshSchedule();
            _btnTest.Click += (sender, args) => AnnounceNextOrSample();
            _btnSettings.Click += (sender, args) => OpenSettings();
            _btnMinimize.Click += (sender, args) => HideToTray();
            _btnMute.Click += (sender, args) => ToggleMute();
            toolbar.Controls.Add(_btnRefresh);
            toolbar.Controls.Add(_btnTest);
            toolbar.Controls.Add(_btnSettings);
            toolbar.Controls.Add(_btnMinimize);
            toolbar.Controls.Add(_btnMute);

            BuildGrid();
            BuildStatusBar();
            BuildTray();

            Controls.Add(_grid);
            Controls.Add(toolbar);
            Controls.Add(_statusBar);

            _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _uiTimer.Tick += OnUiTick;
        }

        /// <summary>带配色的扁平按钮：常态、悬停、按下三种颜色，白字加粗。</summary>
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
                Font = new Font("Microsoft YaHei", 10.5F, FontStyle.Bold),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = pressed;
            return button;
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
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            _grid.DefaultCellStyle.Font = _plainFont;
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

        private void BuildStatusBar()
        {
            _statusBar = new StatusStrip
            {
                SizingGrip = false,
                ShowItemToolTips = true,
                Font = new Font("Microsoft YaHei", 9F),
            };
            _lblClock = new ToolStripStatusLabel
            {
                Text = "--:--:--",
                Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold),
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
            _lblLastAnnounce = new ToolStripStatusLabel { Text = "播报：--", AutoSize = true };

            _statusBar.Items.AddRange(new ToolStripItem[]
            {
                _lblClock, _lblClass, _lblNext, _lblData, _lblAudio, _lblLastAnnounce,
            });
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
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (sender, args) => ExitApplication());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (sender, args) => ShowWindowNow();
        }

        // ---------- 生命周期 ----------

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            ApplyAutoStartFromConfig();
            ApplyMuteState();

            // 配置里没有班级 = 第一次运行，稍后取到课表要弹一次选择框
            _needsClassChoice = string.IsNullOrWhiteSpace(_config.SelectedClassId);

            Logger.Info("界面已加载，课表来源：" + _config.ScheduleFilePath);
            Logger.Info(string.Format(
                "窗口客户区 {0} x {1}，设备 DPI {2}，界面缩放 {3:P0}",
                ClientSize.Width,
                ClientSize.Height,
                DeviceDpi,
                _scale));
            RefreshSchedule();
            _scheduler.Start();
            UpdateAudioStatus();
            UpdateNextReminderLabel(DateTime.Now);
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

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }

            base.OnFormClosed(e);
        }

        private void OnUiTick(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            _lblClock.Text = now.ToString("HH:mm:ss");

            if ((now - _lastRefresh).TotalMinutes >= _config.RefreshIntervalMinutes)
            {
                RefreshSchedule();
            }

            if ((now - _lastAudioCheck).TotalSeconds >= 30)
            {
                UpdateAudioStatus();
            }

            UpdateNextReminderLabel(now);
            UpdateHighlights(now);
        }

        // ---------- 课表 ----------

        private void RefreshSchedule()
        {
            _lastRefresh = DateTime.Now;
            var ok = false;
            try
            {
                var source = new LocalFileScheduleSource(_config.ScheduleFilePath);
                var result = source.Load();
                _dataStatus = result.Message;

                if (result.Success)
                {
                    ok = true;
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
                    UpdateHighlights(DateTime.Now);
                    Logger.Info("课表已更新：" + result.Message);
                }
                else
                {
                    Logger.Warn("课表加载失败：" + result.Message);
                }
            }
            catch (Exception ex)
            {
                _dataStatus = "更新失败：" + ex.Message;
                Logger.Error("更新课表异常", ex);
            }

            // 状态栏只放短状态，完整信息放悬停提示，避免长文本挤掉"下一个提醒点"
            _lblData.Text = "数据：" + (ok ? "正常 " : "失败 ") + DateTime.Now.ToString("HH:mm");
            _lblData.ToolTipText = _dataStatus;

            // 首次运行（配置里还没班级）时在这里弹一次"选择班级"；
            // 数据第一次没取到也没关系，后续刷新到数据后会补上
            if (_needsClassChoice) { EnsureClassChosen(); }
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
                cells[0] = string.Format("第 {0} 节{1}{2} – {3}", period.Index, Environment.NewLine, period.Start, period.End);

                for (var day = 1; day <= 7; day++)
                {
                    var entry = _schedule.FindCourse(day, period.Index);
                    cells[day] = entry == null ? string.Empty : entry.Course ?? string.Empty;
                }

                var rowIndex = _grid.Rows.Add(cells);
                _rowByPeriod[period.Index] = rowIndex;
                _grid.Rows[rowIndex].Cells[0].Style.ForeColor = Color.FromArgb(110, 116, 122);
                _grid.Rows[rowIndex].Cells[0].Style.Font = new Font("Microsoft YaHei", 9F);

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

            var periods = GradeSchemes.Create(_config.GradeScheme);
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
                UpdateNextReminderLabel(DateTime.Now);
                UpdateHighlights(DateTime.Now);
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

            var statusHeight = Math.Max(_statusBar.Height, S(22));
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
            cell.Style.Font = _boldFont;
        }

        private void ResetCell(DataGridViewCell cell)
        {
            if (cell == null) { return; }
            cell.Style.BackColor = Color.Empty;
            cell.Style.ForeColor = Color.Empty;
            cell.Style.SelectionBackColor = Color.Empty;
            cell.Style.Font = _plainFont;
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
                SetLastAnnounce(DateTime.Now.ToString("HH:mm:ss") + " " + course + "（已静音，未播放）");
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
                                SetLastAnnounce(DateTime.Now.ToString("HH:mm:ss") + " " + name + (result.Ok ? "（成功）" : "（失败）"));
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
                    BeginInvoke((Action)(() => { _lblLastAnnounce.Text = "播报：" + text; }));
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
            _lastAudioCheck = DateTime.Now;
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

        /// <summary>打开设置对话框，确认后立即生效并落盘。</summary>
        private void OpenSettings()
        {
            using (var dialog = new SettingsForm(
                _config.AutoStart,
                _school == null ? null : _school.Classes,
                _config.SelectedClassId,
                _config.GradeScheme,
                _config.RemindAheadMinutes,
                _config.AnnounceVolumePercent,
                _config.RefreshIntervalMinutes))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }

                var schemeChanged = !string.Equals(_config.GradeScheme, dialog.GradeScheme, StringComparison.Ordinal);
                var classChanged = !string.Equals(_config.SelectedClassId, dialog.SelectedClassId, StringComparison.Ordinal);

                _config.SelectedClassId = dialog.SelectedClassId;
                _config.RemindAheadMinutes = dialog.RemindAheadMinutes;
                _config.AnnounceVolumePercent = dialog.AnnounceVolumePercent;
                _config.RefreshIntervalMinutes = dialog.RefreshIntervalMinutes;
                _config.GradeScheme = dialog.GradeScheme;
                _config.AutoStart = dialog.AutoStartEnabled;
                _config.Save();

                _scheduler.UpdateAheadMinutes(_config.RemindAheadMinutes);
                _lastRefresh = DateTime.Now; // 刚改完刷新间隔，别马上又刷一次

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
                    "设置已更新：自启={0}，方案={1}，提前={2} 分钟，播报音量={3}%，刷新间隔={4} 分钟",
                    _config.AutoStart,
                    _config.GradeScheme,
                    _config.RemindAheadMinutes,
                    _config.AnnounceVolumePercent,
                    _config.RefreshIntervalMinutes));

                UpdateNextReminderLabel(DateTime.Now);
                UpdateHighlights(DateTime.Now);
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

        private void ShowWindowNow()
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
