using HM27.Core;

// Проверка ядра HM27 по критериям ТЗ (раздел 7.3). Код выхода 1 — какой-то критерий не выполнен.
var path = args.Length > 0 ? args[0] : "game/data/hm27-data.json";
int seasons = args.Length > 1 && int.TryParse(args[1], out var seasonsArg) ? seasonsArg : 20;
var data = GameData.Load(path);
var fails = new List<string>();
void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}: {detail}");
    if (!ok) fails.Add(name);
}

if (args.Length > 1 && args[1] == "engine") { EngineReport.Run(data, args.Length > 2 ? int.Parse(args[2]) : 1); return 0; }
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
var lgLead = new List<double>(); var lgLast = new List<double>(); var lgSd = new List<double>(); int favFirst = 0; double jumps = 0; List<string> prevFin = null;
var champions = new Dictionary<string, int>();
bool playoffsOk = true;
// сверка с реальностью: очки и голы игроков на 82 матча против реального сезона 2025-26
var pPts = new Dictionary<int, double>(); var pG = new Dictionary<int, double>(); var pGp = new Dictionary<int, double>();
var top10 = new List<double>(); var topSnipers = new List<int>();
var svSd = new List<double>();   // разброс % отражённых у вратарей с 40+ матчами, по сезонам
// зеркальная проверка: те же тесты, что на реальных данных 2022–2026 (на всех сезонах игры)
var mirrorPts = new Dictionary<int, List<(bool po, int pts)>>();
var mirror4 = new Dictionary<int, List<(bool po, int pts)>>();   // сезоны 1–4 и 5–8 — тот же объём данных, что у реальной проверки
var mirror4b = new Dictionary<int, List<(bool po, int pts)>>();
var mirrorGk = new List<double>();
for (int s = 0; s < seasons; s++)
{
    var c = Career.New(data, "MIN", 1000 + s);
    // сила клубов до сезона: «снизу вверх» из игроков (как её видит движок перед первым матчем)
    var pre = data.Teams.ToDictionary(t => t.Abbr, t => { var sd = c.Side(t.Abbr, c.Today); return (sd.Att ?? t.Att) - (sd.Dfn ?? t.Dfn); });
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
    foreach (var t in data.Teams) corr.Add((pre[t.Abbr], c.Table[t.Abbr].Pts));
    {
        var pts = c.Table.Values.Select(r => (double)r.Pts).OrderBy(x => x).ToList();
        double m = pts.Average();
        lgLead.Add(pts[^1]); lgLast.Add(pts[0]); lgSd.Add(Math.Sqrt(pts.Average(x => (x - m) * (x - m))));
        var fin = c.Table.OrderByDescending(kv => kv.Value.Pts).Select(kv => kv.Key).ToList();
        if (fin[0] == pre.OrderByDescending(kv => kv.Value).First().Key) favFirst++;
        if (prevFin != null) jumps += fin.Take(8).Count(t => prevFin.IndexOf(t) >= 16);
        prevFin = fin;
    }
    foreach (var (id, l) in c.Skaters) { pPts[id] = pPts.GetValueOrDefault(id) + l.G + l.A; pG[id] = pG.GetValueOrDefault(id) + l.G; pGp[id] = pGp.GetValueOrDefault(id) + l.Gp; }
    top10.Add(c.Skaters.Values.Where(l => l.Gp >= 60).Select(l => (l.G + l.A) * 82.0 / l.Gp).OrderByDescending(x => x).Take(10).Average());
    topSnipers.AddRange(c.Skaters.OrderByDescending(x => x.Value.G).Take(10).Select(x => x.Key));
    if (s < 10)
    {
        var byGk = new Dictionary<int, List<(int sa, int ga)>>();
        foreach (var g in c.Games.Where(g => g.Result != null).OrderBy(g => g.Date).ThenBy(g => g.Id))
        {
            var r = g.Result;
            var pts = new Dictionary<int, int>();
            foreach (var e in r.Goals.Where(e => e.Kind != "so"))
                foreach (var id in new[] { e.Scorer, e.A1, e.A2 }) if (id != 0) pts[id] = pts.GetValueOrDefault(id) + 1;
            foreach (var id in r.HomeDressed.Concat(r.AwayDressed))
                if (!data.PlayerById[id].IsGoalie)
                {
                    (mirrorPts.TryGetValue(id, out var l) ? l : mirrorPts[id] = new()).Add((g.Playoff, pts.GetValueOrDefault(id)));
                    if (s < 8) { var m4 = s < 4 ? mirror4 : mirror4b; (m4.TryGetValue(id, out var l4) ? l4 : m4[id] = new()).Add((g.Playoff, pts.GetValueOrDefault(id))); }
                }
            if (!g.Playoff)
            {
                int gaH = r.Goals.Count(e => e.Team == g.Away && e.Kind != "so" && e.Kind != "en"), gaA = r.Goals.Count(e => e.Team == g.Home && e.Kind != "so" && e.Kind != "en");
                (byGk.TryGetValue(r.HomeGoalie, out var a) ? a : byGk[r.HomeGoalie] = new()).Add((r.AwayShots, gaH));
                (byGk.TryGetValue(r.AwayGoalie, out var b) ? b : byGk[r.AwayGoalie] = new()).Add((r.HomeShots, gaA));
            }
        }
        var halves = byGk.Values.Where(l => l.Count >= 40).Select(l => (
            a: 1 - l.Where((_, i) => i % 2 == 0).Sum(x => x.ga) / (double)l.Where((_, i) => i % 2 == 0).Sum(x => x.sa),
            b: 1 - l.Where((_, i) => i % 2 == 1).Sum(x => x.ga) / (double)l.Where((_, i) => i % 2 == 1).Sum(x => x.sa))).ToList();
        if (halves.Count > 5) mirrorGk.Add(Pearson(halves.Select(x => x.a).ToList(), halves.Select(x => x.b).ToList()));
    }
    var sv = c.Goalies.Values.Where(x => x.Gp >= 40).Select(x => x.SvPct).ToList();
    if (sv.Count > 5) svSd.Add(Math.Sqrt(sv.Average(x => Math.Pow(x - sv.Average(), 2))));
    if (c.Champion == null || c.Bracket.Count != 15 || c.Games.Count(g => g.Playoff) < 15 * 4) playoffsOk = false;
    else champions[c.Champion] = champions.GetValueOrDefault(c.Champion) + 1;
}
double gpg = goals / (double)games, target = 2 * data.League.GoalsPerGame;
Check("Голы за матч", Math.Abs(gpg / target - 1) <= 0.03, $"{gpg:F2} при цели {target:F2} (±3%), {games} матчей");
Check("Доля овертаймов", ot / (double)games is >= 0.19 and <= 0.26, $"{100.0 * ot / games:F1}% (НХЛ ~22–24%), буллиты {100.0 * so / games:F1}%");
Check("Победы хозяев", homeWins / (double)games is >= 0.52 and <= 0.565, $"{100.0 * homeWins / games:F1}% (НХЛ ~53–55%)");
// НХЛ 2022-26 по протоколам: 29,3 броска в створ (с голами) на команду за матч (data/engine_targets.json)
Check("Броски в створ", Math.Abs(shots / (2.0 * games) / 29.3 - 1) <= 0.04, $"{shots / (2.0 * games):F1} на команду (НХЛ 2022-26: 29,3)");
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
Check("Сила → очки", rr >= 0.55, $"корреляция силы клуба до сезона (из игроков) и очков r = {rr:F2}");
// таблица как в НХЛ 2022–2026: лидер 114–135, последний 47–58, разброс 13,0–18,6; из нижней половины прошлого сезона в топ-8 — 1–3
Check("Таблица как в НХЛ", lgLead.Average() is >= 112 and <= 130 && lgLast.Average() is >= 46 and <= 60 && lgSd.Average() is >= 13 and <= 18.6
        && jumps / Math.Max(1, seasons - 1) is >= 1 and <= 3.5 && favFirst / (double)seasons <= 0.45,
    $"лидер {lgLead.Average():F0} (НХЛ 114–135), последний {lgLast.Average():F0} (47–58), разброс {lgSd.Average():F1} (13,0–18,6); " +
    $"«выстрелов» из нижней половины в топ-8: {jumps / Math.Max(1, seasons - 1):F1} за сезон (НХЛ 1–3); сильнейший до сезона берёт 1-е место в {100.0 * favFirst / seasons:F0}% сезонов");
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
{
    // «клатч»: очки за матч в плей-офф сверх своего темпа регулярки; половинки — чётные и нечётные матчи игрока.
    // Реальность 2022–2026: r = 0,017 (484 игрока) — особого плей-офф-таланта нет; игра не должна его «изобретать» заметно
    double ClutchR(Dictionary<int, List<(bool po, int pts)>> src, out int n)
    {
    var a = new List<double>(); var b = new List<double>();
    foreach (var (id, l) in src)
    {
        double R(int h)
        {
            var x = l.Where((_, i) => i % 2 == h).ToList();
            var reg = x.Where(t => !t.po).ToList(); var po = x.Where(t => t.po).ToList();
            if (po.Count < 4 || reg.Count < 20) return double.NaN;
            double exp = po.Count * reg.Average(t => t.pts);
            return (po.Sum(t => t.pts) - exp) / Math.Sqrt(Math.Max(exp, 1e-3));
        }
        double r0 = R(0), r1 = R(1);
        if (!double.IsNaN(r0) && !double.IsNaN(r1)) { a.Add(r0); b.Add(r1); }
    }
    n = a.Count;
    return a.Count > 20 ? Pearson(a, b) : double.NaN;
    }
    // среднее по двум независимым блокам по 4 сезона — меньше шума проверки
    double rc1 = ClutchR(mirror4, out int n1), rc2 = mirror4b.Count > 0 ? ClutchR(mirror4b, out _) : double.NaN;
    double rc = double.IsNaN(rc2) ? rc1 : (rc1 + rc2) / 2;
    var a = new List<double>(new double[n1]);
    // опыт в плей-офф по всем игрокам сразу: очки в плей-офф / ожидаемые по темпу регулярки ~ 1 + bх·(хладнокровие − 11) + bу·(уровень − 11).
    // Реальность 2022–2026 (по минутам, 557 игроков): bх = +0,050 (90%: 0,024–0,077), bу = −0,038 — при равном уровне опытные держатся лучше
    var rows = new List<(double y, double w, double x1, double x2)>();
    foreach (var (id, l) in mirrorPts)
    {
        var reg = l.Where(t => !t.po).ToList(); var po = l.Where(t => t.po).ToList();
        if (po.Count < 5 || reg.Count < 60) continue;
        var p = data.PlayerById[id];
        double exp = po.Count * reg.Average(t => t.pts);
        if (exp <= 0) continue;
        rows.Add((po.Sum(t => t.pts) / exp, exp, p.AttrEst("cmp") - 11, p.Level - 11));
    }
    var bx = Wls(rows);
    double rg = mirrorGk.Count > 0 ? mirrorGk.Average() : double.NaN;
    Check("Зеркальная проверка", rc <= 0.12 && rg is >= 0.15 and <= 0.5 && bx.b1 is >= -0.01 and <= 0.09,
        $"те же тесты, что на реальных данных: «клатч» в плей-офф r = {rc:F3} ({a.Count} игроков, 4 сезона; НХЛ 0,017 ± 0,05, предел 0,12); " +
        $"опыт в плей-офф: +{bx.b1:F3} очков на единицу хладнокровия при равном уровне (НХЛ +0,050, 90%: 0,024–0,077), уровень {bx.b2:+0.000;-0.000} (НХЛ −0,038); " +
        $"вратари, % отражённых в чётных и нечётных матчах сезона r = {rg:F2} (НХЛ 0,19–0,55, в среднем 0,34)");
}
Check("Вратари: разброс сезона", svSd.Average() is >= 0.008 and <= 0.013,
    $"стандартное отклонение % отражённых у вратарей с 40+ матчами {svSd.Average():F4} (НХЛ 2022–2026: 0,009–0,013)");
// ---------------------------------------------------------------- Движок по сменам: особенности и реализация как в протоколах НХЛ
{
    var D = ShiftEngine.Diag = new ShiftEngine.Diagnostics();
    var cs = new List<Career>();
    double lowN = 0, allN = 0;
    for (int s = 0; s < 3; s++)
    {
        var c = Career.New(data, "MIN", 5000 + s);
        while (c.Phase == Phase.Regular)
        {
            foreach (var g in c.GamesOn(c.Today).Where(g => !g.Played))
                foreach (var t in new[] { g.Home, g.Away })
                    foreach (var id in c.Lineups[t].Skaters) { allN++; if (c.Players[id].Fresh < Fatigue.FRESH_OK) lowN++; }
            c.SimDay();
        }
        cs.Add(c);
    }
    ShiftEngine.Diag = null;
    // перегрузка своего клуба: первым звеньям огромные минуты, основной вратарь без отдыха
    double lowO = 0, allO = 0;
    var ov = Career.New(data, "MIN", 5100); var ol = ov.Lineups["MIN"];
    ol.FMin = new double[] { 31, 17, 8, 4 }; ol.DMin = new double[] { 35, 16, 9 }; ol.BackupOnB2B = false; int og = ol.G1;
    while (ov.Phase == Phase.Regular)
    {
        foreach (var g in ov.GamesOn(ov.Today).Where(g => !g.Played && (g.Home == "MIN" || g.Away == "MIN")))
            foreach (var id in ov.Lineups["MIN"].F[0].Concat(ov.Lineups["MIN"].D[0])) { allO++; if (ov.Players[id].Fresh < Fatigue.FRESH_OK) lowO++; }
        ov.SimDay();
    }
    int ogGp = ov.Goalies.GetValueOrDefault(og)?.Gp ?? 0;
    Check("Нагрузка", lowN / allN < 0.02 && lowO / Math.Max(1, allO) > 0.05 && ov.Players[og].Fresh < Fatigue.FRESH_OK + 10,
        $"обычные звенья (все клубы): матчей в усталости {lowN / allN:P1} (меньше 2%); перегрузка (первым звеньям ~28/31 мин всего, вратарь без отдыха): первая тройка и пара устали в {lowO / Math.Max(1, allO):P0} матчей, " +
        $"основной вратарь сыграл {ogGp}, свежесть к концу {ov.Players[og].Fresh:F0}");
    // «горячая рука»: очки в соседних отрезках по 10 матчей (за вычетом среднего за сезон) — как в НХЛ 2023-26 (−0,085…−0,152)
    {
        var xs = new List<double>(); var ys = new List<double>();
        foreach (var c in cs)
        {
            var seq = new Dictionary<int, List<double>>();
            foreach (var g in c.Games.Where(g => g.Played && !g.Playoff).OrderBy(g => g.Date))
            {
                var pts = new Dictionary<int, int>();
                foreach (var e in g.Result.Goals.Where(e => e.Kind != "so")) foreach (var id in new[] { e.Scorer, e.A1, e.A2 }) if (id != 0) pts[id] = pts.GetValueOrDefault(id) + 1;
                foreach (var id in g.Result.HomeDressed.Concat(g.Result.AwayDressed))
                    if (!data.PlayerById[id].IsGoalie) { if (!seq.ContainsKey(id)) seq[id] = new(); seq[id].Add(pts.GetValueOrDefault(id)); }
            }
            foreach (var q0 in seq.Values.Where(q => q.Count >= 60))
            {
                var q = q0.Take(80).ToList(); double m = q.Average();
                var bl = new List<double>(); for (int i = 0; i + 10 <= q.Count; i += 10) bl.Add(q.Skip(i).Take(10).Average() - m);
                for (int i = 0; i + 1 < bl.Count; i++) { xs.Add(bl[i]); ys.Add(bl[i + 1]); }
            }
        }
        double rs = Pearson(xs, ys);
        Check("Серии игроков", rs is >= -0.18 and <= -0.08, $"очки в соседних отрезках по 10 матчей (минус средний за сезон): r = {rs:F3} (НХЛ 2023-26: −0,085…−0,152; чистая случайность ≈ −0,14)");
    }
    var tp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "..", "..", "data", "engine_targets.json");
    var sig = EngineReport.TraitSignatures(data, cs, D, tp);
    var bad = sig.Where(x => Math.Abs(Math.Log(x.sim / x.real)) > Math.Log(1.3)).ToList();
    Check("Особенности в игре", sig.Count >= 12 && bad.Count == 0,
        $"{sig.Count} подписей особенностей (обладатели / остальные по показателю из протоколов), расхождение больше ×1,3: " +
        (bad.Count == 0 ? "нет" : string.Join(", ", bad.Select(x => $"{x.key} ×{x.sim:F2} при НХЛ ×{x.real:F2}"))));
    var f = EngineReport.Finishing(data, D); var rf = EngineReport.Real(tp);
    Check("Реализация по зонам", rf != null && Math.Abs(f.fin - rf.Value.fin) <= 0.015 && Math.Abs(f.lng - rf.Value.lng) <= 0.025 && f.top3 is >= 0.85 and <= 1.05,
        $"регрессия по оценкам из базы, 5×5: завершение {f.fin:+0.000} (НХЛ {rf?.fin:+0.000}), дальний бросок издали {f.lng:+0.000} (НХЛ {rf?.lng:+0.000}), топ-3% по завершению ×{f.top3:F2} (НХЛ ×{rf?.top3:F2})");
}

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
    // ожидаемая разница голов (детерминированно) растёт; очки за 10 сезонов — не падают сверх шума (теперь больше минут = и больше травм у лидеров)
    var cl = Career.New(data, "MIN", 1); var gfBase = cl.LineupEffect("MIN"); cl.Lineups["MIN"].FMin = new double[] { 23, 19, 12, 6 }; var gfTop = cl.LineupEffect("MIN");
    double effTop = (gfTop.gf - gfTop.ga) - (gfBase.gf - gfBase.ga);
    Check("Чувствительность: звенья", effTop > 0.01 && ptsTop > ptsBase - 40, $"больше времени первым звеньям: разница голов за матч {effTop:+0.000}, очки {ptsBase / 10:F1} → {ptsTop / 10:F1} (травм у лидеров больше)");
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
    // по частотам НХЛ 2023-26 (6×5: 0,0023 гола в секунду, в пустые 0,0066) раннее снятие почти нейтрально: оптимум за 3–4 минуты
    // даёт +1 п.п. шанса сравнять, это ~+0,2 очка за сезон — меньше шума 12 сезонов (±0,6); проверяем, что настройка не вредит заметно
    Check("Раннее снятие вратаря", ptsPull >= ptsBase - 24 && ptsPull <= ptsBase + 48, $"«Сан-Хосе», 12 сезонов: очков {ptsBase / 12:F1} → {ptsPull / 12:F1} за сезон (по частотам НХЛ — около нуля, чуть выгоднее)");
    // меньше голов → чуть больше ничьих (+0,6 п.п. при −7% голов), но за 12 сезонов разброс доли ОТ ±1,8 п.п. (матчи расходятся целиком) —
    // проверяем, что голов меньше, а ОТ не падает сверх шума
    Check("Закрытый темп", gClosed < gBase && tiesClosed >= ties - 0.035 * n, $"голов в матчах «Сан-Хосе» {gBase / n:F2} → {gClosed / n:F2}, овертаймов {100 * ties / n:F1}% → {100 * tiesClosed / n:F1}%");
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

    // характер: сильные метки на старте — только с фактами; через 10 сезонов появляются новые лидеры из игры
    {
        var ck = Career.New(data, "MIN", 3131);
        bool LeadFact(PlayerData p) => p.Fx != null && (p.Fx.Cap != null || p.Fx.CapSeasons > 0 || p.Fx.Messier > 0 || p.Fx.Clancy > 0 || p.Fx.Awards?.Contains("leader") == true);
        bool ProfFact(PlayerData p) => p.Fx != null && (p.Fx.Masterton > 0 || p.Fx.Clancy > 0 || p.Fx.Gp82 >= 0.97 || p.Fx.Awards?.Contains("pro") == true);
        var noFactLeaders = data.Players.Where(p => ck.Players[p.Id].Lead >= 17 && !LeadFact(p)).Select(p => p.Name).ToList();
        var noFactPros = data.Players.Where(p => ck.Players[p.Id].Prof >= 16 && !ProfFact(p)).Select(p => p.Name).ToList();
        int lead0 = data.Players.Count(p => ck.Players[p.Id].Lead >= 17);
        var notLead = data.Players.Where(p => ck.Players[p.Id].Lead < 17).Select(p => p.Id).ToHashSet();
        for (int y = 0; y < 8; y++) { ck.Today = ck.Today.AddDays(365); Development.NewSeason(ck); }
        var alive = data.Players.Where(p => (p.Age(ck.Today) ?? 40) <= 37).ToList();
        int lead8 = alive.Count(p => ck.Players[p.Id].Lead >= 17);
        int fresh = alive.Count(p => ck.Players[p.Id].Lead >= 17 && notLead.Contains(p.Id));
        Check("Модель игрока: характер", noFactLeaders.Count == 0 && noFactPros.Count == 0 && fresh >= 10 && lead8 <= lead0 * 2.5,
            $"на старте лидеров (лидерство 17+) {lead0}, без фактов: {(noFactLeaders.Count == 0 ? "нет" : string.Join(", ", noFactLeaders.Take(4)))}; " +
            $"профессионалов без фактов: {(noFactPros.Count == 0 ? "нет" : string.Join(", ", noFactPros.Take(4)))}; через 8 сезонов лидеров {lead8} (среди тех, кому ≤ 37), из них новых {fresh}");
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
    Development.Offseason(c1);   // год развития = сезон (30%) + лето (70%)
    var young = data.Players.Where(p => p.Age(c1.Today) is <= 22).Select(p => c1.Players[p.Id].Lvl - start[p.Id]).ToList();
    var old = data.Players.Where(p => p.Age(c1.Today) is >= 34).Select(p => c1.Players[p.Id].Lvl - start[p.Id]).ToList();
    double all = data.Players.Average(p => c1.Players[p.Id].Lvl - start[p.Id]);
    double know = data.Roster("MIN").Average(p => c1.Players[p.Id].Know);
    var youngCa = data.Players.Where(p => p.Age(c1.Today) is <= 22).Select(p => c1.Players[p.Id].Ca - c1.Players[p.Id].Ca0).ToList();
    var oldCa = data.Players.Where(p => p.Age(c1.Today) is >= 34).Select(p => c1.Players[p.Id].Ca - c1.Players[p.Id].Ca0).ToList();
    var eliteYoung = data.Players.Where(p => p.Age(c1.Today) is <= 22 && p.CaEst >= 150).Select(p => c1.Players[p.Id].Pa - c1.Players[p.Id].Ca0).ToList();
    Check("Модель игрока: развитие", young.Average() > 0.25 && old.Average() < -0.2 && Math.Abs(all) < 0.35 && know > 0.95
                                       && youngCa.Average() > 2 && oldCa.Average() < -1.5 && eliteYoung.Count > 0 && eliteYoung.Average() > 8,
        $"за год — сезон и лето (шкала 1–20): до 22 лет {young.Average():+0.00;-0.00} ({young.Count}), от 34 {old.Average():+0.00;-0.00} ({old.Count}), вся лига {all:+0.00;-0.00}; " +
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
            Development.Offseason(c);
            for (int w = 0; w < 36; w++) { c.Today = c.Today.AddDays(7); Development.Week(c); foreach (var kv in st) best[kv.Key] = Math.Max(best[kv.Key], kv.Value.Ca); }
            c.Today = c.Today.AddDays(365 - 36 * 7);
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

static (double b0, double b1, double b2) Wls(List<(double y, double w, double x1, double x2)> r)
{
    // взвешенный МНК для y ~ b0 + b1·x1 + b2·x2 (нормальные уравнения 3×3)
    double[,] A = new double[3, 4];
    foreach (var (y, w, x1, x2) in r)
    {
        double[] x = { 1, x1, x2 };
        for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) A[i, j] += w * x[i] * x[j]; A[i, 3] += w * x[i] * y; }
    }
    for (int i = 0; i < 3; i++)
    {
        int piv = i; for (int k = i + 1; k < 3; k++) if (Math.Abs(A[k, i]) > Math.Abs(A[piv, i])) piv = k;
        for (int j = 0; j < 4; j++) (A[i, j], A[piv, j]) = (A[piv, j], A[i, j]);
        for (int k = 0; k < 3; k++) if (k != i) { double f = A[k, i] / A[i, i]; for (int j = 0; j < 4; j++) A[k, j] -= f * A[i, j]; }
    }
    return (A[0, 3] / A[0, 0], A[1, 3] / A[1, 1], A[2, 3] / A[2, 2]);
}

static double Pearson(List<double> x, List<double> y)
{
    double mx = x.Average(), my = y.Average();
    double sxy = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
    double sx = Math.Sqrt(x.Sum(a => (a - mx) * (a - mx))), sy = Math.Sqrt(y.Sum(b => (b - my) * (b - my)));
    return sxy / (sx * sy);
}
