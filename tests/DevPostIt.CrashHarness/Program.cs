using DevPostIt.Core;
using DevPostIt.Storage;

var root = args[0];
var id = Guid.Parse(args[1]);
using var lease = new DataDirectoryLease(root);
var repository = new MarkdownNoteRepository(Path.Combine(root, "notes"));
var journal = new FileRecoveryJournal(Path.Combine(root, "recovery"));
await using var service = new NoteService(repository, journal, new SqliteNoteIndex(Path.Combine(root, "index.db")), debounce: TimeSpan.FromHours(1));
await service.OpenAsync(id, default);
await service.ApplyChangeAsync(new(id, 1, "강제 종료 전에 기록한 복구 초안"), default);
await File.WriteAllTextAsync(Path.Combine(root, "ready"), "저널 기록 완료");
await Task.Delay(Timeout.Infinite);

namespace DevPostIt.CrashHarness { public sealed class Marker; }
