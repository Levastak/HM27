namespace HM27.Core;

public sealed class Injury
{
    public int Id { get; set; }
    public DateOnly From { get; set; }
    /// <summary>Последний день, когда игрок недоступен.</summary>
    public DateOnly Until { get; set; }
    public string Kind { get; set; }
}

/// <summary>
/// Травмы: частота и длительность — по средним НХЛ (клуб теряет около 250–300 игроко-матчей за сезон,
/// большинство травм — до недели, около 15% — больше месяца). Свой генератор случайности, исход матчей не меняет.
/// </summary>
public static class InjuryModel
{
    public static readonly string[] Kinds =
    {
        "травма верхней части тела", "травма нижней части тела", "травма верхней части тела", "травма нижней части тела",
        "сотрясение мозга", "растяжение паховой мышцы", "травма колена", "травма плеча", "травма кисти", "травма голеностопа", "болезнь",
    };
    /// <summary>Частота за матч. Подбор: около 18–19 травм на клуб за сезон (раньше было 25 — слишком часто, много «на пару дней»),
    /// а потерянных игроко-матчей столько же: травмы реже, но в среднем чуть длиннее. Проверка «Травмы» в HM27.Check.</summary>
    public const double SKATER_RATE = 0.0122, GOALIE_RATE = 0.0037;

    public static void AfterGame(Career c, ScheduledGame g, Random rng)
    {
        var r = g.Result;
        foreach (var (dressed, team) in new[] { (r.HomeDressed, g.Home), (r.AwayDressed, g.Away) })
            foreach (var id in dressed)
            {
                if (!c.Data.PlayerById.TryGetValue(id, out var p) || c.Injured.Contains(id)) continue;
                // риск растёт с временем на льду медленнее минут (у лидеров на минуту меньше силовой борьбы): × (мин / 16,7)^0,5; плюс усталость
                double min = r.Box.TryGetValue(id, out var bx) && bx.Toi > 0 ? bx.Toi / 60.0 : c.Lineups.TryGetValue(team, out var ln) ? ln.MinutesOf(id) : 16.7;
                double rate = (p.IsGoalie ? GOALIE_RATE : SKATER_RATE * (0.7 + 0.6 * p.Rating("hit") / 99.0) * Math.Pow(Math.Max(min, 3) / 16.7, 0.5))
                              * Fatigue.InjuryMult(c.Players.GetValueOrDefault(id)) * Training.InjuryMult(c, p)
                              * (c.Players.TryGetValue(id, out var hs) && hs.HurtUntil is { } hu && hu >= c.Today ? Career.HURT_INJ : 1);
                if (rng.NextDouble() >= rate) continue;
                double u = rng.NextDouble();
                int days = u < 0.40 ? rng.Next(2, 8) : u < 0.74 ? rng.Next(8, 26) : u < 0.925 ? rng.Next(26, 61) : rng.Next(61, 151);
                // играл через боль: травма обострилась — дольше, и раздевалка запомнит, что тренер рискнул
                bool aggravated = c.Players.TryGetValue(id, out var hs2) && hs2.HurtUntil is { } hu2 && hu2 >= c.Today;
                if (aggravated) { days = (int)(days * 1.5) + 3; hs2.HurtUntil = null; }
                var kind = Kinds[rng.Next(Kinds.Length)];
                if (kind == "болезнь") days = Math.Min(days, 6);
                c.Injuries.Add(new Injury { Id = id, From = g.Date, Until = g.Date.AddDays(days), Kind = kind });
                Development.SeriousInjury(c, p, days, rng);
                c.Injured.Add(id);
                if (team == c.UserTeam)
                {
                    InboxWriter.Add(c, "Медицинский отдел", aggravated ? $"Травма обострилась: {p.NameRu}" : $"Травма: {p.NameRu}",
                        (aggravated ? $"{p.NameRu} играл через боль — и травма обострилась ({kind}).\n" : $"{p.NameRu} получил травму в матче {Ui(g, c)}: {kind}.\n") +
                        $"Прогноз: {Days(days)} (ориентировочно до {c.Today.AddDays(days).Day}.{c.Today.AddDays(days).Month:00}).\n\n" +
                        "Помощник уже заменил его в составе. Проверьте звенья перед следующим матчем.", "medical", $"player:{id}");
                    if (aggravated) Trust.RoomSees(c, id, -2, $"{p.LastRu} выпустили недолеченным, и теперь он выбыл надолго");
                }
            }
    }

    /// <summary>Новый день: кто восстановился — снова доступен.</summary>
    public static void Heal(Career c)
    {
        var healed = c.Injuries.Where(i => i.Until < c.Today && c.Injured.Contains(i.Id)).ToList();
        foreach (var i in healed)
        {
            // у игрока может быть несколько записей — снимаем, только если активных больше нет
            if (c.Injuries.Any(x => x.Id == i.Id && x.Until >= c.Today)) continue;
            c.Injured.Remove(i.Id);
            if (!c.Data.PlayerById.TryGetValue(i.Id, out var p) || p.Team != c.UserTeam) continue;
            // помощник возвращает вылечившегося на его место (кто заменял — в запас)
            var l = c.Lineups[c.UserTeam];
            var back = l.Restore(c.Data, c.UserTeam, c.Injured.ToHashSet()).Where(x => x.id == p.Id).Select(x => x.key).FirstOrDefault();
            if (back != null) l.FixPowerPlay(c.Data);
            // о возвращении пишем только после травм дольше недели — короткие помощник отслеживает сам
            if (i.Until.DayNumber - i.From.DayNumber >= 7)
                InboxWriter.Add(c, "Медицинский отдел", $"{p.NameRu} готов играть",
                    back != null
                        ? $"{p.NameRu} восстановился ({i.Kind}). Помощник вернул его в состав: {Lineup.SlotName(back)}. Кто его заменял — снова в запасе. Если хотите иначе — раздел «Звенья»."
                        : $"{p.NameRu} восстановился ({i.Kind}) и может вернуться в состав. Сейчас он в запасе — поставьте его в звено, если нужно.",
                    "medical", $"player:{p.Id}");
        }
    }

    public static Injury Active(Career c, int id) => c.Injuries.Where(i => i.Id == id && i.Until >= c.Today).OrderByDescending(i => i.Until).FirstOrDefault();

    static string Ui(ScheduledGame g, Career c) => $"«{c.Team(g.Home).City}» — «{c.Team(g.Away).City}»";
    static string Days(int d) => d <= 2 ? "день-два" : d <= 7 ? "до недели" : d <= 14 ? "1–2 недели" : d <= 30 ? "2–4 недели" : d <= 60 ? "1–2 месяца" : "больше двух месяцев";
}
