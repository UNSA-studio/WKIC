using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinKbdCheck
{
    /// <summary>检测过程中收集到的一条"步骤日志"。</summary>
    internal sealed class TimelineEntry
    {
        public DateTime Time;
        public string Stage;
        public string Message;

        public TimelineEntry(string stage, string message)
        {
            Time = DateTime.Now;
            Stage = stage;
            Message = message;
        }

        public override string ToString()
        {
            return string.Format("[{0:HH:mm:ss.fff}] [{1}] {2}", Time, Stage, Message);
        }
    }

    /// <summary>一台键盘设备的完整信息快照。</summary>
    internal sealed class KeyboardDeviceInfo
    {
        // ---- Raw Input ----
        public IntPtr RawHandle;
        public string DevicePath = "";       // \\?\HID#VID_046D&PID_C31C#...#{GUID}
        public string VendorId = "";         // VID_046D
        public string ProductId = "";        // PID_C31C
        public string Revision = "";         // REV_0100
        public int SubType;
        public int KeyboardMode;
        public int NumberOfFunctionKeys;
        public int NumberOfIndicators;
        public int NumberOfKeysTotal;

        // ---- WMI（Win32_PnPEntity） ----
        public string PnpName = "";
        public string PnpManufacturer = "";
        public string PnpDeviceId = "";
        public string PnpClass = "";
        public string Service = "";
        public string Status = "";
        public int ConfigManagerErrorCode = -1;
        public string HardwareIds = "";

        // ---- WMI（Win32_PnPSignedDriver） ----
        public string DriverName = "";
        public string DriverVersion = "";
        public string DriverDateRaw = "";
        public DateTime? DriverDate;
        public string DriverProvider = "";
        public string DriverManufacturer = "";
        public string InfName = "";
        public string Signer = "";
        public string DriverHardwareId = "";

        // ---- 推断结论 ----
        public string ConnectionType = "未知";
        public string InterfaceEra = "未知";
        public string DriverAge = "未知";
        public string VendorName = "未知";

        /// <summary>设备路径 -> 用于展示的短名</summary>
        public string ShortName
        {
            get
            {
                if (!string.IsNullOrEmpty(PnpName))
                    return PnpName;
                if (!string.IsNullOrEmpty(DriverName))
                    return DriverName;
                if (!string.IsNullOrEmpty(VendorId))
                    return VendorId + " / " + ProductId + " 键盘";
                if (!string.IsNullOrEmpty(DevicePath))
                    return DevicePath;
                return "未知键盘设备";
            }
        }

        public string DriverDateText
        {
            get
            {
                if (DriverDate.HasValue)
                    return DriverDate.Value.ToString("yyyy-MM-dd");
                if (!string.IsNullOrEmpty(DriverDateRaw))
                    return DriverDateRaw;
                return "未知";
            }
        }
    }

    /// <summary>系统级键盘参数（来自 user32 + 注册表）。</summary>
    internal sealed class SystemKeyboardInfo
    {
        public int KeyboardType = -1;          // GetKeyboardType(0)
        public int KeyboardSubType = -1;       // GetKeyboardType(1)
        public int FunctionKeyCount = -1;      // GetKeyboardType(2)
        public string KeyboardLayoutId = "";   // 例如 00000409
        public string LayoutLanguage = "";     // 例如 中文(简体，中国)
        public string InstalledLayouts = "";
        public string KbdClassService = "";
        public string KeyboardClassDriverDate = "";
        public string KeyboardClassDriverVersion = "";
        public string BiosDate = "";
        public string Manufacturer = "";
        public string Model = "";
        public string WindowsVersion = "";
    }

    /// <summary>
    /// 键盘设备信息采集器。所有耗时操作都在后台线程执行，
    /// 通过 Log 回调向 UI 推送过程日志。
    /// </summary>
    internal static class DeviceInfoCollector
    {
        public delegate void LogHandler(string stage, string message);

        /* ==================== Raw Input 枚举 ==================== */

        public static List<KeyboardDeviceInfo> EnumerateRawKeyboards(LogHandler log)
        {
            List<KeyboardDeviceInfo> list = new List<KeyboardDeviceInfo>();

            uint deviceCount = 0;
            uint structSize = (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTDEVICELIST));

            uint ret = NativeMethods.GetRawInputDeviceList(IntPtr.Zero, ref deviceCount, structSize);
            if (ret == uint.MaxValue || deviceCount == 0)
            {
                if (log != null)
                    log("设备枚举", "GetRawInputDeviceList 失败（错误码 " + Marshal.GetLastWin32Error() + "）");
                return list;
            }

            if (log != null)
                log("设备枚举", "系统报告原始输入设备总数：" + deviceCount);

            int bytes = (int)(deviceCount * structSize);
            IntPtr buffer = Marshal.AllocHGlobal(bytes);
            try
            {
                uint got = NativeMethods.GetRawInputDeviceList(buffer, ref deviceCount, structSize);
                if (got == uint.MaxValue)
                {
                    if (log != null)
                        log("设备枚举", "第二次 GetRawInputDeviceList 失败");
                    return list;
                }

                for (int i = 0; i < got; i++)
                {
                    IntPtr item = new IntPtr(buffer.ToInt64() + i * structSize);
                    NativeMethods.RAWINPUTDEVICELIST dev =
                        (NativeMethods.RAWINPUTDEVICELIST)Marshal.PtrToStructure(
                            item, typeof(NativeMethods.RAWINPUTDEVICELIST));

                    if (dev.dwType != NativeMethods.RIM_TYPEKEYBOARD)
                        continue;

                    KeyboardDeviceInfo info = new KeyboardDeviceInfo();
                    info.RawHandle = dev.hDevice;
                    info.DevicePath = QueryDeviceName(dev.hDevice);
                    ReadDeviceInfo(dev.hDevice, info);
                    ParseDevicePath(info);

                    if (log != null)
                        log("RawInput", "发现键盘设备：" + info.DevicePath);

                    list.Add(info);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (log != null)
                log("设备枚举", "RawInput 键盘设备数量：" + list.Count);

            return list;
        }

        private static string QueryDeviceName(IntPtr hDevice)
        {
            uint size = 0;
            NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref size);
            if (size == 0)
                return "";

            IntPtr buf = Marshal.AllocHGlobal((int)size * 2 + 4);
            try
            {
                if (NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, buf, ref size)
                    == uint.MaxValue)
                    return "";
                return Marshal.PtrToStringUni(buf);
            }
            catch
            {
                return "";
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static void ReadDeviceInfo(IntPtr hDevice, KeyboardDeviceInfo info)
        {
            NativeMethods.RID_DEVICE_INFO di = new NativeMethods.RID_DEVICE_INFO();
            di.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.RID_DEVICE_INFO));

            uint size = di.cbSize;
            IntPtr buf = Marshal.AllocHGlobal((int)di.cbSize);
            try
            {
                Marshal.StructureToPtr(di, buf, false);
                uint r = NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICEINFO, buf, ref size);
                if (r == uint.MaxValue)
                    return;
                di = (NativeMethods.RID_DEVICE_INFO)Marshal.PtrToStructure(
                    buf, typeof(NativeMethods.RID_DEVICE_INFO));

                info.SubType = (int)di.u1;
                info.KeyboardMode = (int)di.u2;
                info.NumberOfFunctionKeys = (int)di.u3;
                info.NumberOfIndicators = (int)di.u4;
                info.NumberOfKeysTotal = (int)di.u5;
            }
            catch
            {
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static void ParseDevicePath(KeyboardDeviceInfo info)
        {
            string p = info.DevicePath == null ? "" : info.DevicePath.ToUpperInvariant();

            info.VendorId = ExtractToken(p, "VID_");
            info.ProductId = ExtractToken(p, "PID_");
            info.Revision = ExtractToken(p, "REV_");

            if (info.VendorId.Length > 0)
                info.VendorName = LookupVendor(info.VendorId);

            // 连接类型推断
            if (p.IndexOf("ACPI", StringComparison.Ordinal) >= 0 &&
                p.IndexOf("PNP0303", StringComparison.Ordinal) >= 0)
            {
                info.ConnectionType = "PS/2 (i8042 控制器，ACPI\\PNP0303)";
            }
            else if (p.IndexOf("BTHENUM", StringComparison.Ordinal) >= 0 ||
                     p.IndexOf("BTHLE", StringComparison.Ordinal) >= 0 ||
                     p.IndexOf("BTHLEDEVICE", StringComparison.Ordinal) >= 0)
            {
                info.ConnectionType = "蓝牙 (Bluetooth HID)";
            }
            else if (p.IndexOf("USB", StringComparison.Ordinal) >= 0 &&
                     p.IndexOf("VID_", StringComparison.Ordinal) >= 0)
            {
                info.ConnectionType = "USB (含 USB 无线接收器)";
            }
            else if (p.IndexOf("HID", StringComparison.Ordinal) >= 0)
            {
                info.ConnectionType = "HID (未知物理总线)";
            }
            else if (p.IndexOf("ACPI", StringComparison.Ordinal) >= 0)
            {
                info.ConnectionType = "ACPI (PS/2 或内嵌 EC 键盘)";
            }
        }

        private static string ExtractToken(string source, string prefix)
        {
            int i = source.IndexOf(prefix, StringComparison.Ordinal);
            if (i < 0)
                return "";
            int start = i + prefix.Length;
            int end = start;
            while (end < source.Length && IsHex(source[end]))
                end++;
            if (end <= start)
                return "";
            return source.Substring(start, end - start);
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
        }

        private static string LookupVendor(string vid)
        {
            // 常见 USB HID 键盘厂商（USB-IF 分配的 VID）
            switch (vid.ToUpperInvariant())
            {
                case "046D": return "Logitech（罗技）";
                case "045E": return "Microsoft（微软）";
                case "04F2": return "Chicony（群光电子）";
                case "04F3": return "Elan Microelectronics";
                case "05AC": return "Apple Inc.";
                case "0C45": return "Sonix Technology";
                case "1532": return "Razer（雷蛇）";
                case "1B1C": return "Corsair（海盗船）";
                case "258A": return "SINO WEALTH / 中颖电子";
                case "04D9": return "Holtek（盛群半导体）";
                case "1A2C": return "China Resource Semico";
                case "0B05": return "ASUSTeK（华硕）";
                case "0955": return "NVIDIA / 笔记本内嵌";
                case "17EF": return "Lenovo（联想）";
                case "048D": return "ITE Tech（笔记本 EC 键盘）";
                case "04CA": return "Lite-On Technology";
                case "04E8": return "Samsung（三星）";
                case "0A5C": return "Broadcom（蓝牙芯片）";
                case "8087": return "Intel（无线 / 蓝牙）";
                default: return "未知厂商 (VID " + vid + ")";
            }
        }

        /* ==================== WMI 补充 ==================== */

        public static void EnrichWithWmi(KeyboardDeviceInfo info, LogHandler log)
        {
            // Win32_PnPEntity：拿到友好名、制造商、服务名、状态、硬件 ID
            try
            {
                string query = "SELECT * FROM Win32_PnPEntity";

                ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
                searcher.Options.Timeout = new TimeSpan(0, 0, 30);

                foreach (ManagementBaseObject mo in searcher.Get())
                {
                    string did = GetStr(mo, "DeviceID");
                    string pnpdid = GetStr(mo, "PNPDeviceID");
                    if (!Matches(did, info) && !Matches(pnpdid, info))
                        continue;

                    info.PnpName = GetStr(mo, "Name");
                    info.PnpManufacturer = GetStr(mo, "Manufacturer");
                    info.PnpDeviceId = pnpdid.Length > 0 ? pnpdid : did;
                    info.PnpClass = GetStr(mo, "PNPClass");
                    if (info.PnpClass.Length == 0)
                        info.PnpClass = GetStr(mo, "ClassGuid");
                    info.Service = GetStr(mo, "Service");
                    info.Status = GetStr(mo, "Status");
                    object cm = mo["ConfigManagerErrorCode"];
                    if (cm != null)
                        info.ConfigManagerErrorCode = Convert.ToInt32(cm, CultureInfo.InvariantCulture);

                    object hids = mo["HardwareID"];
                    if (hids is string[])
                        info.HardwareIds = string.Join(" | ", (string[])hids);

                    if (log != null)
                        log("WMI", "匹配到 PnP 实体：" + info.PnpName + " (" + info.PnpDeviceId + ")");
                    break;
                }
            }
            catch (Exception ex)
            {
                if (log != null)
                    log("WMI", "Win32_PnPEntity 查询失败：" + ex.Message);
            }

            // Win32_PnPSignedDriver：驱动版本 / 日期 / 供应商 / INF / 数字签名
            try
            {
                ManagementObjectSearcher searcher =
                    new ManagementObjectSearcher("SELECT * FROM Win32_PnPSignedDriver");
                searcher.Options.Timeout = new TimeSpan(0, 0, 45);

                foreach (ManagementBaseObject mo in searcher.Get())
                {
                    string did = GetStr(mo, "DeviceID");
                    string cls = GetStr(mo, "DeviceClass");
                    if (!Matches(did, info))
                        continue;
                    if (cls.Length > 0 &&
                        cls.IndexOf("KEYBOARD", StringComparison.OrdinalIgnoreCase) < 0 &&
                        cls.IndexOf("HID", StringComparison.OrdinalIgnoreCase) < 0 &&
                        cls.IndexOf("SYSTEM", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    info.DriverName = GetStr(mo, "DeviceName");
                    info.DriverVersion = GetStr(mo, "DriverVersion");
                    info.DriverDateRaw = GetStr(mo, "DriverDate");
                    info.DriverProvider = GetStr(mo, "DriverProviderName");
                    info.DriverManufacturer = GetStr(mo, "Manufacturer");
                    info.InfName = GetStr(mo, "InfName");
                    info.Signer = GetStr(mo, "Signer");
                    info.DriverHardwareId = GetStr(mo, "HardWareID");
                    info.DriverDate = ParseCimDate(info.DriverDateRaw);

                    if (log != null)
                        log("WMI", "匹配到签名驱动：" + info.DriverName +
                            " 版本 " + info.DriverVersion + " 日期 " + info.DriverDateRaw);
                    break;
                }
            }
            catch (Exception ex)
            {
                if (log != null)
                    log("WMI", "Win32_PnPSignedDriver 查询失败：" + ex.Message);
            }

            InferEra(info);
        }

        private static string GetStr(ManagementBaseObject mo, string prop)
        {
            try
            {
                object v = mo[prop];
                if (v == null)
                    return "";
                return v.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static bool Matches(string wmiDeviceId, KeyboardDeviceInfo info)
        {
            if (string.IsNullOrEmpty(wmiDeviceId))
                return false;

            string a = NormalizeForWmi(wmiDeviceId);
            string b = NormalizeForWmi(info.DevicePath);
            if (a.Length == 0 || b.Length == 0)
                return false;

            // WMI 的 DeviceID 通常就是 RawInput 设备路径去掉 \\?\ 前缀后的形式
            if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
                return true;
            if (a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // 退而求其次：VID/PID 相同也算匹配
            if (info.VendorId.Length > 0 && info.ProductId.Length > 0)
            {
                string up = wmiDeviceId.ToUpperInvariant();
                if (up.IndexOf("VID_" + info.VendorId.ToUpperInvariant(), StringComparison.Ordinal) >= 0 &&
                    up.IndexOf("PID_" + info.ProductId.ToUpperInvariant(), StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        private static string NormalizeForWmi(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            string s = path;
            if (s.StartsWith(@"\\?\", StringComparison.Ordinal))
                s = s.Substring(4);

            // 去掉最后一段 GUID 后缀
            int lastHash = s.LastIndexOf('#');
            if (lastHash > 0 && lastHash < s.Length - 1)
            {
                string tail = s.Substring(lastHash + 1);
                if (tail.StartsWith("{", StringComparison.Ordinal))
                    s = s.Substring(0, lastHash);
            }
            return s.Length > 0 ? s : path;
        }

        private static DateTime? ParseCimDate(string cim)
        {
            if (string.IsNullOrEmpty(cim))
                return null;
            try
            {
                // 形如 20210315000000.000000-000
                string head = cim;
                int dot = head.IndexOf('.');
                if (dot > 0)
                    head = head.Substring(0, dot);
                if (head.Length < 8)
                    return null;
                int year = int.Parse(head.Substring(0, 4), CultureInfo.InvariantCulture);
                int month = int.Parse(head.Substring(4, 2), CultureInfo.InvariantCulture);
                int day = int.Parse(head.Substring(6, 2), CultureInfo.InvariantCulture);
                if (year < 1980 || year > 2100)
                    return null;
                return new DateTime(year, month, day);
            }
            catch
            {
                return null;
            }
        }

        /* ==================== 时代推断 ==================== */

        private static void InferEra(KeyboardDeviceInfo info)
        {
            string conn = info.ConnectionType;

            if (conn.StartsWith("PS/2", StringComparison.Ordinal))
            {
                info.InterfaceEra = "PS/2 时代（1987 年 IBM PS/2 引入，至今仍以内嵌 EC 方式存在）";
            }
            else if (conn.StartsWith("蓝牙", StringComparison.Ordinal))
            {
                info.InterfaceEra = "蓝牙无线 HID 时代（2001 年 Bluetooth HID Profile 起）";
            }
            else if (conn.StartsWith("USB", StringComparison.Ordinal))
            {
                info.InterfaceEra = "USB HID 时代（1996 年 USB 1.0 HID 类规范起，键盘已全数字报告）";
            }
            else if (conn.StartsWith("HID", StringComparison.Ordinal))
            {
                info.InterfaceEra = "通用 HID 时代（USB-IF HID 1.11 / 1.12 规范）";
            }
            else
            {
                info.InterfaceEra = "未知接口时代";
            }

            if (!info.DriverDate.HasValue)
            {
                info.DriverAge = "未知（驱动日期不可读）";
                return;
            }

            int y = info.DriverDate.Value.Year;
            if (y < 2000)
                info.DriverAge = "上古驱动（" + y + " 年，DOS/9x 过渡期）";
            else if (y <= 2001)
                info.DriverAge = "Windows 98/ME 时代驱动（" + y + " 年）";
            else if (y <= 2006)
                info.DriverAge = "Windows XP 时代驱动（" + y + " 年，HID 1.1 主流）";
            else if (y <= 2009)
                info.DriverAge = "Windows Vista 时代驱动（" + y + " 年，驱动模型重构 WDM/WDF）";
            else if (y <= 2012)
                info.DriverAge = "Windows 7 时代驱动（" + y + " 年，USB HID 2.0 成熟期）";
            else if (y <= 2015)
                info.DriverAge = "Windows 8/8.1 时代驱动（" + y + " 年，精细化 HID 报告）";
            else if (y <= 2020)
                info.DriverAge = "Windows 10 时代驱动（" + y + " 年，通用 HID 类驱动）";
            else
                info.DriverAge = "Windows 10/11 现代驱动（" + y + " 年）";
        }

        /* ==================== 系统键盘信息 ==================== */

        public static SystemKeyboardInfo CollectSystemKeyboardInfo(LogHandler log)
        {
            SystemKeyboardInfo si = new SystemKeyboardInfo();

            try
            {
                si.KeyboardType = NativeMethods.GetKeyboardType(0);
                si.KeyboardSubType = NativeMethods.GetKeyboardType(1);
                si.FunctionKeyCount = NativeMethods.GetKeyboardType(2);
                if (log != null)
                    log("系统", "GetKeyboardType -> 类型=" + si.KeyboardType +
                        ", 子类型=" + si.KeyboardSubType + ", 功能键数=" + si.FunctionKeyCount);
            }
            catch (Exception ex)
            {
                if (log != null)
                    log("系统", "GetKeyboardType 失败：" + ex.Message);
            }

            try
            {
                StringBuilder sb = new StringBuilder(16);
                NativeMethods.GetKeyboardLayoutName(sb);
                si.KeyboardLayoutId = sb.ToString();
                if (log != null)
                    log("系统", "当前输入法布局 KLID = " + si.KeyboardLayoutId);
            }
            catch
            {
            }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts", false))
                {
                    if (k != null)
                    {
                        List<string> names = new List<string>();
                        string[] subs = k.GetSubKeyNames();
                        for (int i = 0; i < subs.Length && i < 64; i++)
                        {
                            using (RegistryKey sk = k.OpenSubKey(subs[i], false))
                            {
                                if (sk == null)
                                    continue;
                                object txt = sk.GetValue("Layout Text");
                                if (txt != null)
                                    names.Add(subs[i] + " = " + txt.ToString());
                            }
                        }
                        si.InstalledLayouts = string.Join("\r\n", names.ToArray());
                    }
                }
            }
            catch
            {
            }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\kbdclass", false))
                {
                    if (k != null)
                    {
                        object img = k.GetValue("ImagePath");
                        si.KbdClassService = img == null ? "kbdclass" : img.ToString();
                    }
                }
            }
            catch
            {
            }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    if (k != null)
                    {
                        object product = k.GetValue("ProductName");
                        object build = k.GetValue("CurrentBuildNumber");
                        object display = k.GetValue("DisplayVersion");
                        si.WindowsVersion = (product == null ? "Windows" : product.ToString());
                        if (display != null)
                            si.WindowsVersion += " " + display.ToString();
                        if (build != null)
                            si.WindowsVersion += " (Build " + build.ToString() + ")";
                    }
                }
            }
            catch
            {
            }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\BIOS", false))
                {
                    if (k != null)
                    {
                        object bd = k.GetValue("BIOSReleaseDate");
                        object mf = k.GetValue("SystemManufacturer");
                        object pm = k.GetValue("SystemProductName");
                        if (bd != null) si.BiosDate = bd.ToString();
                        if (mf != null) si.Manufacturer = mf.ToString();
                        if (pm != null) si.Model = pm.ToString();
                    }
                }
            }
            catch
            {
            }

            return si;
        }

        /* ==================== 键盘类驱动文件信息 ==================== */

        public static void CollectKeyboardDriverFiles(LogHandler log, List<string[]> rows)
        {
            // 枚举 kbdclass / i8042prt / kbdhid / hidusb 等驱动的版本与日期
            string[] services = new string[] { "kbdclass", "kbdhid", "i8042prt", "hidusb", "hidclass", "hidi2c" };

            for (int i = 0; i < services.Length; i++)
            {
                string svc = services[i];
                try
                {
                    string path = null;
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Services\" + svc, false))
                    {
                        if (k == null)
                            continue;
                        object img = k.GetValue("ImagePath");
                        object start = k.GetValue("Start");
                        if (img != null)
                        {
                            path = img.ToString();
                            path = path.Replace(@"\SystemRoot", Environment.GetEnvironmentVariable("SystemRoot"));
                            path = path.Replace(@"\??\", "");
                        }
                        if (start == null)
                        {
                            rows.Add(new string[] { svc, "未加载（Start 未定义）", "-", "-" });
                            continue;
                        }
                    }

                    string ver = "-";
                    string date = "-";
                    if (path != null && System.IO.File.Exists(path))
                    {
                        System.Diagnostics.FileVersionInfo fvi =
                            System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                        ver = fvi.FileVersion;
                        try
                        {
                            date = System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd");
                        }
                        catch
                        {
                            date = "-";
                        }
                    }

                    rows.Add(new string[] { svc, path == null ? "未知路径" : path, ver, date });
                    if (log != null)
                        log("驱动", svc + " -> " + path);
                }
                catch (Exception ex)
                {
                    rows.Add(new string[] { svc, "读取失败: " + ex.Message, "-", "-" });
                }
            }
        }
    }
}