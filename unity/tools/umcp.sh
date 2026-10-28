#!/bin/bash
# usage: umcp.sh <method> '<params-json>'
SID_FILE="${TEMP:-/tmp}/umcp.sid"
URL=http://127.0.0.1:8080/mcp
init() {
  H=$(mktemp)
  curl -s -D "$H" -X POST $URL -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
    -d '{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}' >/dev/null
  grep -i '^mcp-session-id' "$H" | awk '{print $2}' | tr -d '\r' > "$SID_FILE"
  curl -s -X POST $URL -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -H "mcp-session-id: $(cat $SID_FILE)" \
    -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' >/dev/null
}
[ -s "$SID_FILE" ] || init
curl -s -X POST $URL -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -H "mcp-session-id: $(cat $SID_FILE)" \
  -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"$1\",\"params\":${2:-{\}}}" | sed -n 's/^data: //p'
