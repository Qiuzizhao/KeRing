using System.Drawing;

namespace KeRing.UI
{
    /// <summary>
    /// 全局字号表。**改字号只改这里**，别在界面代码里写死磅数。
    ///
    /// 两条依据：
    ///   1. 这台机器是教室一体机，触屏、看的人站得远，所以整体比桌面软件大一档；
    ///   2. 字号用"磅"，会随屏幕 DPI 自动放大，所以**不要**再过一遍 `UiScale.S()`
    ///      ——那是给像素尺寸用的（见开发交接.md 第五节第 8 条）。
    ///
    /// 当前层级（由大到小）：
    ///   步进符号 17 > 步进数值 15 > 课程名/选班列表 13 > 时钟·班级按钮·二选一 12
    ///   > 工具栏按钮·对话框按钮 11 > 状态栏·节次列·对话框标签 10
    /// </summary>
    internal static class UiFont
    {
        private const string Family = "Microsoft YaHei";

        /// <summary>状态栏文字、窗口/托盘默认字体、节次列、对话框标签。</summary>
        public static readonly Font Body = new Font(Family, 10F);

        /// <summary>更小一号：悬浮窗预览里的时刻、行标题下面的小字。</summary>
        public static readonly Font Small = new Font(Family, 8.5F);

        /// <summary>表头（周一…）。</summary>
        public static readonly Font Header = new Font(Family, 10.5F, FontStyle.Bold);

        /// <summary>工具栏按钮、对话框按钮。</summary>
        public static readonly Font Button = new Font(Family, 11F, FontStyle.Bold);

        /// <summary>状态栏时钟——一眼看时间用的，比其他状态文字大两档。</summary>
        public static readonly Font Clock = new Font(Family, 12F, FontStyle.Bold);

        /// <summary>设置里的大按钮（选班级）和二选一控件。</summary>
        public static readonly Font DialogButton = new Font(Family, 12F, FontStyle.Bold);

        /// <summary>选班对话框的列表项——手指点的目标，大一点。</summary>
        public static readonly Font ListItem = new Font(Family, 13F);

        /// <summary>课表格子里的课程名。格子约 124×56 起，13 磅约占行高四分之一。</summary>
        public static readonly Font Course = new Font(Family, 13F);

        /// <summary>课程名的高亮态（正在上的课、下一个提醒点）。</summary>
        public static readonly Font CourseBold = new Font(Family, 13F, FontStyle.Bold);

        /// <summary>触屏步进控件的数值。</summary>
        public static readonly Font Stepper = new Font(Family, 15F, FontStyle.Bold);

        /// <summary>触屏步进控件的加减号。</summary>
        public static readonly Font StepperSign = new Font(Family, 17F, FontStyle.Bold);
    }
}
