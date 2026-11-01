using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HunminNotes.App;
using HunminNotes.Core;
using HunminNotes.Storage;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var root = Environment.GetEnvironmentVariable("DEVPOSTIT_DESKTOP_SMOKE_ROOT") ?? throw new InvalidOperationException("검증 전용 데이터 폴더를 지정하세요.");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", System.IO.Path.Combine(root, "webview-profile"));
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var code = 0;
        application.Startup += async (_, _) =>
        {
            try { await VerifyAsync(application, root); Console.WriteLine("WPF 수명 검증 통과: 접기·클릭 복원·개별 창 닫기·생성/종료 경합·임대 해제"); }
            catch (Exception error) { code = 1; Console.Error.WriteLine(error); }
            finally { application.Shutdown(); }
        };
        application.Run();
        return code;
    }
    private static async Task VerifyAsync(System.Windows.Application application, string root)
    {
        var workspace = await NoteWorkspace.OpenAsync(System.IO.Path.Combine(root, "data"), default);
        var completion = new TaskCompletionSource<Note>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delaying = false;
        var quit = false;
        var manager = new DesktopNoteManager(workspace, () => delaying ? completion.Task : workspace.CreateAsync(default), () => quit = true);
        using var tray = new TrayService(manager, workspace, application.Dispatcher);
        var first = manager.ShowNote(workspace.Notes.Single().Id)!;
        await ReadyAsync(first);
        var editor = (WebView2)first.FindName("EditorView");
        var original = await workspace.Service.GetStateAsync(first.NoteId, default);
        var warningVisible = false;
        first.CloseFailurePrompt = (uncaptured, _) =>
        {
            warningVisible = uncaptured && first.IsVisible;
            return MessageBoxResult.OK;
        };
        await editor.ExecuteScriptAsync("window.hunminEditor.load('oversized', ('x'.repeat(80)+'\\n').repeat(13000), 100)");
        manager.Collapse(first);
        Check(!await first.RequestCloseAsync(), "미보존 초안의 종료 차단");
        Check(warningVisible, "접힌 창의 미보존 초안 경고는 복원된 창에 표시");
        Check(first.IsVisible && !application.Windows.OfType<CollapsedNoteWindow>().Any(), "종료 취소 시 작은 아이콘 제거");
        await editor.ExecuteScriptAsync($"window.hunminEditor.load({JsonSerializer.Serialize(first.NoteId.ToString())}, {JsonSerializer.Serialize(original.Note.Markdown)}, 101, {JsonSerializer.Serialize(original.Note.Title)})");
        await UntilAsync(async () => (await workspace.Service.GetStateAsync(first.NoteId, default)).Revision == 101);
        first.CloseFailurePrompt = null;
        await editor.ExecuteScriptAsync("window.hunminEditor.rename('접기 복원 검증')");
        await UntilAsync(async () => (await workspace.Service.GetStateAsync(first.NoteId, default)).Note.Title == "접기 복원 검증");
        manager.Collapse(first);
        var icon = application.Windows.OfType<CollapsedNoteWindow>().Single();
        Check(!first.IsVisible && icon.IsVisible && icon.Width == 48, "접힌 아이콘 표시");
        ((Button)icon.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(first.IsVisible && !application.Windows.OfType<CollapsedNoteWindow>().Any(), "같은 창 클릭 복원");
        Check(ReferenceEquals(first, manager.ShowNote(first.NoteId)), "중복 창 방지");
        Check(JsonSerializer.Deserialize<string>(await editor.ExecuteScriptAsync("window.hunminEditor.getTitle()")) == "접기 복원 검증", "복원 후 편집 내용 보존");
        tray.Menu.Show(new System.Drawing.Point(120, 120));
        Check(tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Any(item => item.Text == "메모 목록"), "트레이 메모 목록");
        tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "설정").PerformClick();
        tray.Menu.Close();
        await UntilAsync(async () => await editor.ExecuteScriptAsync("Boolean(document.querySelector('dialog[aria-label=설정]')?.open)") == "true");
        await editor.ExecuteScriptAsync("document.querySelector('dialog[aria-label=설정]').close()");
        tray.Menu.Show(new System.Drawing.Point(120, 120));
        tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "새 메모").PerformClick();
        tray.Menu.Close();
        await UntilAsync(() => Task.FromResult(application.Windows.OfType<MainWindow>().Count() == 2));
        var second = application.Windows.OfType<MainWindow>().Single(window => window != first);
        await ReadyAsync(second);
        tray.Menu.Show(new System.Drawing.Point(120, 120));
        var list = tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "메모 목록");
        Check(list.DropDownItems.OfType<Forms.ToolStripMenuItem>().Any(item => item.Text == "접기 복원 검증"), "트레이 제목 반영");
        tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "모두 접기").PerformClick();
        tray.Menu.Close();
        Check(application.Windows.OfType<CollapsedNoteWindow>().Count() == 2, "모두 접기");
        tray.Menu.Show(new System.Drawing.Point(120, 120));
        tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "모두 표시").PerformClick();
        tray.Menu.Close();
        Check(first.IsVisible && second.IsVisible, "모두 표시");
        Check(await first.RequestCloseAsync(), "개별 창 닫기");
        var current = await workspace.Service.GetStateAsync(second.NoteId, default);
        await ((WebView2)second.FindName("EditorView")).ExecuteScriptAsync($"window.hunminEditor.load({JsonSerializer.Serialize(second.NoteId.ToString())}, '다른 창 닫은 후에도 저장', {current.Revision + 1}, {JsonSerializer.Serialize(current.Note.Title)})");
        await UntilAsync(async () => { var state = await workspace.Service.GetStateAsync(second.NoteId, default); return state.Note.Markdown == "다른 창 닫은 후에도 저장" && !state.HasUnsavedChanges; });
        Check(await second.RequestCloseAsync(), "두 번째 창 닫기");
        Check(application.Windows.Count == 0, "마지막 창 정리");
        Check(!application.Dispatcher.HasShutdownStarted, "마지막 창 이후 앱 수명 유지");
        tray.Menu.Show(new System.Drawing.Point(120, 120));
        var closedList = tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "메모 목록");
        closedList.DropDownItems.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "접기 복원 검증").PerformClick();
        tray.Menu.Close();
        var reopened = application.Windows.OfType<MainWindow>().Single();
        await ReadyAsync(reopened);
        Check(await reopened.RequestCloseAsync(), "마지막 창 닫은 뒤 트레이 목록에서 다시 열기");
        using (var attempted = new DataDirectoryLease(System.IO.Path.Combine(root, "other-data"))) { }
        delaying = true;
        var creating = manager.CreateAsync();
        tray.Menu.Show(new System.Drawing.Point(120, 120));
        tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "종료").PerformClick();
        tray.Menu.Close();
        await application.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Check(!quit, "생성 완료까지 종료 대기");
        Check(manager.ShowNote(first.NoteId) is null, "종료 중 새 창 차단");
        try { using var unexpected = new DataDirectoryLease(System.IO.Path.Combine(root, "data")); throw new Exception("진행 중 임대가 풀렸습니다."); }
        catch (IOException) { }
        completion.SetResult(await workspace.CreateAsync(default));
        await creating;
        await UntilAsync(() => Task.FromResult(quit));
        Check(quit && application.Windows.Count == 0, "종료 중 생성은 새 창을 열지 않음");
        Check(manager.ShowNote(first.NoteId) is null, "종료된 저장소를 다시 열지 않음");
        using var released = new DataDirectoryLease(System.IO.Path.Combine(root, "data"));
    }
    private static async Task ReadyAsync(MainWindow window) => await UntilAsync(() => Task.FromResult(((Border)window.FindName("Loading")).Visibility == Visibility.Collapsed));
    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("편집기 준비 시간 초과");
            await Task.Delay(50);
        }
    }
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
}
