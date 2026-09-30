using Microsoft.UI.Xaml;
using System;
using System.IO;
using WarThunderChatTranslator.Configurations;
using System.Threading.Tasks;
using Application = Microsoft.UI.Xaml.Application;
using H.NotifyIcon;
using Microsoft.UI;
using System.Net;
using System.Text;
using System.Net.Http;
using System.Diagnostics;
using GTranslate.Translators;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.Security.Principal;
using Windows.UI.Notifications;
using Windows.ApplicationModel.Core;
using NLog;
using System.Text.RegularExpressions;
using WarThunderChatTranslator.Pages;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.FloatWindow;
using Microsoft.UI.Xaml.Input;
using WinUICommunity;
using System.Threading; // 引入命名空间

namespace WarThunderChatTranslator
{
    public partial class App : Microsoft.UI.Xaml.Application
    {
        public NLog.Logger logger;
        public static IThemeService themeService { get; set; }

        private Window m_window;
        public TaskbarIcon TrayIcon { get; private set; }
        private static readonly string url = IsAdmin() ? "http://+:8100/" : "http://localhost:8100/";
        private HttpListener _httpListener;
        private FloatWindowController _floatController;

        private static Mutex mutex; // 定义静态 Mutex 变量

        public App()
        {
            // 创建 Mutex，判断是否已经存在同名 Mutex
            bool isNewInstance;
            mutex = new Mutex(true, "WarThunderChatTranslator_Mutex", out isNewInstance);

            if (!isNewInstance)
            {
                var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
                toastXml.GetElementsByTagName("text")[0].AppendChild(toastXml.CreateTextNode("WarThunderChatTranslator 已在运行，请勿开启新实例。"));
                var toast = new ToastNotification(toastXml);
                ToastNotificationManager.CreateToastNotifier().Show(toast);
                Environment.Exit(0);
                return;
            }
            this.InitializeComponent();
            InitializeLogging();
            RegisterGlobalExceptionHandlers();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            InitializeAppSettings();
            InitializeFloatWindow();
            InitializeTrayIcon();
            StartHttpServer();
        }

        private void InitializeFloatWindow()
        {
            try
            {
                _floatController = new FloatWindowController();
                _floatController.Init();
            }
            catch (Exception ex)
            {
                logger?.Error($"初始化浮窗失败: {ex}");
            }
        }

        private void InitializeLogging()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
            logger.Info("--------程序启动--------");
            DeleteOldLogs();
        }

        private void RegisterGlobalExceptionHandlers()
        {
            TaskScheduler.UnobservedTaskException += (sender, e) => HandleException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleException(e.ExceptionObject as Exception);
            App.Current.UnhandledException += (sender, e) => HandleException(e.Exception);
        }

        private void InitializeAppSettings()
        {
            var defaultSettings = new Dictionary<string, string>
            {
                { "NetworkProxyMode", "Default" },
                { "ProxyAddress", "" },
                { "ProxyAccount", "" },
                { "ProxyPassword", "" },
                { "LastUpdateCheckDate", "从未" },
                { "TranslateAPI", "Microsoft" },
                { "TargetLanguage", "zh-CN" },
                { "FontFamily", "Segoe UI" },
                { "FontSize", "14" },
                { "FontStyle", "normal" },
                { "AllyFontColor", "#FF5472F2" },
                { "EnemyFontColor", "#FFF25A54" },
                { "SystemFontColor", "#FFD4A017" },
                { "NeutralFontColor", "#FFB6B6B6" },
                { "Theme", "Default" },
                { "BackgroundCSS", "background-color: #f4f4f4;" },
                { "CustomAI_BaseUrl", "" },
                { "CustomAI_ApiKey", "" },
                { "CustomAI_Model", "" },
                { "CustomAI_Endpoint", "/chat/completions" },
                { "CustomAI_SystemPrompt", "" },
                { "FloatWindow_Enabled", "false" },
                { "FloatWindow_DisplayDuration", "3" },
                { "FloatWindow_FadeSpeed", "0.5" },
                { "FloatWindow_Opacity", "20" },
                { "FloatWindow_ShowOriginal", "false" },
                { "FloatWindow_ShowChannelTag", "true" },
                { "FloatWindow_Pinned", "false" },
                { "FloatWindow_HideWhenGameInactive", "false" },
                { "FloatWindow_Position", "" },
                { "FloatWindow_Size", "" },
                { "FloatWindow_Outline", "false" },
                { "FloatWindow_OutlineColor", "#FFFFFFFF" },
                { "FloatWindow_OutlineWidth", "1" },
                { "FloatWindow_ClearMode", "exit" },
                { "FloatWindow_TogglePinByEnter", "true" },
            };

            foreach (var setting in defaultSettings)
            {
                if (ApplicationConfig.GetSettings(setting.Key) == null)
                {
                    ApplicationConfig.SaveSettings(setting.Key, setting.Value);
                }
            }

            // 旧默认色是网页样式配色、和游戏内不一致；只升级仍停留在旧默认值上的用户
            MigrateSettingIfUnchanged("AllyFontColor", "#FF5BC0DE", "#FF5472F2");
            MigrateSettingIfUnchanged("EnemyFontColor", "#FFD9534F", "#FFF25A54");
            MigrateSettingIfUnchanged("SystemFontColor", "#FF856404", "#FFD4A017");
            // 背景 85% 在游戏画面上太像一块面板，默认降到 20%（设置页可一键恢复）
            MigrateSettingIfUnchanged("FloatWindow_Opacity", "85", "20");

            logger.Info("初始化翻译器对象");
            TranslationHelper.init();
            logger.Info("翻译器对象初始化完成");
        }

        private static void MigrateSettingIfUnchanged(string key, string oldValue, string newValue)
        {
            if (ApplicationConfig.GetSettings(key) == oldValue)
            {
                ApplicationConfig.SaveSettings(key, newValue);
            }
        }

        private void InitializeTrayIcon()
        {
            var floatWindowCommand = (XamlUICommand)Resources["FloatWindowCommand"];
            floatWindowCommand.ExecuteRequested += (sender, args) =>
            {
                if (_floatController == null)
                {
                    return;
                }
                _floatController.SetEnabled(!_floatController.Enabled);
                UpdateFloatWindowMenuItem(_floatController.Enabled);
            };

            var OpenDashboardCommand = (XamlUICommand)Resources["OpenDashboardCommand"];
            OpenDashboardCommand.ExecuteRequested += (sender, args) => Windows.System.Launcher.LaunchUriAsync(new System.Uri("http://localhost:8100"));

            var showHideWindowCommand = (XamlUICommand)Resources["ShowHideWindowCommand"];
            showHideWindowCommand.ExecuteRequested += ToggleMainWindowVisibility;

            var exitApplicationCommand = (XamlUICommand)Resources["ExitApplicationCommand"];
            exitApplicationCommand.ExecuteRequested += (sender, args) => ExitApplication();

            TrayIcon = (TaskbarIcon)Resources["TrayIcon"];
            TrayIcon.ForceCreate();

            UpdateFloatWindowMenuItem(_floatController != null && _floatController.Enabled);

            CoreApplication.Exiting += (sender, e) => ExitApplication();
        }

        private void UpdateFloatWindowMenuItem(bool isOn)
        {
            if (TrayIcon?.ContextFlyout is Microsoft.UI.Xaml.Controls.MenuFlyout flyout)
            {
                foreach (var item in flyout.Items)
                {
                    if (item is Microsoft.UI.Xaml.Controls.MenuFlyoutItem mfi && Equals(mfi.Tag, "FloatWindow"))
                    {
                        // WinUI 的 MenuFlyoutItem 没有 IsChecked：用图标切换表示状态
                        mfi.Icon = isOn
                            ? new Microsoft.UI.Xaml.Controls.FontIcon
                            {
                                Glyph = "\uE73E", // Segoe Fluent Icons: CheckMark
                                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
                            }
                            : new Microsoft.UI.Xaml.Controls.SymbolIcon { Symbol = Microsoft.UI.Xaml.Controls.Symbol.View };
                    }
                }
            }
        }

        private void ToggleMainWindowVisibility(XamlUICommand sender, ExecuteRequestedEventArgs args)
        {
            if (m_window == null)
            {
                InitializeMainWindow();
                return;
            }

            if (!m_window.Visible)
            {
                m_window.Show();
            }
        }

        private void InitializeMainWindow()
        {
            m_window = new MainWindow();

            var theme = ApplicationConfig.GetSettings("Theme") ?? "Default";
            ElementTheme SettingsTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            ApplicationConfig.SaveSettings("Theme", theme);

            themeService = new ThemeService();
            themeService.Initialize(m_window);
            themeService.ConfigBackdrop(BackdropType.AcrylicThin);
            themeService.ConfigElementTheme(SettingsTheme);
            themeService.ConfigTitleBar(new TitleBarCustomization
            {
                TitleBarWindowType = TitleBarWindowType.AppWindow,
                LightTitleBarButtons = new TitleBarButtons { ButtonBackgroundColor = Colors.Transparent },
                DarkTitleBarButtons = new TitleBarButtons { ButtonBackgroundColor = Colors.Transparent }
            });

            CenterWindow(m_window);

            m_window.Closed += (sender, args) =>
            {
                if (HandleClosedEvents)
                {
                    args.Handled = true;
                    m_window.Hide();
                }
            };
            m_window.Show();
        }

        private static void CenterWindow(Window window)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);

            if (appWindow is not null && displayArea is not null)
            {
                var CenteredPosition = appWindow.Position;
                CenteredPosition.X = (displayArea.WorkArea.Width - appWindow.Size.Width) / 2;
                CenteredPosition.Y = (displayArea.WorkArea.Height - appWindow.Size.Height) / 2;
                appWindow.Move(CenteredPosition);
            }
        }

        private static bool IsAdmin()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public bool HandleClosedEvents { get; set; } = true;

        private void ExitApplication()
        {
            HandleClosedEvents = false;
            try
            {
                OnClosed();
            }
            catch (Exception)
            { 

            }
            try
            {
                TrayIcon?.Dispose();
            }
            catch (Exception)
            {

            }
            try
            {
                m_window?.Close();
            }
            catch (Exception)
            {

            }
            Application.Current.Exit();
            Environment.Exit(0);
        }

        private void DeleteOldLogs()
        {
            try
            {
                string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WTChatTranslator", "Log");
                string logFilePrefix = "Log-WTChatTranslator-";
                DateTime deletionDate = DateTime.Now.AddDays(-3);

                foreach (var logFile in Directory.EnumerateFiles(logDirectory, $"{logFilePrefix}*.log"))
                {
                    var dateString = Path.GetFileName(logFile)?.Substring(logFilePrefix.Length, 10);
                    if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime logDate) && logDate <= deletionDate)
                    {
                        File.Delete(logFile);
                        logger.Info("删除过期日志: " + Path.GetFileName(logFile));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.ToString());
            }
        }

        private void HandleException(Exception ex)
        {
            logger.Error(ex.ToString());

            var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
            toastXml.GetElementsByTagName("text")[0].AppendChild(toastXml.CreateTextNode(ex.Message + ex.StackTrace));
            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }

        private async void StartHttpServer()
        {
            if (!IsPortAllowedInFirewall(8100))
            {
                logger.Info("端口 8100 在防火墙中未被允许。正在添加规则...");
                AddFirewallRule(8100, "WarThunderChatTranslator：允许端口 8100");
            }
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add(url);
                _httpListener.Start();
                logger.Info($"HTTP服务器已启动，正在监听 {url}");
            }
            catch (Exception ex)
            {
                logger.Error(ex.ToString());
                var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
                toastXml.GetElementsByTagName("text")[0].AppendChild(toastXml.CreateTextNode("8100端口被其他端口占用，请检查端口占用后再打开应用！"));
                var toast = new ToastNotification(toastXml);
                ToastNotificationManager.CreateToastNotifier().Show(toast);
                Environment.Exit(0);
                return;
            }

            await Task.Run(HandleRequests);
        }

        private bool IsPortAllowedInFirewall(int port)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall firewall show rule name=all",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output.Contains($"WarThunderChatTranslator：允许端口 {port}");
        }

        private void AddFirewallRule(int port, string ruleName)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" protocol=TCP dir=in localport={port} action=allow description=\"此规则允许端口 {port} 的入站访问\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(processStartInfo);
                process.WaitForExit();
                logger.Info($"防火墙规则 '{ruleName}' 已添加。");
            }
            catch (Exception ex)
            {
                logger.Info($"无法添加防火墙规则: {ex.Message}");
            }
        }

        private async Task HandleRequests()
        {
            while (_httpListener.IsListening)
            {
                var context = await _httpListener.GetContextAsync();
                var response = context.Response;

                try
                {
                    await ProcessRequest(context);
                }
                catch (Exception ex)
                {
                    logger.Error($"处理请求时发生错误: {ex.Message}");
                    await SendErrorResponse(response, "聊天数据请求失败");
                }
                finally
                {
                    response.OutputStream.Close();
                }
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            switch (request.Url.AbsolutePath)
            {
                case "/gamechat":
                    await HandleGameChatRequest(request, response);
                    break;
                case "/styles.css":
                    await ServeDynamicCss(response);
                    break;
                case "/dashboard":
                    await ServeFile(response, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"), "dashboard.html", "text/html");
                    break;
                case "/favicon.ico":
                    await ServeFile(response, AppDomain.CurrentDomain.BaseDirectory, "favicon.ico", "image/x-icon");
                    break;
                case "/":
                    response.StatusCode = 302;
                    response.RedirectLocation = "/dashboard";
                    response.ContentEncoding = Encoding.UTF8;
                    response.ContentType = "text/html; charset=utf-8";
                    var buffer = Encoding.UTF8.GetBytes("302");
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    break;
                default:
                    await SendErrorResponse(response, "404 Not Found");
                    break;
            }
        }

        private async Task HandleGameChatRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            // 面板行为保持不变：转发查询中的 lastId（面板目前不传，即全量拉取后整页重渲染）
            int lastId = 0;
            if (!int.TryParse(request.QueryString["lastId"], out lastId))
            {
                lastId = 0;
            }

            var chatMessages = await ChatService.FetchAndTranslateAsync(lastId);

            if (chatMessages == null)
            {
                await SendErrorResponse(response, "聊天数据请求失败");
                return;
            }

            var processedData = JsonConvert.SerializeObject(chatMessages);
            await SendResponse(response, processedData, "application/json; charset=utf-8");
        }

        private async Task ServeDynamicCss(HttpListenerResponse response)
        {
            var fontFamily = ApplicationConfig.GetSettings("FontFamily") ?? "Segoe UI";
            var fontSize = ApplicationConfig.GetSettings("FontSize") ?? "14px";
            var fontStyle = ApplicationConfig.GetSettings("FontStyle") ?? "Normal";
            var allyFontColor = ToRgba(ApplicationConfig.GetSettings("AllyFontColor") ?? "#FF5472F2");
            var enemyFontColor = ToRgba(ApplicationConfig.GetSettings("EnemyFontColor") ?? "#FFF25A54");
            var systemFontColor = ToRgba(ApplicationConfig.GetSettings("SystemFontColor") ?? "#FF856404");
            var bodyBackground = ApplicationConfig.GetSettings("BackgroundCSS") ?? "opacity: 0;";

            var cssContent = $@"
                body {{
                    font-family: {fontFamily}, Arial, sans-serif;
                    font-weight: {fontStyle};
                    margin: 0;
                    padding: 20px;
                    {bodyBackground}
                }}
                h1 {{
                    text-align: center;
                    color: #333;
                }}
                #chat-container {{
                    max-width: 84vw;
                    margin: 20px auto;
                    background-color: #fff;
                    border-radius: 10px;
                    box-shadow: 0 2px 10px rgba(0, 0, 0, 0.1);
                    padding: 20px;
                    height: 70vh;
                    overflow-y: auto;
                }}
                .chat-message {{
                    display: flex;
                    align-items: center;
                    margin-bottom: 15px;
                    padding: 10px;
                    border-radius: 5px;
                    font-size: {fontSize}px;
                    line-height: 1.5;
                }}
                .chat-message img {{
                    width: 20px;
                    height: 20px;
                    margin-right: 10px;
                }}
                .chat-message.ally {{
                    background-color: #e5f7ff;
                    color: {allyFontColor};
                }}
                .chat-message.enemy {{
                    background-color: #ffe5e5;
                    color: {enemyFontColor};
                }}
                .chat-message.system {{
                    background-color: #fff3cd;
                    color: {systemFontColor};
                }}";

            var buffer = Encoding.UTF8.GetBytes(cssContent);
            response.ContentType = "text/css; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        private string ToRgba(string argbColor)
        {
            if (argbColor.StartsWith("#"))
            {
                var argb = argbColor.Substring(1);
                var a = int.Parse(argb.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255.0;
                var r = int.Parse(argb.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                var g = int.Parse(argb.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                var b = int.Parse(argb.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
                return $"rgba({r}, {g}, {b}, {a.ToString("0.##")})";
            }
            return argbColor;
        }
        private async Task ServeFile(HttpListenerResponse response, string directory, string fileName, string contentType)
        {
            var filePath = Path.Combine(directory, fileName);
            logger.Debug($"返回响应文件{filePath}");
            if (File.Exists(filePath))
            {
                var fileContent = await File.ReadAllBytesAsync(filePath);
                response.ContentType = contentType;
                response.ContentLength64 = fileContent.Length;
                await response.OutputStream.WriteAsync(fileContent, 0, fileContent.Length);
            }
            else
            {
                await SendErrorResponse(response, "404 Not Found - File is missing.");
            }
        }

        private async Task SendResponse(HttpListenerResponse response, string data, string contentType)
        {
            response.ContentEncoding = Encoding.UTF8;
            response.ContentType = contentType;
            var buffer = Encoding.UTF8.GetBytes(data);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task SendErrorResponse(HttpListenerResponse response, string errorMessage)
        {
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await SendResponse(response, errorMessage, "text/html; charset=utf-8");
        }

        protected void OnClosed()
        {
            _floatController?.Dispose();
            _httpListener.Stop();
            _httpListener.Close();
        }
    }
}