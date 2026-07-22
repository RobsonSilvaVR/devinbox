using DevInbox.Core.GitHub.Models;

namespace DevInbox.Core.Polling;

public static class StateDiffer
{
    private const int PreviewLength = 140;

    public static DiffResult Diff(DiffInput input)
    {
        var events = new List<NotificationEvent>();
        var resolutionEvents = new List<NotificationEvent>();
        var prUpserts = new List<PrDbState>();
        var newSeenItems = new List<SeenItem>();
        var threadUpserts = new List<ThreadDbState>();

        var snapshot = input.Snapshot;
        var reviewRequestedIds = snapshot.ReviewRequested.Select(pr => pr.Id).ToHashSet();

        foreach (var pr in snapshot.Mine)
        {
            var known = input.KnownPrs.GetValueOrDefault(pr.Id);
            // PR visto pela primeira vez: registra tudo em silêncio para não inundar de toasts.
            var baseline = !input.BaselineDone || known is null;

            DiffComments(input, pr, baseline, events, newSeenItems);
            DiffReviews(input, pr, baseline, events, newSeenItems);
            DiffThreads(input, pr, baseline, resolutionEvents, threadUpserts);
            var checksFailedNotified = DiffChecks(pr, known, baseline, events, resolutionEvents, input.Now);
            var (mergeable, conflictNotified) = DiffMergeable(pr, known, baseline, events, resolutionEvents, input.Now);

            prUpserts.Add(new PrDbState(
                pr.Id, pr.Repo, pr.Number, pr.Title, pr.Url,
                IsMine: true,
                IsReviewRequested: reviewRequestedIds.Contains(pr.Id),
                mergeable, pr.HeadOid, pr.CheckRollupState,
                conflictNotified, checksFailedNotified,
                IsOpen: true));
        }

        var mineIds = snapshot.Mine.Select(pr => pr.Id).ToHashSet();
        foreach (var pr in snapshot.ReviewRequested)
        {
            if (mineIds.Contains(pr.Id))
                continue;

            var known = input.KnownPrs.GetValueOrDefault(pr.Id);
            if (input.BaselineDone && (known is null || !known.IsReviewRequested))
            {
                events.Add(new NotificationEvent(
                    NotificationEventType.ReviewRequested,
                    pr.Repo, pr.Number, pr.Title, pr.Url,
                    DedupKey: $"revreq:{pr.Id}",
                    input.Now,
                    Actor: pr.Author));
            }

            prUpserts.Add(new PrDbState(
                pr.Id, pr.Repo, pr.Number, pr.Title, pr.Url,
                IsMine: known?.IsMine ?? false,
                IsReviewRequested: true,
                known?.Mergeable ?? "UNKNOWN",
                known?.HeadOid,
                known?.CheckRollup,
                known?.ConflictNotified ?? false,
                known?.ChecksFailedNotified ?? false,
                IsOpen: true));
        }

        var visibleIds = mineIds.Concat(reviewRequestedIds).ToHashSet();
        var closedPrIds = input.KnownPrs.Values
            .Where(pr => pr.IsOpen && !visibleIds.Contains(pr.PrId))
            .Select(pr => pr.PrId)
            .ToList();

        return new DiffResult(events, resolutionEvents, prUpserts, newSeenItems, threadUpserts, closedPrIds);
    }

    private static void DiffComments(
        DiffInput input, PrSnapshot pr, bool baseline,
        List<NotificationEvent> events, List<SeenItem> newSeenItems)
    {
        var issueComments = pr.IssueComments.Select(c => (Comment: c, Kind: "issue_comment"));
        var reviewComments = pr.Threads.SelectMany(t => t.Comments).Select(c => (Comment: c, Kind: "review_comment"));

        foreach (var (comment, kind) in issueComments.Concat(reviewComments))
        {
            if (input.SeenItemIds.Contains(comment.Id))
                continue;

            newSeenItems.Add(new SeenItem(comment.Id, pr.Id, kind));

            if (baseline || IsViewer(comment.Author, input.ViewerLogin))
                continue;

            var mentioned = MentionsViewer(comment.Body, input.ViewerLogin);
            events.Add(new NotificationEvent(
                mentioned ? NotificationEventType.Mentioned : NotificationEventType.NewComment,
                pr.Repo, pr.Number, pr.Title,
                comment.Url.Length > 0 ? comment.Url : pr.Url,
                DedupKey: $"{(mentioned ? "mention" : "comment")}:{comment.Id}",
                input.Now,
                Actor: comment.Author,
                BodyPreview: Truncate(comment.Body)));
        }
    }

    private static void DiffReviews(
        DiffInput input, PrSnapshot pr, bool baseline,
        List<NotificationEvent> events, List<SeenItem> newSeenItems)
    {
        foreach (var review in pr.Reviews)
        {
            if (input.SeenItemIds.Contains(review.Id))
                continue;

            newSeenItems.Add(new SeenItem(review.Id, pr.Id, "review"));

            if (baseline || IsViewer(review.Author, input.ViewerLogin))
                continue;

            if (review.State is not ("APPROVED" or "CHANGES_REQUESTED" or "COMMENTED"))
                continue;

            // Review COMMENTED sem corpo é só o invólucro de comentários inline, que já geram evento próprio.
            if (review.State == "COMMENTED" && string.IsNullOrWhiteSpace(review.Body))
                continue;

            events.Add(new NotificationEvent(
                NotificationEventType.ReviewReceived,
                pr.Repo, pr.Number, pr.Title,
                review.Url.Length > 0 ? review.Url : pr.Url,
                DedupKey: $"review:{review.Id}",
                input.Now,
                Subtype: review.State,
                Actor: review.Author,
                BodyPreview: Truncate(review.Body)));
        }
    }

    private static void DiffThreads(
        DiffInput input, PrSnapshot pr, bool baseline,
        List<NotificationEvent> resolutionEvents, List<ThreadDbState> threadUpserts)
    {
        foreach (var thread in pr.Threads)
        {
            var known = input.KnownThreads.GetValueOrDefault(thread.Id);
            // Participação é permanente: comentários antigos podem sair da janela de 5 do snapshot.
            var participated = thread.ViewerParticipated || (known?.IParticipated ?? false);

            var firstComment = thread.Comments.FirstOrDefault();
            threadUpserts.Add(new ThreadDbState(
                thread.Id, pr.Id, thread.IsResolved, participated,
                firstComment?.Url ?? known?.Url,
                firstComment is null ? known?.Preview : Truncate(firstComment.Body),
                firstComment?.Author ?? known?.Author));

            if (baseline || known is null || known.IsResolved || !thread.IsResolved)
                continue;

            // Resolução: sai da aba de pendências e vai ao histórico já como lida (canal silencioso).
            resolutionEvents.Add(new NotificationEvent(
                NotificationEventType.ThreadResolved,
                pr.Repo, pr.Number, pr.Title,
                thread.Comments.FirstOrDefault()?.Url is { Length: > 0 } url ? url : pr.Url,
                DedupKey: $"resolved:{thread.Id}",
                input.Now,
                BodyPreview: Truncate(thread.Comments.FirstOrDefault()?.Body ?? "")));
        }
    }

    /// <summary>Diferencia o estado dos checks e devolve se o PR permanece com checks em falha rastreados.</summary>
    private static bool DiffChecks(
        PrSnapshot pr, PrDbState? known, bool baseline,
        List<NotificationEvent> events, List<NotificationEvent> resolutionEvents, DateTimeOffset now)
    {
        var rollup = pr.CheckRollupState;
        var wasNotified = known?.ChecksFailedNotified ?? false;

        if (rollup is "FAILURE" or "ERROR")
        {
            // Rollup anterior só vale para o mesmo head; push novo zera o rastreio e re-notifica.
            var previousRollup = known?.HeadOid == pr.HeadOid ? known?.CheckRollup : null;
            var wasFailureSameHead = previousRollup is "FAILURE" or "ERROR";
            if (!baseline && !wasFailureSameHead)
            {
                var failing = pr.FailingChecks.FirstOrDefault();
                events.Add(new NotificationEvent(
                    NotificationEventType.ChecksFailed,
                    pr.Repo, pr.Number, pr.Title,
                    failing?.DetailsUrl ?? $"{pr.Url}/checks",
                    DedupKey: $"checks:{pr.Id}:{pr.HeadOid ?? "-"}",
                    now,
                    BodyPreview: failing is null ? null : $"Check com falha: {failing.Name}"));
            }

            return true;
        }

        if (rollup == "SUCCESS")
        {
            // Recuperou depois de ter falhado: registra no histórico como lido (canal silencioso).
            if (!baseline && wasNotified)
                resolutionEvents.Add(new NotificationEvent(
                    NotificationEventType.ChecksRecovered,
                    pr.Repo, pr.Number, pr.Title,
                    $"{pr.Url}/checks",
                    DedupKey: $"checks-ok:{pr.Id}:{pr.HeadOid ?? "-"}",
                    now));

            return false;
        }

        // Estados transitórios (PENDING/EXPECTED/null): preserva o rastreamento anterior.
        return wasNotified;
    }

    private static (string Mergeable, bool ConflictNotified) DiffMergeable(
        PrSnapshot pr, PrDbState? known, bool baseline,
        List<NotificationEvent> events, List<NotificationEvent> resolutionEvents, DateTimeOffset now)
    {
        // UNKNOWN é transitório (GitHub recalculando); preserva o último estado definitivo
        // para não perder a transição MERGEABLE→CONFLICTING que passa por UNKNOWN.
        var mergeable = pr.Mergeable == "UNKNOWN"
            ? known?.Mergeable ?? "UNKNOWN"
            : pr.Mergeable;

        var wasConflictNotified = known?.ConflictNotified ?? false;
        var conflictNotified = wasConflictNotified;
        if (mergeable == "MERGEABLE")
        {
            // Conflito resolvido: sai da aba de pendências e vai ao histórico como lido.
            if (!baseline && wasConflictNotified)
                resolutionEvents.Add(new NotificationEvent(
                    NotificationEventType.MergeConflictResolved,
                    pr.Repo, pr.Number, pr.Title, pr.Url,
                    DedupKey: $"conflict-ok:{pr.Id}:{pr.HeadOid ?? "-"}",
                    now));

            conflictNotified = false;
        }

        if (!baseline
            && pr.Mergeable == "CONFLICTING"
            && known?.Mergeable == "MERGEABLE"
            && !conflictNotified)
        {
            conflictNotified = true;
            events.Add(new NotificationEvent(
                NotificationEventType.MergeConflict,
                pr.Repo, pr.Number, pr.Title, pr.Url,
                DedupKey: $"conflict:{pr.Id}:{pr.HeadOid ?? "-"}",
                now));
        }

        return (mergeable, conflictNotified);
    }

    private static bool IsViewer(string? author, string viewerLogin)
        => author is not null && string.Equals(author, viewerLogin, StringComparison.OrdinalIgnoreCase);

    private static bool MentionsViewer(string body, string viewerLogin)
    {
        var index = body.IndexOf($"@{viewerLogin}", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        // Garante fronteira de palavra à direita para não casar @robson com @robson-silva.
        var end = index + viewerLogin.Length + 1;
        return end >= body.Length || !(char.IsLetterOrDigit(body[end]) || body[end] == '-');
    }

    private static string? Truncate(string body)
    {
        var normalized = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
            return null;

        return normalized.Length <= PreviewLength ? normalized : normalized[..PreviewLength] + "…";
    }
}
