namespace HM27.Core;

public sealed class Message
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string From { get; set; }        // кто пишет: «Владелец клуба», «Помощник тренера», «Аналитический отдел», «Лига»
    public string Subject { get; set; }
    public string Body { get; set; }
    public string Kind { get; set; }        // board | match | week | month | league
    public bool Read { get; set; }
    /// <summary>Ссылка на объект: id матча, id игрока, «table» и т. п.</summary>
    public string Link { get; set; }
}

/// <summary>
/// Входящие, как в FM: письма о событиях карьеры. Тексты собираются из фактов сезона —
/// никаких выдуманных эффектов на результат, только то, что посчитал движок.
/// </summary>
public static class InboxWriter
{
    public static void Add(Career c, string from, string subject, string body, string kind, string link = null)
    {
        int id = c.Messages.Count == 0 ? 1 : c.Messages.Max(m => m.Id) + 1;
        c.Messages.Add(new Message { Id = id, Date = c.Today, From = from, Subject = subject, Body = body, Kind = kind, Link = link });
    }

    public static void Welcome(Career c)
    {
        var t = c.Team(c.UserTeam);
        int rank = c.PowerRank(t.Abbr).all;
        string goal = rank <= 4 ? "бороться за Кубок Стэнли" : rank <= 10 ? "уверенно выйти в плей-офф и пройти хотя бы раунд" :
                      rank <= 18 ? "выйти в плей-офф" : rank <= 25 ? "бороться за плей-офф до последних недель" : "показать прогресс и развивать молодых";
        Add(c, "Владелец клуба", "Добро пожаловать в клуб!",
            $"Рады видеть вас во главе «{t.Name}».\n\n" +
            $"По оценке аналитиков, по силе состава мы {rank}-е в лиге. Задача на сезон 2026-27 — {goal}.\n\n" +
            "Состав и звенья — на ваше усмотрение. Помощник подготовил расстановку, как у клуба в последних матчах; её можно поменять в разделе «Звенья».\n\nУдачи!",
            "board");
        var l = c.Lineups[c.UserTeam];
        var top = l.Skaters.Select(id => c.Data.PlayerById[id]).OrderByDescending(p => p.Ixg60 * p.Toi).Take(3).Select(p => p.NameRu);
        var g = c.Data.PlayerById.GetValueOrDefault(l.G1);
        Add(c, "Помощник тренера", "Состав на старт сезона",
            $"Ключевые игроки по созданию моментов: {string.Join(", ", top)}.\n" +
            (g != null ? $"Основной вратарь — {g.NameRu} (сейвы сверх ожидаемого {g.Gsax * 100:+0.0;-0.0}% от ожидаемых голов).\n" : "") +
            (c.Injured.Any(id => c.Data.PlayerById.TryGetValue(id, out var p) && p.Team == c.UserTeam)
                ? $"В лазарете: {string.Join(", ", c.Injured.Where(id => c.Data.PlayerById[id].Team == c.UserTeam).Select(id => c.Data.PlayerById[id].NameRu))}.\n" : "") +
            "\nСовет: во втором матче за два дня лучше ставить запасного вратаря — усталость стоит команде около 5% голов.",
            "match");
    }

    public static void AfterUserGame(Career c, ScheduledGame g)
    {
        var r = g.Result;
        bool home = g.Home == c.UserTeam;
        var opp = c.Team(home ? g.Away : g.Home);
        int my = home ? r.HomeGoals : r.AwayGoals, op = home ? r.AwayGoals : r.HomeGoals;
        bool win = my > op;
        string end = r.End == "OT" ? " в овертайме" : r.End == "SO" ? " по буллитам" : "";
        double pre = home ? r.PHome : 1 - r.PHome;
        var scorers = r.Goals.Where(e => e.Team == c.UserTeam && e.Kind != "so" && e.Scorer != 0)
            .GroupBy(e => e.Scorer).Select(x => (c.Short(x.Key), x.Count())).ToList();
        string sc = scorers.Count > 0 ? "Голы: " + string.Join(", ", scorers.Select(x => x.Item2 > 1 ? $"{x.Item1} ({x.Item2})" : x.Item1)) + "." : "Забить не смогли.";
        string verdict = win
            ? (pre < 0.42 ? "Победа вопреки прогнозу — модель давала нам только " + Pct(pre) + "." : pre > 0.62 ? "Плановая победа." : "Важные два очка.")
            : (pre > 0.6 ? "Обидное поражение: по модели мы были фаворитами (" + Pct(pre) + ")." : r.End != "REG" ? "Очко всё-таки взяли." : "Соперник был сильнее.");
        var row = c.Table[c.UserTeam];
        Add(c, "Помощник тренера", $"{(win ? "Победа" : "Поражение")}{end}: {my}:{op} {(home ? "с" : "в гостях у")} «{opp.City}»",
            $"{verdict} {sc}\nБроски в створ: {(home ? r.HomeShots : r.AwayShots)} — {(home ? r.AwayShots : r.HomeShots)}.\n\n" +
            (g.Playoff ? "" : $"У нас {row.W}-{row.L}-{row.Otl}, {row.Pts} {Plural(row.Pts, "очко", "очка", "очков")} в {row.Gp} {Plural(row.Gp, "матче", "матчах", "матчах")}."),
            "match", $"game:{g.Id}");
    }

    /// <summary>Обзор недели (понедельник) и игрок месяца (1-е число).</summary>
    public static void Periodic(Career c)
    {
        if (c.Phase != Phase.Regular) return;
        if (c.Today.DayOfWeek == DayOfWeek.Monday && c.Table[c.UserTeam].Gp > 0)
        {
            var t = c.Team(c.UserTeam);
            var div = c.Sorted(x => x.Division == t.Division);
            int place = div.FindIndex(x => x.Team == c.UserTeam) + 1;
            var conf = c.Sorted(x => x.Conference == t.Conference);
            int cplace = conf.FindIndex(x => x.Team == c.UserTeam) + 1;
            var week = c.TeamGames(c.UserTeam).Where(g => g.Played && g.Date >= c.Today.AddDays(-7)).ToList();
            int w = week.Count(g => (g.Home == c.UserTeam) == (g.Result.HomeGoals > g.Result.AwayGoals));
            var lead = c.Skaters.Where(kv => c.Data.PlayerById[kv.Key].Team == c.UserTeam).OrderByDescending(kv => kv.Value.Pts).FirstOrDefault();
            Add(c, "Аналитический отдел", $"Итоги недели: {w} {Plural(w, "победа", "победы", "побед")} в {week.Count} {Plural(week.Count, "матче", "матчах", "матчах")}",
                $"Место в дивизионе: {place}-е, в конференции: {cplace}-е.\n" +
                (cplace <= 8 ? "Сейчас мы в зоне плей-офф." : $"До зоны плей-офф: {conf[7].Pts - c.Table[c.UserTeam].Pts} {Plural(conf[7].Pts - c.Table[c.UserTeam].Pts, "очко", "очка", "очков")} (с поправкой на игры в запасе смотрите таблицу «Уайлд-кард»).") +
                (lead.Key != 0 ? $"\nЛучший бомбардир клуба: {c.Data.PlayerById[lead.Key].NameRu}, {lead.Value.G}+{lead.Value.A}." : ""),
                "week", "table");
        }
        if (c.Today.Day == 1 && c.Today.Month != 10)
        {
            var from = c.Today.AddMonths(-1);
            var pts = new Dictionary<int, int>();
            foreach (var g in c.Games.Where(g => g.Played && !g.Playoff && g.Date >= from && g.Date < c.Today))
                foreach (var e in g.Result.Goals.Where(e => e.Kind != "so"))
                    foreach (var id in new[] { e.Scorer, e.A1, e.A2 }.Where(x => x != 0))
                        pts[id] = pts.GetValueOrDefault(id) + 1;
            if (pts.Count > 0)
            {
                var best = pts.OrderByDescending(kv => kv.Value).First();
                var p = c.Data.PlayerById[best.Key];
                string[] months = { "январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь" };
                Add(c, "Лига", $"Игрок месяца ({months[from.Month - 1]}): {p.NameRu}",
                    $"{p.NameRu} («{c.Team(p.Team).City}») набрал {best.Value} {Plural(best.Value, "очко", "очка", "очков")} за месяц.",
                    "league", $"player:{p.Id}");
            }
        }
    }

    public static void PlayoffsStart(Career c)
    {
        bool inPo = c.Bracket.Any(s => s.High == c.UserTeam || s.Low == c.UserTeam);
        var s0 = c.Bracket.FirstOrDefault(s => s.High == c.UserTeam || s.Low == c.UserTeam);
        Add(c, "Владелец клуба", inPo ? "Мы в плей-офф!" : "Сезон окончен для нас",
            inPo ? $"Поздравляю с выходом в плей-офф. В первом раунде — «{c.Team(s0.High == c.UserTeam ? s0.Low : s0.High).Name}»."
                 : "В плей-офф мы не попали. Разберём ошибки и подготовимся к следующему сезону. Многосезонная карьера появится в следующих версиях.",
            "board", "playoffs");
    }

    static string Pct(double p) => $"{Math.Round(p * 100):0}%";
    static string Plural(int n, string one, string few, string many)
    {
        int a = Math.Abs(n) % 100, b = a % 10;
        if (a is > 10 and < 20) return many;
        return b == 1 ? one : b is >= 2 and <= 4 ? few : many;
    }
}
