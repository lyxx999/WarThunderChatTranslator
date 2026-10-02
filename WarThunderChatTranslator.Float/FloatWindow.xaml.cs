using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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

        // 行间距：行高 = 字号 + 这个像素数（0 = 上下两排字刚好贴在一起）
        private const double OriginalFontSize = 12;
        private double _lineSpacing;

        // 应用到主行/原文行/8 份描边副本的行高（必须完全一致，否则换行位置会错开）
        private double _rowLineHeight = 14;
        private double _originalLineHeight = 12;

        // 淡出调度：以"最新一条消息的身份"为锚，只有锚点前进才重新计时（Environment.TickCount64 基准）
        private long _anchorKey = long.MinValue;
        private long _fadeDeadlineMs;

        private readonly DispatcherTimer _displayTimer = new DispatcherTimer();

        // 淡出兜底：隐藏不能只依赖 DoubleAnimation.Completed（回调可能被吞掉，
        // 那样窗口会弹回全不透明且再无计时器，表现为"永远不淡出"）
        private readonly DispatcherTimer _hideSafetyTimer = new DispatcherTimer();

        // 文字配色（来自「字体和样式」页；默认值取色自游戏内聊天）
        private Brush _allyBrush;
        private Brush _enemyBrush;
        private Brush _systemBrush;
        private Brush _neutralBrush;
        private Brush _coordBrush;

        /// <summary>
        /// 正文里的网格坐标：左括号 + 1~3 个字母 + 1~3 位数字，后面可以跟一段说明
        /// （实测 "[d5]"、"[ka1, 高度 600 米]"）。游戏是用 <color=#FF00FF50> 单独包起来的，
        /// ChatService 剥掉标签后只剩形状，所以这里按形状认。纯字母（[BRUHL]）或纯数字（[1]）不算坐标。
        /// </summary>
        private static readonly Regex CoordPattern = new Regex(
            @"[[［][A-Za-z]{1,3}[0-9]{1,3}[^[\]］]{0,20}[]］]", RegexOptions.Compiled);

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
            // 游戏给坐标用的标签是 <color=#FF00FF50>（Unity 的 #RRGGBBAA：品红 + 0x50 透明度）
            _coordBrush = MakeBrush("#50FF00FF");

            _displayTimer.Tick += DisplayTimer_Tick;

            // 淡出动画的兜底：到时无论如何把窗口真正隐藏掉
            _hideSafetyTimer.Tick += (s, e) =>
            {
                _hideSafetyTimer.Stop();
                if (_fadeRunning)
                {
                    FinishFadeOut();
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
            string fontStyle, double lineSpacing,
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

            // 行间距：0 = 上下两排字刚好贴在一起
            _lineSpacing = Math.Max(0, Math.Min(lineSpacing, 24));
            RefreshLineMetrics();

            if (palette != null)
            {
                if (palette.AllyBrush != null) _allyBrush = palette.AllyBrush;
                if (palette.EnemyBrush != null) _enemyBrush = palette.EnemyBrush;
                if (palette.SystemBrush != null) _systemBrush = palette.SystemBrush;
                if (palette.NeutralBrush != null) _neutralBrush = palette.NeutralBrush;
                if (palette.CoordBrush != null) _coordBrush = palette.CoordBrush;
            }

            // 透明度只作用于背景：窗口本身保持全不透明，文字永远 100% 不透明
            Opacity = 1.0;
            RootBorder.Background = MakeBackgroundBrush();

            if (_pinned)
            {
                _displayTimer.Stop();
            }
            else if (IsVisible)
            {
                // 改设置要立即生效：以当前时刻为起点重新给一段显示时长
                _fadeDeadlineMs = NowMs() + ToMs(_displayDuration);
                ScheduleFadeOut();
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

        /// <summary>
        /// 按当前字号重算行高：行高 = 字号 + 行间距。字号就是汉字方块的高度，所以 0 时
        /// 上下两排字刚好贴在一起，之后每加 1 像素都看得见。
        /// 不能拿字体自然行高当下限：实测它约 1.33 倍字号（中文字体更大），
        /// 那样 0 就已经很宽，往下没有空间——必须配 LineStackingStrategy=BlockLineHeight
        /// 才能让 WPF 真把行高压到自然行高以下（MaxHeight 会被夹回自然行高）。
        /// </summary>
        private void RefreshLineMetrics()
        {
            double size = TextElement.GetFontSize(ContentGrid);
            _rowLineHeight = size + _lineSpacing;
            _originalLineHeight = OriginalFontSize + _lineSpacing;
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
            var rowGap = new Thickness(0, 0, 0, _lineSpacing);

            foreach (var m in pool) // 时间序、最新在最后
            {
                var segments = BuildSegments(m);
                items.Add(new FloatMessageItem
                {
                    Segments = segments,
                    Line = string.Concat(segments.Select(s => s.Text)),
                    OriginalLine = m.Msg,
                    OriginalVisibility = _showOriginal ? Visibility.Visible : Visibility.Collapsed,
                    RowLineHeight = _rowLineHeight,
                    OriginalLineHeight = _originalLineHeight,
                    RowGap = rowGap,
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

            // 这批消息里"最新一条"的身份：淡出倒计时只认它有没有前进
            long key = NewestKey(pool);
            bool advanced = key != _anchorKey;

            MessageList.ItemsSource = items;
            Placeholder.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (advanced)
            {
                // 确有新消息：回到底部显示最新（规范行为），等布局完成后再滚
                _userScrolledUp = false;
                Dispatcher.BeginInvoke(new Action(ScrollToBottom), DispatcherPriority.Background);
            }

            if (_forcedHidden)
            {
                // 游戏不在前台：只刷新内容，不弹出（解除后由调用方决定是否立刻恢复显示）
                _anchorKey = key;
                return;
            }

            if (_pinned)
            {
                _anchorKey = key;
                _displayTimer.Stop();
                if (!IsVisible)
                {
                    FadeIn();
                }
                else if (_fadeRunning)
                {
                    CancelFadeOut();
                }
                return;
            }

            // 只有"来了更新的一条消息"或"窗口刚从隐藏弹出"才重新计时；
            // 同一批消息的重复刷新（改设置重渲染等）不再延长显示时间。
            if (advanced || !IsVisible)
            {
                _anchorKey = key;
                _fadeDeadlineMs = NowMs() + ToMs(_displayDuration);
            }

            if (!IsVisible)
            {
                FadeIn();
            }
            else if (_fadeRunning)
            {
                CancelFadeOut();
            }

            ScheduleFadeOut();
        }

        /// <summary>取这批消息中最新一条的身份标识（时间为主、ID 次之）；空批返回 -1。</summary>
        private static long NewestKey(List<ChatMessage> pool)
        {
            long best = -1;
            foreach (var m in pool)
            {
                long k = ((long)m.Time << 32) | (uint)m.Id;
                if (k > best)
                {
                    best = k;
                }
            }
            return best;
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
        /// 玩家手打的消息正文一律中性色（游戏里那条偏白的 #B6B6B6）；
        /// 正文里的网格坐标再单独拆一段，用「坐标颜色」（游戏里坐标是被单独包了色标签的）。
        /// </summary>
        private List<FloatSegment> BuildSegments(ChatMessage m)
        {
            bool systemMsg = string.IsNullOrEmpty(m.Sender) && m.Enemy;
            var senderBrush = systemMsg ? _systemBrush : (m.Enemy ? _enemyBrush : _allyBrush);
            var bodyBrush = systemMsg ? _systemBrush
                : (m.Radio || RadioMessages.IsRadio(m.Msg) ? senderBrush : _neutralBrush);

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
            AddBodySegments(segments, m.TranslatedMessage ?? m.Msg, bodyBrush);
            return segments;
        }

        /// <summary>正文按坐标切开：坐标段用坐标色，其余用正文色。</summary>
        private void AddBodySegments(List<FloatSegment> segments, string body, Brush bodyBrush)
        {
            if (string.IsNullOrEmpty(body))
            {
                return;
            }

            int last = 0;
            foreach (Match match in CoordPattern.Matches(body))
            {
                if (match.Index > last)
                {
                    segments.Add(MakeSegment(body.Substring(last, match.Index - last), bodyBrush));
                }
                segments.Add(MakeSegment(match.Value, _coordBrush));
                last = match.Index + match.Length;
            }
            if (last < body.Length)
            {
                segments.Add(MakeSegment(body.Substring(last), bodyBrush));
            }
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
            _anchorKey = long.MinValue; // 清空之后，下一批消息一律按"新消息"起算
            if (_pinned || !IsVisible)
            {
                _displayTimer.Stop();
            }
            else
            {
                // 非固定模式：重启显示计时，"等待消息…"占位也按显示时长自动淡出
                _fadeDeadlineMs = NowMs() + ToMs(_displayDuration);
                ScheduleFadeOut();
            }
            if (IsVisible)
            {
                Placeholder.Visibility = Visibility.Visible;
            }
        }

        private void FadeIn()
        {
            CancelAnimation();
            _hideSafetyTimer.Stop();
            _fadeRunning = true;
            Opacity = 0;
            if (!IsVisible)
            {                Show(); // ShowActivated=False：不抢占焦点
            }
            var anim = new DoubleAnimation(1.0, TimeSpan.FromSeconds(_fadeSpeed));
            anim.Completed += (s, e) => _fadeRunning = false;
            BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>
        /// 淡出：动画只负责视觉效果，隐藏由 Completed 或兜底计时器完成（谁先到算谁）。
        /// 动画用 HoldEnd：万一两个回调都没来，窗口也停在透明态而不是弹回全不透明。
        /// </summary>
        private void FadeOut()
        {
            if (!IsVisible)
            {
                return;
            }

            _hideSafetyTimer.Stop();
            _fadeRunning = true;
            var anim = new DoubleAnimation(0, TimeSpan.FromSeconds(_fadeSpeed)) { FillBehavior = FillBehavior.HoldEnd };
            anim.Completed += (s, e) => FinishFadeOut();
            BeginAnimation(OpacityProperty, anim);

            _hideSafetyTimer.Interval = TimeSpan.FromSeconds(_fadeSpeed) + TimeSpan.FromMilliseconds(400);
            _hideSafetyTimer.Start();
        }

        /// <summary>淡出收尾：真正隐藏窗口并复位状态（幂等，动画回调与兜底计时器共用）。</summary>
        private void FinishFadeOut()
        {
            if (!_fadeRunning)
            {
                return;
            }

            _fadeRunning = false;
            _hideSafetyTimer.Stop();
            _displayTimer.Stop();
            Hide(); // 先隐藏再撤动画、复位基值：避免中间渲染出一帧全不透明
            CancelAnimation();
            Opacity = 1.0;
        }

        /// <summary>淡出过程中来新消息：取消淡出、回到完全可见。</summary>
        private void CancelFadeOut()
        {
            _hideSafetyTimer.Stop();
            CancelAnimation();
            _fadeRunning = false;
            Opacity = 1.0;
        }

        private void CancelAnimation()
        {
            BeginAnimation(OpacityProperty, (AnimationTimeline)null);
        }

        private void DisplayTimer_Tick(object sender, EventArgs e)
        {
            if (_pinned || _forcedHidden)
            {
                _displayTimer.Stop();
                return;
            }

            long remain = _fadeDeadlineMs - NowMs();
            if (remain > 0)
            {
                // 期间来了更新的一条消息：按新的截止时刻续期
                _displayTimer.Interval = TimeSpan.FromMilliseconds(remain);
                return;
            }

            _displayTimer.Stop();
            FadeOut();
        }

        /// <summary>按"淡出截止时刻"挂定时器；已到期则立即淡出。</summary>
        private void ScheduleFadeOut()
        {
            long remain = _fadeDeadlineMs - NowMs();
            if (remain <= 0)
            {
                FadeOut();
                return;
            }

            _displayTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, remain));
            _displayTimer.Start();
        }

        private static long NowMs() => Environment.TickCount64;

        private static long ToMs(double seconds) => (long)Math.Round(Math.Max(0, seconds) * 1000);

        /// <summary>立即隐藏并停止一切计时/动画（浮窗被关闭时调用）。</summary>
        public void HideNow()
        {
            _displayTimer.Stop();
            _hideSafetyTimer.Stop();
            _fadeRunning = false;
            _anchorKey = long.MinValue;
            CancelAnimation();
            Opacity = 1.0;
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
