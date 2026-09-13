using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KeRing.UI
{
    /// <summary>
    /// 触屏用的单选控件：一排大按钮，选中的高亮。用于"低年级 / 高年级"这类二选一。
    /// 尺寸跟 TouchStepper 对齐，放在同一列里不会参差。
    /// </summary>
    internal sealed class TouchChoice : Panel
    {
        private const int Height_ = 56;
        private const int Gap = 8;

        private static readonly Color SelectedBack = Color.FromArgb(46, 107, 230);
        private static readonly Color SelectedHover = Color.FromArgb(74, 130, 240);
        private static readonly Color UnselectedBack = Color.FromArgb(90, 100, 112);
        private static readonly Color UnselectedHover = Color.FromArgb(112, 122, 136);

        private readonly string[] _options;
        private readonly List<Button> _buttons = new List<Button>();
        private int _selected;

        public event EventHandler SelectionChanged;

        public string Selected
        {
            get { return _options[_selected]; }
        }

        public TouchChoice(string[] options, string selected, int totalWidth)
        {
            _options = options;
            BackColor = SystemColors.Control;

            var count = options.Length;
            var buttonWidth = (totalWidth - Gap * (count - 1)) / count;
            Size = UiScale.S(totalWidth, Height_);

            // 先定好选中项，再建按钮——否则默认值 0 会在匹配之前就占住第一个选项
            _selected = 0;
            for (var i = 0; i < count; i++)
            {
                if (string.Equals(options[i], selected, StringComparison.Ordinal))
                {
                    _selected = i;
                    break;
                }
            }

            for (var i = 0; i < count; i++)
            {
                var index = i;
                var button = new Button
                {
                    Text = options[i],
                    Location = UiScale.P(i * (buttonWidth + Gap), 0),
                    Size = UiScale.S(buttonWidth, Height_),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.White,
                    Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold),
                    UseVisualStyleBackColor = false,
                    Cursor = Cursors.Hand,
                    BackColor = i == _selected ? SelectedBack : UnselectedBack,
                };

                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = i == _selected ? SelectedHover : UnselectedHover;
                button.FlatAppearance.MouseDownBackColor = SelectedBack;
                button.Click += (sender, args) => Select(index);

                _buttons.Add(button);
                Controls.Add(button);
            }
        }

        private void Select(int index)
        {
            if (index == _selected) { return; }
            _selected = index;

            for (var i = 0; i < _buttons.Count; i++)
            {
                var button = _buttons[i];
                var isSelected = i == _selected;
                button.BackColor = isSelected ? SelectedBack : UnselectedBack;
                button.FlatAppearance.MouseOverBackColor = isSelected ? SelectedHover : UnselectedHover;
            }

            var handler = SelectionChanged;
            if (handler != null) { handler(this, EventArgs.Empty); }
        }
    }
}
