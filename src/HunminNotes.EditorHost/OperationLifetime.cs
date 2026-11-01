namespace HunminNotes.EditorHost;

public sealed class OperationLifetime
{
    private readonly object gate = new();
    private int running;
    private bool stopping;
    private TaskCompletionSource? drained;
    public IDisposable? TryBegin()
    {
        lock (gate)
        {
            if (stopping) return null;
            running++;
            return new Lease(this);
        }
    }
    public Task StopAsync()
    {
        lock (gate)
        {
            stopping = true;
            if (running == 0) return Task.CompletedTask;
            return (drained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }
    public void Resume()
    {
        lock (gate)
        {
            if (running != 0) throw new InvalidOperationException("진행 중인 작업이 있습니다.");
            stopping = false; drained = null;
        }
    }
    private sealed class Lease(OperationLifetime owner) : IDisposable
    {
        private OperationLifetime? current = owner;
        public void Dispose()
        {
            var value = Interlocked.Exchange(ref current, null);
            if (value is null) return;
            lock (value.gate) { if (--value.running == 0) value.drained?.TrySetResult(); }
        }
    }
}
