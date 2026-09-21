using System;
using System.Collections.Generic;

namespace WinKbdCheck
{
    /// <summary>单个按键的定义。坐标使用"键位单位"（1 单位 = 标准键宽）。</summary>
    internal sealed class KeyDef
    {
        public int Id;
        public int Vk;
        public int ScanCode;    // 非 0 时优先用扫描码匹配（用于区分左右 Shift）
        public int ExtMode;     // 0=忽略扩展位, 1=必须带扩展位, 2=必须不带扩展位
        public double X, Y, W, H;
        public string Label;    // 键帽主字符
        public string Sub;      // 键帽副字符（Shift 字符）
        public string Name;     // 完整中文名
        public bool Modifier;   // 是否为修饰键

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// 标准 104/105 键美式布局（ANSI）。坐标系：
    ///   x：0 ~ 23 单位，y：0 ~ 6.5 单位
    ///   主键区 x 0~15，导航区 x 15.5~18.5，数字小键盘 x 19~23
    /// </summary>
    internal static class KeyboardLayout
    {
        public const double TotalUnitsX = 23.0;
        public const double TotalUnitsY = 6.5;

        private static List<KeyDef> _keys;
        private static int _idSeq;

        public static List<KeyDef> Keys
        {
            get
            {
                if (_keys == null)
                    _keys = Build();
                return _keys;
            }
        }

        /* ---------------- 构造函数辅助 ---------------- */

        private static KeyDef Mk(int vk, double x, double y, double w, double h,
                                 string label, string sub, string name)
        {
            KeyDef k = new KeyDef();
            k.Id = ++_idSeq;
            k.Vk = vk;
            k.X = x; k.Y = y; k.W = w; k.H = h;
            k.Label = label;
            k.Sub = sub;
            k.Name = (name == null) ? label : name;
            k.ScanCode = 0;
            k.ExtMode = 0;
            k.Modifier = false;
            return k;
        }

        private static KeyDef MkScan(int vk, int scan, double x, double y, double w, double h,
                                     string label, string sub, string name)
        {
            KeyDef k = Mk(vk, x, y, w, h, label, sub, name);
            k.ScanCode = scan;
            return k;
        }

        private static KeyDef MkExt(int vk, int extMode, double x, double y, double w, double h,
                                    string label, string sub, string name)
        {
            KeyDef k = Mk(vk, x, y, w, h, label, sub, name);
            k.ExtMode = extMode;
            return k;
        }

        private static KeyDef Mod(KeyDef k)
        {
            k.Modifier = true;
            return k;
        }

        /* ---------------- 布局构建 ---------------- */

        private static List<KeyDef> Build()
        {
            _idSeq = 0;
            List<KeyDef> list = new List<KeyDef>();

            /* ========== 功能键行 (y = 0) ========== */
            list.Add(Mk(0x1B, 0.00, 0, 1, 1, "Esc", null, "Esc 退出键"));

            list.Add(Mk(0x70, 2.00, 0, 1, 1, "F1", null, "F1"));
            list.Add(Mk(0x71, 3.00, 0, 1, 1, "F2", null, "F2"));
            list.Add(Mk(0x72, 4.00, 0, 1, 1, "F3", null, "F3"));
            list.Add(Mk(0x73, 5.00, 0, 1, 1, "F4", null, "F4"));

            list.Add(Mk(0x74, 6.50, 0, 1, 1, "F5", null, "F5"));
            list.Add(Mk(0x75, 7.50, 0, 1, 1, "F6", null, "F6"));
            list.Add(Mk(0x76, 8.50, 0, 1, 1, "F7", null, "F7"));
            list.Add(Mk(0x77, 9.50, 0, 1, 1, "F8", null, "F8"));

            list.Add(Mk(0x78, 11.00, 0, 1, 1, "F9", null, "F9"));
            list.Add(Mk(0x79, 12.00, 0, 1, 1, "F10", null, "F10"));
            list.Add(Mk(0x7A, 13.00, 0, 1, 1, "F11", null, "F11"));
            list.Add(Mk(0x7B, 14.00, 0, 1, 1, "F12", null, "F12"));

            /* ========== 主键区 第 1 行 (y = 1.5) ========== */
            list.Add(Mk(0xC0, 0.00, 1.5, 1, 1, "`", "~", "重音符 / 波浪号"));
            list.Add(Mk(0x31, 1.00, 1.5, 1, 1, "1", "!", "数字 1"));
            list.Add(Mk(0x32, 2.00, 1.5, 1, 1, "2", "@", "数字 2"));
            list.Add(Mk(0x33, 3.00, 1.5, 1, 1, "3", "#", "数字 3"));
            list.Add(Mk(0x34, 4.00, 1.5, 1, 1, "4", "$", "数字 4"));
            list.Add(Mk(0x35, 5.00, 1.5, 1, 1, "5", "%", "数字 5"));
            list.Add(Mk(0x36, 6.00, 1.5, 1, 1, "6", "^", "数字 6"));
            list.Add(Mk(0x37, 7.00, 1.5, 1, 1, "7", "&", "数字 7"));
            list.Add(Mk(0x38, 8.00, 1.5, 1, 1, "8", "*", "数字 8"));
            list.Add(Mk(0x39, 9.00, 1.5, 1, 1, "9", "(", "数字 9"));
            list.Add(Mk(0x30, 10.00, 1.5, 1, 1, "0", ")", "数字 0"));
            list.Add(Mk(0xBD, 11.00, 1.5, 1, 1, "-", "_", "减号 / 下划线"));
            list.Add(Mk(0xBB, 12.00, 1.5, 1, 1, "=", "+", "等号 / 加号"));
            list.Add(Mk(0x08, 13.00, 1.5, 2, 1, "Backspace", null, "退格键"));

            /* ========== 主键区 第 2 行 (y = 2.5) ========== */
            list.Add(Mk(0x09, 0.00, 2.5, 1.5, 1, "Tab", null, "制表键"));
            list.Add(Mk(0x51, 1.50, 2.5, 1, 1, "Q", null, "字母 Q"));
            list.Add(Mk(0x57, 2.50, 2.5, 1, 1, "W", null, "字母 W"));
            list.Add(Mk(0x45, 3.50, 2.5, 1, 1, "E", null, "字母 E"));
            list.Add(Mk(0x52, 4.50, 2.5, 1, 1, "R", null, "字母 R"));
            list.Add(Mk(0x54, 5.50, 2.5, 1, 1, "T", null, "字母 T"));
            list.Add(Mk(0x59, 6.50, 2.5, 1, 1, "Y", null, "字母 Y"));
            list.Add(Mk(0x55, 7.50, 2.5, 1, 1, "U", null, "字母 U"));
            list.Add(Mk(0x49, 8.50, 2.5, 1, 1, "I", null, "字母 I"));
            list.Add(Mk(0x4F, 9.50, 2.5, 1, 1, "O", null, "字母 O"));
            list.Add(Mk(0x50, 10.50, 2.5, 1, 1, "P", null, "字母 P"));
            list.Add(Mk(0xDB, 11.50, 2.5, 1, 1, "[", "{", "左方括号"));
            list.Add(Mk(0xDD, 12.50, 2.5, 1, 1, "]", "}", "右方括号"));
            list.Add(Mk(0xDC, 13.50, 2.5, 1.5, 1, "\\", "|", "反斜杠 / 竖线"));

            /* ========== 主键区 第 3 行 (y = 3.5) ========== */
            list.Add(Mk(0x14, 0.00, 3.5, 1.75, 1, "Caps Lock", null, "大写锁定键"));
            list.Add(Mk(0x41, 1.75, 3.5, 1, 1, "A", null, "字母 A"));
            list.Add(Mk(0x53, 2.75, 3.5, 1, 1, "S", null, "字母 S"));
            list.Add(Mk(0x44, 3.75, 3.5, 1, 1, "D", null, "字母 D"));
            list.Add(Mk(0x46, 4.75, 3.5, 1, 1, "F", null, "字母 F"));
            list.Add(Mk(0x47, 5.75, 3.5, 1, 1, "G", null, "字母 G"));
            list.Add(Mk(0x48, 6.75, 3.5, 1, 1, "H", null, "字母 H"));
            list.Add(Mk(0x4A, 7.75, 3.5, 1, 1, "J", null, "字母 J"));
            list.Add(Mk(0x4B, 8.75, 3.5, 1, 1, "K", null, "字母 K"));
            list.Add(Mk(0x4C, 9.75, 3.5, 1, 1, "L", null, "字母 L"));
            list.Add(Mk(0xBA, 10.75, 3.5, 1, 1, ";", ":", "分号 / 冒号"));
            list.Add(Mk(0xDE, 11.75, 3.5, 1, 1, "'", "\"", "单引号 / 双引号"));
            list.Add(Mk(0x0D, 12.75, 3.5, 2.25, 1, "Enter", null, "回车键（主键区）"));

            /* ========== 主键区 第 4 行 (y = 4.5) ========== */
            list.Add(Mod(MkScan(0x10, 0x2A, 0.00, 4.5, 2.25, 1, "Shift", null, "左 Shift")));
            list.Add(Mk(0x5A, 2.25, 4.5, 1, 1, "Z", null, "字母 Z"));
            list.Add(Mk(0x58, 3.25, 4.5, 1, 1, "X", null, "字母 X"));
            list.Add(Mk(0x43, 4.25, 4.5, 1, 1, "C", null, "字母 C"));
            list.Add(Mk(0x56, 5.25, 4.5, 1, 1, "V", null, "字母 V"));
            list.Add(Mk(0x42, 6.25, 4.5, 1, 1, "B", null, "字母 B"));
            list.Add(Mk(0x4E, 7.25, 4.5, 1, 1, "N", null, "字母 N"));
            list.Add(Mk(0x4D, 8.25, 4.5, 1, 1, "M", null, "字母 M"));
            list.Add(Mk(0xBC, 9.25, 4.5, 1, 1, ",", "<", "逗号 / 小于号"));
            list.Add(Mk(0xBE, 10.25, 4.5, 1, 1, ".", ">", "句号 / 大于号"));
            list.Add(Mk(0xBF, 11.25, 4.5, 1, 1, "/", "?", "斜杠 / 问号"));
            list.Add(Mod(MkScan(0x10, 0x36, 12.25, 4.5, 2.75, 1, "Shift", null, "右 Shift")));

            /* ========== 主键区 第 5 行 (y = 5.5) ========== */
            list.Add(Mod(MkExt(0x11, 2, 0.00, 5.5, 1.25, 1, "Ctrl", null, "左 Ctrl")));
            list.Add(Mod(Mk(0x5B, 1.25, 5.5, 1.25, 1, "Win", null, "左 Windows 键")));
            list.Add(Mod(MkExt(0x12, 2, 2.50, 5.5, 1.25, 1, "Alt", null, "左 Alt")));
            list.Add(Mk(0x20, 3.75, 5.5, 6.25, 1, "Space", null, "空格键"));
            list.Add(Mod(MkExt(0x12, 1, 10.00, 5.5, 1.25, 1, "Alt", null, "右 Alt (AltGr)")));
            list.Add(Mod(Mk(0x5C, 11.25, 5.5, 1.25, 1, "Win", null, "右 Windows 键")));
            list.Add(Mk(0x5D, 12.50, 5.5, 1.25, 1, "Menu", null, "应用程序键（菜单键）"));
            list.Add(Mod(MkExt(0x11, 1, 13.75, 5.5, 1.25, 1, "Ctrl", null, "右 Ctrl")));

            /* ========== 导航 / 编辑区 ========== */
            list.Add(Mk(0x2C, 15.50, 0, 1, 1, "PrtScn", null, "Print Screen 截屏键"));
            list.Add(Mk(0x91, 16.50, 0, 1, 1, "Scroll", null, "Scroll Lock 滚动锁定"));
            list.Add(Mk(0x13, 17.50, 0, 1, 1, "Pause", null, "Pause / Break"));

            list.Add(Mk(0x2D, 15.50, 1.5, 1, 1, "Insert", null, "插入键"));
            list.Add(Mk(0x24, 16.50, 1.5, 1, 1, "Home", null, "Home 键"));
            list.Add(Mk(0x21, 17.50, 1.5, 1, 1, "PgUp", null, "Page Up"));

            list.Add(Mk(0x2E, 15.50, 2.5, 1, 1, "Delete", null, "删除键"));
            list.Add(Mk(0x23, 16.50, 2.5, 1, 1, "End", null, "End 键"));
            list.Add(Mk(0x22, 17.50, 2.5, 1, 1, "PgDn", null, "Page Down"));

            list.Add(Mk(0x26, 16.50, 4.5, 1, 1, "\u2191", null, "方向键 上"));
            list.Add(Mk(0x25, 15.50, 5.5, 1, 1, "\u2190", null, "方向键 左"));
            list.Add(Mk(0x28, 16.50, 5.5, 1, 1, "\u2193", null, "方向键 下"));
            list.Add(Mk(0x27, 17.50, 5.5, 1, 1, "\u2192", null, "方向键 右"));

            /* ========== 数字小键盘 (x 从 19 开始) ========== */
            list.Add(Mk(0x90, 19.00, 1.5, 1, 1, "Num", null, "Num Lock 数字锁定"));
            list.Add(Mk(0x6F, 20.00, 1.5, 1, 1, "/", null, "小键盘 除号"));
            list.Add(Mk(0x6A, 21.00, 1.5, 1, 1, "*", null, "小键盘 乘号"));
            list.Add(Mk(0x6D, 22.00, 1.5, 1, 1, "-", null, "小键盘 减号"));

            list.Add(Mk(0x67, 19.00, 2.5, 1, 1, "7", null, "小键盘 7"));
            list.Add(Mk(0x68, 20.00, 2.5, 1, 1, "8", null, "小键盘 8"));
            list.Add(Mk(0x69, 21.00, 2.5, 1, 1, "9", null, "小键盘 9"));
            list.Add(Mk(0x6B, 22.00, 2.5, 1, 2, "+", null, "小键盘 加号"));

            list.Add(Mk(0x64, 19.00, 3.5, 1, 1, "4", null, "小键盘 4"));
            list.Add(Mk(0x65, 20.00, 3.5, 1, 1, "5", null, "小键盘 5"));
            list.Add(Mk(0x66, 21.00, 3.5, 1, 1, "6", null, "小键盘 6"));

            list.Add(Mk(0x61, 19.00, 4.5, 1, 1, "1", null, "小键盘 1"));
            list.Add(Mk(0x62, 20.00, 4.5, 1, 1, "2", null, "小键盘 2"));
            list.Add(Mk(0x63, 21.00, 4.5, 1, 1, "3", null, "小键盘 3"));
            list.Add(MkExt(0x0D, 1, 22.00, 4.5, 1, 2, "Enter", null, "小键盘 回车"));

            list.Add(Mk(0x60, 19.00, 5.5, 2, 1, "0", null, "小键盘 0"));
            list.Add(Mk(0x6E, 21.00, 5.5, 1, 1, ".", null, "小键盘 小数点"));

            return list;
        }

        /* ---------------- 匹配逻辑 ---------------- */

        /// <summary>
        /// 把底层钩子给出的 (vk, scanCode, extended) 映射到布局中的某个按键。
        /// 左右修饰键在 Win32 里共享同一个虚拟键码，必须靠扩展位 / 扫描码区分。
        /// </summary>
        public static KeyDef Match(int vk, uint scanCode, bool extended)
        {
            List<KeyDef> keys = Keys;

            // 第一轮：扫描码或扩展位有明确要求的键
            for (int i = 0; i < keys.Count; i++)
            {
                KeyDef k = keys[i];
                if (k.ScanCode != 0)
                {
                    if ((int)scanCode == k.ScanCode)
                        return k;
                    continue;
                }
                if (k.ExtMode != 0)
                {
                    if (k.Vk != vk)
                        continue;
                    if (k.ExtMode == 1 && !extended)
                        continue;
                    if (k.ExtMode == 2 && extended)
                        continue;
                    return k;
                }
            }

            // 第二轮：普通键按虚拟键码匹配
            for (int i = 0; i < keys.Count; i++)
            {
                KeyDef k = keys[i];
                if (k.ScanCode == 0 && k.ExtMode == 0 && k.Vk == vk)
                    return k;
            }

            // 第三轮：兜底 —— 只按虚拟键码匹配（应对非常规键盘 / 虚拟机上报的怪异扫描码）
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].Vk == vk)
                    return keys[i];
            }

            return null;
        }

        /// <summary>虚拟键码到名称（用于日志）。</summary>
        public static string VirtualKeyName(int vk)
        {
            switch (vk)
            {
                case 0x08: return "VK_BACK";
                case 0x09: return "VK_TAB";
                case 0x0D: return "VK_RETURN";
                case 0x10: return "VK_SHIFT";
                case 0x11: return "VK_CONTROL";
                case 0x12: return "VK_MENU";
                case 0x13: return "VK_PAUSE";
                case 0x14: return "VK_CAPITAL";
                case 0x1B: return "VK_ESCAPE";
                case 0x20: return "VK_SPACE";
                case 0x21: return "VK_PRIOR";
                case 0x22: return "VK_NEXT";
                case 0x23: return "VK_END";
                case 0x24: return "VK_HOME";
                case 0x25: return "VK_LEFT";
                case 0x26: return "VK_UP";
                case 0x27: return "VK_RIGHT";
                case 0x28: return "VK_DOWN";
                case 0x2C: return "VK_SNAPSHOT";
                case 0x2D: return "VK_INSERT";
                case 0x2E: return "VK_DELETE";
                case 0x5B: return "VK_LWIN";
                case 0x5C: return "VK_RWIN";
                case 0x5D: return "VK_APPS";
                case 0x90: return "VK_NUMLOCK";
                case 0x91: return "VK_SCROLL";
                default: break;
            }
            if (vk >= 0x70 && vk <= 0x7B)
                return "VK_F" + (vk - 0x70 + 1);
            if (vk >= 0x60 && vk <= 0x69)
                return "VK_NUMPAD" + (vk - 0x60);
            if (vk >= 0x30 && vk <= 0x39)
                return "VK_" + (char)vk;
            if (vk >= 0x41 && vk <= 0x5A)
                return "VK_" + (char)vk;
            return "VK_0x" + vk.ToString("X2");
        }
    }
}