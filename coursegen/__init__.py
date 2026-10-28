"""오픈스트리트맵 데이터로 3D 러닝 코스(course.json)를 만드는 패키지.

  osm.py      OSM 데이터 받기(캐시), 위경도 <-> 미터 좌표
  routing.py  길 그래프, 두 점 사이 경로, 원하는 거리의 순환·왕복 코스 자동 생성
  scene.py    코스 주변의 강·공원·도로·건물·나무를 3D 화면용 데이터로 정리
  build.py    출발점 + 거리 -> course.json (서버와 명령줄에서 사용)

지도 데이터 © OpenStreetMap contributors (ODbL) — 화면에 출처 표기 필수.
"""
