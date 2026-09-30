using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WarThunderChatTranslator.Entities;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 聊天浮窗（WPF）：透明圆角、置顶、不占任务栏。
    /// 新消息淡入 → 显示时长停留 → 淡出隐藏；固定模式下常显不淡出。
    /// 游戏在前台时整体鼠标穿透（WS_EX_TRANSPARENT|WS_EX_NOACTIVATE）；
    /// 游戏不在前台时可交互：窗口内任意位置按下拖动改位置，
    /// 按住边缘/角落缩放；滚轮翻看旧消息，移出窗口或来新消息自动回底。
    /// </summary>
    public partial class FloatWindow : System.Windows.Window
    {
        private const double MinFloatWidth = 220;
        private const double MinFloatHeight = 110;
        private const double HitZone = 12;
        private const int MaxMessages = 50;

        // Win32 扩展样式
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TRANSPARENT = 0x00000080;
        private const long WS_EX_NOACTIVATE = 0x08000000;

        private double _displayDuration = 3;
        private double _fadeSpeed = 0.5;
        private double _targetOpacity = 0.20;
        private bool _showOriginal;
        private bool _showChannelTag = true;
        private bool _pinned;
        private bool _fadeRunning;
        // 外部强制隐藏（游戏不在前台）：隐藏并阻止淡入，解除后恢复正常显隐逻辑
        private bool _forcedHidden;

        // 文字描边
        private bool _outline;
        private Color _outlineColor = Colors.White;
        private double _outlineWidth = 1;

        private readonly DispatcherTimer _displayTimer = new DispatcherTimer();

        // 文字配色（来自「字体和样式」页；默认值取色自游戏内聊天）
        private Brush _allyBrush;
        private Brush _enemyBrush;
        private Brush _systemBrush;
        private Brush _neutralBrush;

        [Flags]
        private enum ResizeDir
        {
            None = 0,
            Left = 1,
            Right = 2,
            Top = 4,
            Bottom = 8,
            LeftTop = 5,
            RightTop = 6,
            LeftBottom = 9,
            RightBottom = 10
        }

        private ResizeDir _resizeDir = ResizeDir.None;
        private Point _resizeOrigin;
        private Rect _resizeStartRect;

        // 用户滚轮上翻查看旧消息中（此时不自动回底）；回到底部或移出窗口/来新消息时恢复
        private bool _userScrolledUp;

        /// <summary>位置/尺寸变化（拖动结束、缩放结束）后触发，参数：left, top, width, height。</summary>
        public event Action<double, double, double, double> LayoutChanged;

        public FloatWindow()
        {
            InitializeComponent();

            // 游戏内实测取色：阵营色 友军 #5472F2 / 敌军 #F25A54，手打正文与时间戳 #B6B6B6
            _allyBrush = MakeBrush("#FF5472F2");
            _enemyBrush = MakeBrush("#FFF25A54");
            _systemBrush = MakeBrush("#FFD4A017");
            _neutralBrush = MakeBrush("#FFB6B6B6");

            _displayTimer.Tick += (s, e) =>
            {
                _displayTimer.Stop();
                if (!_pinned)
                {
                    FadeOut();
                }
            };

            // 鼠标事件用隧道（Preview）阶段绑定在窗口级：先于 ScrollViewer 等子元素处理，
            // 保证窗口内任意位置按下都能触发拖动/缩放判定
            PreviewMouseLeftButtonDown += Window_MouseLeftButtonDown;
            PreviewMouseLeftButtonUp += FloatWindow_MouseLeftButtonUp;
            MouseMove += FloatWindow_MouseMove;
            MouseLeave += FloatWindow_MouseLeave;

            MessageScroll.ScrollChanged += MessageScroll_ScrollChanged;
        }

        // ---------- 设置 ----------

        public void ApplySettings(double displayDuration, double fadeSpeed, double opacity,
            bool showOriginal, bool showChannelTag, bool pinned, double fontSize, string fontFamilyName,
            string fontStyle,
            FloatPalette palette,
            bool outline, string outlineColor, double outlineWidth)
        {
            _displayDuration = Math.Max(1, displayDuration);
            _fadeSpeed = Math.Max(0.1, fadeSpeed);
            // 背景不透明度允许 0（完全透明的背景，文字仍全不透明）
            _targetOpacity = Math.Min(1, Math.Max(0, opacity));
            _showOriginal = showOriginal;
            _showChannelTag = showChannelTag;
            _pinned = pinned;
            _outline = outline;
            _outlineWidth = Math.Max(0.5, Math.Min(outlineWidth, 8));
            if (!string.IsNullOrWhiteSpace(outlineColor))
            {
                try
                {
                    _outlineColor = (Color)ColorConverter.ConvertFromString(outlineColor);
                }
                catch
                {
                    // 非法颜色：沿用当前
                }
            }

            if (fontSize > 0)
            {
                TextElement.SetFontSize(ContentGrid, fontSize);
            }

            if (!string.IsNullOrWhiteSpace(fontFamilyName))
            {
                try
                {
                    TextElement.SetFontFamily(ContentGrid, new FontFamily(fontFamilyName));
                }
                catch
                {
                    // 非法字体名：沿用默认
                }
            }

            // 文字样式与浏览器面板共用同一个设置项（CSS 字面量）
            TextElement.SetFontWeight(ContentGrid, ParseFontWeight(fontStyle));

            if (palette != null)
            {
                if (palette.AllyBrush != null) _allyBrush = palette.AllyBrush;
                if (palette.EnemyBrush != null) _enemyBrush = palette.EnemyBrush;
                if (palette.SystemBrush != null) _systemBrush = palette.SystemBrush;
                if (palette.NeutralBrush != null) _neutralBrush = palette.NeutralBrush;
            }

            // 透明度只作用于背景：窗口本身保持全不透明，文字永远 100% 不透明
            Opacity = 1.0;
            RootBorder.Background = MakeBackgroundBrush();

            if (_pinned)
            {
                _displayTimer.Stop();
            }
        }

        /// <summary>设置里的文字样式（与面板 CSS 同一套字面量）映射到 WPF 字重。</summary>
        private static FontWeight ParseFontWeight(string style)
        {
            switch ((style ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "lighter": return FontWeights.Light;
                case "bold": return FontWeights.Bold;
                case "bolder": return FontWeights.ExtraBold;
                default: return FontWeights.Normal;
            }
        }

        /// <summary>按当前透明度生成背景刷（#101418 + 设定不透明度）。</summary>
        private SolidColorBrush MakeBackgroundBrush()
        {
            byte a = (byte)Math.Round(_targetOpacity * 255);
            return new SolidColorBrush(Color.FromArgb(a, 0x10, 0x14, 0x18));
        }

        /// <summary>恢复持久化的位置/尺寸；缺省落在主屏工作区右上角。</summary>
        public void RestoreLayout(double? left, double? top, double? width, double? height)
        {
            if (width.HasValue && height.HasValue)
            {
                var wa = SystemParameters.WorkArea;
                Width = Math.Max(MinFloatWidth, Math.Min(width.Value, wa.Width));
                Height = Math.Max(MinFloatHeight, Math.Min(height.Value, wa.Height));
            }

            if (left.HasValue && top.HasValue)
            {
                Left = left.Value;
                Top = top.Value;
            }
            else
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 24;
                Top = wa.Top + 24;
            }

            ClampToScreen();
        }

        // ---------- 消息与显隐 ----------

        /// <summary>
        /// 用最新一批消息刷新显示并触发 淡入/重置显示计时/淡出中断 逻辑。
        /// </summary>
        public void UpdateMessages(IReadOnlyList<ChatMessage> messages)
        {
            var items = new List<FloatMessageItem>();
            var pool = new List<ChatMessage>(messages ?? new List<ChatMessage>());
            if (pool.Count > MaxMessages)
            {
                pool = pool.GetRange(pool.Count - MaxMessages, MaxMessages);
            }

            // 描边参数：8 方向偏移副本（关闭时全部 Collapsed，零成本）
            double ow = _outline ? _outlineWidth : 0;
            double od = ow * 0.70710678; // 对角方向偏移 = 宽 / √2
            var outlineBrush = _outline ? new SolidColorBrush(_outlineColor) : null;
            var outlineVis = _outline ? Visibility.Visible : Visibility.Collapsed;

            foreach (var m in pool) // 时间序、最新在最后
            {
                var segments = BuildSegments(m);
                items.Add(new FloatMessageItem
                {
                    Segments = segments,
                    Line = string.Concat(segments.Select(s => s.Text)),
                    OriginalLine = m.Msg,
                    OriginalVisibility = _showOriginal ? Visibility.Visible : Visibility.Collapsed,
                    OutlineColor = outlineBrush,
                    OutlineVisibility = outlineVis,
                    Ox_E = ow, Oy_E = 0,
                    Ox_W = -ow, Oy_W = 0,
                    Ox_N = 0, Oy_N = -ow,
                    Ox_S = 0, Oy_S = ow,
                    Ox_NE = od, Oy_NE = -od,
                    Ox_NW = -od, Oy_NW = -od,
                    Ox_SE = od, Oy_SE = od,
                    Ox_SW = -od, Oy_SW = od
                });
            }

            MessageList.ItemsSource = items;
            Placeholder.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 来新消息：回到底部显示最新（规范行为），等布局完成后再滚
            _userScrolledUp = false;
            Dispatcher.BeginInvoke(new Action(ScrollToBottom), DispatcherPriority.Background);

            if (_forcedHidden)
            {
                // 游戏不在前台：只刷新内容，不弹出（解除后由调用方决定是否立刻恢复显示）
                return;
            }

            if (_pinned)
            {
                _displayTimer.Stop();
                if (!IsVisible)
                {
                    FadeIn();
                }
                else if (_fadeRunning)
                {
                    CancelFadeBack();
                }
                return;
            }

            if (!IsVisible)
            {
                FadeIn();
            }
            else if (_fadeRunning)
            {
                CancelFadeBack();
            }

            RestartDisplayTimer();
        }

        /// <summary>
        /// 外部强制隐藏（"游戏不在前台时隐藏浮窗"开关）：立即隐藏并阻止淡入。
        /// 解除时不主动弹出，避免把失焦期间的旧消息重新推出来。
        /// </summary>
        public void SetForcedHidden(bool forced)
        {
            if (_forcedHidden == forced)
            {
                return;
            }

            _forcedHidden = forced;
            if (forced)
            {
                _displayTimer.Stop();
                HideNow();
            }
        }

        /// <summary>
        /// 拼一行：时间 [频道] 名字: 正文，逐段取色（对齐游戏内聊天）。
        /// 频道与名字始终用发送方阵营色；正文只有无线电快捷指令跟阵营同色，
        /// 玩家手打的消息正文一律中性色（游戏里那条偏白的 #B6B6B6）。
        /// </summary>
        private List<FloatSegment> BuildSegments(ChatMessage m)
        {
            bool systemMsg = string.IsNullOrEmpty(m.Sender) && m.Enemy;
            var senderBrush = systemMsg ? _systemBrush : (m.Enemy ? _enemyBrush : _allyBrush);
            var bodyBrush = systemMsg ? _systemBrush
                : (RadioMessages.IsRadio(m.Msg) ? senderBrush : _neutralBrush);

            var segments = new List<FloatSegment>();
            if (m.Time > 0)
            {
                segments.Add(MakeSegment($"{m.Time / 60}:{m.Time % 60:00} ", _neutralBrush));
            }

            string mode = (m.Mode ?? string.Empty).Trim();
            if (_showChannelTag && mode.Length > 0)
            {
                segments.Add(MakeSegment($"[{mode}] ", senderBrush));
            }

            string sender = string.IsNullOrEmpty(m.Sender) ? "系统" : m.Sender;
            segments.Add(MakeSegment($"{sender}: ", senderBrush));
            segments.Add(MakeSegment(m.TranslatedMessage ?? m.Msg, bodyBrush));
            return segments;
        }

        private static FloatSegment MakeSegment(string text, Brush brush) =>
            new FloatSegment { Text = text, Brush = brush };

        /// <summary>
        /// 清空消息列表（对局边界调用）：显示"等待消息…"占位。
        /// 窗口可见时（固定显示/显示中）立即清空；窗口已淡出时只重置
        /// 内部状态，不重新弹出。
        /// </summary>
        public void ClearMessages()
        {
            _userScrolledUp = false;
            MessageList.ItemsSource = null;
            if (_pinned)
            {
                _displayTimer.Stop();
            }
            else
            {
                // 非固定模式：重启显示计时，"等待消息…"占位也按显示时长自动淡出
                // （修复：之前无条件停表，退出对局后浮窗会一直挂着不消失）
                RestartDisplayTimer();
            }
            if (IsVisible)
            {
                Placeholder.Visibility = Visibility.Visible;
            }
        }

        private void FadeIn()
        {
            CancelAnimation();
            _fadeRunning = true;
            Opacity = 0;
            if (!IsVisible)
            {                Show(); // ShowActivated=False：不抢占焦点
            }
            var anim = new DoubleAnimation(1.0, TimeSpan.FromSeconds(_fadeSpeed));
            anim.Completed += (s, e) => _fadeRunning = false;
            BeginAnimation(OpacityProperty, anim);
        }

        private void FadeOut()
        {
            _fadeRunning = true;
            var anim = new DoubleAnimation(0, TimeSpan.FromSeconds(_fadeSpeed)) { FillBehavior = FillBehavior.Stop };
            anim.Completed += (s, e) =>
            {
                _fadeRunning = false;
                Hide();
            };
            BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>淡出过程中来新消息：取消淡出、回到完全可见。</summary>
        private void CancelFadeBack()
        {
            CancelAnimation();
            _fadeRunning = false;
            Opacity = 1.0;
        }

        private void CancelAnimation()
        {
            BeginAnimation(OpacityProperty, (AnimationTimeline)null);
        }

        private void RestartDisplayTimer()
        {
            _displayTimer.Interval = TimeSpan.FromSeconds(_displayDuration);
            _displayTimer.Stop();
            _displayTimer.Start();
        }

        /// <summary>立即隐藏并停止一切计时/动画（浮窗被关闭时调用）。</summary>
        public void HideNow()
        {
            CancelAnimation();
            _displayTimer.Stop();
            if (IsVisible)
            {
                Hide();
            }
        }

        // ---------- 鼠标穿透 ----------

        /// <summary>
        /// 设置鼠标穿透（游戏在前台时为 true）。
        /// 穿透时窗口对鼠标完全透明，点击落到下层窗口（游戏）。
        /// </summary>
        public void SetClickThrough(bool on)
        {
            var handle = new WindowInteropHelper(this).EnsureHandle();
            long ex = GetExStyle(handle).ToInt64();
            long updated = on
                ? (ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)
                : (ex & ~WS_EX_TRANSPARENT & ~WS_EX_NOACTIVATE);
            if (updated != ex)
            {
                SetExStyle(handle, updated);
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetExStyle(IntPtr handle) =>
            IntPtr.Size == 8
                ? GetWindowLongPtr64(handle, GWL_EXSTYLE)
                : new IntPtr(GetWindowLong32(handle, GWL_EXSTYLE));

        private static void SetExStyle(IntPtr handle, long value)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(handle, GWL_EXSTYLE, new IntPtr(value));
            }
            else
            {
                SetWindowLong32(handle, GWL_EXSTYLE, (int)value);
            }
        }

        // ---------- 滚轮翻看旧消息 ----------

        private void MessageScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 离开底部 8px 以上视为用户在翻看旧消息；滚回底部则恢复跟随
            _userScrolledUp = MessageScroll.VerticalOffset < MessageScroll.ScrollableHeight - 8;
        }

        private void FloatWindow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_userScrolledUp)
            {
                _userScrolledUp = false;
                ScrollToBottom();
            }
        }

        private void ScrollToBottom()
        {
            MessageScroll.ScrollToVerticalOffset(double.MaxValue);
        }

        // ---------- 拖动与缩放 ----------

        // 临时诊断日志（定位缩放不生效问题，稳定后移除）
        private bool _resizeMoveLogged;
        private ResizeDir _lastHoverDir = ResizeDir.None;
        private static void Dbg(string msg)
        {
            try
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wct-float-dbg.log");
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
            }
            catch { }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            // 穿透状态下收不到鼠标事件；这里只处理可交互状态
            var pos = e.GetPosition(this);
            _resizeDir = GetResizeDir(pos, Width, Height);
            bool clickThrough = false;
            try
            {
                clickThrough = (GetExStyle(new WindowInteropHelper(this).Handle).ToInt64() & WS_EX_TRANSPARENT) != 0;
            }
            catch { }
            Dbg($"down pos=({pos.X:F1},{pos.Y:F1}) size={Width:F0}x{Height:F0} dir={_resizeDir} clickThrough={clickThrough}");
            _resizeMoveLogged = false;

            if (_resizeDir == ResizeDir.None)
            {
                try
                {
                    DragMove();
                    RaiseLayoutChanged();
                }
                catch
                {
                    // 拖动被系统拒绝：忽略
                }
            }
            else
            {
                _resizeOrigin = Mouse.GetPosition(this);
                _resizeStartRect = new Rect(Left, Top, Width, Height);
                CaptureMouse();
                Dbg($"capture started dir={_resizeDir} origin=({_resizeOrigin.X:F1},{_resizeOrigin.Y:F1}) start=({_resizeStartRect.Left:F0},{_resizeStartRect.Top:F0},{_resizeStartRect.Width:F0},{_resizeStartRect.Height:F0})");
            }
        }

        private static bool Has(ResizeDir dir, ResizeDir flag) => (dir & flag) == flag;

        private static ResizeDir GetResizeDir(Point pos, double width, double height)
        {
            bool l = pos.X < HitZone;
            bool r = pos.X > width - HitZone;
            bool t = pos.Y < HitZone;
            bool b = pos.Y > height - HitZone;

            if (l && t) return ResizeDir.LeftTop;
            if (r && t) return ResizeDir.RightTop;
            if (l && b) return ResizeDir.LeftBottom;
            if (r && b) return ResizeDir.RightBottom;
            if (l) return ResizeDir.Left;
            if (r) return ResizeDir.Right;
            if (t) return ResizeDir.Top;
            if (b) return ResizeDir.Bottom;
            return ResizeDir.None;
        }

        private void FloatWindow_MouseMove(object sender, MouseEventArgs e)
        {
            if (_resizeDir != ResizeDir.None && IsMouseCaptured)
            {
                var pos = Mouse.GetPosition(this);
                double dx = pos.X - _resizeOrigin.X;
                double dy = pos.Y - _resizeOrigin.Y;
                var r = _resizeStartRect;
                var wa = SystemParameters.WorkArea;

                double newW = r.Width;
                double newH = r.Height;
                double newL = r.Left;
                double newT = r.Top;

                if (Has(_resizeDir, ResizeDir.Right)) newW = r.Width + dx;
                if (Has(_resizeDir, ResizeDir.Left)) newW = r.Width - dx;
                if (Has(_resizeDir, ResizeDir.Bottom)) newH = r.Height + dy;
                if (Has(_resizeDir, ResizeDir.Top)) newH = r.Height - dy;

                newW = Math.Max(MinFloatWidth, Math.Min(newW, wa.Width));
                newH = Math.Max(MinFloatHeight, Math.Min(newH, wa.Height));
                if (Has(_resizeDir, ResizeDir.Left)) newL = r.Right - newW;
                if (Has(_resizeDir, ResizeDir.Top)) newT = r.Bottom - newH;

                if (!_resizeMoveLogged)
                {
                    Dbg($"move resize dx={dx:F1} dy={dy:F1} -> ({newL:F0},{newT:F0},{newW:F0},{newH:F0})");
                    _resizeMoveLogged = true;
                }

                Left = newL;
                Top = newT;
                Width = newW;
                Height = newH;
                return;
            }

            // 悬停边缘时显示对应光标
            if (IsVisible)
            {
                var pos = e.GetPosition(this);
                var dir = GetResizeDir(pos, Width, Height);
                if (dir != _lastHoverDir)
                {
                    Dbg($"hover dir {_lastHoverDir} -> {dir} pos=({pos.X:F1},{pos.Y:F1}) size={Width:F0}x{Height:F0}");
                    _lastHoverDir = dir;
                }
                Cursor = dir switch
                {
                    ResizeDir.Left or ResizeDir.Right => Cursors.SizeWE,
                    ResizeDir.Top or ResizeDir.Bottom => Cursors.SizeNS,
                    ResizeDir.LeftTop or ResizeDir.RightBottom => Cursors.SizeNWSE,
                    ResizeDir.RightTop or ResizeDir.LeftBottom => Cursors.SizeNESW,
                    _ => null
                };
            }
        }

        private void FloatWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            if (_resizeDir != ResizeDir.None)
            {
                _resizeDir = ResizeDir.None;
                ClampToScreen();
                RaiseLayoutChanged();
            }
        }

        private void RaiseLayoutChanged()
        {
            LayoutChanged?.Invoke(Left, Top, Width, Height);
        }

        private void ClampToScreen()
        {
            var wa = SystemParameters.WorkArea;
            if (Width > wa.Width) Width = wa.Width;
            if (Height > wa.Height) Height = wa.Height;
            // 允许窗口大部分移出屏幕，但保留 40px 在屏内，防止彻底丢失
            Left = Math.Min(Math.Max(Left, wa.Left - Width + 40), wa.Right - 40);
            Top = Math.Min(Math.Max(Top, wa.Top - Height + 40), wa.Bottom - 40);
        }

        private static Brush MakeBrush(string color)
        {
            try
            {
                return (Brush)new BrushConverter().ConvertFromString(color);
            }
            catch
            {
                return Brushes.White;
            }
        }
    }
}
