#!/usr/bin/env python3
"""Ревью базы перед выпуском: топы и выбросы по каждой характеристике, проверка эталонных игроков.

Запуск:  python3 data/review.py data/derived.json.gz data/review.md
Вызывается и из derive.py (--report). Возвращает код 1, если эталонные игроки ниже порога.
"""
import gzip, json, sys

NAMES = {
    "fin": "Завершение", "pwr": "Мощь броска", "lng": "Дальний бросок", "net": "Игра у ворот", "pas": "Передача",
    "hnd": "Владение шайбой", "fo": "Вбрасывание", "dfn": "Позиционная оборона", "tka": "Отбор", "blk": "Блокирование",
    "hit": "Силовая борьба", "pk": "Меньшинство", "spd": "Скорость", "acc": "Ускорение", "str": "Сила", "sta": "Выносливость",
    "dec": "Решения", "vis": "Видение", "dis": "Дисциплина", "wrk": "Работоспособность", "tm": "Командная игра",
    "pp": "Большинство", "cmp": "Хладнокровие", "con": "Стабильность",
    "rfx": "Реакция", "gpos": "Позиция", "reb": "Контроль отскока", "scr": "Игра через заслон", "gsta": "Выносливость",
    "gcmp": "Хладнокровие", "gcon": "Стабильность",
}
# эталоны: игрок → минимальный уровень (страховка от поломки формул; правится вместе с базой)
ANCHORS = {
    "Connor McDavid": 18.5, "Nathan MacKinnon": 18.5, "Nikita Kucherov": 18, "Leon Draisaitl": 18, "Auston Matthews": 17.5,
    "Kirill Kaprizov": 17.5, "David Pastrnak": 17.5, "Cale Makar": 17, "Quinn Hughes": 17.5, "Adam Fox": 16.5,
    "Igor Shesterkin": 16, "Connor Hellebuyck": 16, "Andrei Vasilevskiy": 16,
}
TECH = ("fin", "pas", "hnd", "dfn", "dec", "vis", "tm")


def write(d, path):
    sk, gk = d["skaters"], d["goalies"]
    m = d["meta"]
    L = ["# HM27 — ревью базы игроков", "",
         f"Сборка {m['built']}, сезоны {', '.join(m['seasons'])}. Шкала: середина {m['scale']['mid']}, 1 ст. откл. = {m['scale']['sd']} ед.", ""]
    L += ["## Проверка расчёта", "",
          f"- xG: {m['xg']['shots']} бросков, AUC {m['xg']['auc']}, лог-лосс {m['xg']['logloss']}, средний xG {m['xg']['mean_xg']} при доле голов {m['xg']['goal_rate']}.",
          f"- RAPM: {m['rapm']['stints']} стинтов 5 на 5, лямбда {m['rapm']['lambda']}, преимущество площадки {m['rapm']['home_xg60']} xG/60.",
          "- Распределение уровня у регулярных полевых: " + ", ".join(f"{k}: {v:.1%}" for k, v in m["distribution"].items()) + ".",
          "- Надёжность метрик (корреляция половинок сезона → константа сжатия k): "
          + ", ".join(f"{k} r={v['r_half']} k={v['k']}" for k, v in m["reliability"].items()) + ".", ""]
    bad = []
    L += ["## Эталонные игроки", "", "| Игрок | Уровень | Порог | Ключевое |", "| --- | --- | --- | --- |"]
    allp = {**sk, **gk}
    by_name = {p["name"]: p for p in allp.values()}
    for n, lim in ANCHORS.items():
        p = by_name.get(n)
        if not p:
            L.append(f"| {n} | — | {lim} | нет в базе |"); bad.append(n); continue
        top = sorted(p["a"].items(), key=lambda kv: -kv[1][0])[:5]
        ok = p["level"][0] >= lim
        if not ok:
            bad.append(n)
        L.append(f"| {n} | {p['level'][0]:.1f}{'' if ok else ' ⚠'} | {lim} | " + ", ".join(f"{NAMES.get(k, k)} {v[0]:.0f}" for k, v in top) + " |")
    L.append("")
    L += ["## Топ-40 по уровню (полевые)", "", "| # | Игрок | Поз. | Уровень | ± | Минут |", "| --- | --- | --- | --- | --- | --- |"]
    for i, p in enumerate(sorted(sk.values(), key=lambda p: -p["level"][0])[:40], 1):
        L.append(f"| {i} | {p['name']} | {p['pos']} | {p['level'][0]:.1f} | {p['level'][1]:.1f} | {p['toi']:.0f} |")
    L += ["", "## Топ-15 вратарей", "", "| # | Вратарь | Уровень | ± | Бросков |", "| --- | --- | --- | --- | --- |"]
    for i, p in enumerate(sorted(gk.values(), key=lambda p: -p["level"][0])[:15], 1):
        L.append(f"| {i} | {p['name']} | {p['level'][0]:.1f} | {p['level'][1]:.1f} | {p['raw']['shots']:.0f} |")
    L += ["", "## Характеристики: топ-12 и сюрпризы", "",
          "Сюрприз — игрок с уровнем ниже 13 в топ-12 по технике. Такие строки стоит проверить глазами.", ""]
    keys = list(dict.fromkeys(k for p in sk.values() for k in p["a"]))
    for k in keys:
        rows = sorted((p for p in sk.values() if k in p["a"] and p["toi"] >= 300), key=lambda p: -p["a"][k][0])[:12]
        L.append(f"**{NAMES.get(k, k)}** ({m['attr_kinds'].get(k, '')}): " + ", ".join(
            f"{p['name']} {p['a'][k][0]:.1f}" + (" ⚠" if k in TECH and p["level"][0] < 13 else "") for p in rows))
        L.append("")
    for k in list(dict.fromkeys(k for p in gk.values() for k in p["a"])):
        rows = sorted((p for p in gk.values() if k in p["a"] and p["toi"] >= 1000), key=lambda p: -p["a"][k][0])[:10]
        L.append(f"**Вратари — {NAMES.get(k, k)}**: " + ", ".join(f"{p['name']} {p['a'][k][0]:.1f}" for p in rows))
        L.append("")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(L))
    return bad


if __name__ == "__main__":
    with gzip.open(sys.argv[1], "rt", encoding="utf-8") as f:
        d = json.load(f)
    bad = write(d, sys.argv[2])
    print("Эталоны ниже порога:", bad or "нет")
    sys.exit(1 if bad else 0)
