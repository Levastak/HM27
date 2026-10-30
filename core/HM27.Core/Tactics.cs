namespace HM27.Core;

/// <summary>
/// Система игры клуба (этап «Тренер»): девять рычагов в трёх фазах, у каждого — два края и середина.
///   Без шайбы: форчек (1-2-2 / 2-1-2 / 2-3), жёсткость (чисто / обычно / жёстко), своя зона (зонная / смешанная / персональная).
///   С шайбой: выход из зоны (по борту / по ситуации / длинный пас), вход в зону, защитники, броски.
///   Спецбригады: большинство (зонтик / обычно / 1-3-1), меньшинство (коробка / обычно / давить).
/// Порядок в массиве — исторический (старые сохранения хранят первые четыре), группировка для экрана — Phases.
///
/// Принцип «нет одного верного решения»: край без подходящих игроков — размен (больше голов у обеих команд или меньше),
/// с подходящими — выгода, с неподходящими — убыток; у каждого края есть контрприём соперника (Counter); новая схема требует
/// выучки (~15 матчей). Сила эффектов — дизайн (данных о системах в протоколах НХЛ нет), масштаб: система под состав против
/// обычной — обычно +0,03…+0,1 гола за матч, чужая — столько же в минус.
///
/// Каждый край меняет не только голы, но и то, что видно в статистике матча (подпись схемы): силовые, отборы, потери и голы
/// после отбора, блоки, броски у ворот и издали, вбрасывания в своей зоне, броски защитников в большинстве. Голы движок держит
/// по силе (For/Against), а подпись меняет состав событий — так игрок по журналу видит, что схема делает, но не видит её цену.
/// Удаления и спецбригады движок разыгрывает сам — в прогнозе (For/Against) они учтены по доле голов в большинстве (PP_SHARE).
/// </summary>
public static class Tactics
{
    public sealed record Pole(string Name, string Short, string What, (string k, double w)[] Fit, char Group, string Watch);
    public sealed record Lever(string Key, string Name, Pole Minus, string Mid, Pole Plus, string MidShort, string MidWhat);

    public static readonly Lever[] Levers =
    {
        new("fc", "Форчек",
            new("Сдержанный 1-2-2", "1-2-2", "Один игрок давит, четверо закрывают середину: меньше моментов у обеих команд, нужна дисциплина и позиционная игра",
                new[] { ("dfn", 0.3), ("dec", 0.25), ("dis", 0.2), ("tm", 0.15), ("blk", 0.1) }, 'A', "меньше отборов в чужой зоне и меньше удалений, к третьему периоду свежее"),
            "Обычный 2-1-2",
            new("Прессинг 2-3", "2-3", "Двое давят на защитников, третий подстраховывает: больше отборов и бросков, но и выходов соперника в численном большинстве; нужны скорость и выносливость",
                new[] { ("spd", 0.25), ("acc", 0.2), ("wrk", 0.2), ("tka", 0.2), ("sta", 0.15) }, 'A', "больше отборов и голов после отбора, но и удалений больше, устаём быстрее"), "2-1-2",
            "Двое на шайбе в углу, третий — на подстраховке у синей линии: обычный хоккей, его знают все игроки."),
        new("en", "Вход в зону",
            new("Вброс", "Вброс", "Шайбу — в угол и борьба за неё: меньше потерь на синей линии, броски похуже; нужны сила и работа в углах",
                new[] { ("str", 0.3), ("hit", 0.25), ("wrk", 0.25), ("net", 0.2) }, 'F', "меньше потерь, больше силовых в углах, броски похуже"),
            "По ситуации",
            new("Проход с шайбой", "С шайбой", "Вход в зону с контролем шайбы: броски опаснее, но потери на синей линии дают контратаки; нужны владение и скорость",
                new[] { ("hnd", 0.35), ("spd", 0.25), ("pas", 0.2), ("acc", 0.2) }, 'F', "броски опаснее, но потерь больше — и голов соперника после них"), "Смешанно",
            "Есть место — заходим с шайбой, нет — отдаём в угол и боремся."),
        new("da", "Защитники",
            new("Сзади", "Сзади", "Защитники не подключаются: меньше бросков от синей линии и меньше выходов один в один; нужны домоседы",
                new[] { ("dfn", 0.35), ("blk", 0.25), ("str", 0.2), ("dec", 0.2) }, 'D', "меньше бросков наших защитников, реже выходы соперника"),
            "Обычно",
            new("В атаку", "В атаку", "Защитники подключаются и бросают: атака шире, но сзади пусто; нужны быстрые защитники с передачей",
                new[] { ("pas", 0.3), ("spd", 0.25), ("vis", 0.2), ("hnd", 0.15), ("lng", 0.1) }, 'D', "больше бросков наших защитников, но и выходов соперника больше"), "Обычно",
            "Защитники подключаются, когда шайба у своих на синей линии, и успевают назад."),
        new("sh", "Броски",
            new("Только моменты", "Моменты", "Ищем передачу в опасную зону: бросков меньше, но они лучше; нужны видение и передача",
                new[] { ("pas", 0.3), ("vis", 0.3), ("dec", 0.2), ("fin", 0.2) }, 'A', "бросков меньше, больше от ворот, реализация выше"),
            "Обычно",
            new("Бросать всё", "Всё", "Бросаем при любой возможности, игроки на пятаке добивают: бросков больше, качество ниже; нужны бросок издали и игра у ворот",
                new[] { ("lng", 0.3), ("net", 0.3), ("pwr", 0.2), ("wrk", 0.2) }, 'A', "бросков больше, больше издали, заблокированных и добиваний"), "Обычно",
            "Бросаем, когда есть позиция, и ищем передачу, когда её нет."),
        new("agg", "Жёсткость",
            new("Чисто", "Чисто", "Отбираем клюшкой и положением, без охоты за силовыми: меньше удалений, но соперник смелее с шайбой; нужны отбор и дисциплина",
                new[] { ("tka", 0.35), ("dis", 0.3), ("dec", 0.2), ("dfn", 0.15) }, 'A', "меньше удалений и силовых, больше отборов клюшкой"),
            "Обычная",
            new("Жёстко", "Жёстко", "Силовые при каждой возможности: соперник торопится и теряет шайбу, но удалений больше; нужны сила и игра в корпус",
                new[] { ("hit", 0.35), ("str", 0.3), ("sta", 0.2), ("dis", 0.15) }, 'A', "больше силовых и потерь у соперника, но и наших удалений"), "Обычно",
            "Силовые — когда они напрашиваются, без охоты за соперником."),
        new("dz", "Своя зона",
            new("Зонная: закрыть пятак", "Зонная", "Держим середину и пятак: соперник бросает больше, но издали, мы чаще блокируем; нужны блок броска и позиционная игра",
                new[] { ("blk", 0.35), ("dfn", 0.25), ("str", 0.2), ("dec", 0.2) }, 'A', "у соперника больше бросков, но меньше у ворот; больше наших блоков"),
            "Смешанная",
            new("Персональная", "Персональная", "Каждый держит своего: соперник бросает реже, но если нас обыграли — бросок опасный; нужны скорость, отбор и выносливость",
                new[] { ("tka", 0.3), ("spd", 0.25), ("dfn", 0.25), ("sta", 0.2) }, 'A', "у соперника меньше бросков, у нас больше отборов, устаём быстрее"), "Смешанная",
            "У ворот держим зону, у бортов — своего игрока."),
        new("bo", "Выход из зоны",
            new("Надёжно: по борту", "По борту", "Выходим по борту, в крайнем случае выбрасываем: меньше потерь у своих ворот, но и атак с ходу мало, а вбрасываний в своей зоне больше; нужны сила и работа у борта",
                new[] { ("str", 0.3), ("dec", 0.3), ("wrk", 0.2), ("dfn", 0.2) }, 'A', "меньше потерь, но больше вбрасываний в своей зоне"),
            "По ситуации",
            new("Длинный пас", "Длинный пас", "Первым пасом — через зону на ход нападающему: больше выходов с ходу, но и потерь больше; нужны передача, видение и скорость",
                new[] { ("pas", 0.35), ("vis", 0.3), ("spd", 0.2), ("acc", 0.15) }, 'A', "больше голов с хода после отбора, но и потерь больше"), "Смешанно",
            "Короткий пас центру, а если давят — по борту."),
        new("pp", "Большинство",
            new("Зонтик: через бросок", "Зонтик", "Шайба — на синюю линию, бросок сквозь заслон и подправление на пятаке: бросков больше, качество ниже; нужны бросок издали и игра у ворот",
                new[] { ("lng", 0.35), ("pwr", 0.25), ("net", 0.25), ("str", 0.15) }, 'P', "в большинстве больше бросков защитников и издали"),
            "Обычное",
            new("1-3-1: через передачу", "1-3-1", "Розыгрыш через передачи на бросок в одно касание: бросков меньше, но они опаснее; нужны передача, видение и бросок",
                new[] { ("pas", 0.35), ("vis", 0.3), ("fin", 0.2), ("hnd", 0.15) }, 'P', "в большинстве бросков меньше, но ближе к воротам"), "Обычно",
            "По ситуации: бросок, когда открыта синяя линия, передача — когда открыт пятак."),
        new("pk", "Меньшинство",
            new("Коробка", "Коробка", "Четверо держат середину и отдают периметр: соперник бросает издали, мы блокируем; своих моментов почти нет; нужны блок броска и позиция",
                new[] { ("blk", 0.35), ("dfn", 0.3), ("str", 0.2), ("dec", 0.15) }, 'K', "в меньшинстве больше блоков, соперник бросает издали"),
            "Обычное",
            new("Давить на шайбу", "Давить", "Давим на шайбу и в меньшинстве: больше отборов и своих моментов, но если обыграли — бросок с близи; нужны скорость, отбор и выносливость",
                new[] { ("spd", 0.3), ("tka", 0.3), ("sta", 0.2), ("dec", 0.2) }, 'K', "в меньшинстве больше своих бросков и отборов"), "Обычно",
            "Держим форму и давим на шайбу у борта, когда соперник ошибся."),
    };

    /// <summary>Фазы для экрана: название и рычаги по порядку.</summary>
    public static readonly (string name, int[] levers)[] Phases =
    {
        ("Без шайбы", new[] { 0, 4, 5 }),
        ("С шайбой", new[] { 6, 1, 2, 3 }),
        ("Спецбригады", new[] { 7, 8 }),
    };

    public static int IndexOf(string key) => Array.FindIndex(Levers, l => l.Key == key);

    /// <summary>
    /// Готовые стили — отправная точка для тех, кто не хочет собирать систему по рычагам (порядок — как в Levers).
    /// Выбор стиля просто ставит рычаги; дальше их можно менять.
    /// </summary>
    public static readonly (string name, string what, int[] sys)[] Styles =
    {
        ("Давление", "Прессинг по всей площадке, жёсткая игра, персональная опека и длинный пас после отбора. Много моментов, много удалений, к концу матча устаём.",
            new[] { 1, 0, 1, 0, 1, 1, 1, 0, 1 }),
        ("Контроль шайбы", "Заходим с шайбой, бросаем только из моментов, большинство — через передачу. Нужны техничные игроки.",
            new[] { 0, 1, 0, -1, -1, 0, 0, 1, 0 }),
        ("Надёжная оборона", "Сдержанный форчек, закрытый пятак, выход по борту, защитники сзади. Мало моментов у обеих команд — ставка на вратаря и терпение.",
            new[] { -1, -1, -1, 0, 0, -1, -1, 0, -1 }),
        ("Быстрые переходы", "Отдаём инициативу в средней зоне и наказываем контратакой: проход с шайбой и длинный пас. Нужны быстрые нападающие.",
            new[] { -1, 1, 0, 0, 0, 0, 1, 0, 1 }),
        ("Силовой хоккей", "Шайбу — в угол и борьба, бросать всё, жёсткость, зонтик в большинстве. Нужны крупные игроки и бросок издали.",
            new[] { 0, -1, 0, 1, 1, -1, -1, -1, 0 }),
    };

    /// <summary>Эффект системы на матч (в лог-голах, множители — как есть).</summary>
    public sealed class Effect
    {
        /// <summary>Свои голы и голы соперника (лог) — итог для прогноза: с большинством, меньшинством и удалениями.</summary>
        public double For, Against;
        /// <summary>Часть For/Against от спецбригад и удалений: движок разыгрывает её сам, поэтому в общий множитель не берёт.</summary>
        public double SpF, SpA;
        /// <summary>Качество своих бросков (больше — бросков меньше при тех же голах), доля бросков защитников, дальних бросков, удаления, усталость.</summary>
        public double Q = 1, DW = 1, Far = 1, Pen = 1, Drain = 1;
        /// <summary>Подпись схемы: свои силовые, отборы, потери; потери соперника под давлением; свои блоки; броски соперника у ворот и издали;
        /// вбрасывания в своей зоне (выброс); сила атак с хода после отбора.</summary>
        public double Hit = 1, Tk = 1, Gv = 1, OppGv = 1, Blk = 1, CloseA = 1, FarA = 1, Ice = 1, Rush = 1, OppQ = 1;
        /// <summary>Своё большинство: голы (лог), качество бросков, доля защитников, броски у ворот и издали.</summary>
        public double PpF, PpQ = 1, PpDW = 1, PpClose = 1, PpFar = 1;
        /// <summary>Своё меньшинство: голы соперника в большинстве (лог), свои броски в меньшинстве, блоки, броски соперника у ворот и издали.</summary>
        public double PkA, PkSh = 1, PkBlk = 1, PkClose = 1, PkFar = 1;
        /// <summary>Разбор по рычагам для экрана: (рычаг, свои голы, голы соперника) в лог-единицах за матч.</summary>
        public readonly List<(string key, double f, double a)> Parts = new();
        /// <summary>Контрприёмы соперника, сработавшие в этом матче (для разбора и совета помощника).</summary>
        public readonly List<string> Notes = new();
        /// <summary>Множители голов, которые движок берёт на весь матч (без спецбригад и удалений).</summary>
        public double EngF => For - SpF;
        public double EngA => Against - SpA;
        public bool IsNeutral => Math.Abs(For) < 1e-9 && Math.Abs(Against) < 1e-9 && Q == 1 && DW == 1 && Far == 1 && Pen == 1 && Drain == 1
                                 && Hit == 1 && Tk == 1 && Gv == 1 && OppGv == 1 && Blk == 1 && CloseA == 1 && FarA == 1 && Ice == 1 && Rush == 1 && OppQ == 1
                                 && PpF == 0 && PpQ == 1 && PpDW == 1 && PpClose == 1 && PpFar == 1 && PkA == 0 && PkSh == 1 && PkBlk == 1 && PkClose == 1 && PkFar == 1;
    }

    // ------------------------------------------------------------------ размеры эффектов (дизайн)
    /// <summary>
    /// Край без учёта игроков: (свои, соперника) в лог-голах за матч; у большинства — лог голов в большинстве (свои),
    /// у меньшинства — лог голов соперника в большинстве. Удаления, большинство и голы в меньшинстве в прогнозе добавляются отдельно.
    /// </summary>
    static readonly Dictionary<string, (double f, double a)> BASE = new()
    {
        ["fc-"] = (-0.042, -0.035), ["fc+"] = (0.045, 0.03),
        ["en-"] = (-0.015, -0.015), ["en+"] = (0.02, 0.02),
        ["da-"] = (-0.03, -0.03), ["da+"] = (0.03, 0.03),
        ["sh-"] = (0, 0), ["sh+"] = (0, 0),
        ["agg-"] = (-0.022, 0.006), ["agg+"] = (0.026, -0.003),
        ["dz-"] = (-0.01, -0.012), ["dz+"] = (0.008, 0.006),
        ["bo-"] = (-0.018, -0.018), ["bo+"] = (0.02, 0.02),
        ["pp-"] = (0, 0), ["pp+"] = (0, 0),
        ["pk-"] = (0, -0.05), ["pk+"] = (0, 0.06),
    };
    /// <summary>Пригодность состава (в стандартных отклонениях клубов лиги): своим голам +FIT_F, голам соперника −FIT_A за единицу.</summary>
    public static double FIT_F = 0.0045, FIT_A = 0.0035;
    /// <summary>Спецбригады: пригодность звеньев большинства/меньшинства — лог голов в большинстве за единицу.</summary>
    public static double FIT_S = 0.05;
    /// <summary>Доля голов команды в большинстве (НХЛ ~20%) и в меньшинстве (~3%): перевод эффектов спецбригад и удалений в голы за матч.</summary>
    public const double PP_SHARE = 0.2, SH_SHARE = 0.03;
    /// <summary>Невыученная схема: свои голы −LEARN_F, соперника +LEARN_A при нулевой выучке (линейно до полной).</summary>
    public const double LEARN_F = 0.015, LEARN_A = 0.01;
    /// <summary>Выучка: за матч в этой схеме и за день (тренировки этапа 8 ускорят), стартовая у новой схемы.</summary>
    public const double LEARN_GAME = 0.06, LEARN_DAY = 0.012, LEARN_NEW = 0.15;

    static string Key(Lever l, int v) => l.Key + (v < 0 ? "-" : "+");
    public static string KeyOf(int lever, int v) => v == 0 ? null : Key(Levers[lever], v);
    public static Pole PoleOf(Lever l, int v) => v < 0 ? l.Minus : v > 0 ? l.Plus : null;
    public static string PoleName(Lever l, int v) => v < 0 ? l.Minus.Name : v > 0 ? l.Plus.Name : l.Mid;
    public static string PoleShort(Lever l, int v) => v < 0 ? l.Minus.Short : v > 0 ? l.Plus.Short : l.MidShort;
    static bool Special(string k) => k.StartsWith("pp") || k.StartsWith("pk");

    /// <summary>Подпись края: множители событий матча (одна таблица для движка, прогноза и экрана).</summary>
    static void Knobs(string k, Effect e)
    {
        switch (k)
        {
            case "fc+": e.Drain *= 1.04; e.Pen *= 1.06; e.Tk *= 1.25; e.OppGv *= 1.15; break;
            case "fc-": e.Drain *= 0.95; e.Pen *= 0.95; e.Tk *= 0.85; e.OppGv *= 0.92; break;
            case "en+": e.Q *= 1.03; e.Gv *= 1.15; break;
            case "en-": e.Q *= 0.97; e.Pen *= 1.03; e.Gv *= 0.85; e.Hit *= 1.15; break;
            case "da+": e.DW *= 1.2; break;
            case "da-": e.DW *= 0.85; break;
            case "sh+": e.Q *= 0.93; e.Far *= 1.15; break;
            case "sh-": e.Q *= 1.07; e.Far *= 0.87; break;
            case "agg+": e.Hit *= 1.5; e.Pen *= 1.15; e.OppGv *= 1.12; e.Drain *= 1.02; break;
            case "agg-": e.Hit *= 0.65; e.Pen *= 0.85; e.Tk *= 1.15; break;
            case "dz-": e.CloseA *= 0.8; e.FarA *= 1.25; e.Blk *= 1.15; break;
            case "dz+": e.CloseA *= 1.1; e.FarA *= 0.85; e.OppQ *= 1.06; e.Tk *= 1.15; e.Drain *= 1.03; break;
            case "bo-": e.Gv *= 0.8; e.Ice *= 1.4; e.Rush *= 0.8; break;
            case "bo+": e.Gv *= 1.2; e.Rush *= 1.6; break;
            case "pp-": e.PpQ *= 0.92; e.PpDW *= 1.5; e.PpFar *= 1.35; break;
            case "pp+": e.PpQ *= 1.1; e.PpDW *= 0.7; e.PpClose *= 1.15; break;
            case "pk-": e.PkSh *= 0.7; e.PkBlk *= 1.2; e.PkClose *= 0.8; e.PkFar *= 1.3; break;
            case "pk+": e.PkSh *= 1.7; e.PkClose *= 1.08; break;
        }
    }

    static readonly Dictionary<string, Effect> KnobCache = new();
    static Effect KnobsOf(string k)
    {
        lock (KnobCache)
        {
            if (!KnobCache.TryGetValue(k, out var e)) { e = new Effect(); Knobs(k, e); KnobCache[k] = e; }
            return e;
        }
    }

    // ------------------------------------------------------------------ пригодность состава

    /// <summary>
    /// Вклад игрока в край: взвешенная сумма нужных характеристик минус его средняя по всем — стиль, а не класс
    /// (класс игрока и так в силе клуба; иначе сильным подходило бы всё, а слабым ничего).
    /// </summary>
    static double Score(Func<PlayerData, string, double> attr, PlayerData p, Pole pole) =>
        pole.Fit.Sum(x => x.w * attr(p, x.k)) - Attrs.Skater.Average(a => attr(p, a.Key));

    static bool InGroup(PlayerData p, char g) => g is 'A' or 'P' or 'K' || (g == 'D') == p.IsDefense;

    /// <summary>Сырая пригодность состава: средняя по минутам в своей группе (нападающие, защитники, все; у спецбригад — их звенья).</summary>
    static double Raw(GameData d, Lineup l, Func<PlayerData, string, double> attr, Pole pole)
    {
        double s = 0, w = 0;
        void Add(int id, double min)
        {
            if (id == 0 || min <= 0 || !d.PlayerById.TryGetValue(id, out var p) || !InGroup(p, pole.Group)) return;
            s += min * Score(attr, p, pole); w += min;
        }
        if (pole.Group == 'P') { foreach (var id in l.Pp1 ?? Array.Empty<int>()) Add(id, 0.62); foreach (var id in l.Pp2 ?? Array.Empty<int>()) Add(id, 0.38); }
        else if (pole.Group == 'K') { foreach (var id in l.Pk1 ?? Array.Empty<int>()) Add(id, 0.5); foreach (var id in l.Pk2 ?? Array.Empty<int>()) Add(id, 0.5); }
        else
        {
            for (int i = 0; i < l.F.Count; i++) foreach (var id in l.F[i]) Add(id, i < l.FMin.Length ? l.FMin[i] : 0);
            for (int i = 0; i < l.D.Count; i++) foreach (var id in l.D[i]) Add(id, i < l.DMin.Length ? l.DMin[i] : 0);
        }
        return w > 0 ? s / w : 11;
    }

    /// <summary>Шкала пригодности по лиге: среднее и разброс по клубам (составы на старте карьеры, истинные характеристики без формы).</summary>
    sealed class Ref { public readonly Dictionary<string, (double m, double sd)> P = new(); }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Career, Ref> Refs = new();

    static Ref RefOf(Career c) => Refs.GetValue(c, cc =>
    {
        var r = new Ref();
        var attr = TeamStrength.Attr(cc, cc.DlBase);
        foreach (var lv in Levers)
            foreach (var v in new[] { -1, 1 })
            {
                var pole = PoleOf(lv, v);
                var xs = cc.Data.Teams.Select(t => Raw(cc.Data, cc.Lineups[t.Abbr], attr, pole)).ToList();
                double m = xs.Average(), sd = Math.Sqrt(xs.Average(x => (x - m) * (x - m)));
                r.P[Key(lv, v)] = (m, Math.Max(0.05, sd));
            }
        return r;
    });

    /// <summary>Пригодность состава к краю в стандартных отклонениях клубов лиги (0 — средний клуб), в пределах ±2,5.</summary>
    public static double Fit(Career c, Lineup l, Lever lv, int v, Func<PlayerData, string, double> attr = null)
    {
        if (v == 0) return 0;
        attr ??= TeamStrength.Attr(c, c.DlBase);
        var (m, sd) = RefOf(c).P[Key(lv, v)];
        return Math.Clamp((Raw(c.Data, l, attr, PoleOf(lv, v)) - m) / sd, -2.5, 2.5);
    }

    /// <summary>Пригодность отдельного игрока к краю (для списков «кто подходит»), в тех же единицах, что у клуба × 3 (игроки разбросаны шире).</summary>
    public static double PlayerFit(Career c, PlayerData p, Lever lv, int v, Func<PlayerData, string, double> attr = null)
    {
        var pole = PoleOf(lv, v);
        if (pole == null || !InGroup(p, pole.Group)) return double.NaN;
        var (m, sd) = RefOf(c).P[Key(lv, v)];
        return (Score(attr ?? TeamStrength.Attr(c, c.DlBase), p, pole) - m) / (3 * sd);
    }

    // ------------------------------------------------------------------ эффект на матч

    /// <summary>Край с пригодностью: (свои, соперника) в лог-голах; у спецбригад — в голах большинства (своего или соперника).</summary>
    static (double f, double a) Raw(string k, double fit)
    {
        var (bf, ba) = BASE[k];
        if (k.StartsWith("pp")) return (bf + FIT_S * fit, 0);
        if (k.StartsWith("pk")) return (0, ba - FIT_S * fit);
        return (bf + FIT_F * fit, ba - FIT_A * fit);
    }

    /// <summary>Перевод края в голы за матч (лог): спецбригады — по доле голов в большинстве, удаления — по их цене.</summary>
    static (double f, double a, double spf, double spa) Whole(string k, double f, double a)
    {
        var kn = KnobsOf(k);
        double pen = PP_SHARE * Math.Log(kn.Pen), sh = SH_SHARE * Math.Log(kn.PkSh);
        if (k.StartsWith("pp")) return (PP_SHARE * f, pen, PP_SHARE * f, pen);
        if (k.StartsWith("pk")) return (sh, PP_SHARE * a + pen, sh, PP_SHARE * a + pen);
        return (f, a + pen, 0, pen);
    }

    /// <summary>Система стороны против системы и состава соперника.</summary>
    public static Effect Of(Career c, MatchSide me, MatchSide opp)
    {
        var e = new Effect();
        var sys = me.Plan ?? me.Lineup.Sys;
        if (sys == null || sys.All(x => x == 0)) return e;
        var attr = me.Attr ?? TeamStrength.Attr(c, c.DlBase);
        var oSys = opp.Plan ?? opp.Lineup.Sys ?? new int[Levers.Length];
        for (int i = 0; i < Levers.Length; i++)
        {
            int v = i < sys.Length ? sys[i] : 0;
            if (v == 0) continue;
            var lv = Levers[i];
            string k = Key(lv, v);
            double fit = Fit(c, me.Lineup, lv, v, attr);
            var (f, a) = Raw(k, fit);
            // контрприёмы соперника
            var (cf, ca) = Counter(c, k, opp, oSys, e.Notes);
            f += cf; a += ca;
            // выучка
            double learn = me.Lineup.Learned(k);
            if (Special(k)) { if (k.StartsWith("pp")) f -= 3 * LEARN_F * (1 - learn); else a += 3 * LEARN_A * (1 - learn); }
            else { f -= LEARN_F * (1 - learn); a += LEARN_A * (1 - learn); }
            if (k.StartsWith("pp")) e.PpF += f;
            if (k.StartsWith("pk")) e.PkA += a;
            Knobs(k, e);
            var (wf, wa, spf, spa) = Whole(k, f, a);
            e.For += wf; e.Against += wa; e.SpF += spf; e.SpA += spa;
            e.Parts.Add((lv.Key, wf, wa));
        }
        return e;
    }

    /// <summary>
    /// Контрприёмы: как выбранный край работает против этого соперника (свои голы, голы соперника; у спецбригад — голы большинства):
    ///   прессинг вязнет против защитников, которые быстро отдают шайбу, и против выхода по борту; проход с шайбой упирается
    ///   в сдержанный форчек, а вброс его вскрывает; подключение защитников наказывают быстрые нападающие соперника;
    ///   «бросать всё» хорошо против вратаря, который отдаёт отскоки, и плохо против блокирующих защитников; жёсткость ломает
    ///   техничных и не пугает крепких; зонную оборону расстреливают издали, персональную растаскивают передачей; длинный пас
    ///   перехватывает 1-2-2 и наказывает прессинг; зонтик бьёт коробку, 1-3-1 — агрессивное меньшинство.
    /// </summary>
    static (double f, double a) Counter(Career c, string k, MatchSide opp, int[] oSys, List<string> notes)
    {
        var oattr = opp.Attr ?? TeamStrength.Attr(c, c.DlBase);
        double OppFit(int lever, int v) => Fit(c, opp.Lineup, Levers[lever], v, oattr);
        int O(int lever) => lever < oSys.Length ? oSys[lever] : 0;
        (double f, double a) Note((double f, double a) x, string good, string bad)
        {
            double net = x.f - x.a;
            if (net > 0.004 && good != null) notes.Add(good);
            else if (net < -0.004 && bad != null) notes.Add(bad);
            return x;
        }
        bool trap = O(0) < 0;
        switch (k)
        {
            case "fc+":
            {
                double mv = OppFit(2, 1);
                return Note((-0.008 * mv - (O(6) < 0 ? 0.008 : 0), 0.008 * mv), "их защитники медленно отдают шайбу — прессинг сработает",
                    O(6) < 0 ? "они выходят из зоны по борту — прессинг ловит мало" : "их защитники быстро отдают шайбу — прессинг будет вязнуть");
            }
            case "en+": return Note((trap ? -0.025 : 0, 0), null, "их сдержанный форчек 1-2-2 закрывает проход с шайбой");
            case "en-": return Note(((trap ? 0.015 : 0) - 0.006 * OppFit(1, -1), 0), "вброс вскрывает их 1-2-2", "у них сильные игроки в углах — вброс отдаёт шайбу");
            case "da+": return Note((0, 0.01 * OppFit(1, 1)), "их нападающие небыстрые — защитникам можно подключаться", "у них быстрые нападающие — за подключение защитников накажут");
            case "sh+":
            {
                double reb = c.Data.PlayerById.TryGetValue(opp.Goalie, out var g) ? oattr(g, "reb") : 11;
                return Note((0.006 * (11 - reb) - 0.006 * OppFit(2, -1), 0), "их вратарь отдаёт отскоки — бросать стоит", "их вратарь держит шайбу, а защитники блокируют — дальние броски пропадут");
            }
            case "sh-": return Note((0.004 * OppFit(2, -1), 0), "их защитники блокируют броски — ищем передачу", null);
            case "agg+": return Note((-0.008 * OppFit(4, 1), 0), "у них лёгкие техничные игроки — жёсткая игра выбьет из колеи", "у них крепкие ребята — жёсткостью их не напугать");
            case "dz-": return Note((0, 0.008 * OppFit(3, 1)), "у них нет дальнего броска — зонная оборона закроет пятак", "у них есть бросок издали — зонная оборона отдаст им дальние броски");
            case "dz+": return Note((0, 0.008 * OppFit(3, -1)), "у них небыстрый пас — персональная опека задушит", "у них быстрый пас — персональную опеку растащат");
            case "bo-": return Note((0, O(0) > 0 ? -0.01 : 0), "их прессинг не поймает выход по борту", null);
            case "bo+": return Note((trap ? -0.02 : O(0) > 0 ? 0.012 : 0, 0), "за их прессингом — свободный лёд для длинного паса", "их 1-2-2 перехватывает длинные передачи");
            case "pp-": return Note((O(8) < 0 ? 0.04 : O(8) > 0 ? -0.025 : 0, 0), "их коробка отдаёт синюю линию — зонтик сработает", "их меньшинство давит на бросающих — зонтику не дадут бросить");
            case "pp+": return Note((O(8) > 0 ? 0.04 : O(8) < 0 ? -0.025 : 0, 0), "их меньшинство давит и оставляет место для передачи", "их коробка закрывает середину — передачами не пройти");
            case "pk-": return Note((0, O(7) < 0 ? 0.04 : O(7) > 0 ? -0.025 : 0), "их большинство играет через передачу — коробка закроет середину", "их зонтик расстреляет коробку издали");
            case "pk+": return Note((0, O(7) > 0 ? 0.04 : O(7) < 0 ? -0.025 : 0), "их зонтик не успеет бросить под давлением", "их 1-3-1 разыграет давление передачами");
        }
        return (0, 0);
    }

    /// <summary>Разбор системы против среднего соперника: по рычагам — край, пригодность, выучка, свои голы и голы против (лог за матч).</summary>
    public sealed record LeverView(int Index, int V, double Fit, double Learn, double F, double A);

    public static List<LeverView> Breakdown(Career c, Lineup l, Func<PlayerData, string, double> attr = null)
    {
        var list = new List<LeverView>();
        for (int i = 0; i < Levers.Length; i++)
        {
            int v = l.SysOf(i);
            if (v == 0) { list.Add(new LeverView(i, 0, 0, 1, 0, 0)); continue; }
            var lv = Levers[i]; string k = Key(lv, v);
            double fit = Fit(c, l, lv, v, attr), learn = l.Learned(k);
            var (f, a) = Raw(k, fit);
            if (k.StartsWith("pp")) f -= 3 * LEARN_F * (1 - learn); else if (k.StartsWith("pk")) a += 3 * LEARN_A * (1 - learn);
            else { f -= LEARN_F * (1 - learn); a += LEARN_A * (1 - learn); }
            var (wf, wa, _, _) = Whole(k, f, a);
            list.Add(new LeverView(i, v, fit, learn, wf, wa));
        }
        return list;
    }

    /// <summary>Применить системы к сторонам матча (в прогнозе и в матче).</summary>
    public static void Prepare(Career c, MatchSide h, MatchSide a)
    {
        h.Sys = Of(c, h, a);
        a.Sys = Of(c, a, h);
        // лига в целом — как в НХЛ: системы перераспределяют голы, удаления, броски между клубами, а не добавляют их всем
        var n = Norm(c);
        foreach (var e in new[] { h.Sys, a.Sys })
        {
            e.For -= n.G;
            double lp = PP_SHARE * Math.Log(n.Pen), ls = SH_SHARE * Math.Log(n.Sh);
            e.Pen /= n.Pen; e.Against -= lp; e.SpA -= lp;
            e.PkSh /= n.Sh; e.For -= ls; e.SpF -= ls;
            e.Q /= n.Q;
        }
        // удаления соперника — наше большинство
        foreach (var (me, op) in new[] { (h, a), (a, h) })
        {
            double x = PP_SHARE * Math.Log(op.Sys.Pen);
            me.Sys.For += x; me.Sys.SpF += x;
        }
    }

    /// <summary>Средние по лиге: лог голов (за + против, без спецбригад), множители удалений, бросков в меньшинстве и качества бросков.</summary>
    public sealed record LeagueNorm(double G, double Pen, double Sh, double Q);
    sealed class NormCache { public long Key = long.MinValue; public LeagueNorm N; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Career, NormCache> Norms = new();

    /// <summary>Нормировка по лиге (раз в день или после смены систем — по ключу из дня и систем всех клубов).</summary>
    public static LeagueNorm Norm(Career c)
    {
        var cache = Norms.GetOrCreateValue(c);
        long key = c.Today.DayNumber;
        foreach (var t in c.Data.Teams)
        {
            var l = c.Lineups[t.Abbr];
            for (int i = 0; i < Levers.Length; i++) key = unchecked(key * 3 + l.SysOf(i) + 1);
        }
        if (cache.Key == key && cache.N != null) return cache.N;
        double g = 0, pen = 0, sh = 0; int n = 0;
        foreach (var t in c.Data.Teams)
        {
            var l = c.Lineups[t.Abbr];
            foreach (var v in Breakdown(c, l))
            {
                if (v.V == 0) continue;
                string k = Key(Levers[v.Index], v.V);
                var (f, a) = Raw(k, v.Fit);
                double learn = v.Learn;
                if (k.StartsWith("pp")) f -= 3 * LEARN_F * (1 - learn); else if (k.StartsWith("pk")) a += 3 * LEARN_A * (1 - learn);
                else { f -= LEARN_F * (1 - learn); a += LEARN_A * (1 - learn); }
                var (wf, wa, spf, spa) = Whole(k, f, a);
                g += (wf - spf) + (wa - spa);
                var kn = KnobsOf(k);
                pen += Math.Log(kn.Pen); sh += Math.Log(kn.PkSh);
            }
            n++;
        }
        cache.N = new LeagueNorm(g / Math.Max(1, n), Math.Exp(pen / Math.Max(1, n)), Math.Exp(sh / Math.Max(1, n)), LeagueQ(c));
        cache.Key = key;
        return cache.N;
    }

    /// <summary>
    /// Средний по лиге множитель «качества броска» от систем (вместе с дальними бросками и бросками защитников — они тоже
    /// снижают отдачу попытки: +15% дальних ≈ −2,4%, +20% бросков защитников ≈ −2%).
    /// </summary>
    static double LeagueQ(Career c)
    {
        double sum = 0; int n = 0;
        foreach (var l in c.Lineups.Values)
        {
            double q = 1;
            for (int i = 0; i < Levers.Length; i++)
            {
                int v = l.SysOf(i);
                if (v == 0) continue;
                q *= Key(Levers[i], v) switch
                {
                    "en+" => 1.03, "en-" => 0.97, "sh+" => 0.93 * Math.Pow(1.15, -0.17), "sh-" => 1.07 * Math.Pow(0.87, -0.17),
                    "da+" => Math.Pow(1.2, -0.11), "da-" => Math.Pow(0.85, -0.11), "dz+" => 1.06, _ => 1,
                };
            }
            sum += Math.Log(q); n++;
        }
        return n == 0 ? 1 : Math.Exp(sum / n);
    }

    // ------------------------------------------------------------------ выучка и выбор ИИ

    /// <summary>После матча: выучка схемы растёт у обоих клубов.</summary>
    public static void AfterGame(Career c, ScheduledGame g)
    {
        foreach (var t in new[] { g.Home, g.Away }) Grow(c.Lineups[t], LEARN_GAME);
    }

    public static void Daily(Career c)
    {
        foreach (var l in c.Lineups.Values) Grow(l, LEARN_DAY);
    }

    /// <summary>Тренировка системы (Training): выучка сверх обычной.</summary>
    public static void Train(Lineup l, double d) => Grow(l, d);

    static void Grow(Lineup l, double d)
    {
        if (l.Sys == null) return;
        l.Learn ??= new();
        for (int i = 0; i < Levers.Length && i < l.Sys.Length; i++)
        {
            if (l.Sys[i] == 0) continue;
            string k = Key(Levers[i], l.Sys[i]);
            l.Learn[k] = (float)Math.Min(1, l.Learned(k) + d);
        }
    }

    /// <summary>Сменить полюс рычага: новая схема начинает с выученного раньше (не меньше LEARN_NEW).</summary>
    public static void Set(Lineup l, int lever, int v)
    {
        l.Sys ??= new int[Levers.Length];
        if (l.Sys.Length < Levers.Length) { var s = new int[Levers.Length]; l.Sys.CopyTo(s, 0); l.Sys = s; }
        l.Sys[lever] = v;
        if (v != 0)
        {
            l.Learn ??= new();
            string k = Key(Levers[lever], v);
            l.Learn[k] = (float)Math.Max(LEARN_NEW, l.Learned(k));
        }
    }

    /// <summary>Сколько даёт край этому составу против среднего соперника (свои − чужие голы за матч, лог), при полной выучке.</summary>
    public static double Value(Career c, Lineup l, int lever, int v, Func<PlayerData, string, double> attr = null)
    {
        if (v == 0) return 0;
        var lv = Levers[lever];
        string k = Key(lv, v);
        var (f, a) = Raw(k, Fit(c, l, lv, v, attr));
        var (wf, wa, _, _) = Whole(k, f, a);
        return wf - wa;
    }

    /// <summary>
    /// Система по составу глазами тренера: для каждого рычага — полюс с наибольшей выгодой, если она заметна (иначе середина).
    /// Тренер ошибается (тактика) и тянет к любимому форчеку.
    /// </summary>
    public static int[] Suggest(Career c, Lineup l, double min = 0.006, StaffMember who = null, int salt = 0)
    {
        var s = new int[Levers.Length];
        for (int i = 0; i < Levers.Length; i++)
        {
            double Est(int v) => v == 0 ? 0 : Value(c, l, i, v) + (who == null ? 0 : Staff.TacSd(who) / 3 * Staff.Noise(c, who, 900 + i + 10 * salt, v)
                                                   + (i == 0 && who.Press == v ? 0.006 : 0));
            double vm = Est(-1), vp = Est(1);
            if (vp >= vm && vp > min) s[i] = 1; else if (vm > vp && vm > min) s[i] = -1;
        }
        return s;
    }

    // ------------------------------------------------------------------ роли звеньев

    /// <summary>Атака и оборона игрока (ед. шкалы, как в силе клуба): для ролей и подсказок.</summary>
    public static (double off, double def) OffDef(Career c, int id)
    {
        if (!c.Data.PlayerById.TryGetValue(id, out var p)) return (0, 0);
        var attr = TeamStrength.Attr(c, c.DlBase);
        return (TeamStrength.Off.Sum(x => x.w * (attr(p, x.k) - 11)), TeamStrength.Def.Sum(x => x.w * (attr(p, x.k) - 11)));
    }

    static (double off, double def) UnitOD(Career c, int[] ids)
    {
        var v = ids.Where(id => id != 0).Select(id => OffDef(c, id)).ToList();
        return v.Count == 0 ? (0, 0) : (v.Average(x => x.off), v.Average(x => x.def));
    }

    /// <summary>
    /// Роли как у тренеров НХЛ: атакующая — сильнейшая в атаке из первых двух троек (пар), оборонительная — с наибольшим
    /// перевесом обороны над атакой среди остальных (кроме четвёртой тройки); против лучших — да.
    /// </summary>
    public static void DefaultRoles(Career c, Lineup l)
    {
        int[] Pick(List<int[]> units, int maxDef)
        {
            var r = new int[units.Count];
            if (units.Count < 2) return r;
            var od = units.Select(u => UnitOD(c, u)).ToList();
            int att = od[0].off >= od[1].off ? 0 : 1;
            r[att] = 1;
            int def = Enumerable.Range(0, Math.Min(units.Count, maxDef)).Where(i => i != att).OrderByDescending(i => od[i].def - od[i].off).First();
            r[def] = 2;
            return r;
        }
        l.FRole = Pick(l.F, 3); l.DRole = Pick(l.D, 3); l.Matchup = true;
    }

    /// <summary>Старт карьеры: у каждого клуба — система под свой состав, выученная (так клуб и играл), и роли звеньев.</summary>
    public static void Init(Career c)
    {
        foreach (var t in c.Data.Teams)
        {
            var l = c.Lineups[t.Abbr];
            DefaultRoles(c, l);
            l.Rules = t.Abbr == c.UserTeam ? Rules.Default() : Rules.Ai();
            l.Sys = Suggest(c, l, who: Staff.Decider(c, t.Abbr));
            l.Learn = new();
            for (int i = 0; i < Levers.Length; i++) if (l.Sys[i] != 0) l.Learn[Key(Levers[i], l.Sys[i])] = 1;
        }
    }
}
