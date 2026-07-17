using System.Windows;
using System.Windows.Controls;
using DevInbox.App.Services;
using DevInbox.Core.Storage;
using H.NotifyIcon;

namespace DevInbox.App.Tray;

public sealed class TrayIconController(
    NotificationRepository notifications,
    AppStateService state,
    IconRenderer renderer,
    WindowService windows) : IDisposable
{
    private TaskbarIcon? _icon;
    private TrayIconImage? _currentImage;

    public void Initialize()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "DevInbox",
            ContextMenu = BuildContextMenu(),
        };
        _icon.TrayMouseDoubleClick += (_, _) => windows.ShowHistory();

        UpdateIcon();
        _icon.ForceCreate();

        notifications.HistoryChanged += OnStateChanged;
        state.Changed += OnStateChanged;
    }

    public void Dispose()
    {
        notifications.HistoryChanged -= OnStateChanged;
        state.Changed -= OnStateChanged;
        _icon?.Dispose();
        _icon = null;
        _currentImage?.Dispose();
        _currentImage = null;
    }

    private void OnStateChanged()
        => Application.Current?.Dispatcher.BeginInvoke(UpdateIcon);

    private void UpdateIcon()
    {
        if (_icon is null)
            return;

        var unread = notifications.UnreadCount();
        var image = renderer.Render(unread, state.Status);
        _icon.Icon = image.Icon;
        // O handle antigo só pode ser destruído depois que a bandeja já exibe o novo.
        _currentImage?.Dispose();
        _currentImage = image;

        var statusText = state.Status switch
        {
            TrayStatus.AuthRequired => "autenticação necessária",
            TrayStatus.Error => "erro no último sync",
            _ => state.LastSync is { } lastSync
                ? $"último sync {lastSync.ToLocalTime():HH:mm}"
                : "sincronizando…",
        };
        _icon.ToolTipText = $"DevInbox — {unread} não lida(s), {statusText}";
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var history = new MenuItem { Header = "_Histórico" };
        history.Click += (_, _) => windows.ShowHistory();
        menu.Items.Add(history);

        var settings = new MenuItem { Header = "_Configurações" };
        settings.Click += (_, _) => windows.ShowSettings();
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var about = new MenuItem { Header = "S_obre" };
        about.Click += (_, _) => windows.ShowAbout();
        menu.Items.Add(about);

        var exit = new MenuItem { Header = "_Sair" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);

        return menu;
    }
}
