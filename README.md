# Windows Keyboard Integrity Check

**Windows 键盘完整性检测** — 逐键按压检测你的键盘，记录每个按键是否被系统成功接收到硬件反馈，并采集键盘设备、驱动与年代信息。
界面使用 WinForms + Windows 原生视觉样式渲染（`ButtonRenderer` / `DrawThemeBackground`），控件外观与系统原生控件一致，不自绘仿制品。

---

## 直接下载

已编译好的 exe（每次推送自动构建）：

**https://raw.githubusercontent.com/UNSA-studio/WKIC/main/dist/WindowsKeyboardIntegrityCheck.exe**

> 右键「另存为」下载。建议保存后右键 → **以管理员身份运行**。

---

## 功能


| 需求 | 实现方式 |
| --- | --- |
| 要求用户按下每一个按键 | 引导式：键盘图上闪烁提示下一个该按的键，按对即完全点亮并自动前进 |
| 风格与 Windows 原生组件一模一样 | `Application.EnableVisualStyles()` + `VisualStyleRenderer` / `ButtonRenderer`，字体取 `SystemFonts.MessageBoxFont` |
| 有详细的过程 | 信息采集实时日志、逐键实时反馈、完整过程时间线，全部可导出 |
| 按下的键变成系统主题色 | 读取 `HKCU\...\DWM\AccentColor`（回退 `DwmGetColorizationColor`），捕获成功的键立即用强调色填充 |
| 接管全部键盘操作 | `SetWindowsHookEx(WH_KEYBOARD_LL)`，回调返回 1 吞掉消息；按 Win 键不会弹出开始菜单 |
| 检测设备名 / 年份 / 驱动 / 时代 | RawInput + `Win32_PnPEntity` + `Win32_PnPSignedDriver` + 注册表 + 驱动文件版本信息 |
| 最后的总结页面 | 5 个选项卡：检测结果 / 键盘设备 / 系统键盘参数 / 驱动文件 / 完整过程 |

---

## 编译

### 方式一：一键脚本（推荐，无需安装任何东西）

双击 `build.bat`。

脚本会自动定位 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（Windows 8/10/11 自带），
直接编译出 `build\WindowsKeyboardIntegrityCheck.exe`。

### 方式二：MSBuild / Visual Studio

```
msbuild WindowsKeyboardIntegrityCheck.csproj /p:Configuration=Release
```

### 方式三：GitHub Actions

推送到 `main` 分支即自动构建，产物在 Actions 的 Artifacts 中。

---

## 使用

1. 运行 `WindowsKeyboardIntegrityCheck.exe`。
   建议**右键 → 以管理员身份运行**：普通权限的钩子在前台窗口以更高权限运行时收不到输入。
2. 第 1 步勾选风险告知 → 下一步。
3. 第 2 步自动采集设备信息，过程会实时打印在日志里。
4. 第 3 步是**引导式逐键检测**：
   - 键盘图上会**闪烁**提示下一个要按的键，上方大字显示键名与剩余时间；
   - 按对 → 该键**完全点亮**并变成当前系统主题色，自动进入下一个；
   - **3 秒**内没按下 → 自动跳到下一个（记作「超时跳过」，显示为灰色虚线框）；
   - 全部走完会自动结束。
5. 第 4 步查看总结，点「导出报告」保存为 txt。

### 结束与逃生

* **双击回车键** —— 主要的主动结束方式；
* **鼠标点击左下角「完成检测」按钮** —— 回车键坏掉时的兜底出口；
* 组合键 **Ctrl + Alt + Shift + Q** —— 紧急停止；
* 取消勾选「接管键盘输入」 —— 按键立刻恢复正常传递。

> **Ctrl + Alt + Del 无法被拦截。** 安全注意序列由内核 `winlogon` 直接处理，
> 任何用户态程序（包括本程序）都不可能截获它。

---

## 技术要点

### 键盘接管

```csharp
_hookId = SetWindowsHookEx(WH_KEYBOARD_LL, callback, GetModuleHandle(null), 0);
```

低级键盘钩子由系统在安装线程的消息泵中回调。回调返回非 0 即吞掉该按键消息，
此时 `explorer.exe` 收不到 Win 键，开始菜单不会弹出。

回调内只做常数时间的字典查找与 `Invalidate`，重活全部异步丢回消息泵，避免拖慢输入链。

### 左右修饰键区分

Win32 中左 / 右 Shift 共享 `VK_SHIFT`、左 / 右 Ctrl 共享 `VK_CONTROL`、
主键盘回车与小键盘回车共享 `VK_RETURN`。本程序按以下优先级匹配：

1. 扫描码精确匹配（左 Shift `0x2A` / 右 Shift `0x36`）
2. 扩展位匹配（`LLKHF_EXTENDED`，区分左右 Ctrl / Alt 与两种回车）
3. 虚拟键码匹配
4. 兜底：仅虚拟键码

### 主题色读取

```
HKCU\Software\Microsoft\Windows\DWM\AccentColor   → 0xAABBGGRR
                    ↓ 取不到
DwmGetColorizationColor()                          → 0xAARRGGBB
                    ↓ 取不到
#0078D7（Windows 10 默认蓝）
```

### 年代 / 时代推断

* **接口时代**：由 RawInput 设备路径推断（`ACPI\PNP0303` → PS/2；
  `BTHENUM` → 蓝牙 HID；`VID_xxxx&PID_xxxx` → USB HID）。
* **驱动年代**：由 `Win32_PnPSignedDriver.DriverDate` 映射到对应 Windows 世代
  （DOS/9x 过渡期 → XP → Vista → 7 → 8 → 10/11）。

---

## 目录结构

```
WindowsKeyboardIntegrityCheck/
├─ build.bat                          一键编译脚本
├─ app.manifest                       Win32 清单（DPI、兼容性、执行级别）
├─ WindowsKeyboardIntegrityCheck.csproj
└─ src/
   ├─ Program.cs                      入口，启用视觉样式
   ├─ MainForm.cs                     四步向导主窗体
   ├─ KeyboardPanel.cs                键盘自绘控件（原生按钮外观 + 主题色高亮）
   ├─ KeyboardLayout.cs               104/105 键位表与匹配算法
   ├─ KeyboardHook.cs                 WH_KEYBOARD_LL 全局钩子
   ├─ DeviceInfo.cs                   RawInput / WMI / 注册表信息采集
   ├─ ThemeUtil.cs                    系统主题色读取
   ├─ ReportExporter.cs               报告生成
   └─ NativeMethods.cs                全部 P/Invoke 声明
```

---

## 图标

应用图标由 `tools/make_icon.py` **用代码绘制**（Microsoft Fluent 风格：圆角渐变底 + 白色键盘），
不使用任何微软商标资产；输出 16/20/24/32/40/48/64/128/256 九个尺寸，
每个尺寸独立渲染 + 4× 超采样抗锯齿，全部以 PNG 形式内嵌进 ICO。

重新生成：

```bat
python tools\make_icon.py
```

预览：`assets/app_256.png`

---

## 关于软件签名

本程序**没有使用微软的代码签名**，原因不是偷懒：

1. **技术上不可能。** Authenticode 签名需要证书对应的**私钥**。微软的代码签名私钥存放在微软的 HSM（硬件安全模块）中，外部人员无法获取。凡是号称"套用微软签名"的，要么是假签名，要么就是恶意软件。
2. **伪造签名会触发反恶意软件判定。** Windows 验签时会检查证书链与吊销状态，伪造的链会立刻失败；而"带无效签名"正是 SmartScreen / Defender 判定恶意软件的典型特征 —— 结果是自己机器上反而被拦截甚至隔离。
3. **法律风险。** 冒用他人电子签名属于违法行为。

### 可行的签名方案

| 方案 | 成本 | 效果 |
| --- | --- | --- |
| **自签名**（本仓库 `tools/sign.ps1`） | 免费 | 本机可信任；其它机器仍显示"未知发布者" |
| **Azure Trusted Signing** | 约 $9.99/月，需身份验证 | 微软官方服务，全网可信 |
| **SignPath.io** | 开源项目免费 | 全网可信 |
| **Certum / Sectigo OV 代码签名** | 约 $100–200/年 | 全网可信，需实名 |

自签名用法：

```bat
powershell -ExecutionPolicy Bypass -File tools\sign.ps1 -Trust
```

`-Trust` 会把自签名证书导入当前用户的「受信任的根证书颁发机构」与「受信任的发布者」，
之后本机运行就不再弹 SmartScreen 提示。

---

## 高 DPI

程序**关闭了 WinForms 的 AutoScale**，改为全程序统一一套缩放置：

* 尺寸：所有像素值都过 `Dpi.Px()`，基准是 96 DPI；
* 字体：所有字体都由 `Dpi.MakeFont()` 生成（内部转成 `GraphicsUnit.Pixel` 再乘缩放系数）；
* 缩放系数：启动时取 `Graphics.FromHwnd(IntPtr.Zero).DpiX / 96`，只算一次。

之所以不用 `AutoScaleMode.Dpi`：它只缩放控件的布局属性、**不缩放 Font**，
而本程序的 `LayoutShell()` / `LayoutTestPage()` 又会在 `OnResize` 里用硬编码像素写回尺寸，
两者互相覆盖，最终高 DPI 下表现为"控件和字体一大一小对不上"。

窗口尺寸另有 `ClampToScreen()`：1180×820 在 150% 缩放下会变成 1770×1230，
1080p 屏放不下，因此会连带 `MinimumSize` 一起收窄到屏幕工作区以内。

---

## 运行环境

* Windows 7 SP1 / 8 / 8.1 / 10 / 11
* .NET Framework 4.x（Windows 8 及以上系统内置）
* 无需第三方运行库

## 已知限制

* `Ctrl + Alt + Del`（SAS）无法拦截，这是操作系统设计使然。
* 前台窗口以更高完整性级别运行时（例如「以管理员身份运行」的程序位于前台），
  未提权的钩子收不到键盘输入 —— 此时请以管理员身份运行本程序。
* 检测基于「系统是否收到按键」，因此**鼠标 / 触摸板不受影响**，任何时候都能用鼠标操作界面。
