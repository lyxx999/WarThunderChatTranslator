using System;
using System.Collections.Generic;
using System.Text;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 无线电消息（游戏内按 T 键呼出的快捷指令）识别的**兜底**路径。
    /// 主路径是 ChatService 按原文里的字距 \t 判定，但游戏只给中文插字距
    /// （lang/ui.csv 实测 Chinese / Traditional Chinese 每行都有 \t，English / Russian / Korean 等一列都没有），
    /// 所以非中文客户端发出的指令只能靠文案认出来。
    /// 命中 → 正文用发送方阵营色（与游戏内一致）；未命中（玩家手打）→ 正文用中性色。
    /// 误判只影响颜色、不影响内容，所以带目标名的模板宁可放宽前缀也不要漏。
    /// </summary>
    public static class RadioMessages
    {
        /// <summary>
        /// 取自游戏本地化文件 lang/ui.csv 的 voice_message_*（英文 + 简体中文两列；跳过
        /// voice_message_category/* 这些菜单标题和纯占位片段），并已按 Normalize 同一规则预处理，
        /// 所以这里的比对是直接相等。要支持别的客户端语言，从同一批行的对应列再取一遍即可。
        /// </summary>
        private static readonly HashSet<string> Phrases = new HashSet<string>(StringComparer.Ordinal)
        {
            "no", "不行", "反对", "同意", "多谢", "好的", "感谢", "抱歉", "收到", "漂亮", "赞成", "yes", "原谅我", "太棒了", "好极了", "对不起",
            "干得好", "我拒绝", "掩护我", "装填中", "请原谅", "谢谢你", "跟我来", "跟我走", "跟着我", "onme", "准备着陆", "守卫基地", "正在修理", "正在着陆",
            "正在维修", "正在装填", "正在降落", "注意后面", "空袭警报", "绝对不行", "请求掩护", "请求支援", "跟我行动", "进攻a点", "进攻b点", "进攻c点", "进攻d点",
            "防守a点", "防守b点", "防守c点", "防守d点", "防守基地", "防御基地", "需要支援", "需要维修", "非常感谢", "bravo", "never", "sorry",
            "就在你身后", "iagree", "thanks", "抱歉失手误伤", "摧毁敌军基地", "摧毁敌方基地", "攻击敌军基地", "攻击敌军部队", "攻击敌方基地", "攻击敌方部队",
            "正在前往基地", "正在返回基地", "正在返回机场", "注意战术地图", "消灭敌军部队", "消灭敌方部队", "误伤实在抱歉", "请求空中支援", "请求航空侦察", "awesome",
            "coverme", "imsorry", "irefuse", "对不起误伤到你", "注意该坐标位置", "airalert", "excuseme", "followme", "gramercy",
            "lookback", "negative", "thankyou", "welldone", "注意当前标识区域", "请求指示空袭目标", "behindyou", "excellent",
            "guideonme", "needcover", "reloading", "repairing", "rogerthat", "为基地提供空中掩护", "检查你的六点钟方向", "needbackup",
            "不小心误伤了你对不起", "正在从无人机发送坐标", "affirmative", "gettingdown", "moveafterme", "checkyoursix", "coverthebase",
            "无人机操控员传输目标坐标", "无人机操控员传输目标方位", "defendourbase", "defendthebase", "ibegyourpardon", "attackenemybase",
            "attacktheapoint", "attackthebpoint", "attackthecpoint", "attackthedpoint", "defendtheapoint",
            "defendthebpoint", "defendthecpoint", "defendthedpoint", "somebodycoverme", "destroyenemybase",
            "headingtothebase", "sorryforteamkill", "thankyouverymuch", "attackenemytroops", "attackhostilebase",
            "attentiontothemap", "leadingforlanding", "requestingrepairs", "destroyenemytroops",
            "destroyhostilebase", "returningtothebase", "无人机操控员在该坐标发现敌方单位活动", "attackhostiletroops",
            "destroyhostiletroops", "returningtotheairfield", "provideaircoverforourbase",
            "requestingaviationsupport", "requestingairreconnaissance", "sendingcoordinatesfromadrone",
            "requestingatargetforanairattack", "attentiontothedesignatedgridzone",
            "attentiontothedesignatedgridsquare", "uavoperatortransmitstargetcoordinates",
            "uavoperatornoticedactivityinthedesignatedgridsquare",
        };

        /// <summary>
        /// 带 %s / %d 占位的模板（"攻击 %s！"、"%s，下次注意！"）：拆成前缀与后缀，空的一侧不检查。
        /// 最后两条来自 "空袭警报！" + voice_message_air_suffix（" 方位 %d，高度约 %d"）的拼接，
        /// 基础句只能当前缀用（实测原文 "空\t袭\t警\t报！ 方\t位 230，\t高\t度\t约 500"）。
        /// </summary>
        private static readonly (string Prefix, string Suffix)[] Patterns =
        {
            ( "", "下次注意" ),
            ( "", "下次看着点" ),
            ( "", "我接受你的道歉" ),
            ( "", "收到" ),
            ( "assaulting", "" ),
            ( "attack", "" ),
            ( "destroy", "" ),
            ( "eliminate", "" ),
            ( "engagingwith", "" ),
            ( "iacceptapologyforteamkillfrom", "" ),
            ( "imattacking", "" ),
            ( "someoneattack", "" ),
            ( "startingcombatwith", "" ),
            ( "干掉", "" ),
            ( "快去做掉", "" ),
            ( "我正在攻击", "" ),
            ( "攻击", "" ),
            ( "正准备攻击", "" ),
            ( "正同", "接战" ),
            ( "正在攻击", "" ),
            ( "消灭", "" ),
            ( "airalert", "" ),
            ( "空袭警报", "" ),
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
            foreach (var pattern in Patterns)
            {
                if (key.StartsWith(pattern.Prefix, StringComparison.Ordinal) &&
                    (pattern.Suffix.Length == 0 || key.EndsWith(pattern.Suffix, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 去掉点位后缀（[a1]、[ka1, 高度 600 米]）、标点与空白，英文转小写。
        /// Phrases / Patterns 里的文案也是按这套规则预处理过的，两边必须一致。
        /// </summary>
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
