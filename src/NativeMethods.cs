using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinKbdCheck
{
    /// <summary>
    /// 所有 P/Invoke 声明集中在这里。全部为 Windows 官方 API，无第三方依赖。
    /// </summary>
    internal static class NativeMethods
    {
        /* ==================== 低级键盘钩子 (Win32 Hooks) ==================== */

        public const int WH_KEYBOARD_LL = 13;

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public const uint LLKHF_EXTENDED = 0x01;
        public const uint LLKHF_LOWER_IL_INJECTED = 0x02;
        public const uint LLKHF_INJECTED = 0x10;
        public const uint LLKHF_ALTDOWN = 0x20;
        public const uint LLKHF_UP = 0x80;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        /* ==================== 键盘状态 / 布局 ==================== */

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern int GetKeyboardType(int nTypeFlag);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetKeyNameText(int lParam, StringBuilder lpString, int cchSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetKeyboardLayoutName([Out] StringBuilder pwszKLID);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetLocaleInfo(int Locale, int lcType, StringBuilder lpLCData, int cchData);

        /* ==================== Raw Input（设备枚举） ==================== */

        public const uint RIDI_DEVICENAME = 0x20000007;
        public const uint RIDI_DEVICEINFO = 0x2000000B;

        public const uint RIM_TYPEKEYBOARD = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        /// <summary>
        /// RID_DEVICE_INFO：原生大小为 32 字节（cbSize + dwType + 6 个 DWORD 的联合体）。
        /// 这里把联合体展开成 u0..u5 六个 DWORD，键盘类型的含义依次为：
        /// u0 = dwType, u1 = dwSubType, u2 = dwKeyboardMode,
        /// u3 = dwNumberOfFunctionKeys, u4 = dwNumberOfIndicators, u5 = dwNumberOfKeysTotal
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RID_DEVICE_INFO
        {
            public uint cbSize;
            public uint dwType;
            public uint u0;
            public uint u1;
            public uint u2;
            public uint u3;
            public uint u4;
            public uint u5;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        /* ==================== Desktop Window Manager（主题色） ==================== */

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetColorizationColor(out uint pcrColorization,
            [MarshalAs(UnmanagedType.Bool)] out bool pfOpaqueBlend);

        [DllImport("dwmapi.dll")]
        public static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool pfEnabled);
    }
}
