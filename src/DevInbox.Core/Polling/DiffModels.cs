using DevInbox.Core.GitHub.Models;

namespace DevInbox.Core.Polling;

public sealed record PrDbState(
    string PrId,
    string Repo,
    int Number,
    string Title,
    string Url,
    bool IsMine,
    bool IsReviewRequested,
    string Mergeable,
    string? HeadOid,
    string? CheckRollup,
    bool ConflictNotified,
    bool IsOpen);

public sealed record ThreadDbState(
    string ThreadId,
    string PrId,
    bool IsResolved,
    bool IParticipated,
    string? Url = null,
    string? Preview = null,
    string? Author = null);

public sealed record SeenItem(string ItemId, string PrId, string Kind);

public sealed record DiffInput(
    string ViewerLogin,
    bool BaselineDone,
    IReadOnlyDictionary<string, PrDbState> KnownPrs,
    IReadOnlySet<string> SeenItemIds,
    IReadOnlyDictionary<string, ThreadDbState> KnownThreads,
    PollSnapshot Snapshot,
    DateTimeOffset Now);

public sealed record DiffResult(
    IReadOnlyList<NotificationEvent> Events,
    IReadOnlyList<PrDbState> PrUpserts,
    IReadOnlyList<SeenItem> NewSeenItems,
    IReadOnlyList<ThreadDbState> ThreadUpserts,
    IReadOnlyList<string> ClosedPrIds);
