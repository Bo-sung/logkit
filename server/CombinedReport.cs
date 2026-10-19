using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogServer;

/// <summary>
/// 원본 로그 미포함 통합 스냅샷 리포트. /report/full 이 4개 페이지를 탭 iframe 으로 묶는다.
/// 스냅샷 data 는 API 응답과 동일한 JsonSerializerDefaults.Web 옵션으로 직렬화한다.
/// </summary>
public static class CombinedReport
{
    public static readonly string[] Series = { "#4f8cff", "#34d399", "#fbbf24", "#f87171", "#a78bfa", "#22d3ee" };

    public static JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static Dictionary<string, string> VersionColors(object data)
    {
        var map = new Dictionary<string, string>();
        try
        {
            var versions = data.GetType().GetProperty("Versions")?.GetValue(data);
            if (versions is System.Collections.IEnumerable enumerable)
            {
                int i = 0;
                foreach (var item in enumerable)
                {
                    var name = item?.GetType().GetProperty("Version")?.GetValue(item) as string;
                    if (name != null)
                        map[name] = Series[i % Series.Length];
                    i++;
                }
            }
        }
        catch { }
        return map;
    }

    public static List<string> DefaultSelected(object data)
    {
        var versions = data.GetType().GetProperty("Versions")?.GetValue(data);
        var list = new List<string>();
        if (versions is System.Collections.IEnumerable enumerable)
            foreach (var item in enumerable)
            {
                var name = item?.GetType().GetProperty("Version")?.GetValue(item) as string;
                if (name != null) list.Add(name);
            }
        return list;
    }

    public static string EmbedPage(string pageHtml, string page, object data, string month, string savedAt)
    {
        var colorOf = new Dictionary<string, string>();
        int i = 0;
        var versions = data.GetType().GetProperty("Versions")?.GetValue(data);
        if (versions is System.Collections.IEnumerable enumerable)
            foreach (var item in enumerable)
            {
                var name = item?.GetType().GetProperty("Version")?.GetValue(item) as string;
                if (name != null) colorOf[name] = Series[i % Series.Length];
                i++;
            }

        string withNav = pageHtml.Replace("<style>", "<style>.nav.pages{display:none!important}");
        var snap = new
        {
            page,
            origin = (string?)null,
            savedAt,
            status = "리포트 스냅샷",
            data,
            selected = DefaultSelected(data),
            colorOf,
            prefs = new { month },
            maps = Array.Empty<object>()
        };
        var json = JsonSerializer.Serialize(snap, WebOptions);
        json = json.Replace("<", "\\u003c");
        string script = "<script>window.LOGKIT_SNAPSHOT = " + json + ";<" + "/script>";
        int at = withNav.IndexOf("<script>");
        return at >= 0 ? withNav[..at] + script + withNav[at..] : withNav + script;
    }
}
