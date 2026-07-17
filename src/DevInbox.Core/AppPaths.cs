namespace DevInbox.Core;

public static class AppPaths
{
    public static string ConfigDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevInbox");

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevInbox");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "state.db");

    /// <summary>Move as pastas da era "GitHubChecker" preservando settings, token e histórico (dedup incluso).</summary>
    public static void MigrateLegacyDirectories()
    {
        MigrateDirectory(Environment.SpecialFolder.ApplicationData, ConfigDirectory);
        MigrateDirectory(Environment.SpecialFolder.LocalApplicationData, DataDirectory);
    }

    private static void MigrateDirectory(Environment.SpecialFolder root, string newDirectory)
    {
        var legacy = Path.Combine(Environment.GetFolderPath(root), "GitHubChecker");
        try
        {
            if (Directory.Exists(legacy) && !Directory.Exists(newDirectory))
                Directory.Move(legacy, newDirectory);
        }
        catch (IOException)
        {
            // Sem migração o app recomeça vazio; o baseline silencioso evita enxurrada de toasts.
        }
    }
}
