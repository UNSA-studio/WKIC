using System.Diagnostics;

namespace WinKbdCheck;

internal readonly record struct KeyInput(int VirtualKey, uint ScanCode, bool Extended, bool IsDown,
    bool Injected, long StampMs, DateTime Time);

internal enum KeyResult { NotTested, Responded, NoResponse, ReleaseMissing }

internal sealed class KeyRecord
{
    public KeyDef Def { get; init; }
    public bool IsDown { get; internal set; }
    public bool TimedOut { get; internal set; }
    public bool Excluded { get; internal set; }
    public int DownCount { get; internal set; }
    public int UpCount { get; internal set; }
    public int Repeats { get; internal set; }
    public int LastVirtualKey { get; internal set; }
    public uint ScanCode { get; internal set; }
    public bool Extended { get; internal set; }
    public DateTime? FirstDown { get; internal set; }
    public DateTime? LastDown { get; internal set; }
    public DateTime? LastUp { get; internal set; }
    public double HoldMs { get; internal set; }
    internal long HoldStart;
    public List<string> Events { get; } = new();
    public KeyResult Result => DownCount > 0
        ? UpCount > 0 && !IsDown ? KeyResult.Responded : KeyResult.ReleaseMissing
        : TimedOut ? KeyResult.NoResponse : KeyResult.NotTested;
    public string Status => Excluded ? "此键不存在（手动排除）" : Result switch
    {
        KeyResult.Responded => "已响应 · 完好",
        KeyResult.NoResponse => "未响应 · 疑似故障，需复测",
        KeyResult.ReleaseMissing => "已按下，但未收到完整抬起反馈",
        _ => "未检测"
    };
}

/// <summary>Pure state machine shared by WinUI, reports and regression tests. Time is monotonic.</summary>
internal sealed class TestSession
{
    public const int TimeoutMs = 3000;
    public const int DoubleEnterMs = 450;
    private readonly Func<long> _clock;
    private readonly List<KeyRecord> _queue = new();
    private int _index;
    private long _deadline;
    private int _lastEnterId;
    private long _lastEnterUp = long.MinValue;
    private readonly HashSet<int> _pressed = new();
    public List<KeyRecord> Keys { get; }
    public List<string> Timeline { get; } = new();
    public bool Running { get; private set; }
    public bool HasRun { get; private set; }
    public int IgnoredInjected { get; private set; }
    public string FinishReason { get; private set; } = "";
    public DateTime Started { get; private set; }
    public DateTime Ended { get; private set; }
    public bool Compact { get; private set; }
    public int Responded => Keys.Count(k => !k.Excluded && k.Result == KeyResult.Responded);
    public int Skipped => Keys.Count(k => !k.Excluded && k.Result == KeyResult.NoResponse);
    public int Required => Keys.Count(k => !k.Excluded);
    public int Unfinished => Required - Responded;
    public TimeSpan Duration => !HasRun ? TimeSpan.Zero : (Running ? DateTime.Now : Ended) - Started;
    public KeyRecord Target => Running && _index < _queue.Count ? _queue[_index] : null;
    public int Position => Math.Min(_index + 1, _queue.Count);
    public int RemainingMs => Target == null || Target.IsDown ? TimeoutMs : (int)Math.Clamp(_deadline - _clock(), 0, TimeoutMs);

    public TestSession(Func<long> clock = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
        Keys = KeyboardLayout.Keys.Select(d => new KeyRecord { Def = d }).ToList();
    }

    public void Start(bool compact = false)
    {
        Stop("重新开始");
        Keys.Clear();
        Keys.AddRange(KeyboardLayout.Keys.Select(d => new KeyRecord { Def = d, Excluded = compact && d.X >= 19 }));
        Compact = compact;
        _queue.Clear();
        _queue.AddRange(Keys.Where(k => !k.Excluded).OrderBy(k => k.Def.Y).ThenBy(k => k.Def.X));
        _index = 0;
        _pressed.Clear();
        _lastEnterId = 0;
        _lastEnterUp = long.MinValue;
        IgnoredInjected = 0;
        Timeline.Clear();
        Started = DateTime.Now;
        Ended = default;
        FinishReason = "";
        HasRun = Running = true;
        _deadline = _clock() + TimeoutMs;
        Log($"开始顺序检测，共 {Required} 个键位；每键等待 3 秒。双击回车或左下角完成可结束。");
    }

    public KeyRecord Handle(KeyInput input)
    {
        if (!Running) return null;
        if (input.Injected) { IgnoredInjected++; return null; }
        KeyDef def = KeyboardLayout.Match(input.VirtualKey, input.ScanCode, input.Extended);
        if (def == null) { if (input.IsDown) Log($"额外按键 VK=0x{input.VirtualKey:X2} SC=0x{input.ScanCode:X2}"); return null; }
        KeyRecord key = Keys.First(k => k.Def.Id == def.Id);
        if (key.Excluded) key.Excluded = false;
        key.LastVirtualKey = input.VirtualKey;
        key.ScanCode = input.ScanCode;
        key.Extended = input.Extended;
        bool fresh = input.IsDown && _pressed.Add(def.Id);
        if (input.IsDown)
        {
            if (fresh)
            {
                key.IsDown = true;
                key.HoldStart = input.StampMs;
                key.DownCount++;
                key.FirstDown ??= input.Time;
                key.LastDown = input.Time;
                AddEvent(key, input.Time, $"按下 VK=0x{input.VirtualKey:X2} SC=0x{input.ScanCode:X2} EXT={input.Extended}");
                Log($"收到按下：{key.Def.Name}");
            }
            else key.Repeats++;
        }
        else if (_pressed.Remove(def.Id))
        {
            key.IsDown = false;
            key.UpCount++;
            key.LastUp = input.Time;
            key.HoldMs += Math.Max(0, input.StampMs - key.HoldStart);
            AddEvent(key, input.Time, "抬起 · 一次按压完成");
            if (input.VirtualKey == 0x0D)
            {
                // Only two real press/release cycles of the SAME Enter key. Holding is not double-click.
                bool doubleClick = _lastEnterId == def.Id && _lastEnterUp != long.MinValue &&
                    input.StampMs - _lastEnterUp >= 0 && input.StampMs - _lastEnterUp <= DoubleEnterMs;
                _lastEnterUp = input.StampMs;
                _lastEnterId = def.Id;
                if (doubleClick) { Stop("双击回车完成"); return key; }
            }
            else { _lastEnterId = 0; _lastEnterUp = long.MinValue; }
        }
        if (Running && Target?.Def.Id == def.Id && key.Result == KeyResult.Responded) Advance();
        return key;
    }

    public void Tick()
    {
        if (!Running || Target == null) return;
        if (Target.IsDown) return; // the 3s deadline is for pressing, not for how long someone holds a key
        if (_clock() < _deadline) return;
        Target.TimedOut = true;
        AddEvent(Target, DateTime.Now, "3 秒内未按下，自动跳过；这不等于已证实硬件损坏。");
        Log($"超时跳过：{Target.Def.Name}");
        _index++;
        Advance();
    }

    private void Advance()
    {
        while (_index < _queue.Count && (_queue[_index].Excluded || _queue[_index].Result == KeyResult.Responded)) _index++;
        if (_index >= _queue.Count) { Stop("引导队列已完成"); return; }
        _deadline = _clock() + TimeoutMs;
    }

    public void Stop(string reason)
    {
        if (!Running) return;
        Running = false;
        Ended = DateTime.Now;
        FinishReason = reason;
        foreach (var key in Keys.Where(k => k.IsDown))
            AddEvent(key, Ended, "会话结束时尚未收到抬起；不补造抬起事件。");
        _pressed.Clear();
        Log($"结束：{reason}；完整响应 {Responded}/{Required}，超时 {Skipped}。");
    }

    public void Log(string text) => Timeline.Add($"[{DateTime.Now:HH:mm:ss.fff}] {text}");
    private static void AddEvent(KeyRecord key, DateTime time, string text)
    {
        if (key.Events.Count < 200) key.Events.Add($"[{time:HH:mm:ss.fff}] {text}");
    }
}
