import re
import sys
from pathlib import Path

d = Path(sys.argv[1])
m = (d / "mentor-report.html").read_text(encoding="utf-8")
style = re.search(r"<style>.*?</style>", m, re.S).group(0)
img = re.search(r'<img src="data:image/png;base64,[^"]+"[^>]*>', m).group(0)
links = re.search(r'(<link rel="preconnect".*?display=swap">)', m, re.S).group(1)

old = ".wrap { max-width: 860px; margin: 0 auto; padding-inline: 20px; padding-block: 40px 64px;"
new = (".wrap { max-width: 860px; margin: 0 auto; padding-left: max(16px, env(safe-area-inset-left, 0px)); "
       "padding-right: max(16px, env(safe-area-inset-right, 0px)); padding-block: 40px 64px;")
assert old in style
style = style.replace(old, new)
style = style.replace("</style>", """  .diagram { overflow-x: auto; background: var(--surface); border: 1px solid var(--line); border-radius: 8px; padding: 8px; }
  .diagram svg { display: block; min-width: 640px; width: 100%; height: auto; }
  .d-box { fill: var(--bg); stroke: var(--line); stroke-width: 1.5; }
  .d-main { stroke: var(--accent); stroke-width: 2; }
  .d-plan { stroke-dasharray: 6 5; }
  .d-title { fill: var(--fg); font: 700 15px var(--font-body); text-anchor: middle; }
  .d-text { fill: var(--fg); font: 400 13px var(--font-body); text-anchor: middle; }
  .muted-fill { fill: var(--muted); }
  .d-line { stroke: var(--muted); stroke-width: 1.6; fill: none; }
  .d-arrowhead { fill: var(--muted); }
</style>""")

head = '<title>스크런 미팅자료</title>\n<meta name="claude:viewport-fit-sides" content="cover">\n'
import base64


def jpg(name, alt):
    b64 = base64.b64encode((d / name).read_bytes()).decode()
    return f'<img src="data:image/jpeg;base64,{b64}" alt="{alt}">'


style = style.replace("</style>", """  .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }
  .cards.two { grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); }
  .card { background: var(--surface); border: 1px solid var(--line); border-radius: 8px; padding: 14px 16px; display: grid; gap: 6px; align-content: start; min-width: 0; }
  .card .n { font-family: var(--font-num); font-size: 12px; color: var(--accent); }
  .card h3 { margin: 0; font-size: 15px; }
  .card p { font-size: 14px; color: var(--muted); }
  .card .for { font-size: 12px; color: var(--muted); }
  .legend { display: flex; flex-wrap: wrap; gap: 8px; }
  .road { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: 12px; counter-reset: none; }
  .stage { background: var(--surface); border: 1px solid var(--line); border-top: 3px solid var(--accent); border-radius: 8px; padding: 12px 14px; display: grid; gap: 8px; align-content: start; min-width: 0; }
  .st-head { display: grid; gap: 2px; }
  .st-head .n { font-family: var(--font-num); font-size: 12px; color: var(--accent); }
  .st-head h3 { margin: 0; font-size: 15px; }
  .stage ul { list-style: none; padding: 0; margin: 0; display: grid; gap: 6px; font-size: 14px; }
  .stage ul li { display: grid; grid-template-columns: auto 1fr; gap: 6px; align-items: start; }
  .tk { display: inline-block; font-size: 11px; font-weight: 700; padding: 1px 7px; border-radius: 10px; white-space: nowrap; border: 1px solid currentColor; line-height: 1.6; }
  .t-speed { color: var(--accent); } .t-course { color: var(--good); } .t-together { color: var(--warn); }
  .stage ul li .tk { margin-top: 2px; }
  .done { font-size: 13px; color: var(--muted); border-top: 1px dashed var(--line); padding-top: 8px; }
  .done b { color: var(--fg); margin-right: 4px; }
  .shots { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 14px; }
</style>""")
body = ((d / "prof_body.html").read_text(encoding="utf-8")
        .replace("%%IMG%%", img)
        .replace("%%IMG_VIDEO%%", jpg("shot_video.jpg", "실사 영상 코스 화면: 해안길 영상 위에 달리는 아바타가 합성된 모습"))
        .replace("%%IMG_3D%%", jpg("shot_3dmap.jpg", "3D 맵 코스 화면: 나무가 늘어선 빨간 트랙을 아바타가 달리는 모습")))
(d / "professor-meeting.html").write_text(head + links + "\n" + style + "\n\n" + body, encoding="utf-8")
print("ok")
