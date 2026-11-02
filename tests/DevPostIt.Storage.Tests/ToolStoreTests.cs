using System.Text.Json;
using DevPostIt.Storage;
using Xunit;

namespace DevPostIt.Storage.Tests;

public sealed class ToolStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "devpostit-tools-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static object Todo(string? id = null, string title = "할 일", string listId = "tasks", string? reminder = null, string? completedAt = null) => new { id = id ?? Guid.NewGuid().ToString(), title, notes = "", listId, important = false, myDay = (string?)null, due = (string?)null, reminder, repeat = "none", completedAt, steps = Array.Empty<object>(), created = "2026-05-01T09:00", order = 0.0 };
    private static object Event(string title = "일정", string start = "2026-05-13T10:00", string end = "2026-05-13T11:00", bool allDay = false, string repeat = "none", int? reminderMinutes = null, string[]? exceptions = null) => new { id = Guid.NewGuid().ToString(), title, start, end, allDay, location = "", notes = "", color = "blue", repeat, repeatUntil = (string?)null, reminderMinutes, exceptions = exceptions ?? [] };

    [Fact]
    public async Task PlannerItemsSurviveReopeningAndReplaceById()
    {
        var store = new PlannerStore(root);
        var id = Guid.NewGuid().ToString();
        await store.PutAsync("todos", Json(Todo(id, "처음")), default);
        await store.PutAsync("todos", Json(Todo(id, "바뀜")), default);
        await store.PutAsync("events", Json(Event()), default);
        var reopened = await new PlannerStore(root).LoadAsync(default);
        Assert.Equal("바뀜", Assert.Single(reopened.Todos).Title);
        Assert.Single(reopened.Events);
    }

    [Fact]
    public async Task ToolFilesKeepKoreanReadable()
    {
        await new PlannerStore(root).PutAsync("todos", Json(Todo(title: "읽을 수 있는 할 일")), default);
        Assert.Contains("읽을 수 있는 할 일", await File.ReadAllTextAsync(Path.Combine(root, "planner.json")));
        var map = await new MindMapStore(root).SaveAsync(Json(Map(title: "읽을 수 있는 맵")), default);
        Assert.Contains("읽을 수 있는 맵", await File.ReadAllTextAsync(Path.Combine(root, $"{Guid.Parse(map.Id):N}.json")));
    }

    [Fact]
    public async Task DeletingAListDeletesItsTodosOnly()
    {
        var store = new PlannerStore(root);
        var list = Guid.NewGuid().ToString();
        await store.PutAsync("lists", Json(new { id = list, name = "회사", order = 1.0 }), default);
        await store.PutAsync("todos", Json(Todo(title: "회사 일", listId: list)), default);
        await store.PutAsync("todos", Json(Todo(title: "다른 일")), default);
        var state = await store.DeleteAsync("lists", list, default);
        Assert.Empty(state.Lists);
        Assert.Equal("다른 일", Assert.Single(state.Todos).Title);
    }

    public static TheoryData<string, object> InvalidItems => new()
    {
        { "todos", new { id = "경로/../x", title = "a" } },
        { "todos", Todo(title: "   ") },
        { "todos", Todo(title: new string('a', 501)) },
        { "todos", Todo(listId: Guid.NewGuid().ToString()) },
        { "todos", Todo(reminder: "내일") },
        { "todos", new { id = Guid.NewGuid().ToString(), title = "a", created = "2026-05-01T09:00", path = "C:\\Windows" } },
        { "events", Event(start: "2026-05-13T10:00", end: "2026-05-13T09:00") },
        { "events", Event(start: "오늘", end: "내일") },
        { "events", Event(start: "2026-05-13", end: "2026-05-12", allDay: true) },
        { "events", Event(repeat: "hourly") },
        { "events", Event(reminderMinutes: 7) },
        { "events", Event(exceptions: ["어제"]) },
        { "unknown", Todo() },
    };
    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task PlannerRejectsInvalidItemsAndKeepsState(string collection, object item)
    {
        var store = new PlannerStore(root);
        await store.PutAsync("todos", Json(Todo(title: "기존")), default);
        await Assert.ThrowsAsync<PlannerDataException>(() => store.PutAsync(collection, Json(item), default));
        Assert.Equal("기존", Assert.Single((await new PlannerStore(root).LoadAsync(default)).Todos).Title);
    }

    [Fact]
    public async Task CorruptPlannerFileIsKeptAside()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "planner.json"), "{ 망가짐");
        var state = await new PlannerStore(root).LoadAsync(default);
        Assert.Empty(state.Todos);
        Assert.Single(Directory.GetFiles(root, "planner.json.corrupt-*"));
    }

    [Theory]
    [InlineData("none", "2026-05-13", true)]
    [InlineData("none", "2026-05-14", false)]
    [InlineData("daily", "2026-05-12", false)]
    [InlineData("daily", "2026-06-01", true)]
    [InlineData("weekdays", "2026-05-16", false)]
    [InlineData("weekdays", "2026-05-15", true)]
    [InlineData("weekly", "2026-05-20", true)]
    [InlineData("weekly", "2026-05-21", false)]
    [InlineData("monthly", "2026-06-13", true)]
    [InlineData("yearly", "2027-05-13", true)]
    [InlineData("yearly", "2027-06-13", false)]
    public void OccurrenceRules(string repeat, string day, bool expected)
    {
        var item = new CalendarEvent { Start = "2026-05-13T10:00", End = "2026-05-13T11:00", Repeat = repeat };
        Assert.Equal(expected, PlannerStore.OccursOn(item, DateOnly.Parse(day)));
    }

    [Fact]
    public void RemindersFireOnceInTheirWindow()
    {
        var state = new PlannerState
        {
            Todos = [new TodoItem { Id = "a", Title = "할 일", Reminder = "2026-05-13T10:00" }, new TodoItem { Id = "b", Title = "끝남", Reminder = "2026-05-13T10:00", CompletedAt = "2026-05-13T09:00" }],
            Events = [
                new CalendarEvent { Id = "c", Title = "회의", Start = "2026-05-13T10:30", End = "2026-05-13T11:00", ReminderMinutes = 30 },
                new CalendarEvent { Id = "d", Title = "알림 없음", Start = "2026-05-13T10:00", End = "2026-05-13T11:00" },
                new CalendarEvent { Id = "e", Title = "매일", Start = "2026-05-01T10:00", End = "2026-05-01T11:00", Repeat = "daily", ReminderMinutes = 0, Exceptions = ["2026-05-12"] },
                new CalendarEvent { Id = "f", Title = "종일", Start = "2026-05-13", End = "2026-05-13", AllDay = true, ReminderMinutes = 60 },
            ],
        };
        var due = PlannerStore.FindReminders(state, new DateTime(2026, 5, 13, 9, 59, 30), new DateTime(2026, 5, 13, 10, 0, 0));
        Assert.Equal(["a", "c", "e"], due.Select(item => item.Id).OrderBy(id => id));
        Assert.Empty(PlannerStore.FindReminders(state, new DateTime(2026, 5, 13, 10, 0, 0), new DateTime(2026, 5, 13, 10, 0, 30)));
        Assert.Equal("f", Assert.Single(PlannerStore.FindReminders(state, new DateTime(2026, 5, 13, 7, 59, 0), new DateTime(2026, 5, 13, 8, 0, 0))).Id);
        Assert.Empty(PlannerStore.FindReminders(state, new DateTime(2026, 5, 12, 9, 59, 0), new DateTime(2026, 5, 12, 10, 0, 0)));
    }

    [Fact]
    public async Task TakeRemindersRemembersLastCheckAndLimitsMissedOnesToADay()
    {
        var store = new PlannerStore(root);
        await store.PutAsync("todos", Json(Todo(title: "어제", reminder: "2026-05-12T12:00")), default);
        await store.PutAsync("todos", Json(Todo(title: "그제 전", reminder: "2026-05-01T12:00")), default);
        Assert.Empty(await store.TakeRemindersAsync(new DateTime(2026, 5, 1, 13, 0, 0), default));
        var due = await store.TakeRemindersAsync(new DateTime(2026, 5, 13, 9, 0, 0), default);
        Assert.Equal("어제", Assert.Single(due).Title);
        Assert.Empty(await store.TakeRemindersAsync(new DateTime(2026, 5, 13, 9, 0, 30), default));
    }

    private static object Node(string id = "n", string text = "주제", object[]? children = null, string? color = null, string? icon = null, string? side = null, string? link = null) => new { id, text, children = children ?? [], collapsed = false, color, bold = false, note = "", link, icon, side };
    private static object Map(string? id = null, string title = "맵", string layout = "both", object? root = null) => new { id = id ?? Guid.NewGuid().ToString(), title, layout, updated = "", root = root ?? Node() };

    [Fact]
    public async Task MindMapsAreSavedListedOpenedAndDeleted()
    {
        var store = new MindMapStore(root);
        var id = Guid.NewGuid().ToString();
        var saved = await store.SaveAsync(Json(Map(id, "첫 맵", root: Node(children: [Node("c1", "하위")]))), default);
        Assert.NotEmpty(saved.Updated);
        await store.SaveAsync(Json(Map(title: "둘째 맵")), default);
        var list = await store.ListAsync(default);
        Assert.Equal(2, list.Count);
        Assert.Equal(2, list.Single(map => map.Id == id).Nodes);
        Assert.Equal("하위", (await store.OpenAsync(id, default)).Root.Children[0].Text);
        await store.DeleteAsync(id, default);
        Assert.Single(await store.ListAsync(default));
        await Assert.ThrowsAsync<PlannerDataException>(() => store.OpenAsync(id, default));
    }

    public static TheoryData<object> InvalidMaps => new()
    {
        Map(id: "../../x"),
        Map(title: " "),
        Map(layout: "up"),
        Map(root: Node(text: new string('a', 4001))),
        Map(root: Node(color: "red")),
        Map(root: Node(icon: "bomb")),
        Map(root: Node(side: "top")),
        Map(root: Node(link: new string('a', 2049))),
        Map(root: Node("same", children: [Node("same")])),
        new { id = Guid.NewGuid().ToString(), title = "맵", layout = "both", updated = "", root = Node(), path = "C:\\" },
    };
    [Theory]
    [MemberData(nameof(InvalidMaps))]
    public async Task MindMapRejectsInvalidDocuments(object map) => await Assert.ThrowsAsync<PlannerDataException>(() => new MindMapStore(root).SaveAsync(Json(map), default));

    [Theory]
    [InlineData("../../Windows/x")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("")]
    public async Task MindMapIdsCannotBePaths(string id)
    {
        var store = new MindMapStore(root);
        await Assert.ThrowsAsync<PlannerDataException>(() => store.OpenAsync(id, default));
        await Assert.ThrowsAsync<PlannerDataException>(() => store.DeleteAsync(id, default));
    }

    [Fact]
    public async Task DamagedMindMapFileIsLeftOutOfTheListButKept()
    {
        var store = new MindMapStore(root);
        await store.SaveAsync(Json(Map(title: "정상")), default);
        var broken = Path.Combine(root, new string('a', 32) + ".json");
        await File.WriteAllTextAsync(broken, "{ 망가짐");
        Assert.Single(await store.ListAsync(default));
        Assert.True(File.Exists(broken));
    }

    [Fact]
    public async Task BackupIncludesToolDataAndRestoresIt()
    {
        var data = Path.Combine(root, "data");
        await using (var workspace = await NoteWorkspace.OpenAsync(data, default)) { }
        var tools = Path.Combine(data, ".hunmin", "tools");
        await new PlannerStore(tools).PutAsync("todos", Json(Todo(title: "백업할 일")), default);
        var mapId = Guid.NewGuid().ToString();
        await new MindMapStore(Path.Combine(tools, "mindmaps")).SaveAsync(Json(Map(mapId, "백업할 맵")), default);
        var backup = Path.Combine(root, "backup.zip");
        await NoteBackup.CreateAsync(data, backup, "0.0.0", default);
        await NoteBackup.PrepareRestoreAsync(backup, data, default);
        NoteBackup.ApplyPending(data);
        Assert.Equal("백업할 일", Assert.Single((await new PlannerStore(tools).LoadAsync(default)).Todos).Title);
        Assert.Equal("백업할 맵", (await new MindMapStore(Path.Combine(tools, "mindmaps")).OpenAsync(mapId, default)).Title);
    }
}
