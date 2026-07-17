using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevInbox.App.Hosting;
using DevInbox.App.Services;
using DevInbox.Core.Auth;
using DevInbox.Core.Settings;

namespace DevInbox.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly StartupManager _startupManager;
    private readonly TokenProviderChain _authChain;
    private readonly PatTokenStore _patStore;
    private readonly WindowService _windows;
    private readonly PollingBackgroundService _polling;
    private readonly NotificationSoundPlayer _soundPlayer;
    private readonly ThemeManager _themeManager;
    private readonly string _originalThemeId;
    private bool _saved;

    [ObservableProperty]
    public partial int PollIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial bool NewComment { get; set; }

    [ObservableProperty]
    public partial bool ReviewRequested { get; set; }

    [ObservableProperty]
    public partial bool ThreadResolved { get; set; }

    [ObservableProperty]
    public partial bool ReviewReceived { get; set; }

    [ObservableProperty]
    public partial bool ChecksFailed { get; set; }

    [ObservableProperty]
    public partial bool MergeConflict { get; set; }

    [ObservableProperty]
    public partial bool Mentioned { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    public IReadOnlyList<ThemeInfo> AvailableThemes => ThemeManager.Themes;

    [ObservableProperty]
    public partial ThemeInfo SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool NotificationSoundEnabled { get; set; }

    [ObservableProperty]
    public partial string? SelectedSound { get; set; }

    public ObservableCollection<string> AvailableSounds { get; } = [];

    /// <summary>0 = padrão do Windows (~7 s); 1 = longa (~25 s). Únicas opções da plataforma de toasts.</summary>
    [ObservableProperty]
    public partial int DurationIndex { get; set; }

    public IReadOnlyList<string> DurationOptions { get; } =
        ["Padrão do Windows (~7 s)", "Longa (~25 s)"];

    [ObservableProperty]
    public partial bool QuietEnabled { get; set; }

    [ObservableProperty]
    public partial string QuietStart { get; set; }

    [ObservableProperty]
    public partial string QuietEnd { get; set; }

    [ObservableProperty]
    public partial string AuthSourceText { get; set; }

    [ObservableProperty]
    public partial string ValidationMessage { get; set; }

    public SettingsViewModel(
        SettingsStore store,
        StartupManager startupManager,
        TokenProviderChain authChain,
        PatTokenStore patStore,
        WindowService windows,
        PollingBackgroundService polling,
        NotificationSoundPlayer soundPlayer,
        ThemeManager themeManager)
    {
        _store = store;
        _startupManager = startupManager;
        _authChain = authChain;
        _patStore = patStore;
        _windows = windows;
        _polling = polling;
        _soundPlayer = soundPlayer;
        _themeManager = themeManager;

        var current = _store.Current;
        _originalThemeId = current.Theme;
        SelectedTheme = ThemeManager.Resolve(current.Theme);
        PollIntervalSeconds = current.PollIntervalSeconds;
        NewComment = current.Events.NewComment;
        ReviewRequested = current.Events.ReviewRequested;
        ThreadResolved = current.Events.ThreadResolved;
        ReviewReceived = current.Events.ReviewReceived;
        ChecksFailed = current.Events.ChecksFailed;
        MergeConflict = current.Events.MergeConflict;
        Mentioned = current.Events.Mentioned;
        StartWithWindows = _startupManager.IsEnabled();
        NotificationSoundEnabled = current.NotificationSoundEnabled;
        foreach (var sound in NotificationSoundPlayer.ListSounds())
            AvailableSounds.Add(sound);
        SelectedSound = AvailableSounds.FirstOrDefault(s =>
                string.Equals(s, current.NotificationSoundFile, StringComparison.OrdinalIgnoreCase))
            ?? AvailableSounds.FirstOrDefault();
        DurationIndex = current.LongToastDuration ? 1 : 0;
        QuietEnabled = current.QuietHours.Enabled;
        QuietStart = current.QuietHours.Start;
        QuietEnd = current.QuietHours.End;
        AuthSourceText = "";
        ValidationMessage = "";
        RefreshAuthSource();
    }

    public event Action? Saved;

    partial void OnSelectedThemeChanged(ThemeInfo value)
    {
        if (value is null || value.Id == _themeManager.CurrentThemeId)
            return;

        _themeManager.Apply(value.Id);
    }

    /// <summary>Desfaz a pré-visualização de tema quando a janela fecha sem salvar.</summary>
    public void RevertThemeIfNeeded()
    {
        if (!_saved && _themeManager.CurrentThemeId != _originalThemeId)
            _themeManager.Apply(_originalThemeId);
    }

    [RelayCommand]
    private void Save()
    {
        if (PollIntervalSeconds < 15)
        {
            ValidationMessage = "O intervalo mínimo é 15 segundos.";
            return;
        }

        if (QuietEnabled && (!TimeSpan.TryParse(QuietStart, out _) || !TimeSpan.TryParse(QuietEnd, out _)))
        {
            ValidationMessage = "Horário silencioso inválido — use o formato HH:mm.";
            return;
        }

        var settings = new AppSettings
        {
            PollIntervalSeconds = PollIntervalSeconds,
            Events = new EventToggles
            {
                NewComment = NewComment,
                ReviewRequested = ReviewRequested,
                ThreadResolved = ThreadResolved,
                ReviewReceived = ReviewReceived,
                ChecksFailed = ChecksFailed,
                MergeConflict = MergeConflict,
                Mentioned = Mentioned,
            },
            StartWithWindows = StartWithWindows,
            NotificationSoundEnabled = NotificationSoundEnabled,
            NotificationSoundFile = SelectedSound ?? _store.Current.NotificationSoundFile,
            LongToastDuration = DurationIndex == 1,
            QuietHours = new QuietHoursSettings
            {
                Enabled = QuietEnabled,
                Start = QuietStart,
                End = QuietEnd,
            },
            MaxHistoryItems = _store.Current.MaxHistoryItems,
            Theme = SelectedTheme.Id,
        };

        _store.Save(settings);
        _startupManager.SetEnabled(StartWithWindows);
        _polling.TriggerNow();
        _saved = true;
        ValidationMessage = "";
        Saved?.Invoke();
    }

    [RelayCommand]
    private void PreviewSound()
    {
        if (SelectedSound is not null)
            _soundPlayer.PlayFile(SelectedSound);
    }

    [RelayCommand]
    private void ConfigureToken() => _windows.ShowAuthSetup();

    public void RefreshAuthSource()
    {
        var source = _authChain.CurrentSource;
        var status = source is null ? "não conectado" : $"conectado via {source}";
        var tokenInfo = _patStore.HasToken ? " · token salvo nesta máquina" : "";
        AuthSourceText = $"GitHub — {status}{tokenInfo}";
    }
}
