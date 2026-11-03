using UserNotifications;

namespace DevPostIt.Mac;

// 할 일과 일정 알림입니다. 누르면 해당 창을 엽니다. 알림을 쓸 수 없으면 대화 상자로 대신해 알림을 잃지 않습니다.
internal sealed class MacNotifications : UNUserNotificationCenterDelegate
{
    private const string KindKey = "kind";
    private const int MaxTitleLength = 200;
    private readonly Action<string> open;
    private readonly UNUserNotificationCenter center = UNUserNotificationCenter.Current;

    public MacNotifications(Action<string> open)
    {
        this.open = open;
        center.Delegate = this;
    }

    public void Show(string kind, string title)
    {
        var heading = kind == "todo" ? "할 일 알림" : "일정 알림";
        var body = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
        center.RequestAuthorization(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound, (granted, _) =>
        {
            if (!granted) { Fallback(heading, body); return; }
            var content = new UNMutableNotificationContent { Title = heading, Body = body, UserInfo = NSDictionary.FromObjectAndKey(new NSString(kind), new NSString(KindKey)) };
            center.AddNotificationRequest(UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, null), error => { if (error is not null) Fallback(heading, body); });
        });
    }

    private void Fallback(string heading, string body) => BeginInvokeOnMainThread(() => MacDialogs.Ask(heading, body, "확인"));

    // 앱이 앞에 있어도 표시합니다.
    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler) =>
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);

    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
    {
        var kind = response.Notification.Request.Content.UserInfo?[new NSString(KindKey)]?.ToString();
        BeginInvokeOnMainThread(() => { if (kind is not null) open(kind); completionHandler(); });
    }
}
