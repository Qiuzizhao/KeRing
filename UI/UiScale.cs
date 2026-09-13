using System;
using System.Drawing;
using KeRing.App;

namespace KeRing.UI
{
    /// <summary>
    /// 界面按 96 DPI 设计，在高 DPI 屏上整体放大。
    /// 不走 WinForms 的自动缩放：实测 AutoScaleDimensions 会被重置成当前 DPI，缩放比例恒为 1，
    /// 结果是在 200% 缩放的 4K 屏上窗口只有预期物理尺寸的一半，行高不够把课表内容裁掉。
    /// </summary>
    internal static class UiScale
    {
        public static readonly float Factor = Detect();

        public static int S(int value)
        {
            return (int)Math.Round(value * Factor);
        }

        public static Size S(int width, int height)
        {
            return new Size(S(width), S(height));
        }

        public static Point P(int x, int y)
        {
            return new Point(S(x), S(y));
        }

        private static float Detect()
        {
            try
            {
                using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    if (graphics.DpiX > 0) { return graphics.DpiX / 96f; }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("检测 DPI 失败，按 100% 处理：" + ex.Message);
            }

            return 1f;
        }
    }
}
