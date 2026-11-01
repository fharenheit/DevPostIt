namespace DevPostIt.Storage;

public sealed class DataDirectoryLease : IDisposable
{
    private readonly FileStream lease;
    public DataDirectoryLease(string root)
    {
        var path = Path.Combine(Path.GetFullPath(root), ".hunmin", "session.lock");
        StoragePathGuard.RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { lease = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new IOException("같은 데이터 폴더를 다른 DevPostIt이 사용 중입니다. 기존 창을 닫은 후 다시 실행하세요.", error); }
    }
    public void Dispose() => lease.Dispose();
}
