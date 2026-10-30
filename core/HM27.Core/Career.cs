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
    /// <summary>Движок по сменам: секунды на льду, броски в створ, попытки, блоки, +/−, штрафные минуты, очки в большинстве.</summary>
    public int Toi { get; set; }
    public int S { get; set; }
    public int Att { get; set; }
    public int Blk { get; set; }
    public int Pm { get; set; }
    public int Pim { get; set; }
    public int Ppp { get; set; }
    public int Fow { get; set; }
    public int Fol { get; set; }
    public int Hit { get; set; }
    public int Tk { get; set; }
    public int Gv { get; set; }
    /// <summary>Ожидаемые голы бросков в створ (движок по сменам).</summary>
    public double Ixg { get; set; }
    public double FoPct => Fow + Fol == 0 ? 0 : Fow / (double)(Fow + Fol);
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
    /// <summary>Ожидаемые голы по броскам в створ (движок по сменам): сейвы сверх ожидаемых = Xga − Ga.</summary>
    public double Xga { get; set; }
    public double Gsax => Xga - Ga;
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
    /// <summary>Статус клуба у прессы (ступень 0–3) и когда его пересматривали — см. Pundits.ViewAll.</summary>
    public Dictionary<string, int> PressBand { get; set; } = new();
    public DateOnly PressBandDate { get; set; }
    public int PressBandSeason { get; set; }
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
    /// <summary>Обещания тренера игрокам (Decisions).</summary>
    public List<Promise> Promises { get; set; } = new();
    /// <summary>Средний настрой по лиге — точка отсчёта эффекта настроя (Morale.Effect).</summary>
    public float MoraleMean { get; set; } = 60;
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
        // травмированные на старте (по реальным данным): помощник знает, куда их вернуть — как в составе здорового клуба
        {
            var ul = c.Lineups[c.UserTeam];
            HM27.Core.Staff.Of(c);
            Tactics.Init(c);
            AiCoach.Init(c);
            // свой клуб: темп и снятие вратаря по совету помощника на каждый матч (тренер может решить сам)
            ul.TempoAuto = true; ul.Pull = Coach.ASSIST;
            var full = Lineup.FromTeam(data, data.TeamByAbbr[c.UserTeam], new HashSet<int>());
            foreach (var id in inj)
            {
                if (!data.PlayerById.TryGetValue(id, out var p) || p.Team != c.UserTeam) continue;
                List<string> keys;
                if (p.IsGoalie)
                {
                    double g1 = data.PlayerById.TryGetValue(ul.G1, out var a) ? a.CaEst : 0, g2 = data.PlayerById.TryGetValue(ul.G2, out var b) ? b.CaEst : 0;
                    keys = p.CaEst >= g1 + 5 ? new() { "G1" } : p.CaEst > g2 ? new() { "G2" } : new();
                }
                else keys = full.SlotKeys(id).ToList();
                if (keys.Count > 0) ul.Hold[id] = keys;
            }
        }
        InboxWriter.Welcome(c);
        return c;
    }

    public void Attach(GameData data) => Data = data;

    /// <summary>Сдвиг силы игрока для движка (модель игрока).</summary>
    public double Dl(int id) => Data.PlayerById.TryGetValue(id, out var p) ? PlayerModel.Delta(this, p) + Fatigue.Penalty(Players.GetValueOrDefault(id), p.IsGoalie) + Mood(id) : 0;

    /// <summary>Настрой и игра через боль (ед. шкалы 1–20).</summary>
    double Mood(int id)
    {
        double d = Morale.Effect(this, id);
        if (Players.TryGetValue(id, out var s) && s.HurtUntil is { } h && h >= Today) d -= HURT_DL;
        return d;
    }
    /// <summary>Игра через боль: минус к уровню (дизайн) и риск травмы × HURT_INJ.</summary>
    public const double HURT_DL = 0.7, HURT_INJ = 2.5;

    /// <summary>Сдвиг силы в плей-офф: + хладнокровие (опыт больших матчей), в пределах, которые не ловит зеркальная проверка.</summary>
    public double DlPlayoff(int id) => Data.PlayerById.TryGetValue(id, out var p) ? PlayerModel.Delta(this, p) + PlayerModel.Composure(this, p) + Fatigue.Penalty(Players.GetValueOrDefault(id), p.IsGoalie) + Mood(id) : 0;

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
        // в глазах помощника (его оценка игроков может ошибаться), против звеньев «как в НХЛ»
        var def = Lineup.FromTeam(Data, Data.TeamByAbbr[abbr], Injured.ToHashSet());
        def.FRole = l.FRole; def.DRole = l.DRole;
        var who = HM27.Core.Staff.Decider(this, abbr);
        var (f1, g1) = LineOpt.Eval(this, abbr, l, who); var (f0, g0) = LineOpt.Eval(this, abbr, def, who);
        return (f1 - f0, g1 - g0);
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
    // кириллица без \uXXXX (в 3 раза короче), пустые ссылки не пишутся
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false, Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
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
        // старые сохранения: система из 4 рычагов — дополняем серединой новых
        foreach (var l in c.Lineups.Values)
            if (l.Sys != null && l.Sys.Length < Tactics.Levers.Length) { var s = new int[Tactics.Levers.Length]; l.Sys.CopyTo(s, 0); l.Sys = s; }
        if (c.Plan?.Sys != null && c.Plan.Sys.Length < Tactics.Levers.Length) { var s = new int[Tactics.Levers.Length]; c.Plan.Sys.CopyTo(s, 0); c.Plan.Sys = s; }
        if (c.LastUserSys != null && c.LastUserSys.Length < Tactics.Levers.Length) { var s = new int[Tactics.Levers.Length]; c.LastUserSys.CopyTo(s, 0); c.LastUserSys = s; }
        return c;
    }

    // ------------------------------------------------------------------ календарь
    public IEnumerable<ScheduledGame> GamesOn(DateOnly d) => Games.Where(g => g.Date == d);
    public IEnumerable<ScheduledGame> TeamGames(string t) => Games.Where(g => g.Home == t || g.Away == t);
    public ScheduledGame NextGame(string t) => Games.Where(g => !g.Played && (g.Home == t || g.Away == t)).OrderBy(g => g.Date).FirstOrDefault();
    public ScheduledGame LastGame(string t) => Games.Where(g => g.Played && (g.Home == t || g.Away == t)).OrderByDescending(g => g.Date).FirstOrDefault();
    public bool PlayedYesterday(string t, DateOnly d) => Games.Any(g => g.Date == d.AddDays(-1) && (g.Home == t || g.Away == t));

    // ------------------------------------------------------------------ сыгранность
    /// <summary>Минуты вместе на льду для пар партнёров (тройки — между собой, пары защиты — между собой); ключ — ShiftEngine.PairKey.</summary>
    public Dictionary<long, float> Chem { get; set; }
    /// <summary>Когда главный тренер ИИ последний раз правил звенья.</summary>
    public Dictionary<string, DateOnly> AiTuned { get; set; } = new();
    /// <summary>Система, которой клуб игрока играл в последнем матче (ИИ отвечает на неё в серии плей-офф).</summary>
    public int[] LastUserSys { get; set; }
    /// <summary>Новости: настроение трибун 0–100 и посты болельщиков, рейтинг силы прошлой недели, призы, погони, кресла тренеров.</summary>
    public float FanMood { get; set; } = 50;
    public List<FanPost> Fans { get; set; } = new();
    public List<FanPoint> FanTrend { get; set; } = new();
    public Dictionary<string, int> PowerWeek { get; set; } = new();
    public List<Honor> Honors { get; set; } = new();
    public HashSet<string> Chases { get; set; } = new();
    public Dictionary<string, int> HotSeat { get; set; } = new();
    /// <summary>Журнал тренера: свои матчи — система и то, что она дала (Journal).</summary>
    public List<JournalEntry> Journal { get; set; } = new();
    /// <summary>Тренеры: главные тренеры клубов ИИ и помощник своего клуба (Staff).</summary>
    public List<StaffMember> Staff { get; set; }
    /// <summary>План тренировок своего клуба (Training); null — обычная общая подготовка.</summary>
    public TrainingPlan Training { get; set; }
    /// <summary>Матчи, перед которыми тренер на пресс-конференции «обещал доказать» (Press).</summary>
    public List<int> Boasts { get; set; } = new();
    /// <summary>План тренера на ближайший свой матч (Career.Sides); после матча — сброс.</summary>
    public MatchPlan Plan { get; set; }
    /// <summary>Сыгранность 0–1 = 1 − e^(−(база + минуты вместе) / FAM_M0). База — общие тренировки и матчи одного клуба;
    /// звенья на старте (как в последних матчах клуба) — FAM_START минут, это ровно ShiftEngine.FAM_REF.</summary>
    public const double FAM_M0 = 200, FAM_BASE = 40;
    public static readonly double FAM_START = -FAM_M0 * Math.Log(1 - ShiftEngine.FAM_REF) - FAM_BASE;

    public double Fam(int a, int b)
    {
        EnsureChem();
        return 1 - Math.Exp(-(FAM_BASE + Chem.GetValueOrDefault(ShiftEngine.PairKey(a, b))) / FAM_M0);
    }

    /// <summary>Сыгранность звена (тройки или пары): средняя по парам.</summary>
    public double UnitFam(IReadOnlyList<int> ids)
    {
        double s = 0; int n = 0;
        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
                if (ids[i] != 0 && ids[j] != 0) { s += Fam(ids[i], ids[j]); n++; }
        return n == 0 ? 0 : s / n;
    }

    void EnsureChem()
    {
        if (Chem != null) return;
        Chem = new();
        foreach (var l in Lineups.Values)
        {
            // звенья, пары и спецбригады — как в последних матчах клуба: сыграны на старте
            foreach (var unit in l.F.Concat(l.D).Concat(new[] { l.Pp1, l.Pp2, l.Pk1, l.Pk2 }).Where(u => u != null))
                for (int i = 0; i < unit.Length; i++)
                    for (int j = i + 1; j < unit.Length; j++)
                        if (unit[i] != 0 && unit[j] != 0) Chem[ShiftEngine.PairKey(unit[i], unit[j])] = (float)FAM_START;
        }
    }

    /// <summary>Сыгранность с тренировок: минуты вместе для пар внутри звеньев (Training, упор «сыгранность»).</summary>
    public void TrainChem(IEnumerable<int[]> units, float minutes)
    {
        EnsureChem();
        foreach (var u in units)
        {
            if (u == null) continue;
            for (int i = 0; i < u.Length; i++)
                for (int j = i + 1; j < u.Length; j++)
                    if (u[i] != 0 && u[j] != 0) { var k = ShiftEngine.PairKey(u[i], u[j]); Chem[k] = Chem.GetValueOrDefault(k) + minutes; }
        }
    }

    void AddChem(MatchResult r)
    {
        if (r.PairSec == null) return;
        EnsureChem();
        foreach (var (k, sec) in r.PairSec) Chem[k] = Chem.GetValueOrDefault(k) + sec / 60f;
        r.PairSec = null;
    }

    public MatchSide Side(string abbr, DateOnly d, bool forPreview = false, bool playoff = false)
    {
        var l = Lineups[abbr];
        bool b2b = PlayedYesterday(abbr, d);
        int goalie = l.G1;
        if (abbr == UserTeam)
        {
            // помощник даёт отдых основному: второй матч за два дня или основной вымотан (свежесть ниже 60%)
            double fr = Players.TryGetValue(l.G1, out var gs) ? gs.Fresh : 100;
            if (l.BackupOnB2B && l.G2 != 0 && (b2b || fr < 60)) goalie = l.G2;
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
                Team = Data.TeamByAbbr[abbr], Lineup = l, Tempo = l.Tempo, Goalie = goalie, B2B = b2b, Dl = dl, Att = att, Dfn = dfn, Attr = TeamStrength.Attr(this, dl), Fam = Fam,
                BaseOff = MatchEngine.OffIndex(Data, l, dl), BaseDef = MatchEngine.DefIndex(Data, l, dl), BaseGsax = MatchEngine.GsaxOf(Data, goalie, dl),
            };
        }
        return new MatchSide
        {
            Team = Data.TeamByAbbr[abbr], Lineup = l, Tempo = l.Tempo, BaseOff = BaseOff[abbr], BaseDef = BaseDef[abbr], BaseGsax = BaseGsax[abbr], Goalie = goalie, B2B = b2b, Dl = dl,
        };
    }

    public double Sharp => BottomUp && ShiftEngine.Enabled ? ShiftEngine.SHARP : MatchEngine.SHARP;

    public MatchOdds Preview(ScheduledGame g)
    {
        var (h, a) = Sides(g, true);
        return MatchEngine.Odds(Data, h, a, g.Playoff, sharp: Sharp);
    }

    /// <summary>Стороны матча с планом тренеров на этот матч (темп по сопернику, снятие вратаря — Coach.Prepare).</summary>
    public (MatchSide home, MatchSide away) Sides(ScheduledGame g, bool preview = false)
    {
        var h = Side(g.Home, g.Date, preview, g.Playoff); var a = Side(g.Away, g.Date, preview, g.Playoff);
        // план тренера на этот матч: система на матч и задания на соперника
        if (Plan != null && Plan.Game == g.Id && (g.Home == UserTeam || g.Away == UserTeam))
        {
            var me = g.Home == UserTeam ? h : a;
            me.Plan = (int[])Plan.Sys?.Clone(); me.Targets = new(Plan.All());
        }
        if (BottomUp)
        {
            Tactics.Prepare(this, h, a);
            // тренеры ИИ: план под соперника (гибкие)
            var hp = h.Plan; var ap = a.Plan;
            AiCoach.Plan(this, g, h, a); AiCoach.Plan(this, g, a, h);
            if (h.Plan != hp || a.Plan != ap) Tactics.Prepare(this, h, a);
        }
        Coach.Prepare(this, g, h, a);
        foreach (var x in new[] { h, a }) { x.StartSys = (int[])(x.Plan ?? x.Lineup.Sys)?.Clone(); x.StartTempo = x.Tempo; }
        return (h, a);
    }

    // ------------------------------------------------------------------ симуляция
    Random RngFor(ScheduledGame g) => new(unchecked(Seed * 1_000_003 ^ g.Id));   // детерминированно (HashCode в .NET случаен по процессам)

    public MatchResult Play(ScheduledGame g)
    {
        if (g.Played) return g.Result;
        var (h, a) = PrepareMatch(g);
        // новые карьеры — движок по сменам; старые сохранения (сила модели) — движок «матч целиком»
        MatchResult r;
        if (BottomUp && ShiftEngine.Enabled)
        {
            // перерывы: помощники обоих клубов решают по своим правилам (база — план на матч)
            var hb = (h.Tempo, h.Plan ?? h.Lineup.Sys); var ab = (a.Tempo, a.Plan ?? a.Lineup.Sys);
            r = ShiftEngine.Play(Data, h, a, RngFor(g), g.Playoff, sim => Rules.Break(this, sim, hb, ab));
        }
        else r = MatchEngine.Play(Data, h, a, RngFor(g), g.Playoff);
        Complete(g, h, a, r);
        return r;
    }

    /// <summary>Перед матчем: звенья с учётом травм (свой клуб — места выбывших запомнены, ИИ — как в НХЛ) и стороны с планами.</summary>
    (MatchSide h, MatchSide a) PrepareMatch(ScheduledGame g)
    {
        var inj = Injured.ToHashSet();
        foreach (var t in new[] { g.Home, g.Away })
        {
            if (t == UserTeam)
            {
                Lineups[t].Restore(Data, t, inj);
                Lineups[t].Repair(Data, t, inj, hold: true);
            }
            else
            {
                // ИИ: свои звенья (места выбывших запомнены, вернувшиеся — на место), раз в неделю тренер их правит
                var l = Lineups[t];
                l.Restore(Data, t, inj);
                l.Repair(Data, t, inj, hold: true);
                AiCoach.BeforeMatch(this, t, l);
            }
        }
        return Sides(g);
    }

    /// <summary>
    /// Свой матч в трансляции: движок по шагам. В перерывах ИИ решает сам, а свой клуб — тренер (помощник советует);
    /// FinishLive доигрывает (оставшиеся перерывы — по правилам помощника) и записывает матч как обычно.
    /// </summary>
    public sealed class LiveGame
    {
        public ScheduledGame G; public ShiftEngine.Sim Sim; public MatchSide H, A;
        public (int tempo, int[] sys) HB, AB;
        public bool UserHome; public bool AtBreak;
        /// <summary>Слова команде в этом матче: «вид:период» (одно на период).</summary>
        public readonly HashSet<string> Talks = new();
        public MatchSide Me => UserHome ? H : A;
        public MatchSide Opp => UserHome ? A : H;
    }

    public LiveGame BeginLive(ScheduledGame g)
    {
        if (g.Played || !BottomUp || !ShiftEngine.Enabled) return null;
        var (h, a) = PrepareMatch(g);
        // в трансляции свой состав — копия: перестановки по ходу матча не меняют звенья клуба
        if (g.Home == UserTeam) h.Lineup = h.Lineup.Clone(); else a.Lineup = a.Lineup.Clone();
        var lg = new LiveGame { G = g, H = h, A = a, UserHome = g.Home == UserTeam, HB = (h.Tempo, h.Plan ?? h.Lineup.Sys), AB = (a.Tempo, a.Plan ?? a.Lineup.Sys) };
        lg.Sim = ShiftEngine.Start(Data, h, a, RngFor(g), g.Playoff);
        lg.Sim.PauseAtBreaks = true;
        lg.Sim.OnBreak = sim =>
        {
            // соперник решает по своим правилам сразу; свой клуб — на экране перерыва
            var side = lg.UserHome ? sim.AwaySide : sim.HomeSide;
            var (bt, bs) = lg.UserHome ? lg.AB : lg.HB;
            var d = Rules.Decide(sim, !lg.UserHome, side.Lineup.Rules ?? Rules.Ai(), bt, bs);
            if (Rules.ApplyTo(this, sim, !lg.UserHome, d)) { Tactics.Prepare(this, sim.HomeSide, sim.AwaySide); sim.Apply(); }
            lg.AtBreak = true;
        };
        return lg;
    }

    /// <summary>Доиграть трансляцию (оставшиеся перерывы — по правилам помощника) и записать матч.</summary>
    public MatchResult FinishLive(LiveGame lg)
    {
        if (lg.G.Played) return lg.G.Result;
        lg.Sim.PauseAtBreaks = false;
        lg.Sim.OnBreak = sim => Rules.Break(this, sim, lg.HB, lg.AB);
        lg.Sim.Run();
        Complete(lg.G, lg.H, lg.A, lg.Sim.Result);
        return lg.G.Result;
    }

    /// <summary>После матча: сыгранность, хроника, статистика, травмы, усталость, настрой, выучка, новости.</summary>
    void Complete(ScheduledGame g, MatchSide h, MatchSide a, MatchResult r)
    {
        if (Plan?.Game == g.Id) Plan = null;
        if (g.Home == UserTeam || g.Away == UserTeam) { var us = g.Home == UserTeam ? h : a; LastUserSys = (int[])(us.Plan ?? us.Lineup.Sys)?.Clone(); }
        g.Result = r;
        AddChem(r);
        bool mine = g.Home == UserTeam || g.Away == UserTeam;
        Chronicle.Build(Data, g, h, a, r, new Random(unchecked(Seed * 7919 + g.Id * 31 + 1)));
        Record(g);
        InjuryModel.AfterGame(this, g, new Random(unchecked(Seed * 104729 + g.Id)));
        Fatigue.AfterGame(this, g);
        Morale.AfterGame(this, g);
        if (mine) Orders.AfterGame(this, UserTeam, g.Home == UserTeam ? g.Result.HomeDressed : g.Result.AwayDressed);
        if (mine) Press.AfterGame(this, g);
        if (mine) HM27.Core.Journal.Record(this, g, g.Home == UserTeam ? h : a, r);
        if (mine) Voices.AfterGame(this, g);
        Tactics.AfterGame(this, g);
        if (!mine) { r.Events = new(); r.Box = new(); }   // хронику и протокол храним только для своих матчей — сохранение не раздувается
        foreach (var (side, abbr) in new[] { (h, g.Home), (a, g.Away) })
            GoalieStreak[abbr] = side.Goalie == Lineups[abbr].G1 ? GoalieStreak.GetValueOrDefault(abbr) + 1 : 0;
        if (mine) News.MatchReport(this, g);
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
        Fatigue.Recover(this, 1);
        InjuryModel.Heal(this);
        if (Today.DayOfWeek == DayOfWeek.Monday) Development.Week(this);   // развитие, форма, знание — раз в неделю
        if (Phase == Phase.Playoffs) SchedulePlayoffDay();
        InboxWriter.Periodic(this);
        Trust.Daily(this);
        HM27.Core.Training.Daily(this);
        AiCoach.Daily(this);
        Morale.Daily(this);
        Tactics.Daily(this);
        Decisions.Daily(this);
        News.NewDay(this);
    }

    /// <summary>Симулировать до ближайшего матча клуба игрока (сам матч не играется).</summary>
    public int SimToUserGame(bool stopOnDecision = false)
    {
        int days = 0;
        while (Phase != Phase.Finished && UserGameToday() == null && days < 400)
        {
            if (stopOnDecision && Decisions.Blocking(this) > 0) break;   // интерфейс: сначала ответьте на обязательное письмо
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
            var s = Get(sk, e.Scorer); s.G++; if (e.Kind == "pp") { s.Ppg++; s.Ppp++; }
            if (e.A1 != 0) { var a1 = Get(sk, e.A1); a1.A++; if (e.Kind == "pp") a1.Ppp++; }
            if (e.A2 != 0) { var a2 = Get(sk, e.A2); a2.A++; if (e.Kind == "pp") a2.Ppp++; }
        }
        foreach (var (id, b) in r.Box)
        {
            if (!Data.PlayerById.TryGetValue(id, out var bp) || bp.IsGoalie) continue;
            var s = Get(sk, id); s.Toi += b.Toi; s.S += b.S; s.Att += b.Att; s.Blk += b.Blk; s.Pm += b.Pm; s.Pim += b.Pim;
            s.Fow += b.Fow; s.Fol += b.Fol; s.Hit += b.Hit; s.Tk += b.Tk; s.Gv += b.Gv; s.Ixg += b.Ixg;
        }
        // Победный гол: гол, после которого у победителя на 1 больше, чем у проигравшего в итоге
        var winTeam = homeWon ? g.Home : g.Away; int loserGoals = homeWon ? r.AwayGoals : r.HomeGoals; int cnt = 0;
        foreach (var e in r.Goals.Where(e => e.Team == winTeam && e.Kind != "so"))
            if (++cnt == loserGoals + 1) { Get(sk, e.Scorer).Gwg++; break; }

        void G(int goalie, int sa, int ga, bool won, string end, bool shutout, bool decision = true)
        {
            if (goalie == 0) return;
            var x = Get(gk, goalie); x.Gp++; x.Sa += sa; x.Ga += ga;
            if (r.Box.TryGetValue(goalie, out var gb)) x.Xga += gb.Xga;
            if (r.Ratings.TryGetValue(goalie, out var rt)) { x.RatingSum += rt; x.Rated++; }
            if (!decision) return;
            if (won) x.W++; else if (end == "REG" || g.Playoff) x.L++; else x.Otl++;
            if (shutout) x.So++;
        }
        // замена по ходу матча: оба вратаря — по своему протоколу, решение — тому, кто доиграл
        foreach (var (g1, g2, won) in new[] { (r.HomeGoalie, r.HomeGoalie2, homeWon), (r.AwayGoalie, r.AwayGoalie2, !homeWon) })
        {
            if (g2 == 0) continue;
            foreach (var (id, dec) in new[] { (g1, false), (g2, true) })
                if (r.Box.TryGetValue(id, out var bx)) G(id, bx.Sa, bx.Ga, won, r.End, false, dec);
        }
        // голы в пустые ворота и победный буллит вратарю не засчитываются
        int hGa = r.AwayGoals - (r.End == "SO" && !homeWon ? 1 : 0) - r.Goals.Count(e => e.Team == g.Away && e.Kind == "en");
        int aGa = r.HomeGoals - (r.End == "SO" && homeWon ? 1 : 0) - r.Goals.Count(e => e.Team == g.Home && e.Kind == "en");
        // броски по вратарю: из протокола (без бросков по пустым воротам), иначе — броски команды без голов в пустые
        int hSa = r.Box.TryGetValue(r.HomeGoalie, out var hb) ? hb.Sa : r.AwayShots - r.Goals.Count(e => e.Team == g.Away && e.Kind == "en");
        int aSa = r.Box.TryGetValue(r.AwayGoalie, out var ab) ? ab.Sa : r.HomeShots - r.Goals.Count(e => e.Team == g.Home && e.Kind == "en");
        if (r.HomeGoalie2 == 0) G(r.HomeGoalie, hSa, hGa, homeWon, r.End, hGa == 0);
        if (r.AwayGoalie2 == 0) G(r.AwayGoalie, aSa, aGa, !homeWon, r.End, aGa == 0);
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
        News.SeasonAwards(this);
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
        Fatigue.Recover(this, 3);
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
    public int Unread => Messages.Count(m => !m.Read && !m.IsNews);

    public string Short(int id) => id != 0 && Data.PlayerById.TryGetValue(id, out var p) ? p.LastRu : "—";
    public TeamData Team(string abbr) => Data.TeamByAbbr[abbr];
    public IEnumerable<PlayerData> Roster(string abbr) => Data.Roster(abbr);
}
