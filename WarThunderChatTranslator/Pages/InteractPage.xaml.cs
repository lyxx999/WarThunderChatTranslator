using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.FloatWindow;

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// 互动和操作页：游戏内交互设置（长按回车切换浮窗固定显示、长按判定时长）。
    /// </summary>
    public sealed partial class InteractPage : Page
    {
        private const double DefaultHoldSeconds = 0.5;

        private bool _initialized;

        /// <summary>滑块与数字输入框互相回写，用此标记阻止事件成环。</summary>
        private bool _syncingValues;

        public InteractPage()
        {
            this.InitializeComponent();
            EnterTogglePinToggle.Toggled += (s, e) => OnEnterTogglePinToggled();
            HoldSlider.ValueChanged += (s, e) => OnHoldChanged();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            EnterTogglePinToggle.IsOn = ParseBool(ApplicationConfig.GetSettings("FloatWindow_TogglePinByEnter"));
            HoldSlider.Value = ParseHoldMs(ApplicationConfig.GetSettings("FloatWindow_EnterHoldMs")) / 1000.0;
            SyncHoldInput();
            _initialized = true;
        }

        private void OnEnterTogglePinToggled()
        {
            if (!_initialized)
            {
                return;
            }
            ApplicationConfig.SaveSettings("FloatWindow_TogglePinByEnter",
                EnterTogglePinToggle.IsOn ? "true" : "false");
            FloatWindowController.Instance?.ApplySettings();
        }

        /// <summary>
        /// 长按阈值按毫秒存整数：界面用秒显示，存秒会引入小数舍入歧义。
        /// </summary>
        private void OnHoldChanged()
        {
            SyncHoldInput();
            if (!_initialized)
            {
                return;
            }
            int ms = (int)Math.Round(HoldSlider.Value * 1000);
            ApplicationConfig.SaveSettings("FloatWindow_EnterHoldMs", ms.ToString(CultureInfo.InvariantCulture));
            FloatWindowController.Instance?.ApplySettings();
        }

        /// <summary>把滑块当前值同步到右侧数字输入框。</summary>
        private void SyncHoldInput()
        {
            if (_syncingValues)
            {
                return;
            }
            _syncingValues = true;
            try
            {
                HoldInput.Value = Math.Round(HoldSlider.Value, 1);
            }
            finally
            {
                _syncingValues = false;
            }
        }

        /// <summary>数字输入框只写滑块，保存统一走 Slider.ValueChanged 那一条路径。</summary>
        private void HoldInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(args.NewValue) || _syncingValues)
            {
                return;
            }
            HoldSlider.Value = args.NewValue;
        }

        private void HoldResetButton_Click(object sender, RoutedEventArgs e)
        {
            // 滑块已经停在默认值时 ValueChanged 不会触发，这里补一次写入
            if (HoldSlider.Value == DefaultHoldSeconds)
            {
                OnHoldChanged();
            }
            else
            {
                HoldSlider.Value = DefaultHoldSeconds;
            }
        }

        private static int ParseHoldMs(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms)
                && ms >= 100 && ms <= 5000)
            {
                return ms;
            }
            return (int)(DefaultHoldSeconds * 1000);
        }

        private static bool ParseBool(string value)
        {
            return value == null || bool.TryParse(value, out bool b) && b;
        }
    }
}
