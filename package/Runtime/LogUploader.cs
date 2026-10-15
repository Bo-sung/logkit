using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;

using UnityEngine;

namespace LogKit
{

    /// <summary>배치 한 건의 전송 결과.</summary>
    public enum UploadResult
    {
        /// <summary>서버가 2xx로 받았다.</summary>
        Success,
        /// <summary>통신 실패·타임아웃·인증 오류·서버 오류처럼 나중에 다시 보내면 성공할 수 있는 실패.</summary>
        Failed,
        /// <summary>서버가 내용 자체를 거부했다(400 잘못된 본문, 413 본문 초과). 같은 내용을 다시 보내도 실패한다.</summary>
        Rejected
    }

    /// <summary>
    /// 로그 배치를 gzip으로 압축해 수신 서버로 전송하는 전송기다.
    /// 서버 주소, API 키, 타임아웃을 사용하며, 응답 상태에 따른 전송 결과를 반환한다.
    /// 백그라운드 스레드에서 호출되므로 Unity API 중 스레드에 안전한 JsonUtility만 사용한다.
    /// </summary>
    public class LogUploader
    {
        private const int MIN_SUCCESS_STATUS_CODE = 200;
        private const int MAX_SUCCESS_STATUS_CODE = 299;
        private const int PAYLOAD_TOO_LARGE_STATUS_CODE = 413;

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
        /// batch를 사용하며, 2xx면 Success, 400·413이면 Rejected, 그 밖의 실패는 Failed를 반환한다.
        /// </summary>
        public UploadResult Upload(UploadBatch batch)
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
                    return Classify((int)response.StatusCode);
                }
            }
            catch (WebException exception) when (exception.Response is HttpWebResponse errorResponse)
            {
                // HttpWebRequest는 2xx가 아닌 응답을 예외로 던지므로 상태 코드를 여기서 꺼낸다.
                using (errorResponse)
                {
                    return Classify((int)errorResponse.StatusCode);
                }
            }
            catch
            {
                return UploadResult.Failed;
            }
        }

        /// <summary>HTTP 상태 코드를 전송 결과로 바꾼다.</summary>
        private static UploadResult Classify(int statusCode)
        {
            if (statusCode >= MIN_SUCCESS_STATUS_CODE && statusCode <= MAX_SUCCESS_STATUS_CODE)
                return UploadResult.Success;
            if (statusCode == (int)HttpStatusCode.BadRequest || statusCode == PAYLOAD_TOO_LARGE_STATUS_CODE)
                return UploadResult.Rejected;
            return UploadResult.Failed;
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
