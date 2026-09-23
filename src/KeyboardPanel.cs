using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace WinKbdCheck
{
    /// <summary>
    /// 键盘控件。未按下的键使用 Win32 原生按钮渲染器绘制
    /// （System.Windows.Forms.ButtonRenderer -> DrawThemeBackground("BUTTON")），
    /// 因此外观与系统原生按钮完全一致；已被程序捕获到反馈的键，
    /// 使用当前 Windows 主题强调色填充。
    /// </summary>
    internal sealed class KeyboardPanel : Control
    {
        /// <summary>键盘图的两种用途。</summary>
        internal enum PanelMode
        {
            /// <summary>检测中：引导键闪烁，捕获成功的键点亮。</summary>
            Test,
            /// <summary>结果展示：按完好 / 跳过 / 无响应着色，可点击查看该键的检测记录。</summary>
            Result
        }

        // 结果模式的三色语义（取自 Windows 系统色板）
        private static readonly Color ResultOk = Color.FromArgb(0x10, 0x7C, 0x10);       // 完好
        private static readonly Color ResultSkipped = Color.FromArgb(0x9D, 0x5D, 0x00);  // 引导超时跳过
        private static readonly Color ResultBad = Color.FromArgb(0xC4, 0x2B, 0x1C);      // 无响应

        internal sealed class KeyState
        {
            public KeyDef Def;
            public bool IsDown;
            public bool WasCaptured;
            public bool TimedOut;          // 引导期间超时未响应
            public int DownCount;
            public DateTime FirstDown = DateTime.MinValue;
            public DateTime LastDown = DateTime.MinValue;
            public DateTime HoldStart = DateTime.MinValue;
            public double TotalHoldMs;
            public long LastScanCode;
            public bool LastExtended;
            public bool LastInjected;
        }

        // 以下尺寸会随 DPI 缩放；字体统一由 Dpi.MakeFont 生成
        private int PadMargin { get { return Dpi.Px(6); } }
        private int Gap { get { return Dpi.Px(3); } }

        private readonly List<KeyState> _states = new List<KeyState>();
        private readonly Dictionary<int, KeyState> _stateById = new Dictionary<int, KeyState>();
        private Font _fontLabel;
        private Font _fontSmall;
        private Font _fontSub;
        private bool _visualStylesAvailable;
        private Color _accent = Color.FromArgb(0, 120, 215);
        private Color _accentDown = Color.FromArgb(0, 90, 158);
        private string _hoverText;
        private ToolTip _tip;

        // ---- 引导模式状态 ----
        private int _guideKeyId;       // 0 = 无引导
        private bool _guideFlash = true;

        // ---- 结果展示模式状态 ----
        private PanelMode _mode = PanelMode.Test;
        private int _selectedKeyId;

        public event EventHandler<KeyState> KeyCaptured;

        /// <summary>在键盘图上点击某个键（总结页用来查看该键的检测记录）。</summary>
        public event EventHandler<KeyState> KeyClicked;

        public KeyboardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Control;

            _fontLabel = Dpi.MakeFont(8.0f);
            _fontSmall = Dpi.MakeFont(6.5f);
            _fontSub = Dpi.MakeFont(6.5f);

            _tip = new ToolTip();
            _tip.InitialDelay = 600;
            _tip.ReshowDelay = 100;

            try
            {
                _visualStylesAvailable = VisualStyleInformation.IsEnabledByUser &&
                                         VisualStyleInformation.IsSupportedByOS;
            }
            catch
            {
                _visualStylesAvailable = false;
            }

            foreach (KeyDef def in KeyboardLayout.Keys)
            {
                KeyState st = new KeyState();
                st.Def = def;
                _states.Add(st);
                _stateById[def.Id] = st;
            }

            RefreshThemeColor();
        }

        /* ---------------- 公开接口 ---------------- */

        public int TotalKeyCount
        {
            get { return _states.Count; }
        }

        public int CapturedKeyCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _states.Count; i++)
                    if (_states[i].WasCaptured) n++;
                return n;
            }
        }

        public List<KeyState> States
        {
            get { return _states; }
        }

        public Color AccentColor
        {
            get { return _accent; }
        }

        public void RefreshThemeColor()
        {
            _accent = ThemeUtil.AccentColor;
            _accentDown = ControlPaint.Dark(_accent, 0.18f);
            Invalidate();
        }

        public void ResetAll()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                KeyState s = _states[i];
                s.IsDown = false;
                s.WasCaptured = false;
                s.TimedOut = false;
                s.DownCount = 0;
                s.FirstDown = DateTime.MinValue;
                s.LastDown = DateTime.MinValue;
                s.HoldStart = DateTime.MinValue;
                s.TotalHoldMs = 0;
            }
            _guideKeyId = 0;
            _guideFlash = true;
            Invalidate();
        }

        /* ---------------- 引导模式 ---------------- */

        /// <summary>当前被高亮引导、等待用户按下的按键 Id；0 表示没有引导目标。</summary>
        public int GuideKeyId
        {
            get { return _guideKeyId; }
        }

        /// <summary>设置引导目标。传 0 清除引导。会自动只重绘受影响的两个键。</summary>
        public void SetGuide(int keyId)
        {
            if (_guideKeyId == keyId)
                return;
            int old = _guideKeyId;
            _guideKeyId = keyId;
            _guideFlash = true;
            InvalidateKeyById(old);
            InvalidateKeyById(_guideKeyId);
        }

        /// <summary>切换引导键的闪烁相位。</summary>
        public void SetFlash(bool on)
        {
            if (_guideFlash == on)
                return;
            _guideFlash = on;
            InvalidateKeyById(_guideKeyId);
        }

        /// <summary>切换键盘图的用途（检测中 / 结果展示）。</summary>
        public PanelMode Mode
        {
            get { return _mode; }
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    Invalidate();
                }
            }
        }

        /// <summary>结果模式下当前被选中查看的键 Id。</summary>
        public int SelectedKeyId
        {
            get { return _selectedKeyId; }
            set
            {
                if (_selectedKeyId != value)
                {
                    _selectedKeyId = value;
                    Invalidate();
                }
            }
        }

        public void RefreshKey(KeyState st)
        {
            if (st == null)
                return;
            Invalidate(RectOf(st.Def));
        }

        private void InvalidateKeyById(int keyId)
        {
            if (keyId == 0)
                return;
            KeyState st;
            if (_stateById.TryGetValue(keyId, out st))
                Invalidate(RectOf(st.Def));
        }

        /// <summary>
        /// 处理一次按键事件。返回匹配到的按键状态；未匹配返回 null。
        /// 该方法必须在 UI 线程调用。
        /// </summary>
        public KeyState HandleKey(int vk, uint scanCode, bool extended, bool isDown, bool injected)
        {
            KeyDef def = KeyboardLayout.Match(vk, scanCode, extended);
            if (def == null)
                return null;

            KeyState st;
            if (!_stateById.TryGetValue(def.Id, out st))
                return null;

            st.LastScanCode = scanCode;
            st.LastExtended = extended;
            st.LastInjected = injected;

            if (isDown)
            {
                if (!st.IsDown)
                {
                    st.IsDown = true;
                    st.DownCount++;
                    st.HoldStart = DateTime.Now;
                    if (st.FirstDown == DateTime.MinValue)
                        st.FirstDown = DateTime.Now;
                    st.LastDown = DateTime.Now;
                }
            }
            else
            {
                if (st.IsDown)
                {
                    st.IsDown = false;
                    if (st.HoldStart != DateTime.MinValue)
                    {
                        st.TotalHoldMs += (DateTime.Now - st.HoldStart).TotalMilliseconds;
                        st.HoldStart = DateTime.MinValue;
                    }
                }
            }

            bool firstCapture = false;
            if (isDown && !st.WasCaptured)
            {
                st.WasCaptured = true;
                firstCapture = true;
                Invalidate(RectOf(def));
            }
            else
            {
                Invalidate(RectOf(def));
            }

            if (firstCapture)
            {
                EventHandler<KeyState> handler = KeyCaptured;
                if (handler != null)
                    handler(this, st);
            }

            return st;
        }

        /* ---------------- 坐标换算 ---------------- */

        /// <summary>
        /// 键位单位对应的像素边长：取宽高两个方向能容纳的最小值，
        /// 这样键盘图无论容器多扁多宽都**不会变形**，并且整体居中。
        /// 布局由控件自己负责，外部只要 Dock=Fill 即可，不需要手工算尺寸。
        /// </summary>
        private double CellSize
        {
            get
            {
                double cw = (Width - 2.0 * PadMargin) / KeyboardLayout.TotalUnitsX;
                double ch = (Height - 2.0 * PadMargin) / KeyboardLayout.TotalUnitsY;
                double cs = Math.Min(cw, ch);
                return (cs > 1.0) ? cs : 1.0;
            }
        }

        private double OffsetX
        {
            get { return (Width - KeyboardLayout.TotalUnitsX * CellSize) / 2.0; }
        }

        private double OffsetY
        {
            get { return (Height - KeyboardLayout.TotalUnitsY * CellSize) / 2.0; }
        }

        private Rectangle RectOf(KeyDef k)
        {
            double cs = CellSize;
            int x = (int)Math.Round(OffsetX + k.X * cs) + Gap / 2;
            int y = (int)Math.Round(OffsetY + k.Y * cs) + Gap / 2;
            int w = (int)Math.Round(k.W * cs) - Gap;
            int h = (int)Math.Round(k.H * cs) - Gap;
            int minSide = Dpi.Px(4);
            if (w < minSide) w = minSide;
            if (h < minSide) h = minSide;
            return new Rectangle(x, y, w, h);
        }

        /* ---------------- 绘制 ---------------- */

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool resultMode = (_mode == PanelMode.Result);

            for (int i = 0; i < _states.Count; i++)
            {
                KeyState st = _states[i];
                Rectangle r = RectOf(st.Def);
                bool guide = (!resultMode && st.Def.Id == _guideKeyId);
                bool selected = (resultMode && st.Def.Id == _selectedKeyId);

                if (resultMode)
                    DrawResultKey(g, r, st, selected);
                else if (st.WasCaptured)
                    DrawCapturedKey(g, r, st);
                else if (guide)
                    DrawGuideKey(g, r);
                else if (st.TimedOut)
                    DrawTimedOutKey(g, r);
                else
                    DrawNativeKey(g, r, st.IsDown);

                DrawKeyText(g, r, st, guide, resultMode);
            }
        }

        /// <summary>结果模式：绿=完好，琥珀=引导超时跳过，红=无响应；选中的键加粗边框。</summary>
        private void DrawResultKey(Graphics g, Rectangle r, KeyState st, bool selected)
        {
            Color fill;
            if (st.WasCaptured)
                fill = ResultOk;
            else if (st.TimedOut)
                fill = ResultSkipped;
            else
                fill = ResultBad;

            int radius = Dpi.Px(3);

            using (SolidBrush brush = new SolidBrush(fill))
            {
                using (GraphicsPath path = RoundedRect(r, radius))
                {
                    g.FillPath(brush, path);
                }
            }

            using (Pen pen = new Pen(ControlPaint.Dark(fill, 0.30f)))
            {
                using (GraphicsPath path = RoundedRect(r, radius))
                {
                    g.DrawPath(pen, path);
                }
            }

            if (selected)
            {
                using (Pen pen = new Pen(Color.Black, Dpi.Px(2)))
                {
                    g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
                }
            }
        }

        /// <summary>当前被引导、等待按下的键：主题色闪烁（实心 ↔ 描边）。</summary>
        private void DrawGuideKey(Graphics g, Rectangle r)
        {
            if (_guideFlash)
            {
                using (SolidBrush brush = new SolidBrush(_accent))
                {
                    g.FillRectangle(brush, r);
                }
                using (Pen pen = new Pen(ControlPaint.Dark(_accent, 0.30f)))
                {
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                }
            }
            else
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(60, _accent)))
                {
                    g.FillRectangle(brush, r);
                }
                using (Pen pen = new Pen(_accent, Dpi.Px(2)))
                {
                    g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
                }
            }
        }

        /// <summary>引导期间超时未响应的键：灰底点线框，与"尚未检测"区分开。</summary>
        private void DrawTimedOutKey(Graphics g, Rectangle r)
        {
            using (SolidBrush brush = new SolidBrush(SystemColors.Control))
            {
                g.FillRectangle(brush, r);
            }
            using (Pen pen = new Pen(SystemColors.ControlDark))
            {
                pen.DashStyle = DashStyle.Dot;
                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
        }

        /// <summary>未捕获的键：使用系统原生按钮渲染器，外观与系统按钮一模一样。</summary>
        private void DrawNativeKey(Graphics g, Rectangle r, bool down)
        {
            bool ok = false;
            if (_visualStylesAvailable)
            {
                try
                {
                    PushButtonState state = down ? PushButtonState.Pressed : PushButtonState.Normal;
                    if (ButtonRenderer.IsBackgroundPartiallyTransparent(state))
                        ButtonRenderer.DrawParentBackground(g, r, this);
                    ButtonRenderer.DrawButton(g, r, state);
                    ok = true;
                }
                catch
                {
                    ok = false;
                }
            }

            if (!ok)
            {
                // 经典（无视觉样式）回退：3D 边框按钮
                ControlPaint.DrawButton(g, r, down ? ButtonState.Pushed : ButtonState.Normal);
            }
        }

        /// <summary>已捕获的键：填充当前 Windows 主题强调色。</summary>
        private void DrawCapturedKey(Graphics g, Rectangle r, KeyState st)
        {
            Color fill = st.IsDown ? _accentDown : _accent;
            Color border = ControlPaint.Dark(_accent, 0.30f);

            int radius = Dpi.Px(3);

            using (SolidBrush brush = new SolidBrush(fill))
            {
                using (GraphicsPath path = RoundedRect(r, radius))
                {
                    g.FillPath(brush, path);
                }
            }

            using (Pen pen = new Pen(border))
            {
                using (GraphicsPath path = RoundedRect(r, radius))
                {
                    g.DrawPath(pen, path);
                }
            }

            // 顶部 1px 高光，让高亮键看起来有立体感（与原生按钮质感一致）
            using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255)))
            {
                g.DrawLine(pen,
                    r.Left + radius, r.Top + Dpi.Px(2),
                    r.Right - radius - 1, r.Top + Dpi.Px(2));
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (r.Width <= d || r.Height <= d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void DrawKeyText(Graphics g, Rectangle r, KeyState st, bool guide, bool resultMode)
        {
            KeyDef k = st.Def;

            Color color;
            if (resultMode)
                color = Color.White;
            else if (st.WasCaptured)
                color = Color.White;
            else if (guide)
                color = _guideFlash ? Color.White : _accent;
            else if (st.TimedOut)
                color = SystemColors.GrayText;
            else
                color = SystemColors.ControlText;

            // 短标签（1~3 个字符）用正常字号；长标签用略小字号避免溢出
            string label = k.Label;
            Font mainFont = _fontLabel;
            if (label != null && label.Length > 4)
                mainFont = _fontSmall;

            Rectangle textRect = r;

            if (k.Sub != null && k.Sub.Length > 0)
            {
                // 副字符画在左上角，主字符居中
                Rectangle subRect = new Rectangle(
                    r.Left + Dpi.Px(3), r.Top + Dpi.Px(1),
                    r.Width - Dpi.Px(6), Dpi.Px(12));
                TextRenderer.DrawText(g, k.Sub, _fontSub, subRect, color,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding |
                    TextFormatFlags.EndEllipsis);

                textRect = new Rectangle(
                    r.Left + Dpi.Px(1), r.Top + Dpi.Px(5),
                    r.Width - Dpi.Px(2), r.Height - Dpi.Px(6));
            }

            TextRenderer.DrawText(g, label, mainFont, textRect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left)
                return;

            KeyState hit = HitTest(e.Location);
            if (hit == null)
                return;

            _selectedKeyId = hit.Def.Id;
            Invalidate();

            EventHandler<KeyState> handler = KeyClicked;
            if (handler != null)
                handler(this, hit);
        }

        private KeyState HitTest(Point p)
        {
            for (int i = 0; i < _states.Count; i++)
            {
                if (RectOf(_states[i].Def).Contains(p))
                    return _states[i];
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            string text = null;
            for (int i = 0; i < _states.Count; i++)
            {
                if (RectOf(_states[i].Def).Contains(e.Location))
                {
                    text = _states[i].Def.Name;
                    break;
                }
            }

            if (_mode == PanelMode.Result)
                Cursor = (text != null) ? Cursors.Hand : Cursors.Default;

            if (text != _hoverText)
            {
                _hoverText = text;
                if (_tip != null)
                    _tip.SetToolTip(this, text == null ? "" : text);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontLabel != null) _fontLabel.Dispose();
                if (_fontSmall != null) _fontSmall.Dispose();
                if (_fontSub != null) _fontSub.Dispose();
                if (_tip != null) _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}