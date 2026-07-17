using System.IO;
using DevInbox.Core.Settings;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace DevInbox.App.Services;

public sealed class NotificationSoundPlayer(SettingsStore settings, ILogger<NotificationSoundPlayer> logger)
{
    /// <summary>Quanto do volume original das outras sessões fica durante o chime (0,5 = metade).</summary>
    private const float DuckFactor = 0.5f;

    private static readonly string[] SupportedExtensions = [".wav", ".mp3"];

    public static string AudioDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "audio");

    public static IReadOnlyList<string> ListSounds()
        => Directory.Exists(AudioDirectory)
            ? [.. Directory.EnumerateFiles(AudioDirectory)
                .Where(file => SupportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .Select(file => Path.GetFileName(file)!)
                .Order()]
            : [];

    public void Play()
    {
        var configured = settings.Current.NotificationSoundFile;
        var available = ListSounds();
        var fileName = available.Contains(configured, StringComparer.OrdinalIgnoreCase)
            ? configured
            : available.FirstOrDefault();

        if (fileName is not null)
            PlayFile(fileName);
    }

    public void PlayFile(string fileName)
    {
        var path = Path.Combine(AudioDirectory, fileName);
        if (!File.Exists(path))
        {
            logger.LogWarning("Som de notificação não encontrado: {Path}.", path);
            return;
        }

        Task.Run(() => PlayWithDucking(path));
    }

    private void PlayWithDucking(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            using var output = new WaveOutEvent();
            output.Init(reader);

            using var finished = new ManualResetEventSlim();
            output.PlaybackStopped += (_, _) => finished.Set();

            // Só reduz temporariamente o volume das outras sessões — nada é pausado.
            var ducked = DuckOtherSessions();
            try
            {
                output.Play();
                finished.Wait(TimeSpan.FromSeconds(15));
                Thread.Sleep(120);
            }
            finally
            {
                foreach (var (volume, original) in ducked)
                {
                    try
                    {
                        volume.Volume = original;
                    }
                    catch (Exception)
                    {
                        // A sessão pode ter terminado durante o chime.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao tocar o som de notificação {Path}.", path);
        }
    }

    private List<(SimpleAudioVolume Volume, float Original)> DuckOtherSessions()
    {
        var ducked = new List<(SimpleAudioVolume, float)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            var myProcessId = (uint)Environment.ProcessId;

            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.GetProcessID == myProcessId)
                    continue;

                if (session.State != AudioSessionState.AudioSessionStateActive)
                    continue;

                var volume = session.SimpleAudioVolume;
                var original = volume.Volume;
                volume.Volume = Math.Clamp(original * DuckFactor, 0f, 1f);
                ducked.Add((volume, original));
            }
        }
        catch (Exception ex)
        {
            // Ducking é melhor-esforço: sem ele o chime ainda toca por cima da música.
            logger.LogDebug(ex, "Não foi possível reduzir o volume das outras sessões.");
        }

        return ducked;
    }
}
