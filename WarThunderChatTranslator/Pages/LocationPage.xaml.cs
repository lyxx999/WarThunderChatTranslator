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

        /// <summary>滑块与数字输入框互相回写，用此标记阻止事件成环。</summary>
        private bool _syncingValues;

        public LocationPage()
        {
            this.InitializeComponent();

            // 用 lambda 接线，避免显式引用事件参数类型（WinAppSDK 1.5 中
            // RangeBase 位于 Primitives 命名空间、Toggled 参数类型不便引用）
            DurationSlider.ValueChanged += (s, e) => OnDurationChanged();
            FadeSlider.ValueChanged += (s, e) => OnFadeChanged();
            OpacitySlider.ValueChanged += (s, e) => OnOpacityChanged();
            ShowOriginalToggle.Toggled += (s, e) => OnShowOriginalToggled();
            ShowChannelToggle.Toggled += (s, e) => OnShowChannelToggled();
            PinnedToggle.Toggled += (s, e) => OnPinnedToggled();
            HideWhenInactiveToggle.Toggled += (s, e) => OnHideWhenInactiveToggled();
            ClearModeCombo.SelectionChanged += (s, e) => OnClearModeChanged();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            DurationSlider.Value = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_DisplayDuration"), 3);
            FadeSlider.Value = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_FadeSpeed"), 0.5);
            OpacitySlider.Value = ParseDouble(ApplicationConfig.GetSettings("FloatWindow_Opacity"), 20);
            ShowOriginalToggle.IsOn = ParseBool(ApplicationConfig.GetSettings("FloatWindow_ShowOriginal"));
            ShowChannelToggle.IsOn = ParseBool(ApplicationConfig.GetSettings("FloatWindow_ShowChannelTag"));
            PinnedToggle.IsOn = ParseBool(ApplicationConfig.GetSettings("FloatWindow_Pinned"));
            HideWhenInactiveToggle.IsOn = ParseBool(ApplicationConfig.GetSettings("FloatWindow_HideWhenGameInactive"));
            ClearModeCombo.SelectedIndex = ApplicationConfig.GetSettings("FloatWindow_ClearMode") == "next" ? 1 : 0;
            SyncNumberInputs();
            _initialized = true;
        }

        /// <summary>把滑块当前值同步到右侧数字输入框。</summary>
        private void SyncNumberInputs()
        {
            if (_syncingValues)
            {
                return;
            }
            _syncingValues = true;
            try
            {
                // 显示"真正落盘"的取值：滑块可停在 0.75 这类中间值，但保存会按精度取整
                DurationInput.Value = (int)DurationSlider.Value;
                FadeInput.Value = Math.Round(FadeSlider.Value, 1);
                OpacityInput.Value = (int)OpacitySlider.Value;
            }
            finally
            {
                _syncingValues = false;
            }
        }

        /// <summary>
        /// 数字输入框 → 滑块：只写滑块，保存与应用统一由 Slider.ValueChanged 那一条路径完成，
        /// 回写触发的 ValueChanged 用 _syncingValues 挡掉，避免一次改动产生多次重排。
        /// </summary>
        private void ApplyNumberInput(Slider slider, double value)
        {
            if (double.IsNaN(value) || _syncingValues)
            {
                return;
            }
            slider.Value = value;
        }

        private void DurationInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            ApplyNumberInput(DurationSlider, args.NewValue);
        }

        private void FadeInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            ApplyNumberInput(FadeSlider, args.NewValue);
        }

        private void OpacityInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            ApplyNumberInput(OpacitySlider, args.NewValue);
        }

        private void OnDurationChanged()
        {
            SyncNumberInputs();
            if (_initialized)
            {
                ApplicationConfig.SaveSettings("FloatWindow_DisplayDuration",
                    ((int)DurationSlider.Value).ToString(CultureInfo.InvariantCulture));
                FloatWindowController.Instance?.ApplySettings();
            }
        }

        private void OnFadeChanged()
        {
            SyncNumberInputs();
            if (_initialized)
            {
                ApplicationConfig.SaveSettings("FloatWindow_FadeSpeed",
                    FadeSlider.Value.ToString("0.0", CultureInfo.InvariantCulture));
                FloatWindowController.Instance?.ApplySettings();
            }
        }

        private void OnOpacityChanged()
        {
            SyncNumberInputs();
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

        private void OnShowChannelToggled()
        {
            if (_initialized)
            {
                ApplicationConfig.SaveSettings("FloatWindow_ShowChannelTag",
                    ShowChannelToggle.IsOn ? "true" : "false");
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

        private void OnHideWhenInactiveToggled()
        {
            if (_initialized)
            {
                ApplicationConfig.SaveSettings("FloatWindow_HideWhenGameInactive",
                    HideWhenInactiveToggle.IsOn ? "true" : "false");
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

        // ---------- 恢复默认 ----------

        private void DurationResetButton_Click(object sender, RoutedEventArgs e) =>
            ResetTo(DurationSlider, 3, OnDurationChanged);

        private void FadeResetButton_Click(object sender, RoutedEventArgs e) =>
            ResetTo(FadeSlider, 0.5, OnFadeChanged);

        private void OpacityResetButton_Click(object sender, RoutedEventArgs e) =>
            ResetTo(OpacitySlider, 20, OnOpacityChanged);

        /// <summary>滑块已经停在默认值时 ValueChanged 不会触发，这里补一次写入。</summary>
        private static void ResetTo(Slider slider, double defaultValue, Action apply)
        {
            if (slider.Value == defaultValue)
            {
                apply();
            }
            else
            {
                slider.Value = defaultValue;
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
