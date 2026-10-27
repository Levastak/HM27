#!/usr/bin/env python3
"""Данные MoneyPuck для модели игрока (запускается в GitHub Actions; из рабочего окружения сайт закрыт).

Запуск:  python3 data/fetch_mp.py --dir raw --shots 2022 2023 2024 2025 2026 --hist 2010-2026

MoneyPuck.com — бесплатно для некоммерческого использования, с указанием источника (moneypuck.com/data.htm).
  • броски с xG MoneyPuck:  peter-tanner.com/moneypuck/downloads/shots_<год начала сезона>.zip
      → mp_shots_<год>.csv.gz (только нужные колонки) — в derive.py заменяют нашу модель xG;
  • сводки сезонов (регулярка) полевых и вратарей за 2010–2026:
      moneypuck.com/moneypuck/playerData/seasonSummary/<год>/regular/{skaters,goalies}.csv
      → mp_hist.json.gz — для возрастных кривых (метод разностей по одним и тем же игрокам);
  • справочник игроков (дата рождения, позиция):  moneypuck.com/moneypuck/playerData/playerBios/allPlayersLookup.csv
"""
import argparse, csv, gzip, io, json, os, sys, time, urllib.error, urllib.request, zipfile

UA = "Mozilla/5.0 (HM27 data; github.com/Levastak/HM27)"
SHOTS = "https://peter-tanner.com/moneypuck/downloads/shots_{y}.zip"
SUMMARY = "https://moneypuck.com/moneypuck/playerData/seasonSummary/{y}/regular/{kind}.csv"
BIOS = "https://moneypuck.com/moneypuck/playerData/playerBios/allPlayersLookup.csv"
SHOT_COLS = ["season", "game_id", "isPlayoffGame", "period", "time", "event", "shooterPlayerId", "goalieIdForShot", "xGoal",
             "shotWasOnGoal", "shotType", "arenaAdjustedShotDistance", "shotRebound", "shotRush", "homeTeamCode", "teamCode"]
SK = {"gp": "games_played", "toi": "icetime", "g": "I_F_goals", "a1": "I_F_primaryAssists", "a2": "I_F_secondaryAssists",
      "ixg": "I_F_xGoals", "sa": "I_F_shotAttempts", "hits": "I_F_hits", "tk": "I_F_takeaways", "gv": "I_F_giveaways",
      "blk": "shotsBlockedByPlayer", "pen": "penalties", "fow": "faceoffsWon", "fol": "faceoffsLost",
      "on_xgf": "OnIce_F_xGoals", "on_xga": "OnIce_A_xGoals", "off_xgf": "OffIce_F_xGoals", "off_xga": "OffIce_A_xGoals"}
GK = {"gp": "games_played", "toi": "icetime", "xga": "xGoals", "ga": "goals", "sog": "ongoal"}


def log(*a):
    print(*a, file=sys.stderr, flush=True)


def fetch(url, tries=4, timeout=300):
    for i in range(tries):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=timeout) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            log(f"  {url}: HTTP {e.code}, повтор {i + 1}")
        except Exception as e:
            log(f"  {url}: {e}, повтор {i + 1}")
        time.sleep(5 * (i + 1))
    return None


def shots(y, out_dir, report):
    path = os.path.join(out_dir, f"mp_shots_{y}.csv.gz")
    if os.path.exists(path) and y < 2026:
        report[f"shots_{y}"] = "кэш"; return
    raw = fetch(SHOTS.format(y=y))
    if not raw:
        report[f"shots_{y}"] = "нет файла"; return
    z = zipfile.ZipFile(io.BytesIO(raw))
    name = [n for n in z.namelist() if n.endswith(".csv")][0]
    n = 0
    with z.open(name) as fin, gzip.open(path + ".tmp", "wt", encoding="utf-8", newline="") as fout:
        rd = csv.DictReader(io.TextIOWrapper(fin, encoding="utf-8"))
        cols = [c for c in SHOT_COLS if c in (rd.fieldnames or [])]
        w = csv.writer(fout)
        w.writerow(cols)
        for r in rd:
            w.writerow([r.get(c, "") for c in cols]); n += 1
    os.replace(path + ".tmp", path)
    report[f"shots_{y}"] = n


def num(x):
    try:
        v = float(x)
        return v if v == v else None
    except Exception:
        return None


def hist(years, out_dir, report):
    out = {"skaters": {}, "goalies": {}}
    for y in years:
        for kind, cols in (("skaters", SK), ("goalies", GK)):
            raw = fetch(SUMMARY.format(y=y, kind=kind))
            if not raw:
                report[f"hist_{kind}_{y}"] = "нет"; continue
            rd = csv.DictReader(io.StringIO(raw.decode("utf-8", "replace")))
            k = 0
            for r in rd:
                if r.get("situation") != "all":
                    continue
                d = {c: num(r.get(src)) for c, src in cols.items()}
                d["pos"] = r.get("position"); d["team"] = r.get("team")
                out[kind].setdefault(r["playerId"], {})[str(y)] = d; k += 1
            report[f"hist_{kind}_{y}"] = k
    raw = fetch(BIOS)
    bios = {}
    if raw:
        for r in csv.DictReader(io.StringIO(raw.decode("utf-8", "replace"))):
            bios[r.get("playerId")] = {"born": r.get("birthDate"), "pos": r.get("position"), "name": r.get("name")}
    out["bios"] = bios
    report["bios"] = len(bios)
    with gzip.open(os.path.join(out_dir, "mp_hist.json.gz"), "wt", encoding="utf-8") as f:
        json.dump(out, f, separators=(",", ":"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default="raw")
    ap.add_argument("--shots", nargs="*", type=int, default=[2022, 2023, 2024, 2025, 2026])
    ap.add_argument("--hist", default="2010-2026")
    a = ap.parse_args()
    os.makedirs(a.dir, exist_ok=True)
    report = {}
    for y in a.shots:
        shots(y, a.dir, report)
    y0, y1 = map(int, a.hist.split("-"))
    hist(range(y0, y1 + 1), a.dir, report)
    with open(os.path.join(a.dir, "mp_report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
