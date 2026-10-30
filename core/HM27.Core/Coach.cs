namespace HM27.Core;

/// <summary>
/// Помощник тренера: план на матч и советы. Принцип (ТЗ «Тренер»): у решений нет одного верного ответа —
/// лучшее зависит от соперника, матча и игроков, а помощник считает по тому, что видит клуб, и о цене решений говорит прямо.
///   • Снятие вратаря: время, при котором шанс сравнять счёт выше всего, — цепь «гол / пропущенный» по секундам
///     с частотами голов этого матча (6×5 и в пустые ворота у НХЛ — ShiftEngine.GF60_6V5, GF60_EN). Против сильной атаки
///     раннее снятие опасно (в пустые забьют быстрее), против слабой — выгодно; разница между матчами — минуты.
///   • Темп по сопернику: закрытая игра — меньше голов и больше овертаймов (это нужно слабому), открытая — фавориту.
///   • Вариант состава: лучшие по способностям и свежести, но помощник не держит в голове сыгранность звеньев,
///     обиды тех, кого он опускает, и минуты молодых — их он показывает, решает тренер.
/// </summary>
public static class Coach
{
    public const int USUAL = 0, EARLY = 1, ASSIST = 2;
    /// <summary>«Рано» — всегда за 4:00 при −1 и за 5:00 при −2, кто бы ни был соперник.</summary>
    public const double EARLY1 = 240, EARLY2 = 300;
    /// <summary>Темп меняется, только если это даёт хотя бы столько очков за матч (иначе — обычный).</summary>
    const double TEMPO_MIN = 0.004;
    /// <summary>Насколько больше весит атака атакующего звена и оборона оборонительного в оценке помощника.</summary>
    const double ROLE_W = 0.1;
    /// <summary>
    /// Раньше этого помощник вратаря не снимает (7:00 при −1, 9:00 при −2). Расчёт по частотам НХЛ (как у Asness и Brown, 2018)
    /// даёт оптимум около 5–6 минут при −1 и 10+ при −2, но тренеры НХЛ так не делают никогда: за этим — крупные поражения.
    /// </summary>
    const double MAX1 = 420, MAX2 = 540;
    /// <summary>Средняя λ прогноза на матч — доля голов базы за матч (основное время; HM27.Check «coach»: 3,02 при 3,18).</summary>
    const double LAM_REF = 0.95;

    // ------------------------------------------------------------------ план на матч

    public static void Prepare(Career c, ScheduledGame g, MatchSide h, MatchSide a)
    {
        foreach (var (s, home) in new[] { (h, true), (a, false) })
            if (s.Lineup.TempoAuto) s.Tempo = Tempo(c, g, h, a, home).tempo;
        foreach (var (s, home) in new[] { (h, true), (a, false) })
            switch (s.Lineup.Pull)
            {
                case EARLY: s.Pull1 = EARLY1; s.Pull2 = EARLY2; break;
                case ASSIST: var p = Pull(c, g, h, a, home); s.Pull1 = p.T1; s.Pull2 = p.T2; break;
            }
    }

    /// <summary>Очки за матч в среднем (плей-офф — шанс победы) для стороны.</summary>
    static double Value(MatchOdds o, bool home, bool playoff)
    {
        double pw = home ? o.PHome : 1 - o.PHome;
        if (playoff) return pw;
        double reg = home ? o.PHomeReg : o.PAwayReg;
        return 2 * pw + (o.PTie - (pw - reg));   // победа — 2, поражение в ОТ/по буллитам — 1
    }

    public readonly record struct TempoPick(int tempo, double gain, string conf);

    /// <summary>
    /// Темп по сопернику — как его видит тренер стороны (помощник у своего клуба, главный тренер у ИИ): выгода каждого темпа
    /// по прогнозу + его ошибка (тактика) + пристрастие (рисковый любит открытую игру, осторожный — закрытую).
    /// </summary>
    public static TempoPick Tempo(Career c, ScheduledGame g, MatchSide h, MatchSide a, bool home)
    {
        var me = home ? h : a;
        var who = Staff.Decider(c, me.Team.Abbr);
        int keep = me.Tempo;
        double V(int t) { me.Tempo = t; return Value(MatchEngine.Odds(c.Data, h, a, g.Playoff, sharp: c.Sharp), home, g.Playoff); }
        double v0 = V(0), best = 0; int pick = 0;
        foreach (int t in new[] { -1, 1 })
        {
            double est = Staff.Estimate(c, who, V(t) - v0, g.Id, 100 + t) / 3 + (who?.Risk ?? 0) * 0.003 * t;   // очки: ошибка в голах/3
            if (est > best + TEMPO_MIN) { best = est; pick = t; }
        }
        me.Tempo = keep;
        return new TempoPick(pick, best, Staff.Confidence(who, best * 3));
    }

    public static string TempoText(int tempo, double gain, string opp, bool playoff) => tempo switch
    {
        -1 => $"{opp} сильнее: в закрытой игре меньше моментов у обеих команд и чаще овертайм — {(playoff ? "шанс на победу" : "очков в среднем")} {Gain(gain, playoff)}.",
        1 => $"{opp} слабее: в открытой игре больше голов, и сильнему реже не хватает времени — {(playoff ? "шанс на победу" : "очков в среднем")} {Gain(gain, playoff)}.",
        _ => $"С {opp} примерно на равных: менять темп нет смысла.",
    };

    static string Gain(double g, bool playoff) => playoff ? $"+{100 * g:0.0}%" : $"+{g:0.000} за матч";

    // ------------------------------------------------------------------ снятие вратаря

    public readonly record struct PullPlan(double T1, double T2, double Avg1, double Avg2, double Tie1, double TieUsual1, double Own, double Opp);

    /// <summary>Частоты голов в концовке (в секунду): свои и соперника при 5×5 (−1, −2, −3), 6×5 и в пустые ворота.</summary>
    readonly record struct Rates(double[] G5, double[] C5, double G6, double E);

    static Rates RatesFor(double ro, double rp)
    {
        double k3 = ShiftEngine.PERIOD_K[2] / 3600;
        var g5 = new double[4]; var c5 = new double[4];
        for (int d = 1; d <= 3; d++)
        {
            g5[d] = ShiftEngine.GF60_5V5 * k3 * ro * ShiftEngine.SE_G_TRAIL[d - 1];
            c5[d] = ShiftEngine.GF60_5V5 * k3 * rp * ShiftEngine.SE_G_LEAD[d - 1];
        }
        return new Rates(g5, c5, ShiftEngine.GF60_6V5 / 3600 * ro, ShiftEngine.GF60_EN / 3600 * rp);
    }

    /// <summary>Шанс сравнять счёт за последние T секунд при −1 и −2, если снимать вратаря за t1 и t2 до конца (при −3 — нет).</summary>
    static (double v1, double v2) Chain(in Rates r, double t1, double t2, int T = 600)
    {
        Span<double> v = stackalloc double[5], nv = stackalloc double[5];
        v.Clear(); v[0] = 1;
        for (int t = 1; t <= T; t++)
        {
            nv[0] = 1; nv[4] = 0;
            for (int d = 1; d <= 3; d++)
            {
                bool pulled = d == 1 ? t <= t1 : d == 2 && t <= t2;
                double s = pulled ? r.G6 : r.G5[d], k = pulled ? r.E : r.C5[d];
                nv[d] = s * v[d - 1] + k * v[d + 1] + (1 - s - k) * v[d];
            }
            nv.CopyTo(v);
        }
        return (v[1], v[2]);
    }

    static (double t1, double t2) Best(in Rates r)
    {
        double t1 = 200, t2 = 300;
        for (int round = 0; round < 2; round++)
        {
            double b = -1;
            for (double t = 30; t <= MAX1; t += 5) { var v = Chain(r, t, t2).v1; if (v > b) { b = v; t1 = t; } }
            b = -1;
            for (double t = 30; t <= MAX2; t += 5) { var v = Chain(r, t1, t, 720).v2; if (v > b) { b = v; t2 = t; } }
        }
        return (t1, t2);
    }

    static (double t1, double t2)? _avg;

    /// <summary>План снятия вратаря на матч: частоты голов — из прогноза матча (λ своих и соперника против среднего по лиге).</summary>
    public static PullPlan Pull(Career c, ScheduledGame g, MatchSide h, MatchSide a, bool home)
    {
        var o = MatchEngine.Odds(c.Data, h, a, g.Playoff, sharp: c.Sharp);
        double avg = c.Data.League.GoalsPerGame * LAM_REF;
        double ro = (home ? o.LamHome : o.LamAway) / avg, rp = (home ? o.LamAway : o.LamHome) / avg;
        var r = RatesFor(ro, rp);
        var (t1, t2) = Best(r);
        // тренер не калькулятор: рисковый снимает раньше, осторожный позже, тактика 8 ошибается на минуту с лишним
        var who = Staff.Decider(c, (home ? h : a).Team.Abbr);
        if (who != null)
        {
            double sd = 6.0 * (20 - who.Tac), sh = 25 * who.Risk;
            t1 = Math.Clamp(t1 + sh + sd * Staff.Noise(c, who, g.Id, 201), 60, MAX1);
            t2 = Math.Clamp(t2 + sh + sd * Staff.Noise(c, who, g.Id, 202), 60, MAX2);
        }
        _avg ??= Best(RatesFor(1, 1));
        return new PullPlan(t1, t2, _avg.Value.t1, _avg.Value.t2, Chain(r, t1, t2).v1, Chain(r, ShiftEngine.PULL1, ShiftEngine.PULL2).v1, ro, rp);
    }

    public static string Clock(double sec) => $"{(int)sec / 60}:{(int)sec % 60:00}";

    /// <summary>Почему план раньше или позже среднего матча (пусто — соперник и мы средние).</summary>
    public static string PullWhy(PullPlan p, string opp)
    {
        var why = new List<string>();
        if (p.Opp > 1.06) why.Add($"{opp} опасен в атаке — в пустые ворота забьёт быстро");
        else if (p.Opp < 0.94) why.Add($"{opp} создаёт мало — пустые ворота не так страшны");
        if (p.Own > 1.06) why.Add("у нас сильная атака — вшестером забиваем чаще");
        else if (p.Own < 0.94) why.Add("атака у нас слабее обычного — вшестером забиваем реже");
        string head = p.T1 < p.Avg1 - 10 ? "Позже, чем в среднем матче" : p.T1 > p.Avg1 + 10 ? "Раньше, чем в среднем матче" : "Как в среднем матче";
        return head + (why.Count > 0 ? ": " + string.Join("; ", why) : "") + ".";
    }

    public static string PullText(PullPlan p, string opp) =>
        $"Снять вратаря за {Clock(p.T1)} при −1 и за {Clock(p.T2)} при −2. {PullWhy(p, opp)} "
        + $"Шанс сравнять при −1 за 10 минут до конца — {100 * p.Tie1:0}% (если снимать за 2:00, как большинство тренеров, — {100 * p.TieUsual1:0}%). Цена — чаще крупные поражения.";

    // ------------------------------------------------------------------ вариант состава

    public sealed class Review
    {
        public Lineup Lineup;
        /// <summary>Чья это оценка (помощник своего клуба).</summary>
        public StaffMember By;
        /// <summary>Голы за матч по оценке помощника: забиваем и пропускаем (против текущего состава).</summary>
        public double Gf, Ga;
        /// <summary>То же от сыгранности: новые звенья сыграны хуже.</summary>
        public double FamGf, FamGa;
        public List<string> Changes = new();
        public List<(int id, string text)> Upset = new();
        public List<(int id, string text)> Youth = new();
        public int Moved;
    }

    /// <summary>Текущий состав, каким он выйдет на матч: травмированных помощник заменит сам.</summary>
    public static Lineup Baseline(Career c, string abbr)
    {
        var l = c.Lineups[abbr].Clone();
        l.Repair(c.Data, abbr, c.Injured.ToHashSet());
        return l;
    }

    /// <summary>
    /// Вариант помощника: от текущего состава — перестановки и замены, пока они добавляют голов в его глазах
    /// (Staff.View: его оценка игроков с ошибкой и пристрастиями, усталость, половина формы). Нападающих в защиту и наоборот
    /// не ставит, вратарей не трогает, сыгранность, обиды и минуты молодых не считает — их показывает Assess.
    /// </summary>
    public static Review Propose(Career c, string abbr)
    {
        var l = Baseline(c, abbr);
        LineOpt.Improve(c, abbr, l, Staff.Decider(c, abbr), 0.002, 16);
        // спецбригады: выбывшие из состава — заменить по одному месту
        var dressed = l.Skaters.ToHashSet();
        foreach (var u in new[] { l.Pp1, l.Pp2, l.Pk1, l.Pk2 }) for (int k = 0; k < u.Length; k++) if (!dressed.Contains(u[k])) u[k] = 0;
        l.FixPowerPlay(c.Data);
        return Assess(c, abbr, l);
    }

    /// <summary>Что даст состав l против текущего: голы по способностям, сыгранность, обиды, минуты молодых.</summary>
    public static Review Assess(Career c, string abbr, Lineup l)
    {
        var cur = Baseline(c, abbr);
        var rv = new Review { Lineup = l, By = Staff.Decider(c, abbr) };
        // по оценке помощника — не по истинной силе: он может ошибаться
        var (g1, a1) = LineOpt.Eval(c, abbr, l, rv.By); var (g0, a0) = LineOpt.Eval(c, abbr, cur, rv.By);
        rv.Gf = g1 - g0; rv.Ga = a1 - a0;
        var (f1, fa1) = FamGoals(c, l); var (f0, fa0) = FamGoals(c, cur);
        rv.FamGf = f1 - f0; rv.FamGa = fa1 - fa0;

        string Where(Lineup x, int id)
        {
            for (int i = 0; i < x.F.Count; i++) if (x.F[i].Contains(id)) return $"{i + 1}-я тройка";
            for (int i = 0; i < x.D.Count; i++) if (x.D[i].Contains(id)) return $"{i + 1}-я пара";
            return null;
        }
        var now = cur.Skaters.ToHashSet(); var next = l.Skaters.ToHashSet();
        foreach (var id in next.Except(now)) rv.Changes.Add($"В состав: {c.Short(id)} ({Where(l, id)})");
        foreach (var id in now.Except(next)) rv.Changes.Add($"На трибуну: {c.Short(id)}");
        foreach (var id in next.Intersect(now))
        {
            string w0 = Where(cur, id), w1 = Where(l, id);
            if (w0 != w1) { rv.Moved++; rv.Changes.Add($"{c.Short(id)}: {w0} → {w1}"); }
        }
        if (l.G1 != cur.G1 && l.G1 != 0) rv.Changes.Add($"Основной вратарь: {c.Short(l.G1)}");

        foreach (var p in c.Roster(abbr).Where(p => !p.IsGoalie && !c.Injured.Contains(p.Id)))
        {
            double m0 = cur.MinutesOf(p.Id), m1 = l.MinutesOf(p.Id);
            if (m1 >= m0 - 1) continue;
            int st = Morale.Status(c, p);
            double exp = Morale.ExpectedToi(st, p);
            if (st <= 2 && m1 < exp * 0.85)
                rv.Upset.Add((p.Id, m1 == 0 ? $"сядет на трибуну — {Morale.StatusWord(st, p)} обидится" : $"{m0:0} → {m1:0} мин — {Morale.StatusWord(st, p)} ждёт {exp:0}"));
            if (p.Age(c.Today) is int age && age <= 22)
                rv.Youth.Add((p.Id, m1 == 0 ? $"{age} лет: без матчей растёт медленнее" : $"{age} лет: {m0:0} → {m1:0} мин — меньше практики"));
        }
        return rv;
    }

    /// <summary>
    /// Голы за матч от сыгранности звеньев против нейтральной (FAM_REF): так же, как в движке, — средняя сыгранность пар
    /// на льду (три пары нападающих и пара защиты), по долям времени троек и пар.
    /// </summary>
    public static (double gf, double ga) FamGoals(Career c, Lineup l)
    {
        double gpg = c.Data.League.GoalsPerGame * MatchEngine.REG_SHARE;
        double fs = l.FMin.Take(l.F.Count).Sum(), ds = l.DMin.Take(l.D.Count).Sum();
        if (fs <= 0 || ds <= 0) return (0, 0);
        double e = 0;
        for (int i = 0; i < l.F.Count && i < l.FMin.Length; i++)
            for (int j = 0; j < l.D.Count && j < l.DMin.Length; j++)
            {
                double f = (3 * c.UnitFam(l.F[i]) + c.UnitFam(l.D[j])) / 4;
                e += l.FMin[i] / fs * l.DMin[j] / ds * ShiftEngine.FamE(f);
            }
        return (gpg * (Math.Exp(e) - 1), gpg * (Math.Exp(-e) - 1));
    }
}

/// <summary>План на конкретный матч: система на матч (вместо системы клуба) и задания на соперника.</summary>
public sealed class MatchPlan
{
    public int Game { get; set; }
    public int[] Sys { get; set; }
    public int Shadow { get; set; }
    public int PressD { get; set; }
}

/// <summary>
/// Правила «если — то»: помощник применяет их в перерывах (и в матче «сразу к итогу», и у клубов ИИ); в трансляции — советует.
/// Каждый перерыв решение считается заново от плана на матч: правило перестало выполняться — настройка возвращается.
/// Смена вратаря — один раз за матч.
/// </summary>
public static class Rules
{
    public sealed record Rule(string When, string Then, bool On = true);

    public static readonly (string key, string text)[] Whens =
    {
        ("lead2", "Ведём в две шайбы и больше"),
        ("lead1", "Ведём в одну шайбу после второго периода"),
        ("trail", "Проигрываем после второго периода"),
        ("trail2", "Проигрываем в две шайбы и больше"),
        ("pens", "Три удаления и больше за период"),
        ("goalie", "Вратарь пропускает много, а мы проигрываем"),
        ("shots", "Уступаем по броскам восемь и больше"),
    };

    public static readonly (string key, string text)[] Thens =
    {
        ("none", "Ничего не менять"),
        ("tempo-", "Закрыть игру"), ("tempo+", "Открыть игру"),
        ("fc-", "Сдержанный форчек 1-2-2"), ("fc0", "Убрать прессинг"), ("fc+", "Прессинг 2-3"),
        ("sh+", "Бросать всё"), ("da-", "Защитники — сзади"), ("da+", "Защитники — в атаку"),
        ("goalie", "Сменить вратаря"),
    };

    public static string WhenText(string k) => Whens.FirstOrDefault(x => x.key == k).text ?? k;
    public static string ThenText(string k) => Thens.FirstOrDefault(x => x.key == k).text ?? k;

    /// <summary>Как поступают тренеры НХЛ: при большом счёте закрываются, проигрывая после второго — открываются, после четырёх пропущенных меняют вратаря.</summary>
    public static List<Rule> Default() => new()
    {
        new("lead2", "tempo-"), new("lead1", "fc-", false), new("trail", "tempo+"), new("trail2", "fc+", false),
        new("pens", "fc0"), new("goalie", "goalie"), new("shots", "sh+", false),
    };

    /// <summary>Клубы ИИ: только смена вратаря — поведение при счёте (закрыться, открыться) уже внутри эффекта счёта по данным НХЛ.</summary>
    public static List<Rule> Ai() => new() { new("goalie", "goalie") };

    public static List<Rule> Of(Lineup l) => l.Rules ?? Default();

    /// <summary>Выполняется ли условие для стороны в этом перерыве.</summary>
    public static bool Holds(ShiftEngine.Sim sim, bool home, string when)
    {
        int my = home ? sim.HomeGoals : sim.AwayGoals, op = home ? sim.AwayGoals : sim.HomeGoals, diff = my - op, per = sim.Period;
        var side = home ? sim.HomeSide : sim.AwaySide;
        return when switch
        {
            "lead2" => diff >= 2,
            "lead1" => per >= 2 && diff == 1,
            "trail" => per >= 2 && diff < 0,
            "trail2" => diff <= -2,
            "pens" => sim.PensIn(home, per) >= 3,
            "goalie" => GoalieBad(sim.GoalieLine(home), diff) && sim.GoalieOn(home) == sim.Result.GoalieStart(home) && Backup(side) != 0,
            "shots" => (home ? sim.AwayShots - sim.HomeShots : sim.HomeShots - sim.AwayShots) >= 8,
            _ => false,
        };
    }

    /// <summary>Вратаря меняют, когда он пропустил 5+ и команда проигрывает, или 4 при отражённых ниже 85% и −3 и больше.</summary>
    static bool GoalieBad((int ga, int sa) g, int diff) => (g.ga >= 5 && diff < 0) || (g.ga >= 4 && diff <= -3 && g.sa > 0 && 1 - g.ga / (double)g.sa < 0.85);

    static int Backup(MatchSide s) => s.Goalie == s.Lineup.G1 ? s.Lineup.G2 : s.Lineup.G1;

    /// <summary>Что получится, если применить действия: темп, система на матч, смена вратаря (база — план на матч).</summary>
    public sealed class Decision { public int Tempo; public int[] Sys; public bool Goalie; public readonly List<string> Fired = new(); }

    public static Decision Decide(ShiftEngine.Sim sim, bool home, IEnumerable<Rule> rules, int baseTempo, int[] baseSys)
    {
        var d = new Decision { Tempo = baseTempo, Sys = (int[])(baseSys ?? new int[Tactics.Levers.Length]).Clone() };
        foreach (var r in rules)
        {
            if (!r.On || r.Then == "none" || !Holds(sim, home, r.When)) continue;
            switch (r.Then)
            {
                case "tempo-": d.Tempo = -1; break;
                case "tempo+": d.Tempo = 1; break;
                case "goalie": d.Goalie = true; break;
                case "fc0" when d.Sys[0] <= 0: continue;   // «убрать прессинг» — только если давим
                default:
                    int lever = Array.FindIndex(Tactics.Levers, l => r.Then.StartsWith(l.Key));
                    if (lever >= 0) d.Sys[lever] = r.Then.EndsWith("-") ? -1 : r.Then.EndsWith("+") ? 1 : 0;
                    break;
            }
            d.Fired.Add(ThenText(r.Then).ToLowerInvariant());
        }
        return d;
    }

    /// <summary>Перерыв: оба клуба решают по своим правилам (свой — по правилам тренера), матч продолжается с новыми настройками.</summary>
    public static void Break(Career c, ShiftEngine.Sim sim, (int tempo, int[] sys) homeBase, (int tempo, int[] sys) awayBase)
    {
        bool changed = false;
        foreach (bool home in new[] { true, false })
        {
            var side = home ? sim.HomeSide : sim.AwaySide;
            var (bt, bs) = home ? homeBase : awayBase;
            var d = Decide(sim, home, side.Lineup.Rules ?? (side.Team.Abbr == c.UserTeam ? Default() : Ai()), bt, bs);
            changed |= ApplyTo(c, sim, home, d);
        }
        if (changed) { Tactics.Prepare(c, sim.HomeSide, sim.AwaySide); sim.Apply(); }
    }

    /// <summary>Записать решение в сторону матча (без пересчёта систем); true — что-то поменялось.</summary>
    public static bool ApplyTo(Career c, ShiftEngine.Sim sim, bool home, Decision d)
    {
        var side = home ? sim.HomeSide : sim.AwaySide;
        var cur = side.Plan ?? side.Lineup.Sys ?? new int[Tactics.Levers.Length];
        bool sysChanged = !cur.SequenceEqual(d.Sys), tempoChanged = side.Tempo != d.Tempo, g = d.Goalie && Backup(side) != 0 && sim.GoalieOn(home) == sim.Result.GoalieStart(home);
        if (!sysChanged && !tempoChanged && !g) return false;
        var what = new List<string>();
        if (tempoChanged) what.Add(d.Tempo < 0 ? "закрываем игру" : d.Tempo > 0 ? "открываем игру" : "обычный темп");
        if (sysChanged)
            for (int i = 0; i < Tactics.Levers.Length; i++)
                if (cur.Length <= i || cur[i] != d.Sys[i]) what.Add(Tactics.Levers[i].Name.ToLowerInvariant() + " — " + Tactics.PoleName(Tactics.Levers[i], d.Sys[i]).ToLowerInvariant());
        if (g) { side.Goalie = Backup(side); what.Add("в воротах " + c.Short(side.Goalie)); }
        side.Tempo = d.Tempo;
        side.Plan = d.Sys;
        sim.Note(home, string.Join(", ", what));
        return true;
    }
}

/// <summary>
/// Советы помощника по ходу матча (перерывы и пауза в трансляции): правила тренера, контрприёмы против того, как сейчас
/// играет соперник, и «горячий» игрок соперника. Каждый совет можно принять или нет — решает тренер.
/// </summary>
public static class LiveAdvice
{
    public sealed record Item(string Title, string Why, Action Apply);

    public static List<Item> For(Career c, Career.LiveGame lg)
    {
        var sim = lg.Sim; bool home = lg.UserHome;
        var me = lg.Me; var op = lg.Opp;
        var list = new List<Item>();
        void Refresh() { Tactics.Prepare(c, sim.HomeSide, sim.AwaySide); sim.Apply(); }
        var curSys = (int[])(me.Plan ?? me.Lineup.Sys ?? new int[Tactics.Levers.Length]).Clone();
        var who = Staff.Decider(c, me.Team.Abbr);

        // 1) правила тренера
        var (bt, bs) = home ? lg.HB : lg.AB;
        var d = Rules.Decide(sim, home, Rules.Of(me.Lineup), bt, bs);
        if (d.Tempo != me.Tempo)
            list.Add(new(d.Tempo < 0 ? "Закрыть игру" : d.Tempo > 0 ? "Открыть игру" : "Вернуть обычный темп", Why(sim, home, me.Lineup, d.Tempo < 0 ? "tempo-" : "tempo+"),
                () => { me.Tempo = d.Tempo; sim.Note(home, d.Tempo < 0 ? "закрываем игру" : d.Tempo > 0 ? "открываем игру" : "обычный темп"); Refresh(); }));
        for (int i = 0; i < Tactics.Levers.Length; i++)
            if (d.Sys[i] != curSys[i])
            {
                int li = i, v = d.Sys[i];
                var lv = Tactics.Levers[i];
                list.Add(new($"{lv.Name}: {Tactics.PoleName(lv, v).ToLowerInvariant()}", Why(sim, home, me.Lineup, lv.Key + (v < 0 ? "-" : v > 0 ? "+" : "0")),
                    () => { var s = (int[])(me.Plan ?? me.Lineup.Sys ?? new int[Tactics.Levers.Length]).Clone(); s[li] = v; me.Plan = s; sim.Note(home, $"{lv.Name.ToLowerInvariant()} — {Tactics.PoleName(lv, v).ToLowerInvariant()}"); Refresh(); }));
            }
        if (d.Goalie && sim.GoalieOn(home) == sim.Result.GoalieStart(home))
        {
            int backup = me.Goalie == me.Lineup.G1 ? me.Lineup.G2 : me.Lineup.G1;
            var (ga, sa) = sim.GoalieLine(home);
            if (backup != 0)
                list.Add(new($"Сменить вратаря: {c.Short(backup)}", $"{c.Short(sim.GoalieOn(home))} пропустил {ga} из {sa} бросков",
                    () => { me.Goalie = backup; sim.Note(home, "в воротах " + c.Short(backup)); sim.Apply(); }));
        }

        // 2) против того, как соперник играет сейчас: полюс, который заметно лучше текущего (с выучкой)
        var keep = me.Plan;
        double Eval(int[] s) { me.Plan = s; var e = Tactics.Of(c, me, op); me.Plan = keep; return c.Data.League.GoalsPerGame * 0.95 * (Math.Exp(e.For) - Math.Exp(e.Against)); }
        double cur = Eval(curSys);
        for (int i = 0; i < Tactics.Levers.Length; i++)
        {
            if (list.Any(x => x.Title.StartsWith(Tactics.Levers[i].Name))) continue;
            foreach (int v in new[] { -1, 0, 1 })
            {
                if (v == curSys[i]) continue;
                var s = (int[])curSys.Clone(); s[i] = v;
                double gain = Staff.Estimate(c, who, Eval(s) - cur, lg.G.Id * 100 + sim.Clock / 60, 70 + i * 10 + v) + (i == 0 && who != null && who.Press == v ? 0.02 : 0);
                if (gain < 0.03) continue;
                int li = i, vv = v; var lv = Tactics.Levers[i];
                me.Plan = s; var notes = Tactics.Of(c, me, op).Notes; me.Plan = keep;
                list.Add(new($"{lv.Name}: {Tactics.PoleName(lv, v).ToLowerInvariant()}",
                    (notes.FirstOrDefault() ?? "против их нынешней игры так выгоднее") + $" — около {gain:+0.00} гола за матч, {Staff.Confidence(who, gain)}",
                    () => { var s2 = (int[])(me.Plan ?? me.Lineup.Sys ?? new int[Tactics.Levers.Length]).Clone(); s2[li] = vv; me.Plan = s2; sim.Note(home, $"{lv.Name.ToLowerInvariant()} — {Tactics.PoleName(lv, vv).ToLowerInvariant()}"); Refresh(); }));
                break;
            }
        }

        // 3) у кого-то не идёт игра: из первых двух троек — минус два и хуже без очков; поменять с лучшим из третьей-четвёртой
        var ml = me.Lineup;
        int Pts(int id) => sim.Result.Events.Count(e => e.Kind == "goal" && e.Team == me.Team.Abbr && e.P1 == id)
                           + sim.Result.Goals.Count(g => g.Team == me.Team.Abbr && (g.A1 == id || g.A2 == id));
        var top = ml.F.Take(2).SelectMany(x => x).Where(id => id != 0).ToList();
        var low = ml.F.Skip(2).SelectMany(x => x).Where(id => id != 0).ToList();
        var bad = top.Select(id => (id, b: sim.BoxOf(home, id))).Where(x => x.b != null && x.b.Pm <= -2 && Pts(x.id) == 0).OrderBy(x => x.b.Pm).FirstOrDefault();
        if (bad.b != null && low.Count > 0)
        {
            var good = low.Select(id => (id, b: sim.BoxOf(home, id), p: Pts(id))).Where(x => x.b != null).OrderByDescending(x => x.p * 2 + x.b.Pm).First();
            if (good.p > 0 || good.b.Pm > 0)
            {
                int x1 = bad.id, x2 = good.id;
                list.Add(new($"Поменять {c.Short(x1)} и {c.Short(x2)}", $"у {c.Short(x1)} {bad.b.Pm} и ни одного очка, у {c.Short(x2)} {(good.p > 0 ? $"{good.p} очк." : $"+{good.b.Pm}")}; сыгранность новых троек ниже",
                    () => { sim.SwapLines(home, x1, x2); sim.Note(home, $"{c.Short(x2)} — выше, {c.Short(x1)} — ниже"); sim.Apply(); }));
            }
        }
        // указание не работает: игрок с указанием в минусе
        foreach (var (id, key) in ml.Orders ?? new())
        {
            var b = sim.BoxOf(home, id);
            if (b == null || b.Pm > -2 || Orders.Get(key) is not { } od) continue;
            list.Add(new($"Снять указание «{od.Name.ToLowerInvariant()}» с {c.Short(id)}", $"с ним на льду {b.Pm} — не работает",
                () => { ml.Orders = new(ml.Orders); ml.Orders.Remove(id); sim.Note(home, $"{c.Short(id)} — без указаний"); sim.Apply(); }));
        }

        // 4) у соперника «горячий» игрок — опека
        var oppAbbr = op.Team.Abbr;
        var hot = sim.Result.Events.Where(e => e.Kind == "goal" && e.Team == oppAbbr).GroupBy(e => e.P1).OrderByDescending(x => x.Count()).FirstOrDefault();
        if (hot != null && hot.Count() >= 2 && me.Shadow != hot.Key)
        {
            int id = hot.Key;
            list.Add(new($"Плотно опекать {c.Short(id)}", $"у него уже {hot.Count()} {(hot.Count() < 5 ? "гола" : "голов")}; у нас станет чуть больше удалений",
                () => { me.Shadow = id; sim.Note(home, "опекаем " + c.Short(id)); sim.Apply(); }));
        }
        return list;
    }

    static string Why(ShiftEngine.Sim sim, bool home, Lineup l, string then)
    {
        var r = Rules.Of(l).FirstOrDefault(x => x.On && x.Then == then && Rules.Holds(sim, home, x.When));
        return r != null ? "правило: " + Rules.WhenText(r.When).ToLowerInvariant() : "по правилам тренера условие больше не выполняется — возвращаем план на матч";
    }
}


/// <summary>
/// Звенья глазами тренера (помощник своего клуба и главные тренеры ИИ): вклад игрока на месте = минуты × (атака − штраф места,
/// оборона − штраф места) с весами силы клуба (TeamStrength: A1, D1), атакующему звену атака весит больше, оборонительному — оборона.
/// Модель линейная, как сама сила клуба, — перебор перестановок мгновенный. Игроки — как их видит тренер (Staff.View).
/// </summary>
public static class LineOpt
{
    const double ROLE_W = 0.1;

    sealed class Ctx
    {
        public Dictionary<int, (double off, double def)> View; public Dictionary<int, PlayerData> P;
        public double WA, WD;
    }

    static Ctx Make(Career c, string abbr, StaffMember who)
    {
        var sc = TeamStrength.Coef(c.Data);
        double gpg = c.Data.League.GoalsPerGame * MatchEngine.REG_SHARE;
        var ps = c.Roster(abbr).Where(p => !p.IsGoalie).ToList();
        return new Ctx
        {
            View = ps.ToDictionary(p => p.Id, p => Staff.View(c, who, p)), P = ps.ToDictionary(p => p.Id),
            WA = gpg * TeamStrength.SPREAD * sc.A1, WD = -gpg * TeamStrength.SPREAD * sc.D1,
        };
    }

    static (double gf, double ga) Slot(Ctx x, Lineup l, int id, int line, int s, bool d)
    {
        if (id == 0 || !x.View.TryGetValue(id, out var v)) return (0, 0);
        char sl = d ? 'D' : s == 0 ? 'L' : s == 1 ? 'C' : 'R';
        var (po, pd) = Lineup.SlotPenalty(x.P[id], sl);
        double min = (d ? (line < l.DMin.Length ? l.DMin[line] : 0) : (line < l.FMin.Length ? l.FMin[line] : 0)) / 60.0;
        int role = d ? l.RoleD(line) : l.RoleF(line);
        return (min * x.WA * (role == 1 ? 1 + ROLE_W : 1) * TeamStrength.Damp(v.off - po), -min * x.WD * (role == 2 ? 1 + ROLE_W : 1) * TeamStrength.Damp(v.def - pd));
    }

    /// <summary>Голы за матч в глазах тренера: забиваем (+) и пропускаем (−, меньше — лучше) — для сравнения составов.</summary>
    public static (double gf, double ga) Eval(Career c, string abbr, Lineup l, StaffMember who)
    {
        var x = Make(c, abbr, who);
        double gf = 0, ga = 0;
        for (int i = 0; i < l.F.Count; i++) for (int s = 0; s < l.F[i].Length; s++) { var v = Slot(x, l, l.F[i][s], i, s, false); gf += v.gf; ga += v.ga; }
        for (int i = 0; i < l.D.Count; i++) for (int s = 0; s < l.D[i].Length; s++) { var v = Slot(x, l, l.D[i][s], i, s, true); gf += v.gf; ga += v.ga; }
        return (gf, ga);
    }

    /// <summary>Перестановки и замены со скамейки, пока ход даёт не меньше minGain гола за матч; не больше maxMoves ходов. Возвращает число ходов.</summary>
    public static int Improve(Career c, string abbr, Lineup l, StaffMember who, double minGain, int maxMoves)
    {
        var x = Make(c, abbr, who);
        var inj = c.Injured.ToHashSet();
        double V(int id, int line, int s, bool d) { var v = Slot(x, l, id, line, s, d); return v.gf - v.ga; }
        int moves = 0;
        for (; moves < maxMoves; moves++)
        {
            double best = minGain; Action apply = null;
            foreach (bool d in new[] { false, true })
            {
                var lines = d ? l.D : l.F;
                var slots = new List<(int i, int s)>();
                for (int i = 0; i < lines.Count; i++) for (int s = 0; s < lines[i].Length; s++) slots.Add((i, s));
                for (int a = 0; a < slots.Count; a++)
                    for (int b = a + 1; b < slots.Count; b++)
                    {
                        var (i1, s1) = slots[a]; var (i2, s2) = slots[b];
                        if (d && i1 == i2) continue;
                        int p1 = lines[i1][s1], p2 = lines[i2][s2];
                        double delta = V(p2, i1, s1, d) + V(p1, i2, s2, d) - V(p1, i1, s1, d) - V(p2, i2, s2, d);
                        if (delta > best) { best = delta; apply = () => { lines[i1][s1] = p2; lines[i2][s2] = p1; }; }
                    }
                var dressed = l.Skaters.ToHashSet();
                foreach (var (id, p) in x.P)
                {
                    if (dressed.Contains(id) || inj.Contains(id) || p.IsDefense != d) continue;
                    foreach (var (i1, s1) in slots)
                    {
                        int cur = lines[i1][s1];
                        double delta = V(id, i1, s1, d) - V(cur, i1, s1, d);
                        if (delta > best) { best = delta; int nid = id; apply = () => lines[i1][s1] = nid; }
                    }
                }
            }
            if (apply == null) break;
            apply();
        }
        return moves;
    }
}
