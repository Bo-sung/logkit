namespace LogServer;

/// <summary>
/// RFC 4180 규칙의 CSV 텍스트를 읽는 공용 파서.
/// 인용 필드 안의 개행과 쉼표, 겹쳐 쓴 따옴표를 존중하며 세션 CSV와 요약 CSV 읽기가 함께 쓴다.
/// </summary>
public static class CsvText
{
    private static readonly System.Buffers.SearchValues<char> _special = System.Buffers.SearchValues.Create(",\r\n\"");

    /// <summary>
    /// CSV 텍스트를 행 목록으로 읽어 들인다.
    /// text를 입력으로 사용하며 비어 있지 않은 행만 담은 목록을 반환한다.
    /// </summary>
    public static List<string[]> ParseRows(string text) => ReadRows(new StringReader(text)).ToList();

    /// <summary>
    /// CSV를 읽기 도구에서 한 행씩 읽는다. 파일 전체를 문자열로 올리지 않아 큰 세션 CSV도 메모리를 적게 쓴다.
    /// 규칙은 RFC 4180(인용 필드 안의 쉼표·개행, 겹쳐 쓴 따옴표)이며 빈 행은 건너뛴다. 따옴표 밖의 CR(\r)은 무시한다.
    /// </summary>
    public static IEnumerable<string[]> ReadRows(TextReader reader)
    {
        char[] buffer = new char[1 << 16];
        System.Text.StringBuilder field = new();
        List<string> fields = new(8);
        bool quoted = false;
        bool quoteAtEnd = false; // 인용 중 버퍼 끝에서 따옴표를 만남: 다음 글자가 따옴표면 이스케이프, 아니면 인용 끝
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            int i = 0;
            if (quoteAtEnd)
            {
                quoteAtEnd = false;
                if (buffer[0] == '"')
                {
                    field.Append('"');
                    i = 1;
                }
                else
                {
                    quoted = false;
                }
            }

            while (i < read)
            {
                if (quoted)
                {
                    int close = Array.IndexOf(buffer, '"', i, read - i);
                    if (close < 0)
                    {
                        field.Append(buffer, i, read - i);
                        i = read;
                        break;
                    }

                    field.Append(buffer, i, close - i);
                    i = close + 1;
                    if (i == read)
                    {
                        quoteAtEnd = true;
                    }
                    else if (buffer[i] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }

                    continue;
                }

                int next = buffer.AsSpan(i, read - i).IndexOfAny(_special);
                if (next < 0)
                {
                    field.Append(buffer, i, read - i);
                    break;
                }

                field.Append(buffer, i, next);
                i += next;
                char c = buffer[i++];
                switch (c)
                {
                    case '"':
                        quoted = true;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\n':
                        fields.Add(field.ToString());
                        field.Clear();
                        if (!IsEmptyRow(fields))
                        {
                            yield return fields.ToArray();
                        }

                        fields.Clear();
                        break;
                }
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            if (!IsEmptyRow(fields))
            {
                yield return fields.ToArray();
            }
        }
    }

    private static bool IsEmptyRow(List<string> fields) => fields.Count == 0 || (fields.Count == 1 && fields[0].Length == 0);
}
