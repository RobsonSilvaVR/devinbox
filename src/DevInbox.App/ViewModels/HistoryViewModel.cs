using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
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

    public DateTimeOffset CreatedAt { get; } = item.CreatedAt;

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

public sealed class PendingItemViewModel(PendingItem item)
{
    public string TypeLabel { get; } = item.Kind switch
    {
        PendingKind.Conversation => "Conversa",
        PendingKind.Conflict => "Conflito",
        PendingKind.Checks => "Checks",
        _ => "—",
    };

    public string Repo { get; } = item.Repo;

    public string PrLabel { get; } = $"#{item.PrNumber}";

    public string PrTitle { get; } = item.PrTitle;

    public string Detail { get; } = item.Detail ?? "(sem detalhe)";

    public string Author { get; } = item.Author ?? "—";

    public string? Url { get; } = item.Url;
}

public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private const string AllRepos = "Todos os repositórios";
    private const string AllTypes = "Todos os tipos";

    private readonly NotificationRepository _notifications;
    private readonly PrStateRepository _prState;
    private readonly SettingsStore _settings;
    private readonly AppStateService _appState;
    private readonly ICollectionView _pendingView;
    private readonly ICollectionView _itemsView;
    private bool _loading;

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

        PendingRepoFilter = AllRepos;
        PendingTypeFilter = AllTypes;
        HistoryRepoFilter = AllRepos;
        HistoryTypeFilter = AllTypes;
        ReadFilter = ReadOptions[0];
        PeriodFilter = PeriodOptions[2]; // "Últimos 7 dias" como padrão
        PendingHeader = "Pendências";

        _pendingView = CollectionViewSource.GetDefaultView(Pending);
        _pendingView.Filter = PendingFilter;
        _itemsView = CollectionViewSource.GetDefaultView(Items);
        _itemsView.Filter = HistoryFilter;

        _notifications.HistoryChanged += OnDataChanged;
        // Cada poll pode resolver/criar pendências sem gerar notificação minha — recarrega também aí.
        _appState.Changed += OnDataChanged;
        Reload();
    }

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public ObservableCollection<PendingItemViewModel> Pending { get; } = [];

    public ICollectionView ItemsView => _itemsView;

    public ICollectionView PendingView => _pendingView;

    public ObservableCollection<string> PendingRepoOptions { get; } = [];

    public IReadOnlyList<string> PendingTypeOptions { get; } = [AllTypes, "Conversa", "Conflito", "Checks"];

    public ObservableCollection<string> HistoryRepoOptions { get; } = [];

    public ObservableCollection<string> HistoryTypeOptions { get; } = [];

    public IReadOnlyList<string> ReadOptions { get; } = ["Todas", "Não lidas", "Lidas"];

    public IReadOnlyList<string> PeriodOptions { get; } = ["Qualquer período", "Hoje", "Últimos 7 dias", "Últimos 30 dias"];

    [ObservableProperty]
    public partial string PendingHeader { get; set; }

    [ObservableProperty]
    public partial string PendingRepoFilter { get; set; }

    [ObservableProperty]
    public partial string PendingTypeFilter { get; set; }

    [ObservableProperty]
    public partial string HistoryRepoFilter { get; set; }

    [ObservableProperty]
    public partial string HistoryTypeFilter { get; set; }

    [ObservableProperty]
    public partial string ReadFilter { get; set; }

    [ObservableProperty]
    public partial string PeriodFilter { get; set; }

    public void Open(HistoryItemViewModel item)
    {
        BrowserLauncher.Open(item.Url);
        _notifications.MarkRead(item.Id);
    }

    public void OpenPending(PendingItemViewModel item)
    {
        if (item.Url is not null)
            BrowserLauncher.Open(item.Url);
    }

    public void Dispose()
    {
        _notifications.HistoryChanged -= OnDataChanged;
        _appState.Changed -= OnDataChanged;
    }

    [RelayCommand]
    private void MarkAllRead() => _notifications.MarkAllRead();

    partial void OnPendingRepoFilterChanged(string value) => RefreshPending();

    partial void OnPendingTypeFilterChanged(string value) => RefreshPending();

    partial void OnHistoryRepoFilterChanged(string value) => RefreshHistory();

    partial void OnHistoryTypeFilterChanged(string value) => RefreshHistory();

    partial void OnReadFilterChanged(string value) => RefreshHistory();

    partial void OnPeriodFilterChanged(string value) => RefreshHistory();

    private void RefreshPending()
    {
        // Os filtros são inicializados no construtor antes das views existirem; ignora até lá.
        if (_loading || _pendingView is null)
            return;

        _pendingView.Refresh();
        PendingHeader = $"Pendências ({_pendingView.Cast<object>().Count()})";
    }

    private void RefreshHistory()
    {
        if (!_loading && _itemsView is not null)
            _itemsView.Refresh();
    }

    private bool PendingFilter(object obj)
    {
        var item = (PendingItemViewModel)obj;
        if (PendingRepoFilter != AllRepos && item.Repo != PendingRepoFilter)
            return false;
        if (PendingTypeFilter != AllTypes && item.TypeLabel != PendingTypeFilter)
            return false;
        return true;
    }

    private bool HistoryFilter(object obj)
    {
        var item = (HistoryItemViewModel)obj;
        if (HistoryRepoFilter != AllRepos && item.Repo != HistoryRepoFilter)
            return false;
        if (HistoryTypeFilter != AllTypes && item.TypeLabel != HistoryTypeFilter)
            return false;
        if (ReadFilter == "Não lidas" && item.IsRead)
            return false;
        if (ReadFilter == "Lidas" && !item.IsRead)
            return false;
        return InPeriod(item.CreatedAt);
    }

    private bool InPeriod(DateTimeOffset createdAt)
    {
        var now = DateTimeOffset.Now;
        return PeriodFilter switch
        {
            "Hoje" => createdAt.ToLocalTime().Date == now.ToLocalTime().Date,
            "Últimos 7 dias" => createdAt >= now.AddDays(-7),
            "Últimos 30 dias" => createdAt >= now.AddDays(-30),
            _ => true,
        };
    }

    private void OnDataChanged()
        => Application.Current?.Dispatcher.BeginInvoke(Reload);

    private void Reload()
    {
        _loading = true;
        try
        {
            Items.Clear();
            var now = DateTimeOffset.Now;
            foreach (var item in _notifications.GetHistory(Math.Max(_settings.Current.MaxHistoryItems, 100)))
                Items.Add(new HistoryItemViewModel(item, now));

            Pending.Clear();
            foreach (var item in _prState.GetPendingItems())
                Pending.Add(new PendingItemViewModel(item));

            SyncOptions(PendingRepoOptions, AllRepos, Pending.Select(p => p.Repo));
            SyncOptions(HistoryRepoOptions, AllRepos, Items.Select(i => i.Repo));
            SyncOptions(HistoryTypeOptions, AllTypes, Items.Select(i => i.TypeLabel));

            if (!PendingRepoOptions.Contains(PendingRepoFilter))
                PendingRepoFilter = AllRepos;
            if (!HistoryRepoOptions.Contains(HistoryRepoFilter))
                HistoryRepoFilter = AllRepos;
            if (!HistoryTypeOptions.Contains(HistoryTypeFilter))
                HistoryTypeFilter = AllTypes;
        }
        finally
        {
            _loading = false;
        }

        _pendingView.Refresh();
        _itemsView.Refresh();
        PendingHeader = $"Pendências ({_pendingView.Cast<object>().Count()})";
    }

    private static void SyncOptions(ObservableCollection<string> target, string allLabel, IEnumerable<string> values)
    {
        target.Clear();
        target.Add(allLabel);
        foreach (var value in values.Distinct().OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
            target.Add(value);
    }
}
