#!/usr/bin/env python3
"""Сборка базы игры HM27 (game/data/hm27-data.json) из данных NHL-STATS.RU (репозиторий Levastak/nhl-svodka).

Запуск:  python3 data/build.py <путь к клону nhl-svodka> [game/data/hm27-data.json]

Источники (все уже собраны «Сводкой» из публичных API НХЛ и MoneyPuck):
  site/data.json        — клубы, звенья и вратари (DailyFaceoff / последний матч), травмы, таблица,
                          статистика сезона, личный xG и xG против вратарей в этом сезоне, рост/вес/дата рождения,
                          номера, позиции, id игроков (через путь к фото);
  site/names_ru.json    — русские имена;
  scripts/prior.json    — прошлый сезон: рейтинги атаки/обороны команд по xG, личный xG игроков,
                          сейвы сверх ожидаемого у вратарей (GSAx) за 2021–2026.

Данные MoneyPuck — только для некоммерческого использования, со ссылкой на MoneyPuck.com.

Что считается (ТЗ, раздел 6):
  • сила команды — лог-рейтинги атаки и обороны, как в модели 4.4: прошлый сезон с весом 30 «матчей»
    плюс этот сезон (цель = 0,7·xG + 0,3·голы);
  • полевые: личный xG за матч и за 60 минут (прошлые сезоны не больше 20 матчей + этот),
    реализация (голы / xG, сильно сжатая к 1), передачи за матч, игровое время;
  • вратари: GSAx на единицу xG против (прошлые сезоны + этот, 200 «виртуальных» xG к нулю);
  • рейтинги 1–99 — перцентили среди игроков позиции (только для интерфейса).
"""
import datetime as dt
import json
import math
import os
import sys

TEAMS = {
    # аббревиатура: (город, клуб, полное имя, основной цвет, второй цвет, акцент)
    "ANA": ("Анахайм", "Анахайм Дакс", "Anaheim Ducks", "#FC4C02", "#111111", "#B9975B"),
    "BOS": ("Бостон", "Бостон Брюинз", "Boston Bruins", "#111111", "#FFB81C", "#FFFFFF"),
    "BUF": ("Баффало", "Баффало Сейбрз", "Buffalo Sabres", "#003087", "#FFB81C", "#FFFFFF"),
    "CGY": ("Калгари", "Калгари Флэймз", "Calgary Flames", "#C8102E", "#F1BE48", "#111111"),
    "CAR": ("Каролина", "Каролина Харрикейнз", "Carolina Hurricanes", "#CE1126", "#111111", "#A2AAAD"),
    "CHI": ("Чикаго", "Чикаго Блэкхокс", "Chicago Blackhawks", "#CF0A2C", "#111111", "#FFD100"),
    "COL": ("Колорадо", "Колорадо Эвеланш", "Colorado Avalanche", "#6F263D", "#236192", "#A2AAAD"),
    "CBJ": ("Коламбус", "Коламбус Блю Джекетс", "Columbus Blue Jackets", "#002654", "#CE1126", "#A4A9AD"),
    "DAL": ("Даллас", "Даллас Старз", "Dallas Stars", "#006847", "#111111", "#8F8F8C"),
    "DET": ("Детройт", "Детройт Ред Уингз", "Detroit Red Wings", "#CE1126", "#FFFFFF", "#111111"),
    "EDM": ("Эдмонтон", "Эдмонтон Ойлерз", "Edmonton Oilers", "#041E42", "#FF4C00", "#FFFFFF"),
    "FLA": ("Флорида", "Флорида Пантерз", "Florida Panthers", "#C8102E", "#041E42", "#B9975B"),
    "LAK": ("Лос-Анджелес", "Лос-Анджелес Кингз", "Los Angeles Kings", "#111111", "#A2AAAD", "#FFFFFF"),
    "MIN": ("Миннесота", "Миннесота Уайлд", "Minnesota Wild", "#154734", "#A6192E", "#EAAA00"),
    "MTL": ("Монреаль", "Монреаль Канадиенс", "Montreal Canadiens", "#AF1E2D", "#192168", "#FFFFFF"),
    "NSH": ("Нэшвилл", "Нэшвилл Предаторз", "Nashville Predators", "#FFB81C", "#041E42", "#FFFFFF"),
    "NJD": ("Нью-Джерси", "Нью-Джерси Девилз", "New Jersey Devils", "#CE1126", "#111111", "#FFFFFF"),
    "NYI": ("Айлендерс", "Нью-Йорк Айлендерс", "New York Islanders", "#00539B", "#F47D30", "#FFFFFF"),
    "NYR": ("Рейнджерс", "Нью-Йорк Рейнджерс", "New York Rangers", "#0038A8", "#CE1126", "#FFFFFF"),
    "OTT": ("Оттава", "Оттава Сенаторз", "Ottawa Senators", "#DA1A32", "#111111", "#B79257"),
    "PHI": ("Филадельфия", "Филадельфия Флайерз", "Philadelphia Flyers", "#F74902", "#111111", "#FFFFFF"),
    "PIT": ("Питтсбург", "Питтсбург Пингвинз", "Pittsburgh Penguins", "#111111", "#FCB514", "#FFFFFF"),
    "SJS": ("Сан-Хосе", "Сан-Хосе Шаркс", "San Jose Sharks", "#006D75", "#EA7200", "#111111"),
    "SEA": ("Сиэтл", "Сиэтл Кракен", "Seattle Kraken", "#001628", "#99D9D9", "#E9072B"),
    "STL": ("Сент-Луис", "Сент-Луис Блюз", "St. Louis Blues", "#002F87", "#FCB514", "#FFFFFF"),
    "TBL": ("Тампа-Бэй", "Тампа-Бэй Лайтнинг", "Tampa Bay Lightning", "#002868", "#FFFFFF", "#111111"),
    "TOR": ("Торонто", "Торонто Мейпл Лифс", "Toronto Maple Leafs", "#00205B", "#FFFFFF", "#A2AAAD"),
    "UTA": ("Юта", "Юта Маммот", "Utah Mammoth", "#111111", "#6CACE4", "#FFFFFF"),
    "VAN": ("Ванкувер", "Ванкувер Кэнакс", "Vancouver Canucks", "#00205B", "#00843D", "#FFFFFF"),
    "VGK": ("Вегас", "Вегас Голден Найтс", "Vegas Golden Knights", "#333F42", "#B4975A", "#C8102E"),
    "WSH": ("Вашингтон", "Вашингтон Кэпиталз", "Washington Capitals", "#C8102E", "#041E42", "#FFFFFF"),
    "WPG": ("Виннипег", "Виннипег Джетс", "Winnipeg Jets", "#041E42", "#004C97", "#AC162C"),
}
CONF = {"Atlantic": "Восточная", "Metropolitan": "Восточная", "Central": "Западная", "Pacific": "Западная"}
DIV_RU = {"Atlantic": "Атлантический", "Metropolitan": "Столичный", "Central": "Центральный", "Pacific": "Тихоокеанский"}

KEEP, LAM, XG_W = 0.65, 30.0, 0.7          # как в model.py
PRIOR_GP = 20                              # прошлые сезоны игрока весят не больше 20 матчей
G_K = 200.0                                # вратари: «виртуальные» xG против, тянущие к нулю
FIN_K = 25.0                               # реализация: «виртуальные» xG, тянущие к 1,0
AST_K = 20                                 # передачи: «виртуальные» матчи среднего уровня
POS_TOI = {"C": 16.5, "LW": 15.5, "RW": 15.5, "D": 19.5, "G": 60.0}


def nkey(s):
    return "".join(ch for ch in (s or "").split("(")[0].lower() if ch.isalpha())


def pct_rank(values):
    """Перцентили 1–99 (одинаковые значения — одинаковый перцентиль)."""
    srt = sorted(values)
    n = len(srt)
    out = []
    for v in values:
        lo = sum(1 for x in srt if x < v); eq = sum(1 for x in srt if x == v)
        out.append(max(1, min(99, round(100 * (lo + 0.5 * eq) / n))))
    return out


def main(src, out_path):
    D = json.load(open(os.path.join(src, "site/data.json"), encoding="utf-8"))
    P = json.load(open(os.path.join(src, "scripts/prior.json"), encoding="utf-8"))
    RU = json.load(open(os.path.join(src, "site/names_ru.json"), encoding="utf-8"))

    # ---------------- клубы: сила по xG (модель 4.4, без поправки на соперников)
    st = {x["abbr"]: x for x in D["standings"]}
    txg = D.get("team_xg") or {}
    prior_xg = P["teams_xg"]
    tgt = {}
    for t in TEAMS:
        x = txg.get(t) or {}; s = st.get(t) or {}
        gp = x.get("gp") or 0
        if gp:
            f = XG_W * x["xgf"] / gp + (1 - XG_W) * (s.get("gf", 0) / max(s.get("gp", 1), 1))
            a = XG_W * x["xga"] / gp + (1 - XG_W) * (s.get("ga", 0) / max(s.get("gp", 1), 1))
            tgt[t] = (gp, f, a)
    lg = sum(v[1] * v[0] for v in tgt.values()) / max(1, sum(v[0] for v in tgt.values())) if tgt else 3.05
    teams = []
    for t, (city, club, en, c1, c2, c3) in TEAMS.items():
        pa, pd = KEEP * prior_xg[t][0], KEEP * prior_xg[t][1]
        gp, f, a = tgt.get(t, (0, lg, lg))
        att = (LAM * pa + gp * math.log(f / lg)) / (LAM + gp)
        dfn = (LAM * pd + gp * math.log(a / lg)) / (LAM + gp)
        div = (st.get(t) or {}).get("div") or ""
        teams.append(dict(abbr=t, city=city, name=club, name_en=en, conf=CONF.get(div, ""), div=DIV_RU.get(div, div),
                          colors=[c1, c2, c3], logo=f"https://assets.nhle.com/logos/nhl/svg/{t}_light.svg",
                          att=round(att, 4), dfn=round(dfn, 4)))

    # ---------------- игроки
    heads, bio, nums, ppos = D["heads"], D["bio"], D["numbers"], D["ppos"]
    cur_sk = {nkey(p["name"]): p for p in D["skaters"]}
    cur_g = {nkey(g["name"]): g for g in D["goalies"]}
    sxg = {nkey(k): v for k, v in (D.get("skater_xg") or {}).items()}
    gxg = {nkey(k): v for k, v in (D.get("goalie_xg") or {}).items()}
    pix = {nkey(k): v for k, v in P["players_ixg"].items()}
    gsx = {nkey(k): v for k, v in P["goalies_gsax"].items()}
    pgl = {nkey(k): v for k, v in P["goalies"].items()}

    team_of = {}
    for t, tm in D["teams"].items():
        for line in tm.get("forwards", []) + tm.get("defense", []):
            for nm in line:
                team_of.setdefault(nm, t)
        for nm in tm.get("goalies", []):
            team_of.setdefault(nm, t)
        for x in tm.get("injuries", []):
            team_of.setdefault(x.get("name"), t)
    for p in D["skaters"] + D["goalies"]:
        team_of.setdefault(p["name"], p["team"])

    players = []
    by_name = {}
    for nm, hs in heads.items():
        try:
            pid = int(hs.rsplit("/", 1)[1])
        except Exception:
            continue
        t = team_of.get(nm) or hs.split("/")[1]
        if t not in TEAMS:
            continue
        pos = ppos.get(nm) or "C"
        pos = "D" if pos in ("LD", "RD", "D") else pos
        b = bio.get(nm) or [None, None, None, None]
        k = nkey(nm)
        p = dict(id=pid, name=nm, name_ru=RU.get(nm, nm), team=t, pos=pos, num=nums.get(nm) or nums.get(f"{nm}|{pos[0]}"),
                 h=b[0], w=b[1], born=b[2], country=b[3], head=hs)
        if pos == "G":
            pg, px = (gsx.get(k) or [0.0, 0.0])
            c = gxg.get(k) or {}
            cg = (c.get("xga") or 0) - (c.get("ga") or 0); cx = c.get("xga") or 0
            p["gsax"] = round((pg + cg) / (px + cx + G_K), 4)
            cs = cur_g.get(k) or {}
            p["stats"] = dict(gp=cs.get("gp", 0), w=cs.get("w", 0), l=cs.get("l", 0), otl=cs.get("otl", 0), sv=cs.get("sv", 0), gaa=cs.get("gaa", 0))
            p["prior_gp"] = (pgl.get(k) or [0])[0]
        else:
            pr = pix.get(k); c = sxg.get(k) or {}; cs = cur_sk.get(k) or {}
            gp = xg = 0.0
            if pr:
                w = min(1.0, PRIOR_GP / max(pr[0], 1)); gp += w * pr[0]; xg += w * pr[1]
            if c.get("gp"):
                gp += c["gp"]; xg += c.get("ixg", 0.0)
            toi = cs.get("toi") or POS_TOI[pos]
            # личный xG за матч, сжатый к низу позиции при малом числе матчей
            base = 0.08 if pos == "D" else 0.18
            ixg_pg = (xg + 5 * base) / (gp + 5)
            g_cur, a_cur, gp_cur = cs.get("g", 0), cs.get("a", 0), cs.get("gp", 0)
            fin = (g_cur + FIN_K) / ((c.get("ixg") or 0.0) + FIN_K)
            a_base = 0.22 if pos == "D" else 0.28
            apg = (a_cur + AST_K * a_base) / (gp_cur + AST_K)
            # «за 60 минут» с гармонизацией времени: у игроков с малым временем «за 60» завышено (слабые соперники,
            # только 5 на 5), у звёзд занижено — делим на среднее между своим временем и типичным для позиции
            ixg60 = ixg_pg * 60 / ((toi + POS_TOI[pos]) / 2)
            p.update(ixg_pg=round(ixg_pg, 4), ixg60=round(ixg60, 4), toi=round(toi, 2), fin=round(fin, 3), apg=round(apg, 3))
            p["stats"] = dict(gp=gp_cur, g=g_cur, a=a_cur, pts=cs.get("pts", 0), pm=cs.get("pm", 0), toi=cs.get("toi", 0))
        players.append(p)
        by_name[nm] = p

    # ---------------- рейтинги 1–99 (перцентиль среди позиции)
    def rate(group, field, key, invert=False):
        vals = [(-1 if invert else 1) * g[field] for g in group]
        for g, r in zip(group, pct_rank(vals)):
            g.setdefault("r", {})[key] = r
    fw = [p for p in players if p["pos"] in ("C", "LW", "RW")]
    dm = [p for p in players if p["pos"] == "D"]
    gk = [p for p in players if p["pos"] == "G"]
    for grp in (fw, dm):
        rate(grp, "ixg60", "att"); rate(grp, "apg", "pas"); rate(grp, "toi", "sta")
        if grp is dm:
            rate(grp, "toi", "dfn")   # v0: оборона защитника — доверие тренера (время); в v1 — xG против при нём на льду
        for p in grp:
            r = p["r"]
            # реализация — почти везение: узкая шкала вокруг 50
            r["fin"] = max(30, min(80, round(50 + (p["fin"] - 1) * 150)))
            if grp is fw:
                r["dfn"] = max(35, min(65, round(50 + (p["toi"] - POS_TOI[p["pos"]]) * 3)))
        for p in grp:
            r = p["r"]
            ovr = (0.4 * r["att"] + 0.3 * r["pas"] + 0.05 * r["fin"] + 0.1 * r["dfn"] + 0.15 * r["sta"]) if p["pos"] != "D" else \
                  (0.3 * r["dfn"] + 0.25 * r["sta"] + 0.2 * r["att"] + 0.2 * r["pas"] + 0.05 * r["fin"])
            r["ovr"] = round(ovr)
    rate(gk, "gsax", "gk")
    for p in gk:
        p["r"]["ovr"] = p["r"]["gk"]

    # ---------------- звенья клубов (по именам → id)
    def ids(names):
        return [by_name[n]["id"] for n in names if n in by_name]
    for tm in teams:
        src_t = D["teams"].get(tm["abbr"]) or {}
        tm["lines"] = dict(f=[ids(l) for l in src_t.get("forwards", [])], d=[ids(l) for l in src_t.get("defense", [])],
                           g=ids(src_t.get("goalies", [])), pp1=ids(src_t.get("pp1", [])), pp2=ids(src_t.get("pp2", [])))
        tm["injured"] = ids([x["name"] for x in src_t.get("injuries", []) if (x.get("status") or "").lower().startswith(("out", "ir", "ltir"))])
        s = st.get(tm["abbr"]) or {}
        tm["record"] = dict(gp=s.get("gp", 0), w=s.get("w", 0), l=s.get("l", 0), otl=s.get("otl", 0), pts=s.get("pts", 0), gf=s.get("gf", 0), ga=s.get("ga", 0))

    pack = dict(
        format=1, season=2026, built=dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%MZ"), data_date=D.get("results_date"),
        source="NHL-STATS.RU (github.com/Levastak/nhl-svodka): API НХЛ, MoneyPuck.com (некоммерческое использование), DailyFaceoff",
        league=dict(goals_pg=round(lg, 3)),
        teams=teams, players=sorted(players, key=lambda p: (p["team"], p["pos"], -(p.get("r", {}).get("ovr", 0)))),
    )
    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as fh:
        json.dump(pack, fh, ensure_ascii=False, separators=(",", ":"))
    missing = [(t["abbr"], len(sum(t["lines"]["f"], [])), len(sum(t["lines"]["d"], [])), len(t["lines"]["g"])) for t in teams
               if len(sum(t["lines"]["f"], [])) < 12 or len(sum(t["lines"]["d"], [])) < 6 or len(t["lines"]["g"]) < 2]
    print(f"Клубов {len(teams)}, игроков {len(players)} (нападающих {len(fw)}, защитников {len(dm)}, вратарей {len(gk)}); "
          f"голов за матч на команду {lg:.3f}; неполные звенья: {missing or 'нет'}; файл {out_path} ({os.path.getsize(out_path) // 1024} КБ)")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "game/data/hm27-data.json")
