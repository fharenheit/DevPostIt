using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevPostIt.Storage;

public sealed class TodoStep
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}
public sealed class TodoItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public string ListId { get; set; } = PlannerStore.DefaultList;
    public bool Important { get; set; }
    public string? MyDay { get; set; }
    public string? Due { get; set; }
    public string? Reminder { get; set; }
    public string Repeat { get; set; } = "none";
    public string? CompletedAt { get; set; }
    public List<TodoStep> Steps { get; set; } = [];
    public string Created { get; set; } = "";
    public double Order { get; set; }
}
public sealed class TodoList
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Order { get; set; }
}
public sealed class CalendarEvent
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public bool AllDay { get; set; }
    public string Location { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Color { get; set; } = "blue";
    public string Repeat { get; set; } = "none";
    public string? RepeatUntil { get; set; }
    public int? ReminderMinutes { get; set; }
    public List<string> Exceptions { get; set; } = [];
}
public sealed class PlannerState
{
    public List<TodoList> Lists { get; set; } = [];
    public List<TodoItem> Todos { get; set; } = [];
    public List<CalendarEvent> Events { get; set; } = [];
    public string? LastReminderCheck { get; set; }
}
public sealed record PlannerReminder(string Kind, string Id, string Title, string When);
public sealed class PlannerDataException(string message) : IOException(message);

// 할 일과 달력의 원본입니다. 창이 여러 개여도 앱이 항목 단위로 고쳐 서로 덮어쓰지 않습니다.
public sealed class PlannerStore(string folder)
{
    public const string DefaultList = "tasks";
    private const int MaxItems = 50_000, MaxTitle = 500, MaxNotes = 20_000, MaxSteps = 200, MaxExceptions = 2_000;
    private static readonly string[] Repeats = ["none", "daily", "weekdays", "weekly", "monthly", "yearly"];
    private static readonly string[] Colors = ["blue", "green", "red", "orange", "purple", "gray"];
    private static readonly int[] ReminderChoices = [0, 5, 10, 15, 30, 60, 120, 1440];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly AtomicFileWriter writer = new();
    private PlannerState? state;
    private string FilePath => Path.Combine(folder, "planner.json");

    public async Task<PlannerState> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await EnsureAsync(cancellationToken); }
        finally { gate.Release(); }
    }
    public async Task<PlannerState> PutAsync(string collection, JsonElement item, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await EnsureAsync(cancellationToken);
            switch (collection)
            {
                case "todos": Replace(current.Todos, ValidateTodo(Read<TodoItem>(item), current), value => value.Id); break;
                case "lists": Replace(current.Lists, ValidateList(Read<TodoList>(item)), value => value.Id); break;
                case "events": Replace(current.Events, ValidateEvent(Read<CalendarEvent>(item)), value => value.Id); break;
                default: throw new PlannerDataException("알 수 없는 항목 종류입니다.");
            }
            await SaveAsync(current, cancellationToken);
            return current;
        }
        finally { gate.Release(); }
    }
    public async Task<PlannerState> DeleteAsync(string collection, string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await EnsureAsync(cancellationToken);
            switch (collection)
            {
                case "todos": current.Todos.RemoveAll(value => value.Id == id); break;
                case "events": current.Events.RemoveAll(value => value.Id == id); break;
                case "lists":
                    current.Lists.RemoveAll(value => value.Id == id);
                    current.Todos.RemoveAll(value => value.ListId == id);
                    break;
                default: throw new PlannerDataException("알 수 없는 항목 종류입니다.");
            }
            await SaveAsync(current, cancellationToken);
            return current;
        }
        finally { gate.Release(); }
    }
    // 앱이 꺼져 있는 동안 지난 알림은 최근 24시간 것만 알립니다. 항목별 표시 없이 마지막 확인 시각으로 중복을 막습니다.
    public async Task<IReadOnlyList<PlannerReminder>> TakeRemindersAsync(DateTime now, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await EnsureAsync(cancellationToken);
            var floor = now.AddHours(-24);
            var from = TryDateTime(current.LastReminderCheck, out var last) && last > floor ? last : current.LastReminderCheck is null ? now : floor;
            if (from > now) from = now;
            var result = FindReminders(current, from, now);
            current.LastReminderCheck = now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            await SaveAsync(current, cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }
    public static IReadOnlyList<PlannerReminder> FindReminders(PlannerState state, DateTime from, DateTime to)
    {
        var result = new List<PlannerReminder>();
        foreach (var todo in state.Todos)
            if (todo.CompletedAt is null && TryDateTime(todo.Reminder, out var when) && when > from && when <= to)
                result.Add(new("todo", todo.Id, todo.Title, todo.Reminder!));
        foreach (var item in state.Events)
        {
            if (item.ReminderMinutes is not { } minutes) continue;
            for (var day = DateOnly.FromDateTime(from.AddMinutes(minutes)); day <= DateOnly.FromDateTime(to.AddMinutes(minutes)); day = day.AddDays(1))
            {
                if (!OccursOn(item, day) || !StartTime(item, day, out var start)) continue;
                var when = start.AddMinutes(-minutes);
                if (when > from && when <= to) result.Add(new("event", item.Id, item.Title, start.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)));
            }
        }
        return result;
    }
    // 종일 일정의 알림은 그날 오전 9시를 기준으로 합니다.
    private static bool StartTime(CalendarEvent item, DateOnly day, out DateTime start)
    {
        start = default;
        if (item.AllDay) { start = day.ToDateTime(new TimeOnly(9, 0)); return true; }
        if (!TryDateTime(item.Start, out var first)) return false;
        start = day.ToDateTime(TimeOnly.FromDateTime(first));
        return true;
    }
    public static bool OccursOn(CalendarEvent item, DateOnly day)
    {
        if (!DateOnly.TryParseExact(item.Start[..Math.Min(10, item.Start.Length)], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)) return false;
        if (day < first || item.Exceptions.Contains(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) return false;
        if (DateOnly.TryParseExact(item.RepeatUntil, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until) && day > until) return false;
        return item.Repeat switch
        {
            "daily" => true,
            "weekdays" => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
            "weekly" => day.DayOfWeek == first.DayOfWeek,
            "monthly" => day.Day == first.Day,
            "yearly" => day.Day == first.Day && day.Month == first.Month,
            _ => day == first,
        };
    }

    private async Task<PlannerState> EnsureAsync(CancellationToken cancellationToken)
    {
        if (state is not null) return state;
        if (!File.Exists(FilePath)) return state = new();
        var text = await File.ReadAllTextAsync(FilePath, cancellationToken);
        try { return state = JsonSerializer.Deserialize<PlannerState>(text, Options) ?? new(); }
        catch (JsonException)
        {
            // 읽지 못한 파일은 지우지 않고 옆에 남깁니다.
            File.Move(FilePath, $"{FilePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
            return state = new();
        }
    }
    private async Task SaveAsync(PlannerState current, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        await writer.WriteAsync(FilePath, JsonSerializer.Serialize(current, Options), cancellationToken);
    }
    private static T Read<T>(JsonElement item)
    {
        try { return item.Deserialize<T>(Options) ?? throw new PlannerDataException("항목이 비어 있습니다."); }
        catch (JsonException) { throw new PlannerDataException("항목 형식이 잘못되었습니다."); }
    }
    private static void Replace<T>(List<T> items, T value, Func<T, string> id)
    {
        var at = items.FindIndex(item => id(item) == id(value));
        if (at >= 0) items[at] = value;
        else if (items.Count >= MaxItems) throw new PlannerDataException("항목이 너무 많습니다.");
        else items.Add(value);
    }
    private static TodoItem ValidateTodo(TodoItem item, PlannerState current)
    {
        RequireId(item.Id);
        Text(item.Title, MaxTitle, true);
        Text(item.Notes, MaxNotes, false);
        if (item.ListId != DefaultList && current.Lists.All(list => list.Id != item.ListId)) throw new PlannerDataException("목록을 찾지 못했습니다.");
        OptionalDate(item.MyDay);
        OptionalDate(item.Due);
        OptionalDateTime(item.Reminder);
        if (item.CompletedAt is not null && !TryDateTime(item.CompletedAt, out _)) throw new PlannerDataException("완료 시각이 잘못되었습니다.");
        if (!Repeats.Contains(item.Repeat)) throw new PlannerDataException("반복 설정이 잘못되었습니다.");
        if (item.Steps.Count > MaxSteps) throw new PlannerDataException("단계가 너무 많습니다.");
        foreach (var step in item.Steps) { RequireId(step.Id); Text(step.Title, MaxTitle, true); }
        if (!TryDateTime(item.Created, out _)) throw new PlannerDataException("만든 시각이 잘못되었습니다.");
        if (!double.IsFinite(item.Order)) throw new PlannerDataException("순서가 잘못되었습니다.");
        return item;
    }
    private static TodoList ValidateList(TodoList item)
    {
        RequireId(item.Id);
        Text(item.Name, 200, true);
        if (!double.IsFinite(item.Order)) throw new PlannerDataException("순서가 잘못되었습니다.");
        return item;
    }
    private static CalendarEvent ValidateEvent(CalendarEvent item)
    {
        RequireId(item.Id);
        Text(item.Title, MaxTitle, true);
        Text(item.Location, MaxTitle, false);
        Text(item.Notes, MaxNotes, false);
        if (item.AllDay)
        {
            if (!TryDate(item.Start, out var start) || !TryDate(item.End, out var end) || end < start) throw new PlannerDataException("종일 일정의 날짜가 잘못되었습니다.");
        }
        else if (!TryDateTime(item.Start, out var start) || !TryDateTime(item.End, out var end) || end < start) throw new PlannerDataException("일정의 시각이 잘못되었습니다.");
        if (!Colors.Contains(item.Color)) throw new PlannerDataException("일정 색상이 잘못되었습니다.");
        if (!Repeats.Contains(item.Repeat)) throw new PlannerDataException("반복 설정이 잘못되었습니다.");
        OptionalDate(item.RepeatUntil);
        if (item.ReminderMinutes is { } minutes && !ReminderChoices.Contains(minutes)) throw new PlannerDataException("알림 설정이 잘못되었습니다.");
        if (item.Exceptions.Count > MaxExceptions) throw new PlannerDataException("제외 날짜가 너무 많습니다.");
        foreach (var day in item.Exceptions) if (!TryDate(day, out _)) throw new PlannerDataException("제외 날짜가 잘못되었습니다.");
        return item;
    }
    private static void RequireId(string id) { if (!Guid.TryParse(id, out var parsed) || parsed == Guid.Empty) throw new PlannerDataException("항목 ID가 잘못되었습니다."); }
    private static void Text(string value, int max, bool required)
    {
        if (value.Length > max || (required && value.Trim().Length == 0)) throw new PlannerDataException("글자 수가 맞지 않습니다.");
    }
    private static void OptionalDate(string? value) { if (value is not null && !TryDate(value, out _)) throw new PlannerDataException("날짜가 잘못되었습니다."); }
    private static void OptionalDateTime(string? value) { if (value is not null && !TryDateTime(value, out _)) throw new PlannerDataException("시각이 잘못되었습니다."); }
    private static bool TryDate(string? value, out DateOnly date) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    private static bool TryDateTime(string? value, out DateTime date) => DateTime.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
