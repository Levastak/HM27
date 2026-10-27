#!/usr/bin/env python3
"""Сырые данные для модели игрока HM27: протоколы, карты смен, страницы игроков (запускается в GitHub Actions).

Запуск:  python3 data/fetch_pbp.py --dir raw --seasons 20232024 20242025 20252026 20262027

Источники (открытые API НХЛ):
  • протокол матча:  api-web.nhle.com/v1/gamecenter/<id>/play-by-play
  • карта смен:      api.nhle.com/stats/rest/en/shiftcharts?cayenneExp=gameId=<id>
  • страница игрока: api-web.nhle.com/v1/player/<id>/landing  (драфт, рост/вес, карьера по лигам, награды)

Выход (в --dir, сжато):
  pbp_<сезон>.json.gz    = {game_id: {date, home, away, home_id, away_id, roster:[[pid, team_id, pos]], plays:[...]}}
  shifts_<сезон>.json.gz = {game_id: [[pid, team_id, period, start_s, end_s], ...]}
  players.json.gz        = {pid: {страница игрока без тяжёлых полей}}
Уже скачанные завершённые матчи не скачиваются заново (файлы прошлых запусков лежат в релизе raw-data).
"""
import argparse, concurrent.futures as cf, gzip, json, os, sys, threading, time, urllib.error, urllib.request

UA = "Mozilla/5.0 (HM27 data; github.com/Levastak/HM27)"
PBP = "https://api-web.nhle.com/v1/gamecenter/{gid}/play-by-play"
SHIFTS = "https://api.nhle.com/stats/rest/en/shiftcharts?cayenneExp=gameId={gid}"
LANDING = "https://api-web.nhle.com/v1/player/{pid}/landing"
GAMES_PER_SEASON = 1312
DONE = ("OFF", "FINAL")

# тяжёлые или ненужные поля протокола и страницы игрока
DROP_DETAILS = {"highlightClip", "highlightClipFr", "highlightClipSharingUrl", "highlightClipSharingUrlFr",
                "discreteClip", "discreteClipFr", "awaySOG", "homeSOG"}
DROP_LANDING = {"featuredStats", "last5Games", "shopLink", "twitterLink", "watchLink", "heroImage", "headshot",
                "currentTeamRoster", "teamLogo", "badges", "playerSlug"}

_lock = threading.Lock()


def log(*a):
    with _lock:
        print(*a, file=sys.stderr, flush=True)


def fetch_json(url, tries=5, timeout=60):
    for i in range(tries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "application/json"})
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            log(f"  {url}: HTTP {e.code}, повтор {i + 1}")
        except Exception as e:
            log(f"  {url}: {e}, повтор {i + 1}")
        time.sleep(2 * (i + 1))
    return None


def secs(mmss):
    try:
        m, s = mmss.split(":")
        return int(m) * 60 + int(s)
    except Exception:
        return None


def slim_pbp(j):
    """Протокол без лишнего: время, тип, ситуация на льду, участники, координаты."""
    plays = []
    for p in j.get("plays", []):
        pd = p.get("periodDescriptor") or {}
        d = {k: v for k, v in (p.get("details") or {}).items() if k not in DROP_DETAILS}
        plays.append({
            "per": pd.get("number"), "pt": pd.get("periodType"), "t": secs(p.get("timeInPeriod", "")),
            "type": p.get("typeDescKey"), "sit": p.get("situationCode"), "side": p.get("homeTeamDefendingSide"), "d": d,
        })
    roster = [[r.get("playerId"), r.get("teamId"), r.get("positionCode")] for r in j.get("rosterSpots", [])]
    h, a = j.get("homeTeam") or {}, j.get("awayTeam") or {}
    return {"date": j.get("gameDate"), "type": j.get("gameType"), "home": h.get("abbrev"), "away": a.get("abbrev"),
            "home_id": h.get("id"), "away_id": a.get("id"), "venue": (j.get("venue") or {}).get("default"),
            "roster": roster, "plays": plays}


def slim_shifts(j):
    out = []
    for s in (j or {}).get("data", []):
        if s.get("typeCode") != 517:   # 517 — смена; 505 — отметка гола
            continue
        a, b = secs(s.get("startTime") or ""), secs(s.get("endTime") or "")
        if a is None or b is None or b <= a:
            continue
        out.append([s.get("playerId"), s.get("teamId"), s.get("period"), a, b])
    return out


def load(path):
    if os.path.exists(path):
        with gzip.open(path, "rt", encoding="utf-8") as f:
            return json.load(f)
    return {}


def save(path, obj):
    tmp = path + ".tmp"
    with gzip.open(tmp, "wt", encoding="utf-8", compresslevel=9) as f:
        json.dump(obj, f, ensure_ascii=False, separators=(",", ":"))
    os.replace(tmp, path)


def game(gid):
    """(статус, протокол, смены). Статус: done / future / missing."""
    j = fetch_json(PBP.format(gid=gid))
    if j is None:
        return "missing", None, None
    if j.get("gameState") not in DONE:
        return "future", None, None
    sh = slim_shifts(fetch_json(SHIFTS.format(gid=gid)))
    return "done", slim_pbp(j), sh


def season_games(season, out_dir, workers, report):
    pbp_path = os.path.join(out_dir, f"pbp_{season}.json.gz")
    sh_path = os.path.join(out_dir, f"shifts_{season}.json.gz")
    pbp, shifts = load(pbp_path), load(sh_path)
    # регулярный сезон: <сезон начала>02<номер>; плей-офф: 03<раунд><серия><матч>
    y = season[:4]
    ids = [f"{y}02{n:04d}" for n in range(1, GAMES_PER_SEASON + 1)]
    ids += [f"{y}030{r}{s}{g}" for r in range(1, 5) for s in range(1, 9 if r == 1 else (5 if r == 2 else (3 if r == 3 else 2))) for g in range(1, 8)]
    todo = [g for g in ids if g not in pbp or (g not in shifts or not shifts[g])]
    log(f"{season}: в кэше {len(pbp)}, к загрузке {len(todo)}")
    stat = {"done": 0, "future": 0, "missing": 0, "no_shifts": 0}
    t0 = time.time()
    with cf.ThreadPoolExecutor(workers) as ex:
        for i, (gid, (st, p, sh)) in enumerate(zip(todo, ex.map(game, todo))):
            stat[st] += 1
            if st == "done":
                pbp[gid] = p
                if sh:
                    shifts[gid] = sh
                else:
                    stat["no_shifts"] += 1
            if (i + 1) % 200 == 0:
                log(f"  {season}: {i + 1}/{len(todo)}, {time.time() - t0:.0f} с")
                save(pbp_path, pbp); save(sh_path, shifts)
    save(pbp_path, pbp); save(sh_path, shifts)
    report[season] = {"games": len(pbp), "with_shifts": len(shifts), **stat}
    return pbp


def players(pids, out_dir, workers, report, refresh):
    path = os.path.join(out_dir, "players.json.gz")
    have = load(path)
    todo = sorted(p for p in pids if refresh or str(p) not in have)
    log(f"Страницы игроков: в кэше {len(have)}, к загрузке {len(todo)}")

    def one(pid):
        j = fetch_json(LANDING.format(pid=pid))
        if not j:
            return pid, None
        for k in DROP_LANDING:
            j.pop(k, None)
        return pid, j

    miss = 0
    with cf.ThreadPoolExecutor(workers) as ex:
        for pid, j in ex.map(one, todo):
            if j:
                have[str(pid)] = j
            else:
                miss += 1
    save(path, have)
    report["players"] = {"total": len(have), "fetched": len(todo) - miss, "missing": miss}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default="raw")
    ap.add_argument("--seasons", nargs="+", default=["20232024", "20242025", "20252026", "20262027"])
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("--refresh-players", action="store_true", help="обновить страницы всех игроков")
    a = ap.parse_args()
    os.makedirs(a.dir, exist_ok=True)
    report, pids = {}, set()
    for s in a.seasons:
        for g in season_games(s, a.dir, a.workers, report).values():
            pids.update(r[0] for r in g["roster"] if r[0])
    players(pids, a.dir, a.workers, report, a.refresh_players)
    with open(os.path.join(a.dir, "report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
