namespace HM27.Core;

/// <summary>Журналист: имя, издание, склад (аналитик, инсайдер, эксперт-ветеран, местный репортёр, таблоид) и его манера.</summary>
public sealed record Journalist(string Kind, string Name, string Outlet, string Bio, string Color);

/// <summary>Пост болельщика: ник, текст, настроение (−1 злой … +1 счастливый), клуб.</summary>
/// <summary>Настроение трибун после своего матча — для графика на странице «Трибуна».</summary>
public sealed class FanPoint
{
    public DateOnly Date { get; set; }
    public int Game { get; set; }
    public float Mood { get; set; }
}

public sealed class FanPost
{
    public DateOnly Date { get; set; }
    public string Team { get; set; }
    public string Handle { get; set; }
    public string Text { get; set; }
    public int Mood { get; set; }
    public int Likes { get; set; }
}

/// <summary>
/// Голоса лиги (как в FM): у новостей есть авторы со своей манерой, у клуба — болельщики. Журналисты вымышленные и постоянные
/// для карьеры (сид), местный репортёр пишет о клубе игрока. Болельщики реагируют на матчи по настроению трибун
/// (Career.FanMood: результат против ожиданий, серии, место против прогноза прессы). На игру ничто из этого не влияет.
/// </summary>
public static class Voices
{
    public const string STATS = "stats", INSIDER = "insider", PUNDIT = "pundit", BEAT = "beat", TABLOID = "tabloid", WIRE = "wire";

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Career, List<Journalist>> Cache = new();

    /// <summary>Пресса карьеры: по одному голосу каждого склада (местный репортёр — о клубе игрока).</summary>
    public static List<Journalist> Of(Career c) => Cache.GetValue(c, cc =>
    {
        var r = new Random(unchecked(cc.Seed * 9187 + 3));
        string Pick(params string[] xs) => xs[r.Next(xs.Length)];
        var team = cc.Team(cc.UserTeam);
        return new List<Journalist>
        {
            new(STATS, Pick("Nate Lindgren", "Owen Pratt", "Sam Kowal", "Eli Marchand"), "«Лёд и цифры»",
                "аналитик: верит ожидаемым голам больше, чем счёту на табло", "35C985"),
            new(INSIDER, Pick("Frank Sorrento", "Bill Dreger", "Mitch Lavallee", "Darren Holt"), "«Хоккейный инсайдер»",
                "инсайдер: знает, что говорят в кабинетах и раздевалках", "FFB627"),
            new(PUNDIT, Pick("Glen Sanders", "Ray Dupuis", "Mike Kovalenko", "Doug Whitney"), "студия «Овертайм»",
                $"эксперт, бывший защитник, {Pick("912", "1040", "867", "1003")} матчей в НХЛ: судит по характеру и не любит цифры", "9DB4FF"),
            new(BEAT, Pick("Jenna Ruiz", "Katie Morrow", "Alex Brandt", "Chris Nolan"), $"«{team.City} Herald»",
                $"пишет о «{team.Name}» каждый день: ездит с командой, знает раздевалку", team.Colors[0]),
            new(TABLOID, Pick("Vic Malone", "Tony Gerber", "Ricky Sloan"), "«Грязная шайба»",
                "таблоид: громкие заголовки, любой спад — кризис, любая серия — сенсация", "FF5A5A"),
            new(WIRE, "Лента", "НХЛ", "сводка без мнений: только результаты", "8C99AC"),
        };
    });

    public static Journalist Get(Career c, string kind) => Of(c).First(j => j.Kind == kind);

    /// <summary>Подпись: «Имя, издание».</summary>
    public static string By(Journalist j) => j.Kind == WIRE ? "Лента НХЛ" : $"{j.Name}, {j.Outlet}";

    /// <summary>Журналист по подписи письма (для аватара в ленте).</summary>
    public static Journalist ByLine(Career c, string from) => from == null ? null : Of(c).FirstOrDefault(j => By(j) == from);

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : parts.Length == 1 ? parts[0][..1] : $"{parts[0][0]}{parts[^1][0]}";
    }

    // ================================================================ болельщики

    /// <summary>Настроение трибун 0–100: 50 — спокойно; ниже 30 — ропот, выше 70 — эйфория.</summary>
    public static string MoodWord(float m) => m >= 80 ? "Эйфория" : m >= 65 ? "Довольны" : m >= 45 ? "Спокойны" : m >= 30 ? "Ворчат" : "В ярости";

    /// <summary>
    /// После своего матча: настроение трибун (результат против прогноза, серия, место против ожиданий прессы) и 2–4 поста.
    /// Сдвиги небольшие и тянутся к 50: один матч ничего не решает, серия — решает.
    /// </summary>
    public static void AfterGame(Career c, ScheduledGame g)
    {
        string me = c.UserTeam;
        bool home = g.Home == me;
        var r = g.Result;
        int gf = home ? r.HomeGoals : r.AwayGoals, ga = home ? r.AwayGoals : r.HomeGoals;
        bool win = gf > ga;
        double pre = home ? r.PHome : 1 - r.PHome;
        double d = win ? 3 + 6 * (1 - pre) : r.End != "REG" ? 1 - 2 * pre : -3 - 6 * pre;
        if (ga - gf >= 4) d -= 3;
        if (gf - ga >= 4) d += 2;
        // место против ожиданий прессы (раз в несколько матчей это заметно)
        if (!g.Playoff && c.Table.TryGetValue(me, out var row) && row.Gp >= 10)
        {
            var t = c.Team(me);
            int div = c.Sorted(x => x.Division == t.Division).FindIndex(x => x.Team == me) + 1;
            int exp = Pundits.DivisionPick(c, me);
            if (exp > 0) d += 0.4 * Math.Clamp(exp - div, -3, 3);
        }
        if (g.Playoff) d *= 1.6;
        c.FanMood = (float)Math.Clamp(c.FanMood + d + (50 - c.FanMood) * 0.04, 3, 99);
        c.FanTrend.Add(new FanPoint { Date = c.Today, Game = g.Id, Mood = c.FanMood });

        var rng = new Random(unchecked(c.Seed * 7577 + g.Id * 13));
        var team = c.Team(me);
        string nick = team.Name.Replace(team.City, "").Trim();
        string opp = c.Team(home ? g.Away : g.Home).City;
        var goals = r.Goals.Where(e => e.Team == me && e.Kind != "so").ToList();
        var top = goals.SelectMany(e => new[] { e.Scorer, e.A1, e.A2 }).Where(x => x != 0).GroupBy(x => x).OrderByDescending(x => x.Count()).FirstOrDefault();
        string hero = top != null ? c.Short(top.Key) : null;
        int myG = home ? r.HomeGoalie : r.AwayGoalie;
        string goalie = c.Short(myG);
        string score = $"{gf}:{ga}";
        var lines = new List<(string text, int mood)>();
        float mood = c.FanMood;
        if (win)
        {
            lines.Add((Pick(rng, $"{score}! Вот это я понимаю хоккей", $"Ещё два очка. Так держать, {nick}!", $"Кто сомневался? {score}, едем дальше", "Красиво. Сегодня всё получилось"), 1));
            if (hero != null) lines.Add((Pick(rng, $"{hero} сегодня просто космос", $"{hero} — лучший в лиге, не спорьте", $"Дайте {hero} новый контракт. Срочно", $"{hero}, ты легенда"), 1));
            if (ga == 0) lines.Add((Pick(rng, $"{goalie} — стена. Ноль!", $"Сухарь {goalie}. Кто-то ещё хочет обсудить вратаря?"), 1));
            if (pre < 0.4) lines.Add((Pick(rng, $"Никто не верил, а мы взяли. Эксперты, ау?", $"Обыграли {opp}. Где те, кто ставил против?"), 1));
            if (mood < 40) lines.Add((Pick(rng, "Одна победа ничего не меняет. Посмотрим, что дальше", "Ну наконец-то. Не расслабляемся"), 0));
        }
        else
        {
            bool blow = ga - gf >= 3;
            lines.Add((blow ? Pick(rng, $"{score}… выключаю телевизор", "Это был позор. Без вариантов", $"Как можно так играть с {opp}?", "Я за что билет покупал?")
                            : r.End != "REG" ? Pick(rng, "Очко лучше, чем ничего. Но обидно", "Почти. Опять почти", "Овертаймы нас когда-нибудь добьют")
                            : Pick(rng, $"Проиграли {opp}. Бывает, но неприятно", "Не наш вечер. Следующий матч — отвечаем", "Опять не хватило совсем чуть-чуть"), blow ? -1 : 0));
            if (blow && mood < 40) lines.Add((Pick(rng, "Тренер, что происходит? Где система?", "Пора что-то менять. Звенья, вратарь, хоть что-нибудь", "Сколько можно терпеть это?"), -1));
            if (goalie != "—" && ga >= 5) lines.Add((Pick(rng, $"{goalie} сегодня пропускал всё подряд", $"Может, пора дать шанс второму вратарю?"), -1));
            if (mood >= 60) lines.Add((Pick(rng, "Спокойно, сезон длинный. Мы всё ещё хороши", "Одно поражение. Не паникуем"), 0));
        }
        var row2 = c.Table.GetValueOrDefault(me);
        if (row2 != null && row2.Streak.Length > 1 && int.TryParse(row2.Streak[1..].TrimStart('О'), out var sn) && sn >= 4)
            lines.Add((row2.Streak[0] == 'В' ? Pick(rng, $"{sn} побед подряд! Кто нас остановит?", $"Серия {sn}. Я уже покупаю билеты на плей-офф") : Pick(rng, $"{sn} поражений подряд. Это уже не случайность", $"Серия {sn}… руководство, вы это видите?"), row2.Streak[0] == 'В' ? 1 : -1));
        int n = Math.Min(lines.Count, 2 + rng.Next(2));
        foreach (var (text, m) in lines.Take(n))
            c.Fans.Add(new FanPost { Date = c.Today, Team = me, Handle = Handle(rng, nick, team.City), Text = text, Mood = m, Likes = 5 + rng.Next(m == 0 ? 60 : 400) });
        if (c.Fans.Count > 400) c.Fans.RemoveRange(0, c.Fans.Count - 400);
    }

    static string Pick(Random r, params string[] xs) => xs[r.Next(xs.Length)];

    static string Handle(Random r, string nick, string city)
    {
        string n = new string(nick.ToLowerInvariant().Where(char.IsLetter).ToArray());
        string ci = new string(city.ToLowerInvariant().Where(char.IsLetter).ToArray());
        return "@" + Pick(r, $"{n}_forever", $"{ci}_puck", $"{n}fan{r.Next(10, 99)}", $"rink_rat_{r.Next(100, 999)}", $"{ci}_icehead", $"{n}_nation", $"old_{n}_{r.Next(70, 99)}", $"goalhorn_{ci}", $"{n}_ultra", $"section{r.Next(101, 330)}");
    }
}
