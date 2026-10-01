using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 内置轮询器：仅当浮窗启用时运行，每 2 秒拉取一次游戏消息。
    /// 发现新消息时触发 MessagesReceived（传入全部近期消息，时间序、最新在最后）。
    /// 环境变量 WCT_FAKE_CHAT=1 时注入假消息（开发测试用，不产生 AI 调用）。
    /// </summary>
    public sealed class ChatPoller
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
        private readonly object _gate = new object();
        private readonly HashSet<int> _seenIds = new HashSet<int>();
        private long _lastMaxTime;
        private bool? _matchState; // null=未定，true=对局中，false=菜单
        private bool _polling;
        private Timer _timer;
        private int _fakeSeq;

        public event Action<IReadOnlyList<ChatMessage>> MessagesReceived;

        /// <summary>进入对局（地图加载完成，可能早于第一条聊天）。</summary>
        public event Action MatchStarted;

        /// <summary>退出对局（回到菜单，或游戏关闭）。</summary>
        public event Action MatchEnded;

        public void Start()
        {
            Stop();
            lock (_gate)
            {
                _seenIds.Clear();
                _lastMaxTime = 0;
                _matchState = null;
            }
            _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.Zero, Interval);
        }

        public void Stop()
        {
            var t = _timer;
            _timer = null;
            t?.Dispose();
        }

        /// <summary>会话切换（进/出对局、游戏关闭）：清空时间水位与已见 ID 集合。</summary>
        private void ResetSessionState()
        {
            lock (_gate)
            {
                _seenIds.Clear();
                _lastMaxTime = 0;
            }
        }

        private async Task TickAsync()
        {
            lock (_gate)
            {
                if (_polling)
                {
                    return;
                }
                _polling = true;
            }

            try
            {
                List<ChatMessage> messages =
                    Environment.GetEnvironmentVariable("WCT_FAKE_CHAT") == "1"
                        ? FakeMessages()
                        : await ChatService.FetchAndTranslateAsync(0);

                // 对局状态探测必须在本轮无条件执行（退出对局后聊天缓冲为空，
                // 若放在消息处理之后会被提前 return 跳过）
                await ProbeMatchStateAsync();

                if (messages == null || messages.Count == 0)
                {
                    return;
                }

                bool anyNew;
                long maxTime = messages.Max(m => m.Time);
                lock (_gate)
                {
                    // 时间回退说明本批是上一局/上次会话的残留（早已显示过），只需静默重新播种。
                    // 绝不能把它当成"一批新消息"上报：轮询 2 秒一轮，而显示时长可以到 60 秒，
                    // 每轮都上报就会把浮窗的淡出倒计时一路往前推，浮窗永远不淡出。
                    // （游戏重启/换局导致的 ID 复用，由下面的对局边界归零负责。）
                    bool regressed = _lastMaxTime > 0 && maxTime < _lastMaxTime;
                    if (regressed)
                    {
                        _seenIds.Clear();
                    }
                    _lastMaxTime = maxTime;

                    anyNew = false;
                    foreach (var m in messages)
                    {
                        if (!regressed && _seenIds.Add(m.Id))
                        {
                            anyNew = true;
                        }
                    }

                    if (_seenIds.Count > 1000)
                    {
                        // 防止无界增长：只保留本轮消息（同样不制造"新消息"）
                        _seenIds.Clear();
                        foreach (var m in messages)
                        {
                            _seenIds.Add(m.Id);
                        }
                    }
                }

                if (anyNew)
                {
                    MessagesReceived?.Invoke(messages);
                }
            }
            catch
            {
                // 游戏未运行、端口不可达、JSON 异常：本轮静默
            }
            finally
            {
                lock (_gate)
                {
                    _polling = false;
                }
            }
        }

        /// <summary>
        /// 对局状态监视：/map_info.json 的 valid 字段（实测：对局中 true，菜单 false）。
        /// 状态跳变时触发 MatchStarted / MatchEnded（假聊天模式跳过）。
        /// </summary>
        private async Task ProbeMatchStateAsync()
        {
            if (Environment.GetEnvironmentVariable("WCT_FAKE_CHAT") == "1")
            {
                return;
            }

            bool? state = await ChatService.ProbeMatchStateAsync();
            if (!state.HasValue)
            {
                // 端口不可达（游戏关闭）：若之前在对局中，视为退出对局。
                // 注意只在"真的离开对局"这一跃迁上清状态：map_info 单独不可达时
                // 每轮都清空已见集合，会让所有消息每轮都被当成新消息（浮窗永不淡出）。
                bool wasInMatch = _matchState == true;
                _matchState = null;
                if (wasInMatch)
                {
                    ResetSessionState();
                    MatchEnded?.Invoke();
                }
                return;
            }

            bool wasMatch = _matchState == true;
            _matchState = state.Value;
            if (_matchState == wasMatch)
            {
                return;
            }

            // 对局边界：游戏聊天消息 ID 会被下一局复用，时间戳也重新计数，
            // 必须在这里归零水位与已见集合（否则新消息会被误判成"已见过"而漏报）
            ResetSessionState();
            if (state.Value)
            {
                MatchStarted?.Invoke();
            }
            else
            {
                MatchEnded?.Invoke();
            }
        }

        private List<ChatMessage> FakeMessages()
        {
            _fakeSeq++;
            int now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string[] texts = { "push now!", "they are rotating to the west", "low ammo, falling back", "need backup", "gg" };
            string[] senders = { "EnemyPlayer", "AllyPlayer" };

            var list = new List<ChatMessage>();
            int count = 1 + (_fakeSeq % 3);
            for (int i = 0; i < count; i++)
            {
                string sender = senders[(_fakeSeq + i) % senders.Length];
                string text = texts[(_fakeSeq + i) % texts.Length];
                list.Add(new ChatMessage
                {
                    Id = _fakeSeq * 1000 + i,
                    Msg = text,
                    Sender = sender,
                    Enemy = sender == "EnemyPlayer",
                    Mode = "Global",
                    Time = now + i,
                    TranslatedMessage = "（测试）" + text,
                    PrettyMessage = $"{sender}: （测试）{text}"
                });
            }
            return list;
        }
    }
}
