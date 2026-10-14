using System;

namespace LogKit
{

    /// <summary>
    /// 로그 한 항목을 표현하는 직렬화 모델이다.
    /// Unity JsonUtility는 public 필드만 직렬화하므로 서버 JSON 계약과 같은 camelCase 필드로 선언한다.
    /// </summary>
    [Serializable]
    public class LogEntry
    {
        public string timestampUtc = "";
        public string level = "";
        public string category = "";
        public string scene = "";
        public string message = "";
        public string dataJson = "";
        public string stackTrace = "";
    }

    /// <summary>
    /// 세션의 메타데이터를 표현하는 직렬화 모델이다.
    /// 세션 파일의 첫 줄에 기록되며, 크래시 후 재전송 때 배치 조립에 사용된다.
    /// </summary>
    [Serializable]
    public class LogSessionHeader
    {
        public string sessionId = "";
        public string startedAtUtc = "";
        public string appVersion = "";
        public string unityVersion = "";
        public bool isEditor;
        public string platform = "";
    }

    /// <summary>
    /// 서버로 전송할 업로드 봉투를 표현하는 직렬화 모델이다.
    /// 세션 헤더 필드를 평면으로 펼쳐 담고, 로그를 파트 번호로 나눠 여러 번 전송할 수 있다.
    /// metrics에는 세션 동안 집계한 플레이 지표가 들어가며, 같은 세션이면 파트와 무관하게 같은 값을 보낸다.
    /// </summary>
    [Serializable]
    public class UploadBatch
    {
        public string sessionId = "";
        public string startedAtUtc = "";
        public string endedAtUtc = "";
        public string appVersion = "";
        public string unityVersion = "";
        public bool isEditor;
        public string platform = "";
        public bool endedCleanly;
        public bool truncated;

        /// <summary>0부터 시작하는 배치 파트 번호. 서버는 파트별로 저장해 멱등적으로 병합한다.</summary>
        public int partIndex;

        /// <summary>세션 동안 집계한 플레이 지표. 비어 있으면 서버의 기존 지표를 유지한다.</summary>
        public MetricSample[] metrics = new MetricSample[0];
        public LogEntry[] entries = new LogEntry[0];
    }

    /// <summary>
    /// 플레이 지표 한 샘플을 표현하는 직렬화 모델이다.
    /// Count 호출은 count만 늘리고, Measure 호출은 count와 sum을 함께 늘려 평균을 낼 수 있게 한다.
    /// </summary>
    [Serializable]
    public class MetricSample
    {
        public string key = "";
        public long count;
        public double sum = 0.0;
    }

    /// <summary>
    /// 지표 파일 직렬화 봉투. Unity JsonUtility는 최상위 배열을 직렬화하지 못하므로 객체로 감싼다.
    /// </summary>
    [Serializable]
    public class MetricSnapshot
    {
        public MetricSample[] metrics = new MetricSample[0];
    }
}
