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

SEASON_W = {"20262027": 1.0, "20252026": 1.0, "20242025": 0.8, "20232024": 0.64, "20222023": 0.5}
MID, SCALE = 11.0, 3.4          # шкала 1–20: середина и цена одного стандартного отклонения
# общий уровень: ценность за матч (общая шкала F и D), очки за 60, игровое время, доля сыгранных матчей
LEVEL_W = {"value": 0.5, "pts": 0.25, "toi": 0.15, "share": 0.10}
CA_MID, CA_TOP, CA_TOP_G = 120.0, 190.0, 182.0   # КА: средний регулярный игрок, лучший полевой, лучший вратарь
CA_PER_LVL = 6.0                                # единица шкалы 1–20 ≈ 6 пунктов КА (1 ст. откл. ≈ 3,4 ед. ≈ 20 КА)
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


def mp_xg(shots, raw_dir, meta):
    """Заменить xG на модель MoneyPuck (moneypuck.com, некоммерческое использование): у неё больше признаков броска.
    Сопоставление: матч, бросающий, секунда матча (±1 с). Несопоставленные броски остаются с нашим xG."""
    keys = {}
    years = sorted({int(s[:4]) for s in shots["season"].unique()})
    total = 0
    for y in years:
        path = os.path.join(raw_dir, f"mp_shots_{y}.csv.gz")
        if not os.path.exists(path):
            continue
        df = pd.read_csv(path, usecols=lambda c: c in ("game_id", "isPlayoffGame", "time", "shooterPlayerId", "xGoal", "event", "period"))
        df = df.dropna(subset=["shooterPlayerId", "xGoal"])
        gid = (str(y) + df["game_id"].astype(int).map(lambda g: f"{g:06d}"))
        for g, t, pid, x in zip(gid, df["time"].astype(int), df["shooterPlayerId"].astype(int), df["xGoal"].astype(float)):
            keys[(g, pid, t)] = x
        total += len(df)
    if not keys:
        meta["xg_source"] = "своя модель (данных MoneyPuck нет)"
        return
    unb = shots["type"].isin(UNBLOCKED)
    gt = ((shots["per"] - 1) * 1200 + shots["t"]).astype(int)
    found = np.full(len(shots), np.nan)
    for i, (g, pid, t, u) in enumerate(zip(shots["gid"], shots["shooter"], gt, unb)):
        if not u:
            continue
        x = keys.get((g, pid, t))
        if x is None:
            x = keys.get((g, pid, t - 1), keys.get((g, pid, t + 1)))
        if x is not None:
            found[i] = x
    ok = ~np.isnan(found) & (shots["en"].values == 0)
    y = (shots["type"] == "goal").values
    m = ok & unb.values
    meta["xg_mp"] = {"mp_shots": total, "matched": round(float(m.sum() / max(1, (unb.values & (shots["en"].values == 0)).sum())), 3),
                     "mean_mp": round(float(found[m].mean()), 4), "goal_rate": round(float(y[m].mean()), 4),
                     "auc_mp": _auc(found[m], y[m]), "auc_own": _auc(shots["xg"].values[m], y[m])}
    shots.loc[ok, "xg"] = found[ok]
    meta["xg_source"] = "MoneyPuck (moneypuck.com) там, где бросок найден, иначе своя модель"
    log("xG MoneyPuck:", meta["xg_mp"])


def _auc(p, y):
    order = np.argsort(p)
    ranks = np.empty(len(p)); ranks[order] = np.arange(1, len(p) + 1)
    npos = y.sum()
    return round(float((ranks[y == 1].sum() - npos * (npos + 1) / 2) / (npos * (len(y) - npos))), 4) if 0 < npos < len(y) else None


# ───────────────────────────── 3. RAPM ─────────────────────────────

class Rapm:
    """Гребневая регрессия по стинтам 5 на 5: y (за 60) ~ нападение + оборона + площадка хозяев.
    Нормальные уравнения копятся по группам строк (фолды, сезоны) один раз; решения — вычитанием групп.
    Сжатие идёт не к нулю, а к априорному значению b0 (если задано): «RAPM с априорной оценкой по личной статистике»."""

    def __init__(self, rows_off, rows_def, home, w, P):
        n = len(w)
        lens = np.array([len(o) + len(d) + (1 if h else 0) for o, d, h in zip(rows_off, rows_def, home)])
        r_idx = np.repeat(np.arange(n), lens)
        c_idx = np.fromiter((c for o, d, h in zip(rows_off, rows_def, home) for c in (*o, *(P + q for q in d), *((2 * P,) if h else ()))),
                            np.int64, count=int(lens.sum()))
        self.X = sparse.csr_matrix((np.ones(len(r_idx)), (r_idx, c_idx)), shape=(n, 2 * P + 1))
        self.w = np.asarray(w, float)
        self.P = P

    def parts(self, groups, ys):
        """groups: массив меток строк; ys: {имя: y}. Возвращает {метка: {A, c_имя, d, sw, swy_имя, swyy_имя}}."""
        out = {}
        for gv in np.unique(groups):
            m = groups == gv
            Xm, wm = self.X[m], self.w[m]
            Wx = Xm.multiply(wm[:, None]).tocsr()
            d = {"A": (Xm.T @ Wx).toarray(), "d": Xm.T @ wm, "sw": wm.sum()}
            for k, y in ys.items():
                ym = y[m]
                d["c_" + k] = Xm.T @ (wm * ym); d["swy_" + k] = (wm * ym).sum(); d["swyy_" + k] = (wm * ym * ym).sum()
            out[gv] = d
        return out

    @staticmethod
    def total(parts, keys):
        ks = list(parts[keys[0]].keys())
        return {k: sum(parts[g][k] for g in keys) for k in ks}

    def solve(self, t, y, lam, b0=None):
        mu = t["swy_" + y] / t["sw"]
        pen = np.full(2 * self.P + 1, float(lam)); pen[-1] = 1e-6
        rhs = t["c_" + y] - mu * t["d"]
        if b0 is not None:
            rhs = rhs + pen * b0
        return np.linalg.solve(t["A"] + np.diag(pen), rhs), mu

    @staticmethod
    def err(te, y, beta, mu):
        sse0 = te["swyy_" + y] - 2 * mu * te["swy_" + y] + mu * mu * te["sw"]
        return float((sse0 - 2 * beta @ (te["c_" + y] - mu * te["d"]) + beta @ te["A"] @ beta) / te["sw"])


def box_prior(beta_off, feats, wts, regs_mask):
    """Априорная оценка атакующего вклада по личной статистике: взвешенная регрессия RAPM-атаки на признаки."""
    F = np.column_stack([feats, np.ones(len(feats))])
    W = wts * regs_mask
    coef = np.linalg.lstsq(F * np.sqrt(W)[:, None], beta_off * np.sqrt(W), rcond=None)[0]
    return F @ coef, coef

# целевое распределение уровня у регулярных игроков НХЛ: (доля игроков ниже, уровень)
LEVEL_TARGET = [(0.0, 5.0), (0.03, 7.0), (0.20, 10.0), (0.65, 13.0), (0.90, 16.0), (0.975, 18.0), (0.997, 19.5), (1.0, 19.8)]


def level_map(raw, regs):
    """Уровень по месту среди регулярных игроков (квантили → целевое распределение); остальные — по той же кривой,
    ниже самого слабого регулярного — с тем же наклоном вниз."""
    r = np.sort(raw[regs].values)
    q = np.linspace(0, 1, len(r))
    tq, tl = zip(*LEVEL_TARGET)
    lev_r = np.interp(q, tq, tl)
    x = raw.values
    out = np.interp(x, r, lev_r)
    slope = (lev_r[min(len(r) - 1, len(r) // 10)] - lev_r[0]) / max(r[min(len(r) - 1, len(r) // 10)] - r[0], 1e-9)
    out = np.where(x < r[0], lev_r[0] + (x - r[0]) * slope, out)
    return pd.Series(np.clip(out, 1, 19.9), index=raw.index)


def wowy_pairs(e5, pidx, P):
    """WOWY: для каждого игрока — насколько доля xG его партнёров с ним выше, чем без него (5 на 5).
    Возвращает (среднее по партнёрам, взвешенное по времени, «эффективные минуты» для сжатия)."""
    w = (e5["dur"] * e5["sw"]).values
    tot = np.zeros((P, 3))       # минуты, xGF, xGA каждого игрока
    pair = np.zeros((P * P, 3))  # [i*P+j]: j вместе с i
    for col, xf, xa in (("hs", "xg_h", "xg_a"), ("aw", "xg_a", "xg_h")):
        H = np.full((len(e5), 6), -1, np.int64)
        for r, t in enumerate(e5[col].values):
            for c_, p_ in enumerate(t[:6]):
                H[r, c_] = pidx[p_]
        f, ag = e5[xf].values * e5["sw"].values, e5[xa].values * e5["sw"].values
        for c_ in range(6):
            m = H[:, c_] >= 0
            np.add.at(tot, H[m, c_], np.stack([w[m], f[m], ag[m]], 1))
        for c1 in range(6):
            for c2 in range(6):
                if c1 == c2:
                    continue
                m = (H[:, c1] >= 0) & (H[:, c2] >= 0)
                idx = H[m, c1] * P + H[m, c2]
                for k_, arr in enumerate((w[m], f[m], ag[m])):
                    pair[:, k_] += np.bincount(idx, weights=arr, minlength=P * P)
    pair = pair.reshape(P, P, 3)
    with_t, with_f, with_a = pair[:, :, 0], pair[:, :, 1], pair[:, :, 2]
    wo_t = tot[None, :, 0] - with_t
    wo_f = tot[None, :, 1] - with_f
    wo_a = tot[None, :, 2] - with_a
    ok = (with_t >= 1800) & (wo_t >= 1800)        # от 30 минут вместе и порознь
    pw = np.where(ok, with_f / np.maximum(with_f + with_a, 1e-9), 0)
    po = np.where(ok, wo_f / np.maximum(wo_f + wo_a, 1e-9), 0)
    wt = np.where(ok, 1 / (1 / np.maximum(with_t, 1) + 1 / np.maximum(wo_t, 1)), 0)
    num = (wt * (pw - po)).sum(1)
    den = wt.sum(1)
    return num / np.maximum(den, 1e-9), den / 60


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
    ap.add_argument("--curves", default="data/age_curves.json", help="возрастные кривые (data/history.py)")
    a = ap.parse_args()
    meta = {"built": time.strftime("%Y-%m-%d"), "seasons": a.seasons, "scale": {"mid": MID, "sd": SCALE}}

    pages = gz(os.path.join(a.raw, "players.json.gz"))
    edge = gz(os.path.join(a.raw, "edge.json.gz")) if os.path.exists(os.path.join(a.raw, "edge.json.gz")) else {}
    pos_of = {int(k): v.get("position") for k, v in pages.items()}

    # ── протоколы и смены ──
    shot_dfs, event_dfs, stint_dfs, pg_dfs, games = [], [], [], [], []
    for season in a.seasons:
        all_shots, all_events, stint_rows, pg_rows = [], [], [], []
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
        # по сезону — сразу в таблицы: словари и кортежи в Python занимают в разы больше памяти
        shot_dfs.append(pd.DataFrame(all_shots)); event_dfs.append(pd.DataFrame(all_events))
        stint_dfs.append(pd.DataFrame(stint_rows, columns=["gid", "season", "sw", "per", "t0", "t1", "hs", "aw", "hg", "ag"]))
        pg_dfs.append(pd.DataFrame(pg_rows, columns=["gid", "season", "pid", "team", "sec"]))
        del all_shots, all_events, stint_rows, pg_rows
    shots = pd.concat(shot_dfs, ignore_index=True); events = pd.concat(event_dfs, ignore_index=True)
    del shot_dfs, event_dfs
    # строки → категории: в разы меньше памяти (в рабочем окружении 8 ГБ)
    for df_, cols_ in ((shots, ("gid", "season", "type", "stype", "zone", "reason")), (events, ("gid", "season", "type", "venue", "pen"))):
        for c_ in cols_:
            if c_ in df_.columns:
                df_[c_] = df_[c_].astype("category")
    games = pd.DataFrame(games).set_index("gid")
    log(f"бросков {len(shots)}, событий {len(events)}, стинтов {sum(len(x) for x in stint_dfs)}")

    # ── xG: своя модель + xG MoneyPuck там, где бросок нашёлся в их данных ──
    shots["xg"], meta["xg"] = fit_xg(shots)
    shots["xg_own"] = shots["xg"]
    mp_xg(shots, a.raw, meta)
    shots["g"] = (shots["type"] == "goal").astype(int)
    shots["w"] = shots["season"].map(SEASON_W).fillna(0.5)
    log("xG:", meta["xg"])

    # ── стинты: суммы xG/бросков/голов по командам ──
    st = pd.concat(stint_dfs, ignore_index=True)
    del stint_dfs
    st["gid"] = st["gid"].astype("category"); st["season"] = st["season"].astype("category")
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
        st[f"ff_{side}"] = mine[mine["type"].isin(UNBLOCKED)].groupby("stint").size().reindex(st.index).fillna(0)
        st[f"gf_{side}"] = mine[mine["type"] == "goal"].groupby("stint").size().reindex(st.index).fillna(0)

    # ── индекс игроков ──
    pids = sorted({p for t in st["hs"] for p in t} | {p for t in st["aw"] for p in t})
    P = len(pids); pidx = {p: i for i, p in enumerate(pids)}
    log(f"полевых в стинтах: {P}")

    # ── RAPM 5 на 5 (xG и попытки бросков) с априорной оценкой по личной статистике ──
    pg = pd.concat(pg_dfs, ignore_index=True)
    del pg_dfs
    ev = st[(st["nh"] == 5) & (st["na"] == 5) & (st["hg"] > 0) & (st["ag"] > 0) & (st["dur"] > 0) & (st["per"] <= 3)]
    fold_of = {g: i % 5 for i, g in enumerate(sorted(ev["gid"].unique()))}
    # «сменившие клуб»: команда в последнем полном сезоне не та, что в предыдущем — для проверки на них
    pteam = pg.groupby(["pid", "season"])["team"].agg(lambda x: x.value_counts().index[0])
    test_season = "20252026"
    movers = {p_ for (p_, se_), t_ in pteam.items() if se_ == test_season and (p_, "20242025") in pteam and pteam[(p_, "20242025")] != t_}
    off, dfn, home, ys_xg, ys_cf, ws, folds, seas, mov = [], [], [], [], [], [], [], [], []
    for r in ev.itertuples():
        h = [pidx[p] for p in r.hs]; aw = [pidx[p] for p in r.aw]
        f = fold_of[r.gid]
        anym = any(p in movers for p in r.hs) or any(p in movers for p in r.aw)
        for o, d, hm, xg_, cf_ in ((h, aw, 1, r.xg_h, r.cf_h), (aw, h, 0, r.xg_a, r.cf_a)):
            off.append(o); dfn.append(d); home.append(hm)
            ys_xg.append(xg_ * 3600 / r.dur); ys_cf.append(cf_ * 3600 / r.dur)
            ws.append(r.dur * r.sw); folds.append(f); seas.append(r.season); mov.append(anym)
    folds, seas, mov = np.array(folds), np.array(seas), np.array(mov)
    ys = {"xg": np.array(ys_xg), "cf": np.array(ys_cf)}
    R = Rapm(off, dfn, home, ws, P)
    del off, dfn, home, ys_xg, ys_cf
    fparts = R.parts(folds, ys)
    lam_grid = [8000, 16000, 32000, 64000, 128000]
    cv = {}
    for lam_ in lam_grid:
        e_ = 0.0
        for f in range(5):
            tr = Rapm.total(fparts, [g for g in range(5) if g != f])
            beta, mu = R.solve(tr, "xg", lam_)
            e_ += Rapm.err(fparts[f], "xg", beta, mu) * fparts[f]["sw"]
        cv[lam_] = e_ / sum(fparts[g]["sw"] for g in range(5))
    lam = min(cv, key=cv.get)
    full = Rapm.total(fparts, list(range(5)))
    b_plain, _ = R.solve(full, "xg", lam)
    c_plain, _ = R.solve(full, "cf", lam)

    # признаки личной статистики (с весами сезонов, сжатые к среднему): очки, личный xG, первые передачи за 60, защитник
    def box_feats(season_mask=None):
        sh_ = shots if season_mask is None else shots[shots["season"].isin(season_mask)]
        pg_ = pg if season_mask is None else pg[pg["season"].isin(season_mask)]
        w_ = sh_["season"].map(SEASON_W).fillna(0.5)
        toi_ = (pg_["sec"] * pg_["season"].map(SEASON_W).fillna(0.5)).groupby(pg_["pid"]).sum().reindex(pids).fillna(0).values / 60
        goal = sh_["type"] == "goal"
        def by(col):
            k = sh_.loc[goal, col].fillna(-1).astype(np.int64)
            return w_[goal].groupby(k).sum().reindex(pids).fillna(0).values
        pts_ = by("shooter") + by("a1") + by("a2")
        a1_ = by("a1")
        fz = sh_["type"].isin(UNBLOCKED) & (sh_["en"] == 0)
        ixg_ = (sh_.loc[fz, "xg"] * w_[fz]).groupby(sh_.loc[fz, "shooter"]).sum().reindex(pids).fillna(0).values
        k_ = 300.0
        def shr(x):
            m_ = x.sum() / max(toi_.sum(), 1)
            return (x + k_ * m_) / (toi_ + k_) * 60
        isd = np.array([1.0 if pos_of.get(p) == "D" else 0.0 for p in pids])
        return np.column_stack([shr(pts_), shr(ixg_), shr(a1_), isd]), toi_

    toi5_ = np.zeros(P)
    for col in ("hs", "aw"):
        for t_, d_ in zip(ev[col].values, (ev["dur"] * ev["sw"]).values):
            for p_ in t_:
                toi5_[pidx[p_]] += d_
    toi5_ /= 60
    regmask = (toi5_ >= 500).astype(float)

    def prior_vec(beta_xg, beta_cf, feats, m):
        bx, cx = box_prior(beta_xg[:P], feats, toi5_, regmask)
        bc, cc = box_prior(beta_cf[:P], feats, toi5_, regmask)
        b0x = np.zeros(2 * P + 1); b0x[:P] = m * bx
        b0c = np.zeros(2 * P + 1); b0c[:P] = m * bc
        return b0x, b0c, cx

    # проверка по времени: обучение на сезонах до 2025-26, проверка на 2025-26 (в т. ч. на стинтах со сменившими клуб)
    train_seasons = [x for x in a.seasons if x < test_season]
    grp = np.where(seas == test_season, 1, 0) + np.where(mov & (seas == test_season), 1, 0)   # 0 обучение, 1 проверка, 2 проверка + сменившие
    sparts = R.parts(grp, ys)
    tr = sparts[0]
    te_all = Rapm.total(sparts, [g for g in (1, 2) if g in sparts])
    te_mov = sparts.get(2)
    bt_plain, mu_t = R.solve(tr, "xg", lam)
    ct_plain, _ = R.solve(tr, "cf", lam)
    feats_tr, _ = box_feats(train_seasons)
    val = {"plain": {"all": Rapm.err(te_all, "xg", bt_plain, mu_t), "movers": Rapm.err(te_mov, "xg", bt_plain, mu_t) if te_mov else None}}
    for m_ in (0.5, 1.0, 1.5):
        b0x, _, _ = prior_vec(bt_plain, ct_plain, feats_tr, m_)
        bt, mu_ = R.solve(tr, "xg", lam, b0x)
        val[f"prior_{m_}"] = {"all": Rapm.err(te_all, "xg", bt, mu_), "movers": Rapm.err(te_mov, "xg", bt, mu_) if te_mov else None}
    best_m = min((k for k in val if k.startswith("prior")), key=lambda k: val[k]["movers"] or val[k]["all"])
    use_prior = (val[best_m]["movers"] or val[best_m]["all"]) < (val["plain"]["movers"] or val["plain"]["all"])
    m_best = float(best_m.split("_")[1]) if use_prior else 0.0
    feats_all, _ = box_feats()
    b0x, b0c, coef = prior_vec(b_plain, c_plain, feats_all, m_best)
    bx, _ = R.solve(full, "xg", lam, b0x if use_prior else None)
    bc, _ = R.solve(full, "cf", lam, b0c if use_prior else None)
    xo, xd, hxg = bx[:P], bx[P:2 * P], float(bx[-1])
    co, cd = bc[:P], bc[P:2 * P]
    meta["rapm"] = {"stints": int(len(ev)), "lambda": lam, "cv": {str(k): round(v, 4) for k, v in cv.items()}, "home_xg60": round(hxg, 3),
                    "validation_2025_26": {k: {kk: (round(vv, 5) if vv is not None else None) for kk, vv in v.items()} for k, v in val.items()},
                    "movers": len(movers), "prior": m_best, "prior_coef": [round(float(x), 4) for x in coef]}
    log("RAPM:", meta["rapm"])
    del R, fparts, sparts, ws

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
    add(flat(["hs", "aw"], is5, {"toi5": dur, "xgf5": own("xg"), "xga5": opp("xg"), "cf5": own("cf"), "ca5": opp("cf"), "ff5": own("ff")}))
    # качество моментов при игроке (5 на 5): xG своей команды сверх «обычного» для её бросков (xG/бросок команды за сезон)
    st["home"], st["away"] = st["gid"].map(games["home"]), st["gid"].map(games["away"])
    e5 = st[is5]
    tq = {}
    for side, tcol in (("h", "home"), ("a", "away")):
        g_ = e5.groupby(["season", tcol])[[f"xg_{side}", f"ff_{side}"]].sum()
        for (se_, tm_), r_ in g_.iterrows():
            v_ = tq.setdefault((se_, tm_), [0.0, 0.0]); v_[0] += r_[f"xg_{side}"]; v_[1] += r_[f"ff_{side}"]
    tq = {k: v[0] / max(v[1], 1) for k, v in tq.items()}
    st["q_h"] = [tq.get((a_, b_), 0.067) for a_, b_ in zip(st["season"], st["home"])]
    st["q_a"] = [tq.get((a_, b_), 0.067) for a_, b_ in zip(st["season"], st["away"])]
    add(flat(["hs", "aw"], is5, {"qres5": lambda s_, col: ((s_["xg_h"] - s_["ff_h"] * s_["q_h"]) if col == "hs" else (s_["xg_a"] - s_["ff_a"] * s_["q_a"])).values * s_["sw"].values}))
    wowy = wowy_pairs(st[is5 & (st["dur"] > 0)], pidx, P)
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
    S["wowy"], S["wowy_n"] = wowy
    S["rapm_c"] = S["rapm_co"] + S["rapm_cd"]

    # ── игровое время по матчам (все ситуации) и сыгранные матчи ──
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
    counts["lng_gax"] = per_player_game(lng, "shooter", lambda d: d["g"] - d["xg"])
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
        if k is not None and expo_col == "expo":
            k = max(k, 250.0)     # пол для показателей «за 60 минут»: у игроков с малым временем они раздуты лёгкими соперниками
        ks[name] = {"k": None if k is None else round(k, 1), "r_half": None if r is None else round(r, 3)}
        return k if k is not None else 1e9

    # ── общий уровень: ценность за матч в голах (общая шкала для нападающих и защитников) ──
    S["pts"] = S["g_all"] + S["a1"] + S["a2"]
    PG["pts"] = PG["g_all"] + PG["a1"] + PG["a2"]
    gpw = S["gp_w"].clip(lower=0.5)
    k_fin = k_for("fin", "gax", "fsh", 25)
    gax_shot = (S["gax"]) / (S["fsh"] + k_fin)                         # сжато к нулю по надёжности
    pp_rate = S["xgfpp"] / S["toipp"].clip(lower=1) * 3600
    pk_rate = S["xgapk"] / S["toipk"].clip(lower=1) * 3600
    val = pd.DataFrame(index=S.index)
    val["ev"] = (S["rapm_xo"] + S["rapm_xd"]) * S["toi5"] / 3600 / gpw          # RAPM 5 на 5: xG за матч
    val["fin"] = gax_shot * S["fsh"] / gpw                                       # реализация сверх xG
    val["pp"] = (pp_rate - lg_pp) * S["toipp"] / 3600 / gpw * 0.2 * (S["toipp"] / (S["toipp"] + 3000))
    val["pk"] = -(pk_rate - lg_pp) * S["toipk"] / 3600 / gpw * 0.2 * (S["toipk"] / (S["toipk"] + 3000))
    val["pen"] = (S["pend"] - S["pen"]) / gpw * 0.17                            # удаление ≈ 0,17 гола
    val["fo"] = (S["fow"] - S["fol"]) / gpw * 0.008
    S["value"] = val.sum(axis=1)
    k_pts = k_for("pts60", "pts")
    allg = {"all": list(S.index)}
    zv = Scale(S["value"], gpw * 10, 1e-6, allg, reg, None, prior_on_level=False).z
    zp = Scale(per60("pts"), S["toi_min"], k_pts, groups, reg, None, prior_on_level=False).z
    zt = Scale(S["toi_gp"], gpw * 10, 1e-6, groups, reg, None, prior_on_level=False).z
    zs = Scale(S["gp_share"], gpw * 10, 1e-6, groups, reg, None, prior_on_level=False).z
    zr = Scale(S["rapm_x"], S["toi5"] / 60, 1e-6, groups, reg, None, prior_on_level=False).z
    raw = LEVEL_W["value"] * zv + LEVEL_W["pts"] * zp + LEVEL_W["toi"] * zt + LEVEL_W["share"] * zs
    regs = [i for i in S.index if reg[i]]
    S["level"] = level_map(raw, regs)
    # КА 1–200 (как в FM): линейно по силе, без сжатия верха. Средний регулярный игрок НХЛ = 120,
    # лучший = CA_TOP; одна единица шкалы 1–20 ≈ CA_PER_LVL пунктов КА (для развития и движка)
    z_raw = (raw - raw[regs].mean()) / raw[regs].std()
    s_ca = float(np.clip((CA_TOP - CA_MID) / z_raw[regs].max(), 14, 26))
    S["ca"] = (CA_MID + s_ca * z_raw).clip(40, 199)
    meta["ca_scale"] = {"mid": CA_MID, "per_sd": round(s_ca, 2), "top": CA_TOP}
    zG = (S["level"] - MID) / SCALE                       # для априорных значений характеристик
    S["level_se"] = (SCALE * np.sqrt(500 / (S["toi_min"] + 500))).clip(0.6, 3.0)
    S["ca_se"] = (s_ca * np.sqrt(500 / (S["toi_min"] + 500))).clip(4, 18)
    meta["level_weights"] = LEVEL_W
    top = S[S["toi_min"] >= REG_MIN].sort_values("level", ascending=False)
    meta["d_share"] = {n: round(float((top.head(n)["grp"] == "D").mean()), 3) for n in (10, 25, 50, 100)}
    log("доля защитников в топах:", meta["d_share"])

    # ── характеристики полевых ──
    A = {}

    def attr(key, kind, x, n, k, sign=1.0, prior=True, grp=None):
        sc = Scale(x, n, k, grp or groups, reg, zG, sign, prior)
        v, se = combine(S["level"], sc.z, sc.se, kind)
        A[key] = {"v": v, "se": se, "beta": sc.beta, "kind": kind}

    def zq(x, n, k, grp=None, sign=1.0):
        return Scale(x, n, k, grp or groups, reg, None, sign, prior_on_level=False).z.fillna(0)

    one = pd.Series(1e6, index=S.index)
    pwr = pd.Series({p: _edge_mean(edge, p, "shot") for p in S.index}).astype(float)
    # атака
    attr("fin", "tech", S["gax"] / S["fsh"].clip(lower=1), S["fsh"], k_fin)
    attr("pwr", "direct", pwr, pd.Series(np.where(pwr.notna(), 1e6, 0), index=S.index), 1e-6, grp=allg)
    # бросок издали: точность (в створ), реализация с 35+ футов и мощь броска
    PG["lng_gax_"] = PG["lng_gax"]
    z_lacc = zq(S["lng_on"] / S["lng_n"].clip(lower=1), S["lng_n"], k_for("lng", "lng_on", "lng_n", 20))
    z_lfin = zq(S["lng_gax"] / S["lng_n"].clip(lower=1), S["lng_n"], k_for("lng_fin", "lng_gax", "lng_n", 20))
    z_pwr = ((pwr - pwr.mean()) / pwr.std()).fillna(0)
    attr("lng", "spec", 0.4 * z_lacc + 0.35 * z_lfin + 0.25 * z_pwr, one, 1e-6)
    attr("net", "spec", per60("net_xg"), S["toi_min"], k_for("net", "net_xg"))
    PG["pas_"] = PG["a1"] + 0.5 * PG["a2"]
    attr("pas", "tech", (S["a1"] + 0.5 * S["a2"]) / S["toi_min"].clip(lower=1) * 60, S["toi_min"], k_for("pas", "pas_"))
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
    attr("pk", "spec", pk_rate, S["toipk"] / 60, 60.0, sign=-1.0, prior=False)
    # физика
    spd = pd.Series({p: _edge_mean(edge, p, "speed") for p in S.index}).astype(float)
    attr("spd", "direct", spd, pd.Series(np.where(spd.notna(), 1e6, 0), index=S.index), 1e-6, grp=allg)
    bur = pd.Series({p: _edge_rate(edge, p, "burstsOver20") for p in S.index}).astype(float)
    attr("acc", "direct", bur, pd.Series(np.where(bur.notna(), 1e6, 0), index=S.index), 1e-6, grp=allg)
    size = pd.Series({p: _size(pages.get(str(p))) for p in S.index}).astype(float)
    zsize = ((size - size.mean()) / size.std()).fillna(0)
    zhit = zq(per60("hits"), S["toi_min"], ks.get("hit", {}).get("k") or 300, allg)
    attr("str", "spec", 0.65 * zsize + 0.35 * zhit, one, 1e-6, grp=allg)
    attr("sta", "spec", S["toi_gp"], gpw * 10, 1e-6)
    # мышление — каждая характеристика из своего источника, без повторного RAPM
    PG["tkgv"] = PG["tk"] + PG["gv"]
    z_tkgv = zq(S["tk"] / (S["tk"] + S["gv"]).clip(lower=1), S["tk"] + S["gv"], k_for("dec_tkgv", "tk", "tkgv", 10))
    z_sel = zq(S["ixg"] / S["fsh"].clip(lower=1), S["fsh"], k_for("dec_sel", "ixg", "fsh", 25))
    z_gvp = zq(S["gv"] / S["cf5"].clip(lower=1), S["cf5"], 300.0, sign=-1.0)          # потери на атаку своей команды
    attr("dec", "tech", 0.45 * z_tkgv + 0.3 * z_sel + 0.25 * z_gvp, one, 1e-6)
    # видение: качество моментов партнёров при нём (xG на бросок сверх обычного для команды) + первые передачи
    z_q = zq(S["qres5"] / S["ff5"].clip(lower=1), S["ff5"], 900.0)
    z_a1 = zq(per60("a1"), S["toi_min"], ks.get("pas", {}).get("k") or 400)
    attr("vis", "tech", 0.65 * z_q + 0.35 * z_a1, one, 1e-6)
    attr("dis", "spec", per60("pen"), S["toi_min"], k_for("dis", "pen"), sign=-1.0, prior=False)
    dist = pd.Series({p: _edge_rate(edge, p, "dist") for p in S.index}).astype(float)
    zwork = 0.6 * zq(per60("blk") + per60("tk"), S["toi_min"], 300) + 0.4 * ((dist - dist.mean()) / dist.std()).fillna(0)
    attr("wrk", "spec", zwork, one, 1e-6)
    # командная игра: насколько партнёрам лучше с ним, чем без него (WOWY по доле xG), + доля передач в очках
    ast_share = (S["a1"] + S["a2"]) / S["pts"].clip(lower=1)
    attr("tm", "tech", 0.7 * zq(S["wowy"], S["wowy_n"], 600.0) + 0.3 * zq(ast_share, S["pts"], 20), one, 1e-6)
    attr("pp", "spec", pp_rate, S["toipp"] / 60, 60.0)
    # хладнокровие: опыт важных матчей (плей-офф) и реализация в концовках при разнице в одну шайбу; данных мало
    po_gp = pd.Series({p: (((pages.get(str(p)) or {}).get("careerTotals") or {}).get("playoffs") or {}).get("gamesPlayed") or 0 for p in S.index}).astype(float)
    z_po = ((np.log1p(po_gp) - np.log1p(po_gp).mean()) / np.log1p(po_gp).std()).fillna(0)
    z_cl = zq(S["cl_gax"] / S["cl_n"].clip(lower=1), S["cl_n"], k_for("cmp", "cl_gax", "cl_n", 10))
    attr("cmp", "weak", 0.6 * z_po + 0.4 * z_cl, one, 1e-6)
    # стабильность: разброс игровой оценки от матча к матчу
    gs = PG.assign(gs=0.75 * PG["g_all"] + 0.7 * PG["a1"] + 0.55 * PG["a2"] + 0.05 * PG["blk"] + 0.15 * PG["pend"] - 0.15 * PG["pen"]
                   + 0.01 * PG["fow"] - 0.01 * PG["fol"] + 0.075 * PG["fsh"])
    gstat = gs.groupby("pid")["gs"].agg(["mean", "std", "count"])
    cv = gstat["std"] / gstat["mean"].clip(lower=0.25)          # коэффициент вариации игровой оценки
    attr("con", "cons", cv.reindex(S.index), gstat["count"].reindex(S.index).fillna(0), 120.0, sign=-1.0, prior=True)
    # корреляции между характеристиками (регулярные игроки): контроль дублей
    vals = pd.DataFrame({k: v["v"] for k, v in A.items()}).loc[regs]
    cm = vals.corr()
    meta["attr_corr_high"] = sorted([(a_, b_, round(float(cm.at[a_, b_]), 2)) for i_, a_ in enumerate(cm.columns) for b_ in cm.columns[i_ + 1:]
                                     if abs(cm.at[a_, b_]) >= 0.75], key=lambda x: -abs(x[2]))
    log("сильно связанные характеристики:", meta["attr_corr_high"])
    meta["reliability"] = ks

    # ── поправка на возраст: данные — средние за прошлые сезоны, а игрок уже старше (молодые выросли, ветераны сдали) ──
    curves = load_curves(a.curves)
    born = {int(k): v.get("birthDate") for k, v in pages.items()}
    mid_age = pg.assign(age=[_age_on(born.get(p_), int(se_[:4]) + 1, 2, 1) for p_, se_ in zip(pg["pid"], pg["season"])],
                        ww=pg["sec"] * pg["w"]).dropna(subset=["age"])
    mean_age = (mid_age["age"] * mid_age["ww"]).groupby(mid_age["pid"]).sum() / mid_age["ww"].groupby(mid_age["pid"]).sum()
    now_age = pd.Series({p_: _age_on(born.get(p_), 2026, 10, 1) for p_ in S.index})
    adj = {}
    if curves:
        for p_ in S.index:
            a0, a1 = mean_age.get(p_), now_age.get(p_)
            if a0 is None or a1 is None or pd.isna(a0) or pd.isna(a1):
                continue
            adj[p_] = {g: _curve_integral(curves[g], a0, a1) for g in ("shoot", "pass", "def", "mind", "phys")}
        dl = pd.Series({p_: np.mean(list(v.values())) for p_, v in adj.items()}).reindex(S.index).fillna(0)
        S["level"] = (S["level"] + dl).clip(1, 19.9)
        S["ca"] = (S["ca"] + CA_PER_LVL * dl).clip(40, 199)
        for k_, v_ in A.items():
            gk = CURVE_GROUP.get(k_, "mind")
            d_ = pd.Series({p_: adj[p_][gk] for p_ in adj}).reindex(S.index).fillna(0)
            nv = v_["v"] + d_
            if CAPS[v_["kind"]] is not None:
                lo_, hi_ = CAPS[v_["kind"]]
                nv = S["level"] + (nv - S["level"]).clip(lo_, hi_)
            v_["v"] = nv.clip(1, 19.9)
        meta["age_adjust"] = {"mean": round(float(dl.mean()), 3), "max": round(float(dl.max()), 2), "min": round(float(dl.min()), 2)}
        log("поправка на возраст:", meta["age_adjust"])

    # ── вратари ──
    G = goalies(shots, pg, pages, games, a.extra, curves, born)

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
            "ca": [round(float(S.at[p, "ca"]), 1), round(float(S.at[p, "ca_se"]), 1)],
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

CURVE_GROUP = {**{k: "phys" for k in ("spd", "acc", "sta", "str")}, **{k: "shoot" for k in ("fin", "pwr", "lng", "net")},
               **{k: "pass" for k in ("pas", "hnd", "vis", "pp")}, **{k: "def" for k in ("dfn", "tka", "blk", "hit", "pk")}}


def load_curves(path):
    if not path or not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        c = json.load(f)["groups"]
    return {g: {int(a): v for a, v in vals.items()} for g, vals in c.items()}


def _age_on(birth, y, m, d):
    """Возраст в годах (дробный) на дату."""
    if not birth:
        return None
    try:
        by, bm, bd = map(int, birth[:10].split("-"))
    except Exception:
        return None
    return (y - by) + ((m - bm) * 30.44 + (d - bd)) / 365.25


def _curve_integral(curve, a0, a1, step=0.1):
    """Изменение за отрезок возраста [a0, a1] (ед. шкалы): кривая — изменение за сезон в возрасте a → a+1."""
    if a0 is None or a1 is None or a1 <= a0:
        return 0.0 if (a0 is None or a1 is None or a1 == a0) else -_curve_integral(curve, a1, a0, step)
    tot, x = 0.0, a0
    while x < a1 - 1e-9:
        h = min(step, a1 - x)
        ai = int(np.floor(x)); f = x - ai
        v = curve.get(min(max(ai, 18), 40), 0) * (1 - f) + curve.get(min(max(ai + 1, 18), 40), 0) * f
        tot += v * h; x += h
    return tot


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


def goalies(shots, pg, pages, games, extra_path=None, curves=None, born=None):
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
    # мало бросков — тянем не к среднему, а к уровню запасного (незнакомые вратари в среднем ниже основных)
    z = z + (1 - agg["n"] / (agg["n"] + k)) * (-0.8)
    zw = Scale(agg["starts"] / agg["gp"].clip(lower=1), agg["gp"], 30.0, grp, reg, None, prior_on_level=False).z   # мало матчей — к среднему
    zl = 0.75 * z.fillna(0) + 0.25 * zw.fillna(0)
    regs = [i for i in agg.index if reg[i]]
    zG = (zl - zl[regs].mean()) / zl[regs].std()
    level = to_scale(zG).clip(1, 20)
    s_cg = float(np.clip((CA_TOP_G - CA_MID) / zG[regs].max(), 12, 26))
    ca = (CA_MID + s_cg * zG).clip(40, 199)
    # поправка на возраст (как у полевых): средний возраст сезонов в данных → возраст сейчас
    if curves and born:
        gpg = pg[pg["pid"].isin(agg.index)]
        ages = pd.Series([_age_on(born.get(p_), int(se_[:4]) + 1, 2, 1) for p_, se_ in zip(gpg["pid"], gpg["season"])], index=gpg.index)
        ww = gpg["sec"] * gpg["w"]
        ok_ = ages.notna()
        ma = (ages[ok_] * ww[ok_]).groupby(gpg.loc[ok_, "pid"]).sum() / ww[ok_].groupby(gpg.loc[ok_, "pid"]).sum()
        gadj = pd.Series({p_: _curve_integral(curves["goalie"], ma[p_], _age_on(born.get(p_), 2026, 10, 1))
                          for p_ in agg.index if p_ in ma.index and _age_on(born.get(p_), 2026, 10, 1) is not None}).reindex(agg.index).fillna(0)
        level = (level + gadj).clip(1, 19.9)
        ca = (ca + CA_PER_LVL * gadj).clip(40, 199)
        zG = zG + gadj / SCALE
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
    # стабильность: разброс сейвов сверх ожидаемого на бросок от матча к матчу (полные матчи, без весов сезонов)
    pg2 = sh.groupby(["goalie", "gid"]).agg(gs=("gsax", "sum"), n=("gsax", "size")).reset_index()
    pg2 = pg2[pg2["n"] >= 15]
    gstat = (pg2["gs"] / pg2["n"]).groupby(pg2["goalie"]).agg(["std", "count"])
    sc = Scale(-gstat["std"].reindex(agg.index), gstat["count"].reindex(agg.index).fillna(0), 60.0, grp, reg, zG, 1.0, False)
    res["gcon"] = combine(level, sc.z, sc.se, "cons")
    out = {}
    for p in agg.index:
        out[p] = {"toi": round(float(agg.at[p, "toi"]), 0), "gp": round(float(agg.at[p, "gp"]), 1),
                  "level": [round(float(level[p]), 2), round(float(se_l[p]), 2)],
                  "ca": [round(float(ca[p]), 1), round(float(np.clip(se_l[p] * s_cg / SCALE, 4, 18)), 1)],
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
