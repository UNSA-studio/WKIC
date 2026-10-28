# Windows Keyboard Integrity Check

**Windows 键盘完整性检测 · WinUI 3**  
开发者：UNSA-studio · 联系邮箱：UNSA-studio@outlook.com  
应用版本保持 **1.0.0**，此次仅迁移界面与构建体系，没有升级版本号。

## 下载与运行

WinUI 3 构建提供两种完整分发包：

- **安装 EXE**：安装程序会安装应用以及它所需的 .NET、Windows App SDK 运行库；不需要 Visual Studio 或手动安装开发环境。
- **免安装 ZIP**：完整解压后运行其中的 `WindowsKeyboardIntegrityCheck.exe`。**不能只复制一个 EXE**，原生 WinUI 3 运行库需要跟随应用分发。

下载入口：GitHub 仓库 `UNSA-studio/WKIC` 的 Releases → `winui3`。成功构建后，安装 EXE 也会更新到 `dist/WindowsKeyboardIntegrityCheck.exe`，继续支持原来的 raw 下载地址；注意该地址现在是**安装程序**，不再是旧 WinForms 的小型单文件程序。

系统要求：**Windows 10 1809（17763）及以上 / Windows 11，x64**。不再支持 Windows 7 / 8。项目预留 ARM64 配置，当前 CI 分发与测试的是 x64。

运行应用需**右键 → 以管理员身份运行**。普通权限启动会显示：

> Windows键盘完整性检测需要依靠管理员运行,请使用管理员权限打开

## 界面与功能

这不是 WinForms 主题包或 WebView 仿制：主窗口和键盘图均使用 `Microsoft.UI.Xaml`，标准控件来自 Windows App SDK。

- 四步向导：使用说明 → 设备信息 → 按键检测 → 结果。
- 原生 `NavigationView`、`Button`、`InfoBar`、`Expander`、`ScrollViewer`，支持系统主题、浅色、深色和高对比度。
- 全部说明、数据值自动换行，不用固定列宽截断长设备名、账户或驱动信息。
- 触屏滚动直接交给 WinUI `ScrollViewer`，移除旧版鼠标消息模拟滚动补丁。
- 使用设备无关单位，不再手工倍乘字体和位置；窄窗口中结果页上下排列，键盘可横向滚动。
- EXE、窗口标题栏和任务栏均使用 `assets/app.ico`，不冒充微软出品。

### 检测流程

1. 当前目标闪烁高亮，等待用户按下并松开。
2. 收到按下事件即显示主题色；收到抬起后确认完整响应，并前进。
3. **3 秒内未按下**自动跳过。按住某键不等于没有按下，不会因为按住超过 3 秒而误判超时。
4. 非当前目标同样可以提前检测；后续自动跳过已经完整响应的键。
5. 超时键可在会话结束前补测。
6. 双击同一颗回车键结束；必须是两次完整的按下／松开，长按自动重复不算双击。两次间隔 450 毫秒。
7. 回车键不能用时，点击左下角**完成检测**。
8. `Ctrl+Alt+Shift+Q` 紧急停止；离开测试页、窗口失去焦点、关闭窗口或界面超时也会解除接管。

设置里可选 104 键 ANSI 或 87 键 TKL（无数字小键盘）布局，也可关闭键盘接管。布局变化用于下一次测试，主题开关立即生效。

### 键盘接管与边界

- 全局 `WH_KEYBOARD_LL` 钩子运行在**独立消息循环线程**，回调不访问 XAML、WMI 或磁盘；UI 从队列读取事件。
- 接管仅在测试期间启用。通常的 Win 键、Alt+Tab 等输入会被吞掉；鼠标和触屏不被锁定。
- 紧急停止和看门狗与 UI 线程独立，UI 超时后解除接管。
- **Ctrl+Alt+Del 等安全桌面操作无法由此钩子屏蔽**。Fn、电源、部分厂商功能键可能不向 Windows 键盘输入栈发送普通事件。
- 注入标志为真的软件模拟事件不会被计作物理键盘通过。
- 多台键盘连接时，测试记录是**合并输入**。枚举设备并不等于已经把事件绑定到某一台键盘。

### 结果页

- 左侧：本次数据、账户、用户组、机器硬件、键盘设备、驱动信息和采集过程；支持换行与触屏滚动。
- 右侧：完整结果键盘，与检测阶段共享**同一组状态记录**。
- **绿**：收到完整按下／抬起；**红**：超时未响应／疑似故障；**琥珀**：缺少抬起反馈；**灰**：未检测。
- 点击任意键才显示扫描码、实际 VK、扩展位、次数、重复次数、时间、累计按住时长与该键事件记录。点击键盘图不会被算作物理按键。
- 未按、无此键、Win Lock、固件、驱动或布局等均可能导致未响应，因此不能单凭超时断定硬件损坏。
- 导出 TXT 包含完整设备信息、逐键记录和时间线，不裁剪长文本。输出位置由保存对话框选择。

### 硬件信息

使用 Raw Input / WMI / 注册表查询设备名称、VID/PID、PnP 状态、驱动版本、驱动日期、提供商、INF、签名者以及系统账户和硬件概况。WMI 按设备类别一次查询，使用完整设备实例 ID 匹配，不把同型号的不同键盘误认成同一实例。

硬件出厂年份未公开时明确显示“未知”，**不把驱动日期或 BIOS 日期当成键盘生产年份**。

## 签名

CI 保留免费自签名，Subject 包含：

```text
CN=UNSA-studio, O=UNSA-studio, E=UNSA-studio@outlook.com, C=CN
```

应用和安装程序签名，免安装包附带对应公钥 `.cer`；不会自动更改用户的受信任根证书存储。

**自签名不等于 CA 信任或 SmartScreen 声誉，不保证消除“未知发布者”提示。** 任何人都可以生成写有相同名称的自签名证书，名称和邮箱本身不能证明发布者身份。下载来源与 SHA-256 校验仍需核实。

若为 CI 配置 `SIGNING_PFX`（base64）和 `SIGNING_PASSWORD` Secrets，可复用自己的稳定证书；不配置时每次构建生成临时自签名证书，旧证书的本机信任不会自动信任下一次生成的新证书。私钥不得提交仓库。

## 构建与验证

Windows 构建环境：.NET 8 SDK、Visual Studio 2022 MSBuild / Windows SDK；CI 自动准备。项目依赖固定到 Windows App SDK `1.8.260921001`。

```powershell
dotnet run --project tests/CoreTests.csproj -c Release
msbuild WindowsKeyboardIntegrityCheck.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:PublishDir=artifacts\publish\
```

本地也可以运行 `build.bat`。发布目录必须整体保留。安装程序使用 Inno Setup 构建（`packaging/WKIC.iss`）。

CI 顺序：

1. 核心回归：左右修饰键、主／小键盘回车、NumLock 关闭、Win 键、3 秒跳过、补测、重复事件、注入过滤、重置、完整 104 键等。
2. 编译真正的 WinUI XAML 并发布自包含目录。
3. **实际启动 WinUI 进程**，验证键盘有尺寸且全部键位已建立、点击记录与会话数据一致、原生欢迎页可滚动，渲染浅色／深色／窄窗口 PNG。该测试明确使用模拟数据，**不是实际硬件认证**，不安装键盘钩子、不接管 CI 机器键盘。
4. 签名应用，打包安装 EXE 与免安装 ZIP，发布并附 SHA-256。
5. `WKIC-UI-verification` artifact 保存测试证据、PNG、构建日志。触屏真机手势和不同硬件组合仍需实机验收。

## 目录

```text
App.xaml / App.xaml.cs        WinUI Application
Program.cs                   权限检查与 WinUI 入口
MainWindow.xaml / .cs        导航、四步界面、结果与设置
Controls/KeyboardView.*      WinUI 按键图与点击记录
Core/TestSession.cs          与 UI 无关的检测状态机
Core/KeyboardCapture.cs      独立线程 Win32 钩子与安全释放
Services/Inventory.cs        设备／驱动采集
Services/Report.cs           完整报告及管理员可用的保存对话框
src/                         复用的硬件 P/Invoke 和键位表
tests/                       核心回归测试
packaging/                   自包含安装程序脚本
legacy/WinForms/             旧界面源码归档，不参与新项目编译
```
