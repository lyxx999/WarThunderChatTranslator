using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class AboutPage : Page
    {
        public string Version { get; } = $"版本：{Package.Current.Id.Version.Major}.{Package.Current.Id.Version.Minor}.{Package.Current.Id.Version.Build}.{Package.Current.Id.Version.Revision}";

        /// <summary>本修改版（fork）的仓库地址：复制链接与"项目页面"都指向它。</summary>
        private const string ProjectUrl = "https://github.com/lyxx999/WarThunderChatTranslator";

        /// <summary>上游原项目地址，按 GPL v3 保留出处。</summary>
        private const string UpstreamUrl = "https://github.com/IShiraiKurokoI/WarThunderChatTranslator";

        public AboutPage()
        {
            InitializeComponent();
        }

        private void HyperlinkButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var dataPackage = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            dataPackage.SetText(ProjectUrl);
            Clipboard.SetContent(dataPackage);
            CopyTip.IsOpen = true;
        }

        private void SettingsCard_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new System.Uri(ProjectUrl));
        }

        private void UpstreamCard_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new System.Uri(UpstreamUrl));
        }
    }
}