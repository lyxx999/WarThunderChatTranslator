using GTranslate;
using GTranslate.Results;
using GTranslate.Translators;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Helpers
{
    /// <summary>
    /// 自定义 AI 翻译器：兼容 OpenAI /chat/completions 协议。
    /// 可用于 OpenAI、DeepSeek、Moonshot、GLM、OpenRouter、Ollama、LM Studio 等任意 OpenAI 兼容服务。
    /// </summary>
    public sealed class CustomAITranslator : ITranslator
    {
        private const string DefaultSystemPrompt =
            "You are a real-time combat chat translator dedicated to the online game War Thunder. " +
            "Translate the user's message (from any source language) into {language}. " +
            "Output ONLY the translation itself — no explanations, no quotes, no prefixes, no extra lines. " +
            "Keep the tone short and casual, like live battle chat. Preserve emojis and symbols. " +
            "Never translate vehicle names, map names, or player nicknames/IDs — keep them exactly as written. " +
            "Use established War Thunder community terminology, not literal translation. " +
            "Commonly used English abbreviations may stay in English (BR, DPM, AP, HE, HEAT, SPAA, SPG, MBT, AFV, BZ, RNG). " +
            "When the target language is Chinese, follow these community mappings: " +
            "BR=战评, Tier=等级; light/medium/heavy tank=轻坦/中坦/重坦, MBT=主战坦克; " +
            "AT=反坦克歼击车, SPG=自行火炮, SPAA=防空炮; " +
            "RB/AR/RCT/RND=写实/街机/快战/遭遇战, BZ=战斗区域; " +
            "spot/spotted=发现/被发现, cam/bush=蹲草/草丛; " +
            "cap/capping=占点/占点中, hold=守住, push=推进, rotate=转移, flank=绕后; " +
            "overmatch=穿深溢出, pen=穿深, RHA=均质装甲, hull down=车体隐蔽; " +
            "AP=穿甲弹, HE=碎甲弹, HEAT=破甲弹, APCR=硬芯穿甲弹, SAP=脱壳穿甲弹; " +
            "DPM=每分钟伤害, alpha strike=爆发伤害, TTK=击杀时间; " +
            "re-rack/reload=装填, traverse=炮塔回转, eject=跳车, suicide=自爆, grief=捣乱, AFK=挂机; " +
            "RNG=脸/运气, gg=打得漂亮, gl=祝好运, 1v1=单挑; " +
            "team/clan/squad=车队/军团/小队; nations=国籍+系 (American=美系, German=德系, Soviet=苏系, etc.). " +
            "If the message is already in the target language, output it unchanged.";

        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _endpoint;
        private readonly string _systemPrompt;

        public string Name { get; }

        public CustomAITranslator(HttpClient client, string baseUrl, string apiKey, string model, string endpoint, string systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("自定义AI未配置：请先在设置页填写 API 地址");
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException("自定义AI未配置：请先在设置页填写模型名称");
            }

            _client = client ?? new HttpClient();
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey ?? "";
            _model = model;
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? "/chat/completions" : (endpoint.StartsWith("/") ? endpoint : "/" + endpoint);
            _systemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? DefaultSystemPrompt : systemPrompt;
            Name = "自定义AI (" + _model + ")";
        }

        public async Task<ITranslationResult> TranslateAsync(string text, string toLanguage, string fromLanguage)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new CustomAIResult(text, text, Name, null, null);
            }

            var target = string.IsNullOrWhiteSpace(toLanguage) ? "auto" : toLanguage;
            var systemPrompt = _systemPrompt.Replace("{language}", target);

            var payload = new JObject
            {
                ["model"] = _model,
                ["stream"] = false,
                ["temperature"] = 0.2,
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = systemPrompt },
                    new JObject { ["role"] = "user", ["content"] = text }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + _endpoint)
            {
                Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            using var response = await _client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"自定义AI请求失败：HTTP {(int)response.StatusCode} {Truncate(body)}");
            }

            JToken root;
            try
            {
                root = JToken.Parse(body);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("自定义AI返回了非JSON内容：" + Truncate(body), ex);
            }

            var content = (string)root?["choices"]?[0]?["message"]?["content"];
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("自定义AI响应缺少 choices[0].message.content 字段：" + Truncate(body));
            }

            return new CustomAIResult(text, content.Trim(), Name, null, null);
        }

        public async Task<ITranslationResult> TranslateAsync(string text, ILanguage toLanguage, ILanguage fromLanguage)
        {
            return await TranslateAsync(text, toLanguage?.ISO6391, fromLanguage?.ISO6391);
        }

        public Task<ITransliterationResult> TransliterateAsync(string text, string toLanguage, string fromLanguage)
        {
            throw new NotSupportedException("自定义AI不支持转写字母");
        }

        public Task<ITransliterationResult> TransliterateAsync(string text, ILanguage toLanguage, ILanguage fromLanguage)
        {
            throw new NotSupportedException("自定义AI不支持转写字母");
        }

        public Task<ILanguage> DetectLanguageAsync(string text)
        {
            throw new NotSupportedException("自定义AI不支持语言检测");
        }

        public bool IsLanguageSupported(string language) => true;

        public bool IsLanguageSupported(ILanguage language) => true;

        private static string Truncate(string s)
        {
            s ??= "";
            return s.Length > 300 ? s.Substring(0, 300) + "…" : s;
        }

        private sealed class CustomAIResult : ITranslationResult
        {
            public string Translation { get; }
            public string Source { get; }
            public string Service { get; }
            public ILanguage SourceLanguage { get; }
            public ILanguage TargetLanguage { get; }

            public CustomAIResult(string source, string translation, string service, ILanguage sourceLanguage, ILanguage targetLanguage)
            {
                Source = source;
                Translation = translation;
                Service = service;
                SourceLanguage = sourceLanguage;
                TargetLanguage = targetLanguage;
            }
        }
    }
}
