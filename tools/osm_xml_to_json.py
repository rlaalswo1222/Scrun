"""OSM 공식 API(/api/0.6/map)로 받은 XML 조각들을 Overpass "out geom" 형식(JSON)으로 합친다.
Overpass 공용 서버가 장애일 때 대신 쓴다.

  python tools/osm_xml_to_json.py out.json tile1.osm tile2.osm ...
"""
import json
import sys
import xml.etree.ElementTree as ET


def main():
    out, files = sys.argv[1], sys.argv[2:]
    nodes, ways = {}, {}
    for f in files:
        root = ET.parse(f).getroot()
        for n in root.iter("node"):
            nodes[n.get("id")] = (float(n.get("lat")), float(n.get("lon")), {t.get("k"): t.get("v") for t in n.iter("tag")})
        for w in root.iter("way"):
            ways[w.get("id")] = ([nd.get("ref") for nd in w.iter("nd")], {t.get("k"): t.get("v") for t in w.iter("tag")})
    elements = []
    for nid, (lat, lon, tags) in nodes.items():
        if tags:
            elements.append({"type": "node", "id": int(nid), "lat": lat, "lon": lon, "tags": tags})
    for wid, (refs, tags) in ways.items():
        if not tags or any(r not in nodes for r in refs):
            continue  # 조각 경계에서 잘린 way는 다른 조각에 온전히 있다
        elements.append({"type": "way", "id": int(wid), "nodes": [int(r) for r in refs], "tags": tags,
                         "geometry": [{"lat": nodes[r][0], "lon": nodes[r][1]} for r in refs]})
    with open(out, "w", encoding="utf-8") as fp:
        json.dump({"elements": elements}, fp, ensure_ascii=False)
    print(f"nodes {len(nodes)}, ways {len(ways)} -> {len(elements)} elements")


if __name__ == "__main__":
    main()
