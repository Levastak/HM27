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
    // ---- письмо-статья (журнальная вёрстка); у старых писем пусто — показываются как раньше
    /// <summary>Рубрика: «Матч», «Вечер в НХЛ», «Неделя в НХЛ», «Цифры», «Пресса», «Лазарет», «Клуб», «Лига».</summary>
    public string Tag { get; set; }
    /// <summary>Подводка под заголовком.</summary>
    public string Lead { get; set; }
    /// <summary>Плашка цифр под подводкой.</summary>
    public List<MsgFact> Facts { get; set; }
    /// <summary>Герои материала (карточки игроков).</summary>
    public List<int> Players { get; set; }
    /// <summary>Клуб, в цветах которого шапка письма.</summary>
    public string Team { get; set; }
    public string Quote { get; set; }
    public string QuoteBy { get; set; }
    /// <summary>Вес для портала: 0 — рутина, 3 — главная новость дня.</summary>
    public int Weight { get; set; }
    // ---- решение: варианты ответа прямо в письме
    public List<MsgOption> Options { get; set; }
    /// <summary>Выбранный вариант (null — ещё не ответили).</summary>
    public string Choice { get; set; }
    /// <summary>Обязательное: пока нет ответа, «Продолжить» не пускает дальше.</summary>
    public bool Required { get; set; }
    /// <summary>Срок ответа; после него — вариант по умолчанию (в автоматическом прогоне — всегда).</summary>
    public DateOnly? Due { get; set; }
    /// <summary>Обработчик решения (Decisions) и его объект — обычно id игрока.</summary>
    public string Action { get; set; }
    public int Target { get; set; }
    /// <summary>Что было после ответа — показывается под вариантами.</summary>
    public string Reply { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool Pending => Options != null && Choice == null;
    /// <summary>Новость лиги или прессы (лента портала), а не письмо тренеру: не входит в счётчик непрочитанных.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool IsNews => Kind == "league" || Tag is "Вечер в НХЛ" or "Пресса" or "Превью" or "Рейтинг силы" or "Погоня" || (Tag == "Клуб" && Link == "press:club");
}

public sealed class MsgOption
{
    public string Id { get; set; }
    public string Label { get; set; }
    /// <summary>Чем это обернётся — подсказка под кнопкой.</summary>
    public string Hint { get; set; }
    public bool Default { get; set; }
    public MsgOption() { }
    public MsgOption(string id, string label, string hint = null, bool def = false) { Id = id; Label = label; Hint = hint; Default = def; }
}

public sealed class MsgFact
{
    public string Label { get; set; }
    public string Value { get; set; }
    public string Sub { get; set; }
    /// <summary>good | bad | null — цвет цифры.</summary>
    public string Tone { get; set; }
    public MsgFact() { }
    public MsgFact(string label, string value, string sub = null, string tone = null) { Label = label; Value = value; Sub = sub; Tone = tone; }
}

/// <summary>
/// Входящие, как в FM: письма о событиях карьеры. Тексты собираются из фактов сезона —
/// никаких выдуманных эффектов на результат, только то, что посчитал движок.
/// </summary>
public static class InboxWriter
{
    public static Message Add(Career c, string from, string subject, string body, string kind, string link = null)
    {
        int id = c.Messages.Count == 0 ? 1 : c.Messages.Max(m => m.Id) + 1;
        var m = new Message { Id = id, Date = c.Today, From = from, Subject = subject, Body = body, Kind = kind, Link = link,
            Tag = kind switch { "medical" => "Лазарет", "board" => "Клуб", "match" => "Матч", "week" => "Неделя", "league" => "Лига", _ => null } };
        c.Messages.Add(m);
        return m;
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
            (g != null ? $"Основной вратарь — {g.NameRu}.\n" : "") +
            (c.Injured.Any(id => c.Data.PlayerById.TryGetValue(id, out var p) && p.Team == c.UserTeam)
                ? $"В лазарете: {string.Join(", ", c.Injured.Where(id => c.Data.PlayerById[id].Team == c.UserTeam).Select(id => c.Data.PlayerById[id].NameRu))}.\n" : "") +
            "\nВо втором матче за два дня я бы ставил запасного — основной после вчерашней игры заметно слабее.",
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
    /// <summary>Игроки месяца и гонка за призами — теперь News.Monthly (с автором и цитатой).</summary>
    public static void Periodic(Career c) { }

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
