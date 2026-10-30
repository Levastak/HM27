namespace HM27.Core;

/// <summary>
/// Настрой игроков (0–100, «Раздевалка»). Двигают его:
///   • роль и минуты — игрок ждёт времени по своему месту в клубе (звезда, ключевой, основа, ротация); меньше — обида копится,
///     сидит здоровым на трибуне — сильнее;
///   • результаты — победа поднимает всех, кто играл, поражение опускает;
///   • свои оценки за матч;
///   • решения тренера (разговоры, обещания, пресса) — Decisions.
/// Сам по себе настрой медленно возвращается к норме характера. Влияние на игру — небольшой сдвиг уровня (как форма):
/// MOR_K ед. шкалы 1–20 на каждые 40 пунктов отклонения от среднего по лиге, поэтому лига в целом не сильнее и не слабее.
/// Это решение дизайна: в данных НХЛ «настрой» не измерить, поэтому эффект нарочно скромный.
/// </summary>
public static class Morale
{
    public const float BASE = 60;
    public static double MOR_K = 0.3;

    public static string Word(float m) => m >= 80 ? "Отличный" : m >= 67 ? "Хороший" : m >= 47 ? "Нормальный" : m >= 32 ? "Плохой" : "Ужасный";

    /// <summary>Место в клубе по способностям (в своей группе): 0 — звезда, 1 — ключевой, 2 — основа, 3 — ротация.</summary>
    public static int Status(Career c, PlayerData p)
    {
        var grp = c.Roster(p.Team).Where(x => x.IsGoalie == p.IsGoalie && x.IsDefense == p.IsDefense)
            .OrderByDescending(x => PlayerModel.State(c, x.Id)?.Ca ?? x.CaEst).ToList();
        int r = grp.IndexOf(p);
        if (p.IsGoalie) return r == 0 ? 0 : r == 1 ? 2 : 3;
        if (p.IsDefense) return r < 2 ? 0 : r < 4 ? 1 : r < 6 ? 2 : 3;
        return r < 3 ? 0 : r < 6 ? 1 : r < 10 ? 2 : 3;
    }

    public static string StatusWord(int s, PlayerData p) => p.IsGoalie
        ? (s == 0 ? "основной вратарь" : "сменщик")
        : s switch { 0 => "звезда клуба", 1 => "ключевой игрок", 2 => "игрок основы", _ => "игрок ротации" };

    /// <summary>Сколько минут за матч игрок считает своими по статусу.</summary>
    public static double ExpectedToi(int status, PlayerData p) => p.IsDefense
        ? status switch { 0 => 22, 1 => 19, 2 => 16.5, _ => 0 }
        : status switch { 0 => 17.5, 1 => 15.5, 2 => 12.5, _ => 0 };

    /// <summary>Сдвиг уровня в матче от настроя (ед. шкалы 1–20).</summary>
    public static double Effect(Career c, int id)
    {
        if (!c.Players.TryGetValue(id, out var s)) return 0;
        return MOR_K * (s.Morale - c.MoraleMean) / 40.0;
    }

    static void Add(PlayerState s, double d) => s.Morale = (float)Math.Clamp(s.Morale + d, 3, 99);

    /// <summary>После матча: результат, своя оценка, минуты против ожидания (для обоих клубов).</summary>
    public static void AfterGame(Career c, ScheduledGame g)
    {
        var r = g.Result;
        bool homeWon = r.HomeGoals > r.AwayGoals;
        foreach (var (team, dressed, won) in new[] { (g.Home, r.HomeDressed, homeWon), (g.Away, r.AwayDressed, !homeWon) })
        {
            var set = dressed.ToHashSet();
            int goalie = team == g.Home ? r.HomeGoalie : r.AwayGoalie;
            foreach (var p in c.Roster(team))
            {
                if (!c.Players.TryGetValue(p.Id, out var s) || c.Injured.Contains(p.Id)) continue;
                int st = Status(c, p);
                if (set.Contains(p.Id) && (!p.IsGoalie || p.Id == goalie))
                {
                    Add(s, won ? 1.8 : r.End == "REG" ? -1.8 : -0.5);
                    // разгром бьёт сильнее, крупная победа — чуть поднимает (цена раннего снятия вратаря — лишние шайбы в пустые)
                    int margin = Math.Abs(r.HomeGoals - r.AwayGoals);
                    if (margin >= 3) Add(s, (won ? 0.6 : -1.2) * Math.Min(3, margin - 2));
                    if (r.Ratings.TryGetValue(p.Id, out var rt)) Add(s, (rt - 7.0) * 2.2);
                    if (!p.IsGoalie)
                    {
                        double toi = r.Box.TryGetValue(p.Id, out var b) && b.Toi > 0 ? b.Toi / 60.0 : c.Lineups[team].MinutesOf(p.Id);
                        double exp = ExpectedToi(st, p);
                        if (exp > 0 && toi < exp * 0.85) { double d = (exp - toi) / exp * Trust.GriefMult(s); s.Grief += (float)(3 * d); Add(s, -1.6 * d); }
                        else Add(s, 0.2);
                    }
                }
                else if (!p.IsGoalie || st == 0)
                {
                    // здоров, но не играет: звезде и ключевым — обидно, ротация к этому готова
                    double hurt = p.IsGoalie ? 1.2 : st switch { 0 => 3.0, 1 => 2.2, 2 => 1.2, _ => 0.3 };
                    if (p.IsGoalie && GoalieStartShare(c, team, p.Id) >= 0.6) hurt = 0;   // отдых основного — норма
                    hurt *= Trust.GriefMult(s);
                    s.Grief += (float)hurt; Add(s, -hurt);
                }
            }
        }
    }

    static double GoalieStartShare(Career c, string team, int id)
    {
        var last = c.TeamGames(team).Where(x => x.Played).OrderByDescending(x => x.Date).Take(10).ToList();
        if (last.Count == 0) return 1;
        return last.Count(x => (x.Home == team ? x.Result.HomeGoalie : x.Result.AwayGoalie) == id) / (double)last.Count;
    }

    /// <summary>Новый день: настрой тянется к норме характера, обида тает; средний по лиге — для нейтрального эффекта.</summary>
    public static void Daily(Career c)
    {
        double sum = 0; int n = 0;
        foreach (var (id, s) in c.Players)
        {
            float target = BASE + (s.Temp - 10) * 0.6f + (s.Prof - 10) * 0.4f + (float)Trust.MoraleShift(s);
            s.Morale += (target - s.Morale) * 0.03f;
            s.Grief *= 0.93f;
            sum += s.Morale; n++;
        }
        c.MoraleMean = n == 0 ? BASE : (float)(sum / n);
    }

    /// <summary>Настрой команды: средний по заявке.</summary>
    public static float Team(Career c, string abbr)
    {
        var ids = c.Lineups[abbr].All.ToList();
        var v = ids.Select(id => c.Players.TryGetValue(id, out var s) ? s.Morale : BASE).ToList();
        return v.Count == 0 ? BASE : v.Average();
    }

    public static void AddTo(Career c, int id, double d) { if (c.Players.TryGetValue(id, out var s)) Add(s, d); }
}
