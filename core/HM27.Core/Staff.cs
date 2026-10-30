namespace HM27.Core;

/// <summary>
/// Тренер (этап «Тренер», модель для своего штаба и для главных тренеров ИИ). Характеристики 1–20:
///   Tac — тактика (насколько верно он оценивает выгоду схем, темпа, снятия вратаря),
///   Judge — оценка игроков (насколько точно видит силу, от этого — звенья и вариант состава),
///   Man — работа с людьми, Mot — мотивация (разговоры и перерывы — этап «Персонал»),
///   Flex — гибкость (готовит ли план под соперника, меняет ли систему, отвечает ли в серии).
/// Стиль: Press — любимый форчек (−1 сдержанный, +1 прессинг), Risk — осторожный (−2) или рисковый (+2): темп и снятие вратаря,
/// Youth — ставка на ветеранов (−1) или молодых (+1). Стиль сдвигает советы, а не только ошибка: у каждого свои пристрастия.
/// </summary>
public sealed class StaffMember
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Team { get; set; }
    /// <summary>head — главный тренер клуба ИИ, assistant — помощник тренера-игрока.</summary>
    public string Role { get; set; }
    public int Age { get; set; }
    public int Tac { get; set; }
    public int Judge { get; set; }
    public int Man { get; set; }
    public int Mot { get; set; }
    public int Flex { get; set; }
    public int Press { get; set; }
    public int Risk { get; set; }
    public int Youth { get; set; }
}

public static class Staff
{
    static readonly string[] First = { "Mike", "Dan", "Jeff", "Paul", "Rick", "Todd", "Brian", "Kevin", "Mark", "Scott", "Derek", "Ryan", "Jason", "Craig", "Dave",
        "Andrei", "Igor", "Sergei", "Lars", "Mikko", "Jari", "Petr", "Tomas", "Marc", "Luc", "Jean", "Bob", "Pete", "Chris", "Glen" };
    static readonly string[] Last = { "Hartley", "Bowman", "Sutherland", "McKinnon", "Dubois", "Lindqvist", "Kowalski", "Rautio", "Novak", "Brennan", "Gallant",
        "Murray", "Keller", "Lavoie", "Sorensen", "Pelletier", "Grant", "Doyle", "Whitfield", "Karlsson", "Volkov", "Tikhonov", "Hollis", "Ruff", "Carlyle",
        "Wright", "Holt", "Beaumont", "Sheppard", "Larionov", "Pronger", "Strand", "Mattila", "Fischer", "Ostrowski" };

    /// <summary>Штаб: главные тренеры клубов ИИ и помощник своего клуба; создаётся при старте (и для старых сохранений — при загрузке).</summary>
    public static List<StaffMember> Of(Career c)
    {
        if (c.Staff != null && c.Staff.Count > 0) return c.Staff;
        c.Staff = new();
        var rng = new Random(unchecked(c.Seed * 48271 + 17));
        int Attr(double m, double sd) => (int)Math.Clamp(Math.Round(m + sd * N(rng)), 4, 19);
        int id = 1;
        var used = new HashSet<string>();
        string Name()
        {
            for (int k = 0; k < 50; k++)
            {
                string n = First[rng.Next(First.Length)] + " " + Last[rng.Next(Last.Length)];
                if (used.Add(n)) return n;
            }
            return "Coach " + id;
        }
        foreach (var t in c.Data.Teams)
        {
            bool user = t.Abbr == c.UserTeam;
            c.Staff.Add(new StaffMember
            {
                Id = id++, Name = Name(), Team = t.Abbr, Role = user ? "assistant" : "head", Age = rng.Next(38, 66),
                // главные тренеры НХЛ — в среднем сильнее помощников
                Tac = Attr(user ? 11 : 12.5, 2.8), Judge = Attr(user ? 11 : 12.5, 2.8), Man = Attr(11.5, 3), Mot = Attr(11.5, 3), Flex = Attr(11, 3.2),
                Press = rng.Next(3) - 1, Risk = Math.Clamp((int)Math.Round(1.1 * N(rng)), -2, 2), Youth = rng.Next(3) - 1,
            });
        }
        return c.Staff;
    }

    /// <summary>Кто решает за клуб: у ИИ — главный тренер, у клуба игрока — помощник (советует).</summary>
    public static StaffMember Decider(Career c, string team) => Of(c).FirstOrDefault(s => s.Team == team && (s.Role == "head" || s.Role == "assistant"));

    public static StaffMember Assistant(Career c) => Of(c).FirstOrDefault(s => s.Team == c.UserTeam && s.Role == "assistant");

    static double N(Random r) { double u1 = 1 - r.NextDouble(), u2 = r.NextDouble(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }

    /// <summary>Детерминированный «шум» мнения тренера: одно и то же про одно и то же — всегда одинаково.</summary>
    public static double Noise(Career c, StaffMember s, long a, long b = 0)
    {
        var r = new Random(unchecked((int)(c.Seed * 2_654_435_761L ^ s.Id * 97_861L ^ a * 31L ^ b * 1_000_003L)));
        return N(r);
    }

    // ------------------------------------------------------------------ взгляд на игроков

    /// <summary>Ошибка оценки игрока (ед. атаки/обороны, как у силы клуба): оценка 8 — около 0,6, 16 — 0,2.</summary>
    public static double JudgeSd(StaffMember s) => 0.5 * (20 - s.Judge) / 10.0;

    /// <summary>
    /// Атака и оборона игрока глазами тренера: истинные (без формы) + его ошибка (постоянная, своя для каждого игрока)
    /// + пристрастие к молодым или ветеранам + усталость и половина формы (их тренер видит).
    /// </summary>
    public static (double off, double def) View(Career c, StaffMember s, PlayerData p)
    {
        var (off, def) = Tactics.OffDef(c, p.Id);
        if (s == null) return (off, def);
        double sd = JudgeSd(s);
        off += sd * Noise(c, s, p.Id, 1); def += sd * Noise(c, s, p.Id, 2);
        int age = p.Age(c.Today) ?? 27;
        double bias = age <= 23 ? 0.25 * s.Youth : age >= 32 ? -0.15 * s.Youth : 0;
        if (c.Players.TryGetValue(p.Id, out var st))
        {
            double lvl = Fatigue.Penalty(st, p.IsGoalie) + 0.5 * st.Form * PlayerModel.FORM_U;
            off += lvl; def += lvl;
        }
        return (off + bias, def + bias);
    }

    /// <summary>Характеристики глазами тренера (для пригодности к схемам и указаниям): истинные без формы + его ошибка на каждую.</summary>
    public static Func<PlayerData, string, double> AttrView(Career c, StaffMember s)
    {
        var t = TeamStrength.Attr(c, c.DlBase);
        if (s == null) return t;
        double sd = 1.5 * JudgeSd(s);
        return (p, k) => t(p, k) + sd * Noise(c, s, p.Id, 1000 + KeyHash(k));
    }

    static int KeyHash(string k) { int h = 17; foreach (var ch in k) h = unchecked(h * 31 + ch); return h & 0xFFFF; }

    // ------------------------------------------------------------------ взгляд на тактику

    /// <summary>Ошибка оценки выгоды решения (голов за матч): тактика 8 — около 0,036, 16 — 0,012.</summary>
    public static double TacSd(StaffMember s) => s == null ? 0 : 0.03 * (20 - s.Tac) / 10.0;

    /// <summary>Оценка выгоды тренером: истинная + ошибка (своя для этого матча и решения).</summary>
    public static double Estimate(Career c, StaffMember s, double truth, long game, long item) =>
        s == null ? truth : truth + TacSd(s) * Noise(c, s, game + 7, item + 11);

    /// <summary>Насколько тренер уверен в совете: оценка против своей ошибки.</summary>
    public static string Confidence(StaffMember s, double est)
    {
        double z = Math.Abs(est) / Math.Max(0.004, TacSd(s));
        return z >= 2 ? "уверен" : z >= 1 ? "скорее да" : "сомневается";
    }

    public static string StyleText(StaffMember s)
    {
        var parts = new List<string>();
        parts.Add(s.Press > 0 ? "любит прессинг" : s.Press < 0 ? "любит сдержанный форчек" : "без любимой схемы");
        parts.Add(s.Risk >= 2 ? "рисковый" : s.Risk == 1 ? "скорее рисковый" : s.Risk == -1 ? "скорее осторожный" : s.Risk <= -2 ? "осторожный" : "взвешенный");
        if (s.Youth != 0) parts.Add(s.Youth > 0 ? "доверяет молодым" : "доверяет ветеранам");
        return string.Join(", ", parts);
    }

    public static string Level(int v) => v >= 17 ? "выдающаяся" : v >= 14 ? "сильная" : v >= 11 ? "средняя" : v >= 8 ? "слабая" : "очень слабая";
}
