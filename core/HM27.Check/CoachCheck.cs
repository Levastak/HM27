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
                var tp = Coach.Tempo(c, g, h, a, home).tempo;
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

/// <summary>Указания игрокам: «бросать чаще» снайперу и мазиле, «подключаться» подходящему и неподходящему защитнику (те же матчи).</summary>
static class OrdersCheck
{
    public static (double sniper, double poor, double dGood, double dBad) Run(GameData data, bool verbose, int reps = 30)
    {
        var c = Career.New(data, "MIN", 717);
        var l = c.Lineups["MIN"];
        l.Rules = new(); l.TempoAuto = false;
        var games = c.Games.Where(g => !g.Playoff && (g.Home == "MIN" || g.Away == "MIN")).Take(41).ToList();
        // зонд движка: ожидаемые голы пятёрки с игроком при одинаковых условиях (5×5, равный счёт, все свежие) — без шума исходов;
        // по каждому матчу (соперник, его состав) и в среднем, в голах за 60 минут пятёрки
        double Net(int id, string order)
        {
            l.Orders = id == 0 || order == null ? null : new() { [id] = order };
            double s = 0; int n = 0;
            foreach (var g in games)
            {
                bool home = g.Home == "MIN";
                var (h, a) = c.Sides(g);
                var sim = ShiftEngine.Start(data, h, a, new Random(g.Id));
                var (gf, ga) = sim.Probe(home, id);
                s += 3600 * (gf - ga); n++;
            }
            l.Orders = null;
            return s / n;
        }
        double Diff(int id, string order) => Net(id, order) - Net(id, null);
        var fw = l.F.Take(3).SelectMany(x => x).Where(id => id != 0).Select(id => c.Data.PlayerById[id]).ToList();
        var ds = l.D.Take(3).SelectMany(x => x).Where(id => id != 0).Select(id => c.Data.PlayerById[id]).ToList();
        var shoot = Orders.Get("shoot"); var join = Orders.Get("join");
        var best = fw.OrderByDescending(p => Orders.Fit(c, p, shoot)).First(); var worst = fw.OrderBy(p => Orders.Fit(c, p, shoot)).First();
        var dG = ds.OrderByDescending(p => Orders.Fit(c, p, join)).First(); var dB = ds.OrderBy(p => Orders.Fit(c, p, join)).First();
        double r1 = Diff(best.Id, "shoot"), r2 = Diff(worst.Id, "shoot"), r3 = Diff(dG.Id, "join"), r4 = Diff(dB.Id, "join");
        if (verbose)
        {
            Console.WriteLine($"Бросать чаще: {c.Short(best.Id)} (пригодность {Orders.Fit(c, best, shoot):+0.00;-0.00}) {r1:+0.000;-0.000} гола за 60 минут пятёрки, {c.Short(worst.Id)} ({Orders.Fit(c, worst, shoot):+0.00;-0.00}) {r2:+0.000;-0.000}");
            Console.WriteLine($"Подключаться: {c.Short(dG.Id)} ({Orders.Fit(c, dG, join):+0.00;-0.00}) {r3:+0.000;-0.000}, {c.Short(dB.Id)} ({Orders.Fit(c, dB, join):+0.00;-0.00}) {r4:+0.000;-0.000}");
        }
        return (r1, r2, r3, r4);
    }
}

/// <summary>
/// «Умный бот»: клуб игрока с хорошей политикой решений против того же клуба на автомате (звенья как на старте,
/// без плана на матч). Политика: раз в неделю — вариант помощника, перед матчем — советы скаута (выгода от 0,02) и опека,
/// темп и снятие вратаря по расчёту. По частям: только звенья, только план на матч.
/// </summary>
static class SmartBot
{
    public sealed record Result(double Auto, double Smart, double Lines, double Plans, int Seasons);

    static double Season(GameData data, string team, int seed, bool lines, bool plans)
    {
        var c = Career.New(data, team, seed);
        var l = c.Lineups[team];
        l.TempoAuto = plans; l.Pull = plans ? Coach.ASSIST : Coach.USUAL;
        if (!plans) l.Rules = new();
        int guard = 0;
        while (c.Phase == Phase.Regular && guard++ < 400)
        {
            if (lines && c.Today.DayOfWeek == DayOfWeek.Monday)
            {
                var rv = Coach.Propose(c, team);
                var nl = rv.Lineup; nl.CopySettings(c.Lineups[team]); nl.Hold = c.Lineups[team].Hold;
                c.Lineups[team] = nl;
            }
            var g = c.UserGameToday();
            if (plans && g != null)
            {
                var rep = Scout.For(c, g);
                var club = c.Lineups[team].Sys ?? new int[Tactics.Levers.Length];
                var plan = new MatchPlan { Game = g.Id, Sys = (int[])club.Clone() };
                foreach (var a in rep.Advice.Where(a => a.Conf == "уверен")) plan.Sys[a.Lever] = a.V;   // умный тренер берёт только уверенные советы
                if (rep.ShadowGain >= 0.02) plan.Shadow = rep.ShadowPick;
                if (rep.PressGain >= 0.02) plan.PressD = rep.PressPick;
                c.Plan = plan;
            }
            c.SimDay();
        }
        return c.Table[team].Pts;
    }

    public static Result Run(GameData data, int seeds, bool verbose, string team = "SJS")
    {
        double a = 0, s = 0, li = 0, pl = 0;
        for (int k = 0; k < seeds; k++)
        {
            int seed = 7100 + k;
            double x0 = Season(data, team, seed, false, false), x1 = Season(data, team, seed, true, true);
            double x2 = Season(data, team, seed, true, false), x3 = Season(data, team, seed, false, true);
            a += x0; s += x1; li += x2; pl += x3;
            if (verbose) Console.WriteLine($"  сезон {k + 1}: автомат {x0}, умный {x1}, только звенья {x2}, только план {x3}");
        }
        return new Result(a / seeds, s / seeds, li / seeds, pl / seeds, seeds);
    }
}

/// <summary>То же, что SmartBot, но на одних и тех же матчах с одними и теми же случайными числами: шум меньше, без сезонной динамики.</summary>
static class SmartBotFixed
{
    public sealed record Result(double Auto, double Lines, double Plans, double Smart);

    /// <summary>Части плана для разбора (botfix … parts): темп и снятие, советы по системе, задания.</summary>
    public static bool UseTempo = true, UseSys = true, UseTargets = true;

    public static Result Run(GameData data, string team, int reps, int seed = 4040)
    {
        var c = Career.New(data, team, seed);
        var games = c.Games.Where(g => !g.Playoff && (g.Home == team || g.Away == team)).Take(41).ToList();
        var auto = c.Lineups[team].Clone();
        var smartL = Coach.Propose(c, team).Lineup; smartL.CopySettings(auto);
        double Pts(MatchResult r, bool home) { bool w = (r.HomeGoals > r.AwayGoals) == home; return w ? 2 : r.End != "REG" ? 1 : 0; }
        double Run(bool lines, bool plans)
        {
            c.Lineups[team] = (lines ? smartL : auto).Clone();
            c.Lineups[team].TempoAuto = plans && UseTempo; c.Lineups[team].Pull = plans && UseTempo ? Coach.ASSIST : Coach.USUAL;
            double s = 0; int n = 0;
            foreach (var g in games)
            {
                c.Plan = null;
                if (plans)
                {
                    var rep = Scout.For(c, g);
                    var plan = new MatchPlan { Game = g.Id, Sys = (int[])(c.Lineups[team].Sys ?? new int[Tactics.Levers.Length]).Clone() };
                    if (UseSys) foreach (var a in rep.Advice.Where(a => a.Conf == "уверен")) plan.Sys[a.Lever] = a.V;   // умный тренер берёт только уверенные советы
                    if (UseTargets && rep.ShadowGain >= 0.02) plan.Shadow = rep.ShadowPick;
                    if (UseTargets && rep.PressGain >= 0.02) plan.PressD = rep.PressPick;
                    c.Plan = plan;
                }
                var (h, a2) = c.Sides(g);
                bool home = g.Home == team;
                for (int k = 0; k < reps; k++) { s += Pts(ShiftEngine.Play(data, h, a2, new Random(k * 7919 + g.Id)), home); n++; }
            }
            c.Plan = null;
            return s / n * 82;
        }
        var res = new Result(Run(false, false), Run(true, false), Run(false, true), Run(true, true));
        c.Lineups[team] = auto;
        return res;
    }
}

/// <summary>
/// Подписи схем: каждый край рычага сдвигает в статистике матча то, что обещает его описание (те же матчи и случайные числа,
/// клуб против себя с обычной системой). Плюс доля голов «с хода после отбора» среди голов в равных составах.
/// </summary>
static class SignCheck
{
    // край → (сторона: u — наши, t — соперника; показатель; знаменатель или null; ожидаемое направление)
    static readonly (string pole, string side, string num, string den, int dir)[] Expect =
    {
        ("fc+", "u", "tk", null, 1), ("fc+", "u", "rush_sec", null, 1), ("fc-", "u", "tk", null, -1),
        ("en+", "u", "gv", null, 1), ("en-", "u", "gv", null, -1),
        ("da+", "u", "datt", null, 1), ("da-", "u", "datt", null, -1),
        ("sh+", "u", "att", null, 1), ("sh-", "u", "att", null, -1),
        ("agg+", "u", "hit", null, 1), ("agg+", "u", "penx", null, 1), ("agg-", "u", "hit", null, -1), ("agg-", "u", "penx", null, -1),
        ("dz-", "t", "z0", "zs", -1), ("dz-", "u", "blk", null, 1), ("dz+", "t", "att", null, -1), ("dz+", "u", "tk", null, 1),
        ("bo-", "u", "gv", null, -1), ("bo-", "u", "fo_dz", "fo", 1), ("bo+", "u", "rush_xg", "rush_sec", 1), ("bo+", "u", "gv", null, 1),
        ("pp-", "u", "pp_datt", "pp_att", 1), ("pp+", "u", "pp_datt", "pp_att", -1),
        ("pk-", "u", "pk_blk", null, 1), ("pk-", "u", "pk_att", null, -1), ("pk+", "u", "pk_att", null, 1),
    };

    public static (int ok, int total, double rushShare, List<string> lines) Run(GameData data, bool verbose, int reps = 4, string team = "MIN")
    {
        var c = Career.New(data, team, 818);
        var games = c.Games.Where(g => !g.Playoff && (g.Home == team || g.Away == team)).Take(41).ToList();
        var l = c.Lineups[team];
        var keep = (int[])l.Sys.Clone(); var keepL = new Dictionary<string, float>(l.Learn);
        double rushG = 0, esG = 0;
        Dictionary<string, double>[] Play(int[] sys)
        {
            l.Sys = sys; l.Learn = new();
            for (int i = 0; i < sys.Length; i++) if (sys[i] != 0) l.Learn[Tactics.Levers[i].Key + (sys[i] < 0 ? "-" : "+")] = 1;
            var us = new Dictionary<string, double>(); var them = new Dictionary<string, double>();
            foreach (var g in games)
            {
                var (h, a) = c.Sides(g);
                bool home = g.Home == team;
                for (int k = 0; k < reps; k++)
                {
                    var r = ShiftEngine.Play(data, h, a, new Random(k * 7919 + g.Id), false);
                    foreach (var (d, src) in new[] { (us, home ? r.HomeStats : r.AwayStats), (them, home ? r.AwayStats : r.HomeStats) })
                        foreach (var kv in src) d[kv.Key] = d.GetValueOrDefault(kv.Key) + kv.Value;
                    if (sys.All(x => x == 0))
                    {
                        rushG += (r.HomeStats.GetValueOrDefault("rush_g") + r.AwayStats.GetValueOrDefault("rush_g"));
                        esG += r.Events.Count(e => e.Kind == "goal" && e.Note == "ev");
                    }
                }
            }
            foreach (var d in new[] { us, them }) d["zs"] = d.GetValueOrDefault("z0") + d.GetValueOrDefault("z1") + d.GetValueOrDefault("z2");
            return new[] { us, them };
        }
        var b = Play(new int[Tactics.Levers.Length]);
        int ok = 0;
        var lines = new List<string>();
        foreach (var grp in Expect.GroupBy(x => x.pole))
        {
            string key = grp.Key.TrimEnd('+', '-');
            int lever = Tactics.IndexOf(key), v = grp.Key.EndsWith("+") ? 1 : -1;
            var sys = new int[Tactics.Levers.Length]; sys[lever] = v;
            var x = Play(sys);
            foreach (var (pole, side, num, den, dir) in grp)
            {
                int si = side == "u" ? 0 : 1;
                double Val(Dictionary<string, double>[] s) => den == null ? s[si].GetValueOrDefault(num) : s[si].GetValueOrDefault(num) / Math.Max(1e-9, s[si].GetValueOrDefault(den));
                double v0 = Val(b), v1 = Val(x), rel = v0 == 0 ? 0 : v1 / v0 - 1;
                bool good = Math.Sign(rel) == dir && Math.Abs(rel) >= 0.03;
                if (good) ok++;
                string line = $"{Tactics.PoleName(Tactics.Levers[lever], v),-24} {(side == "u" ? "наши" : "их"),-4} {num}{(den != null ? "/" + den : ""),-12} {rel:+0%;-0%} {(good ? "OK" : "НЕТ")}";
                lines.Add(line);
                if (verbose) Console.WriteLine(line);
            }
        }
        l.Sys = keep; l.Learn = keepL;
        return (ok, Expect.Length, esG == 0 ? 0 : rushG / esG, lines);
    }
}
