using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace WinKbdCheck
{
    /// <summary>
    /// 主窗体 —— 四步向导：
    ///   0. 欢迎与风险告知
    ///   1. 键盘设备 / 驱动信息采集（详细过程日志）
    ///   2. 逐键检测（接管全部键盘输入，捕获成功的键变成系统主题色）
    ///   3. 总结报告（设备详情、年代推断、逐键明细、完整时间线、导出）
    /// </summary>
    internal sealed partial class MainForm : Form
    {
        /* ---------- 骨架 ---------- */
        private Panel _header;
        private Panel _stage;
        private Panel _footer;
        private Label _lblHeaderTitle;
        private Label _lblHeaderStep;
        private Button _btnBack;
        private Button _btnNext;
        private Button _btnClose;

        /* ---------- 页面 ---------- */
        private Panel _pageWelcome;
        private Panel _pageInfo;
        private Panel _pageTest;
        private Panel _pageSummary;

        /* ---------- 第 1 步 ---------- */
        private CheckBox _chkAgree;

        /* ---------- 第 2 步 ---------- */
        private ProgressBar _pbInfo;
        private ListBox _lbInfoLog;
        private Label _lblInfoTitle;
        private bool _infoDone;

        /* ---------- 第 3 步 ---------- */
        private KeyboardPanel _keyboard;
        private Panel _kbdHost;
        private Panel _testBar;
        private SplitContainer _testSplit;
        private ProgressBar _pbTest;
        private Label _lblTestProgress;
        private Label _lblTestHint;
        private CheckBox _chkBlock;
        private Button _btnReset;
        private Button _btnStopNow;
        private ListView _lvMissing;
        private TextBox _txtLive;
        private Label _lblNowKey;
        private Label _lblTarget;
        private Button _btnFinish;

        /* ---------- 第 4 步：结果展示 ---------- */
        private Label _lblVerdict;
        private TableLayoutPanel _summaryGrid;
        private ListView _lvOverview;        // 左上角：检测数据 / 当前用户 / 硬件信息
        private Panel _kbdResultHost;
        private KeyboardPanel _kbdResult;    // 结果键盘图（可点击）
        private Label _lblLegend;
        private ListView _lvKeyDetail;       // 点击某个键后显示的记录
        private Button _btnExport;
        private Button _btnRestart;

        /* ---------- 数据 ---------- */
        private readonly List<TimelineEntry> _timeline = new List<TimelineEntry>();
        private List<KeyboardDeviceInfo> _devices = new List<KeyboardDeviceInfo>();
        private SystemKeyboardInfo _sysInfo;
        private MachineInfo _machine = new MachineInfo();
        private List<string[]> _driverRows = new List<string[]>();

        private KeyboardHook _hook;
        private int _step;
        private bool _testing;
        private bool _tested;
        private DateTime _testStart;
        private DateTime _testEnd;

        private bool _modCtrl;
        private bool _modAlt;
        private bool _modShift;

        /* ---------- 引导式检测流程 ---------- */
        private List<KeyboardPanel.KeyState> _testQueue = new List<KeyboardPanel.KeyState>();
        private int _testIndex;
        private System.Windows.Forms.Timer _guideTimer;
        private int _guideElapsedMs;
        private int _skippedCount;
        private DateTime _lastReturnDown = DateTime.MinValue;

        /// <summary>单个键的响应窗口：超过这个时间没有按下就自动跳到下一个。</summary>
        private const int GuideTimeoutMs = 3000;

        /// <summary>双击回车判定窗口。</summary>
        private const int DoubleReturnMs = 450;

        public MainForm()
        {
            BuildUi();

            _hook = new KeyboardHook();
            _hook.KeyEvent += OnKeyboardEvent;
            if (!_hook.Install())
            {
                MessageBox.Show(
                    "无法安装全局键盘钩子（SetWindowsHookEx 失败）。\r\n" +
                    "检测功能将不可用。请尝试右键以管理员身份运行。",
                    Program.AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                Log("初始化", "全局低级键盘钩子 WH_KEYBOARD_LL 安装成功（当前未接管）。");
            }

            _sysInfo = null;

            // 界面全部构建完成后，按 DPI 统一缩放整棵控件树的布局属性
            Dpi.ScaleTree(this);
            ClampToScreen();

            GoToStep(0);
        }

        /// <summary>
        /// 把窗口尺寸限制在屏幕工作区内。
        /// 1180×820 在 150% 缩放下会变成 1770×1230，1080p 屏根本放不下，
        /// 这里连带把 MinimumSize 一起收窄，避免窗口比屏幕还大。
        /// </summary>
        private void ClampToScreen()
        {
            Rectangle wa;
            try
            {
                wa = Screen.PrimaryScreen.WorkingArea;
            }
            catch
            {
                return;
            }

            int maxW = Math.Max(Dpi.Px(640), wa.Width - Dpi.Px(30));
            int maxH = Math.Max(Dpi.Px(480), wa.Height - Dpi.Px(30));

            int minW = Math.Min(MinimumSize.Width, maxW);
            int minH = Math.Min(MinimumSize.Height, maxH);
            MinimumSize = new Size(minW, minH);

            int w = Math.Min(ClientSize.Width, maxW);
            int h = Math.Min(ClientSize.Height, maxH);
            if (w < minW) w = minW;
            if (h < minH) h = minH;

            ClientSize = new Size(w, h);
        }

        /* ====================================================================
         *  UI 构建
         * ==================================================================== */

        private void BuildUi()
        {
            Text = Program.AppTitle + "  —  " + Program.AppTitleCn;

            // 关闭 WinForms 自带的自动缩放：本程序的所有坐标都以 96 DPI 为基准手工编写，
            // 尺寸走 Dpi.Px()、字体走 Dpi.MakeFont()，全程序只有一套缩放置。
            // （AutoScaleMode.Dpi 只缩放布局属性、不缩放 Font，且会被 OnResize 里的
            //   动态布局覆盖回去，所以这里必须关掉。）
            AutoScaleMode = AutoScaleMode.None;
            Font = Dpi.MakeFont(9f);

            StartPosition = FormStartPosition.CenterScreen;

            // 下面是 96 DPI 基准值，实际尺寸由 Dpi.ScaleTree 统一放大
            ClientSize = new Size(1180, 820);
            MinimumSize = new Size(1024, 720);
            BackColor = SystemColors.Control;
            KeyPreview = false;

            /* ---- 页眉 ---- */
            _header = new Panel();
            _header.Height = 66;
            _header.BackColor = SystemColors.Control;
            _header.Paint += HeaderPaint;

            _lblHeaderTitle = new Label();
            _lblHeaderTitle.Text = "Windows 键盘完整性检测";
            _lblHeaderTitle.Font = Dpi.MakeFont(13.5f);
            _lblHeaderTitle.AutoSize = true;
            _lblHeaderTitle.Location = new Point(18, 10);

            _lblHeaderStep = new Label();
            _lblHeaderStep.Text = "";
            _lblHeaderStep.ForeColor = SystemColors.GrayText;
            _lblHeaderStep.AutoSize = true;
            _lblHeaderStep.Location = new Point(20, 40);

            _header.Controls.Add(_lblHeaderTitle);
            _header.Controls.Add(_lblHeaderStep);

            /* ---- 页脚 ---- */
            _footer = new Panel();
            _footer.Height = 58;
            _footer.BackColor = SystemColors.Control;
            _footer.Paint += FooterPaint;

            FlowLayoutPanel nav = new FlowLayoutPanel();
            nav.Dock = DockStyle.Fill;
            nav.FlowDirection = FlowDirection.RightToLeft;
            nav.Padding = new Padding(0, 11, 16, 11);
            nav.WrapContents = false;

            _btnClose = new Button();
            _btnClose.Text = "关闭(&C)";
            _btnClose.Width = 100;
            _btnClose.Height = 26;
            _btnClose.Click += delegate { Close(); };

            _btnNext = new Button();
            _btnNext.Text = "下一步(&N) >";
            _btnNext.Width = 116;
            _btnNext.Height = 26;
            _btnNext.Click += delegate { OnNextClicked(); };

            _btnBack = new Button();
            _btnBack.Text = "< 上一步(&B)";
            _btnBack.Width = 116;
            _btnBack.Height = 26;
            _btnBack.Click += delegate { OnBackClicked(); };

            nav.Controls.Add(_btnClose);
            nav.Controls.Add(_btnNext);
            nav.Controls.Add(_btnBack);
            _footer.Controls.Add(nav);

            /* ---- 舞台 ---- */
            _stage = new Panel();
            _stage.BackColor = SystemColors.Control;

            BuildWelcomePage();
            BuildInfoPage();
            BuildTestPage();
            BuildSummaryPage();

            _stage.Controls.Add(_pageWelcome);
            _stage.Controls.Add(_pageInfo);
            _stage.Controls.Add(_pageTest);
            _stage.Controls.Add(_pageSummary);

            Controls.Add(_stage);
            Controls.Add(_footer);
            Controls.Add(_header);

            LayoutShell();
        }

        /// <summary>
        /// 窗体骨架布局：页眉 / 页脚固定高度，舞台占据中间区域。
        /// 全部使用像素坐标 + SetBounds，不依赖 Dock 的 z-order 处理顺序，行为完全确定。
        /// </summary>
        private void LayoutShell()
        {
            if (_header == null || _footer == null || _stage == null)
                return;

            int headerH = Dpi.Px(66);
            int footerH = Dpi.Px(58);

            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0)
                return;

            _header.SetBounds(0, 0, w, headerH);
            _footer.SetBounds(0, h - footerH, w, footerH);
            _stage.SetBounds(0, headerH, w, Math.Max(0, h - headerH - footerH));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutShell();
            LayoutTestPage();
            LayoutResultKeyboard();
        }

        private void HeaderPaint(object sender, PaintEventArgs e)
        {
            using (Pen p = new Pen(SystemColors.ControlDark))
            {
                e.Graphics.DrawLine(p, 0, _header.Height - 1, _header.Width, _header.Height - 1);
            }
        }

        private void FooterPaint(object sender, PaintEventArgs e)
        {
            using (Pen p = new Pen(SystemColors.ControlDark))
            {
                e.Graphics.DrawLine(p, 0, 0, _footer.Width, 0);
            }
        }

        /* ---------------- 第 0 步：欢迎 ---------------- */

        private void BuildWelcomePage()
        {
            _pageWelcome = new Panel();
            _pageWelcome.Dock = DockStyle.Fill;
            _pageWelcome.BackColor = SystemColors.Control;

            GroupBox box = new GroupBox();
            box.Text = "欢迎使用 Windows 键盘完整性检测";
            box.Location = new Point(24, 20);
            box.Size = new Size(1120, 300);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Label desc = new Label();
            desc.AutoSize = false;
            desc.Location = new Point(18, 26);
            desc.Size = new Size(1080, 250);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            desc.Text =
                "本程序用于逐键检测你的键盘：它会要求你按下键盘上的每一个按键，\r\n" +
                "并记录每个按键是否被系统成功接收到硬件反馈。\r\n" +
                "\r\n" +
                "检测过程分为 4 个步骤：\r\n" +
                "    1)  欢迎与风险告知\r\n" +
                "    2)  采集键盘设备、驱动与年代信息（详细过程会实时显示）\r\n" +
                "    3)  引导式逐键检测：键盘图上会闪烁提示下一个该按的键，\r\n" +
                "        按对后该键会完全点亮并变成当前系统主题色，然后自动进入下一个；\r\n" +
                "        如果 3 秒内没有按下，会自动跳到下一个键。\r\n" +
                "        全部按完后自动结束；也可以随时双击回车键结束检测，\r\n" +
                "        或者（回车键坏掉时）用鼠标点击左下角的『完成检测』按钮。\r\n" +
                "    4)  生成总结报告，可导出为文本文件\r\n" +
                "\r\n" +
                "关于『接管键盘』：\r\n" +
                "    在检测过程中，本程序会安装一个系统级低级键盘钩子（WH_KEYBOARD_LL），\r\n" +
                "    在此期间所有键盘输入都会被本程序截收并不会传递给其它程序，\r\n" +
                "    因此即使你按下 Windows 键，开始菜单也不会弹出。\r\n" +
                "    你可以随时用鼠标点击界面上的按钮结束检测，或者按下组合键\r\n" +
                "    Ctrl + Alt + Shift + Q 紧急停止接管。\r\n" +
                "\r\n" +
                "注意：Ctrl + Alt + Del（安全注意序列）由 Windows 内核 winlogon 处理，\r\n" +
                "      任何用户态程序（包括本程序）都无法拦截它。";
            box.Controls.Add(desc);

            _chkAgree = new CheckBox();
            _chkAgree.Text = "我已知悉并同意：检测期间键盘输入将被本程序接管。";
            _chkAgree.AutoSize = true;
            _chkAgree.Location = new Point(42, 336);
            _chkAgree.CheckedChanged += delegate { UpdateNav(); };

            Label tip = new Label();
            tip.AutoSize = true;
            tip.ForeColor = Color.FromArgb(160, 30, 30);
            tip.Location = new Point(42, 366);
            tip.Text = "提示：建议以管理员身份运行，以便在高权限窗口（如任务管理器）处于前台时仍能完整接管键盘。";

            _pageWelcome.Controls.Add(box);
            _pageWelcome.Controls.Add(_chkAgree);
            _pageWelcome.Controls.Add(tip);
        }

        /* ---------------- 第 1 步：信息采集 ---------------- */

        private void BuildInfoPage()
        {
            _pageInfo = new Panel();
            _pageInfo.Dock = DockStyle.Fill;
            _pageInfo.BackColor = SystemColors.Control;

            _lblInfoTitle = new Label();
            _lblInfoTitle.AutoSize = true;
            _lblInfoTitle.Location = new Point(24, 14);
            _lblInfoTitle.Text = "正在采集键盘设备、驱动与系统信息……";

            _pbInfo = new ProgressBar();
            _pbInfo.Location = new Point(26, 40);
            _pbInfo.Size = new Size(1120, 20);
            _pbInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _pbInfo.Style = ProgressBarStyle.Marquee;
            _pbInfo.MarqueeAnimationSpeed = 28;

            GroupBox box = new GroupBox();
            box.Text = "采集过程（详细步骤）";
            box.Location = new Point(24, 72);
            box.Size = new Size(1124, 610);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            _lbInfoLog = new ListBox();
            _lbInfoLog.Dock = DockStyle.Fill;
            _lbInfoLog.IntegralHeight = false;
            _lbInfoLog.Font = Dpi.MakeFont("Consolas", 8.5f, FontStyle.Regular);
            _lbInfoLog.HorizontalScrollbar = true;
            box.Controls.Add(_lbInfoLog);

            _pageInfo.Controls.Add(_lblInfoTitle);
            _pageInfo.Controls.Add(_pbInfo);
            _pageInfo.Controls.Add(box);
        }

        /* ---------------- 第 2 步：逐键检测 ---------------- */

        private void BuildTestPage()
        {
            _pageTest = new Panel();
            _pageTest.Dock = DockStyle.Fill;
            _pageTest.BackColor = SystemColors.Control;

            /* 顶部工具条 */
            Panel bar = new Panel();
            bar.Height = 62;
            bar.BackColor = SystemColors.Control;
            bar.Paint += FooterPaint;
            _testBar = bar;

            _lblTestHint = new Label();
            _lblTestHint.AutoSize = true;
            _lblTestHint.Location = new Point(24, 8);
            _lblTestHint.Text = "键盘图中闪烁的那个键就是下一个要按的键。按对后它会完全点亮并自动进入下一个；3 秒无响应会自动跳过。";

            _pbTest = new ProgressBar();
            _pbTest.Location = new Point(26, 32);
            _pbTest.Size = new Size(460, 18);
            _pbTest.Style = ProgressBarStyle.Continuous;

            _lblTestProgress = new Label();
            _lblTestProgress.AutoSize = true;
            _lblTestProgress.Location = new Point(496, 34);
            _lblTestProgress.Text = "已捕获 0 / 0";

            _chkBlock = new CheckBox();
            _chkBlock.AutoSize = true;
            _chkBlock.Checked = true;
            _chkBlock.Location = new Point(700, 34);
            _chkBlock.Text = "接管键盘输入（Win 键不会弹出开始菜单）";
            _chkBlock.CheckedChanged += delegate
            {
                if (_hook != null)
                    _hook.IsBlocking = _chkBlock.Checked;
                Log("接管", _chkBlock.Checked
                    ? "已开启键盘接管：所有按键将被本程序吞掉，不再传递给系统。"
                    : "已关闭键盘接管：按键会正常传递给系统和其它程序。");
            };

            _btnReset = new Button();
            _btnReset.Text = "重置检测";
            _btnReset.Size = new Size(88, 24);
            _btnReset.Location = new Point(940, 30);
            _btnReset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnReset.Click += delegate
            {
                if (_keyboard == null) return;
                _keyboard.ResetAll();
                _skippedCount = 0;
                _testIndex = 0;
                _guideElapsedMs = 0;
                _testStart = DateTime.Now;
                _lvMissing.Tag = null;
                RefreshTestProgress();
                if (_testing)
                    AdvanceGuide();
                Log("检测", "检测结果已重置，重新从第一个键开始引导。");
            };

            _btnStopNow = new Button();
            _btnStopNow.Text = "完成检测";
            _btnStopNow.Size = new Size(88, 24);
            _btnStopNow.Location = new Point(1034, 30);
            _btnStopNow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStopNow.Click += delegate { FinishTest(false); };

            bar.Controls.Add(_lblTestHint);
            bar.Controls.Add(_pbTest);
            bar.Controls.Add(_lblTestProgress);
            bar.Controls.Add(_chkBlock);
            bar.Controls.Add(_btnReset);
            bar.Controls.Add(_btnStopNow);

            /* 引导目标提示（大字） */
            _lblTarget = new Label();
            _lblTarget.AutoSize = false;
            _lblTarget.TextAlign = ContentAlignment.MiddleCenter;
            _lblTarget.Font = Dpi.MakeFont(12.5f);
            _lblTarget.BackColor = SystemColors.Control;
            _lblTarget.Text = "准备开始……";

            /* 键盘区域 */
            _kbdHost = new Panel();
            _kbdHost.Height = 300;
            _kbdHost.BackColor = SystemColors.Control;
            _kbdHost.Resize += delegate { LayoutKeyboard(); };

            _keyboard = new KeyboardPanel();
            _keyboard.KeyCaptured += OnKeyCaptured;
            _kbdHost.Controls.Add(_keyboard);

            /* 底部信息区 */
            SplitContainer split = new SplitContainer();
            split.Orientation = Orientation.Vertical;
            split.Size = new Size(1180, 400);   // 先给足尺寸，避免 MinSize 约束冲突
            split.Panel1MinSize = 200;
            split.Panel2MinSize = 160;
            _testSplit = split;

            GroupBox gbMissing = new GroupBox();
            gbMissing.Text = "尚未完成检测的按键";
            gbMissing.Dock = DockStyle.Fill;

            _lvMissing = new ListView();
            _lvMissing.Dock = DockStyle.Fill;
            _lvMissing.View = View.Details;
            _lvMissing.FullRowSelect = true;
            _lvMissing.GridLines = false;
            _lvMissing.HideSelection = false;
            _lvMissing.Columns.Add("顺序", 60);
            _lvMissing.Columns.Add("键位", 170);
            _lvMissing.Columns.Add("虚拟键码", 90);
            _lvMissing.Columns.Add("区域", 100);
            gbMissing.Controls.Add(_lvMissing);

            _btnFinish = new Button();
            _btnFinish.Text = "完成检测";
            _btnFinish.Dock = DockStyle.Fill;
            _btnFinish.Click += delegate { FinishTest(false); };

            // 左下角的"完成检测"：双击回车坏掉时的鼠标兜底出口
            TableLayoutPanel leftGrid = new TableLayoutPanel();
            leftGrid.Dock = DockStyle.Fill;
            leftGrid.ColumnCount = 2;
            leftGrid.RowCount = 2;
            leftGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            leftGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            leftGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            leftGrid.Controls.Add(gbMissing, 0, 0);
            leftGrid.SetColumnSpan(gbMissing, 2);
            leftGrid.Controls.Add(_btnFinish, 0, 1);

            GroupBox gbLive = new GroupBox();
            gbLive.Text = "实时按键反馈";
            gbLive.Dock = DockStyle.Fill;

            Panel liveHost = new Panel();
            liveHost.Dock = DockStyle.Fill;

            _lblNowKey = new Label();
            _lblNowKey.Dock = DockStyle.Top;
            _lblNowKey.Height = 22;
            _lblNowKey.Text = "当前按键：—";
            _lblNowKey.Padding = new Padding(6, 3, 0, 0);

            _txtLive = new TextBox();
            _txtLive.Dock = DockStyle.Fill;
            _txtLive.Multiline = true;
            _txtLive.ReadOnly = true;
            _txtLive.ScrollBars = ScrollBars.Vertical;
            _txtLive.WordWrap = false;
            _txtLive.Font = Dpi.MakeFont("Consolas", 8.5f, FontStyle.Regular);
            _txtLive.MaxLength = 1000000;

            liveHost.Controls.Add(_txtLive);
            liveHost.Controls.Add(_lblNowKey);
            gbLive.Controls.Add(liveHost);

            split.Panel1.Controls.Add(leftGrid);
            split.Panel2.Controls.Add(gbLive);

            _pageTest.Controls.Add(split);
            _pageTest.Controls.Add(_kbdHost);
            _pageTest.Controls.Add(_lblTarget);
            _pageTest.Controls.Add(bar);

            _pageTest.Resize += delegate { LayoutTestPage(); };
            LayoutTestPage();
        }

        /// <summary>检测页内部布局：工具条 + 键盘图 + 详情区，按像素手动排布。</summary>
        private void LayoutTestPage()
        {
            if (_pageTest == null || _testBar == null || _testSplit == null ||
                _kbdHost == null || _lblTarget == null)
                return;

            int w = _pageTest.ClientSize.Width;
            int h = _pageTest.ClientSize.Height;
            if (w <= 0 || h <= 0)
                return;

            int barH = Dpi.Px(62);
            int targetH = Dpi.Px(40);
            int kbdH = Dpi.Px(300);

            _testBar.SetBounds(0, 0, w, barH);
            _lblTarget.SetBounds(0, barH, w, targetH);
            _kbdHost.SetBounds(0, barH + targetH, w, kbdH);

            int minBottom = Dpi.Px(140);
            int bottomH = h - barH - targetH - kbdH;
            if (bottomH < minBottom)
                bottomH = minBottom;
            _testSplit.SetBounds(0, barH + targetH + kbdH, w, bottomH);

            int leftW = (int)(w * 0.58);
            int minLeft = _testSplit.Panel1MinSize;
            int minRight = _testSplit.Panel2MinSize;
            int maxLeft = w - minRight - _testSplit.SplitterWidth;
            if (leftW < minLeft) leftW = minLeft;
            if (leftW > maxLeft) leftW = maxLeft;

            if (leftW >= minLeft && leftW <= maxLeft)
            {
                try
                {
                    _testSplit.SplitterDistance = leftW;
                }
                catch (InvalidOperationException)
                {
                    // 尺寸极端时忽略即可，SplitContainer 会自己保持合法值
                }
            }
        }

        private void LayoutKeyboard()
        {
            if (_keyboard == null || _kbdHost == null)
                return;

            int hostW = _kbdHost.ClientSize.Width - Dpi.Px(24);
            int hostH = _kbdHost.ClientSize.Height - Dpi.Px(12);
            if (hostW < 200 || hostH < 60)
                return;

            double ratio = KeyboardLayout.TotalUnitsY / KeyboardLayout.TotalUnitsX; // 6.5 / 23
            int w = hostW;
            int h = (int)Math.Round(w * ratio);
            if (h > hostH)
            {
                h = hostH;
                w = (int)Math.Round(h / ratio);
            }

            _keyboard.Size = new Size(w, h);
            _keyboard.Location = new Point(
                (_kbdHost.ClientSize.Width - w) / 2,
                (_kbdHost.ClientSize.Height - h) / 2);
        }

        private static ListView MakeListView(string[] headers, int[] widths)
        {
            ListView lv = new ListView();
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.HideSelection = false;
            lv.GridLines = true;
            for (int i = 0; i < headers.Length; i++)
            {
                int w = (i < widths.Length) ? widths[i] : 120;
                lv.Columns.Add(headers[i], w);
            }
            return lv;
        }

        /* ====================================================================
         *  步骤控制
         * ==================================================================== */

        private void GoToStep(int step)
        {
            // 离开检测页时确保解除接管并停止引导心跳
            if (_step == 2 && step != 2)
            {
                if (_hook != null)
                    _hook.IsBlocking = false;
                if (_guideTimer != null)
                    _guideTimer.Stop();
                if (_keyboard != null)
                    _keyboard.SetGuide(0);
                _testing = false;
            }

            _step = step;

            _pageWelcome.Visible = (step == 0);
            _pageInfo.Visible = (step == 1);
            _pageTest.Visible = (step == 2);
            _pageSummary.Visible = (step == 3);

            switch (step)
            {
                case 0:
                    _lblHeaderStep.Text = "第 1 步，共 4 步 — 欢迎与风险告知";
                    break;
                case 1:
                    _lblHeaderStep.Text = "第 2 步，共 4 步 — 采集键盘设备与驱动信息";
                    break;
                case 2:
                    _lblHeaderStep.Text = "第 3 步，共 4 步 — 逐键检测（键盘接管中）";
                    break;
                default:
                    _lblHeaderStep.Text = "第 4 步，共 4 步 — 检测结果与总结";
                    break;
            }

            if (step == 1 && !_infoDone)
                StartInfoCollection();

            if (step == 2)
                StartTest();

            if (step == 3)
                PopulateSummary();

            UpdateNav();
        }

        private void UpdateNav()
        {
            switch (_step)
            {
                case 0:
                    _btnBack.Enabled = false;
                    _btnNext.Enabled = _chkAgree.Checked;
                    _btnNext.Text = "下一步(&N) >";
                    break;
                case 1:
                    _btnBack.Enabled = true;
                    _btnNext.Enabled = _infoDone;
                    _btnNext.Text = "下一步(&N) >";
                    break;
                case 2:
                    _btnBack.Enabled = false;
                    _btnNext.Enabled = true;
                    _btnNext.Text = "结束检测(&S)";
                    break;
                default:
                    _btnBack.Enabled = false;
                    _btnNext.Enabled = true;
                    _btnNext.Text = "完成(&F)";
                    break;
            }
        }

        private void OnNextClicked()
        {
            if (_step == 0)
            {
                Log("向导", "用户已确认风险告知，开始采集系统信息。");
                GoToStep(1);
            }
            else if (_step == 1)
            {
                if (!_infoDone)
                    return;
                Log("向导", "信息采集完成，进入逐键检测。");
                GoToStep(2);
            }
            else if (_step == 2)
            {
                FinishTest(false);
            }
            else
            {
                Close();
            }
        }

        private void OnBackClicked()
        {
            if (_step == 1)
                GoToStep(0);
        }

        /* ====================================================================
         *  信息采集（后台线程）
         * ==================================================================== */

        private void StartInfoCollection()
        {
            _infoDone = false;
            _pbInfo.Style = ProgressBarStyle.Marquee;
            _pbInfo.MarqueeAnimationSpeed = 28;
            _lblInfoTitle.Text = "正在采集键盘设备、驱动与系统信息……";

            // 把窗口创建之前（句柄尚未就绪）记录的日志补进列表框
            _lbInfoLog.Items.Clear();
            lock (_timeline)
            {
                for (int i = 0; i < _timeline.Count; i++)
                    _lbInfoLog.Items.Add(_timeline[i].ToString());
            }

            UpdateNav();

            Thread t = new Thread(new ThreadStart(CollectWorker));
            t.IsBackground = true;
            t.Start();
        }

        private void CollectWorker()
        {
            try
            {
                Log("初始化", "程序版本 " + Program.AppVersion +
                    " / " + Environment.OSVersion.VersionString +
                    " / " + (Environment.Is64BitProcess ? "64 位进程" : "32 位进程"));
                Log("初始化", "当前时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Log("初始化", "主题强调色 " + ThemeUtil.ColorToHex(ThemeUtil.AccentColor) +
                    (ThemeUtil.IsDarkAppTheme ? "（深色主题）" : "（浅色主题）"));

                Log("步骤", "【1/4】枚举 RawInput 键盘设备……");
                List<KeyboardDeviceInfo> devs = DeviceInfoCollector.EnumerateRawKeyboards(
                    new DeviceInfoCollector.LogHandler(Log));

                Log("步骤", "【2/4】读取每台设备的 WMI / 驱动信息……");
                for (int i = 0; i < devs.Count; i++)
                {
                    Log("WMI", "(" + (i + 1) + "/" + devs.Count + ") 正在解析：" + devs[i].DevicePath);
                    DeviceInfoCollector.EnrichWithWmi(devs[i], new DeviceInfoCollector.LogHandler(Log));
                }
                _devices = devs;

                Log("步骤", "【3/4】读取系统级键盘参数……");
                _sysInfo = DeviceInfoCollector.CollectSystemKeyboardInfo(
                    new DeviceInfoCollector.LogHandler(Log));

                Log("步骤", "【4/4】读取键盘相关驱动文件版本……");
                List<string[]> rows = new List<string[]>();
                DeviceInfoCollector.CollectKeyboardDriverFiles(
                    new DeviceInfoCollector.LogHandler(Log), rows);
                _driverRows = rows;

                Log("步骤", "【5/5】读取当前用户、用户组与硬件概况……");
                _machine = DeviceInfoCollector.CollectMachineInfo(new DeviceInfoCollector.LogHandler(Log));

                Log("完成", "信息采集完成，共发现 " + _devices.Count + " 个键盘设备。");
            }
            catch (Exception ex)
            {
                Log("错误", "信息采集过程中出现异常：" + ex.Message);
            }

            try
            {
                BeginInvoke(new MethodInvoker(InfoCollectionFinished));
            }
            catch
            {
            }
        }

        private void InfoCollectionFinished()
        {
            _infoDone = true;
            _pbInfo.Style = ProgressBarStyle.Continuous;
            _pbInfo.MarqueeAnimationSpeed = 0;
            _pbInfo.Value = 100;
            _lblInfoTitle.Text = "信息采集完成，可点击『下一步』进入逐键检测。";
            UpdateNav();
        }

        /* ====================================================================
         *  逐键检测
         * ==================================================================== */

        private void StartTest()
        {
            if (_keyboard == null)
                return;

            _testing = true;
            _tested = true;
            _testStart = DateTime.Now;
            _modCtrl = _modAlt = _modShift = false;
            _lastReturnDown = DateTime.MinValue;
            _skippedCount = 0;

            if (_hook != null)
                _hook.IsBlocking = _chkBlock.Checked;

            // 按键盘图顺序建立引导队列
            _testQueue = new List<KeyboardPanel.KeyState>();
            for (int i = 0; i < _keyboard.States.Count; i++)
                _testQueue.Add(_keyboard.States[i]);
            _testIndex = 0;
            _guideElapsedMs = 0;

            if (_guideTimer == null)
            {
                _guideTimer = new System.Windows.Forms.Timer();
                _guideTimer.Interval = 100;
                _guideTimer.Tick += delegate { OnGuideTick(); };
            }
            _guideTimer.Start();

            LayoutKeyboard();
            AdvanceGuide();

            Log("检测", "=== 引导式逐键检测开始（" + DateTime.Now.ToString("HH:mm:ss.fff") + "）===");
            Log("检测", "键盘接管状态：" + (_chkBlock.Checked ? "已开启" : "已关闭") +
                "；引导队列 " + _testQueue.Count + " 个键位，单键响应窗口 " +
                (GuideTimeoutMs / 1000) + " 秒。");
            Log("检测", "结束方式：双击回车 / 点击左下角『完成检测』/ Ctrl+Alt+Shift+Q 紧急停止。");

            AppendLive("引导式检测开始。请按闪烁提示逐个按键。");
            _lblNowKey.Text = "当前按键：—";
        }

        /// <summary>前进到下一个尚未完成的引导键；队列走完则自动结束检测。</summary>
        private void AdvanceGuide()
        {
            if (!_testing || _keyboard == null)
                return;

            // 跳过用户在自由按压阶段已经点亮过的键
            while (_testIndex < _testQueue.Count && _testQueue[_testIndex].WasCaptured)
                _testIndex++;

            if (_testIndex >= _testQueue.Count)
            {
                _keyboard.SetGuide(0);
                _lblTarget.Text = "全部按键已走完，正在生成总结报告……";
                FinishTest(false);
                return;
            }

            _guideElapsedMs = 0;
            _keyboard.SetGuide(_testQueue[_testIndex].Def.Id);
            UpdateTargetLabel();
            RefreshTestProgress();
        }

        private void UpdateTargetLabel()
        {
            if (_keyboard == null || _testQueue == null)
                return;
            if (_testIndex >= _testQueue.Count)
                return;

            KeyboardPanel.KeyState st = _testQueue[_testIndex];
            int remain = GuideTimeoutMs - _guideElapsedMs;
            if (remain < 0) remain = 0;

            _lblTarget.Text = string.Format(CultureInfo.InvariantCulture,
                "请按下：  {0}      剩余 {1:F1} 秒      [ 第 {2} / {3} 个 ]",
                st.Def.Name, remain / 1000.0, _testIndex + 1, _testQueue.Count);
        }

        /// <summary>100ms 心跳：驱动引导键闪烁 + 3 秒超时自动跳过。</summary>
        private void OnGuideTick()
        {
            if (!_testing || _keyboard == null || _testQueue == null)
                return;
            if (_testIndex >= _testQueue.Count)
                return;

            _guideElapsedMs += 100;

            // 每 500ms 切换一次闪烁相位
            _keyboard.SetFlash(((_guideElapsedMs / 500) % 2) == 0);
            UpdateTargetLabel();

            if (_guideElapsedMs < GuideTimeoutMs)
                return;

            KeyboardPanel.KeyState st = _testQueue[_testIndex];
            if (!st.WasCaptured)
            {
                st.TimedOut = true;
                _skippedCount++;
                _keyboard.RefreshKey(st);
                AppendLive("超时跳过  " + st.Def.Name + "  （" +
                    (GuideTimeoutMs / 1000) + " 秒内未收到硬件反馈）");
                Log("跳过", "键位 " + st.Def.Name + " 在 " +
                    (GuideTimeoutMs / 1000) + " 秒内未响应，已自动跳到下一个。");
            }

            _testIndex++;
            AdvanceGuide();
        }

        private void FinishTest(bool emergency)
        {
            if (_guideTimer != null)
                _guideTimer.Stop();
            if (_keyboard != null)
                _keyboard.SetGuide(0);

            if (_testing)
            {
                _testing = false;
                _testEnd = DateTime.Now;
                if (_hook != null)
                    _hook.IsBlocking = false;   // 立即归还键盘控制权

                Log("检测", "=== 引导式逐键检测结束（" +
                    (_testEnd - _testStart).TotalSeconds.ToString("F2") + " 秒）===");
                Log("检测", "已完成 " + _keyboard.CapturedKeyCount + " / " +
                    _keyboard.TotalKeyCount + " 个键位，自动跳过 " + _skippedCount + " 个。");
                if (emergency)
                    Log("检测", "用户使用紧急组合键中止了接管。");

                AppendLive("检测结束。已完成 " + _keyboard.CapturedKeyCount + " / " +
                    _keyboard.TotalKeyCount + "，跳过 " + _skippedCount + "。");
                _lblTarget.Text = "检测已结束，正在生成总结报告……";
            }

            if (_step == 2)
                GoToStep(3);
        }

        private void OnKeyboardEvent(object sender, KeyboardHookEventArgs e)
        {
            // 紧急停止组合键：Ctrl + Alt + Shift + Q
            if (e.VirtualKey == 0x11 || e.VirtualKey == 0xA2 || e.VirtualKey == 0xA3)
                _modCtrl = e.IsDown;
            else if (e.VirtualKey == 0x12 || e.VirtualKey == 0xA4 || e.VirtualKey == 0xA5)
                _modAlt = e.IsDown;
            else if (e.VirtualKey == 0x10 || e.VirtualKey == 0xA0 || e.VirtualKey == 0xA1)
                _modShift = e.IsDown;

            if (e.IsDown && e.VirtualKey == 0x51 && _modCtrl && _modAlt && _modShift)
            {
                if (_testing)
                    FinishTest(true);
                return;
            }

            if (!_testing || _keyboard == null)
                return;

            // 双击回车 = 结束检测（回车坏掉时用左下角『完成检测』按钮）
            if (e.IsDown && e.VirtualKey == 0x0D)
            {
                DateTime now = DateTime.Now;
                if ((now - _lastReturnDown).TotalMilliseconds <= DoubleReturnMs)
                {
                    AppendLive("检测到双击回车 —— 结束检测。");
                    Log("检测", "用户双击回车，主动结束检测。");
                    FinishTest(false);
                    return;
                }
                _lastReturnDown = now;
            }

            KeyboardPanel.KeyState st = _keyboard.HandleKey(
                e.VirtualKey, e.ScanCode, e.IsExtended, e.IsDown, e.IsInjected);

            if (e.IsDown)
            {
                if (st == null)
                {
                    AppendLive("未映射按键  " + KeyboardLayout.VirtualKeyName(e.VirtualKey) +
                        "  SC=0x" + e.ScanCode.ToString("X2") +
                        (e.IsExtended ? " [EXT]" : "") +
                        (e.IsInjected ? " [注入]" : ""));
                    return;
                }

                _lblNowKey.Text = string.Format(CultureInfo.InvariantCulture,
                    "当前按键：{0}    VK=0x{1:X2} ({2})    SC=0x{3:X2}{4}{5}",
                    st.Def.Name, e.VirtualKey, KeyboardLayout.VirtualKeyName(e.VirtualKey),
                    e.ScanCode,
                    e.IsExtended ? "  [扩展键]" : "",
                    e.IsInjected ? "  [软件注入]" : "");
            }
        }

        private void OnKeyCaptured(object sender, KeyboardPanel.KeyState st)
        {
            bool wasGuide = (_keyboard != null && st.Def.Id == _keyboard.GuideKeyId);

            AppendLive("已点亮  " + st.Def.Name + "   (VK=0x" + st.Def.Vk.ToString("X2") +
                ", 进度 " + _keyboard.CapturedKeyCount + "/" + _keyboard.TotalKeyCount + ")");
            Log("捕获", "键位 " + st.Def.Name + " 收到硬件反馈，已点亮。");

            RefreshTestProgress();

            // 按下的正是当前引导键 → 立即进入下一个
            if (wasGuide && _testing)
                AdvanceGuide();
        }

        private void RefreshTestProgress()
        {
            if (_keyboard == null)
                return;

            int total = _keyboard.TotalKeyCount;
            int done = _keyboard.CapturedKeyCount;

            if (_pbTest.Maximum != total)
                _pbTest.Maximum = total;
            _pbTest.Value = Math.Min(done, total);

            double pct = total == 0 ? 0 : (done * 100.0 / total);
            _lblTestProgress.Text = string.Format(CultureInfo.InvariantCulture,
                "已点亮 {0} / {1}   ({2:F1}%)    ·    已跳过 {3}",
                done, total, pct, _skippedCount);

            RefreshMissingList();
        }

        private void RefreshMissingList()
        {
            if (_lvMissing == null || _keyboard == null)
                return;

            // 简单节流：状态签名没变就不重建
            string sig = _keyboard.CapturedKeyCount + "/" + _skippedCount;
            if ((_lvMissing.Tag as string) == sig)
                return;
            _lvMissing.Tag = sig;

            _lvMissing.BeginUpdate();
            try
            {
                _lvMissing.Items.Clear();
                foreach (KeyboardPanel.KeyState st in _keyboard.States)
                {
                    if (st.WasCaptured)
                        continue;

                    ListViewItem it = new ListViewItem(
                        st.Def.Id.ToString(CultureInfo.InvariantCulture));
                    it.SubItems.Add(st.Def.Name);
                    it.SubItems.Add("0x" + st.Def.Vk.ToString("X2"));
                    it.SubItems.Add(RegionOf(st.Def));
                    if (st.TimedOut)
                        it.ForeColor = SystemColors.GrayText;
                    _lvMissing.Items.Add(it);
                }
            }
            finally
            {
                _lvMissing.EndUpdate();
            }
        }

        private static string RegionOf(KeyDef k)
        {
            if (k.X < 15.0) return "主键区";
            if (k.X < 19.0) return "导航/编辑区";
            return "数字小键盘";
        }

        private void AppendLive(string text)
        {
            if (_txtLive == null)
                return;
            try
            {
                if (_txtLive.TextLength > 30000)
                    _txtLive.Clear();
                _txtLive.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + "\r\n");
            }
            catch
            {
            }
        }

        /* ====================================================================
         *  总结页面
         * ==================================================================== */

        /* ====================================================================
         *  导出
         * ==================================================================== */

        private void ExportReport()
        {
            TimeSpan dur = (_tested && _testEnd > _testStart) ? (_testEnd - _testStart) : TimeSpan.Zero;

            string text = ReportExporter.BuildText(
                _keyboard, _devices, _sysInfo, _driverRows, _timeline, dur);

            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Title = "导出检测报告";
            dlg.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            dlg.FileName = ReportExporter.DefaultFileName();

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                desktop = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
                dlg.InitialDirectory = desktop;

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            string err = ReportExporter.SafeWrite(dlg.FileName, text);
            if (err != null)
            {
                MessageBox.Show(this, "保存报告失败：\r\n" + err, Program.AppTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Log("导出", "报告已保存到 " + dlg.FileName);
            MessageBox.Show(this, "报告已保存到：\r\n" + dlg.FileName, Program.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /* ====================================================================
         *  日志
         * ==================================================================== */

        private void Log(string stage, string message)
        {
            TimelineEntry entry = new TimelineEntry(stage, message);
            lock (_timeline)
            {
                _timeline.Add(entry);
            }

            if (!IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new MethodInvoker(delegate { AppendLogLine(entry.ToString()); }));
            }
            catch
            {
            }
        }

        private void AppendLogLine(string line)
        {
            if (_lbInfoLog == null)
                return;
            _lbInfoLog.Items.Add(line);
            if (_lbInfoLog.Items.Count > 800)
                _lbInfoLog.Items.RemoveAt(0);
            if (_lbInfoLog.Items.Count > 0)
                _lbInfoLog.TopIndex = _lbInfoLog.Items.Count - 1;
        }

        /* ====================================================================
         *  生命周期
         * ==================================================================== */

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutKeyboard();
            LayoutResultKeyboard();

            // 触摸屏：给所有列表 / 文本框挂上拖动滚动
            TouchScroll.Enable(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_testing)
            {
                DialogResult r = MessageBox.Show(this,
                    "检测正在进行中。关闭程序将立即归还键盘控制权。\r\n\r\n确定要关闭吗？",
                    Program.AppTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (r != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (_hook != null)
            {
                _hook.IsBlocking = false;
                _hook.Uninstall();
                _hook.Dispose();
                _hook = null;
            }

            if (_guideTimer != null)
            {
                _guideTimer.Stop();
                _guideTimer.Dispose();
                _guideTimer = null;
            }

            base.OnFormClosing(e);
        }
    }
}