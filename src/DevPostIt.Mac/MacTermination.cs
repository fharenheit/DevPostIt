namespace DevPostIt.Mac;

// 트레이 종료와 같이 메모를 보존합니다. 초안을 저장한 뒤에 응답합니다.
internal static class MacTermination
{
    public static NSApplicationTerminateReply Begin(NSApplication application, MacNoteManager? manager)
    {
        if (manager is not { Exited: false } current) return NSApplicationTerminateReply.Now;
        Finish(application, current);
        return NSApplicationTerminateReply.Later;
    }

    private static async void Finish(NSApplication application, MacNoteManager manager)
    {
        var finished = false;
        try { finished = await manager.ExitAsync(); }
        catch (Exception error) { MacDialogs.Ask("메모", error.Message, "확인"); }
        application.ReplyToApplicationShouldTerminate(finished);
    }
}
