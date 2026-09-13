using System.Drawing;
using System.Windows.Forms;
using KeRing.App;

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

        private readonly CheckBox _chkAutoStart;
        private readonly TouchChoice _choiceScheme;
        private readonly TouchStepper _stepperAhead;
        private readonly TouchStepper _stepperVolume;
        private readonly TouchStepper _stepperRefresh;

        public bool AutoStartEnabled { get; private set; }
        public string GradeScheme { get; private set; }
        public int RemindAheadMinutes { get; private set; }
        public int AnnounceVolumePercent { get; private set; }
        public int RefreshIntervalMinutes { get; private set; }

        public SettingsForm(bool autoStart, string gradeScheme, int aheadMinutes, int volumePercent, int refreshMinutes)
        {
            AutoStartEnabled = autoStart;
            GradeScheme = GradeSchemes.Normalize(gradeScheme);
            RemindAheadMinutes = aheadMinutes;
            AnnounceVolumePercent = volumePercent;
            RefreshIntervalMinutes = refreshMinutes;

            AutoScaleMode = AutoScaleMode.None;
            Text = "设置";
            Font = new Font("Microsoft YaHei", 9F);
            ClientSize = UiScale.S(460, 384);
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

            _choiceScheme = new TouchChoice(
                new[] { GradeSchemes.Junior, GradeSchemes.Senior },
                GradeScheme,
                StepperTotalWidth)
            {
                Location = UiScale.P(StepperX, 60),
            };

            _stepperAhead = MakeStepper(0, 60, 1, aheadMinutes, 124);
            _stepperVolume = MakeStepper(0, 100, 5, volumePercent, 188);
            _stepperRefresh = MakeStepper(1, 1440, 5, refreshMinutes, 252);

            var ok = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Size = UiScale.S(88, 30),
                Location = UiScale.P(264, 332),
            };
            var cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Size = UiScale.S(88, 30),
                Location = UiScale.P(360, 332),
            };

            ok.Click += (sender, args) => Collect();

            Controls.Add(_chkAutoStart);
            Controls.Add(MakeLabel("时段方案：", LabelX, 60));
            Controls.Add(_choiceScheme);
            Controls.Add(MakeLabel("提前提醒：", LabelX, 124));
            Controls.Add(_stepperAhead);
            Controls.Add(MakeLabel("分钟", UnitX, 124));
            Controls.Add(MakeLabel("播报音量：", LabelX, 188));
            Controls.Add(_stepperVolume);
            Controls.Add(MakeLabel("％", UnitX, 188));
            Controls.Add(MakeLabel("自动刷新：", LabelX, 252));
            Controls.Add(_stepperRefresh);
            Controls.Add(MakeLabel("分钟", UnitX, 252));
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Collect()
        {
            AutoStartEnabled = _chkAutoStart.Checked;
            GradeScheme = _choiceScheme.Selected;
            RemindAheadMinutes = _stepperAhead.Value;
            AnnounceVolumePercent = _stepperVolume.Value;
            RefreshIntervalMinutes = _stepperRefresh.Value;
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
