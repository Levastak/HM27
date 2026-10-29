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
