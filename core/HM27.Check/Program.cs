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

if (args.Length > 1 && args[1] == "injuries")
{
    // травмы за сезон: сколько, сколько одновременно, игроко-матчи
    var ic = Career.New(data, "MIN", 31);
    var simul = new List<int>(); var simulMin = new List<int>();
    while (ic.Phase == Phase.Regular)
    {
        foreach (var t in data.Teams) { int n = ic.Injured.Count(id => data.PlayerById[id].Team == t.Abbr); simul.Add(n); }
        simulMin.Add(ic.Injured.Count(id => data.PlayerById[id].Team == "MIN"));
        ic.SimDay();
    }
    var newInj = ic.Injuries.Where(i => i.From >= new DateOnly(2026, 10, 6)).ToList();
    double lost = 0;
    foreach (var g in ic.Games.Where(g => !g.Playoff))
        foreach (var t in new[] { g.Home, g.Away })
            lost += ic.Injuries.Count(i => data.PlayerById[i.Id].Team == t && i.From < g.Date && i.Until >= g.Date);
    Console.WriteLine($"Новых травм на клуб: {newInj.Count / 32.0:F1}; без пропуска матча (≤1 дня): {newInj.Count(i => i.Until.DayNumber - i.From.DayNumber <= 1) / 32.0:F1}");
    Console.WriteLine($"Игроко-матчей пропущено на клуб: {lost / 32:F0}");
    Console.WriteLine($"Одновременно травмированы (клуб-день): среднее {simul.Average():F2}; доля дней с 5+: {simul.Count(x => x >= 5) / (double)simul.Count:P1}; 6+: {simul.Count(x => x >= 6) / (double)simul.Count:P1}; максимум у MIN {simulMin.Max()}");
    Console.WriteLine($"На старте (реальные травмы из базы): {data.Teams.Average(t => t.Injured.Count):F1} на клуб");
    var len = newInj.Select(i => i.Until.DayNumber - i.From.DayNumber).ToList();
    Console.WriteLine($"Длительность: до 7 дней {len.Count(x => x <= 7) / (double)len.Count:P0}, 8–25 {len.Count(x => x is > 7 and <= 25) / (double)len.Count:P0}, 26–60 {len.Count(x => x is > 25 and <= 60) / (double)len.Count:P0}, 60+ {len.Count(x => x > 60) / (double)len.Count:P0}");
    return 0;
}
if (args.Length > 1 && args[1] == "news")
{
    // все письма карьеры за N дней: проверка текстов новостей глазами
    var nc = Career.New(data, args.Length > 2 ? args[2] : "MIN", 27);
    int days = args.Length > 3 ? int.Parse(args[3]) : 60;
    for (int i = 0; i < days && nc.Phase != Phase.Finished; i++) nc.SimDay();
    foreach (var m in nc.Messages)
    {
        Console.WriteLine($"=== #{m.Id} {m.Date} [{m.Tag}] {m.From} · вес {m.Weight}\n# {m.Subject}\n> {m.Lead}");
        if (m.Facts != null) Console.WriteLine("  " + string.Join(" | ", m.Facts.Select(f => $"{f.Label}: {f.Value} ({f.Sub})")));
        Console.WriteLine(m.Body);
        if (m.Quote != null) Console.WriteLine($"«{m.Quote}» — {m.QuoteBy}");
        Console.WriteLine();
    }
    var ts = News.TeamStats(nc);
    {
        var js = nc.ToJson();
        using var doc = System.Text.Json.JsonDocument.Parse(js);
        Console.WriteLine($"Сохранение: {js.Length / 1024} КБ");
        foreach (var pr in doc.RootElement.EnumerateObject().OrderByDescending(x => x.Value.GetRawText().Length).Take(8))
            Console.WriteLine($"  {pr.Name}: {pr.Value.GetRawText().Length / 1024} КБ");
        var g0 = doc.RootElement.GetProperty("Games").EnumerateArray().First(g => g.TryGetProperty("Result", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Object && r.GetProperty("Events").GetArrayLength() == 0);
        Console.WriteLine("  чужой матч: " + g0.GetRawText().Length + " байт: " + g0.GetRawText()[..Math.Min(900, g0.GetRawText().Length)]);
    }
    {
        var all = nc.Players.Values.Select(x => x.Morale).ToList();
        Console.WriteLine($"Настрой лиги: среднее {all.Average():F1}, SD {Math.Sqrt(all.Average(x => (x - all.Average()) * (x - all.Average()))):F1}, ниже 35: {all.Count(x => x < 35)}, выше 85: {all.Count(x => x > 85)}");
        foreach (var p in data.Roster(nc.UserTeam).OrderBy(p => nc.Players[p.Id].Morale))
            Console.Write($"{p.LastRu} {nc.Players[p.Id].Morale:F0}/{nc.Players[p.Id].Grief:F0}  ");
        Console.WriteLine();
        Console.WriteLine($"Решений: {nc.Messages.Count(m => m.Options != null)}, ответы: {string.Join(", ", nc.Messages.Where(m => m.Options != null).GroupBy(m => m.Action + ":" + m.Choice).Select(g => g.Key + "×" + g.Count()))}");
    }
    var ml = nc.Lineups[nc.UserTeam];
    string Nm(int id) => id == 0 ? "—" : data.PlayerById[id].LastRu + (nc.Injured.Contains(id) ? "*" : "");
    Console.WriteLine("Состав: " + string.Join(" | ", ml.F.Select(u => string.Join("-", u.Select(Nm)))) + " || " + string.Join(" | ", ml.D.Select(u => string.Join("-", u.Select(Nm)))) + $" || G {Nm(ml.G1)}/{Nm(ml.G2)}");
    Console.WriteLine($"PP1 {string.Join("-", ml.Pp1.Select(Nm))}  PP2 {string.Join("-", ml.Pp2.Select(Nm))}  PK1 {string.Join("-", ml.Pk1.Select(Nm))}; держим места: {string.Join(", ", ml.Hold.Select(kv => Nm(kv.Key) + " " + string.Join("/", kv.Value)))}");
    Console.WriteLine($"Дубли в составе: {ml.All.Count() - ml.All.Distinct().Count()}");
    Console.WriteLine("Сыгранность звеньев: " + string.Join(", ", ml.F.Select(u => $"{nc.UnitFam(u):0.00}")) + " | пары: " + string.Join(", ", ml.D.Select(u => $"{nc.UnitFam(u):0.00}")));
    Console.WriteLine($"Пар в сыгранности: {nc.Chem?.Count}; эффект нового звена {ShiftEngine.FamE(1 - Math.Exp(-Career.FAM_BASE / Career.FAM_M0)):+0.000;-0.000}, полного {ShiftEngine.FamE(1):+0.000}");
    {
        var sk = nc.Skaters.Where(kv => kv.Value.Toi > 30000).Select(kv => (p: data.PlayerById[kv.Key], s: kv.Value)).ToList();
        double gpTot = nc.Games.Count(g => g.Played);
        Console.WriteLine($"Потери за матч (обе команды): {nc.Skaters.Values.Sum(x => x.Gv) / gpTot:F1}, отборы {nc.Skaters.Values.Sum(x => x.Tk) / gpTot:F1}, силовые {nc.Skaters.Values.Sum(x => x.Hit) / gpTot:F1}");
        foreach (var x in sk.OrderByDescending(x => x.s.Gv * 3600.0 / x.s.Toi).Take(8))
            Console.WriteLine($"  {x.p.Name,-22} gv60 {x.s.Gv * 3600.0 / x.s.Toi:F2} (НХЛ {(x.p.X != null && x.p.X.ContainsKey("gv_60") ? x.p.X["gv_60"] : -1):F2}) toi {x.s.Toi / 60.0 / x.s.Gp:F1} pp {x.s.Ppp}");
        var both = sk.Where(x => x.p.X != null && x.p.X.ContainsKey("gv_60")).ToList();
        Console.WriteLine($"  среднее gv60 игра {both.Average(x => x.s.Gv * 3600.0 / x.s.Toi):F2}, НХЛ {both.Average(x => x.p.X["gv_60"]):F2}");
    }
    Console.WriteLine($"Сумма сейвов сверх ожидаемых по лиге: {nc.Goalies.Values.Sum(g => g.Gsax):0.0} при {nc.Goalies.Values.Sum(g => g.Ga)} пропущенных; xG полевых {nc.Skaters.Values.Sum(s => s.Ixg):0} при {nc.Skaters.Values.Sum(s => s.G)} голах");
    return 0;
}
if (args.Length > 1 && args[1] == "tactics") { TacticsCheck.Run(data, true, args.Length > 2 ? int.Parse(args[2]) : 12); return 0; }
if (args.Length > 1 && args[1] == "orders") { OrdersCheck.Run(data, true, args.Length > 2 ? int.Parse(args[2]) : 30); return 0; }
if (args.Length > 1 && args[1] == "botfix")
{
    foreach (var t in (args.Length > 3 ? args[3] : "MIN,SJS,COL,PHI").Split(','))
    {
        var r = SmartBotFixed.Run(data, t, args.Length > 2 ? int.Parse(args[2]) : 20);
        Console.WriteLine($"{t}: очков на 82 — автомат {r.Auto:F1}, звенья помощника {r.Lines:F1} ({r.Lines - r.Auto:+0.0}), план на матч {r.Plans:F1} ({r.Plans - r.Auto:+0.0}), всё {r.Smart:F1} ({r.Smart - r.Auto:+0.0})");
    }
    return 0;
}
if (args.Length > 1 && args[1] == "bot")
{
    int n = args.Length > 2 ? int.Parse(args[2]) : 6;
    var r = SmartBot.Run(data, n, true, args.Length > 3 ? args[3] : "SJS");
    Console.WriteLine($"Очков за сезон: автомат {r.Auto:F1}, умный {r.Smart:F1} ({r.Smart - r.Auto:+0.0}), только звенья {r.Lines:F1} ({r.Lines - r.Auto:+0.0}), только план на матч {r.Plans:F1} ({r.Plans - r.Auto:+0.0})");
    return 0;
}
if (args.Length > 1 && args[1] == "settings") { SettingsCheck(args.Length > 2 ? int.Parse(args[2]) : 25); return fails.Count == 0 ? 0 : 1; }
if (args.Length > 1 && args[1] == "steps") { StepsCheck(); return fails.Count == 0 ? 0 : 1; }
if (args.Length > 1 && args[1] == "scout")
{
    var sc0 = Career.New(data, "MIN", 27);
    for (int i = 0; i < 20; i++) sc0.SimDay();
    for (int k = 0; k < 3; k++)
    {
        sc0.SimToUserGame();
        var g = sc0.UserGameToday() ?? sc0.NextGame("MIN");
        var rp = Scout.For(sc0, g);
        Console.WriteLine($"=== {rp.Opp}: система " + string.Join(", ", Enumerable.Range(0, Tactics.Levers.Length).Select(i => Tactics.PoleName(Tactics.Levers[i], rp.OppSys[i]))));
        foreach (var t in rp.Threats) Console.WriteLine($"  опасен {sc0.Short(t.Id)}: {t.Why}");
        Console.WriteLine($"  вратарь {sc0.Short(rp.Goalie)}: {rp.GoalieText}; большинство {rp.PpFor:0.00} гола за матч ({rp.PpRank}-е), меньшинство {rp.PkAgainst:0.00} ({rp.PkRank}-е)");
        foreach (var a in rp.Advice) Console.WriteLine($"  совет: {Tactics.Levers[a.Lever].Name} — {Tactics.PoleName(Tactics.Levers[a.Lever], a.V)}: {a.Gain:+0.000} гола, выучка {a.Learn:P0} — {a.Why}");
        Console.WriteLine($"  опека {sc0.Short(rp.ShadowPick)} ≈{rp.ShadowGain:+0.000;-0.000}; давить на {sc0.Short(rp.PressPick)} ≈{rp.PressGain:+0.000;-0.000}");
        if (g == sc0.UserGameToday()) { sc0.Play(g); sc0.SimDay(); }
        var last = sc0.LastGame("MIN");
        if (last != null) foreach (var e in last.Result.Events.Where(e => e.Kind is "coach" or "goalie")) Console.WriteLine($"  [{e.T / 60}:{e.T % 60:00}] {e.Team} {e.Kind}: {e.Note} {(e.Kind == "goalie" ? sc0.Short(e.P1) : "")}");
    }
    return 0;
}
if (args.Length > 1 && args[1] == "roles") { RolesCheck.Run(data, true, args.Length > 2 ? int.Parse(args[2]) : 20); return 0; }
if (args.Length > 1 && args[1] == "coach") { CoachCheck.Run(data, true); return 0; }
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
        && jumps / Math.Max(1, seasons - 1) is >= 1 and <= 3.5 && favFirst / (double)seasons <= 0.5,   // 10 сезонов: при доле 1/3 шум ±15 п.п.
    $"лидер {lgLead.Average():F0} (НХЛ 114–135), последний {lgLast.Average():F0} (47–58), разброс {lgSd.Average():F1} (13,0–18,6); " +
    $"«выстрелов» из нижней половины в топ-8: {jumps / Math.Max(1, seasons - 1):F1} за сезон (НХЛ 1–3); сильнейший до сезона берёт 1-е место в {100.0 * favFirst / seasons:F0}% сезонов");
{
    var real = data.Players.Where(p => p.Real != null && p.Real["gp"] >= 60 && pGp.GetValueOrDefault(p.Id) >= 50.0 * seasons).ToList();
    var gameP = real.Select(p => pPts[p.Id] / pGp[p.Id] * 82).ToList(); var realP = real.Select(p => (p.Real["g"] + p.Real["a"]) * 82.0 / p.Real["gp"]).ToList();
    var gameG = real.Select(p => pG[p.Id] / pGp[p.Id] * 82).ToList(); var realG = real.Select(p => p.Real["g"] * 82.0 / p.Real["gp"]).ToList();
    double rP = Pearson(gameP, realP), rG = Pearson(gameG, realG);
    double realTop = data.Players.Where(p => p.Real != null && p.Real["gp"] >= 60).Select(p => (p.Real["g"] + p.Real["a"]) * 82.0 / p.Real["gp"]).OrderByDescending(x => x).Take(10).Average();
    double gameTop = top10.Average();
    // среди 10 лучших снайперов сезона — почти нет игроков с реальным временем меньше 14 минут за матч
    // (не больше одного случая на 10 сезонов среди игроков без «родословной»)
    // «прорыв с родословной» — не нарушение: был сезон на 25+ голов (Кузьменко, Донато) или свежий топ-10 драфта (Мартон)
    bool Pedigree(PlayerData p) => (p.Bio?.Nhl.Any(r => r[3].GetInt32() >= 25) ?? false) || (p.Bio?.Draft is { Count: >= 4 } dr && dr[3].GetInt32() <= 10 && dr[0].GetInt32() >= 2023);
    var fringeIds = topSnipers.Where(id => data.PlayerById[id].Toi < 14 && !Pedigree(data.PlayerById[id])).ToList();
    var fringe = fringeIds.Select(id => data.PlayerById[id].Name).Distinct().ToList();
    Check("Сверка с реальностью: игроки", rP >= 0.8 && rG >= 0.8 && Math.Abs(gameTop / realTop - 1) <= 0.10 && fringeIds.Count <= Math.Max(1, seasons / 10),
        $"корреляция с сезоном 2025-26 ({real.Count} игроков): очки r = {rP:F2}, голы r = {rG:F2}; средние на 82: очки {gameP.Average():F1}/{realP.Average():F1}; " +
        $"топ-10 бомбардиров {gameTop:F0}/{realTop:F0} очков на 82 ({(gameTop / realTop - 1) * 100:+0;-0}%); игроки с малым временем в топ-10 снайперов: {(fringe.Count == 0 ? "нет" : $"{string.Join(", ", fringe.Take(4))} ({fringeIds.Count} из {topSnipers.Count})")}");
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

// ---------------------------------------------------------------- 4а. Сыгранность звеньев: эффект в заданных пределах
{
    var cf = Career.New(data, "MIN", 77);
    var opps = data.Teams.Where(t => t.Abbr != "MIN").Select(t => t.Abbr).ToList();
    double d0 = 0, d1 = 0; int ng = 3000;
    for (int i = 0; i < ng; i++)
    {
        string op = opps[i % opps.Count];
        foreach (var (fam, k) in new[] { (0.2, 0), (1.0, 1) })
        {
            var h = cf.Side("MIN", cf.Today); h.Fam = (x, y) => fam;
            var a = cf.Side(op, cf.Today); a.Fam = (x, y) => ShiftEngine.FAM_REF;
            var r = ShiftEngine.Play(data, h, a, new Random(5000 + i), false);
            if (k == 0) d0 += r.HomeGoals - r.AwayGoals; else d1 += r.HomeGoals - r.AwayGoals;
        }
    }
    double eff = (d0 - d1) / ng;
    double expect = 3.15 * 2 * (ShiftEngine.FamE(0.2) - ShiftEngine.FamE(1.0));
    Check("Сыгранность", eff is <= -0.04 and >= -0.4, $"все звенья новые (0,2) против сыгранных весь сезон (1,0): разница голов за матч {eff:+0.000;-0.000} (верхняя граница по формуле ≈ {expect:+0.00;-0.00}), {ng} матчей");
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
    {
        var mor = c.Players.Values.Select(x => (double)x.Morale).ToList();
        double mm = mor.Average(), sd = Math.Sqrt(mor.Average(x => (x - mm) * (x - mm)));
        int dec = c.Messages.Count(m => m.Options != null), open = c.Messages.Count(m => m.Pending && m.Due < c.Today);
        Check("Настрой и решения", mm is >= 52 and <= 72 && sd is >= 4 and <= 20 && open == 0 && dec >= 3,
            $"настрой лиги после плей-офф (без матчей тянется к норме): среднее {mm:F1}, разброс {sd:F1}; решений у клуба игрока за сезон {dec}, просроченных без ответа {open}");
        var tr = c.Roster(c.UserTeam).Where(p => c.Players.ContainsKey(p.Id)).Select(p => (double)Trust.Of(c, p.Id)).ToList();
        double tm = tr.Average(), tsd = Math.Sqrt(tr.Average(x => (x - tm) * (x - tm)));
        int press = c.Messages.Count(m => m.Action?.StartsWith("press:") == true);
        Check("Доверие и пресса", tm is >= 40 and <= 75 && tsd >= 1 && press is >= 3 and <= 25,
            $"доверие к тренеру в клубе игрока к концу сезона: среднее {tm:F1}, разброс {tsd:F1}; пресс-конференций за сезон {press} (ответы по умолчанию)");
    }
    int userGames = c.Games.Count(g => g.Home == "MIN" || g.Away == "MIN");
    var mine = c.Games.First(g => g.Home == "MIN" || g.Away == "MIN");
    Check("Хроника матча", mine.Result.Events.Count > 60 && mine.Result.Events.Count(e => e.Kind == "goal") == mine.Result.Goals.Count(e => e.Kind != "so")
        && c.Games.Where(g => g.Home != "MIN" && g.Away != "MIN").All(g => g.Result.Events.Count == 0),
        $"событий в своём матче: {mine.Result.Events.Count} (голы совпадают со счётом), чужие матчи без хроники");

    SettingsCheck(40);
    {
        StepsCheck();
        var oc = OrdersCheck.Run(data, false, 40);
        Check("Указания игрокам", oc.dGood - oc.dBad >= 0.04 && oc.sniper > -0.02,
            $"«подключаться в атаку» подходящему защитнику {oc.dGood:+0.000;-0.000} гола за матч, неподходящему {oc.dBad:+0.000;-0.000}; «бросать чаще» лучшему бросающему {oc.sniper:+0.000;-0.000}, худшему {oc.poor:+0.000;-0.000}");
        {
            // тренировки: тяжёлая нагрузка с упором на атаку и индивидуальной работой молодых — быстрее растут, но устают
            Career Run(bool train)
            {
                var ct = Career.New(data, "MIN", 1313);
                if (train)
                {
                    var plan = Training.Of(ct); plan.Load = 1; plan.Focus = "attack";
                    foreach (var p in ct.Roster("MIN").Where(p => !p.IsGoalie).OrderBy(p => p.Age(ct.Today) ?? 30).Take(Training.SLOTS)) plan.Individual[p.Id] = "fin";
                }
                for (int i = 0; i < 120; i++) ct.SimDay();
                return ct;
            }
            var a0 = Run(false); var a1 = Run(true);
            var young = a1.Training.Individual.Keys.ToList();
            double Fin(Career cc) => young.Average(id => cc.Players[id].A.GetValueOrDefault("fin") - cc.Players[id].A0.GetValueOrDefault("fin", cc.Players[id].A.GetValueOrDefault("fin")));
            double Fresh(Career cc) => cc.Roster("MIN").Where(p => !p.IsGoalie).Average(p => cc.Players[p.Id].Fresh);
            double f0 = Fin(a0), f1 = Fin(a1), fr0 = Fresh(a0), fr1 = Fresh(a1);
            Check("Тренировки", f1 > f0 && fr1 < fr0,
                $"120 дней, тяжёлая нагрузка + атака + индивидуально «завершение» у {young.Count} молодых: завершение {f0:+0.00;-0.00} → {f1:+0.00;-0.00}, свежесть полевых {fr0:0} → {fr1:0}");
        }
        {
            // умный бот: хорошая политика решений против автомата на тех же матчах — заметный, но не огромный плюс, не из одного действия
            var res = new[] { "MIN", "PHI" }.Select(t => (t, r: SmartBotFixed.Run(data, t, 15))).ToList();
            bool ok = res.All(x => x.r.Smart - x.r.Auto is >= 3 and <= 16 && Math.Max(x.r.Lines - x.r.Auto, x.r.Plans - x.r.Auto) <= 0.85 * (x.r.Smart - x.r.Auto) + 1);
            Check("Умный бот", ok, string.Join("; ", res.Select(x => $"{x.t}: автомат {x.r.Auto:F0}, звенья помощника {x.r.Lines - x.r.Auto:+0.0}, план на матч {x.r.Plans - x.r.Auto:+0.0}, всё {x.r.Smart - x.r.Auto:+0.0} очка на 82")) +
                " (те же матчи, без сыгранности и настроя; цель — 6–12, не больше 16)");
        }
        var rc = RolesCheck.Run(data, false, 0);
        Check("Роли звеньев", rc.atkOz >= 0.5 && rc.defDz >= 0.5 && rc.shutShare >= 0.4,
            $"атакующие тройки начинают в зоне атаки {100 * rc.atkOz:0}% вбрасываний, оборонительные в своей — {100 * rc.defDz:0}%; оборонительная тройка против лучшей тройки соперника {100 * rc.shutShare:0}% времени");
        // нет одного верного решения: лучший план помощника зависит от соперника, вариант состава не бесплатный
        var cc = CoachCheck.Run(data, false);
        var tc = TacticsCheck.Run(data, false, 0);
        Check("Нет одного верного решения", cc.PullP90 - cc.PullP10 >= 60 && cc.Closed >= 0.1 && cc.Open >= 0.1 && cc.Normal >= 0.1 && cc.WithFamLoss >= cc.Clubs / 2
            && tc.distinct >= 12 && tc.bothPoles == Tactics.Levers.Length,
            $"снятие при −1 по расчёту: от {Coach.Clock(cc.PullP10)} до {Coach.Clock(cc.PullP90)} (10–90%); темп по сопернику: закрытый {100 * cc.Closed:0}%, обычный {100 * cc.Normal:0}%, открытый {100 * cc.Open:0}%; " +
            $"вариант состава теряет в сыгранности у {cc.WithFamLoss} из {cc.Clubs} клубов, обижает кого-то у {cc.WithUpset}, хуже текущего с учётом сыгранности у {cc.NetWorse}; " +
            $"систем игры под состав у 32 клубов: {tc.distinct} разных, рычагов, где оба полюса выбраны хотя бы тремя клубами: {tc.bothPoles} из {Tactics.Levers.Length}");
    }
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

void StepsCheck()
    {
        // пауза и перерывы без решений не меняют матч: трансляция по шагам = матч целиком
        var cp = Career.New(data, "MIN", 808);
        var gg = cp.Games.Where(g => !g.Playoff).Take(40).ToList();
        int same = 0, swaps = 0;
        foreach (var g in gg)
        {
            var (h1, a1) = cp.Sides(g); var whole = ShiftEngine.Play(data, h1, a1, new Random(g.Id));
            var (h2, a2) = cp.Sides(g); var sim = ShiftEngine.Start(data, h2, a2, new Random(g.Id));
            foreach (int t in new[] { 333, 1200, 1777, 2400, 3000 }) { sim.RunTo(t); sim.Apply(); }
            sim.Run();
            var p = sim.Result;
            if (p.HomeGoals == whole.HomeGoals && p.AwayGoals == whole.AwayGoals && p.HomeShots == whole.HomeShots && p.Events.Count == whole.Events.Count) same++;
        }
        foreach (var g in cp.Games.Where(g => !g.Playoff).Take(400))
    {
        cp.Play(g);
        if (g.Result.HomeGoalie2 != 0 || g.Result.AwayGoalie2 != 0)
        {
            swaps++;
        }
    }
        // трансляция: перерывы, советы помощника (применяем все), доигрывание — матч записан, ошибок нет
    int liveOk = 0, advices = 0, applied = 0;
    {
        var cl = Career.New(data, "MIN", 909);
        for (int k = 0; k < 6; k++)
        {
            cl.SimToUserGame();
            var g = cl.UserGameToday();
            if (g == null) break;
            var lg = cl.BeginLive(g);
            for (int t = 60; !lg.Sim.Finished && t < 4200; t += 60)
            {
                lg.Sim.RunTo(t);
                if (lg.AtBreak || t % 600 == 0)
                {
                    var items = LiveAdvice.For(cl, lg);
                    advices += items.Count;
                    foreach (var it in items) { it.Apply(); applied++; }
                    lg.AtBreak = false;
                }
            }
            cl.FinishLive(lg);
            if (g.Played && g.Result.HomeGoals + g.Result.AwayGoals >= 0) liveOk++;
            cl.SimDay();
        }
    }
    Check("Трансляция", liveOk == 6 && advices > 0, $"матчей в трансляции {liveOk} из 6, советов помощника {advices}, применено {applied}");
    Check("Матч по шагам", same == gg.Count && swaps is > 20 and < 85,
            $"пауза и Apply без решений: совпало {same} из {gg.Count} матчей; замен вратаря по ходу матча — {swaps} на 400 матчей (в НХЛ сменщик выходит примерно в каждом шестом–седьмом матче)");
    }

void SettingsCheck(int REPS = 25)
{
    // одни и те же матчи «Сан-Хосе» с теми же случайными числами: до первого отличия настройки матч идёт одинаково,
    // поэтому разница — эффект самой настройки, а не шум расходящихся сезонов
    double ptsBase = 0, ptsPull = 0, ptsCalc = 0, gBase = 0, gClosed = 0, ties = 0, tiesClosed = 0, n = 0;
    {
        var cs = Career.New(data, "SJS", 9000);
        var l = cs.Lineups["SJS"];
        l.TempoAuto = false; l.Rules = new();
        var sg = cs.Games.Where(g => !g.Playoff && (g.Home == "SJS" || g.Away == "SJS")).ToList();
        double Pts(MatchResult r, bool home) { bool w = (r.HomeGoals > r.AwayGoals) == home; return w ? 2 : r.End != "REG" ? 1 : 0; }
        foreach (var g in sg)
        {
            bool home = g.Home == "SJS";
            for (int k = 0; k < REPS; k++)
            {
                MatchResult Run(int pull, int tempo)
                {
                    l.Pull = pull; l.Tempo = tempo;
                    var (h, a) = cs.Sides(g);
                    return ShiftEngine.Play(data, h, a, new Random(k * 7919 + g.Id));
                }
                var rb = Run(Coach.USUAL, 0); var re = Run(Coach.EARLY, 0); var rc = Run(Coach.ASSIST, 0); var rd = Run(Coach.USUAL, -1);
                ptsBase += Pts(rb, home); ptsPull += Pts(re, home); ptsCalc += Pts(rc, home);
                gBase += rb.HomeGoals + rb.AwayGoals; gClosed += rd.HomeGoals + rd.AwayGoals;
                if (rb.End != "REG") ties++; if (rd.End != "REG") tiesClosed++;
                n++;
            }
        }
    }
    double per82 = 82.0 / n;
    // по частотам НХЛ раннее снятие выгоднее позднего (Asness & Brown): около +0,5 очка за сезон (100 повторов: 74,5 → 75,0 / 74,9);
    // при 40 повторах шум разницы ~±0,3 — проверяем, что не хуже сверх шума
    Check("Раннее снятие вратаря", (ptsPull - ptsBase) * per82 >= -0.6 && (ptsCalc - ptsBase) * per82 >= -0.6,
        $"«Сан-Хосе», те же матчи ×{REPS}: очков на 82 матча — как в НХЛ {ptsBase * per82:F1}, рано {ptsPull * per82:F1}, по расчёту {ptsCalc * per82:F1}");
    Check("Закрытый темп", gClosed < gBase && tiesClosed >= ties, $"голов в матчах «Сан-Хосе» {gBase / n:F2} → {gClosed / n:F2}, овертаймов {100 * ties / n:F1}% → {100 * tiesClosed / n:F1}%");
}
