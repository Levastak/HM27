namespace HM27.Core;

/// <summary>
/// Как игрок выглядит для клуба (как в FM). Звёзды КА и ПА по умолчанию — относительно своего клуба: в своей группе
/// (нападающие, защитники, вратари) 5★ — уровень лучшего, 3★ — середина основы, 1,5★ — слабейший в основе. Так видно, кто
/// усилит именно твой состав: одна и та же звезда в слабом клубе — 5★, в сильном — 3,5★. Переключатель — «относительно лиги»
/// (абсолютная шкала: КА 180 → 5★, 140 → 3★, 100 → 1★). Абсолютный класс — словами по месту в лиге («звезда НХЛ», «топ-6», «первая пара»).
/// </summary>
public static class StarScale
{
    /// <summary>true — звёзды относительно своего клуба (по умолчанию), false — относительно лиги.</summary>
    public static bool Relative = true;

    public static string Group(PlayerData p) => p.IsGoalie ? "G" : p.IsDefense ? "D" : "F";

    sealed record Anchors(double Top, double Mid, double Low);
    static Career _c; static DateOnly _d; static string _team; static Dictionary<string, Anchors> _a;

    /// <summary>Опоры шкалы клуба: лучший, середина основы, слабейший в основе (12 нападающих, 6 защитников, 2 вратаря).</summary>
    static Dictionary<string, Anchors> Club(Career c)
    {
        if (ReferenceEquals(_c, c) && _d == c.Today && _team == c.UserTeam && _a != null) return _a;
        _a = new();
        foreach (var (g, n) in new[] { ("F", 12), ("D", 6), ("G", 2) })
        {
            var v = c.Data.Roster(c.UserTeam).Where(p => Group(p) == g).Select(p => PlayerModel.Visible(c, p, "ca")).OrderByDescending(x => x).ToList();
            if (v.Count == 0) { _a[g] = new Anchors(160, 130, 110); continue; }
            var reg = v.Take(n).ToList();
            double top = v[0], low = reg[^1], mid = reg[reg.Count / 2];
            if (top - mid < 6) top = mid + 6;
            if (mid - low < 6) low = mid - 6;
            _a[g] = new Anchors(top, mid, low);
        }
        _c = c; _d = c.Today; _team = c.UserTeam;
        return _a;
    }

    /// <summary>Звёзды 0,5–5 для КА/ПА игрока p по текущей шкале.</summary>
    public static double Stars(Career c, PlayerData p, double ca)
    {
        if (!Relative || c == null) return PlayerModel.Stars(ca);
        var a = Club(c)[Group(p)];
        double s = ca >= a.Top ? 5 : ca >= a.Mid ? 3 + 2 * (ca - a.Mid) / (a.Top - a.Mid)
                 : ca >= a.Low ? 1.5 + 1.5 * (ca - a.Low) / (a.Mid - a.Low) : 1.5 - (a.Low - ca) / 15.0;
        return Math.Clamp(Math.Round(s * 2, MidpointRounding.AwayFromZero) / 2, 0.5, 5);
    }

    /// <summary>Для Ui.Stars (звёзды = v / 20).</summary>
    public static int StarsV(Career c, PlayerData p, double ca) => (int)Math.Round(Stars(c, p, ca) * 20);

    // ── абсолютный класс: пороги КА по месту в лиге (по базе: КА из данных у игроков клубов)
    sealed record Cuts(double[] Ca, string[] Words);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameData, Dictionary<string, Cuts>> Cache = new();

    static Dictionary<string, Cuts> Make(GameData d)
    {
        var res = new Dictionary<string, Cuts>();
        foreach (var (g, ranks, words) in new (string, int[], string[])[]
        {
            ("F", new[] { 8, 40, 96, 192, 384 }, new[] { "Суперзвезда лиги", "Звезда НХЛ", "Нападающий первого звена", "Нападающий топ-6", "Нападающий нижних звеньев", "Игрок ротации / уровень АХЛ" }),
            ("D", new[] { 6, 32, 64, 128, 192 }, new[] { "Один из лучших защитников лиги", "Защитник первой пары", "Защитник первой-второй пары", "Защитник топ-4", "Защитник нижней пары", "Игрок ротации / уровень АХЛ" }),
            ("G", new[] { 6, 20, 40, 64, 64 }, new[] { "Один из лучших вратарей лиги", "Основной вратарь высокого уровня", "Основной вратарь", "Сменщик", "Сменщик", "Третий вратарь / уровень АХЛ" }),
        })
        {
            var v = d.Players.Where(p => !string.IsNullOrEmpty(p.Team) && Group(p) == g && p.CaData is { Length: > 0 }).Select(p => p.CaEst).OrderByDescending(x => x).ToList();
            res[g] = new Cuts(ranks.Select(r => v.Count == 0 ? 999 : v[Math.Min(r, v.Count) - 1]).ToArray(), words);
        }
        return res;
    }

    /// <summary>Класс игрока словами по КА (или потолок по ПА).</summary>
    public static string Class(GameData d, PlayerData p, double ca)
    {
        var cut = Cache.GetValue(d, Make)[Group(p)];
        for (int i = 0; i < cut.Ca.Length; i++) if (ca >= cut.Ca[i] - 0.5) return cut.Words[i];
        return cut.Words[^1];
    }
}
