using System.Text.Json;

namespace LogServer;

/// <summary>
/// 게임이 실제로 남기는 이벤트 규약과 습격 스테이지 등 게임 의존 설정을 기동 시 파일에서 로딩한다.
/// game-events.json / raid-stages.json이 없으면 명확한 에러로 기동을 실패시킨다.
/// </summary>
public static class GameConfig
{
    public const string ConfigDirectoryName = "GameConfig";
    public const string GameEventsFileName = "game-events.json";
    public const string RaidStagesFileName = "raid-stages.json";

    public static Dictionary<string, string> Categories { get; private set; } = new(StringComparer.Ordinal);
    public static Dictionary<string, string> Events { get; private set; } = new(StringComparer.Ordinal);
    public static List<string> RaidEnds { get; private set; } = new();
    public static List<RaidStage> Stages { get; private set; } = new();
    private static bool _loaded;

    public static void Load(string serverRoot)
    {
        string dir = Path.Combine(serverRoot, ConfigDirectoryName);
        string eventsPath = Path.Combine(dir, GameEventsFileName);
        string stagesPath = Path.Combine(dir, RaidStagesFileName);

        if (!File.Exists(eventsPath))
            throw new FileNotFoundException($"게임 이벤트 규약 설정이 없습니다: {eventsPath}");
        if (!File.Exists(stagesPath))
            throw new FileNotFoundException($"습격 스테이지 설정이 없습니다: {stagesPath}");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(eventsPath));
        Categories = ParseCategories(doc.RootElement);
        Events = ParseEvents(doc.RootElement);
        RaidEnds = ParseStringArray(doc.RootElement, "RaidEnds");

        Stages = ParseStages(File.ReadAllText(stagesPath));

        _loaded = true;
    }

    public static string Category(string key) => Categories[key];
    public static string Event(string key) => Events[key];

    public static bool IsCategory(string category, string key) =>
        Categories.TryGetValue(key, out string? name) && string.Equals(category, name, StringComparison.Ordinal);

    private static Dictionary<string, string> ParseCategories(JsonElement root)
    {
        if (!root.TryGetProperty("categories", out JsonElement cats) || cats.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("game-events.json: categories 설정이 없습니다.");
        return cats.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ParseEvents(JsonElement root)
    {
        if (!root.TryGetProperty("events", out JsonElement evs) || evs.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("game-events.json: events 설정이 없습니다.");
        return evs.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    private static List<string> ParseStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"game-events.json: {name} 배열이 없습니다.");
        return arr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
    }

    private static List<RaidStage> ParseStages(string text)
    {
        List<RaidStage> list = new();
        using JsonDocument doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("raid-stages.json: 배열이 아닙니다.");
        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            list.Add(new RaidStage(
                item.GetProperty("Scene").GetString() ?? string.Empty,
                item.GetProperty("Name").GetString() ?? string.Empty,
                item.GetProperty("Vault").GetDouble()));
        }
        return list;
    }
}

public readonly record struct RaidStage(string Scene, string Name, double Vault);
