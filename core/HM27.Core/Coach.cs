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

    /// <summary>Темп по сопернику: какой из трёх даёт больше очков (с учётом темпа соперника) и сколько.</summary>
    public static (int tempo, double gain) Tempo(Career c, ScheduledGame g, MatchSide h, MatchSide a, bool home)
    {
        var me = home ? h : a;
        int keep = me.Tempo;
        double V(int t) { me.Tempo = t; return Value(MatchEngine.Odds(c.Data, h, a, g.Playoff, sharp: c.Sharp), home, g.Playoff); }
        double v0 = V(0), best = v0; int pick = 0;
        foreach (int t in new[] { -1, 1 }) { double v = V(t); if (v > best + TEMPO_MIN) { best = v; pick = t; } }
        me.Tempo = keep;
        return (pick, best - v0);
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
        /// <summary>Голы за матч: забиваем и пропускаем по способностям (против текущего состава).</summary>
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
    /// Вариант помощника: от текущего состава — перестановки и замены, пока они добавляют голов по способностям
    /// (истинные характеристики без формы — так клуб знает своих). Нападающих в защиту и наоборот не ставит, вратарей не трогает,
    /// а сыгранность, обиды и минуты молодых не считает — их показывает Assess.
    /// </summary>
    public static Review Propose(Career c, string abbr)
    {
        var l = Baseline(c, abbr);
        var inj = c.Injured.ToHashSet();
        var bench = c.Roster(abbr).Where(p => !p.IsGoalie && !inj.Contains(p.Id) && !l.Skaters.Contains(p.Id)).ToList();
        double gpg = c.Data.League.GoalsPerGame * MatchEngine.REG_SHARE;
        // роли звеньев: атакующее звено начинает в зоне атаки, оборонительное — в своей и против лучших; их атака (оборона)
        // весит больше (~10%: столько дают зоны вбрасываний и расстановка в движке)
        var sc = TeamStrength.Coef(c.Data);
        var od = c.Roster(abbr).ToDictionary(p => p.Id, p => Tactics.OffDef(c, p.Id));
        double Role(Lineup x)
        {
            double b = 0;
            void Add(int[] ids, double min, int role)
            {
                if (role == 0) return;
                foreach (var id in ids)
                    if (od.TryGetValue(id, out var v)) b += ROLE_W * min / 60 * TeamStrength.SPREAD * (role == 1 ? sc.A1 * v.off : -sc.D1 * v.def);
            }
            for (int i = 0; i < x.F.Count; i++) Add(x.F[i], i < x.FMin.Length ? x.FMin[i] : 0, x.RoleF(i));
            for (int i = 0; i < x.D.Count; i++) Add(x.D[i], i < x.DMin.Length ? x.DMin[i] : 0, x.RoleD(i));
            return gpg * b;
        }
        double J(Lineup x) { var (at, df) = TeamStrength.Live(c, x, x.G1, c.DlBase); return gpg * (Math.Exp(at) - Math.Exp(df)) + Role(x); }
        var slotsF = new List<(int i, int s)>(); for (int i = 0; i < l.F.Count; i++) for (int s = 0; s < l.F[i].Length; s++) slotsF.Add((i, s));
        var slotsD = new List<(int i, int s)>(); for (int i = 0; i < l.D.Count; i++) for (int s = 0; s < l.D[i].Length; s++) slotsD.Add((i, s));
        double cur = J(l);
        for (int it = 0; it < 16; it++)
        {
            double best = cur + 0.002; Action apply = null;
            void Try(Action doIt, Action undo, Action commit) { doIt(); double v = J(l); undo(); if (v > best) { best = v; apply = commit; } }
            foreach (var (lines, slots, isD) in new[] { (l.F, slotsF, false), (l.D, slotsD, true) })
            {
                for (int x = 0; x < slots.Count; x++)
                    for (int y = x + 1; y < slots.Count; y++)
                    {
                        var (i1, s1) = slots[x]; var (i2, s2) = slots[y];
                        if (i1 == i2 && isD) continue;   // внутри пары — всё равно
                        void Sw() { (lines[i1][s1], lines[i2][s2]) = (lines[i2][s2], lines[i1][s1]); }
                        Try(Sw, Sw, Sw);
                    }
                foreach (var p in bench.Where(p => p.IsDefense == isD).ToList())
                    foreach (var (i1, s1) in slots)
                    {
                        int old = lines[i1][s1];
                        Try(() => lines[i1][s1] = p.Id, () => lines[i1][s1] = old, () =>
                        {
                            lines[i1][s1] = p.Id; bench.Remove(p);
                            if (c.Data.PlayerById.TryGetValue(old, out var op)) bench.Add(op);
                        });
                    }
            }
            if (apply == null) break;
            apply(); cur = J(l);
        }
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
        var rv = new Review { Lineup = l };
        double gpg = c.Data.League.GoalsPerGame * MatchEngine.REG_SHARE;
        var (a1, d1) = TeamStrength.Live(c, l, l.G1 != 0 ? l.G1 : cur.G1, c.DlBase);
        var (a0, d0) = TeamStrength.Live(c, cur, cur.G1, c.DlBase);
        rv.Gf = gpg * (Math.Exp(a1) - Math.Exp(a0)); rv.Ga = gpg * (Math.Exp(d1) - Math.Exp(d0));
        var (f1, g1) = FamGoals(c, l); var (f0, g0) = FamGoals(c, cur);
        rv.FamGf = f1 - f0; rv.FamGa = g1 - g0;

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
