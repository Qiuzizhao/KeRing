using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using KeRing.App;

namespace KeRing.UI
{
    /// <summary>
    /// 课表背景水印（使用方 2026-09-20 要的）：把一张图**居中、保持比例、淡淡地**画在课表格子区域上。
    ///
    /// 几条约定，改的时候别踩：
    ///   · 图**内嵌在 exe 里**：`Assets\watermark.jpg`，512×480、约 14 KB。
    ///     原图是 2048×1920 / 2.4 MB —— 水印本来就看不清细节，高清图纯属浪费体积，已经压过（见 docs）。
    ///   · **不拉伸**：按原宽高比缩放（先按高度算，宽度超了就改用宽度算），然后**居中**。
    ///   · **不碰课表内容**：画的是半透明叠加（GDI+ ImageAttributes + ColorMatrix 的 alpha），
    ///     文字一个像素都不改，只是底下/上面多一层淡淡的图。透明度走
    ///     `AppConfig.WatermarkOpacityPercent`（默认 8，0 = 不画）。
    ///   · 只画**格子区域**（不含列标题那一行），所以表头永远是干净的。
    /// </summary>
    internal static class Watermark
    {
        private const string ResourceName = "KeRing.Assets.watermark.jpg";

        /// <summary>水印最多占格子区域的多大（按高/宽取更严的那个，留出边距）。</summary>
        private const double MaxHeightRatio = 0.72;
        private const double MaxWidthRatio = 0.60;

        private static Image _image;
        private static bool _tried;

        /// <summary>内嵌的水印图；没有就返回 null（画的时候直接跳过，不影响别的）。</summary>
        public static Image GetImage()
        {
            if (_tried) { return _image; }
            _tried = true;

            try
            {
                using (var stream = typeof(Watermark).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        Logger.Warn("内嵌水印图缺失：" + ResourceName);
                        return null;
                    }

                    // 从流里读出来的图要用一份拷贝，别让 GDI+ 一直占着那个流
                    using (var raw = Image.FromStream(stream))
                    {
                        _image = new Bitmap(raw);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("读取内嵌水印图失败：" + ex.Message);
                _image = null;
            }

            return _image;
        }

        /// <summary>课表格子区域：去掉列标题那一行（行标题默认不显示，显示了也一并去掉）。</summary>
        public static Rectangle CellsArea(DataGridView grid)
        {
            if (grid == null) { return Rectangle.Empty; }

            var left = grid.RowHeadersVisible ? grid.RowHeadersWidth : 0;
            var top = grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0;
            var width = grid.ClientSize.Width - left;
            var height = grid.ClientSize.Height - top;
            if (width <= 0 || height <= 0) { return Rectangle.Empty; }

            return new Rectangle(left, top, width, height);
        }

        /// <summary>
        /// 把水印画进 area 的正中间，只画 clip 那块（画的时候会被 clip 裁掉，用于"整块一次画完"
        /// 和"按格子分块画"两种调用）。
        /// </summary>
        public static void Draw(Graphics g, Rectangle area, Rectangle clip, int opacityPercent)
        {
            var image = GetImage();
            if (image == null || g == null) { return; }
            if (opacityPercent <= 0 || area.Width <= 0 || area.Height <= 0) { return; }

            if (opacityPercent > 100) { opacityPercent = 100; }

            Rectangle dest;
            try
            {
                dest = FitCentered(image, area);
            }
            catch (Exception ex)
            {
                Logger.Warn("计算水印位置失败：" + ex.Message);
                return;
            }

            var state = g.Save();
            try
            {
                if (clip.Width > 0 && clip.Height > 0) { g.SetClip(clip, CombineMode.Intersect); }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var attributes = new ImageAttributes())
                {
                    // Matrix33 = alpha（0=全透明，1=不透明）
                    var matrix = new ColorMatrix { Matrix33 = opacityPercent / 100f };
                    attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    g.DrawImage(image, dest, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("画课表水印失败：" + ex.Message);
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>按原比例缩放并居中（不拉伸）。</summary>
        private static Rectangle FitCentered(Image image, Rectangle area)
        {
            var height = area.Height * MaxHeightRatio;
            var width = height * image.Width / (double)image.Height;

            var maxWidth = area.Width * MaxWidthRatio;
            if (width > maxWidth)
            {
                width = maxWidth;
                height = width * image.Height / (double)image.Width;
            }

            var w = (int)Math.Round(width);
            var h = (int)Math.Round(height);
            if (w <= 0 || h <= 0) { return Rectangle.Empty; }

            return new Rectangle(
                area.Left + (area.Width - w) / 2,
                area.Top + (area.Height - h) / 2,
                w,
                h);
        }
    }
}
