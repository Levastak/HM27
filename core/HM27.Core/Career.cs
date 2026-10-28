using System.Text.Json;
using System.Text.Json.Serialization;

namespace HM27.Core;

public sealed class StandingRow
{
    public string Team { get; set; }
    public int Gp { get; set; }
    public int W { get; set; }
    public int L { get; set; }
    public int Otl { get; set; }
    public int Rw { get; set; }      // победы в основное время
    public int Row { get; set; }     // победы без буллитов
    public int Gf { get; set; }
    public int Ga { get; set; }
    public string Streak { get; set; } = "";
    public List<char> Last { get; set; } = new();   // последние матчи: W / L / O
    public int Pts => 2 * W + Otl;
    public double PtsPct => Gp == 0 ? 0 : Pts / (2.0 * Gp);
}

public sealed class SkaterLine
{
    public int Gp { get; set; }
    public int G { get; set; }
    public int A { get; set; }
    public int Ppg { get; set; }
    public int Gwg { get; set; }
    public double RatingSum { get; set; }
    public int Rated { get; set; }
    public double AvgRating => Rated == 0 ? 0 : RatingSum / Rated;
    public int Pts => G + A;
}

public sealed class GoalieLine
{
    public int Gp { get; set; }
    public int W { get; set; }
    public int L { get; set; }
    public int Otl { get; set; }
    public int Sa { get; set; }
    public int Ga { get; set; }
    public int So { get; set; }
    public double RatingSum { get; set; }
    public int Rated { get; set; }
    public double AvgRating => Rated == 0 ? 0 : RatingSum / Rated;
    public double SvPct => Sa == 0 ? 0 : 1 - Ga / (double)Sa;
    public double Gaa => Gp == 0 ? 0 : Ga / (double)Gp;
}

public sealed class Series
{
    public int Round { get; set; }            // 1–4
    public string Conf { get; set; }          // для финала — "Финал"
    public string High { get; set; }          // клуб с преимуществом своей площадки
    public string Low { get; set; }
    public int WinsHigh { get; set; }
    public int WinsLow { get; set; }
    public int Slot { get; set; }             // позиция в сетке
    public List<int> Games { get; set; } = new();
    public DateOnly? LastGame { get; set; }
    public bool Done => WinsHigh == 4 || WinsLow == 4;
    public string Winner => WinsHigh == 4 ? High : WinsLow == 4 ? Low : null;
}

public enum Phase { Regular, Playoffs, Finished }

/// <summary>
/// Карьера: один сезон (MVP). Календарь, результаты, таблица, статистика, составы всех клубов, плей-офф.
/// Всё состояние сериализуется в JSON (сохранение). База игроков не сохраняется — она в данных игры.
/// </summary>
public sealed class Career
{
    public int Version { get; set; } = 3;
    public int Seed { get; set; }
    public int Season { get; set; }
    /// <summary>Прогноз прессы на сезон: место в дивизионе (считается один раз в начале сезона).</summary>
    public Dictionary<string, int> PressPick { get; set; } = new();
    public int PressPickSeason { get; set; }
    public string UserTeam { get; set; }
    public DateOnly Today { get; set; }
    public Phase Phase { get; set; }
    public List<ScheduledGame> Games { get; set; } = new();
    public Dictionary<string, Lineup> Lineups { get; set; } = new();
    public Dictionary<string, double> BaseOff { get; set; } = new();
    public Dictionary<string, double> BaseDef { get; set; } = new();
    public Dictionary<string, double> BaseGsax { get; set; } = new();
    public Dictionary<string, StandingRow> Table { get; set; } = new();
    public Dictionary<int, SkaterLine> Skaters { get; set; } = new();
    public Dictionary<int, GoalieLine> Goalies { get; set; } = new();
    public Dictionary<int, SkaterLine> PoSkaters { get; set; } = new();
    public Dictionary<int, GoalieLine> PoGoalies { get; set; } = new();
    public List<Series> Bracket { get; set; } = new();
    public string Champion { get; set; }
    public List<int> Injured { get; set; } = new();
    /// <summary>Травмы: кто, с какого дня, до какого (включительно — не играет), что.</summary>
    public List<Injury> Injuries { get; set; } = new();
    /// <summary>Сколько стартов подряд у основного вратаря (ИИ даёт отдых).</summary>
    public Dictionary<string, int> GoalieStreak { get; set; } = new();
    public List<Message> Messages { get; set; } = new();
    /// <summary>Модель игрока: истинные характеристики, потенциал, характер, знание клуба, форма.</summary>
    public Dictionary<int, PlayerState> Players { get; set; } = new();
    /// <summary>Сила клубов «снизу вверх» из игроков базы (TeamStrength) — в новых карьерах; старые сохранения — сила модели «Сводки».</summary>
    public bool BottomUp { get; set; }

    [JsonIgnore] public GameData Data { get; private set; }

    public static Career New(GameData data, string userTeam, int seed)
    {
        var c = new Career { Seed = seed, Season = data.Season, UserTeam = userTeam, BottomUp = true };
        c.Attach(data);
        c.Games = ScheduleMaker.Make(data, data.Season, seed);
        c.Today = c.Games.Min(g => g.Date);
        c.Injured = data.Teams.SelectMany(t => t.Injured).Distinct().ToList();
        var irng = new Random(seed * 31 + 7);
        foreach (var id in c.Injured)
            c.Injuries.Add(new Injury { Id = id, From = c.Today.AddDays(-irng.Next(3, 30)), Until = c.Today.AddDays(irng.Next(5, 45)), Kind = InjuryModel.Kinds[irng.Next(InjuryModel.Kinds.Length)] });
        var inj = c.Injured.ToHashSet();
        PlayerModel.Generate(c);
        foreach (var t in data.Teams)
        {
            var l = Lineup.FromTeam(data, t, inj);
            c.Lineups[t.Abbr] = l;
            // База калибровки — состав по умолчанию с разыгранными истинными уровнями: такие же звенья дают ровно силу модели.
            // Форма (и форма сезона вратаря) в базу не входит: это отклонение от ожидания, оно и должно менять результат.
            c.BaseOff[t.Abbr] = MatchEngine.OffIndex(data, l, c.DlBase);
            c.BaseDef[t.Abbr] = MatchEngine.DefIndex(data, l, c.DlBase);
            c.BaseGsax[t.Abbr] = 0.8 * MatchEngine.GsaxOf(data, l.G1, c.DlBase) + 0.2 * MatchEngine.GsaxOf(data, l.G2, c.DlBase);
            c.Table[t.Abbr] = new StandingRow { Team = t.Abbr };
        }
        InboxWriter.Welcome(c);
        return c;
    }

    public void Attach(GameData data) => Data = data;

    /// <summary>Сдвиг силы игрока для движка (модель игрока).</summary>
    public double Dl(int id) => Data.PlayerById.TryGetValue(id, out var p) ? PlayerModel.Delta(this, p) : 0;

    /// <summary>Сдвиг силы в плей-офф: + хладнокровие (опыт больших матчей), в пределах, которые не ловит зеркальная проверка.</summary>
    public double DlPlayoff(int id) => Data.PlayerById.TryGetValue(id, out var p) ? PlayerModel.Delta(this, p) + PlayerModel.Composure(this, p) : 0;

    /// <summary>
    /// Эффект состава тренера: голов за матч забиваем / пропускаем больше (+) или меньше (−), чем со звеньями «как в НХЛ»
    /// (те же травмы, основной вратарь). Считает тот же расчёт силы, что движок перед матчем.
    /// </summary>
    public (double gf, double ga) LineupEffect(string abbr)
    {
        var l = Lineups[abbr];
        double gpg = Data.League.GoalsPerGame * MatchEngine.REG_SHARE;
        if (!BottomUp)
        {
            double dOff = MatchEngine.OffIndex(Data, l, DlBase) - BaseOff[abbr], dDef = MatchEngine.DefIndex(Data, l, DlBase) - BaseDef[abbr];
            return (MatchEngine.K_FOR * dOff + dDef, -(MatchEngine.K_AGAINST * dOff + dDef));
        }
        var def = Lineup.FromTeam(Data, Data.TeamByAbbr[abbr], Injured.ToHashSet());
        var (a1, d1) = TeamStrength.Live(this, l, l.G1 != 0 ? l.G1 : def.G1, DlBase);
        var (a0, d0) = TeamStrength.Live(this, def, def.G1, DlBase);
        return (gpg * (Math.Exp(a1) - Math.Exp(a0)), gpg * (Math.Exp(d1) - Math.Exp(d0)));
    }

    /// <summary>Сила клуба сейчас (атака, оборона в лог-голах): из игроков текущего состава, без формы; в старых сохранениях — модель.</summary>
    public (double att, double dfn) Power(string abbr)
    {
        var t = Data.TeamByAbbr[abbr];
        if (!BottomUp) return (t.Att, t.Dfn);
        var l = Lineups[abbr];
        return TeamStrength.Live(this, l, l.G1, DlBase);
    }

    /// <summary>Место клуба в лиге по силе: общей, атаке и обороне.</summary>
    public (int all, int att, int dfn) PowerRank(string abbr)
    {
        var p = Data.Teams.ToDictionary(t => t.Abbr, t => Power(t.Abbr));
        int R(Func<(double att, double dfn), double> f) => p.OrderByDescending(kv => f(kv.Value)).Select(kv => kv.Key).ToList().IndexOf(abbr) + 1;
        return (R(x => x.att - x.dfn), R(x => x.att), R(x => -x.dfn));
    }

    /// <summary>Сдвиг силы без формы — для базы калибровки клубов.</summary>
    public double DlBase(int id) => Data.PlayerById.TryGetValue(id, out var p) ? PlayerModel.Delta(this, p, form: false) : 0;

    // ------------------------------------------------------------------ сохранение
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };
    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);
    public static Career FromJson(string json, GameData data)
    {
        var c = JsonSerializer.Deserialize<Career>(json, JsonOpts);
        c.Attach(data);
        // сохранения до модели игрока: истинные значения = данные (без розыгрыша), сила клубов не меняется
        if (c.Players.Count == 0) { PlayerModel.Generate(c, noise: false); c.Version = 3; }
        // сохранения до КА/ПА (1–200): досчитать от данных со сдвигом, который был у уровня
        if (c.Players.Values.Any(s => s.Ca == 0))
            foreach (var p in data.Players)
                if (c.Players.TryGetValue(p.Id, out var st) && st.Ca == 0)
                {
                    var fresh = PlayerModel.Make(c, p, noise: false);
                    float shift = (float)((st.Lvl - p.Level) * PlayerModel.CA_PER_LVL);
                    st.Ca = fresh.Ca + shift; st.Ca0 = st.Ca; st.Pa = Math.Max(fresh.Pa + shift, st.Ca);
                }
        // сохранения до «КА из характеристик»: согласовать характеристики с КА, чтобы развитие не дало скачка
        if (c.Version < 3)
        {
            foreach (var p0 in data.Players) if (c.Players.TryGetValue(p0.Id, out var st)) PlayerModel.Align(data, p0, st);
            c.Version = 3;
        }
        foreach (var p in data.Players)   // свои игроки — точные значения (раньше клуб «узнавал» их несколько недель)
            if (p.Team == c.UserTeam && c.Players.TryGetValue(p.Id, out var own)) own.Know = 1f;
        foreach (var p in data.Players)   // новые игроки в базе
            if (!c.Players.ContainsKey(p.Id)) c.Players[p.Id] = PlayerModel.Make(c, p, noise: false);
        return c;
    }

    // ------------------------------------------------------------------ календарь
    public IEnumerable<ScheduledGame> GamesOn(DateOnly d) => Games.Where(g => g.Date == d);
    public IEnumerable<ScheduledGame> TeamGames(string t) => Games.Where(g => g.Home == t || g.Away == t);
    public ScheduledGame NextGame(string t) => Games.Where(g => !g.Played && (g.Home == t || g.Away == t)).OrderBy(g => g.Date).FirstOrDefault();
    public ScheduledGame LastGame(string t) => Games.Where(g => g.Played && (g.Home == t || g.Away == t)).OrderByDescending(g => g.Date).FirstOrDefault();
    public bool PlayedYesterday(string t, DateOnly d) => Games.Any(g => g.Date == d.AddDays(-1) && (g.Home == t || g.Away == t));

    public MatchSide Side(string abbr, DateOnly d, bool forPreview = false, bool playoff = false)
    {
        var l = Lineups[abbr];
        bool b2b = PlayedYesterday(abbr, d);
        int goalie = l.G1;
        if (abbr == UserTeam)
        {
            if (b2b && l.BackupOnB2B && l.G2 != 0) goalie = l.G2;
        }
        else
        {
            // ИИ: запасной во втором матче за два дня и примерно раз в 5 матчей
            int st = GoalieStreak.GetValueOrDefault(abbr);
            if (l.G2 != 0 && (b2b || st >= 5)) goalie = l.G2;
        }
        if (goalie == 0) goalie = l.G1 != 0 ? l.G1 : l.G2;
        if (goalie == 0)   // все вратари в лазарете — выходит кто есть (экстренный вратарь)
            goalie = Data.Roster(abbr).Where(p => p.IsGoalie).OrderByDescending(p => p.Gsax).Select(p => p.Id).FirstOrDefault();
        Func<int, double> dl = playoff ? DlPlayoff : Dl;
        if (BottomUp)
        {
            // сила клуба из текущего состава: звенья, травмы, форма, рост — всё уже внутри, поэтому поправки
            // «относительно базы» обнуляются (база = этот же состав), иначе эффект посчитался бы дважды
            var (att, dfn) = TeamStrength.Live(this, l, goalie, dl);
            return new MatchSide
            {
                Team = Data.TeamByAbbr[abbr], Lineup = l, Goalie = goalie, B2B = b2b, Dl = dl, Att = att, Dfn = dfn,
                BaseOff = MatchEngine.OffIndex(Data, l, dl), BaseDef = MatchEngine.DefIndex(Data, l, dl), BaseGsax = MatchEngine.GsaxOf(Data, goalie, dl),
            };
        }
        return new MatchSide
        {
            Team = Data.TeamByAbbr[abbr], Lineup = l, BaseOff = BaseOff[abbr], BaseDef = BaseDef[abbr], BaseGsax = BaseGsax[abbr], Goalie = goalie, B2B = b2b, Dl = dl,
        };
    }

    public MatchOdds Preview(ScheduledGame g) => MatchEngine.Odds(Data, Side(g.Home, g.Date, true, g.Playoff), Side(g.Away, g.Date, true, g.Playoff), g.Playoff);

    // ------------------------------------------------------------------ симуляция
    Random RngFor(ScheduledGame g) => new(unchecked(Seed * 1_000_003 ^ g.Id));   // детерминированно (HashCode в .NET случаен по процессам)

    public MatchResult Play(ScheduledGame g)
    {
        if (g.Played) return g.Result;
        var inj = Injured.ToHashSet();
        foreach (var t in new[] { g.Home, g.Away })
        {
            if (t == UserTeam) Lineups[t].Repair(Data, t, inj);
            else
            {
                // ИИ ставит звенья как в НХЛ, с поправкой на травмы (вернувшиеся из лазарета — снова в составе)
                var keep = Lineups[t];
                var l = Lineup.FromTeam(Data, Data.TeamByAbbr[t], inj);
                l.Tempo = keep.Tempo; l.EarlyPull = keep.EarlyPull;
                Lineups[t] = l;
            }
        }
        var h = Side(g.Home, g.Date, playoff: g.Playoff); var a = Side(g.Away, g.Date, playoff: g.Playoff);
        var r = MatchEngine.Play(Data, h, a, RngFor(g), g.Playoff);
        g.Result = r;
        bool mine = g.Home == UserTeam || g.Away == UserTeam;
        Chronicle.Build(Data, g, h, a, r, new Random(unchecked(Seed * 7919 + g.Id * 31 + 1)));
        if (!mine) r.Events = new();   // хронику храним только для своих матчей — сохранение не раздувается
        Record(g);
        InjuryModel.AfterGame(this, g, new Random(unchecked(Seed * 104729 + g.Id)));
        foreach (var (side, abbr) in new[] { (h, g.Home), (a, g.Away) })
            GoalieStreak[abbr] = side.Goalie == Lineups[abbr].G1 ? GoalieStreak.GetValueOrDefault(abbr) + 1 : 0;
        if (g.Home == UserTeam || g.Away == UserTeam) InboxWriter.AfterUserGame(this, g);
        return r;
    }

    /// <summary>Сыграть все матчи текущего дня (кроме матча клуба игрока, если skipUser) и перейти к следующему.</summary>
    public List<ScheduledGame> SimDay(bool skipUser = false)
    {
        var played = new List<ScheduledGame>();
        foreach (var g in GamesOn(Today).Where(g => !g.Played).ToList())
        {
            if (skipUser && (g.Home == UserTeam || g.Away == UserTeam)) continue;
            Play(g); played.Add(g);
        }
        if (!GamesOn(Today).Any(g => !g.Played)) Advance();
        return played;
    }

    public ScheduledGame UserGameToday() => GamesOn(Today).FirstOrDefault(g => !g.Played && (g.Home == UserTeam || g.Away == UserTeam));

    /// <summary>Перейти к следующему дню; на стыке регулярки и плей-офф — построить сетку.</summary>
    void Advance()
    {
        if (Phase == Phase.Regular && Games.Where(g => !g.Playoff).All(g => g.Played))
        {
            StartPlayoffs();
            return;
        }
        if (Phase == Phase.Playoffs)
        {
            UpdatePlayoffs();
            if (Phase == Phase.Finished) return;
        }
        Today = Today.AddDays(1);
        InjuryModel.Heal(this);
        if (Today.DayOfWeek == DayOfWeek.Monday) Development.Week(this);   // развитие, форма, знание — раз в неделю
        if (Phase == Phase.Playoffs) SchedulePlayoffDay();
        InboxWriter.Periodic(this);
    }

    /// <summary>Симулировать до ближайшего матча клуба игрока (сам матч не играется).</summary>
    public int SimToUserGame()
    {
        int days = 0;
        while (Phase != Phase.Finished && UserGameToday() == null && days < 400)
        {
            SimDay();
            days++;
            if (Phase == Phase.Playoffs && !Bracket.Any(s => !s.Done && (s.High == UserTeam || s.Low == UserTeam)) && UserGameToday() == null)
                break;   // клуб игрока выбыл — дальше смотрит со стороны
        }
        return days;
    }

    public void SimToEnd()
    {
        int guard = 0;
        while (Phase != Phase.Finished && guard++ < 1000) SimDay();
    }

    // ------------------------------------------------------------------ учёт результата
    void Record(ScheduledGame g)
    {
        var r = g.Result;
        bool homeWon = r.HomeGoals > r.AwayGoals;
        var sk = g.Playoff ? PoSkaters : Skaters;
        var gk = g.Playoff ? PoGoalies : Goalies;

        if (!g.Playoff)
        {
            var H = Table[g.Home]; var A = Table[g.Away];
            H.Gp++; A.Gp++;
            H.Gf += r.HomeGoals; H.Ga += r.AwayGoals; A.Gf += r.AwayGoals; A.Ga += r.HomeGoals;
            var (win, lose) = homeWon ? (H, A) : (A, H);
            win.W++; if (r.End == "REG") win.Rw++; if (r.End != "SO") win.Row++;
            if (r.End == "REG") lose.L++; else lose.Otl++;
            Mark(win, 'W'); Mark(lose, r.End == "REG" ? 'L' : 'O');
        }

        foreach (var id in r.HomeDressed.Concat(r.AwayDressed))
        {
            if (!Data.PlayerById.TryGetValue(id, out var p) || p.IsGoalie) continue;
            var line = Get(sk, id); line.Gp++;
            if (r.Ratings.TryGetValue(id, out var rt)) { line.RatingSum += rt; line.Rated++; }
        }
        foreach (var e in r.Goals.Where(e => e.Kind != "so" && e.Scorer != 0))
        {
            var s = Get(sk, e.Scorer); s.G++; if (e.Kind == "pp") s.Ppg++;
            if (e.A1 != 0) Get(sk, e.A1).A++;
            if (e.A2 != 0) Get(sk, e.A2).A++;
        }
        // Победный гол: гол, после которого у победителя на 1 больше, чем у проигравшего в итоге
        var winTeam = homeWon ? g.Home : g.Away; int loserGoals = homeWon ? r.AwayGoals : r.HomeGoals; int cnt = 0;
        foreach (var e in r.Goals.Where(e => e.Team == winTeam && e.Kind != "so"))
            if (++cnt == loserGoals + 1) { Get(sk, e.Scorer).Gwg++; break; }

        void G(int goalie, int sa, int ga, bool won, string end, bool shutout)
        {
            if (goalie == 0) return;
            var x = Get(gk, goalie); x.Gp++; x.Sa += sa; x.Ga += ga;
            if (r.Ratings.TryGetValue(goalie, out var rt)) { x.RatingSum += rt; x.Rated++; }
            if (won) x.W++; else if (end == "REG" || g.Playoff) x.L++; else x.Otl++;
            if (shutout) x.So++;
        }
        // голы в пустые ворота и победный буллит вратарю не засчитываются
        int hGa = r.AwayGoals - (r.End == "SO" && !homeWon ? 1 : 0) - r.Goals.Count(e => e.Team == g.Away && e.Kind == "en");
        int aGa = r.HomeGoals - (r.End == "SO" && homeWon ? 1 : 0) - r.Goals.Count(e => e.Team == g.Home && e.Kind == "en");
        G(r.HomeGoalie, r.AwayShots, hGa, homeWon, r.End, hGa == 0);
        G(r.AwayGoalie, r.HomeShots, aGa, !homeWon, r.End, aGa == 0);
    }

    static void Mark(StandingRow row, char c)
    {
        row.Last.Add(c); if (row.Last.Count > 10) row.Last.RemoveAt(0);
        int n = 0; for (int i = row.Last.Count - 1; i >= 0 && row.Last[i] == c; i--) n++;
        row.Streak = (c == 'O' ? "ПО" : c == 'W' ? "В" : "П") + n;
    }

    static T Get<T>(Dictionary<int, T> d, int id) where T : new()
    {
        if (!d.TryGetValue(id, out var v)) d[id] = v = new T();
        return v;
    }

    // ------------------------------------------------------------------ таблица
    /// <summary>Порядок НХЛ: очки, меньше игр, победы в основное время, без буллитов, все победы, разница.</summary>
    public List<StandingRow> Sorted(Func<TeamData, bool> filter = null) =>
        Table.Values.Where(r => filter == null || filter(Data.TeamByAbbr[r.Team]))
            .OrderByDescending(r => r.Pts).ThenBy(r => r.Gp).ThenByDescending(r => r.Rw).ThenByDescending(r => r.Row)
            .ThenByDescending(r => r.W).ThenByDescending(r => r.Gf - r.Ga).ThenBy(r => r.Team).ToList();

    public IEnumerable<string> Conferences => Data.Teams.Select(t => t.Conference).Distinct().OrderBy(x => x);
    public IEnumerable<string> Divisions(string conf) => Data.Teams.Where(t => t.Conference == conf).Select(t => t.Division).Distinct().OrderBy(x => x);

    /// <summary>Расклад плей-офф в конференции: топ-3 дивизионов и две «уайлд-кард».</summary>
    public (List<StandingRow> div1, List<StandingRow> div2, List<StandingRow> wild) PlayoffPicture(string conf)
    {
        var divs = Divisions(conf).ToList();
        var d1 = Sorted(t => t.Division == divs[0]); var d2 = Sorted(t => t.Division == divs[1]);
        var top = d1.Take(3).Concat(d2.Take(3)).Select(r => r.Team).ToHashSet();
        var wild = Sorted(t => t.Conference == conf).Where(r => !top.Contains(r.Team)).ToList();
        return (d1, d2, wild);
    }

    // ------------------------------------------------------------------ плей-офф
    void StartPlayoffs()
    {
        Phase = Phase.Playoffs;
        int slot = 0;
        foreach (var conf in Conferences)
        {
            var (d1, d2, wild) = PlayoffPicture(conf);
            // лучший победитель дивизиона играет со второй «уайлд-кард»
            bool d1Best = Sorted(t => t.Conference == conf).FindIndex(r => r.Team == d1[0].Team) <
                          Sorted(t => t.Conference == conf).FindIndex(r => r.Team == d2[0].Team);
            var (wcA, wcB) = d1Best ? (wild[1], wild[0]) : (wild[0], wild[1]);
            AddSeries(1, conf, d1[0].Team, wcA.Team, slot++);
            AddSeries(1, conf, d1[1].Team, d1[2].Team, slot++);
            AddSeries(1, conf, d2[0].Team, wcB.Team, slot++);
            AddSeries(1, conf, d2[1].Team, d2[2].Team, slot++);
        }
        Today = Today.AddDays(3);
        SchedulePlayoffDay();
        InboxWriter.PlayoffsStart(this);
    }

    void AddSeries(int round, string conf, string a, string b, int slot)
    {
        // преимущество площадки — у клуба с большим числом очков в регулярке
        var ra = Table[a]; var rb = Table[b];
        bool aHigh = Sorted().IndexOf(ra) <= Sorted().IndexOf(rb);
        Bracket.Add(new Series { Round = round, Conf = conf, High = aHigh ? a : b, Low = aHigh ? b : a, Slot = slot });
    }

    void SchedulePlayoffDay()
    {
        foreach (var s in Bracket.Where(s => !s.Done))
        {
            if (s.Games.Any(id => Games.First(g => g.Id == id) is var gg && !gg.Played)) continue;
            if (s.LastGame.HasValue && Today.DayNumber - s.LastGame.Value.DayNumber < 2) continue;
            int n = s.Games.Count + 1;   // номер матча серии: 2-2-1-1-1
            bool highHome = n is 1 or 2 or 5 or 7;
            int id = Season * 1_000_000 + 30_000 + s.Round * 100 + s.Slot * 10 + n;
            Games.Add(new ScheduledGame { Id = id, Date = Today, Home = highHome ? s.High : s.Low, Away = highHome ? s.Low : s.High, Playoff = true });
            s.Games.Add(id); s.LastGame = Today;
        }
    }

    void UpdatePlayoffs()
    {
        foreach (var s in Bracket.Where(s => !s.Done))
        {
            s.WinsHigh = s.WinsLow = 0;
            foreach (var id in s.Games)
            {
                var g = Games.First(x => x.Id == id);
                if (!g.Played) continue;
                var w = g.Result.HomeGoals > g.Result.AwayGoals ? g.Home : g.Away;
                if (w == s.High) s.WinsHigh++; else s.WinsLow++;
            }
        }
        int round = Bracket.Max(s => s.Round);
        var cur = Bracket.Where(s => s.Round == round).OrderBy(s => s.Slot).ToList();
        if (!cur.All(s => s.Done)) return;
        if (round == 4) { Champion = cur[0].Winner; Phase = Phase.Finished; return; }
        // следующий раунд: соседние пары по сетке
        for (int i = 0; i < cur.Count; i += 2)
        {
            string conf = round == 3 ? "Финал" : cur[i].Conf;
            AddSeries(round + 1, conf, cur[i].Winner, cur[i + 1].Winner, i / 2);
        }
        foreach (var s in Bracket.Where(s => s.Round == round + 1)) s.LastGame = Today.AddDays(1);
    }

    // ------------------------------------------------------------------ удобства для интерфейса
    public int Unread => Messages.Count(m => !m.Read);

    public string Short(int id) => id != 0 && Data.PlayerById.TryGetValue(id, out var p) ? p.LastRu : "—";
    public TeamData Team(string abbr) => Data.TeamByAbbr[abbr];
    public IEnumerable<PlayerData> Roster(string abbr) => Data.Roster(abbr);
}
