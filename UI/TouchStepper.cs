using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeRing.UI
{
    /// <summary>
    /// 触屏用的数值步进控件：[−] 数值 [+]，按钮 56×56 逻辑像素，适合一体机大屏上手点。
    /// 按住不放会连续加减，免得在大屏上戳几十下；长按第一下仍然立即生效。
    /// </summary>
    internal sealed class TouchStepper : Panel
    {
        private const int ButtonSide = 56;
        private const int ValueWidth = 120;

        private readonly Label _valueBox;
        private readonly Timer _repeat;
        private int _current;
        private int _direction;

        public int Minimum { get; private set; }
        public int Maximum { get; private set; }
        public int Step { get; private set; }

        public event EventHandler ValueChanged;

        public int Value
        {
            get { return _current; }
            set { SetValue(value); }
        }

        public TouchStepper(int minimum, int maximum, int step, int value)
        {
            Minimum = minimum;
            Maximum = maximum;
            Step = step;

            Size = UiScale.S(ButtonSide + ValueWidth + ButtonSide, ButtonSide);
            BackColor = SystemColors.Control;

            var minus = MakeButton("−", 0);
            var plus = MakeButton("+", ButtonSide + ValueWidth);

            _valueBox = new Label
            {
                Location = UiScale.P(ButtonSide, 0),
                Size = UiScale.S(ValueWidth, ButtonSide),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UiFont.Stepper,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(38, 42, 48),
                BorderStyle = BorderStyle.FixedSingle,
            };

            minus.MouseDown += (sender, args) => BeginChange(-1);
            minus.MouseUp += (sender, args) => EndChange();
            minus.MouseLeave += (sender, args) => EndChange();
            plus.MouseDown += (sender, args) => BeginChange(1);
            plus.MouseUp += (sender, args) => EndChange();
            plus.MouseLeave += (sender, args) => EndChange();

            _repeat = new Timer { Interval = HoldDelayMs };
            _repeat.Tick += (sender, args) => OnRepeatTick();

            Controls.Add(minus);
            Controls.Add(_valueBox);
            Controls.Add(plus);

            _current = Math.Max(Minimum, Math.Min(Maximum, value));
            _valueBox.Text = _current.ToString();
        }

        private const int HoldDelayMs = 400;
        private const int RepeatMs = 110;

        private static Button MakeButton(string text, int x)
        {
            var button = new Button
            {
                Text = text,
                Location = UiScale.P(x, 0),
                Size = UiScale.S(ButtonSide, ButtonSide),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(90, 100, 112),
                ForeColor = Color.White,
                Font = UiFont.StepperSign,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(112, 122, 136);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(68, 76, 86);
            return button;
        }

        private void BeginChange(int direction)
        {
            Apply(direction);
            _direction = direction;
            _repeat.Interval = HoldDelayMs;
            _repeat.Start();
        }

        private void EndChange()
        {
            _repeat.Stop();
        }

        /// <summary>
        /// 长按连发。每次都先确认鼠标还按着——只依赖 MouseUp 不保险：
        /// 一旦松开事件丢了（触屏抬起被吞、鼠标移出窗口），数值会一直往下跑。
        /// </summary>
        private void OnRepeatTick()
        {
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                EndChange();
                return;
            }

            Apply(_direction);
            if (_repeat.Interval != RepeatMs) { _repeat.Interval = RepeatMs; }
        }

        private void Apply(int direction)
        {
            SetValue(_current + direction * Step);
        }

        private void SetValue(int value)
        {
            if (value < Minimum) { value = Minimum; }
            if (value > Maximum) { value = Maximum; }
            if (value == _current) { return; }

            _current = value;
            _valueBox.Text = value.ToString();

            var handler = ValueChanged;
            if (handler != null) { handler(this, EventArgs.Empty); }
        }
    }
}
