using Microsoft.Extensions.Time.Testing;

namespace HunminNotes.Core.Tests;

public sealed class NoteServiceTests
{
    private static readonly Note Original = Note.Create("Inbox/note.md", "메모", "원본", []);

    [Fact]
    public async Task OpeningExistingNoteCanRepairMissingIndexWithoutRewritingNote()
    {
        var repository = new Repository(Original);
        var index = new Index();
        await using var service = new NoteService(repository, new Journal(), index);
        await service.OpenAsync(Original.Id, default);
        await service.FlushAsync(Original.Id, default);
        Assert.Equal(1, index.Calls);
        Assert.Empty(repository.Saves);
    }

    [Fact]
    public async Task RapidDraftsAreJournaledButOnlyLatestIsSavedAfter500ms()
    {
        var repository = new Repository(Original);
        var journal = new Journal();
        var clock = new FakeTimeProvider();
        await using var service = new NoteService(repository, journal, new Index(), clock);
        await service.OpenAsync(Original.Id, default);
        await service.ApplyChangeAsync(new(Original.Id, 1, "첫 입력"), default);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        await service.ApplyChangeAsync(new(Original.Id, 2, "최종 입력"), default);
        Assert.Equal("최종 입력", journal.Draft!.Note.Markdown);
        clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Empty(repository.Saves);
        var saved = NextSaved(service);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await saved.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "최종 입력" }, repository.Saves);
        Assert.Null(journal.Draft);
    }

    [Fact]
    public async Task RevisionsAreSerializedAndOldDraftCannotOverwriteNewDraft()
    {
        var repository = new Repository(Original);
        var journal = new Journal();
        await using var service = new NoteService(repository, journal, new Index());
        await service.OpenAsync(Original.Id, default);
        await service.ApplyChangeAsync(new(Original.Id, 2, "리비전2"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyChangeAsync(new(Original.Id, 1, "오래된 입력"), default));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeSave = async () => { entered.SetResult(); await release.Task; };
        var first = service.FlushAsync(Original.Id, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newer = service.ApplyChangeAsync(new(Original.Id, 3, "리비전3"), default);
        release.SetResult();
        await first;
        await newer;
        repository.BeforeSave = null;
        await service.FlushAsync(Original.Id, default);
        Assert.Equal(new[] { "리비전2", "리비전3" }, repository.Saves);
    }

    [Fact]
    public async Task DiskFullKeepsMemoryAndRecoveryDraftAndRetrySavesBeforeIndexing()
    {
        var repository = new Repository(Original) { Failure = new IOException("디스크 공간 부족", unchecked((int)0x80070070)) };
        var journal = new Journal();
        var index = new Index { Check = note => Assert.Equal(note.Markdown, repository.Current!.Markdown) };
        await using var service = new NoteService(repository, journal, index);
        await service.OpenAsync(Original.Id, default);
        await service.ApplyChangeAsync(new(Original.Id, 1, "복구 초안"), default);
        await Assert.ThrowsAsync<IOException>(() => service.FlushAsync(Original.Id, default));
        var failed = await service.GetStateAsync(Original.Id, default);
        Assert.True(failed.HasUnsavedChanges);
        Assert.Equal(NoteSaveStatus.Failed, failed.Status);
        Assert.Equal("복구 초안", failed.Note.Markdown);
        Assert.Equal("원본", repository.Current!.Markdown);
        Assert.Equal("복구 초안", journal.Draft!.Note.Markdown);
        Assert.Equal(0, index.Calls);
        repository.Failure = null;
        await service.FlushAsync(Original.Id, default);
        Assert.Equal(1, index.Calls);
        Assert.Null(journal.Draft);
    }

    [Fact]
    public async Task IndexFailureDoesNotPretendFileSaveFailedAndRetryDoesNotRewriteFile()
    {
        var repository = new Repository(Original);
        var journal = new Journal();
        var index = new Index { Failure = new IOException("인덱스 실패") };
        await using var service = new NoteService(repository, journal, index);
        await service.OpenAsync(Original.Id, default);
        await service.ApplyChangeAsync(new(Original.Id, 1, "저장 내용"), default);
        await Assert.ThrowsAsync<IOException>(() => service.FlushAsync(Original.Id, default));
        var state = await service.GetStateAsync(Original.Id, default);
        Assert.False(state.HasUnsavedChanges);
        Assert.True(state.IndexPending);
        Assert.NotNull(journal.Draft);
        index.Failure = null;
        await service.FlushAsync(Original.Id, default);
        Assert.Single(repository.Saves);
        Assert.Null(journal.Draft);
    }

    [Fact]
    public async Task RestartOffersRecoveryAndDoesNotWriteUntilUserConfirms()
    {
        var repository = new Repository(Original);
        var journal = new Journal();
        await using (var first = new NoteService(repository, journal, new Index(), debounce: TimeSpan.FromHours(1)))
        {
            await first.OpenAsync(Original.Id, default);
            await first.ApplyChangeAsync(new(Original.Id, 8, "재시작 복구"), default);
        }
        await using var restarted = new NoteService(repository, journal, new Index(), debounce: TimeSpan.FromHours(1));
        var recovered = await restarted.OpenAsync(Original.Id, default);
        Assert.True(recovered.RecoveryPending);
        Assert.Equal("재시작 복구", recovered.Note.Markdown);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.FlushAsync(Original.Id, default));
        Assert.Empty(repository.Saves);
        await restarted.ResolveRecoveryAsync(Original.Id, true, default);
        await restarted.FlushAsync(Original.Id, default);
        Assert.Equal("재시작 복구", repository.Current!.Markdown);
    }

    [Fact]
    public async Task ExplicitDiscardKeepsDiskAndRemovesRecoveryRecord()
    {
        var repository = new Repository(Original);
        var journal = new Journal();
        await using (var first = new NoteService(repository, journal, new Index()))
        {
            await first.OpenAsync(Original.Id, default);
            await first.ApplyChangeAsync(new(Original.Id, 1, "초안"), default);
        }
        await using var second = new NoteService(repository, journal, new Index());
        await second.OpenAsync(Original.Id, default);
        var state = await second.ResolveRecoveryAsync(Original.Id, false, default);
        Assert.Equal("원본", state.Note.Markdown);
        Assert.False(state.HasUnsavedChanges);
        Assert.Null(journal.Draft);
        Assert.Empty(repository.Saves);
    }

    [Fact]
    public async Task JournalFailureKeepsDraftAndIsExposedInsteadOfAcknowledgingDurability()
    {
        var journal = new Journal { Failure = new IOException("저널 저장 실패") };
        var repository = new Repository(Original);
        await using var service = new NoteService(repository, journal, new Index());
        await service.OpenAsync(Original.Id, default);
        await Assert.ThrowsAsync<IOException>(() => service.ApplyChangeAsync(new(Original.Id, 1, "메모리 초안"), default));
        var state = await service.GetStateAsync(Original.Id, default);
        Assert.True(state.HasUnsavedChanges);
        Assert.Equal("메모리 초안", state.Note.Markdown);
        Assert.NotNull(state.Error);
        Assert.Empty(repository.Saves);
        journal.Failure = null;
        await service.FlushAsync(Original.Id, default);
        Assert.Equal("메모리 초안", repository.Current!.Markdown);
    }

    [Fact]
    public async Task RestartAfterFileCommitRepairsIndexWithoutAskingToRestoreOrRewritingFile()
    {
        var repository = new Repository(Original);
        var journal = new Journal();
        await using (var first = new NoteService(repository, journal, new Index { Failure = new IOException("인덱스 실패") }))
        {
            await first.OpenAsync(Original.Id, default);
            await first.ApplyChangeAsync(new(Original.Id, 1, "파일 저장 완료"), default);
            await Assert.ThrowsAsync<IOException>(() => first.FlushAsync(Original.Id, default));
        }
        var repairedIndex = new Index();
        await using var restarted = new NoteService(repository, journal, repairedIndex);
        var state = await restarted.OpenAsync(Original.Id, default);
        Assert.False(state.RecoveryPending);
        Assert.False(state.HasUnsavedChanges);
        Assert.True(state.IndexPending);
        await restarted.FlushAsync(Original.Id, default);
        Assert.Single(repository.Saves);
        Assert.Equal(1, repairedIndex.Calls);
        Assert.Null(journal.Draft);
    }

    [Fact]
    public async Task RestartDoesNotRestoreOverExternallyModifiedFile()
    {
        var repository = new Repository(Original);
        var journal = new Journal();
        await using (var first = new NoteService(repository, journal, new Index(), debounce: TimeSpan.FromHours(1)))
        {
            await first.OpenAsync(Original.Id, default);
            await first.ApplyChangeAsync(new(Original.Id, 1, "복구할 초안"), default);
        }
        repository.Current = Original with { Markdown = "외부 변경" };
        await using var restarted = new NoteService(repository, journal, new Index());
        await restarted.OpenAsync(Original.Id, default);
        await Assert.ThrowsAsync<NoteSaveConflictException>(() => restarted.ResolveRecoveryAsync(Original.Id, true, default));
        Assert.Equal("외부 변경", repository.Current.Markdown);
        Assert.Equal("복구할 초안", journal.Draft!.Note.Markdown);
    }

    [Fact]
    public async Task ExternalDiskChangeIsNotOverwrittenAndRecoveryRecordSurvives()
    {
        var repository = new Repository(Original);
        var journal = new Journal();
        await using var service = new NoteService(repository, journal, new Index());
        await service.OpenAsync(Original.Id, default);
        await service.ApplyChangeAsync(new(Original.Id, 1, "로컬 초안"), default);
        repository.Current = Original with { Markdown = "외부 변경" };
        await Assert.ThrowsAsync<NoteSaveConflictException>(() => service.FlushAsync(Original.Id, default));
        Assert.Equal("외부 변경", repository.Current.Markdown);
        Assert.Equal("로컬 초안", journal.Draft!.Note.Markdown);
    }

    [Fact]
    public async Task TitleAndBodyShareJournalSaveAndIndexRevision()
    {
        var repository = new Repository(Original);
        var journal = new Journal();
        var index = new Index { Check = note => Assert.Equal("새 이름", note.Title) };
        await using var service = new NoteService(repository, journal, index);
        await service.OpenAsync(Original.Id, default);
        await service.ApplyChangeAsync(new(Original.Id, 1, "새 본문", "  새 이름  "), default);
        Assert.Equal("새 이름", journal.Draft!.Note.Title);
        await service.FlushAsync(Original.Id, default);
        Assert.Equal("새 이름", repository.Current!.Title);
        Assert.Equal("새 본문", repository.Current.Markdown);
        Assert.Equal(Original.RelativePath, repository.Current.RelativePath);
        Assert.Equal(Original.Id, repository.Current.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("줄\n바꿈")]
    public async Task InvalidTitleDoesNotChangeDraft(string title)
    {
        await using var service = new NoteService(new Repository(Original), new Journal(), new Index());
        await service.OpenAsync(Original.Id, default);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyChangeAsync(new(Original.Id, 1, "수정", title), default));
        Assert.Equal(Original, (await service.GetStateAsync(Original.Id, default)).Note);
    }

    private static Task NextSaved(NoteService service)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += state => { if (state.Status == NoteSaveStatus.Saved) completion.TrySetResult(); };
        return completion.Task;
    }
    private sealed class Repository(Note note) : INoteRepository
    {
        public Note? Current = note;
        public List<string> Saves { get; } = [];
        public Exception? Failure;
        public Func<Task>? BeforeSave;
        public Task<Note?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Current);
        public async Task SaveAsync(Note value, CancellationToken cancellationToken)
        {
            if (BeforeSave is not null) await BeforeSave();
            if (Failure is not null) throw Failure;
            Saves.Add(value.Markdown); Current = value;
        }
    }
    private sealed class Journal : IDraftJournal
    {
        public RecoveryDraft? Draft;
        public Exception? Failure;
        public Task<RecoveryDraft?> ReadAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Draft);
        public Task WriteAsync(RecoveryDraft draft, CancellationToken cancellationToken) { if (Failure is not null) throw Failure; Draft = draft; return Task.CompletedTask; }
        public Task RemoveAsync(Guid id, CancellationToken cancellationToken) { Draft = null; return Task.CompletedTask; }
    }
    private sealed class Index : INoteIndexWriter
    {
        public int Calls;
        public Exception? Failure;
        public Action<Note>? Check;
        public Task UpsertAsync(Note note, CancellationToken cancellationToken) { Check?.Invoke(note); if (Failure is not null) throw Failure; Calls++; return Task.CompletedTask; }
    }
}
