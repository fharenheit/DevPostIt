using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DevPostIt.Storage.Tests;

public sealed class SqliteTokenizerBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public void CompareKoreanSubstringsAcrossTenThousandNotes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "CREATE TABLE documents(text TEXT); CREATE VIRTUAL TABLE words USING fts5(text, tokenize='unicode61'); CREATE VIRTUAL TABLE trigrams USING fts5(text, tokenize='trigram');";
            schema.ExecuteNonQuery();
        }
        var build = Stopwatch.StartNew();
        using (var transaction = connection.BeginTransaction())
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO documents VALUES($text); INSERT INTO words VALUES($text); INSERT INTO trigrams VALUES($text);";
            var parameter = insert.Parameters.Add("$text", SqliteType.Text);
            for (var i = 0; i < 10_000; i++)
            {
                parameter.Value = $"개발자회의록 {i} 한글검색인덱스구현";
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        output.WriteLine($"SQLite {connection.ServerVersion}, 문서 10000개, 3개 테이블 구축 {build.Elapsed.TotalMilliseconds:F1}ms");
        foreach (var query in new[] { "개발자회의록", "발자회", "인덱스", "회의", "의" })
        {
            foreach (var engine in new[] { "words", "trigrams", "scan" })
            {
                using var search = connection.CreateCommand();
                search.CommandText = engine == "scan" ? "SELECT count(*) FROM documents WHERE instr(text,$q)>0;" : $"SELECT count(*) FROM {engine} WHERE {engine} MATCH $q;";
                search.Parameters.AddWithValue("$q", engine == "scan" ? query : $"\"{query}\"");
                var count = Convert.ToInt32(search.ExecuteScalar());
                var timer = Stopwatch.StartNew();
                for (var i = 0; i < 20; i++) search.ExecuteScalar();
                output.WriteLine($"{engine,-8} '{query}' 일치 {count,5}, 평균 {timer.Elapsed.TotalMilliseconds / 20:F3}ms");
                Assert.Equal(engine == "scan" || engine == "trigrams" && query.Length >= 3 || engine == "words" && query == "개발자회의록" ? 10_000 : 0, count);
            }
        }
    }
}
