using System.Text.Json;
using HunminNotes.Core;
using HunminNotes.EditorHost;

namespace HunminNotes.App.Tests;

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
