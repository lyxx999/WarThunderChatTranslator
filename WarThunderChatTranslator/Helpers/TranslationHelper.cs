using GTranslate;
using GTranslate.Results;
using GTranslate.Translators;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Pages;

namespace WarThunderChatTranslator.Helpers
{
    public static class TranslationHelper
    {
        static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        static AggregateTranslator translator;
        static HttpClient client;
        public static void init() {
            UpdateHttpClient();
        }

        public static AggregateTranslator getCurrentTranslator()
        {
            if (translator == null)
            {
                UpdateHttpClient();
            }
            return translator;
        }

        public static void UpdateHttpClient()
        {
            string networkProxyMode = ApplicationConfig.GetSettings("NetworkProxyMode");
            string proxyAddress = ApplicationConfig.GetSettings("ProxyAddress");
            string proxyAccount = ApplicationConfig.GetSettings("ProxyAccount");
            string proxyPassword = ApplicationConfig.GetSettings("ProxyPassword");

            switch (networkProxyMode)
            {
                case "System":
                    client = new HttpClient(new HttpClientHandler
                    {
                        UseProxy = true
                    });
                    logger.Info("更新代理配置，使用系统代理");
                    break;
                case "Custom":
                    try
                    {
                        WebProxy webProxy;
                        if (String.IsNullOrEmpty(proxyAccount) || String.IsNullOrEmpty(proxyPassword))
                        {
                            webProxy = new WebProxy(proxyAddress);
                            logger.Info($"更新代理配置，使用自定义无密码代理 {proxyAddress}");
                        }
                        else
                        {
                            webProxy = new WebProxy(proxyAddress)
                            {
                                Credentials = new NetworkCredential(proxyAccount, proxyPassword)
                            };
                            logger.Info($"更新代理配置，使用自定义代理 {proxyAddress} || {proxyAccount} || {proxyPassword}");
                        }
                        client = new HttpClient(new HttpClientHandler
                        {
                            Proxy = webProxy,
                            UseProxy = true
                        });
                    }
                    catch (Exception)
                    {
                        logger.Info($"用户选择了自定义代理，但配置无效，改为使用系统代理");
                        client = new HttpClient(new HttpClientHandler
                        {
                            UseProxy = true
                        });
                        return;
                    }
                    break;
                case "Default":
                default:
                    client = new HttpClient(new HttpClientHandler
                    {
                        Proxy = null,
                        UseProxy = false
                    });
                    logger.Info("更新代理配置，不使用代理");
                    break;
            }

            UpdateTranslator();
        }

        public static void UpdateTranslator()
        {
            logger.Info($"选择使用{ApplicationConfig.GetSettings("TranslateAPI")}翻译器");
            switch (ApplicationConfig.GetSettings("TranslateAPI"))
            {
                case "Microsoft":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new MicrosoftTranslator(client) });
                        break;
                    }
                case "Yandex":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new YandexTranslator(client) });
                        break;
                    }
                case "Bing":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new BingTranslator(client) });
                        break;
                    }
                case "Google":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new GoogleTranslator2(client) });
                        break;
                    }
                case "CustomAI":
                    {
                        var baseUrl = ApplicationConfig.GetSettings("CustomAI_BaseUrl") ?? "";
                        var apiKey = ApplicationConfig.GetSettings("CustomAI_ApiKey") ?? "";
                        var model = ApplicationConfig.GetSettings("CustomAI_Model") ?? "";
                        var endpoint = ApplicationConfig.GetSettings("CustomAI_Endpoint") ?? "/chat/completions";
                        var systemPrompt = ApplicationConfig.GetSettings("CustomAI_SystemPrompt") ?? "";
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new CustomAITranslator(client, baseUrl, apiKey, model, endpoint, systemPrompt) });
                        break;
                    }
            }
        }

        public static async Task<ITranslationResult> TranslateAsync(string text)
        {
            string toLanguage = ApplicationConfig.GetSettings("TargetLanguage");
            var result = await translator.TranslateAsync(text, toLanguage);

            if (TargetScriptMissing(text, result.Translation, toLanguage))
            {
                logger.Warn($"译文语种与目标语言({toLanguage})不符，回退原文：{text} => {result.Translation}");
                return new PassthroughResult(text);
            }

            return result;
        }

        /// <summary>
        /// 语种兜底：原文里已经有目标语种的字符，译文却一个都没有 → 判定模型换了语种，丢弃译文。
        /// 混合消息不受影响（「注意 enemy 位置」→「注意敌人位置」仍保留译文）。
        /// 只校验非拉丁语种：模型跑偏时默认倒向英文，拉丁语系目标语言不会出现这种翻转，
        /// 强行校验反而会误伤（例如原文含 "GG" 而译文是西里尔字母）。
        /// </summary>
        private static bool TargetScriptMissing(string source, string translation, string targetLanguage)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translation))
            {
                return false;
            }

            Func<char, bool> isTargetScript = TargetScriptOf(targetLanguage);
            if (isTargetScript == null)
            {
                return false;
            }

            return source.Any(isTargetScript) && !translation.Any(isTargetScript);
        }

        private static Func<char, bool> TargetScriptOf(string targetLanguage)
        {
            switch (((targetLanguage ?? string.Empty).Trim().ToLowerInvariant()).Split('-')[0])
            {
                case "zh": return IsHan;
                case "ja": return c => IsHan(c) || IsKana(c);
                case "ko": return IsHangul;
                case "ru":
                case "uk":
                case "be":
                case "sr":
                case "bg":
                case "mk": return IsCyrillic;
                default: return null;
            }
        }

        private static bool IsHan(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF);
        }

        private static bool IsKana(char c) => c >= 0x3040 && c <= 0x30FF;

        private static bool IsHangul(char c) => c >= 0xAC00 && c <= 0xD7A3;

        private static bool IsCyrillic(char c) => c >= 0x0400 && c <= 0x04FF;

        private sealed class PassthroughResult : ITranslationResult
        {
            public string Translation { get; }
            public string Source { get; }
            public string Service { get; } = "原文回退";
            public ILanguage SourceLanguage { get; }
            public ILanguage TargetLanguage { get; }

            public PassthroughResult(string source)
            {
                Source = source;
                Translation = source;
            }
        }
    }
}
