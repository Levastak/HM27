namespace HM27.Core;

/// <summary>
/// Доверие игроков к тренеру (этап «Тренер», шаг 7) — 0–100, только свой клуб. Растёт от сдержанных обещаний, честных
/// разговоров и побед, падает от нарушенных обещаний, публичной критики не тому человеку и решений, которые раздевалка
/// считает несправедливыми. Доверие тянет настрой (норма настроя выше или ниже) и смягчает обиды: тому, кто верит
/// тренеру, проще принять меньшую роль. Реакции зависят от скрытого характера (профессионализм, амбиции, темперамент,
/// лидерство) и немного от случая: один и тот же ответ разные игроки примут по-разному.
/// Раздевалка видит: нарушенное обещание или риск чужим здоровьем замечают лидеры (самые авторитетные в составе).
/// </summary>
public static class Trust
{
    public const float BASE = 55;

    public static string Word(float t) => t >= 80 ? "Безоговорочное" : t >= 65 ? "Высокое" : t >= 45 ? "Обычное" : t >= 28 ? "Сомневается" : "Не доверяет";

    public static float Of(Career c, int id) => c.Players.TryGetValue(id, out var s) && s.Trust > 0 ? s.Trust : BASE;

    public static void Add(Career c, int id, double d)
    {
        if (!c.Players.TryGetValue(id, out var s)) return;
        if (s.Trust <= 0) s.Trust = BASE;
        s.Trust = (float)Math.Clamp(s.Trust + d, 1, 99);
    }

    /// <summary>Сдвиг нормы настроя от доверия (пункты 0–100).</summary>
    public static double MoraleShift(PlayerState s) => s.Trust <= 0 ? 0 : (s.Trust - BASE) * 0.15;

    /// <summary>Во сколько раз сильнее копится обида на роль: доверяющему легче.</summary>
    public static double GriefMult(PlayerState s) => s.Trust <= 0 ? 1 : 1.3 - 0.6 * s.Trust / 100.0;

    /// <summary>Детерминированный «случай» для реакции игрока: −1…+1.</summary>
    public static double Luck(Career c, int id, int salt)
    {
        var r = new Random(unchecked(c.Seed * 7_368_787 ^ id * 31 ^ c.Today.DayNumber * 131 ^ salt));
        return 2 * r.NextDouble() - 1;
    }

    /// <summary>Лидеры раздевалки: самые авторитетные (лидерство, затем статус) из своего клуба.</summary>
    public static List<PlayerData> Leaders(Career c, int n = 3) =>
        c.Roster(c.UserTeam).Where(p => c.Players.ContainsKey(p.Id))
            .OrderByDescending(p => c.Players[p.Id].Lead * 2 - Morale.Status(c, p) * 3).Take(n).ToList();

    /// <summary>
    /// Раздевалка видит решение тренера: лидеры меняют доверие (и чуть настрой); раз в неделю — письмо от капитана,
    /// если это задело.
    /// </summary>
    public static void RoomSees(Career c, int about, double trust, string what)
    {
        var leaders = Leaders(c).Where(p => p.Id != about).ToList();
        foreach (var p in leaders) { Add(c, p.Id, trust); Morale.AddTo(c, p.Id, trust / 2); }
        if (trust >= 0 || leaders.Count == 0) return;
        if (c.Messages.Any(m => m.Action == "room:sees" && c.Today.DayNumber - m.Date.DayNumber < 7)) return;
        var cap = leaders[0];
        var m = InboxWriter.Add(c, cap.NameRu, $"Раздевалка: {what}",
            $"«Тренер, ребята обсуждают: {what}. Я не лезу в ваши решения, но в раздевалке это запомнили.»\n\n" +
            $"Лидеры команды ({string.Join(", ", leaders.Select(x => x.LastRu))}) стали меньше доверять тренеру.", "decision", $"player:{cap.Id}");
        m.Tag = "Раздевалка"; m.Action = "room:sees"; m.Players = leaders.Select(x => x.Id).ToList(); m.Team = c.UserTeam; m.Weight = 2;
        m.Lead = "Лидеры видят, как тренер обращается с другими.";
    }

    /// <summary>Новый день: доверие медленно тянется к норме; серия побед добавляет всем, серия поражений — отнимает.</summary>
    public static void Daily(Career c)
    {
        var row = c.Table.GetValueOrDefault(c.UserTeam);
        double team = 0;
        if (row != null && row.Streak.Length > 1 && int.TryParse(row.Streak[1..].TrimStart('О'), out var n) && n >= 3)
            team = row.Streak.StartsWith("В") ? 0.15 : row.Streak.StartsWith("П") ? -0.15 : 0;
        foreach (var p in c.Roster(c.UserTeam))
        {
            if (!c.Players.TryGetValue(p.Id, out var s)) continue;
            if (s.Trust <= 0) s.Trust = BASE + (s.Loy - 10) * 0.8f + (s.Prof - 10) * 0.5f;
            s.Trust += (BASE - s.Trust) * 0.01f + (float)team;
            s.Trust = Math.Clamp(s.Trust, 1, 99);
        }
    }
}
