using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LogServer;

/// <summary>
/// 수신 서버의 내보내기 API에서 세션 로그를 주기적으로 복사해 분석 미러 저장소에 맞춘다.
/// 목록의 updatedUtc가 미러 사본의 변경 시각과 다른 세션만 내려받고, 수신 서버에서 사라진 세션은 미러에서도 지운다.
/// 미러 파일의 수정 시각을 수신 서버 시각으로 맞춰 두므로 분석 캐시도 변경분만 다시 계산한다.
/// </summary>
public sealed class IngestMirror : BackgroundService
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly AnalyticsOptions _options;
    private readonly CsvSessionStore _store;
    private readonly SessionSummary _summary;
    private readonly GameAnalytics _analytics;
    private readonly ILogger<IngestMirror> _logger;
    private readonly HttpClient _http;
    private MirrorStatus _status = new();

    /// <summary>
    /// 설정과 미러 저장소, 집계기를 받아 동기화 서비스를 만든다.
    /// 수신 서버 주소와 키로 HttpClient를 만들어 인스턴스가 쓰는 동안 재사용한다.
    /// </summary>
    public IngestMirror(
        AnalyticsOptions options,
        CsvSessionStore store,
        SessionSummary summary,
        GameAnalytics analytics,
        ILogger<IngestMirror> logger)
    {
        _options = options;
        _store = store;
        _summary = summary;
        _analytics = analytics;
        _logger = logger;
        _http = new HttpClient
        {
            BaseAddress = new Uri(options.IngestUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(options.IngestTimeoutSeconds),
        };
        _http.DefaultRequestHeaders.Add("X-Api-Key", options.IngestApiKey);
    }

    /// <summary>마지막 동기화 결과. 상태 API가 읽는다.</summary>
    public MirrorStatus Status => _status;

    /// <summary>
    /// 기동 직후 한 번 맞춘 뒤 SyncIntervalSeconds마다 반복한다. 실패해도 서비스는 멈추지 않고 다음 주기에 다시 시도한다.
    /// stoppingToken을 입력으로 받으며 종료 신호가 오면 반복을 끝낸다.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(Math.Max(5, _options.SyncIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>
    /// 한 번의 동기화를 실행하고 결과를 로그와 상태에 남긴다. 바뀐 것이 있으면 분석 캐시를 미리 채운다.
    /// stoppingToken을 입력으로 받으며, 예외는 로그로 남기고 삼킨다(종료 신호는 제외).
    /// </summary>
    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            SyncResult result = await SyncOnceAsync(stoppingToken);
            _status = new MirrorStatus
            {
                Ok = true,
                LastSyncUtc = DateTime.UtcNow,
                Sessions = result.Sessions,
                LastUpdated = result.Updated,
                LastRemoved = result.Removed,
            };

            if (result.Updated > 0 || result.Removed > 0)
            {
                _logger.LogInformation("미러 동기화: 세션 {Sessions}개, 갱신 {Updated}, 삭제 {Removed}", result.Sessions, result.Updated, result.Removed);
                _analytics.Warm();
            }
        }
        catch (Exception e) when (!stoppingToken.IsCancellationRequested)
        {
            _status = new MirrorStatus
            {
                Ok = false,
                LastSyncUtc = _status.LastSyncUtc,
                LastError = e.Message,
                Sessions = _status.Sessions,
            };
            _logger.LogWarning(e, "미러 동기화 실패 — 다음 주기에 다시 시도합니다");
        }
    }

    /// <summary>
    /// 수신 서버 목록과 미러를 맞춘다. 목록에 없는 미러 세션은 지우고, 변경 시각이 다른 세션은 내려받아 요약을 갱신한다.
    /// stoppingToken을 입력으로 받으며 세션 수·갱신 수·삭제 수를 담은 결과를 반환한다.
    /// </summary>
    public async Task<SyncResult> SyncOnceAsync(CancellationToken stoppingToken)
    {
        ExportIndexResponse index = await _http.GetFromJsonAsync<ExportIndexResponse>("api/export/index", _json, stoppingToken)
            ?? throw new InvalidOperationException("수신 서버가 빈 목록을 돌려주었습니다");

        Dictionary<string, DateTime> remote = new(StringComparer.Ordinal);
        foreach (ExportIndexItem item in index.Sessions)
        {
            remote[item.SessionId] = ParseUtc(item.UpdatedUtc);
        }

        int updated = 0;
        int removed = 0;

        foreach (string local in _store.ListSessionIds())
        {
            if (!remote.ContainsKey(local))
            {
                RemoveLocal(local);
                removed++;
            }
        }

        foreach ((string sessionId, DateTime changedUtc) in remote)
        {
            DateTime? have = _store.LastChangedUtc(sessionId);
            if (have is not null && Math.Abs((have.Value - changedUtc).TotalSeconds) < 1)
            {
                continue;
            }

            stoppingToken.ThrowIfCancellationRequested();
            if (await DownloadSessionAsync(sessionId, changedUtc, stoppingToken))
            {
                updated++;
            }
            else
            {
                // 목록을 받은 뒤 수신 서버에서 지워진 세션
                RemoveLocal(sessionId);
                removed++;
            }
        }

        return new SyncResult(remote.Count, updated, removed);
    }

    /// <summary>
    /// 세션 하나를 내려받아 미러에 쓰고 요약 행을 갱신한다. 통합 CSV가 없으면 false를 반환한다.
    /// sessionId와 changedUtc, stoppingToken을 입력으로 사용하며, 메타·지표는 없어도 된다(없으면 미러에서 지운다).
    /// </summary>
    private async Task<bool> DownloadSessionAsync(string sessionId, DateTime changedUtc, CancellationToken stoppingToken)
    {
        if (!await FetchFileAsync($"api/export/sessions/{sessionId}/csv", _store.SessionCsvPath(sessionId), changedUtc, stoppingToken))
        {
            return false;
        }

        if (!await FetchFileAsync($"api/export/sessions/{sessionId}/meta", _store.MetaPath(sessionId), changedUtc, stoppingToken))
        {
            DeleteIfExists(_store.MetaPath(sessionId));
        }

        if (!await FetchFileAsync($"api/export/sessions/{sessionId}/metrics", _store.MetricsCsvPath(sessionId), changedUtc, stoppingToken))
        {
            DeleteIfExists(_store.MetricsCsvPath(sessionId));
        }

        if (_summary.UpsertSession(sessionId, _store) is null)
        {
            _summary.RemoveSession(sessionId);
        }

        return true;
    }

    /// <summary>
    /// 파일 하나를 임시 파일로 받아 교체하고, 수정 시각을 수신 서버 기준 시각으로 맞춘다.
    /// url(상대 경로), path(미러 경로), changedUtc, stoppingToken을 입력으로 사용하며 404면 false를 반환한다.
    /// 분석이 읽고 있는 파일과 겹치면 잠깐 기다렸다가 다시 바꾼다.
    /// </summary>
    private async Task<bool> FetchFileAsync(string url, string path, DateTime changedUtc, CancellationToken stoppingToken)
    {
        using HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string temp = path + ".sync";
        await using (FileStream file = new(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            await response.Content.CopyToAsync(file, stoppingToken);
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                break;
            }
            catch (IOException) when (attempt < 10)
            {
                await Task.Delay(200, stoppingToken);
            }
        }

        File.SetLastWriteTimeUtc(path, changedUtc);
        return true;
    }

    /// <summary>
    /// 세션 파일을 미러에서 지우고 요약 행도 뺀다.
    /// sessionId를 입력으로 사용하며 파일 시스템과 요약 파일을 변경한다.
    /// </summary>
    private void RemoveLocal(string sessionId)
    {
        _store.DeleteSessionFiles(sessionId);
        _summary.RemoveSession(sessionId);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static DateTime ParseUtc(string text)
    {
        return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }
}

/// <summary>한 번의 동기화 결과. 수신 서버 세션 수와 갱신·삭제 개수.</summary>
public readonly record struct SyncResult(int Sessions, int Updated, int Removed);

/// <summary>미러 동기화 상태. 상태 API가 돌려준다.</summary>
public sealed class MirrorStatus
{
    /// <summary>마지막 성공 여부.</summary>
    public bool Ok { get; init; }

    /// <summary>마지막으로 동기화를 시도한 시각(UTC). 한 번도 없으면 null.</summary>
    public DateTime? LastSyncUtc { get; init; }

    /// <summary>마지막 실패 메시지. 성공이면 null.</summary>
    public string? LastError { get; init; }

    /// <summary>수신 서버에 있던 세션 수(마지막 성공 기준).</summary>
    public int Sessions { get; init; }

    /// <summary>마지막 동기화에서 내려받은 세션 수.</summary>
    public int LastUpdated { get; init; }

    /// <summary>마지막 동기화에서 지운 세션 수.</summary>
    public int LastRemoved { get; init; }
}
