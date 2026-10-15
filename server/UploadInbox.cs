using System.Text.Json;

namespace LogServer;

/// <summary>
/// 업로드 수신함. 요청 본문을 검사 없이 data/inbox에 파일로 받아만 두고 즉시 응답하게 한다.
/// 파일 이름은 수신 시각 틱과 순번이라 이름순이 곧 도착순이며, 처리는 InboxProcessor가 나중에 몰아서 한다.
/// 서버가 처리 전에 꺼져도 수신함 파일이 남아 다음 기동에 이어서 처리된다.
/// </summary>
public sealed class UploadInbox
{
    private const string GZIP_EXTENSION = ".json.gz";
    private const string JSON_EXTENSION = ".json";
    private const string TEMP_EXTENSION = ".tmp";

    private readonly ServerOptions _options;
    private readonly SemaphoreSlim _arrived = new(0);
    private long _sequence;

    public UploadInbox(ServerOptions options)
    {
        _options = options;
    }

    /// <summary>처리 대기 파일이 들어가는 디렉터리.</summary>
    public string InboxDirectory => Path.Combine(_options.DataPath, "inbox");

    /// <summary>처리할 수 없었던 파일(깨진 gzip·JSON, 잘못된 sessionId 등)을 옮겨 두는 디렉터리.</summary>
    public string FailedDirectory => Path.Combine(InboxDirectory, "failed");

    /// <summary>
    /// 수신 본문을 수신함에 저장하고 처리기를 깨운다.
    /// 본문과 gzip 여부를 사용하며, 임시 이름으로 다 쓴 뒤 이름을 바꿔 처리기가 덜 쓴 파일을 읽지 않게 한다.
    /// </summary>
    public async Task SaveAsync(byte[] body, bool isGzip, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(InboxDirectory);
        long sequence = Interlocked.Increment(ref _sequence);
        string name = $"{DateTime.UtcNow.Ticks:D19}_{sequence:D10}" + (isGzip ? GZIP_EXTENSION : JSON_EXTENSION);
        string path = Path.Combine(InboxDirectory, name);
        string tempPath = path + TEMP_EXTENSION;

        await File.WriteAllBytesAsync(tempPath, body, cancellationToken);
        File.Move(tempPath, path);
        _arrived.Release();
    }

    /// <summary>
    /// 새 파일이 들어오거나 최대 대기 시간이 지날 때까지 기다린다.
    /// 쌓인 신호는 한 번에 비워 처리 한 바퀴에 한 번만 깨어나게 한다.
    /// </summary>
    public async Task WaitForArrivalAsync(TimeSpan maxWait, CancellationToken cancellationToken)
    {
        await _arrived.WaitAsync(maxWait, cancellationToken);
        while (_arrived.CurrentCount > 0 && _arrived.Wait(0))
        {
        }
    }

    /// <summary>
    /// 처리 대기 파일을 도착순으로 최대 limit개 돌려준다. 쓰는 중인 임시 파일은 제외한다.
    /// </summary>
    public List<string> ListPending(int limit)
    {
        if (!Directory.Exists(InboxDirectory))
        {
            return new List<string>();
        }

        List<string> files = Directory.GetFiles(InboxDirectory)
            .Where(path => !path.EndsWith(TEMP_EXTENSION, StringComparison.Ordinal))
            .ToList();
        files.Sort(StringComparer.Ordinal);
        return files.Count > limit ? files.GetRange(0, limit) : files;
    }

    /// <summary>수신함에 남은 처리 대기 파일 수.</summary>
    public int CountPending()
    {
        return Directory.Exists(InboxDirectory)
            ? Directory.GetFiles(InboxDirectory).Count(path => !path.EndsWith(TEMP_EXTENSION, StringComparison.Ordinal))
            : 0;
    }

    /// <summary>수신함 파일이 gzip 본문인지 확인한다.</summary>
    public static bool IsGzipFile(string path) => path.EndsWith(GZIP_EXTENSION, StringComparison.Ordinal);

    /// <summary>처리할 수 없는 파일을 failed 디렉터리로 옮겨 보존한다. 옮기지 못하면 지운다.</summary>
    public void MoveToFailed(string path)
    {
        try
        {
            Directory.CreateDirectory(FailedDirectory);
            File.Move(path, Path.Combine(FailedDirectory, Path.GetFileName(path)), true);
        }
        catch (IOException)
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// 수신함을 몰아서 처리하는 백그라운드 작업자.
/// 한 바퀴마다 쌓인 파일을 도착순으로 꺼내 파트 파일만 저장하고, 끝나면 이번에 건드린 세션마다
/// 통합 CSV와 월 요약을 한 번씩만 다시 만든다. 5초마다 작은 파트가 들어와도 재구성 비용이 파트 수만큼 늘지 않는다.
/// 작업자는 하나뿐이라 같은 세션의 파트가 도착순으로 처리되고, 같은 파트 재전송은 나중 것이 이긴다.
/// </summary>
public sealed class InboxProcessor : BackgroundService
{
    /// <summary>한 바퀴에 처리할 최대 파일 수. 밀려 있어도 이만큼마다 재구성해 대시보드가 너무 오래 멈춰 보이지 않게 한다.</summary>
    private const int MAX_FILES_PER_ROUND = 500;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly UploadInbox _inbox;
    private readonly CsvSessionStore _store;
    private readonly SessionSummary _summary;
    private readonly ServerOptions _options;
    private readonly ILogger<InboxProcessor> _logger;

    public InboxProcessor(UploadInbox inbox, CsvSessionStore store, SessionSummary summary, ServerOptions options, ILogger<InboxProcessor> logger)
    {
        _inbox = inbox;
        _store = store;
        _summary = summary;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(0.1, _options.InboxProcessIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 밀린 파일이 남아 있으면 쉬지 않고 다음 바퀴를 돈다.
                int processed = ProcessRound();
                if (processed >= MAX_FILES_PER_ROUND)
                {
                    continue;
                }

                await _inbox.WaitForArrivalAsync(Timeout.InfiniteTimeSpan, stoppingToken);
                // 신호 직후 잠시 기다려 그 사이 도착한 파일까지 한 바퀴에 묶는다.
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // 남은 파일은 다음 바퀴에 다시 처리한다. 파트 저장은 교체 방식이라 다시 처리해도 결과가 같다.
                _logger.LogError(exception, "수신함 처리 중 오류");
                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 수신함 파일을 한 바퀴 처리한다. 처리한 파일 수를 반환한다.
    /// 파일 하나의 실패는 그 파일만 failed로 옮기고 나머지는 계속 처리한다.
    /// </summary>
    private int ProcessRound()
    {
        List<string> files = _inbox.ListPending(MAX_FILES_PER_ROUND);
        if (files.Count == 0)
        {
            return 0;
        }

        Dictionary<string, (int Parts, int Entries)> touched = new(StringComparer.Ordinal);
        foreach (string path in files)
        {
            UploadBatch? batch = TryLoad(path, out byte[] body);
            if (batch is null)
            {
                _inbox.MoveToFailed(path);
                continue;
            }

            _store.SavePartFiles(batch, body, UploadInbox.IsGzipFile(path));
            File.Delete(path);

            int entries = batch.Entries?.Length ?? 0;
            touched[batch.SessionId] = touched.TryGetValue(batch.SessionId, out (int Parts, int Entries) count)
                ? (count.Parts + 1, count.Entries + entries)
                : (1, entries);
        }

        foreach ((string sessionId, (int parts, int entries)) in touched)
        {
            _store.RebuildSession(sessionId);
            UploadSummary info = _summary.UpsertSession(sessionId, _store) ?? new UploadSummary();
            Console.WriteLine($"processed session={sessionId} parts={parts} entries={entries} total={info.Total} month={info.Month} endedCleanly={info.EndedCleanly}");
        }

        return files.Count;
    }

    /// <summary>
    /// 수신함 파일을 읽어 배치로 해석한다. gzip 해제·JSON 역직렬화·sessionId·partIndex 검사를 한다.
    /// 처리할 수 없는 파일이면 null을 반환하고 이유를 로그로 남긴다.
    /// </summary>
    private UploadBatch? TryLoad(string path, out byte[] body)
    {
        body = File.ReadAllBytes(path);
        try
        {
            byte[] json = UploadInbox.IsGzipFile(path) ? GzipDecompress(body) : body;
            UploadBatch? batch = JsonSerializer.Deserialize<UploadBatch>(json, _jsonOptions);
            if (batch is null || !CsvSessionStore.IsValidSessionId(batch.SessionId))
            {
                _logger.LogWarning("수신함 파일 거부(sessionId 누락 또는 잘못됨): {File}", Path.GetFileName(path));
                return null;
            }

            if (batch.PartIndex < 0 || batch.PartIndex > CsvSessionStore.MaxPartIndex)
            {
                _logger.LogWarning("수신함 파일 거부(partIndex 범위 밖 {PartIndex}): {File}", batch.PartIndex, Path.GetFileName(path));
                return null;
            }

            return batch;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            _logger.LogWarning("수신함 파일 거부({Reason}): {File}", exception.GetType().Name, Path.GetFileName(path));
            return null;
        }
    }

    private static byte[] GzipDecompress(byte[] compressed)
    {
        using MemoryStream input = new(compressed);
        using System.IO.Compression.GZipStream gzip = new(input, System.IO.Compression.CompressionMode.Decompress);
        using MemoryStream output = new();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
