using DevPostIt.Storage;

namespace DevPostIt.Mac;

[Register("AppDelegate")]
public sealed class AppDelegate : NSApplicationDelegate
{
    private NoteWorkspace? workspace;
    private MacNoteManager? manager;
    private StatusBarMenu? statusBar;
    private string? lastSyncError;
    private MacNotifications? notifications;
    private MacCaptureHotkey? hotkey;

    public override async void DidFinishLaunching(NSNotification notification)
    {
        // 아이콘이 두 개 생기지 않도록 먼저 시작한 인스턴스만 남깁니다. 동시에 시작해도 하나는 남습니다.
        var self = NSRunningApplication.CurrentApplication.ProcessIdentifier;
        if (NSBundle.MainBundle.BundleIdentifier is { } id && NSRunningApplication.GetRunningApplications(id).Any(other => other.ProcessIdentifier < self))
        {
            NSApplication.SharedApplication.Terminate(this);
            return;
        }
        // ⌘Q와 시스템 종료에서 저장을 끝낼 수 있게 갑작스러운 종료를 끕니다.
        NSProcessInfo.ProcessInfo.DisableSuddenTermination();
        NSProcessInfo.ProcessInfo.DisableAutomaticTermination("메모 저장");
        NSApplication.SharedApplication.MainMenu = CreateMainMenu();
        try
        {
            var configured = Environment.GetEnvironmentVariable("DEVPOSTIT_DATA_DIR");
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // 시험용 데이터 폴더를 지정하면 앱 상태도 그 안에 두어 사용자 설정과 섞이지 않게 합니다.
            var store = new AppSettingsStore(configured is null ? Path.Combine(local, "DevPostIt", "app-settings.json") : Path.Combine(configured, ".hunmin", "app-settings.json"));
            var root = configured ?? await ResolveDataRootAsync(store, local);
            var previousData = await Task.Run(() => NoteBackup.ApplyPending(root));
            if (previousData is not null) MacDialogs.Ask("백업에서 복원했습니다.", $"복원 전 데이터는 아래 폴더에 남아 있습니다.\n{previousData}", "확인");
            workspace = await Task.Run(() => NoteWorkspace.OpenAsync(root, CancellationToken.None));
            manager = new MacNoteManager(workspace, store, configured is null);
            if (previousData is not null) manager.PendingPreferences = NoteWorkspace.ReadPreferences(root);
            statusBar = new StatusBarMenu(manager, workspace);
            workspace.ExternalSyncFailed += message => BeginInvokeOnMainThread(() =>
            {
                if (message == lastSyncError) return;
                lastSyncError = message;
                MacDialogs.Ask("앱 밖에서 바뀐 파일을 읽지 못했습니다.", message, "확인");
            });
            workspace.StartWatching();
            manager.RestoreSession();
            notifications = new MacNotifications(kind => { if (kind == "todo") manager?.ShowTodo(); else if (kind == "event") manager?.ShowCalendar(); });
            manager.ReminderDue += reminder => notifications.Show(reminder.Kind, reminder.Title);
            manager.StartReminders();
            manager.StartHolidayUpdates();
            hotkey = new MacCaptureHotkey(() => { if (manager is { } current) _ = MacNoteManager.RunAsync(current.StartCaptureAsync); });
        }
        catch (Exception error)
        {
            MacDialogs.Ask("메모장을 시작하지 못했습니다.", $"이미 실행 중이면 메뉴 막대 아이콘을 확인하세요.\n{error.Message}", "확인");
            if (workspace is not null) await workspace.DisposeAsync();
            workspace = null;
            manager = null;
            NSApplication.SharedApplication.Terminate(this);
        }
    }

    private static async Task<string> ResolveDataRootAsync(AppSettingsStore store, string local)
    {
        var fallback = NoteDataLocation.DefaultRoot(local);
        var settings = store.Current;
        if (settings.DataRoot is null) return fallback;
        if (settings.PendingMigrationFrom is not { } previous) return settings.DataRoot;
        var target = settings.DataRoot;
        try
        {
            await Task.Run(() => NoteDataLocation.MigrateLegacy(previous, target));
            settings.PendingMigrationFrom = null;
            store.Save();
            return target;
        }
        catch (Exception error)
        {
            MacDialogs.Ask("데이터 폴더로 메모를 복사하지 못해 이전 폴더를 계속 사용합니다.", $"{target}\n{error.Message}", "확인");
            settings.DataRoot = string.Equals(previous, fallback, StringComparison.Ordinal) ? null : previous;
            settings.PendingMigrationFrom = null;
            store.Save();
            return previous;
        }
    }

    // 메뉴가 없으면 WKWebView에서 ⌘C/V/X/A/Z가 동작하지 않습니다.
    private NSMenu CreateMainMenu()
    {
        var main = new NSMenu();
        var application = new NSMenu("DevPostIt");
        application.AddItem(new NSMenuItem("정보", "", (_, _) => manager?.ShowAbout()));
        application.AddItem(new NSMenuItem("설정…", ",", (_, _) => manager?.ShowSettings()));
        application.AddItem(NSMenuItem.SeparatorItem);
        application.AddItem(new NSMenuItem("DevPostIt 종료", new ObjCRuntime.Selector("terminate:"), "q"));
        Add(main, application);

        var file = new NSMenu("파일");
        file.AddItem(new NSMenuItem("새 메모", "n", (_, _) => { if (manager is { } current) _ = MacNoteManager.RunAsync(current.CreateAsync); }));
        file.AddItem(new NSMenuItem("메모 검색", "f", (_, _) => manager?.ShowSearch()) { KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask });
        file.AddItem(new NSMenuItem("닫기", new ObjCRuntime.Selector("performClose:"), "w"));
        Add(main, file);

        var edit = new NSMenu("편집");
        edit.AddItem(new NSMenuItem("실행 취소", new ObjCRuntime.Selector("undo:"), "z"));
        var redo = new NSMenuItem("다시 실행", new ObjCRuntime.Selector("redo:"), "z") { KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask };
        edit.AddItem(redo);
        edit.AddItem(NSMenuItem.SeparatorItem);
        edit.AddItem(new NSMenuItem("잘라내기", new ObjCRuntime.Selector("cut:"), "x"));
        edit.AddItem(new NSMenuItem("복사", new ObjCRuntime.Selector("copy:"), "c"));
        edit.AddItem(new NSMenuItem("붙여넣기", new ObjCRuntime.Selector("paste:"), "v"));
        edit.AddItem(new NSMenuItem("전체 선택", new ObjCRuntime.Selector("selectAll:"), "a"));
        Add(main, edit);

        var window = new NSMenu("윈도우");
        window.AddItem(new NSMenuItem("접기", new ObjCRuntime.Selector("performMiniaturize:"), "m"));
        Add(main, window);
        NSApplication.SharedApplication.WindowsMenu = window;
        var help = new NSMenu("도움말");
        help.AddItem(new NSMenuItem("도움말", "", (_, _) => manager?.ShowHelp()));
        Add(main, help);
        NSApplication.SharedApplication.HelpMenu = help;
        return main;
    }

    private static void Add(NSMenu main, NSMenu submenu) => main.AddItem(new NSMenuItem(submenu.Title) { Submenu = submenu });

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => false;

    public override void WillTerminate(NSNotification notification) => hotkey?.Dispose();

    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => MacTermination.Begin(sender, manager);
}
