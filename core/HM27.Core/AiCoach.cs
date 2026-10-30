namespace HM27.Core;

/// <summary>
/// Главные тренеры клубов ИИ (этап «Тренер»): не идиоты и не всезнайки. Видят игроков со своей ошибкой (Staff.View),
/// решают по своему стилю. Что делают:
///   • звенья: перед сезоном и раз в неделю правят звенья по своей оценке (усталые и не в форме опускаются, кто в форме — выше);
///     гибкие правят смелее, упрямые — по чуть-чуть;
///   • план на матч: гибкий тренер (Flex) меняет систему под соперника, если по его оценке это заметно выгодно;
///     в серии плей-офф против клуба игрока — отвечает на то, как тот играл в прошлом матче серии;
///   • перерывы — по стилю (рисковый открывается, проигрывая; осторожный закрывается при +2), снятие вратаря и темп — по стилю;
///   • система: если полсезона не идёт, гибкий тренер её меняет.
/// Клуб игрока обыгрывает ИИ знанием (скаут, привычки тренеров), гибкостью и вниманием к деталям — не дырами в игре.
/// </summary>
public static class AiCoach
{
    /// <summary>Правка звеньев: ход — если по оценке тренера даёт не меньше AI_MIN гола за матч.</summary>
    public const double AI_MIN = 0.008;
    /// <summary>План на матч: менять систему, если выгода в глазах тренера не меньше PLAN_MIN гола.</summary>
    public const double PLAN_MIN = 0.025;
    /// <summary>Опека лучшего нападающего клуба игрока (выключатель для проверок).</summary>
    public static bool ShadowUser = true;

    /// <summary>Старт карьеры: стиль в настройках и первая правка звеньев («сборы»).</summary>
    public static void Init(Career c)
    {
        foreach (var t in c.Data.Teams)
        {
            if (t.Abbr == c.UserTeam) continue;
            var coach = Staff.Decider(c, t.Abbr);
            var l = c.Lineups[t.Abbr];
            if (coach == null) continue;
            l.Pull = coach.Risk >= 2 ? Coach.EARLY : coach.Tac >= 15 ? Coach.ASSIST : Coach.USUAL;
            l.TempoAuto = coach.Tac >= 14;
            l.Rules = Rules(coach);
            LineOpt.Improve(c, t.Abbr, l, coach, AI_MIN, 4 + coach.Flex / 3);
            l.FixPowerPlay(c.Data);
            c.AiTuned[t.Abbr] = c.Today;
        }
    }

    /// <summary>Правила в перерывах по стилю тренера (смена вратаря — у всех).</summary>
    public static List<Rules.Rule> Rules(StaffMember s)
    {
        var r = new List<Rules.Rule> { new("goalie", "goalie") };
        if (s.Risk >= 1) r.Add(new("trail", "tempo+"));
        if (s.Risk <= -1) r.Add(new("lead2", "tempo-"));
        if (s.Flex >= 14) r.Add(new("pens", "fc0"));
        return r;
    }

    /// <summary>Перед матчем ИИ: раз в неделю — правка звеньев (после лазарета и восстановления места).</summary>
    public static void BeforeMatch(Career c, string team, Lineup l)
    {
        var coach = Staff.Decider(c, team);
        if (coach == null) return;
        if (c.AiTuned.TryGetValue(team, out var last) && c.Today.DayNumber - last.DayNumber < 7) return;
        LineOpt.Improve(c, team, l, coach, AI_MIN, 1 + coach.Flex / 6);
        var dressed = l.Skaters.ToHashSet();
        foreach (var u in new[] { l.Pp1, l.Pp2, l.Pk1, l.Pk2 }) for (int k = 0; k < u.Length; k++) if (!dressed.Contains(u[k])) u[k] = 0;
        l.FixPowerPlay(c.Data);
        c.AiTuned[team] = c.Today;
    }

    /// <summary>
    /// План ИИ на матч: гибкий тренер проверяет, не выгоднее ли поменять полюса против этого соперника (его оценка — с ошибкой;
    /// невыученная схема стоит дороже — это в оценке). Против клуба игрока в серии плей-офф смотрит, как тот играл в прошлом матче.
    /// </summary>
    public static void Plan(Career c, ScheduledGame g, MatchSide me, MatchSide opp)
    {
        if (me.Team.Abbr == c.UserTeam) return;
        var coach = Staff.Decider(c, me.Team.Abbr);
        if (coach == null) return;
        int need = g.Playoff ? 9 : 12;
        if (coach.Flex < need) return;
        // что тренер знает о сопернике: систему клуба; в серии против игрока — то, как он играл в прошлый раз
        var oppKeep = opp.Plan;
        if (g.Playoff && opp.Team.Abbr == c.UserTeam && c.LastUserSys != null) opp.Plan = (int[])c.LastUserSys.Clone();
        else opp.Plan = opp.Lineup.Sys == null ? null : (int[])opp.Lineup.Sys.Clone();
        var baseSys = (int[])(me.Lineup.Sys ?? new int[Tactics.Levers.Length]).Clone();
        var keep = me.Plan;
        double gpg = c.Data.League.GoalsPerGame * 0.95;
        double Eval(int[] s) { me.Plan = s; var e = Tactics.Of(c, me, opp); return gpg * (Math.Exp(e.For) - Math.Exp(e.Against)); }
        double cur = Eval(baseSys);
        var plan = (int[])baseSys.Clone();
        for (int i = 0; i < Tactics.Levers.Length; i++)
        {
            double best = PLAN_MIN; int bv = plan[i];
            foreach (int v in new[] { -1, 0, 1 })
            {
                if (v == baseSys[i]) continue;
                var s = (int[])baseSys.Clone(); s[i] = v;
                double est = Staff.Estimate(c, coach, Eval(s) - cur, g.Id, 300 + i * 10 + v) + (i == 0 && coach.Press == v ? 0.02 : 0);
                if (est > best) { best = est; bv = v; }
            }
            plan[i] = bv;
        }
        opp.Plan = oppKeep;
        me.Plan = plan.SequenceEqual(baseSys) ? keep : plan;
        // против клуба игрока гибкий тренер опекает его лучшего нападающего (в плей-офф — почти любой)
        if (ShadowUser && opp.Team.Abbr == c.UserTeam && (coach.Flex >= 13 || g.Playoff && coach.Flex >= 9))
        {
            var top = opp.Lineup.F.Take(2).SelectMany(x => x).Where(id => id != 0)
                .Select(id => (id, v: c.Data.PlayerById.TryGetValue(id, out var p) ? Staff.View(c, coach, p).off : 0)).OrderByDescending(x => x.v).FirstOrDefault();
            if (top.id != 0) { me.Targets ??= new(); me.Targets[top.id] = "shadow"; }
        }
    }

    /// <summary>Раз в месяц: если система полсезона не идёт, гибкий тренер её меняет (по своей оценке состава).</summary>
    public static void Daily(Career c)
    {
        if (c.Today.Day != 1 || c.Phase != Phase.Regular) return;
        foreach (var t in c.Data.Teams)
        {
            if (t.Abbr == c.UserTeam) continue;
            var coach = Staff.Decider(c, t.Abbr);
            var row = c.Table.GetValueOrDefault(t.Abbr);
            if (coach == null || row == null || row.Gp < 20 || coach.Flex < 10) continue;
            var last = c.TeamGames(t.Abbr).Where(g => g.Played && !g.Playoff).OrderByDescending(g => g.Date).Take(20).ToList();
            double pts = last.Sum(g => { bool h = g.Home == t.Abbr; bool w = (g.Result.HomeGoals > g.Result.AwayGoals) == h; return w ? 2 : g.Result.End != "REG" ? 1 : 0; });
            if (pts / (2.0 * last.Count) >= 0.45) continue;
            var l = c.Lineups[t.Abbr];
            var s = Tactics.Suggest(c, l, 0.006, coach, c.Today.Month + 12 * c.Today.Year);
            for (int i = 0; i < s.Length; i++) if (s[i] != l.SysOf(i)) Tactics.Set(l, i, s[i]);
        }
    }
}
