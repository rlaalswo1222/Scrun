using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scrun.App
{
    // C#과 네이티브(아이폰 Swift) 사이의 통로 한 줄. ADR-0009.
    //   C# → Swift: Call(요청 번호, 이름, JSON) — 답은 나중에 콜백으로. Query(이름, JSON) — 바로 답(상태 읽기·키체인).
    //   Swift → C#: 콜백(요청 번호, JSON). 요청 번호 0은 이벤트({"event": "...", ...}).
    // 콜백은 아무 스레드에서나 올 수 있어 큐에 넣기만 하고, 메인 스레드(Drain)에서 꺼내 Awaitable을 끝낸다.
    internal interface INativeChannel
    {
        void Call(int reqId, string name, string json);
        string Query(string name, string json);
    }

    // 네이티브가 실패라고 답했을 때(키체인 오류, 카카오 SDK 오류 등)
    public sealed class NativeException : Exception
    {
        public NativeException(string message) : base(message) { }
    }

    internal sealed class NativeBridge
    {
        readonly INativeChannel channel;
        readonly ConcurrentQueue<(int reqId, string json)> inbox = new ConcurrentQueue<(int, string)>();
        readonly Dictionary<int, AwaitableCompletionSource<JToken>> pending = new Dictionary<int, AwaitableCompletionSource<JToken>>();
        int nextReqId;

        // 이벤트 이름, 이벤트 JSON 전체. 메인 스레드에서 불린다.
        public event Action<string, JObject> Event;

        public NativeBridge(INativeChannel channel) => this.channel = channel;

        public int PendingCount => pending.Count;

        // 답이 오면 {"ok": true, "value": ...}의 value를 돌려준다. {"ok": false, "error": "..."}면 NativeException.
        public Awaitable<JToken> CallAsync(string name, object args = null)
        {
            int id = ++nextReqId;
            if (id <= 0) id = nextReqId = 1; // 21억 번 뒤 넘침(0은 이벤트 번호라 건너뜀)
            var src = new AwaitableCompletionSource<JToken>();
            pending[id] = src;
            try
            {
                channel.Call(id, name, ToJson(args));
            }
            catch
            {
                pending.Remove(id);
                throw;
            }
            return src.Awaitable;
        }

        public JToken Query(string name, object args = null)
        {
            var raw = channel.Query(name, ToJson(args));
            return Unwrap(Parse(raw), name);
        }

        // 네이티브 콜백이 부른다(아무 스레드).
        public void Post(int reqId, string json) => inbox.Enqueue((reqId, json));

        // 메인 스레드에서 매 프레임. 한 메시지 처리가 실패해도 나머지는 계속.
        public void Drain()
        {
            while (inbox.TryDequeue(out var m))
            {
                try
                {
                    Deliver(m.reqId, m.json);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        void Deliver(int reqId, string json)
        {
            if (reqId == 0)
            {
                var ev = Parse(json) as JObject;
                var name = (string)ev?["event"];
                if (string.IsNullOrEmpty(name)) throw new NativeException("이름 없는 네이티브 이벤트: " + json);
                Event?.Invoke(name, ev);
                return;
            }
            if (!pending.Remove(reqId, out var src))
            {
                Debug.LogWarning($"[Native] 기다리는 요청이 없는 답 {reqId}");
                return;
            }
            JToken value;
            try
            {
                value = Unwrap(Parse(json), "#" + reqId);
            }
            catch (Exception e)
            {
                src.SetException(e);
                return;
            }
            src.SetResult(value);
        }

        static string ToJson(object args) => args == null ? "{}" : JsonConvert.SerializeObject(args);

        static JToken Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new NativeException("네이티브가 빈 답을 줬습니다.");
            try
            {
                return JToken.Parse(json);
            }
            catch (JsonException e)
            {
                throw new NativeException("네이티브 답을 읽지 못했습니다: " + e.Message);
            }
        }

        static JToken Unwrap(JToken reply, string what)
        {
            if (reply is JObject o && o.TryGetValue("ok", out var ok) && ok.Type == JTokenType.Boolean)
            {
                if ((bool)ok) return o["value"] ?? JValue.CreateNull();
                throw new NativeException($"{what}: {(string)o["error"] ?? "알 수 없는 오류"}");
            }
            throw new NativeException($"{what}: 답 모양이 다릅니다: {reply}");
        }
    }
}
