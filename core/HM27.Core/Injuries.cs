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
    public const double SKATER_RATE = 0.0165, GOALIE_RATE = 0.005;

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
                              * Fatigue.InjuryMult(c.Players.GetValueOrDefault(id));
                if (rng.NextDouble() >= rate) continue;
                double u = rng.NextDouble();
                int days = u < 0.5 ? rng.Next(1, 8) : u < 0.82 ? rng.Next(8, 26) : u < 0.96 ? rng.Next(26, 61) : rng.Next(61, 151);
                var kind = Kinds[rng.Next(Kinds.Length)];
                if (kind == "болезнь") days = Math.Min(days, 6);
                c.Injuries.Add(new Injury { Id = id, From = g.Date, Until = g.Date.AddDays(days), Kind = kind });
                Development.SeriousInjury(c, p, days, rng);
                c.Injured.Add(id);
                if (team == c.UserTeam)
                    InboxWriter.Add(c, "Медицинский отдел", $"Травма: {p.NameRu}",
                        $"{p.NameRu} получил травму в матче {Ui(g, c)}: {kind}.\nПрогноз: {Days(days)} (ориентировочно до {c.Today.AddDays(days).Day}.{c.Today.AddDays(days).Month:00}).\n\n" +
                        "Помощник уже заменил его в составе. Проверьте звенья перед следующим матчем.", "medical", $"player:{id}");
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
            // о возвращении пишем только после травм дольше недели — короткие помощник отслеживает сам
            if (c.Data.PlayerById.TryGetValue(i.Id, out var p) && p.Team == c.UserTeam && i.Until.DayNumber - i.From.DayNumber >= 7)
                InboxWriter.Add(c, "Медицинский отдел", $"{p.NameRu} готов играть",
                    $"{p.NameRu} восстановился ({i.Kind}) и может вернуться в состав. Сейчас он в запасе — поставьте его в звено, если нужно.",
                    "medical", $"player:{p.Id}");
        }
    }

    public static Injury Active(Career c, int id) => c.Injuries.Where(i => i.Id == id && i.Until >= c.Today).OrderByDescending(i => i.Until).FirstOrDefault();

    static string Ui(ScheduledGame g, Career c) => $"«{c.Team(g.Home).City}» — «{c.Team(g.Away).City}»";
    static string Days(int d) => d <= 2 ? "день-два" : d <= 7 ? "до недели" : d <= 14 ? "1–2 недели" : d <= 30 ? "2–4 недели" : d <= 60 ? "1–2 месяца" : "больше двух месяцев";
}
