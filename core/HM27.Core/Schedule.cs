namespace HM27.Core;

public sealed class ScheduledGame
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Home { get; set; }
    public string Away { get; set; }
    public bool Playoff { get; set; }
    public MatchResult Result { get; set; }
    public bool Played => Result != null;
}

/// <summary>
/// Календарь регулярного чемпионата по формуле НХЛ: 82 матча, 41 дома.
/// Свой дивизион — 26 (пятерым соперникам по 4, двоим по 3), своя конференция — 24 (по 3),
/// другая конференция — 32 (по 2). Даты — с начала октября до середины апреля, не больше двух дней подряд.
/// </summary>
public static class ScheduleMaker
{
    public static List<ScheduledGame> Make(GameData data, int season, int seed)
    {
        var rng = new Random(seed);
        var pairs = new List<(string home, string away)>();
        var teams = data.Teams;
        var byDiv = teams.GroupBy(t => t.Division).ToDictionary(g => g.Key, g => g.OrderBy(_ => rng.Next()).ToList());
        var byConf = teams.GroupBy(t => t.Conference).ToDictionary(g => g.Key, g => g.Select(t => t.Division).Distinct().OrderBy(x => x).ToList());

        // Свой дивизион: «кольцо» из 8 клубов — соседям по кольцу 3 матча (2 дома у того, кто раньше в кольце), остальным 4
        foreach (var div in byDiv.Values)
        {
            int n = div.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    string a = div[i].Abbr, b = div[j].Abbr;
                    bool ring = j == i + 1 || (i == 0 && j == n - 1);
                    if (ring)
                    {
                        // хозяин двух матчей — тот, у кого соседство «вперёд» по кольцу
                        var (h2, h1) = (j == i + 1) ? (a, b) : (b, a);
                        pairs.Add((h2, h1)); pairs.Add((h2, h1)); pairs.Add((h1, h2));
                    }
                    else { pairs.Add((a, b)); pairs.Add((a, b)); pairs.Add((b, a)); pairs.Add((b, a)); }
                }
        }
        // Своя конференция, другой дивизион: по 3 матча, хозяин двух — по чётности индексов
        foreach (var (_, divs) in byConf)
        {
            var A = byDiv[divs[0]]; var B = byDiv[divs[1]];
            for (int i = 0; i < A.Count; i++)
                for (int j = 0; j < B.Count; j++)
                {
                    string a = A[i].Abbr, b = B[j].Abbr;
                    if ((i + j) % 2 == 0) { pairs.Add((a, b)); pairs.Add((a, b)); pairs.Add((b, a)); }
                    else { pairs.Add((b, a)); pairs.Add((b, a)); pairs.Add((a, b)); }
                }
        }
        // Другая конференция: дома и в гостях
        var confs = byConf.Keys.OrderBy(x => x).ToList();
        foreach (var a in teams.Where(t => t.Conference == confs[0]))
            foreach (var b in teams.Where(t => t.Conference == confs[1]))
            { pairs.Add((a.Abbr, b.Abbr)); pairs.Add((b.Abbr, a.Abbr)); }

        return AssignDates(pairs, season, rng);
    }

    static List<ScheduledGame> AssignDates(List<(string home, string away)> pairs, int season, Random rng)
    {
        var left = pairs.OrderBy(_ => rng.Next()).ToList();
        var remaining = new Dictionary<string, int>();
        foreach (var (h, a) in left) { remaining[h] = remaining.GetValueOrDefault(h) + 1; remaining[a] = remaining.GetValueOrDefault(a) + 1; }
        var lastDay = new Dictionary<string, DateOnly>();
        var streak = new Dictionary<string, int>();
        var lastPair = new Dictionary<(string, string), DateOnly>();
        var result = new List<ScheduledGame>();
        var day = new DateOnly(season, 10, 6);
        var end = new DateOnly(season + 1, 4, 15);
        int totalDays = end.DayNumber - day.DayNumber + 1;
        int id = 1;

        while (left.Count > 0)
        {
            // перерыв на Матч звёзд — неделя в начале февраля
            if (day >= new DateOnly(season + 1, 2, 1) && day <= new DateOnly(season + 1, 2, 7)) { day = day.AddDays(1); continue; }
            int daysLeft = Math.Max(1, end.DayNumber - day.DayNumber + 1);
            int cap = day.DayOfWeek switch
            {
                DayOfWeek.Saturday => 11, DayOfWeek.Tuesday or DayOfWeek.Thursday => 9,
                DayOfWeek.Sunday => 6, _ => 4,
            };
            // подтягиваем темп, если отстаём
            int needPerDay = (int)Math.Ceiling(left.Count / (double)daysLeft);
            cap = Math.Max(cap, Math.Min(16, needPerDay));
            var busy = new HashSet<string>();
            var today = new List<int>();
            var order = Enumerable.Range(0, left.Count)
                .OrderByDescending(i => remaining[left[i].home] + remaining[left[i].away] + rng.NextDouble() * 6
                                       - (Yesterday(left[i].home) ? 1 : 0) - (Yesterday(left[i].away) ? 1 : 0)).ToList();
            foreach (var i in order)
            {
                if (today.Count >= cap) break;
                var (h, a) = left[i];
                if (busy.Contains(h) || busy.Contains(a)) continue;
                if (!CanPlay(h) || !CanPlay(a)) continue;
                var key = string.CompareOrdinal(h, a) < 0 ? (h, a) : (a, h);
                if (lastPair.TryGetValue(key, out var lp) && day.DayNumber - lp.DayNumber < 3 && daysLeft > 10) continue;
                busy.Add(h); busy.Add(a); today.Add(i);
            }
            foreach (var i in today.OrderByDescending(x => x))
            {
                var (h, a) = left[i];
                result.Add(new ScheduledGame { Id = season * 1_000_000 + 20_000 + id++, Date = day, Home = h, Away = a });
                foreach (var t in new[] { h, a })
                {
                    streak[t] = lastDay.TryGetValue(t, out var ld) && ld == day.AddDays(-1) ? streak.GetValueOrDefault(t) + 1 : 1;
                    lastDay[t] = day; remaining[t]--;
                }
                lastPair[string.CompareOrdinal(h, a) < 0 ? (h, a) : (a, h)] = day;
                left.RemoveAt(i);
            }
            day = day.AddDays(1);
        }
        // Номера матчей по порядку дат, как у НХЛ
        var sorted = result.OrderBy(g => g.Date).ThenBy(g => g.Home).ToList();
        for (int i = 0; i < sorted.Count; i++) sorted[i].Id = season * 1_000_000 + 20_000 + i + 1;
        return sorted;

        bool Yesterday(string t) => lastDay.TryGetValue(t, out var ld) && ld == day.AddDays(-1);
        bool CanPlay(string t) =>
            !(lastDay.TryGetValue(t, out var ld) && ld == day.AddDays(-1) && streak.GetValueOrDefault(t) >= 2);
    }
}
