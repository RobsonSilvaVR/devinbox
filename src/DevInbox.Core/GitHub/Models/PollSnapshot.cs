namespace DevInbox.Core.GitHub.Models;

public sealed record PollSnapshot(
    string ViewerLogin,
    RateLimitInfo? RateLimit,
    IReadOnlyList<PrSnapshot> Mine,
    IReadOnlyList<PrRef> ReviewRequested);

public sealed record RateLimitInfo(int Cost, int Remaining, DateTimeOffset ResetAt);

public sealed record PrRef(
    string Id,
    string Repo,
    int Number,
    string Title,
    string Url,
    string? Author);

public sealed record PrSnapshot(
    string Id,
    string Repo,
    int Number,
    string Title,
    string Url,
    bool IsDraft,
    string Mergeable,
    string? HeadOid,
    string? CheckRollupState,
    IReadOnlyList<CheckContextInfo> FailingChecks,
    IReadOnlyList<CommentInfo> IssueComments,
    IReadOnlyList<ReviewInfo> Reviews,
    IReadOnlyList<ThreadInfo> Threads);

public sealed record CommentInfo(
    string Id,
    string Url,
    string? Author,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record ReviewInfo(
    string Id,
    string Url,
    string? Author,
    string State,
    string Body,
    DateTimeOffset? SubmittedAt);

public sealed record ThreadInfo(
    string Id,
    bool IsResolved,
    bool ViewerParticipated,
    IReadOnlyList<CommentInfo> Comments);

public sealed record CheckContextInfo(
    string Name,
    string? DetailsUrl);
