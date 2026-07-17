using DevInbox.Core.GitHub.Models;
using DevInbox.Core.Polling;
using Xunit;

namespace DevInbox.Core.Tests;

public class StateDifferTests
{
    private const string Viewer = "robson";

    [Fact]
    public void FirstPollEver_RecordsStateWithoutEvents()
    {
        var snapshot = Snapshot(mine: [Pr(comments: [Comment("c1", "outra-pessoa")])]);

        var result = StateDiffer.Diff(Input(snapshot, baselineDone: false));

        Assert.Empty(result.Events);
        Assert.Contains(result.NewSeenItems, item => item.ItemId == "c1");
        Assert.Single(result.PrUpserts);
    }

    [Fact]
    public void NewPrAfterBaseline_ExistingCommentsAreSilent()
    {
        var snapshot = Snapshot(mine: [Pr(comments: [Comment("c1", "outra-pessoa")])]);

        var result = StateDiffer.Diff(Input(snapshot));

        Assert.Empty(result.Events);
        Assert.Contains(result.NewSeenItems, item => item.ItemId == "c1");
    }

    [Fact]
    public void NewCommentFromOtherUser_FiresNewComment()
    {
        var snapshot = Snapshot(mine: [Pr(comments: [Comment("c1", "outra-pessoa", "olha isso")])]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known()]));

        var notification = Assert.Single(result.Events);
        Assert.Equal(NotificationEventType.NewComment, notification.Type);
        Assert.Equal("outra-pessoa", notification.Actor);
        Assert.Equal("comment:c1", notification.DedupKey);
    }

    [Fact]
    public void OwnComment_DoesNotFire()
    {
        var snapshot = Snapshot(mine: [Pr(comments: [Comment("c1", Viewer)])]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known()]));

        Assert.Empty(result.Events);
    }

    [Fact]
    public void AlreadySeenComment_DoesNotFire()
    {
        var snapshot = Snapshot(mine: [Pr(comments: [Comment("c1", "outra-pessoa")])]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known()], seen: ["c1"]));

        Assert.Empty(result.Events);
        Assert.Empty(result.NewSeenItems);
    }

    [Fact]
    public void CommentMentioningViewer_FiresMentionedInsteadOfNewComment()
    {
        var snapshot = Snapshot(mine:
            [Pr(comments: [Comment("c1", "outra-pessoa", $"ping @{Viewer} veja isso")])]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known()]));

        var notification = Assert.Single(result.Events);
        Assert.Equal(NotificationEventType.Mentioned, notification.Type);
    }

    [Fact]
    public void CommentMentioningLongerLogin_DoesNotCountAsMention()
    {
        var snapshot = Snapshot(mine:
            [Pr(comments: [Comment("c1", "outra-pessoa", $"cc @{Viewer}-silva")])]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known()]));

        var notification = Assert.Single(result.Events);
        Assert.Equal(NotificationEventType.NewComment, notification.Type);
    }

    [Fact]
    public void ReviewApproved_FiresWithSubtype()
    {
        var snapshot = Snapshot(mine: [Pr(reviews: [Review("r1", "APPROVED", body: "bom trabalho")])]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known()]));

        var notification = Assert.Single(result.Events);
        Assert.Equal(NotificationEventType.ReviewReceived, notification.Type);
        Assert.Equal("APPROVED", notification.Subtype);
    }

    [Fact]
    public void EmptyCommentedReview_IsSuppressed()
    {
        var snapshot = Snapshot(mine: [Pr(reviews: [Review("r1", "COMMENTED", body: "")])]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known()]));

        Assert.Empty(result.Events);
        Assert.Contains(result.NewSeenItems, item => item.ItemId == "r1");
    }

    [Fact]
    public void ThreadResolvedTransition_Fires()
    {
        var snapshot = Snapshot(mine: [Pr(threads: [Thread("t1", isResolved: true)])]);

        var result = StateDiffer.Diff(Input(
            snapshot,
            knownPrs: [Known()],
            seen: ["t1-c1"],
            threads: [new ThreadDbState("t1", "PR_1", IsResolved: false, IParticipated: true)]));

        var notification = Assert.Single(result.Events);
        Assert.Equal(NotificationEventType.ThreadResolved, notification.Type);
        Assert.Equal("resolved:t1", notification.DedupKey);
    }

    [Fact]
    public void UnknownThreadAlreadyResolved_IsSilent()
    {
        var snapshot = Snapshot(mine: [Pr(threads: [Thread("t1", isResolved: true)])]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known()], seen: ["t1-c1"]));

        Assert.Empty(result.Events);
    }

    [Fact]
    public void ThreadParticipation_IsSticky()
    {
        // O snapshot só traz os últimos 5 comentários; a participação registrada antes não pode se perder.
        var snapshot = Snapshot(mine: [Pr(threads: [Thread("t1", isResolved: false, viewerParticipated: false)])]);

        var result = StateDiffer.Diff(Input(
            snapshot,
            knownPrs: [Known()],
            threads: [new ThreadDbState("t1", "PR_1", IsResolved: false, IParticipated: true)]));

        var thread = Assert.Single(result.ThreadUpserts);
        Assert.True(thread.IParticipated);
    }

    [Fact]
    public void ReviewRequestedForNewPr_Fires()
    {
        var snapshot = Snapshot(reviewRequested:
            [new PrRef("PR_9", "org/outro", 9, "Feature X", "https://github.com/org/outro/pull/9", "autor")]);

        var result = StateDiffer.Diff(Input(snapshot));

        var notification = Assert.Single(result.Events);
        Assert.Equal(NotificationEventType.ReviewRequested, notification.Type);
        Assert.Equal("revreq:PR_9", notification.DedupKey);
    }

    [Fact]
    public void ReviewRequestedOnFirstPollEver_IsSilent()
    {
        var snapshot = Snapshot(reviewRequested:
            [new PrRef("PR_9", "org/outro", 9, "Feature X", "url", "autor")]);

        var result = StateDiffer.Diff(Input(snapshot, baselineDone: false));

        Assert.Empty(result.Events);
        Assert.Single(result.PrUpserts);
    }

    [Fact]
    public void ReviewRequestedAlreadyKnown_DoesNotFireAgain()
    {
        var snapshot = Snapshot(reviewRequested:
            [new PrRef("PR_9", "org/outro", 9, "Feature X", "url", "autor")]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs:
            [Known(id: "PR_9", isMine: false, isReviewRequested: true)]));

        Assert.Empty(result.Events);
    }

    [Fact]
    public void ChecksTransitionToFailure_Fires()
    {
        var snapshot = Snapshot(mine: [Pr(rollup: "FAILURE",
            failing: [new CheckContextInfo("build", "https://ci.example/1")])]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known(rollup: "PENDING")]));

        var notification = Assert.Single(result.Events);
        Assert.Equal(NotificationEventType.ChecksFailed, notification.Type);
        Assert.Equal("https://ci.example/1", notification.Url);
    }

    [Fact]
    public void ChecksStillFailing_DoesNotFireAgain()
    {
        var snapshot = Snapshot(mine: [Pr(rollup: "FAILURE")]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known(rollup: "FAILURE")]));

        Assert.Empty(result.Events);
    }

    [Fact]
    public void ChecksFailureOnNewHead_FiresAgain()
    {
        var snapshot = Snapshot(mine: [Pr(headOid: "oid2", rollup: "FAILURE")]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known(headOid: "oid1", rollup: "FAILURE")]));

        var notification = Assert.Single(result.Events);
        Assert.Equal("checks:PR_1:oid2", notification.DedupKey);
    }

    [Fact]
    public void ConflictTransitionFromMergeable_FiresOnce()
    {
        var snapshot = Snapshot(mine: [Pr(mergeable: "CONFLICTING")]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known(mergeable: "MERGEABLE")]));

        var notification = Assert.Single(result.Events);
        Assert.Equal(NotificationEventType.MergeConflict, notification.Type);
        Assert.True(Assert.Single(result.PrUpserts).ConflictNotified);
    }

    [Fact]
    public void ConflictWhileAlreadyNotified_DoesNotFire()
    {
        var snapshot = Snapshot(mine: [Pr(mergeable: "CONFLICTING")]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs:
            [Known(mergeable: "CONFLICTING", conflictNotified: true)]));

        Assert.Empty(result.Events);
    }

    [Fact]
    public void ConflictFromUnknownState_DoesNotFire()
    {
        var snapshot = Snapshot(mine: [Pr(mergeable: "CONFLICTING")]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known(mergeable: "UNKNOWN")]));

        Assert.Empty(result.Events);
    }

    [Fact]
    public void UnknownMergeable_PreservesLastDefinitiveState()
    {
        // MERGEABLE→UNKNOWN→CONFLICTING não pode perder a transição por causa do estado transitório.
        var snapshot = Snapshot(mine: [Pr(mergeable: "UNKNOWN")]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs: [Known(mergeable: "MERGEABLE")]));

        Assert.Equal("MERGEABLE", Assert.Single(result.PrUpserts).Mergeable);
    }

    [Fact]
    public void BackToMergeable_ResetsConflictNotified()
    {
        var snapshot = Snapshot(mine: [Pr(mergeable: "MERGEABLE")]);

        var result = StateDiffer.Diff(Input(snapshot, knownPrs:
            [Known(mergeable: "CONFLICTING", conflictNotified: true)]));

        Assert.Empty(result.Events);
        Assert.False(Assert.Single(result.PrUpserts).ConflictNotified);
    }

    [Fact]
    public void PrNoLongerVisible_IsMarkedClosed()
    {
        var result = StateDiffer.Diff(Input(Snapshot(), knownPrs: [Known()]));

        Assert.Equal("PR_1", Assert.Single(result.ClosedPrIds));
    }

    private static DiffInput Input(
        PollSnapshot snapshot,
        bool baselineDone = true,
        IEnumerable<PrDbState>? knownPrs = null,
        IEnumerable<string>? seen = null,
        IEnumerable<ThreadDbState>? threads = null)
        => new(
            Viewer,
            baselineDone,
            (knownPrs ?? []).ToDictionary(pr => pr.PrId),
            (seen ?? []).ToHashSet(),
            (threads ?? []).ToDictionary(thread => thread.ThreadId),
            snapshot,
            DateTimeOffset.UtcNow);

    private static PollSnapshot Snapshot(
        IReadOnlyList<PrSnapshot>? mine = null,
        IReadOnlyList<PrRef>? reviewRequested = null)
        => new(Viewer, null, mine ?? [], reviewRequested ?? []);

    private static PrSnapshot Pr(
        string id = "PR_1",
        string mergeable = "MERGEABLE",
        string? headOid = "oid1",
        string? rollup = null,
        IReadOnlyList<CheckContextInfo>? failing = null,
        IReadOnlyList<CommentInfo>? comments = null,
        IReadOnlyList<ReviewInfo>? reviews = null,
        IReadOnlyList<ThreadInfo>? threads = null)
        => new(id, "org/repo", 1, "Título", "https://github.com/org/repo/pull/1", false,
            mergeable, headOid, rollup, failing ?? [], comments ?? [], reviews ?? [], threads ?? []);

    private static PrDbState Known(
        string id = "PR_1",
        bool isMine = true,
        bool isReviewRequested = false,
        string mergeable = "MERGEABLE",
        string? headOid = "oid1",
        string? rollup = null,
        bool conflictNotified = false)
        => new(id, "org/repo", 1, "Título", "url", isMine, isReviewRequested,
            mergeable, headOid, rollup, conflictNotified, IsOpen: true);

    private static CommentInfo Comment(string id, string author, string body = "corpo do comentário")
        => new(id, $"https://github.com/org/repo/pull/1#issuecomment-{id}", author, body, DateTimeOffset.UtcNow);

    private static ReviewInfo Review(string id, string state, string body)
        => new(id, $"https://github.com/org/repo/pull/1#pullrequestreview-{id}", "revisora", state, body, DateTimeOffset.UtcNow);

    private static ThreadInfo Thread(string id, bool isResolved, bool viewerParticipated = true)
        => new(id, isResolved, viewerParticipated, [Comment($"{id}-c1", "revisora")]);
}
