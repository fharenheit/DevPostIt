namespace DevPostIt.Mac;

[Register("AppDelegate")]
public sealed class AppDelegate : NSApplicationDelegate
{
    private StatusBarMenu? statusBar;

    public override void DidFinishLaunching(NSNotification notification)
    {
        // 아이콘이 두 개 생기지 않도록 먼저 시작한 인스턴스만 남깁니다. 동시에 시작해도 하나는 남습니다.
        var self = NSRunningApplication.CurrentApplication.ProcessIdentifier;
        if (NSBundle.MainBundle.BundleIdentifier is { } id && NSRunningApplication.GetRunningApplications(id).Any(other => other.ProcessIdentifier < self))
        {
            NSApplication.SharedApplication.Terminate(this);
            return;
        }
        statusBar = new StatusBarMenu();
    }
}
