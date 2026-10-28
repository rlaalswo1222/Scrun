# 맵 테스트 (섬 원형 트랙)

GPT로 만든 섬 그림을 설계도로 삼아 Three.js로 직접 만든 원형 코스. 본 앱(run3d)과 따로 돈다.

- 실행: `..\.venv\Scripts\python -m http.server 8100 --bind 0.0.0.0` (이 폴더에서) → `http://localhost:8100`
- 폰: `http://<노트북 IP>:8100`
- 시점: 1인칭 / 3인칭 / 위에서(마우스로 돌려 보기), 뒤 보기. 키보드 1·2·3, B
- 주소 옵션: `?mode=fp|tp|top`, `?kmh=12`, `?back=1`, `?s=300`(출발 위치를 300m 앞으로)
- 한 바퀴 약 710m (직선 160m + 반원 반지름 62m), 6레인
- `assets/Xbot.glb`: 러너 모델 (run3d에서 복사)
