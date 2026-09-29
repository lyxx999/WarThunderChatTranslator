using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WarThunderChatTranslator.Entities;

namespace WarThunderChatTranslator.Helpers
{
    /// <summary>
    /// 游戏消息共享管道：从游戏本地端口 8111 拉取、剥离颜色标签、翻译（带去重缓存）。
    /// 浏览器面板的 /gamechat 接口与浮窗内置轮询共用；翻译缓存保证同一条消息只调用一次 AI。
    /// </summary>
    public static class ChatService
    {
        private static readonly int GamePort = 8111;
        private static readonly string ColorPattern = @"<color(.*?)>(.*?)<\/color>";
        private static readonly ConcurrentDictionary<int, string> TranslationCache = new ConcurrentDictionary<int, string>();
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        /// <summary>
        /// 从游戏拉取消息（lastId=0 表示拉取全部近期消息）并翻译（已翻译的走缓存）。
        /// 游戏未运行或端口不可达时返回 null。
        /// </summary>
        public static async Task<List<ChatMessage>> FetchAndTranslateAsync(int lastId = 0)
        {
            string responseData;
            try
            {
                using var response = await Http.GetAsync($"http://127.0.0.1:{GamePort}/gamechat?lastId={lastId}");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                responseData = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return null; // 游戏未运行 / 端口不可达
            }

            List<ChatMessage> chatMessages;
            try
            {
                chatMessages = JsonConvert.DeserializeObject<List<ChatMessage>>(responseData);
            }
            catch
            {
                return null;
            }

            if (chatMessages == null)
            {
                return null;
            }

            var tasks = chatMessages.Select(async message =>
            {
                message.Msg = Regex.Replace(message.Msg.Replace("\t", ""), ColorPattern, match => match.Groups[2].Value);
                message.Mode = message.Mode.Replace("\t", "");

                if (!TranslationCache.TryGetValue(message.Id, out string translatedMsg))
                {
                    try
                    {
                        var translationResult = await TranslationHelper.TranslateAsync(message.Msg);
                        translatedMsg = translationResult.Translation;
                        TranslationCache[message.Id] = translatedMsg;
                    }
                    catch
                    {
                        // 翻译失败不写缓存，下轮重试
                        translatedMsg = "(翻译失败) " + message.Msg;
                    }
                }

                message.TranslatedMessage = translatedMsg;
                message.PrettyMessage = $"{message.Sender}: {translatedMsg}";
            }).ToList();

            await Task.WhenAll(tasks);
            return chatMessages;
        }

        public static bool IsTranslated(int id) => TranslationCache.ContainsKey(id);

        /// <summary>
        /// 清空翻译缓存（对局边界调用）：游戏消息 ID 可能跨对局重复，
        /// 不清会导致新消息命中旧翻译。
        /// </summary>
        public static void ClearTranslationCache() => TranslationCache.Clear();

        /// <summary>
        /// 探测对局状态：游戏 /map_info.json 的 valid 字段
        /// （实测：对局中 true，菜单/结算 false，游戏关闭时端口不可达）。
        /// 返回 null 表示无法判定（游戏未运行/超时）。
        /// </summary>
        public static async Task<bool?> ProbeMatchStateAsync()
        {
            try
            {
                using var response = await Http.GetAsync($"http://127.0.0.1:{GamePort}/map_info.json");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                string body = await response.Content.ReadAsStringAsync();
                // 响应形如 [{"valid" : true, ...}]，只取 valid 标志
                var m = System.Text.RegularExpressions.Regex.Match(body, "\"valid\"\\s*:\\s*(true|false)");
                if (m.Success)
                {
                    return m.Groups[1].Value == "true";
                }
                return null;
            }
            catch
            {
                return null; // 游戏未运行 / 端口不可达
            }
        }
    }
}
