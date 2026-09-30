using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 前台窗口侦测：判断"游戏是否在前台"以及"本进程（翻译器设置界面）是否在前台"。
    /// 游戏命中条件：前台窗口属于 WarThunder 进程（Steam/Gaijin 客户端进程名均为 WarThunder.exe），
    /// 或前台窗口标题以 "War Thunder" 开头（兜底，兼容改名进程）。
    /// 两个状态各自变化时分别触发 GameForegroundChanged / SelfForegroundChanged。
    /// </summary>
    public sealed class GameFocusWatcher
    {
        private const string GameProcessName = "WarThunder";
        private const string GameWindowTitle = "War Thunder";

        private Timer _timer;
        private volatile bool _gameForeground;
        private volatile bool _selfForeground;

        public bool GameForeground => _gameForeground;

        public event Action<bool> GameForegroundChanged;

        /// <summary>前台窗口是否属于本进程（切到翻译器自己的界面时浮窗不应隐藏）。</summary>
        public event Action<bool> SelfForegroundChanged;

        public void Start()
        {
            Stop();
            _timer = new Timer(_ => Check(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(300));
        }

        public void Stop()
        {
            var t = _timer;
            _timer = null;
            t?.Dispose();
        }

        private void Check()
        {
            bool fg;
            try
            {
                fg = IsGameInForeground();
            }
            catch
            {
                return;
            }

            if (fg != _gameForeground)
            {
                _gameForeground = fg;
                Raise(GameForegroundChanged, fg);
            }

            bool self;
            try
            {
                self = IsSelfInForeground();
            }
            catch
            {
                return;
            }

            if (self != _selfForeground)
            {
                _selfForeground = self;
                Raise(SelfForegroundChanged, self);
            }
        }

        private static void Raise(Action<bool> handler, bool value)
        {
            try
            {
                handler?.Invoke(value);
            }
            catch
            {
                // 订阅者异常不影响侦测
            }
        }

        public static bool IsGameInForeground()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero)
            {
                return false;
            }

            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (string.Equals(process.ProcessName, GameProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // 进程刚退出：退回标题判断
            }

            var sb = new StringBuilder(256);
            GetWindowText(fg, sb, sb.Capacity);
            return sb.ToString().StartsWith(GameWindowTitle, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSelfInForeground()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero)
            {
                return false;
            }

            GetWindowThreadProcessId(fg, out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    }
}
