using System.Text.Json;

namespace LogServer;

/// <summary>
/// 손님 프로필 ID → 에셋 이름 카탈로그(customer-catalog.json)를 기동 시 로딩한다.
/// CustomerNames의 _names는 여기에 이식된다.
/// </summary>
public static class CustomerCatalog
{
    public static Dictionary<string, string> Names { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.TryGetProperty("names", out JsonElement names) && names.ValueKind == JsonValueKind.Object)
        {
            Names = names.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }
    }
}
