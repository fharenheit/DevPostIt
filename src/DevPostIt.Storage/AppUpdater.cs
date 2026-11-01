using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DevPostIt.Storage;

public sealed record AppUpdate(Version Version, string AssetUrl, string Sha256, long Size);

// GitHub 릴리즈에서 새 버전을 찾아 설치합니다. 내려받은 파일은 해시와 시험 실행을 통과해야 교체합니다.
public sealed class AppUpdater(HttpClient http, string executable, Func<string, Task<bool>>? selfTest = null)
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/fharenheit/DevPostIt/releases/latest";
    private const long MaxAssetBytes = 512L * 1024 * 1024;
    private string UpdatePath => Path.Combine(Path.GetDirectoryName(executable)!, Path.GetFileNameWithoutExtension(executable) + ".update.exe");
    private string OldPath => Path.Combine(Path.GetDirectoryName(executable)!, Path.GetFileNameWithoutExtension(executable) + ".old.exe");

    public static AppUpdate? ParseLatest(string json, Version current)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
            if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'), out var version) || Normalize(version) <= Normalize(current)) return null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("DevPostIt-", StringComparison.Ordinal) || !name.EndsWith("-win-x64.exe", StringComparison.Ordinal)) continue;
                var url = asset.GetProperty("browser_download_url").GetString();
                var digest = asset.TryGetProperty("digest", out var item) ? item.GetString() : null;
                var size = asset.GetProperty("size").GetInt64();
                if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71 || size is <= 0 or > MaxAssetBytes) return null;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com") return null;
                return new(version, url!, digest["sha256:".Length..].ToUpperInvariant(), size);
            }
            return null;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    public async Task<AppUpdate?> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DevPostIt", Normalize(current).ToString()));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return ParseLatest(await response.Content.ReadAsStringAsync(cancellationToken), current);
    }

    public async Task DownloadAsync(AppUpdate update, CancellationToken cancellationToken)
    {
        var path = UpdatePath;
        if (File.Exists(path)) File.Delete(path);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.AssetUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DevPostIt", Normalize(update.Version).ToString()));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await input.CopyToAsync(output, cancellationToken);
            }
            string actual;
            await using (var written = File.OpenRead(path))
            {
                if (written.Length != update.Size) throw new InvalidDataException("내려받은 파일의 크기가 맞지 않습니다.");
                actual = Convert.ToHexString(await SHA256.HashDataAsync(written, cancellationToken));
            }
            if (actual != update.Sha256) throw new InvalidDataException("내려받은 파일의 해시가 맞지 않습니다.");
            if (!await (selfTest ?? RunSelfTestAsync)(path))
                throw new InvalidOperationException("새 버전을 실행할 수 없습니다. Windows의 앱 제어 정책이 차단했을 수 있습니다.");
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    private static async Task<bool> RunSelfTestAsync(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "--update-self-test") { UseShellExecute = false });
            if (process is null) return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException) { return false; }
    }

    // 실행 중인 exe는 지울 수 없지만 이름은 바꿀 수 있습니다. 실패하면 원래 파일로 되돌립니다.
    public void Install()
    {
        var update = UpdatePath;
        if (!File.Exists(update)) throw new FileNotFoundException("내려받은 업데이트가 없습니다.", update);
        var old = OldPath;
        if (File.Exists(old)) File.Delete(old);
        File.Move(executable, old);
        try { File.Move(update, executable); }
        catch
        {
            File.Move(old, executable);
            throw;
        }
    }

    public void CleanUp()
    {
        foreach (var path in new[] { OldPath, UpdatePath })
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* 이전 프로세스가 끝나기 전이면 다음 시작 때 지웁니다. */ }
    }
}
