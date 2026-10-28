using System.Management;
using System.Text;

namespace WinKbdCheck;

internal sealed class Inventory
{
    public MachineInfo Machine { get; set; } = new();
    public SystemKeyboardInfo System { get; set; } = new();
    public List<KeyboardDeviceInfo> Devices { get; set; } = new();
    public List<string[]> Drivers { get; } = new();
    public List<string> Logs { get; } = new();
    public bool IsComplete { get; private set; }

    public static Inventory Collect(Action<string> progress, CancellationToken cancellation)
    {
        var result = new Inventory();
        void Log(string stage, string text)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{stage}] {text}";
            result.Logs.Add(line);
            progress?.Invoke(line);
        }
        Log("1/5", "枚举当前 Raw Input 键盘设备");
        result.Devices = DeviceInfoCollector.EnumerateRawKeyboards(Log);
        cancellation.ThrowIfCancellationRequested();
        Log("2/5", "查询键盘设备友好名称、设备状态及驱动签名（每类只查询一次）");
        try
        {
            using var search = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE PNPClass='Keyboard'");
            search.Options.Timeout = TimeSpan.FromSeconds(20);
            using var objects = search.Get();
            foreach (ManagementObject item in objects)
            using (item)
            {
                cancellation.ThrowIfCancellationRequested();
                string id = Get(item, "DeviceID");
                foreach (var d in result.Devices.Where(d => Normalize(d.DevicePath) == Normalize(id)))
                {
                    d.PnpName = Get(item, "Name");
                    d.PnpDeviceId = id;
                    d.PnpManufacturer = Get(item, "Manufacturer");
                    d.Service = Get(item, "Service");
                    d.Status = Get(item, "Status");
                    d.PnpClass = Get(item, "PNPClass");
                    d.ConfigManagerErrorCode = int.TryParse(Get(item, "ConfigManagerErrorCode"), out int e) ? e : -1;
                    d.HardwareIds = item["HardwareID"] is string[] ids ? string.Join(" | ", ids) : "";
                    Log("PnP", d.PnpName + " · " + id);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log("PnP", "读取失败（其余检测仍可继续）：" + ex.Message); }
        try
        {
            using var search = new ManagementObjectSearcher("SELECT * FROM Win32_PnPSignedDriver WHERE DeviceClass='KEYBOARD'");
            search.Options.Timeout = TimeSpan.FromSeconds(25);
            using var objects = search.Get();
            foreach (ManagementObject item in objects)
            using (item)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (var d in result.Devices.Where(d => Normalize(d.DevicePath) == Normalize(Get(item, "DeviceID"))))
                {
                    d.DriverName = Get(item, "DeviceName");
                    d.DriverVersion = Get(item, "DriverVersion");
                    d.DriverProvider = Get(item, "DriverProviderName");
                    d.DriverManufacturer = Get(item, "Manufacturer");
                    d.DriverDateRaw = Get(item, "DriverDate");
                    d.InfName = Get(item, "InfName");
                    d.Signer = Get(item, "Signer");
                    try { d.DriverDate = ManagementDateTimeConverter.ToDateTime(d.DriverDateRaw); } catch { }
                    d.DriverAge = d.DriverDate.HasValue ? $"驱动声明年份：{d.DriverDate.Value.Year}（不代表硬件生产年份）" : "驱动年份未知";
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log("驱动", "读取失败：" + ex.Message); }
        foreach (var d in result.Devices)
        {
            d.InterfaceEra = d.ConnectionType.StartsWith("PS/2") ? "PS/2 接口类别；不能据此确定出厂年代"
                : d.ConnectionType.StartsWith("蓝牙") ? "蓝牙 HID；版本须由设备公开信息确认"
                : "HID 输入设备；物理总线 / 出厂年代无法仅凭此路径确定";
        }
        Log("3/5", "读取系统键盘参数");
        cancellation.ThrowIfCancellationRequested();
        result.System = DeviceInfoCollector.CollectSystemKeyboardInfo(Log);
        Log("4/5", "读取键盘相关驱动文件版本；文件修改日期不是驱动发行日期");
        DeviceInfoCollector.CollectKeyboardDriverFiles(Log, result.Drivers);
        Log("5/5", "读取当前用户、用户组与机器硬件概况");
        cancellation.ThrowIfCancellationRequested();
        result.Machine = DeviceInfoCollector.CollectMachineInfo(Log);
        Log("完成", $"发现 {result.Devices.Count} 个键盘设备。检测输入为所有已连接键盘的合并输入，不宣称已绑定单台设备。");
        result.IsComplete = true;
        return result;
    }

    private static string Get(ManagementBaseObject item, string property)
    {
        try { return item[property]?.ToString() ?? ""; } catch { return ""; }
    }
    private static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        if (path.StartsWith(@"\\?\")) path = path.Substring(4);
        int guid = path.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0) path = path.Substring(0, guid);
        return path.Replace('#', '\\').ToUpperInvariant();
    }

    public IEnumerable<(string Label, string Value)> MachineFacts()
    {
        yield return ("当前账户", Machine.UserName);
        yield return ("登录域 / 机器", Machine.Domain);
        yield return ("运行权限", Machine.IsAdmin ? "管理员" : "标准用户");
        yield return ("计算机", Machine.ComputerName);
        yield return ("Windows", System.WindowsVersion.Length > 0 ? System.WindowsVersion : Machine.OsVersion);
        yield return ("处理器", Machine.Cpu);
        yield return ("内存", Machine.RamTotal);
        yield return ("整机型号", Machine.SystemModel);
        yield return ("主板", Machine.BaseBoard);
        yield return ("BIOS 日期", Machine.BiosDate);
        yield return ("键盘布局 KLID", System.KeyboardLayoutId);
        foreach (string group in Machine.Groups) yield return ("用户组", group);
    }

    public IEnumerable<(string Label, string Value)> DeviceFacts(KeyboardDeviceInfo d)
    {
        yield return ("设备名称", d.ShortName);
        yield return ("设备路径", d.DevicePath);
        yield return ("硬件 ID", d.HardwareIds);
        yield return ("厂商", d.PnpManufacturer);
        yield return ("VID / PID", d.VendorId + " / " + d.ProductId);
        yield return ("设备报告按键总数", d.NumberOfKeysTotal.ToString());
        yield return ("连接类别", d.ConnectionType);
        yield return ("设备状态", d.Status + " / 配置错误码 " + d.ConfigManagerErrorCode);
        yield return ("驱动版本", d.DriverVersion);
        yield return ("驱动日期", d.DriverDateText);
        yield return ("驱动提供商", d.DriverProvider);
        yield return ("INF", d.InfName);
        yield return ("驱动签名者", d.Signer);
        yield return ("内核服务", d.Service);
        yield return ("硬件年份", "未公开 / 无法确定；驱动日期和 BIOS 日期不能当作键盘生产年份");
        yield return ("接口说明", d.InterfaceEra);
    }
}