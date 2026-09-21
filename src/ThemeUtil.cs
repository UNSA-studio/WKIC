using System;
using System.Drawing;
using Microsoft.Win32;

namespace WinKbdCheck
{
    /// <summary>
    /// 读取 Windows 当前主题（强调色 / 浅色深色模式）。
    /// 优先级：HKCU\...\DWM\AccentColor（Win10 1607+ 用户强调色，
    /// 格式 0xAABBGGRR） -> DWM ColorizationColor（0xAARRGGBB） -> 系统默认蓝。
    /// </summary>
    internal static class ThemeUtil
    {
        private static bool _accentResolved;
        private static Color _accent = Color.FromArgb(255, 0, 120, 215);
        private static bool _darkResolved;
        private static bool _dark;

        public static Color AccentColor
        {
            get
            {
                if (!_accentResolved)
                {
                    _accent = ResolveAccent();
                    _accentResolved = true;
                }
                return _accent;
            }
        }

        /// <summary>读取注册表里保存的强调色，取不到就退回 DWM 颜色。</summary>
        private static Color ResolveAccent()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\DWM", false))
                {
                    if (key != null)
                    {
                        object raw = key.GetValue("AccentColor");
                        if (raw is int)
                        {
                            int v = (int)raw;
                            int a = (v >> 24) & 0xFF;
                            int b = (v >> 16) & 0xFF;
                            int g = (v >> 8) & 0xFF;
                            int r = v & 0xFF;
                            if (a == 0) a = 255;
                            Color c = Color.FromArgb(a, r, g, b);
                            // 过滤明显无效的黑色 / 透明值
                            if ((r + g + b) > 30)
                                return c;
                        }
                    }
                }
            }
            catch
            {
                // 忽略：注册表不可用时继续往下走
            }

            try
            {
                uint cr;
                bool opaque;
                if (NativeMethods.DwmGetColorizationColor(out cr, out opaque) == 0)
                {
                    int r = (int)((cr >> 16) & 0xFF);
                    int g = (int)((cr >> 8) & 0xFF);
                    int b = (int)(cr & 0xFF);
                    if ((r + g + b) > 30)
                        return Color.FromArgb(255, r, g, b);
                }
            }
            catch
            {
            }

            return Color.FromArgb(255, 0, 120, 215);
        }

        /// <summary>应用（非系统）是否使用深色主题。</summary>
        public static bool IsDarkAppTheme
        {
            get
            {
                if (!_darkResolved)
                {
                    _darkResolved = true;
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                        {
                            if (key != null)
                            {
                                object v = key.GetValue("AppsUseLightTheme");
                                if (v is int)
                                    _dark = ((int)v) == 0;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                return _dark;
            }
        }

        public static string ColorToHex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }
    }
}
