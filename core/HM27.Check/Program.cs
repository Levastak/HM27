using HM27.Core;

// Проверка ядра HM27 по критериям ТЗ (раздел 7.3). Код выхода 1 — какой-то критерий не выполнен.
var path = args.Length > 0 ? args[0] : "game/data/hm27-data.json";
int seasons = args.Length > 1 ? int.Parse(args[1]) : 20;
var data = GameData.Load(path);
var fails = new List<string>();
void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}: {detail}");
    if (!ok) fails.Add(name);
}

Console.WriteLine($"База: {data.Teams.Count} клубов, {data.Players.Count} игроков, данные на {data.DataDate}; голов за матч на команду {data.League.GoalsPerGame:F3}");

// ---------------------------------------------------------------- 0. Календарь
{
    var c = Career.New(data, "MIN", 1);
    var reg = c.Games;
    var perTeam = data.Teams.ToDictionary(t => t.Abbr, t => reg.Count(g => g.Home == t.Abbr || g.Away == t.Abbr));
    var home = data.Teams.ToDictionary(t => t.Abbr, t => reg.Count(g => g.Home == t.Abbr));
    int threeInRow = 0, b2b = 0;
    foreach (var t in data.Teams)
    {
        var days = reg.Where(g => g.Home == t.Abbr || g.Away == t.Abbr).Select(g => g.Date.DayNumber).OrderBy(x => x).ToList();
        for (int i = 2; i < days.Count; i++) if (days[i] - days[i - 2] == 2) threeInRow++;
        for (int i = 1; i < days.Count; i++) if (days[i] - days[i - 1] == 1) b2b++;
        if (days.Distinct().Count() != days.Count) threeInRow += 100;
    }
    Check("Календарь", perTeam.Values.All(v => v == 82) && home.Values.All(v => v == 41) && threeInRow == 0,
        $"{reg.Count} матчей, {reg.Min(g => g.Date)} — {reg.Max(g => g.Date)}; у всех 82 матча и 41 дома: {perTeam.Values.All(v => v == 82) && home.Values.All(v => v == 41)}; " +
        $"три дня подряд: {threeInRow}; второй матч за два дня в среднем {b2b / 32.0:F1} на клуб");
}

// ---------------------------------------------------------------- 1–3. Средние лиги и калибровка
int games = 0, ot = 0, so = 0, homeWins = 0, goals = 0, shots = 0, pp = 0, scored = 0;
var buckets = new double[20]; var bucketWins = new double[20]; var bucketN = new int[20];
var corr = new List<(double strength, double pts)>();
var champions = new Dictionary<string, int>();
bool playoffsOk = true;
// сверка с реальностью: очки и голы игроков на 82 матча против реального сезона 2025-26
var pPts = new Dictionary<int, double>(); var pG = new Dictionary<int, double>(); var pGp = new Dictionary<int, double>();
var top10 = new List<double>(); var topSnipers = new List<int>();
var svSd = new List<double>();   // разброс % отражённых у вратарей с 40+ матчами, по сезонам
for (int s = 0; s < seasons; s++)
{
    var c = Career.New(data, "MIN", 1000 + s);
    c.SimToEnd();
    foreach (var g in c.Games.Where(g => !g.Playoff))
    {
        var r = g.Result; games++;
        if (r.End != "REG") ot++;
        if (r.End == "SO") so++;
        if (r.HomeGoals > r.AwayGoals) homeWins++;
        goals += r.HomeGoals + r.AwayGoals; shots += r.HomeShots + r.AwayShots;
        pp += r.Goals.Count(e => e.Kind == "pp"); scored += r.Goals.Count(e => e.Kind != "so");
        double fav = Math.Max(r.PHome, 1 - r.PHome); bool favWon = (r.PHome >= 0.5) == (r.HomeGoals > r.AwayGoals);
        int b = Math.Min(19, (int)(fav * 20));
        buckets[b] += fav; bucketWins[b] += favWon ? 1 : 0; bucketN[b]++;
    }
    foreach (var t in data.Teams) corr.Add((t.Att - t.Dfn, c.Table[t.Abbr].Pts));
    foreach (var (id, l) in c.Skaters) { pPts[id] = pPts.GetValueOrDefault(id) + l.G + l.A; pG[id] = pG.GetValueOrDefault(id) + l.G; pGp[id] = pGp.GetValueOrDefault(id) + l.Gp; }
    top10.Add(c.Skaters.Values.Where(l => l.Gp >= 60).Select(l => (l.G + l.A) * 82.0 / l.Gp).OrderByDescending(x => x).Take(10).Average());
    topSnipers.AddRange(c.Skaters.OrderByDescending(x => x.Value.G).Take(10).Select(x => x.Key));
    var sv = c.Goalies.Values.Where(x => x.Gp >= 40).Select(x => x.SvPct).ToList();
    if (sv.Count > 5) svSd.Add(Math.Sqrt(sv.Average(x => Math.Pow(x - sv.Average(), 2))));
    if (c.Champion == null || c.Bracket.Count != 15 || c.Games.Count(g => g.Playoff) < 15 * 4) playoffsOk = false;
    else champions[c.Champion] = champions.GetValueOrDefault(c.Champion) + 1;
}
double gpg = goals / (double)games, target = 2 * data.League.GoalsPerGame;
Check("Голы за матч", Math.Abs(gpg / target - 1) <= 0.03, $"{gpg:F2} при цели {target:F2} (±3%), {games} матчей");
Check("Доля овертаймов", ot / (double)games is >= 0.19 and <= 0.26, $"{100.0 * ot / games:F1}% (НХЛ ~22–24%), буллиты {100.0 * so / games:F1}%");
Check("Победы хозяев", homeWins / (double)games is >= 0.52 and <= 0.565, $"{100.0 * homeWins / games:F1}% (НХЛ ~53–55%)");
Check("Броски в створ", Math.Abs(shots / (2.0 * games) / 28.5 - 1) <= 0.04, $"{shots / (2.0 * games):F1} на команду (НХЛ ~28–29)");
Check("Голы в большинстве", pp / (double)scored is >= 0.18 and <= 0.24, $"{100.0 * pp / scored:F1}% голов (НХЛ ~20–22%)");
Console.WriteLine("Калибровка фаворита (ожидание → факт):");
bool calOk = true;
for (int b = 10; b < 20; b++)
{
    if (bucketN[b] < 200) continue;
    double exp = buckets[b] / bucketN[b], act = bucketWins[b] / bucketN[b];
    Console.WriteLine($"   {exp * 100:F1}% → {act * 100:F1}%  ({bucketN[b]} матчей)");
    if (Math.Abs(exp - act) > 0.02 + 1.0 / Math.Sqrt(bucketN[b])) calOk = false;
}
Check("Калибровка", calOk, "движок выигрывает с той частотой, которую сам обещает (допуск 2 п.п. + шум выборки)");
double rr = Pearson(corr.Select(x => x.strength).ToList(), corr.Select(x => x.pts).ToList());
Check("Сила → очки", rr >= 0.6, $"корреляция силы клуба и очков за сезон r = {rr:F2} (ТЗ: ≥ 0,7 против реальной таблицы — проверим на исторических сезонах в этапе 1)");
{
    var real = data.Players.Where(p => p.Real != null && p.Real["gp"] >= 60 && pGp.GetValueOrDefault(p.Id) >= 50.0 * seasons).ToList();
    var gameP = real.Select(p => pPts[p.Id] / pGp[p.Id] * 82).ToList(); var realP = real.Select(p => (p.Real["g"] + p.Real["a"]) * 82.0 / p.Real["gp"]).ToList();
    var gameG = real.Select(p => pG[p.Id] / pGp[p.Id] * 82).ToList(); var realG = real.Select(p => p.Real["g"] * 82.0 / p.Real["gp"]).ToList();
    double rP = Pearson(gameP, realP), rG = Pearson(gameG, realG);
    double realTop = data.Players.Where(p => p.Real != null && p.Real["gp"] >= 60).Select(p => (p.Real["g"] + p.Real["a"]) * 82.0 / p.Real["gp"]).OrderByDescending(x => x).Take(10).Average();
    double gameTop = top10.Average();
    // среди 10 лучших снайперов сезона — нет игроков с реальным временем меньше 14 минут за матч
    var fringe = topSnipers.Where(id => data.PlayerById[id].Toi < 14).Select(id => data.PlayerById[id].Name).Distinct().ToList();
    Check("Сверка с реальностью: игроки", rP >= 0.8 && rG >= 0.8 && Math.Abs(gameTop / realTop - 1) <= 0.10 && fringe.Count == 0,
        $"корреляция с сезоном 2025-26 ({real.Count} игроков): очки r = {rP:F2}, голы r = {rG:F2}; средние на 82: очки {gameP.Average():F1}/{realP.Average():F1}; " +
        $"топ-10 бомбардиров {gameTop:F0}/{realTop:F0} очков на 82 ({(gameTop / realTop - 1) * 100:+0;-0}%); игроки с малым временем в топ-10 снайперов: {(fringe.Count == 0 ? "нет" : string.Join(", ", fringe.Take(4)))}");
}
Check("Вратари: разброс сезона", svSd.Average() is >= 0.008 and <= 0.013,
    $"стандартное отклонение % отражённых у вратарей с 40+ матчами {svSd.Average():F4} (НХЛ 2022–2026: 0,009–0,013)");
Check("Плей-офф", playoffsOk, $"15 серий и чемпион в каждом сезоне; чемпионы: {string.Join(", ", champions.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} {x.Value}"))}");

// ---------------------------------------------------------------- 4. Вратарь и звенья
{
    var worst = data.Players.Where(p => p.IsGoalie).OrderBy(p => p.Gsax).First();
    var worstTeam = worst.Team;
    double gaBase = 0, gaWorst = 0, ptsBase = 0, ptsWorst = 0, ptsTop = 0;
    for (int s = 0; s < 10; s++)
    {
        var c1 = Career.New(data, "MIN", 5000 + s); c1.SimToEnd();
        var c2 = Career.New(data, "MIN", 5000 + s);
        // слабейший вратарь лиги «переходит» в Миннесоту основным
        worst.Team = "MIN"; c2.Lineups["MIN"].G1 = worst.Id; c2.Lineups["MIN"].BackupOnB2B = false;
        c2.SimToEnd(); worst.Team = worstTeam;
        gaBase += c1.Table["MIN"].Ga; gaWorst += c2.Table["MIN"].Ga; ptsBase += c1.Table["MIN"].Pts; ptsWorst += c2.Table["MIN"].Pts;
        var c3 = Career.New(data, "MIN", 5000 + s);
        c3.Lineups["MIN"].FMin = new double[] { 23, 19, 12, 6 };   // больше времени первым звеньям
        c3.SimToEnd(); ptsTop += c3.Table["MIN"].Pts;
    }
    double d = gaWorst / gaBase - 1;
    Check("Чувствительность: вратарь", d is >= 0.03 and <= 0.16, $"{worst.NameRu} вместо основного: пропущено {gaBase / 10:F0} → {gaWorst / 10:F0} ({d * 100:+0.0;-0.0}%), очки {ptsBase / 10:F0} → {ptsWorst / 10:F0}");
    Check("Чувствительность: звенья", ptsTop > ptsBase - 2, $"больше времени первым звеньям: очки {ptsBase / 10:F1} → {ptsTop / 10:F1}");
}

// ---------------------------------------------------------------- 4б. Новые механики: травмы, оценки, темп, снятие вратаря
{
    var c = Career.New(data, "MIN", 4242); c.SimToEnd();
    var reg = c.Games.Where(g => !g.Playoff).ToList();
    double lost = 0;
    foreach (var g in reg)
        foreach (var t in new[] { g.Home, g.Away })
            lost += c.Injuries.Count(i => data.PlayerById[i.Id].Team == t && i.From < g.Date && i.Until >= g.Date);
    double perTeam = lost / 32;
    Check("Травмы", perTeam is >= 150 and <= 400, $"пропущено игроко-матчей за сезон: {perTeam:F0} на клуб (НХЛ ~200–300), новых травм {c.Injuries.Count}");
    var avg = c.Skaters.Values.Where(x => x.Rated > 0).Sum(x => x.RatingSum) / c.Skaters.Values.Sum(x => x.Rated);
    var gavg = c.Goalies.Values.Where(x => x.Rated > 0).Sum(x => x.RatingSum) / c.Goalies.Values.Sum(x => x.Rated);
    Check("Оценки за матч", avg is >= 6.5 and <= 7.1 && gavg is >= 6.4 and <= 7.3, $"средняя оценка полевых {avg:F2}, вратарей {gavg:F2} (как в FM: 6,5–7,1)");
    int userGames = c.Games.Count(g => g.Home == "MIN" || g.Away == "MIN");
    var mine = c.Games.First(g => g.Home == "MIN" || g.Away == "MIN");
    Check("Хроника матча", mine.Result.Events.Count > 60 && mine.Result.Events.Count(e => e.Kind == "goal") == mine.Result.Goals.Count(e => e.Kind != "so")
        && c.Games.Where(g => g.Home != "MIN" && g.Away != "MIN").All(g => g.Result.Events.Count == 0),
        $"событий в своём матче: {mine.Result.Events.Count} (голы совпадают со счётом), чужие матчи без хроники");

    double ptsBase = 0, ptsPull = 0, gBase = 0, gClosed = 0, ties = 0, tiesClosed = 0, n = 0;
    for (int s = 0; s < 12; s++)
    {
        var a = Career.New(data, "SJS", 9000 + s); a.SimToEnd();
        var b = Career.New(data, "SJS", 9000 + s); b.Lineups["SJS"].EarlyPull = true; b.SimToEnd();
        var d = Career.New(data, "SJS", 9000 + s); d.Lineups["SJS"].Tempo = -1; d.SimToEnd();
        ptsBase += a.Table["SJS"].Pts; ptsPull += b.Table["SJS"].Pts;
        foreach (var g in a.Games.Where(g => !g.Playoff && (g.Home == "SJS" || g.Away == "SJS"))) { gBase += g.Result.HomeGoals + g.Result.AwayGoals; if (g.Result.End != "REG") ties++; n++; }
        foreach (var g in d.Games.Where(g => !g.Playoff && (g.Home == "SJS" || g.Away == "SJS"))) { gClosed += g.Result.HomeGoals + g.Result.AwayGoals; if (g.Result.End != "REG") tiesClosed++; }
    }
    Check("Раннее снятие вратаря", ptsPull >= ptsBase - 12 && ptsPull <= ptsBase + 48, $"«Сан-Хосе», 12 сезонов: очков {ptsBase / 12:F1} → {ptsPull / 12:F1} за сезон (аналитика: раннее снятие слегка выгодно)");
    Check("Закрытый темп", gClosed < gBase && tiesClosed >= ties * 0.97, $"голов в матчах «Сан-Хосе» {gBase / n:F2} → {gClosed / n:F2}, овертаймов {100 * ties / n:F1}% → {100 * tiesClosed / n:F1}%");
}

// ---------------------------------------------------------------- 5. Путь игрока: «Дальше» → матч → следующий день, сохранение
{
    var c = Career.New(data, "CAR", 77);
    int userGames = 0, guard = 0;
    while (c.Phase != Phase.Finished && guard++ < 2000)
    {
        c.SimToUserGame();
        var g = c.UserGameToday();
        if (g != null) { c.Play(g); userGames++; c.SimDay(); }
        else if (c.Phase != Phase.Finished) c.SimDay();
        if (guard % 50 == 0)
        {
            // сохранение и загрузка посреди сезона не меняют состояние
            var json = c.ToJson();
            var back = Career.FromJson(json, data);
            if (back.ToJson() != json) { Check("Сохранение", false, "после загрузки состояние отличается"); break; }
            c = back;
        }
    }
    int poGames = c.Games.Count(g => g.Playoff && (g.Home == "CAR" || g.Away == "CAR"));
    Check("Путь игрока", c.Phase == Phase.Finished && userGames == 82 + poGames && c.Champion != null,
        $"сыграно своих матчей {userGames} (82 + {poGames} в плей-офф), чемпион {c.Champion}; сохранение/загрузка каждые 50 шагов совпадают");
}

// ---------------------------------------------------------------- 6. Модель игрока: розыгрыш, развитие, видимые значения
{
    var c1 = Career.New(data, "MIN", 2027);
    var c2 = Career.New(data, "MIN", 2027);
    bool same = System.Text.Json.JsonSerializer.Serialize(c1.Players) == System.Text.Json.JsonSerializer.Serialize(c2.Players);
    var regs = data.Players.Where(p => p.Lvl is { Length: > 1 } && p.LevelSe < 2.0).ToList();
    double meanShift = regs.Average(p => c1.Players[p.Id].Lvl - p.Level);
    double sdShift = Math.Sqrt(regs.Average(p => Math.Pow(c1.Players[p.Id].Lvl - p.Level, 2)));
    Check("Модель игрока: розыгрыш", same && Math.Abs(meanShift) < 0.15 && sdShift is > 0.4 and < 1.6,
        $"тот же сид → те же игроки: {same}; истинный уровень − данные: среднее {meanShift:+0.00;-0.00}, разброс {sdShift:F2} (у регулярных)");

    // вратари: звёзды остаются звёздами — топ-5 по данным в 20 карьерах остаются в топ-10 по истинному КА
    {
        var gk = data.Players.Where(p => p.IsGoalie && p.Lvl is { Length: > 1 } && p.LevelSe < 2.5).ToList();
        var top5 = gk.OrderByDescending(p => p.CaEst).Take(5).Select(p => p.Id).ToList();
        int stay = 0, tot = 0; var seasonSd = new List<double>(); var shift = new List<double>();
        for (int k = 0; k < 20; k++)
        {
            var ck = Career.New(data, "MIN", 7700 + k);
            var top8g = gk.OrderByDescending(p => ck.Players[p.Id].Ca).Take(8).Select(p => p.Id).ToHashSet();
            stay += top5.Count(top8g.Contains); tot += top5.Count;
            shift.AddRange(gk.Select(p => (double)(ck.Players[p.Id].Ca - p.CaEst)));
            seasonSd.AddRange(gk.Select(p => (double)ck.Players[p.Id].Season));
        }
        double sSd = Math.Sqrt(seasonSd.Average(x => x * x));
        double shSd = Math.Sqrt(shift.Average(x => x * x));
        Check("Модель игрока: вратари", stay / (double)tot >= 0.9 && shSd <= 5.5 && Math.Abs(sSd - PlayerModel.G_SEASON) < 0.3,
            $"истинный КА − данные: разброс {shSd:F1} (погрешность данных ~11 — талант разыгрывается уже, разброс сезонов даёт форма сезона); " +
            $"топ-5 по данным в топ-8 по истинному КА: {100.0 * stay / tot:F0}%; форма сезона: разброс {sSd:F2} ед. шкалы");
    }

    // защита от абсурда: техника не выше уровня + 3 (по данным)
    string[] tech = { "fin", "pas", "hnd", "dfn", "dec", "vis", "tm" };
    int viol = data.Players.Sum(p => tech.Count(k => p.A.TryGetValue(k, out var v) && v[0] > p.Level + 3.05));
    var stars = new[] { "Connor McDavid", "Nathan MacKinnon", "Nikita Kucherov", "Leon Draisaitl", "Cale Makar", "Kirill Kaprizov" };
    var low = stars.Select(n => data.Players.FirstOrDefault(p => p.Name == n)).Where(p => p == null || p.Level < 17).Select(p => p?.Name ?? "?").ToList();
    Check("Модель игрока: баланс", viol == 0 && low.Count == 0,
        $"техника выше уровня +3: {viol}; эталонные звёзды ниже 17: {(low.Count == 0 ? "нет" : string.Join(", ", low))}");

    // роли без противоречий: ключевые характеристики лучшей роли — не слабое место игрока (не ниже его типичных − 3)
    var bad = data.Players.Where(p => p.A.Count > 0).Select(p => (p, why: Roles.Contradiction(p))).Where(x => x.why != null).ToList();
    Check("Модель игрока: роли", bad.Count == 0,
        $"противоречий в ролях: {bad.Count}{(bad.Count > 0 ? " — " + string.Join("; ", bad.Take(6).Select(x => $"{x.p.Name} ({x.why})")) : "")}");

    // сезон развития
    var start = c1.Players.ToDictionary(kv => kv.Key, kv => kv.Value.Lvl);
    c1.SimToEnd();
    var young = data.Players.Where(p => p.Age(c1.Today) is <= 22).Select(p => c1.Players[p.Id].Lvl - start[p.Id]).ToList();
    var old = data.Players.Where(p => p.Age(c1.Today) is >= 34).Select(p => c1.Players[p.Id].Lvl - start[p.Id]).ToList();
    double all = data.Players.Average(p => c1.Players[p.Id].Lvl - start[p.Id]);
    double know = data.Roster("MIN").Average(p => c1.Players[p.Id].Know);
    var youngCa = data.Players.Where(p => p.Age(c1.Today) is <= 22).Select(p => c1.Players[p.Id].Ca - c1.Players[p.Id].Ca0).ToList();
    var oldCa = data.Players.Where(p => p.Age(c1.Today) is >= 34).Select(p => c1.Players[p.Id].Ca - c1.Players[p.Id].Ca0).ToList();
    var eliteYoung = data.Players.Where(p => p.Age(c1.Today) is <= 22 && p.CaEst >= 150).Select(p => c1.Players[p.Id].Pa - c1.Players[p.Id].Ca0).ToList();
    Check("Модель игрока: развитие", young.Average() > 0.25 && old.Average() < -0.2 && Math.Abs(all) < 0.35 && know > 0.95
                                       && youngCa.Average() > 2 && oldCa.Average() < -1.5 && eliteYoung.Count > 0 && eliteYoung.Average() > 8,
        $"за сезон (шкала 1–20): до 22 лет {young.Average():+0.00;-0.00} ({young.Count}), от 34 {old.Average():+0.00;-0.00} ({old.Count}), вся лига {all:+0.00;-0.00}; " +
        $"КА (1–200): до 22 {youngCa.Average():+0.0;-0.0}, от 34 {oldCa.Average():+0.0;-0.0}; запас роста у элитной молодёжи (КА 150+) ПА − КА = {eliteYoung.DefaultIfEmpty().Average():+0.0} ({eliteYoung.Count}); знание своего клуба {know:P0}");
    var p0 = data.Players.First(p => p.Name == "Connor McDavid");
    var (lo, hi) = PlayerModel.Range(c2, p0, "pas");
    var opp = data.Players.Where(p => p.Team != "MIN" && !p.IsGoalie).OrderBy(p => p.Fx.Seasons).First();
    var (lo2, hi2) = PlayerModel.Range(c2, opp, "pas");
    Check("Модель игрока: взгляд скаута", hi - lo <= 3 && hi2 - lo2 >= hi - lo,
        $"Макдэвид, передача: {lo}–{hi} (знание {c2.Players[p0.Id].Know:P0}); {opp.NameRu}: {lo2}–{hi2} (знание {c2.Players[opp.Id].Know:P0}); личность Макдэвида — {PlayerModel.Personality(c2.Players[p0.Id])}");
}

// ---------------------------------------------------------------- 7. ПА — потолок, а не прогноз (лабораторно: 10 лет развития молодых)
{
    var young = data.Players.Where(p => p.Age(new DateOnly(2026, 10, 6)) is <= 22 && p.CaEst >= 100).ToList();
    var rows = new List<(double pa, double peak, double exp)>();
    for (int seed = 1; seed <= 4; seed++)
    {
        var c = Career.New(data, "MIN", 700 + seed);
        var st = young.ToDictionary(p => p.Id, p => c.Players[p.Id]);
        var start = st.ToDictionary(kv => kv.Key, kv => (pa: (double)kv.Value.Pa, exp: (double)kv.Value.Peak));
        var best = st.ToDictionary(kv => kv.Key, kv => (double)kv.Value.Ca);
        c.Players = st;
        for (int y = 0; y < 10; y++)
        {
            Development.NewSeason(c);
            for (int w = 0; w < 26; w++) { c.Today = c.Today.AddDays(7); Development.Week(c); foreach (var kv in st) best[kv.Key] = Math.Max(best[kv.Key], kv.Value.Ca); }
            c.Today = c.Today.AddDays(365 - 26 * 7);
        }
        rows.AddRange(young.Select(p => (start[p.Id].pa, best[p.Id], start[p.Id].exp)));
    }
    double reach = rows.Count(r => r.peak >= r.pa - 5) / (double)rows.Count;
    double gap = rows.Average(r => r.peak - r.exp);
    Check("Модель игрока: ПА — потолок", reach is >= 0.15 and <= 0.4 && Math.Abs(gap) <= 4,
        $"молодые (до 22 лет, КА 100+), 10 лет развития, {rows.Count} карьер: дошли до ПА (±5) {reach:P0} (цель 20–30%); " +
        $"средний пик относительно ожидаемого {gap:+0.0;-0.0}; ПА выше пика в среднем на {rows.Average(r => r.pa - r.peak):F1}");
}

Console.WriteLine(fails.Count == 0 ? "\nВсе критерии выполнены." : $"\nНе выполнено: {string.Join(", ", fails)}");
return fails.Count == 0 ? 0 : 1;

static double Pearson(List<double> x, List<double> y)
{
    double mx = x.Average(), my = y.Average();
    double sxy = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
    double sx = Math.Sqrt(x.Sum(a => (a - mx) * (a - mx))), sy = Math.Sqrt(y.Sum(b => (b - my) * (b - my)));
    return sxy / (sx * sy);
}
