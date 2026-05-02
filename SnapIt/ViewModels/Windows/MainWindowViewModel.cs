using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;

using SnapIt.Application.Contracts;
using SnapIt.Common;
using SnapIt.Common.Entities;
using SnapIt.Common.Helpers;
using SnapIt.Common.Mvvm;
using SnapIt.Services;
using SnapIt.Services.Contracts;
using SnapIt.Views.Dialogs;
using SnapIt.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SnapIt.ViewModels.Windows;

public class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService navigationService;
    private readonly ISettingService settingService;
    private readonly ISnapManager snapManager;
    private readonly IThemeService themeService;
    private readonly INotifyIconService notifyIconService;
    private readonly IContentDialogService contentDialogService;

    private ObservableCollection<object> menuItems;
    private bool isRunning;
    private string status;
    private Window mainWindow;

    public ObservableCollection<object> MenuItems { get => menuItems; set => SetProperty(ref menuItems, value); }
    public bool IsRunning { get => isRunning; set => SetProperty(ref isRunning, value); }
    public string Status { get => status; set => SetProperty(ref status, value); }

    public DelegateCommand<CancelEventArgs> ClosingWindowCommand { get; private set; }
    public DelegateCommand StartStopCommand { get; private set; }

    public MainWindowViewModel(
        INavigationService navigationService,
        ISettingService settingService,
        ISnapManager snapManager,
        IThemeService themeService,
        INotifyIconService notifyIconService,
        IContentDialogService contentDialogService)
    {
        this.navigationService = navigationService;
        this.settingService = settingService;
        this.snapManager = snapManager;
        this.themeService = themeService;
        this.notifyIconService = notifyIconService;
        this.contentDialogService = contentDialogService;

        MenuItems =
        [
            new NavigationViewItem("Home", SymbolRegular.Home24, typeof(DashboardPage)),
            new NavigationViewItemSeparator(),
            new NavigationViewItem("Layout", SymbolRegular.DataTreemap24, typeof(LayoutPage)),
            new NavigationViewItem()
            {
                Content = "Mouse",
                Icon = new FontIcon { Glyph = "", FontFamily = new FontFamily("Segoe Fluent Icons") },
                TargetPageType = typeof(MouseSettingsPage)
            },
            new NavigationViewItem("Keyboard", SymbolRegular.Keyboard24, typeof(KeyboardSettingsPage)),
            new NavigationViewItem("Window", SymbolRegular.CalendarMultiple24, typeof(WindowsPage)),
            new NavigationViewItem("Theme", SymbolRegular.Color24, typeof(ThemePage)),
            new NavigationViewItem()
            {
                Content = "Tutorials",
                Icon = new FontIcon { Glyph = "", FontFamily = new FontFamily("Segoe Fluent Icons") },
                TargetPageType = typeof(TutorialsPage)
            },
            new NavigationViewItem("Settings", SymbolRegular.Settings24, typeof(SettingsPage)),
            new NavigationViewItem()
            {
                Content = "About",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Info24 },
                TargetPageType = typeof(AboutPage),
                MenuItemsSource = new object[]
                {
                    new NavigationViewItem("What's New", typeof(WhatsNewPage))
                }
            }
        ];

        snapManager.StatusChanged += SnapService_StatusChanged;
        snapManager.LayoutChanged += SnapService_LayoutChanged;

        ClosingWindowCommand = new DelegateCommand<CancelEventArgs>((args) =>
        {
            args.Cancel = true;

            if (mainWindow != null)
            {
                settingService.Save();
                mainWindow.Hide();
            }

            if (Dev.IsActive)
            {
                System.Windows.Application.Current.Shutdown();
            }
        });

        StartStopCommand = new DelegateCommand(snapManager.StartStop);
    }

    public override async Task InitializeAsync(RoutedEventArgs args)
    {
            var snapManager = App.Services.GetRequiredService<ISnapManager>();
            await snapManager.InitializeAsync();

        await settingService.InitializeAsync();

        notifyIconService.InitializeAsync();

        mainWindow = (Window)args.Source;
        if (!settingService.Settings.ShowMainWindow && !Dev.IsActive)
        {
            mainWindow.Visibility = Visibility.Hidden;
        }
        else
        {
            mainWindow.Visibility = Visibility.Visible;
        }

        navigationService.Navigate(typeof(DashboardPage));

        ChangeTheme();

        CheckForNewVersion();
    }

    private void ChangeTheme()
    {
        switch (settingService.Settings.AppTheme)
        {
            case UITheme.Dark:
                themeService.SetTheme(ApplicationTheme.Dark);
                break;

            case UITheme.Light:
                themeService.SetTheme(ApplicationTheme.Light);
                break;

            case UITheme.System:
                var system = themeService.GetSystemTheme();
                themeService.SetTheme(system);
                break;
        }

        SystemThemeWatcher.Watch(System.Windows.Application.Current.MainWindow);
    }

    private void SnapService_StatusChanged(bool isRunning)
    {
        IsRunning = isRunning;
        Status = isRunning ? "Stop" : "Start";
    }

    private void SnapService_LayoutChanged(SnapScreen snapScreen, Layout layout)
    {
        ShowNotification("Layout changed", $"{layout.Name} layout is set to Display {snapScreen.DeviceNumber} ({snapScreen.Resolution})");
    }

    public void ShowNotification(string title, string message, int timeout = 1000, System.Windows.Forms.ToolTipIcon tipIcon = System.Windows.Forms.ToolTipIcon.None)
    {
    }

    private async void CheckForNewVersion()
    {
        if (settingService.Settings.CheckForNewVersion)
        {
            try
            {
                await Task.Delay(new TimeSpan(0, 3, 0));
                var client = new HttpClient();
                var url = $"https://{Constants.AppVersionCheckUrl}";

                var result = await client.GetAsync(url);

                if (result.IsSuccessStatusCode)
                {
                    var response = await result.Content.ReadAsStringAsync();
                    var latestVersion = Json.Deserialize<AppVersion>(response);

                    if (latestVersion != null && System.Windows.Application.ResourceAssembly.GetName().Version.ToString() != latestVersion.Version)
                    {
                        if (!mainWindow.IsVisible)
                        {
                            mainWindow.Show();
                        }

                        var newVersionDialog = new NewVersionDialog(contentDialogService.GetContentPresenter());
                        var newVersionResult = await newVersionDialog.ShowAsync();

                        if (newVersionResult == ContentDialogResult.Primary)
                        {
                            var uriToLaunch = string.Format("https://" + Constants.AppNewVersionUrl, latestVersion.Version);
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = uriToLaunch,
                                UseShellExecute = true
                            });
                        }
                    }
                }
            }
            catch { }
        }
    }
}
