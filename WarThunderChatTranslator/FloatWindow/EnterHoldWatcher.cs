using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 长按回车侦测：全局 GetAsyncKeyState 轮询（30ms），回车按住 ≥500ms
    /// 后**松开**时触发 LongPressEnter。
    /// 仅在触发瞬间游戏处于前台时才上报（游戏不在前台时按回车不响应，
    /// 避免干扰其他窗口的正常输入）。无需键盘钩子与消息泵。
    /// </summary>
    public sealed class EnterHoldWatcher
    {
        private const int VK_RETURN = 0x0D;
        private const int HoldThresholdMs = 500;
        private const int PollIntervalMs = 30;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private readonly object _gate = new object();
        private volatile bool _running;
        private Thread _thread;
        private bool _enterDown;
        private long _downMs;

        /// <summary>长按回车（≥500ms）松开且游戏在前台时触发。</summary>
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
