using System.Windows;
using System.Windows.Media;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 浮窗单条消息的视图模型（WPF 数据绑定用）。
    /// </summary>
    public class FloatMessageItem
    {
        /// <summary>主行：发送者: 译文</summary>
        public string Line { get; set; }

        /// <summary>原文行（英文原文，可隐藏）</summary>
        public string OriginalLine { get; set; }

        public Visibility OriginalVisibility { get; set; }

        public Brush Color { get; set; }

        // ---------- 描边（8 方向偏移副本，关闭时 Collapsed） ----------

        /// <summary>描边颜色（描边关闭时为 null）</summary>
        public Brush OutlineColor { get; set; }

        /// <summary>描边副本可见性</summary>
        public Visibility OutlineVisibility { get; set; }

        public double Ox_E { get; set; }
        public double Oy_E { get; set; }
        public double Ox_W { get; set; }
        public double Oy_W { get; set; }
        public double Ox_N { get; set; }
        public double Oy_N { get; set; }
        public double Ox_S { get; set; }
        public double Oy_S { get; set; }
        public double Ox_NE { get; set; }
        public double Oy_NE { get; set; }
        public double Ox_NW { get; set; }
        public double Oy_NW { get; set; }
        public double Ox_SE { get; set; }
        public double Oy_SE { get; set; }
        public double Ox_SW { get; set; }
        public double Oy_SW { get; set; }
    }
}
