using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 浮窗协调器：管理启用状态（持久化）、内置轮询器、游戏前台侦测，
    /// 并把消息推进 WPF 浮窗。
    /// WPF 浮窗运行在专用 STA 线程上（自带消息泵），所有窗口操作经
    /// WPF Dispatcher 切到该线程执行，不依赖 WinUI 主线程的调度 API。
    /// </summary>
    public sealed class FloatWindowController
    {
        public static FloatWindowController Instance { get; private set; }

        private Thread _uiThread;
        private Dispatcher _uiDispatcher;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

        private FloatWindow _window;
        private ChatPoller _poller;
        private GameFocusWatcher _focusWatcher;
        private EnterHoldWatcher _enterWatcher;
        private IReadOnlyList<ChatMessage> _latest;
        private volatile bool _enabled;
        private volatile bool _disposed;

        // 设置快照（LoadSettings 刷新，在 WPF 线程应用）
        private double _displayDuration = 3;
        private double _fadeSpeed = 0.5;
        private double _opacity = 0.20;
        private bool _showOriginal;
        private bool _showChannelTag = true;
        private bool _pinned;
        private bool _outlineEnabled;
        private string _outlineColor = "#FFFFFFFF";
        private double _outlineWidth = 1;
        private double _lineSpacing; // 行间距（像素，加在字体自然行高之上，0=默认）
        private string _clearMode = "exit"; // "exit"=退出对局立即清空，"next"=下一局开始时清空
        private bool _enterTogglePin = true; // 长按回车切换浮窗固定显示（仅游戏前台时生效）
        private int _enterHoldMs = EnterHoldWatcher.DefaultHoldThresholdMs; // 长按回车判定阈值
        private bool _hideWhenGameInactive; // 游戏（且翻译器自己）都不在前台时隐藏浮窗

        public bool Enabled => _enabled;

        /// <summary>
        /// 在 App.OnLaunched 调用。启动 WPF 浮窗线程。
        /// 环境变量 WCT_FLOAT_ON=1 可强制启用（开发测试用）。
        /// </summary>
        public void Init()
        {
            Instance = this;

            _enabled = Environment.GetEnvironmentVariable("WCT_FLOAT_ON") == "1"
                || ParseBool(ApplicationConfig.GetSettings("FloatWindow_Enabled"));

            LoadSettings();

            _uiThread = new Thread(RunWpf) { IsBackground = true, Name = "WctFloatWindow" };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();

            // 等浮窗线程就绪（创建 WPF Application 需要一点时间）；
            // 超时不致命——后续 BeginUi 会继续等待 IsSet
            _ready.Wait(TimeSpan.FromSeconds(5));
        }

        public void SetEnabled(bool on)
        {
            if (_disposed || _enabled == on)
            {
                return;
            }

            _enabled = on;
            ApplicationConfig.SaveSettings("FloatWindow_Enabled", on ? "true" : "false");

            BeginUi(() =>
            {
                if (_disposed)
                {
                    return;
                }

                if (on)
                {
                    LoadSettings();
                    StartFloat();
                }
                else
                {
                    _poller?.Stop();
                    _enterWatcher?.Stop();
                    _window?.HideNow();
                }
            });
        }

        /// <summary>设置页改动后调用：刷新设置快照并应用到浮窗。</summary>
        public void ApplySettings()
        {
            if (_disposed || !_enabled)
            {
                return;
            }

            LoadSettings();

            BeginUi(() =>
            {
                if (_window == null)
                {
                    return;
                }

                ApplyAppearance();
                SyncEnterWatcher();
                ApplyFocusGate();
                // 重渲染现有消息，使描边/颜色等外观改动立即生效（不使隐藏窗口出现）
                if (_pinned || _window.IsVisible)
                {
                    _window.UpdateMessages(_latest ?? new List<ChatMessage>());
                }
            });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                _poller?.Stop();
            }
            catch
            {
                // 忽略：进程正在退出
            }
            try
            {
                _focusWatcher?.Stop();
            }
            catch
            {
                // 忽略：进程正在退出
            }
            try
            {
                _enterWatcher?.Stop();
            }
            catch
            {
                // 忽略：进程正在退出
            }
            try
            {
                if (_ready.IsSet && _uiDispatcher != null && !_uiDispatcher.HasShutdownStarted)
                {
                    _uiDispatcher.Invoke(() =>
                    {
                        _window?.Close();
                        _window = null;
                    });
                    _uiDispatcher.InvokeShutdown();
                }
            }
            catch
            {
                // 忽略：进程正在退出
            }
            Instance = null;
        }

        // ---------- WPF 浮窗线程 ----------

        private void RunWpf()
        {
            try
            {
                _uiDispatcher = Dispatcher.CurrentDispatcher;

                if (System.Windows.Application.Current == null)
                {
                    var wpfApp = new System.Windows.Application();
                    wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                }

                _focusWatcher = new GameFocusWatcher();
                _focusWatcher.GameForegroundChanged += OnGameForegroundChanged;
                _focusWatcher.SelfForegroundChanged += OnSelfForegroundChanged;
                _focusWatcher.Start();

                if (_enabled)
                {
                    StartFloat();
                }

                _ready.Set();
                Dispatcher.Run();
            }
            catch
            {
                // 浮窗线程崩溃不影响主程序：放行等待者后退出
                _ready.Set();
            }
        }

        // ---------- 以下方法都在 WPF 浮窗线程执行 ----------

        private void StartFloat()
        {
            EnsureWindow();
            if (_poller == null)
            {
                _poller = new ChatPoller();
                _poller.MessagesReceived += OnMessagesReceived;
                _poller.MatchStarted += OnMatchStarted;
                _poller.MatchEnded += OnMatchEnded;
            }
            _poller.Start();
            SyncEnterWatcher();
            if (_pinned)
            {
                // 固定模式：即使暂无消息也立即显示（占位提示）
                _window.UpdateMessages(_latest ?? new List<ChatMessage>());
            }
        }

        private void EnsureWindow()
        {
            if (_window != null)
            {
                return;
            }

            _window = new FloatWindow();
            _window.LayoutChanged += OnLayoutChanged;

            double? left = null;
            double? top = null;
            var pos = ApplicationConfig.GetSettings("FloatWindow_Position");
            if (!string.IsNullOrWhiteSpace(pos))
            {
                var parts = pos.Split(',');
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double l)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                {
                    left = l;
                    top = t;
                }
            }

            double? width = null;
            double? height = null;
            var size = ApplicationConfig.GetSettings("FloatWindow_Size");
            if (!string.IsNullOrWhiteSpace(size))
            {
                var parts = size.Split(',');
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double w)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
                {
                    width = w;
                    height = h;
                }
            }

            _window.RestoreLayout(left, top, width, height);
            ApplyAppearance();

            // 初始穿透状态：游戏在前台则立即穿透
            _window.SetClickThrough(GameFocusWatcher.IsGameInForeground());
            ApplyFocusGate();
        }

        private void OnLayoutChanged(double left, double top, double width, double height)
        {
            ApplicationConfig.SaveSettings("FloatWindow_Position",
                string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##}", left, top));
            ApplicationConfig.SaveSettings("FloatWindow_Size",
                string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##}", width, height));
        }

        private void OnMessagesReceived(IReadOnlyList<ChatMessage> messages)
        {
            if (_disposed || !_enabled)
            {
                return;
            }

            BeginUi(() =>
            {
                if (_disposed || !_enabled)
                {
                    return;
                }
                _latest = messages;
                EnsureWindow();
                _window.UpdateMessages(messages);
            });
        }

        // ---------- 对局边界（轮询器线程触发） ----------

        private void OnMatchStarted()
        {
            if (_disposed || !_enabled)
            {
                return;
            }
            // 新局开始：消息 ID 可能重复，先清翻译缓存
            ChatService.ClearTranslationCache();
            if (_clearMode == "next")
            {
                BeginUi(() =>
                {
                    if (_disposed || !_enabled)
                    {
                        return;
                    }
                    _latest = null;
                    _window?.ClearMessages();
                });
            }
        }

        private void OnMatchEnded()
        {
            if (_disposed || !_enabled)
            {
                return;
            }
            ChatService.ClearTranslationCache();
            if (_clearMode == "exit")
            {
                BeginUi(() =>
                {
                    if (_disposed || !_enabled)
                    {
                        return;
                    }
                    _latest = null;
                    _window?.ClearMessages();
                });
            }
        }

        private void OnGameForegroundChanged(bool gameForeground)
        {
            if (_disposed)
            {
                return;
            }

            BeginUi(() =>
            {
                if (!_disposed)
                {
                    _window?.SetClickThrough(gameForeground);
                    ApplyFocusGate();
                }
            });
        }

        private void OnSelfForegroundChanged(bool selfForeground)
        {
            if (_disposed)
            {
                return;
            }

            BeginUi(() =>
            {
                if (!_disposed)
                {
                    ApplyFocusGate();
                }
            });
        }

        /// <summary>
        /// 「游戏不在前台时隐藏浮窗」：翻译器自己的界面在前台时视同在前台，
        /// 这样改设置能实时看到浮窗效果。解除隐藏后固定模式立即恢复显示，
        /// 普通模式等下一条消息再弹（不把失焦期间的旧消息推出来）。
        /// 在 WPF 线程调用。
        /// </summary>
        private void ApplyFocusGate()
        {
            if (_window == null)
            {
                return;
            }

            if (!_hideWhenGameInactive
                || GameFocusWatcher.IsGameInForeground()
                || GameFocusWatcher.IsSelfInForeground())
            {
                _window.SetForcedHidden(false);
                if (_pinned)
                {
                    _window.UpdateMessages(_latest ?? new List<ChatMessage>());
                }
            }
            else
            {
                _window.SetForcedHidden(true);
            }
        }

        // ---------- 长按回车切换固定显示（WPF 线程） ----------

        /// <summary>按设置启停长按回车侦测（在 WPF 线程调用）。</summary>
        private void SyncEnterWatcher()
        {
            if (_enterTogglePin && _enabled)
            {
                if (_enterWatcher == null)
                {
                    _enterWatcher = new EnterHoldWatcher();
                    _enterWatcher.LongPressEnter += OnLongPressEnter;
                }
                _enterWatcher.HoldThresholdMs = _enterHoldMs;
                _enterWatcher.Start();
            }
            else
            {
                _enterWatcher?.Stop();
            }
        }

        private void OnLongPressEnter()
        {
            if (_disposed || !_enabled)
            {
                return;
            }

            BeginUi(() =>
            {
                if (_disposed || !_enabled || _window == null)
                {
                    return;
                }

                _pinned = !_pinned;
                ApplicationConfig.SaveSettings("FloatWindow_Pinned", _pinned ? "true" : "false");
                ApplyAppearance();
                // 开固定：立即显示（无消息时显示占位）；关固定：重启显示计时器到时淡出
                _window.UpdateMessages(_latest ?? new List<ChatMessage>());
            });
        }

        /// <summary>把操作投递到 WPF 浮窗线程（线程安全）。</summary>
        private void BeginUi(Action action)
        {
            try
            {
                var dispatcher = _uiDispatcher;
                if (dispatcher != null && !_uiDispatcher.HasShutdownStarted)
                {
                    dispatcher.BeginInvoke(action);
                }
            }
            catch
            {
                // 线程已在关闭：丢弃
            }
        }

        private void LoadSettings()
        {
            _displayDuration = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_DisplayDuration"), 3);
            _fadeSpeed = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_FadeSpeed"), 0.5);
            _opacity = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_Opacity"), 20) / 100.0;
            _showOriginal = ParseBool(ApplicationConfig.GetSettings("FloatWindow_ShowOriginal"));
            // 阵营标识（[友军]/[所有人] 前缀）默认开：与游戏内聊天一致
            _showChannelTag = ApplicationConfig.GetSettings("FloatWindow_ShowChannelTag") == null
                || ParseBool(ApplicationConfig.GetSettings("FloatWindow_ShowChannelTag"));
            _pinned = ParseBool(ApplicationConfig.GetSettings("FloatWindow_Pinned"));
            _outlineEnabled = ParseBool(ApplicationConfig.GetSettings("FloatWindow_Outline"));
            _outlineColor = ApplicationConfig.GetSettings("FloatWindow_OutlineColor") ?? "#FFFFFFFF";
            _outlineWidth = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_OutlineWidth"), 1);
            _lineSpacing = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_LineSpacing"), 0);
            _clearMode = ApplicationConfig.GetSettings("FloatWindow_ClearMode") ?? "exit";
            _enterTogglePin = ApplicationConfig.GetSettings("FloatWindow_TogglePinByEnter") == null
                || ParseBool(ApplicationConfig.GetSettings("FloatWindow_TogglePinByEnter"));
            _enterHoldMs = ParseInt(ApplicationConfig.GetSettings("FloatWindow_EnterHoldMs"),
                EnterHoldWatcher.DefaultHoldThresholdMs);
            _hideWhenGameInactive = ParseBool(ApplicationConfig.GetSettings("FloatWindow_HideWhenGameInactive"));
        }

        private void ApplyAppearance()
        {
            double fontSize = ParseDouble(ApplicationConfig.GetSettings("FontSize"), 14);
            if (fontSize <= 0)
            {
                fontSize = 14;
            }

            string fontName = ApplicationConfig.GetSettings("FontFamily") ?? "Segoe UI";
            string fontStyle = ApplicationConfig.GetSettings("FontStyle") ?? "normal";
            // 文字颜色统一来自「字体和样式」页（同一套颜色也作用于浏览器面板）
            _window.ApplySettings(
                _displayDuration,
                _fadeSpeed,
                _opacity,
                _showOriginal,
                _showChannelTag,
                _pinned,
                fontSize,
                fontName,
                fontStyle,
                _lineSpacing,
                new FloatPalette
                {
                    AllyBrush = ParseBrush(ApplicationConfig.GetSettings("AllyFontColor"), "#FF5472F2"),
                    EnemyBrush = ParseBrush(ApplicationConfig.GetSettings("EnemyFontColor"), "#FFF25A54"),
                    SystemBrush = ParseBrush(ApplicationConfig.GetSettings("SystemFontColor"), "#FFD4A017"),
                    NeutralBrush = ParseBrush(ApplicationConfig.GetSettings("NeutralFontColor"), "#FFB6B6B6"),
                    CoordBrush = ParseBrush(ApplicationConfig.GetSettings("CoordFontColor"), "#50FF00FF"),
                },
                _outlineEnabled,
                _outlineColor,
                _outlineWidth);
        }

        private static double ParseDouble(string value, double fallback)
        {
            // 注意：0 是合法值（背景不透明度可为 0），只拒绝负数与非法输入
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0)
            {
                return v;
            }
            return fallback;
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out bool b) && b;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0
                ? v
                : fallback;
        }

        private static Brush ParseBrush(string argb, string fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(argb))
                {
                    return (Brush)new BrushConverter().ConvertFromString(argb);
                }
            }
            catch
            {
                // 非法颜色：用默认
            }
            return (Brush)new BrushConverter().ConvertFromString(fallback);
        }
    }
}
