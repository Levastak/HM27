namespace HM27.Core;

/// <summary>
/// Видимая форма игрока — как в FM, по оценкам за последние матчи, но с поправкой на ожидания: у звезды обычная оценка выше,
/// поэтому сравниваем последние 8 матчей с его средней оценкой за сезон (до 10 матчей — с средней по лиге 6,9).
/// </summary>
public static class PlayerForm
{
    public sealed record Info(double Score, string Word, int Arrow, double Recent, int Games);

    static Career _c; static DateOnly _d; static int _n; static Dictionary<int, List<double>> _seq;

    static Dictionary<int, List<double>> Seq(Career c)
    {
        int played = c.Games.Count(g => g.Played);
        if (ReferenceEquals(_c, c) && _d == c.Today && _n == played && _seq != null) return _seq;
        _seq = new();
        foreach (var g in c.Games.Where(g => g.Played && g.Result?.Ratings != null).OrderBy(g => g.Date))
            foreach (var (id, v) in g.Result.Ratings)
            {
                if (!_seq.TryGetValue(id, out var l)) _seq[id] = l = new();
                l.Add(v);
            }
        _c = c; _d = c.Today; _n = played;
        return _seq;
    }

    public static Info Of(Career c, int id)
    {
        if (!Seq(c).TryGetValue(id, out var l) || l.Count < 3) return new Info(0, "—", 0, 0, l?.Count ?? 0);
        var recent = l.TakeLast(8).ToList();
        double mean = (l.Sum() + 10 * 6.9) / (l.Count + 10);
        double score = recent.Average() - mean;
        string word = score >= 0.35 ? "Отличная" : score >= 0.15 ? "Хорошая" : score > -0.15 ? "Обычная" : score > -0.35 ? "Спад" : "Провал";
        int arrow = score >= 0.15 ? 1 : score <= -0.15 ? -1 : 0;
        return new Info(score, word, arrow, recent.Average(), l.Count);
    }
}
