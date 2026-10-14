using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;

using UnityEngine;

namespace LogKit
{

    /// <summary>
    /// 로그 배치를 gzip으로 압축해 수신 서버로 전송하는 전송기다.
    /// 서버 주소, API 키, 타임아웃을 사용하며, HTTP 2xx 응답 여부를 반환한다.
    /// 백그라운드 스레드에서 호출되므로 Unity API 중 스레드에 안전한 JsonUtility만 사용한다.
    /// </summary>
    public class LogUploader
    {
        private const int MIN_SUCCESS_STATUS_CODE = 200;
        private const int MAX_SUCCESS_STATUS_CODE = 299;

        private readonly string _serverUrl;
        private readonly string _apiKey;
        private readonly int _timeoutMilliseconds;

        /// <summary>
        /// 전송기를 준비한다.
        /// 서버 주소와 API 키, 초 단위 타임아웃을 사용하며, 내부 타임아웃 값을 계산한다.
        /// </summary>
        public LogUploader(string serverUrl, string apiKey, int timeoutSeconds)
        {
            _serverUrl = serverUrl;
            _apiKey = apiKey;
            _timeoutMilliseconds = Math.Max(1, timeoutSeconds) * 1000;
        }

        /// <summary>
        /// 배치를 JSON으로 직렬화해 gzip 압축한 뒤 서버로 전송한다.
        /// batch를 사용하며, 서버가 2xx로 응답하면 true를 반환하고 통신 실패 시 false를 반환한다.
        /// </summary>
        public bool Upload(UploadBatch batch)
        {
            try
            {
                byte[] body = Compress(Encoding.UTF8.GetBytes(JsonUtility.ToJson(batch)));
                HttpWebRequest request = CreateRequest(body.Length);

                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(body, 0, body.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    int statusCode = (int)response.StatusCode;
                    return statusCode >= MIN_SUCCESS_STATUS_CODE && statusCode <= MAX_SUCCESS_STATUS_CODE;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 전송에 사용할 HTTP 요청을 만든다.
        /// 본문 길이를 사용하며, POST와 gzip·API 키 헤더, 타임아웃을 설정한다.
        /// </summary>
        private HttpWebRequest CreateRequest(int bodyLength)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_serverUrl);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("Content-Encoding", "gzip");
            request.Headers.Add("X-Api-Key", _apiKey);
            request.ContentLength = bodyLength;
            request.Timeout = _timeoutMilliseconds;
            return request;
        }

        /// <summary>
        /// 바이트 배열을 gzip으로 압축한다.
        /// 원본 배열을 사용하며, 압축된 새 배열을 반환한다.
        /// </summary>
        private static byte[] Compress(byte[] source)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                {
                    gzip.Write(source, 0, source.Length);
                }
                return output.ToArray();
            }
        }
    }
}
