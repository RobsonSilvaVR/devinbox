using Microsoft.Win32;

namespace DevInbox.App.Services;

public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DevInbox";
    private const string LegacyValueName = "GitHubChecker";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is not null;
    }

    /// <summary>Se o autostart da era "GitHubChecker" existir, troca pelo valor novo apontando para o exe atual.</summary>
    public void MigrateLegacyValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key.GetValue(LegacyValueName) is null)
            return;

        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        if (Environment.ProcessPath is { } exePath)
            key.SetValue(ValueName, $"\"{exePath}\" --minimized");
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            if (Environment.ProcessPath is { } exePath)
                key.SetValue(ValueName, $"\"{exePath}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
