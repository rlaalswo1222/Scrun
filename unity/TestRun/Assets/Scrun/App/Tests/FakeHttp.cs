using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Scrun.App.Tests
{
    // 테스트 공용 가짜 HTTP: 보낸 요청을 남기고, 답은 Handler(바로) 또는 AsyncHandler(나중에)로 정한다
    //   var http = new FakeHttp { Handler = _ => FakeHttp.Ok("{\"x\":1}") };
    sealed class FakeHttp : IHttpTransport
    {
        public readonly List<HttpRequest> Sent = new List<HttpRequest>();
        public Func<HttpRequest, HttpResponse> Handler;
        public Func<HttpRequest, Task<HttpResponse>> AsyncHandler;

        public Task<HttpResponse> SendAsync(HttpRequest r)
        {
            Sent.Add(r);
            return AsyncHandler != null ? AsyncHandler(r) : Task.FromResult(Handler(r));
        }

        public int Count(string path) => Sent.Count(r => r.Url.EndsWith(path));

        // 서버 공통 응답 모양(docs/api/README.md)
        public static HttpResponse Ok(string data) => new HttpResponse
        {
            Status = 200,
            Body = "{\"resultType\":\"SUCCESS\",\"success\":{\"data\":" + data + "},\"error\":null,\"meta\":{\"requestId\":\"req_ok\"}}"
        };
    }
}
