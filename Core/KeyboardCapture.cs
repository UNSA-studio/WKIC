using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinKbdCheck;

/// <summary>Dedicated hook message loop; no XAML, disk I/O or WMI in the callback.</summary>
internal sealed class KeyboardCapture : IDisposable
{
    private readonly ConcurrentQueue<KeyInput> _events = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly HashSet<int> _held = new();
    private Thread _thread;
    private NativeMethods.LowLevelKeyboardProc _callback;
    private nint _hook;
    private uint _threadId;
    private Exception _failure;
    private volatile bool _blocking;
    private volatile bool _recording;
    private long _heartbeat;
    private string _interrupt;
    private Timer _watchdog;
    public bool Blocking => _blocking;
    public bool Recording => _recording;
    public string TakeInterrupt() => Interlocked.Exchange(ref _interrupt, null);
    public void Heartbeat() => Interlocked.Exchange(ref _heartbeat, Environment.TickCount64);

    public void Install()
    {
        if (_thread != null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "WKIC keyboard hook" };
        _thread.Start();
        if (!_ready.Wait(5000)) throw new TimeoutException("键盘钩子启动超时。");
        if (_failure != null) throw _failure;
        _watchdog = new Timer(_ =>
        {
            if (_recording && Environment.TickCount64 - Interlocked.Read(ref _heartbeat) > 3000)
                Interrupt("界面超时未响应，已自动归还键盘控制权。");
        }, null, 1000, 500);
    }

    public void Begin(bool block)
    {
        Stop();
        Heartbeat();
        _interrupt = null;
        _recording = true;
        _blocking = block;
    }
    public void SetBlocking(bool enabled) => _blocking = _recording && enabled;
    public void Stop()
    {
        _blocking = false;
        _recording = false;
        while (_events.TryDequeue(out _)) { }
    }
    public bool TryRead(out KeyInput input) => _events.TryDequeue(out input);
    public void Interrupt(string reason)
    {
        _blocking = false;
        _recording = false;
        Interlocked.Exchange(ref _interrupt, reason);
    }

    private void Run()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, 0, 0, 0, 0); // ensure message queue exists before another thread can stop it
            _callback = Callback;
            _hook = NativeMethods.SetWindowsHookEx(13, _callback, NativeMethods.GetModuleHandle(null), 0);
            if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装键盘钩子");
            _ready.Set();
            while (GetMessage(out var msg, 0, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex) { _failure = ex; _ready.Set(); }
        finally
        {
            _blocking = false;
            _recording = false;
            if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        bool swallow = _blocking;
        if (code >= 0)
        {
            int msg = (int)wParam;
            bool down = msg is 0x100 or 0x104;
            if (down || msg is 0x101 or 0x105)
            {
                var raw = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                bool injected = (raw.flags & 0x10) != 0;
                int vk = (int)raw.vkCode;
                if (!injected)
                {
                    if (down) _held.Add(vk); else _held.Remove(vk);
                    bool ctrl = _held.Overlaps(new[] { 0x11, 0xA2, 0xA3 });
                    bool alt = _held.Overlaps(new[] { 0x12, 0xA4, 0xA5 });
                    bool shift = _held.Overlaps(new[] { 0x10, 0xA0, 0xA1 });
                    if (_recording && down && vk == 0x51 && ctrl && alt && shift)
                        Interrupt("Ctrl+Alt+Shift+Q 紧急结束");
                }
                if (_recording)
                {
                    if (_events.Count < 4096)
                        _events.Enqueue(new KeyInput(vk, raw.scanCode, (raw.flags & 1) != 0,
                            down, injected, Environment.TickCount64, DateTime.Now));
                    else Interrupt("输入队列已满，已安全结束接管。");
                }
            }
            // Capture the decision at callback entry. Emergency-key down must not leak into the desktop.
            if (swallow) return 1;
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        _watchdog?.Dispose();
        if (_threadId != 0) PostThreadMessage(_threadId, 0x12, 0, 0);
        _thread?.Join(2000);
        GC.KeepAlive(_callback);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public nint Window; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
}