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
using MessageBox = System.Windows.MessageBox;

namespace DevPostIt.App;

// 메모와 별개인 설정 창입니다. 닫아도 메모에 영향을 주지 않으며 삭제 확인을 거치지 않습니다.
public sealed class SettingsWindow : Window
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string dataFolder;
    private readonly AppSettingsStore? store;
    private readonly bool canChangeFolder;
    internal WebView2 View { get; } = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 253, 245) };
    internal Action<string> FolderOpener { get; set; } = path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    public SettingsWindow(string dataFolder, AppSettingsStore? store = null, bool canChangeFolder = false)
    {
        this.dataFolder = dataFolder; this.store = store; this.canChangeFolder = canChangeFolder && store is not null;
        Title = "설정";
        Icon = NoteIcon.WindowImage;
        Width = 400; Height = 440; MinWidth = 320; MinHeight = 240;
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
        else if (SettingsMessage.IsChangeDataFolder(json, source) && canChangeFolder) ChangeDataFolder(core);
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
        core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "settings.state", dataFolder, pendingFolder = next, startInTray = store?.Current.StartInTray ?? false, canChangeFolder }, Json));
    }
}
