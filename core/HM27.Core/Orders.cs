namespace HM27.Core;

/// <summary>
/// Указания игрокам (этап «Тренер», шаг 5). У каждого — цена и пригодность: «бросать чаще» добавляет голов снайперу и
/// отнимает у того, кто бросает плохо (реализация в движке — по завершению бросающего); подключение защитника — атака
/// за счёт обороны, и чем лучше он для этого подходит, тем выгоднее размен. Указание не по душе (пригодность ниже −0,5)
/// портит настрой понемногу после каждого матча. Хранятся в Lineup.Orders, накладываются в Sim.Apply (можно менять на паузе).
/// </summary>
public static class Orders
{
    public sealed record Order(string Key, string Name, string What, char Pos, (string k, double w)[] Fit);

    public static readonly Order[] All =
    {
        new("shoot", "Бросать чаще", "Бросает при любой возможности, в том числе издали: голов больше, если бросок хороший", 'A',
            new[] { ("fin", 0.4), ("pwr", 0.3), ("lng", 0.3) }),
        new("pass", "Искать передачу", "Реже бросает сам, чаще отдаёт: партнёрам больше моментов, если видит площадку", 'A',
            new[] { ("pas", 0.5), ("vis", 0.5) }),
        new("net", "Играть у ворот", "Стоит на пятаке: броски ближе, больше добиваний; тяжело — устаёт быстрее", 'F',
            new[] { ("net", 0.5), ("str", 0.3), ("wrk", 0.2) }),
        new("hit", "Силовая борьба", "Больше силовых приёмов: отбирает шайбу, но и чаще садится на скамейку штрафников", 'A',
            new[] { ("hit", 0.4), ("str", 0.3), ("dis", 0.3) }),
        new("back", "Помогать в обороне", "Нападающий раньше возвращается: обороне лучше, атаке хуже", 'F',
            new[] { ("dfn", 0.4), ("wrk", 0.3), ("tka", 0.3) }),
        new("join", "Подключаться в атаку", "Защитник идёт вперёд и бросает: атаке лучше, сзади пусто", 'D',
            new[] { ("pas", 0.35), ("spd", 0.3), ("hnd", 0.2), ("lng", 0.15) }),
        new("stay", "Держать позицию", "Защитник не уходит вперёд: обороне лучше, атаке хуже", 'D',
            new[] { ("dfn", 0.4), ("dec", 0.3), ("blk", 0.3) }),
    };

    public static Order Get(string key) => All.FirstOrDefault(o => o.Key == key);

    public static bool Allowed(Order o, PlayerData p) => !p.IsGoalie && (o.Pos == 'A' || (o.Pos == 'D') == p.IsDefense);

    /// <summary>Пригодность игрока к указанию: нужные характеристики против его средней, в долях 3 ед. шкалы (около −1…+1).</summary>
    public static double Fit(Func<PlayerData, string, double> attr, PlayerData p, Order o) =>
        (o.Fit.Sum(x => x.w * attr(p, x.k)) - Attrs.Skater.Average(a => attr(p, a.Key))) / 3.0;

    public static double Fit(Career c, PlayerData p, Order o) => Fit(TeamStrength.Attr(c, c.DlBase), p, o);

    public static string FitWord(double f) => f >= 0.5 ? "его игра" : f >= 0.15 ? "подходит" : f > -0.15 ? "средне" : f > -0.5 ? "не очень" : "не по душе";

    /// <summary>Сколько настроя стоит указание не по душе после каждого матча.</summary>
    public const double DISLIKE = 0.4;

    /// <summary>Настрой: указания не по душе понемногу раздражают (Morale.AfterGame, свой клуб).</summary>
    public static void AfterGame(Career c, string team, ICollection<int> played)
    {
        var l = c.Lineups[team];
        if (l.Orders == null) return;
        foreach (var (id, key) in l.Orders)
        {
            if (!played.Contains(id) || !c.Data.PlayerById.TryGetValue(id, out var p) || Get(key) is not { } o) continue;
            if (Fit(c, p, o) < -0.5) Morale.AddTo(c, id, -DISLIKE);
        }
    }
}
