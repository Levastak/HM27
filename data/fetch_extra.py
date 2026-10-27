#!/usr/bin/env python3
"""Дополнительные данные для характеристик игроков HM27 (запускается в GitHub Actions).

Запуск:  python3 data/fetch_extra.py --ids game/data/hm27-data.json --out data/extra.json.gz

Источники:
  • MoneyPuck, сводки сезонов (только для некоммерческого использования, со ссылкой на MoneyPuck.com):
      moneypuck.com/moneypuck/playerData/seasonSummary/<сезон>/regular/{skaters,goalies}.csv
    Берём ситуацию «all» (и «5on5» для владения). Сезоны: текущий и два прошлых.
  • NHL EDGE (трекинг лиги): api-web.nhle.com/v1/edge/skater-detail/<id>/now — максимальная скорость и бросок.

Выход: extra.json.gz = {seasons, skaters: {id: {season: {...}}}, goalies: {...}, edge: {id: {speed, shot}}, report}
"""
import argparse, csv, datetime as dt, gzip, io, json, os, sys, time, urllib.error, urllib.request

UA = "Mozilla/5.0 (HM27 data; github.com/Levastak/HM27)"
MP = "https://moneypuck.com/moneypuck/playerData/seasonSummary/{season}/regular/{kind}.csv"
EDGE = "https://api-web.nhle.com/v1/edge/skater-detail/{pid}/now"

SK_COLS = {
    "gp": "games_played", "toi": "icetime",
    "ixg": "I_F_xGoals", "g": "I_F_goals", "a1": "I_F_primaryAssists", "a2": "I_F_secondaryAssists",
    "sa": "I_F_shotAttempts", "hits": "I_F_hits", "tk": "I_F_takeaways", "gv": "I_F_giveaways",
    "blk": "shotsBlockedByPlayer", "fow": "faceoffsWon", "fol": "faceoffsLost",
    "pen": "penalties", "pend": "penaltiesDrawn",
    "on_xgf": "OnIce_F_xGoals", "on_xga": "OnIce_A_xGoals", "off_xgf": "OffIce_F_xGoals", "off_xga": "OffIce_A_xGoals",
}
SK_5V5 = {"toi5": "icetime", "on_cf": "OnIce_F_shotAttempts", "on_ca": "OnIce_A_shotAttempts",
          "off_cf": "OffIce_F_shotAttempts", "off_ca": "OffIce_A_shotAttempts"}
G_COLS = {"gp": "games_played", "toi": "icetime", "xga": "xGoals", "ga": "goals", "sog": "ongoal",
          "hd_xg": "highDangerxGoals", "hd_g": "highDangerGoals", "md_xg": "mediumDangerxGoals", "md_g": "mediumDangerGoals",
          "ld_xg": "lowDangerxGoals", "ld_g": "lowDangerGoals", "reb": "rebounds", "xreb": "xRebounds"}


def log(*a):
    print(*a, file=sys.stderr, flush=True)


def fetch(url, tries=4, timeout=120):
    for i in range(tries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            log(f"  {url}: HTTP {e.code}, повтор {i + 1}")
        except Exception as e:
            log(f"  {url}: {e}, повтор {i + 1}")
        time.sleep(3 * (i + 1))
    return None


def num(x):
    try:
        v = float(x)
        return v if v == v else None
    except Exception:
        return None


def mp(kind, season, report):
    raw = fetch(MP.format(season=season, kind=kind))
    if not raw:
        report[f"{kind}_{season}"] = "нет файла"
        return {}
    rd = csv.DictReader(io.StringIO(raw.decode("utf-8", "replace")))
    hdr = rd.fieldnames or []
    cols = SK_COLS if kind == "skaters" else G_COLS
    miss = [c for c in list(cols.values()) + (list(SK_5V5.values()) if kind == "skaters" else []) if c not in hdr]
    out = {}
    n = 0
    for r in rd:
        sit = r.get("situation")
        pid = r.get("playerId")
        if not pid or sit not in ("all", "5on5"):
            continue
        d = out.setdefault(pid, {"team": r.get("team"), "name": r.get("name")})
        src = (cols if sit == "all" else (SK_5V5 if kind == "skaters" else {}))
        for k, c in src.items():
            if c in r:
                v = num(r[c])
                if v is not None:
                    d[k] = round(v, 3)
        n += 1
    report[f"{kind}_{season}"] = dict(rows=n, players=len(out), missing_columns=miss)
    log(f"MoneyPuck {kind} {season}: игроков {len(out)}, нет колонок: {miss}")
    return out


def walk(o, path=""):
    """Все числа в JSON с путём ключей — для разбора EDGE, у которого формат меняется."""
    if isinstance(o, dict):
        for k, v in o.items():
            yield from walk(v, f"{path}.{k}" if path else k)
    elif isinstance(o, list):
        for i, v in enumerate(o):
            yield from walk(v, f"{path}[{i}]")
    elif isinstance(o, (int, float)) and not isinstance(o, bool):
        yield path, o


def edge_one(pid):
    raw = fetch(EDGE.format(pid=pid), tries=2, timeout=30)
    if not raw:
        return None, None
    js = json.loads(raw)
    speed = shot = None
    for p, v in walk(js):
        lp = p.lower()
        # скорость: км/ч (metric) у максимальной скорости катания; бросок: км/ч у самого сильного броска
        if "metric" in lp or "kph" in lp or "km" in lp:
            if ("skatingspeed" in lp or "speedmax" in lp or "maxskatingspeed" in lp or "topspeed" in lp) and "burst" not in lp and 20 < v < 50:
                speed = max(speed or 0, v)
            if ("shotspeed" in lp or "hardestshot" in lp or "topshotspeed" in lp) and 80 < v < 190:
                shot = max(shot or 0, v)
    return dict(speed=round(speed, 2) if speed else None, shot=round(shot, 2) if shot else None), js


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ids", default="game/data/hm27-data.json")
    ap.add_argument("--out", default="data/extra.json.gz")
    ap.add_argument("--season", type=int, default=None, help="год начала текущего сезона (по умолчанию — по дате)")
    ap.add_argument("--no-edge", action="store_true")
    a = ap.parse_args()
    today = dt.date.today()
    cur = a.season or (today.year if today.month >= 9 else today.year - 1)
    seasons = [cur, cur - 1, cur - 2]
    report = {}
    skaters, goalies = {}, {}
    for s in seasons:
        for pid, d in mp("skaters", s, report).items():
            skaters.setdefault(pid, {})[str(s)] = d
        for pid, d in mp("goalies", s, report).items():
            goalies.setdefault(pid, {})[str(s)] = d
    edge, sample = {}, None
    if not a.no_edge:
        base = json.load(open(a.ids, encoding="utf-8"))
        ids = [p["id"] for p in base["players"] if p.get("pos") != "G"]
        log(f"EDGE: {len(ids)} полевых…")
        ok = 0
        for i, pid in enumerate(ids):
            e, js = edge_one(pid)
            if e and (e["speed"] or e["shot"]):
                edge[str(pid)] = e; ok += 1
                if sample is None:
                    sample = js
            time.sleep(0.15)
            if i % 100 == 0:
                log(f"  {i}/{len(ids)}, с данными {ok}")
        report["edge"] = dict(players=len(ids), with_data=ok)
        if sample is not None:
            # образец ответа — чтобы видеть формат, если разбор перестанет находить поля
            with open(os.path.join(os.path.dirname(a.out) or ".", "edge_sample.json"), "w", encoding="utf-8") as fh:
                json.dump(sample, fh, ensure_ascii=False, indent=1)
    pack = dict(built=dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%MZ"), seasons=seasons,
                skaters=skaters, goalies=goalies, edge=edge, report=report)
    with gzip.open(a.out, "wt", encoding="utf-8") as fh:
        json.dump(pack, fh, ensure_ascii=False, separators=(",", ":"))
    log(json.dumps(report, ensure_ascii=False))
    log(f"Готово: {a.out}, {os.path.getsize(a.out) // 1024} КБ")


if __name__ == "__main__":
    main()
