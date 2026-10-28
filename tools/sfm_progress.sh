#!/bin/sh
# 3D 복원 진행 상황 한 줄: 완료 구간 수 / 44, 작업별 현재 구간의 단계
cd "$(dirname "$0")/.." || exit 1
done_n=$(ls run3d/data/raw/sfm/chunk*.npz 2>/dev/null | wc -l)
line="$(date +%H:%M) 완료 $done_n/44"
for w in 0 1; do
  L=run3d/data/raw/sfm/logs/worker$w.log
  [ -f "$L" ] || continue
  # 마지막으로 끝난 구간 이후의 기록만 본다
  start=$(grep -n "^구간" "$L" | tail -1 | cut -d: -f1)
  tail -n +"${start:-1}" "$L" > /tmp/sfm_w$w.txt 2>/dev/null || cp "$L" /tmp/sfm_w$w.txt
  reg=$(grep -o "num_reg_frames=[0-9]*" /tmp/sfm_w$w.txt | tail -1 | cut -d= -f2)
  ext=$(grep -c "Processed file" /tmp/sfm_w$w.txt)
  if [ -n "$reg" ]; then stage="3D 조립 $reg/240"
  elif [ "$ext" -ge 240 ]; then stage="짝짓기 중"
  elif [ "$ext" -gt 0 ]; then stage="특징점 $ext/240"
  else stage="준비 중"; fi
  line="$line | 작업$((w + 1)): $stage"
done
echo "$line"
