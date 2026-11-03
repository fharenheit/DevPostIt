using System.Text.Json;
using DevPostIt.Mac;
using DevPostIt.Storage;
using CoreGraphics;
using WebKit;

// macOS 메모 창의 수명 시험입니다. 별도 데이터 폴더와 웹 저장소를 쓰며 합성 입력을 보내지 않습니다.
Environment.SetEnvironmentVariable("DEVPOSTIT_WEBKIT_STORE", Guid.NewGuid().ToString().ToUpperInvariant());
NSApplication.Init();
NSApplication.SharedApplication.Delegate = new SmokeDelegate();
NSApplication.Main(args);

internal sealed class SmokeDelegate : NSApplicationDelegate
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
    private int failures;
    private string? root;
    private MacNoteManager? finalManager;

    public override async void DidFinishLaunching(NSNotification notification)
    {
        NSApplication.SharedApplication.MainMenu = new NSMenu();
        var root = Path.Combine(Path.GetTempPath(), "devpostit-mac-smoke-" + Guid.NewGuid().ToString("N"));
        this.root = root;
        try
        {
            await RemoveEarlierWebStoresAsync();
            var run = RunAsync(root);
            if (await Task.WhenAny(run, Task.Delay(TimeSpan.FromMinutes(3))) != run) throw new TimeoutException("시험 시간을 넘겼습니다.");
            await run;
            // 실제 ⌘Q와 같은 경로입니다. 끝나면 WillTerminate로 옵니다.
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => { Report("실패: 종료 응답이 없습니다."); Environment.Exit(1); });
            NSApplication.SharedApplication.Terminate(this);
        }
        catch (Exception error)
        {
            Report($"실패: {error}");
            Finish(1);
        }
    }

    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => MacTermination.Begin(sender, finalManager);

    public override void WillTerminate(NSNotification notification)
    {
        Check(finalManager?.Exited == true, "Terminate에서 모든 창 저장 후 응답");
        var file = Directory.GetFiles(Path.Combine(root!, "notes"), "*.md", SearchOption.AllDirectories);
        Check(file.Any(path => File.ReadAllText(path).Contains("# ⌘Q 직전 입력")), "⌘Q 직전 입력을 저장");
        Finish(failures == 0 ? 0 : 1);
    }

    // 쓰는 중인 저장소는 지울 수 없어 시작할 때 이전 실행의 것을 지웁니다. 시험 앱의 번들 ID 안에서만 찾습니다.
    private static async Task RemoveEarlierWebStoresAsync()
    {
        // WebKit이 초기화되기 전에 목록을 물으면 WebKit 안에서 강제 종료됩니다.
        using var configuration = new WKWebViewConfiguration();
        foreach (var id in await WKWebsiteDataStore.FetchAllDataStoreIdentifiersAsync())
        {
            var done = new TaskCompletionSource();
            WKWebsiteDataStore.Remove(id, error =>
            {
                if (error is not null) Report($"이전 웹 저장소를 지우지 못함: {error.LocalizedDescription}");
                done.TrySetResult();
            });
            await done.Task;
        }
    }

    private void Finish(int code)
    {
        Report(code == 0 ? "MacSmoke 통과" : $"MacSmoke 실패: {failures}개");
        try { Directory.Delete(root!, true); } catch (IOException) { }
        Environment.Exit(code);
    }

    // 앱 번들로 실행하면 콘솔이 없을 수 있어 DEVPOSTIT_SMOKE_LOG에도 남깁니다.
    private static void Report(string line)
    {
        Console.WriteLine(line);
        if (Environment.GetEnvironmentVariable("DEVPOSTIT_SMOKE_LOG") is { Length: > 0 } log) File.AppendAllText(log, line + "\n");
    }

    private void Check(bool passed, string name)
    {
        Report($"{(passed ? "통과" : "실패")}: {name}");
        if (!passed) failures++;
    }

    private static async Task UntilAsync(Func<bool> condition, string name)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (condition()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"기다리다 멈췄습니다: {name}");
    }

    private static async Task<MacNoteWindow> ReadyAsync(MacNoteManager manager, Guid id)
    {
        var window = manager.ShowNote(id) ?? throw new InvalidOperationException("창을 열지 못했습니다.");
        await UntilAsync(() => window.Session.IsEditorReady, "편집기 준비");
        return window;
    }

    private static async Task<T?> Evaluate<T>(MacNoteWindow window, string expression) => JsonSerializer.Deserialize<T>(await window.EvaluateAsync(expression));

    private static Task SetTextAsync(MacNoteWindow window, string text, long revision, string title) =>
        window.EvaluateAsync($"window.hunminEditor.load({JsonSerializer.Serialize(window.NoteId.ToString())}, {JsonSerializer.Serialize(text)}, {revision}, {JsonSerializer.Serialize(title)})");

    private static Task<T?> Page<T>(MacWebWindow window, string expression) =>
        window.EvaluateAsync(expression).ContinueWith(task => JsonSerializer.Deserialize<T>(task.Result), TaskScheduler.FromCurrentSynchronizationContext());

    private static async Task PageReadyAsync(MacWebWindow window, string expression)
    {
        await UntilAsync(() => window.View is { IsLoading: false }, "페이지 불러오기");
        for (var attempt = 0; attempt < 300; attempt++)
        {
            try { if (await Page<bool>(window, $"Boolean({expression})")) return; }
            catch (InvalidOperationException) { /* 불러오는 중입니다. */ }
            catch (AggregateException) { /* 불러오는 중입니다. */ }
            await Task.Delay(100);
        }
        throw new TimeoutException($"페이지 준비를 기다리다 멈췄습니다: {expression}");
    }

    private async Task WaitPageAsync(MacWebWindow window, string expression, string name)
    {
        await PageReadyAsync(window, expression);
        Check(true, name);
    }

    private async Task CheckToolWindowsAsync(string root, NoteWorkspace workspace, MacNoteManager manager, AppSettingsStore store)
    {
        // 메모 전체 검색입니다.
        manager.ShowSearch();
        var search = manager.Search!;
        await PageReadyAsync(search, "document.querySelector('#search-query')");
        var target = workspace.Notes[0];
        await search.EvaluateAsync($"(() => {{ const input = document.querySelector('#search-query'); input.value = {JsonSerializer.Serialize(target.Title)}; input.dispatchEvent(new Event('input', {{ bubbles: true }})); }})()");
        await WaitPageAsync(search, "document.querySelector('#search-results button')", "검색 결과 표시");
        await search.EvaluateAsync("document.querySelector('#search-results button').click()");
        await UntilAsync(() => manager.Windows.ContainsKey(target.Id), "검색 결과에서 메모 열기");
        Check(true, "검색 결과에서 메모 열기");
        Check(await Page<bool>(search, "fetch('https://example.com/').then(() => false, () => true)"), "보조 창도 외부 fetch 차단");
        await search.EvaluateAsync("(location.href = 'devpostit://app/index.html', true)");
        await Task.Delay(300);
        Check(await Page<string>(search, "location.href") == "devpostit://app/search.html", "보조 창은 다른 페이지로 이동하지 못함");
        search.Close();
        Check(manager.Search is null, "검색 창 닫기");

        // 설정입니다. 메모 위가 아니라 독립 창으로 열리고 모든 메모에 반영합니다.
        var note = manager.Windows[target.Id];
        manager.ShowSettings();
        var settings = manager.Settings!;
        await PageReadyAsync(settings, "document.querySelector('#larger')");
        var before = await Evaluate<double>(note, "parseFloat(getComputedStyle(document.body).fontSize)");
        await settings.EvaluateAsync("document.querySelector('#larger').click()");
        for (var attempt = 0; attempt < 50 && await Evaluate<double>(note, "parseFloat(getComputedStyle(document.body).fontSize)") == before; attempt++) await Task.Delay(100);
        Check(await Evaluate<double>(note, "parseFloat(getComputedStyle(document.body).fontSize)") == before + 1, "설정의 글자 크기가 열린 메모에 반영");
        await settings.EvaluateAsync("document.querySelector('#smaller').click()");
        await WaitPageAsync(settings, "document.querySelector('#data-folder-row') && !document.querySelector('#data-folder-row').hidden", "설정에 데이터 폴더 표시");
        Check(await Page<bool>(settings, "document.querySelector('#auto-update-row').hidden"), "자동 업데이트 항목 숨김");
        await WaitPageAsync(settings, "document.querySelector('#font option[value^=\"custom:\"]')", "설치된 글꼴을 설정에 전달");
        string? opened = null;
        settings.FolderOpener = path => opened = path;
        await settings.EvaluateAsync("document.querySelector('#open-data-folder').click()");
        await UntilAsync(() => opened is not null, "데이터 폴더 열기");
        Check(opened == workspace.Root, "데이터 폴더 열기");
        await settings.EvaluateAsync("chrome.webview.postMessage({ protocolVersion: 1, type: 'settings.holidayUpdates', value: false })");
        await UntilAsync(() => !store.Current.HolidayUpdates, "설정 저장");
        Check(File.ReadAllText(Path.Combine(root, ".hunmin", "app-settings.json")).Contains("\"HolidayUpdates\": false", StringComparison.OrdinalIgnoreCase) || !store.Current.HolidayUpdates, "설정 메시지를 저장");
        store.Current.HolidayUpdates = true;

        // 백업과 쓰지 않는 이미지 정리입니다. 대화 상자와 실제 휴지통은 쓰지 않습니다.
        var backup = Path.Combine(root, "..", "devpostit-mac-backup-" + Guid.NewGuid().ToString("N") + ".zip");
        settings.BackupPathPicker = () => backup;
        await settings.EvaluateAsync("document.querySelector('#backup').click()");
        await UntilAsync(() => File.Exists(backup) && new FileInfo(backup).Length > 0, "백업 파일");
        Check(true, "백업 파일 저장");
        await WaitPageAsync(settings, "document.querySelector('#settings-status')?.textContent.includes('백업했습니다') || document.body.textContent.includes('백업했습니다')", "백업 결과 표시");
        File.Delete(backup);
        var trashed = new List<string>();
        manager.ImageDeleter = trashed.Add;
        settings.ImageCleanupConfirmation = (count, _) => count == 1;
        await settings.EvaluateAsync("document.querySelector('#clean-images').click()");
        await UntilAsync(() => trashed.Count == 1, "쓰지 않는 이미지 정리");
        Check(trashed[0].EndsWith(".png", StringComparison.Ordinal), "쓰지 않는 이미지만 휴지통으로 넘김");
        settings.Close();

        // 포맷터는 입력을 저장하지 않습니다.
        manager.ShowFormatter();
        await WaitPageAsync(manager.Formatter!, "document.querySelector('.monaco-editor')", "포맷터 표시");
        manager.Formatter!.Close();

        // 마인드맵은 닫기 전에 편집 중인 내용을 저장합니다.
        var toolsRoot = Path.Combine(root, ".hunmin", "tools");
        manager.ShowMindMap();
        var mindMap = manager.MindMap!;
        await WaitPageAsync(mindMap, "document.querySelector('.mm-node.root')", "마인드맵 표시");
        await mindMap.EvaluateAsync("document.querySelector('.mm-editor')?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))");
        await mindMap.EvaluateAsync("document.querySelector('[data-action=\"child\"]').click()");
        await PageReadyAsync(mindMap, "document.querySelector('.mm-editor')");
        await mindMap.EvaluateAsync("(() => { const editor = document.querySelector('.mm-editor'); editor.value = '닫기 직전 주제'; editor.dispatchEvent(new Event('input', { bubbles: true })); })()");
        await mindMap.FlushAndCloseAsync();
        Check(Directory.EnumerateFiles(Path.Combine(toolsRoot, "mindmaps")).Any(file => File.ReadAllText(file).Contains("닫기 직전 주제")), "마인드맵을 닫기 전에 저장");

        // 할 일과 달력은 같은 저장소를 쓰고 서로의 변경을 받습니다.
        manager.ShowTodo();
        var todo = manager.Todo!;
        await PageReadyAsync(todo, "document.querySelector('#todo-title')?.textContent");
        var month = DateTime.Now.ToString("yyyy-MM");
        File.WriteAllText(Path.Combine(toolsRoot, "korean-holidays.json"), $$$"""{"version":1,"years":{"{{{DateTime.Now.Year}}}":[{"date":"{{{month}}}-15","name":"시험 공휴일","substitute":false}]}}""");
        manager.ShowCalendar();
        var calendar = manager.Calendar!;
        await PageReadyAsync(calendar, "document.querySelector('[data-view=\"month\"]')");
        await calendar.EvaluateAsync("document.querySelector('[data-view=\"month\"]').click()");
        await WaitPageAsync(calendar, $"document.querySelector('[data-date=\"{month}-15\"] .cal-holiday')?.textContent.includes('시험 공휴일')", "달력에 공휴일 표시");
        File.Delete(Path.Combine(toolsRoot, "korean-holidays.json"));
        await todo.EvaluateAsync("(() => { const input = document.querySelector('#todo-input'); input.value = '앱에서 등록'; input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); })()");
        for (var attempt = 0; attempt < 100 && !(await manager.Planner.LoadAsync(CancellationToken.None)).Todos.Any(item => item.Title == "앱에서 등록"); attempt++) await Task.Delay(100);
        Check(File.ReadAllText(Path.Combine(toolsRoot, "planner.json")).Contains("앱에서 등록"), "할 일을 파일에 저장");
        await WaitPageAsync(calendar, "document.body.textContent.length > 0", "달력 유지");
        todo.Close();
        calendar.Close();
        for (var attempt = 0; attempt < 50 && (manager.Todo is not null || manager.Calendar is not null); attempt++) await Task.Delay(100);
        Check(manager.Todo is null && manager.Calendar is null, "할 일과 달력 창 닫기");

        // 도움말과 정보입니다.
        manager.ShowHelp();
        var help = manager.Help!.Text;
        var helpText = help.TextStorage!.Value!;
        Check(helpText.Contains("⌘F") && !helpText.Contains("Ctrl") && !helpText.Contains("트레이"), "도움말이 macOS 단축키로 표시");
        Check(help.TextStorage!.GetAttribute(NSStringAttributeKey.Font, 0, out _) is NSFont font && font.PointSize == 13, "도움말 글자 크기 13");
        manager.Help!.Close();
        Check(manager.Help is null, "도움말 창 닫기");
        manager.ShowAbout();
        Check(manager.About!.Text.TextStorage!.Value!.Contains($"버전 {MacNoteManager.CurrentVersion}"), "정보에 버전 표시");
        manager.About!.Close();
    }

    private async Task CheckNoteShellAsync(string root, NoteWorkspace workspace, MacNoteManager manager, MacNoteWindow window)
    {
        // PDF는 대화 상자 없이 파일로 저장합니다.
        var pdf = Path.Combine(root, "..", "devpostit-mac-" + Guid.NewGuid().ToString("N") + ".pdf");
        Check(await window.PrintToPdfAsync(pdf), "PDF 저장 작업 성공");
        var header = File.ReadAllBytes(pdf);
        Check(header.Length > 1000 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "%PDF", "PDF 파일 생성");
        if (Environment.GetEnvironmentVariable("DEVPOSTIT_SMOKE_PDF") is { Length: > 0 } copy) File.Copy(pdf, copy, true);
        File.Delete(pdf);

        // 사용자의 클립보드를 건드리지 않도록 별도 보드를 씁니다.
        var board = NSPasteboard.CreateWithUniqueName();
        Check(MacClipboard.ReadPng(board) is null, "빈 클립보드에는 이미지 없음");
        using (var png = NSData.FromArray(Convert.FromBase64String(Png)))
        {
            board.ClearContents();
            board.SetDataForType(png, "public.png");
            Check(MacClipboard.ReadPng(board)!.SequenceEqual(Convert.FromBase64String(Png)), "클립보드 PNG 읽기");
            using var image = NSBitmapImageRep.ImageRepFromData(png) as NSBitmapImageRep;
            using var tiff = image!.TiffRepresentation;
            board.ClearContents();
            board.SetDataForType(tiff!, "public.tiff");
            Check(MacClipboard.ReadPng(board) is [0x89, 0x50, 0x4E, 0x47, ..], "클립보드 TIFF를 PNG로 변환");
        }
        board.ReleaseGlobally();

        // Markdown 가져오기입니다.
        var source = Path.Combine(root, "..", "devpostit-mac-import-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(source, "# 가져온 메모\n");
        manager.ImportPathPicker = () => [source];
        var count = workspace.Notes.Count;
        await manager.ImportMarkdownAsync();
        File.Delete(source);
        Check(workspace.Notes.Count == count + 1 && workspace.Notes.Any(item => item.Markdown.Contains("# 가져온 메모")), "Markdown 가져오기");
    }

    // 오버레이와 실제 촬영은 띄우지 않습니다. 화면 기록 권한과 실제 마우스가 필요해 수동 시험표로 확인합니다.
    // Playwright WebKit이 가로챈 이미지를 표시하지 못해 건너뛴 웹 시험의 동작을 실제 WKWebView와 스킴 처리기로 확인합니다.
    // 클릭은 페이지 안의 DOM 호출이며 사용자 데스크톱에 입력을 보내지 않습니다. draw.io를 여는 단추는 누르지 않습니다.
    private async Task CheckPreviewImagesAsync(NoteWorkspace workspace, MacNoteWindow window)
    {
        const string images = "document.querySelectorAll('#preview img')";
        const string menu = "document.querySelector('[aria-label=\"이미지 크기\"]')";
        static string Choose(string label) => $"[...document.querySelectorAll('[aria-label=\"이미지 크기\"] button')].find(b => b.textContent === {JsonSerializer.Serialize(label)}).click()";
        async Task WaitAsync(string expression, string name)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!await Evaluate<bool>(window, expression))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException($"{name}: {await window.EvaluateAsync("document.querySelector('#preview').outerHTML")} / {await window.EvaluateAsync("window.hunminEditor.getDraft().markdown")}");
                await Task.Delay(50);
            }
            Check(true, name);
        }

        var image = await workspace.SaveImageAsync(window.NoteId, Convert.FromBase64String(Png), CancellationToken.None);
        var (diagram, _) = await workspace.CreateDiagramAsync(window.NoteId, CancellationToken.None);
        var note = workspace.Notes.Single(item => item.Id == window.NoteId);
        var original = await Evaluate<string>(window, "window.hunminEditor.getDraft().markdown");
        var state = await workspace.Service.GetStateAsync(window.NoteId, CancellationToken.None);
        await SetTextAsync(window, $"{image}\n\n{diagram}\n", state.Revision + 1, note.Title);
        await window.EvaluateAsync("window.hunminEditor.setMode('preview')");
        await WaitAsync($"{images}.length === 2 && {images}[0].naturalWidth > 0", "미리보기에 PNG 표시");
        await WaitAsync($"{images}[1].naturalWidth > 0", "미리보기에 draw.io SVG 표시");

        await window.EvaluateAsync($"{images}[0].click()");
        await WaitAsync($"{menu} !== null && !{menu}.hidden", "이미지를 누르면 크기 메뉴 표시");
        Check(!await Evaluate<bool>(window, "[...document.querySelectorAll('button')].some(b => b.textContent === 'draw.io로 편집' && b.checkVisibility())"), "PNG 메뉴에는 draw.io 편집 없음");
        await window.EvaluateAsync($"{images}[1].click()");
        await WaitAsync("[...document.querySelectorAll('button')].some(b => b.textContent === 'draw.io로 편집' && b.checkVisibility())", "draw.io 그림 메뉴에 편집 표시");

        await window.EvaluateAsync(Choose("50%"));
        await WaitAsync("window.hunminEditor.getDraft().markdown.includes('![다이어그램|50%](')", "크기 메뉴가 원문의 그 이미지만 바꿈");
        Check(await Evaluate<bool>(window, $"{images}[1].getAttribute('width') === '50%' && {images}[0].getAttribute('width') === null"), "고른 크기를 미리보기에 반영");

        // 앱으로 돌아오면 draw.io 그림만 다시 읽습니다. 스킴 처리기가 다시 읽는 주소도 표시해야 합니다.
        var pngBefore = await Evaluate<string>(window, $"{images}[0].getAttribute('src')");
        var svgBefore = await Evaluate<string>(window, $"{images}[1].getAttribute('src')");
        await Task.Delay(5);
        ((NSWindowDelegate)window.Delegate!).DidBecomeKey(NSNotification.FromName("DevPostItSmoke", window));
        await WaitAsync($"{images}[1].getAttribute('src') !== {JsonSerializer.Serialize(svgBefore)} && {images}[1].complete && {images}[1].naturalWidth > 0", "돌아오면 draw.io 그림을 다시 읽음");
        Check(await Evaluate<string>(window, $"{images}[0].getAttribute('src')") == pngBefore, "PNG는 다시 읽지 않음");
        await window.EvaluateAsync("window.hunminEditor.setMode('source')");
        state = await workspace.Service.GetStateAsync(window.NoteId, CancellationToken.None);
        await SetTextAsync(window, original!, state.Revision + 1, note.Title);
    }

    private async Task CheckCaptureAsync(NoteWorkspace workspace, MacNoteManager manager, AppSettingsStore store, MacNoteWindow window)
    {
        Check(MacCaptureSession.VirtualScreen() is { Width: > 0, Height: > 0 }, "화면 전체 영역 계산");
        var own = window.Frame;
        window.BringToFront();
        await Task.Delay(100);
        var primary = NSScreen.Screens[0].Frame.Height;
        var found = MacWindowLookup.FindWindow((int)(own.X + own.Width / 2), (int)(primary - own.Y - own.Height / 2));
        Check(found is not { } rect || rect != new DevPostIt.EditorHost.ScreenRect((int)own.X, (int)(primary - own.Y - own.Height), (int)own.Width, (int)own.Height), "창 찾기에서 이 앱의 창 제외");

        // 40×40 체크 무늬에 모자이크를 넣으면 원래 무늬가 그림에 남지 않습니다.
        const int size = 40;
        var pattern = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var value = (byte)((x + y) % 2 == 0 ? 0 : 255);
                var at = (y * size + x) * 4;
                pattern[at] = pattern[at + 1] = pattern[at + 2] = value;
                pattern[at + 3] = 255;
            }
        using var space = CoreGraphics.CGColorSpace.CreateSrgb();
        using var context = new CoreGraphics.CGBitmapContext(pattern, size, size, 8, size * 4, space, CoreGraphics.CGBitmapFlags.ByteOrder32Little | CoreGraphics.CGBitmapFlags.PremultipliedFirst);
        using var source = context.ToImage();
        var png = CaptureImage.ToPng(source!);
        var (width, height, read) = CaptureImage.Read(png);
        Check(width == size && height == size && read[0] == 0 && read[4] == 255, "캡처 그림을 픽셀로 읽기");

        var mosaic = new DevPostIt.EditorHost.Annotation(DevPostIt.EditorHost.AnnotationTool.Mosaic, 0, 0, 16, 16, DevPostIt.EditorHost.CaptureAnnotations.Red);
        var (_, _, covered) = CaptureImage.Read(CaptureImage.Render(png, [mosaic], 1));
        Check(Math.Abs(covered[0] - covered[4]) <= 2 && covered[0] is > 100 and < 155, "모자이크가 픽셀 자체를 바꿈");
        Check(covered[16 * 4 + 16 * size * 4] != covered[17 * 4 + 16 * size * 4], "모자이크 밖의 픽셀은 유지");

        var arrow = new DevPostIt.EditorHost.Annotation(DevPostIt.EditorHost.AnnotationTool.Rectangle, 5, 5, 35, 35, DevPostIt.EditorHost.CaptureAnnotations.Red);
        var (_, _, boxed) = CaptureImage.Read(CaptureImage.Render(png, [arrow], 1));
        var border = (20 + 5 * size) * 4;
        Check(boxed[border + 2] > 180 && boxed[border] < 110, "사각형을 빨간색으로 위쪽에 그림");
        var mirrored = (20 + 34 * size) * 4;
        Check(boxed[mirrored + 2] > 180, "사각형의 아래쪽도 제자리에 그림");

        var words = new DevPostIt.EditorHost.Annotation(DevPostIt.EditorHost.AnnotationTool.Text, 2, 2, 0, 0, DevPostIt.EditorHost.CaptureAnnotations.Black, "가");
        Check(!CaptureImage.Render(png, [words], 1).SequenceEqual(png), "글자 주석을 그림");

        // 주석을 끄면 바로, 켜면 주석 창을 거쳐 최근 메모에 넣습니다.
        await window.EvaluateAsync("window.hunminEditor.setMode('source')");
        store.Current.AnnotateCapture = false;
        await manager.FinishCaptureAsync(window, png, 1);
        await UntilAsync(() => Directory.EnumerateFiles(Path.Combine(workspace.AssetsRoot, window.NoteId.ToString("N"))).Count(file => file.EndsWith(".png", StringComparison.Ordinal)) >= 1, "캡처 삽입");
        Check(true, "주석을 끄면 바로 메모에 삽입");
        store.Current.AnnotateCapture = true;
        await manager.FinishCaptureAsync(window, png, 1);
        var annotator = manager.Annotators.Single();
        annotator.AddAnnotation(arrow);
        annotator.AddAnnotation(mosaic);
        annotator.Undo();
        Check(annotator.AnnotationCount == 1, "주석 실행 취소");
        var before = Directory.EnumerateFiles(Path.Combine(workspace.AssetsRoot, window.NoteId.ToString("N"))).Count();
        annotator.Finish();
        await UntilAsync(() => Directory.EnumerateFiles(Path.Combine(workspace.AssetsRoot, window.NoteId.ToString("N"))).Count() == before + 1, "주석 그림 삽입");
        Check(manager.Annotators.Count == 0, "메모에 넣으면 주석 창 닫힘");
        await manager.FinishCaptureAsync(window, png, 1);
        var discarded = manager.Annotators.Single();
        discarded.Close();
        await Task.Delay(300);
        Check(manager.Annotators.Count == 0 && Directory.EnumerateFiles(Path.Combine(workspace.AssetsRoot, window.NoteId.ToString("N"))).Count() == before + 1, "주석 창을 닫으면 그림을 버림");
    }

    // 클릭 이벤트 객체를 직접 넘깁니다. 이벤트 큐와 사용자 데스크톱에는 아무것도 보내지 않습니다.
    private async Task CheckStatusClicksAsync(NoteWorkspace workspace, MacNoteManager manager)
    {
        NSEvent Click(NSEventType type, NSEventModifierMask flags = 0) =>
            NSEvent.MouseEvent(type, CGPoint.Empty, flags, NSProcessInfo.ProcessInfo.SystemUptime, 0, null, 0, 0, 0f);
        var opened = 0;
        // 실제 메뉴를 열면 추적이 끝나지 않으므로 표시 대신 횟수를 셉니다.
        var status = new StatusBarMenu(manager, workspace) { PresentMenu = _ => opened++ };
        var notes = workspace.Notes.Count;

        var button = status.ClickView.Superview!;
        var center = button.Superview is { } parent ? button.ConvertPointToView(new CGPoint(button.Bounds.Width / 2, button.Bounds.Height / 2), parent) : CGPoint.Empty;
        Check(button.Superview is not null && button.HitTest(center) == status.ClickView, "메뉴 막대 아이콘의 클릭을 ClickView가 받음");

        status.ClickView.RightMouseDown(Click(NSEventType.RightMouseDown));
        await Task.Delay(1000);
        Check(opened == 1 && workspace.Notes.Count == notes, $"오른쪽 클릭은 메뉴만 표시 (메뉴 {opened}번, 새 메모 {workspace.Notes.Count - notes}개)");

        status.ClickView.MouseDown(Click(NSEventType.LeftMouseDown, NSEventModifierMask.ControlKeyMask));
        status.ClickView.MouseUp(Click(NSEventType.LeftMouseUp, NSEventModifierMask.ControlKeyMask));
        await Task.Delay(1000);
        Check(opened == 2 && workspace.Notes.Count == notes, $"⌃클릭은 메뉴만 표시 (메뉴 {opened}번, 새 메모 {workspace.Notes.Count - notes}개)");

        status.ClickView.MouseDown(Click(NSEventType.LeftMouseDown));
        status.ClickView.MouseUp(Click(NSEventType.LeftMouseUp));
        await Task.Delay(1000);
        Check(opened == 3 && workspace.Notes.Count == notes, $"왼쪽 클릭은 메뉴만 표시 (메뉴 {opened}번, 새 메모 {workspace.Notes.Count - notes}개)");
    }

    private async Task RunAsync(string root)
    {
        var store = new AppSettingsStore(Path.Combine(root, ".hunmin", "app-settings.json"));
        var workspace = await NoteWorkspace.OpenAsync(root, CancellationToken.None);
        var manager = new MacNoteManager(workspace, store);
        var first = workspace.Notes.Single();

        // 실제 번들의 Monaco가 사용자 지정 스킴에서 뜨고 브리지가 메모를 전달합니다.
        var window = await ReadyAsync(manager, first.Id);
        Check(await Evaluate<string>(window, "location.href") == "devpostit://app/index.html", "앱 스킴에서 페이지 표시");
        Check(await Evaluate<bool>(window, "isSecureContext"), "보안 컨텍스트");
        Check(await Evaluate<string>(window, "window.hunminEditor.getDraft().markdown") == first.Markdown, "브리지로 메모 본문 전달");
        Check(await Evaluate<string>(window, "window.hunminEditor.getTitle()") == first.Title, "브리지로 메모 이름 전달");
        Check(window.Title == "Post It for Developer", "창 제목 고정");

        // 리소스 정책입니다.
        Check(await Evaluate<int>(window, "fetch('devpostit://app/missing.js').then(r => r.status)") == 404, "없는 웹 파일은 404");
        Check(await Evaluate<bool>(window, "fetch('https://example.com/').then(() => false, () => true)"), "외부 fetch 차단");
        Check(await Evaluate<bool>(window, "new Promise(r => { const i = new Image(); i.onload = () => r(false); i.onerror = () => r(true); i.src = 'https://example.com/x.png'; })"), "외부 이미지 차단");
        var image = await workspace.SaveImageAsync(first.Id, Convert.FromBase64String(Png), CancellationToken.None);
        var asset = image[(image.IndexOf('(') + 1)..image.IndexOf(')')];
        var assetUrl = "devpostit-assets://notes/" + asset[(asset.IndexOf("assets/", StringComparison.Ordinal) + "assets/".Length)..];
        Check(await Evaluate<bool>(window, $"new Promise(r => {{ const i = new Image(); i.onload = () => r(true); i.onerror = () => r(false); i.src = {JsonSerializer.Serialize(assetUrl)}; }})"), "메모 이미지 표시");
        Check(await Evaluate<bool>(window, $"fetch({JsonSerializer.Serialize(assetUrl)}).then(() => false, () => true)"), "메모 이미지를 스크립트로 읽지 못함");

        // 자동 저장입니다.
        var state = await workspace.Service.GetStateAsync(first.Id, CancellationToken.None);
        await SetTextAsync(window, "# macOS 저장 시험\n", state.Revision + 1, first.Title);
        await UntilAsync(() => workspace.Notes.Single().Markdown == "# macOS 저장 시험\n", "자동 저장");
        await UntilAsync(() => File.ReadAllText(Path.Combine(root, "notes", workspace.Notes.Single().RelativePath)).Contains("# macOS 저장 시험"), "파일 저장");
        Check(true, "입력이 Markdown 파일에 저장");

        // 접기는 Dock으로 내리지 않고 24px 아이콘으로 바꿉니다. 아이콘은 창 이동을 따라가지 않습니다.
        window.PerformMiniaturize(window);
        Check(!window.IsVisible && !window.IsMiniaturized, "접으면 창을 숨김");
        var icon = manager.Icons[first.Id];
        Check(icon.IsVisible && icon.Frame.Width == 24 && icon.Frame.Height == 24, "24×24 아이콘 표시");
        var iconFrame = icon.Frame;
        window.SetFrameOrigin(new CoreGraphics.CGPoint(window.Frame.X + 40, window.Frame.Y));
        Check(icon.Frame == iconFrame, "아이콘이 창 이동을 따라가지 않음");
        manager.ShowNote(first.Id);
        Check(window.IsVisible && !manager.Icons.ContainsKey(first.Id), "복원하면 아이콘을 닫음");
        Check(await Evaluate<string>(window, "window.hunminEditor.getDraft().markdown") == "# macOS 저장 시험\n", "복원 후 내용 유지");

        // 빨간 단추의 흐름입니다. ‘아니요’면 그대로 두고 ‘예’면 최신 입력을 저장한 뒤 휴지통으로 옮깁니다.
        window.TrashConfirmation = () => false;
        window.PerformClose(window);
        await Task.Delay(300);
        Check(window.IsVisible && workspace.TrashedNotes.Count == 0, "삭제 확인에서 ‘아니요’면 유지");
        state = await workspace.Service.GetStateAsync(first.Id, CancellationToken.None);
        await SetTextAsync(window, "# 휴지통 직전 입력\n", state.Revision + 1, first.Title);
        window.TrashConfirmation = () => true;
        window.PerformClose(window);
        await UntilAsync(() => !manager.Windows.ContainsKey(first.Id), "휴지통 이동");
        Check(workspace.TrashedNotes.Single().Id == first.Id, "삭제 확인에서 ‘예’면 휴지통으로 이동");

        // 메모가 하나도 없으면 새로 만들고 복원합니다.
        await manager.CreateAsync();
        var second = workspace.Notes.Single().Id;
        var secondWindow = await ReadyAsync(manager, second);
        await manager.RestoreFromTrashAsync(first.Id);
        window = await ReadyAsync(manager, first.Id);
        Check(await Evaluate<string>(window, "window.hunminEditor.getDraft().markdown") == "# 휴지통 직전 입력\n", "휴지통에서 복원하면 최신 입력 유지");

        // 표시 설정과 접힌 상태를 다시 켠 뒤 복원합니다.
        await window.Session.ProcessMessageAsync(JsonSerializer.Serialize(new
        {
            protocolVersion = 1, type = "window.appearanceChanged", requestId = "smoke", noteId = first.Id,
            revision = (await workspace.Service.GetStateAsync(first.Id, CancellationToken.None)).Revision,
            payload = new { color = "blue", topmost = true },
        }), "devpostit://app/index.html");
        Check(window.NoteColor == "blue" && window.Level == NSWindowLevel.Floating, "메모 색과 항상 위 적용");
        window.SetFrame(new CoreGraphics.CGRect(window.Frame.X, window.Frame.Y, 500, 500), false);
        var frame = window.Frame;
        manager.Collapse(secondWindow);
        var collapsedFrame = manager.Icons[second].Frame;

        // 종료는 메모를 보존합니다.
        state = await workspace.Service.GetStateAsync(first.Id, CancellationToken.None);
        await SetTextAsync(window, "# 종료 직전 입력\n", state.Revision + 1, first.Title);
        Check(await manager.ExitAsync(), "종료 시 모든 창 닫기");
        Check(manager.Exited && manager.Windows.Count == 0, "창 목록 비움");
        Check(workspace.TrashedNotes is { Count: 0 }, "종료해도 휴지통으로 옮기지 않음");

        var reopenedStore = new AppSettingsStore(Path.Combine(root, ".hunmin", "app-settings.json"));
        var reopened = await NoteWorkspace.OpenAsync(root, CancellationToken.None);
        Check(reopened.Notes.Single(note => note.Id == first.Id).Markdown == "# 종료 직전 입력\n", "종료 직전 입력을 저장");
        var restored = new MacNoteManager(reopened, reopenedStore);
        restored.RestoreSession();
        Check(restored.Windows.ContainsKey(first.Id) && restored.Windows.ContainsKey(second), "열어 둔 메모 복원");
        Check(restored.Icons.ContainsKey(second) && !restored.Icons.ContainsKey(first.Id), "접힌 상태 복원");
        Check(restored.Icons[second].Frame == collapsedFrame, "아이콘 위치 복원");
        var again = restored.Windows[first.Id];
        Check(again.NoteColor == "blue" && again.Level == NSWindowLevel.Floating, "색과 항상 위 복원");
        Check(Math.Abs(again.Frame.X - frame.X) < 1 && Math.Abs(again.Frame.GetMaxY() - frame.GetMaxY()) < 1 && Math.Abs(again.Frame.Height - frame.Height) < 1, "창 위치와 크기 복원");
        await UntilAsync(() => again.Session.IsEditorReady, "복원한 편집기 준비");
        state = await reopened.Service.GetStateAsync(first.Id, CancellationToken.None);
        await SetTextAsync(again, "# ⌘Q 직전 입력\n", state.Revision + 1, first.Title);
        await CheckToolWindowsAsync(root, reopened, restored, reopenedStore);
        await CheckNoteShellAsync(root, reopened, restored, again);
        await CheckPreviewImagesAsync(reopened, again);
        await CheckCaptureAsync(reopened, restored, reopenedStore, again);
        await CheckStatusClicksAsync(reopened, restored);
        finalManager = restored;
    }
}
