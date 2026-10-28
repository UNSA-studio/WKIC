using Microsoft.UI.Xaml;

namespace WinKbdCheck;

public partial class App : Application
{
    private MainWindow _window;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            _window?.ReleaseKeyboard();
            e.Handled = true;
            if (Program.SmokeMode)
            {
                Directory.CreateDirectory(Program.SmokeDirectory);
                File.WriteAllText(Path.Combine(Program.SmokeDirectory, "ui-error.txt"), e.Exception.ToString());
                Environment.Exit(3);
            }
            Program.ShowError("界面发生错误，键盘接管已解除：\n" + e.Message);
            _window?.Close();
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}