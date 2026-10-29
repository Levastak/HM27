using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Показатели клубов сезона в игре: голы, броски, ожидаемые голы, реализация, вратари, вбрасывания, силовые.</summary>
public static class TeamNumbers
{
    public sealed record Row(string Team, int Gp, double PtsPct, double Gf, double Ga, double Sf, double Sa, double XgF, double XgA, double Ppg, double Fo, double Sv, double Hits)
    {
        public double XgPct => XgF + XgA <= 0 ? 0.5 : XgF / (XgF + XgA);
        public double ShPct => Sf <= 0 ? 0 : Gf / Sf;
        public double Pdo => ShPct + Sv;
        public double Luck => (Gf - XgF) - (Ga - XgA);
    }

    public static List<Row> All(Career c) =>
        News.TeamStats(c).Select(x => new Row(x.Team, x.Gp, c.Table[x.Team].PtsPct, x.Gf, x.Ga, x.Sf, x.Sa, x.XgF, x.XgA, x.Ppg, x.Fo, x.Sv, x.Hits)).ToList();

    static string F1(double v) => v.ToString("0.0");
    static string F2(double v) => v.ToString("0.00");
    static string P3(double v) => v.ToString(".000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Перцентили клуба против лиги (все 32 клуба).</summary>
    public static List<PlayerViz.Pct> Percentiles(List<Row> all, Row me)
    {
        var res = new List<PlayerViz.Pct>();
        void A(string name, Func<Row, double> f, Func<double, string> fmt, bool low = false, string tip = null)
        {
            double v = f(me);
            var pool = all.Select(f).ToList();
            double below = pool.Count(x => low ? x > v : x < v), eq = pool.Count(x => Math.Abs(x - v) < 1e-9);
            res.Add(new PlayerViz.Pct(name, fmt(v), (below + 0.5 * eq) / pool.Count, tip));
        }
        A("Очки, % от возможных", x => x.PtsPct, v => $"{v * 100:0}%");
        A("Голы за матч", x => x.Gf, F2);
        A("Пропущенные за матч", x => x.Ga, F2, true, "Меньше — лучше");
        A("Броски в створ за матч", x => x.Sf, F1);
        A("Броски соперника за матч", x => x.Sa, F1, true, "Меньше — лучше");
        A("Ожидаемые голы за матч", x => x.XgF, F2, false, "Качество моментов: xG бросков в створ");
        A("Ожидаемые голы соперника", x => x.XgA, F2, true, "Меньше — лучше");
        A("Доля xG", x => x.XgPct, v => $"{v * 100:0.0}%", false, "Свои ожидаемые голы против ожидаемых голов соперника: больше 50% — клуб создаёт больше, чем позволяет");
        A("Реализация бросков", x => x.ShPct, v => $"{v * 100:0.0}%");
        A("% отражённых", x => x.Sv, P3);
        A("PDO", x => x.Pdo, v => $"{v * 1000:0}", false, "Реализация + % отражённых: около 1000 — норма, выше — везёт (обычно ненадолго)");
        A("Голы в большинстве", x => x.Ppg, v => $"{v:0}");
        A("Выигранные вбрасывания", x => x.Fo, v => $"{v * 100:0.0}%");
        A("Силовые за матч", x => x.Hits, F1);
        return res;
    }
}

/// <summary>Вкладка клуба «Статистика»: плитки, клуб на фоне лиги, разбор «игра или везение», полевые игроки.</summary>
public partial class TeamStatsPage : Control
{
    Career C => P.C;
    readonly string _abbr;
    public TeamStatsPage() { }
    public TeamStatsPage(string abbr) { _abbr = abbr; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        var v = Ui.V(12);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        var all = TeamNumbers.All(C);
        var me = all.FirstOrDefault(x => x.Team == _abbr);
        if (me == null) { v.AddChild(Widget.Make("Статистика клуба", Ui.L("Сезон ещё не начался", 13, Ui.Muted))); return; }
        int Rank(Func<TeamNumbers.Row, double> f, bool low = false) => (low ? all.OrderBy(f) : all.OrderByDescending(f)).ToList().FindIndex(x => x.Team == _abbr) + 1;

        var tiles = Ui.H(10);
        void T(string icon, string cap, string val, int rank, string tip)
        {
            var col = rank <= 8 ? Ui.Win : rank >= all.Count - 7 ? Ui.Loss : Ui.Text;
            tiles.AddChild(PlayerViz.Tile(icon, cap, val, col, $"{rank}-е в лиге", tip));
        }
        T("trophy", "Очки", $"{me.PtsPct * 100:0}%", Rank(x => x.PtsPct), "Доля набранных очков от возможных");
        T("target", "Голы за матч", me.Gf.ToString("0.00"), Rank(x => x.Gf), null);
        T("shield", "Пропущенные за матч", me.Ga.ToString("0.00"), Rank(x => x.Ga, true), null);
        T("chart-column", "Доля xG", $"{me.XgPct * 100:0.0}%", Rank(x => x.XgPct), "Качество моментов: свои ожидаемые голы против ожидаемых голов соперника");
        T("gauge", "PDO", $"{me.Pdo * 1000:0}", Rank(x => x.Pdo), "Реализация + % отражённых. Около 1000 — норма");
        v.AddChild(tiles);

        var row = Ui.H(12);
        row.AddChild(Ui.Expand(Widget.Make("На фоне лиги", PlayerViz.PercentileList(TeamNumbers.Percentiles(all, me), $"Среди {all.Count} клубов"))));
        var right = Ui.V(12); right.CustomMinimumSize = new Vector2(520, 0);
        right.AddChild(Widget.Make("Игра или везение?", Luck(me)));
        right.AddChild(Widget.Make("Голы и ожидаемые голы", GoalsVsXg(me)));
        row.AddChild(right);
        v.AddChild(row);
        v.AddChild(Skaters());
    }

    Control Luck(TeamNumbers.Row me)
    {
        var v = Ui.V(8);
        double luck = me.Luck;
        string verdict = Math.Abs(luck) < 0.25 ? "Результаты честные: голы сходятся с моментами." :
            luck > 0 ? "Клуб забивает и спасается лучше, чем создаёт и позволяет. Часть очков — реализация и вратарь; такое редко держится весь сезон." :
                       "Результаты хуже игры: моментов больше, чем голов. На дистанции такое обычно выравнивается.";
        v.AddChild(Ui.H(10, Ui.L(Ui.Signed(luck, "0.00"), 34, luck >= 0 ? Ui.Win : Ui.Loss, Ui.CondBold), Ui.Expand(Ui.L("гола за матч сверх моментов (свои минус чужие)", 12, Ui.Muted).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart))));
        v.AddChild(Ui.L(verdict, 13, Ui.Text).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        v.AddChild(Ui.L($"Реализация {me.ShPct * 100:0.0}% · % отражённых {me.Sv.ToString(".000", System.Globalization.CultureInfo.InvariantCulture)} · PDO {me.Pdo * 1000:0}", 12, Ui.Muted));
        return v;
    }

    Control GoalsVsXg(TeamNumbers.Row me)
    {
        var v = Ui.V(10);
        Control Pair(string name, double g, double x, bool attack)
        {
            double max = Math.Max(4.5, Math.Max(g, x));
            var h = Ui.V(2);
            h.AddChild(Ui.H(8, Ui.L(name, 13), Ui.Spacer(), Ui.L($"{g:0.00} голов · {x:0.00} xG", 13, Ui.Muted)));
            h.AddChild(new BarPair(g / max, x / max, attack ? Ui.Blue : Ui.Red));
            return h;
        }
        v.AddChild(Pair("Забиваем", me.Gf, me.XgF, true));
        v.AddChild(Pair("Пропускаем", me.Ga, me.XgA, false));
        v.AddChild(Ui.L("Сплошная полоса — голы, контур — ожидаемые голы бросков в створ (за матч).", 11, Ui.Dim));
        return v;
    }

    Control Skaters()
    {
        var rows = C.Skaters.Where(kv => C.Data.PlayerById.TryGetValue(kv.Key, out var p) && p.Team == _abbr && kv.Value.Gp > 0)
            .Select(kv => (p: C.Data.PlayerById[kv.Key], s: kv.Value)).ToList();
        string Toi(SkaterLine s) => s.Gp > 0 && s.Toi > 0 ? Ui.Toi(s.Toi / (double)s.Gp) : "—";
        var cols = new List<Col<(PlayerData p, SkaterLine s)>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Links.Player(x.p, null, 13), Sort = x => x.p.LastRu, Desc = false },
            new() { Title = "Поз", Width = 36, Cell = x => Ui.L(Ui.PosRu(x.p.Pos), 12, Ui.Muted) },
            new() { Title = "И", Width = 34, Cell = x => Ui.L($"{x.s.Gp}", 12, Ui.Muted), Sort = x => x.s.Gp },
            new() { Title = "Г", Width = 34, Cell = x => Ui.L($"{x.s.G}", 13), Sort = x => x.s.G },
            new() { Title = "П", Width = 34, Cell = x => Ui.L($"{x.s.A}", 13), Sort = x => x.s.A },
            new() { Title = "О", Width = 38, Cell = x => Ui.L($"{x.s.Pts}", 13, Ui.Text, Ui.Bold), Sort = x => x.s.Pts },
            new() { Title = "xG", Width = 46, Tip = "Ожидаемые голы его бросков в створ", Cell = x => Ui.L(x.s.Ixg.ToString("0.0"), 13, Ui.Muted), Sort = x => x.s.Ixg },
            new() { Title = "Г−xG", Width = 50, Tip = "Забил больше или меньше, чем средний игрок с тех же бросков", Cell = x => { double d = x.s.G - x.s.Ixg; return Ui.L(Ui.Signed(d, "0.0"), 13, d >= 1 ? Ui.Win : d <= -1 ? Ui.Loss : Ui.Muted); }, Sort = x => x.s.G - x.s.Ixg },
            new() { Title = "Бр", Width = 38, Tip = "Броски в створ", Cell = x => Ui.L($"{x.s.S}", 12, Ui.Muted), Sort = x => x.s.S },
            new() { Title = "+/−", Width = 40, Cell = x => Ui.L(Ui.Signed(x.s.Pm, "0"), 12, x.s.Pm > 0 ? Ui.Win : x.s.Pm < 0 ? Ui.Loss : Ui.Muted), Sort = x => x.s.Pm },
            new() { Title = "Бол", Width = 38, Tip = "Очки в большинстве", Cell = x => Ui.L($"{x.s.Ppp}", 12, Ui.Muted), Sort = x => x.s.Ppp },
            new() { Title = "Вр/И", Width = 52, Tip = "Время на льду за матч", Cell = x => Ui.L(Toi(x.s), 12, Ui.Muted), Sort = x => x.s.Gp > 0 ? x.s.Toi / (double)x.s.Gp : 0 },
            new() { Title = "Блк", Width = 38, Cell = x => Ui.L($"{x.s.Blk}", 12, Ui.Muted), Sort = x => x.s.Blk },
            new() { Title = "Сил", Width = 38, Cell = x => Ui.L($"{x.s.Hit}", 12, Ui.Muted), Sort = x => x.s.Hit },
            new() { Title = "Отб", Width = 38, Tip = "Отборы", Cell = x => Ui.L($"{x.s.Tk}", 12, Ui.Muted), Sort = x => x.s.Tk },
            new() { Title = "Пот", Width = 38, Tip = "Потери", Cell = x => Ui.L($"{x.s.Gv}", 12, Ui.Muted), Sort = x => x.s.Gv, Desc = false },
            new() { Title = "Вбр", Width = 52, Tip = "Выигранные вбрасывания", Cell = x => Ui.L(x.s.Fow + x.s.Fol >= 20 ? $"{x.s.FoPct * 100:0.0}%" : "—", 12, Ui.Muted), Sort = x => x.s.Fow + x.s.Fol >= 20 ? x.s.FoPct : 0 },
            new() { Title = "Ср. оц.", Width = 54, Cell = x => Ui.L(x.s.Rated > 0 ? $"{x.s.AvgRating:0.00}" : "—", 12), Sort = x => x.s.AvgRating },
        };
        var grid = new Grid<(PlayerData p, SkaterLine s)>(cols, rows, x => Shell.I.Go("player", null, x.p.Id.ToString()), x => false, 5);
        return Widget.Make("Полевые игроки", grid.Root, null, null, 0);
    }
}

/// <summary>Две полосы: сплошная (факт) поверх контура (ожидание).</summary>
public partial class BarPair : Control
{
    readonly double _a, _b;
    readonly Color _c;
    public BarPair() { }
    public BarPair(double a, double b, Color c) { _a = a; _b = b; _c = c; CustomMinimumSize = new Vector2(100, 18); MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        float w = Size.X, h = 12, y = (Size.Y - h) / 2;
        DrawRect(new Rect2(0, y, w, h), new Color(Ui.Line, 0.5f));
        DrawRect(new Rect2(0, y, (float)(w * Math.Clamp(_a, 0, 1)), h), new Color(_c, 0.85f));
        DrawRect(new Rect2(0, y - 2, (float)(w * Math.Clamp(_b, 0, 1)), h + 4), Ui.Text, false, 1.5f);
    }
}

/// <summary>Лига → «Клубы»: все 32 клуба по продвинутым показателям, сортировка по любой колонке.</summary>
public partial class LeagueTeamsPage : Control
{
    Career C => P.C;
    public LeagueTeamsPage() { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        var v = Ui.V(10);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        var all = TeamNumbers.All(C);
        if (all.Count == 0) { v.AddChild(Widget.Make("Клубы лиги", Ui.L("Сезон ещё не начался", 13, Ui.Muted))); return; }
        static Color Rel(double v, double mean, bool low = false) => (low ? mean - v : v - mean) > 0.04 * Math.Abs(mean) ? Ui.Win : (low ? v - mean : mean - v) > 0.04 * Math.Abs(mean) ? Ui.Loss : Ui.Text;
        double mGf = all.Average(x => x.Gf), mGa = all.Average(x => x.Ga), mXg = all.Average(x => x.XgF), mXa = all.Average(x => x.XgA);
        var cols = new List<Col<TeamNumbers.Row>>
        {
            new() { Title = "Клуб", Width = 0, Align = HorizontalAlignment.Left, Cell = x => P.Team(x.Team), Sort = x => C.Team(x.Team).City, Desc = false },
            new() { Title = "И", Width = 34, Cell = x => Ui.L($"{x.Gp}", 12, Ui.Muted), Sort = x => x.Gp },
            new() { Title = "Очки%", Width = 56, Tip = "Доля набранных очков от возможных", Cell = x => Ui.L($"{x.PtsPct * 100:0}%", 13, Ui.Text, Ui.Bold), Sort = x => x.PtsPct },
            new() { Title = "ГЗ/И", Width = 52, Tip = "Голы за матч", Cell = x => Ui.L(x.Gf.ToString("0.00"), 13, Rel(x.Gf, mGf)), Sort = x => x.Gf },
            new() { Title = "ГП/И", Width = 52, Tip = "Пропущенные за матч", Cell = x => Ui.L(x.Ga.ToString("0.00"), 13, Rel(x.Ga, mGa, true)), Sort = x => x.Ga, Desc = false },
            new() { Title = "Бр/И", Width = 50, Tip = "Броски в створ за матч", Cell = x => Ui.L(x.Sf.ToString("0.0"), 12, Ui.Muted), Sort = x => x.Sf },
            new() { Title = "БрС/И", Width = 54, Tip = "Броски соперника в створ за матч", Cell = x => Ui.L(x.Sa.ToString("0.0"), 12, Ui.Muted), Sort = x => x.Sa, Desc = false },
            new() { Title = "xGЗ", Width = 50, Tip = "Ожидаемые голы за матч", Cell = x => Ui.L(x.XgF.ToString("0.00"), 12, Rel(x.XgF, mXg)), Sort = x => x.XgF },
            new() { Title = "xGП", Width = 50, Tip = "Ожидаемые голы соперника за матч", Cell = x => Ui.L(x.XgA.ToString("0.00"), 12, Rel(x.XgA, mXa, true)), Sort = x => x.XgA, Desc = false },
            new() { Title = "xG%", Width = 54, Tip = "Доля ожидаемых голов: качество игры без удачи", Cell = x => Ui.L($"{x.XgPct * 100:0.0}", 13, x.XgPct >= 0.52 ? Ui.Win : x.XgPct <= 0.48 ? Ui.Loss : Ui.Text, Ui.Semi), Sort = x => x.XgPct },
            new() { Title = "Реал%", Width = 56, Tip = "Реализация бросков в створ", Cell = x => Ui.L($"{x.ShPct * 100:0.0}", 12, Ui.Muted), Sort = x => x.ShPct },
            new() { Title = "%ОБ", Width = 50, Tip = "Процент отражённых", Cell = x => Ui.L(x.Sv.ToString(".000", System.Globalization.CultureInfo.InvariantCulture), 12, Ui.Muted), Sort = x => x.Sv },
            new() { Title = "PDO", Width = 48, Tip = "Реализация + % отражённых. Около 1000 — норма, выше — везёт", Cell = x => Ui.L($"{x.Pdo * 1000:0}", 12, x.Pdo >= 1.015 ? Ui.Gold : x.Pdo <= 0.985 ? Ui.Otl : Ui.Muted), Sort = x => x.Pdo },
            new() { Title = "Г−xG", Width = 54, Tip = "Голы сверх моментов за матч: свои минус чужие", Cell = x => Ui.L(Ui.Signed(x.Luck, "0.00"), 12, x.Luck >= 0.3 ? Ui.Gold : x.Luck <= -0.3 ? Ui.Otl : Ui.Muted), Sort = x => x.Luck },
            new() { Title = "ГБ", Width = 40, Tip = "Голы в большинстве", Cell = x => Ui.L($"{x.Ppg:0}", 12, Ui.Muted), Sort = x => x.Ppg },
            new() { Title = "Вбр%", Width = 52, Tip = "Выигранные вбрасывания", Cell = x => Ui.L($"{x.Fo * 100:0.0}", 12, Ui.Muted), Sort = x => x.Fo },
            new() { Title = "Сил/И", Width = 52, Tip = "Силовые приёмы за матч", Cell = x => Ui.L(x.Hits.ToString("0.0"), 12, Ui.Muted), Sort = x => x.Hits },
        };
        var grid = new Grid<TeamNumbers.Row>(cols, all, x => Shell.I.Go("club", "stats", x.Team), x => x.Team == C.UserTeam, 2);
        v.AddChild(Widget.Make("Клубы лиги: продвинутая статистика", grid.Root, null, null, 0));
        v.AddChild(Ui.L("xG — ожидаемые голы по броскам в створ (зона, тип броска, навык бросающего; без поправки на вратаря). Зелёный — заметно лучше среднего, красный — хуже. " +
                        "PDO и «Г−xG» показывают удачу: сильно выше нормы — результат скорее всего опережает игру.", 12, Ui.Dim).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
    }
}

/// <summary>Раздевалка: настрой команды и каждого игрока, ожидания по роли, обиды и обещания тренера.</summary>
public partial class RoomPage : Control
{
    Career C => P.C;
    public RoomPage() { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public static Color MoodColor(float m) => m >= 80 ? Ui.Win : m >= 67 ? new Color("8BD3A0") : m >= 47 ? Ui.Text : m >= 32 ? Ui.Otl : Ui.Loss;

    public static Control MoodCell(Career c, int id)
    {
        float m = c.Players.TryGetValue(id, out var s) ? s.Morale : Morale.BASE;
        var bar = new PctBar(m / 100.0) { CustomMinimumSize = new Vector2(46, 18) };
        var h = Ui.H(6, bar, Ui.L(Morale.Word(m), 12, MoodColor(m), m < 45 ? Ui.Semi : null));
        h.TooltipText = $"Настрой {m:0} из 100";
        return h;
    }

    string Reason(PlayerData p, PlayerState s)
    {
        var pr = C.Promises.FirstOrDefault(x => x.Player == p.Id);
        if (pr != null) return $"ждёт обещанного до {Ui.DateShort(pr.Due)}";
        if (s.HurtUntil is { } h && h >= C.Today) return "играет через боль";
        if (s.Grief >= 4) return Morale.Status(C, p) <= 1 ? "мало играет для своего статуса" : "хочет больше времени";
        var row = C.Table[p.Team];
        if (row.Streak.StartsWith("П") && row.Streak.Length > 1 && int.TryParse(row.Streak[1..].TrimStart('О'), out var n) && n >= 3) return "давит серия поражений";
        var f = PlayerForm.Of(C, p.Id);
        if (f.Games >= 3 && f.Word is "Спад" or "Провал") return "своя игра не идёт";
        if (s.Morale >= 72) return f.Word is "Отличная" or "Хорошая" ? "в форме и доволен ролью" : "доволен ролью и командой";
        return "всё спокойно";
    }

    public override void _Ready()
    {
        var v = Ui.V(12);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        var roster = C.Roster(C.UserTeam).Where(p => C.Players.ContainsKey(p.Id)).ToList();
        float team = Morale.Team(C, C.UserTeam);
        int happy = roster.Count(p => C.Players[p.Id].Morale >= 65), sad = roster.Count(p => C.Players[p.Id].Morale < 45);
        var tiles = Ui.H(10);
        tiles.AddChild(PlayerViz.Tile("heart-pulse", "Настрой команды", Morale.Word(team), MoodColor(team), $"{team:0} из 100 · в лиге в среднем {C.MoraleMean:0}",
            "Средний настрой заявки. Хороший настрой немного добавляет игрокам, плохой — отнимает"));
        tiles.AddChild(PlayerViz.Tile("trending-up", "Довольны", $"{happy}", Ui.Win, Ui.Plural(happy, "игрок", "игрока", "игроков")));
        tiles.AddChild(PlayerViz.Tile("trending-down", "Недовольны", $"{sad}", sad > 0 ? Ui.Otl : Ui.Text, sad == 0 ? "никто" : "могут прийти поговорить"));
        tiles.AddChild(PlayerViz.Tile("handshake", "Обещания", $"{C.Promises.Count}", Ui.Text, C.Promises.Count == 0 ? "нет действующих" : "сверка в срок — по фактам"));
        v.AddChild(tiles);

        var rows = roster.OrderBy(p => C.Players[p.Id].Morale).ToList();
        var cols = new List<Col<PlayerData>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = p => Links.Player(p, null, 13, C.Injured.Contains(p.Id) ? Ui.Loss : Ui.Text), Sort = p => p.LastRu, Desc = false },
            new() { Title = "Поз", Width = 36, Cell = p => Ui.L(Ui.PosRu(p.Pos), 12, Ui.Muted) },
            new() { Title = "Статус в клубе", Width = 130, Align = HorizontalAlignment.Left, Tip = "Чего он ждёт от роли: по месту среди партнёров по позиции", Cell = p => Ui.L(Morale.StatusWord(Morale.Status(C, p), p), 12, Ui.Muted), Sort = p => Morale.Status(C, p), Desc = false },
            new() { Title = "В составе", Width = 92, Align = HorizontalAlignment.Left, Cell = p => Ui.L(P.Role(p.Id, C.UserTeam), 12, Ui.Muted) },
            new() { Title = "Настрой", Width = 150, Align = HorizontalAlignment.Left, Cell = p => MoodCell(C, p.Id), Sort = p => C.Players[p.Id].Morale, Desc = false },
            new() { Title = "Почему", Width = 0, Align = HorizontalAlignment.Left, Cell = p => Ui.L(Reason(p, C.Players[p.Id]), 12, C.Players[p.Id].Grief >= 4 ? Ui.Otl : Ui.Muted) },
        };
        var grid = new Grid<PlayerData>(cols, rows, p => Shell.I.Go("player", null, p.Id.ToString()), null, 4);
        v.AddChild(Widget.Make("Игроки", grid.Root, null, null, 0));
        v.AddChild(Ui.L("Настрой двигают роль и минуты (звезда ждёт больше времени, чем игрок ротации), результаты, свои оценки и ваши решения — разговоры и обещания. " +
                        "Сам по себе он медленно возвращается к норме характера. Влияние на игру небольшое: как у формы.", 12, Ui.Dim).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
    }
}
