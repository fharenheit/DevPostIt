using System.Windows;
using DevPostIt.Storage;

namespace DevPostIt.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private NoteWorkspace? workspace;
    private TrayService? tray;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 업데이트 전에 새 exe가 이 PC에서 실행되는지 확인합니다. 창과 데이터는 건드리지 않습니다.
        if (e.Args.Contains("--update-self-test")) { Shutdown(0); return; }
        try
        {
            var configured = Environment.GetEnvironmentVariable("DEVPOSTIT_DATA_DIR");
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // 시험용 데이터 폴더를 지정하면 앱 상태도 그 안에 두어 사용자 설정과 섞이지 않게 합니다.
            var store = new AppSettingsStore(configured is null ? System.IO.Path.Combine(local, "DevPostIt", "app-settings.json") : System.IO.Path.Combine(configured, ".hunmin", "app-settings.json"));
            var root = configured ?? await ResolveDataRootAsync(store, local);
            var previousData = await Task.Run(() => NoteBackup.ApplyPending(root));
            if (previousData is not null) System.Windows.MessageBox.Show($"백업에서 복원했습니다.\n복원 전 데이터는 아래 폴더에 남아 있습니다.\n{previousData}", "백업에서 복원", MessageBoxButton.OK, MessageBoxImage.Information);
            workspace = await Task.Run(() => NoteWorkspace.OpenAsync(root, CancellationToken.None));
            var manager = new DesktopNoteManager(workspace, null, null, store, configured is null);
            tray = new TrayService(manager, workspace, Dispatcher);
            string? lastSyncError = null;
            workspace.ExternalSyncFailed += message =>
            {
                if (message == lastSyncError) return;
                lastSyncError = message;
                Dispatcher.BeginInvoke(new Action(() => tray?.Notify($"앱 밖에서 바뀐 파일을 읽지 못했습니다.\n{message}")));
            };
            workspace.StartWatching();
            manager.RestoreSession();
            manager.StartUpdateChecks();
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show($"메모장을 시작하지 못했습니다. 이미 실행 중이면 트레이 아이콘을 확인하세요.\n{error.Message}", "메모", MessageBoxButton.OK, MessageBoxImage.Error);
            if (workspace is not null) await workspace.DisposeAsync();
            Shutdown(1);
        }
    }
    private static async Task<string> ResolveDataRootAsync(AppSettingsStore store, string local)
    {
        var fallback = NoteDataLocation.DefaultRoot(local);
        var settings = store.Current;
        if (settings.DataRoot is null)
        {
            var legacy = NoteDataLocation.LegacyRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            await Task.Run(() => NoteDataLocation.MigrateLegacy(legacy, fallback));
            return fallback;
        }
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
            System.Windows.MessageBox.Show($"데이터 폴더로 메모를 복사하지 못해 이전 폴더를 계속 사용합니다.\n{target}\n{error.Message}", "데이터 폴더 변경", MessageBoxButton.OK, MessageBoxImage.Warning);
            settings.DataRoot = string.Equals(previous, fallback, StringComparison.OrdinalIgnoreCase) ? null : previous;
            settings.PendingMigrationFrom = null;
            store.Save();
            return previous;
        }
    }
    protected override void OnExit(ExitEventArgs e) { tray?.Dispose(); base.OnExit(e); }
}
