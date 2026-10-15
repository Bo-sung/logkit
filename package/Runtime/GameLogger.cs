using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LogKit
{

    /// <summary>
    /// 게임 로그를 수집해 로컬 파일로 기록하고 프로그램 종료 시 서버로 전송하는 로그 클래스다.
    /// Unity 콘솔 자동 캡처와 구조화 로그 API를 제공하며, 외부 싱글턴 베이스 없이 자체 수명 주기로 동작한다.
    /// 로깅 자체의 실패는 삼켜서 게임 진행을 방해하지 않는다.
    /// </summary>
    public class GameLogger : MonoBehaviour
    {
        private const string DEFAULT_SERVER_URL = "http://localhost:5000/api/logs";
        private const string DEFAULT_API_KEY = "dev-log-key";
        private const string LOG_DIRECTORY_NAME = "Logs";
        private const string UNITY_CATEGORY = "Unity";
        private const string LEVEL_INFO = "Info";
        private const string LEVEL_WARNING = "Warning";
        private const string LEVEL_ERROR = "Error";

        private const int PENDING_RETENTION_DAYS = 30;
        private const int QUIT_WAIT_MILLISECONDS = 3000;
        private const int QUIT_WRITER_JOIN_MILLISECONDS = 1500;
        private const int WRITER_SIGNAL_POLL_MILLISECONDS = 200;
        private const int FALLBACK_WAIT_MILLISECONDS = 1000;

        private static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;

        [Header("서버 연결")]
        // 개발 기본값은 로그 서버 호스트 PC의 수신 주소다. 상황에 따라 Inspector에서 덮어쓴다.
        [SerializeField] private string _developmentServerUrl = DEFAULT_SERVER_URL;
        [SerializeField] private string _releaseServerUrl = DEFAULT_SERVER_URL;
        [SerializeField] private string _apiKey = DEFAULT_API_KEY;

        [Header("수집 설정")]
        [SerializeField] private bool _captureUnityLogs = true;
        [SerializeField] private float _flushIntervalSeconds = 5f;

        [Header("전송 설정")]
        [SerializeField] [Tooltip("플레이 중 새로 쌓인 로그를 서버로 보내는 주기(초). 0 이하로 두면 주기 전송 없이 종료 때만 보낸다.")]
        private float _uploadIntervalSeconds = 5f;
        [SerializeField] private bool _uploadOnQuit = true;
        [SerializeField] private bool _retryPendingOnStartup = true;
        [SerializeField] private int _requestTimeoutSeconds = 5;

        private static GameLogger _instance;
        private static bool _quitting;

        private static readonly object _configLock = new object();
        private static Overrides _overrides;

        /// <summary>Configure로 넣은 기동 전 설정. 초기화 때 인스펙터 값 위에 덮어쓰고 비워진다.</summary>
        private sealed class Overrides
        {
            public string DevelopmentServerUrl;
            public string ReleaseServerUrl;
            public string ApiKey;
            public bool? CaptureUnityLogs;
            public float? FlushIntervalSeconds;
            public bool? UploadOnQuit;
            public bool? RetryPendingOnStartup;
            public int? RequestTimeoutSeconds;
            public float? UploadIntervalSeconds;
        }

        private readonly object _queueLock = new object();
        private readonly object _stateLock = new object();
        private readonly Queue<LogEntry> _queue = new Queue<LogEntry>();
        private readonly ConcurrentDictionary<string, MetricAccumulator> _metrics = new ConcurrentDictionary<string, MetricAccumulator>(StringComparer.Ordinal);
        private LogUploader _uploader;
        private LogFileWriter _writer;
        private string _logDirectory;
        private string _sessionId = string.Empty;
        private string _sessionStartedAtUtc = string.Empty;
        private LogSessionHeader _sessionHeader;
        private string _currentSceneName = string.Empty;
        private bool _quitFlushTriggered;
        private bool _bootstrapped;
        private float _flushTimer;

        // 파일 기록은 워커 스레드가 담당한다. Update는 신호만 보내고 종료 경로에서만 동기로 마무리한다.
        private AutoResetEvent _flushSignal;
        private Thread _writerThread;
        private volatile bool _stopWriter;

        // 주기 전송은 별도 스레드가 맡아 네트워크 지연이 파일 기록을 막지 않게 한다.
        private LogShipper _shipper;
        private AutoResetEvent _shipSignal;
        private Thread _shipperThread;
        private volatile bool _stopShipper;

        /// <summary>지표 누적기. count는 Interlocked로, sum은 락으로 더한다.</summary>
        private sealed class MetricAccumulator
        {
            public long count;
            public double sum;
        }

        /// <summary>
        /// 로그 시스템을 즉시 초기화한다.
        /// 게임 기동 경로에서 호출하며, 초기화 실패는 조용히 무시해 게임을 중단시키지 않는다.
        /// 보통은 자동 부트스트랩(AutoBootstrap)이 대신하므로 직접 호출은 불필요하다.
        /// </summary>
        public static void Init()
        {
            EnsureInstance();
        }

        /// <summary>
        /// 씬 로드가 끝나면 로거를 자동으로 준비한다.
        /// 씬에 배치된 GameLogger 컴포넌트가 있으면 인스펙터 설정을 그대로 쓰고,
        /// 없으면 코드 기본값(또는 Configure로 넣은 값)으로 만든다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            EnsureInstance();
        }

        /// <summary>
        /// 인스펙터에서 설정하는 값들을 코드로 넣는다.
        /// 초기화 전에 호출하면 초기화 때 적용되고, 초기화 후에는 살아있는 인스턴스에 즉시 적용된다.
        /// null(기본값)인 항목은 기존 값(인스펙터 값 또는 코드 기본값)을 유지한다.
        /// 전송 URL·키는 되도록 기동 초반(씬 오브젝트 Awake 또는 BeforeSplashScreen 단계)에 넣는다.
        /// </summary>
        public static void Configure(
            string developmentServerUrl = null,
            string releaseServerUrl = null,
            string apiKey = null,
            bool? captureUnityLogs = null,
            float? flushIntervalSeconds = null,
            bool? uploadOnQuit = null,
            bool? retryPendingOnStartup = null,
            int? requestTimeoutSeconds = null,
            float? uploadIntervalSeconds = null)
        {
            GameLogger logger = _instance;
            if (logger != null)
            {
                logger.ApplyValues(developmentServerUrl, releaseServerUrl, apiKey, captureUnityLogs,
                    flushIntervalSeconds, uploadOnQuit, retryPendingOnStartup, requestTimeoutSeconds, uploadIntervalSeconds);
                logger.RebuildUploader();
                return;
            }

            lock (_configLock)
            {
                if (_overrides == null)
                    _overrides = new Overrides();
                if (developmentServerUrl != null) _overrides.DevelopmentServerUrl = developmentServerUrl;
                if (releaseServerUrl != null) _overrides.ReleaseServerUrl = releaseServerUrl;
                if (apiKey != null) _overrides.ApiKey = apiKey;
                if (captureUnityLogs.HasValue) _overrides.CaptureUnityLogs = captureUnityLogs;
                if (flushIntervalSeconds.HasValue) _overrides.FlushIntervalSeconds = flushIntervalSeconds;
                if (uploadOnQuit.HasValue) _overrides.UploadOnQuit = uploadOnQuit;
                if (retryPendingOnStartup.HasValue) _overrides.RetryPendingOnStartup = retryPendingOnStartup;
                if (requestTimeoutSeconds.HasValue) _overrides.RequestTimeoutSeconds = requestTimeoutSeconds;
                if (uploadIntervalSeconds.HasValue) _overrides.UploadIntervalSeconds = uploadIntervalSeconds;
            }
        }

        /// <summary>
        /// null이 아닌 설정 값만 인스턴스 필드에 덮어쓴다.
        /// Configure(즉시 적용)와 기동 전 오버라이드 소비가 함께 쓴다.
        /// </summary>
        private void ApplyValues(string developmentServerUrl, string releaseServerUrl, string apiKey,
            bool? captureUnityLogs, float? flushIntervalSeconds,
            bool? uploadOnQuit, bool? retryPendingOnStartup, int? requestTimeoutSeconds, float? uploadIntervalSeconds)
        {
            if (developmentServerUrl != null) _developmentServerUrl = developmentServerUrl;
            if (releaseServerUrl != null) _releaseServerUrl = releaseServerUrl;
            if (apiKey != null) _apiKey = apiKey;
            if (captureUnityLogs.HasValue) _captureUnityLogs = captureUnityLogs.Value;
            if (flushIntervalSeconds.HasValue) _flushIntervalSeconds = flushIntervalSeconds.Value;
            if (uploadOnQuit.HasValue) _uploadOnQuit = uploadOnQuit.Value;
            if (retryPendingOnStartup.HasValue) _retryPendingOnStartup = retryPendingOnStartup.Value;
            if (requestTimeoutSeconds.HasValue) _requestTimeoutSeconds = requestTimeoutSeconds.Value;
            if (uploadIntervalSeconds.HasValue) _uploadIntervalSeconds = uploadIntervalSeconds.Value;
        }

        /// <summary>현재 필드 값으로 전송기를 다시 만든다. 설정 변경 후 호출한다.</summary>
        private void RebuildUploader()
        {
            lock (_stateLock)
            {
                _uploader = new LogUploader(ResolveServerUrl(), _apiKey, _requestTimeoutSeconds);
            }
        }

        /// <summary>
        /// Configure로 기동 전에 넣은 설정을 소비해 필드에 적용한다.
        /// 초기화 때 호출하며, 소비 후 오버라이드는 비워진다.
        /// </summary>
        private void ConsumeStartupOverrides()
        {
            lock (_configLock)
            {
                Overrides overrides = _overrides;
                _overrides = null;
                if (overrides == null)
                    return;

                ApplyValues(overrides.DevelopmentServerUrl, overrides.ReleaseServerUrl, overrides.ApiKey,
                    overrides.CaptureUnityLogs, overrides.FlushIntervalSeconds,
                    overrides.UploadOnQuit, overrides.RetryPendingOnStartup, overrides.RequestTimeoutSeconds,
                    overrides.UploadIntervalSeconds);
            }
        }

        /// <summary>
        /// 정보 수준의 구조화 로그를 기록한다.
        /// category와 message를 사용하며, 세션 큐에 LogEntry를 쌓는다.
        /// </summary>
        public static void Info(string category, string message) => EnqueueManual(LEVEL_INFO, category, message, null, false);

        /// <summary>
        /// 경고 수준의 구조화 로그를 기록한다.
        /// category와 message를 사용하며, 세션 큐에 LogEntry를 쌓는다.
        /// </summary>
        public static void Warn(string category, string message) => EnqueueManual(LEVEL_WARNING, category, message, null, false);

        /// <summary>
        /// 오류 수준의 구조화 로그를 기록한다.
        /// category와 message를 사용하며, 호출 스택트레이스를 함께 남긴다.
        /// </summary>
        public static void Error(string category, string message) => EnqueueManual(LEVEL_ERROR, category, message, null, true);

        /// <summary>
        /// 부가 데이터를 포함한 로그를 기록한다.
        /// level·category·message·data를 사용하며, data는 문자열 또는 JsonUtility로 직렬화 가능한 객체여야 한다.
        /// </summary>
        public static void Data(string level, string category, string message, object data)
        {
            EnqueueManual(level, category, message, data, level == LEVEL_ERROR);
        }

        /// <summary>
        /// 플레이 지표 카운터를 1 늘린다. 공격 시도·스캔 발동 같은 발생 횟수 집계에 쓴다.
        /// </summary>
        public static void Count(string key) => Count(key, 1L);

        /// <summary>
        /// 플레이 지표 카운터를 weight만큼 늘린다. 데미지 총량처럼 한 사건의 크기가 1보다 클 때 쓴다.
        /// </summary>
        public static void Count(string key, long weight)
        {
            GameLogger logger = _instance;
            if (logger == null || string.IsNullOrEmpty(key) || weight <= 0)
                return;

            MetricAccumulator accumulator = logger._metrics.GetOrAdd(key, _ => new MetricAccumulator());
            Interlocked.Add(ref accumulator.count, weight);
        }

        /// <summary>
        /// 값을 하나 기록해 지표의 횟수와 합계를 함께 늘린다.
        /// 조준 거리·프레임 시간처럼 횟수와 평균을 같이 보고 싶은 값에 쓴다.
        /// </summary>
        public static void Measure(string key, double value)
        {
            GameLogger logger = _instance;
            if (logger == null || string.IsNullOrEmpty(key))
                return;

            MetricAccumulator accumulator = logger._metrics.GetOrAdd(key, _ => new MetricAccumulator());
            Interlocked.Increment(ref accumulator.count);
            lock (accumulator)
            {
                accumulator.sum += value;
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeLogger();
        }

        void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
            Application.wantsToQuit += OnWantsToQuit;
    #if UNITY_EDITOR
            EditorApplication.quitting += OnEditorQuitting;
    #endif
        }

        void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
            Application.wantsToQuit -= OnWantsToQuit;
    #if UNITY_EDITOR
            EditorApplication.quitting -= OnEditorQuitting;
    #endif
            StopWriter(500);
            StopShipper();
            // 종료 경로에서 이미 비우고 업로드를 시작했으면 다시 쓰지 않는다.
            // 업로드가 지운 지표 파일이 다시 만들어져 짝 없는 .metrics.json이 남는 것을 막는다.
            if (!_quitFlushTriggered)
                FlushQueue();
        }

        void Update()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
                _currentSceneName = activeScene.name;

            _flushTimer += Time.unscaledDeltaTime;
            if (_flushTimer < _flushIntervalSeconds)
                return;

            _flushTimer = 0f;
            _flushSignal?.Set();
        }

        void OnApplicationQuit()
        {
            _quitting = true;
            TriggerQuitFlush(FALLBACK_WAIT_MILLISECONDS);
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>
        /// 로거를 초기화한다.
        /// 세션 ID, 로그 디렉터리, 전송기, 파일 기록 워커와 주기 전송 워커를 준비하며, 기동 재전송을 시작한다. 중복 호출은 무시한다.
        /// </summary>
        private void InitializeLogger()
        {
            if (_bootstrapped)
                return;
            _bootstrapped = true;

            _sessionId = CreateSessionId();
            _sessionStartedAtUtc = DateTime.UtcNow.ToString("o");
            // Application.version 등은 메인 스레드에서만 읽을 수 있으므로 헤더를 여기서 미리 만든다.
            // 세션 파일은 워커 스레드가 처음 만들 수 있다.
            _sessionHeader = new LogSessionHeader
            {
                sessionId = _sessionId,
                startedAtUtc = _sessionStartedAtUtc,
                appVersion = Application.version,
                unityVersion = Application.unityVersion,
                isEditor = Application.isEditor,
                platform = Application.platform.ToString()
            };
            _logDirectory = Path.Combine(Application.persistentDataPath, LOG_DIRECTORY_NAME);
            ConsumeStartupOverrides();
            _uploader = new LogUploader(ResolveServerUrl(), _apiKey, _requestTimeoutSeconds);

            Scene activeScene = SceneManager.GetActiveScene();
            _currentSceneName = activeScene.IsValid() ? activeScene.name : string.Empty;

            _flushSignal = new AutoResetEvent(false);
            _stopWriter = false;
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "LogKit.Writer" };
            _writerThread.Start();

            _shipper = new LogShipper(LogFileWriter.GetSessionFilePath(_logDirectory, _sessionId));
            _shipSignal = new AutoResetEvent(false);
            _stopShipper = false;
            _shipperThread = new Thread(ShipperLoop) { IsBackground = true, Name = "LogKit.Shipper" };
            _shipperThread.Start();

            RetryPendingUploads();
        }

        /// <summary>
        /// 로거 인스턴스를 보장한다.
        /// 씬에 배치된 컴포넌트를 재사용하고 없으면 새로 만들며, Play 모드가 아니거나 종료 중이면 만들지 않는다.
        /// </summary>
        private static void EnsureInstance()
        {
            if (_quitting || !Application.isPlaying || _instance != null)
                return;

            GameLogger candidate = FindFirstObjectByType<GameLogger>();
            if (candidate != null)
            {
                _instance = candidate;
                DontDestroyOnLoad(candidate.gameObject);
                candidate.InitializeLogger();
                return;
            }

            GameObject host = new GameObject("LogKit");
            host.AddComponent<GameLogger>();
        }

        void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!_captureUnityLogs)
                return;

            bool isError = type == LogType.Error || type == LogType.Assert || type == LogType.Exception;
            Enqueue(new LogEntry
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                level = MapLevel(type),
                category = UNITY_CATEGORY,
                scene = GetCurrentSceneName(),
                message = condition ?? string.Empty,
                dataJson = string.Empty,
                stackTrace = isError ? stackTrace ?? string.Empty : string.Empty
            });
        }

        private bool OnWantsToQuit()
        {
            TriggerQuitFlush(QUIT_WAIT_MILLISECONDS);
            return true;
        }

    #if UNITY_EDITOR
        void OnEditorQuitting()
        {
            TriggerQuitFlush(0);
        }
    #endif

        /// <summary>
        /// 수동 로그를 큐에 넣는다.
        /// level·category·message·data·스택트레이스 캡처 여부를 사용하며, 인스턴스가 없으면 조용히 무시한다.
        /// </summary>
        private static void EnqueueManual(string level, string category, string message, object data, bool captureStackTrace)
        {
            GameLogger logger = _instance;
            if (logger == null)
                return;

            string dataJson = string.Empty;
            if (data != null)
            {
                try
                {
                    dataJson = data is string text ? text : JsonUtility.ToJson(data);
                }
                catch
                {
                    // 직렬화할 수 없는 data는 빈 값으로 둔다.
                    dataJson = string.Empty;
                }
            }

            logger.Enqueue(new LogEntry
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                level = level,
                category = string.IsNullOrEmpty(category) ? UNITY_CATEGORY : category,
                scene = logger.GetCurrentSceneName(),
                message = message ?? string.Empty,
                dataJson = dataJson,
                stackTrace = captureStackTrace ? Environment.StackTrace : string.Empty
            });
        }

        /// <summary>
        /// 로그 항목을 큐에 추가한다.
        /// entry를 사용하며, 세션당 기록량 상한은 없다. 종료 플러시 이후 항목은 버린다.
        /// </summary>
        private void Enqueue(LogEntry entry)
        {
            if (_quitFlushTriggered)
                return;

            lock (_queueLock)
            {
                _queue.Enqueue(entry);
            }
        }

        /// <summary>큐에 대기 중인 항목이 있는지 확인한다. 워커 종료 조건 판단에 쓴다.</summary>
        private bool HasQueuedEntries()
        {
            lock (_queueLock)
            {
                return _queue.Count > 0;
            }
        }

        /// <summary>
        /// 큐의 모든 항목을 꺼내 목록으로 반환한다.
        /// 큐 비움 결과를 반환하며, 비어 있으면 빈 목록을 반환한다.
        /// </summary>
        private List<LogEntry> DrainQueue()
        {
            lock (_queueLock)
            {
                List<LogEntry> drained = new List<LogEntry>(_queue.Count);
                while (_queue.Count > 0)
                    drained.Add(_queue.Dequeue());
                return drained;
            }
        }

        /// <summary>
        /// 큐의 항목과 지표 스냄샷을 세션 파일에 기록한다.
        /// 메인 스레드 종료 경로의 마지막 비움에 쓰며, 평소에는 워커 스레드가 같은 경로로 기록한다.
        /// </summary>
        private void FlushQueue()
        {
            AppendBatchSafely(DrainQueue());
        }

        /// <summary>
        /// 항목 배치와 최신 지표 스냄샷을 파일에 기록한다.
        /// 배치와 지표가 모두 비면 아무것도 하지 않고, 기록 실패는 삼켜 호출자를 멈추지 않는다.
        /// 워커 스레드와 종료 경로가 함께 부르므로 파일 접근은 상태 락과 작성기 내부 락으로 직렬화한다.
        /// </summary>
        private void AppendBatchSafely(List<LogEntry> batch)
        {
            try
            {
                MetricSample[] metrics = SnapshotMetrics();
                if (batch.Count == 0 && metrics.Length == 0)
                    return;

                lock (_stateLock)
                {
                    EnsureSessionFile();
                    if (batch.Count > 0)
                        _writer.AppendEntries(batch);
                    if (metrics.Length > 0)
                        _writer.WriteMetrics(metrics);
                }
            }
            catch
            {
                // 파일 기록 실패는 삼켜서 게임과 워커를 멈추지 않는다.
            }
        }

        /// <summary>
        /// 파일 기록 워커 루프를 돈다.
        /// 신호가 오면 큐를 비워 파일에 기록하고, 종료 요청 후 큐가 비면 스레드를 끝낸다.
        /// </summary>
        private void WriterLoop()
        {
            while (true)
            {
                _flushSignal.WaitOne(WRITER_SIGNAL_POLL_MILLISECONDS);
                List<LogEntry> batch = DrainQueue();
                if (batch.Count > 0)
                    AppendBatchSafely(batch);

                if (_stopWriter && !HasQueuedEntries())
                    break;
            }
        }

        /// <summary>
        /// 파일 기록 워커를 멈춘다.
        /// 대기 시간을 입력으로 사용하며, 종료 요청 후 큐가 빌 때까지 최대 그 시간만 기다린다.
        /// </summary>
        private void StopWriter(int joinMilliseconds)
        {
            Thread writerThread = _writerThread;
            if (writerThread == null)
                return;

            _stopWriter = true;
            _flushSignal?.Set();
            try
            {
                writerThread.Join(joinMilliseconds);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 주기 전송 워커 루프를 돈다.
        /// 전송 주기마다 현재 세션 파일에서 아직 보내지 않은 줄을 서버로 보낸다. 실패하면 다음 주기에 이어서 보낸다.
        /// 서버가 내용을 거부하면 이 세션의 주기 전송을 멈추고, 마무리는 종료 경로가 맡는다.
        /// </summary>
        private void ShipperLoop()
        {
            while (!_stopShipper)
            {
                float interval = _uploadIntervalSeconds;
                _shipSignal.WaitOne(interval > 0f ? (int)(interval * 1000f) : WRITER_SIGNAL_POLL_MILLISECONDS * 5);
                if (_stopShipper)
                    break;
                if (interval <= 0f)
                    continue;

                try
                {
                    if (_shipper.Ship(_uploader, false, false) == ShipResult.Rejected)
                        break;
                }
                catch
                {
                    // 전송 실패는 다음 주기에 다시 시도한다.
                }
            }
        }

        /// <summary>
        /// 주기 전송 워커를 멈춘다. 전송 중이면 기다리지 않는다.
        /// 마무리 전송은 같은 LogShipper의 잠금을 잡으므로 진행 중인 주기 전송과 겹치지 않는다.
        /// </summary>
        private void StopShipper()
        {
            _stopShipper = true;
            _shipSignal?.Set();
        }

        /// <summary>
        /// 누적된 지표를 스냄샷 배열로 복사한다.
        /// 입력값은 없으며, 워커·전송 스레드에서 안전하게 부를 수 있는 샘플 배열을 반환한다.
        /// </summary>
        private MetricSample[] SnapshotMetrics()
        {
            if (_metrics.IsEmpty)
                return new MetricSample[0];

            List<MetricSample> samples = new List<MetricSample>(_metrics.Count);
            foreach (KeyValuePair<string, MetricAccumulator> pair in _metrics)
            {
                MetricAccumulator accumulator = pair.Value;
                MetricSample sample = new MetricSample();
                lock (accumulator)
                {
                    sample.key = pair.Key;
                    sample.count = Interlocked.Read(ref accumulator.count);
                    sample.sum = accumulator.sum;
                }
                samples.Add(sample);
            }
            return samples.ToArray();
        }

        /// <summary>
        /// 세션 파일 작성기를 준비한다.
        /// 최초 호출에서 초기화 때 만든 세션 헤더로 로그 디렉터리에 파일을 생성한다.
        /// 워커 스레드에서도 불리므로 Unity API를 직접 호출하지 않는다. 상태 락 안에서 호출한다.
        /// </summary>
        private void EnsureSessionFile()
        {
            if (_writer != null)
                return;

            Directory.CreateDirectory(_logDirectory);
            _writer = new LogFileWriter(_logDirectory, _sessionId);
            _writer.WriteSession(_sessionHeader);
        }

        /// <summary>
        /// 종료 경로에서 워커를 마무리하고 현재 세션의 남은 로그를 마무리 전송한다.
        /// 전송 완료 대기 시간을 사용하며, 중복 호출은 한 번만 수행한다.
        /// </summary>
        private void TriggerQuitFlush(int waitMilliseconds)
        {
            if (_quitFlushTriggered)
                return;
            _quitFlushTriggered = true;

            StopWriter(QUIT_WRITER_JOIN_MILLISECONDS);
            FlushQueue();
            StopShipper();
            if (!_uploadOnQuit || _writer == null || _shipper == null)
                return;

            StartUploadThread(new List<LogShipper> { _shipper }, true, waitMilliseconds);
        }

        /// <summary>
        /// 이전 세션이 다 보내지 못한 파일(크래시·전송 실패)을 이어서 보내고 정리한다.
        /// 보존 기간이 지난 파일은 먼저 삭제한다.
        /// </summary>
        private void RetryPendingUploads()
        {
            if (!_retryPendingOnStartup)
                return;

            LogFileWriter.PruneExpiredFiles(_logDirectory, PENDING_RETENTION_DAYS);
            List<LogShipper> shippers = new List<LogShipper>();
            foreach (string path in LogFileWriter.GetUploadableFilePaths(_logDirectory, _sessionId))
                shippers.Add(new LogShipper(path));
            StartUploadThread(shippers, false, 0);
        }

        /// <summary>
        /// 세션 파일들의 마무리 전송을 백그라운드 스레드에서 수행한다.
        /// 정상 종료 여부를 사용하며, 대기 시간이 0보다 크면 그 시간만큼 종료를 늦춘다.
        /// </summary>
        private void StartUploadThread(List<LogShipper> shippers, bool endedCleanly, int waitMilliseconds)
        {
            if (shippers.Count == 0)
                return;

            LogUploader uploader = _uploader;
            if (uploader == null)
                return;

            // 종료 중에도 전송이 끝나도록 포그라운드 스레드로 실행한다.
            Thread thread = new Thread(() => FinishSessions(uploader, shippers, endedCleanly));
            thread.IsBackground = false;
            thread.Start();

            if (waitMilliseconds > 0)
                thread.Join(waitMilliseconds);
        }

        /// <summary>
        /// 세션 파일마다 남은 로그를 모두 보내고 결과에 따라 정리한다.
        /// 다 보냈으면 파일을 지우고, 서버가 거부했거나 읽을 수 없으면 corrupt로 격리하며,
        /// 통신이 실패하면 파일과 진행 위치를 남겨 다음 기동에 이어서 보낸다.
        /// </summary>
        private static void FinishSessions(LogUploader uploader, List<LogShipper> shippers, bool endedCleanly)
        {
            foreach (LogShipper shipper in shippers)
            {
                try
                {
                    switch (shipper.Ship(uploader, true, endedCleanly))
                    {
                        case ShipResult.Sent:
                            shipper.DeleteFiles();
                            break;
                        case ShipResult.Rejected:
                            shipper.Quarantine();
                            break;
                    }
                }
                catch
                {
                    // 남은 파일은 다음 기동에 다시 시도된다.
                }
            }
        }

        /// <summary>
        /// 빌드 유형에 따라 서버 주소를 고른다.
        /// 에디터와 개발 빌드는 개발 주소를, 릴리스 빌드는 릴리스 주소를 반환하며 빈 값은 기본 주소로 대체한다.
        /// </summary>
        private string ResolveServerUrl()
        {
            // 기본값을 릴리스 주소로 두고 개발 문맥에서만 덮어써 두 필드 모두 항상 사용된다.
            string url = string.IsNullOrWhiteSpace(_releaseServerUrl) ? DEFAULT_SERVER_URL : _releaseServerUrl;
    #if UNITY_EDITOR || DEVELOPMENT_BUILD
            url = string.IsNullOrWhiteSpace(_developmentServerUrl) ? DEFAULT_SERVER_URL : _developmentServerUrl;
    #endif
            return url;
        }

        /// <summary>
        /// 현재 씬 이름을 반환한다.
        /// 워커 스레드에서도 안전하도록 Update에서 갱신한 캐시 값을 사용한다.
        /// </summary>
        private string GetCurrentSceneName()
        {
            return _currentSceneName;
        }

        /// <summary>
        /// Unity LogType을 로그 수준 문자열로 바꾼다.
        /// type을 사용하며, 대응되는 수준 문자열을 반환한다.
        /// </summary>
        private static string MapLevel(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return LEVEL_WARNING;
                case LogType.Error:
                    return LEVEL_ERROR;
                case LogType.Assert:
                    return "Assert";
                case LogType.Exception:
                    return "Exception";
                default:
                    return LEVEL_INFO;
            }
        }

        /// <summary>
        /// 세션 ID를 만든다.
        /// GUID 앞 8자와 UTC 시작 시각을 조합해 반환한다.
        /// </summary>
        private static string CreateSessionId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        }
    }
}
