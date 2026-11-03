using System.Text.Json;
using DevPostIt.Mac;
using DevPostIt.Storage;
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
        finalManager = restored;
    }
}
