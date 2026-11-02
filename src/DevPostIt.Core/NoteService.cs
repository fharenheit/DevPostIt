using System.Collections.Concurrent;

namespace DevPostIt.Core;

public sealed class NoteService(INoteRepository repository, IDraftJournal journal, INoteIndexWriter index,
    TimeProvider? timeProvider = null, TimeSpan? debounce = null) : IAsyncDisposable
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan delay = debounce ?? TimeSpan.FromMilliseconds(500);
    private readonly ConcurrentDictionary<Guid, Session> sessions = new();
    private readonly SemaphoreSlim opening = new(1, 1);
    private bool disposed;
    public event Action<NoteSaveState>? StateChanged;
    public event Action<ExternalChange>? ExternalChanged;
    public bool IsOpen(Guid id) => sessions.ContainsKey(id);

    public async Task<NoteSaveState> OpenAsync(Guid id, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await opening.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (sessions.ContainsKey(id)) return await GetStateAsync(id, cancellationToken).ConfigureAwait(false);
            var disk = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("메모를 찾지 못했습니다.");
            var recovery = await journal.ReadAsync(id, cancellationToken).ConfigureAwait(false);
            var fingerprint = NoteFingerprint.Compute(disk);
            var state = new NoteSaveState(disk, 0, 0, NoteSaveStatus.Clean, false, true, null);
            var session = new Session(state, fingerprint);
            if (recovery is not null)
            {
                if (recovery.Note.Id != id || recovery.Revision <= 0) throw new InvalidDataException("복구 기록의 메모 ID 또는 리비전이 잘못되었습니다.");
                var alreadySaved = NoteFingerprint.Compute(recovery.Note) == fingerprint;
                session.State = new(recovery.Note with { RelativePath = disk.RelativePath }, recovery.Revision, alreadySaved ? recovery.Revision : 0,
                    alreadySaved ? NoteSaveStatus.Clean : NoteSaveStatus.RecoveryPending, !alreadySaved, alreadySaved, null);
                session.RecoveryMatchesDisk = recovery.BaseFingerprint == fingerprint;
                session.JournalPending = true;
                session.JournalRevision = recovery.Revision;
            }
            sessions[id] = session;
            return session.State;
        }
        finally { opening.Release(); }
    }

    public async Task<NoteSaveState> ApplyChangeAsync(NoteChange change, CancellationToken cancellationToken)
    {
        var session = GetSession(change.NoteId);
        NoteSaveState? notification = null;
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.Deleted) throw new KeyNotFoundException("삭제된 메모입니다.");
            if (session.State.RecoveryPending) throw new InvalidOperationException("복구 내용을 먼저 선택하세요.");
            if (change.Revision <= session.State.Revision || change.Revision > 9_007_199_254_740_991)
                throw new InvalidOperationException("오래되거나 유효하지 않은 리비전입니다.");
            var title = change.Title is null ? session.State.Note.Title : NoteTitle.Normalize(change.Title);
            StopTimer(session);
            session.State = session.State with
            {
                Note = session.State.Note with { Markdown = change.Markdown, Title = title, Updated = time.GetUtcNow() },
                Revision = change.Revision, Status = NoteSaveStatus.Pending, Error = null
            };
            try
            {
                await WriteJournalAsync(session, cancellationToken).ConfigureAwait(false);
                Schedule(session);
            }
            catch (Exception error)
            {
                session.State = session.State with { Status = NoteSaveStatus.Failed, Error = error.Message };
                throw;
            }
            finally { notification = session.State; }
            return session.State;
        }
        finally { session.Gate.Release(); if (notification is not null) Notify(notification); }
    }

    public Task<NoteSaveState> FlushAsync(Guid id, CancellationToken cancellationToken) => FlushCoreAsync(GetSession(id), null, cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (repository is not INoteDeletionRepository deletions || index is not INoteIndexDeletionWriter indexDeletions)
            throw new NotSupportedException("이 저장소는 영구 삭제를 지원하지 않습니다.");
        var session = GetSession(id);
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.Deleted) return;
            StopTimer(session);
            var previousDraft = await journal.ReadAsync(id, cancellationToken).ConfigureAwait(false);
            await journal.RemoveAsync(id, cancellationToken).ConfigureAwait(false);
            session.JournalPending = false; session.JournalRevision = -1;
            try
            {
                await indexDeletions.RemoveAsync(id, cancellationToken).ConfigureAwait(false);
                await deletions.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                session.Deleted = true;
                sessions.TryRemove(id, out _);
            }
            catch
            {
                try
                {
                    if (previousDraft is not null)
                    {
                        await journal.WriteAsync(previousDraft, CancellationToken.None).ConfigureAwait(false);
                        session.JournalPending = true; session.JournalRevision = previousDraft.Revision;
                    }
                    else if (session.State.HasUnsavedChanges) await WriteJournalAsync(session, CancellationToken.None).ConfigureAwait(false);
                }
                catch { /* 파일은 남겨 두고 삭제 오류를 보고합니다. */ }
                try
                {
                    var disk = await repository.GetAsync(id, CancellationToken.None).ConfigureAwait(false);
                    if (disk is not null) await index.UpsertAsync(disk, CancellationToken.None).ConfigureAwait(false);
                }
                catch { /* 파생 인덱스는 재시작 때 복구하며 메모 파일을 보존합니다. */ }
                throw;
            }
        }
        finally
        {
            if (!session.Deleted && session.State.HasUnsavedChanges && !session.State.RecoveryPending) Schedule(session);
            session.Gate.Release();
        }
    }

    // 파일이 앱 밖에서 바뀌었을 때 호출합니다. 저장하지 않은 입력이 있으면 외부 내용을 별도 메모로 보존한 뒤 입력을 이어서 저장합니다.
    public async Task ReconcileWithDiskAsync(Guid id, Func<Note, CancellationToken, Task<string>> preserveExternal, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(id, out var session)) return;
        NoteSaveState? notification = null;
        ExternalChange? change = null;
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.Deleted || session.State.RecoveryPending) return;
            var disk = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (disk is null)
            {
                if (session.MissingReported) return;
                session.MissingReported = true;
                const string missing = "파일이 앱 밖에서 삭제되었습니다. 이 창의 내용은 복구 기록에 보존합니다.";
                StopTimer(session);
                if (session.State.HasUnsavedChanges && session.JournalRevision != session.State.Revision) await WriteJournalAsync(session, cancellationToken).ConfigureAwait(false);
                session.State = session.State with { Status = NoteSaveStatus.Failed, Error = missing };
                notification = session.State;
                change = new(id, ExternalChangeKind.Missing, session.State, missing);
                return;
            }
            session.MissingReported = false;
            var fingerprint = NoteFingerprint.Compute(disk);
            if (fingerprint == session.BaseFingerprint) return;
            if (!session.State.HasUnsavedChanges)
            {
                StopTimer(session);
                var revision = session.State.Revision + 1;
                session.BaseFingerprint = fingerprint;
                session.State = session.State with { Note = disk, Revision = revision, SavedRevision = revision, Status = NoteSaveStatus.Clean, IndexPending = true, Error = null };
                try
                {
                    await index.UpsertAsync(disk, cancellationToken).ConfigureAwait(false);
                    session.State = session.State with { IndexPending = false };
                }
                catch (Exception) { /* 파생 인덱스는 다음 저장 때 다시 맞춥니다. */ }
                notification = session.State;
                change = new(id, ExternalChangeKind.Reloaded, session.State, null);
                return;
            }
            var preserved = await preserveExternal(disk, cancellationToken).ConfigureAwait(false);
            session.BaseFingerprint = fingerprint;
            await WriteJournalAsync(session, cancellationToken).ConfigureAwait(false);
            session.State = session.State with { Status = NoteSaveStatus.Pending, Error = null };
            Schedule(session);
            notification = session.State;
            change = new(id, ExternalChangeKind.ConflictPreserved, session.State, $"파일이 앱 밖에서 바뀌어 그 내용을 ‘{preserved}’ 메모로 보존했습니다.");
        }
        finally
        {
            session.Gate.Release();
            if (notification is not null) Notify(notification);
            if (change is not null) foreach (var subscriber in ExternalChanged?.GetInvocationList() ?? [])
                try { ((Action<ExternalChange>)subscriber)(change); }
                catch (Exception) { /* 표시 계층의 예외가 저장 결과를 바꾸지 않게 합니다. */ }
        }
    }

    // 파일을 옮기고 세션의 기준 지문과 복구 기록을 새 경로에 맞춥니다. 저장 전 입력은 그대로 유지합니다.
    public async Task<NoteSaveState> RelocateAsync(Guid id, string relativePath, CancellationToken cancellationToken)
    {
        if (repository is not INoteRelocationRepository relocations) throw new NotSupportedException("이 저장소는 파일 이동을 지원하지 않습니다.");
        var session = GetSession(id);
        NoteSaveState? notification = null;
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.Deleted) throw new KeyNotFoundException("삭제된 메모입니다.");
            if (session.State.RecoveryPending) throw new InvalidOperationException("복구 내용을 먼저 선택하세요.");
            if (session.State.Note.RelativePath == relativePath) return session.State;
            var disk = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (disk is null || NoteFingerprint.Compute(disk) != session.BaseFingerprint)
                throw new NoteSaveConflictException("파일이 앱 밖에서 바뀌어 이름을 바꾸지 못했습니다. 잠시 후 다시 시도하세요.");
            await relocations.MoveAsync(id, relativePath, cancellationToken).ConfigureAwait(false);
            session.BaseFingerprint = NoteFingerprint.Compute(disk with { RelativePath = relativePath });
            session.State = session.State with { Note = session.State.Note with { RelativePath = relativePath } };
            notification = session.State;
            // 이동은 끝났으므로 후속 처리가 실패해도 이름 변경을 되돌리지 않습니다. 다음 저장과 시작 때 다시 맞춥니다.
            try { await index.UpsertAsync(session.State.Note, cancellationToken).ConfigureAwait(false); }
            catch (Exception) { session.State = session.State with { IndexPending = true }; notification = session.State; }
            if (session.JournalPending) await WriteJournalAsync(session, CancellationToken.None).ConfigureAwait(false);
            return session.State;
        }
        finally { session.Gate.Release(); if (notification is not null) Notify(notification); }
    }

    private async Task<NoteSaveState> FlushCoreAsync(Session session, long? expectedRevision, CancellationToken cancellationToken)
    {
        NoteSaveState? notification = null;
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.Deleted) throw new KeyNotFoundException("삭제된 메모입니다.");
            if (expectedRevision.HasValue && session.State.Revision != expectedRevision.Value) return session.State;
            if (session.State.RecoveryPending) throw new InvalidOperationException("복구 내용을 먼저 선택하세요.");
            StopTimer(session);
            if (!session.State.HasUnsavedChanges && !session.State.IndexPending && !session.JournalPending) return session.State;
            try
            {
                if (session.State.HasUnsavedChanges)
                {
                    if (session.JournalRevision != session.State.Revision) await WriteJournalAsync(session, cancellationToken).ConfigureAwait(false);
                    var disk = await repository.GetAsync(session.State.Note.Id, cancellationToken).ConfigureAwait(false);
                    if (disk is null || NoteFingerprint.Compute(disk) != session.BaseFingerprint)
                        throw new NoteSaveConflictException("디스크 메모가 변경되어 자동 덮어쓰기를 중단했습니다. 초안은 복구 기록에 보존했습니다.");
                    await repository.SaveAsync(session.State.Note, cancellationToken).ConfigureAwait(false);
                    session.BaseFingerprint = NoteFingerprint.Compute(session.State.Note);
                    session.State = session.State with { SavedRevision = session.State.Revision, IndexPending = true };
                }
                if (session.State.IndexPending)
                {
                    await index.UpsertAsync(session.State.Note, cancellationToken).ConfigureAwait(false);
                    session.State = session.State with { IndexPending = false };
                }
                if (session.JournalPending)
                {
                    await journal.RemoveAsync(session.State.Note.Id, cancellationToken).ConfigureAwait(false);
                    session.JournalPending = false;
                }
                session.State = session.State with { Status = NoteSaveStatus.Saved, Error = null };
                return session.State;
            }
            catch (Exception error)
            {
                var prefix = !session.State.HasUnsavedChanges ? "파일은 저장되었지만 후속 처리를 완료하지 못했습니다: " : "저장하지 못했습니다: ";
                session.State = session.State with { Status = NoteSaveStatus.Failed, Error = prefix + error.Message };
                throw;
            }
            finally { notification = session.State; }
        }
        finally { session.Gate.Release(); if (notification is not null) Notify(notification); }
    }

    public async Task<NoteSaveState> GetStateAsync(Guid id, CancellationToken cancellationToken)
    {
        var session = GetSession(id);
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { if (session.Deleted) throw new KeyNotFoundException("삭제된 메모입니다."); return session.State; }
        finally { session.Gate.Release(); }
    }

    public async Task<NoteSaveState> ResolveRecoveryAsync(Guid id, bool restore, CancellationToken cancellationToken)
    {
        var session = GetSession(id);
        NoteSaveState? notification = null;
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.Deleted) throw new KeyNotFoundException("삭제된 메모입니다.");
            if (!session.State.RecoveryPending) return session.State;
            if (restore)
            {
                if (!session.RecoveryMatchesDisk) throw new NoteSaveConflictException("복구 이후 원본이 변경되었습니다. 원본과 복구 기록을 보존한 채 복구를 중단했습니다.");
                session.State = session.State with { RecoveryPending = false, Status = NoteSaveStatus.Pending };
                Schedule(session);
            }
            else
            {
                var disk = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("메모를 찾지 못했습니다.");
                await journal.RemoveAsync(id, cancellationToken).ConfigureAwait(false);
                session.State = new(disk, session.State.Revision, session.State.Revision, NoteSaveStatus.Clean, false, false, null);
                session.BaseFingerprint = NoteFingerprint.Compute(disk);
                session.JournalPending = false;
            }
            return notification = session.State;
        }
        finally { session.Gate.Release(); if (notification is not null) Notify(notification); }
    }

    private async Task WriteJournalAsync(Session session, CancellationToken cancellationToken)
    {
        await journal.WriteAsync(new(session.State.Note, session.State.Revision, session.BaseFingerprint), cancellationToken).ConfigureAwait(false);
        session.JournalRevision = session.State.Revision;
        session.JournalPending = true;
    }

    private Session GetSession(Guid id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return sessions.TryGetValue(id, out var session) ? session : throw new KeyNotFoundException("메모를 먼저 여세요.");
    }

    private static void StopTimer(Session session) { session.Timer?.Cancel(); session.Timer?.Dispose(); session.Timer = null; }
    private void Schedule(Session session)
    {
        StopTimer(session);
        session.Timer = new CancellationTokenSource();
        session.Jobs.RemoveAll(task => task.IsCompleted);
        session.Jobs.Add(AutosaveAsync(session, session.State.Revision, session.Timer.Token));
    }

    private async Task AutosaveAsync(Session session, long revision, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);
            await FlushCoreAsync(session, revision, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { /* 저장 오류는 상태 이벤트와 복구 저널에 남기고 자동 재시도 루프를 만들지 않습니다. */ }
    }

    private void Notify(NoteSaveState state)
    {
        foreach (var subscriber in StateChanged?.GetInvocationList() ?? [])
            try { ((Action<NoteSaveState>)subscriber)(state); }
            catch (Exception) { /* 표시 계층의 예외가 파일 저장 결과를 바꾸지 않게 합니다. */ }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        foreach (var session in sessions.Values)
        {
            Task[] jobs;
            await session.Gate.WaitAsync().ConfigureAwait(false);
            try { StopTimer(session); jobs = session.Jobs.ToArray(); }
            finally { session.Gate.Release(); }
            await Task.WhenAll(jobs).ConfigureAwait(false);
        }
    }

    private sealed class Session(NoteSaveState state, string baseFingerprint)
    {
        public bool Deleted;
        public bool MissingReported;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly List<Task> Jobs = [];
        public NoteSaveState State = state;
        public string BaseFingerprint = baseFingerprint;
        public bool RecoveryMatchesDisk;
        public long JournalRevision = -1;
        public bool JournalPending;
        public CancellationTokenSource? Timer;
    }
}
