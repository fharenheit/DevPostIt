using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DevPostIt.EditorHost;
using DevPostIt.Storage;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Color = System.Windows.Media.Color;

namespace DevPostIt.App;

// 메모 전체 검색 창입니다. 페이지는 검색어와 메모 ID만 보내며 파일 경로는 다루지 않습니다.
public sealed class SearchWindow : Window
{
    private const int MaxResults = 50;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal WebView2 View { get; } = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 253, 245) };
    private long latestQuery;
    public SearchWindow(NoteWorkspace workspace, Action<Guid> open)
    {
        Title = "메모 검색";
        Icon = NoteIcon.WindowImage;
        Width = 480; Height = 560; MinWidth = 320; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(255, 253, 245));
        Content = View;
        SourceInitialized += (_, _) => MainWindow.ApplyTitleBarColors(new WindowInteropHelper(this).Handle);
        Loaded += async (_, _) =>
        {
            try
            {
                var core = await LocalWebView.ConfigureAsync(View, EditorOrigin.IsSearchPage);
                core.WebMessageReceived += async (_, args) => await ReceiveAsync(core, workspace, open, args.WebMessageAsJson, args.Source);
                core.Navigate(EditorOrigin.SearchPage);
            }
            catch (Exception error) { Content = new TextBlock { Text = $"검색을 열지 못했습니다.\n{error.Message}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) }; }
        };
        Closed += (_, _) => View.Dispose();
    }

    private async Task ReceiveAsync(CoreWebView2 core, NoteWorkspace workspace, Action<Guid> open, string json, string source)
    {
        if (SearchMessage.TryReadOpen(json, source, out var id)) { open(id); return; }
        if (!SearchMessage.TryReadQuery(json, source, out var query)) return;
        var ticket = ++latestQuery;
        object message;
        try
        {
            var found = await Task.Run(() => workspace.SearchAsync(query, MaxResults, CancellationToken.None));
            message = new { type = "search.results", query, results = found.Select(item => new { id = item.Note.Id, title = item.Note.Title, excerpt = item.Excerpt, updated = item.Note.Updated }) };
        }
        catch (Exception error) { message = new { type = "search.failed", query, message = error.Message }; }
        if (ticket == latestQuery && IsLoaded) core.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }
}
