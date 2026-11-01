using System.Collections.Concurrent;
using System.Text;

namespace HunminNotes.Storage;

public sealed class AtomicFileWriter
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.OrdinalIgnoreCase);
    public Task WriteAsync(string path, string content, CancellationToken cancellationToken) => WriteCoreAsync(path, content, false, cancellationToken);
    public Task WriteNewAsync(string path, string content, CancellationToken cancellationToken) => WriteCoreAsync(path, content, true, cancellationToken);

    private async Task WriteCoreAsync(string path, string content, bool createOnly, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        var gate = locks.GetOrAdd(full, _ => new(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            StoragePathGuard.RejectLinks(full);
            var bytes = new UTF8Encoding(false, true).GetBytes(content);
            var directory = Path.GetDirectoryName(full)!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            StoragePathGuard.RejectLinks(full);
            if (createOnly || !File.Exists(full)) File.Move(temporary, full, overwrite: false);
            else File.Replace(temporary, full, null);
        }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            finally { gate.Release(); }
        }
    }
}
