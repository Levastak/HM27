namespace HM27.Core;

/// <summary>
/// Новости карьеры — живые тексты из фактов сезона: отчёты о матчах, «Вечер в НХЛ» (хет-трики, сухари, серии, юбилеи),
/// «Неделя в НХЛ», «Цифры» (место клуба в лиге по ключевым показателям), «Пресса» (сюжеты вокруг клуба).
/// Ничего не влияет на игру и ничего не выдумывает: каждая цифра посчитана движком, каждый сюжет — из таблицы и протоколов.
/// Шаблоны выбираются детерминированно (сид карьеры + день), поэтому одна и та же карьера пишет одни и те же тексты.
/// </summary>
public static class News
{
    static Random Rng(Career c, int salt) => new(unchecked(c.Seed * 7349 + c.Today.DayNumber * 31 + salt));
    static string Pick(Random r, params string[] xs) => xs[r.Next(xs.Length)];
    static string Q(Career c, string abbr) => $"«{c.Team(abbr).City}»";
    static string Name(Career c, int id) => c.Data.PlayerById.TryGetValue(id, out var p) ? p.NameRu : "—";
    static string Plural(int n, string one, string few, string many)
    {
        int a = Math.Abs(n) % 100, b = a % 10;
        if (a is > 10 and < 20) return many;
        return b == 1 ? one : b is >= 2 and <= 4 ? few : many;
    }
    static string N(int n, string one, string few, string many) => $"{n} {Plural(n, one, few, many)}";
    static string F1(double v) => v.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    static string F2(double v) => v.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    static string Ord(int n) => $"{n}-е";

    public static Message Post(Career c, string from, string tag, string headline, string lead, string body, string kind, string link = null,
        string team = null, int weight = 1, List<MsgFact> facts = null, List<int> players = null, string quote = null, string quoteBy = null)
    {
        var m = InboxWriter.Add(c, from, headline, body, kind, link);
        m.Tag = tag; m.Lead = lead; m.Team = team; m.Weight = weight; m.Facts = facts; m.Players = players; m.Quote = quote; m.QuoteBy = quoteBy;
        return m;
    }

    // ================================================================ отчёт о матче своего клуба
    public static void MatchReport(Career c, ScheduledGame g)
    {
        var r = g.Result;
        string me = c.UserTeam;
        bool home = g.Home == me;
        string op = home ? g.Away : g.Home;
        int my = home ? r.HomeGoals : r.AwayGoals, their = home ? r.AwayGoals : r.HomeGoals;
        bool win = my > their;
        string end = r.End == "OT" ? " в овертайме" : r.End == "SO" ? " по буллитам" : "";
        string score = $"{my}:{their}";
        var rng = new Random(unchecked(c.Seed * 131 + g.Id * 17));
        double pre = home ? r.PHome : 1 - r.PHome;

        // протокол: xG, большинство, вбрасывания, силовые
        double xgMe = 0, xgOp = 0; int foMe = 0, foOp = 0, hitMe = 0, hitOp = 0, blkMe = 0, blkOp = 0;
        var mine = new HashSet<int>(home ? r.HomeDressed : r.AwayDressed);
        foreach (var (id, b) in r.Box)
        {
            bool m = mine.Contains(id);
            if (m) { xgMe += b.Ixg; foMe += b.Fow; hitMe += b.Hit; blkMe += b.Blk; } else { xgOp += b.Ixg; foOp += b.Fow; hitOp += b.Hit; blkOp += b.Blk; }
        }
        bool hasBox = r.Box.Count > 0;
        int ppOppMe = r.Events.Count(e => e.Kind == "pen" && e.Team == op && e.P2 is 2 or 4);
        int ppOppOp = r.Events.Count(e => e.Kind == "pen" && e.Team == me && e.P2 is 2 or 4);
        int ppgMe = r.Goals.Count(e => e.Team == me && e.Kind == "pp"), ppgOp = r.Goals.Count(e => e.Team == op && e.Kind == "pp");

        // ход матча: камбэк, победный гол, хет-трик
        int a = 0, b2 = 0, worst = 0, best = 0;
        GoalEvent gwg = null;
        var goals = r.Goals.Where(e => e.Kind != "so").OrderBy(e => e.Period).ThenBy(e => e.Sec).ToList();
        foreach (var e in goals)
        {
            if (e.Team == me) a++; else b2++;
            worst = Math.Min(worst, a - b2); best = Math.Max(best, a - b2);
        }
        int loserGoals = win ? their : my, cnt = 0;
        foreach (var e in goals.Where(e => e.Team == (win ? me : op)))
            if (++cnt == loserGoals + 1) { gwg = e; break; }
        var hat = goals.Where(e => e.Team == me).GroupBy(e => e.Scorer).Where(x => x.Count() >= 3).Select(x => x.Key).FirstOrDefault();
        var multi = goals.Where(e => e.Team == me).SelectMany(e => new[] { e.Scorer, e.A1, e.A2 }).Where(x => x != 0)
            .GroupBy(x => x).Select(x => (id: x.Key, pts: x.Count())).OrderByDescending(x => x.pts).FirstOrDefault();
        int myG = home ? r.HomeGoalie : r.AwayGoalie, opG = home ? r.AwayGoalie : r.HomeGoalie;
        int mySa = r.Box.TryGetValue(myG, out var mgb) ? mgb.Sa : (home ? r.AwayShots : r.HomeShots);
        int opSa = r.Box.TryGetValue(opG, out var ogb) ? ogb.Sa : (home ? r.HomeShots : r.AwayShots);
        int mySv = mySa - their + goals.Count(e => e.Team == op && e.Kind == "en");
        bool shutout = their == 0;

        string O = Q(c, op);
        string headline, lead;
        if (win && shutout)
        {
            headline = Pick(rng, $"{c.Short(myG)} — на ноль: {score} с {O}", $"На ноль: {c.Short(myG)} отразил все {N(mySa, "бросок", "броска", "бросков")}", $"{score}: {O} так и не пробили нашего вратаря");
            lead = $"{Name(c, myG)} отразил все {N(mySa, "бросок", "броска", "бросков")} — победа{end} {score} {(home ? "дома" : "в гостях")}.";
        }
        else if (win && hat != 0)
        {
            headline = Pick(rng, $"{c.Short(hat)} — хет-трик: {score} с {O}", $"{c.Short(hat)} забивает трижды, {score}{end}");
            lead = $"{Name(c, hat)} оформил хет-трик, и клуб взял верх над {O}{end} — {score}.";
        }
        else if (win && worst <= -2)
        {
            headline = Pick(rng, $"Камбэк: уступали {-worst} шайбы — и победили {O}", $"Отыгрались с −{-worst}: {score}{end}", $"Характер: с −{-worst} до победы над {O}");
            lead = $"По ходу матча проигрывали в {N(-worst, "шайбу", "шайбы", "шайб")}, но дожали {O}{end} — {score}.";
        }
        else if (win && r.End != "REG" && gwg != null)
        {
            headline = r.End == "SO" ? $"Буллиты наши: {score} с {O}" : Pick(rng, $"{c.Short(gwg.Scorer)} решил исход в овертайме", $"Овертайм: {c.Short(gwg.Scorer)} приносит победу над {O}");
            lead = r.End == "SO" ? $"Шестьдесят пять минут не выявили сильнейшего, в серии буллитов удача была на нашей стороне." :
                $"{Name(c, gwg.Scorer)} забил в овертайме — {score} с {O}.";
        }
        else if (win && my - their >= 4)
        {
            headline = Pick(rng, $"Разгром: {score} с {O}", $"{score}: {O} разобраны по косточкам", $"Голевой вечер: {score} с {O}");
            lead = $"Крупная победа {(home ? "дома" : "в гостях")} — {score}.{(multi.pts >= 3 ? $" {Name(c, multi.id)} набрал {N(multi.pts, "очко", "очка", "очков")}." : "")}";
        }
        else if (win && pre < 0.4)
        {
            headline = Pick(rng, $"Вопреки прогнозу: {score} с {O}", $"Никто не ставил, а мы взяли: {score}", $"Сенсация {(home ? "дома" : "на выезде")}: {score} с {O}");
            lead = $"До матча шансы были {Math.Round(pre * 100)}% — но победа{end} {score}.";
        }
        else if (win)
        {
            headline = Pick(rng, $"Победа над {O}: {score}{end}", $"{score}{end}: ещё два очка в копилку", $"{O} обыграны — {score}{end}");
            lead = gwg != null ? $"Победную шайбу забросил {Name(c, gwg.Scorer)}.{(multi.pts >= 2 ? $" {Name(c, multi.id)} — {N(multi.pts, "очко", "очка", "очков")}." : "")}" : $"Победа {score}{end}.";
        }
        else if (their == 0)
        {
            headline = Pick(rng, $"Без голов: {score} с {O}", $"{c.Short(opG)} закрыл ворота: {score}", $"Ноль в графе голов: {score} с {O}");
            lead = $"{Name(c, opG)} отразил все {N(opSa, "бросок", "броска", "бросков")} — забить так и не смогли.";
        }
        else if (r.End != "REG")
        {
            headline = Pick(rng, $"Очко в {(r.End == "SO" ? "серии буллитов" : "овертайме")}: {score} с {O}", $"{score}{end}: одно очко из двух");
            lead = $"Основное время закончилось вничью, дополнительное — за {O}. Одно очко всё-таки наше.";
        }
        else if (best >= 2)
        {
            headline = Pick(rng, $"Упустили +{best}: {score} с {O}", $"Растеряли преимущество: {score}");
            lead = $"Вели в {N(best, "шайбу", "шайбы", "шайб")}, но {O} перевернули матч — {score}.";
        }
        else if (their - my >= 4)
        {
            headline = Pick(rng, $"Тяжёлый вечер: {score} с {O}", $"{score}: забыть и работать", $"Провал {(home ? "дома" : "на выезде")}: {score}");
            lead = $"{O} были сильнее во всём. Такие матчи лучше разбирать на видео, а не в таблице.";
        }
        else
        {
            headline = Pick(rng, $"Поражение от {O}: {score}", $"{score}: {O} оказались сильнее", $"Без очков: {score} с {O}");
            lead = pre > 0.6 ? $"Обидно: до матча мы были фаворитами ({Math.Round(pre * 100)}%)." : "Соперник был точнее в ключевые моменты.";
        }

        // цитата помощника — по качеству моментов
        string quote;
        if (!hasBox) quote = win ? "Важная победа. Двигаемся дальше." : "Разберём ошибки и двинемся дальше.";
        else if (win && xgMe >= xgOp + 0.6) quote = Pick(rng, $"Мы контролировали игру: по качеству моментов {F1(xgMe)} против {F1(xgOp)}. Так и надо.", $"Создали больше — {F1(xgMe)} ожидаемых голов против {F1(xgOp)}. Победа заслуженная.");
        else if (win && xgOp >= xgMe + 0.6) quote = Pick(rng, $"Будем честны: соперник создал больше ({F1(xgOp)} против {F1(xgMe)}). Выручил {c.Short(myG)} — {mySv} {Plural(mySv, "сейв", "сейва", "сейвов")}.", $"Очки взяли, но по игре есть вопросы: xG {F1(xgMe)} — {F1(xgOp)} не в нашу пользу.");
        else if (win) quote = Pick(rng, "Равный матч, решили детали. Такие игры надо выигрывать.", $"По моментам почти поровну ({F1(xgMe)} — {F1(xgOp)}), но реализовали лучше мы.");
        else if (xgMe >= xgOp + 0.6) quote = Pick(rng, $"Моменты были — {F1(xgMe)} ожидаемых голов против {F1(xgOp)}. Не забили. На дистанции такое выравнивается, но очки уже ушли.", $"Обидно: создали больше ({F1(xgMe)} — {F1(xgOp)}), а {c.Short(opG)} стоял стеной.");
        else if (xgOp >= xgMe + 0.6) quote = Pick(rng, $"Проиграли по делу: соперник создал заметно больше ({F1(xgOp)} против {F1(xgMe)}). Будем разбирать игру в обороне.", "Мало создавали и много позволяли. Над этим и будем работать.");
        else quote = Pick(rng, "Равная игра, но удача была на их стороне.", $"По моментам — паритет ({F1(xgMe)} — {F1(xgOp)}). Не хватило точности.");
        if (hasBox && ppOppMe >= 3 && ppgMe == 0) quote += $" И большинство: 0 из {ppOppMe} — это нужно чинить.";

        // плашка цифр
        var facts = new List<MsgFact> { new("Счёт", score + (r.End == "OT" ? " ОТ" : r.End == "SO" ? " Б" : ""), home ? "дома" : "в гостях", win ? "good" : "bad") };
        facts.Add(new("Броски в створ", $"{(home ? r.HomeShots : r.AwayShots)}–{(home ? r.AwayShots : r.HomeShots)}", "мы — соперник"));
        if (hasBox)
        {
            facts.Add(new("Ожидаемые голы", $"{F1(xgMe)}–{F1(xgOp)}", "качество моментов", xgMe >= xgOp ? "good" : "bad"));
            facts.Add(new("Большинство", $"{ppgMe} из {ppOppMe}", ppOppOp > 0 ? $"в меньшинстве отбились {ppOppOp - ppgOp} из {ppOppOp}" : "без меньшинства"));
            if (foMe + foOp > 0) facts.Add(new("Вбрасывания", $"{Math.Round(100.0 * foMe / (foMe + foOp))}%", $"{foMe} из {foMe + foOp}"));
            facts.Add(new("Силовые", $"{hitMe}–{hitOp}", $"блоки {blkMe}–{blkOp}"));
        }

        // тело: по периодам, авторы, турнирное положение
        var body = new System.Text.StringBuilder();
        var periods = new List<string>();
        for (int p = 1; p <= Math.Max(3, goals.Count == 0 ? 3 : goals.Max(e => e.Period)); p++)
        {
            int x = goals.Count(e => e.Period == p && e.Team == me), y = goals.Count(e => e.Period == p && e.Team == op);
            periods.Add($"{x}:{y}");
        }
        body.Append($"По периодам: {string.Join(", ", periods)}{(r.End == "SO" ? ", буллиты" : "")}.");
        var scorers = goals.Where(e => e.Team == me).GroupBy(e => e.Scorer).Select(x => x.Count() > 1 ? $"{c.Short(x.Key)} ({x.Count()})" : c.Short(x.Key)).ToList();
        if (scorers.Count > 0) body.Append($" Голы: {string.Join(", ", scorers)}.");
        if (gwg != null && win) body.Append($" Победная шайба — {Name(c, gwg.Scorer)}{(gwg.Shot != null ? $", {gwg.Shot}" : "")}.");
        body.Append($"\n\n{c.Short(myG)}: {mySv} {Plural(mySv, "сейв", "сейва", "сейвов")} из {mySa}.");
        if (!g.Playoff)
        {
            var row = c.Table[me];
            var t = c.Team(me);
            var div = c.Sorted(x => x.Division == t.Division);
            int place = div.FindIndex(x => x.Team == me) + 1;
            body.Append($" У нас {row.W}-{row.L}-{row.Otl}, {N(row.Pts, "очко", "очка", "очков")} — {Ord(place)} место в дивизионе.");
            if (row.Streak.Length > 1 && int.TryParse(row.Streak[1..].TrimStart('О'), out var sn) && sn >= 3)
                body.Append(row.Streak[0] == 'В' ? $" Серия побед — {sn}." : row.Streak.StartsWith("П") ? $" Серия поражений — {sn}." : "");
        }

        // три звезды
        var stars = r.Ratings.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToList();
        Post(c, "Помощник тренера", "Матч", headline, lead, body.ToString(), "match", $"game:{g.Id}", me, win ? 2 : 1, facts, stars, quote, "Помощник тренера");
    }

    // ================================================================ «Вечер в НХЛ»: события вчерашнего дня по лиге
    sealed record Item(double W, string Head, string Line, string Team, int Player);

    public static void Daily(Career c)
    {
        var day = c.Today.AddDays(-1);
        var games = c.Games.Where(g => g.Played && g.Date == day).ToList();
        if (games.Count == 0) return;
        var items = new List<Item>();
        string me = c.UserTeam;
        double Boost(string team, int pid) => (team == me ? 1.3 : 1.0) * (pid != 0 && c.Data.PlayerById.TryGetValue(pid, out var p) ? 1 + Math.Max(0, p.Ovr - 70) / 40.0 : 1);

        foreach (var g in games)
        {
            var r = g.Result;
            var goals = r.Goals.Where(e => e.Kind != "so").ToList();
            string Opp(string t) => t == g.Home ? g.Away : g.Home;
            // хет-трики и 4+ очка
            foreach (var grp in goals.GroupBy(e => e.Scorer).Where(x => x.Key != 0 && x.Count() >= 3))
            {
                var p = c.Data.PlayerById[grp.Key];
                items.Add(new Item(3 * Boost(p.Team, p.Id), $"{p.LastRu} — хет-трик",
                    $"{p.NameRu} забил {(grp.Count() == 3 ? "трижды" : $"{grp.Count()} раза")} в матче с {Q(c, Opp(p.Team))}", p.Team, p.Id));
            }
            foreach (var x in goals.SelectMany(e => new[] { e.Scorer, e.A1, e.A2 }).Where(x => x != 0).GroupBy(x => x).Where(x => x.Count() >= 4))
            {
                var p = c.Data.PlayerById[x.Key];
                if (items.Any(i => i.Player == p.Id)) continue;
                int gl = goals.Count(e => e.Scorer == p.Id);
                items.Add(new Item(2.2 * Boost(p.Team, p.Id), $"{p.LastRu} — {x.Count()} очка за вечер", $"{p.NameRu} набрал {x.Count()} очка ({gl}+{x.Count() - gl}) против {Q(c, Opp(p.Team))}", p.Team, p.Id));
            }
            // сухари
            foreach (var (gid, team, sa, ga) in new[] { (r.HomeGoalie, g.Home, r.AwayShots, r.AwayGoals), (r.AwayGoalie, g.Away, r.HomeShots, r.HomeGoals) })
                if (ga == 0 && gid != 0 && c.Data.PlayerById.TryGetValue(gid, out var gp))
                    items.Add(new Item((sa >= 35 ? 2.4 : 1.8) * Boost(team, gid), $"{gp.LastRu} — на ноль", $"{gp.NameRu} отразил {N(sa, "бросок", "броска", "бросков")} — {Q(c, team)} {r.HomeGoals}:{r.AwayGoals} {(team == g.Home ? "дома" : "в гостях")}", team, gid));
            // разгромы
            int diff = Math.Abs(r.HomeGoals - r.AwayGoals);
            if (diff >= 5)
            {
                string w = r.HomeGoals > r.AwayGoals ? g.Home : g.Away;
                items.Add(new Item(1.4 * Boost(w, 0), $"Разгром: {Math.Max(r.HomeGoals, r.AwayGoals)}:{Math.Min(r.HomeGoals, r.AwayGoals)}", $"Клуб {Q(c, w)} разгромил {Q(c, Opp(w))} — {Math.Max(r.HomeGoals, r.AwayGoals)}:{Math.Min(r.HomeGoals, r.AwayGoals)}", w, 0));
            }
            // серии клубов
            foreach (var t in new[] { g.Home, g.Away })
            {
                var row = c.Table[t];
                if (g.Playoff || row.Streak.Length < 2 || !int.TryParse(row.Streak[1..].TrimStart('О'), out var n)) continue;
                if (row.Streak[0] == 'В' && n >= 5 && (n == 5 || n % 2 == 0 || n >= 9))
                    items.Add(new Item((1.6 + 0.2 * (n - 5)) * Boost(t, 0), $"{Q(c, t)}: {n} побед подряд", $"Клуб {Q(c, t)} выиграл {n}-й матч подряд", t, 0));
                if (row.Streak[0] == 'П' && n >= 5 && (n == 5 || n % 2 == 1))
                    items.Add(new Item((1.3 + 0.2 * (n - 5)) * Boost(t, 0), $"{Q(c, t)}: {n} поражений подряд", $"Клуб {Q(c, t)} проиграл {n}-й матч подряд — пресса уже ищет виноватых", t, 0));
            }
            // очковые серии и юбилеи — у тех, кто набрал очки
            foreach (var pid in goals.SelectMany(e => new[] { e.Scorer, e.A1, e.A2 }).Where(x => x != 0).Distinct())
            {
                if (!c.Data.PlayerById.TryGetValue(pid, out var p)) continue;
                int streak = PointStreak(c, p);
                if (streak >= 8 && (streak == 8 || streak == 10 || streak >= 12))
                    items.Add(new Item((2 + 0.25 * (streak - 8)) * Boost(p.Team, p.Id), $"{p.LastRu}: очки в {streak} матчах подряд",
                        $"{p.NameRu} набирает очки {N(streak, "матч", "матча", "матчей")} подряд", p.Team, p.Id));
                foreach (var (what, before, after) in Milestones(c, p, g))
                    items.Add(new Item(3.2 * Boost(p.Team, p.Id), $"{p.LastRu}: {after} {what} в НХЛ", $"{p.NameRu} добрался до отметки {after} {what} в НХЛ", p.Team, p.Id));
                if (Past(c, p).gp > 0 || p.IsGoalie) continue;
                var s = c.Skaters.GetValueOrDefault(pid);
                if (s != null && s.G == goals.Count(e => e.Scorer == pid) && s.G > 0)
                    items.Add(new Item(1.5 * Boost(p.Team, p.Id), $"{p.LastRu} — первый гол в НХЛ", $"{p.NameRu} забил первый гол в карьере в НХЛ — запомните это имя", p.Team, p.Id));
            }
        }
        if (items.Count == 0) return;
        items = items.GroupBy(i => i.Head).Select(x => x.First()).OrderByDescending(i => i.W).ToList();
        if (items[0].W < 2.0) return;   // скучный вечер — без письма
        var top = items[0];
        var rest = items.Skip(1).Take(5).ToList();
        string body = rest.Count == 0 ? "" : "Ещё в этот вечер:\n" + string.Join("\n", rest.Select(i => "• " + i.Line + "."));
        int played = games.Count;
        body += (body.Length > 0 ? "\n\n" : "") + $"Всего {N(played, "матч", "матча", "матчей")}, {N(games.Sum(g => g.Result.HomeGoals + g.Result.AwayGoals), "гол", "гола", "голов")}.";
        var players = items.Take(4).Where(i => i.Player != 0).Select(i => i.Player).Distinct().ToList();
        Post(c, "Лента НХЛ", "Вечер в НХЛ", top.Head, top.Line + ".", body, "league", top.Player != 0 ? $"player:{top.Player}" : $"club:{top.Team}",
            top.Team, top.W >= 3 ? 2 : 1, null, players);
    }

    /// <summary>Сколько матчей подряд игрок набирает очки (по матчам, где он был в составе).</summary>
    static int PointStreak(Career c, PlayerData p)
    {
        int n = 0;
        foreach (var g in c.TeamGames(p.Team).Where(g => g.Played).OrderByDescending(g => g.Date))
        {
            var dressed = g.Home == p.Team ? g.Result.HomeDressed : g.Result.AwayDressed;
            if (!dressed.Contains(p.Id)) { if (n == 0) continue; else break; }
            if (g.Result.Goals.Any(e => e.Kind != "so" && (e.Scorer == p.Id || e.A1 == p.Id || e.A2 == p.Id))) n++; else break;
        }
        return n;
    }

    /// <summary>Реальная карьера в НХЛ до старта игры: матчи, голы, очки.</summary>
    static (int gp, int g, int pts) Past(Career c, PlayerData p)
    {
        if (p.Bio == null) return (0, 0, 0);
        long cut = (long)c.Data.Season * 10000 + c.Data.Season + 1;
        int gp = 0, gl = 0, pts = 0;
        foreach (var r in p.Bio.Nhl)
        {
            if (r[0].GetInt64() >= cut) continue;
            gp += r[2].GetInt32();
            if (!p.IsGoalie) { gl += r[3].GetInt32(); pts += r[5].GetInt32(); }
        }
        return (gp, gl, pts);
    }

    static IEnumerable<(string what, int before, int after)> Milestones(Career c, PlayerData p, ScheduledGame g)
    {
        if (p.IsGoalie) yield break;
        var past = Past(c, p);
        var s = c.Skaters.GetValueOrDefault(p.Id);
        if (s == null) yield break;
        int gToday = g.Result.Goals.Count(e => e.Kind != "so" && e.Scorer == p.Id);
        int pToday = g.Result.Goals.Count(e => e.Kind != "so" && (e.Scorer == p.Id || e.A1 == p.Id || e.A2 == p.Id));
        int gNow = past.g + s.G, pNow = past.pts + s.Pts;
        foreach (int m in new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 })
            if (gNow - gToday < m && gNow >= m) yield return ("голов", gNow - gToday, m);
        foreach (int m in new[] { 500, 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400, 1500, 1600 })
            if (pNow - pToday < m && pNow >= m) yield return ("очков", pNow - pToday, m);
    }

    // ================================================================ «Неделя в НХЛ» (понедельник)
    public static void Weekly(Career c)
    {
        var from = c.Today.AddDays(-7);
        var games = c.Games.Where(g => g.Played && !g.Playoff && g.Date >= from && g.Date < c.Today).ToList();
        if (games.Count < 10) return;
        string me = c.UserTeam;
        var t = c.Team(me);
        var rng = Rng(c, 5);

        // команда недели
        var rec = new Dictionary<string, (int w, int l, int o, int gf, int ga)>();
        foreach (var g in games)
            foreach (var (team, gf, ga) in new[] { (g.Home, g.Result.HomeGoals, g.Result.AwayGoals), (g.Away, g.Result.AwayGoals, g.Result.HomeGoals) })
            {
                var x = rec.GetValueOrDefault(team);
                if (gf > ga) x.w++; else if (g.Result.End == "REG") x.l++; else x.o++;
                x.gf += gf; x.ga += ga;
                rec[team] = x;
            }
        var tw = rec.OrderByDescending(kv => kv.Value.w * 2 + kv.Value.o).ThenByDescending(kv => kv.Value.gf - kv.Value.ga).First();
        var tl = rec.OrderBy(kv => kv.Value.w * 2 + kv.Value.o).ThenBy(kv => kv.Value.gf - kv.Value.ga).First();

        // игрок недели
        var pts = new Dictionary<int, (int g, int a, int gp)>();
        var gk = new Dictionary<int, (int sa, int ga, int gp)>();
        foreach (var g in games)
        {
            foreach (var e in g.Result.Goals.Where(e => e.Kind != "so"))
            {
                if (e.Scorer != 0) { var x = pts.GetValueOrDefault(e.Scorer); x.g++; pts[e.Scorer] = x; }
                foreach (var a in new[] { e.A1, e.A2 }.Where(a => a != 0)) { var x = pts.GetValueOrDefault(a); x.a++; pts[a] = x; }
            }
            foreach (var (gid, sa, ga) in new[] { (g.Result.HomeGoalie, g.Result.AwayShots, g.Result.AwayGoals), (g.Result.AwayGoalie, g.Result.HomeShots, g.Result.HomeGoals) })
                if (gid != 0) { var x = gk.GetValueOrDefault(gid); x.sa += sa; x.ga += ga; x.gp++; gk[gid] = x; }
        }
        var pw = pts.OrderByDescending(kv => kv.Value.g + kv.Value.a).ThenByDescending(kv => kv.Value.g).First();
        var gw = gk.Where(kv => kv.Value.gp >= 2 && kv.Value.sa > 0).OrderByDescending(kv => 1 - kv.Value.ga / (double)kv.Value.sa).ThenByDescending(kv => kv.Value.sa).FirstOrDefault();
        var pP = c.Data.PlayerById[pw.Key];
        int pTot = pw.Value.g + pw.Value.a;

        // наша неделя и место: сейчас и неделю назад
        var mineRec = rec.GetValueOrDefault(me);
        var conf = c.Sorted(x => x.Conference == t.Conference);
        int cNow = conf.FindIndex(x => x.Team == me) + 1;
        var before = c.Table.Values.Where(x => c.Team(x.Team).Conference == t.Conference)
            .Select(x => (x.Team, pts: x.Pts - (rec.TryGetValue(x.Team, out var w) ? w.w * 2 + w.o : 0)))
            .OrderByDescending(x => x.pts).ToList();
        int cWas = before.FindIndex(x => x.Team == me) + 1;
        int move = cWas - cNow;

        string head = Pick(rng, $"Неделя в НХЛ: {pP.LastRu} и {Q(c, tw.Key)} на коне", $"Неделя в НХЛ: {pTot} очков {pP.LastRu}", $"Неделя в НХЛ: в лидерах — {Q(c, tw.Key)}");
        string lead = $"Лучшая команда недели — {Q(c, tw.Key)} ({tw.Value.w}-{tw.Value.l}-{tw.Value.o}, шайбы {tw.Value.gf}:{tw.Value.ga}). " +
                      $"Игрок недели — {pP.NameRu}: {N(pTot, "очко", "очка", "очков")} ({pw.Value.g}+{pw.Value.a}).";
        var body = new System.Text.StringBuilder();
        body.Append("## Наша неделя\n");
        body.Append(mineRec.w + mineRec.l + mineRec.o == 0 ? "Матчей не было.\n" :
            $"{mineRec.w}-{mineRec.l}-{mineRec.o}, шайбы {mineRec.gf}:{mineRec.ga}. В конференции — {Ord(cNow)} место" +
            (move > 0 ? $", поднялись на {N(move, "строчку", "строчки", "строчек")}." : move < 0 ? $", опустились на {N(-move, "строчку", "строчки", "строчек")}." : ", без изменений.") + "\n");
        var lead8 = conf.Count >= 8 ? conf[7] : null;
        if (lead8 != null && cNow > 8) body.Append($"До зоны плей-офф — {N(lead8.Pts - c.Table[me].Pts, "очко", "очка", "очков")}.\n");
        else if (conf.Count > 8 && cNow <= 8) body.Append($"Запас над девятым местом — {N(c.Table[me].Pts - conf[8].Pts, "очко", "очка", "очков")}.\n");
        body.Append("\n## По лиге\n");
        var top = c.Sorted().First();
        body.Append($"• Лидер лиги — {Q(c, top.Team)}: {top.W}-{top.L}-{top.Otl}, {N(top.Pts, "очко", "очка", "очков")}.\n");
        body.Append($"• Худшая неделя — у {Q(c, tl.Key)}: {tl.Value.w}-{tl.Value.l}-{tl.Value.o}, шайбы {tl.Value.gf}:{tl.Value.ga}.\n");
        if (gw.Key != 0)
        {
            var gP = c.Data.PlayerById[gw.Key];
            body.Append($"• Вратарь недели — {gP.NameRu} ({Q(c, gP.Team)}): {(1 - gw.Value.ga / (double)gw.Value.sa).ToString(".000", System.Globalization.CultureInfo.InvariantCulture)} в {N(gw.Value.gp, "матче", "матчах", "матчах")}.\n");
        }
        var sc = c.Skaters.OrderByDescending(kv => kv.Value.Pts).ThenByDescending(kv => kv.Value.G).First();
        var gl = c.Skaters.OrderByDescending(kv => kv.Value.G).First();
        body.Append($"• Гонка бомбардиров: {Name(c, sc.Key)} — {sc.Value.Pts} ({sc.Value.G}+{sc.Value.A}); лучший снайпер — {Name(c, gl.Key)}, {N(gl.Value.G, "гол", "гола", "голов")}.\n");
        var streaks = c.Table.Values.Where(x => x.Streak.Length > 1 && int.TryParse(x.Streak[1..].TrimStart('О'), out var n) && n >= 4)
            .OrderByDescending(x => int.Parse(x.Streak[1..].TrimStart('О'))).Take(2).ToList();
        foreach (var x in streaks)
            body.Append($"• Серия: {Q(c, x.Team)} — {(x.Streak[0] == 'В' ? "побед" : x.Streak.StartsWith("П") ? "поражений" : "без побед")} подряд: {x.Streak[1..].TrimStart('О')}.\n");

        var facts = new List<MsgFact>
        {
            new("Наша неделя", $"{mineRec.w}-{mineRec.l}-{mineRec.o}", $"шайбы {mineRec.gf}:{mineRec.ga}", mineRec.w * 2 + mineRec.o >= (mineRec.w + mineRec.l + mineRec.o) ? "good" : "bad"),
            new("Место в конференции", Ord(cNow), move > 0 ? $"↑ на {move}" : move < 0 ? $"↓ на {-move}" : "без изменений", move > 0 ? "good" : move < 0 ? "bad" : null),
            new("Команда недели", c.Team(tw.Key).City, $"{tw.Value.w}-{tw.Value.l}-{tw.Value.o}"),
            new("Игрок недели", pP.LastRu, $"{pw.Value.g}+{pw.Value.a}"),
        };
        var players = new List<int> { pP.Id };
        if (gw.Key != 0) players.Add(gw.Key);
        Post(c, "Аналитический отдел", "Неделя в НХЛ", head, lead, body.ToString().TrimEnd(), "week", "table", tw.Key, 2, facts, players);
    }

    // ================================================================ «Цифры»: место клуба в лиге (раз в две недели)
    public sealed record TeamNums(string Team, int Gp, double Gf, double Ga, double Sf, double Sa, double XgF, double XgA, double Ppg, double Fo, double Sv, double Hits);

    public static List<TeamNums> TeamStats(Career c)
    {
        var res = new List<TeamNums>();
        foreach (var row in c.Table.Values)
        {
            if (row.Gp == 0) continue;
            var sk = c.Skaters.Where(kv => c.Data.PlayerById.TryGetValue(kv.Key, out var p) && p.Team == row.Team).Select(kv => kv.Value).ToList();
            var gs = c.Goalies.Where(kv => c.Data.PlayerById.TryGetValue(kv.Key, out var p) && p.Team == row.Team).Select(kv => kv.Value).ToList();
            double gp = row.Gp;
            int fow = sk.Sum(x => x.Fow), fol = sk.Sum(x => x.Fol);
            int sa = gs.Sum(x => x.Sa), ga = gs.Sum(x => x.Ga);
            res.Add(new TeamNums(row.Team, row.Gp, row.Gf / gp, row.Ga / gp, sk.Sum(x => x.S) / gp, sa / gp, sk.Sum(x => x.Ixg) / gp, gs.Sum(x => x.Xga) / gp,
                sk.Sum(x => x.Ppg), fow + fol == 0 ? 0.5 : fow / (double)(fow + fol), sa == 0 ? 0 : 1 - ga / (double)sa, sk.Sum(x => x.Hit) / gp));
        }
        return res;
    }

    public static void Numbers(Career c)
    {
        var all = TeamStats(c);
        var me = all.FirstOrDefault(x => x.Team == c.UserTeam);
        if (me == null || me.Gp < 8) return;
        var rng = Rng(c, 9);
        int Rank(Func<TeamNums, double> f, bool low = false) => (low ? all.OrderBy(f) : all.OrderByDescending(f)).ToList().FindIndex(x => x.Team == c.UserTeam) + 1;
        var m = new List<(string name, string val, int rank, string phrase)>
        {
            ("Голы за матч", F2(me.Gf), Rank(x => x.Gf), "по голам за матч"),
            ("Пропущенные за матч", F2(me.Ga), Rank(x => x.Ga, true), "по надёжности в обороне"),
            ("Броски в створ", F1(me.Sf), Rank(x => x.Sf), "по броскам в створ"),
            ("Доля xG", $"{Math.Round(100 * me.XgF / Math.Max(0.01, me.XgF + me.XgA))}%", Rank(x => x.XgF / Math.Max(0.01, x.XgF + x.XgA)), "по доле ожидаемых голов — качеству моментов"),
            ("Голы в большинстве", $"{me.Ppg:0}", Rank(x => x.Ppg), "по голам в большинстве"),
            ("% отражённых", me.Sv.ToString(".000", System.Globalization.CultureInfo.InvariantCulture), Rank(x => x.Sv), "по проценту отражённых бросков"),
            ("Вбрасывания", $"{Math.Round(me.Fo * 100)}%", Rank(x => x.Fo), "по выигранным вбрасываниям"),
            ("Силовые за матч", F1(me.Hits), Rank(x => x.Hits), "по силовым приёмам"),
        };
        var hi = m.OrderBy(x => x.rank).First();
        var lo = m.OrderByDescending(x => x.rank).First();
        int n = all.Count;
        string head = hi.rank <= 3 ? Pick(rng, $"Цифры: {Ord(hi.rank)} в лиге {hi.phrase}", $"Цифры: в топ-3 {hi.phrase}")
                    : lo.rank >= n - 2 ? $"Цифры: {Ord(lo.rank)} из {n} {lo.phrase}" : $"Цифры: где мы после {N(me.Gp, "матча", "матчей", "матчей")}";
        string lead = $"Сильнее всего — {hi.phrase}: {hi.val}, {Ord(hi.rank)} место из {n}. Слабее всего — {lo.phrase}: {lo.val}, {Ord(lo.rank)}.";
        var body = new System.Text.StringBuilder("## Все показатели\n");
        foreach (var x in m) body.Append($"• {x.name}: {x.val} — {Ord(x.rank)} в лиге.\n");
        body.Append($"\nДоля xG — ожидаемые голы своих бросков против ожидаемых голов соперника. Больше 50% — клуб создаёт больше, чем позволяет. ");
        double luck = (me.Gf - me.XgF) - (me.Ga - me.XgA);
        body.Append(Math.Abs(luck) < 0.25 ? "Голы и моменты сходятся — результаты честные." :
            luck > 0 ? $"Забиваем и спасаемся лучше, чем создаём и позволяем (+{F2(luck)} за матч): часть результата — реализация и вратарь, такое не всегда держится." :
                       $"Результаты хуже моментов ({F2(luck)} за матч): игра лучше таблицы, на дистанции должно выровняться.");
        var facts = m.Take(6).Select(x => new MsgFact(x.name, x.val, $"{Ord(x.rank)} в лиге", x.rank <= 8 ? "good" : x.rank >= n - 7 ? "bad" : null)).ToList();
        Post(c, "Аналитический отдел", "Цифры", head, lead, body.ToString().TrimEnd(), "week", null, c.UserTeam, 1, facts);
    }

    // ================================================================ «Пресса»: сюжеты вокруг клуба
    public static void Press(Career c)
    {
        string me = c.UserTeam;
        var row = c.Table[me];
        if (row.Gp < 5) return;
        var rng = Rng(c, 3);
        // не чаще раза в 4 дня
        if (c.Messages.Any(m => m.Tag == "Пресса" && c.Today.DayNumber - m.Date.DayNumber < 4)) return;
        bool Recent(string key, int days) => c.Messages.Any(m => (m.Tag == "Пресса" || m.Tag == "Клуб") && m.Link == (key.Contains(':') ? key : "press:" + key) && c.Today.DayNumber - m.Date.DayNumber < days);
        var roster = c.Roster(me).ToList();
        var cap = roster.FirstOrDefault(p => p.Fx?.Cap == "C") ?? roster.FirstOrDefault(p => p.Fx?.Cap == "A");
        string capName = cap?.NameRu ?? "капитан";
        int sn = row.Streak.Length > 1 && int.TryParse(row.Streak[1..].TrimStart('О'), out var nn) ? nn : 0;

        var cands = new List<(double w, Action post)>();
        if (row.Streak.StartsWith("П") && sn >= 3 && !Recent("crisis", 10))
            cands.Add((3, () =>
            {
                var last = c.TeamGames(me).Where(g => g.Played).OrderByDescending(g => g.Date).Take(sn).ToList();
                int gf = last.Sum(g => g.Home == me ? g.Result.HomeGoals : g.Result.AwayGoals), ga = last.Sum(g => g.Home == me ? g.Result.AwayGoals : g.Result.HomeGoals);
                Post(c, "Пресса", "Пресса", Pick(rng, $"«Где выход?»: {N(sn, "поражение", "поражения", "поражений")} подряд", $"Пресса: {Q(c, me)} в штопоре", $"{sn} без побед: что происходит?"),
                    $"Шайбы за серию — {gf}:{ga}. Журналисты спрашивают о переменах в составе, в раздевалке говорят о характере.",
                    $"Серия началась {last.Last().Date.Day} {Month(last.Last().Date)}. За это время клуб забивал {F1(gf / (double)sn)} за матч и пропускал {F1(ga / (double)sn)}.\n\n" +
                    "Пресса ждёт ответа от тренера: перестановки в звеньях, свежая кровь из запаса или терпение? Решать вам — звенья в разделе «Состав».",
                    "board", "press:crisis", me, 2, null, cap != null ? new List<int> { cap.Id } : null,
                    Pick(rng, "Мы сами себя загнали в эту яму, сами и выберемся. Никто не опускает руки.", "Нужно просто начать делать простые вещи. Бросать, добивать, играть друг за друга.", "Неприятно, но паники нет. Следующий матч — новый шанс."), capName);
            }));
        if (row.Streak.StartsWith("В") && sn >= 4 && !Recent("run", 10))
            cands.Add((2.5, () =>
            {
                Post(c, "Пресса", "Пресса", Pick(rng, $"{N(sn, "победа", "победы", "побед")} подряд: {Q(c, me)} разогнались", $"Пресса: {Q(c, me)} на ходу", $"Серия {sn}: соперники уже нервничают"),
                    $"Клуб выиграл {N(sn, "матч", "матча", "матчей")} подряд. Аналитики осторожны, болельщики — нет.",
                    $"Сейчас {row.W}-{row.L}-{row.Otl}, {N(row.Pts, "очко", "очка", "очков")}. Главный вопрос прессы — выдержит ли состав темп, и не пора ли дать отдых лидерам: нагрузка видна в колонке «Свежесть».",
                    "board", "press:run", me, 2, null, cap != null ? new List<int> { cap.Id } : null,
                    Pick(rng, "Не смотрим на серию. Каждый матч — с нуля.", "Хорошо, что выигрываем, но мы знаем, где можем лучше.", "Раздевалка как семья. Отсюда и результат."), capName);
            }));
        foreach (var p in roster.Where(p => !p.IsGoalie).OrderByDescending(p => p.Ovr).Take(4))
        {
            var f = PlayerForm.Of(c, p.Id);
            var s = c.Skaters.GetValueOrDefault(p.Id);
            if (s == null || f.Games < 5) continue;
            int recentPts = RecentPoints(c, p, 6, out int recentGp);
            if (f.Word is "Спад" or "Провал" && !Recent($"player:{p.Id}", 21))
                cands.Add((2.2, () => Post(c, "Пресса", "Пресса", Pick(rng, $"{p.LastRu}: что случилось?", $"{p.LastRu} ищет игру", $"{p.LastRu} в спаде: пресса задаёт вопросы"),
                    $"{N(recentPts, "очко", "очка", "очков")} в последних {N(recentGp, "матче", "матчах", "матчах")} — заметно ниже его обычного уровня.",
                    $"За сезон у него {s.G}+{s.A} в {N(s.Gp, "матче", "матчах", "матчах")}, но последние недели — не его. Средняя оценка в последних матчах {F2(f.Recent)}.\n\n" +
                    "Серии спадов у лучших игроков случаются каждый сезон и обычно проходят сами. Но если он ещё и устал — стоит посмотреть на его минуты.",
                    "board", $"player:{p.Id}", me, 1, new List<MsgFact> { new("Последние матчи", $"{recentPts} оч.", $"в {recentGp}", "bad"), new("Сезон", $"{s.G}+{s.A}", $"{s.Gp} матчей") },
                    new List<int> { p.Id })));
            if (f.Word == "Отличная" && recentPts >= recentGp && !Recent($"player:{p.Id}", 21))
                cands.Add((2.0, () => Post(c, "Пресса", "Пресса", Pick(rng, $"{p.LastRu} в огне", $"Невозможно остановить: {p.LastRu}", $"{p.LastRu} тащит команду"),
                    $"{N(recentPts, "очко", "очка", "очков")} в последних {N(recentGp, "матче", "матчах", "матчах")}. Лучший отрезок его сезона.",
                    $"За сезон — {s.G}+{s.A} в {N(s.Gp, "матче", "матчах", "матчах")}, ожидаемые голы его бросков — {F1(s.Ixg)}.\n\n" +
                    (s.G > s.Ixg + 3 ? "Забивает заметно больше, чем «положено» по моментам: часть — мастерство, часть — удача, которая рано или поздно кончится." : "Голы подкреплены моментами: это не случайность, а игра."),
                    "board", $"player:{p.Id}", me, 1, new List<MsgFact> { new("Последние матчи", $"{recentPts} оч.", $"в {recentGp}", "good"), new("Сезон", $"{s.G}+{s.A}", $"{s.Gp} матчей"), new("xG", F1(s.Ixg), "по броскам в створ") },
                    new List<int> { p.Id })));
        }
        foreach (var p in roster.Where(p => !p.IsGoalie && (p.Age(c.Today) ?? 30) <= 22 && Past(c, p).gp < 82))
        {
            var s = c.Skaters.GetValueOrDefault(p.Id);
            if (s == null || s.Gp < 8 || s.Pts < s.Gp * 0.55 || Recent($"player:{p.Id}", 40)) continue;
            cands.Add((2.4, () => Post(c, "Пресса", "Пресса", Pick(rng, $"Новичок, о котором говорят: {p.LastRu}", $"{p.LastRu}: {N(p.Age(c.Today) ?? 20, "год", "года", "лет")} и уже {N(s.Pts, "очко", "очка", "очков")}"),
                $"{p.NameRu} в {N(p.Age(c.Today) ?? 20, "год", "года", "лет")} набирает {F2(s.Pts / (double)s.Gp)} очка за матч. Скауты других клубов уже переписывают отчёты.",
                $"{s.G}+{s.A} в {N(s.Gp, "матче", "матчах", "матчах")}, в среднем {(s.Gp > 0 && s.Toi > 0 ? $"{s.Toi / s.Gp / 60}:{s.Toi / s.Gp % 60:00}" : "—")} на льду.\n\n" +
                "Молодые растут от минут и роли: чем больше доверия, тем быстрее приближаются к своему потолку.",
                "board", $"player:{p.Id}", me, 2, new List<MsgFact> { new("Очки", $"{s.Pts}", $"{s.G}+{s.A}", "good"), new("За матч", F2(s.Pts / (double)s.Gp)) }, new List<int> { p.Id })));
        }
        var g1 = c.Lineups[me].G1;
        if (c.Goalies.TryGetValue(g1, out var gl) && gl.Gp >= 8 && gl.Gsax >= 5 && !Recent($"player:{g1}", 30))
        {
            var gp = c.Data.PlayerById[g1];
            cands.Add((2.3, () => Post(c, "Пресса", "Пресса", Pick(rng, $"Стена: {gp.LastRu}", $"{gp.LastRu} держит команду"),
                $"{gp.NameRu} спас на {F1(gl.Gsax)} гола больше, чем пропустил бы средний вратарь на тех же бросках.",
                $"Процент отражённых — {gl.SvPct.ToString(".000", System.Globalization.CultureInfo.InvariantCulture)} в {N(gl.Gp, "матче", "матчах", "матчах")}. " +
                "По данным НХЛ сезон вратаря наполовину талант и наполовину удача — посмотрим, сколько продлится.",
                "board", $"player:{g1}", me, 2, new List<MsgFact> { new("Сейвы сверх ожидаемых", "+" + F1(gl.Gsax), null, "good"), new("% отражённых", gl.SvPct.ToString(".000", System.Globalization.CultureInfo.InvariantCulture)) }, new List<int> { g1 })));
        }
        if (cands.Count == 0)
        {
            // спокойная неделя: изредка — жизнь клуба вне льда (без влияния на игру)
            if (rng.NextDouble() < 0.06 && cap != null && !Recent("club", 25))
                Post(c, "Пресса", "Клуб", Pick(rng, $"{cap.LastRu} навестил детскую больницу", $"Автограф-сессия: {cap.LastRu} и очередь до улицы", $"{cap.LastRu} провёл открытую тренировку для школьников"),
                    "Клуб вне льда: игроки встретились с болельщиками.", "Пресс-служба клуба благодарит всех, кто пришёл. Следующая встреча — после домашней серии.",
                    "board", "press:club", me, 0, null, new List<int> { cap.Id });
            return;
        }
        if (rng.NextDouble() > 0.45) return;
        cands.OrderByDescending(x => x.w + rng.NextDouble()).First().post();
    }

    static int RecentPoints(Career c, PlayerData p, int n, out int gp)
    {
        int pts = 0; gp = 0;
        foreach (var g in c.TeamGames(p.Team).Where(g => g.Played).OrderByDescending(g => g.Date))
        {
            var dressed = g.Home == p.Team ? g.Result.HomeDressed : g.Result.AwayDressed;
            if (!dressed.Contains(p.Id)) continue;
            pts += g.Result.Goals.Count(e => e.Kind != "so" && (e.Scorer == p.Id || e.A1 == p.Id || e.A2 == p.Id));
            if (++gp >= n) break;
        }
        return pts;
    }

    static readonly string[] MonthsGen = { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };
    static string Month(DateOnly d) => MonthsGen[d.Month - 1];

    /// <summary>Новости нового дня: вечер в НХЛ, по понедельникам — неделя, раз в две недели — цифры, иногда — пресса.</summary>
    public static void NewDay(Career c)
    {
        if (c.Phase == Phase.Finished) return;
        Daily(c);
        if (c.Phase != Phase.Regular) return;
        if (c.Today.DayOfWeek == DayOfWeek.Monday) Weekly(c);
        if (c.Today.DayOfWeek == DayOfWeek.Thursday && (c.Today.DayNumber / 7) % 2 == 0) Numbers(c);
        Press(c);
    }
}
