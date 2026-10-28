namespace HM27.Core;

/// <summary>Описание характеристики 1–20: ключ в базе, название, группа, откуда берётся.</summary>
public sealed record AttrDef(string Key, string Name, string Group, string Tip);

/// <summary>Список характеристик (ТЗ «HM27 — модель игрока», раздел «Характеристики 1–20»).</summary>
public static class Attrs
{
    public static readonly AttrDef[] Skater =
    {
        new("fin", "Завершение", "Атака", "Голы сверх ожидаемых (xG) на бросок; сильно сжато к уровню — реализация шумная"),
        new("pwr", "Мощь броска", "Атака", "Самый сильный бросок сезона (NHL EDGE)"),
        new("lng", "Бросок издали", "Атака", "Броски с 35+ футов (10+ м): точность, реализация, мощь броска"),
        new("net", "Игра у ворот", "Атака", "xG с близкой дистанции, добиваний и подставлений за 60 минут"),
        new("pas", "Передача", "Атака", "Первые передачи за 60 минут"),
        new("hnd", "Владение шайбой", "Атака", "Вклад в попытки бросков команды (RAPM 5 на 5)"),
        new("fo", "Вбрасывание", "Атака", "Процент выигранных вбрасываний"),
        new("dfn", "Позиционная оборона", "Оборона", "Вклад в xG соперника (RAPM 5 на 5): меньше — лучше"),
        new("tka", "Отбор", "Оборона", "Отборы за 60 минут (с поправкой на арену)"),
        new("blk", "Блокирование", "Оборона", "Блоки за 60 минут (с поправкой на арену)"),
        new("hit", "Силовая борьба", "Оборона", "Силовые приёмы за 60 минут (с поправкой на арену)"),
        new("pk", "Игра в меньшинстве", "Оборона", "xG соперника при игроке в меньшинстве 4 на 5"),
        new("spd", "Скорость", "Физика", "Максимальная скорость катания (NHL EDGE)"),
        new("acc", "Ускорение", "Физика", "Рывки быстрее 32 км/ч за матч (NHL EDGE)"),
        new("str", "Сила", "Физика", "Рост, вес и силовые приёмы"),
        new("sta", "Выносливость", "Физика", "Игровое время за матч"),
        new("dec", "Решения", "Мышление", "Вклад в xG (RAPM), отборы к потерям, выбор бросков"),
        new("vis", "Видение", "Мышление", "Вклад в xG своей команды (RAPM 5 на 5)"),
        new("dis", "Дисциплина", "Мышление", "Свои удаления за 60 минут: меньше — лучше"),
        new("wrk", "Работоспособность", "Мышление", "Блоки, отборы, пробег (NHL EDGE) и активность, когда команда проигрывает 3+"),
        new("tm", "Командная игра", "Мышление", "Польза звену сверх личных очков"),
        new("pp", "Игра в большинстве", "Мышление", "xG команды при игроке в большинстве 5 на 4"),
        new("cmp", "Хладнокровие", "Мышление", "Опыт больших матчей: плей-офф и НХЛ. Особого «клатча» в концовках и буллитах данные не подтверждают"),
        new("con", "Стабильность", "Мышление", "Ровность игры от матча к матчу сверх того, что даёт уровень: вклад каждый матч или рывками"),
    };

    public static readonly AttrDef[] Goalie =
    {
        new("rfx", "Реакция", "Вратарь", "Сейвы сверх ожидаемых на опасных бросках"),
        new("gpos", "Позиция", "Вратарь", "Сейвы сверх ожидаемых на средних и дальних бросках"),
        new("reb", "Контроль отскока", "Вратарь", "Добивания после сейва против средних"),
        new("scr", "Игра через заслон", "Вратарь", "Сейвы на подставлениях и рикошетах"),
        new("gsta", "Выносливость", "Вратарь", "Доля стартов и нагрузка"),
        new("gcmp", "Хладнокровие", "Вратарь", "Опыт больших матчей: плей-офф и НХЛ. Особой игры в концовках данные не подтверждают"),
        new("gcon", "Стабильность", "Вратарь", "Разброс сейвов сверх ожидаемых от матча к матчу"),
        new("stk", "Игра клюшкой", "Вратарь", "Нет надёжных данных — оценка"),
    };

    public static AttrDef[] For(PlayerData p) => p.IsGoalie ? Goalie : Skater;
    public static readonly string[] SkaterGroups = { "Атака", "Оборона", "Физика", "Мышление" };

    /// <summary>Группа возрастной кривой (data/age_curves.json): бросок, передачи, оборона, мышление, физика, вратари.</summary>
    public static string CurveGroup(string key) => key switch
    {
        "spd" or "acc" or "sta" or "str" => "phys",
        "fin" or "pwr" or "lng" or "net" => "shoot",
        "pas" or "hnd" or "vis" or "pp" => "pass",
        "dfn" or "tka" or "blk" or "hit" or "pk" => "def",
        "rfx" or "gpos" or "reb" or "scr" or "gsta" or "gcmp" or "gcon" or "stk" => "goalie",
        _ => "mind",
    };

    /// <summary>Группа возрастной кривой: сдвиг возраста (физика стареет раньше, мышление — позже).</summary>
    public static int AgeShift(string key) => key switch
    {
        "spd" or "acc" or "sta" or "str" => 2,
        "fin" or "pwr" or "lng" or "net" => 0,
        "pas" or "hnd" or "dfn" or "tka" or "blk" or "hit" or "pk" => -1,
        "dec" or "vis" or "dis" or "wrk" or "tm" or "pp" or "cmp" or "con" or "fo" => -3,
        _ => -2,   // вратари
    };
}

/// <summary>
/// Состояние игрока в карьере. Истинные значения видит только движок; игрок видит их через «знание» клуба с погрешностью.
/// </summary>
public sealed class PlayerState
{
    /// <summary>Истинный уровень 1–20.</summary>
    public float Lvl { get; set; }
    /// <summary>Уровень в начале сезона — для стрелок развития.</summary>
    public float Lvl0 { get; set; }
    /// <summary>Потенциал — потолок уровня (шкала 1–20, внутреннее).</summary>
    public float Pot { get; set; }
    /// <summary>Текущие способности (КА) 1–200 — истинные; движок и звёзды считаются от них.</summary>
    public float Ca { get; set; }
    /// <summary>КА в начале сезона — для стрелок развития.</summary>
    public float Ca0 { get; set; }
    /// <summary>Потенциальные способности (ПА) 1–200 — потолок КА при идеальных условиях. Дойти до него не обязательно.</summary>
    public float Pa { get; set; }
    /// <summary>Ожидаемый пик КА при обычных условиях (для скаутских отчётов и проверок).</summary>
    public float Peak { get; set; }
    /// <summary>«Удача развития» сезона: ×(0,4–1,7) к росту; разыгрывается заново каждый сезон.</summary>
    public float Luck { get; set; } = 1f;
    public Dictionary<string, float> A { get; set; } = new();
    public Dictionary<string, float> A0 { get; set; } = new();
    // Скрытые качества 1–20
    public int Prof { get; set; }
    public int Amb { get; set; }
    public int Loy { get; set; }
    public int Lead { get; set; }
    public int Temp { get; set; }
    public int Adapt { get; set; }
    public int Inj { get; set; }
    /// <summary>Знание клуба об игроке 0–1: чем больше, тем точнее видимые значения.</summary>
    public float Know { get; set; }
    /// <summary>Настоящая форма −1…+1: небольшая поправка к силе, меняется плавно.</summary>
    public float Form { get; set; }
    /// <summary>
    /// Форма сезона (ед. шкалы), пока только у вратарей: разыгрывается на весь сезон.
    /// Сезон вратаря в НХЛ примерно наполовину — талант, наполовину — то, что случилось именно в этом сезоне.
    /// </summary>
    public float Season { get; set; }
}

public static class PlayerModel
{
    /// <summary>Атака: личный xG × e^(LVL_K × (истинный уровень − уровень по данным)).</summary>
    public const double LVL_K = 0.12;
    /// <summary>Вратарь: GSAx на единицу уровня сверх данных.</summary>
    public const double G_LVL = 0.015;
    /// <summary>Защитник: «минут доверия» на единицу уровня сверх данных.</summary>
    public const double D_LVL = 1.3;
    /// <summary>Форма ±1 = ±0,35 единицы уровня.</summary>
    public const double FORM_U = 0.35;
    /// <summary>Пунктов КА (1–200) на единицу шкалы 1–20.</summary>
    public const double CA_PER_LVL = 6.0;
    /// <summary>
    /// Вратари: доля погрешности данных, с которой разыгрывается истинный талант (у полевых — 1).
    /// Талант вратаря по данным виден плохо (корреляция сезона со следующим ≈ 0,15–0,25 по MoneyPuck 2010–2026),
    /// но в игре звёзды остаются звёздами: разброс сезонов даёт форма сезона, а не пожизненная ошибка таланта.
    /// </summary>
    public const double G_DRAW = 0.5;
    /// <summary>
    /// Вратари: стандартное отклонение формы сезона, ед. шкалы. По MoneyPuck 2022–2026 «сезонная» часть разброса
    /// (сейвы сверх ожидаемого) примерно равна разбросу таланта: ≈ 0,04 доли ожидаемых голов = 2,5–2,7 ед. при G_LVL 0,015.
    /// Сверка: разброс % отражённых у вратарей с 40+ матчами (НХЛ 0,009–0,013) — проверка «Вратари: разброс сезона».
    /// </summary>
    public const double G_SEASON = 2.5;
    /// <summary>Вратари: «горячей руки» почти нет (отрезки по 10 матчей связаны на 0,07) — недельная форма слабее, чем у полевых.</summary>
    public const double G_FORM = 0.5;
    /// <summary>Видимая ошибка при нулевом знании: стандартное отклонение, ед. шкалы.</summary>
    public const double VIEW_ERR = 2.4;

    static double N(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
    static float C(double v, double lo = 1, double hi = 20) => (float)Math.Clamp(v, lo, hi);
    static int I(double v, int lo, int hi) => (int)Math.Round(Math.Clamp(v, lo, hi));

    /// <summary>
    /// Изменение характеристики группы за сезон по возрасту — из истории НХЛ 2010–2026 (data/history.py, метод разностей),
    /// если кривых в базе нет — априорная гипотеза со сдвигом возраста по группе.
    /// </summary>
    public static double Curve(GameData data, string group, double age)
    {
        var c = data?.Model?.Curves;
        if (c != null && c.TryGetValue(group, out var g) && g.Count > 0)
        {
            int a0 = (int)Math.Floor(age);
            double f = age - a0;
            double V(int a) => g.TryGetValue(Math.Clamp(a, 18, 40).ToString(), out var v) ? v : Curve(a);
            return V(a0) * (1 - f) + V(a0 + 1) * f;
        }
        int shift = group switch { "phys" => 2, "shoot" => 0, "pass" or "def" => -1, "goalie" => -2, _ => -3 };
        return Curve(age + shift);
    }

    /// <summary>Средняя кривая уровня: среднее по группам характеристик игрока.</summary>
    public static double LevelCurve(GameData data, PlayerData p, double age) =>
        Attrs.For(p).Average(d => Curve(data, Attrs.CurveGroup(d.Key), age));

    /// <summary>Априорная кривая (гипотеза до данных), ед. шкалы за сезон.</summary>
    public static double Curve(double age)
    {
        double[] ages = { 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 40 };
        double[] dv = { 1.4, 1.3, 1.1, 0.9, 0.7, 0.5, 0.3, 0.15, 0.05, 0, -0.05, -0.1, -0.2, -0.3, -0.4, -0.5, -0.6, -0.75, -0.9, -1.0, -1.2, -1.4 };
        if (age <= ages[0]) return dv[0];
        for (int i = 1; i < ages.Length; i++)
            if (age <= ages[i]) return dv[i - 1] + (dv[i] - dv[i - 1]) * (age - ages[i - 1]) / (ages[i] - ages[i - 1]);
        return dv[^1];
    }

    /// <summary>Рост, который ещё впереди в среднем: сумма положительной кривой от возраста до 30.</summary>
    public static double Remaining(GameData data, PlayerData p, int age)
    {
        double s = 0;
        for (int a = age; a < 32; a++) s += Math.Max(0, LevelCurve(data, p, a));
        return s;
    }

    /// <summary>
    /// Разыграть истинные значения, потенциал и характер для всех игроков (в начале карьеры).
    /// noise = false — без случайного сдвига (перенос старых сохранений: сила клубов не меняется).
    /// </summary>
    public static void Generate(Career c, bool noise = true)
    {
        foreach (var p in c.Data.Players)
            c.Players[p.Id] = Make(c, p, noise);
    }

    public static PlayerState Make(Career c, PlayerData p, bool noise = true)
    {
        var rng = new Random(unchecked(c.Seed * 7349 ^ (p.Id * 31 + 17)));
        var f = p.Fx ?? new CareerFacts();
        int age = p.Age(c.Today) ?? 26;
        double dl = noise ? N(rng) * p.LevelSe * 0.8 * (p.IsGoalie ? G_DRAW : 1) : 0;
        var s = new PlayerState { Lvl = C(p.Level + dl), Ca = (float)Math.Clamp(p.CaEst + dl * CA_PER_LVL, 30, 200) };
        foreach (var d in Attrs.For(p))
        {
            double v = p.AttrEst(d.Key) + (noise ? 0.7 * dl + N(rng) * p.AttrSe(d.Key) * 0.6 : 0);
            s.A[d.Key] = C(v);
        }
        // потенциал: средний рост для возраста, у сильных для своего возраста — больше; разброс тем шире, чем моложе
        // Ожидаемый пик: средний рост для возраста (кривые из данных) × талант (сильные для своих лет растут быстрее)
        // × запас до 200. ПА — потолок при идеальных условиях: выше ожидаемого пика, с разбросом вверх.
        double grow = ExpectedGrowth(c.Data, p, age, s.Ca);
        double sd = (age <= 20 ? 1.8 : age <= 23 ? 1.3 : age <= 25 ? 0.7 : 0.3) * CA_PER_LVL * Math.Max(Head(s.Ca), 0.3);
        s.Peak = (float)Math.Clamp(s.Ca + grow, 1, 200);
        double pa = s.Ca + grow * PA_OVER + 0.8 * Math.Abs(N(rng)) * sd + N(rng) * sd * 0.3;
        if (age <= 25 && rng.NextDouble() < 0.03) pa += 15 * Head(s.Ca);              // «поздно раскрывшийся»
        if (f.Pick is <= 10 && f.Gp < 200) pa += 2.5 * Head(s.Ca);
        s.Pa = age >= 28 ? s.Ca + 1 : (float)Math.Clamp(Math.Max(pa, s.Peak), 1, 200);
        s.Luck = (float)Math.Clamp(1 + 0.3 * N(rng), 0.4, 1.7);
        s.Pot = C(s.Lvl + (s.Pa - s.Ca) / CA_PER_LVL);
        // Характер реальных игроков: случайная часть и опыт ограничены «потолком без фактов» (лидерство и профессионализм — 15,
        // остальное — 16), поэтому сильные метки («Прирождённый лидер», «Профессионал», «Преданный клубу») дают только факты:
        // капитанство, награды и номинации за характер, серии без пропусков, продления со скидкой, просьбы об обмене (data/character.json).
        // Дальше характер меняется по ходу карьеры (Development.NewSeason): у новых поколений лидеры появляются из игры.
        bool Aw(string t) => f.Awards?.Contains(t) == true;
        double Cap(double v, double hi) => Math.Min(v, hi);
        double iron = f.Gp82 >= 0.99 ? 2.5 : f.Gp82 >= 0.97 ? 1.5 : 0;
        // долгая карьера на высоком уровне — сама по себе факт о профессионализме и амбициях: случайная часть уже, середина выше
        double longev = Math.Clamp(f.Seasons / 14.0, 0, 1) * Math.Clamp((p.Level - 11) / 5.0, 0, 1);
        bool winner = Aw("star") || Aw("big_games");
        s.Prof = I(Cap(12 + 2.2 * (1 - 0.6 * longev) * N(rng) + 2.5 * longev + (f.Seasons >= 10 ? 0.5 : 0) + (age >= 36 ? 1.5 : 0), 15)
                   + (Aw("pro") ? 3 : 0) + Math.Min(f.Masterton, 2) * 1.5 + Math.Min(f.Clancy, 2) * 0.5 + iron, 7, 20);
        s.Amb = I(Cap(12 + 2.5 * (winner ? 0.5 : 1) * N(rng) + (p.Level >= 16 ? 1 : 0) + (winner ? 1.5 : 0), 16), 7, 20);
        s.Loy = I(Cap(10.5 + 2.2 * N(rng) + Math.Min(f.ClubSeasons, 10) * 0.4 - Math.Max(f.Clubs - 1, 0) * 0.4, 16)
                  + (f.OneClub && f.Seasons >= 5 ? 2.5 : 0) + Math.Min(f.Discount, 2) * 3 - f.TradeRequests * 3, 4, 20);
        s.Lead = I(Cap(10 + 2.2 * N(rng) + Math.Min(f.Gp / 300.0, 3) + (age >= 28 && p.Level >= 15 ? 1.5 : 0), 15)
                   + (f.Cap == "C" ? 4 : f.Cap == "A" ? 1.5 : 0) + Math.Min(0.5 * f.CapSeasons, 4) + Math.Min(2.0 * f.Messier, 4)
                   + (Aw("leader") ? 1.5 : 0) + Math.Min(f.Clancy, 2) * 1.0, 7, 20);
        // самообладание: случайная часть не ниже 9 — «вспыльчивость» только по данным: частота и доля «эмоциональных» удалений
        // (грубость, неспортивное поведение, драки) за 5 сезонов; спокойные по данным и обладатели Леди Бинг — выше
        s.Temp = I(Math.Max(9, Cap(12.5 + 1.6 * N(rng), 16)) - 2.2 * Math.Max(0, f.Temper) + 1.2 * Math.Max(0, -f.Temper)
                   + Math.Max(Aw("fair") ? 3 : 0, Math.Min(2.0 * f.Byng, 4)), 4, 20);
        s.Adapt = I(Cap(11.5 + 2.3 * N(rng), 16) + Math.Min(Math.Max(f.Clubs - 1, 0), 3) * 0.6, 7, 20);
        double missRate = (f.MissLong + 3.0) / (f.Span + 60.0);
        s.Inj = I(13 + 1.5 * N(rng) - 60 * (missRate - 0.05) + iron * 0.8, 5, 19);
        // знание: свой клуб — с первого дня неплохо, чужие — по известности
        s.Know = p.Team == c.UserTeam ? 0.55f : (float)Math.Clamp(0.25 + 0.04 * f.Seasons + (p.Level >= 16 ? 0.15 : 0), 0.25, 0.75);
        s.Season = noise ? SeasonForm(c, p) : 0;
        s.Lvl0 = s.Lvl;
        s.Ca0 = s.Ca;
        s.A0 = new Dictionary<string, float>(s.A);
        return s;
    }

    /// <summary>ПА выше ожидаемого пика: ×PA_OVER к ожидаемому росту + разброс вверх.</summary>
    public const double PA_OVER = 1.15;

    /// <summary>Запас до потолка шкалы: у самых сильных расти некуда.</summary>
    public static double Head(double ca) => Math.Pow(Math.Clamp((200 - ca) / 80.0, 0.03, 1.0), 0.6);

    /// <summary>Талант для своих лет: сильные растут быстрее.</summary>
    public static double Talent(double ca) => Math.Clamp(0.7 + 0.015 * (ca - 110), 0.6, 1.6);

    /// <summary>Ожидаемый рост КА до пика при обычных условиях.</summary>
    public static double ExpectedGrowth(GameData data, PlayerData p, int age, double ca) =>
        Remaining(data, p, age) * CA_PER_LVL * Talent(ca) * Head(ca);

    /// <summary>Форма сезона: у вратарей — случайная на весь сезон (свой генератор: не сдвигает розыгрыш остального).</summary>
    public static float SeasonForm(Career c, PlayerData p) =>
        p.IsGoalie ? (float)Math.Clamp(N(new Random(unchecked(c.Seed * 40503 ^ p.Id * 13 ^ c.Today.Year * 7919))) * G_SEASON, -2.5 * G_SEASON, 2.5 * G_SEASON) : 0f;

    /// <summary>
    /// Хладнокровие в плей-офф, ед. шкалы на единицу хладнокровия сверх 11. Подбор зеркальной проверкой (HM27.Check):
    /// индивидуального «клатча» в данных не видно (половинки матчей r ≈ 0,02 ± 0,05 на 4 сезонах), а по всем игрокам сразу
    /// при равном уровне опытные в плей-офф держатся лучше (+5% очков на единицу, 90%: 2–8%). 0,3 — индивидуальный тест
    /// на тех же 4 сезонах игры остаётся в пределах шума реального, общий эффект в игре положительный, но мягче реального.
    /// </summary>
    public const double CMP_K = 0.3;

    public static double Composure(Career c, PlayerData p)
    {
        var s = State(c, p.Id);
        if (s == null) return 0;
        double v = s.A.GetValueOrDefault(p.IsGoalie ? "gcmp" : "cmp", 11f);
        return CMP_K * (v - 11);
    }

    public static PlayerState State(Career c, int id) => c.Players.TryGetValue(id, out var s) ? s : null;

    /// <summary>
    /// Сдвиг силы для движка (ед. шкалы): истинный уровень − уровень по данным + форма (+ форма сезона у вратарей).
    /// form = false — без формы: база калибровки клуба (форма — отклонение от ожидания, а не часть силы клуба).
    /// </summary>
    public static double Delta(Career c, PlayerData p, bool form = true)
    {
        var s = State(c, p.Id);
        if (s == null) return 0;
        double d = (s.Ca - p.CaEst) / CA_PER_LVL;
        if (form) d += FORM_U * s.Form * (p.IsGoalie ? G_FORM : 1) + s.Season;
        return d;
    }

    static int KeyIndex(string key)
    {
        int h = 17;
        foreach (var ch in key) h = unchecked(h * 31 + ch);
        return h;
    }

    /// <summary>Ошибка взгляда клуба на характеристику (детерминированно для карьеры), в долях VIEW_ERR.</summary>
    static double ViewErr(Career c, int id, string key) => N(new Random(unchecked(c.Seed * 92821 ^ id * 131 ^ KeyIndex(key))));

    /// <summary>Видимое значение: истинное + ошибка × (1 − знание).</summary>
    public static double Visible(Career c, PlayerData p, string key)
    {
        var s = State(c, p.Id);
        if (s == null) return key == "lvl" ? p.Level : key == "ca" ? p.CaEst : p.AttrEst(key);
        if (key == "ca") return Math.Clamp(s.Ca + ViewErr(c, p.Id, key) * VIEW_ERR * CA_PER_LVL * (1 - s.Know), 1, 200);
        double t = key == "lvl" ? s.Lvl : s.A.GetValueOrDefault(key, s.Lvl);
        return Math.Clamp(t + ViewErr(c, p.Id, key) * VIEW_ERR * (1 - s.Know), 1, 20);
    }

    /// <summary>Диапазон, который показывает скаут: [мин, макс] (при полном знании — одно число).</summary>
    public static (int lo, int hi) Range(Career c, PlayerData p, string key)
    {
        var s = State(c, p.Id);
        double v = Visible(c, p, key);
        double w = s == null ? 0 : 2.4 * (1 - s.Know);
        int lo = (int)Math.Round(Math.Clamp(v - w, 1, 20)), hi = (int)Math.Round(Math.Clamp(v + w, 1, 20));
        return (lo, hi);
    }

    /// <summary>Потенциал глазами скаута: звёзды (0,5–5) с разбросом.</summary>
    public static (double lo, double hi) PotentialStars(Career c, PlayerData p)
    {
        var s = State(c, p.Id);
        if (s == null) return (Stars(p.CaEst), Stars(p.CaEst));
        double v = s.Pa + ViewErr(c, p.Id, "pot") * 1.5 * CA_PER_LVL * (1 - s.Know);
        double w = (2.2 * (1 - s.Know) + 0.3) * CA_PER_LVL;
        return (Stars(Math.Max(v - w, Visible(c, p, "ca"))), Stars(Math.Max(v + w, Visible(c, p, "ca"))));
    }

    /// <summary>Звёзды из КА/ПА (1–200): 190 → 5★, 160 → 4★, 140 → 3★, 120 → 2★, 100 → 1★.</summary>
    public static double Stars(double ca) => Math.Clamp(Math.Round((ca - 80) / 20 * 2, MidpointRounding.AwayFromZero) / 2, 0.5, 5);

    /// <summary>Значение для Ui.Stars (там звёзды = v / 20).</summary>
    public static int StarsOvr(double ca) => (int)Math.Round(Stars(ca) * 20);

    /// <summary>Описание личности, как в FM. Только нейтральные и позитивные формулировки.</summary>
    public static string Personality(PlayerState s)
    {
        if (s == null) return "Нет данных";
        // сильные метки: на старте у реальных игроков — только по фактам (случайная часть ограничена, см. Make)
        if (s.Prof >= 17 && s.Amb >= 15) return "Образцовый профессионал";
        if (s.Lead >= 17) return "Прирождённый лидер";
        if (s.Prof >= 16) return "Профессионал";
        if (s.Amb >= 17) return "Честолюбивый";
        if (s.Loy >= 17) return "Преданный клубу";
        if (s.Temp >= 17) return "Уравновешенный";
        if (s.Adapt >= 17) return "Легко адаптируется";
        if (s.Temp <= 8) return "Эмоциональный";
        // мягкие метки: могут выпасть случайно, у каждой — небольшой эффект (см. PersonalityInfo)
        if (s.Prof >= 15) return "Трудолюбивый";
        if (s.Amb >= 15) return "Целеустремлённый";
        if (s.Lead >= 14 && s.Temp >= 12) return "Решительный";
        if (s.Temp >= 15) return "Спокойный";
        if (s.Adapt >= 15) return "Свой в раздевалке";
        if (s.Prof <= 9) return "Расслабленный";
        if (s.Amb <= 9) return "Скромные амбиции";
        return "Сбалансированный";
    }

    /// <summary>Что метка значит в игре (подсказка в карточке).</summary>
    public static string PersonalityInfo(string label) => label switch
    {
        "Образцовый профессионал" => "Быстрее растёт и дольше держит форму, хороший пример для молодых",
        "Прирождённый лидер" => "Ведёт за собой: молодые рядом с ним растут профессиональнее, в раздевалке спокойнее",
        "Профессионал" => "Быстрее растёт и медленнее стареет",
        "Честолюбивый" => "В удачный сезон растёт заметно быстрее",
        "Преданный клубу" => "Держится за клуб, легче договориться о продлении",
        "Уравновешенный" => "Форма колеблется мало, редко срывается",
        "Легко адаптируется" => "Быстро привыкает к новому клубу",
        "Эмоциональный" => "Форма скачет сильнее, чаще удаляется",
        "Трудолюбивый" => "Растёт чуть быстрее, стареет чуть медленнее",
        "Целеустремлённый" => "В удачный сезон растёт чуть быстрее",
        "Решительный" => "Задаёт тон в своём звене",
        "Спокойный" => "Форма колеблется меньше",
        "Свой в раздевалке" => "Быстрее привыкает после обмена",
        "Расслабленный" => "Растёт медленнее и раньше сдаёт",
        "Скромные амбиции" => "Удачный сезон почти не ускоряет его рост",
        _ => "Без ярких черт",
    };

    /// <summary>Описание качества 1–20 словами (для скрытых качеств: цифр игрок не видит).</summary>
    public static string Word(int v) => v >= 17 ? "очень высокий" : v >= 14 ? "высокий" : v >= 10 ? "средний" : "невысокий";
}

/// <summary>Развитие и старение: раз в неделю, кривые по группам характеристик, потенциал, игровое время, профессионализм.</summary>
public static class Development
{
    public const double WEEKS = 26;

    /// <summary>
    /// Неделя развития. Рост не тянется к ПА сам: он зависит от условий —
    /// возраст (кривые из данных), талант, профессионализм и амбиции, игровое время и роль, «удача сезона»,
    /// оценки за игру (небольшой бонус за хорошую игру), травмы (пока травмирован — не растёт). ПА — только потолок.
    /// </summary>
    public static void Week(Career c)
    {
        var inj = c.Injured.ToHashSet();
        var teamGp = c.Table.ToDictionary(kv => kv.Key, kv => kv.Value.Gp);
        foreach (var p in c.Data.Players)
        {
            if (!c.Players.TryGetValue(p.Id, out var s)) continue;
            var rng = new Random(unchecked(c.Seed * 15485863 ^ p.Id * 7 ^ c.Today.DayNumber));
            int age = p.Age(c.Today) ?? 26;
            double cond = Conditions(c, p, s, teamGp);
            double talent = PlayerModel.Talent(s.Ca) * PlayerModel.Head(s.Ca);
            double nearPa = Math.Clamp((s.Pa - s.Ca) / 4.0, 0, 1);        // у самого потолка рост затухает
            bool hurt = inj.Contains(p.Id);
            double sumD = 0, sumCa = 0; int n = 0;
            foreach (var d in Attrs.For(p))
            {
                double cv = PlayerModel.Curve(c.Data, Attrs.CurveGroup(d.Key), age);
                double g = hurt ? 0 : Math.Max(cv, 0) * talent * cond * nearPa;
                double dec = Math.Min(cv, 0) * (1.15 - 0.03 * (s.Prof - 10));
                sumCa += (g + dec) / WEEKS;
                double dv = (g + dec) / WEEKS + 0.04 * N(rng);
                float old = s.A.GetValueOrDefault(d.Key, s.Lvl);
                float nv = (float)Math.Clamp(old + dv, 1, 20);
                s.A[d.Key] = nv;
                sumD += nv - old; n++;
            }
            if (n > 0)
            {
                s.Lvl = (float)Math.Clamp(s.Lvl + sumD / n, 1, 20);
                // КА растёт по кривым без «упора» в 20 у характеристик — у элиты рост виден так же, как у остальных
                s.Ca = (float)Math.Clamp(s.Ca + sumCa / n * PlayerModel.CA_PER_LVL + 0.15 * N(rng), 1, Math.Max(s.Pa, s.Ca));
                s.Pot = (float)Math.Clamp(s.Lvl + (s.Pa - s.Ca) / PlayerModel.CA_PER_LVL, 1, 20);
            }
            // форма: плавное случайное блуждание, у стабильных — тише
            double con = s.A.GetValueOrDefault(p.IsGoalie ? "gcon" : "con", 11);
            // спокойные (самообладание) — тише, эмоциональные — громче
            s.Form = (float)Math.Clamp(0.85 * s.Form + 0.25 * (1.3 - 0.03 * (con - 10)) * (1 - 0.025 * (s.Temp - 12)) * N(rng), -1, 1);
            // свой клуб узнаёт игроков за несколько недель
            if (p.Team == c.UserTeam) s.Know = Math.Min(1f, s.Know + 0.1f);
        }
    }

    /// <summary>Условия развития (≈1 у обычного игрока основы): характер × игровое время и роль × удача сезона × игра.</summary>
    public static double Conditions(Career c, PlayerData p, PlayerState s, Dictionary<string, int> teamGp = null)
    {
        double character = 1 + 0.05 * (s.Prof - 12) + 0.02 * (s.Amb - 12);
        int tgp = teamGp?.GetValueOrDefault(p.Team) ?? c.Table.GetValueOrDefault(p.Team)?.Gp ?? 0;
        double share; double role; double rating = 0; int gp;
        if (p.IsGoalie)
        {
            var gl = c.Goalies.GetValueOrDefault(p.Id); gp = gl?.Gp ?? 0;
            share = tgp == 0 ? 0.6 : Math.Min(1, gp / (double)tgp);
            role = c.Lineups.TryGetValue(p.Team, out var lg) && lg.G1 == p.Id ? 1.1 : 0.9;
            rating = gl?.AvgRating ?? 0;
        }
        else
        {
            var sl = c.Skaters.GetValueOrDefault(p.Id); gp = sl?.Gp ?? 0;
            share = tgp == 0 ? 0.8 : Math.Min(1, gp / (double)tgp);
            int unit = c.Lineups.TryGetValue(p.Team, out var l) ? l.UnitOf(p.Id) : -1;
            bool pp = l != null && (l.Pp1.Contains(p.Id) || l.Pp2.Contains(p.Id));
            role = unit switch { 0 or 10 => 1.15, 1 or 11 => 1.05, 2 or 12 => 0.95, 3 => 0.85, _ => 0.6 } + (pp ? 0.05 : 0);
            rating = sl?.AvgRating ?? 0;
        }
        // сидящий в запасе почти не растёт; основа на первых ролях — быстрее
        double minutes = (0.35 + 0.65 * share) * role;
        // хорошая игра — небольшой бонус (±15%), только при достаточном числе матчей
        // честолюбивые сильнее «разгоняются» от удачного сезона, со скромными амбициями — слабее
        double pf = Math.Clamp((rating - 6.9) / 0.4, -1, 1);
        double perf = gp >= 10 && rating > 0 ? 1 + 0.15 * pf * (pf > 0 ? 1 + 0.05 * (s.Amb - 12) : 1) : 1;
        return Math.Max(0, character) * minutes * s.Luck * perf;
    }

    /// <summary>Новый сезон (межсезонье): заново разыгрывается «удача развития». Вызывается при переходе к следующему сезону.</summary>
    public static void NewSeason(Career c)
    {
        foreach (var (id, s) in c.Players)
        {
            var rng = new Random(unchecked(c.Seed * 31337 ^ id ^ c.Today.Year));
            s.Luck = (float)Math.Clamp(1 + 0.3 * N(rng), 0.4, 1.7);
            if (c.Data.PlayerById.TryGetValue(id, out var p)) { s.Season = PlayerModel.SeasonForm(c, p); Character(c, p, s, rng); }
            s.Ca0 = s.Ca; s.Lvl0 = s.Lvl; s.A0 = new Dictionary<string, float>(s.A);
        }
    }

    /// <summary>
    /// Характер меняется с карьерой: лидерство растёт с опытом, ролью и капитанством; молодые перенимают профессионализм
    /// у ветеранов-лидеров своего клуба; с возрастом спокойнее. Так через несколько сезонов появляются новые лидеры —
    /// уже не из фактов, а из того, что произошло в игре.
    /// </summary>
    static void Character(Career c, PlayerData p, PlayerState s, Random rng)
    {
        int age = p.Age(c.Today) ?? 26;
        bool Bump(double prob) => rng.NextDouble() < prob;
        int unit = c.Lineups.TryGetValue(p.Team ?? "", out var l) ? l.UnitOf(p.Id) : -1;
        bool core = unit is 0 or 1 or 10 or 11 || (p.IsGoalie && l != null && l.G1 == p.Id);
        bool captain = p.Fx?.Cap == "C" && p.Team != null;
        if (age >= 23 && s.Lead < 20)
            if (Bump((age >= 27 ? 0.18 : 0.08) + (core ? 0.12 : 0) + (captain ? 0.25 : 0) + (s.Prof >= 15 ? 0.05 : 0))) s.Lead++;
        if (age <= 24 && l != null)
        {
            // наставник: самый профессиональный ветеран-лидер клуба (28+ лет, лидерство 14+)
            var mentor = c.Data.Roster(p.Team).Where(x => x.Id != p.Id && (x.Age(c.Today) ?? 0) >= 28 && c.Players.TryGetValue(x.Id, out var ms) && ms.Lead >= 14)
                .Select(x => c.Players[x.Id].Prof).DefaultIfEmpty(0).Max();
            if (mentor > s.Prof && Bump(Math.Min(0.5, 0.08 * (mentor - s.Prof)))) s.Prof++;
        }
        if (age >= 27 && s.Temp < 16 && Bump(0.12)) s.Temp++;
        if (age >= 33 && s.Amb > 8 && Bump(0.12)) s.Amb--;
    }

    /// <summary>Тяжёлая травма (2+ месяца): с шансом 35% навсегда срезает скорость, ускорение и немного КА.</summary>
    public static void SeriousInjury(Career c, PlayerData p, int days, Random rng)
    {
        if (days < 60 || !c.Players.TryGetValue(p.Id, out var s) || rng.NextDouble() >= 0.35) return;
        float hit = (float)(2 + 3 * rng.NextDouble());
        s.Ca = Math.Max(1, s.Ca - hit);
        s.Pa = Math.Max(s.Ca, s.Pa - hit);
        foreach (var k in new[] { "spd", "acc" })
            if (s.A.ContainsKey(k)) s.A[k] = (float)Math.Max(1, s.A[k] - (0.5 + rng.NextDouble()));
    }

    static double N(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
}

/// <summary>Роли игрока: оценка пригодности из характеристик (как в FM). Ключевые — вес ≥ 0,2 (подсветка в карточке).</summary>
public static class Roles
{
    public sealed record Def(string Name, string Pos, (string key, double w)[] Keys);

    public static readonly Def[] All =
    {
        new("Снайпер", "F", new[] { ("fin", 0.45), ("pwr", 0.2), ("lng", 0.15), ("acc", 0.1), ("dec", 0.1) }),
        new("Игрок у ворот", "F", new[] { ("net", 0.4), ("str", 0.2), ("fin", 0.2), ("hit", 0.1), ("wrk", 0.1) }),
        new("Плеймейкер", "F", new[] { ("pas", 0.35), ("vis", 0.3), ("hnd", 0.15), ("dec", 0.1), ("tm", 0.1) }),
        new("Двусторонний форвард", "F", new[] { ("dfn", 0.3), ("vis", 0.15), ("hnd", 0.15), ("tka", 0.15), ("wrk", 0.15), ("dec", 0.1) }),
        new("Силовой форвард", "F", new[] { ("str", 0.3), ("hit", 0.3), ("wrk", 0.2), ("net", 0.1), ("fin", 0.1) }),
        new("Скоростной форвард", "F", new[] { ("spd", 0.35), ("acc", 0.25), ("hnd", 0.2), ("fin", 0.1), ("dec", 0.1) }),
        new("Центр-распасовщик", "C", new[] { ("pas", 0.3), ("fo", 0.25), ("vis", 0.25), ("dec", 0.2) }),
        new("Оборонительный центр", "C", new[] { ("fo", 0.3), ("dfn", 0.3), ("pk", 0.2), ("tka", 0.1), ("dis", 0.1) }),
        new("Атакующий защитник", "D", new[] { ("pas", 0.3), ("vis", 0.25), ("pwr", 0.15), ("pp", 0.15), ("hnd", 0.15) }),
        new("Защитник-домосед", "D", new[] { ("dfn", 0.35), ("blk", 0.25), ("pk", 0.2), ("hit", 0.1), ("dis", 0.1) }),
        new("Универсальный защитник", "D", new[] { ("dfn", 0.25), ("pas", 0.2), ("hnd", 0.2), ("sta", 0.2), ("dec", 0.15) }),
        new("Силовой защитник", "D", new[] { ("hit", 0.35), ("str", 0.35), ("blk", 0.15), ("dfn", 0.15) }),
        // широкий профиль: без ярко выраженных ключевых характеристик — для игроков без явной специализации
        new("Нападающий широкого профиля", "F", new[] { ("fin", 0.15), ("pas", 0.15), ("vis", 0.15), ("dfn", 0.15), ("hnd", 0.15), ("wrk", 0.15), ("spd", 0.1) }),
        new("Защитник широкого профиля", "D", new[] { ("dfn", 0.15), ("pas", 0.15), ("hnd", 0.15), ("blk", 0.15), ("sta", 0.15), ("dec", 0.15), ("pk", 0.1) }),
        new("Вратарь-реакция", "G", new[] { ("rfx", 0.5), ("scr", 0.25), ("gcon", 0.25) }),
        new("Позиционный вратарь", "G", new[] { ("gpos", 0.45), ("reb", 0.2), ("gcon", 0.2), ("rfx", 0.15) }),
        new("Вратарь широкого профиля", "G", new[] { ("rfx", 0.17), ("gpos", 0.17), ("reb", 0.17), ("scr", 0.17), ("gcon", 0.16), ("gsta", 0.16) }),
    };

    static bool Fits(Def d, PlayerData p) => d.Pos switch
    {
        "G" => p.IsGoalie,
        "D" => p.IsDefense,
        "C" => p.Pos == "C",
        _ => p.IsForward,
    };

    /// <summary>Оценка роли 1–20 по характеристикам (из данных или видимым — через get).</summary>
    public static double Score(Def d, Func<string, double> get, double median = double.NaN)
    {
        double s = d.Keys.Sum(k => k.w * get(k.key)) / d.Keys.Sum(k => k.w);
        // роль не держится на слабом месте: штраф, если ключевая характеристика заметно ниже типичной для самого игрока
        if (!double.IsNaN(median))
            foreach (var (key, w) in d.Keys)
                if (w >= 0.2) s -= 0.7 * Math.Max(0, median - 2.5 - get(key));
        return s;
    }

    /// <summary>Медиана характеристик игрока — «его типичный уровень» для проверки ролей.</summary>
    public static double Median(PlayerData p, Func<string, double> get)
    {
        var v = Attrs.For(p).Where(a => a.Key != "stk").Select(a => get(a.Key)).OrderBy(x => x).ToList();
        return v.Count == 0 ? p.Level : v[v.Count / 2];
    }

    /// <summary>Противоречие: ключевая характеристика лучшей роли ниже типичной для игрока больше чем на 3.</summary>
    public static string Contradiction(PlayerData p)
    {
        var best = p.Roles.FirstOrDefault();
        if (best == null) return null;
        double med = Median(p, p.AttrEst);
        foreach (var k in Key(best.Name))
            if (p.AttrEst(k) < med - 3) return $"{best.Name}: {k} {p.AttrEst(k):F0} при типичных {med:F0}";
        return null;
    }

    /// <summary>
    /// Особенности стиля, подтверждающие роль: роль — это то, как игрок играет, а особенности из данных это и показывают.
    /// Без них характеристики, «раздутые» сильными партнёрами (видение рядом с Макдэвидом), уводили бы игрока пятака в плеймейкеры.
    /// </summary>
    static readonly Dictionary<string, string[]> TraitFit = new()
    {
        ["Снайпер"] = new[] { "shoot", "far", "slap" }, ["Игрок у ворот"] = new[] { "net", "tip", "reb" },
        ["Плеймейкер"] = new[] { "pass", "risk" }, ["Двусторонний форвард"] = new[] { "tk", "blk" },
        ["Силовой форвард"] = new[] { "hit", "fight" }, ["Скоростной форвард"] = new[] { "rush" },
        ["Центр-распасовщик"] = new[] { "pass" }, ["Оборонительный центр"] = new[] { "blk", "tk" },
        ["Атакующий защитник"] = new[] { "jump", "slap", "shoot" }, ["Защитник-домосед"] = new[] { "blk" },
        ["Силовой защитник"] = new[] { "hit", "fight" },
    };
    public const double TRAIT_ROLE = 1.2;

    public static List<RoleFit> For(PlayerData p, Func<string, double> get = null)
    {
        get ??= p.AttrEst;
        double med = Median(p, get);
        double Tb(Def d) => TraitFit.TryGetValue(d.Name, out var ts) && p.Tr != null ? TRAIT_ROLE * ts.Count(p.Tr.Contains) : 0;
        // сначала роли без слабого места среди ключевых, внутри — по оценке (с поправкой на особенности стиля)
        return All.Where(d => Fits(d, p))
                  .Select(d => (fit: new RoleFit { Name = d.Name, V = (int)Math.Clamp(Math.Round((Score(d, get, med) + Tb(d)) * 5), 1, 99) },
                                ok: d.Keys.Where(k => k.w >= 0.2).All(k => get(k.key) >= med - 3)))
                  .OrderByDescending(x => x.ok).ThenByDescending(x => x.fit.V).Select(x => x.fit).ToList();
    }

    public static Def Get(string name) => All.FirstOrDefault(d => d.Name == name);

    /// <summary>Ключевые характеристики роли (вес ≥ 0,2) — подсвечиваются в карточке.</summary>
    public static string[] Key(string name) => Get(name)?.Keys.Where(k => k.w >= 0.2).Select(k => k.key).ToArray() ?? Array.Empty<string>();
}
