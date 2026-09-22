using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinKbdCheck
{
    /// <summary>
    /// 统一的 DPI 处理。
    ///
    /// 为什么不用 WinForms 自带的 AutoScaleMode.Dpi？
    ///  1) AutoScaleMode.Dpi 只缩放 Location / Size / Padding 等布局属性，
    ///     **不缩放 Font** —— 字体和控件会一大一小对不上；
    ///  2) 本程序是手工像素排版的，LayoutShell / LayoutTestPage 会在 OnResize 里
    ///     用硬编码像素重新 SetBounds，把 AutoScale 的结果又盖回去。
    ///
    /// 因此这里关掉 AutoScale，改为：所有尺寸与字体统一走本类，
    /// 全程序只有一个缩放基准 _scale = 屏幕 DPI / 96。
    /// </summary>
    internal static class Dpi
    {
        private static float _scale = 1f;
        private static FontFamily _family;

        /// <summary>缩放系数：96 DPI = 1.0，150% 缩放 = 1.5。</summary>
        public static float Scale
        {
            get { return _scale; }
        }

        public static void Init()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    if (g != null)
                        _scale = g.DpiX / 96f;
                }
            }
            catch
            {
                _scale = 1f;
            }

            // 防御：取到离谱值就当作 100%
            if (_scale < 0.5f || _scale > 4f)
                _scale = 1f;

            try
            {
                _family = SystemFonts.MessageBoxFont.FontFamily;
            }
            catch
            {
                _family = FontFamily.GenericSansSerif;
            }
        }

        /* ---------------- 尺寸 ---------------- */

        /// <summary>把一个按 96 DPI 设计的像素值换算成当前 DPI 的像素值。</summary>
        public static int Px(float value)
        {
            return (int)Math.Round(value * _scale);
        }

        public static float PxF(float value)
        {
            return value * _scale;
        }

        /* ---------------- 字体 ---------------- */

        /// <summary>按 96 DPI 下的字号创建字体（内部转成像素单位，避免 pt 的隐式 DPI 行为不一致）。</summary>
        public static Font MakeFont(float sizeInPoints)
        {
            return MakeFont(sizeInPoints, FontStyle.Regular);
        }

        public static Font MakeFont(float sizeInPoints, FontStyle style)
        {
            string name = (_family != null) ? _family.Name : "Segoe UI";
            return MakeFont(name, sizeInPoints, style);
        }

        /// <summary>指定字体族（例如 Consolas 等宽字体）。族不存在时由 GDI+ 自行回退。</summary>
        public static Font MakeFont(string familyName, float sizeInPoints, FontStyle style)
        {
            float px = sizeInPoints * (96f / 72f) * _scale;   // pt -> 96DPI 像素 -> 当前 DPI 像素
            if (px < 1f) px = 1f;

            try
            {
                return new Font(familyName, px, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel);
            }
        }

        /* ---------------- 整棵控件树缩放 ---------------- */

        /// <summary>
        /// 在窗体构建完成后调用一次：把整棵控件树的布局属性按 DPI 缩放。
        /// 只处理布局（Bounds / Min/MaxSize / Padding / Margin / ListView 列宽 /
        /// TableLayoutPanel 的绝对行高列宽），**不碰 Font** —— 字体一律由 MakeFont 负责，
        /// 这样就不会出现双重缩放。
        /// </summary>
        public static void ScaleTree(Control root)
        {
            if (root == null || _scale == 1f)
                return;

            List<Control> all = new List<Control>();
            Collect(root, all);

            for (int i = 0; i < all.Count; i++)
            {
                Control c = all[i];
                if (c.Parent == null)
                    continue;

                // SplitterPanel / TabPage 的尺寸由各自的父控件管理，
                // 直接改 Bounds 会被覆盖甚至引发重排，这里只缩放它们的内部边距。
                bool managedByParent = (c is SplitterPanel) || (c is TabPage);

                if (!managedByParent)
                    c.Bounds = new Rectangle(Px(c.Left), Px(c.Top), Px(c.Width), Px(c.Height));

                if (!c.MinimumSize.IsEmpty)
                    c.MinimumSize = new Size(Px(c.MinimumSize.Width), Px(c.MinimumSize.Height));
                if (!c.MaximumSize.IsEmpty)
                    c.MaximumSize = new Size(Px(c.MaximumSize.Width), Px(c.MaximumSize.Height));

                c.Padding = new Padding(Px(c.Padding.Left), Px(c.Padding.Top),
                                        Px(c.Padding.Right), Px(c.Padding.Bottom));
                c.Margin = new Padding(Px(c.Margin.Left), Px(c.Margin.Top),
                                       Px(c.Margin.Right), Px(c.Margin.Bottom));

                // ListView 的列宽不是 Control 属性，单独缩放
                ListView lv = c as ListView;
                if (lv != null)
                {
                    for (int j = 0; j < lv.Columns.Count; j++)
                        lv.Columns[j].Width = Px(lv.Columns[j].Width);
                }

                // TableLayoutPanel 的绝对尺寸行/列
                // 注意：ColumnStyle 用 Width，RowStyle 用 Height，没有 Size 属性
                TableLayoutPanel tlp = c as TableLayoutPanel;
                if (tlp != null)
                {
                    for (int j = 0; j < tlp.ColumnStyles.Count; j++)
                    {
                        if (tlp.ColumnStyles[j].SizeType == SizeType.Absolute)
                            tlp.ColumnStyles[j].Width = PxF(tlp.ColumnStyles[j].Width);
                    }
                    for (int j = 0; j < tlp.RowStyles.Count; j++)
                    {
                        if (tlp.RowStyles[j].SizeType == SizeType.Absolute)
                            tlp.RowStyles[j].Height = PxF(tlp.RowStyles[j].Height);
                    }
                }
            }

            Form f = root as Form;
            if (f != null)
            {
                f.ClientSize = new Size(Px(f.ClientSize.Width), Px(f.ClientSize.Height));
                if (!f.MinimumSize.IsEmpty)
                    f.MinimumSize = new Size(Px(f.MinimumSize.Width), Px(f.MinimumSize.Height));
            }
        }

        private static void Collect(Control parent, List<Control> list)
        {
            foreach (Control child in parent.Controls)
            {
                list.Add(child);
                Collect(child, list);
            }
        }
    }
}