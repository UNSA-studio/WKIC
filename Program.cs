using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.UI.Dispatching;

namespace WinKbdCheck;

internal static class Program
{
    public const string AppTitle = "Windows Keyboard Integrity Check";
    public const string AppTitleCn = "Windows 键盘完整性检测";
    public const string AppVersion = "1.0.0";
    public static bool SmokeMode { get; private set; }
    public static string SmokeDirectory { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        var smoke = args.FirstOrDefault(a => a.StartsWith("--smoke-test=", StringComparison.Ordinal));
        SmokeMode = smoke != null;
        SmokeDirectory = smoke?.Substring("--smoke-test=".Length);
        if (!SmokeMode && !IsAdministrator())
        {
            ShowError("Windows键盘完整性检测需要依靠管理员运行,请使用管理员权限打开");
            Environment.ExitCode = 1;
            return;
        }
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                var app = new App();
            });
        }
        catch (Exception ex)
        {
            if (SmokeMode)
            {
                Directory.CreateDirectory(SmokeDirectory);
                File.WriteAllText(Path.Combine(SmokeDirectory, "startup-error.txt"), ex.ToString());
            }
            else ShowError("程序启动失败：\n" + ex.Message + "\n请完整解压免安装包，不要单独复制 EXE。");
            Environment.ExitCode = 2;
        }
    }
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public static void ShowError(string message) => MessageBox(0, message, AppTitleCn, 0x10);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint owner, string message, string caption, uint type);
}