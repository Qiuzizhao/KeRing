using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using KeRing.App.Schedule;

namespace KeRing.UI
{
    /// <summary>
    /// 选班级。列表来自数据源，条目做成大行高，一体机大屏上手指能点准。
    /// 首次运行时不可取消（一定要选一个）；在设置里切换时可以取消。
    /// </summary>
    internal sealed class ClassPickerForm : Form
    {
        private const int RowHeight = 52;

        private static readonly Color SelectedBack = Color.FromArgb(46, 107, 230);
        private static readonly Color HoverBack = Color.FromArgb(232, 238, 250);

        private readonly ListBox _list;

        public string SelectedClassId { get; private set; }

        public ClassPickerForm(IList<SchoolClass> classes, string currentId, bool cancellable)
        {
            SelectedClassId = currentId;

            AutoScaleMode = AutoScaleMode.None;
            Text = "选择班级";
            Font = UiFont.Body;
            ClientSize = UiScale.S(460, 508);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            _list = new ListBox
            {
                Location = UiScale.P(24, 24),
                Size = UiScale.S(412, 404),
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = UiScale.S(RowHeight),
                Font = UiFont.ListItem,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
            };
            _list.DrawItem += OnDrawItem;
            _list.DoubleClick += (sender, args) => Accept();

            foreach (var item in classes)
            {
                var index = _list.Items.Add(item);
                if (string.Equals(item.Id, currentId, StringComparison.Ordinal))
                {
                    _list.SelectedIndex = index;
                }
            }

            if (_list.SelectedIndex < 0 && _list.Items.Count > 0) { _list.SelectedIndex = 0; }

            // 主操作按钮做大一点、给点颜色，一体机上才好点
            const int buttonY = 444;
            const int okWidth = 160;
            var okX = cancellable ? 84 : (460 - okWidth) / 2;

            var ok = DialogButtons.Primary("确定", okX, buttonY, okWidth, 48);
            ok.Click += (sender, args) => Accept();

            Controls.Add(_list);
            Controls.Add(ok);

            if (cancellable)
            {
                var cancel = DialogButtons.Secondary("取消", 256, buttonY, 120, 48);
                cancel.DialogResult = DialogResult.Cancel;
                Controls.Add(cancel);
                CancelButton = cancel;
            }

            AcceptButton = ok;
        }

        private void Accept()
        {
            var picked = _list.SelectedItem as SchoolClass;
            if (picked == null) { return; }

            SelectedClassId = picked.Id;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) { return; }

            var item = _list.Items[e.Index] as SchoolClass;
            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            using (var back = new SolidBrush(selected ? SelectedBack : Color.White))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }

            var text = item == null ? string.Empty : item.DisplayName;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var rect = new Rectangle(e.Bounds.Left + UiScale.S(16), e.Bounds.Top,
                                     e.Bounds.Width - UiScale.S(24), e.Bounds.Height);

            TextRenderer.DrawText(
                e.Graphics,
                text,
                _list.Font,
                rect,
                selected ? Color.White : Color.FromArgb(38, 42, 48),
                flags);

            using (var line = new Pen(Color.FromArgb(232, 235, 239)))
            {
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        }
    }
}
