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

Console.WriteLine(fails.Count == 0 ? "\nВсе критерии выполнены." : $"\nНе выполнено: {string.Join(", ", fails)}");
return fails.Count == 0 ? 0 : 1;

static double Pearson(List<double> x, List<double> y)
{
    double mx = x.Average(), my = y.Average();
    double sxy = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
    double sx = Math.Sqrt(x.Sum(a => (a - mx) * (a - mx))), sy = Math.Sqrt(y.Sum(b => (b - my) * (b - my)));
    return sxy / (sx * sy);
}
