# ADR-0001: 聊天浮窗采用 WPF 窗口实现，轮询仅在浮窗启用时运行

## 状态
已接受（2026-07-08）

## 背景
需求：聊天浮窗（浮窗）——置顶、透明圆角、平滑淡入淡出、游戏在前台时鼠标穿透、
游戏不在前台时可拖动/边缘缩放、窗口高度容纳多少条消息就显示多少条、
位置与大小持久化、显示时长与淡出速度可调。

应用主窗口是 WinUI 3（Windows App SDK 1.5，.NET 6）。技术路线候选：

- **WinUI 3 窗口**：1.5 版本不支持窗口逐像素透明（圆角透明/半透明需要 Win32
  分层窗口黑科技，且与 DWM swapchain 渲染管线冲突，效果受限、脆弱）。
- **WPF 窗口**：`AllowsTransparency=True` 原生提供逐像素透明、圆角与动画；
  项目已依赖 WPF 组件（托盘图标经 H.NotifyIcon 走 WPF/HwndSource 桥接），
  .NET 6 Windows Desktop 官方支持 WPF 与 WinUI 同进程混用。

另一个架构问题：原程序是被动代理——只有浏览器面板轮询 `/gamechat` 时才拉取游戏
消息并翻译；浏览器关闭后程序对游戏消息完全无感知。浮窗要独立工作，程序必须自己
轮询游戏本地端口 8111，这会持续产生 AI 翻译请求（成本）。

## 决策
1. 浮窗用 **WPF Window**（`AllowsTransparency` + `WindowStyle=None` + `Topmost` +
   `ShowInTaskbar=False` + `ShowActivated=False`）。WPF 浮窗整体运行在**专用 STA
   线程**上：控制器启动该线程，线程内创建 WPF `Application`
   （`ShutdownMode=OnExplicitShutdown`）与窗口并 `Dispatcher.Run()`。所有窗口操作
   经 WPF `Dispatcher.BeginInvoke` 切到该线程。理由：WinUI 3 的
   `Microsoft.UI.Xaml.Application` 没有 `Dispatcher` 属性（那是 WPF 的概念），
   主窗口又是懒创建的，启动时拿不到 WinUI `DispatcherQueue`；专属线程自洽且
   WPF 动画时钟由自己的消息泵驱动，与 WinUI 线程零耦合。
2. 鼠标穿透用 Win32 扩展样式 `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` 切换；
   `GameFocusWatcher` 每 300ms 检查前台窗口（进程名 `WarThunder` 或窗口标题以
   "War Thunder" 开头），状态变化时切换。
3. 拖动 = 窗口内任意位置按下 `DragMove()`（鼠标事件绑定在窗口级、**隧道
   Preview 阶段**，避免 ScrollViewer 等子元素先吞事件）；缩放 = 12px 边缘/
   角落命中区（悬停时光标变箭头提示）+ 手动改 Left/Top/Width/Height（最小
   220×110，限制在工作区内）；位置/尺寸持久化到
   `FloatWindow_Position` / `FloatWindow_Size`。消息列表套 ScrollViewer
   （隐藏滚动条）：新消息自动回底、窗内滚轮翻看旧消息、鼠标移出或来新消息
   自动回底。长按回车（≥0.5s 松开、仅游戏前台响应）切换固定显示
   （`EnterHoldWatcher`，`GetAsyncKeyState` 30ms 轮询、无键盘钩子；
   「互动和操作」页可关）。
4. 内置轮询器（`ChatPoller`，2 秒间隔）**仅当浮窗启用时运行**；翻译缓存
   （`ChatService`，`ConcurrentDictionary`）与浏览器面板的 `/gamechat` 接口共享，
   同一条消息只调用一次 AI。面板行为保持不变（面板本来就全量拉取、整页重渲染）。
   轮询器顺带探测 `/map_info.json` 的 `valid` 字段监视对局边界（对局中 true、
   菜单 false；`/mission.json` 与 `/indicators` 菜单下缓存不变、不可用），
   按「浮窗清空时机」设置清空（退出对局立即清空 / 下一局开始时清空，默认前者），
   边界同时清空翻译缓存（消息 ID 可能跨对局重复）。
5. 显示时长（默认 3s）与淡出速度（默认 0.5s，淡入淡出共用）分开两个设置；
   背景不透明度（0%-100%，默认 85%）**只作用于浮窗背景**（改背景刷 alpha，
   窗口 Opacity 恒为 1、文字始终全不透明）、显示原文（默认关）、固定显示
   （默认关）为附加设置，统一放在「位置和布局」页。浮窗文字颜色**统一取自
   「字体和样式」页**（该页颜色同时作用于浏览器面板，改后经
   `FloatWindowController.ApplySettings()` 实时同步到浮窗），浮窗页不另设颜色。
   文字描边（默认关）：8 方向偏移副本生成轮廓（关闭时 Collapsed、零开销）；
   开关、颜色（默认白）、宽度（0.5-4px，默认 1）全部在「字体和样式」页
   （描边是文字外观属性，与文字颜色同页管理）。

## 后果
- ✅ 透明圆角 + 平滑淡入淡出零成本，无 Win32 渲染黑科技；
- ✅ 轮询只在需要时运行，AI 成本可控；翻译缓存共享，面板/浮窗不重复请求；
- ✅ 面板路径行为不变（`/gamechat` 语义保持）；
- ⚠️ 进程内并存 WPF/WinUI 双 UI 框架（项目已有 WPF 依赖，增量成本小，
  注意命名空间歧义：`System.Windows.Application` vs `Microsoft.UI.Xaml.Application`）；
- ⚠️ 游戏独占全屏时浮窗不可见（Windows 合成层限制，非本应用可解），
  用户需将游戏切为无边框全屏（交付说明中提示）；
- ⚠️ 显示时长计时期间若用户在拖动/缩放浮窗，窗口仍会到点淡出（可接受：
  用户随时可再触发或开固定显示）。

## 构建陷阱（本仓库特有，改 XAML 前必读）
- WinUI 的 XAML 由 **net472 的 XamlCompiler.exe** 预编译，遇到错误**静默退出 1**，
  不打印任何诊断（MSB3073 只会说"已退出，代码为 1"）。**注意：C# 编译错误也会
  导致它静默失败**（interop 路径会先把 C#+XAML 一起预编译，如 `Windows.UI.Colors`
  不存在 → CS0234 → XamlCompiler exit 1）。排查顺序：先看完整构建日志里的
  CS#### 错误；确认 C# 无错后再按 `obj\x64\Release\...\input.json` + 手动跑
  XamlCompiler 对 XAML 文件二分。
- **WinUI XAML 文件必须有 UTF-8 BOM**（编辑器/脚本剥离 BOM 后，net472 编译器按
  GBK 误解码，同样静默失败）。
- `SymbolIconSource.Symbol` 的枚举是 WinAppSDK 1.5 的 197 个经典符号，
  **没有 `Window`**（只有 `NewWindow`/`BackToWindow`）；浮窗菜单项用的是 `View`。
  枚举可用值可反射 `Microsoft.WinUI.dll` 的 `Microsoft.UI.Xaml.Controls.Symbol` 验证。
- `UseWPF=true` 与 `UseWinUI=true` 不能放同一个项目：WPF 的 MarkupCompilePass 会
  把 WinUI 的 Page 项也拿去编译（MC3072/MC3074 一堆"命名空间不存在"）。
  因此 WPF 浮窗独立在 `WarThunderChatTranslator.Float` 类库（UseWPF），
  主工程经 ProjectReference 传递获得 WPF 程序集。
- WPF 的 `Grid`（Panel）没有 `FontSize`/`FontFamily` 实例属性，
  要用 `TextElement.FontSize`/`TextElement.FontFamily` 附加属性。
- WinAppSDK 1.5 的 `MenuFlyoutItem` **没有 `IsChecked`**（WPF 的 MenuItem 才有）；
  托盘菜单"浮窗"项的选中状态改用 `Icon` 切换（开=CheckMark 字体图标，关=View 符号）。
- WinUI 的 `Slider`/`ToggleSwitch` 事件参数类型不便显式引用（`RangeBase` 在
  `Primitives` 子命名空间、`ToggledEventArgs` 在引用程序集里不存在），
  设置页改用 lambda 接线（`+= (s, e) => ...`），编译器自行绑定委托类型。
