namespace HM27.Core;

/// <summary>Строка игрока в протоколе матча (движок по сменам).</summary>
public sealed class PlayerBox
{
    public int Toi { get; set; }        // секунды на льду
    public int PpToi { get; set; }      // из них в большинстве
    public int ShToi { get; set; }      // в меньшинстве
    public int Shifts { get; set; }
    public int S { get; set; }          // броски в створ (с голами)
    public int Att { get; set; }        // попытки бросков
    public int Blk { get; set; }        // заблокированные броски соперника
    public int Pm { get; set; }         // плюс-минус
    public int Pim { get; set; }        // штрафные минуты
    public int Fow { get; set; }        // вбрасывания: выиграно
    public int Fol { get; set; }        //              проиграно
    public int Hit { get; set; }        // силовые приёмы
    public int Tk { get; set; }         // отборы
    public int Gv { get; set; }         // потери
    // вратарь
    public int Sa { get; set; }
    public int Ga { get; set; }
    public double Xga { get; set; }     // ожидаемые голы по броскам в створ (без поправки на вратаря)
    /// <summary>Полевой: ожидаемые голы своих бросков в створ (по зоне, типу и навыку броска; без вратаря).</summary>
    public double Ixg { get; set; }
}

/// <summary>
/// Движок по сменам: матч проигрывается посекундно — кто на льду (звенья, пары, спецбригады, усталость),
/// какой состав (5×5, большинство, 4×4, снятый вратарь, ОТ 3×3), броски (кто, откуда, блок, мимо, в створ, гол), удаления.
///
/// Главный принцип — та же калибровка, что у силы «снизу вверх» (TeamStrength): сила клуба там — сумма вкладов игроков
/// × их доля матча, а это ровно среднее по времени от суммы вкладов пятёрки на льду. Поэтому интенсивность голов
/// в каждый момент = база состава (цели НХЛ) × e^(атака пятёрки + оборона пятёрки соперника), и за матч сила клубов
/// та же, а внутри матча появляется своё: какое звено против какого, кто на большинстве, кто бросает и кто устал.
///
/// Бросок: попытки = ожидаемые голы / ожидаемая отдача попытки; кто бросает и откуда — по характеристикам и особенностям,
/// реализация — по завершению (часть атаки клуба переходит из «создания» в бросок, без двойного счёта),
/// вратарь меняет голы, а не броски. Цели — data/engine_targets.json (протоколы НХЛ 2022-23 … 2025-26).
/// </summary>
public static class ShiftEngine
{
    // ───────────── цели НХЛ: голов за 60 минут на команду по составам (data/engine_targets.json)
    public const double GF60_5V5 = 2.547, GF60_PP = 7.06, GF60_PK = 0.86, GF60_4V4 = 2.43, GF60_3V3 = 6.27, GF60_6V5 = 10.2, GF60_EN = 22.75;
    /// <summary>Голов на команду за матч в НХЛ 2022-26 (с ОТ, без буллитов) — к нему приводится уровень лиги из базы.</summary>
    public const double NHL_GPG = 3.08;
    /// <summary>Выключатель для сравнения со старым движком в HM27.Check.</summary>
    public static bool Enabled = true;
    /// <summary>Общая поправка уровня голов (подбор в HM27.Check: голов за матч как в лиге).</summary>
    public static double K_CAL = 0.98;
    /// <summary>
    /// Уверенность прогноза до матча для этого движка. В модели «Сводки» SHARP 1,33 поправлял сжатые прогнозные λ;
    /// здесь исход разыгрывается из тех же λ (с эффектом счёта), и честный прогноз — почти без поправки (подбор: «Калибровка» в HM27.Check).
    /// </summary>
    public static double SHARP = 1.15;
    /// <summary>Хозяева: +3,5% голов, гости −3,5% (как в модели: HA 0,02 + HA_MULT 1,015).</summary>
    public const double HA_LOG = 0.0349;
    /// <summary>Голы по периодам (кроме пустых ворот): во втором — длинная смена (НХЛ: 1,77 / 2,09 / 2,15 за матч с пустыми).</summary>
    public static double[] PERIOD_K = { 0.955, 1.10, 0.93 };

    // ───────────── броски при 5×5: зоны (0 — у ворот < 6 м, 1 — средняя дистанция, 2 — издали > 12 м); [защитник][зона]
    static readonly double[][] ZONE = { new[] { 0.398, 0.395, 0.207 }, new[] { 0.170, 0.258, 0.572 } };
    static readonly double[][] BLOCK = { new[] { 0.21, 0.25, 0.25 }, new[] { 0.30, 0.40, 0.36 } };
    /// <summary>Мимо — доля незаблокированных (у НХЛ в протоколе — доля всех попыток: 23,4%).</summary>
    static readonly double[][] MISS = { new[] { 0.29, 0.31, 0.33 }, new[] { 0.24, 0.32, 0.44 } };
    /// <summary>Реализация бросков в створ по зонам — без добиваний (они разыгрываются отдельно; в протоколе они внутри зоны «у ворот»).</summary>
    static readonly double[][] SH = { new[] { 0.168 * SH_K, 0.097 * SH_K, 0.019 * SH_K }, new[] { 0.147 * SH_K * SH_D, 0.089 * SH_K * SH_D, 0.027 * SH_K * SH_D } };
    /// <summary>SH_K — без добиваний; SH_D — у защитников броски из тех же зон хуже (НХЛ: у защитников 14,8% голов при 34,8% попыток).</summary>
    const double SH_K = 0.82, SH_D = 0.75;
    /// <summary>
    /// Контраст звеньев внутри клуба: атака звена отличается от средней по клубу в LINE_K раз сильнее, чем по общей шкале
    /// (средняя сила клуба та же). НХЛ 2024-26, 5×5 на льду: первые три нападающих по минутам — 2,88 гола за 60, четвёртая тройка — 2,17.
    /// </summary>
    public static double LINE_K = 1.5;
    /// <summary>
    /// Сыгранность (дизайн, ТЗ «Тренер»): средняя сыгранность пар на льду (тройка — между собой, пара защиты — между собой)
    /// сдвигает атаку пятёрки и оборону против неё на FAM_MAX × (f − FAM_REF) / FAM_REF: новое звено (f ≈ 0,2) — около −2%,
    /// сыгранное весь сезон (f ≈ 1) — +0,8%. FAM_REF — сыгранность звеньев на старте (как в последних матчах клуба), поэтому
    /// составы по умолчанию дают прежнюю силу; лига в целом не меняется (плюс одних — минус против них).
    /// </summary>
    public static double FAM_MAX = 0.025, FAM_REF = 0.75;
    /// <summary>Учётный xG (протокол, статистика, сейвы сверх ожидаемых): ожидание броска без вратаря чуть ниже факта, потому что
    /// средний вратарь движка (GMult) пропускает чуть больше; поправка делает сумму «сейвов сверх ожидаемых» по лиге ≈ 0. На исходы не влияет.</summary>
    public const double XG_CAL = 1.019;
    public static double FamE(double f) => Math.Log(1 + Math.Clamp(FAM_MAX * (f - FAM_REF) / FAM_REF, -FAM_MAX, FAM_MAX));
    public static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    /// <summary>Вес защитника в выборе бросающего (доля попыток защитников при 5×5 — 34,8%).</summary>
    public const double D_SHOT_W = 0.80;
    /// <summary>Кто бросает: e^(SHOOT_K × (бросковые характеристики − 11)), «бросает при любой возможности» × SHOOT_TRAIT
    /// (НХЛ 2023-26: лидер по броскам в створ ~360 за сезон, топ-10 ~305, 250+ — ~18 игроков, лучший защитник ~270).</summary>
    public static double SHOOT_K = 0.04, SHOOT_TRAIT = 1.2;
    /// <summary>
    /// Реализация снайпера сверх шкалы клуба: e^(FIN_X × (завершение − среднее у бросающих на льду)) — перераспределяет голы
    /// пятёрки к лучшему завершителю, не меняя её итог (у снайперов НХЛ 15–17% реализации при 9,5% в среднем).
    /// </summary>
    public static double FIN_X = 0.035;
    /// <summary>
    /// Завершение у самого верха шкалы слабее линейного (НХЛ 2023-26: топ-3% по завершению забивают ×0,94 от линейного прогноза,
    /// игроки с Х-фактором «бросок» — ×0,98): выше FIN_TOP каждая единица считается с долей FIN_TOP_K.
    /// У защитников завершение на броске весит больше (регрессия НХЛ: +0,085…0,10 на ед. против +0,06…0,08 у нападающих).
    /// </summary>
    public static double FIN_TOP = 16, FIN_TOP_K = 0.6, FIN_X_D = 0.025;
    /// <summary>
    /// Дальний бросок (lng) — отдельный навык реализации бросков издали: регрессия НХЛ 2023-26 при 5×5 по оценкам из базы —
    /// +0,052 на ед. при равном завершении (±0,007). Погрешность оценки lng мала, поэтому та же регрессия на матчах игры
    /// возвращает почти тот же коэффициент (зеркальная проверка «Реализация по зонам»). В силу клуба и КА почти не входит —
    /// это ценность специалиста, которую не видно в общей оценке.
    /// </summary>
    public static double LNG_FAR = 0.052;
    /// <summary>Добивание после сейва: вероятность и реализация попытки (8% небл. попыток — добивания, 10,7% голов).</summary>
    public const double REB_P = 0.12, REB_ON = 0.75, REB_G = 0.107;
    /// <summary>Доля завершения (fin) в атаке клуба — часть, которая играет на броске, а не на создании.</summary>
    const double FIN_SHARE = 0.3;

    // ───────────── эффект счёта при 5×5 (проигрывающий на 1, 2, 3+): попытки — по НХЛ (53,2 / 54,8 / 55,8% попыток),
    // голы — подбор по доле овертаймов (поправка на то, что отстают чаще слабые)
    public static double[] SE_ATT_TRAIL = { 1.066, 1.10, 1.124 }, SE_ATT_LEAD = { 0.938, 0.91, 0.89 };
    public static double[] SE_G_TRAIL = { 1.12, 1.10, 1.08 }, SE_G_LEAD = { 0.94, 0.95, 0.96 };

    // ───────────── удаления: 2,83 большинства на команду за матч, 4×4 — 0,9 мин за матч
    public static double PEN_PER_60 = 2.35, COINC_PER_60 = 0.25;
    static readonly string[] PenNames = { "подножка", "задержка клюшкой", "удар клюшкой", "задержка", "зацеп", "блокировка", "грубость", "толчок на борт", "задержка игры", "атака в голову", "игра высоко поднятой клюшкой" };

    // ───────────── смены (карты смен НХЛ: нападающие 47 с, защитники 49 с)
    public const double SHIFT_F = 48, SHIFT_D = 51, SHIFT_SD = 9, PP1_SEC = 65, PK_SHIFT = 40, OT_SHIFT = 40;
    /// <summary>Расстановка по ролям (Deploy): звено выходит, если средняя энергия не ниже DEPLOY_E и переиграно своих минут не больше DEPLOY_OVER секунд.</summary>
    public static double DEPLOY_E = 0.85, DEPLOY_OVER = 90;
    /// <summary>
    /// Задания на соперника (дизайн): плотная опека — у опекаемого −SHADOW_OFF к атаке и бросков ×SHADOW_W, у нас удалений ×SHADOW_PEN;
    /// давление на защитника — у него −PRESS_DEF к обороне (и больше потерь), у нас усталость ×PRESS_DRAIN.
    /// </summary>
    public static double SHADOW_OFF = 1.5, SHADOW_W = 0.75, SHADOW_PEN = 1.05, PRESS_DEF = 0.9, PRESS_DRAIN = 1.03;
    /// <summary>
    /// Атака с хода (дизайн): после отбора или потери соперника RUSH_WIN секунд голы клуба ×RUSH_G, броски опаснее ×RUSH_Q.
    /// Вне окна интенсивность нормируется на долю времени в окне — голы за матч держит сила, а схемы меняют, откуда они:
    /// прессинг и длинный пас дают больше голов «после отбора», выход по борту — меньше.
    /// </summary>
    public static double RUSH_WIN = 10, RUSH_G = 2.0, RUSH_Q = 1.15;
    // ───────────── вратарь (НХЛ 2023-26: со снятым вратарём при −1 — 25 с за матч, при −2 — 24 с, при −3 — 8 с):
    // снятие при −1 за ~2:00 до конца, при −2 за ~2:05, при −3 (в трети случаев) за ~3:40; план тренера (Coach) — ± PULL_JIT
    public const double PULL1 = 120, PULL2 = 125, PULL3 = 220, PULL_SD = 25, PULL_JIT = 8;
    // ───────────── усталость: энергия 0–1; на льду тратится (меньше при высокой выносливости), на скамейке восстанавливается
    public static double DRAIN = 0.0058, REC = 0.0045, FAT_AT = 0.7, FAT_K = 5.0;
    // ───────────── вбрасывания (НХЛ 2024-26: 56 за матч; у центров 50,5% + 1,45 п.п. на ед. «вбрасывания»).
    // После вбрасывания 30 с попытки и голы обеих команд — как в протоколах (5×5, относительно среднего за 30 с):
    // в зоне атаки выигравший 1,84 / 1,30 (попытки / голы), проигравший 0,42 / 0,44; в средней 0,71 / 0,57 и 0,48 / 0,45;
    // в своей зоне выигравший 0,58 / 0,49, проигравший (он в зоне атаки) 0,74 / 0,72.
    /// <summary>Отдача попытки в целом: после вбрасываний в зоне атаки много бросков от синей — без поправки попыток на гол на 6–7% больше, чем в НХЛ.</summary>
    public static double ATT_Q = 1.10;
    /// <summary>Доля силы атаки (QS_A) и обороны (QS_D) пятёрки, которая меняет качество бросков, а не их число.</summary>
    public static double QS_A = 0.3, QS_D = 0.5;
    public const double FO_K = 0.058, FO_WIN = 30, FREEZE = 0.30, STOP_PER_60 = 24.5, STOP_NEUTRAL = 0.33;
    static readonly double[,] FO_MULT = { { 1.84, 1.30, 0.42, 0.44 }, { 0.71, 0.57, 0.48, 0.45 }, { 0.58, 0.49, 0.74, 0.72 } };
    // ───────────── силовые, отборы, потери за 60 минут на игрока (база НХЛ): среднее и наклон по характеристике
    // (средние приведены к числу событий за матч в протоколах: силовых 43,6, отборов 11,9, потерь 23,4 — у игроков в базе свой учёт по аренам)
    public const double HIT_F = 4.9 * 0.84, HIT_D = 3.3 * 0.84, HIT_K = 0.20, TK_F = 0.95 * 1.27, TK_D = 0.90 * 1.27, TK_K = 0.072, GV_F = 2.9 * 0.77, GV_D = 3.2 * 0.77;
    // ───────────── спецбригады: +/− к силе за ед. характеристики большинства/меньшинства у каждого в звене
    public const double PP_K = 0.015, PK_K = 0.015;

    /// <summary>Счётчики для сверки с целями НХЛ (HM27.Check); null — не считаем.</summary>
    public static Diagnostics Diag;
    public sealed class Diagnostics
    {
        public readonly Dictionary<string, double> Sec = new(), Goals = new(), Att = new(), Blk = new(), Miss = new(), Sog = new();
        public int Games, PpEntries, Rebounds, RebGoals;
        /// <summary>Вбрасывания по ролям звена на льду (0 обычная, 1 атакующая, 2 оборонительная) и зонам (0 атаки, 1 средняя, 2 своя).</summary>
        public readonly int[,] RoleStarts = new int[3, 3];
        /// <summary>Секунды оборонительной тройки против лучшей тройки соперника и всего лучшей тройки соперника на льду (5×5).</summary>
        public int ShutVsTop, TopSec;
        public readonly Dictionary<int, double> ScoreSecAtt = new();   // разница → попытки за
        public readonly Dictionary<int, double> ScoreSec = new();
        public readonly Dictionary<int, double> PullSec = new(), PullGf = new(), PullGa = new();
        public readonly Dictionary<int, double> OnGf = new();
        public readonly Dictionary<int, double> Es5Sec = new(), Es5Gf = new(), Es5Ga = new();
        public readonly Dictionary<string, double> RoleShare = new();   // «g_D», «a1_F»…
        /// <summary>Подпись игрока (как data/trait_targets.py): att, blkd, unb, close, far, near40, sog, tip, slap, back, wrap, reb, a1.</summary>
        public readonly Dictionary<int, Dictionary<string, double>> Sig = new();
        public void S(int id, string k, double v = 1) { if (!Sig.TryGetValue(id, out var d)) Sig[id] = d = new(); d[k] = d.GetValueOrDefault(k) + v; }
        public void Add(Dictionary<string, double> d, string k, double v = 1) => d[k] = d.GetValueOrDefault(k) + v;
    }

    internal sealed class Sk
    {
        public int Id; public PlayerData P; public bool D;
        public double PosOff, PosDef;   // потери на своём месте в равных составах (Lineup.SlotPenalty)
        public double Off, Def, FinPart, Phi, Fin, FinE, Lng, W, Pass, A2W, PenW, Drain, Pp, Pk, BlkW, OtVal, So, Fo, HitR, TkR, GvR;
        /// <summary>Без системы игры и заданий на матч (их накладывает Apply): вес броска, удаления, усталость, атака, оборона, зоны бросков (логиты).</summary>
        public double W0, PenW0, Drain0, Off0, Def0, GvR0, Pass0, HitR0, TkR0;
        public readonly double[] Zone = new double[3], Zl = new double[3], Zc = new double[3];
        /// <summary>Против него играют жёстко (задание соперника): силовые чаще на нём.</summary>
        public bool HitTarget;
        public bool Thru, Draw, Tip, Back, Wrap, Slap, NetT, BlkT;
        public double E = 1; public int T0; public bool On;
        public PlayerBox Box;
        public double Energy(int t) => On ? Math.Max(0.05, E - Drain * (t - T0)) : Math.Min(1, E + REC * (t - T0));
        public void Set(int t, bool on)
        {
            if (on == On) return;
            E = Energy(t);
            if (On) Box.Toi += t - T0; else Box.Shifts++;
            T0 = t; On = on;
        }
    }

    /// <summary>
    /// Средние по лиге суммы вкладов пятёрки для каждого вида звена (равные составы, большинство, меньшинство, ОТ 3×3) —
    /// база состава (цели НХЛ) относится к среднему звену своего вида: на большинство выходят лучшие в атаке, и без этой
    /// поправки их класс учитывался бы дважды. Считается один раз по базе (составы по умолчанию, оценки из данных).
    /// </summary>
    sealed record Refs(Dictionary<string, double> Off, Dictionary<string, double> Def, double PpAttr, double PkAttr, double LngF, double LngD);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameData, Refs> RefCache = new();
    /// <summary>
    /// Поправка интенсивности по составам (подбор по целям НХЛ: HM27.Check engine). Подобрано так, что при уровне лиги
    /// из базы, равном НХЛ 2022-26 (NHL_GPG), движок даёт цели engine_targets.json; при другом уровне все составы масштабируются.
    /// </summary>
    public static readonly Dictionary<string, double> K_STATE = new() { ["5v5"] = 1.077, ["pp"] = 1.011, ["pk"] = 1.30, ["4v4"] = 1.11, ["3v3"] = 1.084, ["en_against"] = 0.90, ["en_for"] = 1.17 };

    static Refs ComputeRefs(GameData d)
    {
        var off = new Dictionary<string, List<double>>(); var def = new Dictionary<string, List<double>>();
        var ppA = new List<double>(); var pkA = new List<double>(); var lngF = new List<double>(); var lngD = new List<double>();
        void Add(Dictionary<string, List<double>> m, string k, double v) { if (!m.TryGetValue(k, out var l)) m[k] = l = new(); l.Add(v); }
        foreach (var team in d.Teams)
        {
            var l = Lineup.FromTeam(d, team, new HashSet<int>(team.Injured));
            var side = new MatchSide { Team = team, Lineup = l, Goalie = l.G1 };
            var sim = new Sim(d, side, side, new Random(1), false, new MatchResult(), refs: false);
            var t = sim.Home;
            double O(IEnumerable<Sk> u) => u.Sum(x => x.Off - x.FinPart);
            double Dd(IEnumerable<Sk> u) => u.Sum(x => x.Def);
            // равные составы: среднее по времени (доли звеньев и пар)
            Add(off, "es", Enumerable.Range(0, t.F.Count).Sum(i => t.FT[i] * O(t.F[i])) + Enumerable.Range(0, t.D.Count).Sum(i => t.DT[i] * O(t.D[i])));
            Add(def, "es", Enumerable.Range(0, t.F.Count).Sum(i => t.FT[i] * Dd(t.F[i])) + Enumerable.Range(0, t.D.Count).Sum(i => t.DT[i] * Dd(t.D[i])));
            double W(Sk[] a, Sk[] b, double wa, Func<Sk[], double> f) => b.Length == 0 ? f(a) : wa * f(a) + (1 - wa) * f(b);
            Add(off, "pp", W(t.Pp1, t.Pp2, 0.62, u => O(u) * 5.0 / Math.Max(1, u.Length)));
            Add(def, "pp", W(t.Pp1, t.Pp2, 0.62, u => Dd(u) * 5.0 / Math.Max(1, u.Length)));
            Add(off, "pk", W(t.Pk1, t.Pk2, 0.5, u => O(u) * 5.0 / Math.Max(1, u.Length)));
            Add(def, "pk", W(t.Pk1, t.Pk2, 0.5, u => Dd(u) * 5.0 / Math.Max(1, u.Length)));
            if (t.Ot.Count > 0)
            {
                Add(off, "ot", t.Ot.Average(u => O(u) * 5.0 / u.Length));
                Add(def, "ot", t.Ot.Average(u => Dd(u) * 5.0 / u.Length));
            }
            ppA.Add(W(t.Pp1, t.Pp2, 0.62, u => u.Sum(x => x.Pp - 11) * 5.0 / Math.Max(1, u.Length)));
            foreach (var x in t.All.Values) (x.D ? lngD : lngF).Add(x.Lng);
            pkA.Add(W(t.Pk1, t.Pk2, 0.5, u => u.Sum(x => x.Pk - 11) * 5.0 / Math.Max(1, u.Length)));
        }
        return new Refs(off.ToDictionary(kv => kv.Key, kv => kv.Value.Average()), def.ToDictionary(kv => kv.Key, kv => kv.Value.Average()), ppA.Average(), pkA.Average(), lngF.Average(), lngD.Average());
    }

    internal sealed class Pen { public int End; public int Pid; public bool Minor, Double, Coinc; }

    internal sealed class Team
    {
        public MatchSide S; public string Abbr; public bool Home;
        public readonly List<Sk[]> F = new(), D = new();
        public double[] FT, DT, FToi, DToi;
        public Sk[] Pp1, Pp2, Pk1, Pk2;
        public List<Sk[]> Ot = new();
        public readonly Dictionary<int, Sk> All = new();
        public int Goalie; public double GMult; public PlayerBox GBox;
        public double GSkill;            // для буллитов
        public int Fi = -1, Di = -1; public double FLeft, DLeft;
        /// <summary>Роли троек и пар (как в t.F / t.D), против лучших; лучшая тройка по атаке.</summary>
        public int[] FRole = Array.Empty<int>(), DRole = Array.Empty<int>(); public bool Matchup; public int FTop = -1;
        public int UnitIdx; public double UnitLeft; public string Mode = "";
        public readonly List<Pen> Pens = new();
        public bool Pulled; public double Pull1, Pull2, Pull3;
        public List<Sk> On = new();
        public int Goals, Sog;
        public double EsOff;             // средняя по времени атака пятёрки в равных составах (по звеньям тренера)
        public double Fam = FAM_REF;     // сыгранность пятёрки на льду (см. FAM_MAX)
        public int OnSince, GSince;
        public double Mult;              // хозяева/гости, усталость от второго матча подряд, темп, плей-офф
        // на текущий момент
        public double Rate, PenRate, Q, Lc, FinBar, HitRate, TkRate, GvRate;
        public int FoUntil = -1; public double FoAtt = 1, FoG = 1;
        /// <summary>Атака с хода после отбора или потери соперника: до этой секунды; сила — по системе (длинный пас — больше).</summary>
        public int RushUntil = -1; public double RushK = 1, Ice = 1;
        /// <summary>Множители зон бросков в нынешнем составе (у ворот, средняя, издали) и доли бросков защитников — спецбригады.</summary>
        public double Zf0 = 1, Zf2 = 1, DwNow = 1, BlkNow = 1;
        /// <summary>Тайм-аут взят; видеопросмотр — по голам (секунда гола).</summary>
        public bool TimeoutUsed;
        /// <summary>Счётчики для журнала тренера (MatchResult.Stats): то, что меняют схемы.</summary>
        public readonly Dictionary<string, double> St = new();
        public void Add(string k, double v = 1) => St[k] = St.GetValueOrDefault(k) + v;
        /// <summary>Сумма интенсивности голов по секундам — ожидаемые голы по ходу матча без шума исходов (для проверок).</summary>
        public double LamSum, PenSum;
        public double[] Pi = Array.Empty<double>();
        public int N;                    // полевых по правилам (без снятого вратаря)
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    public static MatchResult Play(GameData data, MatchSide home, MatchSide away, Random rng, bool playoff = false, Action<Sim> onBreak = null)
    {
        var sim = Start(data, home, away, rng, playoff);
        sim.OnBreak = onBreak;
        sim.Run();
        return sim.Result;
    }

    /// <summary>Матч по шагам (трансляция с паузами): RunTo, Apply после решений тренера, OnBreak — перерывы.</summary>
    public static Sim Start(GameData data, MatchSide home, MatchSide away, Random rng, bool playoff = false)
    {
        var o = MatchEngine.Odds(data, home, away, playoff, sharp: SHARP);
        var r = new MatchResult { PHome = o.PHome, LamHome = o.LamHome, LamAway = o.LamAway, HomeGoalie = home.Goalie, AwayGoalie = away.Goalie };
        r.HomeDressed = home.Lineup.Skaters.Append(home.Goalie).Where(x => x != 0).Distinct().ToList();
        r.AwayDressed = away.Lineup.Skaters.Append(away.Goalie).Where(x => x != 0).Distinct().ToList();
        if (Diag != null) Diag.Games++;
        return new Sim(data, home, away, rng, playoff, r);
    }

    public sealed class Sim
    {
        readonly GameData data; readonly Random rng; readonly bool playoff; readonly MatchResult r;
        readonly Team H, A; readonly TeamStrength.Scale sc; readonly double spread, norm; readonly Refs refs;
        internal Team Home => H;
        int T, Per; bool ot3, done;
        /// <summary>Вбрасывание на следующей секунде: 1 — в центре, 2 — в зоне атаки хозяев, 3 — гостей.</summary>
        int foPending; int lastRate = -100; bool dirty = true, recompose = true;

        public Sim(GameData d, MatchSide home, MatchSide away, Random rng, bool playoff, MatchResult r, bool refs = true)
        {
            data = d; this.rng = rng; this.playoff = playoff; this.r = r;
            this.refs = refs ? RefCache.GetValue(d, ComputeRefs) : null;
            sc = TeamStrength.Coef(d); spread = TeamStrength.SPREAD;
            // средний клуб — ноль атаки и обороны; уровень лиги из базы относительно НХЛ 2022-26
            norm = Math.Exp(-spread * (sc.MA + sc.MY)) * d.League.GoalsPerGame / NHL_GPG;
            H = Build(home, true); A = Build(away, false);
            Apply();
        }

        /// <summary>
        /// Перечитать настройки сторон (темп, система игры, задания на матч, вратарь): при старте и после решений тренера
        /// в перерыве или на паузе. Случайных чисел не тратит — без изменений матч идёт так же.
        /// </summary>
        public void Apply()
        {
            double sig0 = Sig();
            double tempo = (1 + MatchEngine.TEMPO_K * H.S.Tempo) * (1 + MatchEngine.TEMPO_K * A.S.Tempo);
            foreach (var (t, o) in new[] { (H, A), (A, H) })
            {
                // системы игры (Tactics): на весь матч — без спецбригад и удалений (их движок разыгрывает сам); настрой на матч (Spark)
                t.Mult = Math.Exp(playoff ? 0 : t.Home ? HA_LOG : -HA_LOG) * tempo * (playoff ? MatchEngine.PO_TOT : 1)
                    * Math.Exp((t.S.Sys?.EngF ?? 0) + (o.S.Sys?.EngA ?? 0) + t.S.Spark);
                if (t.S.B2B && !o.S.B2B) t.Mult *= MatchEngine.B2B;
                if (o.S.B2B && !t.S.B2B) t.Mult /= MatchEngine.B2B;
                var sys = t.S.Sys; var osys = o.S.Sys;
                t.Ice = sys?.Ice ?? 1; t.RushK = sys?.Rush ?? 1;
                var orders = t.S.Lineup.Orders;
                foreach (var sk in t.All.Values)
                {
                    sk.W = sk.W0 * (sk.D ? sys?.DW ?? 1 : 1);
                    sk.PenW = sk.PenW0 * (sys?.Pen ?? 1);
                    sk.Drain = sk.Drain0 * (sys?.Drain ?? 1);
                    sk.Off = sk.Off0; sk.Def = sk.Def0; sk.Pass = sk.Pass0; sk.HitTarget = false;
                    sk.HitR = sk.HitR0 * (sys?.Hit ?? 1); sk.TkR = sk.TkR0 * (sys?.Tk ?? 1);
                    sk.GvR = sk.GvR0 * (sys?.Gv ?? 1) * (osys?.OppGv ?? 1);
                    // откуда бросает: свой стиль бросков и то, как обороняется соперник (зонная — дальше от ворот)
                    sk.Zc[0] = sk.Zl[0] + Math.Log(osys?.CloseA ?? 1);
                    sk.Zc[1] = sk.Zl[1];
                    sk.Zc[2] = sk.Zl[2] + Math.Log(sys?.Far ?? 1) + Math.Log(osys?.FarA ?? 1);
                    // указание игроку (Orders): размен с пригодностью
                    if (orders != null && orders.TryGetValue(sk.Id, out var ok) && Orders.Get(ok) is { } od && Orders.Allowed(od, sk.P))
                    {
                        var side = t.S;
                        double f = Math.Clamp(Orders.Fit((p, k) => Attr(side, p, k), sk.P, od), -1.5, 1.5);
                        switch (ok)
                        {
                            case "shoot": sk.W *= 1.2; sk.Zc[2] += Math.Log(1.15); break;
                            case "pass": sk.W *= 0.8; sk.Pass *= 1.25 + 0.25 * f; sk.Off += 0.08 * f; break;
                            case "net": sk.Zc[0] += 0.5; sk.Drain *= 1.05; break;
                            case "hit": sk.HitR *= 1.6; sk.PenW *= 1.25 - 0.2 * f; sk.Def += 0.05 + 0.1 * f; break;
                            case "back": sk.Off -= 0.2; sk.Def += 0.12 + 0.12 * f; break;
                            case "join": sk.Off += 0.12 + 0.12 * f; sk.Def -= 0.2 - 0.06 * f; sk.W *= 1.15; break;
                            case "stay": sk.Off -= 0.15; sk.Def += 0.08 + 0.1 * f; sk.W *= 0.85; break;
                        }
                    }
                }
            }
            // задания на соперника (до трёх): у каждого своя цена — удаления или усталость; внимание делится — с каждым новым
            // заданием каждое работает слабее (1, 0,71, 0,58 от полного), а цена остаётся
            foreach (var (t, o) in new[] { (H, A), (A, H) })
            {
                var tg = t.S.Targets;
                if (tg == null || tg.Count == 0) continue;
                int n = tg.Keys.Count(id => o.All.ContainsKey(id));
                if (n == 0) continue;
                double k = 1 / Math.Sqrt(n);
                foreach (var (id, kind) in tg)
                {
                    if (!o.All.TryGetValue(id, out var x)) continue;
                    switch (kind)
                    {
                        case "shadow":   // плотная опека: его атака ниже, бросает реже; у нас больше удалений
                            x.Off -= k * SHADOW_OFF; x.W *= 1 - k * (1 - SHADOW_W);
                            foreach (var sk in t.All.Values) sk.PenW *= SHADOW_PEN;
                            break;
                        case "press":    // давить: он ошибается с шайбой и хуже обороняется; наши устают
                            x.Def -= k * PRESS_DEF; x.GvR *= 1 + 0.5 * k;
                            foreach (var sk in t.All.Values) sk.Drain *= PRESS_DRAIN;
                            break;
                        case "hit":      // играть жёстко против него: устаёт и торопится; у нас больше удалений
                            x.Drain *= 1 + 0.15 * k; x.Off -= 0.5 * k; x.GvR *= 1 + 0.2 * k; x.HitTarget = true;
                            foreach (var sk in t.All.Values) sk.PenW *= 1.04;
                            break;
                        case "lane":     // не давать бросать: бросает реже и дальше, зато больше отдаёт партнёрам
                            x.W *= 1 - 0.35 * k; x.Zc[0] -= 0.35 * k; x.Pass *= 1 + 0.25 * k;
                            foreach (var sk in t.All.Values) sk.Drain *= 1.01;
                            break;
                    }
                }
                if (n > 1) foreach (var sk in t.All.Values) sk.Drain *= 1 + 0.01 * (n - 1);
            }
            foreach (var t in new[] { H, A })
                foreach (var sk in t.All.Values)
                {
                    double m = Math.Max(sk.Zc[0], Math.Max(sk.Zc[1], sk.Zc[2])), e0 = Math.Exp(sk.Zc[0] - m), e1 = Math.Exp(sk.Zc[1] - m), e2 = Math.Exp(sk.Zc[2] - m), es = e0 + e1 + e2;
                    sk.Zone[0] = e0 / es; sk.Zone[1] = e1 / es; sk.Zone[2] = e2 / es;
                }
            // смена вратаря (в перерыве или на паузе)
            foreach (var t in new[] { H, A })
                if (t.S.Goalie != t.Goalie && t.S.Goalie != 0) SwapGoalie(t, t.S.Goalie);
            // ничего не изменилось — интенсивности не пересчитываем (иначе сдвигается их шаг, и матч идёт чуть иначе)
            if (Sig() != sig0) dirty = true;
        }

        /// <summary>Отпечаток настроек сторон: всё, что Apply меняет и что входит в интенсивности.</summary>
        double Sig()
        {
            double x = 0; int k = 1;
            foreach (var t in new[] { H, A })
            {
                if (t == null) return double.NaN;
                x += (k++) * (t.Mult + t.Ice * 3 + t.RushK * 7 + t.Goalie);
                foreach (var sk in t.All.Values)
                    x += (k++ % 97 + 1) * (sk.W + 2 * sk.PenW + 3 * sk.Drain + 5 * sk.Off + 7 * sk.Def + 11 * sk.GvR + 13 * sk.Pass + 17 * sk.HitR + 19 * sk.TkR + 23 * sk.Zone[0] + 29 * sk.Zone[2] + (sk.HitTarget ? 31 : 0));
            }
            return x;
        }

        double Attr(MatchSide s, PlayerData p, string k) => s.Attr?.Invoke(p, k) ?? TeamStrength.Est(p, k);

        Team Build(MatchSide s, bool home)
        {
            var t = new Team { S = s, Abbr = s.Team.Abbr, Home = home, Goalie = s.Goalie };
            var l = s.Lineup;
            Sk Get(int id)
            {
                if (id == 0 || !data.PlayerById.TryGetValue(id, out var p)) return null;
                if (t.All.TryGetValue(id, out var have)) return have;
                double a(string k) => Attr(s, p, k);
                var tr = p.Tr?.ToHashSet() ?? new HashSet<string>();
                double off = TeamStrength.Off.Sum(x => x.w * (a(x.k) - 11)), def = TeamStrength.Def.Sum(x => x.w * (a(x.k) - 11));
                double dOff = TeamStrength.Damp(off), dr = Math.Abs(off) > 1e-6 ? dOff / off : 1;
                bool isD = p.IsDefense;
                var sk = new Sk
                {
                    Id = id, P = p, D = isD, Off = dOff, Def = TeamStrength.Damp(def), Fin = a("fin"),
                    FinPart = FIN_SHARE * (a("fin") - 11) * dr, Phi = 5 * spread * sc.A1 * FIN_SHARE * dr,
                    Pp = a("pp"), Pk = a("pk"), BlkW = Math.Exp(0.06 * (a("blk") - 11)),
                    Drain0 = DRAIN * (1 + 0.05 * (11 - a("sta"))),
                    Thru = tr.Contains("thru"), BlkT = tr.Contains("blk"), Draw = tr.Contains("draw"), Tip = tr.Contains("tip"), Back = tr.Contains("back"),
                    Wrap = tr.Contains("wrap"), Slap = tr.Contains("slap"), NetT = tr.Contains("net") || tr.Contains("reb") || tr.Contains("tip"),
                    Box = new PlayerBox(),
                };
                double shootSkill = (a("fin") + a("pwr") + a("lng")) / 3 - 11;
                sk.W0 = (isD ? D_SHOT_W : 1) * Math.Exp(SHOOT_K * shootSkill) * (tr.Contains("shoot") ? (isD ? SHOOT_TRAIT + 0.15 : SHOOT_TRAIT) : 1) * (tr.Contains("pass") ? 0.8 : 1) * (tr.Contains("jump") ? 1.2 : 1);
                double pv = (a("pas") + a("vis")) / 2 - 11;
                sk.Pass = isD ? 0.45 * Math.Exp(0.11 * pv) : Math.Exp(0.22 * pv) * (tr.Contains("pass") ? 1.4 : 1);
                sk.A2W = (isD ? 0.6 : 1) * Math.Exp(0.15 * pv);
                sk.PenW0 = Math.Exp(-0.2 * (a("dis") - 11)) * (isD ? 1.1 : 1) * (tr.Contains("hit") ? 1.2 : 1) * (tr.Contains("fight") ? 1.3 : 1);
                // откуда бросает: «у ворот» — пятак, подправления, добивания; «издали» — дальний бросок
                var z = ZONE[isD ? 1 : 0];
                double c0 = Math.Log(z[0]) + 0.12 * (a("net") - 11) + (sk.NetT ? 0.45 : 0) + (tr.Contains("jump") ? 0.5 : 0);
                double c1 = Math.Log(z[1]) + (tr.Contains("jump") ? 0.3 : 0);
                double c2 = Math.Log(z[2]) + 0.12 * (a("lng") - 11) + (tr.Contains("far") ? 0.5 : 0);
                sk.Zl[0] = c0; sk.Zl[1] = c1; sk.Zl[2] = c2;
                sk.OtVal = dOff + 0.4 * sk.Def + 0.1 * (a("spd") - 11);
                sk.So = (a("fin") + a("hnd")) / 2;
                sk.FinE = sk.Fin <= FIN_TOP ? sk.Fin : FIN_TOP + FIN_TOP_K * (sk.Fin - FIN_TOP);
                sk.Lng = a("lng");
                sk.Fo = a("fo");
                sk.HitR = (isD ? HIT_D : HIT_F) * Math.Exp(HIT_K * (a("hit") - 11)) / 3600;
                sk.TkR = (isD ? TK_D : TK_F) * Math.Exp(TK_K * (a("tka") - 11)) / 3600;
                sk.GvR = (isD ? GV_D : GV_F) * Math.Exp(0.02 * (a("hnd") - 11)) * (tr.Contains("risk") ? (isD ? 1.17 : 1.27) : 1) / 3600;
                sk.Off0 = sk.Off; sk.Def0 = sk.Def; sk.GvR0 = sk.GvR; sk.Pass0 = sk.Pass; sk.HitR0 = sk.HitR; sk.TkR0 = sk.TkR;
                t.All[id] = sk;
                return sk;
            }
            var fr = new List<int>(); var dr = new List<int>();
            for (int i = 0; i < l.F.Count; i++) { var u = l.F[i].Select(Get).Where(x => x != null).ToArray(); if (u.Length > 0) { t.F.Add(u); fr.Add(l.RoleF(i)); } }
            for (int i = 0; i < l.D.Count; i++) { var u = l.D[i].Select(Get).Where(x => x != null).ToArray(); if (u.Length > 0) { t.D.Add(u); dr.Add(l.RoleD(i)); } }
            t.FRole = fr.ToArray(); t.DRole = dr.ToArray(); t.Matchup = l.Matchup;
            t.FTop = t.F.Count == 0 ? -1 : Enumerable.Range(0, t.F.Count).OrderByDescending(i => t.F[i].Average(x => x.Off)).First();
            foreach (var sk0 in t.All.Values.ToList())
                if (l.SlotOf(sk0.Id) is char slot) (sk0.PosOff, sk0.PosDef) = Lineup.SlotPenalty(sk0.P, slot);
            var fMin = l.F.Select((x, i) => i < l.FMin.Length ? l.FMin[i] : 5).ToList();
            var dMin = l.D.Select((x, i) => i < l.DMin.Length ? l.DMin[i] : 5).ToList();
            t.FT = Enumerable.Range(0, t.F.Count).Select(i => fMin[Math.Min(i, fMin.Count - 1)]).ToArray();
            t.DT = Enumerable.Range(0, t.D.Count).Select(i => dMin[Math.Min(i, dMin.Count - 1)]).ToArray();
            double fs = Math.Max(1e-6, t.FT.Sum()), ds = Math.Max(1e-6, t.DT.Sum());
            for (int i = 0; i < t.FT.Length; i++) t.FT[i] /= fs;
            for (int i = 0; i < t.DT.Length; i++) t.DT[i] /= ds;
            t.FToi = new double[t.F.Count]; t.DToi = new double[t.D.Count];
            t.EsOff = Enumerable.Range(0, t.F.Count).Sum(i => t.FT[i] * t.F[i].Sum(x => x.Off - x.FinPart) * 3.0 / Math.Max(1, t.F[i].Length))
                    + Enumerable.Range(0, t.D.Count).Sum(i => t.DT[i] * t.D[i].Sum(x => x.Off - x.FinPart) * 2.0 / Math.Max(1, t.D[i].Length));
            Sk[] Unit(int[] ids) => (ids ?? Array.Empty<int>()).Select(Get).Where(x => x != null).ToArray();
            t.Pp1 = Unit(l.Pp1); t.Pp2 = Unit(l.Pp2); t.Pk1 = Unit(l.Pk1); t.Pk2 = Unit(l.Pk2);
            // овертайм 3×3: лучшие 6 нападающих парами + 3 защитника
            var fs6 = t.All.Values.Where(x => !x.D).OrderByDescending(x => x.OtVal).Take(6).ToList();
            var ds3 = t.All.Values.Where(x => x.D).OrderByDescending(x => x.OtVal).Take(3).ToList();
            for (int i = 0; i < 3; i++)
            {
                var u = fs6.Skip(2 * i).Take(2).ToList();
                if (i < ds3.Count) u.Add(ds3[i]);
                if (u.Count == 3) t.Ot.Add(u.ToArray());
            }
            // вратарь: как в модели — голы против × (1 − G_W·GSAx) / (1 − G_W·средний GSAx). Шкала клубов (D2) этот эффект
            // занижает вдвое (МНК по клубам «размывает» вратаря), а на броске он должен быть полным — иначе % отражённых
            // почти не зависит от вратаря (зеркальная проверка: чётные/нечётные матчи)
            double gs = MatchEngine.GsaxOf(data, s.Goalie, s.Dl);
            t.GMult = Math.Clamp((1 - MatchEngine.G_W * gs) / (1 - MatchEngine.G_W * sc.G0), 0.8, 1.2);
            t.GBox = new PlayerBox();
            if (data.PlayerById.TryGetValue(s.Goalie, out var gp)) t.GSkill = (Attr(s, gp, "rfx") + Attr(s, gp, "gpos")) / 2 - 11;
            // время снятия: как обычно в НХЛ (разброс по тренерам и ситуациям) или план тренера на матч (± реакция на свисток)
            double n1 = Normal(), n2 = Normal();
            t.Pull1 = s.Pull1 is double p1 ? p1 + PULL_JIT * n1 : PULL1 + PULL_SD * n1;
            t.Pull2 = s.Pull2 is double p2 ? p2 + PULL_JIT * n2 : PULL2 + PULL_SD * n2;
            // при −3 снимают не всегда (примерно в трети таких концовок), зато рано
            t.Pull3 = rng.NextDouble() < 0.35 ? PULL3 + PULL_SD * Normal() : 0;
            return t;
        }

        double Normal()
        {
            double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        Team Opp(Team t) => t == H ? A : H;

        // ─────────────────────────────── ход матча

        // ─────────────────────────────── ход матча: по секундам, с паузами и перерывами (Live, Coach)

        int perStart, perLen, sec; bool perOt, inPer, finished;
        public bool Finished => finished;
        /// <summary>Секунда матча (с начала первого периода), номер периода (4+ — овертаймы).</summary>
        public int Clock => T;
        public int Period => Per;
        public bool InPeriod => inPer;
        public MatchResult Result => r;
        public MatchSide HomeSide => H.S;
        public MatchSide AwaySide => A.S;
        public int HomeGoals => H.Goals;
        public int AwayGoals => A.Goals;
        public int HomeShots => H.Sog;
        public int AwayShots => A.Sog;
        public bool Playoff => playoff;
        /// <summary>Пропущено вратарём на льду (без пустых ворот) и броски по нему: хозяева / гости.</summary>
        public (int ga, int sa) GoalieLine(bool home) { var t = home ? H : A; return (t.GBox.Ga, t.GBox.Sa); }
        public int GoalieOn(bool home) => (home ? H : A).Goalie;
        /// <summary>Вратарь снят (шестой полевой), протокол вратаря на льду и стоит ли полевой в звене защиты.</summary>
        public bool IsPulled(bool home) => (home ? H : A).Pulled;
        public PlayerBox GoalieBox(bool home) => (home ? H : A).GBox;
        public bool IsD(bool home, int id) => (home ? H : A).All.TryGetValue(id, out var sk) && sk.D;
        /// <summary>Кто на льду сейчас (полевые) и их энергия 0–1.</summary>
        public IReadOnlyList<int> OnIce(bool home) => (home ? H : A).On.Select(x => x.Id).ToList();
        public double Energy(bool home, int id) => (home ? H : A).All.TryGetValue(id, out var sk) ? sk.Energy(T) : 1;
        /// <summary>Протокол игрока по ходу матча (полевые).</summary>
        public PlayerBox BoxOf(bool home, int id) => (home ? H : A).All.TryGetValue(id, out var sk) ? sk.Box : null;

        /// <summary>
        /// Поменять местами двух полевых в звеньях равных составов по ходу матча (тот же тип: нападающие или защитники).
        /// Состав стороны (MatchSide.Lineup) меняется тоже — в трансляции это копия, клуб не трогаем.
        /// </summary>
        public bool SwapLines(bool home, int a, int b)
        {
            var t = home ? H : A;
            if (!t.All.TryGetValue(a, out var sa) || !t.All.TryGetValue(b, out var sb) || sa.D != sb.D) return false;
            var units = sa.D ? t.D : t.F;
            foreach (var u in units) for (int i = 0; i < u.Length; i++) u[i] = u[i] == sa ? sb : u[i] == sb ? sa : u[i];
            t.S.Lineup.SwapEven(a, b);
            foreach (var x in new[] { sa, sb }) if (t.S.Lineup.SlotOf(x.Id) is char slot) (x.PosOff, x.PosDef) = Lineup.SlotPenalty(x.P, slot);
            t.FTop = t.F.Count == 0 ? -1 : Enumerable.Range(0, t.F.Count).OrderByDescending(i => t.F[i].Average(x => x.Off)).First();
            recompose = dirty = true;
            return true;
        }

        /// <summary>Кто был на льду при голе (секунда × 2 + сторона): для видеопросмотра.</summary>
        readonly Dictionary<int, (int[] t, int[] o, int scorer, int goalie)> GoalOn = new();

        /// <summary>
        /// Минуты звеньев по ходу матча (тренер сокращает скамейку или раскатывает четвёртую тройку): доли времени в равных составах.
        /// Состав стороны меняется тоже — в трансляции это копия.
        /// </summary>
        public void SetMinutes(bool home, double[] fMin, double[] dMin)
        {
            var t = home ? H : A;
            var l = t.S.Lineup;
            if (fMin != null) l.FMin = (double[])fMin.Clone();
            if (dMin != null) l.DMin = (double[])dMin.Clone();
            double fs = 0, ds = 0;
            for (int i = 0; i < t.F.Count; i++) { t.FT[i] = l.FMin[Math.Min(i, l.FMin.Length - 1)]; fs += t.FT[i]; }
            for (int i = 0; i < t.D.Count; i++) { t.DT[i] = l.DMin[Math.Min(i, l.DMin.Length - 1)]; ds += t.DT[i]; }
            for (int i = 0; i < t.FT.Length; i++) t.FT[i] /= Math.Max(1e-6, fs);
            for (int i = 0; i < t.DT.Length; i++) t.DT[i] /= Math.Max(1e-6, ds);
            // сыгранные минуты пересчитываются в новых долях с этого момента
            double ft = t.FToi.Sum(), dt = t.DToi.Sum();
            for (int i = 0; i < t.FToi.Length; i++) t.FToi[i] = t.FT[i] * ft;
            for (int i = 0; i < t.DToi.Length; i++) t.DToi[i] = t.DT[i] * dt;
            t.EsOff = Enumerable.Range(0, t.F.Count).Sum(i => t.FT[i] * t.F[i].Sum(x => x.Off0 - x.FinPart) * 3.0 / Math.Max(1, t.F[i].Length))
                    + Enumerable.Range(0, t.D.Count).Sum(i => t.DT[i] * t.D[i].Sum(x => x.Off0 - x.FinPart) * 2.0 / Math.Max(1, t.D[i].Length));
            dirty = true;
        }

        /// <summary>Тайм-аут (один за матч): все полевые отдыхают — свежесть +TIMEOUT_E. Случайных чисел не тратит.</summary>
        public const double TIMEOUT_E = 0.25;
        public bool TimeoutUsed(bool home) => (home ? H : A).TimeoutUsed;
        public bool Timeout(bool home)
        {
            var t = home ? H : A;
            if (t.TimeoutUsed || finished) return false;
            t.TimeoutUsed = true;
            foreach (var sk in t.All.Values)
            {
                double e = sk.Energy(T);
                if (sk.On) sk.Box.Toi += T - sk.T0;
                sk.E = Math.Min(1, e + TIMEOUT_E); sk.T0 = T;
            }
            r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "coach", Note = "тайм-аут" });
            dirty = true;
            return true;
        }

        /// <summary>Последний гол соперника, который ещё можно оспорить (не больше 30 секунд назад, не в пустые ворота): секунда гола или −1.</summary>
        public int ChallengeFor(bool home)
        {
            var o = home ? A : H;
            var ev = r.Events.LastOrDefault(e => e.Kind is "goal" or "nogoal");
            if (ev == null || ev.Kind != "goal" || ev.Team != o.Abbr || ev.Note is "en" or "so" || T - ev.T > 30 || ev.Rev != 0) return -1;
            return ev.T;
        }

        /// <summary>Есть ли за голом нарушение (офсайд, помеха вратарю): решено детерминированно от матча и гола — около 9% голов.</summary>
        public bool Reviewable(int goalT, int scorer) => Hash(goalT, scorer, 1) % 100 < 9;

        int Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(r.LamHome * 1e6) * 2654435761u ^ (uint)(r.LamAway * 1e6) * 40503u ^ (uint)a * 97u ^ (uint)b * 31u ^ (uint)c * 7919u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (int)(h % 100000);
            }
        }

        /// <summary>
        /// Видеопросмотр гола соперника: если было нарушение — гол отменяют (почти всегда), если не было — редко; неудачный
        /// просмотр — малый штраф за задержку игры. Случайных чисел движка не тратит. Возвращает: отменён ли гол.
        /// </summary>
        public bool Challenge(bool home)
        {
            int gt = ChallengeFor(home);
            if (gt < 0) return false;
            var t = home ? H : A; var o = Opp(t);
            var ev = r.Events.Last(e => e.Kind == "goal" && e.T == gt && e.Team == o.Abbr);
            bool review = Reviewable(gt, ev.P1);
            bool overturned = Hash(gt, ev.P1, 2) % 100 < (review ? 85 : 4);
            if (overturned)
            {
                ev.Rev = 2; ev.Kind = "nogoal";
                o.Goals--; o.Sog = Math.Max(0, o.Sog - 1);
                if (ev.R == 1) o.Add("rush_g", -1);
                if (ev.Note == "pp") o.Add("pp_g", -1); else if (ev.Note == "sh") o.Add("sh_g", -1);
                var gl = r.Goals.LastOrDefault(g => g.Team == o.Abbr && (g.Period - 1) * 1200 + g.Sec == gt);
                if (gl != null) r.Goals.Remove(gl);
                if (GoalOn.TryGetValue(gt * 2 + (o == H ? 0 : 1), out var on))
                {
                    if (ev.Note != "pp") { foreach (var id in on.o) if (o.All.TryGetValue(id, out var x)) x.Box.Pm--; foreach (var id in on.t) if (t.All.TryGetValue(id, out var x)) x.Box.Pm++; }
                    if (o.All.TryGetValue(on.scorer, out var sc)) sc.Box.S = Math.Max(0, sc.Box.S - 1);
                    if (on.goalie == t.Goalie) { t.GBox.Ga = Math.Max(0, t.GBox.Ga - 1); t.GBox.Sa = Math.Max(0, t.GBox.Sa - 1); }
                }
                r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "coach", Note = "видеопросмотр: гол отменён" });
            }
            else
            {
                ev.Rev = 1;
                // задержка игры: отбывает нападающий со скамейки (детерминированно)
                var who = t.All.Values.Where(x => !x.D && !x.On).OrderBy(x => x.Id).FirstOrDefault() ?? t.All.Values.First();
                t.Pens.Add(new Pen { End = T + 120, Pid = who.Id, Minor = true });
                who.Box.Pim += 2;
                r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "pen", P1 = who.Id, P2 = 2, Note = "задержка игры (видеопросмотр)" });
                recompose = true;
            }
            dirty = true;
            return overturned;
        }

        /// <summary>
        /// Зонд для проверок (до начала матча): ожидаемые голы в секунду при 5×5 и равном счёте, все свежие — пятёрка с игроком
        /// (его тройка и пара) против первой тройки и пары соперника и наоборот; с завершением и броском издали. Случайности не тратит.
        /// </summary>
        public (double gf, double ga) Probe(bool home, int id)
        {
            var t = home ? H : A; var o = Opp(t);
            int fi = t.F.FindIndex(u => u.Any(x => x.Id == id)), di = t.D.FindIndex(u => u.Any(x => x.Id == id));
            var on = (fi >= 0 ? t.F[fi] : t.F[0]).Concat(di >= 0 ? t.D[di] : t.D[0]).ToList();
            var oon = o.F[0].Concat(o.D[0]).ToList();
            var keep = (t.On, o.On, t.Mode, o.Mode, Per);
            t.On = on; o.On = oon; t.Mode = o.Mode = "es"; if (Per == 0) Per = 1;
            Rate(t, o); Rate(o, t);
            double gf = ExpGoals(t, o), ga = ExpGoals(o, t);
            (t.On, o.On, t.Mode, o.Mode, Per) = keep;
            dirty = true;
            return (gf, ga);
        }

        double ExpGoals(Team t, Team o)
        {
            double sum = 0, blkY = o.On.Average(x => x.BlkW) * t.BlkNow, lngRef0 = refs?.LngF ?? 11, lngRef1 = refs?.LngD ?? 11;
            for (int i = 0; i < t.On.Count; i++)
            {
                var s = t.On[i]; int d = s.D ? 1 : 0;
                var zs = ZoneNow(t, s);
                for (int z = 0; z < 3; z++)
                {
                    double b = Math.Min(0.85, BLOCK[d][z] * blkY * (s.Thru ? 0.75 : 1));
                    double on = (1 - b) * (1 - MISS[d][z]);
                    double xg = Math.Min(0.9, SH[d][z]) * Math.Exp(s.Phi * (s.FinE - 11) + (FIN_X + (s.D ? FIN_X_D : 0)) * (s.FinE - t.FinBar) + (z == 2 ? LNG_FAR * (s.Lng - (s.D ? lngRef1 : lngRef0)) : 0)) * t.Q;
                    sum += t.Pi[i] * zs[z] * on * (xg + (1 - xg) * REB_P * REB_G);
                }
            }
            return t.Rate * sum * o.GMult;
        }

        /// <summary>Состав на льду сейчас: «5×5», «5×4», «6×5»… с точки зрения хозяев.</summary>
        public string Strength => H.On.Count == 0 ? "" : $"{H.On.Count}×{A.On.Count}";
        /// <summary>Удаления команды за период (малые и большие, без совпадающих).</summary>
        public int PensIn(bool home, int per) => r.Events.Count(e => e.Kind == "pen" && e.Team == (home ? H : A).Abbr && e.T >= (per - 1) * 1200 && e.T < per * 1200);
        /// <summary>Событие тренера в протокол (решения в перерыве: темп, система, вратарь).</summary>
        public void Note(bool home, string text) => r.Events.Add(new MatchEvent { T = T, Team = (home ? H : A).Abbr, Kind = "coach", Note = text });
        /// <summary>Перерыв после периода: можно поменять стороны (MatchSide) и вызвать Apply.</summary>
        public Action<Sim> OnBreak;
        /// <summary>Трансляция: после перерыва RunTo возвращается (экран перерыва), следующий вызов начинает период.</summary>
        public bool PauseAtBreaks;

        public void Run() => RunTo(int.MaxValue);

        /// <summary>Играть до секунды until (не включая) или до конца; между периодами — OnBreak.</summary>
        public void RunTo(int until)
        {
            while (!finished)
            {
                if (!inPer && !NextPeriod()) return;
                while (inPer && T + 1 <= until && !finished) Tick();
                if (inPer) return;           // пауза внутри периода
                if (!finished && WillContinue()) { OnBreak?.Invoke(this); if (PauseAtBreaks) return; }
                if (T >= until) return;
            }
        }

        /// <summary>Будет ли ещё период после только что закончившегося.</summary>
        bool WillContinue() => Per < 3 || (H.Goals == A.Goals && !done && (playoff ? Per < 11 : !ot3));

        bool NextPeriod()
        {
            if (Per == 0) { Begin(1, 1200, false); return true; }
            if (Per < 3) { Begin(Per + 1, 1200, false); return true; }
            if (H.Goals == A.Goals && !done)
            {
                if (playoff && Per < 11) { Begin(Per + 1, 1200, true); return true; }
                if (!playoff && !ot3) { ot3 = true; Begin(4, 300, true); return true; }
                if (!playoff) Shootout();
            }
            Finish();
            return false;
        }

        void Finish()
        {
            finished = true;
            if (Per >= 4 && r.End == "REG") r.End = done ? "OT" : r.End;
            r.HomeGoals = H.Goals + (r.End == "SO" && r.Goals[^1].Team == H.Abbr ? 1 : 0);
            r.AwayGoals = A.Goals + (r.End == "SO" && r.Goals[^1].Team == A.Abbr ? 1 : 0);
            r.HomeShots = H.Sog; r.AwayShots = A.Sog;
            H.St["lam"] = H.LamSum; A.St["lam"] = A.LamSum; H.St["penx"] = H.PenSum; A.St["penx"] = A.PenSum;
            r.HomeStats = H.St; r.AwayStats = A.St;
            foreach (var t in new[] { H, A })
            {
                foreach (var sk in t.All.Values) r.Box[sk.Id] = sk.Box;
                if (t.Goalie != 0) r.Box[t.Goalie] = t.GBox;
            }
            r.Events.Sort((x, y) => x.T != y.T ? x.T.CompareTo(y.T) : (x.Kind == "goal" ? 1 : 0).CompareTo(y.Kind == "goal" ? 1 : 0));
        }

        void Begin(int per, int len, bool ot)
        {
            Per = per; perLen = len; perOt = ot; sec = 0; inPer = true;
            perStart = (Per - 1) * 1200;
            T = perStart;
            foreach (var t in new[] { H, A })
            {
                foreach (var sk in t.All.Values) { sk.Set(T, false); sk.E = 1; sk.T0 = T; }
                t.FLeft = t.DLeft = 0; t.Mode = "";
                t.Pulled = false;
                t.GSince = T;
            }
            recompose = dirty = true;
        }

        void Tick()
        {
            T = perStart + sec;
            Expire();
            if (!perOt) PullCheck(perLen - sec);
            Rotate();
            if (recompose) Compose();
            if (sec == 0) foPending = 1;
            if (foPending != 0)
            {
                // смена на остановке: гости первыми, хозяева — последними (видят, кого выпустил соперник)
                Deploy(A); Deploy(H);
                if (recompose) Compose();
                Faceoff();
            }
            if (T == H.FoUntil || T == A.FoUntil || T == H.RushUntil || T == A.RushUntil) dirty = true;
            if (dirty || T - lastRate >= 10) Rates();
            double u = rng.NextDouble();
            if ((u -= H.Rate) < 0) Attempt(H);
            else if ((u -= A.Rate) < 0) Attempt(A);
            else if ((u -= H.PenRate) < 0) Penalty(H);
            else if ((u -= A.PenRate) < 0) Penalty(A);
            else if (!ot3 && (u -= CoincRate()) < 0) Coincidental();
            else if ((u -= STOP_PER_60 / 3600) < 0) Stoppage();
            else if ((u -= H.HitRate) < 0) Minor(H, "hit");
            else if ((u -= A.HitRate) < 0) Minor(A, "hit");
            else if ((u -= H.TkRate) < 0) Minor(H, "tk");
            else if ((u -= A.TkRate) < 0) Minor(A, "tk");
            else if ((u -= H.GvRate) < 0) Minor(H, "gv");
            else if ((u -= A.GvRate) < 0) Minor(A, "gv");
            if (perOt && H.Goals != A.Goals) done = true;
            if (Diag != null)
                foreach (var t in new[] { H, A })
                {
                    Diag.Add(Diag.Sec, Label(t));
                    if (Label(t) == "5v5" && Per <= 3) { int df = Math.Clamp(t.Goals - Opp(t).Goals, -3, 3); Diag.ScoreSec[df] = Diag.ScoreSec.GetValueOrDefault(df) + 1; }
                    if (t.Pulled) { int df = t.Goals - Opp(t).Goals; Diag.PullSec[df] = Diag.PullSec.GetValueOrDefault(df) + 1; }
                    if (Label(t) == "5v5" && Per <= 3) foreach (var x in t.On) Diag.Es5Sec[x.Id] = Diag.Es5Sec.GetValueOrDefault(x.Id) + 1;
                }
            if (Diag != null)
                foreach (var t in new[] { H, A })
                {
                    var o = Opp(t);
                    if (t.Mode == "es" && o.Mode == "es" && o.Fi >= 0 && o.Fi == o.FTop)
                    {
                        Diag.TopSec++;
                        if (t.Fi >= 0 && t.Fi < t.FRole.Length && t.FRole[t.Fi] == 2) Diag.ShutVsTop++;
                    }
                }
            // минуты звеньев в равных составах — для ротации
            foreach (var t in new[] { H, A })
                if (t.Mode == "es") { if (t.Fi >= 0) t.FToi[t.Fi]++; if (t.Di >= 0) t.DToi[t.Di]++; }
            foreach (var t in new[] { H, A })
            {
                t.LamSum += t.Lc; t.PenSum += t.PenRate;
                if (t.Mode == "pp") { foreach (var sk in t.On) sk.Box.PpToi++; t.Add("pp_sec"); }
                else if (t.Mode == "pk") { foreach (var sk in t.On) sk.Box.ShToi++; t.Add("pk_sec"); }
                else if (t.Mode == "es" && !t.Pulled && !Opp(t).Pulled && T < t.RushUntil) t.Add("rush_sec");
            }
            sec++;
            if (sec >= perLen || done) End();
        }

        void End()
        {
            inPer = false;
            T = Math.Min(perStart + perLen, T + 1);
            foreach (var t in new[] { H, A })
            {
                Flush(t);
                foreach (var sk in t.All.Values) sk.Set(T, false);
                if (t.Goalie != 0) t.GBox.Toi += T - t.GSince;
                t.GSince = T;
            }
        }

        string Label(Team t)
        {
            var o = Opp(t); int n = Skaters(t), no = Skaters(o);
            if (o.Pulled) return "en_for";
            if (t.Pulled) return "en_against";
            if (n > no) return "pp";
            if (n < no) return "pk";
            return n == 5 ? "5v5" : n == 4 ? "4v4" : "3v3";
        }

        double CoincRate() => (H.N == 5 && A.N == 5 && !H.Pulled && !A.Pulled) ? COINC_PER_60 / 3600 : 0;

        // ─────────────────────────────── составы на льду

        int Skaters(Team t)
        {
            var o = Opp(t);
            // ОТ 3×3: удалённый сидит, у соперника появляется лишний полевой (4×3, 5×3)
            if (ot3) return Math.Min(5, 3 + o.Pens.Count(p => !p.Coinc));
            return Math.Max(3, 5 - t.Pens.Count);
        }

        void Expire()
        {
            foreach (var t in new[] { H, A })
                if (t.Pens.RemoveAll(p => p.End <= T) > 0) { recompose = dirty = true; }
        }

        void PullCheck(int left)
        {
            if (Per != 3) return;
            foreach (var t in new[] { H, A })
            {
                var o = Opp(t);
                int diff = t.Goals - o.Goals;
                bool want = t.Goalie != 0 && ((diff == -1 && left <= t.Pull1) || (diff == -2 && left <= t.Pull2) || (diff == -3 && left <= t.Pull3))
                            && (Skaters(t) >= Skaters(o) || left <= 40);
                if (want != t.Pulled) { t.Pulled = want; recompose = dirty = true; }
            }
        }

        void Rotate()
        {
            foreach (var t in new[] { H, A })
            {
                t.FLeft--; t.DLeft--; t.UnitLeft--;
                if (t.Mode == "es" && (t.FLeft <= 0 || t.DLeft <= 0)) recompose = true;
                if (t.Mode is "pp" or "pk" or "ot" && t.UnitLeft <= 0) recompose = true;
            }
        }

        void Compose()
        {
            recompose = false; dirty = true;
            foreach (var t in new[] { H, A })
            {
                var o = Opp(t);
                int n = Skaters(t), no = Skaters(o);
                t.N = n;
                var boxed = t.Pens.Select(p => p.Pid).ToHashSet();
                string mode = ot3 ? "ot" : n > no ? "pp" : n < no ? "pk" : "es";
                if (mode != t.Mode)
                {
                    if (mode == "pp" && t.Mode == "es" && Diag != null) Diag.PpEntries++;
                    t.UnitIdx = 0; t.UnitLeft = mode == "pp" ? PP1_SEC : mode == "pk" ? PK_SHIFT : OT_SHIFT;
                    if (mode == "es") { t.FLeft = 0; t.DLeft = 0; }
                    t.Mode = mode;
                }
                var list = new List<Sk>();
                switch (mode)
                {
                    case "es":
                        if (t.FLeft <= 0 || t.Fi < 0)
                        {
                            t.Fi = Next(t.F, t.FT, t.FToi, t.Fi, t.All.Values, boxed);
                            // против лучшей тройки соперника — оборонительная, если отдохнула и не сверх своих минут
                            if (t.Matchup && o.Mode == "es" && o.Fi >= 0 && o.Fi == o.FTop && Find(t.FRole, 2) is int sh and >= 0 && sh != t.Fi && Ready(t, true, sh)) t.Fi = sh;
                            t.FLeft = Math.Max(20, SHIFT_F + SHIFT_SD * Normal());
                        }
                        if (t.DLeft <= 0 || t.Di < 0) { t.Di = Next(t.D, t.DT, t.DToi, t.Di, t.All.Values, boxed); t.DLeft = Math.Max(20, SHIFT_D + SHIFT_SD * Normal()); }
                        if (t.Fi >= 0) list.AddRange(t.F[t.Fi].Where(x => !boxed.Contains(x.Id)));
                        if (t.Di >= 0) list.AddRange(t.D[t.Di].Where(x => !boxed.Contains(x.Id)));
                        Fill(t, list, n, boxed);
                        while (list.Count > n) list.Remove(list.Where(x => !x.D || list.Count(y => y.D) > 2).OrderBy(x => x.Off).First());
                        break;
                    case "pp":
                        if (t.UnitLeft <= 0) { t.UnitIdx = 1; t.UnitLeft = 999; }
                        list.AddRange((t.UnitIdx == 0 || t.Pp2.Length == 0 ? t.Pp1 : t.Pp2).Where(x => !boxed.Contains(x.Id)));
                        Fill(t, list, n, boxed);
                        while (list.Count > n) list.Remove(list.OrderBy(x => x.Off).First());
                        break;
                    case "pk":
                        if (t.UnitLeft <= 0) { t.UnitIdx ^= 1; t.UnitLeft = PK_SHIFT; }
                        var pk = t.UnitIdx == 0 || t.Pk2.Length == 0 ? t.Pk1 : t.Pk2;
                        list.AddRange(pk.Where(x => !boxed.Contains(x.Id)));
                        while (list.Count > n) list.Remove(list.Where(x => !x.D || list.Count(y => y.D) > 2).OrderBy(x => x.Def).First());
                        Fill(t, list, n, boxed);
                        break;
                    case "ot":
                        if (t.UnitLeft <= 0) { t.UnitIdx = (t.UnitIdx + 1) % Math.Max(1, t.Ot.Count); t.UnitLeft = OT_SHIFT; }
                        if (t.Ot.Count > 0) list.AddRange(t.Ot[t.UnitIdx % t.Ot.Count].Where(x => !boxed.Contains(x.Id)));
                        Fill(t, list, n, boxed);
                        break;
                }
                if (t.Pulled) Fill(t, list, list.Count + 1, boxed, attack: true);
                foreach (var sk in t.On) if (!list.Contains(sk)) sk.Set(T, false);
                foreach (var sk in list) sk.Set(T, true);
                Flush(t);
                t.On = list;
                t.Fam = UnitFam(t, list);
            }
        }

        static int Find(int[] roles, int role) => Array.IndexOf(roles, role);

        /// <summary>Звено готово выйти: отдохнуло (средняя энергия ≥ DEPLOY_E) и не переиграло свои минуты больше чем на DEPLOY_OVER секунд.</summary>
        bool Ready(Team t, bool fwd, int i)
        {
            var units = fwd ? t.F : t.D; var toi = fwd ? t.FToi : t.DToi; var tg = fwd ? t.FT : t.DT;
            if (i < 0 || i >= units.Count) return false;
            if (units[i].Average(x => x.Energy(T)) < DEPLOY_E) return false;
            return toi[i] - tg[i] * (toi.Sum() + 45) < DEPLOY_OVER;
        }

        /// <summary>
        /// Расстановка на вбрасывание по ролям: в зоне атаки — атакующие тройка и пара, в своей — оборонительные;
        /// хозяева (последняя смена) выпускают оборонительную тройку против лучшей тройки соперника и на вбрасывании в средней зоне.
        /// </summary>
        void Deploy(Team t)
        {
            if (t.Mode != "es" || t.Pulled || ot3 || foPending == 0) return;
            var o = Opp(t);
            int z = foPending == 1 ? 1 : (foPending == 2) == (t == H) ? 0 : 2;
            int wf = z == 0 ? Find(t.FRole, 1) : z == 2 ? Find(t.FRole, 2) : -1;
            int wd = z == 0 ? Find(t.DRole, 1) : z == 2 ? Find(t.DRole, 2) : -1;
            if (t == H && t.Matchup && o.Fi >= 0 && o.Fi == o.FTop && z != 0) { wf = Find(t.FRole, 2); wd = Find(t.DRole, 2); }
            if (wf >= 0 && wf != t.Fi && Ready(t, true, wf)) { t.Fi = wf; t.FLeft = Math.Max(20, SHIFT_F + SHIFT_SD * Normal()); recompose = true; }
            if (wd >= 0 && wd != t.Di && Ready(t, false, wd)) { t.Di = wd; t.DLeft = Math.Max(20, SHIFT_D + SHIFT_SD * Normal()); recompose = true; }
            if (Diag != null && t.Fi >= 0 && t.Fi < t.FRole.Length) Diag.RoleStarts[t.FRole[t.Fi] is >= 0 and <= 2 ? t.FRole[t.Fi] : 0, z]++;
        }

        /// <summary>Смена вратаря: время на льду — уходящему, сменщик — до конца (в протоколе — второй вратарь).</summary>
        void SwapGoalie(Team t, int id)
        {
            if (!data.PlayerById.TryGetValue(id, out var gp)) return;
            int old = t.Goalie;
            if (old != 0) { if (inPer) t.GBox.Toi += T - t.GSince; r.Box[old] = t.GBox; }
            t.GSince = T;
            t.Goalie = id;
            t.GBox = r.Box.TryGetValue(id, out var b) ? b : new PlayerBox();
            double gs = MatchEngine.GsaxOf(data, id, t.S.Dl);
            t.GMult = Math.Clamp((1 - MatchEngine.G_W * gs) / (1 - MatchEngine.G_W * sc.G0), 0.8, 1.2);
            t.GSkill = (Attr(t.S, gp, "rfx") + Attr(t.S, gp, "gpos")) / 2 - 11;
            if (t == H) r.HomeGoalie2 = id; else r.AwayGoalie2 = id;
            if (r.HomeGoalie2 == r.HomeGoalie) r.HomeGoalie2 = 0;
            if (r.AwayGoalie2 == r.AwayGoalie) r.AwayGoalie2 = 0;
            r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "goalie", P1 = id, P2 = old });
        }

        /// <summary>Секунды вместе для пар текущей пятёрки — в протокол сыгранности.</summary>
        void Flush(Team t)
        {
            if (t.S.Fam != null && T > t.OnSince && t.On.Count > 1)
            {
                r.PairSec ??= new();
                int dt = T - t.OnSince;
                for (int i = 0; i < t.On.Count; i++)
                    for (int j = i + 1; j < t.On.Count; j++)
                        if (t.On[i].D == t.On[j].D)
                        {
                            long k = PairKey(t.On[i].Id, t.On[j].Id);
                            r.PairSec[k] = r.PairSec.GetValueOrDefault(k) + dt;
                        }
            }
            t.OnSince = T;
        }

        static double UnitFam(Team t, List<Sk> on)
        {
            // без вратаря (6 на 5) — сборная пятёрка-шестёрка: сыгранность не учитываем
            if (t.S.Fam == null || t.Pulled) return FAM_REF;
            double sum = 0; int n = 0;
            for (int i = 0; i < on.Count; i++)
                for (int j = i + 1; j < on.Count; j++)
                    if (on[i].D == on[j].D) { sum += t.S.Fam(on[i].Id, on[j].Id); n++; }
            return n == 0 ? FAM_REF : sum / n;
        }

        static int Next(List<Sk[]> units, double[] target, double[] toi, int cur, IEnumerable<Sk> all, HashSet<int> boxed)
        {
            if (units.Count == 0) return -1;
            double elapsed = toi.Sum();
            int best = -1; double bv = double.NegativeInfinity;
            for (int i = 0; i < units.Count; i++)
            {
                if (i == cur && units.Count > 1) continue;
                double v = target[i] * (elapsed + 45) - toi[i];
                if (v > bv) { bv = v; best = i; }
            }
            return best;
        }

        /// <summary>Добрать до need игроков: свободные (не на льду, не на скамейке штрафников), по позиции и силе.</summary>
        static void Fill(Team t, List<Sk> list, int need, HashSet<int> boxed, bool attack = false)
        {
            while (list.Count < need)
            {
                int dNow = list.Count(x => x.D);
                bool wantD = !attack && dNow < 2 && need - list.Count <= 2 - dNow;
                var pool = t.All.Values.Where(x => !list.Contains(x) && !boxed.Contains(x.Id)).ToList();
                if (pool.Count == 0) break;
                var pick = (wantD ? pool.Where(x => x.D) : attack ? pool.Where(x => !x.D) : pool).DefaultIfEmpty(pool[0])
                    .OrderByDescending(x => attack ? x.Off : x.Off + x.Def).First();
                list.Add(pick);
            }
        }

        // ─────────────────────────────── интенсивность

        void Rates()
        {
            lastRate = T; dirty = false;
            foreach (var t in new[] { H, A }) Rate(t, Opp(t));
        }

        double Eff(Sk s, double v) { double e = s.Energy(T); return e < FAT_AT ? v - FAT_K * (FAT_AT - e) : v; }

        void Rate(Team t, Team o)
        {
            int n = t.On.Count, no = o.On.Count;
            if (n == 0 || no == 0) { t.Rate = t.PenRate = 0; return; }
            int nt = Skaters(t), nop = Skaters(o);
            // база состава
            double baseG;
            if (o.Pulled) baseG = GF60_EN;
            else if (t.Pulled) baseG = GF60_6V5;
            else if (nt > nop) baseG = GF60_PP * (nt - nop >= 2 ? 1.8 : 1) * (ot3 ? 1.3 : 1);
            else if (nt < nop) baseG = GF60_PK;
            else baseG = nt == 5 ? GF60_5V5 : nt == 4 ? GF60_4V4 : GF60_3V3;
            // атака пятёрки (без доли завершения — она на броске) и оборона пятёрки соперника; вратарь — на броске
            double sOff = 0, sDef = 0;
            bool esT = t.Mode == "es", esO = o.Mode == "es";
            foreach (var s in t.On) { sOff += Eff(s, s.Off) - s.FinPart - (esT ? s.PosOff : 0); }
            foreach (var s in o.On) sDef += Eff(s, s.Def) - (esO ? s.PosDef : 0);
            // звено против среднего звена своего вида (на большинство выходят лучшие — база НХЛ это уже знает)
            string mt = UnitKind(t), mo = UnitKind(o);
            double offRef = refs.Off.GetValueOrDefault(mt, refs.Off["es"]) - refs.Off["es"];
            double defRef = refs.Def.GetValueOrDefault(mo, refs.Def["es"]) - refs.Def["es"];
            double x = 5.0 / n * sOff - offRef;
            double att = spread * (sc.A0 + sc.A1 * (t.EsOff + LINE_K * (x - t.EsOff)));
            double dfn = spread * (sc.D0 + sc.D2 * sc.G0 + sc.D1 * (5.0 / no * sDef - defRef));
            att += FamE(t.Fam); dfn -= FamE(o.Fam);   // сыгранность своей пятёрки и пятёрки соперника
            if (mt == "pp") att += PP_K * (t.On.Sum(x => x.Pp - 11) * 5.0 / n - refs.PpAttr);
            if (mo == "pk") dfn -= PK_K * (o.On.Sum(x => x.Pk - 11) * 5.0 / no - refs.PkAttr);
            baseG *= K_STATE.GetValueOrDefault(Label(t), 1);
            if (Per <= 3 && !o.Pulled) baseG *= PERIOD_K[Per - 1];
            // эффект счёта (5×5 и 4×4 в основное время)
            double seG = 1, seA = 1;
            if (Per <= 3 && nt == nop && !t.Pulled && !o.Pulled)
            {
                int diff = t.Goals - o.Goals;
                if (diff < 0) { int k = Math.Min(3, -diff) - 1; seG = SE_G_TRAIL[k]; seA = SE_ATT_TRAIL[k]; }
                else if (diff > 0) { int k = Math.Min(3, diff) - 1; seG = SE_G_LEAD[k]; seA = SE_ATT_LEAD[k]; }
            }
            double foG = T < t.FoUntil ? t.FoG : 1, foA = T < t.FoUntil ? t.FoAtt : 1;
            t.Lc = baseG / 3600 * norm * K_CAL * t.Mult * Math.Exp(att + dfn) * seG * foG;
            // часть силы атаки и обороны — в качестве бросков (реализации), а не в их числе: голы те же, но за сильной обороной
            // вратарь видит броски похуже, а у сильной атаки выше реализация (зеркальная проверка вратарей: % отражённых устойчив)
            t.Q = seG / seA * foG / foA * ATT_Q * (t.S.Sys?.Q ?? 1) * Math.Exp(QS_A * (att - spread * sc.MA) + QS_D * (dfn - spread * sc.MY));
            t.HitRate = t.TkRate = t.GvRate = 0;
            foreach (var sk in t.On) { t.HitRate += sk.HitR; t.TkRate += sk.TkR; t.GvRate += sk.GvR; }
            // спецбригады по системам: своё большинство и меньшинство соперника (голы, качество, кто и откуда бросает, блоки)
            var ts = t.S.Sys; var os = o.S.Sys;
            t.Zf0 = t.Zf2 = t.DwNow = 1; t.BlkNow = os?.Blk ?? 1;
            if (ts != null && mt == "pp") { t.Lc *= Math.Exp(ts.PpF); t.Q *= ts.PpQ; t.DwNow = ts.PpDW; t.Zf0 = ts.PpClose; t.Zf2 = ts.PpFar; }
            if (os != null && mo == "pk") { t.Lc *= Math.Exp(os.PkA); t.Zf0 *= os.PkClose; t.Zf2 *= os.PkFar; t.BlkNow = os.PkBlk; }
            if (ts != null && mt == "pk") t.Lc *= ts.PkSh;
            if (os != null && mt == "es" && mo == "es") t.Q *= os.OppQ;
            // атака с хода после отбора (только 5×5 и 4×4): в окне — больше и опаснее, вне окна — нормировка (голы за матч те же)
            if (t.Mode == "es" && o.Mode == "es" && !t.Pulled && !o.Pulled)
            {
                double gm = 1 + (RUSH_G - 1) * t.RushK;
                double f = Math.Min(0.5, (t.TkRate + o.On.Sum(x => x.GvR)) * RUSH_WIN);
                t.Lc /= 1 + f * (gm - 1);
                if (T < t.RushUntil) { t.Lc *= gm; t.Q *= RUSH_Q; }
            }
            // ожидаемая отдача попытки при этом составе (без поправки на вратаря и завершение — они в среднем 1)
            t.Pi = new double[n];
            double wsum = 0;
            for (int i = 0; i < n; i++) { t.Pi[i] = t.On[i].W * (t.On[i].D ? t.DwNow : 1); wsum += t.Pi[i]; }
            t.FinBar = 0; for (int i = 0; i < n; i++) t.FinBar += t.Pi[i] / wsum * t.On[i].FinE;
            double blkY = o.On.Average(x => x.BlkW) * t.BlkNow;
            var (bm, mm, sm) = StateMods(nt, nop, t.Pulled, o.Pulled);
            double E = 0;
            for (int i = 0; i < n; i++)
            {
                t.Pi[i] /= wsum;
                var s = t.On[i]; int d = s.D ? 1 : 0;
                var zs = ZoneNow(t, s);
                for (int z = 0; z < 3; z++)
                {
                    double b = Math.Min(0.85, BLOCK[d][z] * bm * blkY * (s.Thru ? 0.75 : 1));
                    double on = (1 - b) * (1 - MISS[d][z] * mm);
                    double sh = o.Pulled ? 1 : Math.Min(0.9, SH[d][z] * sm);
                    E += t.Pi[i] * zs[z] * on * (sh + (o.Pulled ? 0 : (1 - sh) * REB_P * REB_G));
                }
            }
            t.Rate = t.Lc / Math.Max(1e-6, E * t.Q);
            // удаления: дисциплина пятёрки; «зарабатывает удаления» у соперника
            double pw = t.On.Average(x => x.PenW);
            int draws = o.On.Count(x => x.Draw);
            t.PenRate = (ot3 ? 0.5 : 1) * PEN_PER_60 / 3600 * pw * (1 + 0.25 * draws);
        }

        /// <summary>Откуда бросает игрок сейчас: его зоны с поправкой спецбригад (зонтик — издали, 1-3-1 — у ворот).</summary>
        static double[] ZoneNow(Team t, Sk s)
        {
            if (t.Zf0 == 1 && t.Zf2 == 1) return s.Zone;
            double a = s.Zone[0] * t.Zf0, b = s.Zone[1], c = s.Zone[2] * t.Zf2, sum = a + b + c;
            return new[] { a / sum, b / sum, c / sum };
        }

        string UnitKind(Team t) => t.Pulled ? "es" : t.Mode is "pp" or "pk" or "ot" ? t.Mode : "es";

        static (double block, double miss, double sh) StateMods(int n, int no, bool pulled, bool oppPulled)
        {
            if (oppPulled) return (0.8, 1.47, 1);
            if (pulled) return (1.17, 0.97, 1.37);
            if (n > no) return (0.885, 1.0, 1.61);
            if (n < no) return (0.42, 0.74, 1.03);
            return n == 5 ? (1, 1, 1) : n == 4 ? (0.8, 0.93, 1.23) : (0.58, 0.9, 2.07);
        }

        // ─────────────────────────────── события

        // типы бросков по зонам и позициям (НХЛ: щелчки — 5% бросков нападающих и 20% защитников; заезды — 0,8%)
        static readonly string[] Close = { "подставка", "кистевой", "с неудобной руки", "рикошет", "заезд из-за ворот" };
        static readonly double[][] CloseW = { new[] { 0.30, 0.36, 0.20, 0.07, 0.02 }, new[] { 0.15, 0.55, 0.20, 0.07, 0.01 } };
        static readonly string[] Mid = { "кистевой", "с короткого замаха", "с неудобной руки", "щелчок", "в одно касание" };
        static readonly double[][] MidW = { new[] { 0.58, 0.30, 0.07, 0.025, 0.025 }, new[] { 0.50, 0.20, 0.05, 0.15, 0.10 } };
        static readonly string[] Far = { "щелчок", "кистевой", "с короткого замаха" };
        static readonly double[][] FarW = { new[] { 0.15, 0.55, 0.30 }, new[] { 0.45, 0.40, 0.15 } };

        string ShotType(Sk s, int z)
        {
            int d = s.D ? 1 : 0;
            var (names, w) = z == 0 ? (Close, CloseW[d]) : z == 1 ? (Mid, MidW[d]) : (Far, FarW[d]);
            var ww = (double[])w.Clone();
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == "подставка" && s.Tip) ww[i] *= 2.7;
                if (names[i] == "с неудобной руки" && s.Back) ww[i] *= 2.1;
                if (names[i] == "заезд из-за ворот" && s.Wrap) ww[i] *= 4.5;
                if ((names[i] == "щелчок" || names[i] == "в одно касание") && s.Slap) ww[i] *= s.D ? 2.7 : 3.3;
            }
            return names[PickIdx(ww)];
        }

        int PickIdx(IList<double> w)
        {
            double sum = 0; foreach (var x in w) sum += x;
            double u = rng.NextDouble() * sum;
            for (int i = 0; i < w.Count; i++) { u -= w[i]; if (u <= 0) return i; }
            return w.Count - 1;
        }

        Sk PickSk(IList<Sk> list, Func<Sk, double> w)
        {
            if (list.Count == 0) return null;
            var ws = list.Select(w).ToArray();
            return list[PickIdx(ws)];
        }

        void Attempt(Team t)
        {
            var o = Opp(t);
            int i = PickIdx(t.Pi);
            var s = t.On[i];
            int z = PickIdx(ZoneNow(t, s));
            Shot(t, o, s, z, rebound: false, null);
        }

        int curZ = -1, curR = 0; double curXg = 0;   // бросок, ставший голом: зона и xG для события «гол»
        void Shot(Team t, Team o, Sk s, int z, bool rebound, Sk prev)
        {
            int nt = Skaters(t), nop = Skaters(o);
            var (bm, mm, smod) = StateMods(nt, nop, t.Pulled, o.Pulled);
            int d = s.D ? 1 : 0;
            s.Box.Att++;
            Diag?.S(s.Id, "att");
            string lb = Label(t);
            if (Diag != null)
            {
                Diag.Add(Diag.Att, lb);
                if (lb == "5v5" && Per <= 3) { int df = Math.Clamp(t.Goals - o.Goals, -3, 3); Diag.ScoreSecAtt[df] = Diag.ScoreSecAtt.GetValueOrDefault(df) + 1; }
                if (rebound) Diag.Rebounds++;
            }
            int rush = T < t.RushUntil ? 1 : 0;
            t.Add("att"); if (s.D) t.Add("datt"); if (rush == 1) t.Add("rush_att");
            if (!rebound) t.Add("z" + z);
            if (t.Mode == "pp") { t.Add("pp_att"); if (s.D) t.Add("pp_datt"); if (!rebound && z == 2) t.Add("pp_far"); }
            if (t.Mode == "pk") t.Add("pk_att");
            if (!rebound)
            {
                double blkY = o.On.Average(x => x.BlkW) * t.BlkNow;
                double b = Math.Min(0.85, BLOCK[d][z] * bm * blkY * (s.Thru ? 0.75 : 1));
                if (rng.NextDouble() < b)
                {
                    var blocker = PickSk(o.On, x => x.BlkW * (x.D ? 1.5 : 1) * (x.BlkT ? 1.12 : 1));
                    if (blocker != null) blocker.Box.Blk++;
                    o.Add("blk"); if (o.Mode == "pk") o.Add("pk_blk");
                    Diag?.S(s.Id, "blkd");
                    if (Diag != null) Diag.Add(Diag.Blk, lb);
                    r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "block", P1 = s.Id, P2 = blocker?.Id ?? 0, Z = z, R = rush });
                    return;
                }
                if (Diag != null) { Diag.S(s.Id, "unb"); Diag.S(s.Id, z == 0 ? "close" : z == 2 ? "far" : "mid"); if (z < 2) Diag.S(s.Id, "near40"); if (lb == "5v5") Diag.S(s.Id, (z == 0 ? "close" : z == 2 ? "far" : "mid") + "_n5"); }
                if (rng.NextDouble() < MISS[d][z] * mm)
                {
                    r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "miss", P1 = s.Id, Note = rng.NextDouble() < 0.1 ? "в штангу" : "мимо", Z = z, R = rush });
                    if (Diag != null) Diag.Add(Diag.Miss, lb);
                    return;
                }
            }
            else if (Diag != null) { Diag.S(s.Id, "unb"); Diag.S(s.Id, "close"); Diag.S(s.Id, "near40"); Diag.S(s.Id, "reb"); if (lb == "5v5") Diag.S(s.Id, "close_n5"); }
            if (rebound && rng.NextDouble() > REB_ON)
            {
                r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "miss", P1 = s.Id, Note = "добивание мимо", Z = 0 });
                return;
            }
            // в створ
            string type = rebound ? "добивание" : ShotType(s, z);
            if (Diag != null)
            {
                Diag.S(s.Id, "sog");
                string k = type switch { "подставка" or "рикошет" => "tip", "щелчок" or "в одно касание" => "slap", "с неудобной руки" => "back", "заезд из-за ворот" => "wrap", _ => null };
                if (k != null) Diag.S(s.Id, k);
            }
            double xg = rebound ? REB_G / REB_ON : Math.Min(0.9, SH[d][z] * smod);
            double lngRef = s.D ? refs?.LngD ?? 11 : refs?.LngF ?? 11;
            xg *= Math.Exp(s.Phi * (s.FinE - 11) + (FIN_X + (s.D ? FIN_X_D : 0)) * (s.FinE - t.FinBar) + (z == 2 && !rebound ? LNG_FAR * (s.Lng - lngRef) : 0)) * t.Q;
            t.Sog++; s.Box.S++;
            if (Diag != null) Diag.Add(Diag.Sog, lb);
            bool empty = o.Pulled || o.Goalie == 0;
            double p = empty ? 1 : Math.Min(0.95, xg * o.GMult);
            if (Diag != null && z == 2 && !rebound && !empty)
            {
                // ожидаемые голы, которые дал именно дальний бросок (сверх игрока со средним lng)
                double mL = Math.Exp(LNG_FAR * (s.Lng - (s.D ? refs?.LngD ?? 11 : refs?.LngF ?? 11)));
                Diag.S(s.Id, "lngx", p - p / mL);
            }
            double xgRep = Math.Min(0.95, xg) * XG_CAL;   // xG для протокола и статистики (на исход не влияет)
            if (!empty) { o.GBox.Sa++; o.GBox.Xga += xgRep; s.Box.Ixg += xgRep; t.Add("xg", xgRep); if (rush == 1) t.Add("rush_xg", xgRep); }
            t.Add("sog");
            if (rng.NextDouble() < p)
            {
                if (Diag != null) { Diag.Add(Diag.Goals, lb); if (rebound) Diag.RebGoals++; if (lb == "5v5") Diag.S(s.Id, (z == 0 || rebound ? "close" : z == 2 ? "far" : "mid") + "_g5"); }
                curZ = rebound ? 0 : z; curXg = Math.Round(xgRep, 3); curR = rush;
                Goal(t, o, s, type, rebound ? prev : null); curZ = -1; curXg = 0; curR = 0; return;
            }
            r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "shot", P1 = s.Id, P2 = o.Goalie, Note = type, Z = rebound ? 0 : z, Xg = Math.Round(xgRep, 3), R = rush });
            if (!rebound && rng.NextDouble() < FREEZE) { foPending = t == H ? 2 : 3; return; }   // вратарь накрыл — вбрасывание в зоне атаки бросавших
            // добивание
            if (!rebound && rng.NextDouble() < REB_P / (1 - FREEZE))
            {
                var others = t.On.Where(x => x != s).ToList();
                var rs = PickSk(others.Count > 0 ? others : t.On, x => (x.NetT ? 2.5 : 1) * (x.D ? 0.4 : 1) * x.Zone[0]);
                if (rs != null) Shot(t, o, rs, 0, true, s);
            }
        }

        void Goal(Team t, Team o, Sk s, string type, Sk reboundFrom)
        {
            int nt = Skaters(t), nop = Skaters(o);
            string kind = o.Pulled ? "en" : t.Pulled ? "ev6" : nt > nop ? "pp" : nt < nop ? "sh" : "ev";
            if (Diag != null)
            {
                foreach (var x in t.On) Diag.OnGf[x.Id] = Diag.OnGf.GetValueOrDefault(x.Id) + 1;
                if (Label(t) == "5v5" && Per <= 3)
                {
                    foreach (var x in t.On) Diag.Es5Gf[x.Id] = Diag.Es5Gf.GetValueOrDefault(x.Id) + 1;
                    foreach (var x in o.On) Diag.Es5Ga[x.Id] = Diag.Es5Ga.GetValueOrDefault(x.Id) + 1;
                }
                if (t.Pulled) { int df = t.Goals - o.Goals; Diag.PullGf[df] = Diag.PullGf.GetValueOrDefault(df) + 1; }
                if (o.Pulled) { int df = o.Goals - t.Goals; Diag.PullGa[df] = Diag.PullGa.GetValueOrDefault(df) + 1; }
            }
            t.Goals++;
            if (!o.Pulled && o.Goalie != 0) o.GBox.Ga++;
            if (curR == 1) t.Add("rush_g");
            if (kind == "pp") t.Add("pp_g"); if (kind == "sh") t.Add("sh_g");
            var mates = t.On.Where(x => x != s).ToList();
            var ge = new GoalEvent { Period = Per, Sec = T - (Per - 1) * 1200, Team = t.Abbr, Scorer = s.Id, Kind = kind, Shot = type };
            double u = rng.NextDouble();
            int nA = u < 0.064 ? 0 : u < 0.244 ? 1 : 2;
            if (reboundFrom != null && nA == 0) nA = 1;
            if (nA >= 1 && mates.Count > 0)
            {
                var a1 = reboundFrom != null && mates.Contains(reboundFrom) ? reboundFrom : PickSk(mates, x => x.Pass);
                ge.A1 = a1.Id; mates.Remove(a1);
                Diag?.S(a1.Id, "a1");
                if (nA >= 2 && mates.Count > 0) ge.A2 = PickSk(mates, x => x.A2W).Id;
            }
            r.Goals.Add(ge);
            if (Diag != null)
            {
                Diag.Add(Diag.RoleShare, "g_" + (s.D ? "D" : "F"));
                if (ge.A1 != 0) Diag.Add(Diag.RoleShare, "a1_" + (t.All[ge.A1].D ? "D" : "F"));
                if (ge.A2 != 0) Diag.Add(Diag.RoleShare, "a2_" + (t.All[ge.A2].D ? "D" : "F"));
            }
            r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "goal", P1 = s.Id, P2 = ge.A1, Note = kind, Z = curZ, Xg = curXg, R = curR });
            GoalOn[T * 2 + (t == H ? 0 : 1)] = (t.On.Select(x => x.Id).ToArray(), o.On.Select(x => x.Id).ToArray(), s.Id, o.Goalie);
            if (kind != "pp")
            {
                foreach (var x in t.On) x.Box.Pm++;
                foreach (var x in o.On) x.Box.Pm--;
            }
            // гол в большинстве прекращает малый штраф соперника (двойной — до второй половины)
            if (kind == "pp")
            {
                var pen = o.Pens.Where(p => p.Minor && !p.Coinc).OrderBy(p => p.End).FirstOrDefault();
                if (pen != null)
                {
                    if (pen.Double && pen.End - T > 120) pen.End = T + 120; else o.Pens.Remove(pen);
                }
            }
            // после гола — новое вбрасывание в центре, тренеры меняют звенья
            t.FLeft = t.DLeft = o.FLeft = o.DLeft = 0;
            recompose = dirty = true;
            foPending = 1;
        }

        void Penalty(Team t)
        {
            var who = PickSk(t.On, x => x.PenW);
            if (who == null) return;
            double u = rng.NextDouble();
            int min = u < 0.93 ? 2 : u < 0.945 ? 4 : 5;
            var pen = new Pen { End = T + min * 60, Pid = who.Id, Minor = min < 5, Double = min == 4 };
            t.Pens.Add(pen);
            t.Add("pen");
            who.Box.Pim += min;
            r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "pen", P1 = who.Id, P2 = min, Note = PenNames[rng.Next(PenNames.Length)] });
            recompose = dirty = true;
            foPending = Opp(t) == H ? 2 : 3;     // в зоне нарушителя
        }

        /// <summary>Остановка без броска (проброс, вне игры, шайба за бортом): в центре или у ворот того, на кого давят.</summary>
        void Stoppage()
        {
            if (rng.NextDouble() < STOP_NEUTRAL) { foPending = 1; return; }
            // в зоне того, на кого давят; кто выходит из зоны по борту и выбрасывает — чаще у своих ворот
            double wh = H.Rate * A.Ice, wa = A.Rate * H.Ice;
            double ph = wh / Math.Max(1e-9, wh + wa);
            foPending = rng.NextDouble() < ph ? 2 : 3;
        }

        void Faceoff()
        {
            int zone = foPending; foPending = 0;
            Sk Taker(Team t) => t.On.Where(x => !x.D).OrderByDescending(x => x.P.Pos == "C" ? 1 : 0).ThenByDescending(x => x.Fo).FirstOrDefault() ?? t.On.FirstOrDefault();
            var ch = Taker(H); var ca = Taker(A);
            if (ch == null || ca == null) return;
            // не центр на вбрасывании: его «вбрасывание» — по шкале крайних, против центров это −3,4 ед.
            double Fo(Sk x) => x.Fo - (x.P.Pos == "C" ? 0 : Lineup.POS_C_FO);
            bool homeWins = rng.NextDouble() < 1 / (1 + Math.Exp(-FO_K * (Fo(ch) - Fo(ca))));
            var (w, l, wt, lt) = homeWins ? (ch, ca, H, A) : (ca, ch, A, H);
            w.Box.Fow++; l.Box.Fol++;
            H.Add("fo"); A.Add("fo");
            if (zone == 2) A.Add("fo_dz"); else if (zone == 3) H.Add("fo_dz");
            // зона с точки зрения выигравшего: 0 — атаки, 1 — средняя, 2 — своя
            int z = zone == 1 ? 1 : (zone == 2) == (wt == H) ? 0 : 2;
            wt.FoAtt = FO_MULT[z, 0]; wt.FoG = FO_MULT[z, 1]; lt.FoAtt = FO_MULT[z, 2]; lt.FoG = FO_MULT[z, 3];
            wt.FoUntil = lt.FoUntil = T + (int)FO_WIN;
            r.Events.Add(new MatchEvent { T = T, Team = wt.Abbr, Kind = "fo", P1 = w.Id, P2 = l.Id, Note = z == 0 ? "O" : z == 1 ? "N" : "D" });
            Diag?.S(w.Id, "fow"); Diag?.S(l.Id, "fol");
            dirty = true;
        }

        void Minor(Team t, string kind)
        {
            var who = PickSk(t.On, x => kind == "hit" ? x.HitR : kind == "tk" ? x.TkR : x.GvR);
            if (who == null) return;
            int victim = 0;
            t.Add(kind);
            if (kind == "hit") { who.Box.Hit++; victim = PickSk(Opp(t).On, x => x.HitTarget ? 3 : 1)?.Id ?? 0; }
            else if (kind == "tk") { who.Box.Tk++; StartRush(t); }
            else { who.Box.Gv++; StartRush(Opp(t)); }
            r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = kind, P1 = who.Id, P2 = victim });
        }

        /// <summary>Шайба сменила владельца: у забравших — атака с хода (только в равных составах без пустых ворот).</summary>
        void StartRush(Team x)
        {
            var y = Opp(x);
            if (x.Mode != "es" || y.Mode != "es" || x.Pulled || y.Pulled) return;
            x.RushUntil = T + (int)RUSH_WIN; y.RushUntil = -1;
            dirty = true;
        }

        void Coincidental()
        {
            foreach (var t in new[] { H, A })
            {
                var who = PickSk(t.On, x => x.PenW);
                if (who == null) continue;
                t.Pens.Add(new Pen { End = T + 120, Pid = who.Id, Minor = true, Coinc = true });
                who.Box.Pim += 2;
                r.Events.Add(new MatchEvent { T = T, Team = t.Abbr, Kind = "pen", P1 = who.Id, P2 = 2, Note = PenNames[rng.Next(PenNames.Length)] });
            }
            recompose = dirty = true;
        }

        // ─────────────────────────────── буллиты

        void Shootout()
        {
            r.End = "SO";
            List<Sk> Order(Team t) => t.All.Values.OrderByDescending(x => x.So).ToList();
            var hs = Order(H); var as_ = Order(A);
            double P(Sk s, Team g) => Math.Clamp(0.32 * Math.Exp(0.09 * (s.So - 13) - 0.09 * g.GSkill), 0.12, 0.6);
            int h = 0, a = 0;
            for (int round = 0; round < 30; round++)
            {
                if (as_.Count > 0 && rng.NextDouble() < P(as_[round % as_.Count], H)) a++;
                if (round < 3 && h + (3 - round) < a) break;          // хозяевам уже не догнать
                if (hs.Count > 0 && rng.NextDouble() < P(hs[round % hs.Count], A)) h++;
                if (round < 3) { if (a + (2 - round) < h || h + (2 - round) < a) break; }
                else if (h != a) break;
            }
            if (h == a) { if (rng.NextDouble() < 0.5) h++; else a++; }
            var w = h > a ? H : A;
            r.Goals.Add(new GoalEvent { Period = 5, Sec = 0, Team = w.Abbr, Kind = "so" });
        }
    }
}
