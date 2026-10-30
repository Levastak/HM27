namespace HM27.Core;

/// <summary>Приз или звание: игрок месяца, приз сезона.</summary>
public sealed class Honor
{
    public int Season { get; set; }
    public DateOnly Date { get; set; }
    public string Kind { get; set; }      // pom, gom, rom (игрок/вратарь/новичок месяца), hart, ross, richard, vezina, norris, calder
    public string Name { get; set; }      // «Игрок месяца: ноябрь», «Харт Трофи»
    public int Player { get; set; }
    public string Text { get; set; }
}

/// <summary>
/// Сюжеты лиги (этап «Новости»): превью своего матча с прогнозами трёх экспертов, рейтинг силы по вторникам,
/// игроки месяца и гонка за призами, призы сезона, погони за отметками (50 голов, 100 очков), тренеры под угрозой
/// (и увольнения у клубов ИИ — единственное, что здесь меняет игру: новый главный тренер со своим стилем).
/// У каждого материала — автор со своей манерой (Voices); цитаты — по характеру (Quotes).
/// </summary>
public static partial class News
{
    static string By(Career c, string kind) => Voices.By(Voices.Get(c, kind));

    // ================================================================ превью: три эксперта о матче
    public sealed record ExpertPick(Journalist Who, string Team, string Why);

    /// <summary>Прогнозы экспертов на матч (детерминированно): аналитик — по модели, эксперт — по форме, местный репортёр — сердцем, но честно.</summary>
    public static List<ExpertPick> Picks(Career c, ScheduledGame g)
    {
        var o = c.Preview(g);
        string h = g.Home, a = g.Away;
        var res = new List<ExpertPick>();
        // аналитик: прогноз и моменты
        var st = TeamStats(c).ToDictionary(x => x.Team);
        double Share(string t) => st.TryGetValue(t, out var x) && x.XgF + x.XgA > 0 ? x.XgF / (x.XgF + x.XgA) : 0.5;
        string fav = o.PHome >= 0.5 ? h : a;
        double pf = Math.Max(o.PHome, 1 - o.PHome);
        string an = Share(fav) >= 0.52 ? $"у {Q(c, fav)} {Math.Round(100 * Share(fav))}% ожидаемых голов в сезоне — они создают больше, чем позволяют"
                  : pf >= 0.6 ? "модель даёт им заметный перевес по составу" : "почти равные шансы, но небольшой перевес — у них";
        res.Add(new ExpertPick(Voices.Get(c, Voices.STATS), fav, an));
        // эксперт: форма последних пяти, характер, дома стены помогают
        int Form(string t) => c.TeamGames(t).Where(x => x.Played).OrderByDescending(x => x.Date).Take(5)
            .Sum(x => (x.Home == t) == (x.Result.HomeGoals > x.Result.AwayGoals) ? 2 : x.Result.End != "REG" ? 1 : 0);
        int fh = Form(h), fa = Form(a);
        string pt = fh == fa ? h : fh > fa ? h : a;
        int wins = c.TeamGames(pt).Where(x => x.Played).OrderByDescending(x => x.Date).Take(5).Count(x => (x.Home == pt) == (x.Result.HomeGoals > x.Result.AwayGoals));
        string pw = fh == fa ? "дома и стены помогают, а в равном матче решает характер" : $"{Q(c, pt)} выиграли {wins} из последних пяти — форма важнее любых таблиц";
        res.Add(new ExpertPick(Voices.Get(c, Voices.PUNDIT), pt, pw));
        // местный репортёр: за своих, если шансы не совсем малы
        string me = c.UserTeam;
        bool mine = g.Home == me || g.Away == me;
        if (mine)
        {
            double pm = g.Home == me ? o.PHome : 1 - o.PHome;
            string opp = g.Home == me ? g.Away : g.Home;
            var hot = c.Roster(me).Where(p => !p.IsGoalie && !c.Injured.Contains(p.Id)).OrderByDescending(p => PlayerForm.Of(c, p.Id).Recent).FirstOrDefault();
            var out1 = c.Injured.Select(id => c.Data.PlayerById[id]).Where(p => p.Team == me).OrderByDescending(p => p.Ovr).FirstOrDefault();
            if (pm >= 0.38)
                res.Add(new ExpertPick(Voices.Get(c, Voices.BEAT), me, hot != null && PlayerForm.Of(c, hot.Id).Word is "Отличная" or "Хорошая"
                    ? $"{hot.LastRu} в отличной форме — на тренировках это видно" : g.Home == me ? "дома команда играет смелее" : "команда собрана и знает, что делать"));
            else
                res.Add(new ExpertPick(Voices.Get(c, Voices.BEAT), opp, out1 != null ? $"без {out1.LastRu} будет тяжело — сердце за наших, но голова против" : "честно: соперник сейчас сильнее"));
        }
        return res;
    }

    /// <summary>Превью в день своего матча: три прогноза, ключевое противостояние, кто не сыграет.</summary>
    public static void Preview(Career c)
    {
        var g = c.UserGameToday();
        if (g == null || c.Messages.Any(m => m.Tag == "Превью" && m.Link == $"game:{g.Id}")) return;
        string me = c.UserTeam, opp = g.Home == me ? g.Away : g.Home;
        var picks = Picks(c, g);
        int forUs = picks.Count(p => p.Team == me);
        var rng = new Random(unchecked(c.Seed * 811 + g.Id));
        string head = forUs == 3 ? Pick(rng, $"Эксперты единодушны: {Q(c, me)} — фаворит", $"Все трое за нас: превью матча с {Q(c, opp)}")
                    : forUs == 0 ? Pick(rng, $"Никто не верит: превью матча с {Q(c, opp)}", $"Эксперты против нас: {Q(c, opp)} — фаворит")
                    : Pick(rng, $"Мнения разделились: {Q(c, me)} — {Q(c, opp)}", $"Превью: {Q(c, opp)} {(g.Home == me ? "в гостях" : "дома")}, эксперты спорят");
        var body = new System.Text.StringBuilder("## Прогнозы\n");
        foreach (var p in picks) body.Append($"• {p.Who.Name} ({p.Who.Outlet}): {Q(c, p.Team)} — {p.Why}.\n");
        // ключевое противостояние: наш лучший бомбардир против их вратаря
        var star = c.Roster(me).Where(p => !p.IsGoalie && !c.Injured.Contains(p.Id)).OrderByDescending(p => c.Skaters.GetValueOrDefault(p.Id)?.Pts ?? 0).FirstOrDefault();
        var og = c.Lineups[opp].G1;
        if (star != null && c.Skaters.ContainsKey(star.Id) && c.Data.PlayerById.TryGetValue(og, out var gk))
        {
            var s = c.Skaters.GetValueOrDefault(star.Id); var gs = c.Goalies.GetValueOrDefault(og);
            body.Append($"\n## Ключевое противостояние\n{star.NameRu} ({(s == null ? "—" : $"{s.G}+{s.A}")}) против {gk.NameRu} ({(gs == null || gs.Gp == 0 ? "ещё не играл" : $"{gs.SvPct.ToString(".000", System.Globalization.CultureInfo.InvariantCulture)} в {N(gs.Gp, "матче", "матчах", "матчах")}")}).\n");
        }
        var outs = c.Injured.Select(id => c.Data.PlayerById[id]).Where(p => p.Team == me || p.Team == opp).OrderByDescending(p => p.Ovr).Take(4).ToList();
        if (outs.Count > 0) body.Append($"\n## Не сыграют\n{string.Join(", ", outs.Select(p => $"{p.NameRu} ({c.Team(p.Team).City})"))}.\n");
        var facts = picks.Select(p => new MsgFact(p.Who.Name, c.Team(p.Team).City, p.Who.Outlet, p.Team == me ? "good" : "bad")).ToList();
        Post(c, By(c, Voices.BEAT), "Превью", head, $"{c.Team(me).Name} {(g.Home == me ? "принимает" : "в гостях у")} «{c.Team(opp).Name}». Трое экспертов — три прогноза.",
            body.ToString().TrimEnd(), "league", $"game:{g.Id}", me, 1, facts, star != null ? new List<int> { star.Id } : null);
    }

    // ================================================================ рейтинг силы (вторник)
    public sealed record PowerRow(string Team, int Rank, int Prev, double Score, string Note);

    /// <summary>Рейтинг силы: очки сезона, последние 10 матчей и доля ожидаемых голов; с местом неделю назад.</summary>
    public static List<PowerRow> Power(Career c)
    {
        var st = TeamStats(c).ToDictionary(x => x.Team);
        var list = new List<(string t, double s)>();
        foreach (var row in c.Table.Values)
        {
            if (row.Gp == 0) continue;
            var last = row.Last.TakeLast(10).ToList();
            double l10 = last.Count == 0 ? 0.5 : last.Sum(x => x == 'W' ? 2 : x == 'O' ? 1 : 0) / (2.0 * last.Count);
            double xs = st.TryGetValue(row.Team, out var x) && x.XgF + x.XgA > 0 ? x.XgF / (x.XgF + x.XgA) : 0.5;
            list.Add((row.Team, 0.5 * row.PtsPct + 0.3 * l10 + 0.2 * (0.5 + 2 * (xs - 0.5))));
        }
        var ranked = list.OrderByDescending(x => x.s).ToList();
        var used = new HashSet<string>();
        return ranked.Select((x, i) => new PowerRow(x.t, i + 1, c.PowerWeek.GetValueOrDefault(x.t, i + 1), x.s, PowerNote(c, x.t, i + 1, c.PowerWeek.GetValueOrDefault(x.t, i + 1), st, i < 10 ? used : null))).ToList();
    }

    /// <summary>Комментарий к строчке рейтинга: самый яркий факт клуба; одинаковые комментарии в десятке не повторяются (used).</summary>
    static string PowerNote(Career c, string t, int rank, int prev, Dictionary<string, TeamNums> st, HashSet<string> used = null)
    {
        var row = c.Table[t];
        int sn = row.Streak.Length > 1 && int.TryParse(row.Streak[1..].TrimStart('О'), out var n) ? n : 0;
        var top = c.Roster(t).Where(p => !p.IsGoalie).Select(p => (p, s: c.Skaters.GetValueOrDefault(p.Id))).Where(x => x.s != null).OrderByDescending(x => x.s.Pts).FirstOrDefault();
        var x = st.GetValueOrDefault(t);
        var cands = new List<(string kind, string text)>();
        if (row.Streak.StartsWith("В") && sn >= 4) cands.Add(("streak", $"{N(sn, "победа", "победы", "побед")} подряд — сейчас их не остановить"));
        if (row.Streak.StartsWith("П") && sn >= 4) cands.Add(("streak", $"{N(sn, "поражение", "поражения", "поражений")} подряд — в раздевалке тяжело"));
        if (c.PowerWeek.Count > 0 && prev - rank >= 4) cands.Add(("move", $"взлёт на {N(prev - rank, "строчку", "строчки", "строчек")} за неделю"));
        if (c.PowerWeek.Count > 0 && rank - prev >= 4) cands.Add(("move", $"падение на {N(rank - prev, "строчку", "строчки", "строчек")} за неделю"));
        if (x != null && x.Gf - x.XgF > 0.4) cands.Add(("luck", "забивают больше, чем создают — реализация может остыть"));
        if (x != null && x.XgF - x.Gf > 0.4) cands.Add(("unluck", "моментов больше, чем голов — скоро должно пойти"));
        if (x != null && x.Sv >= 0.915) cands.Add(("sv", $"вратари держат {x.Sv.ToString(".000", System.Globalization.CultureInfo.InvariantCulture)} — фундамент результата"));
        if (x != null && x.Ga <= 2.5) cands.Add(("def", $"пропускают {F2(x.Ga)} за матч — одна из лучших оборон"));
        if (x != null && x.Gf >= 3.6) cands.Add(("off", $"забивают {F2(x.Gf)} за матч — атака на ходу"));
        if (top.s != null) cands.Add(("star", $"{top.p.LastRu} — {N(top.s.Pts, "очко", "очка", "очков")}, мотор команды"));
        cands.Add(("plain", rank <= 10 ? "держится в верхней части без лишнего шума" : "крепкий середняк"));
        var pick = cands.FirstOrDefault(cc => used == null || cc.kind is "star" or "plain" || !used.Contains(cc.kind));
        used?.Add(pick.kind);
        return pick.text;
    }

    public static void PowerRankings(Career c)
    {
        if (c.Table.Values.Count(r => r.Gp >= 8) < 28) return;
        var pr = Power(c);
        var me = pr.First(x => x.Team == c.UserTeam);
        var riser = pr.OrderByDescending(x => x.Prev - x.Rank).First();
        var faller = pr.OrderByDescending(x => x.Rank - x.Prev).First();
        var rng = Rng(c, 21);
        string head = me.Rank <= 3 ? $"Рейтинг силы: {Q(c, c.UserTeam)} — {me.Rank}-е" : c.PowerWeek.Count > 0 && pr[0].Prev != 1 ? $"Рейтинг силы: новый лидер — {Q(c, pr[0].Team)}" : Pick(rng, $"Рейтинг силы: {Q(c, pr[0].Team)} на вершине", $"Рейтинг силы недели: кто растёт, кто падает");
        string Mv(PowerRow x) => x.Prev == x.Rank ? "" : x.Prev > x.Rank ? $" (↑{x.Prev - x.Rank})" : $" (↓{x.Rank - x.Prev})";
        var body = new System.Text.StringBuilder("## Десятка\n");
        foreach (var x in pr.Take(10)) body.Append($"{x.Rank}. {c.Team(x.Team).City}{Mv(x)} — {x.Note}.\n");
        if (me.Rank > 10) body.Append($"\n## Мы\n{me.Rank}. {c.Team(me.Team).City}{Mv(me)} — {me.Note}.\n");
        bool first = c.PowerWeek.Count == 0;
        if (!first) body.Append($"\n## Движение\n• Взлёт недели — {Q(c, riser.Team)}: {riser.Prev} → {riser.Rank}.\n• Падение недели — {Q(c, faller.Team)}: {faller.Prev} → {faller.Rank}.\n");
        body.Append("\nСчитаем по очкам сезона, последним десяти матчам и доле ожидаемых голов: рейтинг о том, кто силён сейчас, а не о таблице.");
        // эксперт-ветеран спорит с цифрами
        var lucky = TeamStats(c).OrderByDescending(x => x.Gf - x.XgF).First();
        var pundit = Voices.Get(c, Voices.PUNDIT);
        string quote = Pick(rng, $"Цифры говорят, что {c.Team(lucky.Team).City} везёт. А я говорю — у них характер. Посмотрим, кто прав к весне.",
            "Рейтинги, ожидаемые голы… В моё время считали только шайбы в воротах. Кто забивает — тот и сильный.",
            $"Не верю я в эти таблицы. Покажите мне, как {c.Team(pr[0].Team).City} сыграет в плей-офф, тогда поговорим.");
        foreach (var x in pr) c.PowerWeek[x.Team] = x.Rank;
        Post(c, By(c, Voices.STATS), "Рейтинг силы", head, $"Лидер — {Q(c, pr[0].Team)}. Мы — {me.Rank}-е{(!first && me.Prev != me.Rank ? $", неделю назад были {me.Prev}-е" : "")}.",
            body.ToString().TrimEnd(), "week", "power", pr[0].Team, 1,
            first ? new List<MsgFact> { new("Мы", $"{me.Rank}-е"), new("Лидер", c.Team(pr[0].Team).City) }
                  : new List<MsgFact> { new("Мы", $"{me.Rank}-е", me.Prev == me.Rank ? "без изменений" : me.Prev > me.Rank ? $"↑ на {me.Prev - me.Rank}" : $"↓ на {me.Rank - me.Prev}", me.Prev >= me.Rank ? "good" : "bad"),
                new("Лидер", c.Team(pr[0].Team).City), new("Взлёт", c.Team(riser.Team).City, $"↑ на {riser.Prev - riser.Rank}") },
            null, quote, $"{pundit.Name}, {pundit.Outlet}");
    }

    // ================================================================ игроки месяца и гонка за призами
    /// <summary>Гонка за призами сейчас: (приз, лидеры — игрок и строка цифр).</summary>
    public static List<(string award, string what, List<(int id, string line)> top)> Races(Career c, int n = 3)
    {
        var res = new List<(string, string, List<(int, string)>)>();
        var sk = c.Skaters.Where(kv => kv.Value.Gp > 0 && c.Data.PlayerById.ContainsKey(kv.Key)).ToList();
        var teamPts = c.Table.ToDictionary(kv => kv.Key, kv => kv.Value.PtsPct);
        string T(int id) => c.Data.PlayerById[id].Team;
        res.Add(("Харт Трофи", "самому ценному игроку", sk.OrderByDescending(kv => kv.Value.Pts * (0.8 + 0.6 * teamPts.GetValueOrDefault(T(kv.Key), 0.5))).Take(n)
            .Select(kv => (kv.Key, $"{kv.Value.G}+{kv.Value.A}, {c.Team(T(kv.Key)).City}")).ToList()));
        res.Add(("Арт Росс", "лучшему бомбардиру", sk.OrderByDescending(kv => kv.Value.Pts).ThenByDescending(kv => kv.Value.G).Take(n).Select(kv => (kv.Key, $"{kv.Value.Pts} ({kv.Value.G}+{kv.Value.A})")).ToList()));
        res.Add(("Морис Ришар", "лучшему снайперу", sk.OrderByDescending(kv => kv.Value.G).Take(n).Select(kv => (kv.Key, N(kv.Value.G, "гол", "гола", "голов"))).ToList()));
        res.Add(("Норрис Трофи", "лучшему защитнику", sk.Where(kv => c.Data.PlayerById[kv.Key].IsDefense).OrderByDescending(kv => kv.Value.Pts + 0.3 * kv.Value.Pm).Take(n)
            .Select(kv => (kv.Key, $"{kv.Value.G}+{kv.Value.A}, {(kv.Value.Pm > 0 ? "+" : "")}{kv.Value.Pm}")).ToList()));
        int minGp = Math.Max(5, c.Table.Values.Max(r => r.Gp) / 3);
        res.Add(("Везина Трофи", "лучшему вратарю", c.Goalies.Where(kv => kv.Value.Gp >= minGp && c.Data.PlayerById.ContainsKey(kv.Key)).OrderByDescending(kv => kv.Value.Gsax).Take(n)
            .Select(kv => (kv.Key, $"{kv.Value.SvPct.ToString(".000", System.Globalization.CultureInfo.InvariantCulture)}, {(kv.Value.Gsax >= 0 ? "+" : "")}{F1(kv.Value.Gsax)} сверх ожидаемых")).ToList()));
        res.Add(("Колдер Трофи", "лучшему новичку", sk.Where(kv => Rookie(c, c.Data.PlayerById[kv.Key])).OrderByDescending(kv => kv.Value.Pts).Take(n)
            .Select(kv => (kv.Key, $"{kv.Value.G}+{kv.Value.A}, {c.Team(T(kv.Key)).City}")).ToList()));
        return res;
    }

    static bool Rookie(Career c, PlayerData p) => !p.IsGoalie && Past(c, p).gp < 25 && (p.Age(c.Today) ?? 30) <= 25;

    /// <summary>1-го числа: игрок, вратарь и новичок прошлого месяца + гонка за призами.</summary>
    public static void Monthly(Career c)
    {
        if (c.Today.Day != 1 || c.Phase != Phase.Regular) return;
        var from = c.Today.AddMonths(-1);
        var games = c.Games.Where(g => g.Played && !g.Playoff && g.Date >= from && g.Date < c.Today).ToList();
        if (games.Count < 120) return;
        string month = MonthsNom[from.Month - 1];
        var pts = new Dictionary<int, (int g, int a)>();
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
        var pom = pts.OrderByDescending(kv => kv.Value.g + kv.Value.a).ThenByDescending(kv => kv.Value.g).First();
        var gom = gk.Where(kv => kv.Value.gp >= 6 && kv.Value.sa > 0).OrderByDescending(kv => 1 - kv.Value.ga / (double)kv.Value.sa).FirstOrDefault();
        var rom = pts.Where(kv => c.Data.PlayerById.TryGetValue(kv.Key, out var p) && Rookie(c, p)).OrderByDescending(kv => kv.Value.g + kv.Value.a).FirstOrDefault();
        var rng = Rng(c, 31);
        void Honor(string kind, string name, int id, string text) => c.Honors.Add(new Honor { Season = c.Season, Date = c.Today, Kind = kind, Name = name, Player = id, Text = text });
        var pP = c.Data.PlayerById[pom.Key];
        Honor("pom", $"Игрок месяца: {month}", pom.Key, $"{pom.Value.g}+{pom.Value.a}");
        var body = new System.Text.StringBuilder($"## Призы месяца\n• Игрок — {pP.NameRu} ({c.Team(pP.Team).City}): {pom.Value.g}+{pom.Value.a}.\n");
        var players = new List<int> { pom.Key };
        if (gom.Key != 0)
        {
            var g = c.Data.PlayerById[gom.Key];
            string sv = (1 - gom.Value.ga / (double)gom.Value.sa).ToString(".000", System.Globalization.CultureInfo.InvariantCulture);
            Honor("gom", $"Вратарь месяца: {month}", gom.Key, sv);
            body.Append($"• Вратарь — {g.NameRu} ({c.Team(g.Team).City}): {sv} в {N(gom.Value.gp, "матче", "матчах", "матчах")}.\n");
            players.Add(gom.Key);
        }
        if (rom.Key != 0)
        {
            var r = c.Data.PlayerById[rom.Key];
            Honor("rom", $"Новичок месяца: {month}", rom.Key, $"{rom.Value.g}+{rom.Value.a}");
            body.Append($"• Новичок — {r.NameRu} ({c.Team(r.Team).City}): {rom.Value.g}+{rom.Value.a}.\n");
            players.Add(rom.Key);
        }
        body.Append("\n## Гонка за призами\n");
        foreach (var (award, what, top) in Races(c))
            if (top.Count > 0) body.Append($"• {award} ({what}): " + string.Join("; ", top.Select((x, i) => $"{i + 1}. {c.Data.PlayerById[x.id].LastRu} — {x.line}")) + ".\n");
        string mine = pP.Team == c.UserTeam ? "Наш игрок — лучший в лиге за месяц." : "";
        Post(c, By(c, Voices.STATS), "Лига", Pick(rng, $"{pP.LastRu} — игрок {MonthsGen2[from.Month - 1]}", $"Игрок месяца: {pP.LastRu}", $"{month}: лучший — {pP.LastRu}"),
            $"{pP.NameRu} набрал {N(pom.Value.g + pom.Value.a, "очко", "очка", "очков")} ({pom.Value.g}+{pom.Value.a}) за {MonthsPrep[from.Month - 1]}. {mine}".Trim(),
            body.ToString().TrimEnd(), "league", $"player:{pom.Key}", pP.Team, pP.Team == c.UserTeam ? 2 : 1,
            new List<MsgFact> { new("Игрок", pP.LastRu, $"{pom.Value.g}+{pom.Value.a}", pP.Team == c.UserTeam ? "good" : null) }, players,
            Quotes.Of(c, pom.Key, "award", rng), pP.NameRu);
    }

    /// <summary>Призы сезона — после регулярки.</summary>
    public static void SeasonAwards(Career c)
    {
        if (c.Honors.Any(h => h.Season == c.Season && h.Kind == "hart")) return;
        var races = Races(c);
        var rng = Rng(c, 41);
        var body = new System.Text.StringBuilder("## Призы сезона\n");
        var players = new List<int>();
        string[] kinds = { "hart", "ross", "richard", "norris", "vezina", "calder" };
        for (int i = 0; i < races.Count; i++)
        {
            var (award, what, top) = races[i];
            if (top.Count == 0) continue;
            var p = c.Data.PlayerById[top[0].id];
            c.Honors.Add(new Honor { Season = c.Season, Date = c.Today, Kind = kinds[i], Name = award, Player = p.Id, Text = top[0].line });
            body.Append($"• {award} ({what}) — {p.NameRu}, {c.Team(p.Team).City}: {top[0].line}." + (top.Count > 1 ? $" Финалисты: {string.Join(", ", top.Skip(1).Select(x => c.Data.PlayerById[x.id].LastRu))}." : "") + "\n");
            players.Add(p.Id);
        }
        var hart = c.Data.PlayerById[races[0].top[0].id];
        int ours = players.Count(id => c.Data.PlayerById[id].Team == c.UserTeam);
        Post(c, By(c, Voices.WIRE), "Лига", $"Харт Трофи — {hart.LastRu}", $"Лига вручила призы регулярного чемпионата.{(ours > 0 ? $" У нас — {N(ours, "приз", "приза", "призов")}." : "")}",
            body.ToString().TrimEnd(), "league", $"player:{hart.Id}", hart.Team, 3, null, players.Distinct().Take(4).ToList(), Quotes.Of(c, hart.Id, "award", rng), hart.NameRu);
    }

    // ================================================================ погоня за отметкой
    public static void Chases(Career c)
    {
        if (c.Phase != Phase.Regular) return;
        foreach (var (id, s) in c.Skaters)
        {
            if (s.Gp < 20 || !c.Data.PlayerById.TryGetValue(id, out var p)) continue;
            double gPace = s.G * 82.0 / Math.Max(1, c.Table[p.Team].Gp), pPace = s.Pts * 82.0 / Math.Max(1, c.Table[p.Team].Gp);
            // погоня — только у тех, кто идёт быстрее отметки: начало (45 голов / 90 очков) и сама отметка
            foreach (var (val, pace, marks, goal, what) in new[] { (s.G, gPace, new[] { 45, 50 }, 50, "голов"), (s.Pts, pPace, new[] { 90, 100 }, 100, "очков") })
                foreach (int m in marks)
                {
                    string key = $"{c.Season}:{id}:{what}:{m}";
                    if (val < m || c.Chases.Contains(key)) continue;
                    c.Chases.Add(key);
                    if (m < goal && pace < goal + 2) continue;
                    // начало погони у чужих — только у тройки лучших в лиге по этому показателю
                    if (m < goal && p.Team != c.UserTeam && c.Skaters.Count(kv => (what == "голов" ? kv.Value.G : kv.Value.Pts) > val) >= 3) continue;
                    var rng = new Random(unchecked(c.Seed * 997 + id + m));
                    bool reached = m == goal;
                    int left = goal - val;
                    string head = reached ? Pick(rng, $"{p.LastRu}: {goal} {what}!", $"Есть {goal}: {p.LastRu} вошёл в историю сезона")
                                : m == goal - 1 ? $"{p.LastRu}: до {goal} {what} — один шаг" : $"Погоня за {goal}: у {p.LastRu} уже {val}";
                    string lead = reached ? $"{p.NameRu} набрал {goal} {what} в {N(s.Gp, "матче", "матчах", "матчах")}."
                                : $"{p.NameRu}: {val} {what} в {N(s.Gp, "матче", "матчах", "матчах")}. В таком темпе — {Math.Round(pace)} к концу сезона; до {goal} — {left}.";
                    bool ours = p.Team == c.UserTeam;
                    Post(c, By(c, ours ? Voices.BEAT : reached ? Voices.WIRE : Voices.TABLOID), "Погоня", head, lead,
                        reached ? $"Отметка {goal} {what} за сезон — редкость: в последние годы её берут единицы. {p.LastRu} сделал это за {N(c.Table[p.Team].Gp, "матч", "матча", "матчей")} {Q(c, p.Team)}."
                                : $"Осталось {N(82 - c.Table[p.Team].Gp, "матч", "матча", "матчей")}. Каждый его выход теперь — повод для заголовков.",
                        "league", $"player:{id}", p.Team, ours || reached ? 2 : 1, null, new List<int> { id }, Quotes.Of(c, id, reached ? "award" : "chase", rng), p.NameRu);
                }
        }
    }

    // ================================================================ тренер под угрозой
    /// <summary>
    /// По понедельникам — клубы ИИ: если последние 15 матчей ниже 38% очков (а ждали большего), кресло тренера шатается:
    /// слухи инсайдера → ультиматум → отставка (не раньше 25 матчей, не больше трёх за сезон). Новый тренер — со своим стилем.
    /// </summary>
    public static void HotSeats(Career c)
    {
        if (c.Phase != Phase.Regular || c.Today.DayOfWeek != DayOfWeek.Monday) return;
        int fired = c.Honors.Count(h => h.Season == c.Season && h.Kind == "fired");
        int rumors = 0; bool firedToday = false;
        foreach (var t in c.Data.Teams)
        {
            if (t.Abbr == c.UserTeam) continue;
            var row = c.Table[t.Abbr];
            if (row.Gp < 15) continue;
            // новый тренер — полтора месяца без разговоров об отставке
            if (c.Honors.Any(h => h.Kind == "fired" && h.Text.StartsWith(t.Abbr + ":") && c.Today.DayNumber - h.Date.DayNumber < 45)) continue;
            var last = c.TeamGames(t.Abbr).Where(g => g.Played && !g.Playoff).OrderByDescending(g => g.Date).Take(15).ToList();
            double pct = last.Sum(g => (g.Home == t.Abbr) == (g.Result.HomeGoals > g.Result.AwayGoals) ? 2 : g.Result.End != "REG" ? 1 : 0) / (2.0 * last.Count);
            int exp = Pundits.DivisionPick(c, t.Abbr);
            int lvl = c.HotSeat.GetValueOrDefault(t.Abbr);
            var coach = Staff.Decider(c, t.Abbr);
            if (coach == null) continue;
            var rng = new Random(unchecked(c.Seed * 61 + c.Today.DayNumber + t.Abbr[0] * 7 + t.Abbr[1]));
            bool bad = pct < 0.36 && (exp <= 4 || row.PtsPct < 0.40);
            if (!bad)
            {
                if (lvl >= 2 && pct >= 0.5)
                    Post(c, By(c, Voices.INSIDER), "Лига", $"{coach.Name} спас кресло в {Q(c, t.Abbr)}", "Серия удачных матчей сняла вопрос об отставке — по крайней мере, пока.",
                        $"Ещё недавно руководство {Q(c, t.Abbr)} обсуждало замену. Теперь, по нашим данным, тренеру дают доработать сезон.", "league", $"club:{t.Abbr}", t.Abbr, 1);
                if (pct >= 0.45) c.HotSeat[t.Abbr] = 0;
                continue;
            }
            // не больше двух новых слухов в неделю и одной отставки: остальные ждут своей очереди
            if (lvl == 0 && rumors >= 2) continue;
            if (lvl >= 2 && (firedToday || row.Gp < 25 || fired >= 3)) continue;
            lvl++;
            c.HotSeat[t.Abbr] = lvl;
            if (lvl == 1) rumors++;
            if (lvl == 1)
                Post(c, By(c, Voices.INSIDER), "Лига", $"Кресло под {coach.Name.Split(' ')[^1]} шатается", $"{Q(c, t.Abbr)}: {Math.Round(100 * pct)}% очков в последних 15 матчах — в клубе ждали другого.",
                    $"Источники в клубе говорят, что терпение руководства не бесконечно. {Q(c, t.Abbr)} — {row.W}-{row.L}-{row.Otl}, а до сезона пресса ставила клуб на {exp}-е место в дивизионе.\n\nСам тренер спокоен — по крайней мере, на людях.",
                    "league", $"club:{t.Abbr}", t.Abbr, 1, null, null, Quotes.Coach(coach, "hot", rng), coach.Name);
            else if (lvl == 2)
                Post(c, By(c, Voices.TABLOID), "Лига", Pick(rng, $"Ультиматум! У {coach.Name.Split(' ')[^1]} две недели", $"Последний шанс для {coach.Name}"), $"По слухам, руководство {Q(c, t.Abbr)} дало тренеру срок.",
                    "Ещё одна неудачная неделя — и в клубе будет новый главный тренер. Игроки на тренировках, говорят, молчат.", "league", $"club:{t.Abbr}", t.Abbr, 1);
            else
            {
                firedToday = true;
                var gone = coach;
                var hired = Staff.Replace(c, t.Abbr);
                AiCoach.InitTeam(c, t.Abbr);
                c.HotSeat[t.Abbr] = 0; fired++;
                c.Honors.Add(new Honor { Season = c.Season, Date = c.Today, Kind = "fired", Name = "Отставка", Player = 0, Text = $"{t.Abbr}:{gone.Name}→{hired.Name}" });
                Post(c, By(c, Voices.WIRE), "Лига", $"{Q(c, t.Abbr)} уволили {gone.Name}", $"Новый главный тренер — {hired.Name}: {Staff.StyleText(hired)}.",
                    $"{gone.Name} уходит после {N(row.Gp, "матча", "матчей", "матчей")}: {row.W}-{row.L}-{row.Otl}.\n\n«{Quotes.Coach(gone, "fired", rng)}» — сказал он на прощание.\n\n" +
                    $"{hired.Name}: тактика — {Staff.Level(hired.Tac).ToLowerInvariant()}, работа с игроками — {Staff.Level(hired.Man).ToLowerInvariant()}. Ждите перемен в звеньях и системе.",
                    "league", $"club:{t.Abbr}", t.Abbr, 2, null, null, Quotes.Coach(hired, "new", rng), hired.Name);
            }
        }
    }

    static readonly string[] MonthsNom = { "январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь" };
    static readonly string[] MonthsGen2 = { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };
    static readonly string[] MonthsPrep = { "январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь" };
}
