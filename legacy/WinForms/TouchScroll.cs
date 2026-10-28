using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinKbdCheck
{
    /// <summary>
    /// 让传统 WinForms 控件在触摸屏上能拖动滚动（横向 + 纵向）。
    ///
    /// 背景：ListView / ListBox / TextBox 这些老控件只认鼠标滚轮，
    /// 手指拖动默认完全不会滚动 —— 在平板上就是"列表/协议根本拉不动"。
    ///
    /// 这里做两件事：
    ///  1) 对 ListView 调用 SetWindowTheme(hwnd, "Explorer", NULL)，
    ///     让它走资源管理器那套主题，拿到系统级的触摸惯性；
    ///  2) 子类化窗口过程，用 GetMessageExtraInfo() 里的 FROMTOUCH 标记
    ///     识别"这条鼠标消息其实是手指划出来的"，把拖动自己换算成滚动。
    ///     真实鼠标拖动保持原生行为（例如 ListView 的框选）。
    ///
    /// 横向也处理了：左侧数据栏里 CPU 型号、时间戳这类长文本会自动撑开列宽，
    /// 超出面板宽度的部分靠横向拖动查看。
    /// </summary>
    internal static class TouchScroll
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // 触摸/笔产生的鼠标消息特征：extra info & 0xFFFFFF00 == 0xFF515700
        private const long TOUCH_MASK = 0xFFFFFF00L;
        private const long TOUCH_SIG = 0xFF515700L;

        private const int EM_LINESCROLL = 0x00B6;
        private const int LVM_SCROLL = 0x1014;

        /// <summary>递归给整棵控件树里所有可滚动控件挂上触摸滚动支持。</summary>
        public static void Enable(Control root)
        {
            if (root == null)
                return;

            Apply(root);

            for (int i = 0; i < root.Controls.Count; i++)
                Enable(root.Controls[i]);
        }

        private static void Apply(Control c)
        {
            bool scrollable = (c is ListView) || (c is ListBox) || (c is TextBox) ||
                              (c is TreeView) || (c is RichTextBox);
            if (!scrollable)
                return;

            ListView lv = c as ListView;
            if (lv != null)
            {
                try
                {
                    if (lv.IsHandleCreated)
                        SetWindowTheme(lv.Handle, "Explorer", null);
                }
                catch
                {
                    // uxtheme 不可用时忽略，下面的拖动滚动仍然有效
                }
            }

            new Scroller(c);
        }

        private static bool IsTouchGenerated()
        {
            try
            {
                long extra = GetMessageExtraInfo().ToInt64();
                return (extra & TOUCH_MASK) == TOUCH_SIG;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>挂到控件窗口过程上的滚动逻辑。</summary>
        private sealed class Scroller : NativeWindow
        {
            private const int WM_LBUTTONDOWN = 0x0201;
            private const int WM_MOUSEMOVE = 0x0200;
            private const int WM_LBUTTONUP = 0x0202;
            private const int WM_CAPTURECHANGED = 0x0215;

            private readonly Control _target;
            private bool _armed;
            private bool _moving;
            private Point _startCursor;
            private Point _lastCursor;

            public Scroller(Control target)
            {
                _target = target;
                target.HandleCreated += OnHandleCreated;
                target.HandleDestroyed += OnHandleDestroyed;
                if (target.IsHandleCreated)
                    AssignHandle(target.Handle);
            }

            private void OnHandleCreated(object sender, EventArgs e)
            {
                if (!Handle.Equals(IntPtr.Zero))
                    return;
                AssignHandle(_target.Handle);
            }

            private void OnHandleDestroyed(object sender, EventArgs e)
            {
                if (!Handle.Equals(IntPtr.Zero))
                    ReleaseHandle();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_LBUTTONDOWN && IsTouchGenerated())
                {
                    _armed = true;
                    _moving = false;
                    _startCursor = Cursor.Position;
                    _lastCursor = _startCursor;
                    base.WndProc(ref m);
                    return;
                }

                if (m.Msg == WM_MOUSEMOVE && _armed)
                {
                    if (!IsTouchGenerated())
                    {
                        // 鼠标操作：交回原生逻辑（例如框选）
                        _armed = false;
                        _moving = false;
                        base.WndProc(ref m);
                        return;
                    }

                    Point now = Cursor.Position;

                    if (!_moving)
                    {
                        // 需要超过死区才算拖动，否则轻点仍然正常选中
                        int moved = Math.Abs(now.X - _startCursor.X) + Math.Abs(now.Y - _startCursor.Y);
                        if (moved < Dpi.Px(8))
                        {
                            base.WndProc(ref m);
                            return;
                        }
                        _moving = true;
                        _lastCursor = now;
                        return;
                    }

                    int dx = now.X - _lastCursor.X;
                    int dy = now.Y - _lastCursor.Y;
                    if (dx != 0 || dy != 0)
                    {
                        _lastCursor = now;
                        // 手指往哪边划，内容就往反方向走
                        ScrollBy(-dx, -dy);
                    }

                    return;   // 吞掉，避免控件去画选择框
                }

                if (m.Msg == WM_LBUTTONUP || m.Msg == WM_CAPTURECHANGED)
                {
                    _armed = false;
                    _moving = false;
                    base.WndProc(ref m);
                    return;
                }

                base.WndProc(ref m);
            }

            private int GetItemHeight()
            {
                ListBox lb = _target as ListBox;
                if (lb != null)
                    return Math.Max(1, lb.ItemHeight);

                TreeView tv = _target as TreeView;
                if (tv != null)
                    return Math.Max(1, tv.ItemHeight);

                return Math.Max(1, _target.Font.Height);
            }

            /// <summary>
            /// dx / dy 为「内容移动量」：
            /// 正值表示内容向左 / 向上移动（也就是看到右侧 / 下方的内容）。
            /// </summary>
            private void ScrollBy(int dx, int dy)
            {
                /* ---- ListView：原生就支持按像素横竖滚动 ---- */
                ListView lv = _target as ListView;
                if (lv != null)
                {
                    if (dx == 0 && dy == 0)
                        return;
                    if (!lv.IsHandleCreated)
                        return;
                    SendMessage(lv.Handle, LVM_SCROLL, (IntPtr)dx, (IntPtr)dy);
                    return;
                }

                int itemHeight = GetItemHeight();

                /* ---- ListBox：按行滚，只支持纵向 ---- */
                ListBox lb = _target as ListBox;
                if (lb != null)
                {
                    if (dy == 0 || lb.Items.Count == 0)
                        return;
                    int lines = dy / itemHeight;
                    if (lines == 0)
                        lines = (dy > 0) ? 1 : -1;

                    int idx = lb.TopIndex + lines;
                    if (idx < 0) idx = 0;
                    if (idx > lb.Items.Count - 1) idx = lb.Items.Count - 1;
                    lb.TopIndex = idx;
                    return;
                }

                /* ---- TextBox / RichTextBox：按行滚，只支持纵向 ---- */
                if (_target is TextBox || _target is RichTextBox)
                {
                    if (dy == 0 || !_target.IsHandleCreated)
                        return;
                    int lines = dy / itemHeight;
                    if (lines == 0)
                        lines = (dy > 0) ? 1 : -1;
                    SendMessage(_target.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)lines);
                }
            }
        }
    }
}