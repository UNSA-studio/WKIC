using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WinKbdCheck.Controls;

namespace WinKbdCheck;

public sealed partial class MainWindow : Window
{
    private readonly TestSession _session = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private KeyboardCapture _capture;
    private Inventory _inventory = new();
    private bool _collecting, _inventoryReady, _closed;
    private bool _compact, _block = true;
    private KeyboardView _liveKeyboard, _resultKeyboard;
    private TextBlock _target, _progress, _liveLog, _detailsTitle, _scanLog;
    private ProgressBar _deadline, _coverage;
    private StackPanel _details;
    private ScrollViewer _welcomeScroll;
    private InfoBar _notice;
    private readonly nint _hwnd;
    private string _page = "";
    private KeyRecord _lastKey;

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "assets", "app.ico"));
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min(1260, work.Width - 30), Math.Min(920, work.Height - 30)));
        AppWindow.Move(new PointInt32(work.X + Math.Max(0, (work.Width - AppWindow.Size.Width) / 2),
            work.Y + Math.Max(0, (work.Height - AppWindow.Size.Height) / 2)));
        try { SystemBackdrop = new MicaBackdrop(); } catch { /* Windows 10 keeps the native solid background. */ }
        _timer.Tick += (_, _) => PumpInput();
        Closed += (_, _) => { _closed = true; _closing.Cancel(); ReleaseKeyboard(); _capture?.Dispose(); };
        Activated += (_, e) =>
        {
            if (!Program.SmokeMode && _session.Running && e.WindowActivationState == WindowActivationState.Deactivated)
                StopTest("窗口失去焦点，已安全归还键盘；可在结果页重新检测");
        };
        Nav.SelectedItem = WelcomeItem;
        if (Program.SmokeMode) Root.Loaded += async (_, _) => await SmokeTest();
    }

    public void ReleaseKeyboard() { _timer.Stop(); _capture?.Stop(); }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (PageContent == null) return;
        string page = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "welcome";
        if (_session.Running && page != "test") { StopTest("离开测试页面"); return; }
        ShowPage(page);
    }
    private void ShowPage(string page)
    {
        _page = page;
        PageContent.Content = page switch
        {
            "inventory" => InventoryPage(), "test" => TestPage(), "results" => ResultsPage(),
            "settings" => SettingsPage(), _ => WelcomePage()
        };
    }
    private void Navigate(NavigationViewItem item)
    {
        item.IsEnabled = true;
        if (Nav.SelectedItem == item) ShowPage(item.Tag.ToString()); else Nav.SelectedItem = item;
    }

    private UIElement WelcomePage()
    {
        var body = new StackPanel { Spacing = 18 };
        body.Children.Add(Text("开始之前", 24));
        body.Children.Add(Text("这是使用说明与风险告知。请阅读后勾选确认，再进入检测。", 15));
        AddSection(body, "01  怎样检测", "按照键盘图上闪烁的提示逐个按下并松开按键。收到反馈的键会变成当前 Windows 强调色，并保持点亮。每个键有 3 秒等待时间；超时后自动跳到下一个。已按过的键无需再次按下，跳过的键也可以在检测结束前补测。");
        AddSection(body, "02  怎样结束", "连续按下并松开同一颗回车键两次即可结束（两次间隔不超过 450 毫秒）。长按自动重复不会被误判成双击。回车键损坏时，请用鼠标或触屏点击左下角的“完成检测”。Ctrl + Alt + Shift + Q 可紧急归还键盘控制权。");
        AddSection(body, "03  键盘接管", "本程序需要管理员权限。仅在测试期间开启全局键盘钩子；开启接管后，Win 键和通常的快捷键不会传到开始菜单或其他窗口。鼠标和触屏不受接管影响。关闭窗口、离开测试页、界面超时或紧急停止都会解除接管。Ctrl + Alt + Del 等安全桌面操作不能被拦截，Fn 等固件按键也可能不向 Windows 发送事件。");
        AddSection(body, "04  如何理解结果", "绿色表示本次收到按下和抬起事件；红色表示引导超时未响应，属于疑似故障、需要复测；琥珀色表示缺少抬起反馈；灰色表示未检测。没有按到、键盘无此键、Win Lock 游戏模式、NumLock、布局和驱动均可能影响结果。检测是所有已连接键盘的合并输入，不能声称结果属于某一台指定键盘。");
        AddSection(body, "05  信息与隐私", "本程序在本机读取键盘设备、驱动版本、驱动日期、账户、用户组、CPU、内存和主板等信息。只在测试期间记录按键，退出即停止，不上传按键或硬件数据。只有点击“导出报告”才把报告写到你选择的位置。硬件出厂年份无法读取时会明确显示未知，不会用驱动日期冒充。");
        AddSection(body, "06  关于本软件", $"开发者：UNSA-studio\n联系邮箱：UNSA-studio@outlook.com\n版本：{Program.AppVersion} · 真正的 WinUI 3 / Windows App SDK 界面\n本程序不是微软出品。免费自签名不等于受信任 CA 签名，不保证消除 SmartScreen 提示。支持 Windows 10 1809 及以上版本。免安装版必须完整解压全部运行库，不能只复制 EXE。");
        _welcomeScroll = Scroll(body);
        var consent = new CheckBox { Content = "我已阅读说明，并同意在测试期间接管键盘输入。" };
        var next = Button("同意并检测设备", async () => await CollectInventory(), true);
        next.IsEnabled = false;
        consent.Checked += (_, _) => next.IsEnabled = true;
        consent.Unchecked += (_, _) => next.IsEnabled = false;
        var layout = TwoRows(_welcomeScroll, Stack(consent, next));
        return layout;
    }

    private UIElement InventoryPage()
    {
        var body = Stack(Text("设备信息与采集过程", 24));
        body.Children.Add(new ProgressBar { IsIndeterminate = _collecting, Value = _collecting ? 0 : 100 });
        _scanLog = Text(string.Join("\n", _inventory.Logs), 13);
        body.Children.Add(Card(Stack(Text("实时过程", 18), _scanLog)));
        if (!_collecting)
        {
            body.Children.Add(FactsCard("当前用户、用户组与系统", _inventory.MachineFacts()));
            foreach (var device in _inventory.Devices)
                body.Children.Add(FactsCard("键盘 · " + device.ShortName, _inventory.DeviceFacts(device)));
        }
        var next = Button(_collecting ? "采集中…" : "开始按键检测", StartTest, true);
        next.IsEnabled = !_collecting;
        return TwoRows(Scroll(body), Stack(Text("设备采集失败不等于按键失灵；未知字段会原样保留。"), next));
    }

    private async Task CollectInventory()
    {
        if (_collecting) return;
        _collecting = true;
        _inventory = new Inventory();
        Navigate(InventoryItem);
        try
        {
            var inventory = await Task.Run(() => Inventory.Collect(line =>
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_page == "inventory" && _scanLog != null) _scanLog.Text += line + "\n";
                }), _closing.Token));
            if (_closed) return;
            _inventory = inventory;
            _inventoryReady = true;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _inventory.Logs.Add("采集未全部完成：" + ex.Message);
            _inventoryReady = true;
        }
        finally { _collecting = false; }
        if (!_closed && _page == "inventory") ShowPage("inventory");
        TestItem.IsEnabled = true;
    }

    private UIElement TestPage()
    {
        _target = Text("准备检测", 24);
        _progress = Text("");
        _deadline = new ProgressBar { Maximum = TestSession.TimeoutMs, Value = TestSession.TimeoutMs };
        _coverage = new ProgressBar { Maximum = Math.Max(1, _session.Required) };
        _liveLog = Text("", 13);
        _liveKeyboard = new KeyboardView();
        _liveKeyboard.SetRecords(_session.Keys, false);
        var body = Stack(_target, _deadline, _progress, _coverage,
            Text("闪烁 = 等待按下；主题色 = 收到反馈。请按下并松开。", 14), Card(_liveKeyboard),
            Card(Stack(Text("实时反馈", 18), _liveLog)));
        var finish = Button("完成检测", () => StopTest("用户点击完成检测"), true);
        var reset = Button("从头检测", StartTest);
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(finish);
        var hint = Text("双击回车结束 · Ctrl+Alt+Shift+Q 紧急停止", 13); Grid.SetColumn(hint, 1); footer.Children.Add(hint);
        Grid.SetColumn(reset, 2); footer.Children.Add(reset);
        return TwoRows(Scroll(body), footer);
    }

    private void StartTest()
    {
        if (_collecting) return;
        if (!_inventoryReady && !Program.SmokeMode) { _ = CollectInventory(); return; }
        try
        {
            ReleaseKeyboard();
            if (!Program.SmokeMode)
            {
                _capture ??= new KeyboardCapture();
                _capture.Install();
            }
            _lastKey = null;
            _session.Start(_compact);
            Navigate(TestItem);
            _capture?.Begin(_block);
            WelcomeItem.IsEnabled = InventoryItem.IsEnabled = ResultsItem.IsEnabled = false;
            _timer.Start();
            RefreshTest();
        }
        catch (Exception ex) { ReleaseKeyboard(); _session.Stop("无法接管键盘"); Program.ShowError(ex.Message); }
    }

    private void PumpInput()
    {
        if (!_session.Running) return;
        _capture?.Heartbeat();
        int count = 0;
        if (_capture != null)
        {
            string interruption = _capture.TakeInterrupt();
            if (interruption != null) { StopTest(interruption); return; }
            while (count++ < 1024 && _capture.TryRead(out var input))
            {
                var key = _session.Handle(input);
                if (key != null) _lastKey = key;
                if (!_session.Running) break;
            }
        }
        _session.Tick();
        if (!_session.Running) { StopTest(_session.FinishReason); return; }
        RefreshTest();
    }
    private void RefreshTest()
    {
        var key = _session.Target;
        if (_target == null) return;
        _target.Text = key == null ? "检测完成" : key.IsDown ? $"请松开：{key.Def.Name}" : $"请按下：{key.Def.Name}  ·  {_session.RemainingMs / 1000.0:F1} 秒";
        _deadline.Value = _session.RemainingMs;
        _coverage.Maximum = Math.Max(1, _session.Required);
        _coverage.Value = _session.Responded;
        _progress.Text = $"完整响应 {_session.Responded} / {_session.Required}　·　超时 {_session.Skipped}　·　当前第 {_session.Position} 个";
        _liveKeyboard.Render(key?.Def.Id ?? 0, (Environment.TickCount64 / 500) % 2 == 0);
        string feedback = _lastKey == null ? "尚未收到按键反馈" : $"{_lastKey.Def.Name} · {_lastKey.Status} · SC=0x{_lastKey.ScanCode:X2}";
        _liveLog.Text = feedback + "\n" + string.Join("\n", _session.Timeline.TakeLast(6));
    }
    private void StopTest(string reason)
    {
        ReleaseKeyboard();
        _session.Stop(reason);
        WelcomeItem.IsEnabled = InventoryItem.IsEnabled = true;
        Navigate(ResultsItem);
    }

    private UIElement ResultsPage()
    {
        var stats = new (string, string)[]
        {
            ("开始时间", _session.Started.ToString("yyyy-MM-dd HH:mm:ss")),
            ("结束原因", _session.FinishReason), ("用时", $"{_session.Duration.TotalSeconds:F1} 秒"),
            ("完整响应 / 待复测", $"{_session.Responded} / {_session.Unfinished}"),
            ("超时未响应", _session.Skipped.ToString()), ("忽略的软件注入事件", _session.IgnoredInjected.ToString())
        };
        var overview = Stack(FactsCard("当前检测数据", stats), FactsCard("账户、用户组与硬件", _inventory.MachineFacts()));
        foreach (var d in _inventory.Devices)
            overview.Children.Add(new Expander { Header = d.ShortName, HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = Facts(_inventory.DeviceFacts(d)) });
        overview.Children.Add(new Expander { Header = "驱动文件与采集过程", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Text(string.Join("\n", _inventory.Drivers.Select(r => string.Join(" | ", r))) + "\n" + string.Join("\n", _inventory.Logs), 13) });
        var overviewScroll = Scroll(overview);
        overviewScroll.MaxHeight = Math.Max(260, Root.ActualHeight - 240);
        _resultKeyboard = new KeyboardView();
        _resultKeyboard.SetRecords(_session.Keys, true); // SAME state records, not a fresh empty keyboard
        _resultKeyboard.KeySelected += ShowKey;
        _detailsTitle = Text("单键记录", 20);
        _details = Stack(Text("点击键盘上的任意一个键，查看它在本次检测中的记录。"));
        var right = Stack(Text("绿色：完整响应　红色：超时未响应 / 疑似故障　琥珀：缺抬起　灰色：未检测", 14),
            Card(_resultKeyboard), Card(Stack(_detailsTitle, _details)));
        var columns = new Grid { ColumnSpacing = 20, RowSpacing = 20 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        columns.Children.Add(overviewScroll); Grid.SetColumn(right, 1); columns.Children.Add(right);
        void Reflow()
        {
            bool narrow = Root.ActualWidth < 1000;
            columns.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(330);
            columns.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(right, narrow ? 0 : 1); Grid.SetRow(right, narrow ? 1 : 0);
            overviewScroll.MaxHeight = narrow ? 260 : Math.Max(260, Root.ActualHeight - 240);
        }
        columns.SizeChanged += (_, _) => Reflow();
        Reflow();
        var content = Stack(new InfoBar { IsOpen = true, IsClosable = false,
            Severity = _session.Unfinished == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            Title = $"完整响应 {_session.Responded} / {_session.Required}",
            Message = "未响应不等于已确定损坏：请先确认按过该键、布局和 Win Lock。多键盘输入为合并结果。" }, columns);
        _notice = new InfoBar { IsOpen = false, IsClosable = true };
        var footer = Stack(_notice, Horizontal(Button("重新检测", StartTest, true), Button("导出完整报告", ExportReport)));
        return TwoRows(Scroll(content), footer);
    }
    private void ShowKey(KeyRecord key)
    {
        _detailsTitle.Text = key.Def.Name + " · " + key.Status;
        _details.Children.Clear();
        _details.Children.Add(Facts(Report.KeyFacts(key)));
        if (key.Events.Count > 0) _details.Children.Add(Text(string.Join("\n", key.Events), 13));
    }
    private void ExportReport()
    {
        try
        {
            string path = Report.Save(_hwnd, Report.Build(_session, _inventory));
            if (path == null) return;
            _notice.Severity = InfoBarSeverity.Success; _notice.Title = "报告已保存"; _notice.Message = path; _notice.IsOpen = true;
        }
        catch (Exception ex) { _notice.Severity = InfoBarSeverity.Error; _notice.Title = "保存失败"; _notice.Message = ex.Message; _notice.IsOpen = true; }
    }

    private UIElement SettingsPage()
    {
        var theme = new ComboBox { Header = "界面主题", SelectedIndex = (int)Root.RequestedTheme, MinWidth = 220 };
        theme.Items.Add("跟随 Windows"); theme.Items.Add("浅色"); theme.Items.Add("深色");
        theme.SelectedIndex = (int)Root.RequestedTheme;
        theme.SelectionChanged += (_, _) => { if (theme.SelectedIndex >= 0) Root.RequestedTheme = (ElementTheme)theme.SelectedIndex; };
        var block = new ToggleSwitch { Header = "测试期间接管键盘", IsOn = _block };
        block.Toggled += (_, _) => { _block = block.IsOn; _capture?.SetBlocking(_block); };
        var layout = new ComboBox { Header = "检测布局", MinWidth = 260 };
        layout.Items.Add("ANSI 标准全尺寸（104 键）"); layout.Items.Add("TKL 无数字小键盘（87 键）");
        layout.SelectedIndex = _compact ? 1 : 0;
        layout.SelectionChanged += (_, _) => _compact = layout.SelectedIndex == 1;
        return Scroll(Stack(Text("设置", 24), Card(Stack(theme, block, layout)),
            Text("开关立即生效；布局变更应用于下一次检测。WinUI 3 使用设备无关单位，不再手动倍乘字体或控件尺寸。"),
            FactsCard("关于", new[] { ("名称", Program.AppTitle), ("版本", Program.AppVersion), ("界面", "WinUI 3 / Windows App SDK"),
                ("作者", "UNSA-studio"), ("邮箱", "UNSA-studio@outlook.com"),
                ("签名", "免费自签名；不是微软签名，也不保证免除 SmartScreen 提示。") })));
    }

    private static TextBlock Text(string text, double size = 14) => new()
    {
        Text = text ?? "未知", FontSize = size, TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.None, IsTextSelectionEnabled = true
    };
    private static StackPanel Stack(params UIElement[] children)
    {
        var stack = new StackPanel { Spacing = 14 };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }
    private static StackPanel Horizontal(params UIElement[] children)
    {
        var stack = Stack(children); stack.Orientation = Orientation.Horizontal; return stack;
    }
    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Enabled,
        HorizontalScrollMode = ScrollMode.Disabled, ZoomMode = ZoomMode.Disabled, Padding = new Thickness(0, 0, 8, 8)
    };
    private static Border Card(UIElement content) => new()
    {
        Child = content, Padding = new Thickness(18), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
    };
    private static Button Button(string title, Action click, bool accent = false)
    {
        var button = new Button { Content = title, MinHeight = 40, Padding = new Thickness(18, 8, 18, 8) };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += (_, _) => click();
        return button;
    }
    private static Grid TwoRows(UIElement body, UIElement footer)
    {
        var grid = new Grid { RowSpacing = 16 };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(body); Grid.SetRow(footer, 1); grid.Children.Add(footer); return grid;
    }
    private static void AddSection(StackPanel panel, string title, string text) => panel.Children.Add(Card(Stack(Text(title, 19), Text(text, 15))));
    private static StackPanel Facts(IEnumerable<(string Label, string Value)> facts)
    {
        var stack = new StackPanel { Spacing = 12 };
        foreach (var (label, value) in facts)
        {
            var name = Text(label, 12); name.Opacity = .72;
            stack.Children.Add(Stack(name, Text(string.IsNullOrWhiteSpace(value) ? "未知 / 未公开" : value)));
        }
        return stack;
    }
    private static Border FactsCard(string title, IEnumerable<(string Label, string Value)> facts) => Card(Stack(Text(title, 20), Facts(facts)));

    private async Task SmokeTest()
    {
        try
        {
            Directory.CreateDirectory(Program.SmokeDirectory);
            await Task.Delay(700);
            Root.UpdateLayout();
            double welcomeExtent = _welcomeScroll.ExtentHeight;
            _welcomeScroll.ChangeView(null, 300, null, true);
            await Task.Delay(100);
            _inventoryReady = true;
            StartTest(); // smoke mode never installs or blocks a keyboard hook
            long now = Environment.TickCount64;
            _session.Handle(new KeyInput(0x1B, 1, false, true, false, now, DateTime.Now));
            _session.Handle(new KeyInput(0x1B, 1, false, false, false, now + 50, DateTime.Now));
            StopTest("UI smoke fixture（模拟数据，非硬件测试）");
            await Task.Delay(500);
            Root.UpdateLayout();
            _resultKeyboard.Select(_session.Keys.First(k => k.Def.Vk == 0x1B).Def.Id);
            if (_resultKeyboard.RenderedKeyCount != 104 || _resultKeyboard.BoardWidth < 700 || _resultKeyboard.ActualHeight < 100)
                throw new InvalidOperationException("结果键盘未完成可见布局");
            if (!_detailsTitle.Text.Contains("Esc") || _session.Responded != 1)
                throw new InvalidOperationException("点击记录未使用真实共享的会话状态");
            await Screenshot("results-light.png");
            Root.RequestedTheme = ElementTheme.Dark;
            await Task.Delay(300);
            _resultKeyboard.Render(0, true);
            await Screenshot("results-dark.png");
            AppWindow.Resize(new SizeInt32(820, 700));
            await Task.Delay(300);
            await Screenshot("results-narrow.png");
            string evidence = System.Text.Json.JsonSerializer.Serialize(new { UI = "Microsoft.UI.Xaml", KeyButtons = 104,
                Responded = _session.Responded, ClickRecord = _detailsTitle.Text, WelcomeExtent = welcomeExtent,
                HookInstalled = false, BlockedKeyboard = false, TestData = "synthetic fixture, not hardware certification" });
            File.WriteAllText(Path.Combine(Program.SmokeDirectory, "PASS.json"), evidence);
            Close();
        }
        catch (Exception ex)
        {
            ReleaseKeyboard();
            File.WriteAllText(Path.Combine(Program.SmokeDirectory, "FAIL.txt"), ex.ToString());
            Environment.ExitCode = 3;
            Close();
        }
    }
    private async Task Screenshot(string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(Root);
        var pixels = await bitmap.GetPixelsAsync();
        if (bitmap.PixelWidth == 0 || pixels.Length == 0) throw new InvalidOperationException("WinUI 渲染为空");
        byte[] bytes = new byte[pixels.Length];
        using (var reader = DataReader.FromBuffer(pixels)) reader.ReadBytes(bytes);
        var folder = await StorageFolder.GetFolderFromPathAsync(Program.SmokeDirectory);
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
}