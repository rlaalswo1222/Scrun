#!/bin/sh
# 앱 안의 SpeedEstimator.swift를 그대로 써서 재생 도구를 만들고 돌린다(계획 0024 2번). 사용법은 main.swift 머리 주석
set -e
here=$(cd "$(dirname "$0")" && pwd)
out="${TMPDIR:-/tmp}/speed-replay"
swiftc -O -o "$out" "$here/main.swift" "$here/../../unity/TestRun/Assets/Plugins/iOS/Scrun/SpeedEstimator.swift"
"$out" "$@"
