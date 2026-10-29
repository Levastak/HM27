namespace HM27.Core;

/// <summary>
/// Как клуб и матч видят журналисты: статус клуба («Элита», «Перестройка»…) вместо мест в рейтинге силы
/// и короткий прогноз на матч вместо процентов. Цифры движка остаются внутри; игроку — слова, как в FM.
/// </summary>
public static class Pundits
{
    /// <summary>Статус клуба по месту в лиге по силе и возрасту ядра (8 лучших полевых).</summary>
    public static (string label, string note) Status(int rank, double coreAge) => Label(Band(rank), coreAge);

    /// <summary>Ступень статуса по месту: 0 — элита (1–4), 1 — претендент (5–10), 2 — середина (11–20), 3 — низ (21+).</summary>
    public static int Band(int rank) => rank <= 4 ? 0 : rank <= 10 ? 1 : rank <= 20 ? 2 : 3;

    public static (string label, string note) Label(int band, double coreAge) => band switch
    {
        0 => ("Элита", "Главные претенденты на Кубок Стэнли"),
        1 => ("Претендент", "Сильный состав, рассчитывает на глубокий плей-офф"),
        2 => coreAge <= 25.5 ? ("На подъёме", "Молодое ядро, борется за плей-офф") : ("Борьба за плей-офф", "Плей-офф решится в последних матчах"),
        _ => coreAge <= 25.5 ? ("Перестройка", "Ставка на молодёжь, результат — потом") : ("Аутсайдер", "Состав слабее большинства соперников"),
    };

    /// <summary>
    /// Пресса меняет мнение не каждый день: ступень пересматривается раз в неделю и только с запасом —
    /// подняться можно, если место на 2 лучше границы, опуститься — если на 3 хуже (иначе статус «мигает» после пары поражений).
    /// </summary>
    static int Revise(int band, int rank)
    {
        for (int k = 0; k < 3; k++)
        {
            int up = band switch { 1 => 2, 2 => 8, 3 => 18, _ => 0 }, down = band switch { 0 => 7, 1 => 13, 2 => 23, _ => 99 };
            if (rank <= up) band--; else if (rank >= down) band++; else break;
        }
        return Math.Clamp(band, 0, 3);
    }

    /// <summary>Прогноз прессы до сезона: место в дивизионе по составу (данные базы).</summary>
    public static Dictionary<string, int> DivisionPick(GameData d)
    {
        var est = d.Teams.ToDictionary(t => t.Abbr, t => TeamStrength.Estimate(d, t));
        var res = new Dictionary<string, int>();
        foreach (var div in d.Teams.GroupBy(t => t.Division))
        {
            int i = 0;
            foreach (var t in div.OrderByDescending(t => est[t.Abbr].att - est[t.Abbr].dfn)) res[t.Abbr] = ++i;
        }
        return res;
    }

    /// <summary>Прогноз прессы в карьере: фиксируется в начале сезона и не меняется до следующего.</summary>
    public static int DivisionPick(Career c, string abbr)
    {
        if (c.PressPickSeason != c.Season || c.PressPick.Count == 0) { c.PressPick = DivisionPick(c.Data); c.PressPickSeason = c.Season; }
        return c.PressPick.TryGetValue(abbr, out var r) ? r : 0;
    }

    /// <summary>Возраст ядра: средний возраст 8 лучших полевых по КА из данных.</summary>
    public static double CoreAge(GameData d, string abbr, DateOnly on)
    {
        var a = d.Roster(abbr).Where(p => !p.IsGoalie).OrderByDescending(p => p.CaEst).Take(8).Select(p => (double)(p.Age(on) ?? 27)).ToList();
        return a.Count == 0 ? 27 : a.Average();
    }

    /// <summary>
    /// Взгляд прессы на клубы в карьере. Журналисты не знают истинных уровней игроков: до сезона судят по составу
    /// (данные базы — то же, что на экране выбора клуба), по ходу сезона всё больше по таблице:
    /// вес таблицы gp/(gp+40) — после 40 матчей половина, к концу сезона ~2/3; статус пересматривается раз в неделю с запасом.
    /// </summary>
    public sealed record View(int All, int Att, int Dfn, string Label, string Note);

    static Career _vc; static DateOnly _vd; static int _vg = -1; static Dictionary<string, View> _vv;

    public static Dictionary<string, View> ViewAll(Career c)
    {
        int games = c.Table.Values.Sum(r => r.Gp);
        if (ReferenceEquals(_vc, c) && _vd == c.Today && _vg == games && _vv != null) return _vv;
        var d = c.Data;
        var est = d.Teams.ToDictionary(t => t.Abbr, t => TeamStrength.Estimate(d, t));
        double Z(IEnumerable<double> all, double x) { var l = all.ToList(); double m = l.Average(), sd = Math.Sqrt(l.Average(v => (v - m) * (v - m))); return sd < 1e-9 ? 0 : (x - m) / sd; }
        StandingRow R(string a) => c.Table.TryGetValue(a, out var r) ? r : new StandingRow { Team = a };
        double W(string a) { int gp = R(a).Gp; return gp / (gp + 40.0); }
        double Pts(string a) { var r = R(a); return r.Gp == 0 ? 0 : (2.0 * r.W + r.Otl) / (2.0 * r.Gp); }
        double Gf(string a) { var r = R(a); return r.Gp == 0 ? 0 : (double)r.Gf / r.Gp; }
        double Ga(string a) { var r = R(a); return r.Gp == 0 ? 0 : (double)r.Ga / r.Gp; }
        var abbrs = d.Teams.Select(t => t.Abbr).ToList();
        double Mix(string a, Func<string, double> e, Func<string, double> table) =>
            (1 - W(a)) * Z(abbrs.Select(e), e(a)) + W(a) * Z(abbrs.Select(table), table(a));
        var all = abbrs.ToDictionary(a => a, a => Mix(a, x => est[x].att - est[x].dfn, Pts));
        var att = abbrs.ToDictionary(a => a, a => Mix(a, x => est[x].att, Gf));
        var dfn = abbrs.ToDictionary(a => a, a => Mix(a, x => -est[x].dfn, x => -Ga(x)));
        int Rank(Dictionary<string, double> m, string a) => m.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList().IndexOf(a) + 1;
        // ступени статуса: в начале сезона — по составу, дальше пересмотр раз в неделю с запасом
        bool fresh = c.PressBandSeason != c.Season || c.PressBand.Count == 0;
        if (fresh || c.Today.DayNumber - c.PressBandDate.DayNumber >= 7)
        {
            foreach (var a in abbrs)
            {
                int r = Rank(all, a);
                c.PressBand[a] = fresh || !c.PressBand.TryGetValue(a, out var b0) ? Band(r) : Revise(b0, r);
            }
            c.PressBandSeason = c.Season; c.PressBandDate = c.Today;
        }
        _vv = abbrs.ToDictionary(a => a, a =>
        {
            int r = Rank(all, a); var (label, note) = Label(c.PressBand.GetValueOrDefault(a, Band(r)), CoreAge(d, a, c.Today));
            return new View(r, Rank(att, a), Rank(dfn, a), label, note);
        });
        _vc = c; _vd = c.Today; _vg = games;
        return _vv;
    }

    /// <summary>Статус клуба в карьере — глазами прессы (состав по данным + таблица).</summary>
    public static (string label, string note) Status(Career c, string abbr) { var v = ViewAll(c)[abbr]; return (v.Label, v.Note); }

    /// <summary>Статус до начала карьеры (выбор клуба): сила по данным базы.</summary>
    public static Dictionary<string, (string label, string note)> StatusAll(GameData d, DateOnly on)
    {
        var est = d.Teams.ToDictionary(t => t.Abbr, t => TeamStrength.Estimate(d, t));
        var order = d.Teams.OrderByDescending(t => est[t.Abbr].att - est[t.Abbr].dfn).Select(t => t.Abbr).ToList();
        return d.Teams.ToDictionary(t => t.Abbr, t => Status(order.IndexOf(t.Abbr) + 1, CoreAge(d, t.Abbr, on)));
    }

    /// <summary>Прогноз журналистов на матч: одна фраза. Вариант формулировки — от номера матча (всегда одинаковый).</summary>
    public static string Forecast(GameData d, ScheduledGame g, MatchOdds o)
    {
        var home = d.TeamByAbbr[g.Home]; var away = d.TeamByAbbr[g.Away];
        bool homeFav = o.PHome >= 0.5;
        double q = Math.Max(o.PHome, 1 - o.PHome);
        string fav = (homeFav ? home : away).City, dog = (homeFav ? away : home).City;
        int v = (int)(Math.Abs(g.Id) % 3);
        if (q < 0.54)
            return v switch { 0 => "Журналисты ждут равного матча", 1 => "Пресса не берётся назвать фаворита", _ => "По мнению экспертов — шансы примерно равны" };
        if (q < 0.62)
            return v switch { 0 => $"Журналисты отдают небольшое преимущество «{fav}»", 1 => $"Эксперты чуть больше верят в «{fav}»", _ => $"Лёгкий перевес — у «{fav}», считает пресса" };
        if (q < 0.70)
            return v switch { 0 => $"«{fav}» — фаворит, считают журналисты", 1 => $"Пресса ставит на «{fav}»", _ => $"Эксперты ждут победы «{fav}»" };
        return v switch { 0 => $"«{fav}» — явный фаворит, от «{dog}» ждут сенсации", 1 => $"Журналисты не видят у «{dog}» больших шансов", _ => $"Пресса уверена в «{fav}»" };
    }
}
