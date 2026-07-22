using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevInbox.App.Services;
using DevInbox.App.ViewModels;
using DevInbox.Core.Settings;
using DevInbox.Core.Storage;

namespace DevInbox.App.Windows;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _viewModel;

    public HistoryWindow(
        NotificationRepository notifications,
        PrStateRepository prState,
        SettingsStore settings,
        AppStateService appState)
    {
        _viewModel = new HistoryViewModel(notifications, prState, settings, appState);
        DataContext = _viewModel;
        InitializeComponent();
        WindowStyler.ApplyChrome(this);
        Closed += (_, _) => _viewModel.Dispose();

        // Pendências é a aba principal; sem pendências, abre direto no histórico.
        MainTabs.SelectedIndex = _viewModel.Pending.Count > 0 ? 0 : 1;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is HistoryItemViewModel item)
            _viewModel.Open(item);
    }

    private void OnPendingDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is PendingItemViewModel pending)
            _viewModel.OpenPending(pending);
    }
}
