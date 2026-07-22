using DevInbox.Core.Polling;
using Microsoft.Data.Sqlite;

namespace DevInbox.Core.Storage;

public sealed record DiffDbState(
    bool BaselineDone,
    IReadOnlyDictionary<string, PrDbState> KnownPrs,
    IReadOnlySet<string> SeenItemIds,
    IReadOnlyDictionary<string, ThreadDbState> KnownThreads);

public enum PendingKind
{
    Conversation,
    Conflict,
    Checks,
}

public sealed record PendingItem(
    PendingKind Kind,
    string Repo,
    int PrNumber,
    string PrTitle,
    string? Detail,
    string? Url,
    string? Author);

public sealed class PrStateRepository(Database database)
{
    private const string BaselineDoneKey = "baseline_done";

    private readonly Database _database = database;

    public DiffDbState LoadDiffState()
    {
        using var connection = _database.OpenConnection();

        var prs = new Dictionary<string, PrDbState>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT pr_id, repo, number, title, url, is_mine, is_review_requested,
                       mergeable, head_oid, check_rollup, conflict_notified, checks_failed_notified, is_open
                FROM pr_state
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var state = new PrDbState(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    reader.IsDBNull(4) ? "" : reader.GetString(4),
                    reader.GetInt64(5) != 0,
                    reader.GetInt64(6) != 0,
                    reader.IsDBNull(7) ? "UNKNOWN" : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt64(10) != 0,
                    reader.GetInt64(11) != 0,
                    reader.GetInt64(12) != 0);
                prs[state.PrId] = state;
            }
        }

        var seenIds = new HashSet<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT item_id FROM seen_items";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                seenIds.Add(reader.GetString(0));
        }

        var threads = new Dictionary<string, ThreadDbState>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT thread_id, pr_id, is_resolved, i_participated, url, preview, author FROM thread_state";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var state = new ThreadDbState(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2) != 0,
                    reader.GetInt64(3) != 0,
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6));
                threads[state.ThreadId] = state;
            }
        }

        return new DiffDbState(GetKv(connection, BaselineDoneKey) == "1", prs, seenIds, threads);
    }

    public void ApplyDiff(DiffResult result, DateTimeOffset now)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var pr in result.PrUpserts)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO pr_state
                  (pr_id, repo, number, title, url, is_mine, is_review_requested,
                   mergeable, head_oid, check_rollup, conflict_notified, checks_failed_notified, is_open, closed_at)
                VALUES (@prId, @repo, @number, @title, @url, @isMine, @isReviewRequested,
                        @mergeable, @headOid, @checkRollup, @conflictNotified, @checksFailedNotified, 1, NULL)
                ON CONFLICT(pr_id) DO UPDATE SET
                  repo = excluded.repo,
                  number = excluded.number,
                  title = excluded.title,
                  url = excluded.url,
                  is_mine = excluded.is_mine,
                  is_review_requested = excluded.is_review_requested,
                  mergeable = excluded.mergeable,
                  head_oid = excluded.head_oid,
                  check_rollup = excluded.check_rollup,
                  conflict_notified = excluded.conflict_notified,
                  checks_failed_notified = excluded.checks_failed_notified,
                  is_open = 1,
                  closed_at = NULL
                """;
            command.Parameters.AddWithValue("@prId", pr.PrId);
            command.Parameters.AddWithValue("@repo", pr.Repo);
            command.Parameters.AddWithValue("@number", pr.Number);
            command.Parameters.AddWithValue("@title", pr.Title);
            command.Parameters.AddWithValue("@url", pr.Url);
            command.Parameters.AddWithValue("@isMine", pr.IsMine ? 1 : 0);
            command.Parameters.AddWithValue("@isReviewRequested", pr.IsReviewRequested ? 1 : 0);
            command.Parameters.AddWithValue("@mergeable", pr.Mergeable);
            command.Parameters.AddWithValue("@headOid", (object?)pr.HeadOid ?? DBNull.Value);
            command.Parameters.AddWithValue("@checkRollup", (object?)pr.CheckRollup ?? DBNull.Value);
            command.Parameters.AddWithValue("@conflictNotified", pr.ConflictNotified ? 1 : 0);
            command.Parameters.AddWithValue("@checksFailedNotified", pr.ChecksFailedNotified ? 1 : 0);
            command.ExecuteNonQuery();
        }

        foreach (var item in result.NewSeenItems)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO seen_items (item_id, pr_id, kind, seen_at)
                VALUES (@itemId, @prId, @kind, @seenAt)
                """;
            command.Parameters.AddWithValue("@itemId", item.ItemId);
            command.Parameters.AddWithValue("@prId", item.PrId);
            command.Parameters.AddWithValue("@kind", item.Kind);
            command.Parameters.AddWithValue("@seenAt", now.ToString("O"));
            command.ExecuteNonQuery();
        }

        foreach (var thread in result.ThreadUpserts)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO thread_state (thread_id, pr_id, is_resolved, i_participated, url, preview, author)
                VALUES (@threadId, @prId, @isResolved, @iParticipated, @url, @preview, @author)
                ON CONFLICT(thread_id) DO UPDATE SET
                  is_resolved = excluded.is_resolved,
                  i_participated = excluded.i_participated,
                  url = excluded.url,
                  preview = excluded.preview,
                  author = excluded.author
                """;
            command.Parameters.AddWithValue("@threadId", thread.ThreadId);
            command.Parameters.AddWithValue("@prId", thread.PrId);
            command.Parameters.AddWithValue("@isResolved", thread.IsResolved ? 1 : 0);
            command.Parameters.AddWithValue("@iParticipated", thread.IParticipated ? 1 : 0);
            command.Parameters.AddWithValue("@url", (object?)thread.Url ?? DBNull.Value);
            command.Parameters.AddWithValue("@preview", (object?)thread.Preview ?? DBNull.Value);
            command.Parameters.AddWithValue("@author", (object?)thread.Author ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        if (result.ClosedPrIds.Count > 0)
        {
            foreach (var prId in result.ClosedPrIds)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE pr_state
                    SET is_open = 0, is_review_requested = 0, closed_at = @closedAt
                    WHERE pr_id = @prId AND is_open = 1
                    """;
                command.Parameters.AddWithValue("@prId", prId);
                command.Parameters.AddWithValue("@closedAt", now.ToString("O"));
                command.ExecuteNonQuery();
            }
        }

        SetKv(connection, transaction, BaselineDoneKey, "1");
        transaction.Commit();
    }

    public void PruneClosedPrs(DateTimeOffset olderThan)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM seen_items WHERE pr_id IN
              (SELECT pr_id FROM pr_state WHERE is_open = 0 AND closed_at < @cutoff);
            DELETE FROM thread_state WHERE pr_id IN
              (SELECT pr_id FROM pr_state WHERE is_open = 0 AND closed_at < @cutoff);
            DELETE FROM pr_state WHERE is_open = 0 AND closed_at < @cutoff;
            """;
        command.Parameters.AddWithValue("@cutoff", olderThan.ToString("O"));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Itens que exigem ação minha, derivados do estado atual dos PRs: conversas de review não
    /// resolvidas, conflito de merge e checks/CI em falha. Uma entrada por motivo.
    /// </summary>
    public IReadOnlyList<PendingItem> GetPendingItems()
    {
        using var connection = _database.OpenConnection();
        var items = new List<PendingItem>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT p.repo, p.number, COALESCE(p.title, ''), t.url, t.preview, t.author
                FROM thread_state t
                JOIN pr_state p ON p.pr_id = t.pr_id
                WHERE t.is_resolved = 0 AND p.is_open = 1 AND (p.is_mine = 1 OR t.i_participated = 1)
                ORDER BY p.repo, p.number DESC
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                items.Add(new PendingItem(
                    PendingKind.Conversation,
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT repo, number, COALESCE(title, ''), url
                FROM pr_state
                WHERE is_open = 1 AND is_mine = 1 AND mergeable = 'CONFLICTING'
                ORDER BY repo, number DESC
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                items.Add(new PendingItem(
                    PendingKind.Conflict,
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    "Conflito com a base",
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    Author: null));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT repo, number, COALESCE(title, ''), url
                FROM pr_state
                WHERE is_open = 1 AND is_mine = 1 AND check_rollup IN ('FAILURE', 'ERROR')
                ORDER BY repo, number DESC
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var url = reader.IsDBNull(3) ? null : reader.GetString(3);
                items.Add(new PendingItem(
                    PendingKind.Checks,
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    "CI/checks falhando",
                    url is null ? null : $"{url}/checks",
                    Author: null));
            }
        }

        return items;
    }

    public bool IsTrackedOpenPr(string repo, int number)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM pr_state WHERE repo = @repo AND number = @number AND is_open = 1)
            """;
        command.Parameters.AddWithValue("@repo", repo);
        command.Parameters.AddWithValue("@number", number);
        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }

    public bool HasSeenItem(string itemId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM seen_items WHERE item_id = @itemId)";
        command.Parameters.AddWithValue("@itemId", itemId);
        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }

    public void AddSeenItem(SeenItem item, DateTimeOffset now)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO seen_items (item_id, pr_id, kind, seen_at)
            VALUES (@itemId, @prId, @kind, @seenAt)
            """;
        command.Parameters.AddWithValue("@itemId", item.ItemId);
        command.Parameters.AddWithValue("@prId", item.PrId);
        command.Parameters.AddWithValue("@kind", item.Kind);
        command.Parameters.AddWithValue("@seenAt", now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public string? GetKv(string key)
    {
        using var connection = _database.OpenConnection();
        return GetKv(connection, key);
    }

    public void SetKv(string key, string value)
    {
        using var connection = _database.OpenConnection();
        SetKv(connection, transaction: null, key, value);
    }

    private static string? GetKv(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM kv WHERE key = @key";
        command.Parameters.AddWithValue("@key", key);
        return command.ExecuteScalar() as string;
    }

    private static void SetKv(SqliteConnection connection, SqliteTransaction? transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO kv (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }
}
