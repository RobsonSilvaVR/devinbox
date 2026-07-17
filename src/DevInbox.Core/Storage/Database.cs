using Microsoft.Data.Sqlite;

namespace DevInbox.Core.Storage;

public sealed class Database
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS kv (
          key TEXT PRIMARY KEY,
          value TEXT);

        CREATE TABLE IF NOT EXISTS pr_state (
          pr_id TEXT PRIMARY KEY,
          repo TEXT NOT NULL,
          number INTEGER NOT NULL,
          title TEXT,
          url TEXT,
          is_mine INTEGER NOT NULL,
          is_review_requested INTEGER NOT NULL DEFAULT 0,
          mergeable TEXT,
          head_oid TEXT,
          check_rollup TEXT,
          conflict_notified INTEGER NOT NULL DEFAULT 0,
          is_open INTEGER NOT NULL DEFAULT 1,
          closed_at TEXT);

        CREATE TABLE IF NOT EXISTS seen_items (
          item_id TEXT PRIMARY KEY,
          pr_id TEXT NOT NULL,
          kind TEXT NOT NULL,
          seen_at TEXT NOT NULL);

        CREATE TABLE IF NOT EXISTS thread_state (
          thread_id TEXT PRIMARY KEY,
          pr_id TEXT NOT NULL,
          is_resolved INTEGER NOT NULL,
          i_participated INTEGER NOT NULL DEFAULT 0,
          url TEXT,
          preview TEXT,
          author TEXT);

        CREATE TABLE IF NOT EXISTS notifications (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          event_type TEXT NOT NULL,
          subtype TEXT,
          repo TEXT NOT NULL,
          pr_number INTEGER NOT NULL,
          title TEXT NOT NULL,
          body_preview TEXT,
          actor_login TEXT,
          url TEXT NOT NULL,
          created_at TEXT NOT NULL,
          is_read INTEGER NOT NULL DEFAULT 0,
          dedup_key TEXT NOT NULL UNIQUE);

        CREATE INDEX IF NOT EXISTS ix_notifications_unread ON notifications(is_read);
        """;

    private const int CurrentSchemaVersion = 3;

    public Database(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

        using var connection = OpenConnection();
        Execute(connection, "PRAGMA journal_mode=WAL;" + Schema);
        Migrate(connection);
    }

    private static void Migrate(SqliteConnection connection)
    {
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (version >= CurrentSchemaVersion)
            return;

        if (version < 2)
        {
            // Bancos da v1 não tinham url/preview em thread_state (lista de conversas pendentes).
            TryExecute(connection, "ALTER TABLE thread_state ADD COLUMN url TEXT");
            TryExecute(connection, "ALTER TABLE thread_state ADD COLUMN preview TEXT");
        }

        if (version < 3)
        {
            // Autor da conversa, exibido na aba de pendentes; preenchido no próximo poll.
            TryExecute(connection, "ALTER TABLE thread_state ADD COLUMN author TEXT");
        }

        Execute(connection, $"PRAGMA user_version = {CurrentSchemaVersion}");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void TryExecute(SqliteConnection connection, string sql)
    {
        try
        {
            Execute(connection, sql);
        }
        catch (SqliteException)
        {
            // Coluna já existe (banco recém-criado com o schema completo).
        }
    }

    public string ConnectionString { get; }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }
}
