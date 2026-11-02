using System.Diagnostics;
using Xunit;

namespace DevPostIt.Storage.Tests;

// 파일을 바꾸거나 지우지 못하는 상태를 만듭니다. Windows는 열어 두면 되지만 Unix는 열린 파일도 바꿀 수 있어 잠금 표시를 씁니다.
internal sealed class HeldFile : IDisposable
{
    private readonly string path;
    private readonly FileStream? stream;
    public HeldFile(string path)
    {
        this.path = path;
        if (OperatingSystem.IsWindows()) stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        else SetImmutable("uchg");
    }
    public void Dispose()
    {
        if (stream is not null) stream.Dispose();
        else SetImmutable("nouchg");
    }
    public static async Task AssertFailsAsync(Func<Task> action)
    {
        var error = await Record.ExceptionAsync(action);
        Assert.True(error is IOException or UnauthorizedAccessException, $"파일 오류가 아닙니다: {error}");
    }
    private void SetImmutable(string flag)
    {
        using var process = Process.Start(new ProcessStartInfo("chflags") { ArgumentList = { flag, path }, UseShellExecute = false })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
