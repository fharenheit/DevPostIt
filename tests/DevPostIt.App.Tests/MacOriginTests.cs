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
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.drawio.svg?v=1793692031906", true)]
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.drawio.svg?v=", false)]
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.drawio.svg?v=1&x=2", false)]
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.drawio.svg?x=1", false)]
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.drawio.svg?v=1#a", false)]
    [InlineData("devpostit-assets://notes/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.png?v=1", false)]
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

public sealed class MacCanonicalSourceTests
{
    [Theory]
    [InlineData("devpostit://app/settings.html", "https://devpostit.local/settings.html")]
    [InlineData("devpostit://app/todo.html", "https://devpostit.local/todo.html")]
    // 쿼리와 조각을 남겨 페이지 검사가 그대로 거부하게 합니다.
    [InlineData("devpostit://app/settings.html?x=1", "https://devpostit.local/settings.html?x=1")]
    [InlineData("devpostit://app/settings.html#x", "https://devpostit.local/settings.html#x")]
    public void Mac_source_maps_to_the_page_identity(string source, string expected) => Assert.Equal(expected, EditorOrigin.CanonicalMacSource(source));

    [Theory]
    [InlineData("devpostit://evil/settings.html")]
    [InlineData("devpostit://user@app/settings.html")]
    [InlineData("devpostit-assets://notes/settings.html")]
    [InlineData("https://devpostit.local/settings.html")]
    [InlineData("file:///settings.html")]
    [InlineData("not a url")]
    public void Other_sources_have_no_identity(string source) => Assert.Null(EditorOrigin.CanonicalMacSource(source));

    [Fact]
    public void Canonical_source_still_passes_only_its_own_page_check()
    {
        var settings = EditorOrigin.CanonicalMacSource("devpostit://app/settings.html")!;
        Assert.True(EditorOrigin.IsSettingsPage(settings));
        Assert.False(EditorOrigin.IsSearchPage(settings));
        Assert.False(EditorOrigin.IsSettingsPage(EditorOrigin.CanonicalMacSource("devpostit://app/settings.html?x=1")!));
    }

    [Fact]
    public void Page_address_maps_to_the_mac_scheme() => Assert.Equal("devpostit://app/mindmap.html", EditorOrigin.MacAddress(EditorOrigin.MindMapPage));
}

public sealed class MacHelpTests
{
    [Fact]
    public void Mac_help_has_no_windows_terms()
    {
        var text = string.Join("\n", HelpContent.ForMacOS.SelectMany(section => section.Paragraphs.Prepend(section.Title)));
        foreach (var term in new[] { "Ctrl", "Alt+", "Windows", "트레이", "탐색기", "작업 표시줄", "알림 영역", "PrintScreen", "LOCALAPPDATA", "OneDrive" })
            Assert.DoesNotContain(term, text);
        Assert.Contains("⌘F", text);
        Assert.Contains("⌃⌥F", text);
        Assert.Contains("⌃⇧⌘C", text);
        Assert.Contains("메뉴 막대", text);
    }

    [Fact]
    public void Mac_help_keeps_every_section() => Assert.Equal(HelpContent.Sections.Select(section => section.Title), HelpContent.ForMacOS.Select(section => section.Title));

    [Fact]
    public void Mac_help_stays_in_the_formal_style()
    {
        foreach (var paragraph in HelpContent.ForMacOS.SelectMany(section => section.Paragraphs))
            Assert.Matches("(니다|십시오)\\.$", paragraph);
    }
}
