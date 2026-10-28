"""
Цели для движка по сменам — из реальных протоколов и карт смен НХЛ (регулярка 2022-23 … 2025-26).

  python3 data/engine_targets.py [папка raw] → data/engine_targets.json

Что снимаем (всё — средние по лиге, на команду, если не сказано иное):
  * время и голы по составам: 5×5, большинство, меньшинство, 4×4, 3×3, с шестым полевым, в пустые;
  * броски: попытки, блоки, мимо, в створ; реализация; типы бросков;
  * эффект счёта: доля попыток при 5×5 в зависимости от разницы в счёте;
  * удаления: большинства за матч, реализация, длительность;
  * смены: длина у нападающих и защитников, число смен, минуты по рангу в составе;
  * исходы: доля ОТ и буллитов, голы по периодам, разница в счёте, победы хозяев;
  * передачи: доля голов без передач / с одной / с двумя;
  * распределения игроков за сезон: лидеры по голам и очкам, сколько 30/40-голевых и 60/80/100-очковых.
"""
import gzip
import json
import os
import sys
from collections import Counter, defaultdict

RAW = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "..", "raw")
SEASONS = ["20222023", "20232024", "20242025", "20252026"]
ATT = {"shot-on-goal", "missed-shot", "blocked-shot", "goal"}


def gz(p):
    with gzip.open(p, "rt", encoding="utf-8") as f:
        return json.load(f)


def state_of(sit, home):
    """Состав с точки зрения команды: (свои полевые, чужие полевые, свой вратарь, чужой вратарь)."""
    try:
        ag, an, hn, hg = int(sit[0]), int(sit[1]), int(sit[2]), int(sit[3])
    except Exception:
        ag, an, hn, hg = 1, 5, 5, 1
    return (hn, an, hg, ag) if home else (an, hn, ag, hg)


def label(own, opp, og, pg):
    if pg == 0:
        return "en_for"           # у соперника пустые ворота
    if og == 0:
        return "en_against"       # у нас снят вратарь (6 полевых)
    if own == opp:
        return {5: "5v5", 4: "4v4", 3: "3v3"}.get(own, "other")
    if own > opp:
        return "pp"
    return "pk"


def main():
    T = defaultdict(float)            # время по составам (сек, на команду)
    G = Counter()                     # голы
    A = Counter()                     # попытки
    S = Counter()                     # в створ (включая голы)
    B = Counter()                     # заблокированные (атакующей команды)
    M = Counter()                     # мимо
    stype = defaultdict(lambda: [0, 0])
    score_g = defaultdict(lambda: [0, 0])     # разница → [голы за, против] при 5×5
    zone = defaultdict(lambda: [0, 0, 0, 0, 0])  # (поз, зона) → попытки, блоки, мимо, в створ, голы (5×5)
    reb_n = [0, 0]                            # добивания (≤3 с после броска своей команды): попытки, голы
    score_cf = defaultdict(lambda: [0, 0])   # разница → [попытки за, попытки против] при 5×5
    score_t = defaultdict(float)
    per_goals = Counter()
    n_assists = Counter()
    pp_entries = 0
    pp_goals_first = 0
    pen_dur = Counter()
    games = 0
    ot_games = so_games = 0
    home_wins = 0
    margins = Counter()
    one_goal = 0
    shifts_len = {"F": [], "D": []}
    shifts_n = {"F": [], "D": []}
    toi_rank = {"F": defaultdict(list), "D": defaultdict(list)}
    hits = takeaways = giveaways = 0
    season_pts = {}

    for season in SEASONS:
        pbp = gz(os.path.join(RAW, f"pbp_{season}.json.gz"))
        shf_path = os.path.join(RAW, f"shifts_{season}.json.gz")
        shf = gz(shf_path) if os.path.exists(shf_path) else {}
        gpts = Counter(); gg = Counter(); pos_all = {}
        for gid, g in pbp.items():
            if g.get("type") != 2:
                continue
            games += 1
            home_id = g["home_id"]
            team_of = {r[0]: r[1] for r in g["roster"]}
            pos = {r[0]: ("D" if r[2] == "D" else "G" if r[2] == "G" else "F") for r in g["roster"]}
            pos_all.update(pos)
            plays = g["plays"]
            hs = as_ = 0
            last = {}      # период → (t, sit)
            went_ot = so = False
            prev_lab = {True: "5v5", False: "5v5"}
            prev_att = None
            for p in plays:
                per, t, typ = p.get("per"), p.get("t"), p.get("type")
                if per is None or t is None:
                    continue
                if p.get("pt") == "SO":
                    so = True
                    continue
                if per >= 4:
                    went_ot = True
                sit = p.get("sit") or "1551"
                # время в составе: от прошлого события периода до этого
                if per in last:
                    t0, s0 = last[per]
                    dt = max(0, t - t0)
                    for home in (True, False):
                        own, opp, og, pg = state_of(s0, home)
                        lb = label(own, opp, og, pg)
                        if per >= 4 and lb == "3v3":
                            lb = "3v3"
                        T[lb] += dt
                        if lb == "5v5" and per <= 3:
                            diff = hs - as_ if home else as_ - hs
                            score_t[max(-3, min(3, diff))] += dt
                last[per] = (t, sit)
                # входы в большинство
                for home in (True, False):
                    own, opp, og, pg = state_of(sit, home)
                    lb = label(own, opp, og, pg)
                    if lb == "pp" and prev_lab[home] != "pp" and og == 1 and pg == 1:
                        pp_entries += 1
                    prev_lab[home] = lb
                d = p.get("d") or {}
                if typ in ATT:
                    shooter = d.get("scoringPlayerId") if typ == "goal" else d.get("shootingPlayerId")
                    tm = team_of.get(shooter)
                    if tm is None:
                        tm = d.get("eventOwnerTeamId")
                        if typ == "blocked-shot":
                            # во владельце события у блока — команда блокирующего
                            tm = None
                    if tm is None:
                        continue
                    home = tm == home_id
                    own, opp, og, pg = state_of(sit, home)
                    lb = label(own, opp, og, pg)
                    if per >= 4 and lb == "3v3":
                        lb = "3v3"
                    A[lb] += 1
                    if lb == "5v5" and per <= 3:
                        diff = hs - as_ if home else as_ - hs
                        score_cf[max(-3, min(3, diff))][0] += 1
                        score_cf[max(-3, min(3, -diff))][1] += 1
                    if lb == "5v5" and per <= 3 and typ == "goal":
                        diff = hs - as_ if home else as_ - hs
                        score_g[max(-3, min(3, diff))][0] += 1
                        score_g[max(-3, min(3, -diff))][1] += 1
                    x, y = d.get("xCoord"), d.get("yCoord")
                    if lb == "5v5" and x is not None and y is not None:
                        side = p.get("side")
                        if side in ("left", "right"):
                            right = (side == "left") if home else (side != "left")
                            nx = 89 if right else -89
                        else:
                            nx = 89 if x >= 0 else -89
                        if typ == "blocked-shot":
                            nx = -nx   # у блока координаты — с места блока, сторона атаки та же; зона по расстоянию примерно
                            nx = 89 if x >= 0 else -89
                        dist = ((abs(nx - x)) ** 2 + y * y) ** 0.5
                        zn = "close" if dist < 20 else "mid" if dist < 40 else "far"
                        z = zone[(pos.get(shooter, "F"), zn)]
                        z[0] += 1; z[1] += typ == "blocked-shot"; z[2] += typ == "missed-shot"; z[3] += typ in ("shot-on-goal", "goal"); z[4] += typ == "goal"
                    if lb == "5v5" and prev_att is not None and prev_att[0] == per and 0 <= t - prev_att[1] <= 3 and prev_att[2] == tm and typ != "blocked-shot":
                        reb_n[0] += 1; reb_n[1] += typ == "goal"
                    if typ in ("shot-on-goal", "missed-shot"):
                        prev_att = (per, t, tm)
                    else:
                        prev_att = None if typ == "goal" else prev_att
                    if typ == "blocked-shot":
                        B[lb] += 1
                    elif typ == "missed-shot":
                        M[lb] += 1
                    else:
                        S[lb] += 1
                        st = d.get("shotType") or "?"
                        if lb in ("5v5", "pp", "pk", "4v4", "3v3"):
                            stype[st][0] += 1
                            stype[st][1] += typ == "goal"
                    if typ == "goal":
                        G[lb] += 1
                        per_goals[min(per, 4)] += 1
                        na = (1 if d.get("assist1PlayerId") else 0) + (1 if d.get("assist2PlayerId") else 0)
                        n_assists[na] += 1
                        gg[shooter] += 1; gpts[shooter] += 1
                        for k in ("assist1PlayerId", "assist2PlayerId"):
                            if d.get(k):
                                gpts[d[k]] += 1
                        if home:
                            hs += 1
                        else:
                            as_ += 1
                elif typ == "penalty":
                    pen_dur[d.get("duration")] += 1
                elif typ == "hit":
                    hits += 1
                elif typ == "takeaway":
                    takeaways += 1
                elif typ == "giveaway":
                    giveaways += 1
            if went_ot:
                ot_games += 1
            if so:
                so_games += 1
            # итог: у буллитов победитель — по протоколу нет счёта, считаем по последнему событию period/ OT
            fin_h, fin_a = hs, as_
            if so:
                home_wins += 0.5  # без данных буллитов — половина (поправим ниже по доле)
            else:
                home_wins += fin_h > fin_a
                if not went_ot:
                    margins[min(abs(fin_h - fin_a), 6)] += 1
            # смены
            sh = shf.get(gid) or []
            per_player = defaultdict(lambda: [0, 0])
            for row in sh:
                pid, tm, per, a, b = row[:5]
                if per is None or per > 3 or b is None or a is None or b <= a:
                    continue
                ps = pos.get(pid)
                if ps not in ("F", "D"):
                    continue
                shifts_len[ps].append(b - a)
                per_player[pid][0] += b - a
                per_player[pid][1] += 1
            for tm in {home_id, g["away_id"]}:
                for ps, n in (("F", 12), ("D", 6)):
                    tois = sorted((v[0] for pid, v in per_player.items() if pos.get(pid) == ps and team_of.get(pid) == tm), reverse=True)
                    if len(tois) >= n:
                        for i, v in enumerate(tois[:n]):
                            toi_rank[ps][i].append(v / 60)
                    for pid, v in per_player.items():
                        if pos.get(pid) == ps and team_of.get(pid) == tm:
                            shifts_n[ps].append(v[1])
        # сезонные распределения (82 матча ≈ полный сезон, сокращённых сезонов в списке нет)
        sk = [pid for pid in gpts if pos_all.get(pid) != "G"]
        goals = sorted((gg[p] for p in sk), reverse=True)
        pts = sorted((gpts[p] for p in sk), reverse=True)
        season_pts[season] = {
            "goals_top1": goals[0], "goals_top10": sum(goals[:10]) / 10, "g30": sum(x >= 30 for x in goals), "g40": sum(x >= 40 for x in goals),
            "g50": sum(x >= 50 for x in goals), "pts_top1": pts[0], "pts_top10": sum(pts[:10]) / 10,
            "p60": sum(x >= 60 for x in pts), "p80": sum(x >= 80 for x in pts), "p100": sum(x >= 100 for x in pts),
        }

    tg = games * 2  # команда-матчей
    def per60(cnt, lb):
        return 3600 * cnt[lb] / T[lb] if T[lb] else 0
    states = {}
    for lb in ("5v5", "pp", "pk", "4v4", "3v3", "en_against", "en_for"):
        states[lb] = {
            "min_per_team_game": round(T[lb] / tg / 60, 3),
            "gf60": round(per60(G, lb), 3), "cf60": round(per60(A, lb), 2), "sf60": round(per60(S, lb), 2),
            "block_share": round(B[lb] / A[lb], 3) if A[lb] else 0, "miss_share": round(M[lb] / A[lb], 3) if A[lb] else 0,
            "sh_pct": round(G[lb] / S[lb], 4) if S[lb] else 0,
            "goals_per_team_game": round(G[lb] / tg, 3),
        }
    shot_types = {k: {"share": round(v[0] / sum(x[0] for x in stype.values()), 4), "sh_pct": round(v[1] / v[0], 4) if v[0] else 0}
                  for k, v in sorted(stype.items(), key=lambda kv: -kv[1][0]) if v[0] > 200}
    score = {str(k): {"cf_share": round(score_cf[k][0] / max(1, score_cf[k][0] + score_cf[k][1]), 4),
                      "time_share": round(score_t[k] / sum(score_t.values()), 4)} for k in sorted(score_t)}
    import statistics as stt
    out = {
        "games": games,
        "goals_per_game": round(sum(G.values()) / games, 3),
        "states": states,
        "pp_per_team_game": round(pp_entries / tg, 3),
        "pp_pct": round(G["pp"] / pp_entries, 4) if pp_entries else 0,
        "penalty_duration": {str(k): v for k, v in pen_dur.most_common()},
        "shot_types": shot_types,
        "score_effects_5v5": score,
        "score_goals_5v5": {str(k): round(score_g[k][0] / (score_t[k] / 60) * 60, 3) for k in sorted(score_t)},
        "zones_5v5": {f"{ps}_{zn}": {"share": round(v[0] / sum(x[0] for x in zone.values()), 4), "block": round(v[1] / v[0], 3), "miss": round(v[2] / v[0], 3),
                                      "sh_pct_on_goal": round(v[4] / max(1, v[3]), 4), "g_per_att": round(v[4] / v[0], 4)} for (ps, zn), v in sorted(zone.items())},
        "rebounds_5v5": {"share_of_unblocked": round(reb_n[0] / max(1, A["5v5"] - B["5v5"]), 4), "g_per_att": round(reb_n[1] / max(1, reb_n[0]), 4)},
        "goals_by_period": {str(k): round(v / games, 3) for k, v in sorted(per_goals.items())},
        "assists": {str(k): round(v / sum(n_assists.values()), 4) for k, v in sorted(n_assists.items())},
        "ot_share": round(ot_games / games, 4),
        "so_share_of_ot": round(so_games / max(1, ot_games), 4),
        "home_win_excl_so": round((home_wins - 0.5 * so_games) / (games - so_games), 4),
        "reg_margin": {str(k): round(v / sum(margins.values()), 4) for k, v in sorted(margins.items())},
        "shift_sec": {ps: {"mean": round(stt.mean(v), 1), "median": stt.median(v)} for ps, v in shifts_len.items()},
        "shifts_per_game": {ps: round(stt.mean(v), 1) for ps, v in shifts_n.items()},
        "toi_by_rank": {ps: [round(stt.mean(toi_rank[ps][i]), 2) for i in sorted(toi_rank[ps])] for ps in toi_rank},
        "per_game": {"hits": round(hits / games, 1), "takeaways": round(takeaways / games, 1), "giveaways": round(giveaways / games, 1)},
        "season_players": season_pts,
    }
    path = os.path.join(os.path.dirname(__file__), "engine_targets.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print(json.dumps(out, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
