using System.Drawing;
using System.Windows.Forms;

namespace KeRing.UI
{
    /// <summary>
    /// 对话框按钮的统一样式：主操作蓝底白字、次操作灰底白字，都做大一些。
    /// 尺寸仍走 UiScale，高 DPI 屏上跟着放大。
    /// </summary>
    internal static class DialogButtons
    {
        public static Button Primary(string text, int x, int y, int width, int height)
        {
            return Build(text, x, y, width, height,
                Color.FromArgb(46, 107, 230),
                Color.FromArgb(74, 130, 240),
                Color.FromArgb(34, 84, 186));
        }

        public static Button Secondary(string text, int x, int y, int width, int height)
        {
            return Build(text, x, y, width, height,
                Color.FromArgb(112, 122, 136),
                Color.FromArgb(134, 144, 158),
                Color.FromArgb(88, 96, 108));
        }

        private static Button Build(
            string text, int x, int y, int width, int height,
            Color back, Color hover, Color pressed)
        {
            var button = new Button
            {
                Text = text,
                Size = UiScale.S(width, height),
                Location = UiScale.P(x, y),
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
    }
}
