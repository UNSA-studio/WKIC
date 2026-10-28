using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace WinKbdCheck.Controls;

public sealed partial class KeyboardView : UserControl
{
    private readonly Dictionary<int, Button> _buttons = new();
    private readonly Dictionary<int, TextBlock> _labels = new();
    private readonly Dictionary<int, string> _visual = new();
    private readonly UISettings _settings = new();
    private readonly AccessibilitySettings _accessibility = new();
    private List<KeyRecord> _records;
    private int _selected;
    private bool _result;
    internal event Action<KeyRecord> KeySelected;
    internal int RenderedKeyCount => _buttons.Count;
    internal double BoardWidth => Board.ActualWidth;

    public KeyboardView()
    {
        InitializeComponent();
        foreach (var def in KeyboardLayout.Keys)
        {
            var label = new TextBlock { Text = def.Label, FontSize = def.Label.Length > 4 ? 10 : 12,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { Content = label, Padding = new Thickness(1), MinWidth = 0, MinHeight = 0,
                CornerRadius = new CornerRadius(4), IsTabStop = false, Tag = def.Id,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(button, def.Name);
            ToolTipService.SetToolTip(button, def.Sub == null ? def.Name : $"{def.Name}  /  Shift: {def.Sub}");
            button.Click += (_, _) => Select(def.Id);
            _buttons[def.Id] = button;
            _labels[def.Id] = label;
            Board.Children.Add(button);
        }
        SizeChanged += (_, _) => ArrangeKeys();
        Loaded += (_, _) => ArrangeKeys();
        ActualThemeChanged += (_, _) => _visual.Clear();
        _settings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(() => _visual.Clear());
    }

    internal void SetRecords(List<KeyRecord> records, bool result)
    {
        _records = records;
        _result = result;
        _selected = 0;
        _visual.Clear();
        Render(0, true);
    }

    internal void Select(int id)
    {
        if (!_result) return; // clicking a picture never counts as physical input
        var key = _records?.FirstOrDefault(k => k.Def.Id == id);
        if (key == null) return;
        _selected = id;
        Render(0, true);
        KeySelected?.Invoke(key);
    }

    private void ArrangeKeys()
    {
        double width = Math.Max(760, ActualWidth > 0 ? ActualWidth - 8 : 1012);
        double unit = (width - 12) / KeyboardLayout.TotalUnitsX;
        Board.Width = width;
        Board.Height = KeyboardLayout.TotalUnitsY * unit + 12;
        Height = Board.Height + 18;
        foreach (var def in KeyboardLayout.Keys)
        {
            var button = _buttons[def.Id];
            Canvas.SetLeft(button, 6 + def.X * unit);
            Canvas.SetTop(button, 6 + def.Y * unit);
            button.Width = Math.Max(22, def.W * unit - 3);
            button.Height = Math.Max(22, def.H * unit - 3);
        }
    }

    internal void Render(int guideId, bool phase)
    {
        if (_records == null) return;
        bool highContrast = _accessibility.HighContrast;
        Color accent = _settings.GetColorValue(UIColorType.Accent);
        foreach (var key in _records)
        {
            bool guide = !_result && key.Def.Id == guideId;
            string state = $"{key.Result}/{key.IsDown}/{key.Excluded}/{guide}/{(guide && phase)}/{_selected}/{ActualTheme}/{accent}/{highContrast}";
            if (_visual.TryGetValue(key.Def.Id, out var old) && old == state) continue;
            _visual[key.Def.Id] = state;
            var button = _buttons[key.Def.Id];
            var label = _labels[key.Def.Id];
            label.Text = key.Def.Label;
            button.Opacity = key.Excluded ? .35 : 1;
            button.BorderThickness = new Thickness(key.Def.Id == _selected || guide ? 2 : 1);
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            button.ClearValue(Control.BorderBrushProperty);
            if (highContrast)
            {
                button.Background = new SolidColorBrush(_settings.GetColorValue(UIColorType.Background));
                button.Foreground = new SolidColorBrush(_settings.GetColorValue(UIColorType.Foreground));
                button.BorderBrush = new SolidColorBrush(accent);
            }
            else if (!key.Excluded && (_result || key.DownCount > 0 || guide))
            {
                Color fill = accent;
                if (_result)
                {
                    fill = key.Result switch
                    {
                        KeyResult.Responded => Color.FromArgb(255, 16, 124, 16),
                        KeyResult.NoResponse => Color.FromArgb(255, 196, 43, 28),
                        KeyResult.ReleaseMissing => Color.FromArgb(255, 146, 90, 0),
                        _ => Color.FromArgb(255, 100, 100, 100)
                    };
                }
                bool solid = _result || key.DownCount > 0 || guide && phase;
                button.Background = new SolidColorBrush(solid ? fill : Color.FromArgb(35, accent.R, accent.G, accent.B));
                double luminance = .2126 * fill.R + .7152 * fill.G + .0722 * fill.B;
                button.Foreground = new SolidColorBrush(!solid ? accent : luminance > 170 ? Colors.Black : Colors.White);
                button.BorderBrush = new SolidColorBrush(key.Def.Id == _selected ? accent : fill);
            }
            AutomationProperties.SetHelpText(button, key.Status);
            ToolTipService.SetToolTip(button, $"{key.Def.Name} · {key.Status}");
        }
    }
}