using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DevInbox.App.Hosting;
using DevInbox.App.Services;
using DevInbox.App.Toasts;
using DevInbox.App.Tray;
using DevInbox.Core;
using DevInbox.Core.Auth;
using DevInbox.Core.GitHub;
using DevInbox.Core.Polling;
using DevInbox.Core.Settings;
using DevInbox.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;

namespace DevInbox.App;

public partial class App : Application
{
    private const string MutexName = @"Local\DevInbox.SingleInstance";
    private const string ShowHistorySignalName = @"Local\DevInbox.ShowHistory";

    private IHost? _host;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showHistorySignal;
    private RegisteredWaitHandle? _showHistoryWait;
    private bool _authWindowShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            TrySignalExistingInstance();
            Shutdown();
            return;
        }

        // Antes de qualquer acesso a disco: traz settings/histórico da era "GitHubChecker".
        AppPaths.MigrateLegacyDirectories();

        Directory.CreateDirectory(AppPaths.LogsDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppPaths.LogsDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _host = BuildHost();

        // A inscrição registra o ativador COM da toast; precisa acontecer antes de qualquer UI
        // para que cliques em notificações (inclusive com o app fechado) sejam entregues.
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        _host.Services.GetRequiredService<PollingEngine>().Published +=
            _host.Services.GetRequiredService<ToastService>().Show;

        _host.Start();

        var settings = _host.Services.GetRequiredService<SettingsStore>();
        _host.Services.GetRequiredService<ThemeManager>().Apply(settings.Current.Theme);

        _host.Services.GetRequiredService<TrayIconController>().Initialize();
        _host.Services.GetRequiredService<StartupManager>().MigrateLegacyValue();

        RegisterShowHistorySignal();
        WatchAuthState();

        Log.Information("DevInbox iniciado (toast relaunch: {WasToastActivated}).",
            ToastNotificationManagerCompat.WasCurrentProcessToastActivated());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showHistoryWait?.Unregister(null);
        _showHistorySignal?.Dispose();

        if (_host is not null)
        {
            _host.Services.GetRequiredService<TrayIconController>().Dispose();
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        _instanceMutex?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        builder.Services.AddSingleton(new SettingsStore(AppPaths.ConfigDirectory));
        builder.Services.AddSingleton(new Database(AppPaths.DatabasePath));
        builder.Services.AddSingleton<NotificationRepository>();
        builder.Services.AddSingleton<PrStateRepository>();

        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DevInbox/1.0");
        builder.Services.AddSingleton(http);
        builder.Services.AddSingleton<GitHubGraphQlClient>();
        builder.Services.AddSingleton<GitHubRestClient>();

        builder.Services.AddSingleton<GhCliTokenProvider>();
        builder.Services.AddSingleton(new PatTokenStore(AppPaths.ConfigDirectory));
        builder.Services.AddSingleton(provider => new TokenProviderChain(
        [
            provider.GetRequiredService<GhCliTokenProvider>(),
            provider.GetRequiredService<PatTokenStore>(),
        ]));

        builder.Services.AddSingleton<PollingEngine>();
        builder.Services.AddSingleton<AppStateService>();
        builder.Services.AddSingleton<NotificationSoundPlayer>();
        builder.Services.AddSingleton<ToastService>();
        builder.Services.AddSingleton<WindowService>();
        builder.Services.AddSingleton<ThemeManager>();
        builder.Services.AddSingleton<IconRenderer>();
        builder.Services.AddSingleton<TrayIconController>();
        builder.Services.AddSingleton<StartupManager>();

        builder.Services.AddSingleton<PollingBackgroundService>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<PollingBackgroundService>());

        return builder.Build();
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            if (!args.TryGetValue("action", out var action) || action != "open")
                return;

            if (args.TryGetValue("notifId", out var rawId) && long.TryParse(rawId, out var id))
                _host?.Services.GetRequiredService<NotificationRepository>().MarkRead(id);

            if (args.TryGetValue("url", out var url))
                BrowserLauncher.Open(url);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao tratar ativação de toast.");
        }
    }

    private static void TrySignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ShowHistorySignalName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    private void RegisterShowHistorySignal()
    {
        _showHistorySignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowHistorySignalName);
        _showHistoryWait = ThreadPool.RegisterWaitForSingleObject(
            _showHistorySignal,
            (_, _) => Dispatcher.BeginInvoke(() =>
                _host?.Services.GetRequiredService<WindowService>().ShowHistory()),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);
    }

    private void WatchAuthState()
    {
        var state = _host!.Services.GetRequiredService<AppStateService>();
        state.Changed += () =>
        {
            if (state.Status != TrayStatus.AuthRequired || _authWindowShown)
                return;

            _authWindowShown = true;
            Dispatcher.BeginInvoke(() =>
                _host!.Services.GetRequiredService<WindowService>().ShowAuthSetup());
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Exceção não tratada no dispatcher.");
        e.Handled = true;
    }
}
