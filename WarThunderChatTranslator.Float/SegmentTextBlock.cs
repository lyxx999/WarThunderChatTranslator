using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WarThunderChatTranslator.FloatWindow
{
    /// <summary>一行文字中的一个片段：文本 + 自己的颜色（用于分段着色）。</summary>
    public class FloatSegment
    {
        public string Text { get; set; }
        public Brush Brush { get; set; }
    }

    /// <summary>浮窗调色板，取自「字体和样式」页的五色设置。</summary>
    public class FloatPalette
    {
        public Brush AllyBrush { get; set; }
        public Brush EnemyBrush { get; set; }
        public Brush SystemBrush { get; set; }

        /// <summary>中性色：时间戳、以及敌军消息的正文。</summary>
        public Brush NeutralBrush { get; set; }

        /// <summary>坐标色：正文里的网格坐标（[b2]、[ka1, 高度 600 米]）。</summary>
        public Brush CoordBrush { get; set; }
    }

    /// <summary>
    /// 分段着色的 TextBlock。TextBlock.Inlines 不是依赖属性、不能绑定，
    /// 这里用可绑定的 Segments 在变化时重建 Inlines；整行仍是一个 TextBlock，
    /// 因此换行/行高与单色整行完全一致（描边副本靠同样的文本对齐）。
    /// </summary>
    public class SegmentTextBlock : TextBlock
    {
        public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
            nameof(Segments),
            typeof(IEnumerable<FloatSegment>),
            typeof(SegmentTextBlock),
            new PropertyMetadata(null, OnSegmentsChanged));

        public IEnumerable<FloatSegment> Segments
        {
            get => (IEnumerable<FloatSegment>)GetValue(SegmentsProperty);
            set => SetValue(SegmentsProperty, value);
        }

        private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var block = (SegmentTextBlock)d;
            block.Inlines.Clear();
            if (!(e.NewValue is IEnumerable<FloatSegment> segments))
            {
                return;
            }

            foreach (var segment in segments)
            {
                block.Inlines.Add(new Run(segment.Text) { Foreground = segment.Brush });
            }
        }
    }
}
