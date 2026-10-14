using System;
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
        private const string MARKER_CATEGORY = "Logging";
        private const string LEVEL_INFO = "Info";
        private const string LEVEL_WARNING = "Warning";
        private const string LEVEL_ERROR = "Error";
        private const int MAX_BATCH_ENTRIES = 20000;
        private const int PENDING_RETENTION_DAYS = 30;
        private const int QUIT_WAIT_MILLISECONDS = 3000;
        private const int FALLBACK_WAIT_MILLISECONDS = 1000;

        private static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;

        [Header("서버 연결")]
        // 개발 기본값은 로그 서버 호스트 PC의 수신 주소다. 상황에 따라 Inspector에서 덮어쓴다.
        [SerializeField] private string _developmentServerUrl = DEFAULT_SERVER_URL;
        [SerializeField] private string _releaseServerUrl = DEFAULT_SERVER_URL;
        [SerializeField] private string _apiKey = DEFAULT_API_KEY;

        [Header("수집 설정")]
        [SerializeField] private bool _captureUnityLogs = true;
        [SerializeField] private int _maxEntriesPerSession = MAX_BATCH_ENTRIES;
        [SerializeField] private float _flushIntervalSeconds = 5f;

        [Header("전송 설정")]
        [SerializeField] private bool _uploadOnQuit = true;
        [SerializeField] private bool _retryPendingOnStartup = true;
        [SerializeField] private int _requestTimeoutSeconds = 5;

        private static GameLogger _instance;
        private static bool _quitting;

        private readonly object _queueLock = new object();
        private readonly Queue<LogEntry> _queue = new Queue<LogEntry>();
        private LogUploader _uploader;
        private LogFileWriter _writer;
        private string _logDirectory;
        private string _sessionId = string.Empty;
        private string _sessionStartedAtUtc = string.Empty;
        private string _currentSceneName = string.Empty;
        private bool _truncationMarkQueued;
        private bool _quitFlushTriggered;
        private bool _bootstrapped;
        private float _flushTimer;

        /// <summary>
        /// 로그 시스템을 즉시 초기화한다.
        /// 게임 기동 경로에서 호출하며, 초기화 실패는 조용히 무시해 게임을 중단시키지 않는다.
        /// </summary>
        public static void Init()
        {
            EnsureInstance();
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
            FlushQueue();
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
        /// 세션 ID, 로그 디렉터리, 전송기를 준비하며, 기동 재전송을 시작한다. 중복 호출은 무시한다.
        /// </summary>
        private void InitializeLogger()
        {
            if (_bootstrapped)
                return;
            _bootstrapped = true;

            _sessionId = CreateSessionId();
            _sessionStartedAtUtc = DateTime.UtcNow.ToString("o");
            _logDirectory = Path.Combine(Application.persistentDataPath, LOG_DIRECTORY_NAME);
            _uploader = new LogUploader(ResolveServerUrl(), _apiKey, _requestTimeoutSeconds);

            Scene activeScene = SceneManager.GetActiveScene();
            _currentSceneName = activeScene.IsValid() ? activeScene.name : string.Empty;

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
        /// entry를 사용하며, 세션 상한에 도달하면 이후 항목을 버리고 한 번만 마커를 남긴다.
        /// </summary>
        private void Enqueue(LogEntry entry)
        {
            if (_quitFlushTriggered)
                return;

            lock (_queueLock)
            {
                if (_queue.Count >= _maxEntriesPerSession)
                {
                    // 상한 도달 후에는 항목을 버리고 대신 마커를 한 번 남긴다.
                    if (_truncationMarkQueued)
                        return;
                    _truncationMarkQueued = true;
                    _queue.Enqueue(CreateTruncationMarker());
                    return;
                }

                _queue.Enqueue(entry);
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
        /// 큐의 항목을 세션 파일에 기록한다.
        /// 큐를 비우며, 세션 파일이 없으면 헤더와 함께 새로 생성한다.
        /// </summary>
        private void FlushQueue()
        {
            List<LogEntry> drained = DrainQueue();
            if (drained.Count == 0)
                return;

            EnsureSessionFile();
            _writer.AppendEntries(drained);
        }

        /// <summary>
        /// 세션 파일 작성기를 준비한다.
        /// 최초 호출에서 세션 헤더를 만들고 로그 디렉터리에 파일을 생성한다.
        /// </summary>
        private void EnsureSessionFile()
        {
            if (_writer != null)
                return;

            LogSessionHeader header = new LogSessionHeader
            {
                sessionId = _sessionId,
                startedAtUtc = _sessionStartedAtUtc,
                appVersion = Application.version,
                unityVersion = Application.unityVersion,
                isEditor = Application.isEditor,
                platform = Application.platform.ToString()
            };

            Directory.CreateDirectory(_logDirectory);
            _writer = new LogFileWriter(_logDirectory, _sessionId);
            _writer.WriteSession(header);
        }

        /// <summary>
        /// 종료 경로에서 큐를 비우고 현재 세션 파일 전송을 시작한다.
        /// 전송 완료 대기 시간을 사용하며, 중복 호출은 한 번만 수행한다.
        /// </summary>
        private void TriggerQuitFlush(int waitMilliseconds)
        {
            if (_quitFlushTriggered)
                return;
            _quitFlushTriggered = true;

            FlushQueue();
            if (!_uploadOnQuit || _writer == null)
                return;

            List<string> paths = new List<string> { _writer.CurrentFilePath };
            StartUploadThread(paths, true, waitMilliseconds);
        }

        /// <summary>
        /// 이전 세션의 남은 파일과 pending 파일을 다시 전송한다.
        /// 보존 기간이 지난 pending 파일은 먼저 삭제한다.
        /// </summary>
        private void RetryPendingUploads()
        {
            if (!_retryPendingOnStartup)
                return;

            LogFileWriter.PruneExpiredFiles(_logDirectory, PENDING_RETENTION_DAYS);
            List<string> paths = LogFileWriter.GetUploadableFilePaths(_logDirectory, _sessionId);
            StartUploadThread(paths, false, 0);
        }

        /// <summary>
        /// 파일 목록을 백그라운드 스레드에서 업로드한다.
        /// paths와 정상 종료 여부를 사용하며, 대기 시간이 0보다 크면 그 시간만큼 종료를 늦춘다.
        /// </summary>
        private void StartUploadThread(List<string> paths, bool endedCleanly, int waitMilliseconds)
        {
            if (paths.Count == 0)
                return;

            LogUploader uploader = _uploader;
            if (uploader == null)
                return;

            // 종료 중에도 전송이 끝나도록 포그라운드 스레드로 실행한다.
            Thread thread = new Thread(() => UploadFiles(uploader, paths, endedCleanly));
            thread.IsBackground = false;
            thread.Start();

            if (waitMilliseconds > 0)
                thread.Join(waitMilliseconds);
        }

        /// <summary>
        /// 파일들을 배치로 읽어 순서대로 전송한다.
        /// uploader와 paths, 정상 종료 여부를 사용하며, 성공 파일은 삭제하고 실패 파일은 pending으로 보존한다.
        /// </summary>
        private void UploadFiles(LogUploader uploader, List<string> paths, bool endedCleanly)
        {
            foreach (string path in paths)
            {
                if (!LogFileWriter.TryReadBatch(path, endedCleanly, MAX_BATCH_ENTRIES, out UploadBatch batch))
                {
                    LogFileWriter.MarkCorrupt(path);
                    continue;
                }

                if (uploader.Upload(batch))
                    LogFileWriter.TryDeleteFile(path);
                else
                    LogFileWriter.MarkPending(path);
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
        /// 캡처 상한 도달 사실을 알리는 마커 항목을 만든다.
        /// 현재 설정을 사용하며, 경고 수준의 마커 LogEntry를 반환한다.
        /// </summary>
        private LogEntry CreateTruncationMarker()
        {
            return new LogEntry
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                level = LEVEL_WARNING,
                category = MARKER_CATEGORY,
                scene = GetCurrentSceneName(),
                message = $"로그 캡처 상한 {_maxEntriesPerSession}건 도달. 이후 로그는 이 세션에 기록되지 않는다.",
                dataJson = string.Empty,
                stackTrace = string.Empty
            };
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
