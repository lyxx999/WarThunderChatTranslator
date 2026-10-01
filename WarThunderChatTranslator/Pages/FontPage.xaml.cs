using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Dialogs;
using WarThunderChatTranslator.Helpers;
using Windows.UI;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Shapes;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class FontPage : Page
    {
        public Color FontColor { get; set; }
        public Brush AllyPreviewBrush { get; set; }
        public Brush EnemyPreviewBrush { get; set; }
        public Brush SystemPreviewBrush { get; set; }
        public Brush NeutralPreviewBrush { get; set; }
        public Brush OutlinePreviewBrush { get; set; }

        /// <summary>描边滑块与数字输入框互相回写，用此标记阻止事件成环。</summary>
        private bool _syncingOutlineWidth;

        /// <summary>行间距滑块与数字输入框的回写防环标记。</summary>
        private bool _syncingLineSpacing;

        public List<Tuple<string, FontFamily>> Fonts { get; set; }

        public FontPage()
        {
            InitializeComponent();
            InitializeFontColors();
            LoadFontFamilies();

            // lambda 接线（WinAppSDK 1.5 中 Slider/ToggleSwitch 事件参数类型不便显式引用）
            OutlineToggle.Toggled += (s, e) => OnOutlineToggled();
            OutlineWidthSlider.ValueChanged += (s, e) => OnOutlineWidthChanged();
            LineSpacingSlider.ValueChanged += (s, e) => OnLineSpacingChanged();
        }

        private void InitializeFontColors()
        {
            AllyPreviewBrush = new SolidColorBrush(GetFontColor("AllyFontColor"));
            EnemyPreviewBrush = new SolidColorBrush(GetFontColor("EnemyFontColor"));
            SystemPreviewBrush = new SolidColorBrush(GetFontColor("SystemFontColor"));
            NeutralPreviewBrush = new SolidColorBrush(GetFontColor("NeutralFontColor"));
            OutlinePreviewBrush = new SolidColorBrush(GetFontColor("FloatWindow_OutlineColor"));
        }

        private Color GetFontColor(string settingKey)
        {
            string colorString = ApplicationConfig.GetSettings(settingKey);
            return ToColor(colorString);
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            FontSizePanel.Value = double.Parse(ApplicationConfig.GetSettings("FontSize"));
            FontStylePanel.SelectedIndex = GetFontStyleIndex(ApplicationConfig.GetSettings("FontStyle"));
            OutlineToggle.IsOn = ParseBool(ApplicationConfig.GetSettings("FloatWindow_Outline"));
            OutlineWidthSlider.Value = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_OutlineWidth"), 1);
            SyncOutlineWidthInput();
            LineSpacingSlider.Value = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_LineSpacing"), 0);
            SyncLineSpacingInput();
        }

        private void SyncOutlineWidthInput()
        {
            if (_syncingOutlineWidth)
            {
                return;
            }
            _syncingOutlineWidth = true;
            try
            {
                // 显示"真正落盘"的取值（保存按一位小数取整）
                OutlineWidthInput.Value = Math.Round(OutlineWidthSlider.Value, 1);
            }
            finally
            {
                _syncingOutlineWidth = false;
            }
        }

        /// <summary>
        /// 数字输入框 → 滑块：支持手动键入描边宽度。只写滑块，
        /// 保存与应用统一走 Slider.ValueChanged 那一条路径；回写触发的回调用标记挡掉。
        /// </summary>
        private void OutlineWidthInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(args.NewValue) || _syncingOutlineWidth)
            {
                return;
            }
            OutlineWidthSlider.Value = args.NewValue;
        }

        private void OnOutlineToggled()
        {
            ApplicationConfig.SaveSettings("FloatWindow_Outline",
                OutlineToggle.IsOn ? "true" : "false");
            WarThunderChatTranslator.FloatWindow.FloatWindowController.Instance?.ApplySettings();
        }

        private void OnOutlineWidthChanged()
        {
            SyncOutlineWidthInput();
            ApplicationConfig.SaveSettings("FloatWindow_OutlineWidth",
                OutlineWidthSlider.Value.ToString("0.0", CultureInfo.InvariantCulture));
            WarThunderChatTranslator.FloatWindow.FloatWindowController.Instance?.ApplySettings();
        }

        private void OutlineWidthResetButton_Click(object sender, RoutedEventArgs e)
        {
            // 已在默认值时 ValueChanged 不触发，手动补一次写入
            if (OutlineWidthSlider.Value == 1)
            {
                OnOutlineWidthChanged();
            }
            else
            {
                OutlineWidthSlider.Value = 1;
            }
        }

        private void SyncLineSpacingInput()
        {
            if (_syncingLineSpacing)
            {
                return;
            }
            _syncingLineSpacing = true;
            try
            {
                LineSpacingInput.Value = Math.Round(LineSpacingSlider.Value, 1);
            }
            finally
            {
                _syncingLineSpacing = false;
            }
        }

        /// <summary>
        /// 数字输入框 → 滑块：支持手动键入行间距。只写滑块，
        /// 保存与应用统一走 Slider.ValueChanged 那一条路径。
        /// </summary>
        private void LineSpacingInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(args.NewValue) || _syncingLineSpacing)
            {
                return;
            }
            LineSpacingSlider.Value = args.NewValue;
        }

        private void OnLineSpacingChanged()
        {
            SyncLineSpacingInput();
            ApplicationConfig.SaveSettings("FloatWindow_LineSpacing",
                LineSpacingSlider.Value.ToString("0.0", CultureInfo.InvariantCulture));
            WarThunderChatTranslator.FloatWindow.FloatWindowController.Instance?.ApplySettings();
        }

        private void LineSpacingResetButton_Click(object sender, RoutedEventArgs e)
        {
            // 已在默认值时 ValueChanged 不触发，手动补一次写入
            if (LineSpacingSlider.Value == 0)
            {
                OnLineSpacingChanged();
            }
            else
            {
                LineSpacingSlider.Value = 0;
            }
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out bool b) && b;
        }

        private static double ParseDouble(string value, double fallback)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0)
            {
                return v;
            }
            return fallback;
        }

        private int GetFontStyleIndex(string fontStyle)
        {
            // 与 ComboBox 四项一一对应：误映射会让读回时把「加粗」降级成「粗体」并写回设置
            switch ((fontStyle ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "lighter": return 0;
                case "bold": return 2;
                case "bolder": return 3;
                default: return 1;
            }
        }

        public async Task LoadFontFamilies()
        {
            Fonts = FontHelper.GetFontFamilies()
                .Select(fontFamily => new Tuple<string, FontFamily>(fontFamily.Source, fontFamily))
                .ToList();

            string fontFamilySetting = ApplicationConfig.GetSettings("FontFamily");
            if (fontFamilySetting != null)
            {
                FontFamilyPanel.SelectedIndex = Fonts.FindIndex(f => f.Item1 == fontFamilySetting);
            }
        }

        private void FontFamilyPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontFamilyPanel.SelectedValue is FontFamily selectedFontFamily)
            {
                ApplicationConfig.SaveSettings("FontFamily", selectedFontFamily.Source);
                NotifyFloatWindow();
            }
        }

        private void FontSizePanel_TextChanged(object sender, NumberBoxValueChangedEventArgs e)
        {
            if (double.IsNaN(FontSizePanel.Value))
            {
                return;
            }
            ApplicationConfig.SaveSettings("FontSize", FontSizePanel.Value.ToString(CultureInfo.InvariantCulture));
            NotifyFloatWindow();
        }

        private void FontStylePanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontStylePanel.SelectedItem is ComboBoxItem selectedItem)
            {
                ApplicationConfig.SaveSettings("FontStyle", selectedItem.Tag.ToString());
                NotifyFloatWindow();
            }
        }

        /// <summary>字体/字号/字重/颜色改动都要让浮窗立刻重排重绘。</summary>
        private void NotifyFloatWindow()
        {
            WarThunderChatTranslator.FloatWindow.FloatWindowController.Instance?.ApplySettings();
        }

        public static Color ToColor(string color)
        {
            color = Regex.Replace(color.TrimStart('#').ToLower(), "[g-z]", "");
            int alpha = Convert.ToInt32(color.Substring(0, 2), 16);
            int red = Convert.ToInt32(color.Substring(2, 2), 16);
            int green = Convert.ToInt32(color.Substring(4, 2), 16);
            int blue = Convert.ToInt32(color.Substring(6, 2), 16);
            return Color.FromArgb((byte)alpha, (byte)red, (byte)green, (byte)blue);
        }

        private async void OnColorButtonClick(string settingKey, Brush previewBrush, Shape colorPreview)
        {
            FontColor = GetFontColor(settingKey); // 选色初值 = 该项当前颜色
            var colorPickerDialog = new ColorPickerDialog(FontColor)
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };
            var result = await colorPickerDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                FontColor = colorPickerDialog.pickerColor;
                ApplicationConfig.SaveSettings(settingKey, FontColor.ToString());
                previewBrush = new SolidColorBrush(FontColor);
                colorPreview.Fill = previewBrush;
                // 颜色变化：浮窗文字颜色/描边颜色实时同步
                WarThunderChatTranslator.FloatWindow.FloatWindowController.Instance?.ApplySettings();
            }
        }

        private void Ally_Button_Click(object sender, RoutedEventArgs e)
        {
            OnColorButtonClick("AllyFontColor", AllyPreviewBrush, AllyColorPreview);
            AllyPreviewBrush = new SolidColorBrush(FontColor); // Update the reference after the async operation
        }

        private void Enemy_Button_Click(object sender, RoutedEventArgs e)
        {
            OnColorButtonClick("EnemyFontColor", EnemyPreviewBrush, EnemyColorPreview);
            EnemyPreviewBrush = new SolidColorBrush(FontColor); // Update the reference after the async operation
        }

        private void System_Button_Click(object sender, RoutedEventArgs e)
        {
            OnColorButtonClick("SystemFontColor", SystemPreviewBrush, SystemColorPreview);
            SystemPreviewBrush = new SolidColorBrush(FontColor); // Update the reference after the async operation
        }

        private void Neutral_Button_Click(object sender, RoutedEventArgs e)
        {
            OnColorButtonClick("NeutralFontColor", NeutralPreviewBrush, NeutralColorPreview);
            NeutralPreviewBrush = new SolidColorBrush(FontColor); // Update the reference after the async operation
        }

        private void Outline_Button_Click(object sender, RoutedEventArgs e)
        {
            OnColorButtonClick("FloatWindow_OutlineColor", OutlinePreviewBrush, OutlineColorPreview);
            OutlinePreviewBrush = new SolidColorBrush(FontColor); // Update the reference after the async operation
        }

        // ---------- 颜色恢复默认（默认值与 App.InitializeDefaultSettings 一致） ----------

        private void Ally_Reset_Click(object sender, RoutedEventArgs e) =>
            ResetColor("AllyFontColor", "#FF5472F2", AllyColorPreview, b => AllyPreviewBrush = b);

        private void Enemy_Reset_Click(object sender, RoutedEventArgs e) =>
            ResetColor("EnemyFontColor", "#FFF25A54", EnemyColorPreview, b => EnemyPreviewBrush = b);

        private void System_Reset_Click(object sender, RoutedEventArgs e) =>
            ResetColor("SystemFontColor", "#FFD4A017", SystemColorPreview, b => SystemPreviewBrush = b);

        private void Neutral_Reset_Click(object sender, RoutedEventArgs e) =>
            ResetColor("NeutralFontColor", "#FFB6B6B6", NeutralColorPreview, b => NeutralPreviewBrush = b);

        private void OutlineColor_Reset_Click(object sender, RoutedEventArgs e) =>
            ResetColor("FloatWindow_OutlineColor", "#FFFFFFFF", OutlineColorPreview, b => OutlinePreviewBrush = b);

        private void ResetColor(string settingKey, string defaultColor, Shape preview, Action<Brush> setBrush)
        {
            var brush = new SolidColorBrush(ToColor(defaultColor));
            ApplicationConfig.SaveSettings(settingKey, defaultColor);
            FontColor = brush.Color;
            setBrush(brush);
            preview.Fill = brush;
            NotifyFloatWindow();
        }

    }
}