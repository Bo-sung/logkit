using System.Globalization;

namespace LogServer;

/// <summary>
/// 플레이 지표 합산과 파생 지표 계산을 담당하는 헬퍼.
/// 세션 상세와 월별 지표 API가 지표 파일과 설정된 파생 지표 규칙을 계산할 때 사용한다.
/// </summary>
internal static class MetricMath
{
    /// <summary>
    /// 지표 샘플 목록을 키별 누적 맵으로 바꾼다.
    /// samples를 입력으로 사용하며 키를 비교 시 대소문자를 구분하는 맵을 반환한다.
    /// </summary>
    public static Dictionary<string, (long Count, double Sum)> ToMap(IEnumerable<MetricSample> samples)
    {
        Dictionary<string, (long Count, double Sum)> map = new(StringComparer.Ordinal);
        MergeInto(map, samples);
        return map;
    }

    /// <summary>
    /// 지표 샘플 목록을 기존 맵에 합산한다.
    /// map과 samples를 입력으로 사용하며 map 내용을 변경한다.
    /// </summary>
    public static void MergeInto(Dictionary<string, (long Count, double Sum)> map, IEnumerable<MetricSample> samples)
    {
        foreach (MetricSample sample in samples)
        {
            if (sample == null || string.IsNullOrEmpty(sample.Key))
            {
                continue;
            }

            map.TryGetValue(sample.Key, out (long Count, double Sum) current);
            map[sample.Key] = (current.Count + sample.Count, current.Sum + sample.Sum);
        }
    }

    /// <summary>
    /// 누적 맵을 횟수 내림차순 통계 목록으로 바꾼다.
    /// map을 입력으로 사용하며 횟수 내림차순으로 정렬된 MetricStat 목록을 반환한다.
    /// </summary>
    public static List<MetricStat> ToStats(Dictionary<string, (long Count, double Sum)> map)
    {
        List<MetricStat> stats = new(map.Count);
        foreach (KeyValuePair<string, (long Count, double Sum)> pair in map)
        {
            stats.Add(ToStat(pair.Key, pair.Value.Count, pair.Value.Sum));
        }

        stats.Sort((a, b) => b.Count.CompareTo(a.Count));
        return stats;
    }

    /// <summary>지표 키와 누적값을 통계 한 행으로 바꾼다. 평균은 횟수가 0이면 0이다.</summary>
    public static MetricStat ToStat(string key, long count, double sum)
    {
        return new MetricStat
        {
            Key = key,
            Count = count,
            Sum = sum,
            Avg = count > 0 ? sum / count : 0.0,
        };
    }

    /// <summary>
    /// 설정된 파생 지표 규칙을 누적 맵으로 계산한다.
    /// rules와 map을 입력으로 사용하며, 분모가 0이면 값을 null로 담은 목록을 반환한다.
    /// </summary>
    public static List<DerivedMetricValue> Evaluate(
        IEnumerable<DerivedMetricOptions> rules,
        IReadOnlyDictionary<string, (long Count, double Sum)> map)
    {
        List<DerivedMetricValue> values = new();
        foreach (DerivedMetricOptions rule in rules)
        {
            if (string.IsNullOrEmpty(rule.Name) || string.IsNullOrEmpty(rule.Numerator) || string.IsNullOrEmpty(rule.Denominator))
            {
                continue;
            }

            long numeratorCount = map.TryGetValue(rule.Numerator, out (long Count, double Sum) numerator) ? numerator.Count : 0;
            long denominatorCount = map.TryGetValue(rule.Denominator, out (long Count, double Sum) denominator) ? denominator.Count : 0;
            values.Add(new DerivedMetricValue
            {
                Name = rule.Name,
                Numerator = rule.Numerator,
                Denominator = rule.Denominator,
                Value = denominatorCount > 0 ? (double)numeratorCount / denominatorCount : null,
                Unit = string.IsNullOrEmpty(rule.Unit) ? "%" : rule.Unit,
            });
        }

        return values;
    }

    /// <summary>지표 CSV의 sum 필드를 불러올 때 쓰는 고정 문화권 파싱이다.</summary>
    public static double ParseSum(string text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0.0;
    }
}
