using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LogKit.EditorTools
{
    /// <summary>
    /// 빌드 계약: 빌드 환경변수 LOGKIT_SERVER_URL(로그 서버 주소)과 LOGKIT_UPLOAD_KEY(프로젝트 업로드 키)를 읽어
    /// Assets/Resources/LogKitBuildConfig.json을 만들고, 빌드가 끝나면 지운다. 프로젝트마다 빌드 단계를 만들 필요가 없다.
    /// 값이 없으면 빌드를 실패시킨다. 로컬에서 빌드할 때만 LOGKIT_OPTIONAL=1로 건너뛸 수 있다.
    /// 로컬 주소(localhost)는 LOGKIT_ALLOW_LOCALHOST=1일 때만 허용한다.
    /// </summary>
    internal sealed class LogKitBuildStep : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string ServerUrlVariable = "LOGKIT_SERVER_URL";
        public const string UploadKeyVariable = "LOGKIT_UPLOAD_KEY";
        public const string OptionalVariable = "LOGKIT_OPTIONAL";
        public const string AllowLocalhostVariable = "LOGKIT_ALLOW_LOCALHOST";

        private const string FileName = "LogKitBuildConfig.json";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string url = (Environment.GetEnvironmentVariable(ServerUrlVariable) ?? string.Empty).Trim();
            string key = (Environment.GetEnvironmentVariable(UploadKeyVariable) ?? string.Empty).Trim();

            if (url.Length == 0 || key.Length == 0)
            {
                if (Environment.GetEnvironmentVariable(OptionalVariable) == "1")
                {
                    Debug.LogWarning("[LogKit] " + ServerUrlVariable + " 또는 " + UploadKeyVariable + "이 없어 빌드 설정을 넣지 않습니다(LOGKIT_OPTIONAL=1).");
                    return;
                }

                throw new BuildFailedException(
                    "[LogKit] 빌드 환경변수가 없습니다: " + (url.Length == 0 ? ServerUrlVariable + " " : string.Empty) + (key.Length == 0 ? UploadKeyVariable : string.Empty)
                    + ". CI 빌드 변수에 넣거나, 로컬 빌드라면 " + OptionalVariable + "=1을 설정하세요.");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new BuildFailedException("[LogKit] " + ServerUrlVariable + " 형식이 잘못되었습니다: " + url);

            bool local = uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "::1";
            if (local && Environment.GetEnvironmentVariable(AllowLocalhostVariable) != "1")
                throw new BuildFailedException("[LogKit] " + ServerUrlVariable + "이 localhost입니다. 다른 PC에서 실행될 빌드에 내 PC 주소가 들어갑니다. 로컬 테스트라면 " + AllowLocalhostVariable + "=1을 설정하세요.");

            string directory = Path.Combine(Application.dataPath, "Resources");
            Directory.CreateDirectory(directory);
            string json = JsonUtility.ToJson(new BuildConfigFile { serverUrl = url, uploadKey = key });
            File.WriteAllText(Path.Combine(directory, FileName), json, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log("[LogKit] 빌드 설정을 넣었습니다: 서버 " + uri.Host + ":" + uri.Port + ", 업로드 키 " + key.Length + "자");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            string path = Path.Combine(Application.dataPath, "Resources", FileName);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
            AssetDatabase.Refresh();
        }

        [Serializable]
        private sealed class BuildConfigFile
        {
            public string serverUrl;
            public string uploadKey;
        }
    }
}
