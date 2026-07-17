using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevInbox.App.Services;
using DevInbox.App.Toasts;
using DevInbox.Core.Settings;
using DevInbox.Core.Storage;

namespace DevInbox.App.ViewModels;

public sealed class HistoryItemViewModel(HistoryItem item, DateTimeOffset now)
{
    public long Id { get; } = item.Id;

    public string TypeLabel { get; } = EventDisplay.Label(item.EventType, item.Subtype);

    public string Repo { get; } = item.Repo;

    public string PrLabel { get; } = $"#{item.PrNumber}";

    public string Title { get; } = item.Title;

    public string? Preview { get; } = item.BodyPreview;

    public string? Actor { get; } = item.Actor;

    public string When { get; } = FormatRelative(item.CreatedAt.ToLocalTime(), now.ToLocalTime());

    public bool IsRead { get; } = item.IsRead;

    public string Url { get; } = item.Url;

    public string UnreadDot => IsRead ? "" : "●";

    public FontWeight TitleWeight => IsRead ? FontWeights.Normal : FontWeights.SemiBold;

    private static string FormatRelative(DateTimeOffset createdAt, DateTimeOffset now)
    {
        var elapsed = now - createdAt;
        return elapsed switch
        {
            { TotalSeconds: < 60 } => "agora",
            { TotalMinutes: < 60 } => $"há {(int)elapsed.TotalMinutes} min",
            { TotalHours: < 24 } => $"há {(int)elapsed.TotalHours} h",
            { TotalDays: < 2 } => $"ontem {createdAt:HH:mm}",
            _ => createdAt.ToString("dd/MM HH:mm"),
        };
    }
}

public sealed class ThreadItemViewModel(UnresolvedThread thread)
{
    public string Repo { get; } = thread.Repo;

    public string PrLabel { get; } = $"#{thread.PrNumber}";

    public string PrTitle { get; } = thread.PrTitle;

    public string Preview { get; } = thread.Preview ?? "(sem prévia)";

    public string Author { get; } = thread.Author ?? "—";

    public string Participation { get; } = thread.IParticipated ? "participei" : "no meu PR";

    public string? Url { get; } = thread.Url;
}

public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly NotificationRepository _notifications;
    private readonly PrStateRepository _prState;
    private readonly SettingsStore _settings;
    private readonly AppStateService _appState;

    public HistoryViewModel(
        NotificationRepository notifications,
        PrStateRepository prState,
        SettingsStore settings,
        AppStateService appState)
    {
        _notifications = notifications;
        _prState = prState;
        _settings = settings;
        _appState = appState;
        ThreadsHeader = "Pendências de review";

        _notifications.HistoryChanged += OnDataChanged;
        // Cada poll pode resolver/criar threads sem gerar notificação minha — recarrega também aí.
        _appState.Changed += OnDataChanged;
        Reload();
    }

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public ObservableCollection<ThreadItemViewModel> Threads { get; } = [];

    [ObservableProperty]
    public partial string ThreadsHeader { get; set; }

    public void Open(HistoryItemViewModel item)
    {
        BrowserLauncher.Open(item.Url);
        _notifications.MarkRead(item.Id);
    }

    public void OpenThread(ThreadItemViewModel thread)
    {
        if (thread.Url is not null)
            BrowserLauncher.Open(thread.Url);
    }

    public void Dispose()
    {
        _notifications.HistoryChanged -= OnDataChanged;
        _appState.Changed -= OnDataChanged;
    }

    [RelayCommand]
    private void MarkAllRead() => _notifications.MarkAllRead();

    private void OnDataChanged()
        => Application.Current?.Dispatcher.BeginInvoke(Reload);

    private void Reload()
    {
        Items.Clear();
        var now = DateTimeOffset.Now;
        foreach (var item in _notifications.GetHistory(Math.Max(_settings.Current.MaxHistoryItems, 100)))
            Items.Add(new HistoryItemViewModel(item, now));

        Threads.Clear();
        foreach (var thread in _prState.GetUnresolvedThreads())
            Threads.Add(new ThreadItemViewModel(thread));

        ThreadsHeader = $"Pendências de review ({Threads.Count})";
    }
}
