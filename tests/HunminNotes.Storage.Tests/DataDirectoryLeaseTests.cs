namespace HunminNotes.Storage.Tests;

public sealed class DataDirectoryLeaseTests
{
    [Fact]
    public void OnlyOneWriterCanOwnDataFolderAndDisposalReleasesOwnership()
    {
        var root = Path.Combine(Path.GetTempPath(), "DevPostItLeaseTests", Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = new DataDirectoryLease(root))
                Assert.ThrowsAny<IOException>(() => new DataDirectoryLease(root));
            using var second = new DataDirectoryLease(root);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
