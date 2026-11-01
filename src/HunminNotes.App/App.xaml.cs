using System.Windows;
using HunminNotes.Storage;

namespace HunminNotes.App;

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
        try
        {
            var root = Environment.GetEnvironmentVariable("DEVPOSTIT_DATA_DIR");
            if (root is null)
            {
                root = NoteDataLocation.DefaultRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                var legacy = NoteDataLocation.LegacyRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                await Task.Run(() => NoteDataLocation.MigrateLegacy(legacy, root));
            }
            workspace = await Task.Run(() => NoteWorkspace.OpenAsync(root, CancellationToken.None));
            var manager = new DesktopNoteManager(workspace);
            tray = new TrayService(manager, workspace, Dispatcher);
            manager.ShowNote(workspace.Notes[0].Id);
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show($"메모장을 시작하지 못했습니다. 이미 실행 중이면 트레이 아이콘을 확인하세요.\n{error.Message}", "메모", MessageBoxButton.OK, MessageBoxImage.Error);
            if (workspace is not null) await workspace.DisposeAsync();
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { tray?.Dispose(); base.OnExit(e); }
}
