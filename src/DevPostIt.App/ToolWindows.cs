using System.ComponentModel;
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
using MessageBox = System.Windows.MessageBox;

namespace DevPostIt.App;

// 메모와 별개인 도구 창의 공통 부분입니다. 닫기 전에 페이지가 아직 보내지 않은 내용을 받아 저장합니다.
public abstract class ToolWindow : Window
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private bool flushed;
    private readonly TaskCompletionSource closedCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal WebView2 View { get; } = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 253, 245) };
    protected CoreWebView2? Core { get; private set; }

    protected ToolWindow(string title, string page, Func<string, bool> isAllowed, double width, double height)
    {
        Title = title;
        Icon = NoteIcon.WindowImage;
        Width = width; Height = height; MinWidth = 520; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(255, 253, 245));
        Content = View;
        SourceInitialized += (_, _) => MainWindow.ApplyTitleBarColors(new WindowInteropHelper(this).Handle);
        Loaded += async (_, _) =>
        {
            try
            {
                var core = await LocalWebView.ConfigureAsync(View, isAllowed);
                Core = core;
                core.WebMessageReceived += (_, args) => _ = ReceiveAsync(args.WebMessageAsJson, args.Source);
                core.Navigate(page);
            }
            catch (Exception error) { Content = new TextBlock { Text = $"창을 열지 못했습니다.\n{error.Message}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) }; flushed = true; }
        };
        Closing += OnClosing;
        Closed += (_, _) => { View.Dispose(); closedCompletion.TrySetResult(); };
    }
    protected abstract Task ReceiveAsync(string json, string source);
    protected virtual Task SavePendingAsync(string json) => Task.CompletedTask;
    protected void Post(object message)
    {
        if (IsLoaded && Core is { } core) core.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }
    internal Task FlushAndCloseAsync()
    {
        Close();
        return closedCompletion.Task;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (flushed || Core is null) return;
        e.Cancel = true;
        flushed = true;
        try
        {
            var result = await View.ExecuteScriptAsync("window.devpostitPending ? window.devpostitPending() : null");
            if (JsonSerializer.Deserialize<string?>(result) is { } pending) await SavePendingAsync(pending);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, $"마지막 변경을 저장하지 못했습니다.\n{error.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Close();
    }
}

public sealed class MindMapWindow : ToolWindow
{
    private readonly MindMapStore store;
    internal Func<string, bool> LinkConfirmation { get; set; }
    internal Action<string> LinkOpener { get; set; } = url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    internal Func<string, string?> PngPathPicker { get; set; }

    public MindMapWindow(MindMapStore store) : base("마인드맵", EditorOrigin.MindMapPage, EditorOrigin.IsMindMapPage, 1100, 720)
    {
        this.store = store;
        LinkConfirmation = url => MessageBox.Show(this, $"이 링크를 기본 브라우저로 열까요?\n\n{url}", "링크 열기", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        PngPathPicker = PickPngPath;
    }
    private string? PickPngPath(string name)
    {
        var safe = string.Concat(name.Select(character => System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "PNG로 저장", Filter = "PNG 그림 (*.png)|*.png", FileName = (safe.Length == 0 ? "마인드맵" : safe) + ".png" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
    protected override async Task ReceiveAsync(string json, string source)
    {
        if (!ToolMessage.TryReadMindMap(json, source, out var request)) return;
        var root = request!.Root;
        try
        {
            switch (request.Type)
            {
                case "mindmap.list":
                    Post(new { type = "mindmap.list", maps = await store.ListAsync(CancellationToken.None) });
                    break;
                case "mindmap.open":
                    Post(new { type = "mindmap.opened", map = await store.OpenAsync(root.GetProperty("id").GetString() ?? "", CancellationToken.None) });
                    break;
                case "mindmap.save":
                    var saved = await store.SaveAsync(root.GetProperty("map"), CancellationToken.None);
                    Post(new { type = "mindmap.saved", id = saved.Id, updated = saved.Updated });
                    break;
                case "mindmap.delete":
                    await store.DeleteAsync(root.GetProperty("id").GetString() ?? "", CancellationToken.None);
                    Post(new { type = "mindmap.list", maps = await store.ListAsync(CancellationToken.None) });
                    break;
                case "mindmap.openLink":
                    var url = root.GetProperty("url").GetString();
                    if (!PreviewLink.IsAllowed(url)) { Post(new { type = "mindmap.error", message = "웹·메일 주소만 열 수 있습니다." }); break; }
                    if (LinkConfirmation(url!)) LinkOpener(url!);
                    break;
                case "mindmap.exportPng":
                    await ExportPngAsync(root.GetProperty("name").GetString() ?? "", root.GetProperty("data").GetString() ?? "");
                    break;
            }
        }
        catch (Exception error) when (error is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            Post(new { type = "mindmap.error", message = error.Message });
        }
    }
    private async Task ExportPngAsync(string name, string data)
    {
        var png = Convert.FromBase64String(data);
        if (png is not [0x89, 0x50, 0x4E, 0x47, ..]) throw new System.IO.InvalidDataException("PNG 그림이 아닙니다.");
        if (PngPathPicker(name) is not { } path) return;
        await System.IO.File.WriteAllBytesAsync(path, png);
        Post(new { type = "mindmap.exported" });
    }
    protected override Task SavePendingAsync(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 300 });
        return store.SaveAsync(document.RootElement.Clone(), CancellationToken.None);
    }
}

// 할 일과 달력 창입니다. 같은 저장소를 쓰며, 한쪽의 변경을 앱이 양쪽에 알립니다.
public sealed class PlannerWindow : ToolWindow
{
    private readonly PlannerStore store;
    private readonly Action<PlannerState> changed;
    private readonly Func<HolidayFile?>? holidays;
    public PlannerWindow(string title, string page, Func<string, bool> isAllowed, PlannerStore store, Action<PlannerState> changed, double width, double height, Func<HolidayFile?>? holidays = null) : base(title, page, isAllowed, width, height)
    {
        this.store = store; this.changed = changed; this.holidays = holidays;
    }
    internal void SendHolidays() { if (holidays is not null) Post(new { type = "holidays.state", years = holidays()?.Years }); }
    internal void SendState(PlannerState state) => Post(new { type = "planner.state", state = new { lists = state.Lists, todos = state.Todos, events = state.Events } });
    protected override async Task ReceiveAsync(string json, string source)
    {
        if (!ToolMessage.TryReadPlanner(json, source, out var request)) return;
        var root = request!.Root;
        try
        {
            switch (request.Type)
            {
                case "planner.load":
                    SendHolidays();
                    SendState(await store.LoadAsync(CancellationToken.None));
                    break;
                case "planner.put":
                    changed(await store.PutAsync(root.GetProperty("collection").GetString() ?? "", root.GetProperty("item"), CancellationToken.None));
                    break;
                case "planner.delete":
                    changed(await store.DeleteAsync(root.GetProperty("collection").GetString() ?? "", root.GetProperty("id").GetString() ?? "", CancellationToken.None));
                    break;
            }
        }
        catch (Exception error) when (error is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Post(new { type = "planner.error", message = error.Message });
            SendState(await store.LoadAsync(CancellationToken.None));
        }
    }
}
