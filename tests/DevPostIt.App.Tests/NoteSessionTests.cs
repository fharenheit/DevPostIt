using System.Text.Json;
using DevPostIt.Core;
using DevPostIt.Desktop;
using DevPostIt.EditorHost;
using DevPostIt.Storage;

namespace DevPostIt.App.Tests;

public sealed class NoteSessionTests : IAsyncLifetime
{
    private const string Source = "devpostit://app/index.html";
    private readonly string root = Path.Combine(Path.GetTempPath(), "devpostit-session-" + Guid.NewGuid().ToString("N"));
    private NoteWorkspace workspace = null!;
    private FakeHost host = null!;
    private NoteSession session = null!;
    private Note note = null!;

    public async Task InitializeAsync()
    {
        workspace = await NoteWorkspace.OpenAsync(root, CancellationToken.None);
        note = workspace.Notes.Single();
        host = new FakeHost();
        session = NewSession(note.Id);
    }

    public async Task DisposeAsync()
    {
        await workspace.DisposeAsync();
        Directory.Delete(root, true);
    }

    private NoteSession NewSession(Guid id)
    {
        var created = new NoteSession(workspace, id, EditorOrigin.IsTrustedMacPage, host, host, host, host);
        host.Session = created;
        return created;
    }

    private async Task ReadyAsync()
    {
        await session.EnsureInitializedAsync();
        await session.ProcessMessageAsync(Message("editor.ready", Guid.Empty, 0, new { }), Source);
        host.DraftJson = JsonSerializer.Serialize(new { markdown = note.Markdown, revision = 0, dirty = false, title = note.Title });
    }

    private static string Message(string type, Guid id, long revision, object payload) => JsonSerializer.Serialize(new
    {
        protocolVersion = 1, type, requestId = Guid.NewGuid().ToString(), noteId = id, revision, payload,
    });

    private Task ChangeAsync(long revision, string markdown) =>
        session.ProcessMessageAsync(Message("note.changed", note.Id, revision, new { markdown }), Source);

    [Fact]
    public async Task Ready_loads_the_note_and_hides_the_loading_screen()
    {
        await ReadyAsync();

        var load = host.Sent("note.load");
        Assert.Equal(note.Markdown, load.GetProperty("payload").GetProperty("markdown").GetString());
        Assert.Equal(note.Title, load.GetProperty("payload").GetProperty("title").GetString());
        Assert.Equal("yellow", host.Sent("ui.appearance").GetProperty("payload").GetProperty("color").GetString());
        Assert.True(host.LoadingHidden);
        Assert.True(session.IsEditorReady);
    }

    [Fact]
    public async Task Untrusted_source_is_ignored()
    {
        await session.EnsureInitializedAsync();

        await session.ProcessMessageAsync(Message("editor.ready", Guid.Empty, 0, new { }), "https://devpostit.local/index.html");

        Assert.DoesNotContain(host.Types, type => type == "note.load");
        Assert.False(session.IsEditorReady);
    }

    [Fact]
    public async Task Changes_are_saved_to_the_markdown_file()
    {
        await ReadyAsync();

        await ChangeAsync(1, "# 바뀐 내용\n");
        await workspace.Service.FlushAsync(note.Id, CancellationToken.None);

        Assert.Contains("# 바뀐 내용", await File.ReadAllTextAsync(Path.Combine(root, "notes", note.RelativePath)));
    }

    [Fact]
    public async Task Preferences_are_sent_once_after_ready()
    {
        session.PendingPreferences = "{}";

        await ReadyAsync();
        await session.ProcessMessageAsync(Message("editor.ready", Guid.Empty, 0, new { }), Source);

        Assert.Single(host.Types, type => type == "ui.preferences");
    }

    [Theory]
    [InlineData("window.collapseRequested")]
    [InlineData("note.newRequested")]
    [InlineData("window.settingsRequested")]
    [InlineData("window.searchRequested")]
    [InlineData("window.captureRequested")]
    public async Task Window_requests_raise_their_event(string type)
    {
        var raised = new List<string>();
        session.CollapseRequested += () => raised.Add("window.collapseRequested");
        session.NewNoteRequested += () => raised.Add("note.newRequested");
        session.SettingsRequested += () => raised.Add("window.settingsRequested");
        session.SearchRequested += () => raised.Add("window.searchRequested");
        session.CaptureRequested += () => raised.Add("window.captureRequested");
        await ReadyAsync();

        await session.ProcessMessageAsync(Message(type, note.Id, 0, new { }), Source);

        Assert.Equal([type], raised);
    }

    [Fact]
    public async Task Link_opens_only_after_confirmation()
    {
        await ReadyAsync();
        var message = Message("note.openLinkRequested", note.Id, 0, new { url = "https://example.com/" });

        host.AllowLink = false;
        await session.ProcessMessageAsync(message, Source);
        Assert.Empty(host.OpenedLinks);

        host.AllowLink = true;
        await session.ProcessMessageAsync(message, Source);
        Assert.Equal(["https://example.com/"], host.OpenedLinks);
    }

    [Fact]
    public async Task Rename_changes_the_title_and_reports_back()
    {
        await ReadyAsync();

        await session.ProcessMessageAsync(Message("note.renameRequested", note.Id, 0, new { title = "새 이름" }), Source);

        Assert.Equal("새 이름", host.Sent("note.renamed").GetProperty("payload").GetProperty("title").GetString());
        Assert.Contains("새 이름", workspace.Notes.Single().RelativePath);
    }

    [Fact]
    public async Task Appearance_is_applied_and_reported()
    {
        (string, bool)? reported = null;
        session.AppearanceChanged += (color, topmost) => reported = (color, topmost);
        await ReadyAsync();

        await session.ProcessMessageAsync(Message("window.appearanceChanged", note.Id, 0, new { color = "blue", topmost = true }), Source);

        Assert.Equal(("blue", true), reported);
        Assert.Equal("blue", host.NoteColor);
        Assert.True(host.Topmost);
    }

    [Fact]
    public async Task Failure_while_handling_a_message_is_shown_as_conflict()
    {
        await ReadyAsync();
        host.ClipboardPng = null;

        await session.ProcessMessageAsync(Message("note.imagePasteRequested", note.Id, 0, new { }), Source);

        Assert.Contains("클립보드", host.Sent("note.conflict").GetProperty("payload").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Close_saves_the_latest_draft_and_keeps_the_note()
    {
        await ReadyAsync();
        host.DraftJson = JsonSerializer.Serialize(new { markdown = "# 닫기 직전 입력\n", revision = 5, dirty = true, title = note.Title });

        Assert.True(await session.RequestCloseAsync());

        Assert.True(host.Closed);
        Assert.Contains("# 닫기 직전 입력", await File.ReadAllTextAsync(Path.Combine(root, "notes", note.RelativePath)));
        Assert.Empty(workspace.TrashedNotes);
        Assert.Equal(0, host.TrashConfirmations);
    }

    [Fact]
    public async Task Close_is_cancelled_when_the_draft_cannot_be_captured()
    {
        await ReadyAsync();
        await ChangeAsync(3, "# 저장한 내용\n");
        host.DraftJson = JsonSerializer.Serialize(new { markdown = "# 다른 내용\n", revision = 3, dirty = true, title = note.Title });

        Assert.False(await session.RequestCloseAsync());

        Assert.False(host.Closed);
        Assert.True(host.Uncaptured);
        Assert.True(host.InputEnabled);
    }

    [Fact]
    public async Task Trash_declined_keeps_the_window()
    {
        await ReadyAsync();
        host.ConfirmTrashAnswer = false;

        Assert.False(await session.RequestDeleteAsync());

        Assert.False(host.Closed);
        Assert.Empty(workspace.TrashedNotes);
    }

    [Fact]
    public async Task Trash_confirmed_saves_the_latest_draft_first()
    {
        await ReadyAsync();
        host.ConfirmTrashAnswer = true;
        host.DraftJson = JsonSerializer.Serialize(new { markdown = "# 휴지통 직전 입력\n", revision = 7, dirty = true, title = note.Title });

        Assert.True(await session.RequestDeleteAsync());

        Assert.True(host.Closed);
        Assert.Equal(note.Id, Assert.Single(workspace.TrashedNotes).Id);
        var restored = await workspace.RestoreAsync(note.Id, CancellationToken.None);
        Assert.Contains("# 휴지통 직전 입력", restored.Markdown);
    }

    [Fact]
    public async Task User_close_asks_to_trash_but_a_preserving_close_does_not()
    {
        await ReadyAsync();
        host.ConfirmTrashAnswer = false;

        session.MarkUserClose();
        await session.HandleCloseRequestAsync();
        Assert.Equal(1, host.TrashConfirmations);
        Assert.False(host.Closed);

        Assert.True(await session.RequestCloseAsync());
        Assert.Equal(1, host.TrashConfirmations);
    }

    [Fact]
    public async Task Messages_during_a_cancelled_close_are_processed_afterwards()
    {
        await ReadyAsync();
        await ChangeAsync(3, "# 저장한 내용\n");
        host.DraftJson = JsonSerializer.Serialize(new { markdown = "# 다른 내용\n", revision = 3, dirty = true, title = note.Title });
        host.DuringClose = () => session.ProcessMessageAsync(Message("note.changed", note.Id, 4, new { markdown = "# 닫는 중 입력\n" }), Source);

        Assert.False(await session.RequestCloseAsync());

        // 지연된 메시지는 닫기 취소 뒤 비동기로 처리됩니다.
        for (var attempt = 0; attempt < 100 && (await workspace.Service.GetStateAsync(note.Id, CancellationToken.None)).Revision < 4; attempt++) await Task.Delay(20);
        Assert.Equal("# 닫는 중 입력\n", (await workspace.Service.GetStateAsync(note.Id, CancellationToken.None)).Note.Markdown);
    }

    [Fact]
    public async Task Closed_session_ignores_messages()
    {
        await ReadyAsync();
        Assert.True(await session.RequestCloseAsync());
        var before = host.Types.Count;

        await session.ProcessMessageAsync(Message("editor.ready", Guid.Empty, 0, new { }), Source);

        Assert.Equal(before, host.Types.Count);
        Assert.False(session.CanCollapse);
    }

    private sealed class FakeHost : IWebSurface, INoteWindow, INoteDialogs, INoteShell
    {
        private readonly List<string> sent = [];
        public NoteSession? Session { get; set; }
        public IReadOnlyList<string> Types => sent.Select(json => JsonDocument.Parse(json).RootElement.GetProperty("type").GetString()!).ToArray();
        public JsonElement Sent(string type) => sent.Select(json => JsonDocument.Parse(json).RootElement).First(root => root.GetProperty("type").GetString() == type);
        public bool LoadingHidden { get; private set; }
        public bool Closed { get; private set; }
        public bool Started { get; private set; }
        public bool InputEnabled { get; private set; } = true;
        public bool Uncaptured { get; private set; }
        public bool RecoveryAsked { get; private set; }
        public bool AllowLink { get; set; }
        public bool ConfirmTrashAnswer { get; set; }
        public int TrashConfirmations { get; private set; }
        public string DraftJson { get; set; } = "null";
        public byte[]? ClipboardPng { get; set; }
        public Func<Task>? DuringClose { get; set; }
        public RecoveryChoice Recovery { get; set; } = RecoveryChoice.Discard;
        public List<string> OpenedLinks { get; } = [];
        public string NoteColor { get; private set; } = NoteColors.Default;
        public bool Topmost { get; private set; }
        public void Reset() { sent.Clear(); Closed = false; Started = false; LoadingHidden = false; InputEnabled = true; }

        public Task PrepareAsync() => Task.CompletedTask;
        public void Start(Func<string, string, Task> onMessage) => Started = true;
        public void PostMessage(string json) => sent.Add(json);
        public async Task<string> EvaluateAsync(string expression)
        {
            if (DuringClose is { } during) { DuringClose = null; await during(); }
            return DraftJson;
        }
        public Task<bool> PrintToPdfAsync(string path) => Task.FromResult(true);

        public void ApplyAppearance(string color, bool topmost) { NoteColor = color; Topmost = topmost; }
        public void SetNoteTitle(string title) { }
        public void HideLoading() => LoadingHidden = true;
        public void ShowStartupError(string message) { }
        public void SetInputEnabled(bool enabled) => InputEnabled = enabled;
        public void BringToFront() { }
        public void Post(Action action) => action();
        public void Close()
        {
            if (Session!.AllowClose) { Closed = true; Session.OnWindowClosed(); return; }
            _ = Session.HandleCloseRequestAsync();
        }

        public bool ConfirmTrash() { TrashConfirmations++; return ConfirmTrashAnswer; }
        public RecoveryChoice AskRecovery() { RecoveryAsked = true; return Recovery; }
        public bool ReportCloseFailure(bool uncaptured, string error) { Uncaptured |= uncaptured; return false; }
        public Task ReportTrashFailureAsync(string error) => Task.CompletedTask;
        public bool ConfirmLink(string url) => AllowLink;

        public void OpenLink(string url) => OpenedLinks.Add(url);
        public void OpenFolder(string path) { }
        public string? FindDiagramApp() => null;
        public void OpenDiagram(string application, string path) { }
        public void ReportDiagramAppMissing() { }
        public byte[]? ReadClipboardPng() => ClipboardPng;
        public string? PickPdfPath(string title) => null;
    }
}
