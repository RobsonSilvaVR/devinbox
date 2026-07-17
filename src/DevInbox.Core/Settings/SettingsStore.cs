using System.Text.Json;

namespace DevInbox.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private readonly Lock _lock = new();

    public SettingsStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "settings.json");
        Current = Load();
    }

    public event Action? Changed;

    public AppSettings Current { get; private set; }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
            if (File.Exists(_filePath))
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            else
                File.Move(tempPath, _filePath);

            Current = settings;
        }

        Changed?.Invoke();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Arquivo corrompido: recomeça com defaults; será reescrito no próximo Save.
        }

        return new AppSettings();
    }
}
