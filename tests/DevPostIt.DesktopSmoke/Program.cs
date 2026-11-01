using System.Text.Json;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevPostIt.App;
using DevPostIt.Core;
using DevPostIt.Storage;
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
            try { await VerifyAsync(application, root); Console.WriteLine("WPF 검증 통과: 최소화·24px 복원·X 삭제 확인·초기화 취소·지연 입력 보존·종료 경합"); }
            catch (Exception error) { code = 1; Console.Error.WriteLine(error); }
            finally { application.Shutdown(); }
        };
        application.Run();
        return code;
    }
    private static async Task VerifyAsync(System.Windows.Application application, string root)
    {
        var workspace = await NoteWorkspace.OpenAsync(System.IO.Path.Combine(root, "data"), default);
        var opening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starting = new MainWindow(workspace, workspace.Notes.Single().Id) { InitializationDelay = () => opening.Task, DeleteConfirmation = () => MessageBoxResult.No };
        starting.Show();
        await application.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        var earlyCancel = starting.RequestDeleteAsync();
        opening.SetResult();
        Check(!await earlyCancel, "초기화 중 삭제 취소");
        await ReadyAsync(starting);
        Check(await starting.RequestCloseAsync(), "삭제 취소 후 편집기 초기화 유지");
        var completion = new TaskCompletionSource<Note>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delaying = false;
        var quit = false;
        var manager = new DesktopNoteManager(workspace, () => delaying ? completion.Task : workspace.CreateAsync(default), () => quit = true);
        using var tray = new TrayService(manager, workspace, application.Dispatcher);
        var first = manager.ShowNote(workspace.Notes.Single().Id)!;
        await ReadyAsync(first);
        Check(first.Width == 480 && first.Height == 640 && first.MinWidth == 320 && first.MinHeight == 360, "포스트잇 기본·최소 창 크기");
        Check(first.Title == "Post It for Developer", "창 제목은 메모 이름 대신 고정 문구");
        Check(IsPostItYellow(first.Icon), "메모 창 제목 표시줄 아이콘은 노란 포스트잇");
        var editor = (WebView2)first.FindName("EditorView");
        var firstNote = workspace.Notes.Single();
        first.DeleteConfirmation = () => MessageBoxResult.Yes;
        first.DeleteFailureDialog = async _ =>
        {
            await editor.ExecuteScriptAsync("window.hunminEditor.rename('늦게 전달된 이름')");
            await Task.Delay(100);
        };
        using (var locked = new FileStream(Path.Combine(root, "data", "notes", firstNote.RelativePath), FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!await first.RequestDeleteAsync(), "삭제 실패 시 창 유지");
        await UntilAsync(async () => (await workspace.Service.GetStateAsync(first.NoteId, default)).Note.Title == "늦게 전달된 이름");
        first.DeleteConfirmation = null; first.DeleteFailureDialog = null;
        var original = await workspace.Service.GetStateAsync(first.NoteId, default);
        var warningVisible = false;
        first.CloseFailurePrompt = (uncaptured, _) =>
        {
            warningVisible = uncaptured && first.IsVisible;
            return MessageBoxResult.OK;
        };
        await editor.ExecuteScriptAsync("window.hunminEditor.load('oversized', ('x'.repeat(80)+'\\n').repeat(13000), 100)");
        first.WindowState = WindowState.Minimized;
        await UntilAsync(() => Task.FromResult(application.Windows.OfType<CollapsedNoteWindow>().Count() == 1));
        Check(!await first.RequestCloseAsync(), "미보존 초안의 종료 차단");
        Check(warningVisible, "접힌 창의 미보존 초안 경고는 복원된 창에 표시");
        Check(first.IsVisible && !application.Windows.OfType<CollapsedNoteWindow>().Any(), "종료 취소 시 작은 아이콘 제거");
        await editor.ExecuteScriptAsync($"window.hunminEditor.load({JsonSerializer.Serialize(first.NoteId.ToString())}, {JsonSerializer.Serialize(original.Note.Markdown)}, 101, {JsonSerializer.Serialize(original.Note.Title)})");
        await UntilAsync(async () => (await workspace.Service.GetStateAsync(first.NoteId, default)).Revision == 101);
        first.CloseFailurePrompt = null;
        await editor.ExecuteScriptAsync("window.hunminEditor.rename('접기 복원 검증')");
        await UntilAsync(async () => (await workspace.Service.GetStateAsync(first.NoteId, default)).Note.Title == "접기 복원 검증");
        Check(first.Title == "Post It for Developer", "이름을 바꿔도 창 제목 유지");
        first.WindowState = WindowState.Minimized;
        await UntilAsync(() => Task.FromResult(application.Windows.OfType<CollapsedNoteWindow>().Count() == 1));
        var icon = application.Windows.OfType<CollapsedNoteWindow>().Single();
        Check(!first.IsVisible && icon.IsVisible && icon.Width == 24 && ((Button)icon.Content).Content is null, "24px 빈 노란 아이콘 표시");
        var iconButton = (Button)icon.Content;
        iconButton.ApplyTemplate();
        var iconChrome = (Border)iconButton.Template.FindName("chrome", iconButton);
        Check(iconChrome.Background == iconButton.Background && iconButton.Template.Triggers.Count > 0, "접힌 아이콘은 마우스를 올려도 노란색 유지");
        ((Button)icon.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(first.IsVisible && !application.Windows.OfType<CollapsedNoteWindow>().Any(), "같은 창 클릭 복원");
        Check(ReferenceEquals(first, manager.ShowNote(first.NoteId)), "중복 창 방지");
        Check(JsonSerializer.Deserialize<string>(await editor.ExecuteScriptAsync("window.hunminEditor.getTitle()")) == "접기 복원 검증", "복원 후 편집 내용 보존");
        SendMessage(new WindowInteropHelper(first).Handle, 0x00A3, 2, 0);
        await UntilAsync(() => Task.FromResult(application.Windows.OfType<CollapsedNoteWindow>().Count() == 1));
        Check(!first.IsVisible, "제목 표시줄 더블클릭으로 아이콘 접기");
        icon = application.Windows.OfType<CollapsedNoteWindow>().Single();
        icon.Activate();
        if (Environment.GetEnvironmentVariable("DEVPOSTIT_SMOKE_REAL_INPUT") == "1")
        {
        await application.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        var (left, top) = (icon.Left, icon.Top);
        var grip = icon.PointToScreen(new System.Windows.Point(12, 12));
        GetCursorPos(out var cursor);
        try
        {
            await Task.Run(() =>
            {
                SetCursorPos((int)grip.X, (int)grip.Y); Thread.Sleep(150);
                mouse_event(LeftDown, 0, 0, 0, 0); Thread.Sleep(100);
                for (var step = 1; step <= 20; step++) { SetCursorPos((int)grip.X + 6 * step, (int)grip.Y + 4 * step); Thread.Sleep(20); }
                Thread.Sleep(100);
                mouse_event(LeftUp, 0, 0, 0, 0); Thread.Sleep(200);
            });
            await UntilAsync(() => Task.FromResult(icon.Left > left + 20 && icon.Top > top + 10));
            Check(icon.IsVisible && !first.IsVisible, "접힌 아이콘 실제 드래그 이동은 복원하지 않음");
            var moved = icon.PointToScreen(new System.Windows.Point(12, 12));
            await Task.Run(() =>
            {
                SetCursorPos((int)moved.X, (int)moved.Y); Thread.Sleep(150);
                mouse_event(LeftDown, 0, 0, 0, 0); Thread.Sleep(50);
                mouse_event(LeftUp, 0, 0, 0, 0); Thread.Sleep(100);
            });
            await UntilAsync(() => Task.FromResult(first.IsVisible));
        }
        finally { SetCursorPos(cursor.X, cursor.Y); }
        Check(!application.Windows.OfType<CollapsedNoteWindow>().Any(), "옮긴 아이콘 클릭 복원");
        }
        else
        {
            // 실제 입력은 위에 놓인 다른 창으로 들어갈 수 있어 명시적으로 켰을 때만 보냅니다.
            ((Button)icon.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(first.IsVisible && !application.Windows.OfType<CollapsedNoteWindow>().Any(), "아이콘 클릭 복원");
        }
        // 아이콘은 사용자가 옮기기 전까지 마지막 위치를 유지하고 창 이동을 따라가지 않습니다.
        async Task<CollapsedNoteWindow> CollapseFirstAsync()
        {
            first.WindowState = WindowState.Minimized;
            await UntilAsync(() => Task.FromResult(application.Windows.OfType<CollapsedNoteWindow>().Count() == 1));
            return application.Windows.OfType<CollapsedNoteWindow>().Single();
        }
        void RestoreFirst(CollapsedNoteWindow target) => ((Button)target.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var remembered = await CollapseFirstAsync();
        var (rememberedLeft, rememberedTop) = (remembered.Left, remembered.Top);
        RestoreFirst(remembered);
        first.Left += 150; first.Top += 90;
        var afterWindowMove = await CollapseFirstAsync();
        Check(afterWindowMove.Left == rememberedLeft && afterWindowMove.Top == rememberedTop, "창을 옮겨도 아이콘은 마지막 위치 유지");
        afterWindowMove.Left = rememberedLeft + 40; afterWindowMove.Top = rememberedTop + 30;
        await application.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        RestoreFirst(afterWindowMove);
        first.Left -= 150; first.Top -= 90;
        var afterIconMove = await CollapseFirstAsync();
        Check(afterIconMove.Left == rememberedLeft + 40 && afterIconMove.Top == rememberedTop + 30, "아이콘을 옮기면 그 위치를 유지");
        RestoreFirst(afterIconMove);
        tray.Menu.Show(new System.Drawing.Point(120, 120));
        Check(tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Any(item => item.Text == "메모 목록"), "트레이 메모 목록");
        tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "설정").PerformClick();
        tray.Menu.Close();
        var settings = application.Windows.OfType<SettingsWindow>().Single();
        Check(settings.IsVisible && settings.OwnedWindows.Count == 0 && first.OwnedWindows.Count == 0, "트레이 설정은 독립 창으로 열림");
        Check(await editor.ExecuteScriptAsync("document.querySelector('dialog[aria-label=설정], #settings') === null") == "true", "메모 안에 설정 모달을 띄우지 않음");
        await UntilAsync(async () => settings.View.CoreWebView2 is not null && await settings.View.ExecuteScriptAsync("Boolean(document.querySelector('#larger'))") == "true");
        var baseSize = await editor.ExecuteScriptAsync("getComputedStyle(document.documentElement).fontSize");
        await settings.View.ExecuteScriptAsync("document.querySelector('#larger').click()");
        await UntilAsync(async () => await editor.ExecuteScriptAsync("getComputedStyle(document.documentElement).fontSize") != baseSize);
        await settings.View.ExecuteScriptAsync("document.querySelector('#smaller').click()");
        await UntilAsync(async () => await editor.ExecuteScriptAsync("getComputedStyle(document.documentElement).fontSize") == baseSize);
        Check(IsPostItYellow(settings.Icon), "설정 창 아이콘도 노란 포스트잇");
        string? openedFolder = null;
        settings.FolderOpener = path => openedFolder = path;
        await settings.View.ExecuteScriptAsync("document.querySelector('#open-data-folder').click()");
        await UntilAsync(() => Task.FromResult(openedFolder is not null));
        Check(openedFolder == Path.GetFullPath(Path.Combine(root, "data")), "설정 버튼은 Markdown·SQLite 데이터 폴더를 염");
        Check(Directory.Exists(Path.Combine(openedFolder!, "notes")) && File.Exists(Path.Combine(openedFolder!, ".hunmin", "note-index.db")), "데이터 폴더에 notes와 검색 DB가 있음");
        manager.ShowSettings();
        Check(application.Windows.OfType<SettingsWindow>().Single() == settings, "설정 창 중복 방지");
        settings.Close();
        Check(!application.Windows.OfType<SettingsWindow>().Any() && first.IsVisible, "설정 창을 닫아도 메모 유지");
        await editor.ExecuteScriptAsync("window.dispatchEvent(new KeyboardEvent('keydown', { ctrlKey: true, key: ',' }))");
        await UntilAsync(() => Task.FromResult(application.Windows.OfType<SettingsWindow>().Count() == 1));
        application.Windows.OfType<SettingsWindow>().Single().Close();
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
        var deleting = manager.ShowNote(first.NoteId)!;
        await ReadyAsync(deleting);
        deleting.DeleteConfirmation = () => MessageBoxResult.No;
        Check(!await deleting.RequestDeleteAsync() && deleting.IsVisible, "X 삭제 확인 취소");
        Check(workspace.Notes.Any(note => note.Id == first.NoteId), "취소 시 메모 보존");
        deleting.DeleteConfirmation = () => MessageBoxResult.Yes;
        Check(await deleting.RequestDeleteAsync(), "X 삭제 승인");
        Check(workspace.Notes.All(note => note.Id != first.NoteId), "승인 시 목록 제거");
        Check(workspace.TrashedNotes.Any(note => note.Id == first.NoteId), "X는 휴지통으로 이동");
        await manager.RestoreFromTrashAsync(first.NoteId);
        Check(workspace.Notes.Any(note => note.Id == first.NoteId) && !workspace.TrashedNotes.Any(), "휴지통에서 복원");
        var restored = application.Windows.OfType<MainWindow>().Single(window => window.NoteId == first.NoteId);
        await ReadyAsync(restored);
        Check(await restored.RequestCloseAsync(), "복원한 메모 창 닫기");
        manager.ShowSearch();
        var searchWindow = application.Windows.OfType<SearchWindow>().Single();
        // 실제 WebView2의 PDF 저장과 설정 창의 전체 백업을 파일 결과로 확인합니다.
        var pdfWindow = manager.ShowNote(second.NoteId)!;
        await ReadyAsync(pdfWindow);
        var pdfPath = Path.Combine(root, "note.pdf");
        pdfWindow.PdfPathPicker = _ => pdfPath;
        var pdfEditor = (WebView2)pdfWindow.FindName("EditorView");
        await pdfEditor.ExecuteScriptAsync("document.querySelector('#save-pdf').click()");
        await UntilAsync(async () => File.Exists(pdfPath) && await pdfEditor.ExecuteScriptAsync("document.querySelector('#save-pdf').disabled") == "false");
        using (var pdf = File.OpenRead(pdfPath))
        {
            var header = new byte[4];
            pdf.ReadExactly(header);
            Check(System.Text.Encoding.ASCII.GetString(header) == "%PDF" && pdf.Length > 500, "PDF 파일 저장");
        }
        Check(await pdfWindow.RequestCloseAsync(), "PDF 저장 후 메모 닫기");
        manager.ShowSettings();
        var backupSettings = application.Windows.OfType<SettingsWindow>().Single();
        await UntilAsync(async () => backupSettings.View.CoreWebView2 is not null && await backupSettings.View.ExecuteScriptAsync("Boolean(document.querySelector('#backup'))") == "true");
        var backupPath = Path.Combine(root, "backup.zip");
        backupSettings.BackupPathPicker = () => backupPath;
        await backupSettings.View.ExecuteScriptAsync("document.querySelector('#backup').click()");
        await UntilAsync(() => Task.FromResult(File.Exists(backupPath)));
        Check(backupSettings.Icon is not null, "설정 창 유지");
        backupSettings.Close();
        await UntilAsync(async () => searchWindow.View.CoreWebView2 is not null && await searchWindow.View.ExecuteScriptAsync("document.querySelectorAll('#search-results button').length") != "0");
        await searchWindow.View.ExecuteScriptAsync("document.querySelector('#search-results button').click()");
        await UntilAsync(() => Task.FromResult(application.Windows.OfType<MainWindow>().Any()));
        var opened = application.Windows.OfType<MainWindow>().Single();
        await ReadyAsync(opened);
        Check(await opened.RequestCloseAsync(), "검색 결과로 연 메모 닫기");
        searchWindow.Close();
        Check(workspace.Notes.Any(note => note.Id == second.NoteId), "다른 메모 보존");
        using (var attempted = new DataDirectoryLease(System.IO.Path.Combine(root, "other-data"))) { }
        manager.ShowSettings();
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
    private static bool IsPostItYellow(ImageSource? icon)
    {
        if (icon is not BitmapSource source) return false;
        var pixels = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        pixels.CopyPixels(new Int32Rect(pixels.PixelWidth / 2, pixels.PixelHeight * 13 / 32, 1, 1), pixel, 4, 0);
        return pixel is [143, 232, 255, 255];
    }
    private const uint LeftDown = 0x0002, LeftUp = 0x0004;
    private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int x, int y, uint data, nuint extra);
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
