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
    /// 세션 헤더 필드를 평면으로 펼쳐 담고 세션 전체 로그 항목을 포함한다.
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
        public LogEntry[] entries = new LogEntry[0];
    }
}
