namespace LogServer;

/// <summary>
/// 앱 버전 문자열 정렬 규칙. 수신 서버 목록과 분석 서버 집계가 같은 순서를 쓰도록 한곳에 둔다.
/// </summary>
public static class AppVersionOrder
{
    /// <summary>
    /// 버전 문자열을 점 구분 숫자 단위로 비교한다(1.10이 1.9 뒤). 숫자가 아닌 조각은 문자열로 비교한다.
    /// </summary>
    public static int Compare(string? a, string? b)
    {
        string[] left = (a ?? string.Empty).Split('.', '-', '_');
        string[] right = (b ?? string.Empty).Split('.', '-', '_');
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            string l = i < left.Length ? left[i] : string.Empty;
            string r = i < right.Length ? right[i] : string.Empty;
            int cmp = long.TryParse(l, out long ln) && long.TryParse(r, out long rn)
                ? ln.CompareTo(rn)
                : string.CompareOrdinal(l, r);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return 0;
    }
}
