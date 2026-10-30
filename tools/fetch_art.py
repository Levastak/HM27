#!/usr/bin/env python3
"""Пак логотипов и лиц для личной сборки (art.pak): эмблемы клубов НХЛ (SVG, светлые и тёмные) и фото игроков из базы.

Запускается в GitHub Actions (workflow «Пак логотипов и лиц»): отсюда сайт НХЛ закрыт. Формат пака:
  'HM27ART1' + uint32 длины индекса (little-endian) + индекс JSON {ключ: [смещение, длина]} + данные подряд.
Ключи: «logo:MIN», «logo_d:MIN» (тёмная версия — для светлого фона не нужна, для тёмного лучше), «p:<id>» (PNG фото).
Логотипы и фото — товарные знаки и изображения НХЛ и игроков: пак только для личного пользования, в публичный релиз не входит.
"""
import json, struct, sys, urllib.request, concurrent.futures as cf

data = json.load(open(sys.argv[1] if len(sys.argv) > 1 else "game/data/hm27-data.json"))
out = sys.argv[2] if len(sys.argv) > 2 else "art.pak"

def get(url):
    for _ in range(3):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 HM27"})
            with urllib.request.urlopen(req, timeout=20) as r:
                return r.read()
        except Exception as e:
            err = e
    print("нет:", url, err, file=sys.stderr)
    return None

jobs = {}
for t in data["teams"]:
    a = t["abbr"]
    jobs[f"logo:{a}"] = f"https://assets.nhle.com/logos/nhl/svg/{a}_light.svg"
    jobs[f"logo_d:{a}"] = f"https://assets.nhle.com/logos/nhl/svg/{a}_dark.svg"
for p in data["players"]:
    if p.get("head"):
        jobs[f"p:{p['id']}"] = f"https://assets.nhle.com/mugs/nhl/{p['head']}.png"

blobs = {}
with cf.ThreadPoolExecutor(16) as ex:
    for k, b in zip(jobs, ex.map(get, jobs.values())):
        if b: blobs[k] = b

index, off = {}, 0
for k, b in blobs.items():
    index[k] = [off, len(b)]; off += len(b)
ib = json.dumps(index, separators=(",", ":")).encode()
with open(out, "wb") as f:
    f.write(b"HM27ART1"); f.write(struct.pack("<I", len(ib))); f.write(ib)
    for b in blobs.values(): f.write(b)
print(f"пак: {len(blobs)} из {len(jobs)} файлов, {off / 1e6:.1f} МБ → {out}")
