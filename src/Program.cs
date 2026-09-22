using System;
using System.Threading;
using System.Windows.Forms;

namespace WinKbdCheck
{
    internal static class Program
    {
        public const string AppTitle = "Windows Keyboard Integrity Check";
        public const string AppTitleCn = "Windows 键盘完整性检测";
        public const string AppVersion = "1.0.0";

        [STAThread]
        private static void Main()
        {
            // 启用 Windows 视觉样式，让控件外观与系统原生控件完全一致
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 统一 DPI 基准：全程序的尺寸与字体都从这里取缩放系数
            Dpi.Init();

            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                MessageBox.Show(
                    "程序遇到未处理的错误：\r\n\r\n" + e.Exception.Message,
                    AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                MessageBox.Show(
                    "程序遇到致命错误：\r\n\r\n" + (ex == null ? "未知错误" : ex.Message),
                    AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            using (MainForm form = new MainForm())
            {
                Application.Run(form);
            }
        }
    }
}
