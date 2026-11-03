using System.Text.Json;
using DevPostIt.EditorHost;

namespace DevPostIt.App.Tests;

public sealed class MacOriginTests
{
    private const string Ready = """{"protocolVersion":1,"type":"editor.ready","requestId":"r","noteId":"00000000-0000-0000-0000-000000000000","revision":0,"payload":{}}""";

    [Theory]
    [InlineData("devpostit://app/index.html", true)]
    [InlineData("devpostit://app/settings.html", false)]
    [InlineData("devpostit://app/index.html?x=1", false)]
    [InlineData("devpostit://app/index.html#x", false)]
    [InlineData("devpostit://evil/index.html", false)]
    [InlineData("devpostit://user@app/index.html", false)]
    [InlineData("devpostit-assets://notes/index.html", false)]
    [InlineData("https://devpostit.local/index.html", false)]
    [InlineData("file:///index.html", false)]
    public void Mac_page_trust_is_exact(string source, bool expected) => Assert.Equal(expected, EditorOrigin.IsTrustedMacPage(source));

    [Theory]
    [InlineData("devpostit://app/index.html", true)]
    [InlineData("devpostit://app/assets/main.js", true)]
    [InlineData("devpostit://other/index.html", false)]
    [InlineData("https://devpostit.local/index.html", false)]
    public void Mac_local_resource_is_exact(string source, bool expected) => Assert.Equal(expected, EditorOrigin.IsLocalMacResource(source));

    [Theory]
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.drawio.svg", true)]
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.svg", false)]
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/../../secret.png", false)]
    [InlineData("devpostit-assets://other/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.png", false)]
    [InlineData("https://devpostit-assets.local/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.png", false)]
    public void Mac_note_asset_is_exact(string source, bool expected) => Assert.Equal(expected, EditorOrigin.IsMacNoteAsset(source));

    [Fact]
    public async Task Bridge_uses_the_given_page_policy()
    {
        var bridge = new EditorBridge(Guid.NewGuid(), 0, EditorOrigin.IsTrustedMacPage);

        Assert.Equal("editor.ready", (await bridge.ReceiveAsync(Ready, "devpostit://app/index.html", CancellationToken.None)).Message.Type);
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.ReceiveAsync(Ready, EditorOrigin.Page, CancellationToken.None));
    }

    [Fact]
    public async Task Bridge_defaults_to_the_windows_page_policy()
    {
        var bridge = new EditorBridge(Guid.NewGuid(), 0);

        Assert.Equal("editor.ready", (await bridge.ReceiveAsync(Ready, EditorOrigin.Page, CancellationToken.None)).Message.Type);
        await Assert.ThrowsAsync<EditorProtocolException>(() => bridge.ReceiveAsync(Ready, "devpostit://app/index.html", CancellationToken.None));
    }
}
