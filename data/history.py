#!/usr/bin/env python3
"""Возрастные кривые HM27 по истории НХЛ (метод разностей): как меняются одни и те же игроки от сезона к сезону.

Запуск:  python3 data/history.py --raw raw --out data/age_curves.json

Вход: mp_hist.json.gz (сводки MoneyPuck 2010–2026 + даты рождения; moneypuck.com, некоммерческое использование),
      edge.json.gz (NHL EDGE 2021–2027: скорость — для физики).
Группы (ключи — как в core/HM27.Core/PlayerModel.cs, Attrs.CurveGroup):
  shoot — бросок (личный xG и голы за 60), pass — передачи (первые передачи за 60),
  def — оборона (xG соперника при игроке на льду против без него), mind — влияние на игру (доля xG на льду против без него),
  phys — скорость (EDGE), goalie — вратари (сейвы сверх ожидаемого на бросок).
Выход: {группа: {возраст: изменение за сезон в ед. шкалы 1–20}}, плюс число пар и надёжность метрики.
Изменение переводится в единицы «истинного умения»: Δz наблюдаемое / √(корреляция сезон-к-сезону).
Ограничение метода: «выживают» лучшие (кто сдал — уходит из лиги), поэтому спад у ветеранов, скорее, немного занижен.
"""
import argparse, gzip, json, math, os
from collections import defaultdict

SCALE = 3.4
AGES = range(18, 41)


def gz(path):
    with gzip.open(path, "rt", encoding="utf-8") as f:
        return json.load(f)


def age_at(born, season_start):
    """Возраст на 1 февраля сезона (сезон 2015 = 2015-16)."""
    try:
        y, m, d = map(int, born[:10].split("-"))
    except Exception:
        return None
    return season_start + 1 - y - ((2, 1) < (m, d))


def zscore(rows, key):
    """z по сезону среди регулярных: убирает изменения самой лиги (правила, стиль)."""
    by = defaultdict(list)
    for r in rows:
        if r.get(key) is not None:
            by[r["season"]].append(r[key])
    stats = {}
    for s, v in by.items():
        if len(v) < 30:
            continue
        mu = sum(v) / len(v)
        sd = math.sqrt(sum((x - mu) ** 2 for x in v) / len(v)) or 1
        stats[s] = (mu, sd)
    for r in rows:
        st = stats.get(r["season"])
        r["z_" + key] = (r[key] - st[0]) / st[1] if st and r.get(key) is not None else None


def deltas(rows, key):
    """Пары соседних сезонов одного игрока → (возраст в первом, Δz, вес), корреляция сезон-к-сезону."""
    by = defaultdict(dict)
    for r in rows:
        if r.get("z_" + key) is not None and r.get("age") is not None:
            by[r["pid"]][r["season"]] = r
    out, xs, ys = [], [], []
    for pid, ss in by.items():
        for s, r in ss.items():
            n = ss.get(s + 1)
            if not n:
                continue
            w = 2 / (1 / r["w"] + 1 / n["w"])
            out.append((r["age"], n["z_" + key] - r["z_" + key], w))
            xs.append(r["z_" + key]); ys.append(n["z_" + key])
    if len(xs) < 50:
        return out, 0.5
    mx, my = sum(xs) / len(xs), sum(ys) / len(ys)
    cov = sum((a - mx) * (b - my) for a, b in zip(xs, ys))
    r = cov / math.sqrt(sum((a - mx) ** 2 for a in xs) * sum((b - my) ** 2 for b in ys))
    return out, max(r, 0.2)


PRIOR_AGES = [18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 40]
PRIOR_DV = [1.4, 1.3, 1.1, 0.9, 0.7, 0.5, 0.3, 0.15, 0.05, 0, -0.05, -0.1, -0.2, -0.3, -0.4, -0.5, -0.6, -0.75, -0.9, -1.0, -1.2, -1.4]
# сдвиг возраста для априорной кривой (как Attrs.AgeShift в ядре) и доверие к данным по группе:
# индивидуальные показатели (бросок, передачи) меряются чисто; показатели «на льду» искажены нагрузкой — молодых
# прячут от сильных соперников, ветеранов выпускают против лучших, поэтому им доверяем меньше; вратари — шум.
SHIFT = {"shoot": 0, "pass": -1, "def": -1, "mind": -3, "phys": 2, "goalie": -2}
TRUST = {"shoot": 0.8, "pass": 0.75, "phys": 0.6, "mind": 0.35, "def": 0.25, "goalie": 0.2}


def prior(age):
    if age <= PRIOR_AGES[0]:
        return PRIOR_DV[0]
    for i in range(1, len(PRIOR_AGES)):
        if age <= PRIOR_AGES[i]:
            a0, a1 = PRIOR_AGES[i - 1], PRIOR_AGES[i]
            return PRIOR_DV[i - 1] + (PRIOR_DV[i] - PRIOR_DV[i - 1]) * (age - a0) / (a1 - a0)
    return PRIOR_DV[-1]


def curve(pairs, rel, group):
    """Среднее Δ по возрасту → квадратичная аппроксимация (вес — число пар) → центрирование
    (поправка на отбор: в выборку попадают те, у кого был удачный сезон, дальше их цифры откатываются к среднему)
    → единицы шкалы → смесь с априорной кривой по доверию к группе."""
    import numpy as np
    acc = defaultdict(lambda: [0.0, 0.0, 0])
    tot_d = tot_w = 0.0
    for a, d, w in pairs:
        if 18 <= a <= 40:
            acc[a][0] += d * w; acc[a][1] += w; acc[a][2] += 1
            tot_d += d * w; tot_w += w
    ages = [a for a in sorted(acc) if acc[a][2] >= 15 and 19 <= a <= 38]
    n = {a: acc[a][2] for a in sorted(acc)}
    if len(ages) < 6:
        return {a: round(prior(a + SHIFT[group]), 3) for a in AGES}, n
    bias = tot_d / tot_w
    x = np.array(ages, float); y = np.array([acc[a][0] / acc[a][1] - bias for a in ages]); wts = np.sqrt([acc[a][2] for a in ages])
    coef = np.polyfit(x, y, 2, w=wts)
    out = {}
    for a in AGES:
        aa = min(max(a, ages[0]), ages[-1])
        data = float(np.polyval(coef, aa)) / math.sqrt(rel) * SCALE
        t = TRUST[group]
        out[a] = round(t * data + (1 - t) * prior(a + SHIFT[group]), 3)
    return out, n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", default="raw")
    ap.add_argument("--out", default="data/age_curves.json")
    a = ap.parse_args()
    H = gz(os.path.join(a.raw, "mp_hist.json.gz"))
    bios = H["bios"]
    sk_rows, g_rows = [], []
    for pid, seasons in H["skaters"].items():
        born = (bios.get(pid) or {}).get("born")
        for y, d in seasons.items():
            y = int(y)
            toi = (d.get("toi") or 0) / 60
            gp = d.get("gp") or 0
            if toi < 500 or gp < 30:
                continue
            team_toi = gp * 60.0
            off_t = max(team_toi - toi, 1)
            on_f, on_a, off_f, off_a = (d.get(k) or 0 for k in ("on_xgf", "on_xga", "off_xgf", "off_xga"))
            sk_rows.append({
                "pid": pid, "season": y, "age": age_at(born, y) if born else None, "w": toi, "pos": d.get("pos"),
                "shoot": ((d.get("ixg") or 0) + 0.3 * ((d.get("g") or 0) - (d.get("ixg") or 0))) / toi * 60,
                "pass": (d.get("a1") or 0) / toi * 60,
                "def": -(on_a / toi - off_a / off_t) * 60,
                "mind": on_f / max(on_f + on_a, 1e-9) - off_f / max(off_f + off_a, 1e-9),
            })
    for pid, seasons in H["goalies"].items():
        born = (bios.get(pid) or {}).get("born")
        for y, d in seasons.items():
            y = int(y)
            sog = d.get("sog") or 0
            if sog < 600:
                continue
            g_rows.append({"pid": pid, "season": y, "age": age_at(born, y) if born else None, "w": sog,
                           "goalie": ((d.get("xga") or 0) - (d.get("ga") or 0)) / sog})
    # скорость (EDGE) — для физики
    ph_rows = []
    edge = gz(os.path.join(a.raw, "edge.json.gz")) if os.path.exists(os.path.join(a.raw, "edge.json.gz")) else {}
    pages = gz(os.path.join(a.raw, "players.json.gz")) if os.path.exists(os.path.join(a.raw, "players.json.gz")) else {}
    for pid, ss in edge.items():
        born = (pages.get(pid) or {}).get("birthDate")
        for s, v in ss.items():
            if v.get("speed") and (v.get("gp") or 0) >= 30 and born:
                y = int(s[:4])
                ph_rows.append({"pid": pid, "season": y, "age": age_at(born, y), "w": v["gp"], "phys": v["speed"]})
    res = {"about": __doc__.strip().splitlines()[0], "groups": {}, "pairs": {}, "reliability": {}, "trust": TRUST, "shift": SHIFT}
    for key, rows in (("shoot", sk_rows), ("pass", sk_rows), ("def", sk_rows), ("mind", sk_rows), ("phys", ph_rows), ("goalie", g_rows)):
        zscore(rows, key)
        pairs, rel = deltas(rows, key)
        c, n = curve(pairs, rel, key)
        res["groups"][key] = c
        res["pairs"][key] = n
        res["reliability"][key] = round(rel, 3)
    res["seasons"] = sorted({r["season"] for r in sk_rows})
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    for k, c in res["groups"].items():
        print(k, f"r={res['reliability'][k]}", " ".join(f"{a}:{v:+.2f}" for a, v in c.items() if a in (19, 21, 23, 25, 27, 29, 31, 33, 35, 37)))


if __name__ == "__main__":
    main()
