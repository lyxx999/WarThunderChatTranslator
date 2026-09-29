using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Dialogs;
using WarThunderChatTranslator.Helpers;
using GTranslate;
using GTranslate.Translators;
using NLog;
using Windows.UI.Notifications;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class APIPage : Page
    {
        private readonly Logger _logger;
        private bool _loaded;

        public APIPage()
        {
            InitializeComponent();
            _logger = LogManager.GetCurrentClassLogger();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var selectedAPI = ApplicationConfig.GetSettings("TranslateAPI") ?? "Microsoft";
            InitializeAPIPanel(selectedAPI);
            LoadLanguages();
            UpdateLanguageSupport(selectedAPI);
            LoadCustomAIFields();
            _loaded = true;
        }

        private void InitializeAPIPanel(String selectedAPI)
        {
            APIPanel.SelectedIndex = selectedAPI switch
            {
                "Yandex" => 1,
                "Bing" => 2,
                "Google" => 3,
                "CustomAI" => 4,
                _ => 0
            };
        }

        private void LoadCustomAIFields()
        {
            CustomAI_BaseUrl.Text = ApplicationConfig.GetSettings("CustomAI_BaseUrl") ?? "";
            CustomAI_ApiKey.Text = ApplicationConfig.GetSettings("CustomAI_ApiKey") ?? "";
            CustomAI_Model.Text = ApplicationConfig.GetSettings("CustomAI_Model") ?? "";
            CustomAI_Endpoint.Text = ApplicationConfig.GetSettings("CustomAI_Endpoint") ?? "/chat/completions";
            CustomAI_SystemPrompt.Text = ApplicationConfig.GetSettings("CustomAI_SystemPrompt") ?? "";
        }

        private bool CustomAIConfigValid(out string missing)
        {
            missing = "";
            if (string.IsNullOrWhiteSpace(CustomAI_BaseUrl.Text))
            {
                missing = "API地址";
                return false;
            }
            if (string.IsNullOrWhiteSpace(CustomAI_Model.Text))
            {
                missing = "模型";
                return false;
            }
            return true;
        }

        private void SaveAndRebuildCustomAI()
        {
            if (!_loaded || ApplicationConfig.GetSettings("TranslateAPI") != "CustomAI") return;
            try
            {
                TranslationHelper.UpdateTranslator();
            }
            catch (Exception ex)
            {
                _logger.Debug($"自定义AI配置未生效，保留上一可用翻译器：{ex.Message}");
            }
        }

        private void LoadLanguages()
        {
            var languageDictionary = GTranslate.Language.LanguageDictionary;
            var savedLanguage = ApplicationConfig.GetSettings("TargetLanguage");

            foreach (var language in languageDictionary.Values)
            {
                var comboBoxItem = new ComboBoxItem
                {
                    Content = language.NativeName,
                    Tag = language,
                    IsSelected = language.ISO6391 == savedLanguage
                };
                TargetLanguage.Items.Add(comboBoxItem);
            }
        }

        private void APIPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;

            var selectedTag = ((ComboBoxItem)APIPanel.SelectedItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(selectedTag)) return;

            ApplicationConfig.SaveSettings("TranslateAPI", selectedTag);
            if (selectedTag == "CustomAI" && !CustomAIConfigValid(out string missing))
            {
                ShowToastNotification("自定义AI尚未配置完成", $"请填写{missing}后，再点击一次该选项或测试翻译功能");
                return;
            }
            TranslationHelper.UpdateTranslator();
            UpdateLanguageSupport(selectedTag);
        }

        private void UpdateLanguageSupport(string selectedTag)
        {
            foreach (ComboBoxItem item in TargetLanguage.Items)
            {
                if (item.Tag is Language language)
                {
                    item.IsEnabled = selectedTag switch
                    {
                        "Microsoft" => language.IsServiceSupported(TranslationServices.Microsoft),
                        "Yandex" => language.IsServiceSupported(TranslationServices.Yandex),
                        "Bing" => language.IsServiceSupported(TranslationServices.Bing),
                        "Google" => language.IsServiceSupported(TranslationServices.Google),
                        _ => true,
                    };
                }
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var inputDialog = new InputDialog
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };
            var result = await inputDialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            Checking.Visibility = Visibility.Visible;
            TestResult.Visibility = Visibility.Visible;
            TestResult.ClearValue(TextBlock.ForegroundProperty);
            TestResult.Text = "翻译中，请稍候…";
            try
            {
                var translationResult = await TranslationHelper.TranslateAsync(inputDialog.text);
                TestResult.Text = $"翻译结果：{translationResult.Translation}（调用翻译器：{translationResult.Service}）";
                _logger.Debug($"翻译测试成功！翻译器：{translationResult.Service}, 翻译内容：{translationResult.Source}, 翻译结果：{translationResult.Translation}");
            }
            catch (Exception ex)
            {
                TestResult.Foreground = (Brush)Application.Current.Resources["SystemErrorTextColor"];
                TestResult.Text = $"翻译失败：{ex.Message}";
                _logger.Debug($"翻译测试失败！翻译器：{TranslationHelper.getCurrentTranslator().Name}, 翻译内容：{inputDialog.text}, 错误：{ex.Message}");
            }
            finally
            {
                Checking.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowToastNotification(string title, string message, string subtitle = "")
        {
            var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText04);
            var stringElements = toastXml.GetElementsByTagName("text");

            stringElements[0].AppendChild(toastXml.CreateTextNode(title));
            stringElements[1].AppendChild(toastXml.CreateTextNode(message));
            if (!string.IsNullOrEmpty(subtitle))
            {
                stringElements[2].AppendChild(toastXml.CreateTextNode(subtitle));
            }

            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }

        private void Bing_Token_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loaded)
            {
                ApplicationConfig.SaveSettings("Bing_Token", ((TextBox)sender).Text);
            }
        }

        private void CustomAI_BaseUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded) return;
            ApplicationConfig.SaveSettings("CustomAI_BaseUrl", ((TextBox)sender).Text);
            SaveAndRebuildCustomAI();
        }

        private void CustomAI_ApiKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded) return;
            ApplicationConfig.SaveSettings("CustomAI_ApiKey", ((TextBox)sender).Text);
            SaveAndRebuildCustomAI();
        }

        private void CustomAI_Model_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded) return;
            ApplicationConfig.SaveSettings("CustomAI_Model", ((TextBox)sender).Text);
            SaveAndRebuildCustomAI();
        }

        private void CustomAI_Endpoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded) return;
            ApplicationConfig.SaveSettings("CustomAI_Endpoint", ((TextBox)sender).Text);
            SaveAndRebuildCustomAI();
        }

        private void CustomAI_SystemPrompt_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded) return;
            ApplicationConfig.SaveSettings("CustomAI_SystemPrompt", ((TextBox)sender).Text);
            SaveAndRebuildCustomAI();
        }

        private void TargetLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loaded && TargetLanguage.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is Language selectedLanguage)
            {
                ApplicationConfig.SaveSettings("TargetLanguage", selectedLanguage.ISO6391);
                _logger.Debug($"设置目标语言为{selectedLanguage.ISO6391}");
            }
        }
    }
}