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

        // ---------- 行间距（由代码按当前字号算好，描边副本必须与主文字完全一致） ----------

        /// <summary>主行行高（字体自然行高 + 用户设定的额外行间距）。</summary>
        public double RowLineHeight { get; set; }

        /// <summary>原文行行高（原文固定 12 像素字号）。</summary>
        public double OriginalLineHeight { get; set; }

        /// <summary>消息之间的间隔（底部外边距）。</summary>
        public Thickness RowGap { get; set; }

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
