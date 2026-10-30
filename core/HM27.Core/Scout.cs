namespace HM27.Core;

/// <summary>
/// Скаутский отчёт о сопернике перед матчем (этап «Тренер», шаг 4): система, опасные игроки, вратарь, спецбригады
/// и советы на матч — что поменять в системе против именно этого соперника (с учётом выучки) и кого опекать.
/// Всё — из того, что клуб видит: характеристики соперника — видимые (знание клуба), система — как он играет.
/// </summary>
public static class Scout
{
    public sealed record Threat(int Id, string Why);
    public sealed record Advice(int Lever, int V, double Gain, double Learn, string Why, string Conf);

    public sealed class Report
    {
        public string Opp;
        public int[] OppSys;
        public List<Threat> Threats = new();
        public int Goalie; public string GoalieText;
        public double PpFor, PkAgainst; public int PpRank, PkRank;
        public List<Advice> Advice = new();
        public int ShadowPick; public string ShadowWhy; public double ShadowGain;
        public int PressPick; public string PressWhy; public double PressGain;
        /// <summary>Сколько даёт план (система на матч + задания) против текущей системы клуба, голов за матч (свои − чужие).</summary>
        public double Now;
        /// <summary>Кто готовил советы (помощник своего клуба) — его оценка, не истина.</summary>
        public StaffMember By;
        public string ShadowConf, PressConf;
    }

    static double Gpg(Career c) => c.Data.League.GoalsPerGame * 0.95;

    /// <summary>Чистые голы за матч от системы стороны (свои − пропущенные) — в голах.</summary>
    static double Net(Career c, Tactics.Effect e) => Gpg(c) * (Math.Exp(e.For) - Math.Exp(e.Against));

    public static Report For(Career c, ScheduledGame g)
    {
        string me = c.UserTeam;
        bool home = g.Home == me;
        string oppAbbr = home ? g.Away : g.Home;
        var (h, a) = c.Sides(g, true);
        var my = home ? h : a; var op = home ? a : h;
        var rep = new Report { Opp = c.Data.TeamByAbbr[oppAbbr].Name, OppSys = op.Plan ?? op.Lineup.Sys ?? new int[Tactics.Levers.Length] };

        // опасные: сильнейшие в атаке нападающие соперника — со статистикой и формой
        var ol = c.Lineups[oppAbbr];
        var fw = ol.F.SelectMany(x => x).Where(id => id != 0).Distinct()
            .Select(id => (id, od: Tactics.OffDef(c, id))).OrderByDescending(x => x.od.off).ToList();
        foreach (var (id, _) in fw.Take(3))
        {
            var st = c.Skaters.GetValueOrDefault(id);
            var f = PlayerForm.Of(c, id);
            string stat = st == null || st.Gp == 0 ? "сезон только начался" : $"{st.G} + {st.A} в {st.Gp} {Plural(st.Gp, "матче", "матчах", "матчах")}";
            rep.Threats.Add(new Threat(id, $"{stat}; форма — {f.Word.ToLowerInvariant()}"));
        }
        // вратарь: сезон и отскоки
        rep.Goalie = op.Goalie;
        if (c.Data.PlayerById.TryGetValue(op.Goalie, out var gp))
        {
            var gl = c.Goalies.GetValueOrDefault(op.Goalie);
            double reb = PlayerModel.Visible(c, gp, "reb");
            string rebWord = reb >= 13 ? "держит шайбу после броска" : reb <= 9 ? "часто отдаёт отскоки" : "отскоки — как у среднего";
            rep.GoalieText = (gl == null || gl.Gp == 0 ? "в сезоне ещё не играл" : $"{gl.SvPct:0.000} отражённых в {gl.Gp} {Plural(gl.Gp, "матче", "матчах", "матчах")}") + "; " + rebWord;
        }
        // спецбригады: голы в большинстве и пропущенные в меньшинстве за матч, место в лиге
        var pp = c.Data.Teams.ToDictionary(t => t.Abbr, t => PerGame(c, t.Abbr, true));
        var pk = c.Data.Teams.ToDictionary(t => t.Abbr, t => PerGame(c, t.Abbr, false));
        rep.PpFor = pp[oppAbbr]; rep.PkAgainst = pk[oppAbbr];
        rep.PpRank = pp.Values.Count(v => v > rep.PpFor) + 1; rep.PkRank = pk.Values.Count(v => v < rep.PkAgainst) + 1;

        // советы по системе: каждый рычаг — какой полюс лучше всего против этого соперника (с выучкой) и насколько
        var baseSys = (int[])(my.Plan ?? my.Lineup.Sys ?? new int[Tactics.Levers.Length]).Clone();
        var keepPlan = my.Plan;
        double Eval(int[] sys) { my.Plan = sys; var e = Tactics.Of(c, my, op); my.Plan = keepPlan; return Net(c, e); }
        double cur = Eval(baseSys);
        var clubSys = my.Lineup.Sys ?? new int[Tactics.Levers.Length];
        rep.Now = cur - Eval(clubSys);
        var who = rep.By = Staff.Decider(c, me);
        for (int i = 0; i < Tactics.Levers.Length; i++)
        {
            double best = 0.01; int bv = baseSys[i];
            foreach (int v in new[] { -1, 0, 1 })
            {
                if (v == baseSys[i]) continue;
                var s = (int[])baseSys.Clone(); s[i] = v;
                // выгода в глазах помощника: его ошибка и любимый форчек
                double x = Staff.Estimate(c, who, Eval(s) - cur, g.Id, 50 + i * 10 + v) + (i == 0 && who != null && who.Press == v ? 0.02 : 0);
                if (x > best) { best = x; bv = v; }
            }
            if (bv == baseSys[i]) continue;
            var lv = Tactics.Levers[i];
            double learn = bv == 0 ? 1 : my.Lineup.Learned(lv.Key + (bv < 0 ? "-" : "+"));
            rep.Advice.Add(new Advice(i, bv, best, learn, Why(c, my, op, i, bv), Staff.Confidence(who, best)));
        }
        rep.Advice = rep.Advice.OrderByDescending(x => x.Gain).ToList();

        // кого опекать: самый опасный нападающий; на кого давить: защитник соперника, хуже всех отдающий шайбу
        double lamOp = Gpg(c) * Math.Exp(op.Sys?.For ?? 0), lamMy = Gpg(c) * Math.Exp(my.Sys?.For ?? 0);
        var sc = TeamStrength.Coef(c.Data);
        if (fw.Count > 0)
        {
            rep.ShadowPick = fw[0].id;
            double share = ol.MinutesOf(fw[0].id) / 60.0;
            rep.ShadowGain = lamOp * share * TeamStrength.SPREAD * sc.A1 * ShiftEngine.LINE_K * ShiftEngine.SHADOW_OFF
                             - 0.2 * 2.8 * (ShiftEngine.SHADOW_PEN - 1);   // цена: больше наших удалений
            rep.ShadowGain = Staff.Estimate(c, who, rep.ShadowGain, g.Id, 31); rep.ShadowConf = Staff.Confidence(who, rep.ShadowGain);
            rep.ShadowWhy = $"его атака ниже, бросает реже; у нас чуть больше удалений";
        }
        var ds = ol.D.Take(2).SelectMany(x => x).Where(id => id != 0 && c.Data.PlayerById.ContainsKey(id))
            .Select(id => (id, v: Move(c, c.Data.PlayerById[id]))).OrderBy(x => x.v).ToList();
        if (ds.Count > 0)
        {
            rep.PressPick = ds[0].id;
            double share = ol.MinutesOf(ds[0].id) / 60.0;
            rep.PressGain = lamMy * share * TeamStrength.SPREAD * -sc.D1 * ShiftEngine.PRESS_DEF - 0.01;   // цена: усталость
            rep.PressGain = Staff.Estimate(c, who, rep.PressGain, g.Id, 32); rep.PressConf = Staff.Confidence(who, rep.PressGain);
            rep.PressWhy = "хуже всех в их первых парах отдаёт шайбу — под давлением ошибается";
        }
        return rep;
    }

    /// <summary>Как защитник выходит из-под давления: передача, владение, решения (видимые).</summary>
    static double Move(Career c, PlayerData p) => PlayerModel.Visible(c, p, "pas") + PlayerModel.Visible(c, p, "hnd") + PlayerModel.Visible(c, p, "dec");

    static double PerGame(Career c, string abbr, bool forTeam)
    {
        var games = c.TeamGames(abbr).Where(x => x.Played).ToList();
        if (games.Count == 0) return 0;
        int n = games.Sum(x => x.Result.Goals.Count(e => e.Kind == "pp" && (e.Team == abbr) == forTeam));
        return n / (double)games.Count;
    }

    /// <summary>Почему этот полюс против этого соперника: контрприём, если сработал, иначе — пригодность своих.</summary>
    static string Why(Career c, MatchSide my, MatchSide op, int lever, int v)
    {
        var keep = my.Plan;
        var s = (int[])(my.Plan ?? my.Lineup.Sys ?? new int[Tactics.Levers.Length]).Clone(); s[lever] = v;
        my.Plan = s;
        var e = Tactics.Of(c, my, op);
        my.Plan = keep;
        var lv = Tactics.Levers[lever];
        string note = e.Notes.FirstOrDefault(n => NoteFor(n, lv.Key));
        if (note != null) return note;
        if (v == 0) return "против них края не окупаются — лучше обычная схема";
        double fit = Tactics.Fit(c, my.Lineup, lv, v);
        return $"нашим игрокам подходит {(fit >= 0.4 ? "хорошо" : "средне")}";
    }

    static bool NoteFor(string note, string key) => key switch
    {
        "fc" => note.Contains("прессинг"),
        "en" => note.Contains("вброс") || note.Contains("проход"),
        "da" => note.Contains("защитник") && note.Contains("подключ"),
        _ => note.Contains("брос") || note.Contains("отскок") || note.Contains("передач"),
    };

    static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        return m10 == 1 && m100 != 11 ? one : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? few : many;
    }
}
