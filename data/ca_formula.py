#!/usr/bin/env python3
"""КА из характеристик (как в FM): веса по позициям, подобранные по базе.

КА = a + b·s + c·max(0, s − s0)²,  s = Σ w_k · (характеристика_k − 11).
Веса неотрицательные (характеристика не может «вредить» КА), подбор — по регулярным игрокам (NNLS), выпуклый хвост наверху —
у суперзвёзд характеристики упираются в 20, а КА растёт дальше. В КА входят только навыки: «опыт» (хладнокровие),
выносливость по игровому времени, стабильность и командная игра почти повторяют уровень и дали бы порочный круг.
Вызывается из merge_model.py; результат — db["model"]["ca"], ядро считает КА этой формулой (PlayerModel.CaOf).
"""
import numpy as np
from scipy.optimize import nnls

SKATER_KEYS = ["fin", "pwr", "lng", "net", "pas", "hnd", "fo", "dfn", "tka", "blk", "hit", "pk", "spd", "acc", "str", "dec", "vis", "dis", "wrk", "pp"]
GOALIE_KEYS = ["rfx", "gpos", "reb", "scr", "gsta", "gcon"]


def fit_group(players, keys, convex=True):
    X = np.array([[p["a"].get(k, [11])[0] - 11 for k in keys] for p in players])
    y = np.array([p["ca"][0] for p in players])
    w, _ = nnls(np.column_stack([X, np.ones(len(X)), -np.ones(len(X))]), y)
    w = w[:-2]
    s = X @ w
    s0 = float(np.percentile(s, 85))
    A = np.column_stack([np.ones_like(s), s, np.maximum(0, s - s0) ** 2 if convex else np.zeros_like(s)])
    a, b, c = np.linalg.lstsq(A, y, rcond=None)[0]
    pred = A @ np.array([a, b, c])
    r2 = 1 - ((y - pred) ** 2).sum() / ((y - y.mean()) ** 2).sum()
    return {"w": {k: round(float(v), 5) for k, v in zip(keys, w) if v > 1e-6}, "a": round(float(a), 4), "b": round(float(b), 5),
            "c": round(float(c), 6), "s0": round(s0, 4), "r2": round(float(r2), 3), "err": round(float(np.std(y - pred)), 2), "n": len(players)}


def fit(derived):
    sk = [p for p in derived["skaters"].values() if p["toi"] >= 800 and p["a"]]
    gk = [p for p in derived["goalies"].values() if p["toi"] >= 1500 and p["a"]]
    return {"F": fit_group([p for p in sk if p["pos"] != "D"], SKATER_KEYS), "D": fit_group([p for p in sk if p["pos"] == "D"], SKATER_KEYS),
            "G": fit_group(gk, GOALIE_KEYS, convex=False)}
