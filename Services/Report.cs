using System.Text;
using System.Runtime.InteropServices;

namespace WinKbdCheck;

internal static class Report
{
    public static string Build(TestSession session, Inventory inventory)
    {
        var s = new StringBuilder();
        s.AppendLine($"{Program.AppTitle} / {Program.AppTitleCn}");
        s.AppendLine($"版本 {Program.AppVersion} · WinUI 3 · UNSA-studio@outlook.com");
        s.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        s.AppendLine($"开始：{session.Started:yyyy-MM-dd HH:mm:ss}；结束：{session.Ended:yyyy-MM-dd HH:mm:ss}");
        s.AppendLine($"完整响应 {session.Responded}/{session.Required}；超时未响应 {session.Skipped}；耗时 {session.Duration.TotalSeconds:F1} 秒");
        s.AppendLine($"结束原因：{session.FinishReason}；已忽略软件注入事件 {session.IgnoredInjected}");
        s.AppendLine("注意：未响应可能是未按、无此键、Win Lock、固件或布局等原因，不能单凭超时断定损坏。输入为多键盘合并输入。");
        Section("当前用户 / 用户组 / 系统硬件", inventory.MachineFacts());
        foreach (var d in inventory.Devices) Section("键盘设备 · " + d.ShortName, inventory.DeviceFacts(d));
        s.AppendLine("\n=== 键盘相关驱动文件（末列仅为文件修改时间） ===");
        foreach (var row in inventory.Drivers) s.AppendLine(string.Join(" | ", row));
        s.AppendLine("\n=== 逐键记录 ===");
        foreach (var key in session.Keys)
        {
            s.AppendLine($"\n{key.Def.Name}：{key.Status}");
            foreach (var fact in KeyFacts(key)) s.AppendLine($"  {fact.Label}：{fact.Value}");
            foreach (var e in key.Events) s.AppendLine("  " + e);
        }
        s.AppendLine("\n=== 设备采集过程 ===");
        foreach (string line in inventory.Logs) s.AppendLine(line);
        s.AppendLine("\n=== 测试过程 ===");
        foreach (string line in session.Timeline) s.AppendLine(line);
        return s.ToString();
        void Section(string name, IEnumerable<(string Label, string Value)> facts)
        {
            s.AppendLine("\n=== " + name + " ===");
            foreach (var fact in facts) s.AppendLine($"{fact.Label}：{fact.Value}");
        }
    }
    public static IEnumerable<(string Label, string Value)> KeyFacts(KeyRecord key)
    {
        yield return ("检测状态", key.Status);
        yield return ("虚拟键码", key.DownCount == 0 ? "未收到" : $"0x{key.LastVirtualKey:X2}");
        yield return ("扫描码", key.DownCount == 0 ? "未收到" : $"0x{key.ScanCode:X2}");
        yield return ("扩展位 E0", key.DownCount == 0 ? "未收到" : key.Extended ? "是" : "否");
        yield return ("按下 / 抬起次数", $"{key.DownCount} / {key.UpCount}");
        yield return ("自动重复事件", key.Repeats.ToString());
        yield return ("首次按下", key.FirstDown?.ToString("HH:mm:ss.fff") ?? "未收到");
        yield return ("最后按下", key.LastDown?.ToString("HH:mm:ss.fff") ?? "未收到");
        yield return ("最后抬起", key.LastUp?.ToString("HH:mm:ss.fff") ?? "未收到");
        yield return ("累计按住", $"{key.HoldMs:F0} ms（已完成的按压）");
        yield return ("注入过滤", "软件模拟事件不计作物理键盘通过");
    }

    // The legacy Win32 common dialog works in an elevated unpackaged WinUI process as well.
    public static string Save(nint owner, string content)
    {
        var file = new StringBuilder($"KeyboardIntegrityReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt", 32768);
        var o = new OpenFileName
        {
            Size = Marshal.SizeOf<OpenFileName>(), Owner = owner, File = file, MaxFile = file.Capacity,
            Filter = "文本报告 (*.txt)\0*.txt\0所有文件 (*.*)\0*.*\0\0", FilterIndex = 1,
            Title = "保存键盘检测报告", DefaultExtension = "txt", Flags = 0x00080000 | 0x00000002 | 0x00000800,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (!GetSaveFileName(ref o))
        {
            uint error = CommDlgExtendedError();
            if (error != 0) throw new InvalidOperationException($"保存对话框错误 0x{error:X}");
            return null;
        }
        File.WriteAllText(file.ToString(), content, new UTF8Encoding(true));
        return file.ToString();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int Size; public nint Owner, Instance; public string Filter; public nint CustomFilter;
        public int MaxCustomFilter, FilterIndex; public StringBuilder File; public int MaxFile;
        public nint FileTitle; public int MaxFileTitle; public string InitialDirectory, Title;
        public uint Flags; public ushort FileOffset, FileExtension; public string DefaultExtension;
        public nint CustomData, Hook; public string Template; public nint Reserved; public uint Reserved2, FlagsEx;
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSaveFileName(ref OpenFileName o);
    [DllImport("comdlg32.dll")] private static extern uint CommDlgExtendedError();
}