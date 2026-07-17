using System.Windows;
using DevInbox.App.Hosting;
using DevInbox.App.Services;
using DevInbox.App.ViewModels;
using DevInbox.Core.Auth;
using DevInbox.Core.Settings;

namespace DevInbox.App.Windows;

public partial class SettingsWindow : Window
{
    public SettingsWindow(
        SettingsStore store,
        StartupManager startupManager,
        TokenProviderChain authChain,
        PatTokenStore patStore,
        WindowService windows,
        PollingBackgroundService polling,
        NotificationSoundPlayer soundPlayer,
        ThemeManager themeManager)
    {
        var viewModel = new SettingsViewModel(store, startupManager, authChain, patStore, windows, polling, soundPlayer, themeManager);
        viewModel.Saved += Close;
        DataContext = viewModel;
        InitializeComponent();
        WindowStyler.ApplyChrome(this);
        Activated += (_, _) => viewModel.RefreshAuthSource();
        Closed += (_, _) => viewModel.RevertThemeIfNeeded();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
