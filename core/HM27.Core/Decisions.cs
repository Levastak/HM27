namespace HM27.Core;

/// <summary>Обещание тренера игроку: проверяется в срок по фактам (минуты в своих матчах).</summary>
public sealed class Promise
{
    public int Player { get; set; }
    public string Kind { get; set; }          // minutes
    public DateOnly Since { get; set; }
    public DateOnly Due { get; set; }
    public double Target { get; set; }        // минут за матч
}

/// <summary>
/// Решения тренера: письма с вариантами ответа. Ответ меняет настрой игроков (Morale), иногда — состав и риски.
/// Обязательные решения не пускают «Продолжить» в интерфейсе; у каждого есть срок — после него применяется вариант по умолчанию
/// (так же решаются письма в автоматических прогонах HM27.Check). Только клуб игрока.
/// </summary>
public static class Decisions
{
    static string N(int n, string one, string few, string many)
    {
        int a = Math.Abs(n) % 100, b = a % 10;
        string w = a is > 10 and < 20 ? many : b == 1 ? one : b is >= 2 and <= 4 ? few : many;
        return $"{n} {w}";
    }

    public static Message Ask(Career c, string from, string tag, string headline, string lead, string body, string action, int target,
        List<MsgOption> options, int dueDays, bool required, string team = null)
    {
        var m = InboxWriter.Add(c, from, headline, body, "decision", $"player:{target}");
        m.Tag = tag; m.Lead = lead; m.Action = action; m.Target = target; m.Options = options; m.Required = required;
        m.Due = c.Today.AddDays(dueDays); m.Team = team ?? c.UserTeam; m.Weight = 3;
        if (target != 0) m.Players = new List<int> { target };
        return m;
    }

    public static int Blocking(Career c) => c.Messages.Count(m => m.Required && m.Pending);

    public static void Answer(Career c, Message m, string optionId, bool auto = false)
    {
        if (!m.Pending) return;
        var o = m.Options.FirstOrDefault(x => x.Id == optionId) ?? m.Options.First(x => x.Default);
        m.Choice = o.Id; m.Read = true;
        string reply = m.Action switch
        {
            "talk:minutes" => Minutes(c, m, o.Id),
            "injury:hurt" => Hurt(c, m, o.Id),
            _ when m.Action?.StartsWith("press:") == true => Press.Answer(c, m, o.Id),
            _ => null,
        };
        m.Reply = (auto ? "Ответа не было — решили по умолчанию. " : "") + reply;
    }

    /// <summary>Новый день: просроченные решения — по умолчанию, обещания — сверка, новые поводы для разговоров.</summary>
    public static void Daily(Career c)
    {
        foreach (var m in c.Messages.Where(m => m.Pending && m.Due.HasValue && m.Due.Value < c.Today).ToList())
            Answer(c, m, m.Options.First(x => x.Default).Id, auto: true);
        CheckPromises(c);
        if (c.Phase == Phase.Finished) return;
        MinutesTrigger(c);
        HurtTrigger(c);
        Press.Daily(c);
    }

    // ================================================================ «Мало играю»
    static void MinutesTrigger(Career c)
    {
        // не чаще одного такого разговора в 4 дня
        if (c.Messages.Any(m => m.Action == "talk:minutes" && c.Today.DayNumber - m.Date.DayNumber < 4)) return;
        foreach (var p in c.Roster(c.UserTeam).Where(p => !p.IsGoalie || Morale.Status(c, p) == 0))
        {
            if (!c.Players.TryGetValue(p.Id, out var s) || c.Injured.Contains(p.Id)) continue;
            if (s.Grief < 5.5 || s.Morale > 56) continue;
            if (c.Messages.Any(m => m.Action == "talk:minutes" && m.Target == p.Id && c.Today.DayNumber - m.Date.DayNumber < 30)) continue;
            if (c.Promises.Any(x => x.Player == p.Id)) continue;
            int st = Morale.Status(c, p);
            double exp = p.IsGoalie ? 0 : Morale.ExpectedToi(st, p);
            var recent = RecentToi(c, p, 6, out int played, out int games);
            string first = p.NameRu.Split(' ')[0];
            string body = p.IsGoalie
                ? $"«Тренер, я пришёл сюда быть первым номером. Последние матчи я смотрю со скамейки, и я не понимаю почему.»\n\nСейчас он сыграл {N(played, "матч", "матча", "матчей")} из последних {games}."
                : played < games
                    ? $"«Я здоров, готов, а сижу на трибуне. Мне нужно понимать, на что я могу рассчитывать.»\n\n" +
                      (played == 0 ? $"Из последних {N(games, "матча", "матчей", "матчей")} клуба он не сыграл ни одного." : $"В последних {N(games, "матче", "матчах", "матчах")} клуба он сыграл только {played}.")
                    : $"«Мне не хватает времени на льду. С такими минутами я не могу помочь команде так, как умею.»\n\nВ последних матчах у него в среднем {Toi(recent)} — меньше, чем обычно получает {Morale.StatusWord(st, p)} ({exp:0} мин).";
            Ask(c, p.NameRu, "Разговор", $"{p.LastRu} хочет поговорить о роли",
                $"{first} недоволен временем на льду и пришёл в кабинет. Настрой — {Morale.Word(s.Morale).ToLower()}.",
                body + "\n\nЧто ответить?", "talk:minutes", p.Id,
                new List<MsgOption>
                {
                    new("promise", "Обещаю больше времени", p.IsGoalie ? "Он должен начинать большинство матчей в ближайшие две недели — иначе обида станет сильнее." : $"В ближайшие две недели — не меньше {Math.Max(exp, recent + 2):0} минут за матч. Не сдержите — будет хуже, чем сейчас."),
                    new("earn", "Заслужи место на тренировках", "Профессионалы воспримут это как вызов, остальные — как отказ.", true),
                    new("role", "Твоя роль такая — команда важнее", "Честно, но обидно. Если команда выигрывает, принять легче."),
                }, dueDays: 3, required: false, team: p.Team);
            return;
        }
    }

    static double RecentToi(Career c, PlayerData p, int n, out int played, out int games)
    {
        played = 0; games = 0; double sum = 0;
        foreach (var g in c.TeamGames(p.Team).Where(x => x.Played).OrderByDescending(x => x.Date).Take(n))
        {
            games++;
            var dressed = g.Home == p.Team ? g.Result.HomeDressed : g.Result.AwayDressed;
            if (!dressed.Contains(p.Id)) continue;
            if (p.IsGoalie && (g.Home == p.Team ? g.Result.HomeGoalie : g.Result.AwayGoalie) != p.Id) continue;
            played++;
            sum += g.Result.Box.TryGetValue(p.Id, out var b) && b.Toi > 0 ? b.Toi / 60.0 : c.Lineups[p.Team].MinutesOf(p.Id);
        }
        return played == 0 ? 0 : sum / played;
    }

    static string Toi(double min) => min <= 0 ? "0 минут" : $"{(int)min}:{(int)((min - (int)min) * 60):00}";

    static string Minutes(Career c, Message m, string id)
    {
        if (!c.Data.PlayerById.TryGetValue(m.Target, out var p) || !c.Players.TryGetValue(p.Id, out var s)) return null;
        string first = p.NameRu.Split(' ')[0];
        switch (id)
        {
            case "promise":
                int st = Morale.Status(c, p);
                double target = p.IsGoalie ? 0.55 : Math.Max(Morale.ExpectedToi(st, p) * 0.9, RecentToi(c, p, 6, out _, out _) + 2);
                c.Promises.Add(new Promise { Player = p.Id, Kind = p.IsGoalie ? "starts" : "minutes", Since = c.Today, Due = c.Today.AddDays(14), Target = target });
                Morale.AddTo(c, p.Id, 14); s.Grief = 0; Trust.Add(c, p.Id, 3);
                return p.IsGoalie
                    ? $"{first} ушёл довольным. Обещание: начинать больше половины матчей в ближайшие две недели. Поставьте его основным в «Звеньях»."
                    : $"{first} ушёл довольным. Обещание: не меньше {target:0} минут за матч в ближайшие две недели — поставьте его выше в «Звеньях».";
            case "earn":
            {
                // профессионал примет как вызов; остальные — как отказ; доверие и случай сдвигают реакцию
                double x = (s.Prof - 12) * 0.35 + (Trust.Of(c, p.Id) - Trust.BASE) / 20 + Trust.Luck(c, p.Id, 1);
                bool took = x > 0;
                Morale.AddTo(c, p.Id, took ? 5 : -7); s.Grief *= took ? 0.5f : 0.8f;
                Trust.Add(c, p.Id, took ? 4 : -4);
                return took ? $"{first} кивнул: «Понял. Покажу». Принял как вызов." : $"{first} ушёл молча. Похоже, он ждал другого ответа — доверия к тренеру стало меньше.";
            }
            default:
            {
                bool winning = c.Table[c.UserTeam].PtsPct >= 0.6;
                double x = (winning ? 1 : -0.5) + (s.Loy - 10) * 0.15 - (s.Amb - 10) * 0.2 + Trust.Luck(c, p.Id, 2);
                bool ok = x > 0;
                Morale.AddTo(c, p.Id, ok ? -3 : -10);
                Trust.Add(c, p.Id, ok ? 2 : -6);
                return ok ? $"{first} не в восторге, но спорить не стал: «Ладно. Команда важнее». За честность — уважение."
                          : $"{first} расстроен: «Тогда мне, наверное, стоит подумать о будущем». Настрой заметно упал.";
            }
        }
    }

    static void CheckPromises(Career c)
    {
        foreach (var pr in c.Promises.Where(x => x.Due <= c.Today).ToList())
        {
            c.Promises.Remove(pr);
            if (!c.Data.PlayerById.TryGetValue(pr.Player, out var p) || p.Team != c.UserTeam) continue;
            var games = c.TeamGames(p.Team).Where(g => g.Played && g.Date >= pr.Since && g.Date < pr.Due).ToList();
            if (games.Count == 0) continue;
            bool kept;
            if (pr.Kind == "starts")
                kept = games.Count(g => (g.Home == p.Team ? g.Result.HomeGoalie : g.Result.AwayGoalie) == p.Id) >= pr.Target * games.Count;
            else
            {
                var toi = games.Select(g => g.Result.Box.TryGetValue(p.Id, out var b) ? b.Toi / 60.0 : 0).ToList();
                kept = toi.Average() >= pr.Target * 0.95 || c.Injured.Contains(p.Id);
            }
            Morale.AddTo(c, p.Id, kept ? 8 : -22);
            Trust.Add(c, p.Id, kept ? 12 : -22);
            if (!kept && c.Players.TryGetValue(p.Id, out var s)) s.Grief += 8;
            // раздевалка видит: сдержанное слово — плюс у лидеров, нарушенное — минус и разговор капитана
            Trust.RoomSees(c, p.Id, kept ? 1.5 : -4, kept ? null : $"тренер не сдержал слово, данное {p.LastRu}");
            var m = InboxWriter.Add(c, p.NameRu, kept ? $"{p.LastRu} доволен: обещание выполнено" : $"{p.LastRu}: «Вы обещали»",
                kept ? $"Две недели назад вы пообещали {p.NameRu.Split(' ')[0]} больше времени — и сдержали слово. Он это запомнил."
                     : $"Две недели назад вы пообещали {p.NameRu.Split(' ')[0]} больше времени. Обещание не выполнено, и он этого не скрывает. Доверие к тренеру упало.",
                "decision", $"player:{p.Id}");
            m.Tag = "Разговор"; m.Players = new List<int> { p.Id }; m.Team = p.Team; m.Weight = 2;
            m.Lead = kept ? "Слово тренера в раздевалке кое-что значит." : "Нарушенное обещание бьёт по настрою сильнее, чем честный отказ.";
        }
    }

    // ================================================================ «Сыграю через боль»
    static void HurtTrigger(Career c)
    {
        var g = c.TeamGames(c.UserTeam).Where(x => !x.Played).OrderBy(x => x.Date).FirstOrDefault();
        if (g == null || g.Date.DayNumber - c.Today.DayNumber > 1) return;
        // редкий разговор: не чаще раза в три недели и не всегда
        if (c.Messages.Any(m => m.Action == "injury:hurt" && c.Today.DayNumber - m.Date.DayNumber < 21)) return;
        if (new Random(unchecked(c.Seed * 31 + c.Today.DayNumber)).NextDouble() > 0.5) return;
        foreach (var inj in c.Injuries.Where(i => i.Until >= c.Today && c.Injured.Contains(i.Id)).ToList())
        {
            if (!c.Data.PlayerById.TryGetValue(inj.Id, out var p) || p.Team != c.UserTeam || p.IsGoalie) continue;
            int left = inj.Until.DayNumber - g.Date.DayNumber + 1;   // сколько ещё дней после дня матча
            if (left < 1 || left > 4 || inj.Kind.Contains("сотрясение") || inj.Kind == "болезнь") continue;
            if (c.Messages.Any(m => m.Action == "injury:hurt" && m.Target == p.Id && m.Date >= inj.From)) continue;
            if (Morale.Status(c, p) > 0) continue;   // просятся в игру звёзды
            Ask(c, "Медицинский отдел", "Лазарет", $"{p.LastRu} просится в состав",
                $"{p.NameRu} ещё не долечился ({inj.Kind}), но говорит, что готов сыграть {(g.Date == c.Today ? "сегодня" : "завтра")}.",
                $"Врачи не против, но предупреждают: до полного восстановления {N(left, "день", "дня", "дней")}. Через боль он будет слабее обычного и чаще рискует получить новую травму.\n\nВыпускать?",
                "injury:hurt", p.Id,
                new List<MsgOption>
                {
                    new("play", "Пусть играет", "Вернётся в состав сразу; до полного восстановления — слабее и с повышенным риском травмы."),
                    new("rest", "Пусть долечится", left <= 1 ? "Пропустит ближайший матч, зато вернётся здоровым." : "Пропустит ещё матч-другой, зато вернётся здоровым.", true),
                }, dueDays: Math.Max(0, g.Date.DayNumber - c.Today.DayNumber), required: true);
            return;
        }
    }

    static string Hurt(Career c, Message m, string id)
    {
        if (!c.Data.PlayerById.TryGetValue(m.Target, out var p) || !c.Players.TryGetValue(p.Id, out var s)) return null;
        var inj = c.Injuries.Where(i => i.Id == p.Id && i.Until >= c.Today).OrderByDescending(i => i.Until).FirstOrDefault();
        string first = p.NameRu.Split(' ')[0];
        if (id == "play" && inj != null)
        {
            s.HurtUntil = inj.Until;
            inj.Until = c.Today.AddDays(-1);
            c.Injured.Remove(p.Id);
            var l = c.Lineups[c.UserTeam];
            l.Restore(c.Data, c.UserTeam, c.Injured.ToHashSet());
            l.FixPowerPlay(c.Data);
            Morale.AddTo(c, p.Id, s.Amb >= 13 ? 6 : 3);
            return $"{first} в составе. До {s.HurtUntil.Value.Day}.{s.HurtUntil.Value.Month:00} играет через боль: чуть слабее обычного и с риском новой травмы.";
        }
        Morale.AddTo(c, p.Id, s.Amb >= 15 ? -4 : 0);
        Trust.Add(c, p.Id, s.Amb >= 15 ? -2 : 2);
        return s.Amb >= 15 ? $"{first} недоволен — рвался в бой. Но врачи на вашей стороне." : $"{first} согласился: лучше вернуться здоровым.";
    }
}
