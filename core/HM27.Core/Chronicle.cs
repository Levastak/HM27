namespace HM27.Core;

/// <summary>
/// Хроника матча — постановка поверх итога движка (ТЗ 7.4: показ не решает исход).
/// По счёту, броскам в створ и голам раскладываются по времени сейвы, промахи, блоки и удаления:
/// голы в большинстве получают удаление соперника, проигрывающий бросает чаще (эффект счёта),
/// бросают чаще те, у кого выше личный xG. Использует свою случайность и не трогает результат.
/// Затем — оценки игроков за матч (6,0–10,0), как в FM.
/// </summary>
public static class Chronicle
{
    static readonly string[] ShotKinds = { "кистевой", "щелчок", "с ходу", "добивание", "в одно касание", "бросок с неудобной руки", "подставка" };
    static readonly string[] Penalties = { "подножка", "задержка клюшкой", "удар клюшкой", "задержка", "зацеп", "блокировка", "грубость", "толчок на борт", "задержка игры" };

    public static void Build(GameData data, ScheduledGame g, MatchSide home, MatchSide away, MatchResult r, Random rng)
    {
        // движок по сменам сам записал события — остаются оценки
        if (r.Box.Count > 0) { Rate(data, g, r); return; }
        var ev = new List<MatchEvent>();
        int endT = 3600;
        var ot = r.Goals.Where(e => e.Period >= 4 && e.Kind != "so").ToList();
        if (ot.Count > 0) endT = (ot[0].Period - 1) * 1200 + ot[0].Sec;
        else if (r.End == "SO") endT = 3600 + 300;
        int Abs(GoalEvent e) => (e.Period - 1) * 1200 + e.Sec;

        // --- удаления: под каждый гол в большинстве — удаление соперника до гола; ещё немного без последствий
        var pens = new List<(int t, string team)>();
        foreach (var e in r.Goals.Where(e => e.Kind == "pp"))
        {
            string victim = e.Team == g.Home ? g.Away : g.Home;
            pens.Add((Math.Max(5, Abs(e) - rng.Next(8, 110)), victim));
        }
        foreach (var e in r.Goals.Where(e => e.Kind == "sh"))
            pens.Add((Math.Max(5, Abs(e) - rng.Next(8, 100)), e.Team));
        foreach (var (abbr, side) in new[] { (g.Home, home), (g.Away, away) })
        {
            int want = Poisson(rng, 2.9);
            int have = pens.Count(p => p.team == abbr);
            for (int i = have; i < want; i++) pens.Add((rng.Next(30, Math.Min(endT, 3600) - 30), abbr));
        }
        foreach (var (t, team) in pens.OrderBy(p => p.t))
        {
            var side = team == g.Home ? home : away;
            int who = Pick(side.Lineup.Skaters.Distinct(), id => (data.PlayerById[id].IsDefense ? 1.3 : 1.0) * (110 - data.PlayerById[id].Rating("dis")) * side.Lineup.MinutesOf(id), rng);
            ev.Add(new MatchEvent { T = t, Team = team, Kind = "pen", P1 = who, Note = Penalties[rng.Next(Penalties.Length)] });
        }

        // --- броски: сейвы, промахи, блоки
        int Score(string team, int t) => r.Goals.Count(e => e.Team == team && e.Kind != "so" && Abs(e) < t);
        foreach (var (abbr, side, opp, shots) in new[] { (g.Home, home, away, r.HomeShots), (g.Away, away, home, r.AwayShots) })
        {
            int goals = r.Goals.Count(e => e.Team == abbr && e.Kind != "so" && e.Kind != "en");
            int saves = Math.Max(0, shots - goals);
            int misses = (int)Math.Round(saves * (0.42 + rng.NextDouble() * 0.12));
            int blocks = (int)Math.Round(saves * (0.4 + rng.NextDouble() * 0.12));
            string oppAbbr = abbr == g.Home ? g.Away : g.Home;
            int Time()
            {
                // эффект счёта: проигрывающий давит — отбор времени с весом
                for (int k = 0; k < 20; k++)
                {
                    int t = rng.Next(10, Math.Max(20, endT - 5));
                    int diff = Score(abbr, t) - Score(oppAbbr, t);
                    double w = diff < 0 ? 1.0 : diff == 0 ? 0.85 : 0.68;
                    if (rng.NextDouble() < w) return t;
                }
                return rng.Next(10, Math.Max(20, endT - 5));
            }
            int Shooter() => Pick(side.Lineup.Skaters.Distinct(), id => (data.PlayerById[id].Ixg60 + 0.25) * side.Lineup.MinutesOf(id) * (data.PlayerById[id].Rating("sht") + 30), rng);
            for (int i = 0; i < saves; i++)
                ev.Add(new MatchEvent { T = Time(), Team = abbr, Kind = "shot", P1 = Shooter(), P2 = abbr == g.Home ? r.AwayGoalie : r.HomeGoalie, Note = ShotKinds[rng.Next(ShotKinds.Length)] });
            for (int i = 0; i < misses; i++)
                ev.Add(new MatchEvent { T = Time(), Team = abbr, Kind = "miss", P1 = Shooter(), Note = rng.NextDouble() < 0.25 ? "в штангу" : "мимо" });
            for (int i = 0; i < blocks; i++)
            {
                int b = Pick(opp.Lineup.Skaters.Distinct(), id => (data.PlayerById[id].Rating("blk") + 20) * opp.Lineup.MinutesOf(id), rng);
                ev.Add(new MatchEvent { T = Time(), Team = abbr, Kind = "block", P1 = Shooter(), P2 = b });
            }
        }
        foreach (var e in r.Goals.Where(e => e.Kind != "so"))
            ev.Add(new MatchEvent { T = Abs(e), Team = e.Team, Kind = "goal", P1 = e.Scorer, P2 = e.A1, Note = e.Kind });
        r.Events = ev.OrderBy(x => x.T).ThenBy(x => x.Kind == "goal" ? 1 : 0).ToList();
        Rate(data, g, r);
    }

    /// <summary>Оценка за матч: база 6,6; голы, передачи, броски, блоки, итог матча; у вратаря — сейвы относительно ожидаемого.</summary>
    static void Rate(GameData data, ScheduledGame g, MatchResult r)
    {
        bool homeWon = r.HomeGoals > r.AwayGoals;
        foreach (var (dressed, team, won, gf, ga) in new[] { (r.HomeDressed, g.Home, homeWon, r.HomeGoals, r.AwayGoals), (r.AwayDressed, g.Away, !homeWon, r.AwayGoals, r.HomeGoals) })
        {
            foreach (var id in dressed)
            {
                if (!data.PlayerById.TryGetValue(id, out var p)) continue;
                double v;
                if (p.IsGoalie)
                {
                    bool home = team == g.Home;
                    if ((home ? r.HomeGoalie : r.AwayGoalie) != id) continue;
                    int sh = home ? r.AwayShots : r.HomeShots;
                    int conceded = (home ? r.AwayGoals : r.HomeGoals) - r.Goals.Count(e => e.Team != team && (e.Kind == "so" || e.Kind == "en"));
                    // движок по сменам знает ожидаемые голы по броскам в створ; иначе — 9,8% бросков
                    double exp = r.Box.TryGetValue(id, out var gb) ? gb.Xga : sh * 0.098;
                    v = 6.7 + 0.42 * (exp - conceded) + (conceded == 0 ? 0.6 : 0) + (won ? 0.25 : -0.15);
                }
                else
                {
                    int goals = r.Goals.Count(e => e.Scorer == id && e.Kind != "so");
                    int assists = r.Goals.Count(e => e.A1 == id || e.A2 == id);
                    if (r.Box.TryGetValue(id, out var b))
                    {
                        // протокол движка по сменам: +/− на льду вместо разницы шайб команды
                        v = 6.55 + 0.85 * goals + 0.5 * assists + 0.07 * b.S + 0.06 * b.Blk - 0.075 * b.Pim + (won ? 0.25 : -0.2) + 0.12 * b.Pm;
                    }
                    else
                    {
                        int shots = r.Events.Count(e => e.P1 == id && (e.Kind == "shot" || e.Kind == "goal"));
                        int blocks = r.Events.Count(e => e.Kind == "block" && e.P2 == id);
                        int pens = r.Events.Count(e => e.Kind == "pen" && e.P1 == id);
                        v = 6.55 + 0.85 * goals + 0.5 * assists + 0.07 * shots + 0.06 * blocks - 0.15 * pens + (won ? 0.25 : -0.2) + 0.04 * (gf - ga);
                    }
                }
                r.Ratings[id] = Math.Round(Math.Clamp(v, 5.0, 10.0), 1);
            }
        }
    }

    static int Pick(IEnumerable<int> ids, Func<int, double> w, Random rng)
    {
        var list = ids.Where(id => id != 0).Select(id => (id, w: Math.Max(1e-6, w(id)))).ToList();
        if (list.Count == 0) return 0;
        double u = rng.NextDouble() * list.Sum(x => x.w);
        foreach (var (id, ww) in list) { u -= ww; if (u <= 0) return id; }
        return list[^1].id;
    }

    static int Poisson(Random rng, double l)
    {
        double L = Math.Exp(-l), p = 1; int k = 0;
        do { k++; p *= rng.NextDouble(); } while (p > L);
        return k - 1;
    }
}
