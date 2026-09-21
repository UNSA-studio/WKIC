using System;
using System.Runtime.InteropServices;

namespace WinKbdCheck
{
    internal sealed class KeyboardHookEventArgs : EventArgs
    {
        public int VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public bool IsDown;
        public bool IsExtended;
        public bool IsInjected;
        public bool AltDown;
        public uint Time;
        public IntPtr ExtraInfo;

        public bool IsUp
        {
            get { return !IsDown; }
        }
    }

    /// <summary>
    /// WH_KEYBOARD_LL 全局低级键盘钩子。
    ///
    /// 关键点：
    ///  - 低级键盘钩子由系统在"安装钩子的线程"上回调，因此本类必须在 UI 线程安装，
    ///    回调里的耗时工作必须通过 BeginInvoke 异步丢回消息泵，避免拖慢整个输入链。
    ///  - 回调返回 1（非 0）即吞掉该按键消息，任何窗口（包括 Explorer 的开始菜单）
    ///    都收不到它 —— 这就是"接管全部键盘操作"的实现方式。
    ///  - Ctrl+Alt+Del（SAS）由内核 winlogon 处理，任何用户态钩子都无法拦截。
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        private readonly NativeMethods.LowLevelKeyboardProc _callback;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _block;
        private bool _installed;

        public event EventHandler<KeyboardHookEventArgs> KeyEvent;

        public KeyboardHook()
        {
            _callback = HookProc;
        }

        /// <summary>为 true 时吞掉所有键盘消息，系统与其它程序都收不到。</summary>
        public bool IsBlocking
        {
            get { return _block; }
            set { _block = value; }
        }

        public bool IsInstalled
        {
            get { return _installed; }
        }

        public bool Install()
        {
            if (_installed)
                return true;

            IntPtr hModule = NativeMethods.GetModuleHandle(null);
            _hookId = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL, _callback, hModule, 0);

            _installed = _hookId != IntPtr.Zero;
            return _installed;
        }

        public void Uninstall()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _installed = false;
        }

        public void Dispose()
        {
            Uninstall();
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isDown = (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN);
                bool isUp = (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP);

                if (isDown || isUp)
                {
                    NativeMethods.KBDLLHOOKSTRUCT raw = (NativeMethods.KBDLLHOOKSTRUCT)
                        Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));

                    KeyboardHookEventArgs e = new KeyboardHookEventArgs();
                    e.VirtualKey = (int)raw.vkCode;
                    e.ScanCode = raw.scanCode;
                    e.Flags = raw.flags;
                    e.IsDown = isDown;
                    e.IsExtended = (raw.flags & NativeMethods.LLKHF_EXTENDED) != 0;
                    e.IsInjected = (raw.flags & NativeMethods.LLKHF_INJECTED) != 0;
                    e.AltDown = (raw.flags & NativeMethods.LLKHF_ALTDOWN) != 0;
                    e.Time = raw.time;
                    e.ExtraInfo = raw.dwExtraInfo;

                    EventHandler<KeyboardHookEventArgs> handler = KeyEvent;
                    if (handler != null)
                    {
                        try
                        {
                            handler(this, e);
                        }
                        catch
                        {
                            // 回调里绝不能抛出异常，否则钩子链会断掉
                        }
                    }
                }

                if (_block)
                    return (IntPtr)1;   // 吞掉消息：系统收不到，开始菜单不会弹出
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}
