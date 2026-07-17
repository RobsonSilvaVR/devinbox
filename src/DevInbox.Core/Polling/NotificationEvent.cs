namespace DevInbox.Core.Polling;

public enum NotificationEventType
{
    NewComment,
    ReviewRequested,
    ThreadResolved,
    ReviewReceived,
    ChecksFailed,
    MergeConflict,
    Mentioned,
}

public sealed record NotificationEvent(
    NotificationEventType Type,
    string Repo,
    int PrNumber,
    string PrTitle,
    string Url,
    string DedupKey,
    DateTimeOffset CreatedAt,
    string? Subtype = null,
    string? Actor = null,
    string? BodyPreview = null);

public sealed record NotificationRecord(long Id, NotificationEvent Event);
