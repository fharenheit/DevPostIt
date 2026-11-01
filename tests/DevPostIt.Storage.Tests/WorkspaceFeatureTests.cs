using DevPostIt.Core;

namespace DevPostIt.Storage.Tests;

public sealed class WorkspaceFeatureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "devpostit-features-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private string NotePath(Note note) => Path.Combine(root, "notes", note.RelativePath);

    private static string WithBody(string fileText, string body) => fileText[..(fileText.IndexOf("\n---", 4, StringComparison.Ordinal) + 4)] + "\n" + body;

    [Fact]
    public async Task TrashKeepsLatestDraftAndRestoresWithSameId()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);
        await workspace.Service.ApplyChangeAsync(new(note.Id, 1, "저장 전 입력", "이름"), default);

        await workspace.TrashAsync(note.Id, default);

        Assert.DoesNotContain(workspace.Notes, item => item.Id == note.Id);
        Assert.False(File.Exists(NotePath(note)));
        Assert.Equal("이름", workspace.TrashedNotes.Single().Title);

        var restored = await workspace.RestoreAsync(note.Id, default);

        Assert.Equal(note.Id, restored.Id);
        Assert.Equal("저장 전 입력", (await workspace.OpenNoteAsync(note.Id, default)).Note.Markdown);
        Assert.Empty(workspace.TrashedNotes);
        Assert.True(File.Exists(NotePath(note)));
    }

    [Fact]
    public async Task RestoreUsesNewPathWhenOriginalIsTaken()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.TrashAsync(note.Id, default);
        Directory.CreateDirectory(Path.GetDirectoryName(NotePath(note))!);
        await File.WriteAllTextAsync(NotePath(note), "다른 사람이 만든 파일");

        var restored = await workspace.RestoreAsync(note.Id, default);

        Assert.NotEqual(note.RelativePath, restored.RelativePath);
        Assert.Equal("다른 사람이 만든 파일", await File.ReadAllTextAsync(NotePath(note)));
    }

    [Fact]
    public async Task FailedTrashKeepsNoteAndLeavesNoCopy()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);
        using (new FileStream(NotePath(note), FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAnyAsync<IOException>(() => workspace.TrashAsync(note.Id, default));

        Assert.Contains(workspace.Notes, item => item.Id == note.Id);
        Assert.Empty(workspace.TrashedNotes);
        Assert.True(File.Exists(NotePath(note)));
    }

    [Fact]
    public async Task PurgeRemovesTrashAndImages()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.SaveImageAsync(note.Id, [1, 2, 3], default);
        await workspace.TrashAsync(note.Id, default);
        await workspace.EmptyTrashAsync(default);
        Assert.Empty(workspace.TrashedNotes);
        Assert.False(Directory.Exists(Path.Combine(workspace.AssetsRoot, note.Id.ToString("N"))));
    }

    [Fact]
    public async Task SavedImageReferenceIsRelativeToNoteFile()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        var markdown = await workspace.SaveImageAsync(note.Id, [1, 2, 3], default);
        Assert.Matches($"^!\\[이미지\\]\\(\\.\\./assets/{note.Id:N}/[0-9a-f]{{32}}\\.png\\)$", markdown);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(workspace.AssetsRoot, note.Id.ToString("N"))));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(file));
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveImageAsync(note.Id, [], default));
    }

    [Fact]
    public async Task SearchFindsNotesThatWereNeverOpened()
    {
        Guid id;
        await using (var first = await NoteWorkspace.OpenAsync(root, default))
        {
            id = (await first.CreateAsync(default)).Id;
            await first.Service.ApplyChangeAsync(new(id, 1, "앞부분 찾을한글단어 뒷부분", "검색 대상"), default);
            await first.Service.FlushAsync(id, default);
        }
        File.Delete(Path.Combine(root, ".hunmin", "note-index.db"));
        await using var second = await NoteWorkspace.OpenAsync(root, default);
        var result = Assert.Single(await second.SearchAsync("찾을한글단어", 10, default));
        Assert.Equal(id, result.Note.Id);
        Assert.Contains("찾을한글단어", result.Excerpt);
        Assert.Empty(await second.SearchAsync("없는단어입니다", 10, default));
        Assert.Equal(2, (await second.SearchAsync("  ", 10, default)).Length);
    }

    [Fact]
    public async Task TrashedNotesDisappearFromSearch()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        Assert.Single(await workspace.SearchAsync("아이디어", 10, default));
        await workspace.TrashAsync(note.Id, default);
        Assert.Empty(await workspace.SearchAsync("아이디어", 10, default));
    }

    [Fact]
    public async Task ExternalChangeReloadsCleanOpenNote()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        var before = await workspace.OpenNoteAsync(note.Id, default);
        ExternalChange? seen = null;
        workspace.Service.ExternalChanged += change => seen = change;
        await File.WriteAllTextAsync(NotePath(note), WithBody(await File.ReadAllTextAsync(NotePath(note)), "외부 편집기 내용"));

        await workspace.SyncExternalChangesAsync(default);

        Assert.Equal(ExternalChangeKind.Reloaded, seen?.Kind);
        var state = await workspace.Service.GetStateAsync(note.Id, default);
        Assert.Equal("외부 편집기 내용", state.Note.Markdown);
        Assert.True(state.Revision > before.Revision);
        Assert.False(state.HasUnsavedChanges);
        Assert.Single(await workspace.SearchAsync("외부 편집기", 10, default));
    }

    [Fact]
    public async Task ExternalChangeDuringEditingKeepsBothVersions()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);
        await workspace.Service.ApplyChangeAsync(new(note.Id, 1, "앱에서 입력한 내용"), default);
        await File.WriteAllTextAsync(NotePath(note), WithBody(await File.ReadAllTextAsync(NotePath(note)), "외부 편집기 내용"));
        ExternalChange? seen = null;
        workspace.Service.ExternalChanged += change => seen = change;

        await workspace.SyncExternalChangesAsync(default);
        await workspace.Service.FlushAsync(note.Id, default);

        Assert.Equal(ExternalChangeKind.ConflictPreserved, seen?.Kind);
        Assert.Contains("(외부 변경)", seen!.Message);
        var copy = Assert.Single(workspace.Notes, item => item.Id != note.Id);
        Assert.Equal("외부 편집기 내용", copy.Markdown);
        Assert.EndsWith("(외부 변경)", copy.Title);
        Assert.Equal("앱에서 입력한 내용", (await workspace.Service.GetStateAsync(note.Id, default)).Note.Markdown);
        Assert.Contains("앱에서 입력한 내용", await File.ReadAllTextAsync(NotePath(note)));
    }

    [Fact]
    public async Task OwnSavesAreNotTreatedAsExternalChanges()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);
        await workspace.Service.ApplyChangeAsync(new(note.Id, 1, "내 저장"), default);
        await workspace.Service.FlushAsync(note.Id, default);
        var changed = 0;
        workspace.Service.ExternalChanged += _ => changed++;
        await workspace.SyncExternalChangesAsync(default);
        Assert.Equal(0, changed);
        Assert.Single(workspace.Notes);
    }

    [Fact]
    public async Task ExternalDeletionOfOpenNoteKeepsDraft()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var open = workspace.Notes.Single();
        await workspace.OpenNoteAsync(open.Id, default);
        await workspace.Service.ApplyChangeAsync(new(open.Id, 1, "보존할 입력"), default);
        ExternalChange? seen = null;
        workspace.Service.ExternalChanged += change => seen = change;
        File.Delete(NotePath(open));

        await workspace.SyncExternalChangesAsync(default);

        Assert.Equal(ExternalChangeKind.Missing, seen?.Kind);
        Assert.Equal("보존할 입력", (await workspace.Service.GetStateAsync(open.Id, default)).Note.Markdown);
    }

    [Fact]
    public async Task NewExternalNoteAppearsInList()
    {
        Note template;
        await using (var workspace = await NoteWorkspace.OpenAsync(root, default)) template = workspace.Notes.Single();
        var text = await File.ReadAllTextAsync(NotePath(template));
        var id = Guid.NewGuid();
        await using var reopened = await NoteWorkspace.OpenAsync(root, default);
        await File.WriteAllTextAsync(Path.Combine(root, "notes", "Inbox", "외부.md"), text.Replace(template.Id.ToString(), id.ToString()));
        await reopened.SyncExternalChangesAsync(default);
        Assert.Contains(reopened.Notes, note => note.Id == id);
    }

    [Fact]
    public async Task WatcherPicksUpExternalChangesOnItsOwn()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);
        workspace.StartWatching();
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Service.ExternalChanged += _ => reloaded.TrySetResult();
        await File.WriteAllTextAsync(NotePath(note), WithBody(await File.ReadAllTextAsync(NotePath(note)), "감시로 감지"));
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("감시로 감지", (await workspace.Service.GetStateAsync(note.Id, default)).Note.Markdown);
    }

    [Fact]
    public void AppSettingsSurviveRestartAndCorruption()
    {
        var path = Path.Combine(root, "settings", "app-settings.json");
        var id = Guid.NewGuid();
        var store = new AppSettingsStore(path);
        store.Current.StartInTray = true;
        store.Current.OpenNotes = [id];
        store.Note(id).Left = 12.5;
        store.Note(id).Color = "blue";
        store.Note(id).Topmost = true;
        store.Save();

        var again = new AppSettingsStore(path);
        Assert.True(again.Current.StartInTray);
        Assert.Equal(id, Assert.Single(again.Current.OpenNotes!));
        Assert.Equal(12.5, again.Note(id).Left);
        Assert.Equal("blue", again.Note(id).Color);
        Assert.True(again.Note(id).Topmost);

        File.WriteAllText(path, "{ 손상");
        Assert.False(new AppSettingsStore(path).Current.StartInTray);
    }

    [Fact]
    public void MigrationCopiesIntoEmptyChosenFolderAndKeepsFilledOne()
    {
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "notes"));
        File.WriteAllText(Path.Combine(source, "notes", "a.md"), "본문");
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        Assert.True(NoteDataLocation.MigrateLegacy(source, empty));
        Assert.Equal("본문", File.ReadAllText(Path.Combine(empty, "notes", "a.md")));

        var filled = Path.Combine(root, "filled");
        Directory.CreateDirectory(Path.Combine(filled, "notes"));
        File.WriteAllText(Path.Combine(filled, "notes", "b.md"), "기존");
        Assert.False(NoteDataLocation.MigrateLegacy(source, filled));
        Assert.False(File.Exists(Path.Combine(filled, "notes", "a.md")));
    }

    [Theory]
    [InlineData(@"C:\a\data", @"C:\a\data", true)]
    [InlineData(@"C:\a\data\sub", @"C:\a\data", true)]
    [InlineData(@"C:\a", @"C:\a\data", true)]
    [InlineData(@"C:\a\data2", @"C:\a\data", false)]
    [InlineData(@"D:\data", @"C:\a\data", false)]
    public void OverlappingFoldersAreDetected(string first, string second, bool expected) => Assert.Equal(expected, NoteDataLocation.Overlaps(first, second));
}
