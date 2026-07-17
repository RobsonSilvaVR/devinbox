using System.Diagnostics;
using DevInbox.Core.Auth;
using DevInbox.Core.GitHub;
using DevInbox.Core.GitHub.Models;
using DevInbox.Core.Settings;
using DevInbox.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DevInbox.Core.Polling;

public sealed record PollOutcome(RateLimitInfo? RateLimit, int EventsDetected, int NotificationsCreated);

public sealed class PollingEngine
{
    private const string ViewerLoginKey = "viewer_login";
    private const string NotifLastModifiedKey = "notif_last_modified";

    private readonly GitHubGraphQlClient _graphQl;
    private readonly GitHubRestClient _rest;
    private readonly TokenProviderChain _auth;
    private readonly PrStateRepository _prRepository;
    private readonly NotificationRepository _notificationRepository;
    private readonly SettingsStore _settings;
    private readonly ILogger<PollingEngine> _logger;

    private DateTimeOffset _nextMentionPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _nextPrune = DateTimeOffset.MinValue;

    public PollingEngine(
        GitHubGraphQlClient graphQl,
        GitHubRestClient rest,
        TokenProviderChain auth,
        PrStateRepository prRepository,
        NotificationRepository notificationRepository,
        SettingsStore settings,
        ILogger<PollingEngine> logger)
    {
        _graphQl = graphQl;
        _rest = rest;
        _auth = auth;
        _prRepository = prRepository;
        _notificationRepository = notificationRepository;
        _settings = settings;
        _logger = logger;
    }

    public event Action<NotificationRecord>? Published;

    public async Task<PollOutcome> PollOnceAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var token = await _auth.GetTokenAsync(cancellationToken)
            ?? throw new AuthUnavailableException(_auth.LastFailures);

        var login = _prRepository.GetKv(ViewerLoginKey);
        if (login is null)
        {
            login = await _graphQl.GetViewerLoginAsync(token, cancellationToken);
            _prRepository.SetKv(ViewerLoginKey, login);
        }

        var now = DateTimeOffset.UtcNow;
        var snapshot = await _graphQl.PollAsync(token, login, cancellationToken);
        var dbState = _prRepository.LoadDiffState();

        var diff = StateDiffer.Diff(new DiffInput(
            login,
            dbState.BaselineDone,
            dbState.KnownPrs,
            dbState.SeenItemIds,
            dbState.KnownThreads,
            snapshot,
            now));

        _prRepository.ApplyDiff(diff, now);

        var created = 0;
        var toggles = _settings.Current.Events;
        foreach (var notification in diff.Events)
        {
            if (!toggles.IsEnabled(notification.Type))
                continue;

            if (_notificationRepository.TryInsert(notification, out var id))
            {
                Published?.Invoke(new NotificationRecord(id, notification));
                created++;
            }
        }

        created += await PollMentionsAsync(token, login, now, cancellationToken);

        if (now >= _nextPrune)
        {
            _notificationRepository.Prune(_settings.Current.MaxHistoryItems);
            _prRepository.PruneClosedPrs(now.AddDays(-30));
            _nextPrune = now.AddHours(6);
        }

        _logger.LogInformation(
            "Poll concluído em {DurationMs} ms: {PrCount} PRs meus, {ReviewCount} com review pedido, "
            + "{Events} eventos, {Created} notificações novas, custo {Cost}, restante {Remaining}.",
            stopwatch.ElapsedMilliseconds,
            snapshot.Mine.Count,
            snapshot.ReviewRequested.Count,
            diff.Events.Count,
            created,
            snapshot.RateLimit?.Cost,
            snapshot.RateLimit?.Remaining);

        return new PollOutcome(snapshot.RateLimit, diff.Events.Count, created);
    }

    private async Task<int> PollMentionsAsync(
        string token, string login, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now < _nextMentionPoll)
            return 0;

        try
        {
            var lastModified = _prRepository.GetKv(NotifLastModifiedKey);
            // Primeira consulta de menções: registra o que já existe sem notificar (baseline).
            var isBaseline = lastModified is null;

            var result = await _rest.FetchMentionsAsync(token, lastModified, cancellationToken);
            _nextMentionPoll = now.AddSeconds(result.PollIntervalSeconds);

            if (result.NotModified)
                return 0;

            if (result.LastModified is not null)
                _prRepository.SetKv(NotifLastModifiedKey, result.LastModified);

            var created = 0;
            foreach (var mention in result.Mentions)
            {
                var seenId = $"mention_thread:{mention.ThreadId}:{mention.UpdatedAt}";
                if (_prRepository.HasSeenItem(seenId))
                    continue;

                _prRepository.AddSeenItem(new SeenItem(seenId, "rest", "mention_thread"), now);

                if (isBaseline)
                    continue;

                // PRs já rastreados pelo GraphQL geram evento por lá; aqui só menções fora deles.
                if (_prRepository.IsTrackedOpenPr(mention.Repo, mention.PrNumber))
                    continue;

                if (!_settings.Current.Events.Mentioned)
                    continue;

                MentionDetails? details = null;
                if (mention.LatestCommentApiUrl is not null)
                    details = await _rest.GetMentionDetailsAsync(token, mention.LatestCommentApiUrl, cancellationToken);

                var url = details?.HtmlUrl ?? $"https://github.com/{mention.Repo}/pull/{mention.PrNumber}";
                var notification = new NotificationEvent(
                    NotificationEventType.Mentioned,
                    mention.Repo,
                    mention.PrNumber,
                    mention.PrTitle,
                    url,
                    DedupKey: $"mention:rest:{seenId}",
                    now,
                    Actor: details?.Author,
                    BodyPreview: TruncateBody(details?.Body));

                if (_notificationRepository.TryInsert(notification, out var id))
                {
                    Published?.Invoke(new NotificationRecord(id, notification));
                    created++;
                }
            }

            return created;
        }
        catch (Exception ex) when (ex is HttpRequestException or GitHubApiException)
        {
            // Falha nas menções não pode derrubar o ciclo principal do GraphQL.
            _logger.LogWarning(ex, "Falha no poll de menções; nova tentativa no próximo ciclo.");
            _nextMentionPoll = now.AddSeconds(60);
            return 0;
        }
    }

    private static string? TruncateBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var normalized = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 140 ? normalized : normalized[..140] + "…";
    }
}
