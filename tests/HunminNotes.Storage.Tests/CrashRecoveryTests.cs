using System.Diagnostics;
using System.Runtime.InteropServices;
using HunminNotes.Core;

namespace HunminNotes.Storage.Tests;

public sealed class CrashRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DevPostItCrashTests", Guid.NewGuid().ToString("N"));
    public CrashRecoveryTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task KilledProcessLeavesOriginalFileAndRestartRecoversJournaledDraft()
    {
        var repository = new MarkdownNoteRepository(Path.Combine(root, "notes"));
        var note = Note.Create("note.md", "크래시 시험", "원본 파일", []);
        await repository.SaveAsync(note, default);
        var dotnetRoot = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../.."));
        var start = new ProcessStartInfo(Path.Combine(dotnetRoot, "dotnet.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(HunminNotes.CrashHarness.Marker).Assembly.Location);
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(note.Id.ToString());
        start.Environment["DOTNET_ROOT"] = dotnetRoot;
        using var child = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(Path.Combine(root, "ready")))
            {
                if (child.HasExited) throw new InvalidOperationException(await child.StandardError.ReadToEndAsync());
                await Task.Delay(20, timeout.Token);
            }
            Assert.ThrowsAny<IOException>(() => new DataDirectoryLease(root));
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(timeout.Token);
            Assert.Equal("원본 파일", (await repository.GetAsync(note.Id, default))!.Markdown);
            using var lease = new DataDirectoryLease(root);
            await using var restarted = new NoteService(repository, new FileRecoveryJournal(Path.Combine(root, "recovery")), new SqliteNoteIndex(Path.Combine(root, "index.db")));
            var state = await restarted.OpenAsync(note.Id, default);
            Assert.True(state.RecoveryPending);
            Assert.Equal("강제 종료 전에 기록한 복구 초안", state.Note.Markdown);
            await restarted.ResolveRecoveryAsync(note.Id, true, default);
            await restarted.FlushAsync(note.Id, default);
            Assert.Equal("강제 종료 전에 기록한 복구 초안", (await repository.GetAsync(note.Id, default))!.Markdown);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "recovery"), "*.json"));
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }
    public void Dispose() => Directory.Delete(root, true);
}
