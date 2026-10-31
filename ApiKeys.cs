namespace LogServer;

/// <summary>
/// API 호출 권한. 업로드는 게임 클라이언트가, 열람은 GM 툴과 분석 서버가, 관리(삭제·복원)는 운영자가 쓴다.
/// </summary>
public enum ApiRole
{
    /// <summary>GET·HEAD 등 조회와 내보내기.</summary>
    Read,

    /// <summary>POST /api/logs 업로드 전용.</summary>
    Upload,

    /// <summary>삭제·복원·맵 등록처럼 데이터를 바꾸는 호출. 관리 키는 모든 역할을 처리한다.</summary>
    Admin,
}

/// <summary>
/// 역할별 API 키 묶음. 읽기·업로드 키를 설정에서 비우면 관리 키로 대신한다(예전 단일 ApiKey 설정과 같은 동작).
/// 키가 역할에 맞지 않으면 403, 어떤 키와도 맞지 않으면 401로 구분한다.
/// </summary>
public sealed class ApiKeys
{
    private static readonly System.Text.RegularExpressions.Regex _projectName =
        new("^[a-z0-9][a-z0-9_-]{0,31}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly Dictionary<string, string> _projectsByKey = new(StringComparer.Ordinal);

    /// <summary>
    /// 역할별 키를 만든다. admin은 필수이며 비어 있으면 어떤 호출도 허용하지 않는다.
    /// read·upload가 비어 있으면 admin 키를 그 역할에도 쓴다.
    /// projects는 프로젝트 이름 → 업로드 키 매핑이다. 이름은 소문자·숫자·_·-만 쓰고, 키는 비어 있으면 안 된다.
    /// </summary>
    public ApiKeys(string admin, string? read = null, string? upload = null, IEnumerable<KeyValuePair<string, string>>? projects = null)
    {
        Admin = admin ?? string.Empty;
        Read = string.IsNullOrEmpty(read) ? Admin : read;
        Upload = string.IsNullOrEmpty(upload) ? Admin : upload;

        if (projects is not null)
        {
            foreach ((string name, string key) in projects)
            {
                if (!_projectName.IsMatch(name))
                {
                    throw new ArgumentException($"프로젝트 이름이 잘못되었습니다(소문자·숫자·_·- 32자 이내): {name}");
                }

                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException($"프로젝트 '{name}'의 업로드 키가 비어 있습니다");
                }

                if (_projectsByKey.ContainsKey(key))
                {
                    throw new ArgumentException($"같은 업로드 키가 여러 프로젝트에 쓰였습니다: {name}");
                }

                _projectsByKey[key] = name;
            }
        }
    }

    /// <summary>관리 키. 모든 역할을 처리한다.</summary>
    public string Admin { get; }

    /// <summary>열람 키. 조회와 내보내기만 허용한다.</summary>
    public string Read { get; }

    /// <summary>업로드 키. POST /api/logs만 허용한다.</summary>
    public string Upload { get; }

    /// <summary>provided 키가 role에 필요한 권한을 갖는지 본다.</summary>
    public bool IsAllowed(string? provided, ApiRole role)
    {
        return role switch
        {
            ApiRole.Read => Matches(provided, Read) || Matches(provided, Admin),
            ApiRole.Upload => Matches(provided, Upload) || Matches(provided, Admin) || ProjectFor(provided) is not null,
            _ => Matches(provided, Admin),
        };
    }

    /// <summary>
    /// 업로드 키에 해당하는 프로젝트 이름을 돌려준다. 프로젝트 키가 아니면 null이다.
    /// 공용 업로드 키나 관리 키로 온 요청은 프로젝트가 없다.
    /// </summary>
    public string? ProjectFor(string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return null;
        }

        return _projectsByKey.TryGetValue(provided, out string? name) ? name : null;
    }

    /// <summary>provided 키가 어떤 역할의 키로든 쓰이는지 본다(권한 부족과 잘못된 키를 구분할 때 쓴다).</summary>
    public bool IsKnown(string? provided)
    {
        return Matches(provided, Admin) || Matches(provided, Read) || Matches(provided, Upload) || ProjectFor(provided) is not null;
    }

    private static bool Matches(string? provided, string key)
    {
        return !string.IsNullOrEmpty(key) && string.Equals(provided, key, StringComparison.Ordinal);
    }
}
