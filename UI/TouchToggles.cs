using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KeRing.UI
{
    /// <summary>
    /// 触屏用的**多选开关**：一排大按钮，点一下切换开 / 关，亮蓝色的是"已启用"。
    /// 用在设置里的"提前提醒"那一行——想"提前 10 分钟、7 分钟各响一次"就把 10 和 7 都点亮。
    ///
    /// 为什么是开关而不是加减号步进器（使用方 2026-09-18 定的）：
    /// 一体机上手指点 58×56 的按钮比点 12 像素的小箭头靠谱得多，而且**一眼看得出现在开了几档**。
    /// 尺寸跟 TouchStepper 对齐（高 56），放在同一列里不会参差；配色跟 TouchChoice 一致。
    /// </summary>
    internal sealed class TouchToggles : Panel
    {
        private const int Height_ = 56;
        private const int Gap = 8;

        private static readonly Color OnBack = Color.FromArgb(46, 107, 230);
        private static readonly Color OnHover = Color.FromArgb(74, 130, 240);
        private static readonly Color OffBack = Color.FromArgb(90, 100, 112);
        private static readonly Color OffHover = Color.FromArgb(112, 122, 136);

        private readonly int[] _values;
        private readonly bool[] _on;
        private readonly List<Button> _buttons = new List<Button>();

        /// <summary>任意一个开关被点动之后触发（使用方不用它做联动，只是留个口子）。</summary>
        public event EventHandler SelectionChanged;

        public TouchToggles(int[] values, IList<int> selected, string unit, int totalWidth)
        {
            _values = values ?? new int[0];
            _on = new bool[_values.Length];
            BackColor = SystemColors.Control;

            var count = _values.Length;
            if (count == 0) { return; }

            var buttonWidth = (totalWidth - Gap * (count - 1)) / count;
            Size = UiScale.S(totalWidth, Height_);

            for (var i = 0; i < count; i++)
            {
                var value = _values[i];
                _on[i] = Contains(selected, value);

                var button = new Button
                {
                    Text = value + unit,
                    Location = UiScale.P(i * (buttonWidth + Gap), 0),
                    Size = UiScale.S(buttonWidth, Height_),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.White,
                    Font = UiFont.DialogButton,
                    UseVisualStyleBackColor = false,
                    Cursor = Cursors.Hand,
                };

                button.FlatAppearance.BorderSize = 0;
                var index = i;
                button.Click += (sender, args) => Toggle(index);

                _buttons.Add(button);
                Controls.Add(button);
            }

            ApplyColors();
        }

        /// <summary>当前点亮的档位（按显示顺序：大的在前）。可以一个都不亮 = 不打铃。</summary>
        public List<int> Selected
        {
            get
            {
                var result = new List<int>();
                for (var i = 0; i < _values.Length; i++)
                {
                    if (_on[i]) { result.Add(_values[i]); }
                }

                return result;
            }
        }

        private void Toggle(int index)
        {
            if (index < 0 || index >= _on.Length) { return; }

            _on[index] = !_on[index];
            ApplyColors();

            var handler = SelectionChanged;
            if (handler != null) { handler(this, EventArgs.Empty); }
        }

        private void ApplyColors()
        {
            for (var i = 0; i < _buttons.Count; i++)
            {
                var button = _buttons[i];
                button.BackColor = _on[i] ? OnBack : OffBack;
                button.FlatAppearance.MouseOverBackColor = _on[i] ? OnHover : OffHover;
                button.FlatAppearance.MouseDownBackColor = OnBack;
            }
        }

        private static bool Contains(IList<int> source, int value)
        {
            if (source == null) { return false; }

            foreach (var item in source)
            {
                if (item == value) { return true; }
            }

            return false;
        }
    }
}
