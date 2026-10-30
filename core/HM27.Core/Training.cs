namespace HM27.Core;

/// <summary>План тренировок своего клуба (этап «Тренер», шаг 8).</summary>
public sealed class TrainingPlan
{
    /// <summary>Нагрузка: −1 лёгкая, 0 обычная, +1 тяжёлая.</summary>
    public int Load { get; set; }
    /// <summary>Упор недели: balance, attack, defense, phys, mind, system, chem, rest.</summary>
    public string Focus { get; set; } = "balance";
    /// <summary>Индивидуальная работа: игрок → характеристика.</summary>
    public Dictionary<int, string> Individual { get; set; } = new();
}

/// <summary>
/// Тренировки: у каждого выбора — цена. Тяжёлая нагрузка ускоряет развитие, но съедает свежесть и чуть повышает риск
/// травм; лёгкая — наоборот. Упор недели ускоряет рост своей группы характеристик за счёт остальных, а «система»,
/// «сыгранность» и «восстановление» тратят тренировочное время не на рост. Индивидуальная работа удваивает рост одной
/// характеристики (до SLOTS игроков). У клубов ИИ — обычная нагрузка и общая подготовка (множители ровно 1).
/// </summary>
public static class Training
{
    public const int SLOTS = 6;
    /// <summary>Тяжёлая нагрузка: свежесть выше этого не поднимается.</summary>
    public const double HEAVY_CAP = 90;

    public sealed record Focus(string Key, string Name, string What, string Group);

    public static readonly Focus[] Foci =
    {
        new("balance", "Общая", "Всё понемногу: развитие как обычно", null),
        new("attack", "Атака", "Бросок, передача, владение: эти характеристики растут быстрее, остальные — медленнее", "Атака"),
        new("defense", "Оборона", "Позиционная игра, отбор, блоки, меньшинство", "Оборона"),
        new("phys", "Физика", "Скорость, ускорение, сила, выносливость", "Физика"),
        new("mind", "Мышление", "Решения, видение, дисциплина, большинство", "Мышление"),
        new("system", "Система", "Разбор схем: выучка новой системы идёт втрое быстрее, развитие — чуть медленнее", null),
        new("chem", "Сыгранность", "Звенья работают вместе: сыгранность растёт и без матчей, развитие — чуть медленнее", null),
        new("rest", "Восстановление", "Меньше работы, больше отдыха: свежесть возвращается быстрее, развитие замедляется", null),
    };

    public static TrainingPlan Of(Career c) => c.Training ??= new TrainingPlan();

    /// <summary>Множитель недельного роста характеристики key у игрока (свой клуб; у ИИ — 1).</summary>
    public static double Growth(Career c, PlayerData p, string key)
    {
        if (p.Team != c.UserTeam || c.Training == null) return 1;
        var t = c.Training;
        double m = t.Load switch { 1 => 1.25, -1 => 0.8, _ => 1 };
        var f = Foci.FirstOrDefault(x => x.Key == t.Focus);
        if (f?.Group != null) m *= Attrs.Skater.FirstOrDefault(a => a.Key == key)?.Group == f.Group ? 1.45 : 0.85;
        else if (t.Focus is "system" or "chem") m *= 0.85;
        else if (t.Focus == "rest") m *= 0.6;
        if (t.Individual.TryGetValue(p.Id, out var ik)) m *= ik == key ? 2.0 : 0.95;
        return m;
    }

    /// <summary>Свежесть за день сверх обычного восстановления (свой клуб).</summary>
    public static double FreshDelta(Career c, PlayerData p)
    {
        if (p.Team != c.UserTeam || c.Training == null) return 0;
        double d = c.Training.Load switch { 1 => -1.5, -1 => 1.5, _ => 0 };
        if (c.Training.Focus == "rest") d += 3;
        return d;
    }

    /// <summary>Риск травмы от нагрузки (свой клуб).</summary>
    public static double InjuryMult(Career c, PlayerData p) =>
        p.Team != c.UserTeam || c.Training == null ? 1 : c.Training.Load switch { 1 => 1.12, -1 => 0.92, _ => 1 };

    /// <summary>Новый день: свежесть, выучка системы, сыгранность звеньев, настрой от нагрузки.</summary>
    public static void Daily(Career c)
    {
        var t = c.Training;
        if (t == null) return;
        var l = c.Lineups[c.UserTeam];
        foreach (var p in c.Roster(c.UserTeam))
        {
            if (!c.Players.TryGetValue(p.Id, out var s)) continue;
            double fd = FreshDelta(c, p);
            if (fd != 0) s.Fresh = (float)Math.Clamp(s.Fresh + fd, 0, 100);
            if (t.Load == 1 && s.Fresh > HEAVY_CAP) s.Fresh = (float)HEAVY_CAP;   // на тяжёлой нагрузке полностью не отдыхают
            if (t.Load == 1) Morale.AddTo(c, p.Id, (p.Age(c.Today) ?? 26) >= 31 ? -0.25 : -0.1);
            else if (t.Load == -1) Morale.AddTo(c, p.Id, 0.08);
        }
        if (t.Focus == "system") Tactics.Train(l, 2 * Tactics.LEARN_DAY);
        if (t.Focus == "chem")
        {
            c.TrainChem(l.F.Concat(l.D), 6);
            c.TrainChem(new[] { l.Pp1, l.Pp2, l.Pk1, l.Pk2 }, 3);
        }
    }
}
