using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Entities
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public string Msg { get; set; }
        public string Sender { get; set; }
        public bool Enemy { get; set; }
        public string Mode { get; set; }
        public int Time { get; set; }
        public string TranslatedMessage { get; set; }
        public string PrettyMessage { get; set; }

        /// <summary>无线电快捷指令（游戏原文带字距 \t，见 ChatService）。不是游戏字段，由本地判定填入。</summary>
        public bool Radio { get; set; }
    }
}
