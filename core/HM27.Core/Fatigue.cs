namespace HM27.Core;

/// <summary>
/// Нагрузка за сезон: «свежесть» игрока 0–100. Матч отнимает её по времени на льду (сверх нормы позиции — всё быстрее), каждый день без
/// матча возвращает. При обычной нагрузке НХЛ (первая пара ~25 мин, основной вратарь 55–65 матчей, второй матч за два дня
/// у вратаря — запасной) свежесть почти не опускается ниже FRESH_OK; перегрузка (28–30+ мин каждый матч, вратарь 70+ матчей
/// подряд без отдыха) снижает уровень в матче и повышает риск травмы. Проверка «Нагрузка» в HM27.Check.
/// </summary>
public static class Fatigue
{
    /// <summary>
    /// Цена матча полевому: SK_COST × (минуты / норма)^2,2 × (1 − 0,03 × (выносливость − 11)); норма — верх обычной нагрузки НХЛ:
    /// нападающие 23 мин, защитники 27. Восстановление 9,5 в день: при матче раз в ~2,15 дня (82 за сезон) норма держится,
    /// а сверх неё (нападающий 27+, защитник 30+ мин каждый матч) свежесть уходит.
    /// </summary>
    public const double SK_COST = 19, SK_NORM_F = 23, SK_NORM_D = 27, SK_REC = 9.5;
    /// <summary>Вратарь: матч целиком — 24, восстановление 9,5 в день (безубыточно при старте раз в 2,5 дня ≈ 70 матчей за сезон).</summary>
    public const double G_COST = 24, G_REC = 9.5;
    /// <summary>Ниже FRESH_OK — потеря уровня: полевые 0,04 ед. шкалы за единицу свежести, вратари 0,08.</summary>
    public const double FRESH_OK = 75, SK_PEN = 0.04, G_PEN = 0.08;

    /// <summary>Сдвиг уровня от усталости (≤ 0) — добавляется к сдвигу движка (Career.Dl).</summary>
    public static double Penalty(PlayerState s, bool goalie) =>
        s == null || s.Fresh >= FRESH_OK ? 0 : -(FRESH_OK - s.Fresh) * (goalie ? G_PEN : SK_PEN);

    /// <summary>Риск травмы от усталости: ×1 при свежести 75+, ×2 при 35.</summary>
    public static double InjuryMult(PlayerState s) => s == null || s.Fresh >= FRESH_OK ? 1 : 1 + (FRESH_OK - s.Fresh) / 40.0;

    public static void AfterGame(Career c, ScheduledGame g)
    {
        var r = g.Result;
        foreach (var (dressed, goalie, side) in new[] { (r.HomeDressed, r.HomeGoalie, g.Home), (r.AwayDressed, r.AwayGoalie, g.Away) })
            foreach (var id in dressed)
            {
                if (!c.Players.TryGetValue(id, out var s) || !c.Data.PlayerById.TryGetValue(id, out var p)) continue;
                double cost;
                if (p.IsGoalie)
                {
                    if (id != goalie) continue;
                    cost = G_COST;
                }
                else
                {
                    double min = r.Box.TryGetValue(id, out var b) && b.Toi > 0 ? b.Toi / 60.0 : c.Lineups.TryGetValue(side, out var l) ? l.MinutesOf(id) : 16;
                    double sta = s.A.GetValueOrDefault("sta", s.Lvl);
                    cost = SK_COST * Math.Pow(min / (p.IsDefense ? SK_NORM_D : SK_NORM_F), 2.2) * Math.Clamp(1 - 0.03 * (sta - 11), 0.7, 1.3);
                }
                s.Fresh = (float)Math.Max(0, s.Fresh - cost);
            }
    }

    /// <summary>Отдых: прошло days дней.</summary>
    public static void Recover(Career c, int days)
    {
        foreach (var (id, s) in c.Players)
            if (s.Fresh < 100 && c.Data.PlayerById.TryGetValue(id, out var p))
                s.Fresh = (float)Math.Min(100, s.Fresh + days * (p.IsGoalie ? G_REC : SK_REC));
    }

    public static string Word(PlayerState s) => s == null ? "" : s.Fresh >= 85 ? "Свежий" : s.Fresh >= FRESH_OK ? "В норме" : s.Fresh >= 55 ? "Уставший" : "Вымотан";
}
