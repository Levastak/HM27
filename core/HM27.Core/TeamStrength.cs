namespace HM27.Core;

/// <summary>
/// Сила клуба «снизу вверх»: из истинных характеристик игроков текущего состава при их минутах, с формой, и из вратаря матча.
/// Считается перед каждым матчем — в силу сразу попадают звенья тренера, травмы, форма, рост и старение, сезон вратаря.
/// Масштаб (сколько голов даёт единица индекса) подбирается один раз по базе: разброс сил клубов — как у модели «Сводки»
/// × SPREAD (подбор по разбросу таблиц НХЛ 2022–2026 в HM27.Check). Модель решает только масштаб, не порядок.
/// </summary>
public static class TeamStrength
{
    public static readonly (string k, double w)[] Off = { ("fin", 0.3), ("pas", 0.25), ("vis", 0.2), ("hnd", 0.15), ("pp", 0.1) };
    public static readonly (string k, double w)[] Def = { ("dfn", 0.4), ("tka", 0.2), ("blk", 0.15), ("pk", 0.15), ("dec", 0.1) };

    /// <summary>Множитель разброса сил относительно модели (подбор по таблицам НХЛ).</summary>
    public static double SPREAD = 1.08;
    /// <summary>«Одна шайба»: вклад сверх STAR_AT ед. выше среднего берётся с долей STAR_K (у звёзд отдача убывает).</summary>
    public static double STAR_AT = 3.0, STAR_K = 0.6;

    public static double Damp(double v) => v > STAR_AT ? STAR_AT + STAR_K * (v - STAR_AT) : v;

    /// <summary>Индексы атаки и обороны (ед. шкалы × доля матча) и GSAx вратаря.</summary>
    public static (double off, double def, double g) Index(GameData d, Lineup l, int goalie, Func<PlayerData, string, double> attr, Func<int, double> gsax)
    {
        double off = 0, def = 0;
        // по местам в звеньях: на чужом месте игрок теряет (Lineup.SlotPenalty)
        void Add(int id, double minutes, char slot)
        {
            if (id == 0 || !d.PlayerById.TryGetValue(id, out var p)) return;
            var (po, pd) = Lineup.SlotPenalty(p, slot);
            off += minutes / 60.0 * Damp(Off.Sum(x => x.w * (attr(p, x.k) - 11)) - po);
            def += minutes / 60.0 * Damp(Def.Sum(x => x.w * (attr(p, x.k) - 11)) - pd);
        }
        for (int i = 0; i < l.F.Count; i++)
            for (int s = 0; s < l.F[i].Length; s++) Add(l.F[i][s], i < l.FMin.Length ? l.FMin[i] : 0, s == 0 ? 'L' : s == 1 ? 'C' : 'R');
        for (int i = 0; i < l.D.Count; i++)
            foreach (var id in l.D[i]) Add(id, i < l.DMin.Length ? l.DMin[i] : 0, 'D');
        return (off, def, gsax(goalie));
    }

    public static double Est(PlayerData p, string k) => p.A.Count > 0 ? p.AttrEst(k) : p.Level;

    /// <summary>Масштаб: атака = A0 + A1·индекс, оборона = D0 + D1·индекс + D2·GSAx вратаря; G0 — средний GSAx основных вратарей.</summary>
    public sealed record Scale(double A0, double A1, double D0, double D1, double D2, double G0, double MA, double MY);
    public static Scale Coef(GameData d) => Cache.GetValue(d, Fit);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameData, Scale> Cache = new();

    /// <summary>
    /// Масштаб по базе (оценки из данных, составы клубов по умолчанию): направление — МНК (атака модели ~ индекс атаки,
    /// оборона ~ индекс обороны + вратарь), величина — так, чтобы разброс сил совпал с моделью × SPREAD
    /// (МНК сам по себе сжимает разброс: аутсайдеры выходили слишком сильными).
    /// </summary>
    static Scale Fit(GameData d)
    {
        var rows = d.Teams.Select(t =>
        {
            var l = Lineup.FromTeam(d, t, new HashSet<int>());
            return (t, ix: Index(d, l, l.G1, Est, id => MatchEngine.GsaxOf(d, id)));
        }).ToList();
        double Sd(IEnumerable<double> v) { var a = v.ToList(); double m = a.Average(); return Math.Sqrt(a.Average(x => (x - m) * (x - m))); }
        double mO = rows.Average(r => r.ix.off), mA = rows.Average(r => r.t.Att);
        double b = rows.Sum(r => (r.ix.off - mO) * (r.t.Att - mA)) / rows.Sum(r => (r.ix.off - mO) * (r.ix.off - mO));
        double mD = rows.Average(r => r.ix.def), mG = rows.Average(r => r.ix.g), mY = rows.Average(r => r.t.Dfn);
        double sdd = 0, sgg = 0, sdg = 0, sdy = 0, sgy = 0;
        foreach (var r in rows)
        {
            double x1 = r.ix.def - mD, x2 = r.ix.g - mG, y = r.t.Dfn - mY;
            sdd += x1 * x1; sgg += x2 * x2; sdg += x1 * x2; sdy += x1 * y; sgy += x2 * y;
        }
        double det = sdd * sgg - sdg * sdg;
        double d1 = (sdy * sgg - sgy * sdg) / det, d2 = (sgy * sdd - sdy * sdg) / det;
        // разброс суммарной силы (атака − оборона) — как у модели
        double sdModel = Sd(rows.Select(r => r.t.Att - r.t.Dfn));
        double sdOurs = Sd(rows.Select(r => b * r.ix.off - (d1 * r.ix.def + d2 * r.ix.g)));
        double k = sdModel / Math.Max(sdOurs, 1e-9);
        b *= k; d1 *= k; d2 *= k;
        return new Scale(mA - b * mO, b, mY - d1 * mD - d2 * mG, d1, d2, mG, mA, mY);
    }

    /// <summary>Сила клуба по данным базы (до начала карьеры: выбор клуба) — состав по умолчанию, оценки из данных.</summary>
    public static (double att, double dfn) Estimate(GameData d, TeamData t)
    {
        var s = Cache.GetValue(d, Fit);
        var l = Lineup.FromTeam(d, t, new HashSet<int>(t.Injured));
        var ix = Index(d, l, l.G1, Est, id => MatchEngine.GsaxOf(d, id));
        return (SPREAD * (s.A0 + s.A1 * ix.off), SPREAD * (s.D0 + s.D1 * ix.def + s.D2 * ix.g));
    }

    /// <summary>
    /// Истинная характеристика на матч: розыгрыш от данных + всё «не талантное» из сдвига движка
    /// (форма, а в плей-офф ещё и хладнокровие). Сдвиг уровня (истинный − данные) уже внутри характеристик, поэтому вычитается.
    /// </summary>
    public static Func<PlayerData, string, double> Attr(Career c, Func<int, double> dl) => (p, key) =>
        !c.Players.TryGetValue(p.Id, out var st) ? Est(p, key) : st.A.GetValueOrDefault(key, st.Lvl) + dl(p.Id) - (st.Ca - p.CaEst) / PlayerModel.CA_PER_LVL;

    /// <summary>Сила клуба на матч: текущий состав, вратарь матча, истинные значения с формой.</summary>
    public static (double att, double dfn) Live(Career c, Lineup l, int goalie, Func<int, double> dl)
    {
        var s = Cache.GetValue(c.Data, Fit);
        var ix = Index(c.Data, l, goalie, Attr(c, dl), id => MatchEngine.GsaxOf(c.Data, id, dl));
        double att = s.A0 + s.A1 * ix.off, dfn = s.D0 + s.D1 * ix.def + s.D2 * ix.g;
        // разброс × SPREAD вокруг среднего лиги (среднее по базе — ноль атаки/обороны модели)
        return (SPREAD * att, SPREAD * dfn);
    }
}
