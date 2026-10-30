namespace HM27.Core;

/// <summary>Запись журнала тренера: свой матч — система, темп, задания и то, что движок насчитал по обеим командам.</summary>
public sealed class JournalEntry
{
    public int Game { get; set; }
    public DateOnly Date { get; set; }
    public string Opp { get; set; }
    public bool Home { get; set; }
    public bool Playoff { get; set; }
    public int[] Sys { get; set; }
    public int Tempo { get; set; }
    /// <summary>Виды заданий на соперника в этом матче (shadow, press, hit, lane).</summary>
    public List<string> Targets { get; set; }
    public int GF { get; set; }
    public int GA { get; set; }
    public string End { get; set; }
    public Dictionary<string, double> Us { get; set; }
    public Dictionary<string, double> Them { get; set; }
    public int Points => GF > GA ? 2 : End != "REG" ? 1 : 0;
    public int SysOf(int lever) => Sys != null && lever < Sys.Length ? Sys[lever] : 0;
}

/// <summary>
/// Журнал «что сработало» (этап «Тренер»): по каждому рычагу — матчи с каждым краем и серединой, очки, моменты и подпись схемы
/// (то, что она меняет в статистике) против остальных матчей. Только факты из движка: цену схемы журнал не знает —
/// выводы делает тренер, а помощник честно говорит, когда матчей мало.
/// </summary>
public static class Journal
{
    /// <summary>Показатель: числитель и знаменатель по матчам (знаменатель 1 — «за матч»).</summary>
    public sealed record Metric(string Name, Func<JournalEntry, double> Num, Func<JournalEntry, double> Den, string Fmt, bool Share = false);

    static double U(JournalEntry e, string k) => e.Us?.GetValueOrDefault(k) ?? 0;
    static double Th(JournalEntry e, string k) => e.Them?.GetValueOrDefault(k) ?? 0;
    static double One(JournalEntry e) => 1;
    static double Zs(Dictionary<string, double> d) => d == null ? 0 : d.GetValueOrDefault("z0") + d.GetValueOrDefault("z1") + d.GetValueOrDefault("z2");

    /// <summary>Подписи рычагов — в порядке Tactics.Levers.</summary>
    public static readonly Dictionary<string, Metric[]> Signs = new()
    {
        ["fc"] = new Metric[] { new("Отборов за матч", e => U(e, "tk"), One, "0.0"), new("Голов после отбора", e => U(e, "rush_g"), One, "0.00"), new("Наших удалений", e => U(e, "pen"), One, "0.0") },
        ["en"] = new Metric[] { new("Наших потерь", e => U(e, "gv"), One, "0.0"), new("Ожидаемых голов на бросок в створ", e => U(e, "xg"), e => U(e, "sog"), "0.000"), new("Голов соперника после отбора", e => Th(e, "rush_g"), One, "0.00") },
        ["da"] = new Metric[] { new("Бросков наших защитников", e => U(e, "datt"), One, "0.0"), new("Бросков соперника с хода", e => Th(e, "rush_att"), One, "0.0") },
        ["sh"] = new Metric[] { new("Попыток бросков", e => U(e, "att"), One, "0.0"), new("Бросков издали", e => U(e, "z2"), e => Zs(e.Us), "0%", true), new("Ожидаемых голов на бросок в створ", e => U(e, "xg"), e => U(e, "sog"), "0.000") },
        ["agg"] = new Metric[] { new("Силовых", e => U(e, "hit"), One, "0.0"), new("Наших удалений", e => U(e, "pen"), One, "0.0"), new("Потерь соперника", e => Th(e, "gv"), One, "0.0") },
        ["dz"] = new Metric[] { new("Бросков соперника", e => Th(e, "att"), One, "0.0"), new("Его бросков у ворот", e => Th(e, "z0"), e => Zs(e.Them), "0%", true), new("Наших блоков", e => U(e, "blk"), One, "0.0") },
        ["bo"] = new Metric[] { new("Наших потерь", e => U(e, "gv"), One, "0.0"), new("Вбрасываний в своей зоне", e => U(e, "fo_dz"), e => U(e, "fo"), "0%", true), new("Голов после отбора", e => U(e, "rush_g"), One, "0.00") },
        ["pp"] = new Metric[] { new("Бросков за 2 минуты большинства", e => 120 * U(e, "pp_att"), e => U(e, "pp_sec"), "0.0"), new("Из них — защитников", e => U(e, "pp_datt"), e => U(e, "pp_att"), "0%", true), new("Голов в большинстве за 2 минуты", e => 120 * U(e, "pp_g"), e => U(e, "pp_sec"), "0.00") },
        ["pk"] = new Metric[] { new("Своих бросков в меньшинстве", e => U(e, "pk_att"), One, "0.0"), new("Блоков в меньшинстве", e => U(e, "pk_blk"), One, "0.0"), new("Пропущено за 2 минуты меньшинства", e => 120 * Th(e, "pp_g"), e => U(e, "pk_sec"), "0.00") },
    };

    public sealed record Line(Metric M, double Val, double Rest);
    public sealed record Row(int V, int N, int Pts, int Max, double GDiff, double XgDiff, List<Line> Sign);

    static double Agg(IEnumerable<JournalEntry> es, Metric m)
    {
        double n = 0, d = 0;
        foreach (var e in es) { n += m.Num(e); d += m.Den(e); }
        return d <= 0 ? double.NaN : n / d;
    }

    public static List<JournalEntry> Of(Career c) => (c.Journal ?? new()).Where(e => e.Us != null).ToList();

    /// <summary>Строки рычага: каждый край, с которым играли, против всех остальных матчей.</summary>
    public static List<Row> Rows(Career c, int lever)
    {
        var all = Of(c);
        var key = Tactics.Levers[lever].Key;
        var list = new List<Row>();
        foreach (int v in new[] { -1, 0, 1 })
        {
            var mine = all.Where(e => e.SysOf(lever) == v).ToList();
            if (mine.Count == 0) continue;
            var rest = all.Where(e => e.SysOf(lever) != v).ToList();
            var sign = Signs[key].Select(m => new Line(m, Agg(mine, m), rest.Count == 0 ? double.NaN : Agg(rest, m))).ToList();
            list.Add(new Row(v, mine.Count, mine.Sum(e => e.Points), 2 * mine.Count,
                mine.Average(e => e.GF - e.GA), mine.Average(e => U(e, "xg") - Th(e, "xg")), sign));
        }
        return list;
    }

    /// <summary>
    /// Что видит помощник в журнале по рычагу — словами. Разница в моментах (ожидаемые голы) против остальных матчей и её шум:
    /// за матч разница xG гуляет примерно на ±1,3, поэтому за 10 матчей различить можно только большое.
    /// </summary>
    public static string Read(Career c, int lever)
    {
        var all = Of(c);
        var rows = Rows(c, lever);
        var lv = Tactics.Levers[lever];
        if (rows.Count == 0) return "Матчей ещё не было.";
        if (rows.Count == 1) return $"Все матчи сыграны с одним вариантом — «{Tactics.PoleName(lv, rows[0].V).ToLowerInvariant()}». Сравнить не с чем.";
        var best = rows.Where(r => r.N >= 5).ToList();
        if (best.Count < 2) return "Матчей с другими вариантами пока мало — по одному-двум матчам ничего не понять.";
        var parts = new List<string>();
        foreach (var r in best)
        {
            var rest = all.Where(e => e.SysOf(lever) != r.V).ToList();
            double xr = rest.Average(e => U(e, "xg") - Th(e, "xg"));
            double d = r.XgDiff - xr, se = 1.3 * Math.Sqrt(1.0 / r.N + 1.0 / rest.Count);
            string name = Tactics.PoleName(lv, r.V).ToLowerInvariant();
            if (Math.Abs(d) < se) parts.Add($"с вариантом «{name}» по моментам разницы не видно");
            else parts.Add($"с вариантом «{name}» моментов {(d > 0 ? "больше" : "меньше")} в нашу пользу{(Math.Abs(d) < 2 * se ? ", но это может быть и случайность" : "")}");
        }
        return Cap(string.Join("; ", parts)) + ".";
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>После своего матча: запись в журнал (движок по сменам; у старого движка счётчиков нет).</summary>
    public static void Record(Career c, ScheduledGame g, MatchSide us, MatchResult r)
    {
        bool home = g.Home == c.UserTeam;
        var st = home ? r.HomeStats : r.AwayStats;
        if (st == null) return;
        c.Journal ??= new();
        c.Journal.RemoveAll(e => e.Game == g.Id);
        c.Journal.Add(new JournalEntry
        {
            Game = g.Id, Date = g.Date, Opp = home ? g.Away : g.Home, Home = home, Playoff = g.Playoff,
            Sys = (int[])(us.StartSys ?? us.Plan ?? us.Lineup.Sys ?? new int[Tactics.Levers.Length]).Clone(), Tempo = us.StartSys != null ? us.StartTempo : us.Tempo,
            Targets = us.Targets?.Values.ToList(),
            GF = home ? r.HomeGoals : r.AwayGoals, GA = home ? r.AwayGoals : r.HomeGoals, End = r.End,
            Us = Round(st), Them = Round(home ? r.AwayStats : r.HomeStats),
        });
    }

    static Dictionary<string, double> Round(Dictionary<string, double> d) => d?.ToDictionary(x => x.Key, x => Math.Round(x.Value, 3));
}
