using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.FloatWindow;

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// 互动和操作页：游戏内交互相关设置（长按回车切换浮窗固定显示）。
    /// </summary>
    public sealed partial class InteractPage : Page
    {
        private bool _initialized;

        public InteractPage()
        {
            this.InitializeComponent();
            EnterTogglePinToggle.Toggled += (s, e) => OnEnterTogglePinToggled();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            EnterTogglePinToggle.IsOn = ParseBool(ApplicationConfig.GetSettings("FloatWindow_TogglePinByEnter"));
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

        private static bool ParseBool(string value)
        {
            return value == null || bool.TryParse(value, out bool b) && b;
        }
    }
}
