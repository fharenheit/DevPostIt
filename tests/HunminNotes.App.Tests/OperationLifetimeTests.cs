using HunminNotes.EditorHost;

namespace HunminNotes.App.Tests;
public sealed class OperationLifetimeTests
{
    [Fact]
    public async Task StopWaitsForExistingOperationAndRejectsNewWorkUntilResumed()
    {
        var lifetime = new OperationLifetime();
        var running = lifetime.TryBegin();
        Assert.NotNull(running);
        var stopping = lifetime.StopAsync();
        Assert.False(stopping.IsCompleted);
        Assert.Null(lifetime.TryBegin());
        running.Dispose();
        await stopping.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(lifetime.TryBegin());
        lifetime.Resume();
        using var resumed = lifetime.TryBegin();
        Assert.NotNull(resumed);
    }
}
