namespace HM27.Core;

/// <summary>
/// Система игры клуба (этап «Тренер»): четыре рычага, у каждого — два полюса и середина.
///   Форчек: сдержанный 1-2-2 / обычный 2-1-2 / прессинг 2-3.
///   Вход в зону: вброс / по ситуации / проход с шайбой.
///   Защитники: сзади / обычно / в атаку.
///   Броски: только моменты / обычно / бросать всё.
/// Принцип «нет одного верного решения»: полюс без подходящих игроков — размен (больше голов у обеих команд или меньше),
/// с подходящими — выгода, с неподходящими — убыток; у каждого полюса есть контрприём соперника (Counter); новая схема
/// требует выучки (Learn: ~15 матчей), середина известна всем. Сила эффектов — дизайн (данных о системах в протоколах НХЛ нет),
/// масштаб: система под состав против обычной — обычно +0,03…+0,1 гола за матч, чужая — столько же в минус; контрприёмы — до ±0,07.
/// Эффект — множители к голам (своим и соперника), качеству бросков, доле бросков защитников, дальним броскам, удалениям и усталости;
/// считается на матч по составам (MatchSide.Sys) и входит и в прогноз, и в движок.
/// </summary>
public static class Tactics
{
    public sealed record Pole(string Name, string Short, string What, (string k, double w)[] Fit, char Group);
    public sealed record Lever(string Key, string Name, Pole Minus, string Mid, Pole Plus, string MidShort);

    public static readonly Lever[] Levers =
    {
        new("fc", "Форчек",
            new("Сдержанный 1-2-2", "1-2-2", "Один игрок давит, четверо закрывают середину: меньше моментов у обеих команд, нужна дисциплина и позиционная игра",
                new[] { ("dfn", 0.3), ("dec", 0.25), ("dis", 0.2), ("tm", 0.15), ("blk", 0.1) }, 'A'),
            "Обычный 2-1-2",
            new("Прессинг 2-3", "2-3", "Двое давят на защитников, третий подстраховывает: больше отборов и бросков, но и выходов соперника в численном большинстве; нужны скорость и выносливость",
                new[] { ("spd", 0.25), ("acc", 0.2), ("wrk", 0.2), ("tka", 0.2), ("sta", 0.15) }, 'A'), "2-1-2"),
        new("en", "Вход в зону",
            new("Вброс", "Вброс", "Шайбу — в угол и борьба за неё: меньше потерь на синей линии, броски похуже; нужны сила и работа в углах",
                new[] { ("str", 0.3), ("hit", 0.25), ("wrk", 0.25), ("net", 0.2) }, 'F'),
            "По ситуации",
            new("Проход с шайбой", "С шайбой", "Вход в зону с контролем шайбы: броски опаснее, но потери на синей линии дают контратаки; нужны владение и скорость",
                new[] { ("hnd", 0.35), ("spd", 0.25), ("pas", 0.2), ("acc", 0.2) }, 'F'), "Смешанно"),
        new("da", "Защитники",
            new("Сзади", "Сзади", "Защитники не подключаются: меньше бросков от синей линии и меньше выходов один в один; нужны домоседы",
                new[] { ("dfn", 0.35), ("blk", 0.25), ("str", 0.2), ("dec", 0.2) }, 'D'),
            "Обычно",
            new("В атаку", "В атаку", "Защитники подключаются и бросают: атака шире, но сзади пусто; нужны быстрые защитники с передачей",
                new[] { ("pas", 0.3), ("spd", 0.25), ("vis", 0.2), ("hnd", 0.15), ("lng", 0.1) }, 'D'), "Обычно"),
        new("sh", "Броски",
            new("Только моменты", "Моменты", "Ищем передачу в опасную зону: бросков меньше, но они лучше; нужны видение и передача",
                new[] { ("pas", 0.3), ("vis", 0.3), ("dec", 0.2), ("fin", 0.2) }, 'A'),
            "Обычно",
            new("Бросать всё", "Всё", "Бросаем при любой возможности, игроки на пятаке добивают: бросков больше, качество ниже; нужны бросок издали и игра у ворот",
                new[] { ("lng", 0.3), ("net", 0.3), ("pwr", 0.2), ("wrk", 0.2) }, 'A'), "Обычно"),
    };

    /// <summary>Эффект системы на матч (в лог-голах, множители — как есть).</summary>
    public sealed class Effect
    {
        /// <summary>Свои голы и голы соперника (лог): система клуба меняет и то, и другое.</summary>
        public double For, Against;
        /// <summary>Качество своих бросков (больше — бросков меньше при тех же голах), доля бросков защитников, дальних бросков.</summary>
        public double Q = 1, DW = 1, Far = 1, Pen = 1, Drain = 1;
        /// <summary>Разбор по рычагам для экрана: (рычаг, свои голы, голы соперника) в лог-единицах.</summary>
        public readonly List<(string key, double f, double a)> Parts = new();
        /// <summary>Контрприёмы соперника, сработавшие в этом матче (для разбора и совета помощника).</summary>
        public readonly List<string> Notes = new();
        public bool IsNeutral => Math.Abs(For) < 1e-9 && Math.Abs(Against) < 1e-9 && Q == 1 && DW == 1 && Far == 1 && Pen == 1 && Drain == 1;
    }

    // ------------------------------------------------------------------ размеры эффектов (дизайн)
    /// <summary>Полюс без учёта игроков: (свои, соперника) в лог-голах.</summary>
    static readonly Dictionary<string, (double f, double a)> BASE = new()
    {
        ["fc-"] = (-0.035, -0.035), ["fc+"] = (0.035, 0.035),
        ["en-"] = (-0.015, -0.015), ["en+"] = (0.02, 0.02),
        ["da-"] = (-0.03, -0.03), ["da+"] = (0.03, 0.03),
        ["sh-"] = (0, 0), ["sh+"] = (0, 0),
    };
    /// <summary>Пригодность состава (в стандартных отклонениях клубов лиги): своим голам +FIT_F, голам соперника −FIT_A за единицу.</summary>
    public static double FIT_F = 0.0045, FIT_A = 0.0035;
    /// <summary>Невыученная схема: свои голы −LEARN_F, соперника +LEARN_A при нулевой выучке (линейно до полной).</summary>
    public const double LEARN_F = 0.015, LEARN_A = 0.01;
    /// <summary>Выучка: за матч в этой схеме и за день (тренировки этапа 8 ускорят), стартовая у новой схемы.</summary>
    public const double LEARN_GAME = 0.06, LEARN_DAY = 0.012, LEARN_NEW = 0.15;

    static string Key(Lever l, int v) => l.Key + (v < 0 ? "-" : "+");
    public static Pole PoleOf(Lever l, int v) => v < 0 ? l.Minus : v > 0 ? l.Plus : null;
    public static string PoleName(Lever l, int v) => v < 0 ? l.Minus.Name : v > 0 ? l.Plus.Name : l.Mid;

    // ------------------------------------------------------------------ пригодность состава

    /// <summary>
    /// Вклад игрока в полюс: взвешенная сумма нужных характеристик минус его средняя по всем — стиль, а не класс
    /// (класс игрока и так в силе клуба; иначе сильным подходило бы всё, а слабым ничего).
    /// </summary>
    static double Score(Func<PlayerData, string, double> attr, PlayerData p, Pole pole) =>
        pole.Fit.Sum(x => x.w * attr(p, x.k)) - Attrs.Skater.Average(a => attr(p, a.Key));

    static bool InGroup(PlayerData p, char g) => g == 'A' || (g == 'D') == p.IsDefense;

    /// <summary>Сырая пригодность состава: средняя по минутам в своей группе (нападающие, защитники или все).</summary>
    static double Raw(GameData d, Lineup l, Func<PlayerData, string, double> attr, Pole pole)
    {
        double s = 0, w = 0;
        void Add(int id, double min)
        {
            if (id == 0 || min <= 0 || !d.PlayerById.TryGetValue(id, out var p) || !InGroup(p, pole.Group)) return;
            s += min * Score(attr, p, pole); w += min;
        }
        for (int i = 0; i < l.F.Count; i++) foreach (var id in l.F[i]) Add(id, i < l.FMin.Length ? l.FMin[i] : 0);
        for (int i = 0; i < l.D.Count; i++) foreach (var id in l.D[i]) Add(id, i < l.DMin.Length ? l.DMin[i] : 0);
        return w > 0 ? s / w : 11;
    }

    /// <summary>Шкала пригодности по лиге: среднее и разброс по клубам (составы на старте карьеры, истинные характеристики без формы).</summary>
    sealed class Ref { public readonly Dictionary<string, (double m, double sd)> P = new(); }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Career, Ref> Refs = new();

    static Ref RefOf(Career c) => Refs.GetValue(c, cc =>
    {
        var r = new Ref();
        var attr = TeamStrength.Attr(cc, cc.DlBase);
        foreach (var lv in Levers)
            foreach (var v in new[] { -1, 1 })
            {
                var pole = PoleOf(lv, v);
                var xs = cc.Data.Teams.Select(t => Raw(cc.Data, cc.Lineups[t.Abbr], attr, pole)).ToList();
                double m = xs.Average(), sd = Math.Sqrt(xs.Average(x => (x - m) * (x - m)));
                r.P[Key(lv, v)] = (m, Math.Max(0.05, sd));
            }
        return r;
    });

    /// <summary>Пригодность состава к полюсу в стандартных отклонениях клубов лиги (0 — средний клуб), в пределах ±2,5.</summary>
    public static double Fit(Career c, Lineup l, Lever lv, int v, Func<PlayerData, string, double> attr = null)
    {
        if (v == 0) return 0;
        attr ??= TeamStrength.Attr(c, c.DlBase);
        var (m, sd) = RefOf(c).P[Key(lv, v)];
        return Math.Clamp((Raw(c.Data, l, attr, PoleOf(lv, v)) - m) / sd, -2.5, 2.5);
    }

    /// <summary>Пригодность отдельного игрока к полюсу (для списков «кто подходит»), в тех же единицах, что у клуба × 3 (игроки разбросаны шире).</summary>
    public static double PlayerFit(Career c, PlayerData p, Lever lv, int v, Func<PlayerData, string, double> attr = null)
    {
        var pole = PoleOf(lv, v);
        if (pole == null || !InGroup(p, pole.Group)) return double.NaN;
        var (m, sd) = RefOf(c).P[Key(lv, v)];
        return (Score(attr ?? TeamStrength.Attr(c, c.DlBase), p, pole) - m) / (3 * sd);
    }

    // ------------------------------------------------------------------ эффект на матч

    /// <summary>Система стороны против системы и состава соперника.</summary>
    public static Effect Of(Career c, MatchSide me, MatchSide opp)
    {
        var e = new Effect();
        var sys = me.Plan ?? me.Lineup.Sys;
        if (sys == null || sys.All(x => x == 0)) return e;
        var attr = me.Attr ?? TeamStrength.Attr(c, c.DlBase);
        var oSys = opp.Plan ?? opp.Lineup.Sys ?? new int[Levers.Length];
        for (int i = 0; i < Levers.Length; i++)
        {
            int v = i < sys.Length ? sys[i] : 0;
            if (v == 0) continue;
            var lv = Levers[i];
            string k = Key(lv, v);
            double fit = Fit(c, me.Lineup, lv, v, attr);
            var (bf, ba) = BASE[k];
            double f = bf + FIT_F * fit, a = ba - FIT_A * fit;
            // контрприёмы соперника
            var (cf, ca) = Counter(c, k, opp, oSys, e.Notes);
            f += cf; a += ca;
            // выучка
            double learn = me.Lineup.Learned(k);
            f -= LEARN_F * (1 - learn); a += LEARN_A * (1 - learn);
            e.For += f; e.Against += a;
            e.Parts.Add((lv.Key, f, a));
            switch (k)
            {
                case "fc+": e.Drain *= 1.04; e.Pen *= 1.06; break;
                case "fc-": e.Drain *= 0.95; e.Pen *= 0.95; break;
                case "en+": e.Q *= 1.03; break;
                case "en-": e.Q *= 0.97; e.Pen *= 1.03; break;
                case "da+": e.DW *= 1.2; break;
                case "da-": e.DW *= 0.85; break;
                case "sh+": e.Q *= 0.93; e.Far *= 1.15; break;
                case "sh-": e.Q *= 1.07; e.Far *= 0.87; break;
            }
        }
        return e;
    }

    /// <summary>
    /// Контрприёмы: как выбранный полюс работает против этого соперника (свои голы, голы соперника):
    ///   прессинг вязнет против защитников, которые быстро отдают шайбу; проход с шайбой упирается в сдержанный форчек,
    ///   а вброс его, наоборот, вскрывает; подключение защитников наказывают быстрые нападающие соперника;
    ///   «бросать всё» хорошо против вратаря, который отдаёт отскоки, и плохо против блокирующих защитников.
    /// </summary>
    static (double f, double a) Counter(Career c, string k, MatchSide opp, int[] oSys, List<string> notes)
    {
        var oattr = opp.Attr ?? TeamStrength.Attr(c, c.DlBase);
        double OppFit(int lever, int v) => Fit(c, opp.Lineup, Levers[lever], v, oattr);
        (double f, double a) Note((double f, double a) x, string good, string bad)
        {
            double net = x.f - x.a;
            if (net > 0.004 && good != null) notes.Add(good);
            else if (net < -0.004 && bad != null) notes.Add(bad);
            return x;
        }
        bool trap = oSys.Length > 0 && oSys[0] < 0;
        switch (k)
        {
            case "fc+": { double mv = OppFit(2, 1); return Note((-0.008 * mv, 0.008 * mv), "их защитники медленно отдают шайбу — прессинг сработает", "их защитники быстро отдают шайбу — прессинг будет вязнуть"); }
            case "en+": return Note((trap ? -0.025 : 0, 0), null, "их сдержанный форчек 1-2-2 закрывает проход с шайбой");
            case "en-": return Note(((trap ? 0.015 : 0) - 0.006 * OppFit(1, -1), 0), "вброс вскрывает их 1-2-2", "у них сильные игроки в углах — вброс отдаёт шайбу");
            case "da+": return Note((0, 0.01 * OppFit(1, 1)), "их нападающие небыстрые — защитникам можно подключаться", "у них быстрые нападающие — за подключение защитников накажут");
            case "sh+":
            {
                double reb = c.Data.PlayerById.TryGetValue(opp.Goalie, out var g) ? oattr(g, "reb") : 11;
                return Note((0.006 * (11 - reb) - 0.006 * OppFit(2, -1), 0), "их вратарь отдаёт отскоки — бросать стоит", "их вратарь держит шайбу, а защитники блокируют — дальние броски пропадут");
            }
            case "sh-": return Note((0.004 * OppFit(2, -1), 0), "их защитники блокируют броски — ищем передачу", null);
        }
        return (0, 0);
    }

    /// <summary>Разбор системы против среднего соперника: по рычагам — полюс, пригодность, выучка, свои голы и голы против (лог).</summary>
    public sealed record LeverView(int Index, int V, double Fit, double Learn, double F, double A);

    public static List<LeverView> Breakdown(Career c, Lineup l, Func<PlayerData, string, double> attr = null)
    {
        var list = new List<LeverView>();
        for (int i = 0; i < Levers.Length; i++)
        {
            int v = l.SysOf(i);
            if (v == 0) { list.Add(new LeverView(i, 0, 0, 1, 0, 0)); continue; }
            var lv = Levers[i]; string k = Key(lv, v);
            double fit = Fit(c, l, lv, v, attr), learn = l.Learned(k);
            var (bf, ba) = BASE[k];
            list.Add(new LeverView(i, v, fit, learn, bf + FIT_F * fit - LEARN_F * (1 - learn), ba - FIT_A * fit + LEARN_A * (1 - learn)));
        }
        return list;
    }

    /// <summary>Применить системы к сторонам матча (в прогнозе и в матче).</summary>
    public static void Prepare(Career c, MatchSide h, MatchSide a)
    {
        h.Sys = Of(c, h, a);
        a.Sys = Of(c, a, h);
        // число бросков в лиге — как в НХЛ: системы перераспределяют броски между клубами, а не добавляют их всем
        double q = LeagueQ(c);
        h.Sys.Q /= q; a.Sys.Q /= q;
    }

    /// <summary>
    /// Средний по лиге множитель «качества броска» от систем (вместе с дальними бросками и бросками защитников — они тоже
    /// снижают отдачу попытки: +15% дальних ≈ −2,4%, +20% бросков защитников ≈ −2%).
    /// </summary>
    static double LeagueQ(Career c)
    {
        double sum = 0; int n = 0;
        foreach (var l in c.Lineups.Values)
        {
            double q = 1;
            for (int i = 0; i < Levers.Length; i++)
            {
                int v = l.SysOf(i);
                if (v == 0) continue;
                q *= Key(Levers[i], v) switch
                {
                    "en+" => 1.03, "en-" => 0.97, "sh+" => 0.93 * Math.Pow(1.15, -0.17), "sh-" => 1.07 * Math.Pow(0.87, -0.17),
                    "da+" => Math.Pow(1.2, -0.11), "da-" => Math.Pow(0.85, -0.11), _ => 1,
                };
            }
            sum += Math.Log(q); n++;
        }
        return n == 0 ? 1 : Math.Exp(sum / n);
    }

    // ------------------------------------------------------------------ выучка и выбор ИИ

    /// <summary>После матча: выучка схемы растёт у обоих клубов.</summary>
    public static void AfterGame(Career c, ScheduledGame g)
    {
        foreach (var t in new[] { g.Home, g.Away }) Grow(c.Lineups[t], LEARN_GAME);
    }

    public static void Daily(Career c)
    {
        foreach (var l in c.Lineups.Values) Grow(l, LEARN_DAY);
    }

    /// <summary>Тренировка системы (Training): выучка сверх обычной.</summary>
    public static void Train(Lineup l, double d) => Grow(l, d);

    static void Grow(Lineup l, double d)
    {
        if (l.Sys == null) return;
        l.Learn ??= new();
        for (int i = 0; i < Levers.Length && i < l.Sys.Length; i++)
        {
            if (l.Sys[i] == 0) continue;
            string k = Key(Levers[i], l.Sys[i]);
            l.Learn[k] = (float)Math.Min(1, l.Learned(k) + d);
        }
    }

    /// <summary>Сменить полюс рычага: новая схема начинает с выученного раньше (не меньше LEARN_NEW).</summary>
    public static void Set(Lineup l, int lever, int v)
    {
        l.Sys ??= new int[Levers.Length];
        if (l.Sys.Length < Levers.Length) { var s = new int[Levers.Length]; l.Sys.CopyTo(s, 0); l.Sys = s; }
        l.Sys[lever] = v;
        if (v != 0)
        {
            l.Learn ??= new();
            string k = Key(Levers[lever], v);
            l.Learn[k] = (float)Math.Max(LEARN_NEW, l.Learned(k));
        }
    }

    /// <summary>Сколько даёт полюс этому составу против среднего соперника (свои − чужие голы, лог), при полной выучке.</summary>
    public static double Value(Career c, Lineup l, int lever, int v)
    {
        if (v == 0) return 0;
        var lv = Levers[lever];
        double fit = Fit(c, l, lv, v);
        var (bf, ba) = BASE[Key(lv, v)];
        return (bf + FIT_F * fit) - (ba - FIT_A * fit);
    }

    /// <summary>
    /// Система по составу глазами тренера: для каждого рычага — полюс с наибольшей выгодой, если она заметна (иначе середина).
    /// Тренер ошибается (тактика) и тянет к любимому форчеку.
    /// </summary>
    public static int[] Suggest(Career c, Lineup l, double min = 0.006, StaffMember who = null, int salt = 0)
    {
        var s = new int[Levers.Length];
        for (int i = 0; i < Levers.Length; i++)
        {
            double Est(int v) => v == 0 ? 0 : Value(c, l, i, v) + (who == null ? 0 : Staff.TacSd(who) / 3 * Staff.Noise(c, who, 900 + i + 10 * salt, v)
                                                   + (i == 0 && who.Press == v ? 0.006 : 0));
            double vm = Est(-1), vp = Est(1);
            if (vp >= vm && vp > min) s[i] = 1; else if (vm > vp && vm > min) s[i] = -1;
        }
        return s;
    }

    // ------------------------------------------------------------------ роли звеньев

    /// <summary>Атака и оборона игрока (ед. шкалы, как в силе клуба): для ролей и подсказок.</summary>
    public static (double off, double def) OffDef(Career c, int id)
    {
        if (!c.Data.PlayerById.TryGetValue(id, out var p)) return (0, 0);
        var attr = TeamStrength.Attr(c, c.DlBase);
        return (TeamStrength.Off.Sum(x => x.w * (attr(p, x.k) - 11)), TeamStrength.Def.Sum(x => x.w * (attr(p, x.k) - 11)));
    }

    static (double off, double def) UnitOD(Career c, int[] ids)
    {
        var v = ids.Where(id => id != 0).Select(id => OffDef(c, id)).ToList();
        return v.Count == 0 ? (0, 0) : (v.Average(x => x.off), v.Average(x => x.def));
    }

    /// <summary>
    /// Роли как у тренеров НХЛ: атакующая — сильнейшая в атаке из первых двух троек (пар), оборонительная — с наибольшим
    /// перевесом обороны над атакой среди остальных (кроме четвёртой тройки); против лучших — да.
    /// </summary>
    public static void DefaultRoles(Career c, Lineup l)
    {
        int[] Pick(List<int[]> units, int maxDef)
        {
            var r = new int[units.Count];
            if (units.Count < 2) return r;
            var od = units.Select(u => UnitOD(c, u)).ToList();
            int att = od[0].off >= od[1].off ? 0 : 1;
            r[att] = 1;
            int def = Enumerable.Range(0, Math.Min(units.Count, maxDef)).Where(i => i != att).OrderByDescending(i => od[i].def - od[i].off).First();
            r[def] = 2;
            return r;
        }
        l.FRole = Pick(l.F, 3); l.DRole = Pick(l.D, 3); l.Matchup = true;
    }

    /// <summary>Старт карьеры: у каждого клуба — система под свой состав, выученная (так клуб и играл), и роли звеньев.</summary>
    public static void Init(Career c)
    {
        foreach (var t in c.Data.Teams)
        {
            var l = c.Lineups[t.Abbr];
            DefaultRoles(c, l);
            l.Rules = t.Abbr == c.UserTeam ? Rules.Default() : Rules.Ai();
            l.Sys = Suggest(c, l, who: Staff.Decider(c, t.Abbr));
            l.Learn = new();
            for (int i = 0; i < Levers.Length; i++) if (l.Sys[i] != 0) l.Learn[Key(Levers[i], l.Sys[i])] = 1;
        }
    }
}
