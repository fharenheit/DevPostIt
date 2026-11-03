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
using DevPostIt.EditorHost;
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
        const string renameScript = "document.querySelector('h1').dispatchEvent(new MouseEvent('dblclick', { bubbles: true })); document.querySelector('#note-name').value = {0}; document.querySelector('#rename-form').requestSubmit();";
        await editor.ExecuteScriptAsync(renameScript.Replace("{0}", JsonSerializer.Serialize("접기 복원 검증 파일")));
        await UntilAsync(async () => (await workspace.Service.GetStateAsync(first.NoteId, default)) is { Note.RelativePath: "Inbox/접기 복원 검증 파일.md", Note.Title: "접기 복원 검증 파일" });
        Check(File.Exists(Path.Combine(root, "data", "notes", "Inbox", "접기 복원 검증 파일.md")) && !File.Exists(Path.Combine(root, "data", "notes", firstNote.RelativePath)), "이름 변경 대화상자로 파일 이름 변경");
        await editor.ExecuteScriptAsync(renameScript.Replace("{0}", JsonSerializer.Serialize("접기 복원 검증")));
        await UntilAsync(async () => (await workspace.Service.GetStateAsync(first.NoteId, default)) is { Note.RelativePath: "Inbox/접기 복원 검증.md", Note.Title: "접기 복원 검증" });
        first.WindowState = WindowState.Minimized;
        await UntilAsync(() => Task.FromResult(application.Windows.OfType<CollapsedNoteWindow>().Count() == 1));
        var icon = application.Windows.OfType<CollapsedNoteWindow>().Single();
        Check(!first.IsVisible && icon.IsVisible && icon.Width == 24 && ((Button)icon.Content).Content is null, "24px 빈 노란 아이콘 표시");
        var iconButton = (Button)icon.Content;
        iconButton.ApplyTemplate();
        var iconChrome = (Border)iconButton.Template.FindName("chrome", iconButton);
        Check(iconChrome.Background == iconButton.Background && iconButton.Template.Triggers.Count > 0, "접힌 아이콘은 마우스를 올려도 노란색 유지");
        Check(editor.DefaultBackgroundColor.ToArgb() == System.Drawing.Color.FromArgb(255, 253, 245).ToArgb(), "메모 창의 WebView 기본 배경은 종이색");
        var repaints = first.RepaintCount;
        ((Button)icon.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(first.IsVisible && !application.Windows.OfType<CollapsedNoteWindow>().Any(), "같은 창 클릭 복원");
        await UntilAsync(() => Task.FromResult(first.RepaintCount > repaints));
        Check(editor.Margin == new Thickness(0), "복원 후 다시 그리고 편집기 크기를 원래대로 둠");
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
        Check(tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Any(item => item.Text == "정보"), "트레이 정보 메뉴");
        Check(tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Any(item => item.Text == "도움말"), "트레이 도움말 메뉴");
        Check(tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Any(item => item.Text == "화면 캡처"), "트레이 화면 캡처 메뉴");
        Check(tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Any(item => item.Text == "소스코드 포맷터"), "트레이 소스코드 포맷터 메뉴");
        tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "설정").PerformClick();
        tray.Menu.Close();
        var settings = application.Windows.OfType<SettingsWindow>().Single();
        Check(settings.IsVisible && settings.OwnedWindows.Count == 0 && first.OwnedWindows.Count == 0, "트레이 설정은 독립 창으로 열림");
        Check(await editor.ExecuteScriptAsync("document.querySelector('dialog[aria-label=설정], #settings') === null") == "true", "메모 안에 설정 모달을 띄우지 않음");
        // 이동 전의 about:blank도 readyState가 complete이므로 설정 페이지의 단추까지 확인합니다. 단추가 없으면 click 오류는 조용히 사라집니다.
        await UntilAsync(async () => settings.View.CoreWebView2 is not null && await settings.View.ExecuteScriptAsync("document.readyState === 'complete' && document.querySelector('#larger') !== null") == "true");
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
        manager.ShowHelp();
        var help = application.Windows.OfType<HelpWindow>().Single();
        Check(help.IsVisible && help.Texts.Any() && help.Texts.All(text => text.FontSize == 13), "도움말 글자 크기 13");
        Check(help.Texts.All(text => !string.IsNullOrWhiteSpace(text.Text)), "도움말 내용 표시");
        manager.ShowHelp();
        Check(application.Windows.OfType<HelpWindow>().Single() == help, "도움말 창 중복 방지");
        help.Close();
        manager.ShowAbout();
        var about = application.Windows.OfType<AboutWindow>().Single();
        Check(about.IsVisible && about.VersionText.Contains(DesktopNoteManager.CurrentVersion.ToString(3)), "정보 창의 버전");
        string? openedLink = null;
        about.LinkOpener = link => openedLink = link;
        about.OpenRepository();
        Check(openedLink == AboutWindow.RepositoryUrl, "정보 창의 GitHub 주소");
        manager.ShowAbout();
        Check(application.Windows.OfType<AboutWindow>().Single() == about, "정보 창 중복 방지");
        about.Close();
        // 소스코드 포맷터: 실제 WebView2의 보안 정책 안에서 Monaco와 Java 정리까지 확인합니다.
        var notesBeforeFormatter = workspace.Notes.Count;
        manager.ShowFormatter();
        var formatter = application.Windows.OfType<FormatterWindow>().Single();
        await UntilAsync(async () => formatter.View.CoreWebView2 is not null && await formatter.View.ExecuteScriptAsync("Boolean(window.hunminFormatter)") == "true");
        await formatter.View.ExecuteScriptAsync("(() => { const select = document.querySelector('#formatter-language'); select.value = 'java'; select.dispatchEvent(new Event('change')); window.hunminFormatter.setValue('class Main{int a=1;}'); document.querySelector('#formatter-format').click(); })()");
        await UntilAsync(async () => (await formatter.View.ExecuteScriptAsync("window.hunminFormatter.getValue()")).Contains("class Main {"));
        Check(await formatter.View.ExecuteScriptAsync("window.hunminFormatter.getLanguage()") == "\"java\"", "포맷터의 언어 선택");
        Check(workspace.Notes.Count == notesBeforeFormatter, "포맷터는 메모를 만들지 않음");
        foreach (var (language, source, expected) in new[] { ("python", "a=1", "a = 1"), ("go", "package a\\nvar b=1", "var b = 1"), ("cpp", "int main(){}", "int main() {"), ("sql", "select 1", "select"), ("xml", "<a><b>1</b></a>", "<b>1</b>"), ("shell", "a=1;b=2", "a=1") })
        {
            await formatter.View.ExecuteScriptAsync($"(() => {{ const select = document.querySelector('#formatter-language'); select.value = '{language}'; select.dispatchEvent(new Event('change')); window.hunminFormatter.setValue('{source}'); document.querySelector('#formatter-format').click(); }})()");
            await UntilAsync(async () => (await formatter.View.ExecuteScriptAsync("document.querySelector('#formatter-status').textContent")).Contains("포맷했습니다"));
            Check(JsonSerializer.Deserialize<string>(await formatter.View.ExecuteScriptAsync("window.hunminFormatter.getValue()"))!.Contains(expected), $"포맷터의 {language} 정리");
        }
        manager.ShowFormatter();
        Check(application.Windows.OfType<FormatterWindow>().Single() == formatter, "포맷터 창 중복 방지");
        formatter.Close();
        // 마인드맵, 할 일, 달력: 실제 WebView2에서 페이지와 저장소를 연결해 파일 결과까지 확인합니다.
        Check(tray.Menu.Items.OfType<Forms.ToolStripMenuItem>().Select(item => item.Text).SkipWhile(text => text != "소스코드 포맷터").Skip(1).Take(3).SequenceEqual(["마인드맵", "할 일", "달력"]), "포맷터 아래의 마인드맵, 할 일, 달력 메뉴");
        manager.ShowMindMap();
        var mindMapWindow = application.Windows.OfType<MindMapWindow>().Single();
        await UntilAsync(async () => mindMapWindow.View.CoreWebView2 is not null && await mindMapWindow.View.ExecuteScriptAsync("Boolean(document.querySelector('.mm-node.root'))") == "true");
        var toolsRoot = Path.Combine(root, "data", ".hunmin", "tools");
        await UntilAsync(async () => { await Task.Yield(); return Directory.Exists(Path.Combine(toolsRoot, "mindmaps")) && Directory.GetFiles(Path.Combine(toolsRoot, "mindmaps"), "*.json").Length == 1; });
        var mapFile = Directory.GetFiles(Path.Combine(toolsRoot, "mindmaps"), "*.json").Single();
        await mindMapWindow.View.ExecuteScriptAsync("document.querySelector('.mm-editor')?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))");
        await mindMapWindow.View.ExecuteScriptAsync("document.querySelector('[data-action=\"child\"]').click()");
        await UntilAsync(async () => await mindMapWindow.View.ExecuteScriptAsync("Boolean(document.querySelector('.mm-editor'))") == "true");
        await mindMapWindow.View.ExecuteScriptAsync("(() => { const editor = document.querySelector('.mm-editor'); editor.value = '닫기 직전 주제'; editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); })()");
        await mindMapWindow.FlushAndCloseAsync();
        Check(File.ReadAllText(mapFile).Contains("닫기 직전 주제"), "마인드맵 창을 닫기 직전 변경도 저장");
        Check(!application.Windows.OfType<MindMapWindow>().Any(), "마인드맵 창 닫기");
        manager.ShowTodo();
        var todoWindow = application.Windows.OfType<PlannerWindow>().Single();
        await UntilAsync(async () => todoWindow.View.CoreWebView2 is not null && await todoWindow.View.ExecuteScriptAsync("Boolean(document.querySelector('#todo-title')?.textContent)") == "true");
        // 저장해 둔 공휴일 파일이 있으면 실제 WebView2의 달력이 그 목록을 씁니다. 하네스는 네트워크로 받지 않습니다.
        var thisMonth = DateTime.Now.ToString("yyyy-MM");
        File.WriteAllText(Path.Combine(toolsRoot, "korean-holidays.json"), $$$"""{"version":1,"years":{"{{{DateTime.Now.Year}}}":[{"date":"{{{thisMonth}}}-15","name":"하네스 공휴일","substitute":false}]}}""");
        manager.ShowCalendar();
        var calendarWindow = application.Windows.OfType<PlannerWindow>().Single(window => window != todoWindow);
        await UntilAsync(async () => calendarWindow.View.CoreWebView2 is not null && await calendarWindow.View.ExecuteScriptAsync("document.querySelectorAll('.cal-day').length > 0 || document.querySelectorAll('.cal-column').length > 0") == "true");
        await calendarWindow.View.ExecuteScriptAsync("document.querySelector('[data-view=\"month\"]').click()");
        await UntilAsync(async () => (await calendarWindow.View.ExecuteScriptAsync($"document.querySelector('[data-date=\"{thisMonth}-15\"] .cal-holiday')?.textContent ?? ''")).Contains("하네스 공휴일"));
        File.Delete(Path.Combine(toolsRoot, "korean-holidays.json"));
        await todoWindow.View.ExecuteScriptAsync("(() => { const input = document.querySelector('#todo-input'); input.value = '앱에서 등록'; input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); })()");
        await UntilAsync(async () => { await Task.Yield(); return (await manager.Planner.LoadAsync(CancellationToken.None)).Todos.Any(todo => todo.Title == "앱에서 등록"); });
        Check(File.ReadAllText(Path.Combine(toolsRoot, "planner.json")).Contains("앱에서 등록"), "할 일을 파일에 저장");
        Check(await todoWindow.View.ExecuteScriptAsync("document.querySelector('#todo-input').value") == "\"\"", "등록 뒤 입력칸 비움");
        // 달력에서 만든 일정이 저장소를 거쳐 할 일 창에도 전달되는지 확인합니다.
        await todoWindow.View.ExecuteScriptAsync("window.__received = 0; window.chrome.webview.addEventListener('message', () => { window.__received++; })");
        await calendarWindow.View.ExecuteScriptAsync("document.querySelector('#cal-new').click()");
        await calendarWindow.View.ExecuteScriptAsync("(() => { document.querySelector('#ev-title').value = '앱 일정'; document.querySelector('#event-form').requestSubmit(); })()");
        await UntilAsync(async () => { await Task.Yield(); return (await manager.Planner.LoadAsync(CancellationToken.None)).Events.Any(item => item.Title == "앱 일정"); });
        await UntilAsync(async () => await todoWindow.View.ExecuteScriptAsync("window.__received > 0") == "true");
        var holidayCount = await calendarWindow.View.ExecuteScriptAsync("(() => { const count = []; for (let m = 0; m < 12; m++) { document.querySelector('#cal-next').click(); count.push(document.querySelectorAll('.cal-holiday').length); } return count.some(value => value > 0); })()");
        Check(holidayCount == "true", "WebView2의 달력에 한국 공휴일 표시");
        var reminders = new List<PlannerReminder>();
        manager.ReminderDue += reminders.Add;
        await manager.CheckRemindersAsync(new DateTime(2030, 1, 1, 9, 0, 0));
        await manager.Planner.PutAsync("todos", JsonSerializer.SerializeToElement(new { id = Guid.NewGuid().ToString(), title = "알림", notes = "", listId = "tasks", important = false, myDay = (string?)null, due = (string?)null, reminder = "2030-01-01T09:30", repeat = "none", completedAt = (string?)null, steps = Array.Empty<object>(), created = "2030-01-01T09:00", order = 0.0 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), CancellationToken.None);
        await manager.CheckRemindersAsync(new DateTime(2030, 1, 1, 10, 0, 0));
        Check(reminders.Count == 1 && reminders[0].Title == "알림", "지난 알림을 한 번 알림");
        await manager.CheckRemindersAsync(new DateTime(2030, 1, 1, 10, 1, 0));
        Check(reminders.Count == 1, "같은 알림을 되풀이하지 않음");
        await todoWindow.FlushAndCloseAsync();
        await calendarWindow.FlushAndCloseAsync();
        manager.ShowSearch();
        var searchWindow = application.Windows.OfType<SearchWindow>().Single();
        // 실제 WebView2의 PDF 저장과 설정 창의 전체 백업을 파일 결과로 확인합니다.
        var pdfWindow = manager.ShowNote(second.NoteId)!;
        await ReadyAsync(pdfWindow);
        // 사용자 클립보드를 건드리지 않도록 읽는 부분만 바꾸고, 붙여넣기부터 미리보기 표시까지 실제 WebView2로 확인합니다.
        var samplePng = SamplePng();
        // 클립보드 스크린샷은 알파값이 모두 0으로 올 수 있습니다. 그대로 저장하면 투명한 그림이 됩니다.
        byte[] Pixel(byte[] png)
        {
            using var input = new MemoryStream(png);
            var frame = new FormatConvertedBitmap(BitmapDecoder.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
            var values = new byte[4];
            frame.CopyPixels(new Int32Rect(0, 0, 1, 1), values, 4, 0);
            return values;
        }
        BitmapSource Solid(byte alpha) { var values = new byte[16]; for (var index = 0; index < 16; index += 4) { values[index] = 10; values[index + 1] = 120; values[index + 2] = 230; values[index + 3] = alpha; } return BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, values, 8); }
        Check(Pixel(MainWindow.EncodeClipboardImage(Solid(0))) is [10, 120, 230, 255], "알파값이 모두 0인 클립보드 이미지를 불투명하게 저장");
        Check(Pixel(MainWindow.EncodeClipboardImage(Solid(128)))[3] == 128, "실제 투명도가 있는 이미지는 유지");
        var imageFile = Path.Combine(root, "복사한 이미지.png");
        File.WriteAllBytes(imageFile, samplePng);
        Check(MainWindow.ImageFileToPng(imageFile) is { Length: > 0 }, "복사한 이미지 파일 읽기");
        var fakeImage = Path.Combine(root, "가짜.png");
        File.WriteAllText(fakeImage, "이미지 아님");
        Check(MainWindow.ImageFileToPng(fakeImage) is null, "이미지가 아닌 파일 거부");
        var textFile = Path.Combine(root, "글.txt");
        File.WriteAllText(textFile, "글");
        Check(MainWindow.ImageFileToPng(textFile) is null, "이미지 확장자가 아닌 파일 거부");
        // 스크린샷처럼 알파값이 모두 0인 이미지로 붙여넣어 실제 화면과 PDF까지 확인합니다.
        var screenshotPixels = new byte[200 * 200 * 4];
        for (var index = 0; index < screenshotPixels.Length; index += 4) { screenshotPixels[index] = 10; screenshotPixels[index + 1] = 120; screenshotPixels[index + 2] = 230; }
        var screenshot = BitmapSource.Create(200, 200, 96, 96, PixelFormats.Bgra32, null, screenshotPixels, 800);
        pdfWindow.ClipboardImageReader = () => MainWindow.EncodeClipboardImage(screenshot);
        var imageEditor = (WebView2)pdfWindow.FindName("EditorView");
        await imageEditor.ExecuteScriptAsync("(() => { const data = new DataTransfer(); data.items.add(new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })); document.querySelector('#source textarea').dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true })); })()");
        await UntilAsync(async () => (await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown")).Contains("assets/"));
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.setMode('preview')");
        await UntilAsync(async () => await imageEditor.ExecuteScriptAsync("document.querySelector('#preview img')?.naturalWidth ?? 0") == "200");
        using (var capture = new MemoryStream())
        {
            await imageEditor.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, capture);
            capture.Position = 0;
            var shot = new FormatConvertedBitmap(BitmapDecoder.Create(capture, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
            var shotPixels = new byte[shot.PixelWidth * shot.PixelHeight * 4];
            shot.CopyPixels(shotPixels, shot.PixelWidth * 4, 0);
            var colored = 0;
            for (var index = 0; index < shotPixels.Length; index += 4) if (Math.Abs(shotPixels[index] - 10) < 12 && Math.Abs(shotPixels[index + 1] - 120) < 12 && Math.Abs(shotPixels[index + 2] - 230) < 12) colored++;
            Check(colored > 10000, $"붙여넣은 이미지가 미리보기 화면에 실제로 표시 ({colored}픽셀)");
        }
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.setMode('source')");
        // 개체 캡처: 입력을 보내지 않고 숨기기·복원, 화면 복사, 메모 삽입만 확인합니다. 후크는 설치하지 않습니다.
        manager.ObjectCaptureHooks = false;
        var visibleBefore = pdfWindow.IsVisible;
        await manager.StartObjectCaptureAsync();
        Check(manager.IsObjectCapturing && !pdfWindow.IsVisible, "개체 캡처 중 메모 창 숨김");
        await Task.Delay(300);
        manager.CancelObjectCapture();
        Check(!manager.IsObjectCapturing && pdfWindow.IsVisible == visibleBefore, "개체 캡처 취소 시 메모 창 복원");
        await application.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Check(application.Windows.OfType<Window>().All(window => window.Width > 1 || window is CollapsedNoteWindow), "개체 캡처 강조 창 정리");
        var (hitX, hitY) = ((int)pdfWindow.Left + 100, (int)pdfWindow.Top + 100);
        var hit = await Task.Run(() => ObjectCaptureSession.HitTest(hitX, hitY, 0));
        Check(hit is null || hit.Value.Width >= 4, "요소 검색 호출");
        var capturedPng = ObjectCaptureSession.CapturePng(new DevPostIt.EditorHost.ScreenRect((int)pdfWindow.Left, (int)pdfWindow.Top, 30, 20));
        using (var capturedStream = new MemoryStream(capturedPng))
        {
            var capturedFrame = BitmapDecoder.Create(capturedStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Check(capturedFrame.PixelWidth == 30 && capturedFrame.PixelHeight == 20, "화면 영역 PNG 생성");
        }
        var beforeInsert = await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown");
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.setMode('preview')");
        await pdfWindow.InsertImageAsync(samplePng);
        await UntilAsync(async () => await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown") != beforeInsert);
        Check(await imageEditor.ExecuteScriptAsync("window.hunminEditor.getMode()") == "\"source\"", "캡처 삽입 시 소스 편집으로 전환");
        string? openedImages = null;
        pdfWindow.ImageFolderOpener = path => openedImages = path;
        await imageEditor.ExecuteScriptAsync("document.querySelector('#note-images').click()");
        await UntilAsync(async () => { await Task.Yield(); return openedImages is not null; });
        Check(Path.GetDirectoryName(openedImages) == Path.GetFullPath(Path.Combine(root, "data", "notes", "assets")) && Directory.GetFiles(openedImages!, "*.png").Length > 0, "이미지 폴더 버튼이 이 메모의 이미지 폴더를 염");
        var openedLinks = new List<string>();
        var allowLink = false;
        pdfWindow.LinkConfirmation = _ => allowLink;
        pdfWindow.LinkOpener = openedLinks.Add;
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.linkRequested('https://example.com/refused')");
        await Task.Delay(300);
        Check(openedLinks.Count == 0, "확인하지 않은 링크는 열지 않음");
        allowLink = true;
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.linkRequested('file:///C:/Windows/notepad.exe')");
        await Task.Delay(300);
        Check(openedLinks.Count == 0, "파일 주소는 확인해도 열지 않음");
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.linkRequested('https://example.com/ok')");
        await UntilAsync(async () => { await Task.Yield(); return openedLinks.Count == 1; });
        Check(openedLinks[0] == "https://example.com/ok", "확인한 웹 링크만 염");
        // Mermaid는 실제 WebView2의 보안 정책 안에서 그립니다.
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.setMode('source')");
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.insertText('\\n```mermaid\\nflowchart TD\\n  가[시작] --> 나[끝]\\n```\\n')");
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.setMode('preview')");
        await UntilAsync(async () => (await imageEditor.ExecuteScriptAsync("document.querySelector('#preview figure.mermaid-diagram svg')?.textContent ?? ''")).Contains("시작"));
        Check(await imageEditor.ExecuteScriptAsync("document.querySelector('#preview .diagram-error') === null") == "true", "Mermaid를 WebView2에서 표시");
        // draw.io: 실제 앱을 띄우지 않고 열려는 파일만 확인합니다.
        var diagramOpens = new List<string>();
        var diagramMissing = 0;
        pdfWindow.DiagramAppOpener = (_, file) => diagramOpens.Add(file);
        pdfWindow.DiagramAppMissing = () => diagramMissing++;
        pdfWindow.DiagramAppFinder = () => null;
        var imageFilesBefore = Directory.GetFiles(openedImages!).Length;
        await imageEditor.ExecuteScriptAsync("document.querySelector('#new-diagram').click()");
        await UntilAsync(async () => { await Task.Yield(); return diagramMissing == 1; });
        Check(Directory.GetFiles(openedImages!).Length == imageFilesBefore && diagramOpens.Count == 0, "draw.io가 없으면 파일을 만들지 않고 안내");
        pdfWindow.DiagramAppFinder = () => "draw.io.exe";
        await imageEditor.ExecuteScriptAsync("document.querySelector('#new-diagram').click()");
        await UntilAsync(async () => { await Task.Yield(); return diagramOpens.Count == 1; });
        Check(diagramOpens[0].EndsWith(".drawio.svg") && File.Exists(diagramOpens[0]), "새 다이어그램 파일을 만들어 draw.io로 염");
        await UntilAsync(async () => (await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown")).Contains(".drawio.svg"));
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.setMode('preview')");
        await UntilAsync(async () => await imageEditor.ExecuteScriptAsync("document.querySelector('#preview img[src*=\".drawio.svg\"]')?.naturalWidth ?? 0") == "240");
        var diagramFile = $"{Path.GetFileName(Path.GetDirectoryName(diagramOpens[0]))}/{Path.GetFileName(diagramOpens[0])}";
        await imageEditor.ExecuteScriptAsync($"window.hunminEditor.diagramEditRequested('{diagramFile}')");
        await UntilAsync(async () => { await Task.Yield(); return diagramOpens.Count == 2; });
        Check(diagramOpens[1] == diagramOpens[0], "기존 다이어그램을 draw.io로 다시 염");
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.setMode('source')");
        // 캡처 주석: 입력을 보내지 않고 창의 동작과 결과 그림만 확인합니다.
        static byte[] SolidPng(int width, int height, byte blue, byte green, byte red)
        {
            var pixels = new byte[width * height * 4];
            for (var at = 0; at < pixels.Length; at += 4) { pixels[at] = blue; pixels[at + 1] = green; pixels[at + 2] = red; pixels[at + 3] = 255; }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        static (int Width, int Height, byte[] Pixels) ReadPng(byte[] png)
        {
            using var stream = new MemoryStream(png);
            var frame = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            return (frame.PixelWidth, frame.PixelHeight, pixels);
        }
        static bool IsReddish(byte[] pixels, int width, int x, int y) { var at = (y * width + x) * 4; return pixels[at + 2] > 180 && pixels[at + 1] < 110 && pixels[at] < 110; }
        var plainCapture = SolidPng(200, 120, 255, 255, 255);
        byte[]? annotated = null;
        var annotator = new CaptureAnnotationWindow(plainCapture, png => annotated = png);
        annotator.Show();
        annotator.AddAnnotation(new Annotation(AnnotationTool.Rectangle, 20, 20, 80, 60, CaptureAnnotations.Red));
        annotator.AddAnnotation(new Annotation(AnnotationTool.Arrow, 100, 30, 180, 30, CaptureAnnotations.Red));
        annotator.AddAnnotation(new Annotation(AnnotationTool.Text, 20, 80, 0, 0, CaptureAnnotations.Red, "주석"));
        annotator.AddAnnotation(new Annotation(AnnotationTool.Arrow, 0, 0, 10, 10, CaptureAnnotations.Red));
        annotator.Undo();
        Check(annotator.AnnotationCount == 3, "주석 실행 취소");
        annotator.Finish();
        Check(annotated is not null && !annotator.IsVisible, "주석 완료 시 그림을 돌려주고 창을 닫음");
        var marked = ReadPng(annotated!);
        Check(marked.Width == 200 && marked.Height == 120, "주석 그림은 원본 픽셀 크기 유지");
        Check(IsReddish(marked.Pixels, 200, 20, 40) && IsReddish(marked.Pixels, 200, 140, 30), "사각형과 화살표를 그림에 그림");
        Check(!IsReddish(marked.Pixels, 200, 50, 40) && !IsReddish(marked.Pixels, 200, 5, 5), "주석 밖의 그림은 그대로");
        Check(Enumerable.Range(20, 40).Any(x => Enumerable.Range(80, 30).Any(y => IsReddish(marked.Pixels, 200, x, y))), "글자 주석을 그림에 그림");
        var checker = new byte[40 * 40 * 4];
        for (var at = 0; at < checker.Length; at += 4) { var dark = (at / 4 % 40 + at / 4 / 40) % 2 == 0; checker[at] = checker[at + 1] = checker[at + 2] = (byte)(dark ? 0 : 255); checker[at + 3] = 255; }
        var checkerEncoder = new PngBitmapEncoder();
        checkerEncoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40, 40, 96, 96, PixelFormats.Bgra32, null, checker, 160)));
        using var checkerStream = new MemoryStream();
        checkerEncoder.Save(checkerStream);
        var hidden = ReadPng(CaptureAnnotationWindow.Render(checkerStream.ToArray(), [new Annotation(AnnotationTool.Mosaic, 0, 0, 16, 16, 0)]));
        Check(hidden.Pixels[0] == hidden.Pixels[(5 * 40 + 5) * 4] && hidden.Pixels[0] is > 100 and < 155, "모자이크가 원래 무늬를 가림");
        Check(hidden.Pixels[(30 * 40 + 30) * 4] is 0 or 255, "모자이크 밖은 그대로");
        byte[]? untouched = null;
        var plainAnnotator = new CaptureAnnotationWindow(plainCapture, png => untouched = png);
        plainAnnotator.Show();
        plainAnnotator.AddAnnotation(new Annotation(AnnotationTool.Arrow, 0, 0, 50, 50, CaptureAnnotations.Red));
        plainAnnotator.InsertOriginal();
        Check(untouched is not null && untouched.SequenceEqual(plainCapture), "그대로 넣기는 원본을 돌려줌");
        byte[]? discarded = null;
        var cancelled = new CaptureAnnotationWindow(plainCapture, png => discarded = png);
        cancelled.Show();
        cancelled.Close();
        Check(discarded is null, "주석 창을 닫으면 메모에 넣지 않음");
        // 캡처에서 메모까지: 설정이 켜져 있으면 주석 창을 거치고, 끄면 바로 넣습니다.
        await imageEditor.ExecuteScriptAsync("window.hunminEditor.setMode('source')");
        var beforeAnnotated = await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown");
        var clipboardCopies = new List<byte[]>();
        manager.CaptureClipboard = clipboardCopies.Add;
        await manager.FinishCaptureAsync(pdfWindow, plainCapture);
        var flow = application.Windows.OfType<CaptureAnnotationWindow>().Single();
        Check(clipboardCopies.Count == 0, "주석을 마치기 전에는 클립보드를 바꾸지 않음");
        Check(await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown") == beforeAnnotated, "주석을 마치기 전에는 메모에 넣지 않음");
        flow.Finish();
        await UntilAsync(async () => await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown") != beforeAnnotated);
        Check(clipboardCopies.Count == 1, "주석을 마친 그림을 클립보드에 넣음");
        manager.AnnotateCapture = false;
        var beforeDirect = await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown");
        await manager.FinishCaptureAsync(pdfWindow, plainCapture);
        await UntilAsync(async () => await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown") != beforeDirect);
        Check(!application.Windows.OfType<CaptureAnnotationWindow>().Any(), "주석을 끄면 바로 메모에 넣음");
        Check(clipboardCopies.Count == 2 && clipboardCopies[1].SequenceEqual(plainCapture), "주석을 끄면 캡처한 그림을 클립보드에 넣음");
        // 실제 클립보드는 사용자의 내용을 덮어쓰므로 CI에서만 확인합니다.
        if (Environment.GetEnvironmentVariable("CI") == "true")
        {
            Check(DesktopNoteManager.CopyCaptureToClipboard(plainCapture), "실제 클립보드에 씀");
            Check(System.Windows.Clipboard.GetData("PNG") is MemoryStream copied && copied.ToArray().SequenceEqual(plainCapture) && System.Windows.Clipboard.ContainsImage(), "실제 클립보드에 PNG와 비트맵이 들어감");
        }
        manager.AnnotateCapture = true;
        var beforeCopy = await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown");
        await manager.FinishCaptureAsync(pdfWindow, plainCapture);
        var copier = application.Windows.OfType<CaptureAnnotationWindow>().Single();
        copier.AddAnnotation(new Annotation(AnnotationTool.Rectangle, 20, 20, 80, 60, CaptureAnnotations.Red));
        copier.CopyToClipboard();
        Check(!application.Windows.OfType<CaptureAnnotationWindow>().Any(), "클립보드에 넣으면 주석 창 닫힘");
        Check(clipboardCopies.Count == 3 && !clipboardCopies[2].SequenceEqual(plainCapture), "클립보드에 넣기는 주석을 넣은 그림을 복사");
        await Task.Delay(300);
        Check(await imageEditor.ExecuteScriptAsync("window.hunminEditor.getDraft().markdown") == beforeCopy, "클립보드에 넣기는 메모를 바꾸지 않음");
        // 가져오기와 이미지 정리: 파일 대화상자, 확인 창, Windows 휴지통 없이 분리한 데이터 폴더에서만 확인합니다.
        var importFile = Path.Combine(root, "가져올 메모.md");
        File.WriteAllText(importFile, "# 가져온 본문");
        var importReports = new List<string>();
        manager.ImportReporter = importReports.Add;
        manager.ImportPathPicker = () => [importFile, Path.Combine(root, "없는 파일.md")];
        await manager.ImportMarkdownAsync();
        var importedNote = workspace.Notes.Single(note => note.Title == "가져올 메모");
        Check(importedNote.Markdown == "# 가져온 본문" && File.ReadAllText(importFile) == "# 가져온 본문", "외부 Markdown을 새 메모로 가져오고 원본 유지");
        Check(importReports.Single().Contains("없는 파일.md"), "가져오지 못한 파일 보고");
        await application.Windows.OfType<MainWindow>().Single(window => window.NoteId == importedNote.Id).RequestCloseAsync();
        var cleanFolder = workspace.EnsureImageFolder(importedNote.Id);
        var cleanFile = Path.Combine(cleanFolder, new string('d', 32) + ".png");
        File.WriteAllBytes(cleanFile, [1, 2, 3]);
        var deletedImages = new List<string>();
        manager.ImageDeleter = deletedImages.Add;
        var cleanCandidates = await manager.FindUnusedImagesAsync();
        Check(cleanCandidates.Any(image => image.Path == cleanFile), "쓰지 않는 이미지 찾기");
        Check(await manager.DeleteUnusedImagesAsync(cleanCandidates) == cleanCandidates.Count && deletedImages.Contains(cleanFile), "쓰지 않는 이미지 정리");
        var pdfPath = Path.Combine(root, "note.pdf");
        pdfWindow.PdfPathPicker = _ => pdfPath;
        var pdfEditor = (WebView2)pdfWindow.FindName("EditorView");
        await pdfEditor.ExecuteScriptAsync("document.querySelector('#save-pdf').click()");
        await UntilAsync(async () => File.Exists(pdfPath) && await pdfEditor.ExecuteScriptAsync("document.querySelector('#save-pdf').disabled") == "false");
        Check(!(await pdfEditor.ExecuteScriptAsync("document.querySelector('#host-status').textContent")).Contains("PDF"), "PDF 저장 경로를 하단에 표시하지 않음");
        using (var pdf = File.OpenRead(pdfPath))
        {
            var header = new byte[4];
            pdf.ReadExactly(header);
            Check(System.Text.Encoding.ASCII.GetString(header) == "%PDF" && pdf.Length > 500, "PDF 파일 저장");
        }
        Check(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(pdfPath)).Contains("/Image"), "PDF에 이미지 포함");
        // 0.2.0에서 투명하게 저장된 이미지 복구
        var repairRoot = Path.Combine(root, "repair-assets");
        var repairDirectory = Path.Combine(repairRoot, new string('a', 32));
        Directory.CreateDirectory(repairDirectory);
        byte[] SaveAlpha(byte alpha)
        {
            var values = new byte[16];
            for (var index = 0; index < 16; index += 4) { values[index] = 10; values[index + 1] = 120; values[index + 2] = 230; values[index + 3] = alpha; }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, values, 8)));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        var transparentFile = Path.Combine(repairDirectory, new string('b', 32) + ".png");
        var partialFile = Path.Combine(repairDirectory, new string('c', 32) + ".png");
        var otherFile = Path.Combine(repairDirectory, "사용자 파일.png");
        File.WriteAllBytes(transparentFile, SaveAlpha(0));
        File.WriteAllBytes(partialFile, SaveAlpha(128));
        File.WriteAllBytes(otherFile, SaveAlpha(0));
        var partialBefore = File.ReadAllBytes(partialFile);
        var otherBefore = File.ReadAllBytes(otherFile);
        Check(NoteImageRepair.Run(repairRoot) == 1, "완전히 투명한 앱 이미지만 복구");
        Check(Pixel(File.ReadAllBytes(transparentFile)) is [10, 120, 230, 255], "복구한 이미지의 색상 유지");
        Check(File.ReadAllBytes(partialFile).SequenceEqual(partialBefore) && File.ReadAllBytes(otherFile).SequenceEqual(otherBefore), "다른 이미지는 건드리지 않음");
        Check(NoteImageRepair.Run(Path.Combine(root, "없는 폴더")) == 0, "이미지 폴더가 없어도 동작");
        Check(await pdfWindow.RequestCloseAsync(), "PDF 저장 후 메모 닫기");
        manager.ShowSettings();
        var backupSettings = application.Windows.OfType<SettingsWindow>().Single();
        await UntilAsync(async () => backupSettings.View.CoreWebView2 is not null && await backupSettings.View.ExecuteScriptAsync("document.querySelector('#backup-row')?.hidden === false") == "true");
        var backupPath = Path.Combine(root, "backup.zip");
        backupSettings.BackupPathPicker = () => backupPath;
        await backupSettings.View.ExecuteScriptAsync("document.querySelector('#backup').click()");
        await UntilAsync(() => Task.FromResult(File.Exists(backupPath)));
        Check(backupSettings.Icon is not null, "설정 창 유지");
        await UntilAsync(async () => await backupSettings.View.ExecuteScriptAsync("document.querySelectorAll('#body-font option').length > 1") == "true");
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
    private static byte[] SamplePng()
    {
        var pixels = new byte[16];
        Array.Fill<byte>(pixels, 200);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
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
