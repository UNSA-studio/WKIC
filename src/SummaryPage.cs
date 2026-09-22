using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace WinKbdCheck
{
    /// <summary>
    /// 第 4 步「结果展示」页。
    ///
    /// 布局：
    ///   ┌──────────────────────────────────────────────────────┐
    ///   │ 结论横幅                                              │
    ///   ├────────────────┬─────────────────────────────────────┤
    ///   │ 检测数据        │ 图例                                │
    ///   │ 当前用户/用户组 │ 键盘图（绿=完好 琥珀=跳过 红=无响应）│
    ///   │ 硬件信息        │ ───────────────────────────────────  │
    ///   │                │ 点击某个键后显示的检测记录            │
    ///   ├────────────────┴─────────────────────────────────────┤
    ///   │ 重新检测 / 导出报告                                   │
    ///   └──────────────────────────────────────────────────────┘
    /// </summary>
    internal sealed partial class MainForm
    {
        /* ====================================================================
         *  界面构建
         * ==================================================================== */

        private void BuildSummaryPage()
        {
            _pageSummary = new Panel();
            _pageSummary.Dock = DockStyle.Fill;
            _pageSummary.BackColor = SystemColors.Control;

            /* ---- 主栅格：3 行 2 列（结论 / 内容 / 按钮行） ---- */
            _summaryGrid = new TableLayoutPanel();
            _summaryGrid.Dock = DockStyle.Fill;
            _summaryGrid.ColumnCount = 2;
            _summaryGrid.RowCount = 3;
            _summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390f));
            _summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _summaryGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
            _summaryGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _summaryGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

            /* ---- 结论横幅 ---- */
            _lblVerdict = new Label();
            _lblVerdict.AutoSize = false;
            _lblVerdict.Dock = DockStyle.Fill;
            _lblVerdict.Padding = new Padding(24, 0, 24, 0);
            _lblVerdict.Font = Dpi.MakeFont(11f);
            _lblVerdict.TextAlign = ContentAlignment.MiddleLeft;
            _lblVerdict.Text = "正在汇总……";

            /* ---- 左上角：检测数据 / 当前用户 / 硬件信息 ---- */
            _lvOverview = new ListView();
            _lvOverview.Dock = DockStyle.Fill;
            _lvOverview.View = View.Details;
            _lvOverview.FullRowSelect = true;
            _lvOverview.HideSelection = false;
            _lvOverview.ShowGroups = true;
            _lvOverview.MultiSelect = false;
            _lvOverview.Columns.Add("项目", 168);
            _lvOverview.Columns.Add("值", 210);

            /* ---- 右栏：图例 + 键盘图 + 单键记录 ---- */
            TableLayoutPanel rightGrid = new TableLayoutPanel();
            rightGrid.Dock = DockStyle.Fill;
            rightGrid.ColumnCount = 1;
            rightGrid.RowCount = 3;
            rightGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            rightGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 330f));
            rightGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _lblLegend = new Label();
            _lblLegend.Dock = DockStyle.Fill;
            _lblLegend.TextAlign = ContentAlignment.MiddleLeft;
            _lblLegend.Padding = new Padding(8, 0, 0, 0);
            _lblLegend.Text = "绿色 = 完好　　琥珀 = 引导超时跳过　　红色 = 无响应（可能已损坏）　　← 点击任意按键查看它的检测记录";

            _kbdResultHost = new Panel();
            _kbdResultHost.Dock = DockStyle.Fill;
            _kbdResultHost.BackColor = SystemColors.Control;
            _kbdResultHost.Resize += delegate { LayoutResultKeyboard(); };

            _kbdResult = new KeyboardPanel();
            _kbdResult.Mode = KeyboardPanel.PanelMode.Result;
            _kbdResult.KeyClicked += OnResultKeyClicked;
            _kbdResultHost.Controls.Add(_kbdResult);

            _lvKeyDetail = new ListView();
            _lvKeyDetail.Dock = DockStyle.Fill;
            _lvKeyDetail.View = View.Details;
            _lvKeyDetail.FullRowSelect = true;
            _lvKeyDetail.HideSelection = false;
            _lvKeyDetail.GridLines = true;
            _lvKeyDetail.MultiSelect = false;
            _lvKeyDetail.Columns.Add("项目", 180);
            _lvKeyDetail.Columns.Add("值", 620);

            rightGrid.Controls.Add(_lblLegend, 0, 0);
            rightGrid.Controls.Add(_kbdResultHost, 0, 1);
            rightGrid.Controls.Add(_lvKeyDetail, 0, 2);

            /* ---- 底部按钮行 ---- */
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;

            _btnRestart = new Button();
            _btnRestart.Text = "重新检测(&R)";
            _btnRestart.Size = new Size(140, 32);
            _btnRestart.Location = new Point(24, 9);
            _btnRestart.Click += delegate
            {
                if (_keyboard == null)
                    return;
                _keyboard.ResetAll();
                _skippedCount = 0;
                _testIndex = 0;
                _guideElapsedMs = 0;
                _lvMissing.Tag = null;
                GoToStep(2);
            };

            _btnExport = new Button();
            _btnExport.Text = "导出报告(&E)…";
            _btnExport.Size = new Size(140, 32);
            _btnExport.Location = new Point(176, 9);
            _btnExport.Click += delegate { ExportReport(); };

            bottom.Controls.Add(_btnRestart);
            bottom.Controls.Add(_btnExport);

            _summaryGrid.Controls.Add(_lblVerdict, 0, 0);
            _summaryGrid.SetColumnSpan(_lblVerdict, 2);
            _summaryGrid.Controls.Add(_lvOverview, 0, 1);
            _summaryGrid.Controls.Add(rightGrid, 1, 1);
            _summaryGrid.Controls.Add(bottom, 0, 2);
            _summaryGrid.SetColumnSpan(bottom, 2);

            _pageSummary.Controls.Add(_summaryGrid);
        }

        /// <summary>结果页里的键盘图：按 23:6.5 的比例在容器内居中铺满。</summary>
        private void LayoutResultKeyboard()
        {
            if (_kbdResult == null || _kbdResultHost == null)
                return;

            int hostW = _kbdResultHost.ClientSize.Width - Dpi.Px(16);
            int hostH = _kbdResultHost.ClientSize.Height - Dpi.Px(8);
            if (hostW < 120 || hostH < 50)
                return;

            double ratio = KeyboardLayout.TotalUnitsY / KeyboardLayout.TotalUnitsX;
            int w = hostW;
            int h = (int)Math.Round(w * ratio);
            if (h > hostH)
            {
                h = hostH;
                w = (int)Math.Round(h / ratio);
            }

            _kbdResult.Size = new Size(w, h);
            _kbdResult.Location = new Point(
                (_kbdResultHost.ClientSize.Width - w) / 2,
                (_kbdResultHost.ClientSize.Height - h) / 2);
        }

        /* ====================================================================
         *  数据填充
         * ==================================================================== */

        private void PopulateSummary()
        {
            if (_keyboard == null)
                return;

            int total = _keyboard.TotalKeyCount;
            int captured = _keyboard.CapturedKeyCount;
            double pct = total == 0 ? 0 : (captured * 100.0 / total);

            TimeSpan dur = (_tested && _testEnd > _testStart)
                ? (_testEnd - _testStart)
                : TimeSpan.Zero;

            /* ---- 结论横幅 ---- */
            if (captured >= total && total > 0)
            {
                _lblVerdict.ForeColor = Color.FromArgb(0, 110, 40);
                _lblVerdict.Text = string.Format(CultureInfo.InvariantCulture,
                    "✔ 检测通过 —— 全部 {0} 个键位均正常工作。（耗时 {1}）",
                    total, ReportExporter.FormatDuration(dur));
            }
            else if (captured == 0)
            {
                _lblVerdict.ForeColor = Color.FromArgb(160, 100, 0);
                _lblVerdict.Text = "⚠ 未产生有效检测数据，请点击下方『重新检测』。";
            }
            else
            {
                _lblVerdict.ForeColor = Color.FromArgb(170, 30, 30);
                _lblVerdict.Text = string.Format(CultureInfo.InvariantCulture,
                    "✖ 检测未通过 —— {0} / {1} 个键位正常（{2:F1}%），另有 {3} 个键位未收到反馈。（耗时 {4}）",
                    captured, total, pct, total - captured, ReportExporter.FormatDuration(dur));
            }

            /* ---- 键盘图切到结果模式 ---- */
            _kbdResult.Mode = KeyboardPanel.PanelMode.Result;
            _kbdResult.SelectedKeyId = 0;

            FillOverview(captured, total, dur);
            FillKeyDetail(null);
            LayoutResultKeyboard();
        }

        private void OnResultKeyClicked(object sender, KeyboardPanel.KeyState st)
        {
            FillKeyDetail(st);
        }

        /// <summary>往带分组的 ListView 里加一行「项目 / 值」。</summary>
        private static void AddPair(ListView lv, ListViewGroup group, string key, string value)
        {
            ListViewItem it = new ListViewItem(key);
            it.SubItems.Add(value == null ? "" : value);
            it.Group = group;
            lv.Items.Add(it);
        }

        private void AddDetail(string key, string value)
        {
            ListViewItem it = new ListViewItem(key);
            it.SubItems.Add(value == null ? "" : value);
            _lvKeyDetail.Items.Add(it);
        }

        /// <summary>显示某个按键在本次检测里的完整记录。传 null 则显示提示。</summary>
        private void FillKeyDetail(KeyboardPanel.KeyState st)
        {
            _lvKeyDetail.BeginUpdate();
            try
            {
                _lvKeyDetail.Items.Clear();

                if (st == null)
                {
                    AddDetail("提示", "点击键盘图上的任意一个按键，这里会显示它在本次检测中的记录。");
                    return;
                }

                string status;
                if (st.WasCaptured)
                    status = "完好 —— 已收到硬件反馈";
                else if (st.TimedOut)
                    status = "引导超时跳过 —— 3 秒内未收到任何反馈";
                else
                    status = "无响应 —— 未曾检测到";

                AddDetail("键位名称", st.Def.Name);
                AddDetail("检测结果", status);
                AddDetail("虚拟键码", "0x" + st.Def.Vk.ToString("X2") + "  (" +
                    KeyboardLayout.VirtualKeyName(st.Def.Vk) + ")");
                AddDetail("扫描码", "0x" + st.LastScanCode.ToString("X2"));
                AddDetail("扩展键", st.LastExtended ? "是（E0 前缀）" : "否");
                AddDetail("所在区域", RegionOf(st.Def));
                AddDetail("按下次数", st.DownCount.ToString(CultureInfo.InvariantCulture));
                AddDetail("首次按下", st.FirstDown == DateTime.MinValue
                    ? "—" : st.FirstDown.ToString("HH:mm:ss.fff"));
                AddDetail("最后按下", st.LastDown == DateTime.MinValue
                    ? "—" : st.LastDown.ToString("HH:mm:ss.fff"));
                AddDetail("累计按住", st.TotalHoldMs.ToString("F0", CultureInfo.InvariantCulture) + " ms");
                AddDetail("按键来源", st.LastInjected
                    ? "软件注入（由程序模拟，非物理按键）" : "物理按键");
            }
            finally
            {
                _lvKeyDetail.EndUpdate();
            }
        }

        /// <summary>左上角：检测数据 + 当前用户与用户组 + 硬件信息。</summary>
        private void FillOverview(int captured, int total, TimeSpan dur)
        {
            _lvOverview.BeginUpdate();
            try
            {
                _lvOverview.Items.Clear();
                _lvOverview.Groups.Clear();

                /* ---- 检测数据 ---- */
                ListViewGroup g1 = new ListViewGroup("检测数据");
                _lvOverview.Groups.Add(g1);
                AddPair(_lvOverview, g1, "检测开始",
                    _testStart == DateTime.MinValue ? "—" : _testStart.ToString("yyyy-MM-dd HH:mm:ss"));
                AddPair(_lvOverview, g1, "检测耗时", ReportExporter.FormatDuration(dur));
                AddPair(_lvOverview, g1, "键位总数", total.ToString(CultureInfo.InvariantCulture));
                AddPair(_lvOverview, g1, "完好", captured.ToString(CultureInfo.InvariantCulture));
                AddPair(_lvOverview, g1, "引导超时跳过", _skippedCount.ToString(CultureInfo.InvariantCulture));
                AddPair(_lvOverview, g1, "无响应", (total - captured).ToString(CultureInfo.InvariantCulture));
                AddPair(_lvOverview, g1, "覆盖率", total == 0 ? "0%" :
                    string.Format(CultureInfo.InvariantCulture, "{0:F1}%", captured * 100.0 / total));
                AddPair(_lvOverview, g1, "键盘接管", _chkBlock.Checked ? "已开启" : "已关闭");

                /* ---- 当前用户 ---- */
                ListViewGroup g2 = new ListViewGroup("当前用户");
                _lvOverview.Groups.Add(g2);
                AddPair(_lvOverview, g2, "用户账户", _machine.UserName);
                AddPair(_lvOverview, g2, "登录域 / 机器",
                    _machine.Domain.Length > 0 ? _machine.Domain : "（本地账户）");
                AddPair(_lvOverview, g2, "完整性级别",
                    _machine.IsAdmin ? "管理员（High Mandatory Level）" : "标准用户（Medium）");
                AddPair(_lvOverview, g2, "用户组数量",
                    _machine.Groups.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < _machine.Groups.Count; i++)
                    AddPair(_lvOverview, g2, "用户组 [" + (i + 1) + "]", _machine.Groups[i]);

                /* ---- 硬件信息 ---- */
                ListViewGroup g3 = new ListViewGroup("硬件信息");
                _lvOverview.Groups.Add(g3);
                AddPair(_lvOverview, g3, "计算机名", _machine.ComputerName);
                AddPair(_lvOverview, g3, "操作系统", _machine.OsVersion);
                AddPair(_lvOverview, g3, "CPU", _machine.Cpu);
                AddPair(_lvOverview, g3, "内存", _machine.RamTotal);
                AddPair(_lvOverview, g3, "整机型号", _machine.SystemModel);
                AddPair(_lvOverview, g3, "主板", _machine.BaseBoard);
                AddPair(_lvOverview, g3, "BIOS 日期", _machine.BiosDate);

                if (_sysInfo != null)
                {
                    AddPair(_lvOverview, g3, "键盘类型", _sysInfo.KeyboardType + " " +
                        ReportExporter.KeyboardTypeText(_sysInfo.KeyboardType));
                    AddPair(_lvOverview, g3, "键盘布局 KLID", _sysInfo.KeyboardLayoutId);
                }

                for (int i = 0; i < _devices.Count; i++)
                {
                    KeyboardDeviceInfo d = _devices[i];
                    AddPair(_lvOverview, g3, "键盘设备 [" + (i + 1) + "]", d.ShortName);
                    AddPair(_lvOverview, g3, "　└ 接口 / 驱动", d.ConnectionType + "  ·  " +
                        (d.DriverVersion.Length > 0 ? d.DriverVersion : "驱动版本未知"));
                }
            }
            finally
            {
                _lvOverview.EndUpdate();
            }
        }
    }
}