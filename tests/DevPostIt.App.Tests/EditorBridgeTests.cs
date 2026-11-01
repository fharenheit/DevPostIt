using System.Text.Json;
using DevPostIt.Core;
using DevPostIt.EditorHost;

namespace DevPostIt.App.Tests;

public class EditorBridgeTests
{
    private static readonly Guid Id = Guid.Parse("018f5df0-7e6c-7ad0-a8c1-9b67490da11b");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static EditorMessage Change(long revision = 8) => new(1, "note.changed", "request-1", Id, revision, JsonSerializer.SerializeToElement(new { markdown = "한글 본문" }));
    private static string Serialize(EditorMessage message) => JsonSerializer.Serialize(message, Json);

    [Fact]
    public async Task ValidChangeEmitsTypedNoteChangeAndUpdatesRevision()
    {
        var bridge = new EditorBridge(Id, 7);
        var result = await bridge.ReceiveAsync(Serialize(Change()), EditorOrigin.Page, default);
        Assert.Equal(new NoteChange(Id, 8, "한글 본문"), result.Change);
        Assert.Equal(8, bridge.Revision);
    }

    [Fact]
    public async Task ReadyAndSaveRequestAreValidatedWithoutPretendingToSave()
    {
        var bridge = new EditorBridge(Id, 7);
        var ready = new EditorMessage(1, "editor.ready", "ready", Guid.Empty, 0, JsonSerializer.SerializeToElement(new { }));
        Assert.Null((await bridge.ReceiveAsync(Serialize(ready), EditorOrigin.Page, default)).Change);
        Assert.Null(await bridge.HandleAsync(Change(7) with { Type = "note.saveRequested" }, default));
        Assert.Equal(7, bridge.Revision);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("type")]
    [InlineData("note")]
    [InlineData("stale")]
    [InlineData("negative")]
    [InlineData("unsafe-number")]
    [InlineData("request")]
    [InlineData("payload")]
    public async Task InvalidMessageIsRejectedWithoutChangingState(string field)
    {
        var message = field switch
        {
            "version" => Change() with { ProtocolVersion = 2 },
            "type" => Change() with { Type = "execute.script" },
            "note" => Change() with { NoteId = Guid.NewGuid() },
            "stale" => Change(7),
            "negative" => Change(-1),
            "unsafe-number" => Change(9_007_199_254_740_992),
            "request" => Change() with { RequestId = "" },
            _ => Change() with { Payload = JsonSerializer.SerializeToElement(new { markdown = 123 }) }
        };
        var bridge = new EditorBridge(Id, 7);
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.ReceiveAsync(Serialize(message), EditorOrigin.Page, default));
        Assert.Equal(7, bridge.Revision);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"protocolVersion\":1,\"protocolVersion\":2}")]
    public async Task MalformedJsonIsRejected(string json)
    {
        await Assert.ThrowsAsync<EditorProtocolException>(() => new EditorBridge(Id, 7).ReceiveAsync(json, EditorOrigin.Page, default));
    }

    [Fact]
    public async Task UnknownAndDuplicateFieldsAreRejected()
    {
        var raw = Serialize(Change());
        foreach (var json in new[] { raw[..^1] + ",\"extra\":1}", raw.Replace("\"markdown\":", "\"markdown\":\"first\",\"markdown\":"), raw.Replace("\"markdown\":", "\"path\":\"C:/unsafe\",\"markdown\":") })
            await Assert.ThrowsAsync<EditorProtocolException>(() => new EditorBridge(Id, 7).ReceiveAsync(json, EditorOrigin.Page, default));
    }

    [Fact]
    public async Task OversizedUtf8AndUntrustedSourceAreRejected()
    {
        var large = Change() with { Payload = JsonSerializer.SerializeToElement(new { markdown = new string('한', 400_000) }) };
        await Assert.ThrowsAsync<EditorProtocolException>(() => new EditorBridge(Id, 7).ReceiveAsync(Serialize(large), EditorOrigin.Page, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => new EditorBridge(Id, 7).ReceiveAsync(Serialize(Change()), "https://evil.example/", default));
    }

    [Theory]
    [InlineData("https://devpostit.local/index.html", true)]
    [InlineData("https://devpostit.local/index.html#unsafe", false)]
    [InlineData("https://devpostit.local/index.html?file=x", false)]
    [InlineData("http://devpostit.local/index.html", false)]
    [InlineData("https://devpostit.local:444/index.html", false)]
    [InlineData("https://devpostit.local.evil/index.html", false)]
    [InlineData("https://user@devpostit.local/index.html", false)]
    [InlineData("file:///C:/notes.md", false)]
    [InlineData("javascript:alert(1)", false)]
    public void NavigationIsRestrictedToBundledPage(string uri, bool expected) => Assert.Equal(expected, EditorOrigin.IsTrustedPage(uri));

    [Theory]
    [InlineData("https://devpostit.local/settings.html", true)]
    [InlineData("https://devpostit.local/settings.html?x=1", false)]
    [InlineData("https://devpostit.local/index.html", false)]
    [InlineData("https://example.com/settings.html", false)]
    public void SettingsPageIsSeparateFromNoteBridge(string uri, bool expected)
    {
        Assert.Equal(expected, EditorOrigin.IsSettingsPage(uri));
        Assert.False(EditorOrigin.IsTrustedPage(EditorOrigin.SettingsPage));
    }

    [Fact]
    public async Task BridgeRejectsMessagesFromSettingsPage()
    {
        var bridge = new EditorBridge(Id, 7);
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.ReceiveAsync(Serialize(Change()), EditorOrigin.SettingsPage, default));
    }

    [Fact]
    public async Task SettingsRequestCarriesNoChangeAndChecksRevision()
    {
        var bridge = new EditorBridge(Id, 7);
        var request = Change(7) with { Type = "window.settingsRequested", Payload = JsonSerializer.SerializeToElement(new { }) };
        Assert.Null(await bridge.HandleAsync(request, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(request with { Revision = 6 }, default));
    }

    [Theory]
    [InlineData("""{"protocolVersion":1,"type":"settings.openDataFolder"}""", EditorOrigin.SettingsPage, true)]
    [InlineData("""{"protocolVersion":1,"type":"settings.openDataFolder"}""", EditorOrigin.Page, false)]
    [InlineData("""{"protocolVersion":1,"type":"settings.openDataFolder"}""", "https://example.com/settings.html", false)]
    [InlineData("""{"protocolVersion":2,"type":"settings.openDataFolder"}""", EditorOrigin.SettingsPage, false)]
    [InlineData("""{"protocolVersion":1,"type":"note.changed"}""", EditorOrigin.SettingsPage, false)]
    [InlineData("""{"protocolVersion":1,"type":"settings.openDataFolder","path":"C:\\Windows"}""", EditorOrigin.SettingsPage, false)]
    [InlineData("""{"protocolVersion":1}""", EditorOrigin.SettingsPage, false)]
    [InlineData("""{"protocolVersion":"1","type":"settings.openDataFolder"}""", EditorOrigin.SettingsPage, false)]
    [InlineData("""[]""", EditorOrigin.SettingsPage, false)]
    [InlineData("""not json""", EditorOrigin.SettingsPage, false)]
    public void OpenDataFolderRequestAcceptsOnlyExactMessageFromSettingsPage(string json, string source, bool expected) =>
        Assert.Equal(expected, SettingsMessage.IsOpenDataFolder(json, source));

    [Fact]
    public void OpenDataFolderRequestRejectsOversizedMessage() =>
        Assert.False(SettingsMessage.IsOpenDataFolder(new string(' ', 2048) + """{"protocolVersion":1,"type":"settings.openDataFolder"}""", EditorOrigin.SettingsPage));

    [Theory]
    [InlineData("""{"protocolVersion":1,"type":"settings.changeDataFolder"}""", true)]
    [InlineData("""{"protocolVersion":1,"type":"settings.changeDataFolder","path":"C:\\x"}""", false)]
    public void ChangeDataFolderCarriesNoPath(string json, bool expected) => Assert.Equal(expected, SettingsMessage.IsChangeDataFolder(json, EditorOrigin.SettingsPage));

    [Theory]
    [InlineData("""{"protocolVersion":1,"type":"settings.startInTray","value":true}""", true, true)]
    [InlineData("""{"protocolVersion":1,"type":"settings.startInTray","value":false}""", true, false)]
    [InlineData("""{"protocolVersion":1,"type":"settings.startInTray","value":"yes"}""", false, false)]
    [InlineData("""{"protocolVersion":1,"type":"settings.startInTray"}""", false, false)]
    public void StartInTrayNeedsBoolean(string json, bool accepted, bool value)
    {
        Assert.Equal(accepted, SettingsMessage.TryReadStartInTray(json, EditorOrigin.SettingsPage, out var read));
        Assert.Equal(value, read);
        Assert.False(SettingsMessage.TryReadStartInTray(json, EditorOrigin.Page, out _));
    }

    [Fact]
    public void SearchMessagesAcceptOnlySearchPageAndValidFields()
    {
        Assert.True(SearchMessage.TryReadQuery("""{"protocolVersion":1,"type":"search.query","query":"메모"}""", EditorOrigin.SearchPage, out var query));
        Assert.Equal("메모", query);
        Assert.False(SearchMessage.TryReadQuery("""{"protocolVersion":1,"type":"search.query","query":"메모"}""", EditorOrigin.SettingsPage, out _));
        Assert.False(SearchMessage.TryReadQuery("""{"protocolVersion":1,"type":"search.query","query":1}""", EditorOrigin.SearchPage, out _));
        Assert.False(SearchMessage.TryReadQuery($$"""{"protocolVersion":1,"type":"search.query","query":"{{new string('a', 201)}}"}""", EditorOrigin.SearchPage, out _));
        var id = Guid.NewGuid();
        Assert.True(SearchMessage.TryReadOpen($$"""{"protocolVersion":1,"type":"search.open","id":"{{id}}"}""", EditorOrigin.SearchPage, out var read));
        Assert.Equal(id, read);
        Assert.False(SearchMessage.TryReadOpen("""{"protocolVersion":1,"type":"search.open","id":"C:\x"}""", EditorOrigin.SearchPage, out _));
        Assert.False(SearchMessage.TryReadOpen($$"""{"protocolVersion":1,"type":"search.open","id":"{{Guid.Empty}}"}""", EditorOrigin.SearchPage, out _));
    }

    [Theory]
    [InlineData("https://devpostit-assets.local/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.png", true)]
    [InlineData("https://devpostit-assets.local/../secret.png", false)]
    [InlineData("https://devpostit-assets.local/0123456789abcdef0123456789abcdef/a.svg", false)]
    [InlineData("http://devpostit-assets.local/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.png", false)]
    [InlineData("https://example.com/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.png", false)]
    public void NoteAssetsAreRestricted(string uri, bool expected) => Assert.Equal(expected, EditorOrigin.IsNoteAsset(uri));

    [Theory]
    [InlineData("yellow", true, true)]
    [InlineData("blue", false, true)]
    [InlineData("red", false, false)]
    public async Task AppearanceMessageAllowsOnlyKnownColors(string color, bool topmost, bool accepted)
    {
        var bridge = new EditorBridge(Id, 7);
        var message = Change(7) with { Type = "window.appearanceChanged", Payload = JsonSerializer.SerializeToElement(new { color, topmost }) };
        if (accepted) Assert.Null(await bridge.HandleAsync(message, default));
        else await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(message, default));
    }

    [Fact]
    public async Task RevisionCanFollowExternalReload()
    {
        var bridge = new EditorBridge(Id, 7);
        bridge.SyncRevision(8);
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(Change(8), default));
        Assert.NotNull(await bridge.HandleAsync(Change(9), default));
    }

    [Fact]
    public void BackupAndUpdateRequestsCarryNoPathAndNeedSettingsPage()
    {
        Assert.True(SettingsMessage.IsBackup("""{"protocolVersion":1,"type":"settings.backup"}""", EditorOrigin.SettingsPage));
        Assert.True(SettingsMessage.IsRestore("""{"protocolVersion":1,"type":"settings.restore"}""", EditorOrigin.SettingsPage));
        Assert.True(SettingsMessage.IsCheckUpdate("""{"protocolVersion":1,"type":"settings.checkUpdate"}""", EditorOrigin.SettingsPage));
        Assert.False(SettingsMessage.IsRestore("""{"protocolVersion":1,"type":"settings.restore","path":"C:\\a.zip"}""", EditorOrigin.SettingsPage));
        Assert.False(SettingsMessage.IsBackup("""{"protocolVersion":1,"type":"settings.backup"}""", EditorOrigin.Page));
        Assert.True(SettingsMessage.TryReadAutoUpdate("""{"protocolVersion":1,"type":"settings.autoUpdate","value":false}""", EditorOrigin.SettingsPage, out var value));
        Assert.False(value);
        Assert.False(SettingsMessage.TryReadAutoUpdate("""{"protocolVersion":1,"type":"settings.autoUpdate","value":1}""", EditorOrigin.SettingsPage, out _));
    }

    [Fact]
    public async Task PdfRequestCarriesNoPath()
    {
        var bridge = new EditorBridge(Id, 7);
        var request = Change(7) with { Type = "note.exportPdfRequested", Payload = JsonSerializer.SerializeToElement(new { }) };
        Assert.Null(await bridge.HandleAsync(request, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(request with { Payload = JsonSerializer.SerializeToElement(new { path = "C:\\a.pdf" }) }, default));
    }

    [Fact]
    public void HelpIsWrittenInFormalKoreanAndCoversFeatures()
    {
        Assert.NotEmpty(HelpContent.Sections);
        foreach (var section in HelpContent.Sections)
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Title));
            Assert.NotEmpty(section.Paragraphs);
            foreach (var paragraph in section.Paragraphs)
                foreach (var sentence in paragraph.Split(". ", StringSplitOptions.RemoveEmptyEntries))
                    Assert.True(System.Text.RegularExpressions.Regex.IsMatch(sentence.TrimEnd('.'), "(니다|십시오)$"), $"합니다체가 아닙니다: {sentence}");
        }
        var text = string.Join('\n', HelpContent.Sections.SelectMany(section => section.Paragraphs.Prepend(section.Title)));
        foreach (var topic in new[] { "휴지통", "백업", "복원", "검색", "PDF", "찾기", "바꾸기", "단축키", "미리보기", "글꼴", "업데이트", "표", "이미지", "항상 위", "데이터 폴더", "캡처", "이미지 폴더" })
            Assert.Contains(topic, text);
    }

    [Theory]
    [InlineData(10.2, 20.7, 100.5, 50.5, 10, 20, 101, 52)]
    [InlineData(-50, -50, 100, 100, 0, 0, 50, 50)]
    [InlineData(1900, 1000, 500, 500, 1900, 1000, 20, 80)]
    public void CaptureRectIsClampedToScreen(double x, double y, double width, double height, int ex, int ey, int ew, int eh) =>
        Assert.Equal(new ScreenRect(ex, ey, ew, eh), CaptureGeometry.ClampToScreen(x, y, width, height, new ScreenRect(0, 0, 1920, 1080)));

    [Theory]
    [InlineData(10, 10, 110, 60)]
    [InlineData(110, 60, 10, 10)]
    [InlineData(110, 10, 10, 60)]
    public void DragRectIsSameInAnyDirection(int startX, int startY, int endX, int endY) =>
        Assert.Equal(new ScreenRect(10, 10, 100, 50), CaptureGeometry.FromDrag(startX, startY, endX, endY, new ScreenRect(0, 0, 1920, 1080)));

    [Fact]
    public void TinyDragIsRejected() => Assert.Null(CaptureGeometry.FromDrag(10, 10, 12, 12, new ScreenRect(0, 0, 1920, 1080)));

    [Theory]
    [InlineData("""{"protocolVersion":1,"type":"settings.captureDelay","value":5}""", true, 5)]
    [InlineData("""{"protocolVersion":1,"type":"settings.captureDelay","value":0}""", true, 0)]
    [InlineData("""{"protocolVersion":1,"type":"settings.captureDelay","value":7}""", false, 0)]
    [InlineData("""{"protocolVersion":1,"type":"settings.captureDelay","value":"5"}""", false, 0)]
    public void CaptureDelayAllowsOnlyListedValues(string json, bool accepted, int value)
    {
        Assert.Equal(accepted, SettingsMessage.TryReadCaptureDelay(json, EditorOrigin.SettingsPage, out var seconds));
        Assert.Equal(value, seconds);
        Assert.False(SettingsMessage.TryReadCaptureDelay(json, EditorOrigin.Page, out _));
    }

    [Theory]
    [InlineData("https://example.com/a?b=1", true)]
    [InlineData("http://localhost:3000/", true)]
    [InlineData("mailto:me@example.com", true)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("C:\\Windows\\notepad.exe", false)]
    [InlineData("a.md", false)]
    [InlineData("https://bank.example@evil.example/", false)]
    [InlineData("https://example.com/a b", false)]
    [InlineData("https://example.com/\n", false)]
    [InlineData("", false)]
    public void PreviewLinkAllowsOnlyWebAndMail(string url, bool allowed) => Assert.Equal(allowed, PreviewLink.IsAllowed(url));

    [Fact]
    public void PreviewLinkRejectsVeryLongAddress() => Assert.False(PreviewLink.IsAllowed("https://example.com/" + new string('a', 2100)));

    [Fact]
    public async Task OpenLinkRequestChecksAddressFieldsAndRevision()
    {
        var bridge = new EditorBridge(Id, 7);
        var request = Change(7) with { Type = "note.openLinkRequested", Payload = JsonSerializer.SerializeToElement(new { url = "https://example.com" }) };
        Assert.Null(await bridge.HandleAsync(request, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(request with { Revision = 6 }, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(request with { Payload = JsonSerializer.SerializeToElement(new { url = "file:///C:/a" }) }, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(request with { Payload = JsonSerializer.SerializeToElement(new { url = 1 }) }, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(request with { Payload = JsonSerializer.SerializeToElement(new { url = "https://example.com", extra = 1 }) }, default));
    }

    [Fact]
    public void CleanImagesAcceptsOnlyThePlainRequestFromSettings()
    {
        const string plain = """{"protocolVersion":1,"type":"settings.cleanImages"}""";
        Assert.True(SettingsMessage.IsCleanImages(plain, EditorOrigin.SettingsPage));
        Assert.False(SettingsMessage.IsCleanImages(plain, EditorOrigin.Page));
        Assert.False(SettingsMessage.IsCleanImages("""{"protocolVersion":1,"type":"settings.cleanImages","path":"C:\\a"}""", EditorOrigin.SettingsPage));
    }

    [Fact]
    public async Task OpenImagesRequestCarriesNothingAndChecksRevision()
    {
        var bridge = new EditorBridge(Id, 7);
        var request = Change(7) with { Type = "note.openImagesRequested", Payload = JsonSerializer.SerializeToElement(new { }) };
        Assert.Null(await bridge.HandleAsync(request, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(request with { Revision = 6 }, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(request with { Payload = JsonSerializer.SerializeToElement(new { path = "C:\\Windows" }) }, default));
    }

    [Fact]
    public async Task CaptureRequestCarriesNothingAndChecksRevision()
    {
        var bridge = new EditorBridge(Id, 7);
        var request = Change(7) with { Type = "window.captureRequested", Payload = JsonSerializer.SerializeToElement(new { }) };
        Assert.Null(await bridge.HandleAsync(request, default));
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.HandleAsync(request with { Revision = 6 }, default));
    }

    [Fact]
    public void CaptureRectWorksOnMonitorsLeftOfPrimary() =>
        Assert.Equal(new ScreenRect(-1920, 0, 100, 100), CaptureGeometry.ClampToScreen(-1920, 0, 100, 100, new ScreenRect(-1920, 0, 3840, 1080)));

    [Theory]
    [InlineData(0, 0, 0, 10)]
    [InlineData(0, 0, 3, 3)]
    [InlineData(5000, 5000, 100, 100)]
    [InlineData(double.NaN, 0, 10, 10)]
    [InlineData(0, 0, double.PositiveInfinity, 10)]
    [InlineData(0, 0, -10, 10)]
    public void InvalidCaptureRectIsRejected(double x, double y, double width, double height) =>
        Assert.Null(CaptureGeometry.ClampToScreen(x, y, width, height, new ScreenRect(0, 0, 1920, 1080)));

    [Fact]
    public void AssetsStayOnTrustedOrigin()
    {
        Assert.True(EditorOrigin.IsLocalResource("https://devpostit.local/assets/editor.worker.js"));
        Assert.False(EditorOrigin.IsLocalResource("https://example.com/font.woff2"));
    }

    [Fact]
    public void ClosingDraftMustBeCompleteValidAndWithinMessageLimit()
    {
        var valid = JsonSerializer.Serialize(new { markdown = "종료 직전 입력", revision = 9, dirty = true });
        Assert.Equal("종료 직전 입력", EditorDraftSnapshot.Parse(valid).Markdown);
        var oversized = JsonSerializer.Serialize(new { markdown = new string('x', EditorBridge.MaxMessageBytes + 1), revision = 9, dirty = true });
        Assert.Throws<EditorProtocolException>(() => EditorDraftSnapshot.Parse(oversized));
        Assert.Throws<EditorProtocolException>(() => EditorDraftSnapshot.Parse("null"));
        Assert.Throws<EditorProtocolException>(() => EditorDraftSnapshot.Parse("{}"));
    }

    [Fact]
    public async Task TitleTravelsWithBodyAndClosingSnapshot()
    {
        var message = Change() with { Payload = JsonSerializer.SerializeToElement(new { markdown = "본문", title = "새 이름" }) };
        var result = await new EditorBridge(Id, 7).ReceiveAsync(Serialize(message), EditorOrigin.Page, default);
        Assert.Equal("새 이름", result.Change!.Title);
        var snapshot = EditorDraftSnapshot.Parse(JsonSerializer.Serialize(new { markdown = "본문", title = "새 이름", revision = 8, dirty = true }));
        Assert.Equal("새 이름", snapshot.Title);
        foreach (var title in new[] { " ", new string('가', 201) })
            await Assert.ThrowsAsync<EditorProtocolException>(() => new EditorBridge(Id, 7).ReceiveAsync(Serialize(message with { Payload = JsonSerializer.SerializeToElement(new { markdown = "본문", title }) }), EditorOrigin.Page, default));
    }

    [Fact]
    public async Task WindowCommandsRequireCurrentNoteRevisionAndEmptyPayload()
    {
        foreach (var type in new[] { "window.collapseRequested", "note.newRequested" })
        {
            var bridge = new EditorBridge(Id, 7);
            var message = Change() with { Type = type, Revision = 7, Payload = JsonSerializer.SerializeToElement(new { }) };
            Assert.Null((await bridge.ReceiveAsync(Serialize(message), EditorOrigin.Page, default)).Change);
            Assert.Equal(7, bridge.Revision);
            await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.ReceiveAsync(Serialize(message with { Revision = 6 }), EditorOrigin.Page, default));
            await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.ReceiveAsync(Serialize(message with { Payload = JsonSerializer.SerializeToElement(new { path = "x" }) }), EditorOrigin.Page, default));
        }
    }

    [Fact]
    public async Task CancelledMessageDoesNotChangeRevision()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var bridge = new EditorBridge(Id, 7);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bridge.ReceiveAsync(Serialize(Change()), EditorOrigin.Page, source.Token));
        Assert.Equal(7, bridge.Revision);
    }
}
