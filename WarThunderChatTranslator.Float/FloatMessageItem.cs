using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>
    /// 浮窗单条消息的视图模型（WPF 数据绑定用）。
    /// </summary>
    public class FloatMessageItem
    {
        /// <summary>主行分段：时间 [频道] 名字: 译文，每段带自己的颜色</summary>
        public List<FloatSegment> Segments { get; set; }

        /// <summary>主行纯文本（= Segments 拼接），描边用它画整行单色副本</summary>
        public string Line { get; set; }

        /// <summary>原文行（英文原文，可隐藏）</summary>
        public string OriginalLine { get; set; }

        public Visibility OriginalVisibility { get; set; }

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
