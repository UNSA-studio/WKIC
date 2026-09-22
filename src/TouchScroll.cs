using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinKbdCheck
{
    /// <summary>
    /// 让传统 WinForms 控件在触摸屏上能拖动滚动。
    ///
    /// 背景：ListView / ListBox / TextBox 这些老控件只认鼠标滚轮，
    /// 手指拖动默认不会滚动内容 —— 在平板上会出现"列表根本滚不动"的问题。
    ///
    /// 这里做两件事：
    ///  1) 对 ListView 调用 SetWindowTheme(hwnd, "Explorer", NULL)，
    ///     让它走资源管理器那套主题，从而获得系统级的触摸惯性与拖动滚动；
    ///  2) 子类化窗口过程，识别「由触摸产生的鼠标消息」
    ///     （GetMessageExtraInfo 里的 FROMTOUCH 标记），
    ///     把这些拖动自己换算成滚动；真实鼠标拖动保持原生行为（框选不动）。
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
                        // 需要超过阈值才算拖动，否则单击仍然正常选中
                        if (Math.Abs(now.Y - _startCursor.Y) < Dpi.Px(8))
                        {
                            base.WndProc(ref m);
                            return;
                        }
                        _moving = true;
                        _lastCursor = now;
                        return;
                    }

                    int itemHeight = GetItemHeight();
                    if (itemHeight > 0)
                    {
                        int lines = (now.Y - _lastCursor.Y) / itemHeight;
                        if (lines != 0)
                        {
                            _lastCursor = now;
                            ScrollBy(-lines);
                        }
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
                ListView lv = _target as ListView;
                if (lv != null)
                {
                    if (lv.Items.Count > 0 && lv.Items[0].Bounds.Height > 0)
                        return lv.Items[0].Bounds.Height;
                    return Dpi.Px(20);
                }

                ListBox lb = _target as ListBox;
                if (lb != null)
                    return Math.Max(1, lb.ItemHeight);

                TreeView tv = _target as TreeView;
                if (tv != null)
                    return Math.Max(1, tv.ItemHeight);

                return Math.Max(1, _target.Font.Height);
            }

            /// <summary>lines &gt; 0 表示内容向上滚（看到更靠后的内容）。</summary>
            private void ScrollBy(int lines)
            {
                ListView lv = _target as ListView;
                if (lv != null)
                {
                    if (lv.Items.Count == 0 || lv.TopItem == null)
                        return;
                    int idx = lv.TopItem.Index + lines;
                    if (idx < 0) idx = 0;
                    if (idx > lv.Items.Count - 1) idx = lv.Items.Count - 1;
                    try
                    {
                        lv.TopItem = lv.Items[idx];
                    }
                    catch
                    {
                    }
                    return;
                }

                ListBox lb = _target as ListBox;
                if (lb != null)
                {
                    if (lb.Items.Count == 0)
                        return;
                    int idx = lb.TopIndex + lines;
                    if (idx < 0) idx = 0;
                    if (idx > lb.Items.Count - 1) idx = lb.Items.Count - 1;
                    lb.TopIndex = idx;
                    return;
                }

                // TextBox / RichTextBox：直接发 EM_LINESCROLL
                if (_target is TextBox || _target is RichTextBox)
                {
                    if (!_target.IsHandleCreated)
                        return;
                    SendMessage(_target.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)lines);
                }
            }
        }
    }
}