using DevInbox.Core.Polling;

namespace DevInbox.Core.Storage;

public sealed record HistoryItem(
    long Id,
    string EventType,
    string? Subtype,
    string Repo,
    int PrNumber,
    string Title,
    string? BodyPreview,
    string? Actor,
    string Url,
    DateTimeOffset CreatedAt,
    bool IsRead);

public sealed class NotificationRepository(Database database)
{
    private readonly Database _database = database;

    /// <summary>Disparado após qualquer mutação — badge da bandeja e janela de histórico se atualizam por aqui.</summary>
    public event Action? HistoryChanged;

    public bool TryInsert(NotificationEvent notification, out long id)
    {
        using var connection = _database.OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT OR IGNORE INTO notifications
                  (event_type, subtype, repo, pr_number, title, body_preview, actor_login, url, created_at, dedup_key)
                VALUES (@eventType, @subtype, @repo, @prNumber, @title, @bodyPreview, @actor, @url, @createdAt, @dedupKey)
                """;
            command.Parameters.AddWithValue("@eventType", notification.Type.ToString());
            command.Parameters.AddWithValue("@subtype", (object?)notification.Subtype ?? DBNull.Value);
            command.Parameters.AddWithValue("@repo", notification.Repo);
            command.Parameters.AddWithValue("@prNumber", notification.PrNumber);
            command.Parameters.AddWithValue("@title", notification.PrTitle);
            command.Parameters.AddWithValue("@bodyPreview", (object?)notification.BodyPreview ?? DBNull.Value);
            command.Parameters.AddWithValue("@actor", (object?)notification.Actor ?? DBNull.Value);
            command.Parameters.AddWithValue("@url", notification.Url);
            command.Parameters.AddWithValue("@createdAt", notification.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("@dedupKey", notification.DedupKey);

            if (command.ExecuteNonQuery() == 0)
            {
                id = 0;
                return false;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT last_insert_rowid()";
            id = Convert.ToInt64(command.ExecuteScalar());
        }

        HistoryChanged?.Invoke();
        return true;
    }

    public IReadOnlyList<HistoryItem> GetHistory(int limit)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, event_type, subtype, repo, pr_number, title, body_preview, actor_login, url, created_at, is_read
            FROM notifications
            ORDER BY id DESC
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@limit", limit);

        var items = new List<HistoryItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new HistoryItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9)),
                reader.GetInt64(10) != 0));
        }

        return items;
    }

    public int UnreadCount()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM notifications WHERE is_read = 0";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void MarkRead(long id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE notifications SET is_read = 1 WHERE id = @id AND is_read = 0";
        command.Parameters.AddWithValue("@id", id);
        if (command.ExecuteNonQuery() > 0)
            HistoryChanged?.Invoke();
    }

    public void MarkAllRead()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE notifications SET is_read = 1 WHERE is_read = 0";
        if (command.ExecuteNonQuery() > 0)
            HistoryChanged?.Invoke();
    }

    public void Prune(int maxItems)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM notifications
            WHERE id NOT IN (SELECT id FROM notifications ORDER BY id DESC LIMIT @maxItems)
            """;
        command.Parameters.AddWithValue("@maxItems", maxItems);
        command.ExecuteNonQuery();
    }
}
