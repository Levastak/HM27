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
        new("lng", "Дальний бросок", "Атака", "Точность бросков с 35+ футов (доля в створ)"),
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
        new("wrk", "Работоспособность", "Мышление", "Блоки, отборы, силовые и пробег (NHL EDGE)"),
        new("tm", "Командная игра", "Мышление", "Польза звену сверх личных очков"),
        new("pp", "Игра в большинстве", "Мышление", "xG команды при игроке в большинстве 5 на 4"),
        new("cmp", "Хладнокровие", "Мышление", "Реализация в концовках при разнице в одну шайбу; данных мало — узкий диапазон"),
        new("con", "Стабильность", "Мышление", "Разброс игровой оценки от матча к матчу"),
    };

    public static readonly AttrDef[] Goalie =
    {
        new("rfx", "Реакция", "Вратарь", "Сейвы сверх ожидаемых на опасных бросках"),
        new("gpos", "Позиция", "Вратарь", "Сейвы сверх ожидаемых на средних и дальних бросках"),
        new("reb", "Контроль отскока", "Вратарь", "Добивания после сейва против средних"),
        new("scr", "Игра через заслон", "Вратарь", "Сейвы на подставлениях и рикошетах"),
        new("gsta", "Выносливость", "Вратарь", "Доля стартов и нагрузка"),
        new("gcmp", "Хладнокровие", "Вратарь", "Концовки при разнице в одну шайбу; данных мало — узкий диапазон"),
        new("gcon", "Стабильность", "Вратарь", "Разброс сейвов сверх ожидаемых от матча к матчу"),
        new("stk", "Игра клюшкой", "Вратарь", "Нет надёжных данных — оценка"),
    };

    public static AttrDef[] For(PlayerData p) => p.IsGoalie ? Goalie : Skater;
    public static readonly string[] SkaterGroups = { "Атака", "Оборона", "Физика", "Мышление" };

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
    /// <summary>Потенциал — потолок уровня.</summary>
    public float Pot { get; set; }
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
    /// <summary>Видимая ошибка при нулевом знании: стандартное отклонение, ед. шкалы.</summary>
    public const double VIEW_ERR = 2.4;

    static double N(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
    static float C(double v, double lo = 1, double hi = 20) => (float)Math.Clamp(v, lo, hi);
    static int I(double v, int lo, int hi) => (int)Math.Round(Math.Clamp(v, lo, hi));

    /// <summary>Ожидаемое изменение уровня за сезон по возрасту (ед. шкалы). Начальная гипотеза, уточняется по данным.</summary>
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
    static double Remaining(int age)
    {
        double s = 0;
        for (int a = age; a < 30; a++) s += Math.Max(0, Curve(a));
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
        double dl = noise ? N(rng) * p.LevelSe * 0.8 : 0;
        var s = new PlayerState { Lvl = C(p.Level + dl) };
        foreach (var d in Attrs.For(p))
        {
            double v = p.AttrEst(d.Key) + (noise ? 0.7 * dl + N(rng) * p.AttrSe(d.Key) * 0.6 : 0);
            s.A[d.Key] = C(v);
        }
        // потенциал: средний рост для возраста, у сильных для своего возраста — больше; разброс тем шире, чем моложе
        double rem = Remaining(age) * Math.Clamp(0.6 + 0.08 * (s.Lvl - 10), 0.6, 1.4);
        double sd = age <= 20 ? 1.8 : age <= 23 ? 1.3 : age <= 25 ? 0.7 : 0.3;
        double pot = s.Lvl + rem + N(rng) * sd;
        if (age <= 25 && rng.NextDouble() < 0.03) pot += 2.5;                       // «поздно раскрывшийся»
        if (f.Pick is <= 10 && f.Gp < 200) pot += 0.4;
        s.Pot = age >= 27 ? s.Lvl + 0.2f : C(Math.Max(pot, s.Lvl));
        // характер: из фактов карьеры, недостающее — мягкая генерация (у реальных игроков не ниже 7)
        bool Aw(string t) => f.Awards?.Contains(t) == true;
        s.Prof = I(12 + 2.2 * N(rng) + (f.Seasons >= 10 ? 1 : 0) + (age >= 33 && p.Level >= 12 ? 1.5 : 0) + (Aw("pro") ? 3 : 0), 7, 20);
        s.Amb = I(12 + 2.5 * N(rng) + (p.Level >= 16 ? 1 : 0), 7, 20);
        s.Loy = I(10.5 + 2.2 * N(rng) + Math.Min(f.ClubSeasons, 10) * 0.4 - Math.Max(f.Clubs - 1, 0) * 0.4, 7, 20);
        s.Lead = I(10 + 2.2 * N(rng) + Math.Min(f.Gp / 300.0, 3) + (Aw("leader") ? 4 : 0) + (age >= 28 && p.Level >= 15 ? 1.5 : 0), 7, 20);
        s.Temp = I(12.5 + 1.8 * N(rng) - Math.Min(f.Fights, 8) * 0.6 + (Aw("fair") ? 3 : 0), 6, 20);
        s.Adapt = I(11.5 + 2.3 * N(rng) + Math.Min(Math.Max(f.Clubs - 1, 0), 3) * 0.6, 7, 20);
        double missRate = (f.MissLong + 3.0) / (f.Span + 60.0);
        s.Inj = I(13 + 1.5 * N(rng) - 60 * (missRate - 0.05), 5, 19);
        // знание: свой клуб — с первого дня неплохо, чужие — по известности
        s.Know = p.Team == c.UserTeam ? 0.55f : (float)Math.Clamp(0.25 + 0.04 * f.Seasons + (p.Level >= 16 ? 0.15 : 0), 0.25, 0.75);
        s.Lvl0 = s.Lvl;
        s.A0 = new Dictionary<string, float>(s.A);
        return s;
    }

    public static PlayerState State(Career c, int id) => c.Players.TryGetValue(id, out var s) ? s : null;

    /// <summary>Сдвиг силы для движка: истинный уровень − уровень по данным + форма (ед. шкалы).</summary>
    public static double Delta(Career c, PlayerData p)
    {
        var s = State(c, p.Id);
        return s == null ? 0 : s.Lvl - p.Level + FORM_U * s.Form;
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
        if (s == null) return key == "lvl" ? p.Level : p.AttrEst(key);
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
        if (s == null) return (p.Level / 4, p.Level / 4);
        double v = s.Pot + ViewErr(c, p.Id, "pot") * 1.5 * (1 - s.Know);
        double w = 2.2 * (1 - s.Know) + 0.3;
        static double St(double x) => Math.Clamp(Math.Round(x / 4 * 2) / 2, 0.5, 5);
        return (St(Math.Max(v - w, s.Lvl)), St(v + w));
    }

    /// <summary>Описание личности, как в FM. Только нейтральные и позитивные формулировки.</summary>
    public static string Personality(PlayerState s)
    {
        if (s == null) return "Нет данных";
        if (s.Prof >= 17 && s.Amb >= 15) return "Образцовый профессионал";
        if (s.Lead >= 17) return "Прирождённый лидер";
        if (s.Prof >= 16) return "Профессионал";
        if (s.Amb >= 17) return "Честолюбивый";
        if (s.Loy >= 17) return "Преданный клубу";
        if (s.Temp >= 17) return "Уравновешенный";
        if (s.Adapt >= 17) return "Легко адаптируется";
        if (s.Temp <= 8) return "Эмоциональный";
        return "Сбалансированный";
    }

    /// <summary>Описание качества 1–20 словами (для скрытых качеств: цифр игрок не видит).</summary>
    public static string Word(int v) => v >= 17 ? "очень высокий" : v >= 14 ? "высокий" : v >= 10 ? "средний" : "невысокий";
}

/// <summary>Развитие и старение: раз в неделю, кривые по группам характеристик, потенциал, игровое время, профессионализм.</summary>
public static class Development
{
    public const double WEEKS = 26;

    public static void Week(Career c)
    {
        var inj = c.Injured.ToHashSet();
        var teamGp = c.Table.ToDictionary(kv => kv.Key, kv => kv.Value.Gp);
        foreach (var p in c.Data.Players)
        {
            if (!c.Players.TryGetValue(p.Id, out var s)) continue;
            var rng = new Random(unchecked(c.Seed * 15485863 ^ p.Id * 7 ^ c.Today.DayNumber));
            int age = p.Age(c.Today) ?? 26;
            // игровое время: доля сыгранных матчей клуба в этом сезоне
            int tgp = teamGp.GetValueOrDefault(p.Team);
            int gp = p.IsGoalie ? (c.Goalies.TryGetValue(p.Id, out var gl) ? gl.Gp : 0) : (c.Skaters.TryGetValue(p.Id, out var sl) ? sl.Gp : 0);
            double share = tgp == 0 ? 0.7 : Math.Min(1, gp / (double)tgp);
            double mins = p.IsGoalie ? 0.7 + 0.5 * share : 0.6 + 0.6 * share;
            double room = Math.Clamp((s.Pot - s.Lvl) / 1.5, 0, 1.3);
            double sumD = 0; int n = 0;
            foreach (var d in Attrs.For(p))
            {
                double cv = PlayerModel.Curve(age + Attrs.AgeShift(d.Key));
                double g = Math.Max(cv, 0) * room * (0.7 + 0.06 * (s.Prof - 10)) * mins;
                if (inj.Contains(p.Id)) g = 0;
                double dec = Math.Min(cv, 0) * (1.15 - 0.03 * (s.Prof - 10));
                double dv = (g + dec) / WEEKS + 0.04 * N(rng);
                float old = s.A.GetValueOrDefault(d.Key, s.Lvl);
                float nv = (float)Math.Clamp(old + dv, 1, 20);
                s.A[d.Key] = nv;
                sumD += nv - old; n++;
            }
            if (n > 0) s.Lvl = (float)Math.Clamp(s.Lvl + sumD / n, 1, Math.Max(s.Pot + 0.3, s.Lvl));
            // форма: плавное случайное блуждание, у стабильных — тише
            double con = s.A.GetValueOrDefault(p.IsGoalie ? "gcon" : "con", 11);
            s.Form = (float)Math.Clamp(0.85 * s.Form + 0.25 * (1.3 - 0.03 * (con - 10)) * N(rng), -1, 1);
            // свой клуб узнаёт игроков за несколько недель
            if (p.Team == c.UserTeam) s.Know = Math.Min(1f, s.Know + 0.1f);
        }
    }

    static double N(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
}
