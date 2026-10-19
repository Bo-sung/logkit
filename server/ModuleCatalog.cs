using System.Text.Json;

namespace LogServer;

/// <summary>
/// 총기 코드 모듈 카탈로그(module-catalog.json)를 기동 시 로딩한다.
/// _moduleNames/_modulePrices/CorePrice는 여기에 이식되어 하드코딩을 대체한다.
/// </summary>
public static class ModuleCatalog
{
    public static Dictionary<string, string> ModuleNames { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, (int Buy, int Credits, bool Shotgun)> ModulePrices { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public static int CorePrice { get; private set; } = 50;

    public static void Load(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("CorePrice", out JsonElement cp) && cp.ValueKind == JsonValueKind.Number)
            CorePrice = cp.GetInt32();

        ModuleNames = ParseStringMap(root, "moduleNames");
        ModulePrices = ParsePriceMap(root);
    }

    private static Dictionary<string, string> ParseStringMap(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out JsonElement map) || map.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return map.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, (int Buy, int Credits, bool Shotgun)> ParsePriceMap(JsonElement root)
    {
        Dictionary<string, (int Buy, int Credits, bool Shotgun)> map = new(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("modulePrices", out JsonElement prices) || prices.ValueKind != JsonValueKind.Object)
            return map;
        foreach (JsonProperty p in prices.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object) continue;
            int Buy = p.Value.TryGetProperty("Buy", out JsonElement b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : 30;
            int Credits = p.Value.TryGetProperty("Credits", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            bool Shotgun = p.Value.TryGetProperty("Shotgun", out JsonElement s) && s.ValueKind == JsonValueKind.True;
            map[p.Name] = (Buy, Credits, Shotgun);
        }
        return map;
    }
}
