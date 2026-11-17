"""합성·데이터 눈으로 확인: 상자와 정답 글자를 그린 모음 그림. 사용: python preview.py <COCO 폴더> <출력.jpg> [장 수]"""
import json
import sys

import cv2
import numpy as np


def sheet(folder, out, n=24):
    j = json.load(open(f"{folder}/annotations.json"))
    names = {c["id"]: c["name"] for c in j["categories"]}
    tiles = []
    for im in j["images"][:n]:
        img = cv2.imread(f"{folder}/images/{im['file_name']}")
        for a in j["annotations"]:
            if a["image_id"] == im["id"]:
                x, y, w, h = map(int, a["bbox"])
                cv2.rectangle(img, (x, y), (x + w, y + h), (0, 255, 255), 1)
        img = cv2.resize(img, (400, 120))
        img = cv2.copyMakeBorder(img, 0, 22, 0, 0, cv2.BORDER_CONSTANT, value=(255, 255, 255))
        cv2.putText(img, im.get("text", ""), (4, 138), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 0), 1)
        tiles.append(img)
    while len(tiles) % 3:
        tiles.append(np.full_like(tiles[0], 255))
    cv2.imwrite(out, np.vstack([np.hstack(tiles[i:i + 3]) for i in range(0, len(tiles), 3)]))


if __name__ == "__main__":
    sheet(sys.argv[1], sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 24)
