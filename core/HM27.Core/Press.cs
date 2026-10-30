namespace HM27.Core;

/// <summary>
/// Пресс-конференции (этап «Тренер», шаг 7): после разгрома, перед большим матчем, о спаде игрока. Ответ на камеры слышит
/// раздевалка: публичная критика заводит профессионала и ломает ранимого, громкие слова перед матчем заряжают своих,
/// но и соперника, — а после матча их припомнят. Реакция зависит от скрытого характера и немного от случая.
/// Не обязательны: без ответа — «общие слова», ничего не меняется. Не чаще одной в неделю.
/// </summary>
public static class Press
{
    const string FROM = "Пресс-служба клуба", TAG = "Пресс-конференция";

    public static void Daily(Career c)
    {
        if (c.Phase == Phase.Finished) return;
        if (c.Messages.Any(m => m.Action?.StartsWith("press:") == true && c.Today.DayNumber - m.Date.DayNumber < 7)) return;
        if (AfterBlowout(c) || BeforeBigGame(c) || Slump(c)) return;
    }

    static Message Ask(Career c, string action, int target, string headline, string lead, string body, List<MsgOption> options)
    {
        var m = Decisions.Ask(c, FROM, TAG, headline, lead, body, action, target, options, dueDays: 1, required: false);
        if (target == 0) m.Players = null;
        return m;
    }

    // ------------------------------------------------------------------ после разгрома
    static bool AfterBlowout(Career c)
    {
        var g = c.LastGame(c.UserTeam);
        if (g == null || g.Date.DayNumber < c.Today.DayNumber - 1) return false;
        bool home = g.Home == c.UserTeam;
        int gf = home ? g.Result.HomeGoals : g.Result.AwayGoals, ga = home ? g.Result.AwayGoals : g.Result.HomeGoals;
        if (ga - gf < 3 || c.Messages.Any(m => m.Action == "press:blowout" && m.Target == g.Id)) return false;
        var opp = c.Data.TeamByAbbr[home ? g.Away : g.Home];
        var m = Ask(c, "press:blowout", 0, $"Журналисты о поражении {gf}:{ga}",
            $"После матча с «{opp.Name}» журналисты ждут объяснений.",
            $"«Тренер, {gf}:{ga} — что случилось с командой?»\n\nЧто сказать на камеры?",
            new List<MsgOption>
            {
                new("self", "Это моя ответственность", "Защитить игроков: доверие к тренеру вырастет, особенно у лидеров."),
                new("demand", "Игроки должны добавить", "Публично потребовать большего: профессионалов это заведёт, ранимых — обидит."),
                new("generic", "Отделаться общими словами", "Ничего не изменится.", true),
            });
        m.Target = g.Id;
        return true;
    }

    // ------------------------------------------------------------------ перед большим матчем
    static bool BeforeBigGame(Career c)
    {
        var g = c.NextGame(c.UserTeam);
        if (g == null || g.Date.DayNumber - c.Today.DayNumber > 1) return false;
        if (c.Messages.Any(m => m.Action == "press:big" && c.Today.DayNumber - m.Date.DayNumber < 14)) return false;
        string opp = g.Home == c.UserTeam ? g.Away : g.Home;
        var order = c.Sorted(_ => true).Select(r => r.Team).ToList();
        int place = order.IndexOf(opp) + 1;
        if (place == 0 || place > 4 || c.Table[c.UserTeam].Gp < 5) return false;
        var o = c.Preview(g);
        double pw = g.Home == c.UserTeam ? o.PHome : 1 - o.PHome;
        var t = c.Data.TeamByAbbr[opp];
        Ask(c, "press:big", 0, $"Перед матчем с «{t.Name}»",
            $"«{t.Name}» — {place}-е место в лиге. Журналисты спрашивают о завтрашнем матче.",
            $"«Как настроены на матч с одним из лидеров?»\n\nПо прогнозу шансы на победу — {100 * pw:0}%.\n\nЧто ответить?",
            new List<MsgOption>
            {
                new("underdog", "Нас никто не ждёт", "Снять давление: команде легче, если она и правда не фаворит."),
                new("boast", "Мы сильнее — и докажем", "Зарядить своих — но и соперника; после матча слова припомнят."),
                new("respect", "Уважаем соперника, готовимся", "Спокойный ответ: ничего не изменится.", true),
            }).Target = g.Id;
        return true;
    }

    // ------------------------------------------------------------------ спад игрока
    static bool Slump(Career c)
    {
        foreach (var p in c.Roster(c.UserTeam).Where(p => !p.IsGoalie && !c.Injured.Contains(p.Id)))
        {
            if (Morale.Status(c, p) > 0) continue;
            var f = PlayerForm.Of(c, p.Id);
            if (f.Games < 5 || f.Word is not ("Спад" or "Провал")) continue;
            if (c.Messages.Any(m => m.Action == "press:slump" && m.Target == p.Id && c.Today.DayNumber - m.Date.DayNumber < 30)) continue;
            var st = c.Skaters.GetValueOrDefault(p.Id);
            Ask(c, "press:slump", 0, $"Журналисты о спаде {p.LastRu}",
                $"{p.NameRu} не в форме, и это заметили.",
                $"«Что происходит с {p.LastRu}? {(st != null ? $"У него {st.G} + {st.A} в {st.Gp} матчах." : "")} Он ещё нужен команде в таком виде?»\n\nЧто ответить?",
                new List<MsgOption>
                {
                    new("back", "Полностью ему доверяем", "Поддержать: настрой и доверие игрока вырастут."),
                    new("demand", "Ему нужно добавить", "Публичный вызов: одних это зажигает, других ломает — и раздевалка видит."),
                    new("none", "Без комментариев", "Ничего не изменится.", true),
                }).Also(m => { m.Target = p.Id; m.Players = new List<int> { p.Id }; });
            return true;
        }
        return false;
    }


    // ------------------------------------------------------------------ ответы
    public static string Answer(Career c, Message m, string id)
    {
        var roster = c.Roster(c.UserTeam).Where(p => c.Players.ContainsKey(p.Id)).ToList();
        switch (m.Action)
        {
            case "press:blowout":
                if (id == "self")
                {
                    foreach (var p in roster) { Trust.Add(c, p.Id, 2); Morale.AddTo(c, p.Id, 1); }
                    foreach (var p in Trust.Leaders(c)) Trust.Add(c, p.Id, 2);
                    return "Игроки оценили: тренер не стал искать виноватых. Доверие в раздевалке выросло.";
                }
                if (id == "demand")
                {
                    int up = 0, down = 0;
                    foreach (var p in roster)
                    {
                        var s = c.Players[p.Id];
                        double x = (s.Prof - 11) * 0.3 + (s.Temp - 10) * 0.05 + Trust.Luck(c, p.Id, 11);
                        if (x > 0) { Morale.AddTo(c, p.Id, 3); up++; } else { Morale.AddTo(c, p.Id, -3); Trust.Add(c, p.Id, -3); down++; }
                    }
                    return $"Слова дошли до раздевалки: {up} {Pl(up)} приняли их как вызов, {down} — обиделись.";
                }
                return "Общие слова — ни шума, ни пользы.";

            case "press:big":
                if (id == "underdog")
                {
                    var g = c.Games.FirstOrDefault(x => x.Id == m.Target);
                    double pw = g == null ? 0.5 : (g.Home == c.UserTeam ? c.Preview(g).PHome : 1 - c.Preview(g).PHome);
                    foreach (var p in roster) Morale.AddTo(c, p.Id, pw < 0.45 ? 2 : -1);
                    return pw < 0.45 ? "Давление снято: команда выходит без груза ожиданий." : "Игроки удивились: они-то считают, что сильнее. Эффект обратный.";
                }
                if (id == "boast")
                {
                    foreach (var p in roster) Morale.AddTo(c, p.Id, 3);
                    var g = c.Games.FirstOrDefault(x => x.Id == m.Target);
                    if (g != null)
                    {
                        string opp = g.Home == c.UserTeam ? g.Away : g.Home;
                        foreach (var p in c.Roster(opp)) Morale.AddTo(c, p.Id, 3);
                        c.Boasts.Add(g.Id);
                    }
                    return "Команда заряжена. Соперник ваши слова тоже прочитал — и после матча их припомнят.";
                }
                return "Спокойный ответ — без последствий.";

            case "press:slump":
            {
                if (!c.Data.PlayerById.TryGetValue(m.Target, out var p) || !c.Players.TryGetValue(p.Id, out var s)) return null;
                string first = p.NameRu.Split(' ')[0];
                if (id == "back")
                {
                    double x = 1 + (s.Loy - 10) * 0.1 + 0.5 * Trust.Luck(c, p.Id, 21);
                    Morale.AddTo(c, p.Id, x > 0 ? 6 : 2); Trust.Add(c, p.Id, x > 0 ? 5 : 2);
                    return x > 0 ? $"{first} прочитал и пришёл сказать спасибо. Настрой и доверие выросли." : $"{first} принял поддержку сдержанно — но это было не лишним.";
                }
                if (id == "demand")
                {
                    double x = (s.Prof - 12) * 0.35 + (s.Amb - 10) * 0.15 + Trust.Luck(c, p.Id, 22);
                    if (x > 0)
                    {
                        Morale.AddTo(c, p.Id, 4); s.Form = Math.Min(1f, s.Form + 0.15f);
                        return $"{first} ответил на тренировке: злее обычного. Похоже, вызов сработал.";
                    }
                    Morale.AddTo(c, p.Id, -8); Trust.Add(c, p.Id, -8);
                    Trust.RoomSees(c, p.Id, -1.5, $"тренер публично раскритиковал {p.LastRu}");
                    return $"{first} обиделся: «Мог бы сказать мне в лицо». Раздевалка это обсуждает.";
                }
                return "Без комментариев — тема ушла сама.";
            }
        }
        return null;
    }

    /// <summary>После матча, перед которым тренер «обещал доказать»: победа — плюс, поражение — минус всем.</summary>
    public static void AfterGame(Career c, ScheduledGame g)
    {
        if (!c.Boasts.Remove(g.Id)) return;
        bool home = g.Home == c.UserTeam;
        bool won = (g.Result.HomeGoals > g.Result.AwayGoals) == home;
        foreach (var p in c.Roster(c.UserTeam)) Morale.AddTo(c, p.Id, won ? 2 : -5);
        if (!won) foreach (var p in Trust.Leaders(c)) Trust.Add(c, p.Id, -2);
    }

    static string Pl(int n) => n % 10 == 1 && n % 100 != 11 ? "игрок" : n % 10 is >= 2 and <= 4 && (n % 100 < 12 || n % 100 > 14) ? "игрока" : "игроков";

    static Message Also(this Message m, Action<Message> a) { a(m); return m; }
}
