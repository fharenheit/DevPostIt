using DevPostIt.Core;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DevPostIt.Storage;

public sealed class SqliteNoteIndex : INoteIndexDeletionWriter
{
    private const int ApplicationId = 0x44504958;
    private const int SchemaVersion = 1;
    private readonly string databasePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public SqliteNoteIndex(string databasePath) => this.databasePath = Path.GetFullPath(databasePath);

    public async Task UpsertAsync(Note note, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(databasePath, cancellationToken);
            using var transaction = connection.BeginTransaction();
            await UpsertAsync(connection, transaction, note, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }
        finally { gate.Release(); }
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(databasePath, cancellationToken);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM notes_fts WHERE rowid=(SELECT rowid FROM notes WHERE id=$id); DELETE FROM notes WHERE id=$id;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<Guid>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(query.Trim());
        if (normalized.Length == 0) return [];
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(databasePath, cancellationToken);
            using var command = connection.CreateCommand();
            const string literalMatch = "(instr(n.title_search,$query)>0 OR instr(n.body_search,$query)>0 OR instr(n.tags_search,$query)>0)";
            if (!normalized.Contains('\0') && normalized.EnumerateRunes().Count() >= 3)
            {
                command.CommandText = $"SELECT n.id FROM notes_fts JOIN notes n ON n.rowid=notes_fts.rowid WHERE notes_fts MATCH $fts AND {literalMatch} ORDER BY bm25(notes_fts,0,5,1,2),n.updated DESC,n.id LIMIT $limit;";
                command.Parameters.AddWithValue("$fts", "\"" + normalized.Replace("\"", "\"\"") + "\"");
            }
            else command.CommandText = $"SELECT n.id FROM notes n WHERE {literalMatch} ORDER BY n.updated DESC,n.id LIMIT $limit;";
            command.Parameters.AddWithValue("$query", normalized);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = new List<Guid>();
            while (await reader.ReadAsync(cancellationToken)) result.Add(Guid.Parse(reader.GetString(0)));
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task RebuildAsync(IAsyncEnumerable<Note> notes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notes);
        await gate.WaitAsync(cancellationToken);
        var staging = databasePath + ".rebuild-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var connection = await OpenAsync(staging, cancellationToken))
            using (var transaction = connection.BeginTransaction())
            {
                var ids = new HashSet<Guid>();
                await foreach (var note in notes.WithCancellation(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ids.Add(note.Id)) throw new DuplicateNoteIdException(note.Id);
                    await UpsertAsync(connection, transaction, note, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                transaction.Commit();
            }
            // 임시 파일을 모두 닫은 뒤에만 기존 인덱스를 교체합니다.
            cancellationToken.ThrowIfCancellationRequested();
            await ProtectReplacementAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, databasePath, true);
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); }
            finally { gate.Release(); }
        }
    }

    private async Task ProtectReplacementAsync(CancellationToken cancellationToken)
    {
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(databasePath + suffix)) throw new IOException("인덱스가 사용 중이거나 복구가 필요합니다. 다른 연결을 닫은 후 다시 시도하세요.");
        if (!File.Exists(databasePath)) return;
        var corrupt = false;
        try
        {
            await using var connection = CreateConnection(databasePath, SqliteOpenMode.ReadOnly);
            await connection.OpenAsync(cancellationToken);
            await ValidateIdentityAsync(connection, cancellationToken);
            using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA quick_check;";
            corrupt = !string.Equals(await check.ExecuteScalarAsync(cancellationToken) as string, "ok", StringComparison.Ordinal);
        }
        catch (SqliteException error) when (error.SqliteErrorCode is 11 or 26) { corrupt = true; }
        if (corrupt)
            File.Copy(databasePath, databasePath + ".corrupt-" + Guid.NewGuid().ToString("N"), false);
    }

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = CreateConnection(path, SqliteOpenMode.ReadWriteCreate);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var version = await ValidateIdentityAsync(connection, cancellationToken);
            using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_keys=ON;";
            await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
            if (version == 0)
            {
                using var transaction = connection.BeginTransaction();
                using var schema = connection.CreateCommand();
                schema.Transaction = transaction;
                schema.CommandText = $"""
                    CREATE TABLE notes (
                        id TEXT PRIMARY KEY NOT NULL,
                        relative_path TEXT NOT NULL, path_key TEXT NOT NULL UNIQUE,
                        title TEXT NOT NULL, body TEXT NOT NULL, tags TEXT NOT NULL,
                        created TEXT NOT NULL, updated TEXT NOT NULL,
                        title_search TEXT NOT NULL, body_search TEXT NOT NULL, tags_search TEXT NOT NULL
                    );
                    CREATE TABLE note_tags (
                        note_id TEXT NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
                        tag TEXT NOT NULL, PRIMARY KEY(note_id,tag)
                    );
                    CREATE VIRTUAL TABLE notes_fts USING fts5(note_id UNINDEXED,title,body,tags,tokenize='trigram');
                    PRAGMA application_id={ApplicationId};
                    PRAGMA user_version={SchemaVersion};
                    """;
                await schema.ExecuteNonQueryAsync(cancellationToken);
                transaction.Commit();
            }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<int> ValidateIdentityAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA application_id;";
        var application = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (application == ApplicationId && version == SchemaVersion) return version;
        if (application == 0 && version == 0)
        {
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 0) return 0;
        }
        throw new InvalidDataException("검색 전용 DB가 아니거나 지원하지 않는 스키마 버전입니다.");
    }

    private static SqliteConnection CreateConnection(string path, SqliteOpenMode mode) => new(new SqliteConnectionStringBuilder
    {
        DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 5
    }.ToString());

    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormC).ToUpperInvariant();

    private static async Task UpsertAsync(SqliteConnection connection, SqliteTransaction transaction, Note note, CancellationToken cancellationToken)
    {
        if (note.Id == Guid.Empty) throw new ArgumentException("빈 ID는 인덱싱할 수 없습니다.", nameof(note));
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO notes(id,relative_path,path_key,title,body,tags,created,updated,title_search,body_search,tags_search)
            VALUES($id,$path,$path_key,$title,$body,$tags,$created,$updated,$ts,$bs,$tags_search)
            ON CONFLICT(id) DO UPDATE SET relative_path=excluded.relative_path,path_key=excluded.path_key,title=excluded.title,
                body=excluded.body,tags=excluded.tags,created=excluded.created,updated=excluded.updated,
                title_search=excluded.title_search,body_search=excluded.body_search,tags_search=excluded.tags_search;
            DELETE FROM note_tags WHERE note_id=$id;
            DELETE FROM notes_fts WHERE rowid=(SELECT rowid FROM notes WHERE id=$id);
            INSERT INTO notes_fts(rowid,note_id,title,body,tags) SELECT rowid,id,$ts,$bs,$tags_search FROM notes WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$id", note.Id.ToString("D"));
        command.Parameters.AddWithValue("$path", note.RelativePath.Replace('\\', '/'));
        command.Parameters.AddWithValue("$path_key", note.RelativePath.Replace('\\', '/').ToUpperInvariant());
        command.Parameters.AddWithValue("$title", note.Title);
        command.Parameters.AddWithValue("$body", note.Markdown);
        command.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(note.Tags));
        command.Parameters.AddWithValue("$created", note.Created.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updated", note.Updated.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ts", Normalize(note.Title));
        command.Parameters.AddWithValue("$bs", Normalize(note.Markdown));
        command.Parameters.AddWithValue("$tags_search", Normalize(string.Join('\n', note.Tags)));
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText = "INSERT INTO note_tags(note_id,tag) VALUES($id,$tag);";
        var tagParameter = command.Parameters.Add("$tag", SqliteType.Text);
        foreach (var tag in note.Tags.Distinct(StringComparer.Ordinal))
        {
            tagParameter.Value = tag;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
