#!/usr/bin/env python3
"""Добавляет модель игрока (data/derived.json.gz) в базу игры game/data/hm27-data.json (формат 3).

Запуск:  python3 data/merge_model.py [game/data/hm27-data.json] [data/derived.json.gz]

У каждого игрока появляются:
  lvl = [уровень 1–20, погрешность]          — оценка по данным (центр, вокруг которого карьера разыгрывает истинный уровень)
  a   = {ключ: [значение 1–20, погрешность]} — характеристики
  fx  = факты карьеры для характера: матчи НХЛ и плей-офф, сезоны, драфт, награды, клубы, пропуски, драки
Старые рейтинги r (перцентили) остаются для ролей и хроники; r.ovr = уровень × 5 — для сортировок и звёзд.
Игроки без данных за 4 сезона (дебютанты) получают уровень из перцентиля старой оценки с большой погрешностью.
"""
import gzip, json, math, sys

MID, SD = 11.0, 3.4


def soft(u):
    if u > 15:
        return 15 + 5 * math.tanh((u - 15) / 5)
    if u < 5:
        return 5 - 4 * math.tanh((5 - u) / 4)
    return u


def inv_norm(p):
    """Обратная функция нормального распределения (Акклам), p в (0, 1)."""
    p = min(max(p, 1e-4), 1 - 1e-4)
    a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00]
    b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155657660e+01]
    c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00]
    d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00]
    if p < 0.02425:
        q = math.sqrt(-2 * math.log(p))
        return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1)
    if p > 1 - 0.02425:
        return -inv_norm(1 - p)
    q = p - 0.5; r = q * q
    return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1)


def facts(f):
    if not f:
        return {}
    tags = sorted({a["tag"] for a in f.get("awards") or [] if a.get("tag")})
    dr = f.get("draft") or {}
    out = {
        "gp": f.get("nhl_gp") or 0, "po": f.get("po_gp") or 0, "sea": f.get("nhl_seasons") or 0,
        "clubs": f.get("clubs") or 0, "club_sea": f.get("club_seasons") or 0,
        "miss": f.get("miss_long") or 0, "span": f.get("span") or 0, "fights": f.get("fights") or 0,
        "aw": tags,
    }
    if dr.get("overallPick"):
        out["pick"] = dr["overallPick"]; out["draft"] = dr.get("year")
    return {k: v for k, v in out.items() if v not in (None, [], 0) or k in ("gp",)}


def main(db_path="game/data/hm27-data.json", model_path="data/derived.json.gz"):
    db = json.load(open(db_path, encoding="utf-8"))
    with gzip.open(model_path, "rt", encoding="utf-8") as f:
        m = json.load(f)
    found = fallback = 0
    for p in db["players"]:
        src = (m["goalies"] if p["pos"] == "G" else m["skaters"]).get(str(p["id"]))
        if src and src.get("toi", 0) > 0:
            p["lvl"] = [round(src["level"][0], 2), round(src["level"][1], 2)]
            p["a"] = {k: [round(v[0], 2), round(v[1], 2)] for k, v in src["a"].items()}
            p["fx"] = facts(src.get("facts"))
            found += 1
        else:
            # дебютант без матчей в протоколах: уровень из старой перцентильной оценки, не выше 12, большая погрешность
            pct = (p.get("r") or {}).get("ovr", 40) / 100
            lv = min(12.0, soft(MID + SD * inv_norm(pct) * 0.7))
            p["lvl"] = [round(lv, 2), 3.0]
            p["a"] = {}
            p["fx"] = {}
            fallback += 1
        p.setdefault("r", {})["ovr"] = int(round(p["lvl"][0] * 5))
    db["format"] = 3
    db["model"] = {"built": m["meta"]["built"], "seasons": m["meta"]["seasons"], "scale": m["meta"]["scale"],
                   "kinds": m["meta"].get("attr_kinds", {})}
    with open(db_path, "w", encoding="utf-8") as fh:
        json.dump(db, fh, ensure_ascii=False, separators=(",", ":"))
    print(f"Модель игрока: {found} игроков из расчёта, {fallback} без данных (оценка по перцентилю)")


if __name__ == "__main__":
    main(*sys.argv[1:])
