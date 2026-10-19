using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogServer;

/// <summary>
/// 습격 맵 윗면 이미지 저장소. data/maps/{scene}.{png|jpg|webp}와 그 이미지가 덮는 월드 좌표 범위 data/maps/{scene}.json을 둔다.
/// 대시보드는 이 범위로 이미지를 히트맵·리플레이 좌표(X 가로, +Z 위)에 맞춰 배경으로 깐다.
/// </summary>
public sealed class MapStore
{
    public const long MaxImageBytes = 20L * 1024 * 1024;
    private static readonly Regex _scenePattern = new("^[A-Za-z0-9_\\-]{1,80}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["webp"] = "image/webp",
    };

    private readonly ServerOptions _options;
    private readonly object _gate = new();

    public MapStore(ServerOptions options)
    {
        _options = options;
    }

    public string MapsDirectory => Path.Combine(_options.DataPath, "maps");

    public static bool IsValidScene(string? scene) => scene != null && _scenePattern.IsMatch(scene);

    /// <summary>등록된 맵 목록(씬 이름순). 이미지나 범위 파일이 없는 항목은 뺀다.</summary>
    public List<MapInfo> List()
    {
        List<MapInfo> maps = new();
        if (!Directory.Exists(MapsDirectory))
        {
            return maps;
        }

        foreach (string file in Directory.GetFiles(MapsDirectory, "*.json"))
        {
            string scene = Path.GetFileNameWithoutExtension(file);
            if (IsValidScene(scene) && Read(scene) is { } info)
            {
                maps.Add(info);
            }
        }

        return maps.OrderBy(m => m.Scene, StringComparer.Ordinal).ToList();
    }

    /// <summary>씬 하나의 맵 정보. 범위 파일이나 이미지가 없으면 null.</summary>
    public MapInfo? Read(string scene)
    {
        string meta = Path.Combine(MapsDirectory, scene + ".json");
        if (!File.Exists(meta) || ImagePath(scene) is not { } image)
        {
            return null;
        }

        try
        {
            MapInfo? info = JsonSerializer.Deserialize<MapInfo>(File.ReadAllText(meta), _json);
            if (info == null)
            {
                return null;
            }

            info.Scene = scene;
            info.Bytes = new FileInfo(image).Length;
            info.Type = _types[Path.GetExtension(image).TrimStart('.')];
            return info;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>씬 이미지 파일 경로(확장자 png·jpg·webp 중 있는 것). 없으면 null.</summary>
    public string? ImagePath(string scene) =>
        _types.Keys.Select(ext => Path.Combine(MapsDirectory, scene + "." + ext)).FirstOrDefault(File.Exists);

    /// <summary>
    /// 이미지와 좌표 범위를 저장한다(같은 씬은 교체). 이미지 형식은 파일 앞부분(시그니처)으로 판별한다.
    /// 형식이 틀리거나 범위가 잘못되면 오류 문구를, 성공하면 null을 반환한다.
    /// </summary>
    public string? Save(string scene, byte[] image, double minX, double maxX, double minZ, double maxZ, string source)
    {
        string? ext = Sniff(image);
        if (ext == null)
        {
            return "PNG·JPEG·WebP 이미지만 올릴 수 있습니다";
        }

        if (!(double.IsFinite(minX) && double.IsFinite(maxX) && double.IsFinite(minZ) && double.IsFinite(maxZ)) || maxX <= minX || maxZ <= minZ)
        {
            return "좌표 범위가 잘못됐습니다(minX < maxX, minZ < maxZ)";
        }

        lock (_gate)
        {
            Directory.CreateDirectory(MapsDirectory);
            foreach (string old in _types.Keys.Select(e => Path.Combine(MapsDirectory, scene + "." + e)).Where(File.Exists))
            {
                File.Delete(old);
            }

            File.WriteAllBytes(Path.Combine(MapsDirectory, scene + "." + ext), image);
            MapInfo info = new()
            {
                Scene = scene,
                MinX = minX,
                MaxX = maxX,
                MinZ = minZ,
                MaxZ = maxZ,
                UpdatedUtc = DateTime.UtcNow,
                Source = source,
            };
            File.WriteAllText(Path.Combine(MapsDirectory, scene + ".json"), JsonSerializer.Serialize(info, _json));
        }

        return null;
    }

    /// <summary>씬의 이미지와 범위 파일을 지운다. 지운 것이 없으면 false.</summary>
    public bool Delete(string scene)
    {
        lock (_gate)
        {
            bool any = false;
            foreach (string path in _types.Keys.Select(e => Path.Combine(MapsDirectory, scene + "." + e)).Append(Path.Combine(MapsDirectory, scene + ".json")))
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    any = true;
                }
            }

            return any;
        }
    }

    private static string? Sniff(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            return "png";
        }

        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
        {
            return "jpg";
        }

        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
        {
            return "webp";
        }

        return null;
    }

    /// <summary>폼 값 하나를 숫자로 읽는다(소수점은 점). 없거나 틀리면 NaN.</summary>
    public static double ParseNumber(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : double.NaN;
}

/// <summary>맵 이미지 하나. MinX~MaxX, MinZ~MaxZ = 이미지 왼쪽~오른쪽, 아래~위가 덮는 월드 좌표.</summary>
public sealed class MapInfo
{
    public string Scene { get; set; } = string.Empty;
    public double MinX { get; set; }
    public double MaxX { get; set; }
    public double MinZ { get; set; }
    public double MaxZ { get; set; }
    public DateTime UpdatedUtc { get; set; }

    /// <summary>등록 방법(예: upload, unity-capture).</summary>
    public string Source { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Type { get; set; } = string.Empty;
}
