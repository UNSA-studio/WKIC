using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WinKbdCheck
{
    /// <summary>把整个检测结果导出为纯文本报告。</summary>
    internal static class ReportExporter
    {
        public static string DefaultFileName()
        {
            return "KeyboardIntegrityReport_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt";
        }

        public static string BuildText(
            KeyboardPanel panel,
            List<KeyboardDeviceInfo> devices,
            SystemKeyboardInfo sysInfo,
            List<string[]> driverRows,
            List<TimelineEntry> timeline,
            TimeSpan duration)
        {
            StringBuilder sb = new StringBuilder();
            string line = new string('=', 78);
            string thin = new string('-', 78);

            sb.AppendLine(line);
            sb.AppendLine("  Windows Keyboard Integrity Check");
            sb.AppendLine("  Windows 键盘完整性检测 · 检测报告");
            sb.AppendLine(line);
            sb.AppendLine("报告生成时间 : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("程序版本     : " + Program.AppVersion);
            sb.AppendLine("检测耗时     : " + FormatDuration(duration));
            sb.AppendLine("操作系统     : " + Environment.OSVersion.VersionString);
            sb.AppendLine("机器名称     : " + Environment.MachineName);
            sb.AppendLine("当前用户     : " + Environment.UserName);
            sb.AppendLine("主题强调色   : " + ThemeUtil.ColorToHex(ThemeUtil.AccentColor) +
                          (ThemeUtil.IsDarkAppTheme ? "（深色主题）" : "（浅色主题）"));
            sb.AppendLine();

            /* ---------- 结论 ---------- */
            int total = panel.TotalKeyCount;
            int captured = panel.CapturedKeyCount;
            double percent = total == 0 ? 0 : (captured * 100.0 / total);

            sb.AppendLine(thin);
            sb.AppendLine("一、总体结论");
            sb.AppendLine(thin);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "键盘总键位数 : {0}", total));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "成功捕获键位 : {0}", captured));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "覆盖率       : {0:F2}%", percent));
            sb.AppendLine("判定结果     : " + Verdict(captured, total));
            sb.AppendLine();

            /* ---------- 未捕获键位 ---------- */
            sb.AppendLine(thin);
            sb.AppendLine("二、未捕获 / 无响应键位");
            sb.AppendLine(thin);
            List<KeyboardPanel.KeyState> missing = new List<KeyboardPanel.KeyState>();
            foreach (KeyboardPanel.KeyState st in panel.States)
                if (!st.WasCaptured) missing.Add(st);

            if (missing.Count == 0)
            {
                sb.AppendLine("（无）—— 全部键位均收到硬件反馈。");
            }
            else
            {
                foreach (KeyboardPanel.KeyState st in missing)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  · {0,-22} VK=0x{1:X2} ({2})",
                        st.Def.Name, st.Def.Vk, KeyboardLayout.VirtualKeyName(st.Def.Vk)));
                }
            }
            sb.AppendLine();

            /* ---------- 逐键明细 ---------- */
            sb.AppendLine(thin);
            sb.AppendLine("三、逐键明细（按按下顺序）");
            sb.AppendLine(thin);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-22} {1,-12} {2,-8} {3,-8} {4,-14} {5}",
                "键位", "虚拟键码", "扫描码", "次数", "首次按下", "累计按住"));

            List<KeyboardPanel.KeyState> ordered = new List<KeyboardPanel.KeyState>();
            foreach (KeyboardPanel.KeyState st in panel.States)
                if (st.WasCaptured) ordered.Add(st);
            ordered.Sort(delegate(KeyboardPanel.KeyState a, KeyboardPanel.KeyState b)
            {
                return a.FirstDown.CompareTo(b.FirstDown);
            });

            foreach (KeyboardPanel.KeyState st in ordered)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-22} {1,-12} {2,-8} {3,-8} {4,-14} {5:F0} ms",
                    Trim(st.Def.Name, 20),
                    "0x" + st.Def.Vk.ToString("X2"),
                    "0x" + st.LastScanCode.ToString("X2"),
                    st.DownCount,
                    st.FirstDown == DateTime.MinValue ? "-" : st.FirstDown.ToString("HH:mm:ss.fff"),
                    st.TotalHoldMs));
            }
            sb.AppendLine();

            /* ---------- 键盘设备 ---------- */
            sb.AppendLine(thin);
            sb.AppendLine("四、键盘硬件设备详细信息");
            sb.AppendLine(thin);

            if (devices == null || devices.Count == 0)
            {
                sb.AppendLine("（未枚举到 RawInput 键盘设备）");
            }
            else
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    KeyboardDeviceInfo d = devices[i];
                    sb.AppendLine("── 设备 " + (i + 1) + " ──");
                    sb.AppendLine("  设备名称     : " + d.ShortName);
                    sb.AppendLine("  设备路径     : " + d.DevicePath);
                    sb.AppendLine("  硬件 ID      : " + (d.HardwareIds.Length > 0 ? d.HardwareIds : d.PnpDeviceId));
                    sb.AppendLine("  厂商 VID     : " + (d.VendorId.Length > 0 ? d.VendorId : "未知") +
                                  "  " + d.VendorName);
                    sb.AppendLine("  产品 PID     : " + (d.ProductId.Length > 0 ? d.ProductId : "未知"));
                    sb.AppendLine("  修订版本     : " + (d.Revision.Length > 0 ? d.Revision : "未知"));
                    sb.AppendLine("  连接类型     : " + d.ConnectionType);
                    sb.AppendLine("  驱动名称     : " + (d.DriverName.Length > 0 ? d.DriverName : "未知"));
                    sb.AppendLine("  驱动版本     : " + (d.DriverVersion.Length > 0 ? d.DriverVersion : "未知"));
                    sb.AppendLine("  驱动日期     : " + d.DriverDateText);
                    sb.AppendLine("  驱动提供商   : " + (d.DriverProvider.Length > 0 ? d.DriverProvider : "未知"));
                    sb.AppendLine("  驱动制造商   : " + (d.DriverManufacturer.Length > 0 ? d.DriverManufacturer : "未知"));
                    sb.AppendLine("  INF 文件     : " + (d.InfName.Length > 0 ? d.InfName : "未知"));
                    sb.AppendLine("  数字签名者   : " + (d.Signer.Length > 0 ? d.Signer : "未知"));
                    sb.AppendLine("  PnP 服务     : " + (d.Service.Length > 0 ? d.Service : "未知"));
                    sb.AppendLine("  设备状态     : " + (d.Status.Length > 0 ? d.Status : "未知") +
                                  (d.ConfigManagerErrorCode > 0 ? "（错误码 " + d.ConfigManagerErrorCode + "）" : ""));
                    sb.AppendLine("  键盘子类型   : " + d.SubType);
                    sb.AppendLine("  键盘模式     : " + d.KeyboardMode);
                    sb.AppendLine("  功能键数量   : " + d.NumberOfFunctionKeys);
                    sb.AppendLine("  指示灯数量   : " + d.NumberOfIndicators);
                    sb.AppendLine("  按键总数     : " + d.NumberOfKeysTotal);
                    sb.AppendLine("  ★ 接口时代   : " + d.InterfaceEra);
                    sb.AppendLine("  ★ 驱动年代   : " + d.DriverAge);
                    sb.AppendLine();
                }
            }

            /* ---------- 系统键盘信息 ---------- */
            sb.AppendLine(thin);
            sb.AppendLine("五、系统级键盘参数");
            sb.AppendLine(thin);
            if (sysInfo != null)
            {
                sb.AppendLine("  Windows 版本       : " + sysInfo.WindowsVersion);
                sb.AppendLine("  键盘类型 (type)    : " + sysInfo.KeyboardType + "  " + KeyboardTypeText(sysInfo.KeyboardType));
                sb.AppendLine("  键盘子类型         : " + sysInfo.KeyboardSubType);
                sb.AppendLine("  功能键数量         : " + sysInfo.FunctionKeyCount);
                sb.AppendLine("  当前键盘布局 KLID  : " + sysInfo.KeyboardLayoutId);
                sb.AppendLine("  键盘类驱动服务     : " + sysInfo.KbdClassService);
                sb.AppendLine("  BIOS 日期          : " + sysInfo.BiosDate);
                sb.AppendLine("  OEM 厂商           : " + sysInfo.Manufacturer);
                sb.AppendLine("  机型               : " + sysInfo.Model);
                sb.AppendLine();
                sb.AppendLine("  已安装键盘布局：");
                if (string.IsNullOrEmpty(sysInfo.InstalledLayouts))
                {
                    sb.AppendLine("    （读取失败或被拒绝访问）");
                }
                else
                {
                    string[] ls = sysInfo.InstalledLayouts.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < ls.Length; i++)
                        sb.AppendLine("    " + ls[i]);
                }
            }
            sb.AppendLine();

            /* ---------- 键盘类驱动文件 ---------- */
            sb.AppendLine(thin);
            sb.AppendLine("六、键盘相关驱动文件");
            sb.AppendLine(thin);
            if (driverRows != null)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-14} {1,-52} {2,-20} {3}", "服务名", "文件路径", "版本", "日期"));
                foreach (string[] row in driverRows)
                {
                    if (row.Length < 4)
                        continue;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-14} {1,-52} {2,-20} {3}",
                        Trim(row[0], 12), Trim(row[1], 50), Trim(row[2], 18), row[3]));
                }
            }
            sb.AppendLine();

            /* ---------- 时间线 ---------- */
            sb.AppendLine(thin);
            sb.AppendLine("七、完整过程时间线");
            sb.AppendLine(thin);
            if (timeline != null)
            {
                for (int i = 0; i < timeline.Count; i++)
                    sb.AppendLine(timeline[i].ToString());
            }
            sb.AppendLine();

            sb.AppendLine(line);
            sb.AppendLine("报告结束。本报告由 Windows Keyboard Integrity Check 自动生成。");
            sb.AppendLine(line);

            return sb.ToString();
        }

        private static string Verdict(int captured, int total)
        {
            if (captured == total)
                return "通过 —— 所有键位均收到硬件反馈，键盘完整性良好。";
            int missing = total - captured;
            if (missing <= 3)
                return "基本通过 —— 有 " + missing + " 个键位未收到反馈，建议复测确认。";
            if (captured == 0)
                return "未完成 —— 尚未开始按键检测。";
            return "存在异常 —— 有 " + missing + " 个键位未收到反馈，可能存在按键失灵、键盘布局不匹配或驱动问题。";
        }

        private static string Trim(string s, int max)
        {
            if (s == null)
                return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        public static string KeyboardTypeText(int type)
        {
            switch (type)
            {
                case 1: return "（IBM PC/XT 83 键）";
                case 2: return "（Olivetti 102 键）";
                case 3: return "（IBM AT 84 键）";
                case 4: return "（IBM 增强型 101/102 键）";
                case 5: return "（Nokia 1050）";
                case 6: return "（Nokia 1051）";
                case 7: return "（日文键盘）";
                default: return "";
            }
        }

        public static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalSeconds < 60)
                return string.Format(CultureInfo.InvariantCulture, "{0:F2} 秒", ts.TotalSeconds);
            return string.Format(CultureInfo.InvariantCulture,
                "{0} 分 {1:F0} 秒", (int)ts.TotalMinutes, ts.Seconds);
        }

        public static string SafeWrite(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content, new UTF8Encoding(true));
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}