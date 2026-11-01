using System.Diagnostics;
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
using Forms = System.Windows.Forms;
using FileDialogs = Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace DevPostIt.App;

// 메모와 별개인 설정 창입니다. 닫아도 메모에 영향을 주지 않으며 삭제 확인을 거치지 않습니다.
public sealed class SettingsWindow : Window
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly DesktopNoteManager manager;
    private readonly string dataFolder;
    private readonly AppSettingsStore? store;
    private readonly bool canChangeFolder;
    internal WebView2 View { get; } = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 253, 245) };
    internal Func<string?> BackupPathPicker { get; set; }
    internal Func<string?> RestorePathPicker { get; set; }
    internal Action<string> FolderOpener { get; set; } = path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    public SettingsWindow(DesktopNoteManager manager, string dataFolder, AppSettingsStore? store = null, bool canChangeFolder = false)
    {
        this.manager = manager;
        BackupPathPicker = PickBackupPath;
        RestorePathPicker = PickRestorePath;
        this.dataFolder = dataFolder; this.store = store; this.canChangeFolder = canChangeFolder && store is not null;
        Title = "설정";
        Icon = NoteIcon.WindowImage;
        Width = 420; Height = 720; MinWidth = 320; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(255, 253, 245));
        Content = View;
        SourceInitialized += (_, _) => MainWindow.ApplyTitleBarColors(new WindowInteropHelper(this).Handle);
        Loaded += async (_, _) =>
        {
            try
            {
                var core = await LocalWebView.ConfigureAsync(View, EditorOrigin.IsSettingsPage);
                core.WebMessageReceived += (_, args) => Receive(core, args.WebMessageAsJson, args.Source);
                core.NavigationCompleted += (_, _) => SendState(core, null);
                core.Navigate(EditorOrigin.SettingsPage);
            }
            catch (Exception error) { Content = new TextBlock { Text = $"설정을 열지 못했습니다.\n{error.Message}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) }; }
        };
        Closed += (_, _) => View.Dispose();
    }

    private void Receive(CoreWebView2 core, string json, string source)
    {
        if (SettingsMessage.IsOpenDataFolder(json, source))
        {
            try { FolderOpener(dataFolder); }
            catch (Exception error) { MessageBox.Show(this, $"데이터 폴더를 열지 못했습니다.\n{dataFolder}\n{error.Message}", "설정", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        else if (SettingsMessage.TryReadStartInTray(json, source, out var startInTray) && store is not null)
        {
            store.Current.StartInTray = startInTray;
            store.Save();
        }
        else if (SettingsMessage.TryReadAutoUpdate(json, source, out var autoUpdate) && store is not null)
        {
            store.Current.AutoUpdate = autoUpdate;
            store.Save();
        }
        else if (SettingsMessage.IsChangeDataFolder(json, source) && canChangeFolder) ChangeDataFolder(core);
        else if (SettingsMessage.IsBackup(json, source)) _ = BackupAsync(core);
        else if (SettingsMessage.IsRestore(json, source)) _ = RestoreAsync(core);
        else if (SettingsMessage.IsCheckUpdate(json, source)) _ = CheckUpdateAsync(core);
    }

    private string? PickBackupPath()
    {
        var dialog = new FileDialogs.SaveFileDialog { Title = "백업 파일 저장", Filter = "DevPostIt 백업 (*.zip)|*.zip", FileName = $"DevPostIt-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
    private string? PickRestorePath()
    {
        var dialog = new FileDialogs.OpenFileDialog { Title = "복원할 백업 파일 선택", Filter = "DevPostIt 백업 (*.zip)|*.zip" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
    private async Task BackupAsync(CoreWebView2 core)
    {
        if (BackupPathPicker() is not { } path) return;
        SendStatus(core, "백업 중입니다…");
        try
        {
            var summary = await manager.BackupAsync(path);
            SendStatus(core, $"메모 {summary.Notes}개를 백업했습니다: {path}");
        }
        catch (Exception error) { SendStatus(core, $"백업하지 못했습니다: {error.Message}"); }
    }
    private async Task RestoreAsync(CoreWebView2 core)
    {
        if (RestorePathPicker() is not { } path) return;
        var answer = MessageBox.Show(this, $"백업으로 모든 메모를 바꾸고 앱을 다시 시작합니다.\n{path}\n\n현재 데이터는 지우지 않고 데이터 폴더 옆의 ‘before-restore’ 폴더로 옮깁니다. 앱 설정과 창 위치는 그대로입니다.\n\n계속할까요?", "백업에서 복원", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        SendStatus(core, "백업을 확인하는 중입니다…");
        try { await manager.RestoreBackupAsync(path); }
        catch (Exception error) { SendStatus(core, $"복원하지 못했습니다. 현재 메모는 그대로입니다: {error.Message}"); }
    }
    private async Task CheckUpdateAsync(CoreWebView2 core)
    {
        SendStatus(core, "업데이트를 확인하는 중입니다…");
        SendStatus(core, await manager.CheckForUpdateAsync());
    }
    private void SendStatus(CoreWebView2 core, string message)
    {
        if (IsLoaded) core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "settings.status", message }, Json));
    }

    private void ChangeDataFolder(CoreWebView2 core)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "메모와 검색 DB를 저장할 폴더를 선택하세요.", UseDescriptionForTitle = true, InitialDirectory = dataFolder };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        var chosen = System.IO.Path.GetFullPath(dialog.SelectedPath);
        if (NoteDataLocation.Overlaps(chosen, dataFolder))
        {
            MessageBox.Show(this, "현재 데이터 폴더나 그 안팎의 폴더는 선택할 수 없습니다. 다른 폴더를 선택하세요.", "설정", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var answer = MessageBox.Show(this, $"다음 실행부터 아래 폴더를 사용합니다.\n{chosen}\n\n폴더가 비어 있으면 현재 메모를 복사합니다. 이미 내용이 있으면 복사하지 않고 그 내용을 사용합니다. 현재 폴더의 원본은 지우지 않습니다.\n\n계속할까요?", "데이터 폴더 변경", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        store!.Current.DataRoot = chosen;
        store.Current.PendingMigrationFrom = dataFolder;
        store.Save();
        SendState(core, chosen);
    }

    private void SendState(CoreWebView2 core, string? pending)
    {
        var next = pending ?? (store?.Current.PendingMigrationFrom is not null ? store.Current.DataRoot : null);
        core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "settings.state", dataFolder, pendingFolder = next, startInTray = store?.Current.StartInTray ?? false, canChangeFolder, autoUpdate = store?.Current.AutoUpdate ?? true, canUpdate = manager.CanUpdate, version = DesktopNoteManager.CurrentVersion.ToString(3) }, Json));
    }
}
