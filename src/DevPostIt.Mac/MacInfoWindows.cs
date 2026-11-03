using System.Text.RegularExpressions;
using CoreGraphics;
using DevPostIt.EditorHost;

namespace DevPostIt.Mac;

// 글자만 있는 창의 공통 부분입니다.
internal abstract class MacTextWindow : NSWindow
{
    private static readonly NSColor Paper = NSColor.FromRgb(255, 253, 245);
    private static readonly NSColor Ink = NSColor.FromRgb(47, 42, 31);
    protected readonly NSMutableAttributedString Body = new();
    protected readonly nfloat TextSize;
    public event Action? WindowClosed;
    internal NSTextView Text { get; }

    protected MacTextWindow(string title, double width, double height, nfloat textSize)
        : base(new CGRect(0, 0, width, height), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
    {
        ReleaseWhenClosed(false);
        Title = title;
        TextSize = textSize;
        MinSize = new CGSize(320, 240);
        BackgroundColor = Paper;
        var scroll = new NSScrollView(ContentView!.Bounds) { HasVerticalScroller = true, DrawsBackground = false, AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable };
        Text = new NSTextView(scroll.ContentView.Bounds)
        {
            Editable = false, Selectable = true, DrawsBackground = false, TextContainerInset = new CGSize(16, 14),
            AutoresizingMask = NSViewResizingMask.WidthSizable, VerticallyResizable = true,
        };
        scroll.DocumentView = Text;
        ContentView.AddSubview(scroll);
        Center();
        WillClose += (_, _) => WindowClosed?.Invoke();
    }

    protected void Add(string text, bool bold = false, double after = 6, string? link = null)
    {
        var paragraph = new NSMutableParagraphStyle { ParagraphSpacing = (nfloat)after, LineSpacing = 3 };
        var attributes = new NSStringAttributes
        {
            Font = bold ? NSFont.BoldSystemFontOfSize(TextSize) : NSFont.SystemFontOfSize(TextSize),
            ForegroundColor = Ink,
            ParagraphStyle = paragraph,
        };
        if (link is not null) attributes.Dictionary[NSStringAttributeKey.Link] = new NSUrl(link);
        Body.Append(new NSAttributedString(text + "\n", attributes));
    }

    protected void Show() => Text.TextStorage!.SetString(Body);

    public void BringToFront()
    {
        NSApplication.SharedApplication.Activate();
        MakeKeyAndOrderFront(this);
    }
}

// 기능 안내 창입니다. 글자 크기는 메모 설정과 관계없이 13으로 고정합니다.
internal sealed class MacHelpWindow : MacTextWindow
{
    public MacHelpWindow() : base("도움말", 520, 640, 13)
    {
        foreach (var section in HelpContent.ForMacOS)
        {
            Add(section.Title, bold: true, after: 4);
            foreach (var paragraph in section.Paragraphs) Add(paragraph);
        }
        Show();
    }
}

// 제품 정보 창입니다. 정해진 저장소 주소만 엽니다.
internal sealed class MacAboutWindow : MacTextWindow
{
    public const string RepositoryUrl = "https://github.com/fharenheit/DevPostIt";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    private const string Developer = "Edward KIM (fharenheit)";
    private const string Components = ".NET, WebKit, Monaco Editor, Prettier, Mermaid, clang-format, Ruff, gofmt, shfmt, sql-formatter, markdown-it, DOMPurify, SQLite, D2Coding, JetBrains Mono";

    public MacAboutWindow() : base("정보", 420, 300, 14)
    {
        Add("Post It for Developer", bold: true, after: 2);
        Add("개발자를 위한 Markdown 포스트잇 메모", after: 12);
        Add($"버전 {MacNoteManager.CurrentVersion}", after: 4);
        Add($"개발자: {Developer}", after: 4);
        Add(RepositoryUrl, after: 4, link: RepositoryUrl);
        Add(ReleasesUrl, after: 12, link: ReleasesUrl);
        Add($"사용한 오픈소스: {Components}");
        Show();
    }
}

// 페이지에 글꼴 조회 권한을 주지 않고 앱이 설치된 글꼴 이름만 넘깁니다.
internal static partial class MacFonts
{
    private const int MaxFonts = 1000;
    [GeneratedRegex(@"^[\p{L}\p{N} ._-]{1,100}$")] private static partial Regex SafeName();

    public static (string[] Monospace, string[] All) Read()
    {
        var all = new List<string>();
        var monospace = new List<string>();
        foreach (var name in NSFontManager.SharedFontManager.AvailableFontFamilies.OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase))
        {
            if (all.Count >= MaxFonts) break;
            if (name.StartsWith('.') || !SafeName().IsMatch(name)) continue;
            all.Add(name);
            if (NSFont.FromFontName(name, 12)?.IsFixedPitch == true) monospace.Add(name);
        }
        return ([.. monospace], [.. all]);
    }
}

internal static class MacDiagramApp
{
    private const string BundleId = "com.jgraph.drawio.desktop";
    private const string FallbackPath = "/Applications/draw.io.app";
    public const string DownloadPage = "https://github.com/jgraph/drawio-desktop/releases/latest";

    public static string? Find()
    {
        var found = NSWorkspace.SharedWorkspace.UrlForApplication(BundleId)?.Path;
        return found ?? (Directory.Exists(FallbackPath) ? FallbackPath : null);
    }

    public static void Open(string application, string file)
    {
        var configuration = NSWorkspaceOpenConfiguration.Create();
        NSWorkspace.SharedWorkspace.OpenUrls([NSUrl.CreateFileUrl(file, false, null)], NSUrl.CreateFileUrl(application, true, null), configuration, null);
    }
}

// 클립보드의 이미지를 PNG로 읽고 씁니다. 시험은 별도 NSPasteboard를 넘깁니다.
internal static class MacClipboard
{
    private const string PngType = "public.png";
    private const string TiffType = "public.tiff";
    private const string FileUrlType = "public.file-url";

    public static byte[]? ReadPng(NSPasteboard board)
    {
        if (board.GetDataForType(PngType) is { } png) return png.ToArray();
        if (board.GetDataForType(TiffType) is { } tiff) return ToPng(tiff);
        var files = board.PasteboardItems?.Select(item => item.GetStringForType(FileUrlType)).Where(value => value is not null).ToArray() ?? [];
        if (files.Length != 1 || NSUrl.FromString(files[0]!)?.Path is not { } path || !File.Exists(path)) return null;
        using var data = NSData.FromFile(path);
        return data is null ? null : ToPng(data);
    }

    public static void WritePng(NSPasteboard board, byte[] png)
    {
        using var data = NSData.FromArray(png);
        board.ClearContents();
        board.SetDataForType(data, PngType);
    }

    private static byte[]? ToPng(NSData data)
    {
        using var image = NSBitmapImageRep.ImageRepFromData(data) as NSBitmapImageRep;
        return image?.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary())?.ToArray();
    }
}
