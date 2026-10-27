#!/usr/bin/env python3
"""Модель игрока HM27: характеристики 1–20 из протоколов и карт смен (ТЗ «HM27 — модель игрока»).

Запуск:  python3 data/derive.py --raw raw --out data/derived.json.gz [--report data/review.md]

Вход (релиз raw-data, см. data/fetch_pbp.py): pbp_<сезон>.json.gz, shifts_<сезон>.json.gz, players.json.gz, edge.json.gz.

Шаги:
  1. Протоколы → броски (с координатами) и события; карты смен → стинты (отрезки с неизменным составом на льду).
  2. Своя модель xG (логистическая регрессия: дистанция, угол, тип броска, добивание, атака с ходу, численность).
  3. RAPM (гребневая регрессия по стинтам 5 на 5): вклад игрока в xG своей команды и соперника за 60 минут,
     с поправкой на партнёров, соперников и площадку хозяев. Лямбда — кросс-валидацией по матчам.
  4. Метрики игроков за сезоны с весами, поправка на арены (силовые, блоки, отборы, потери считают по-разному).
  5. Общий уровень игрока (RAPM, очки на 60, игровое время, доля сыгранных матчей).
  6. Каждая характеристика = уровень + отклонение. Отклонение сжато к априорному по объёму выборки
     (надёжность — по половинкам матчей, чёт/нечет) и ограничено по типу характеристики:
     прямое измерение — без ограничения, специализация — ±6, техника — от −6 до +3, «слабые» данные — ±2.
  7. Шкала: средний игрок НХЛ ≈ 11, 1 ед. z = SCALE ед. шкалы; у каждой характеристики есть погрешность (se).
"""
import argparse, gzip, json, math, os, sys, time
from collections import defaultdict

import numpy as np
import pandas as pd
from scipy import sparse
from sklearn.linear_model import LogisticRegression

SEASON_W = {"20262027": 1.0, "20252026": 1.0, "20242025": 0.8, "20232024": 0.64}
MID, SCALE = 11.0, 3.4          # шкала 1–20: середина и цена одного стандартного отклонения
REG_MIN = 400                   # «регулярный» игрок: от 400 взвешенных минут (полевые), 1000 (вратари)
NET_X = 89.0
SHOT_TYPES = ["wrist", "snap", "slap", "backhand", "tip-in", "deflected", "wrap-around"]
UNBLOCKED = ("shot-on-goal", "missed-shot", "goal")
ATTEMPTS = UNBLOCKED + ("blocked-shot",)

# тип характеристики → (нижняя, верхняя) граница отклонения от общего уровня, ед. шкалы; None — без ограничения.
#   direct — прямое измерение (EDGE, рост/вес); spec — специализация (вбрасывания, силовые, блоки…), меряется сама по себе;
#   tech — техника и мышление: не выше уровня +3 (игрок 4-го звена не обгонит звезду), не ниже уровня −6;
#   weak — малые выборки и слабый эффект (хладнокровие): уровень ±2; cons — стабильность, сама по себе.
CAPS = {"direct": None, "spec": None, "tech": (-6, 3), "weak": (-2, 2), "cons": None}

T0 = time.time()


def log(*a):
    print(f"[{time.time() - T0:6.0f} с]", *a, file=sys.stderr, flush=True)


def gz(path):
    with gzip.open(path, "rt", encoding="utf-8") as f:
        return json.load(f)


# ───────────────────────────── 1. разбор протоколов и смен ─────────────────────────────

def sit_counts(code):
    """situationCode «1551»: вратарь гостей, полевые гостей, полевые хозяев, вратарь хозяев."""
    try:
        return int(code[0]), int(code[1]), int(code[2]), int(code[3])
    except Exception:
        return 1, 5, 5, 1


def build_stints(shifts, pos):
    """Стинты матча: [(период, t0, t1, полевые хозяев/гостей, вратари)] по картам смен."""
    by_per = defaultdict(list)
    for pid, team, per, a, b in shifts:
        if per and per <= 4:
            by_per[per].append((pid, team, a, b))
    out = []
    for per in sorted(by_per):
        ev = defaultdict(lambda: ([], []))          # t → (кто уходит, кто выходит)
        for pid, team, a, b in by_per[per]:
            ev[a][1].append((pid, team)); ev[b][0].append((pid, team))
        times = sorted(ev)
        on = {}
        for i, t in enumerate(times[:-1]):
            for pid, team in ev[t][0]:
                on.pop(pid, None)
            for pid, team in ev[t][1]:
                on[pid] = team
            t1 = times[i + 1]
            if t1 > t and on:
                out.append((per, t, t1, dict(on)))
    return out


def attack_net(play, is_home, x):
    side = play.get("side")
    if side in ("left", "right"):
        home_attacks_right = side == "left"
        right = home_attacks_right if is_home else not home_attacks_right
        return NET_X if right else -NET_X
    return NET_X if (x or 0) >= 0 else -NET_X


def parse_game(gid, g, shifts, season, pidx):
    """Броски, события и стинты одного матча."""
    home, away = g["home_id"], g["away_id"]
    pos = {r[0]: r[2] for r in g["roster"]}
    team_of = {r[0]: r[1] for r in g["roster"]}
    shots, events = [], []
    prev = None
    for k, p in enumerate(g["plays"]):
        per, t, typ, d = p.get("per"), p.get("t"), p.get("type"), p.get("d") or {}
        if per is None or t is None or p.get("pt") == "SO":
            prev = None
            continue
        ag, an, hn, hg = sit_counts(p.get("sit") or "1551")
        if typ in ATTEMPTS:
            shooter = d.get("scoringPlayerId") if typ == "goal" else d.get("shootingPlayerId")
            team = team_of.get(shooter)
            if team is None:
                prev = p
                continue
            is_home = team == home
            x, y = d.get("xCoord"), d.get("yCoord")
            if x is None or y is None:
                prev = p
                continue
            nx = attack_net(p, is_home, x)
            dx, dy = abs(nx - x), abs(y)
            dist = math.hypot(dx, dy)
            ang = math.degrees(math.atan2(dy, dx)) if dx > 0 else 90.0
            own_sk, opp_sk = (hn, an) if is_home else (an, hn)
            opp_g = ag if is_home else hg
            reb = rush = 0
            if prev is not None and prev.get("per") == per and prev.get("t") is not None:
                dt = t - prev["t"]
                pd_ = prev.get("d") or {}
                if dt <= 3 and prev.get("type") in ATTEMPTS:
                    ps = pd_.get("shootingPlayerId") or pd_.get("scoringPlayerId")
                    if team_of.get(ps) == team:
                        reb = 1
                px = pd_.get("xCoord")
                if dt <= 4 and px is not None and (px * (1 if nx > 0 else -1)) < 25 and not reb:
                    rush = 1
            shots.append({
                "gid": gid, "season": season, "per": per, "t": t, "type": typ, "shooter": shooter, "team": team, "home": int(is_home),
                "goalie": d.get("goalieInNetId"), "blocker": d.get("blockingPlayerId"), "dist": dist, "ang": ang,
                "stype": d.get("shotType") or "", "reb": reb, "rush": rush, "own": own_sk, "opp": opp_sk, "en": int(opp_g == 0),
                "a1": d.get("assist1PlayerId"), "a2": d.get("assist2PlayerId"), "zone": d.get("zoneCode"), "reason": d.get("reason"),
                "close": 0,
            })
        elif typ in ("hit", "takeaway", "giveaway", "faceoff", "penalty"):
            row = {"gid": gid, "season": season, "per": per, "t": t, "type": typ, "venue": g["home"]}
            if typ == "hit":
                row["pid"], row["pid2"] = d.get("hittingPlayerId"), d.get("hitteePlayerId")
            elif typ in ("takeaway", "giveaway"):
                row["pid"] = d.get("playerId")
            elif typ == "faceoff":
                row["pid"], row["pid2"] = d.get("winningPlayerId"), d.get("losingPlayerId")
            else:
                row["pid"], row["pid2"] = d.get("committedByPlayerId"), d.get("drawnByPlayerId")
                row["pen"] = d.get("typeCode")
                row["dur"] = d.get("duration")
            events.append(row)
        prev = p
    # счёт перед броском — для «концовок при ничьей»
    hs = as_ = 0
    for s in shots:
        diff = hs - as_ if s["home"] else as_ - hs
        s["close"] = int(abs(diff) <= 1 and (s["per"] >= 3))
        if s["type"] == "goal":
            if s["home"]:
                hs += 1
            else:
                as_ += 1
    st = build_stints(shifts, pos) if shifts else []
    return shots, events, st, pos, team_of


# ───────────────────────────── 2. модель xG ─────────────────────────────

def xg_features(df):
    X = pd.DataFrame({
        "dist": df["dist"], "dist2": df["dist"] ** 2 / 100, "ldist": np.log1p(df["dist"]),
        "ang": df["ang"], "ang2": df["ang"] ** 2 / 100, "da": df["dist"] * df["ang"] / 100,
        "reb": df["reb"], "rush": df["rush"],
        "pp": ((df["own"] > df["opp"])).astype(int), "sh": ((df["own"] < df["opp"])).astype(int),
        "ot3": ((df["own"] == 3) & (df["opp"] == 3)).astype(int),
    })
    for t in SHOT_TYPES:
        X["st_" + t] = (df["stype"] == t).astype(int)
    return X


def fit_xg(shots):
    fen = shots[shots["type"].isin(UNBLOCKED) & (shots["en"] == 0)].copy()
    X, y = xg_features(fen), (fen["type"] == "goal").astype(int)
    m = LogisticRegression(max_iter=2000, C=10.0)
    m.fit(X.values, y.values)
    p = m.predict_proba(X.values)[:, 1]
    # качество: лог-лосс и AUC (простая оценка без sklearn.metrics, чтобы не тянуть лишнего)
    ll = -np.mean(y * np.log(p) + (1 - y) * np.log(1 - p))
    order = np.argsort(p)
    ranks = np.empty(len(p)); ranks[order] = np.arange(1, len(p) + 1)
    npos = y.sum(); auc = (ranks[y.values == 1].sum() - npos * (npos + 1) / 2) / (npos * (len(y) - npos))
    info = {"shots": int(len(fen)), "goals": int(npos), "logloss": round(float(ll), 4), "auc": round(float(auc), 4),
            "mean_xg": round(float(p.mean()), 4), "goal_rate": round(float(y.mean()), 4)}
    xg = np.zeros(len(shots))
    mask = shots["type"].isin(UNBLOCKED).values
    Xa = xg_features(shots[mask])
    xg[mask] = m.predict_proba(Xa.values)[:, 1]
    en = mask & (shots["en"].values == 1)
    xg[en] = 0.6     # пустые ворота: условная вероятность, в метрики игроков не входят
    return xg, info


# ───────────────────────────── 3. RAPM ─────────────────────────────

def rapm(rows_off, rows_def, y, w, home, folds, P, lambdas):
    """Гребневая регрессия: y (за 60) ~ нападение + оборона + площадка.
    Нормальные уравнения считаются по фолдам один раз, кросс-валидация — вычитанием фолда.
    Возвращает (off, def, лучшая лямбда, ошибки CV, эффект площадки)."""
    n = len(y)
    lens = np.array([len(o) + len(d) + (1 if h else 0) for o, d, h in zip(rows_off, rows_def, home)])
    r_idx = np.repeat(np.arange(n), lens)
    c_idx = np.fromiter((c for o, d, h in zip(rows_off, rows_def, home) for c in (*o, *(P + q for q in d), *((2 * P,) if h else ()))),
                        np.int64, count=int(lens.sum()))
    X = sparse.csr_matrix((np.ones(len(r_idx)), (r_idx, c_idx)), shape=(n, 2 * P + 1))
    w = np.asarray(w, float); y = np.asarray(y, float)
    parts = []
    for f in range(5):
        m = folds == f
        Xm, ym, wm = X[m], y[m], w[m]
        Wx = Xm.multiply(wm[:, None]).tocsr()
        parts.append({"A": (Xm.T @ Wx).toarray(), "c": Xm.T @ (wm * ym), "d": Xm.T @ wm,
                      "sw": wm.sum(), "swy": (wm * ym).sum(), "swyy": (wm * ym * ym).sum()})
    tot = {k: sum(p_[k] for p_ in parts) for k in parts[0]}

    def solve(t, lam):
        mu = t["swy"] / t["sw"]
        pen = np.full(2 * P + 1, lam); pen[-1] = 1e-6
        beta = np.linalg.solve(t["A"] + np.diag(pen), t["c"] - mu * t["d"])
        return beta, mu

    errs = {}
    for lam in lambdas:
        e = 0.0
        for f in range(5):
            tr = {k: tot[k] - parts[f][k] for k in tot}
            beta, mu = solve(tr, lam)
            te = parts[f]
            sse0 = te["swyy"] - 2 * mu * te["swy"] + mu * mu * te["sw"]
            cross = beta @ (te["c"] - mu * te["d"])
            e += sse0 - 2 * cross + beta @ te["A"] @ beta
        errs[lam] = e / tot["sw"]
    best = min(errs, key=errs.get)
    beta, mu = solve(tot, best)
    return beta[:P], beta[P:2 * P], best, {str(k): round(float(v), 4) for k, v in errs.items()}, float(beta[-1])


# ───────────────────────────── 4–6. метрики и шкала ─────────────────────────────

def split_half_k(per_game, value_col, expo_col, min_expo):
    """Константа сжатия k (в единицах экспозиции) по надёжности половинок (чёт/нечет номера матча)."""
    g = per_game.copy()
    g["half"] = g["gid"].astype(np.int64) % 2
    agg = g.groupby(["pid", "half"])[[value_col, expo_col]].sum().unstack("half")
    agg = agg.dropna()
    e0, e1 = agg[(expo_col, 0)], agg[(expo_col, 1)]
    ok = (e0 >= min_expo) & (e1 >= min_expo)
    if ok.sum() < 30:
        return None, None
    r0 = agg[(value_col, 0)][ok] / e0[ok]
    r1 = agg[(value_col, 1)][ok] / e1[ok]
    r = float(np.corrcoef(r0, r1)[0, 1])
    if not r > 0.01:
        return 1e9, r
    nbar = float(((e0 + e1)[ok] / 2).mean())
    return nbar * (1 - r) / r, r


class Scale:
    """Перевод метрики в z относительно «регулярных» игроков группы с учётом шума выборки."""

    def __init__(self, x, n, k, groups, reg, zG, sign=1.0, prior_on_level=True):
        self.x, self.n, self.k = x, n, k
        z_obs = pd.Series(np.nan, index=x.index)
        z_post = pd.Series(np.nan, index=x.index)
        se = pd.Series(np.nan, index=x.index)
        self.beta = {}
        for gname, idx in groups.items():
            idx = [i for i in idx if i in x.index]
            known = [i for i in idx if pd.notna(x[i])]
            if not known:
                continue
            regs = [i for i in known if reg.get(i)]
            if len(regs) < 10:
                regs = known
            wr_ = n[regs].clip(lower=1)
            mu = np.average(x[regs], weights=wr_)
            sd_obs = float(np.sqrt(np.average((x[regs] - mu) ** 2, weights=wr_)))
            nbar = float(n[regs].mean())
            rel = nbar / (nbar + k) if k < 1e8 else 0.0
            sd_true = max(sd_obs * math.sqrt(max(rel, 0.05)), 1e-9)
            xr = x[idx].fillna(mu)
            nr = n[idx].where(x[idx].notna(), 0).fillna(0)
            zo = sign * (xr - mu) / sd_true
            z_obs[idx] = zo
            # априорное значение: доля общего уровня (наклон по регулярным игрокам)
            if prior_on_level and zG is not None and len(regs) > 10:
                zr = zo[regs]; gr = zG.reindex(regs).fillna(0)
                beta = float(np.clip(np.cov(zr, gr)[0, 1] / max(np.var(gr), 1e-9), 0, 1))
            else:
                beta = 0.0
            self.beta[gname] = round(beta, 3)
            prior = beta * zG.reindex(idx).fillna(0) if zG is not None else 0.0
            wgt = nr / (nr + k) if k < 1e8 else nr * 0
            z_post[idx] = wgt * zo + (1 - wgt) * prior
            se[idx] = np.sqrt((1 - wgt) * max(1 - beta ** 2, 0.2))
        self.z, self.se, self.z_obs = z_post, se, z_obs


def soft(u):
    """Мягкие края шкалы: 20 и 1 достижимы только асимптотически, середина линейна."""
    u = np.asarray(u, float)
    hi, lo = 15.0, 5.0
    out = u.copy()
    out = np.where(u > hi, hi + 5.0 * np.tanh((u - hi) / 5.0), out)
    out = np.where(u < lo, lo - 4.0 * np.tanh((lo - u) / 4.0), out)
    return out


def to_scale(z):
    if isinstance(z, pd.Series):
        return pd.Series(soft(MID + SCALE * z.values), index=z.index)
    return soft(MID + SCALE * z)


def combine(level_v, z, se, kind):
    """Характеристика = уровень + ограниченное отклонение."""
    v = to_scale(z)
    cap = CAPS[kind]
    if cap is not None:
        v = level_v + (v - level_v).clip(cap[0], cap[1])
    return v.clip(1, 20), (SCALE * se).clip(0.4, 3.5)


# ───────────────────────────── основной расчёт ─────────────────────────────

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", default="raw")
    ap.add_argument("--out", default="data/derived.json.gz")
    ap.add_argument("--report", default=None)
    ap.add_argument("--extra", default="data/extra.json.gz", help="MoneyPuck (data/fetch_extra.py) — второе мнение по вратарям")
    ap.add_argument("--seasons", nargs="+", default=list(SEASON_W))
    a = ap.parse_args()
    meta = {"built": time.strftime("%Y-%m-%d"), "seasons": a.seasons, "scale": {"mid": MID, "sd": SCALE}}

    pages = gz(os.path.join(a.raw, "players.json.gz"))
    edge = gz(os.path.join(a.raw, "edge.json.gz")) if os.path.exists(os.path.join(a.raw, "edge.json.gz")) else {}
    pos_of = {int(k): v.get("position") for k, v in pages.items()}

    # ── протоколы и смены ──
    all_shots, all_events, stint_rows, pg_rows, games = [], [], [], [], []
    for season in a.seasons:
        p_path = os.path.join(a.raw, f"pbp_{season}.json.gz")
        if not os.path.exists(p_path):
            log(f"{season}: нет протоколов"); continue
        pbp = gz(p_path)
        sh = gz(os.path.join(a.raw, f"shifts_{season}.json.gz"))
        sw = SEASON_W.get(season, 0.5)
        log(f"{season}: матчей {len(pbp)}, смен {len(sh)}")
        for gid, g in pbp.items():
            shots, events, st, pos, team_of = parse_game(gid, g, sh.get(gid), season, None)
            all_shots += shots; all_events += events
            games.append({"gid": gid, "season": season, "type": g.get("type"), "date": g.get("date"),
                          "home": g["home"], "away": g["away"], "home_id": g["home_id"], "away_id": g["away_id"], "has_shifts": bool(st)})
            hid = g["home_id"]
            # время игроков по сменам (все ситуации)
            for pid, team, per, a_, b_ in (sh.get(gid) or []):
                if per and per <= 4:
                    pg_rows.append((gid, season, pid, team, b_ - a_))
            for per, t0, t1, on in st:
                hs = [p for p, tm in on.items() if tm == hid and pos.get(p) != "G"]
                as_ = [p for p, tm in on.items() if tm != hid and pos.get(p) != "G"]
                hgk = [p for p, tm in on.items() if tm == hid and pos.get(p) == "G"]
                agk = [p for p, tm in on.items() if tm != hid and pos.get(p) == "G"]
                stint_rows.append((gid, season, sw, per, t0, t1, tuple(hs[:6]), tuple(as_[:6]), hgk[0] if hgk else 0, agk[0] if agk else 0))
        del pbp, sh
    shots = pd.DataFrame(all_shots); events = pd.DataFrame(all_events)
    games = pd.DataFrame(games).set_index("gid")
    del all_shots, all_events
    log(f"бросков {len(shots)}, событий {len(events)}, стинтов {len(stint_rows)}")

    # ── xG ──
    shots["xg"], meta["xg"] = fit_xg(shots)
    shots["g"] = (shots["type"] == "goal").astype(int)
    shots["w"] = shots["season"].map(SEASON_W).fillna(0.5)
    log("xG:", meta["xg"])

    # ── стинты: суммы xG/бросков/голов по командам ──
    st = pd.DataFrame(stint_rows, columns=["gid", "season", "sw", "per", "t0", "t1", "hs", "aw", "hg", "ag"])
    del stint_rows
    st["dur"] = st["t1"] - st["t0"]
    st["nh"], st["na"] = st["hs"].map(len), st["aw"].map(len)
    st["key0"], st["key1"] = st["per"] * 10000 + st["t0"], st["per"] * 10000 + st["t1"]
    st = st.sort_values(["gid", "key0"]).reset_index(drop=True)
    shots["skey"] = shots["per"] * 10000 + shots["t"]
    # быстрый поиск стинта: по каждому матчу — searchsorted по концам
    st_by_game = {gid: (grp.index.values, grp["key0"].values, grp["key1"].values) for gid, grp in st.groupby("gid")}
    sidx = np.full(len(shots), -1)
    for gid, grp in shots.groupby("gid"):
        if gid not in st_by_game:
            continue
        ids, k0, k1 = st_by_game[gid]
        keys = grp["skey"].values
        j = np.searchsorted(k1, keys, side="left")
        j = np.clip(j, 0, len(k1) - 1)
        ok = (k0[j] < keys) | ((k0[j] == keys) & (keys % 10000 == 0))
        ok &= k1[j] >= keys
        sidx[grp.index.values[ok]] = ids[j[ok]]
    shots["stint"] = sidx
    log(f"броски со стинтом: {np.mean(sidx >= 0):.3f}")

    fen = shots["type"].isin(UNBLOCKED) & (shots["en"] == 0)
    sm = shots[shots["stint"] >= 0]
    for side, hflag in (("h", 1), ("a", 0)):
        mine = sm[sm["home"] == hflag]
        st[f"xg_{side}"] = mine[mine["type"].isin(UNBLOCKED)].groupby("stint")["xg"].sum().reindex(st.index).fillna(0)
        st[f"cf_{side}"] = mine.groupby("stint").size().reindex(st.index).fillna(0)
        st[f"gf_{side}"] = mine[mine["type"] == "goal"].groupby("stint").size().reindex(st.index).fillna(0)

    # ── индекс игроков ──
    pids = sorted({p for t in st["hs"] for p in t} | {p for t in st["aw"] for p in t})
    P = len(pids); pidx = {p: i for i, p in enumerate(pids)}
    log(f"полевых в стинтах: {P}")

    # ── RAPM 5 на 5 (xG и попытки бросков) ──
    ev = st[(st["nh"] == 5) & (st["na"] == 5) & (st["hg"] > 0) & (st["ag"] > 0) & (st["dur"] > 0) & (st["per"] <= 3)]
    fold_of = {g: i % 5 for i, g in enumerate(sorted(ev["gid"].unique()))}
    off, dfn, home, ys_xg, ys_cf, ws, folds = [], [], [], [], [], [], []
    for r in ev.itertuples():
        h = [pidx[p] for p in r.hs]; aw = [pidx[p] for p in r.aw]
        f = fold_of[r.gid]
        for o, d, hm, xg_, cf_ in ((h, aw, 1, r.xg_h, r.cf_h), (aw, h, 0, r.xg_a, r.cf_a)):
            off.append(o); dfn.append(d); home.append(hm)
            ys_xg.append(xg_ * 3600 / r.dur); ys_cf.append(cf_ * 3600 / r.dur)
            ws.append(r.dur * r.sw); folds.append(f)
    folds = np.array(folds)
    lam_grid = [4000, 8000, 16000, 32000, 64000, 128000]
    xo, xd, lam, errs, hxg = rapm(off, dfn, ys_xg, ws, home, folds, P, lam_grid)
    co, cd, lam_cf, _, _ = rapm(off, dfn, ys_cf, ws, home, folds, P, [lam])
    meta["rapm"] = {"stints": int(len(ev)), "lambda": lam, "cv": errs, "home_xg60": round(hxg, 3)}
    log("RAPM:", meta["rapm"])
    del off, dfn, home, ys_xg, ys_cf, ws

    # ── время на льду и ситуации по игрокам ──
    flat_cache = {}
    for col in ("hs", "aw"):
        lens = st[col].map(len).values
        flat_cache[col] = (np.fromiter((pidx[p] for t in st[col] for p in t), np.int64, count=int(lens.sum())),
                           np.repeat(np.arange(len(st)), lens))

    def flat(cols, strength_mask, vals):
        """Суммы по игрокам: для каждого стинта (в маске) всем игрокам колонки cols добавить vals."""
        m_ = np.asarray(strength_mask)
        out = {}
        for name, v in vals.items():
            acc_ = np.zeros(P)
            for col in cols:
                fp, so = flat_cache[col]
                sel = m_[so]
                acc_ += np.bincount(fp[sel], weights=v(st, col)[so[sel]], minlength=P)
            out[name] = acc_
        return out

    is5 = (st["nh"] == 5) & (st["na"] == 5) & (st["hg"] > 0) & (st["ag"] > 0)
    pp_h = (st["nh"] == 5) & (st["na"] == 4) & (st["hg"] > 0) & (st["ag"] > 0)
    pp_a = (st["nh"] == 4) & (st["na"] == 5) & (st["hg"] > 0) & (st["ag"] > 0)
    acc = {}

    def add(d):
        for k, v in d.items():
            acc[k] = acc.get(k, 0) + v

    own = lambda c: (lambda s, col: (s[c + ("_h" if col == "hs" else "_a")] * s["sw"]).values)
    opp = lambda c: (lambda s, col: (s[c + ("_a" if col == "hs" else "_h")] * s["sw"]).values)
    dur = lambda s, col: (s["dur"] * s["sw"]).values
    add(flat(["hs", "aw"], is5, {"toi5": dur, "xgf5": own("xg"), "xga5": opp("xg"), "cf5": own("cf"), "ca5": opp("cf")}))
    # большинство: свои 5 на 4; меньшинство: свои 4 на 5
    add({k: v for k, v in flat(["hs"], pp_h, {"toipp": dur, "xgfpp": own("xg")}).items()})
    add({k: v for k, v in flat(["aw"], pp_a, {"toipp": dur, "xgfpp": own("xg")}).items()})
    add({k: v for k, v in flat(["aw"], pp_h, {"toipk": dur, "xgapk": opp("xg")}).items()})
    add({k: v for k, v in flat(["hs"], pp_a, {"toipk": dur, "xgapk": opp("xg")}).items()})
    # командные итоги в большинстве/меньшинстве по сезонам и командам — для «относительно команды»
    team_pp = defaultdict(lambda: [0.0, 0.0])
    team_pk = defaultdict(lambda: [0.0, 0.0])
    for r in st[pp_h].itertuples():
        team_pp[(r.season, games.at[r.gid, "home"])][0] += r.dur * r.sw; team_pp[(r.season, games.at[r.gid, "home"])][1] += r.xg_h * r.sw
        team_pk[(r.season, games.at[r.gid, "away"])][0] += r.dur * r.sw; team_pk[(r.season, games.at[r.gid, "away"])][1] += r.xg_h * r.sw
    for r in st[pp_a].itertuples():
        team_pp[(r.season, games.at[r.gid, "away"])][0] += r.dur * r.sw; team_pp[(r.season, games.at[r.gid, "away"])][1] += r.xg_a * r.sw
        team_pk[(r.season, games.at[r.gid, "home"])][0] += r.dur * r.sw; team_pk[(r.season, games.at[r.gid, "home"])][1] += r.xg_a * r.sw
    lg_pp = sum(v[1] for v in team_pp.values()) / max(sum(v[0] for v in team_pp.values()), 1) * 3600
    lg_pk = lg_pp
    S = pd.DataFrame(acc, index=pids)
    S["rapm_xo"], S["rapm_xd"] = xo, -xd           # оборона: меньше xG соперника → больше
    S["rapm_co"], S["rapm_cd"] = co, -cd
    S["rapm_x"] = S["rapm_xo"] + S["rapm_xd"]
    S["rapm_c"] = S["rapm_co"] + S["rapm_cd"]

    # ── игровое время по матчам (все ситуации) и сыгранные матчи ──
    pg = pd.DataFrame(pg_rows, columns=["gid", "season", "pid", "team", "sec"])
    del pg_rows
    pg = pg.groupby(["gid", "season", "pid", "team"], as_index=False)["sec"].sum()
    pg["w"] = pg["season"].map(SEASON_W).fillna(0.5)
    pg["type"] = pg["gid"].map(games["type"])
    S["toi"] = (pg.assign(x=pg["sec"] * pg["w"]).groupby("pid")["x"].sum()).reindex(S.index).fillna(0)
    S["gp_w"] = pg.groupby("pid")["w"].sum().reindex(S.index).fillna(0)
    S["toi_min"] = S["toi"] / 60
    S["toi_gp"] = S["toi_min"] / S["gp_w"].clip(lower=0.1)
    # доля сыгранных матчей регулярки за команду (по сезонам, с весами)
    reg = pg[pg["type"] == 2]
    team_games = games[games["type"] == 2].reset_index()
    tg = pd.concat([team_games[["season", "home_id"]].rename(columns={"home_id": "team"}),
                    team_games[["season", "away_id"]].rename(columns={"away_id": "team"})]).groupby(["season", "team"]).size()
    pst = reg.groupby(["pid", "season", "team"]).size().rename("gp").reset_index()
    pst["tg"] = [tg.get((s, t), 82) for s, t in zip(pst["season"], pst["team"])]
    pst["w"] = pst["season"].map(SEASON_W).fillna(0.5)
    # доля сыгранных матчей в сезоне (по всем командам сезона)
    ps = pst.groupby(["pid", "season"]).agg(gp=("gp", "sum"), w=("w", "first")).reset_index()
    full = {s: games[(games["season"] == s) & (games["type"] == 2)].shape[0] * 2 / 32 for s in a.seasons}
    ps["share"] = ps.apply(lambda r: min(1.0, r["gp"] / max(full.get(r["season"], 82), 1)), axis=1)
    S["gp_share"] = (ps.assign(x=ps["share"] * ps["w"]).groupby("pid")["x"].sum() / ps.groupby("pid")["w"].sum()).reindex(S.index).fillna(0)

    # ── индивидуальные события: поправка на арены ──
    events["w"] = events["season"].map(SEASON_W).fillna(0.5)
    venue_f = {}
    for typ in ("hit", "takeaway", "giveaway", "blk"):
        if typ == "blk":
            e = shots[shots["type"] == "blocked-shot"].assign(venue=shots["gid"].map(games["home"]))
        else:
            e = events[events["type"] == typ]
        per_game = e.groupby(["season", "venue", "gid"]).size().groupby(["season", "venue"]).mean()
        lg = e.groupby(["season", "gid"]).size().groupby("season").mean()
        f = {(s, v): float(np.clip(per_game[(s, v)] / lg[s], 0.5, 2.0)) for s, v in per_game.index}
        venue_f[typ] = f
    meta["arena"] = {t: {f"{s}:{v}": round(x, 2) for (s, v), x in sorted(f.items()) if abs(x - 1) > 0.25} for t, f in venue_f.items()}

    def per_player_game(df, pid_col, weight_fn=None, name="n"):
        df = df[df[pid_col].notna()]
        d = df[[pid_col, "gid", "season"]].copy()
        d[pid_col] = d[pid_col].astype(np.int64)
        d["v"] = weight_fn(df) if weight_fn is not None else 1.0
        d["v"] = d["v"] * d["season"].map(SEASON_W).fillna(0.5)
        return d.rename(columns={pid_col: "pid"}).groupby(["pid", "gid"], as_index=False)["v"].sum().rename(columns={"v": name})

    venue_of = games["home"]

    def arena_w(typ):
        return lambda df: 1.0 / np.array([venue_f[typ].get((s, venue_of[g]), 1.0) for s, g in zip(df["season"], df["gid"])])

    pgw = pg.assign(expo=pg["sec"] / 60 * pg["w"])[["pid", "gid", "expo"]]
    counts = {
        "hits": per_player_game(events[events["type"] == "hit"], "pid", arena_w("hit")),
        "tk": per_player_game(events[events["type"] == "takeaway"], "pid", arena_w("takeaway")),
        "gv": per_player_game(events[events["type"] == "giveaway"], "pid", arena_w("giveaway")),
        "blk": per_player_game(shots[(shots["type"] == "blocked-shot") & (shots["reason"] != "teammate-blocked")].dropna(subset=["blocker"]).astype({"blocker": "int64"}), "blocker", arena_w("blk")),
        "fow": per_player_game(events[events["type"] == "faceoff"].dropna(subset=["pid"]), "pid"),
        "fol": per_player_game(events[events["type"] == "faceoff"].dropna(subset=["pid2"]).astype({"pid2": "int64"}), "pid2"),
        "pen": per_player_game(events[(events["type"] == "penalty") & (events["dur"].fillna(2) <= 4)].dropna(subset=["pid"]), "pid"),
        "pend": per_player_game(events[(events["type"] == "penalty")].dropna(subset=["pid2"]).astype({"pid2": "int64"}), "pid2"),
        "fight": per_player_game(events[(events["type"] == "penalty") & (events["dur"].fillna(0) >= 5)].dropna(subset=["pid"]), "pid"),
    }
    fen_ok = shots[fen]
    counts["g"] = per_player_game(shots[(shots["type"] == "goal") & (shots["en"] == 0)], "shooter")
    counts["g_all"] = per_player_game(shots[shots["type"] == "goal"], "shooter")
    counts["a1"] = per_player_game(shots[shots["type"] == "goal"].dropna(subset=["a1"]).astype({"a1": "int64"}), "a1")
    counts["a2"] = per_player_game(shots[shots["type"] == "goal"].dropna(subset=["a2"]).astype({"a2": "int64"}), "a2")
    counts["ixg"] = per_player_game(fen_ok, "shooter", lambda d: d["xg"])
    counts["fsh"] = per_player_game(fen_ok, "shooter")
    counts["gax"] = per_player_game(fen_ok, "shooter", lambda d: d["g"] - d["xg"])
    lng = fen_ok[fen_ok["dist"] >= 35]
    counts["lng_n"] = per_player_game(lng, "shooter")
    counts["lng_on"] = per_player_game(lng, "shooter", lambda d: (d["type"] != "missed-shot").astype(float))
    net = fen_ok[(fen_ok["dist"] < 20) | fen_ok["stype"].isin(["tip-in", "deflected"]) | (fen_ok["reb"] == 1)]
    counts["net_xg"] = per_player_game(net, "shooter", lambda d: d["xg"])
    clutch = fen_ok[(fen_ok["close"] == 1)]
    counts["cl_gax"] = per_player_game(clutch, "shooter", lambda d: d["g"] - d["xg"])
    counts["cl_n"] = per_player_game(clutch, "shooter")

    PG = pgw.copy()
    for name, c in counts.items():
        PG = PG.merge(c.rename(columns={"n": name}), on=["pid", "gid"], how="left")
    PG = PG.fillna(0)
    tot = PG.groupby("pid").sum(numeric_only=True)
    for c in counts:
        S[c] = tot[c].reindex(S.index).fillna(0)
    S["expo"] = tot["expo"].reindex(S.index).fillna(0)

    # ── позиции и группы ──
    S["pos"] = [pos_of.get(p) or "F" for p in S.index]
    S["grp"] = np.where(S["pos"] == "D", "D", "F")
    reg = (S["toi_min"] >= REG_MIN).to_dict()
    groups = {g: list(S.index[S["grp"] == g]) for g in ("F", "D")}
    per60 = lambda c: S[c] / S["toi_min"].clip(lower=1) * 60

    ks = {}

    def k_for(name, value_col, expo_col="expo", min_expo=150, df=None):
        k, r = split_half_k(df if df is not None else PG, value_col, expo_col, min_expo)
        ks[name] = {"k": None if k is None else round(k, 1), "r_half": None if r is None else round(r, 3)}
        return k if k is not None else 1e9

    # ── общий уровень ──
    S["pts"] = S["g_all"] + S["a1"] + S["a2"]
    PG["pts"] = PG["g_all"] + PG["a1"] + PG["a2"]
    k_pts = k_for("pts60", "pts")
    zp = Scale(per60("pts"), S["toi_min"], k_pts, groups, reg, None, prior_on_level=False).z
    zr = Scale(S["rapm_x"], S["toi5"] / 60, 1e-6, groups, reg, None, prior_on_level=False).z      # уже сжат гребнем
    zt = Scale(S["toi_gp"], S["gp_w"] * 10, 1e-6, groups, reg, None, prior_on_level=False).z
    zs = Scale(S["gp_share"], S["gp_w"] * 10, 1e-6, groups, reg, None, prior_on_level=False).z
    WF, WD = (0.32, 0.33, 0.22, 0.13), (0.36, 0.20, 0.30, 0.14)
    zG = pd.Series(0.0, index=S.index)
    for g, (wr, wp, wt, wsh) in (("F", WF), ("D", WD)):
        idx = groups[g]
        raw = wr * zr[idx] + wp * zp[idx] + wt * zt[idx] + wsh * zs[idx]
        regs = [i for i in idx if reg[i]]
        zG[idx] = (raw - raw[regs].mean()) / raw[regs].std()
    S["level"] = to_scale(zG).clip(1, 20)
    S["level_se"] = (SCALE * np.sqrt(500 / (S["toi_min"] + 500))).clip(0.6, 3.0)

    # ── характеристики полевых ──
    A = {}

    def attr(key, kind, x, n, k, sign=1.0, prior=True, grp=None):
        sc = Scale(x, n, k, grp or groups, reg, zG, sign, prior)
        v, se = combine(S["level"], sc.z, sc.se, kind)
        A[key] = {"v": v, "se": se, "beta": sc.beta, "kind": kind}

    # атака
    attr("fin", "tech", S["gax"] / S["fsh"].clip(lower=1), S["fsh"], k_for("fin", "gax", "fsh", 25))
    pwr = pd.Series({p: _edge_mean(edge, p, "shot") for p in S.index})
    attr("pwr", "direct", pwr.astype(float), pd.Series(np.where(pwr.notna(), 1e6, 0), index=S.index), 1e-6,
         grp={"all": list(S.index)})
    attr("lng", "spec", S["lng_on"] / S["lng_n"].clip(lower=1), S["lng_n"], k_for("lng", "lng_on", "lng_n", 20))
    attr("net", "spec", per60("net_xg"), S["toi_min"], k_for("net", "net_xg"))
    attr("pas", "tech", per60("a1"), S["toi_min"], k_for("pas", "a1"))
    attr("hnd", "tech", S["rapm_c"], S["toi5"] / 60, 1e-6)
    fo_n = S["fow"] + S["fol"]
    PG["fo_n"] = PG["fow"] + PG["fol"]
    fo_grp = {"C": list(S.index[S["pos"] == "C"]), "W": list(S.index[S["pos"].isin(["L", "R"])]), "D": groups["D"]}
    attr("fo", "spec", S["fow"] / fo_n.clip(lower=1), fo_n, k_for("fo", "fow", "fo_n", 60), prior=False, grp=fo_grp)
    # оборона
    attr("dfn", "tech", S["rapm_xd"], S["toi5"] / 60, 1e-6)
    attr("tka", "spec", per60("tk"), S["toi_min"], k_for("tka", "tk"))
    attr("blk", "spec", per60("blk"), S["toi_min"], k_for("blk", "blk"))
    attr("hit", "spec", per60("hits"), S["toi_min"], k_for("hit", "hits"))
    pk_rel = (S["xgapk"] / S["toipk"].clip(lower=1) * 3600)
    attr("pk", "spec", pk_rel, S["toipk"] / 60, 60.0, sign=-1.0, prior=False)
    # физика
    spd = pd.Series({p: _edge_mean(edge, p, "speed") for p in S.index})
    attr("spd", "direct", spd.astype(float), pd.Series(np.where(spd.notna(), 1e6, 0), index=S.index), 1e-6, grp={"all": list(S.index)})
    bur = pd.Series({p: _edge_rate(edge, p, "burstsOver20") for p in S.index})
    attr("acc", "direct", bur.astype(float), pd.Series(np.where(bur.notna(), 1e6, 0), index=S.index), 1e-6, grp={"all": list(S.index)})
    size = pd.Series({p: _size(pages.get(str(p))) for p in S.index}).astype(float)
    zsize = (size - size.mean()) / size.std()
    zhit = Scale(per60("hits"), S["toi_min"], ks.get("hit", {}).get("k") or 300, {"all": list(S.index)}, reg, None, prior_on_level=False).z
    strength = 0.65 * zsize.fillna(0) + 0.35 * zhit.fillna(0)
    attr("str", "spec", strength, pd.Series(1e6, index=S.index), 1e-6, grp={"all": list(S.index)})
    attr("sta", "spec", S["toi_gp"], S["gp_w"] * 10, 1e-6)
    # мышление
    PG["tkgv"] = PG["tk"] + PG["gv"]
    # решения: влияние на xG (RAPM) + отборы к потерям + качество выбора броска (xG на бросок)
    z_tkgv = Scale(S["tk"] / (S["tk"] + S["gv"]).clip(lower=1), S["tk"] + S["gv"], k_for("dec_tkgv", "tk", "tkgv", 10), groups, reg, None, prior_on_level=False).z
    PG["ixg_"] = PG["ixg"]
    z_sel = Scale(S["ixg"] / S["fsh"].clip(lower=1), S["fsh"], k_for("dec_sel", "ixg", "fsh", 25), groups, reg, None, prior_on_level=False).z
    attr("dec", "tech", 0.5 * zr.fillna(0) + 0.25 * z_tkgv.fillna(0) + 0.25 * z_sel.fillna(0), pd.Series(1e6, index=S.index), 1e-6)
    attr("vis", "tech", S["rapm_xo"], S["toi5"] / 60, 1e-6)
    attr("dis", "spec", per60("pen"), S["toi_min"], k_for("dis", "pen"), sign=-1.0, prior=False)
    dist = pd.Series({p: _edge_rate(edge, p, "dist") for p in S.index}).astype(float)
    zwork = (Scale(per60("blk") + per60("hits") + per60("tk"), S["toi_min"], 300, groups, reg, None, prior_on_level=False).z.fillna(0) * 0.7
             + ((dist - dist.mean()) / dist.std()).fillna(0) * 0.3)
    attr("wrk", "spec", zwork, pd.Series(1e6, index=S.index), 1e-6)
    ast_share = (S["a1"] + S["a2"]) / S["pts"].clip(lower=1)
    attr("tm", "tech", 0.6 * zr.fillna(0) + 0.4 * Scale(ast_share, S["pts"], 20, groups, reg, None, prior_on_level=False).z.fillna(0),
         pd.Series(1e6, index=S.index), 1e-6)
    attr("pp", "spec", S["xgfpp"] / S["toipp"].clip(lower=1) * 3600, S["toipp"] / 60, 60.0)
    attr("cmp", "weak", S["cl_gax"] / S["cl_n"].clip(lower=1), S["cl_n"], k_for("cmp", "cl_gax", "cl_n", 10))
    # стабильность: разброс игровой оценки от матча к матчу
    gs = PG.assign(gs=0.75 * PG["g_all"] + 0.7 * PG["a1"] + 0.55 * PG["a2"] + 0.05 * PG["blk"] + 0.15 * PG["pend"] - 0.15 * PG["pen"]
                   + 0.01 * PG["fow"] - 0.01 * PG["fol"] + 0.075 * PG["fsh"])
    gstat = gs.groupby("pid")["gs"].agg(["mean", "std", "count"])
    cv = gstat["std"] / gstat["mean"].clip(lower=0.25)          # коэффициент вариации игровой оценки
    attr("con", "cons", cv.reindex(S.index), gstat["count"].reindex(S.index).fillna(0), 120.0, sign=-1.0, prior=True)
    meta["reliability"] = ks

    # ── вратари ──
    G = goalies(shots, pg, pages, games, a.extra)

    # ── факты карьеры (скрытые качества) ──
    facts = {p: career_facts(pages.get(str(p))) for p in list(S.index) + list(G.index)}
    avail = availability(pg, games)

    # ── выход ──
    out = {"meta": meta, "skaters": {}, "goalies": {}}
    for p in S.index:
        pg_ = pages.get(str(p)) or {}
        out["skaters"][str(p)] = {
            "name": _name(pg_), "pos": S.at[p, "pos"], "age": _age(pg_),
            "toi": round(float(S.at[p, "toi_min"]), 0), "gp": round(float(S.at[p, "gp_w"]), 1), "toi_gp": round(float(S.at[p, "toi_gp"]), 1),
            "level": [round(float(S.at[p, "level"]), 2), round(float(S.at[p, "level_se"]), 2)],
            "a": {k: [round(float(v["v"][p]), 2), round(float(v["se"][p]), 2)] for k, v in A.items() if pd.notna(v["v"][p])},
            "raw": {"rapm_x": round(float(S.at[p, "rapm_x"]), 3), "rapm_xo": round(float(S.at[p, "rapm_xo"]), 3), "rapm_xd": round(float(S.at[p, "rapm_xd"]), 3),
                    "rapm_c": round(float(S.at[p, "rapm_c"]), 2), "pts60": round(float(per60("pts")[p]), 2), "gax": round(float(S.at[p, "gax"]), 1)},
            "facts": {**facts.get(p, {}), **avail.get(p, {}), "fights": round(float(S.at[p, "fight"]), 1), "pen60": round(float(per60("pen")[p]), 2)},
        }
    for p in G.index:
        pg_ = pages.get(str(p)) or {}
        out["goalies"][str(p)] = {"name": _name(pg_), "pos": "G", "age": _age(pg_), **G.loc[p, "out"], "facts": {**facts.get(p, {}), **avail.get(p, {})}}
    apply_overrides(out, os.path.join(os.path.dirname(os.path.abspath(__file__)), "overrides.json"), meta)
    meta["attr_kinds"] = {k: v["kind"] for k, v in A.items()}
    meta["attr_beta"] = {k: v["beta"] for k, v in A.items()}
    meta["distribution"] = distribution(S, reg)
    meta["age_curves"] = age_curves(PG, pg, pages, S)
    with gzip.open(a.out, "wt", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, separators=(",", ":"))
    log(f"записано: {a.out}, полевых {len(out['skaters'])}, вратарей {len(out['goalies'])}")
    print(json.dumps({k: meta[k] for k in ("xg", "rapm", "distribution")}, ensure_ascii=False))
    if a.report:
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import review
        review.write(out, a.report)


# ───────────────────────────── вспомогательное ─────────────────────────────

def apply_overrides(out, path, meta):
    """Ручные поправки (data/overrides.json): [{"player": имя или id, "attr": "level"|ключ, "v": 1–20, "why": "…"}]."""
    if not os.path.exists(path):
        return
    with open(path, encoding="utf-8") as f:
        rules = json.load(f).get("overrides", [])
    applied = []
    for r in rules:
        for grp in ("skaters", "goalies"):
            for pid, p in out[grp].items():
                if str(r["player"]) not in (pid, p["name"]):
                    continue
                if r["attr"] == "level":
                    p["level"][0] = float(r["v"])
                elif r["attr"] in p["a"]:
                    p["a"][r["attr"]][0] = float(r["v"])
                p.setdefault("overrides", []).append({"attr": r["attr"], "v": r["v"], "why": r.get("why", "")})
                applied.append(f"{p['name']}:{r['attr']}")
    meta["overrides"] = applied


def _edge_mean(edge, pid, key):
    d = edge.get(str(pid)) or {}
    vals = [(SEASON_W.get(s, 0.5) * max(v.get("gp") or 1, 1), v.get(key)) for s, v in d.items() if v.get(key)]
    if not vals:
        return None
    if key in ("speed", "shot"):
        # максимум за сезон зависит от числа матчей — берём лучшее из полных сезонов, текущий учитываем с весом
        return float(max(x for _, x in vals))
    return float(sum(w * x for w, x in vals) / sum(w for w, _ in vals))


def _edge_rate(edge, pid, key):
    """Сумма за сезоны на матч (рывки, дистанция)."""
    d = edge.get(str(pid)) or {}
    num = den = 0.0
    for s, v in d.items():
        if v.get(key) is not None and v.get("gp"):
            w = SEASON_W.get(s, 0.5)
            num += w * v[key]; den += w * v["gp"]
    return num / den if den >= 5 else None


def _team_rate(pg, team_tot):
    return {k: (v[1] / v[0] * 3600 if v[0] else None) for k, v in team_tot.items()}


def _size(page):
    if not page:
        return None
    h, w = page.get("heightInCentimeters"), page.get("weightInKilograms")
    if not h or not w:
        return None
    return 0.4 * (h - 186) / 5.5 + 0.6 * (w - 91) / 7.5


def _name(page):
    if not page:
        return ""
    return f"{(page.get('firstName') or {}).get('default', '')} {(page.get('lastName') or {}).get('default', '')}".strip()


def _age(page):
    b = (page or {}).get("birthDate")
    if not b:
        return None
    y, m, d = map(int, b.split("-"))
    ref = (2027, 2, 1)
    return ref[0] - y - ((ref[1], ref[2]) < (m, d))


AWARD_TAGS = {
    "Mark Messier": "leader", "King Clancy": "leader", "Bill Masterton": "pro", "Lady Byng": "fair",
    "Hart": "star", "Ted Lindsay": "star", "Conn Smythe": "big_games", "Selke": "two_way", "Norris": "star_d",
    "Vezina": "star_g", "Calder": "rookie", "Art Ross": "scorer", "Maurice": "scorer", "Jennings": "",
}


def career_facts(page):
    if not page:
        return {}
    ct = page.get("careerTotals") or {}
    rs, po = ct.get("regularSeason") or {}, ct.get("playoffs") or {}
    dd = page.get("draftDetails") or {}
    awards = []
    for aw in page.get("awards") or []:
        name = (aw.get("trophy") or {}).get("default", "")
        tag = next((t for k, t in AWARD_TAGS.items() if k.lower() in name.lower()), "")
        awards.append({"name": name, "tag": tag, "n": len(aw.get("seasons") or [])})
    leagues = sorted({s.get("leagueAbbrev") for s in page.get("seasonTotals") or [] if s.get("leagueAbbrev")})
    nhl = sorted(((s.get("season"), (s.get("teamName") or {}).get("default")) for s in page.get("seasonTotals") or []
                  if s.get("leagueAbbrev") == "NHL" and s.get("gameTypeId") == 2), key=lambda x: x[0] or 0)
    clubs = len({t for _, t in nhl if t})
    same = 0
    if nhl:
        last = nhl[-1][1]
        for _, t in reversed(nhl):
            if t != last:
                break
            same += 1
    seasons_nhl = len({s.get("season") for s in page.get("seasonTotals") or [] if s.get("leagueAbbrev") == "NHL" and s.get("gameTypeId") == 2})
    return {
        "nhl_gp": rs.get("gamesPlayed"), "po_gp": po.get("gamesPlayed"), "nhl_seasons": seasons_nhl,
        "draft": {k: dd.get(k) for k in ("year", "round", "overallPick")} if dd else None,
        "awards": awards, "leagues": leagues, "clubs": clubs, "club_seasons": len({x for x, t in nhl if t == (nhl[-1][1] if nhl else None)}) if nhl else 0,
        "h": page.get("heightInCentimeters"), "w": page.get("weightInKilograms"), "shoots": page.get("shootsCatches"),
        "country": page.get("birthCountry"), "born": page.get("birthDate"),
    }


def availability(pg, games):
    """Пропуски сериями ≥5 матчей подряд внутри отрезка игры за команду (полные сезоны) — признак травм."""
    out = {}
    reg = games[games["type"] == 2].reset_index()
    team_sched = defaultdict(list)
    for r in reg.itertuples():
        team_sched[(r.season, r.home_id)].append(r.gid); team_sched[(r.season, r.away_id)].append(r.gid)
    for k in team_sched:
        team_sched[k].sort()
    played = pg[pg["gid"].isin(reg["gid"])].groupby(["pid", "season", "team"])["gid"].apply(set)
    acc = defaultdict(lambda: [0, 0])
    for (pid, season, team), gset in played.items():
        sched = team_sched.get((season, team), [])
        idx = [i for i, g in enumerate(sched) if g in gset]
        if len(idx) < 10:
            continue
        span = sched[idx[0]: idx[-1] + 1]
        run = missed = 0
        for g in span:
            if g in gset:
                if run >= 5:
                    missed += run
                run = 0
            else:
                run += 1
        acc[pid][0] += missed; acc[pid][1] += len(span)
    for pid, (m, n) in acc.items():
        out[int(pid)] = {"miss_long": m, "span": n}
    return out


def goalies(shots, pg, pages, games, extra_path=None):
    sh = shots[shots["type"].isin(UNBLOCKED) & (shots["en"] == 0) & shots["goalie"].notna()].copy()
    sh["goalie"] = sh["goalie"].astype(np.int64)
    sh["gsax"] = sh["xg"] - sh["g"]
    sh["hd"] = sh["xg"] >= 0.15
    sh["tip"] = sh["stype"].isin(["tip-in", "deflected"])
    # отскок: следующий бросок той же команды в течение 3 с после сейва
    sh = sh.sort_values(["gid", "per", "t"])
    nxt_t = sh.groupby(["gid", "per"])["t"].shift(-1)
    nxt_team = sh.groupby(["gid", "per"])["team"].shift(-1)
    sh["reb_after"] = ((sh["type"] == "shot-on-goal") & (nxt_team == sh["team"]) & ((nxt_t - sh["t"]) <= 3)).astype(int)
    sog = sh[sh["type"].isin(["shot-on-goal", "goal"])]
    lg_reb = sh.loc[sh["type"] == "shot-on-goal", "reb_after"].mean()
    w = sh["w"]
    agg = pd.DataFrame({
        "n": sh.groupby("goalie")["w"].sum(),
        "gsax": (sh["gsax"] * w).groupby(sh["goalie"]).sum(),
        "n_hd": (sh["hd"] * w).groupby(sh["goalie"]).sum(),
        "gsax_hd": (sh["gsax"] * sh["hd"] * w).groupby(sh["goalie"]).sum(),
        "n_ld": ((~sh["hd"]) * w).groupby(sh["goalie"]).sum(),
        "gsax_ld": (sh["gsax"] * (~sh["hd"]) * w).groupby(sh["goalie"]).sum(),
        "n_tip": (sh["tip"] * w).groupby(sh["goalie"]).sum(),
        "gsax_tip": (sh["gsax"] * sh["tip"] * w).groupby(sh["goalie"]).sum(),
        "n_cl": (sh["close"] * w).groupby(sh["goalie"]).sum(),
        "gsax_cl": (sh["gsax"] * sh["close"] * w).groupby(sh["goalie"]).sum(),
        "saves": ((sh["type"] == "shot-on-goal") * w).groupby(sh["goalie"]).sum(),
        "reb": ((sh["reb_after"] - lg_reb) * (sh["type"] == "shot-on-goal") * w).groupby(sh["goalie"]).sum(),
    }).fillna(0)
    gp = pg[pg["pid"].isin(agg.index)]
    agg["toi"] = (gp["sec"] * gp["w"]).groupby(gp["pid"]).sum().reindex(agg.index).fillna(0) / 60
    agg["gp"] = gp.groupby("pid")["w"].sum().reindex(agg.index).fillna(0)
    # старты: матчи с ≥ 40 минутами
    starts = gp[gp["sec"] >= 2400]
    agg["starts"] = starts.groupby("pid")["w"].sum().reindex(agg.index).fillna(0)
    # надёжность GSAx на бросок по половинкам
    per_game = sh.assign(v=sh["gsax"] * sh["w"], e=sh["w"]).groupby(["goalie", "gid"], as_index=False)[["v", "e"]].sum().rename(columns={"goalie": "pid"})
    k, r = split_half_k(per_game, "v", "e", 400)
    k = k or 1500
    reg = (agg["toi"] >= 1000).to_dict()
    grp = {"G": list(agg.index)}
    z = Scale(agg["gsax"] / agg["n"].clip(lower=1), agg["n"], k, grp, reg, None, prior_on_level=False).z
    # второе мнение: GSAx по модели MoneyPuck (у неё больше признаков броска), сезоны 2024-25 и новее
    if extra_path and os.path.exists(extra_path):
        mp = gz(extra_path).get("goalies", {})
        mpw = {"2026": 1.0, "2025": 1.0, "2024": 0.8}
        rows = {}
        for pid, seas in mp.items():
            num = den = 0.0
            for y, v in seas.items():
                w_ = mpw.get(y, 0)
                if w_ and v.get("sog"):
                    num += w_ * ((v.get("xga") or 0) - (v.get("ga") or 0)); den += w_ * v["sog"]
            if den > 0:
                rows[int(pid)] = (num / den, den)
        mx = pd.Series({p: rows[p][0] for p in agg.index if p in rows}, dtype=float).reindex(agg.index)
        mn = pd.Series({p: rows[p][1] for p in agg.index if p in rows}, dtype=float).reindex(agg.index).fillna(0)
        zm = Scale(mx, mn, k * 0.7, grp, reg, None, prior_on_level=False).z
        z = pd.Series(np.where(mx.notna(), 0.5 * z.fillna(0) + 0.5 * zm.fillna(0), z), index=agg.index)
    # доля стартов в сезонах (с весами): нагрузка — признак доверия
    zw = Scale(agg["starts"] / agg["gp"].clip(lower=1), agg["gp"] * 10, 1e-6, grp, reg, None, prior_on_level=False).z
    zl = 0.75 * z.fillna(0) + 0.25 * zw.fillna(0)
    regs = [i for i in agg.index if reg[i]]
    zG = (zl - zl[regs].mean()) / zl[regs].std()
    level = to_scale(zG).clip(1, 20)
    se_l = (0.65 * SCALE * np.sqrt(k / (agg["n"] + k))).clip(0.6, 3.0)
    res = {}
    for key, kind, x, n, kk, sign in (
        ("rfx", "tech", agg["gsax_hd"] / agg["n_hd"].clip(lower=1), agg["n_hd"], k * 0.25, 1.0),
        ("gpos", "tech", agg["gsax_ld"] / agg["n_ld"].clip(lower=1), agg["n_ld"], k * 2.0, 1.0),
        ("reb", "spec", agg["reb"] / agg["saves"].clip(lower=1), agg["saves"], 1500.0, -1.0),
        ("scr", "spec", agg["gsax_tip"] / agg["n_tip"].clip(lower=1), agg["n_tip"], k * 0.3, 1.0),
        ("gsta", "spec", agg["starts"] / agg["gp"].clip(lower=1), agg["gp"] * 10, 1e-6, 1.0),
        ("gcmp", "weak", agg["gsax_cl"] / agg["n_cl"].clip(lower=1), agg["n_cl"], k, 1.0),
    ):
        sc = Scale(x, n, kk, grp, reg, zG, sign, True)
        v, se = combine(level, sc.z, sc.se, kind)
        res[key] = (v, se)
    # стабильность: разброс GSAx по матчам
    gstat = per_game.groupby("pid")["v"].agg(["std", "count"])
    sc = Scale(-gstat["std"].reindex(agg.index), gstat["count"].reindex(agg.index).fillna(0), 60.0, grp, reg, zG, 1.0, False)
    res["gcon"] = combine(level, sc.z, sc.se, "cons")
    out = {}
    for p in agg.index:
        out[p] = {"toi": round(float(agg.at[p, "toi"]), 0), "gp": round(float(agg.at[p, "gp"]), 1),
                  "level": [round(float(level[p]), 2), round(float(se_l[p]), 2)],
                  "a": {k_: [round(float(v[p]), 2), round(float(se[p]), 2)] for k_, (v, se) in res.items() if pd.notna(v[p])},
                  "raw": {"gsax": round(float(agg.at[p, "gsax"]), 1), "shots": round(float(agg.at[p, "n"]), 0)}}
    return pd.DataFrame({"out": pd.Series(out)})


def distribution(S, reg):
    regs = S[[reg[i] for i in S.index]]
    bins = [(18, 21), (16, 18), (13, 16), (10, 13), (7, 10), (0, 7)]
    return {f"{a}-{b - 1}": round(float(((regs["level"] >= a) & (regs["level"] < b)).mean()), 3) for a, b in bins}


def age_curves(PG, pg, pages, S):
    """Изменение уровня (очки и игровое время на 60) от сезона к сезону по возрасту — черновая оценка."""
    d = pg[pg["type"] == 2].groupby(["pid", "season"]).agg(sec=("sec", "sum"), gp=("gid", "nunique")).reset_index()
    d = d[d["sec"] >= 500 * 60]
    d["toi_gp"] = d["sec"] / 60 / d["gp"]
    born = {int(k): v.get("birthDate") for k, v in pages.items()}
    d["age"] = [(int(s[:4]) + 1 - int(born[p][:4])) if born.get(p) else None for p, s in zip(d["pid"], d["season"])]
    d = d.dropna(subset=["age"]).sort_values(["pid", "season"])
    d["d_toi"] = d.groupby("pid")["toi_gp"].diff()
    d["prev_season"] = d.groupby("pid")["season"].shift()
    d = d[d["prev_season"].notna()]
    d = d[d["season"].astype(int) - d["prev_season"].astype(int) == 10001]
    return {int(a): round(float(v), 2) for a, v in d.groupby("age")["d_toi"].mean().items() if 18 <= a <= 40}


if __name__ == "__main__":
    main()
