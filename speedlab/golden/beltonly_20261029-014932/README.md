# 벨트만(사람은 옆 발판) — Swift 비교용(계획 0024)

10/29 M SpeedLab(갤럭시 S22 420 Hz) `data/speedlab/SpeedLab/20261029-014932` 95–172초를 100 Hz로 맞춰 아이폰 기록 모양(`phone/iphone_acc.csv`)으로 옮김.
106–126초 벨트 2 km/h, 140–161초 벨트 4 km/h(모터 진동이 222 spm·규칙성 0.64로 걸음처럼 보임). 에어팟·구간 파일 없음.
정답: `../replay_beltonly_20261029-014932.csv`. 벨트만 도는 동안 걸음 상태 0(걸음 수는 222로 나와도), 170초쯤부터 사람이 걸어 걷기(1)·125 spm. 만들기: `speedlab/exp/replay_live.py`
