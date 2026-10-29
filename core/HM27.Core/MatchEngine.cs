namespace HM27.Core;

/// <summary>Одна команда в матче.</summary>
public sealed class MatchSide
{
    public TeamData Team;
    public Lineup Lineup;
    /// <summary>Индексы состава по умолчанию (база калибровки): отличие текущего состава от него — эффект решений тренера.</summary>
    public double BaseOff, BaseDef;
    /// <summary>GSAx вратарей по умолчанию (0,8 основного + 0,2 запасного): сила обороны клуба в модели уже включает их.</summary>
    public double BaseGsax;
    public int Goalie;
    public bool B2B;
    /// <summary>Сдвиг силы игрока (ед. шкалы 1–20): истинный уровень − уровень по данным + форма. null — без модели игрока.</summary>
    public Func<int, double> Dl;
    /// <summary>Сила клуба «снизу вверх» (из игроков базы); null — сила модели «Сводки» из данных клуба.</summary>
    public double? Att, Dfn;
    /// <summary>Характеристика игрока на матч (истинная + форма); null — оценка из данных. Нужна движку по сменам.</summary>
    public Func<PlayerData, string, double> Attr;
    /// <summary>Сыгранность пары партнёров 0–1 (движок по сменам); null — без сыгранности (нейтрально).</summary>
    public Func<int, int, double> Fam;
}

public sealed class GoalEvent
{
    public int Period { get; set; }          // 1–3, 4+ — овертайм
    public int Sec { get; set; }             // секунда внутри периода
    public string Team { get; set; }
    public int Scorer { get; set; }
    public int A1 { get; set; }
    public int A2 { get; set; }
    public string Kind { get; set; } = "ev"; // ev | pp | sh | en | ev6 | so (победный буллит)
    /// <summary>Тип броска (движок по сменам): «кистевой», «щелчок», «добивание»…</summary>
    public string Shot { get; set; }
}

public sealed class MatchResult
{
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public string End { get; set; } = "REG";     // REG | OT | SO
    public int HomeShots { get; set; }
    public int AwayShots { get; set; }
    public int HomeGoalie { get; set; }
    public int AwayGoalie { get; set; }
    public List<GoalEvent> Goals { get; set; } = new();
    public double PHome { get; set; }            // шанс хозяев перед матчем (с ОТ и буллитами)
    public double LamHome { get; set; }
    public double LamAway { get; set; }
    /// <summary>Кто был в составе (для «сыгранных матчей» в статистике).</summary>
    public List<int> HomeDressed { get; set; } = new();
    public List<int> AwayDressed { get; set; } = new();
    /// <summary>Оценки игроков за матч (6,0–10,0), как в FM.</summary>
    public Dictionary<int, double> Ratings { get; set; } = new();
    /// <summary>Хроника матча: броски, сейвы, удаления — постановка, согласованная с итогом движка.</summary>
    public List<MatchEvent> Events { get; set; } = new();
    /// <summary>Протокол игроков (движок по сменам): минуты, броски, блоки, +/−, штраф; у вратарей — броски и ожидаемые голы.
    /// Хранится только для своих матчей (как хроника), в сезонную статистику попадает всегда.</summary>
    public Dictionary<int, PlayerBox> Box { get; set; } = new();
    /// <summary>Вратарь снят раньше обычного (стиль игры), и это что-то изменило.</summary>
    public string PullNote { get; set; }
    /// <summary>Секунды вместе на льду для пар партнёров (нападающие с нападающими, защитники с защитниками) — для сыгранности.
    /// Не сохраняется: карьера забирает сразу после матча.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<long, int> PairSec { get; set; }
}

public sealed class MatchEvent
{
    public int T { get; set; }              // секунда матча от начала (ОТ — после 3600)
    public string Team { get; set; }
    public string Kind { get; set; }        // shot (сейв) | miss | block | goal | pen | pp_end
    public int P1 { get; set; }             // бросающий / нарушитель / автор
    public int P2 { get; set; }             // вратарь / заблокировавший
    public string Note { get; set; }        // тип броска или нарушения
    /// <summary>Бросок (движок по сменам): зона 0 — у ворот, 1 — средняя, 2 — издали; −1 — не бросок.</summary>
    public int Z { get; set; } = -1;
    /// <summary>Ожидаемый гол броска в створ (без вратаря); у блоков и промахов — 0.</summary>
    public double Xg { get; set; }
}

/// <summary>Шансы до матча.</summary>
public readonly record struct MatchOdds(double LamHome, double LamAway, double PHome, double PHomeReg, double PAwayReg, double PTie, double QOtHome);

/// <summary>
/// Движок уровня 1 «матч целиком» — модель прогнозов NHL-STATS.RU v4.4 (scripts/model.py), перенесённая в игру.
/// Шансы считаются как в модели (Пуассон, доля ничьих ×1,35, ОТ 3 на 3, калибровка уверенности 1,33),
/// затем исход разыгрывается, а счёт берётся из того же пуассоновского распределения.
/// </summary>
public static class MatchEngine
{
    public const double TIE_K = 1.35, OT_HOME = 0.517, OT_K = 0.45, SHARP = 1.33;
    public const double HA = 0.02, HA_MULT = 1.015, B2B = 0.95, PO_TOT = 0.97;
    /// <summary>Решения тренера: + голов своей команды и − голов соперника на единицу личного xG за матч сверх базы.</summary>
    public const double K_FOR = 0.45, K_AGAINST = 0.35;
    /// <summary>Защитник: ±голов за каждую минуту реального времени сверх 16 (модель, п. 5).</summary>
    public const double D_K = 0.012, D_BASE = 16;
    /// <summary>Вратарь: голы соперника × (1 − G_W × GSAx) / (1 − G_W × GSAx вратарей по умолчанию), в пределах ±15%.</summary>
    public const double G_W = 0.8;
    /// <summary>Доля голов в основное время без овертаймных (в базе — голы с ОТ).</summary>
    public const double REG_SHARE = 0.97;
    /// <summary>Темп игры: каждый шаг ±7% к голам обеих команд. Меньше голов — больше овертаймов, это помогает слабому.</summary>
    public const double TEMPO_K = 0.07;
    /// <summary>
    /// Раннее снятие вратаря при −1 в концовке: +3,5% сравнять и +7% пропустить в пустые (против средних по НХЛ).
    /// Аналитика НХЛ: тренеры снимают вратаря слишком поздно, раннее снятие в среднем приносит очки.
    /// </summary>
    public const double PULL_TIE = 0.035, PULL_EN = 0.07, PULL_TIE2 = 0.008, PULL_EN2 = 0.06;

    static double Pois(int k, double l)
    {
        double r = Math.Exp(-l);
        for (int i = 1; i <= k; i++) r *= l / i;
        return r;
    }

    /// <summary>Атакующий индекс состава: ожидаемый личный xG всех полевых за матч при их минутах.</summary>
    public static double OffIndex(GameData data, Lineup l, Func<int, double> dl = null)
    {
        double s = 0;
        foreach (var id in l.Skaters.Distinct())
            if (data.PlayerById.TryGetValue(id, out var p)) s += p.Ixg60 * Lvl(dl, id) * l.MinutesOf(id) / 60.0;
        return s;
    }

    /// <summary>Множитель личного xG от модели игрока: e^(LVL_K × сдвиг уровня).</summary>
    public static double Lvl(Func<int, double> dl, int id) => dl == null ? 1 : Math.Exp(PlayerModel.LVL_K * dl(id));

    /// <summary>Индекс защиты: у защитников — «доверие» (реальное время сверх 16 минут) пропорционально их минутам.</summary>
    public static double DefIndex(GameData data, Lineup l, Func<int, double> dl = null)
    {
        double s = 0;
        foreach (var pair in l.D)
            foreach (var id in pair)
                if (id != 0 && data.PlayerById.TryGetValue(id, out var p))
                    s += (p.IsDefense ? D_K * (p.Toi - D_BASE + PlayerModel.D_LVL * (dl?.Invoke(id) ?? 0)) : -0.06) * l.MinutesOf(id) / Math.Max(p.Toi, 12);
        return s / 2;  // пара делит эффект
    }

    public static double GoalieFactor(GameData data, int goalie, double baseGsax, Func<int, double> dl = null)
    {
        double gs = GsaxOf(data, goalie, dl);
        return Math.Clamp((1 - G_W * gs) / (1 - G_W * baseGsax), 0.85, 1.15);
    }

    public static double GsaxOf(GameData data, int id, Func<int, double> dl = null) =>
        id != 0 && data.PlayerById.TryGetValue(id, out var g) ? g.Gsax + PlayerModel.G_LVL * (dl?.Invoke(id) ?? 0) : -0.12;

    public static MatchOdds Odds(GameData data, MatchSide home, MatchSide away, bool playoff = false, bool neutral = false, double sharp = SHARP)
    {
        double mu = Math.Log(data.League.GoalsPerGame * REG_SHARE);
        double ha = neutral ? 0 : HA;
        double bh = Math.Exp(mu + (home.Att ?? home.Team.Att) + (away.Dfn ?? away.Team.Dfn) + ha);
        double ba = Math.Exp(mu + (away.Att ?? away.Team.Att) + (home.Dfn ?? home.Team.Dfn) - ha);

        double dOffH = OffIndex(data, home.Lineup, home.Dl) - home.BaseOff, dOffA = OffIndex(data, away.Lineup, away.Dl) - away.BaseOff;
        double dDefH = DefIndex(data, home.Lineup, home.Dl) - home.BaseDef, dDefA = DefIndex(data, away.Lineup, away.Dl) - away.BaseDef;
        double adjH = Math.Clamp(K_FOR * dOffH - K_AGAINST * dOffA + dDefH - dDefA, -0.9, 0.9);
        double adjA = Math.Clamp(K_FOR * dOffA - K_AGAINST * dOffH + dDefA - dDefH, -0.9, 0.9);

        double lh = Math.Max(0.6, bh + adjH) * GoalieFactor(data, away.Goalie, away.BaseGsax, away.Dl);
        double la = Math.Max(0.6, ba + adjA) * GoalieFactor(data, home.Goalie, home.BaseGsax, home.Dl);
        if (home.B2B && !away.B2B) { lh *= B2B; la /= B2B; }
        if (away.B2B && !home.B2B) { la *= B2B; lh /= B2B; }
        if (playoff) { lh *= PO_TOT; la *= PO_TOT; }
        else if (!neutral) { lh *= HA_MULT; la /= HA_MULT; }
        double tempo = (1 + TEMPO_K * home.Lineup.Tempo) * (1 + TEMPO_K * away.Lineup.Tempo);
        lh *= tempo; la *= tempo;

        double phr0 = 0, par0 = 0;
        for (int i = 0; i < 13; i++)
            for (int j = 0; j < 13; j++)
            {
                double p = Pois(i, lh) * Pois(j, la);
                if (i > j) phr0 += p; else if (j > i) par0 += p;
            }
        double tie0 = 1 - phr0 - par0;
        double tie = Math.Min(0.5, tie0 * TIE_K);
        double phr = phr0 * (1 - tie) / (phr0 + par0), par = par0 * (1 - tie) / (phr0 + par0);
        double q = Math.Clamp((neutral ? 0.5 : OT_HOME) + OT_K * (phr - par), 0.2, 0.8);
        double ph = phr + tie * q;
        double z = sharp * Math.Log(ph / (1 - ph));
        ph = 1 / (1 + Math.Exp(-z));
        // v4.3: основное время согласовано с итоговой вероятностью
        double phrF = Math.Min(1 - tie, Math.Max(0, ph - tie * q));
        double parF = 1 - tie - phrF;
        return new MatchOdds(lh, la, ph, phrF, parF, tie, q);
    }

    public static MatchResult Play(GameData data, MatchSide home, MatchSide away, Random rng, bool playoff = false)
    {
        var o = Odds(data, home, away, playoff);
        var r = new MatchResult { PHome = o.PHome, LamHome = o.LamHome, LamAway = o.LamAway, HomeGoalie = home.Goalie, AwayGoalie = away.Goalie };
        r.HomeDressed = home.Lineup.Skaters.Append(home.Goalie).Where(x => x != 0).Distinct().ToList();
        r.AwayDressed = away.Lineup.Skaters.Append(away.Goalie).Where(x => x != 0).Distinct().ToList();

        // 1. Исход основного времени
        double u = rng.NextDouble();
        int outcome = u < o.PHomeReg ? 1 : u < o.PHomeReg + o.PAwayReg ? -1 : 0;
        // 2. Счёт основного времени из пуассоновского распределения в пределах исхода
        (int hg, int ag) = SampleScore(o.LamHome, o.LamAway, outcome, rng);
        // 2а. Раннее снятие вратаря (стиль игры проигрывающей команды). Без этой настройки случайность не тратится.
        int lateTie = 0, lateEn = 0;   // +1 хозяевам / −1 гостям
        if (outcome != 0)
        {
            var trail = outcome > 0 ? away : home;
            int diff = Math.Abs(hg - ag);
            if (trail.Lineup.EarlyPull && diff <= 2)
            {
                double u2 = rng.NextDouble();
                double pt = diff == 1 ? PULL_TIE : PULL_TIE2, pe = diff == 1 ? PULL_EN : PULL_EN2;
                if (u2 < pt && diff == 1) { if (outcome > 0) { ag++; lateTie = -1; } else { hg++; lateTie = 1; } outcome = 0; r.PullNote = "tie"; }
                else if (u2 < pt + pe) { if (outcome > 0) { hg++; lateEn = 1; } else { ag++; lateEn = -1; } r.PullNote = "en"; }
            }
        }
        r.HomeGoals = hg; r.AwayGoals = ag;

        var events = new List<(int t, bool home)>();
        int hgN = hg - (lateTie == 1 ? 1 : 0) - (lateEn == 1 ? 1 : 0), agN = ag - (lateTie == -1 ? 1 : 0) - (lateEn == -1 ? 1 : 0);
        for (int i = 0; i < hgN; i++) events.Add((rng.Next(0, 3600), true));
        for (int i = 0; i < agN; i++) events.Add((rng.Next(0, 3600), false));
        int tTie = lateTie != 0 ? rng.Next(3480, 3590) : -1, tEn = lateEn != 0 ? rng.Next(3500, 3598) : -1;
        if (lateTie != 0) events.Add((tTie, lateTie == 1));
        if (lateEn != 0) events.Add((tEn, lateEn == 1));
        events.Sort((a, b) => a.t.CompareTo(b.t));
        foreach (var (t, isHome) in events)
        {
            var ge = MakeGoal(data, isHome ? home : away, isHome ? away : home, t / 1200 + 1, t % 1200, rng);
            if (t == tEn) ge.Kind = "en";
            if (t == tTie) ge.Kind = "ev6";   // с шестым полевым
            r.Goals.Add(ge);
        }

        // 3. Ничья → овертайм (в регулярке 3 на 3, 5 минут, потом буллиты; в плей-офф — до гола)
        if (outcome == 0)
        {
            bool homeWins = rng.NextDouble() < o.QOtHome;
            var w = homeWins ? home : away; var l = homeWins ? away : home;
            bool decidedInOt = playoff || rng.NextDouble() < 0.62;
            if (decidedInOt)
            {
                int period = 4, sec = rng.Next(5, playoff ? 1200 : 300);
                if (playoff) while (rng.NextDouble() < 0.25) period++;   // длинные овертаймы плей-офф
                r.End = "OT";
                r.Goals.Add(MakeGoal(data, w, l, period, sec, rng, ot: true));
            }
            else
            {
                r.End = "SO";
                r.Goals.Add(new GoalEvent { Period = 5, Sec = 0, Team = w.Team.Abbr, Kind = "so" });
            }
            if (homeWins) r.HomeGoals++; else r.AwayGoals++;
        }

        // 4. Броски в створ: около 29 на команду, больше у создающей моменты. Вратарь соперника меняет голы, а не броски:
        //    без его поправки сильный вратарь «срезал» бы и броски, и его % отражённых почти не отличался бы от среднего
        double shH = o.LamHome / GoalieFactor(data, away.Goalie, away.BaseGsax, away.Dl);
        double shA = o.LamAway / GoalieFactor(data, home.Goalie, home.BaseGsax, home.Dl);
        r.HomeShots = Math.Max(r.HomeGoals + 8, (int)Math.Round(Normal(rng, 28.5 * Math.Pow(shH / 3.0, 0.6), 4.5)));
        r.AwayShots = Math.Max(r.AwayGoals + 8, (int)Math.Round(Normal(rng, 28.5 * Math.Pow(shA / 3.0, 0.6), 4.5)));
        return r;
    }

    static (int, int) SampleScore(double lh, double la, int outcome, Random rng)
    {
        var cells = new List<(int i, int j, double p)>();
        double total = 0;
        for (int i = 0; i < 11; i++)
            for (int j = 0; j < 11; j++)
            {
                if (Math.Sign(i - j) != outcome) continue;
                double p = Pois(i, lh) * Pois(j, la);
                cells.Add((i, j, p)); total += p;
            }
        double u = rng.NextDouble() * total;
        foreach (var c in cells) { u -= c.p; if (u <= 0) return (c.i, c.j); }
        return (cells[^1].i, cells[^1].j);
    }

    static GoalEvent MakeGoal(GameData data, MatchSide side, MatchSide opp, int period, int sec, Random rng, bool ot = false)
    {
        var l = side.Lineup;
        var ev = new GoalEvent { Period = period, Sec = sec, Team = side.Team.Abbr };
        double kindU = rng.NextDouble();
        int[] unit = null;
        if (!ot && kindU < 0.21)
        {
            ev.Kind = "pp";
            unit = rng.NextDouble() < 0.7 ? l.Pp1 : l.Pp2;
            if (unit.Count(x => x != 0) < 3) unit = null;
        }
        else if (!ot && kindU < 0.235) ev.Kind = "sh";

        IEnumerable<int> cands = unit != null ? unit.Where(x => x != 0) : l.Skaters.Distinct();
        // Автор: пропорционально личному xG за 60 × минуты × реализация
        ev.Scorer = Pick(cands, id =>
        {
            var p = data.PlayerById[id];
            double mins = unit != null ? 1 : l.MinutesOf(id);
            if (ot) mins *= l.UnitOf(id) is 0 or 10 ? 3 : 1;   // в ОТ играют лучшие
            if (ev.Kind == "sh") mins *= p.IsDefense ? 1 : (l.UnitOf(id) is 2 or 3 ? 1.5 : 0.7);
            return p.Ixg60 * p.Fin * mins * Lvl(side.Dl, id);
        }, rng);

        // Передачи: без передач ~7%, одна ~27%, две ~66%; партнёры по звену — в 3 раза чаще
        double au = rng.NextDouble();
        int nA = au < 0.07 ? 0 : au < 0.34 ? 1 : 2;
        int myUnit = l.UnitOf(ev.Scorer);
        var aCands = cands.Where(x => x != ev.Scorer).ToList();
        double AW(int id)
        {
            var p = data.PlayerById[id];
            double mins = unit != null ? 1 : l.MinutesOf(id);
            double same = unit != null ? 1 : (l.UnitOf(id) == myUnit ? 3 : 1);
            return (p.Apg + 0.05) * mins * same;
        }
        if (nA >= 1 && aCands.Count > 0) { ev.A1 = Pick(aCands, AW, rng); aCands.Remove(ev.A1); }
        if (nA >= 2 && aCands.Count > 0) ev.A2 = Pick(aCands, AW, rng);
        return ev;
    }

    static int Pick(IEnumerable<int> ids, Func<int, double> w, Random rng)
    {
        var list = ids.Where(id => id != 0).Select(id => (id, w: Math.Max(1e-6, w(id)))).ToList();
        if (list.Count == 0) return 0;
        double u = rng.NextDouble() * list.Sum(x => x.w);
        foreach (var (id, ww) in list) { u -= ww; if (u <= 0) return id; }
        return list[^1].id;
    }

    static double Normal(Random rng, double mean, double sd)
    {
        double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
        return mean + sd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
