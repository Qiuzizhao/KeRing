using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using KeRing.App;
using KeRing.App.Schedule;

namespace KeRing.UI
{
    /// <summary>
    /// 设置对话框。数值一律用触屏友好的大按钮步进（见 TouchStepper），
    /// 不用 NumericUpDown 那种 16×16 的上下箭头——一体机大屏上根本点不准。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private const int LabelX = 24;
        private const int StepperX = 112;
        private const int UnitX = 356;
        private const int StepperTotalWidth = 232;
        /// <summary>这一行内容的右边界（客户区 460 减去右边距 24），跟确定/取消那排对齐。</summary>
        private const int ContentRight = 436;
        private const int RowHeight = 64;
        /// <summary>"提前提醒"那一行的 y（前面是"班级 / 时段方案"两行）。</summary>
        private const int AheadRowY = 60 + RowHeight * 2;
        /// <summary>开关排**两行**，所以这一行占 120 像素而不是 64（使用方 2026-09-18 要求分两行）。</summary>
        private const int AheadBlockHeight = 120;
        /// <summary>开关每行放几个：5 档 = 3 + 2，按钮能做到 84 像素宽，比挤在一行里舒服得多。</summary>
        private const int AheadColumns = 3;
        /// <summary>"提前提醒"下面那一行（播报音量）的 y：跳过两行的开关，再留 8 像素。</summary>
        private const int AfterAheadY = AheadRowY + AheadBlockHeight + 8;
        /// <summary>按钮的纵坐标：排在所有设置行下面（改行数时记得一起挪）。</summary>
        private const int ButtonY = 528;

        private readonly IList<SchoolClass> _classes;
        private readonly CheckBox _chkAutoStart;
        private readonly CheckBox _chkFloating;
        private readonly Button _btnClass;
        private readonly Label _lblScheme;
        private readonly TouchToggles _togglesAhead;
        private readonly TouchStepper _stepperVolume;
        private readonly TouchStepper _stepperRepeat;
        private readonly TouchStepper _stepperRefresh;

        private string _classId;

        public bool AutoStartEnabled { get; private set; }
        public bool FloatingEnabled { get; private set; }
        public string SelectedClassId { get; private set; }
        public string GradeScheme { get; private set; }
        /// <summary>选中的提前提醒档位（分钟，大的在前）；空列表 = 一档都不打铃。</summary>
        public List<int> RemindAheadList { get; private set; }
        public int AnnounceVolumePercent { get; private set; }
        public int AnnounceRepeatCount { get; private set; }
        public int RefreshIntervalMinutes { get; private set; }

        public SettingsForm(
            bool autoStart,
            bool floatingEnabled,
            IList<SchoolClass> classes,
            string classId,
            string gradeScheme,
            IList<int> aheadList,
            int volumePercent,
            int repeatCount,
            int refreshMinutes)
        {
            _classes = classes;
            _classId = classId;

            AutoStartEnabled = autoStart;
            FloatingEnabled = floatingEnabled;
            SelectedClassId = classId;
            GradeScheme = GradeSchemes.Normalize(gradeScheme);
            RemindAheadList = new List<int>(aheadList ?? new List<int>());
            AnnounceVolumePercent = volumePercent;
            AnnounceRepeatCount = repeatCount;
            RefreshIntervalMinutes = refreshMinutes;

            AutoScaleMode = AutoScaleMode.None;
            Text = "设置";
            Font = UiFont.Body;
            // 比原来多一行"播报次数"、提前提醒又排成两行开关，所以对话框比最初高了两行
            ClientSize = UiScale.S(460, 592);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            _chkAutoStart = new CheckBox
            {
                Text = "开机自启（登录后自动运行）",
                AutoSize = true,
                Location = UiScale.P(LabelX, 20),
                Checked = autoStart,
            };

            // "显示悬浮窗"跟开机自启并排（这一行放得下两个复选框）
            _chkFloating = new CheckBox
            {
                Text = "显示悬浮窗",
                AutoSize = true,
                Location = UiScale.P(250, 20),
                Checked = floatingEnabled,
            };

            _btnClass = new Button
            {
                Text = DescribeClass(classId),
                Location = UiScale.P(StepperX, 60),
                Size = UiScale.S(StepperTotalWidth, 56),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(90, 100, 112),
                ForeColor = Color.White,
                Font = UiFont.DialogButton,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            _btnClass.FlatAppearance.BorderSize = 0;
            _btnClass.FlatAppearance.MouseOverBackColor = Color.FromArgb(112, 122, 136);
            _btnClass.FlatAppearance.MouseDownBackColor = Color.FromArgb(68, 76, 86);
            _btnClass.Click += (sender, args) => ChooseClass();

            // 作息方案不再让用户选：它由班级决定（一二年级低年级、三到六年级高年级）。
            // 这一行保留成只读显示，让管理员看得见这台机器现在用的到底是哪套时间。
            _lblScheme = new Label
            {
                AutoSize = true,
                Font = UiFont.DialogButton,
                ForeColor = Color.FromArgb(60, 64, 68),
                Location = UiScale.P(StepperX, 60 + RowHeight + 16),
            };

            // 提前提醒改成"快捷开关"（2026-09-18 使用方定的）：想提前 10 分钟和 7 分钟各响一次，
            // 就把 10 和 7 都点亮；一个都不点 = 不打铃。
            //
            // **单位写在行标题里、按钮上只留数字**："10 分"在 12 磅粗体下要 51 像素，而 5 个按钮
            // 排一排每个才 58 像素（文字区约 48），WinForms 会把它折成两行（使用方 2026-09-18 反馈）。
            // 只写数字要 24 像素，档位再多也放得下。
            //
            // **开关的起点按标签宽度算，不写死**（中文标签 10 磅下 135 像素，写死就会压到按钮上，
            // 使用方 2026-09-18 连续反馈了两次）。但**不能读 label.Right**：Label 在没加进窗体之前
            // Font 还没继承、Width 还是默认的 100（实测少 29 像素，于是又压上了），
            // 所以这里自己用 TextRenderer 量。
            const string AheadLabelText = "提前提醒（分钟）：";
            // 标签在两行开关中间垂直居中：MakeLabel 内部还会 +17，所以这里传 AheadRowY + 32
            var aheadLabel = MakeLabel(AheadLabelText, LabelX, AheadRowY + 32);
            var aheadLabelWidth = TextRenderer.MeasureText(
                AheadLabelText,
                UiFont.Body,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            var aheadX = UiScale.S(LabelX) + aheadLabelWidth + UiScale.S(9);
            _togglesAhead = new TouchToggles(
                BuildAheadValues(aheadList),
                aheadList,
                string.Empty,   // 单位在行标题里，按钮上只写数字
                UiScale.S(ContentRight) - aheadX,
                AheadColumns)
            {
                Location = new Point(aheadX, UiScale.S(AheadRowY)),
            };
            _stepperVolume = MakeStepper(0, 100, 5, volumePercent, AfterAheadY);
            _stepperRepeat = MakeStepper(1, 10, 1, repeatCount, AfterAheadY + RowHeight);
            _stepperRefresh = MakeStepper(1, 1440, 5, refreshMinutes, AfterAheadY + RowHeight * 2);

            var ok = DialogButtons.Primary("确定", 232, ButtonY, 116, 44);
            ok.DialogResult = DialogResult.OK;

            var cancel = DialogButtons.Secondary("取消", 356, ButtonY, 88, 44);
            cancel.DialogResult = DialogResult.Cancel;

            ok.Click += (sender, args) => Collect();

            Controls.Add(_chkAutoStart);
            Controls.Add(_chkFloating);
            Controls.Add(MakeLabel("班级：", LabelX, 60));
            Controls.Add(_btnClass);
            Controls.Add(MakeLabel("时段方案：", LabelX, 60 + RowHeight));
            Controls.Add(_lblScheme);
            // 先加开关、后加标签：万一日后标签又变宽，也是标签被按钮压住（难看但能点），
            // 而不是标签盖住第一个按钮
            Controls.Add(_togglesAhead);
            Controls.Add(aheadLabel);
            Controls.Add(MakeLabel("播报音量：", LabelX, AfterAheadY));
            Controls.Add(_stepperVolume);
            Controls.Add(MakeLabel("％", UnitX, AfterAheadY));
            Controls.Add(MakeLabel("播报次数：", LabelX, AfterAheadY + RowHeight));
            Controls.Add(_stepperRepeat);
            Controls.Add(MakeLabel("遍", UnitX, AfterAheadY + RowHeight));
            Controls.Add(MakeLabel("自动刷新：", LabelX, AfterAheadY + RowHeight * 2));
            Controls.Add(_stepperRefresh);
            Controls.Add(MakeLabel("分钟", UnitX, AfterAheadY + RowHeight * 2));
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            UpdateScheme();
        }

        /// <summary>按当前班级刷新"时段方案"那一行，并同步 GradeScheme 属性。</summary>
        private void UpdateScheme()
        {
            var scheme = GradeSchemes.SchemeForClass(DescribeClass(_classId));
            if (scheme != null) { GradeScheme = scheme; }

            _lblScheme.Text = scheme == null
                ? GradeScheme + "（认不出年级，沿用原设置）"
                : scheme + "（按班级自动）";
        }

        private void Collect()
        {
            AutoStartEnabled = _chkAutoStart.Checked;
            FloatingEnabled = _chkFloating.Checked;
            SelectedClassId = _classId;
            RemindAheadList = _togglesAhead.Selected;
            AnnounceVolumePercent = _stepperVolume.Value;
            AnnounceRepeatCount = _stepperRepeat.Value;
            RefreshIntervalMinutes = _stepperRefresh.Value;
        }

        private void ChooseClass()
        {
            if (_classes == null || _classes.Count == 0) { return; }

            using (var picker = new ClassPickerForm(_classes, _classId, true))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) { return; }

                _classId = picker.SelectedClassId;
                _btnClass.Text = DescribeClass(_classId);
                UpdateScheme();
            }
        }

        private string DescribeClass(string id)
        {
            if (_classes != null)
            {
                foreach (var item in _classes)
                {
                    if (string.Equals(item.Id, id, System.StringComparison.Ordinal))
                    {
                        return item.DisplayName;
                    }
                }
            }

            return "（未选择）";
        }

        private static TouchStepper MakeStepper(int minimum, int maximum, int step, int value, int y)
        {
            return new TouchStepper(minimum, maximum, step, value)
            {
                Location = UiScale.P(StepperX, y),
            };
        }

        /// <summary>
        /// 那排开关显示哪些档位：**预设档 + 配置里已有的非预设值**（都按降序排）。
        /// 为什么要带上非预设值：配置是纯文本、允许手改（比如有人把档位设成 8 分钟），
        /// 界面要是不认它，一打开设置再点确定就把它悄悄抹掉了——宁可多画一个按钮，也别改使用者的值。
        /// </summary>
        private static int[] BuildAheadValues(IList<int> current)
        {
            var values = new List<int>(AppConfig.RemindAheadPresets);

            if (current != null)
            {
                foreach (var value in current)
                {
                    if (value < 0 || value > 60) { continue; }
                    if (!values.Contains(value)) { values.Add(value); }
                }
            }

            values.Sort((a, b) => b.CompareTo(a));
            return values.ToArray();
        }

        /// <summary>行标题：往下偏 17，好跟 56 高的步进控件垂直居中。</summary>
        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Location = UiScale.P(x, y + 17),
            };
        }
    }
}
