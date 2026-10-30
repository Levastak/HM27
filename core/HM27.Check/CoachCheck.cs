using HM27.Core;

/// <summary>
/// «Нет одного верного решения» (ТЗ «Тренер»): лучший выбор помощника зависит от соперника, а вариант состава
/// не всегда лучше текущего, если считать сыгранность и обиды.
/// </summary>
static class CoachCheck
{
    public sealed record Result(double PullP10, double PullP90, double EarlyBetter, double UsualWorse, double Closed, double Open, double Normal,
        int Clubs, int NetWorse, int WithUpset, int WithFamLoss);

    public static Result Run(GameData data, bool verbose)
    {
        var c = Career.New(data, "MIN", 4242);
        var t1 = new List<double>(); int earlyBetter = 0, n = 0;
        int closed = 0, open = 0, normal = 0;
        foreach (var g in c.Games.Where(g => !g.Playoff).Take(400))
        {
            var h = c.Side(g.Home, g.Date); var a = c.Side(g.Away, g.Date);
            foreach (bool home in new[] { true, false })
            {
                var p = Coach.Pull(c, g, h, a, home);
                t1.Add(p.T1); n++;
                // «рано» (4:00) против расчёта: ближе ли к лучшему, чем «как в НХЛ» (2:00)
                if (Math.Abs(p.T1 - Coach.EARLY1) < Math.Abs(p.T1 - ShiftEngine.PULL1)) earlyBetter++;
                var (tp, _) = Coach.Tempo(c, g, h, a, home);
                if (tp < 0) closed++; else if (tp > 0) open++; else normal++;
            }
        }
        t1.Sort();
        double P(double q) => t1[(int)(q * (t1.Count - 1))];

        int netWorse = 0, upset = 0, famLoss = 0;
        foreach (var t in data.Teams)
        {
            var rv = Coach.Propose(c, t.Abbr);
            double net = rv.Gf - rv.Ga + rv.FamGf - rv.FamGa;
            if (net < 0) netWorse++;
            if (rv.Upset.Count > 0) upset++;
            if (rv.FamGf < -0.005) famLoss++;
            if (verbose)
                Console.WriteLine($"{t.Abbr}: способности {rv.Gf:+0.000;-0.000} / {rv.Ga:+0.000;-0.000}, сыгранность {rv.FamGf:+0.000;-0.000} / {rv.FamGa:+0.000;-0.000}, итог {net:+0.000;-0.000}; " +
                    $"изменений {rv.Changes.Count}, перестановок {rv.Moved}, обиды {rv.Upset.Count}, молодые {rv.Youth.Count}");
        }
        if (verbose)
        {
            double lsum = 0; int ln = 0;
            foreach (var g in c.Games.Where(g => !g.Playoff).Take(400)) { var o = MatchEngine.Odds(data, c.Side(g.Home, g.Date), c.Side(g.Away, g.Date), false, sharp: c.Sharp); lsum += o.LamHome + o.LamAway; ln += 2; }
            Console.WriteLine($"Средняя λ по матчам: {lsum / ln:F3}; база {data.League.GoalsPerGame:F3}");
            Console.WriteLine($"Снятие при −1: p10 {Coach.Clock(P(0.1))}, медиана {Coach.Clock(P(0.5))}, p90 {Coach.Clock(P(0.9))}; «рано» ближе к расчёту в {100.0 * earlyBetter / n:0}% матчей");
            Console.WriteLine($"Темп: закрытый {100.0 * closed / n:0}%, обычный {100.0 * normal / n:0}%, открытый {100.0 * open / n:0}%");
            var g0 = c.Games.First(g => g.Home == "MIN" || g.Away == "MIN");
            var (hh, aa) = (c.Side(g0.Home, g0.Date), c.Side(g0.Away, g0.Date));
            bool mh = g0.Home == "MIN";
            var pp = Coach.Pull(c, g0, hh, aa, mh);
            var oo = MatchEngine.Odds(data, hh, aa, false, sharp: c.Sharp);
            Console.WriteLine($"{g0.Home}-{g0.Away}: λ {oo.LamHome:F2}/{oo.LamAway:F2}, среднее {data.League.GoalsPerGame * MatchEngine.REG_SHARE:F2}, P {oo.PHome:F2}; сила COL {c.PowerRank("COL")}, MIN {c.PowerRank("MIN")}");
            Console.WriteLine(Coach.PullText(pp, "«" + data.TeamByAbbr[mh ? g0.Away : g0.Home].Name + "»"));
            var rv = Coach.Propose(c, "MIN");
            foreach (var s in rv.Changes) Console.WriteLine("  " + s);
            foreach (var (id, s) in rv.Upset) Console.WriteLine($"  обида: {c.Short(id)} {s}");
            foreach (var (id, s) in rv.Youth) Console.WriteLine($"  молодой: {c.Short(id)} {s}");
        }
        return new Result(P(0.1), P(0.9), (double)earlyBetter / n, 0, (double)closed / n, (double)open / n, (double)normal / n,
            data.Teams.Count, netWorse, upset, famLoss);
    }
}

/// <summary>Системы игры: что выбрал каждый клуб, пригодность и проверка движком (общие случайные числа).</summary>
static class TacticsCheck
{
    public static (double bothPoles, double fcPlus, double modelErr, int distinct) Run(GameData data, bool verbose, int reps = 12)
    {
        var c = Career.New(data, "MIN", 515);
        var picks = new Dictionary<string, int>();
        foreach (var t in data.Teams)
        {
            var l = c.Lineups[t.Abbr];
            string sig = string.Join("", l.Sys.Select(v => v < 0 ? "−" : v > 0 ? "+" : "0"));
            picks[sig] = picks.GetValueOrDefault(sig) + 1;
            if (verbose)
                Console.WriteLine($"{t.Abbr}: {sig}  " + string.Join("  ", Enumerable.Range(0, Tactics.Levers.Length).Select(i =>
                    $"{Tactics.Levers[i].Key} {Tactics.Fit(c, l, Tactics.Levers[i], -1):+0.0;-0.0}/{Tactics.Fit(c, l, Tactics.Levers[i], 1):+0.0;-0.0}")));
        }
        if (verbose) Console.WriteLine("Системы клубов: " + string.Join(", ", picks.OrderByDescending(x => x.Value).Select(x => $"{x.Key} ×{x.Value}")));
        int[] cnt(int lever, int v) => new[] { data.Teams.Count(t => c.Lineups[t.Abbr].SysOf(lever) == v) };
        double fcM = cnt(0, -1)[0], fcP = cnt(0, 1)[0];

        // движок против модели: клуб играет свои матчи с разными системами (те же случайные числа)
        double err = 0; int nErr = 0;
        foreach (var team in reps > 0 ? new[] { "MIN", "SJS", "COL" } : Array.Empty<string>())
        {
            var games = c.Games.Where(g => !g.Playoff && (g.Home == team || g.Away == team)).Take(41).ToList();
            var keep = (int[])c.Lineups[team].Sys.Clone(); var keepL = new Dictionary<string, float>(c.Lineups[team].Learn);
            (double gf, double ga, double model) Play(int[] sys)
            {
                c.Lineups[team].Sys = sys; c.Lineups[team].Learn = new();
                for (int i = 0; i < sys.Length; i++) if (sys[i] != 0) c.Lineups[team].Learn[Tactics.Levers[i].Key + (sys[i] < 0 ? "-" : "+")] = 1;
                double gf = 0, ga = 0, model = 0;
                foreach (var g in games)
                {
                    var (h, a) = c.Sides(g);
                    bool home = g.Home == team;
                    var o = MatchEngine.Odds(data, h, a, false);
                    model += home ? o.LamHome - o.LamAway : o.LamAway - o.LamHome;
                    for (int k = 0; k < reps; k++)
                    {
                        var r = ShiftEngine.Play(data, h, a, new Random(k * 7919 + g.Id), false);
                        gf += home ? r.HomeGoals : r.AwayGoals; ga += home ? r.AwayGoals : r.HomeGoals;
                    }
                }
                int n = games.Count * reps;
                return (gf / n, ga / n, model / games.Count);
            }
            var b = Play(new int[Tactics.Levers.Length]);
            for (int lv = 0; lv < Tactics.Levers.Length; lv++)
                foreach (int v in new[] { -1, 1 })
                {
                    var sys = new int[Tactics.Levers.Length]; sys[lv] = v;
                    var x = Play(sys);
                    double dEng = (x.gf - x.ga) - (b.gf - b.ga), dMod = x.model - b.model;
                    err += Math.Abs(dEng - dMod); nErr++;
                    if (verbose) Console.WriteLine($"  {team} {Tactics.PoleName(Tactics.Levers[lv], v),-18} пригодность {Tactics.Fit(c, c.Lineups[team], Tactics.Levers[lv], v),5:+0.0;-0.0}: " +
                        $"движок {x.gf - b.gf:+0.00;-0.00} / {x.ga - b.ga:+0.00;-0.00} (разница {dEng:+0.00;-0.00}), модель {dMod:+0.00;-0.00}");
                }
            c.Lineups[team].Sys = keep; c.Lineups[team].Learn = keepL;
        }
        int bothPoles = Enumerable.Range(0, Tactics.Levers.Length).Count(i => cnt(i, -1)[0] >= 3 && cnt(i, 1)[0] >= 3);
        return (bothPoles, fcP, nErr > 0 ? err / nErr : 0, picks.Count);
    }
}

/// <summary>Роли звеньев: как расставляют по зонам и против лучших; что дают роли против «все обычные» (общие сиды).</summary>
static class RolesCheck
{
    public static (double atkOz, double defDz, double shutShare, double gain) Run(GameData data, bool verbose, int reps = 20)
    {
        ShiftEngine.Diag = new ShiftEngine.Diagnostics();
        var c = Career.New(data, "MIN", 616);
        var games = c.Games.Where(g => !g.Playoff).Take(300).ToList();
        foreach (var g in games) { var (h, a) = c.Sides(g); ShiftEngine.Play(data, h, a, new Random(g.Id), false); }
        var d = ShiftEngine.Diag; ShiftEngine.Diag = null;
        double Share(int role, int z) { double s = 0; for (int k = 0; k < 3; k++) s += d.RoleStarts[role, k]; return s == 0 ? 0 : d.RoleStarts[role, z] / s; }
        double atkOz = Share(1, 0), defDz = Share(2, 2), shut = d.TopSec == 0 ? 0 : d.ShutVsTop / (double)d.TopSec;
        if (verbose)
        {
            for (int r = 0; r < 3; r++) Console.WriteLine($"{Lineup.RoleNames[r],-15}: в зоне атаки {100 * Share(r, 0):0}%, в средней {100 * Share(r, 1):0}%, в своей {100 * Share(r, 2):0}%");
            Console.WriteLine($"Оборонительная тройка против лучшей тройки соперника: {100 * shut:0}% её времени (без расстановки ~25%)");
        }
        // эффект ролей: клуб с ролями и расстановкой против того же клуба без них (все соперники — с ролями)
        double gain = 0;
        if (reps > 0)
        {
            foreach (var team in new[] { "MIN", "COL", "SJS" })
            {
                var tg = c.Games.Where(g => !g.Playoff && (g.Home == team || g.Away == team)).Take(41).ToList();
                var l = c.Lineups[team];
                var (fr, dr, mu) = (l.FRole, l.DRole, l.Matchup);
                double Diff(bool roles)
                {
                    l.FRole = roles ? fr : null; l.DRole = roles ? dr : null; l.Matchup = roles && mu;
                    double s = 0; int n = 0;
                    foreach (var g in tg)
                    {
                        var (h, a) = c.Sides(g); bool home = g.Home == team;
                        for (int k = 0; k < reps; k++)
                        {
                            var r = ShiftEngine.Play(data, h, a, new Random(k * 7919 + g.Id), false);
                            s += home ? r.HomeGoals - r.AwayGoals : r.AwayGoals - r.HomeGoals; n++;
                        }
                    }
                    return s / n;
                }
                double with = Diff(true), without = Diff(false);
                (l.FRole, l.DRole, l.Matchup) = (fr, dr, mu);
                gain += with - without;
                if (verbose) Console.WriteLine($"{team}: разница голов за матч с ролями {with:+0.000;-0.000}, без {without:+0.000;-0.000} → {with - without:+0.000;-0.000}");
            }
            gain /= 3;
        }
        return (atkOz, defDz, shut, gain);
    }
}
