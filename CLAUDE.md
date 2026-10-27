# Заметки для разработки HM27

Игру целиком пишет Claude; заказчик — Лев Кузьминых, тестирует сборки на Mac (M5) и ПК (RTX 4070). Весь текст для игрока — по-русски. ТЗ — `docs/concept.md`.

## Структура
- `core/HM27.Core` — ядро на C# (.NET 8) без Godot и без NuGet-пакетов. Движок уровня 1 (`MatchEngine.cs`) повторяет `scripts/model.py` v4.4 из Levastak/nhl-svodka; константы там же с комментариями.
- `core/HM27.Check` — консольная проверка критериев ТЗ 7.3; запускается в CI. Любая правка движка должна её проходить:
  `dotnet run -c Release --project core/HM27.Check -- game/data/hm27-data.json 10`
- `game/` — проект Godot 4.7.2 .NET. Интерфейс строится кодом (`game/ui/*.cs`), без .tscn-сцен, кроме корневых.
- `data/build.py` — база `game/data/hm27-data.json` из файлов nhl-svodka (`site/data.json`, `site/names_ru.json`, `scripts/prior.json`).
- Модель игрока (ТЗ — документ «HM27 — модель игрока»): `data/fetch_pbp.py` (workflow «Сырые данные» → релиз raw-data: протоколы, смены, страницы игроков, EDGE) →
  `data/derive.py` (xG, RAPM, характеристики 1–20 с погрешностью → `data/derived.json.gz`, ревью `data/review.py`, ручные поправки `data/overrides.json`) →
  `data/history.py` (возрастные кривые по сводкам MoneyPuck 2010–2026 → `data/age_curves.json`) →
  `data/merge_model.py` (в базу игры, формат 3; капитаны — `data/leaders.json`; тёзки в одном клубе — `overrides.json` → namesakes). Ассеты релиза качать через REST: `gh api -H "Accept: application/octet-stream" repos/Levastak/HM27/releases/assets/<id>`.
- Роли считаются в ядре из характеристик (`Roles` в `PlayerModel.cs`); проверка «роли без противоречий» в HM27.Check.
- В ядре: `PlayerModel.cs` — истинные значения (разыгрываются от сида вокруг данных), видимые (знание клуба), потенциал, характер; `Development` — раз в неделю.
  Движок учитывает сдвиг «истинный уровень − данные + форма» (LVL_K, G_LVL, D_LVL); база калибровки считается после розыгрыша, поэтому состав по умолчанию = сила модели.

## Принципы движка
- Состав по умолчанию (звенья из данных) даёт ровно силу модели: эффект решений игрока считается как отличие от базы (`BaseOff`, `BaseDef`, `BaseGsax` в `Career`).
- Случайность детерминирована: `Random(Seed * 1_000_003 ^ gameId)`. Не использовать `HashCode` (он случаен между процессами).
- 3Д не решает исход: только показывает то, что посчитал движок (ТЗ 7.4).

## Локальная проверка в рабочем окружении Claude
- NuGet.org недоступен: пакеты Godot берутся из локального источника (`~/.nuget/NuGet/NuGet.Config` → `GodotSharp/Tools/nupkgs` из папки Godot). В репозитории `nuget.config` нет — в CI используется nuget.org.
- Снимки экранов без монитора: Xvfb + программный Vulkan (llvmpipe). Скрипт `tools/shot.sh` (нужны `xvfb`, `mesa-vulkan-drivers`).
  Параметры игры для снимков: `-- --screen=menu|clubs|hub|match --tab=lines|table|stats|calendar|playoffs --days=N --sim=1 --played=1 --shot=путь.png`.
- Экспорт под Windows/macOS локально не работает (нужны runtime-пакеты с nuget.org) — сборки делает GitHub Actions (`.github/workflows/build.yml`), результат — релиз `dev`.
