using DevInbox.Core.Polling;

namespace DevInbox.Core.Settings;

public sealed class AppSettings
{
    public int PollIntervalSeconds { get; set; } = 60;

    public EventToggles Events { get; set; } = new();

    public bool StartWithWindows { get; set; }

    /// <summary>Toca o som escolhido no lugar do som padrão de toast do Windows.</summary>
    public bool NotificationSoundEnabled { get; set; } = true;

    /// <summary>Nome do arquivo (na pasta audio\ do app) tocado ao notificar.</summary>
    public string NotificationSoundFile { get; set; } = "msn_message.wav";

    /// <summary>Toast em modo "long" (~25 s) em vez do padrão do Windows (~7 s) — únicos valores que a plataforma aceita.</summary>
    public bool LongToastDuration { get; set; }

    public QuietHoursSettings QuietHours { get; set; } = new();

    public int MaxHistoryItems { get; set; } = 500;

    /// <summary>Id do tema visual aplicado à interface (ver ThemeManager). "GitHub" é o padrão.</summary>
    public string Theme { get; set; } = "GitHub";
}

public sealed class EventToggles
{
    public bool NewComment { get; set; } = true;

    public bool ReviewRequested { get; set; } = true;

    public bool ThreadResolved { get; set; } = true;

    public bool ReviewReceived { get; set; } = true;

    public bool ChecksFailed { get; set; } = true;

    public bool MergeConflict { get; set; } = true;

    public bool Mentioned { get; set; } = true;

    public bool IsEnabled(NotificationEventType type) => type switch
    {
        NotificationEventType.NewComment => NewComment,
        NotificationEventType.ReviewRequested => ReviewRequested,
        NotificationEventType.ThreadResolved => ThreadResolved,
        NotificationEventType.ReviewReceived => ReviewReceived,
        NotificationEventType.ChecksFailed => ChecksFailed,
        NotificationEventType.MergeConflict => MergeConflict,
        NotificationEventType.Mentioned => Mentioned,
        // Resoluções seguem o mesmo toggle do evento de origem.
        NotificationEventType.MergeConflictResolved => MergeConflict,
        NotificationEventType.ChecksRecovered => ChecksFailed,
        _ => true,
    };
}

public sealed class QuietHoursSettings
{
    public bool Enabled { get; set; }

    public string Start { get; set; } = "22:00";

    public string End { get; set; } = "08:00";

    public bool IsQuietAt(TimeSpan timeOfDay)
    {
        if (!Enabled)
            return false;

        if (!TimeSpan.TryParse(Start, out var start) || !TimeSpan.TryParse(End, out var end))
            return false;

        return start <= end
            ? timeOfDay >= start && timeOfDay < end
            : timeOfDay >= start || timeOfDay < end;
    }
}
