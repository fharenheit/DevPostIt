using System.Text.Json;
using DevPostIt.EditorHost;
using DevPostIt.Storage;

namespace DevPostIt.Mac;

// 설정 창입니다. 닫아도 메모에 영향을 주지 않으며 삭제 확인을 거치지 않습니다.
internal sealed class MacSettingsWindow : MacWebWindow
{
    private readonly MacNoteManager manager;
    private readonly string dataFolder;
    private readonly AppSettingsStore store;
    private readonly bool canChangeFolder;
    internal Func<string?> BackupPathPicker { get; set; } = () => MacDialogs.PickSavePath("백업 파일 저장", $"DevPostIt-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip", "zip");
    internal Func<string?> RestorePathPicker { get; set; } = () => MacDialogs.PickOpenPaths("복원할 백업 파일 선택", false, "zip").FirstOrDefault();
    internal Func<int, long, bool> ImageCleanupConfirmation { get; set; } = (count, size) =>
        MacDialogs.Ask($"어느 메모도 쓰지 않는 이미지 {count}개({size / 1024.0 / 1024.0:0.#}MB)를 휴지통으로 옮깁니다.", "본문에서 방금 지운 이미지를 실행 취소로 되살릴 생각이라면 ‘아니요’를 누르세요.", "아니요", "예") == 1;
    internal Action<string> FolderOpener { get; set; } = path => NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.CreateFileUrl(path, true, null));

    public MacSettingsWindow(MacNoteManager manager, string dataFolder, AppSettingsStore store, bool canChangeFolder)
        : base("설정", EditorOrigin.SettingsPage, EditorOrigin.IsSettingsPage, 420, 760, 320, 240)
    {
        this.manager = manager; this.dataFolder = dataFolder; this.store = store; this.canChangeFolder = canChangeFolder;
    }

    protected override void OnPageLoaded()
    {
        SendState(null);
        SendFonts();
    }

    protected override Task ReceiveAsync(string json, string source)
    {
        if (SettingsMessage.IsOpenDataFolder(json, source))
        {
            try { FolderOpener(dataFolder); }
            catch (Exception error) when (error is IOException or UriFormatException or ArgumentException) { MacDialogs.Ask("데이터 폴더를 열지 못했습니다.", $"{dataFolder}\n{error.Message}", "확인"); }
        }
        else if (SettingsMessage.TryReadStartInTray(json, source, out var startInTray)) Save(settings => settings.StartInTray = startInTray);
        else if (SettingsMessage.TryReadAutoUpdate(json, source, out var autoUpdate)) Save(settings => settings.AutoUpdate = autoUpdate);
        else if (SettingsMessage.TryReadCaptureDelay(json, source, out var captureDelay)) Save(settings => settings.CaptureDelaySeconds = captureDelay);
        else if (SettingsMessage.TryReadHolidayUpdates(json, source, out var holidayUpdates)) Save(settings => settings.HolidayUpdates = holidayUpdates);
        else if (SettingsMessage.TryReadCaptureAnnotate(json, source, out var captureAnnotate)) Save(settings => settings.AnnotateCapture = captureAnnotate);
        else if (SettingsMessage.IsChangeDataFolder(json, source) && canChangeFolder) ChangeDataFolder();
        else if (SettingsMessage.IsCleanImages(json, source)) return CleanImagesAsync();
        else if (SettingsMessage.IsBackup(json, source)) return BackupAsync();
        else if (SettingsMessage.IsRestore(json, source)) return RestoreAsync();
        else if (SettingsMessage.IsCheckUpdate(json, source)) SendStatus("macOS 버전은 아직 자동 업데이트를 지원하지 않습니다. 새 버전은 GitHub 릴리즈에서 받습니다.");
        return Task.CompletedTask;
    }

    private void Save(Action<AppSettings> change)
    {
        change(store.Current);
        store.Save();
    }

    private async Task BackupAsync()
    {
        if (BackupPathPicker() is not { } path) return;
        SendStatus("백업 중입니다…");
        try
        {
            var saved = await EvaluateAsync("localStorage.getItem('devpostit.editor.preferences.v1')");
            var summary = await manager.BackupAsync(path, JsonSerializer.Deserialize<string?>(saved));
            SendStatus($"메모 {summary.Notes}개를 백업했습니다: {path}");
        }
        catch (Exception error) { SendStatus($"백업하지 못했습니다: {error.Message}"); }
    }

    private async Task RestoreAsync()
    {
        if (RestorePathPicker() is not { } path) return;
        if (MacDialogs.Ask("백업으로 모든 메모를 바꾸고 앱을 다시 시작합니다.", $"{path}\n\n현재 데이터는 지우지 않고 데이터 폴더 옆의 ‘before-restore’ 폴더로 옮깁니다. 앱 설정과 창 위치는 그대로입니다.\n\n계속할까요?", "아니요", "예") != 1) return;
        SendStatus("백업을 확인하는 중입니다…");
        try { await manager.RestoreBackupAsync(path); }
        catch (Exception error) { SendStatus($"복원하지 못했습니다. 현재 메모는 그대로입니다: {error.Message}"); }
    }

    private async Task CleanImagesAsync()
    {
        SendStatus("쓰지 않는 이미지를 찾는 중입니다…");
        try
        {
            var unused = await manager.FindUnusedImagesAsync();
            if (unused.Count == 0) { SendStatus("쓰지 않는 이미지가 없습니다."); return; }
            if (!ImageCleanupConfirmation(unused.Count, unused.Sum(image => image.Size))) { SendStatus("이미지를 정리하지 않았습니다."); return; }
            SendStatus($"이미지 {await manager.DeleteUnusedImagesAsync(unused)}개를 휴지통으로 옮겼습니다.");
        }
        catch (Exception error) { SendStatus($"이미지를 정리하지 못했습니다: {error.Message}"); }
    }

    // AppKit의 글꼴 목록은 주 스레드에서만 읽습니다.
    private void SendFonts()
    {
        var (monospace, all) = MacFonts.Read();
        Post(new { type = "settings.fonts", monospace, all });
    }

    private void SendStatus(string message) => Post(new { type = "settings.status", message });

    private void ChangeDataFolder()
    {
        if (MacDialogs.PickFolder("메모와 검색 DB를 저장할 폴더를 선택하세요.", dataFolder) is not { } selected) return;
        var chosen = Path.GetFullPath(selected);
        if (NoteDataLocation.Overlaps(chosen, dataFolder))
        {
            MacDialogs.Ask("현재 데이터 폴더나 그 안팎의 폴더는 선택할 수 없습니다.", "다른 폴더를 선택하세요.", "확인");
            return;
        }
        if (MacDialogs.Ask("다음 실행부터 아래 폴더를 사용합니다.", $"{chosen}\n\n폴더가 비어 있으면 현재 메모를 복사합니다. 이미 내용이 있으면 복사하지 않고 그 내용을 사용합니다. 현재 폴더의 원본은 지우지 않습니다.\n\n계속할까요?", "아니요", "예") != 1) return;
        store.Current.DataRoot = chosen;
        store.Current.PendingMigrationFrom = dataFolder;
        store.Save();
        SendState(chosen);
    }

    private void SendState(string? pending)
    {
        var next = pending ?? (store.Current.PendingMigrationFrom is not null ? store.Current.DataRoot : null);
        Post(new
        {
            type = "settings.state", dataFolder, pendingFolder = next, startInTray = store.Current.StartInTray, canChangeFolder,
            autoUpdate = store.Current.AutoUpdate, captureDelay = store.Current.CaptureDelaySeconds, captureAnnotate = store.Current.AnnotateCapture,
            holidayUpdates = store.Current.HolidayUpdates, canUpdate = false, version = MacNoteManager.CurrentVersion,
        });
    }
}

internal sealed class MacMindMapWindow(MindMapStore store) : MacWebWindow("마인드맵", EditorOrigin.MindMapPage, EditorOrigin.IsMindMapPage, 1100, 720, 520, 360)
{
    protected override bool HasPending => true;
    internal Func<string, bool> LinkConfirmation { get; set; } = url => MacDialogs.Ask("이 링크를 기본 브라우저로 열까요?", url, "아니요", "예") == 1;
    internal Action<string> LinkOpener { get; set; } = url => NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(url));
    internal Func<string, string?> PngPathPicker { get; set; } = name =>
    {
        var safe = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        return MacDialogs.PickSavePath("PNG로 저장", (safe.Length == 0 ? "마인드맵" : safe) + ".png", "png");
    };

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
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            Post(new { type = "mindmap.error", message = error.Message });
        }
    }

    private async Task ExportPngAsync(string name, string data)
    {
        var png = Convert.FromBase64String(data);
        if (png is not [0x89, 0x50, 0x4E, 0x47, ..]) throw new InvalidDataException("PNG 그림이 아닙니다.");
        if (PngPathPicker(name) is not { } path) return;
        await File.WriteAllBytesAsync(path, png);
        Post(new { type = "mindmap.exported" });
    }

    protected override Task SavePendingAsync(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 300 });
        return store.SaveAsync(document.RootElement.Clone(), CancellationToken.None);
    }
}

// 할 일과 달력 창입니다. 같은 저장소를 쓰며, 한쪽의 변경을 앱이 양쪽에 알립니다.
internal sealed class MacPlannerWindow(string title, string page, Func<string, bool> isAllowed, PlannerStore store, Action<PlannerState> changed, double width, double height, Func<HolidayFile?>? holidays = null)
    : MacWebWindow(title, page, isAllowed, width, height, 520, 360)
{
    protected override bool HasPending => true;
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
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Post(new { type = "planner.error", message = error.Message });
            SendState(await store.LoadAsync(CancellationToken.None));
        }
    }
}
