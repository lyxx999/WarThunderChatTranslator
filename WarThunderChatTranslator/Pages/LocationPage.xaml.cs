// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.FloatWindow;

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// 位置和布局：聊天浮窗的行为设置（时长/透明度/原文/固定显示）。
    /// 文字颜色与描边（开关/颜色/宽度）统一由「字体和样式」页管理。
    /// </summary>
    public sealed partial class LocationPage : Page
    {
        private bool _initialized;

        public LocationPage()
        {
            this.InitializeComponent();

            // 用 lambda 接线，避免显式引用事件参数类型（WinAppSDK 1.5 中
            // RangeBase 位于 Primitives 命名空间、Toggled 参数类型不便引用）
            DurationSlider.ValueChanged += (s, e) => OnDurationChanged();
            FadeSlider.ValueChanged += (s, e) => OnFadeChanged();
            OpacitySlider.ValueChanged += (s, e) => OnOpacityChanged();
            ShowOriginalToggle.Toggled += (s, e) => OnShowOriginalToggled();
            PinnedToggle.Toggled += (s, e) => OnPinnedToggled();
            ClearModeCombo.SelectionChanged += (s, e) => OnClearModeChanged();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            DurationSlider.Value = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_DisplayDuration"), 3);
            FadeSlider.Value = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_FadeSpeed"), 0.5);
            OpacitySlider.Value = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_Opacity"), 85);
            ShowOriginalToggle.IsOn = ParseBool(ApplicationConfig.GetSettings("FloatWindow_ShowOriginal"));
            PinnedToggle.IsOn = ParseBool(ApplicationConfig.GetSettings("FloatWindow_Pinned"));
            ClearModeCombo.SelectedIndex = ApplicationConfig.GetSettings("FloatWindow_ClearMode") == "next" ? 1 : 0;
            UpdateLabels();
            _initialized = true;
        }

        private void UpdateLabels()
        {
            DurationValue.Text = $"{(int)DurationSlider.Value} 秒";
            FadeValue.Text = $"{FadeSlider.Value:0.0} 秒";
            OpacityValue.Text = $"{(int)OpacitySlider.Value}%";
        }

        private void OnDurationChanged()
        {
            UpdateLabels();
            if (_initialized)
            {
                ApplicationConfig.SaveSettings("FloatWindow_DisplayDuration",
                    ((int)DurationSlider.Value).ToString(CultureInfo.InvariantCulture));
                FloatWindowController.Instance?.ApplySettings();
            }
        }

        private void OnFadeChanged()
        {
            UpdateLabels();
            if (_initialized)
            {
                ApplicationConfig.SaveSettings("FloatWindow_FadeSpeed",
                    FadeSlider.Value.ToString("0.0", CultureInfo.InvariantCulture));
                FloatWindowController.Instance?.ApplySettings();
            }
        }

        private void OnOpacityChanged()
        {
            UpdateLabels();
            if (_initialized)
            {
                ApplicationConfig.SaveSettings("FloatWindow_Opacity",
                    ((int)OpacitySlider.Value).ToString(CultureInfo.InvariantCulture));
                FloatWindowController.Instance?.ApplySettings();
            }
        }

        private void OnShowOriginalToggled()
        {
            if (_initialized)
            {
                ApplicationConfig.SaveSettings("FloatWindow_ShowOriginal",
                    ShowOriginalToggle.IsOn ? "true" : "false");
                FloatWindowController.Instance?.ApplySettings();
            }
        }

        private void OnPinnedToggled()
        {
            if (_initialized)
            {
                ApplicationConfig.SaveSettings("FloatWindow_Pinned",
                    PinnedToggle.IsOn ? "true" : "false");
                FloatWindowController.Instance?.ApplySettings();
            }
        }

        private void OnClearModeChanged()
        {
            if (_initialized && ClearModeCombo.SelectedItem is ComboBoxItem item)
            {
                ApplicationConfig.SaveSettings("FloatWindow_ClearMode", (string)item.Tag);
                FloatWindowController.Instance?.ApplySettings();
            }
        }

        private static double ParseDouble(string value, double fallback)
        {
            // 注意：0 是合法值（背景不透明度可为 0），只拒绝负数与非法输入
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0)
            {
                return v;
            }
            return fallback;
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out bool b) && b;
        }
    }
}
