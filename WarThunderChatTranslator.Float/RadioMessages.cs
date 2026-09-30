using System;
using System.Collections.Generic;
using System.Text;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 无线电消息（游戏内按 T 键呼出的快捷指令）识别。
    /// 游戏的 /gamechat 数据里没有"这条是无线电消息"的标志，只能按固定文案匹配原文：
    /// 快捷指令由发送方客户端本地化后发出，同一局里会中英混杂，所以两张表都要有。
    /// 命中 → 正文用发送方阵营色（与游戏内一致）；未命中（玩家手打）→ 正文用中性色。
    /// 漏掉的指令往 Phrases 里补即可，误判只影响颜色，不影响内容。
    /// </summary>
    public static class RadioMessages
    {
        private static readonly HashSet<string> Phrases = new HashSet<string>(StringComparer.Ordinal)
        {
            // 客户端为英文时游戏发出的原文
            "attack enemy base", "attack enemy troops", "attack a point", "attack b point",
            "attack c point", "attack d point", "attack designated target",
            "cover our troops", "cover our base", "cover me",
            "defend a point", "defend b point", "defend c point", "defend d point", "defend our base",
            "need support", "need help", "request support", "request bombing", "request artillery",
            "request airstrike", "request reconnaissance", "request repair", "need repairs",
            "affirmative", "roger", "negative", "sorry", "thank you", "thanks", "well done",
            "follow me", "returning to base", "on my way", "coming for help",
            "enemy position", "enemy spotted", "out of ammo", "low fuel", "out of fuel",
            "repairing", "attention tactical map", "attention at this position",
            "attention this coordinate", "preparing to land", "landing",

            // 客户端为中文时游戏发出的原文（含截图实测到的写法）
            "进攻敌方基地", "进攻敌军部队", "进攻a点", "进攻b点", "进攻c点", "进攻d点", "攻击指定目标",
            "掩护我方部队", "掩护我方基地", "掩护我",
            "防守a点", "防守b点", "防守c点", "防守d点", "防守我方基地",
            "需要支援", "需要帮助", "请求支援", "请求轰炸", "请求炮击", "请求空袭", "请求侦察", "需要维修",
            "收到", "同意", "明白", "否定", "拒绝", "抱歉", "对不起", "谢谢你", "多谢", "谢谢", "干得漂亮",
            "跟着我", "返回基地", "正在返回基地", "马上到", "正在赶来",
            "敌人位置", "发现敌人", "弹药耗尽", "燃料不足", "燃油不足", "正在维修",
            "注意战术地图", "注意该坐标位置", "准备着陆", "正在着陆"
        };

        /// <summary>带目标/点位参数的指令：前缀命中即可（点位后缀会被剥掉，但游戏可能拼进正文）。</summary>
        private static readonly string[] Prefixes =
        {
            "i'm attacking", "im attacking", "attacking", "正在攻击", "攻击目标", "enemy at", "敌人在"
        };

        public static bool IsRadio(string msg)
        {
            string key = Normalize(msg);
            if (key.Length == 0)
            {
                return false;
            }
            if (Phrases.Contains(key))
            {
                return true;
            }
            foreach (var prefix in Prefixes)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>去掉点位后缀（[a1]、[ka1, 高度 600 米]）、标点与空白，英文转小写。</summary>
        private static string Normalize(string msg)
        {
            if (string.IsNullOrEmpty(msg))
            {
                return string.Empty;
            }
            var sb = new StringBuilder(msg.Length);
            int depth = 0;
            foreach (char ch in msg)
            {
                if (ch == '[' || ch == '［')
                {
                    depth++;
                    continue;
                }
                if (ch == ']' || ch == '］')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                    continue;
                }
                if (depth > 0 || char.IsWhiteSpace(ch))
                {
                    continue;
                }
                if (IsPunctuation(ch))
                {
                    continue;
                }
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        private static bool IsPunctuation(char ch)
        {
            switch (ch)
            {
                case '!': case '?': case '.': case ',': case ';': case ':': case '\'': case '"':
                case '！': case '？': case '。': case '，': case '；': case '：': case '、':
                case '“': case '”': case '‘': case '’': case '·': case '~': case '～':
                    return true;
                default:
                    return char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.DashPunctuation;
            }
        }
    }
}
