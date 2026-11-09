using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Scrun.App
{
    public sealed class HttpRequest
    {
        public string Method;
        public string Url;
        public string Body; // JSON, 없으면 null
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
        public int TimeoutSeconds = ApiConfig.TimeoutSeconds;
    }

    public sealed class HttpResponse
    {
        public int Status;          // 0이면 서버에 못 닿음
        public string Body;         // 응답 본문(없으면 null)
        public string NetworkError; // 연결 안 됨·시간 초과면 그 설명, 아니면 null
    }

    // 실제 HTTP 보내기. ApiClient는 이 모양만 알아서 테스트에서는 가짜로 바꿔 끼운다.
    public interface IHttpTransport
    {
        Task<HttpResponse> SendAsync(HttpRequest request);
    }

    public sealed class UnityHttpTransport : IHttpTransport
    {
        public async Task<HttpResponse> SendAsync(HttpRequest r)
        {
            using var req = new UnityWebRequest(r.Url, r.Method) { downloadHandler = new DownloadHandlerBuffer() };
            if (r.Body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(r.Body));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.SetRequestHeader("Accept", "application/json");
            foreach (var h in r.Headers) req.SetRequestHeader(h.Key, h.Value);
            req.timeout = r.TimeoutSeconds;

            await Awaitable.FromAsyncOperation(req.SendWebRequest());

            // 연결 안 됨·시간 초과, 받는 도중 끊김 → 네트워크 실패(화면은 "다시 시도")
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
                return new HttpResponse { Status = 0, NetworkError = req.error };
            return new HttpResponse { Status = (int)req.responseCode, Body = req.downloadHandler?.text };
        }
    }
}
