"""OSM 데이터 받기와 좌표 변환."""
import hashlib
import json
import math
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CACHE = ROOT / "run3d" / "data" / "raw" / "cache"
OVERPASS_SERVERS = [  # 공용 Overpass 서버들 (앞에서부터 시도)
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
]
USER_AGENT = "Capstone1-course-builder/0.1 (student project)"

SCENERY_FILTERS = """
  way["highway"]({bb});
  way["building"]({bb});
  way["natural"="water"]({bb});
  relation["natural"="water"]({bb});
  way["waterway"="riverbank"]({bb});
  relation["waterway"="riverbank"]({bb});
  way["leisure"~"^(park|garden)$"]({bb});
  way["landuse"~"^(grass|recreation_ground|forest|meadow|village_green)$"]({bb});
  way["natural"~"^(wood|scrub|grassland)$"]({bb});
  way["landuse"~"^(residential|commercial|retail)$"]({bb});
  way["amenity"~"^(parking|school|university|kindergarten)$"]({bb});
  way["leisure"~"^(pitch|playground|track)$"]({bb});
  way["barrier"~"^(fence|wall|retaining_wall|hedge|guard_rail)$"]({bb});
  node["highway"~"^(traffic_signals|crossing|bus_stop)$"]({bb});
  node["public_transport"="platform"]["bus"="yes"]({bb});
"""


def overpass(query):
    """Overpass API 요청. 같은 질의는 파일로 캐시해서 공용 서버에 다시 묻지 않는다.
    공용 서버가 바쁘면(429/504/시간 초과) 잠시 쉬었다가 다른 서버(미러)로 다시 시도한다."""
    CACHE.mkdir(parents=True, exist_ok=True)
    path = CACHE / f"{hashlib.sha1(query.encode()).hexdigest()[:16]}.json"
    if path.exists():
        return json.loads(path.read_text(encoding="utf-8"))["elements"]
    data = urllib.parse.urlencode({"data": query}).encode()
    errors = []
    for attempt, url in enumerate(OVERPASS_SERVERS * 2):
        try:
            req = urllib.request.Request(url, data=data, headers={"User-Agent": USER_AGENT})
            raw = urllib.request.urlopen(req, timeout=120).read()
            elements = json.loads(raw)["elements"]
            path.write_bytes(raw)
            return elements
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as e:
            errors.append(f"{url.split('/')[2]}: {getattr(e, 'code', '') or type(e).__name__}")
            time.sleep(5 * (attempt + 1))
    raise RuntimeError("OSM 서버가 바빠서 데이터를 받지 못했어요. 잠시 후 다시 시도하세요 (" + ", ".join(errors) + ")")


def bbox_around(lat, lon, radius_m):
    dlat = radius_m / 111_000
    dlon = radius_m / (111_000 * math.cos(math.radians(lat)))
    return (lat - dlat, lon - dlon, lat + dlat, lon + dlon)


def bbox_str(bbox):
    return ",".join(f"{v:.5f}" for v in bbox)


def fetch_network(lat, lon, radius_m):
    """경로 찾기용: 반경 안의 모든 길(highway)."""
    bb = bbox_str(bbox_around(lat, lon, radius_m))
    return overpass(f'[out:json][timeout:180];way["highway"]({bb});out geom;')


def fetch_scenery(bbox):
    """3D 지도용: 길, 건물, 물, 공원 등."""
    bb = bbox_str(bbox)
    return overpass(f"[out:json][timeout:180];({SCENERY_FILTERS.format(bb=bb)});out geom;")


class Projector:
    """위경도 <-> 출발점 기준 미터 좌표 (x=동쪽, y=북쪽). 수 km 범위에서는 평면 근사로 충분."""

    def __init__(self, lat0, lon0):
        self.lat0, self.lon0 = lat0, lon0
        self.kx = math.cos(math.radians(lat0)) * 6371000 * math.pi / 180
        self.ky = 6371000 * math.pi / 180

    def xy(self, lat, lon):
        return ((lon - self.lon0) * self.kx, (lat - self.lat0) * self.ky)

    def latlon(self, x, y):
        return (self.lat0 + y / self.ky, self.lon0 + x / self.kx)

    def geom(self, pts):
        return [self.xy(p["lat"], p["lon"]) for p in pts]
