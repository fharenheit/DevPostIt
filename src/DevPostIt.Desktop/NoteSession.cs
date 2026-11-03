using System.Text.Json;
using DevPostIt.Core;
using DevPostIt.EditorHost;
using DevPostIt.Storage;

namespace DevPostIt.Desktop;

// 메모 창 하나의 브리지 메시지 처리와 닫기 상태 기계입니다. 저장·삭제 규칙이 운영체제별로 갈라지지 않게 합니다.
public sealed class NoteSession(NoteWorkspace workspace, Guid noteId, Func<string, bool> isTrustedPage, IWebSurface web, INoteWindow window, INoteDialogs dialogs, INoteShell shell)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim messages = new(1, 1);
    private readonly Queue<(string Json, string Source)> deferredMessages = new();
    private EditorBridge? bridge;
    private NoteService service => workspace.Service;
    private Task<NoteSaveState>? startup;
    private Task? initialization;
    private bool closing;
    private bool confirming;
    private bool closed;
    private bool opened;
    private bool loaded;
    private TaskCompletionSource<bool>? closeCompletion;
    private bool preserveOnClose = true;

    public Guid NoteId => noteId;
    public bool AllowClose { get; private set; }
    public bool CanCollapse => !closed && !closing;
    public bool HasEditor => bridge is not null && !closed;
    public bool IsEditorReady => bridge is not null && !closed && !closing && loaded;
    public Func<Task>? InitializationDelay { get; set; }
    public string? PendingPreferences { get; set; }
    public event Action? CollapseRequested;
    public event Action? NewNoteRequested;
    public event Action? SettingsRequested;
    public event Action? SearchRequested;
    public event Action? CaptureRequested;
    public event Action<string, bool>? AppearanceChanged;

    public Task<bool> RequestDeleteAsync() => BeginClose(false);
    public Task<bool> RequestCloseAsync() => BeginClose(true);
    // 사용자가 창의 닫기 단추를 눌렀습니다. 다음 닫기에서 휴지통 이동을 묻습니다.
    public void MarkUserClose() { if (!closing && !confirming) preserveOnClose = false; }
    public Task EnsureInitializedAsync() => initialization ??= InitializeEditorAsync();
    public void NotifyAssetsChanged() { if (IsEditorReady) Send("note.assetsChanged", new { }, bridge!.Revision); }

    private Task<bool> BeginClose(bool preserve)
    {
        if (closed) return Task.FromResult(true);
        if (closeCompletion is not null) return closeCompletion.Task;
        var completion = closeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (closing || confirming) return completion.Task;
        preserveOnClose = preserve;
        window.Close();
        return completion.Task;
    }

    private void CancelClose()
    {
        closing = false;
        window.SetInputEnabled(true);
        preserveOnClose = true;
        closeCompletion?.TrySetResult(false); closeCompletion = null;
        window.BringToFront();
        if (initialization?.IsCompleted == true && bridge is null) initialization = InitializeEditorAsync();
        var pending = deferredMessages.ToArray();
        deferredMessages.Clear();
        foreach (var message in pending) _ = ProcessMessageAsync(message.Json, message.Source);
    }

    private async Task InitializeEditorAsync()
    {
        try
        {
            if (InitializationDelay is { } delay) await delay();
            startup = Task.Run(() => workspace.OpenNoteAsync(noteId, CancellationToken.None));
            var state = await startup;
            if (closing || closed) return;
            opened = true;
            if (state.RecoveryPending)
            {
                var choice = dialogs.AskRecovery();
                if (choice == RecoveryChoice.KeepAndClose)
                {
                    AllowClose = true; window.Close(); return;
                }
                state = await Task.Run(() => service.ResolveRecoveryAsync(noteId, choice == RecoveryChoice.Restore, CancellationToken.None));
            }
            if (!state.HasUnsavedChanges)
            {
                try { state = await Task.Run(() => service.FlushAsync(noteId, CancellationToken.None)); }
                catch (Exception) { state = await service.GetStateAsync(noteId, CancellationToken.None); }
            }
            await web.PrepareAsync();
            window.SetNoteTitle(state.Note.Title);
            service.ExternalChanged += OnExternalChanged;
            bridge = new EditorBridge(noteId, state.Revision, isTrustedPage);
            service.StateChanged += OnNoteStateChanged;
            web.Start(ProcessMessageAsync);
        }
        catch (Exception error) { window.ShowStartupError($"메모장을 시작하지 못했습니다.\n{error.Message}"); }
    }

    public async Task ProcessMessageAsync(string json, string source)
    {
        if (bridge is null || closed) return;
        if (closing) { deferredMessages.Enqueue((json, source)); return; }
        await messages.WaitAsync();
        try
        {
            if (closed) return;
            if (closing) { deferredMessages.Enqueue((json, source)); return; }
            var result = await bridge.ReceiveAsync(json, source, CancellationToken.None);
            if (result.Change is not null) await Task.Run(() => service.ApplyChangeAsync(result.Change, CancellationToken.None));
            else if (result.Message.Type == "editor.ready")
            {
                var state = await service.GetStateAsync(noteId, CancellationToken.None);
                Send("note.load", new { markdown = state.Note.Markdown, title = state.Note.Title }, state.Revision);
                if (state.Error is not null) Send("note.conflict", new { message = state.Error }, state.Revision);
                else if (state.HasUnsavedChanges) Send("note.conflict", new { message = "복구 초안을 자동 저장하는 중입니다." }, state.Revision);
                Send("ui.appearance", new { color = window.NoteColor, topmost = window.Topmost }, state.Revision);
                if (PendingPreferences is { } preferences)
                {
                    PendingPreferences = null;
                    Send("ui.preferences", new { value = preferences }, state.Revision);
                }
                loaded = true;
                window.HideLoading();
            }
            else if (result.Message.Type == "window.collapseRequested") CollapseRequested?.Invoke();
            else if (result.Message.Type == "note.newRequested") NewNoteRequested?.Invoke();
            else if (result.Message.Type == "window.settingsRequested") SettingsRequested?.Invoke();
            else if (result.Message.Type == "window.searchRequested") SearchRequested?.Invoke();
            else if (result.Message.Type == "window.captureRequested") CaptureRequested?.Invoke();
            else if (result.Message.Type == "note.exportPdfRequested") await ExportPdfAsync();
            else if (result.Message.Type == "note.newDiagramRequested")
            {
                if (shell.FindDiagramApp() is not { } application) shell.ReportDiagramAppMissing();
                else
                {
                    var (markdown, path) = await Task.Run(() => workspace.CreateDiagramAsync(noteId, CancellationToken.None));
                    await web.EvaluateAsync("window.hunminEditor.setMode('source')");
                    Send("note.imageSaved", new { markdown }, bridge.Revision);
                    shell.OpenDiagram(application, path);
                }
            }
            else if (result.Message.Type == "note.editDiagramRequested")
            {
                var path = workspace.DiagramPath(result.Message.Payload.GetProperty("file").GetString()!);
                if (shell.FindDiagramApp() is { } application) shell.OpenDiagram(application, path);
                else shell.ReportDiagramAppMissing();
            }
            else if (result.Message.Type == "note.openLinkRequested")
            {
                var url = result.Message.Payload.GetProperty("url").GetString()!;
                if (dialogs.ConfirmLink(url)) shell.OpenLink(url);
            }
            else if (result.Message.Type == "note.openImagesRequested") shell.OpenFolder(await Task.Run(() => workspace.EnsureImageFolder(noteId)));
            else if (result.Message.Type == "note.renameRequested")
            {
                var title = EditorBridge.ReadTitle(result.Message.Payload)!;
                try
                {
                    await Task.Run(() => workspace.RenameFileAsync(noteId, title, CancellationToken.None));
                    Send("note.renamed", new { title }, bridge.Revision);
                }
                catch (Exception error) { Send("note.renameRejected", new { message = error.Message }, bridge.Revision); }
            }
            else if (result.Message.Type == "window.appearanceChanged")
            {
                var color = result.Message.Payload.GetProperty("color").GetString()!;
                var topmost = result.Message.Payload.GetProperty("topmost").GetBoolean();
                window.ApplyAppearance(color, topmost);
                AppearanceChanged?.Invoke(color, topmost);
            }
            else if (result.Message.Type == "note.imagePasteRequested")
            {
                var png = shell.ReadClipboardPng() ?? throw new InvalidOperationException("클립보드에서 이미지를 찾지 못했습니다.");
                var markdown = await Task.Run(() => workspace.SaveImageAsync(noteId, png, CancellationToken.None));
                Send("note.imageSaved", new { markdown }, bridge.Revision);
            }
            else if (result.Message.Type == "note.saveRequested")
            {
                var state = await service.GetStateAsync(noteId, CancellationToken.None);
                if (state.Note.Markdown != result.Message.Payload.GetProperty("markdown").GetString() || (EditorBridge.ReadTitle(result.Message.Payload) is { } requestedTitle && requestedTitle != state.Note.Title))
                    throw new EditorProtocolException("저장 요청 본문이 현재 초안과 일치하지 않습니다.");
                state = await Task.Run(() => service.FlushAsync(noteId, CancellationToken.None));
                Send("note.saved", new { }, state.SavedRevision);
            }
        }
        catch (EditorProtocolException) { /* 잘못된 메시지는 저장에 사용하지 않습니다. */ }
        catch (Exception error)
        {
            if (!closed) Send("note.conflict", new { message = error.Message }, bridge.Revision);
        }
        finally { messages.Release(); }
    }

    // 캡처한 이미지를 커서 위치에 넣습니다. 미리보기 중이면 소스 편집으로 돌아옵니다.
    public async Task InsertImageAsync(byte[] png)
    {
        if (!IsEditorReady) throw new InvalidOperationException("메모가 아직 준비되지 않았습니다.");
        var markdown = await Task.Run(() => workspace.SaveImageAsync(noteId, png, CancellationToken.None));
        await web.EvaluateAsync("window.hunminEditor.setMode('source')");
        Send("note.imageSaved", new { markdown }, bridge!.Revision);
    }

    // 화면은 페이지가 미리보기로 바꿔 둔 상태입니다. 끝나면 항상 원래 보기로 돌리도록 알립니다.
    private async Task ExportPdfAsync()
    {
        try
        {
            var state = await service.GetStateAsync(noteId, CancellationToken.None);
            if (shell.PickPdfPath(state.Note.Title) is not { } path) return;
            if (!await web.PrintToPdfAsync(path)) Send("note.conflict", new { message = "PDF로 저장하지 못했습니다." }, bridge!.Revision);
        }
        finally { if (!closed) Send("note.pdfFinished", new { }, bridge!.Revision); }
    }

    private void OnExternalChanged(ExternalChange change)
    {
        if (closed || change.NoteId != noteId) return;
        window.Post(() =>
        {
            if (closed || closing || bridge is null) return;
            if (change.Kind == ExternalChangeKind.Reloaded)
            {
                bridge.SyncRevision(change.State.Revision);
                Send("note.load", new { markdown = change.State.Note.Markdown, title = change.State.Note.Title }, change.State.Revision);
            }
            else if (change.Message is not null) Send("note.conflict", new { message = change.Message }, bridge.Revision);
        });
    }

    private void OnNoteStateChanged(NoteSaveState state)
    {
        if (closed || state.Note.Id != noteId) return;
        window.Post(() =>
        {
            if (closed || closing || bridge is null) return;
            window.SetNoteTitle(state.Note.Title);
            if (!state.HasUnsavedChanges && !state.RecoveryPending) Send("note.saved", new { }, state.SavedRevision);
            if (state.Error is not null) Send("note.conflict", new { message = state.Error }, state.Revision);
        });
    }

    private void Send(string type, object payload, long revision)
    {
        if (closed) return;
        var message = new EditorMessage(1, type, Guid.NewGuid().ToString(), noteId, revision, JsonSerializer.SerializeToElement(payload));
        web.PostMessage(JsonSerializer.Serialize(message, Json));
    }

    // 네이티브 닫기를 취소한 뒤 호출합니다. 최신 입력을 저장하고 끝나면 다시 닫습니다.
    public async Task HandleCloseRequestAsync()
    {
        if (closing || confirming) return;
        if (!preserveOnClose)
        {
            confirming = true;
            try
            {
                window.BringToFront();
                if (!dialogs.ConfirmTrash()) { CancelClose(); return; }
            }
            finally { confirming = false; }
        }
        closing = true;
        window.SetInputEnabled(false);
        await messages.WaitAsync();
        var snapshotCaptured = true;
        try
        {
            if (initialization is not null) { try { await initialization; } catch (Exception) { } }
            if (opened)
            {
                if (bridge is not null && loaded)
                {
                    snapshotCaptured = false;
                    var json = await web.EvaluateAsync("window.hunminEditor ? ({ ...window.hunminEditor.getDraft(), title: window.hunminEditor.getTitle() }) : null");
                    var draft = EditorDraftSnapshot.Parse(json);
                    var state = await service.GetStateAsync(noteId, CancellationToken.None);
                    if (draft.Revision < state.Revision || draft.Revision == state.Revision && (draft.Markdown != state.Note.Markdown || draft.Title is not null && draft.Title != state.Note.Title))
                        throw new EditorProtocolException("화면과 저장 초안이 일치하지 않습니다.");
                    if (draft.Revision > state.Revision)
                    {
                        var message = new EditorMessage(1, "note.changed", Guid.NewGuid().ToString(), noteId, draft.Revision, JsonSerializer.SerializeToElement(new { markdown = draft.Markdown, title = draft.Title }));
                        var change = await bridge.HandleAsync(message, CancellationToken.None);
                        if (change is not null) await Task.Run(() => service.ApplyChangeAsync(change, CancellationToken.None));
                    }
                    snapshotCaptured = true;
                }
                await Task.Run(() => service.FlushAsync(noteId, CancellationToken.None));
            }
            // 화면의 최신 입력까지 저장한 뒤 휴지통으로 옮깁니다.
            if (!preserveOnClose)
            {
                await Task.Run(() => workspace.TrashAsync(noteId, CancellationToken.None));
                AllowClose = true; window.Close(); return;
            }
        }
        catch (Exception error)
        {
            if (!preserveOnClose)
            {
                await dialogs.ReportTrashFailureAsync(error.Message);
                CancelClose(); return;
            }
            if (!snapshotCaptured)
            {
                window.BringToFront();
                dialogs.ReportCloseFailure(true, error.Message);
                CancelClose(); return;
            }
            var state = opened ? await service.GetStateAsync(noteId, CancellationToken.None) : null;
            if (state?.HasUnsavedChanges == true)
            {
                window.BringToFront();
                if (!dialogs.ReportCloseFailure(false, error.Message)) { CancelClose(); return; }
            }
        }
        finally { messages.Release(); }

        AllowClose = true;
        window.Close();
    }

    public void OnWindowClosed()
    {
        closed = true;
        deferredMessages.Clear();
        service.StateChanged -= OnNoteStateChanged;
        service.ExternalChanged -= OnExternalChanged;
        closeCompletion?.TrySetResult(true);
    }
}
