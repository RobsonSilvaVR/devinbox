using System.Windows;
using DevInbox.App.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace DevInbox.App.Services;

public sealed class WindowService(IServiceProvider services)
{
    private HistoryWindow? _history;
    private SettingsWindow? _settings;
    private AuthSetupWindow? _authSetup;
    private AboutWindow? _about;

    public void ShowHistory()
    {
        if (_history is { } existing)
        {
            Activate(existing);
            return;
        }

        _history = ActivatorUtilities.CreateInstance<HistoryWindow>(services);
        _history.Closed += (_, _) => _history = null;
        _history.Show();
    }

    public void ShowSettings()
    {
        if (_settings is { } existing)
        {
            Activate(existing);
            return;
        }

        _settings = ActivatorUtilities.CreateInstance<SettingsWindow>(services);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    public void ShowAuthSetup()
    {
        if (_authSetup is { } existing)
        {
            Activate(existing);
            return;
        }

        _authSetup = ActivatorUtilities.CreateInstance<AuthSetupWindow>(services);
        _authSetup.Closed += (_, _) => _authSetup = null;
        _authSetup.Show();
    }

    public void ShowAbout()
    {
        if (_about is { } existing)
        {
            Activate(existing);
            return;
        }

        _about = ActivatorUtilities.CreateInstance<AboutWindow>(services);
        _about.Closed += (_, _) => _about = null;
        _about.Show();
    }

    private static void Activate(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
    }
}
