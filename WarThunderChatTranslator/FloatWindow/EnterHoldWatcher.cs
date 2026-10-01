using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 长按回车侦测：全局 GetAsyncKeyState 轮询（30ms），回车按住 ≥<see cref="HoldThresholdMs"/>
    /// 后**松开**时触发 LongPressEnter。
    /// 仅在触发瞬间游戏处于前台时才上报（游戏不在前台时按回车不响应，
    /// 避免干扰其他窗口的正常输入）。无需键盘钩子与消息泵。
    /// </summary>
    public sealed class EnterHoldWatcher
    {
        private const int VK_RETURN = 0x0D;
        public const int DefaultHoldThresholdMs = 500;
        private const int MinHoldThresholdMs = 100;
        private const int MaxHoldThresholdMs = 5000;
        private const int PollIntervalMs = 30;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private readonly object _gate = new object();
        private volatile bool _running;
        private volatile int _holdThresholdMs = DefaultHoldThresholdMs;
        private Thread _thread;
        private bool _enterDown;
        private long _downMs;

        /// <summary>长按判定阈值（毫秒），100-5000；运行中改动立即生效。</summary>
        public int HoldThresholdMs
        {
            get { return _holdThresholdMs; }
            set { _holdThresholdMs = Math.Max(MinHoldThresholdMs, Math.Min(value, MaxHoldThresholdMs)); }
        }

        /// <summary>长按回车（达到 HoldThresholdMs）松开且游戏在前台时触发。</summary>
        public event Action LongPressEnter;

        public void Start()
        {
            lock (_gate)
            {
                if (_running)
                {
                    return;
                }
                _running = true;
                _enterDown = false;
                _thread = new Thread(Run) { IsBackground = true, Name = "WctEnterHold" };
                _thread.Start();
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                _running = false;
            }
        }

        private void Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_running)
            {
                Thread.Sleep(PollIntervalMs);
                try
                {
                    bool down = (GetAsyncKeyState(VK_RETURN) & 0x8000) != 0;
                    if (down && !_enterDown)
                    {
                        _enterDown = true;
                        _downMs = sw.ElapsedMilliseconds;
                    }
                    else if (!down && _enterDown)
                    {
                        _enterDown = false;
                        long held = sw.ElapsedMilliseconds - _downMs;
                        if (held >= HoldThresholdMs && GameFocusWatcher.IsGameInForeground())
                        {
                            LongPressEnter?.Invoke();
                        }
                    }
                }
                catch
                {
                    // 单轮失败忽略，继续轮询
                }
            }
        }
    }
}
