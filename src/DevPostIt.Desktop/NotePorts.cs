namespace DevPostIt.Desktop;

public enum RecoveryChoice { Restore, Discard, KeepAndClose }

// 운영체제별로 구현하는 메모 창의 기능입니다. NoteSession은 이 뒤의 WPF나 AppKit을 알지 못합니다.
public interface IWebSurface
{
    Task PrepareAsync();
    void Start(Func<string, string, Task> onMessage);
    void PostMessage(string json);
    // 식의 결과를 JSON 문자열로 돌려줍니다.
    Task<string> EvaluateAsync(string expression);
    Task<bool> PrintToPdfAsync(string path);
}

public interface INoteWindow
{
    string NoteColor { get; }
    bool Topmost { get; }
    void ApplyAppearance(string color, bool topmost);
    void SetNoteTitle(string title);
    void HideLoading();
    void ShowStartupError(string message);
    void SetInputEnabled(bool enabled);
    void BringToFront();
    // 네이티브 닫기를 요청합니다. NoteSession.AllowClose가 거짓이면 HandleCloseRequestAsync로 돌아옵니다.
    void Close();
    void Post(Action action);
}

public interface INoteDialogs
{
    bool ConfirmTrash();
    RecoveryChoice AskRecovery();
    // uncaptured가 거짓일 때 참을 돌려주면 복구 기록을 보존한 채 닫습니다.
    bool ReportCloseFailure(bool uncaptured, string error);
    Task ReportTrashFailureAsync(string error);
    bool ConfirmLink(string url);
}

public interface INoteShell
{
    void OpenLink(string url);
    void OpenFolder(string path);
    string? FindDiagramApp();
    void OpenDiagram(string application, string path);
    void ReportDiagramAppMissing();
    byte[]? ReadClipboardPng();
    string? PickPdfPath(string title);
}
