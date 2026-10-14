namespace LogServer;

/// <summary>
/// RFC 4180 규칙의 CSV 텍스트를 읽는 공용 파서.
/// 인용 필드 안의 개행과 쉼표, 겹쳐 쓴 따옴표를 존중하며 세션 CSV와 요약 CSV 읽기가 함께 쓴다.
/// </summary>
public static class CsvText
{
    /// <summary>
    /// CSV 텍스트를 행 목록으로 읽어 들인다.
    /// text를 입력으로 사용하며 비어 있지 않은 행만 담은 목록을 반환한다.
    /// </summary>
    public static List<string[]> ParseRows(string text)
    {
        List<string[]> rows = new();
        List<string> fields = new();
        System.Text.StringBuilder field = new();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }

        rows.RemoveAll(row => row.Length == 0 || (row.Length == 1 && row[0].Length == 0));
        return rows;
    }
}
