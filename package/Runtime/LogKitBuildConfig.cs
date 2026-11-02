using System;
using UnityEngine;

namespace LogKit
{
    /// <summary>
    /// 빌드 때 LogKit 빌드 단계가 Resources에 넣는 설정. 파일 이름과 필드 이름은 빌드 단계와 같아야 한다.
    /// 빌드 단계를 거치지 않은 실행(에디터 실행 등)에서는 없으며, 그때는 Inspector와 Configure 값을 쓴다.
    /// </summary>
    [Serializable]
    internal sealed class LogKitBuildConfig
    {
        public const string RESOURCE_NAME = "LogKitBuildConfig";

        public string serverUrl;
        public string uploadKey;

        /// <summary>
        /// Resources에서 설정을 읽는다. 없거나 읽을 수 없으면 null을 반환한다.
        /// 게임 시작 때 한 번 호출하며, 예외는 삼켜서 로깅을 멈추지 않는다.
        /// </summary>
        public static LogKitBuildConfig Load()
        {
            try
            {
                TextAsset asset = Resources.Load<TextAsset>(RESOURCE_NAME);
                return asset == null ? null : JsonUtility.FromJson<LogKitBuildConfig>(asset.text);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
