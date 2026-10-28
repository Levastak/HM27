"""
Подпись особенностей стиля в реальных протоколах (НХЛ 2023-24 … 2025-26) — для зеркальной проверки движка.

  python3 data/trait_targets.py [папка raw] → data/trait_targets.json

Для каждой особенности — тот же показатель, по которому она выбрана в derive.py (доля дальних бросков, доля бросков
у ворот, частота блоков своих бросков…), у её обладателей и у остальных игроков той же позиции; в игре отношение
«обладатели / остальные» должно быть таким же (HM27.Check «Особенности в игре»). Зоны — как в движке: у ворот < 20 футов,
средняя 20–40, издали 40+; доли — от незаблокированных попыток, типы бросков — от бросков в створ.
"""
import gzip
import json
import math
import os
import sys
from collections import defaultdict

RAW = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "..", "raw")
DB = os.path.join(os.path.dirname(__file__), "..", "game", "data", "hm27-data.json")
SEASONS = ["20232024", "20242025", "20252026"]
ATT = {"shot-on-goal", "missed-shot", "blocked-shot", "goal"}

# особенность → (показатель, числитель, знаменатель); у «rate» знаменатель — 60 минут (из базы)
METRICS = {
    "far": ("доля бросков издали", "far", "unb"),
    "net": ("доля бросков у ворот", "close", "unb"),
    "jump": ("доля бросков ближе 40 футов", "near40", "unb"),
    "tip": ("доля подправлений", "tip", "sog"),
    "reb": ("доля добиваний", "reb", "unb"),
    "slap": ("доля щелчков", "slap", "sog"),
    "back": ("доля бросков с неудобной руки", "back", "sog"),
    "wrap": ("доля заездов из-за ворот", "wrap", "sog"),
    "thru": ("доля заблокированных своих бросков", "blkd", "att"),
    "pass": ("первые передачи на попытку броска", "a1", "att"),
}
MIN_DEN = {"unb": 150, "sog": 100, "att": 200}


def main():
    db = json.load(open(DB, encoding="utf-8"))
    players = {p["id"]: p for p in db["players"]}
    c = defaultdict(lambda: defaultdict(float))
    for season in SEASONS:
        with gzip.open(os.path.join(RAW, f"pbp_{season}.json.gz"), "rt", encoding="utf-8") as f:
            pbp = json.load(f)
        for gid, g in pbp.items():
            if g.get("type") != 2:
                continue
            home_id = g["home_id"]
            team_of = {r[0]: r[1] for r in g["roster"]}
            prev = None
            for p in g["plays"]:
                per, t, typ = p.get("per"), p.get("t"), p.get("type")
                if per is None or t is None or p.get("pt") == "SO" or per > 4:
                    prev = None
                    continue
                if typ not in ATT:
                    continue
                d = p.get("d") or {}
                shooter = d.get("scoringPlayerId") if typ == "goal" else d.get("shootingPlayerId")
                tm = team_of.get(shooter)
                if tm is None:
                    continue
                x = c[shooter]
                x["att"] += 1
                if typ == "goal":
                    a1 = d.get("assist1PlayerId")
                    if a1:
                        c[a1]["a1"] += 1
                if typ == "blocked-shot":
                    x["blkd"] += 1
                    continue
                x["unb"] += 1
                xc, yc = d.get("xCoord"), d.get("yCoord")
                if xc is not None and yc is not None:
                    side = p.get("side")
                    home = tm == home_id
                    if side in ("left", "right"):
                        right = (side == "left") if home else (side != "left")
                        nx = 89 if right else -89
                    else:
                        nx = 89 if xc >= 0 else -89
                    dist = math.hypot(abs(nx - xc), yc)
                    if (p.get("sit") or "1551") == "1551" and per <= 3:
                        zn = "close" if dist < 20 else "mid" if dist < 40 else "far"
                        x[zn + "_n5"] += 1
                        x[zn + "_g5"] += typ == "goal"
                    x["close"] += dist < 20
                    x["far"] += dist >= 40
                    x["near40"] += dist < 40
                if prev is not None and prev[0] == per and 0 <= t - prev[1] <= 3 and prev[2] == tm:
                    x["reb"] += 1
                prev = (per, t, tm) if typ != "goal" else None
                if typ in ("shot-on-goal", "goal"):
                    x["sog"] += 1
                    st = d.get("shotType") or ""
                    x["tip"] += st in ("tip-in", "deflected")
                    x["slap"] += st == "slap"
                    x["back"] += st == "backhand"
                    x["wrap"] += st == "wrap-around"
    out = {}
    for tr, (name, num, den) in METRICS.items():
        for grp in ("F", "D"):
            have, other = [0.0, 0.0], [0.0, 0.0]
            nh = 0
            for pid, p in players.items():
                if p.get("pos") == "G" or ((p.get("pos") == "D") != (grp == "D")):
                    continue
                x = c.get(pid)
                if not x or x[den] < MIN_DEN[den]:
                    continue
                bucket = have if tr in (p.get("tr") or []) else other
                bucket[0] += x[num]; bucket[1] += x[den]
                nh += tr in (p.get("tr") or [])
            if nh < 4 or have[1] == 0 or other[1] == 0:
                continue
            rh, ro = have[0] / have[1], other[0] / other[1]
            out[f"{tr}_{grp}"] = {"trait": tr, "group": grp, "metric": name, "num": num, "den": den,
                                 "have": round(rh, 4), "other": round(ro, 4), "ratio": round(rh / ro, 3), "n": nh}
    # частотные особенности — из базы (за 60 минут, все составы): броски, блоки
    for tr, key in (("shoot", "sa_60"), ("blk", "blk_60")):
        for grp in ("F", "D"):
            have = [p["x"][key] for p in players.values() if p.get("pos") != "G" and (p.get("pos") == "D") == (grp == "D")
                    and p.get("x") and key in p["x"] and (p.get("toi") or 0) >= 10 and tr in (p.get("tr") or [])]
            other = [p["x"][key] for p in players.values() if p.get("pos") != "G" and (p.get("pos") == "D") == (grp == "D")
                     and p.get("x") and key in p["x"] and (p.get("toi") or 0) >= 10 and tr not in (p.get("tr") or [])]
            if len(have) >= 4:
                rh, ro = sum(have) / len(have), sum(other) / len(other)
                out[f"{tr}_{grp}"] = {"trait": tr, "group": grp, "metric": f"{key} (база)", "num": key, "den": "60",
                                     "have": round(rh, 3), "other": round(ro, 3), "ratio": round(rh / ro, 3), "n": len(have)}
    out["finishing"] = finishing(players, c)
    path = os.path.join(os.path.dirname(__file__), "trait_targets.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    for k, v in out.items():
        if "metric" not in v:
            continue
        print(f"{k:10s} {v['metric']:38s} обладатели {v['have']:.4f}  остальные {v['other']:.4f}  ×{v['ratio']:.2f}  (n={v['n']})")


def finishing(players, c):
    """
    Реализация при 5×5 (незаблокированные попытки) по зонам: пуассоновская регрессия на зону, позицию, завершение (fin)
    и дальний бросок (lng, только для бросков издали) по оценкам из базы; плюс отношение «факт / прогноз» у топ-3% по завершению.
    Движок должен дать те же коэффициенты на тех же оценках из базы (зеркальная проверка).
    """
    import numpy as np
    rows = []
    for pid, p in players.items():
        a = p.get("a")
        if not a or p.get("pos") == "G" or pid not in c or "fin" not in a or "lng" not in a:
            continue
        for zi, zn in enumerate(("close", "mid", "far")):
            n = c[pid][zn + "_n5"]
            if n > 0:
                rows.append((pid, zi, p["pos"] == "D", a["fin"][0] - 11, (a["lng"][0] - 11) if zi == 2 else 0.0, c[pid][zn + "_g5"], n))
    A = np.array([r[1:] for r in rows], dtype=float)
    X = np.column_stack([np.ones(len(A)), A[:, 0] == 1, A[:, 0] == 2, A[:, 1], A[:, 2], A[:, 3]])
    y, n = A[:, 4], A[:, 5]
    b = np.zeros(X.shape[1]); b[0] = math.log(y.sum() / n.sum())
    for _ in range(60):
        mu = n * np.exp(X @ b)
        z = X @ b + (y - mu) / np.maximum(mu, 1e-9)
        b = np.linalg.solve(X.T @ (mu[:, None] * X) + 1e-6 * np.eye(len(b)), X.T @ (mu * z))
    mu = n * np.exp(X @ b)
    se = np.sqrt(np.diag(np.linalg.inv(X.T @ (mu[:, None] * X))))
    fins = np.array([players[r[0]]["a"]["fin"][0] for r in rows])
    thr = float(np.quantile([players[pid]["a"]["fin"][0] for pid in {r[0] for r in rows}], 0.97))
    sel = fins >= thr
    res = {"fin": round(float(b[4]), 4), "fin_se": round(float(se[4]), 4), "lng_far": round(float(b[5]), 4), "lng_se": round(float(se[5]), 4),
           "top3_ratio": round(float(y[sel].sum() / mu[sel].sum()), 3), "top3_thr": round(thr, 2)}
    print("Реализация 5×5:", res)
    return res


if __name__ == "__main__":
    main()
