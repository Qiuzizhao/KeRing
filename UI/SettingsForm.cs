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
        private const int RowHeight = 64;

        private readonly IList<SchoolClass> _classes;
        private readonly CheckBox _chkAutoStart;
        private readonly Button _btnClass;
        private readonly Label _lblScheme;
        private readonly TouchStepper _stepperAhead;
        private readonly TouchStepper _stepperVolume;
        private readonly TouchStepper _stepperRefresh;

        private string _classId;

        public bool AutoStartEnabled { get; private set; }
        public string SelectedClassId { get; private set; }
        public string GradeScheme { get; private set; }
        public int RemindAheadMinutes { get; private set; }
        public int AnnounceVolumePercent { get; private set; }
        public int RefreshIntervalMinutes { get; private set; }

        public SettingsForm(
            bool autoStart,
            IList<SchoolClass> classes,
            string classId,
            string gradeScheme,
            int aheadMinutes,
            int volumePercent,
            int refreshMinutes)
        {
            _classes = classes;
            _classId = classId;

            AutoStartEnabled = autoStart;
            SelectedClassId = classId;
            GradeScheme = GradeSchemes.Normalize(gradeScheme);
            RemindAheadMinutes = aheadMinutes;
            AnnounceVolumePercent = volumePercent;
            RefreshIntervalMinutes = refreshMinutes;

            AutoScaleMode = AutoScaleMode.None;
            Text = "设置";
            Font = UiFont.Body;
            ClientSize = UiScale.S(460, 448);
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

            _stepperAhead = MakeStepper(0, 60, 1, aheadMinutes, 60 + RowHeight * 2);
            _stepperVolume = MakeStepper(0, 100, 5, volumePercent, 60 + RowHeight * 3);
            _stepperRefresh = MakeStepper(1, 1440, 5, refreshMinutes, 60 + RowHeight * 4);

            var ok = DialogButtons.Primary("确定", 232, 390, 116, 44);
            ok.DialogResult = DialogResult.OK;

            var cancel = DialogButtons.Secondary("取消", 356, 390, 88, 44);
            cancel.DialogResult = DialogResult.Cancel;

            ok.Click += (sender, args) => Collect();

            Controls.Add(_chkAutoStart);
            Controls.Add(MakeLabel("班级：", LabelX, 60));
            Controls.Add(_btnClass);
            Controls.Add(MakeLabel("时段方案：", LabelX, 60 + RowHeight));
            Controls.Add(_lblScheme);
            Controls.Add(MakeLabel("提前提醒：", LabelX, 60 + RowHeight * 2));
            Controls.Add(_stepperAhead);
            Controls.Add(MakeLabel("分钟", UnitX, 60 + RowHeight * 2));
            Controls.Add(MakeLabel("播报音量：", LabelX, 60 + RowHeight * 3));
            Controls.Add(_stepperVolume);
            Controls.Add(MakeLabel("％", UnitX, 60 + RowHeight * 3));
            Controls.Add(MakeLabel("自动刷新：", LabelX, 60 + RowHeight * 4));
            Controls.Add(_stepperRefresh);
            Controls.Add(MakeLabel("分钟", UnitX, 60 + RowHeight * 4));
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
            SelectedClassId = _classId;
            RemindAheadMinutes = _stepperAhead.Value;
            AnnounceVolumePercent = _stepperVolume.Value;
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
