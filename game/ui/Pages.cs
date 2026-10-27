using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Общие кусочки страниц FM-оболочки.</summary>
public static class P
{
    public static Career C => Main.I.Career;

    public static MarginContainer Page(Control content, bool scroll = true)
    {
        var m = Ui.Margin(scroll ? Ui.Scroll(content) : content, 16, 14, 16, 14);
        m.SizeFlagsHorizontal = m.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return m;
    }

    public static Control Team(string abbr, int size = 18, bool full = false)
    {
        var t = C.Team(abbr);
        return Ui.H(6, new TeamBadge(t, size), Ui.L(full ? t.Name : t.City, 13, abbr == C.UserTeam ? Colors.White : Ui.Text, abbr == C.UserTeam ? Ui.Semi : null));
    }

    /// <summary>Форма: квадратики В/П/О последних матчей (новые справа).</summary>
    public static Control Form(string abbr, int n = 5, int size = 20)
    {
        var h = Ui.H(3);
        foreach (var g in C.TeamGames(abbr).Where(g => g.Played).OrderByDescending(g => g.Date).Take(n).Reverse())
        {
            bool home = g.Home == abbr; var r = g.Result;
            bool win = (home ? r.HomeGoals : r.AwayGoals) > (home ? r.AwayGoals : r.HomeGoals);
            var (txt, col) = win ? ("В", Ui.Win) : r.End == "REG" || g.Playoff ? ("П", Ui.Loss) : ("О", Ui.Otl);
            var p = new PanelContainer { CustomMinimumSize = new Vector2(size, size), TooltipText = $"{Ui.DateShort(g.Date)}: {(home ? "" : "@")}{C.Team(home ? g.Away : g.Home).City} {(home ? r.HomeGoals : r.AwayGoals)}:{(home ? r.AwayGoals : r.HomeGoals)}" };
            p.AddThemeStyleboxOverride("panel", Ui.Box(col, 3, null, 0).Also(s => s.ContentMarginTop = s.ContentMarginBottom = 0));
            p.AddChild(Ui.L(txt, 11, Colors.White, Ui.Bold, HorizontalAlignment.Center));
            h.AddChild(p);
        }
        if (h.GetChildCount() == 0) h.AddChild(Ui.L("—", 13, Ui.Dim));
        return h;
    }

    /// <summary>Где игрок в составе своего клуба.</summary>
    public static string Role(int id, string abbr)
    {
        if (C.Injured.Contains(id)) return "Травма";
        var l = C.Lineups[abbr];
        for (int i = 0; i < l.F.Count; i++) if (l.F[i].Contains(id)) return $"{i + 1}-е звено";
        for (int i = 0; i < l.D.Count; i++) if (l.D[i].Contains(id)) return $"{i + 1}-я пара";
        if (id == l.G1) return "Основной";
        if (id == l.G2) return "Запасной";
        return "Запас";
    }

    public static string PosLong(string pos) => PlayerCard.PosLong(pos);

    public static void OpenLink(string link)
    {
        if (string.IsNullOrEmpty(link)) return;
        var (k, v) = link.Contains(':') ? (link[..link.IndexOf(':')], link[(link.IndexOf(':') + 1)..]) : (link, null);
        switch (k)
        {
            case "game": var g = C.Games.FirstOrDefault(x => x.Id.ToString() == v); if (g != null) Main.I.ShowMatch(g); break;
            case "player": Shell.I?.Go("player", null, v); break;
            case "club": Shell.I?.Go("club", null, v); break;
            case "table": Shell.I?.Go("comp", "table"); break;
            case "playoffs": Shell.I?.Go("comp", "playoffs"); break;
        }
    }
}

// =====================================================================================================
/// <summary>Главная: виджеты, как на домашнем экране FM.</summary>
public partial class HomePage : Control
{
    Career C => P.C;
    public HomePage() { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        var cols = Ui.H(12);
        cols.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var a = Ui.Expand(Ui.V(12)); a.SizeFlagsStretchRatio = 1.35f;
        var b = Ui.Expand(Ui.V(12));
        var c = Ui.Expand(Ui.V(12));
        cols.AddChild(a); cols.AddChild(b); cols.AddChild(c);
        var page = P.Page(cols);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);

        if (C.Phase == Phase.Finished && C.Champion != null)
            a.AddChild(Widget.Make("Кубок Стэнли 2027", Ui.H(14, new TeamBadge(C.Team(C.Champion), 56), Ui.V(2, Ui.L(C.Team(C.Champion).Name, 20, Ui.Text, Ui.Black),
                Ui.L(C.Champion == C.UserTeam ? "Это ваш клуб!" : "Многосезонная карьера — в следующих версиях.", 13, Ui.Muted)))));
        a.AddChild(NextMatch());
        a.AddChild(Upcoming());
        b.AddChild(InboxWidget());
        b.AddChild(Results());
        c.AddChild(Division());
        c.AddChild(Leaders());
    }

    Control NextMatch()
    {
        var g = C.NextGame(C.UserTeam);
        if (g == null) return Widget.Make("Следующий матч", Ui.L("Матчей больше нет", 13, Ui.Muted));
        bool home = g.Home == C.UserTeam;
        var opp = C.Team(home ? g.Away : g.Home);
        var o = C.Preview(g);
        double p = home ? o.PHome : 1 - o.PHome;
        var oppRow = C.Table[opp.Abbr];
        var v = Ui.V(10);
        var h = Ui.H(16);
        h.AddChild(new TeamBadge(C.Team(C.UserTeam), 64));
        h.AddChild(Ui.V(2, Ui.L(home ? "ДОМА" : "В ГОСТЯХ", 11, Ui.Muted, Ui.Bold, HorizontalAlignment.Center), Ui.L("vs", 18, Ui.Dim, Ui.Bold, HorizontalAlignment.Center)));
        h.AddChild(new TeamBadge(opp, 64));
        var info = Ui.Expand(Ui.V(3));
        info.AddChild(Ui.L(opp.Name, 18, Ui.Text, Ui.Bold));
        info.AddChild(Ui.L($"{Ui.Date(g.Date)}{(g.Playoff ? " · плей-офф" : "")} · {oppRow.W}-{oppRow.L}-{oppRow.Otl}, {C.Sorted(t => t.Conference == opp.Conference).FindIndex(r => r.Team == opp.Abbr) + 1}-е в конференции", 12, Ui.Muted));
        info.AddChild(Ui.H(8, Ui.L("Форма соперника", 12, Ui.Muted), P.Form(opp.Abbr, 5, 18)));
        h.AddChild(info);
        v.AddChild(h);
        var bar = Ui.H(10, Ui.L("Шанс на победу (модель)", 12, Ui.Muted), Ui.Expand(new Control()), Ui.L(Ui.Pct(p), 16, Ui.Text, Ui.Black));
        v.AddChild(bar);
        var track = new ColorRect { Color = Ui.Line, CustomMinimumSize = new Vector2(0, 6) };
        track.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        track.Resized += () => { if (track.GetChildCount() > 0) ((ColorRect)track.GetChild(0)).Size = new Vector2((float)(track.Size.X * p), 6); };
        track.AddChild(new ColorRect { Color = Ui.AccentBright, Size = new Vector2(0, 6) });
        v.AddChild(track);
        bool b2b = C.PlayedYesterday(C.UserTeam, g.Date), ob2b = C.PlayedYesterday(opp.Abbr, g.Date);
        if (b2b || ob2b) v.AddChild(Ui.L(b2b && ob2b ? "Обе команды играли накануне" : b2b ? "У нас второй матч за два дня — подумайте о запасном вратаре" : "Соперник играл накануне — он устал", 12, Ui.Otl));
        return Widget.Make("Следующий матч", v, "Звенья →", () => Shell.I.Go("lines"));
    }

    Control Upcoming()
    {
        var games = C.TeamGames(C.UserTeam).Where(g => !g.Played).OrderBy(g => g.Date).Skip(1).Take(5).ToList();
        var grid = new Grid<ScheduledGame>(new List<Col<ScheduledGame>>
        {
            new() { Title = "Дата", Width = 110, Align = HorizontalAlignment.Left, Cell = g => Ui.L(Ui.Date(g.Date), 12, Ui.Muted) },
            new() { Title = "", Width = 20, Cell = g => Ui.L(g.Home == C.UserTeam ? "" : "@", 12, Ui.Muted) },
            new() { Title = "Соперник", Width = 0, Align = HorizontalAlignment.Left, Cell = g => P.Team(g.Home == C.UserTeam ? g.Away : g.Home) },
            new() { Title = "Шанс", Width = 60, Cell = g => { var o = C.Preview(g); return Ui.L(Ui.Pct(g.Home == C.UserTeam ? o.PHome : 1 - o.PHome), 12, Ui.Muted); } },
        }, games, g => P.OpenLink($"club:{(g.Home == C.UserTeam ? g.Away : g.Home)}"));
        return Widget.Make("Ближайшие матчи", grid.Root, "Расписание →", () => Shell.I.Go("schedule"), 0);
    }

    Control InboxWidget()
    {
        var v = Ui.V(0);
        foreach (var m in C.Messages.OrderByDescending(m => m.Id).Take(6))
        {
            var row = new PanelContainer { MouseDefaultCursorShape = CursorShape.PointingHand };
            var sb = Ui.Box(new Color(0, 0, 0, 0), 0, null, 12); sb.ContentMarginTop = sb.ContentMarginBottom = 7; sb.BorderColor = Ui.Line; sb.BorderWidthBottom = 1;
            row.AddThemeStyleboxOverride("panel", sb);
            var h = Ui.H(10, Ui.Icon(m.Read ? "mail-open" : "mail", 16, m.Read ? Ui.Dim : Ui.AccentBright),
                Ui.Expand(Ui.V(1, Ui.L(m.Subject, 13, m.Read ? Ui.Muted : Ui.Text, m.Read ? null : Ui.Semi).Also(l => { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }),
                    Ui.L($"{m.From} · {Ui.DateShort(m.Date)}", 11, Ui.Dim))));
            h.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(h);
            int id = m.Id;
            row.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Shell.I.Go("inbox", null, id.ToString()); };
            v.AddChild(row);
        }
        string t = C.Unread > 0 ? $"Входящие · {C.Unread} {Ui.Plural(C.Unread, "новое", "новых", "новых")}" : "Входящие";
        return Widget.Make(t, v, "Все →", () => Shell.I.Go("inbox"), 0);
    }

    Control Results()
    {
        var games = C.TeamGames(C.UserTeam).Where(g => g.Played).OrderByDescending(g => g.Date).Take(6).ToList();
        var grid = new Grid<ScheduledGame>(new List<Col<ScheduledGame>>
        {
            new() { Title = "Дата", Width = 56, Align = HorizontalAlignment.Left, Cell = g => Ui.L(Ui.DateShort(g.Date), 12, Ui.Muted) },
            new() { Title = "", Width = 16, Cell = g => Ui.L(g.Home == C.UserTeam ? "" : "@", 12, Ui.Muted) },
            new() { Title = "Соперник", Width = 0, Align = HorizontalAlignment.Left, Cell = g => P.Team(g.Home == C.UserTeam ? g.Away : g.Home) },
            new() { Title = "Счёт", Width = 64, Cell = g => { var r = g.Result; bool h = g.Home == C.UserTeam; int my = h ? r.HomeGoals : r.AwayGoals, op = h ? r.AwayGoals : r.HomeGoals;
                var col = my > op ? Ui.Win : r.End == "REG" || g.Playoff ? Ui.Loss : Ui.Otl;
                return Ui.L($"{my}:{op}{(r.End == "OT" ? " ОТ" : r.End == "SO" ? " Б" : "")}", 13, col, Ui.Bold); } },
        }, games, g => Main.I.ShowMatch(g));
        var v = Ui.V(8, Ui.Margin(Ui.H(8, Ui.L("Форма", 12, Ui.Muted), P.Form(C.UserTeam, 10, 18)), 12, 10, 12, 4), grid.Root);
        return Widget.Make("Последние матчи", v, null, null, 0);
    }

    Control Division()
    {
        var t = C.Team(C.UserTeam);
        var rows = C.Sorted(x => x.Division == t.Division).Select((r, i) => (r, i: i + 1)).ToList();
        var grid = new Grid<(StandingRow r, int i)>(new List<Col<(StandingRow r, int i)>>
        {
            new() { Title = "#", Width = 20, Cell = x => Ui.L($"{x.i}", 12, x.i <= 3 ? Ui.Text : Ui.Muted) },
            new() { Title = "Клуб", Width = 0, Align = HorizontalAlignment.Left, Cell = x => P.Team(x.r.Team) },
            new() { Title = "И", Width = 28, Cell = x => Ui.L($"{x.r.Gp}", 12, Ui.Muted) },
            new() { Title = "+/-", Width = 36, Cell = x => Ui.L(Ui.Signed(x.r.Gf - x.r.Ga, "0"), 12, Ui.Muted) },
            new() { Title = "О", Width = 30, Cell = x => Ui.L($"{x.r.Pts}", 13, Ui.Text, Ui.Bold) },
        }, rows, x => P.OpenLink($"club:{x.r.Team}"), x => x.r.Team == C.UserTeam);
        return Widget.Make($"{t.Division} дивизион", grid.Root, "Таблица →", () => Shell.I.Go("comp", "table"), 0);
    }

    Control Leaders()
    {
        var rows = C.Skaters.Where(kv => C.Data.PlayerById.TryGetValue(kv.Key, out var p) && p.Team == C.UserTeam)
            .OrderByDescending(kv => kv.Value.Pts).ThenByDescending(kv => kv.Value.G).Take(6).Select(kv => (p: C.Data.PlayerById[kv.Key], s: kv.Value)).ToList();
        if (rows.Count == 0)
            rows = C.Roster(C.UserTeam).Where(p => !p.IsGoalie).OrderByDescending(p => p.Ovr).Take(6).Select(p => (p, new SkaterLine())).ToList();
        var grid = new Grid<(PlayerData p, SkaterLine s)>(new List<Col<(PlayerData p, SkaterLine s)>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.p.NameRu, 13) },
            new() { Title = "Г", Width = 26, Cell = x => Ui.L($"{x.s.G}", 12, Ui.Muted) },
            new() { Title = "П", Width = 26, Cell = x => Ui.L($"{x.s.A}", 12, Ui.Muted) },
            new() { Title = "О", Width = 30, Cell = x => Ui.L($"{x.s.Pts}", 13, Ui.Text, Ui.Bold) },
        }, rows, x => Shell.I.Go("player", null, x.p.Id.ToString()));
        return Widget.Make("Бомбардиры клуба", grid.Root, "Состав →", () => Shell.I.Go("squad"), 0);
    }
}

// =====================================================================================================
/// <summary>Состав клуба: плотная таблица с сортировкой, фильтр по позициям — как «Игроки» в FM.</summary>
public partial class SquadPage : Control
{
    Career C => P.C;
    readonly string _abbr;
    int _filter;
    VBoxContainer _box;
    public SquadPage() { }
    public SquadPage(string abbr) { _abbr = abbr; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        _box = Ui.V(10);
        var page = P.Page(_box);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        Build();
    }

    void Build()
    {
        Ui.Clear(_box);
        var t = C.Team(_abbr);
        var all = C.Roster(_abbr).ToList();
        var head = Ui.H(12);
        head.AddChild(new Segmented(new[] { $"Все ({all.Count})", "Нападающие", "Защитники", "Вратари" }, _filter, k => { _filter = k; Build(); }));
        head.AddChild(Ui.Spacer());
        if (_abbr != C.UserTeam)
        {
            var o = C.NextGame(C.UserTeam);
            head.AddChild(Ui.L($"Сила по модели: {C.Data.Teams.OrderByDescending(x => x.Att - x.Dfn).ToList().IndexOf(t) + 1}-е место в лиге", 12, Ui.Muted));
        }
        else head.AddChild(Ui.L("Щёлкните игрока — профиль. Заголовок колонки — сортировка.", 12, Ui.Dim));
        _box.AddChild(head);

        Func<PlayerData, bool> f = _filter switch { 1 => p => p.IsForward, 2 => p => p.IsDefense, 3 => p => p.IsGoalie, _ => p => true };
        var rows = all.Where(f).ToList();
        bool gk = _filter == 3;
        var age = C.Today;
        var cols = new List<Col<PlayerData>>
        {
            new() { Title = "№", Width = 30, Cell = p => Ui.L(p.Num?.ToString() ?? "", 12, Ui.Muted), Sort = p => p.Num ?? 99, Desc = false },
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Sort = p => p.LastRu, Desc = false, Cell = p =>
            {
                bool inj = C.Injured.Contains(p.Id);
                var h = Ui.H(6, Ui.L(p.NameRu, 13, inj ? Ui.Loss : Ui.Text, Ui.Medium));
                if (inj) h.AddChild(Ui.Icon("cross", 13, Ui.Loss));
                return h;
            } },
            new() { Title = "Поз", Width = 40, Cell = p => Ui.L(Ui.PosRu(p.Pos), 12, Ui.Muted), Sort = p => "C LWRWD G".IndexOf(p.Pos), Desc = false },
            new() { Title = "Возр", Width = 40, Cell = p => Ui.L(p.Age(age)?.ToString() ?? "", 12), Sort = p => p.Age(age) ?? 0, Desc = false },
            new() { Title = "Страна", Width = 50, Cell = p => Ui.L(p.Country ?? "", 11, Ui.Muted), Sort = p => p.Country ?? "", Desc = false },
            new() { Title = "Рост", Width = 40, Cell = p => Ui.L(p.Height?.ToString() ?? "", 12, Ui.Muted), Sort = p => p.Height ?? 0 },
            new() { Title = "В составе", Width = 92, Align = HorizontalAlignment.Left, Cell = p => { var r = P.Role(p.Id, _abbr); return Ui.L(r, 12, r == "Травма" ? Ui.Loss : r == "Запас" ? Ui.Dim : Ui.Muted); }, Sort = p => RoleOrder(p.Id), Desc = false },
            new() { Title = "Способности", Width = 86, Cell = p => Ui.Stars(p.Ovr, 13), Sort = p => p.Ovr, Tip = "Оценка тренерского штаба (из статистики)" },
        };
        if (!gk)
            foreach (var (key, name, tip) in new[] { ("att", "Атк", "Атака: создание моментов (личный xG за 60 минут)"), ("pas", "Пас", "Передачи за матч"), ("fin", "Реал", "Реализация: голы относительно xG"), ("dfn", "Обор", "Оборона"), ("sta", "Вын", "Выносливость: доверие по игровому времени") })
                cols.Add(new() { Title = name, Width = 38, Tip = tip, Cell = p => p.IsGoalie ? Ui.L("—", 12, Ui.Dim) : Ui.Attr(p.Rating(key)), Sort = p => p.IsGoalie ? -1 : p.Rating(key) });
        if (gk || _filter == 0)
            cols.Add(new() { Title = "Вр", Width = 38, Tip = "Вратарское мастерство: сейвы сверх ожидаемого", Cell = p => p.IsGoalie ? Ui.Attr(p.Rating("gk")) : Ui.L("", 12), Sort = p => p.IsGoalie ? p.Rating("gk") : -1 });
        if (!gk)
        {
            cols.Add(new() { Title = "И", Width = 30, Cell = p => Ui.L($"{C.Skaters.GetValueOrDefault(p.Id)?.Gp ?? 0}", 12, Ui.Muted), Sort = p => C.Skaters.GetValueOrDefault(p.Id)?.Gp ?? 0 });
            cols.Add(new() { Title = "Г", Width = 30, Cell = p => Ui.L($"{C.Skaters.GetValueOrDefault(p.Id)?.G ?? 0}", 12), Sort = p => C.Skaters.GetValueOrDefault(p.Id)?.G ?? 0 });
            cols.Add(new() { Title = "П", Width = 30, Cell = p => Ui.L($"{C.Skaters.GetValueOrDefault(p.Id)?.A ?? 0}", 12), Sort = p => C.Skaters.GetValueOrDefault(p.Id)?.A ?? 0 });
            cols.Add(new() { Title = "О", Width = 34, Cell = p => Ui.L($"{C.Skaters.GetValueOrDefault(p.Id)?.Pts ?? 0}", 13, Ui.Text, Ui.Bold), Sort = p => C.Skaters.GetValueOrDefault(p.Id)?.Pts ?? 0 });
        }
        else
        {
            cols.Add(new() { Title = "И", Width = 34, Cell = p => Ui.L($"{C.Goalies.GetValueOrDefault(p.Id)?.Gp ?? 0}", 12, Ui.Muted), Sort = p => C.Goalies.GetValueOrDefault(p.Id)?.Gp ?? 0 });
            cols.Add(new() { Title = "В-П-ПО", Width = 70, Cell = p => { var s = C.Goalies.GetValueOrDefault(p.Id); return Ui.L(s == null ? "—" : $"{s.W}-{s.L}-{s.Otl}", 12); } });
            cols.Add(new() { Title = "%ОБ", Width = 50, Cell = p => { var s = C.Goalies.GetValueOrDefault(p.Id); return Ui.L(s == null || s.Sa == 0 ? "—" : $"{s.SvPct:.000}", 13, Ui.Text, Ui.Bold); }, Sort = p => C.Goalies.GetValueOrDefault(p.Id)?.SvPct ?? 0 });
            cols.Add(new() { Title = "КН", Width = 44, Cell = p => { var s = C.Goalies.GetValueOrDefault(p.Id); return Ui.L(s == null ? "—" : $"{s.Gaa:0.00}", 12); }, Sort = p => -(C.Goalies.GetValueOrDefault(p.Id)?.Gaa ?? 9), Desc = true });
        }
        var grid = new Grid<PlayerData>(cols, rows, p => Shell.I.Go("player", null, p.Id.ToString()), null, sortCol: 6);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 0).Also(s => s.ContentMarginTop = s.ContentMarginBottom = 0));
        panel.AddChild(grid.Root);
        _box.AddChild(panel);
    }

    int RoleOrder(int id)
    {
        var r = P.Role(id, _abbr);
        if (r.EndsWith("звено")) return int.Parse(r[..1]);
        if (r.EndsWith("пара")) return 10 + int.Parse(r[..1]);
        return r switch { "Основной" => 20, "Запасной" => 21, "Запас" => 30, _ => 40 };
    }
}

// =====================================================================================================
/// <summary>Профиль игрока, как в FM: шапка с фото и фактами, характеристики 1–20, аналитика, роль, статистика.</summary>
public partial class PlayerPage : Control
{
    Career C => P.C;
    readonly int _id;
    public PlayerPage() { }
    public PlayerPage(int id) { _id = id; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        var p = C.Data.PlayerById[_id];
        var t = C.Team(p.Team);
        var v = Ui.V(12);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);

        // ---------- шапка
        var top = Ui.H(18);
        var c1 = new Color(t.Colors[0]);
        top.AddChild(new PlayerPhoto(p, 132, c1.Darkened(0.35f)));
        var info = Ui.Expand(Ui.V(4));
        info.AddChild(Ui.H(10, Ui.L(p.Num.HasValue ? $"#{p.Num}" : "", 24, Ui.AccentBright, Ui.Black), Ui.L(p.NameRu, 26, Ui.Text, Ui.Bold)));
        info.AddChild(Ui.H(8, new TeamBadge(t, 22), Ui.L($"{t.Name} · {Capital(P.PosLong(p.Pos))}", 14, Ui.Muted)));
        info.AddChild(Ui.Spacer(false, 6));
        var facts = new GridContainer { Columns = 4 };
        facts.AddThemeConstantOverride("h_separation", 34); facts.AddThemeConstantOverride("v_separation", 4);
        var age = p.Age(C.Today);
        void Fact(string k, string val) { facts.AddChild(Ui.V(0, Ui.L(k.ToUpperInvariant(), 10, Ui.Dim, Ui.Bold), Ui.L(val, 14, Ui.Text, Ui.Semi))); }
        Fact("Возраст", age.HasValue ? $"{age} ({Born(p.Born)})" : "—");
        Fact("Рост / вес", $"{p.Height} см / {p.Weight} кг");
        Fact("Страна", PlayerCard.Country(p.Country));
        Fact("В составе", P.Role(p.Id, p.Team));
        info.AddChild(facts);
        top.AddChild(info);
        var right = Ui.V(6);
        right.AddChild(Ui.L("СПОСОБНОСТИ", 10, Ui.Dim, Ui.Bold, HorizontalAlignment.Right));
        right.AddChild(Ui.H(0, Ui.Spacer(), Ui.Stars(p.Ovr, 20)));
        right.AddChild(Ui.L($"Общий рейтинг {p.Ovr} из 99", 12, Ui.Muted, null, HorizontalAlignment.Right));
        if (C.Injured.Contains(p.Id)) right.AddChild(Ui.H(6, Ui.Spacer(), Ui.Icon("cross", 14, Ui.Loss), Ui.L("Травмирован", 13, Ui.Loss, Ui.Semi)));
        top.AddChild(right);
        var head = new PanelContainer();
        var hs = Ui.Box(Ui.Panel, 4, null, 16); hs.BorderColor = c1.Luminance < 0.08f ? c1.Lightened(0.3f) : c1; hs.BorderWidthLeft = 4;
        head.AddThemeStyleboxOverride("panel", hs);
        head.AddChild(top);
        v.AddChild(head);

        // ---------- характеристики, аналитика, роль
        var row = Ui.H(12);
        var attrs = Ui.V(6);
        var keys = p.IsGoalie ? new[] { ("gk", "Вратарское мастерство") } :
            new[] { ("att", "Атака"), ("pas", "Пас"), ("fin", "Реализация"), ("dfn", "Оборона"), ("sta", "Выносливость") };
        foreach (var (k, name) in keys)
        {
            int v20 = Ui.Attr20(p.Rating(k));
            var bar = new ColorRect { Color = Ui.Line, CustomMinimumSize = new Vector2(110, 6) };
            bar.AddChild(new ColorRect { Color = Ui.AttrColor(v20), Size = new Vector2(110 * v20 / 20f, 6) });
            attrs.AddChild(Ui.H(10, Ui.Expand(Ui.L(name, 13)), bar, Ui.Attr(p.Rating(k), 15).Also(l => l.CustomMinimumSize = new Vector2(26, 0))));
        }
        attrs.AddChild(Ui.Spacer(false, 4));
        attrs.AddChild(Ui.L("Шкала 1–20, как в FM: место игрока среди всех на его позиции в лиге.", 11, Ui.Dim).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        row.AddChild(Ui.Expand(Widget.Make("Характеристики", attrs)));

        var an = Ui.V(6);
        void Line(string k, string val) => an.AddChild(Ui.H(10, Ui.Expand(Ui.L(k, 13, Ui.Muted)), Ui.L(val, 13, Ui.Text, Ui.Semi)));
        if (p.IsGoalie)
        {
            Line("Сейвы сверх ожидаемого", $"{Ui.Signed(p.Gsax * 100, "0.0")}% от xG");
            Line("Матчей в прошлом сезоне", p.Stats.ContainsKey("gp") ? "" : "—");
        }
        else
        {
            Line("Личный xG за матч", $"{p.IxgPg:0.00}");
            Line("Личный xG за 60 минут", $"{p.Ixg60:0.00}");
            Line("Время в НХЛ за матч", $"{p.Toi:0.0} мин");
            Line("Передач за матч", $"{p.Apg:0.00}");
            Line("Реализация (голы / xG)", $"{p.Fin:0.00}");
        }
        an.AddChild(Ui.L("Данные MoneyPuck и НХЛ за последние сезоны.", 11, Ui.Dim));
        row.AddChild(Ui.Expand(Widget.Make("Аналитика", an)));

        var role = Ui.V(6);
        var l = C.Lineups[p.Team];
        role.AddChild(Ui.H(10, Ui.Expand(Ui.L("Место в составе", 13, Ui.Muted)), Ui.L(P.Role(p.Id, p.Team), 13, Ui.Text, Ui.Semi)));
        if (!p.IsGoalie)
        {
            role.AddChild(Ui.H(10, Ui.Expand(Ui.L("Минут за матч по плану", 13, Ui.Muted)), Ui.L($"{l.MinutesOf(p.Id):0}", 13, Ui.Text, Ui.Semi)));
            string pp = l.Pp1.Contains(p.Id) ? "1-е звено" : l.Pp2.Contains(p.Id) ? "2-е звено" : "нет";
            role.AddChild(Ui.H(10, Ui.Expand(Ui.L("Большинство", 13, Ui.Muted)), Ui.L(pp, 13, Ui.Text, Ui.Semi)));
        }
        if (p.Team == C.UserTeam) role.AddChild(Ui.B("Изменить звенья", () => Shell.I.Go("lines")).Also(b => b.AddThemeFontSizeOverride("font_size", 12)));
        else role.AddChild(Ui.B($"Открыть клуб «{t.City}»", () => Shell.I.Go("club", null, t.Abbr)).Also(b => b.AddThemeFontSizeOverride("font_size", 12)));
        row.AddChild(Ui.Expand(Widget.Make("Роль", role)));
        v.AddChild(row);

        // ---------- статистика
        if (p.IsGoalie)
        {
            var rows = new List<(string name, GoalieLine s)>();
            var real = p.Stats;
            if (real.Count > 0) rows.Add(("НХЛ 2026-27 (реальность, на " + Ui.DateShort(DateOnly.Parse(C.Data.DataDate)) + ")", new GoalieLine { Gp = (int)real.GetValueOrDefault("gp"), W = (int)real.GetValueOrDefault("w"), L = (int)real.GetValueOrDefault("l"), Otl = (int)real.GetValueOrDefault("otl") }));
            if (C.Goalies.TryGetValue(p.Id, out var s1)) rows.Add(("HM27 · регулярный чемпионат", s1));
            if (C.PoGoalies.TryGetValue(p.Id, out var s2)) rows.Add(("HM27 · плей-офф", s2));
            var g = new Grid<(string name, GoalieLine s)>(new List<Col<(string name, GoalieLine s)>>
            {
                new() { Title = "Турнир", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.name, 13) },
                new() { Title = "И", Width = 40, Cell = x => Ui.L($"{x.s.Gp}", 13) },
                new() { Title = "В-П-ПО", Width = 80, Cell = x => Ui.L($"{x.s.W}-{x.s.L}-{x.s.Otl}", 13) },
                new() { Title = "%ОБ", Width = 60, Cell = x => Ui.L(x.s.Sa > 0 ? $"{x.s.SvPct:.000}" : real.ContainsKey("sv") && x.name.StartsWith("НХЛ") ? $"{real["sv"]:.000}" : "—", 13, Ui.Text, Ui.Bold) },
                new() { Title = "КН", Width = 50, Cell = x => Ui.L(x.s.Sa > 0 ? $"{x.s.Gaa:0.00}" : real.ContainsKey("gaa") && x.name.StartsWith("НХЛ") ? $"{real["gaa"]:0.00}" : "—", 13) },
                new() { Title = "Сух", Width = 40, Cell = x => Ui.L($"{x.s.So}", 13) },
            }, rows);
            v.AddChild(Widget.Make("Статистика", g.Root, null, null, 0));
        }
        else
        {
            var rows = new List<(string name, int gp, int g, int a, int ppg)>();
            var real = p.Stats;
            if (real.Count > 0) rows.Add(("НХЛ 2026-27 (реальность, на " + Ui.DateShort(DateOnly.Parse(C.Data.DataDate)) + ")", (int)real.GetValueOrDefault("gp"), (int)real.GetValueOrDefault("g"), (int)real.GetValueOrDefault("a"), -1));
            if (C.Skaters.TryGetValue(p.Id, out var s1)) rows.Add(("HM27 · регулярный чемпионат", s1.Gp, s1.G, s1.A, s1.Ppg));
            if (C.PoSkaters.TryGetValue(p.Id, out var s2)) rows.Add(("HM27 · плей-офф", s2.Gp, s2.G, s2.A, s2.Ppg));
            var g = new Grid<(string name, int gp, int g, int a, int ppg)>(new List<Col<(string name, int gp, int g, int a, int ppg)>>
            {
                new() { Title = "Турнир", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.name, 13) },
                new() { Title = "И", Width = 40, Cell = x => Ui.L($"{x.gp}", 13) },
                new() { Title = "Г", Width = 40, Cell = x => Ui.L($"{x.g}", 13) },
                new() { Title = "П", Width = 40, Cell = x => Ui.L($"{x.a}", 13) },
                new() { Title = "О", Width = 44, Cell = x => Ui.L($"{x.g + x.a}", 13, Ui.Text, Ui.Bold) },
                new() { Title = "ГБ", Width = 40, Cell = x => Ui.L(x.ppg < 0 ? "—" : $"{x.ppg}", 13, Ui.Muted) },
                new() { Title = "О/И", Width = 50, Cell = x => Ui.L(x.gp > 0 ? $"{(x.g + x.a) / (double)x.gp:0.00}" : "—", 13, Ui.Muted) },
            }, rows);
            v.AddChild(Widget.Make("Статистика", g.Root, null, null, 0));
        }

        // ---------- матчи игрока в HM27
        var games = C.Games.Where(gm => gm.Played && (gm.Home == p.Team || gm.Away == p.Team) && (gm.Result.HomeDressed.Contains(p.Id) || gm.Result.AwayDressed.Contains(p.Id)))
            .OrderByDescending(gm => gm.Date).Take(8).ToList();
        if (games.Count > 0)
        {
            var gg = new Grid<ScheduledGame>(new List<Col<ScheduledGame>>
            {
                new() { Title = "Дата", Width = 70, Align = HorizontalAlignment.Left, Cell = gm => Ui.L(Ui.DateShort(gm.Date), 12, Ui.Muted) },
                new() { Title = "Соперник", Width = 0, Align = HorizontalAlignment.Left, Cell = gm => P.Team(gm.Home == p.Team ? gm.Away : gm.Home) },
                new() { Title = "Счёт", Width = 70, Cell = gm => { bool h = gm.Home == p.Team; return Ui.L($"{(h ? gm.Result.HomeGoals : gm.Result.AwayGoals)}:{(h ? gm.Result.AwayGoals : gm.Result.HomeGoals)}", 13); } },
                new() { Title = p.IsGoalie ? "Сейвы" : "Г+П", Width = 70, Cell = gm =>
                {
                    var r = gm.Result;
                    if (p.IsGoalie)
                    {
                        bool h = gm.Home == p.Team;
                        if ((h ? r.HomeGoalie : r.AwayGoalie) != p.Id) return Ui.L("не играл", 12, Ui.Dim);
                        int sh = h ? r.AwayShots : r.HomeShots, ga = h ? r.AwayGoals : r.HomeGoals;
                        return Ui.L($"{sh - ga}/{sh}", 13);
                    }
                    int g1 = r.Goals.Count(e => e.Scorer == p.Id && e.Kind != "so"), a1 = r.Goals.Count(e => e.A1 == p.Id || e.A2 == p.Id);
                    return Ui.L($"{g1}+{a1}", 13, g1 + a1 > 0 ? Ui.Text : Ui.Dim, g1 + a1 > 0 ? Ui.Bold : null);
                } },
            }, games, gm => Main.I.ShowMatch(gm));
            v.AddChild(Widget.Make("Последние матчи в HM27", gg.Root, null, null, 0));
        }
    }

    static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
    static string Born(string iso) => DateOnly.TryParse(iso, out var d) ? $"{d.Day:00}.{d.Month:00}.{d.Year}" : "";
}

// =====================================================================================================
/// <summary>Входящие: список писем слева, письмо справа.</summary>
public partial class InboxPage : Control
{
    Career C => P.C;
    int _sel;
    VBoxContainer _list, _view;
    public InboxPage() { }
    public InboxPage(string arg) { _sel = int.TryParse(arg, out var i) ? i : 0; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        if (_sel == 0) _sel = C.Messages.Where(m => !m.Read).OrderBy(m => m.Id).Select(m => m.Id).FirstOrDefault();
        if (_sel == 0) _sel = C.Messages.Select(m => m.Id).DefaultIfEmpty(0).Max();
        var h = Ui.H(12);
        var m = Ui.Margin(h, 16, 14, 16, 14);
        m.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(m);
        _list = Ui.V(0);
        var lp = new PanelContainer { CustomMinimumSize = new Vector2(400, 0) };
        lp.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 0).Also(s => s.ContentMarginTop = s.ContentMarginBottom = 0));
        lp.SizeFlagsVertical = SizeFlags.ExpandFill;
        lp.AddChild(Ui.Scroll(_list));
        h.AddChild(lp);
        _view = Ui.V(12);
        var vp = new PanelContainer();
        vp.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 24));
        vp.SizeFlagsHorizontal = vp.SizeFlagsVertical = SizeFlags.ExpandFill;
        vp.AddChild(Ui.Scroll(_view));
        h.AddChild(vp);
        Build();
    }

    static string IconOf(string kind) => kind switch { "board" => "landmark", "match" => "activity", "week" => "chart-column", "league" => "trophy", _ => "mail" };

    void Build()
    {
        var msg = C.Messages.FirstOrDefault(x => x.Id == _sel);
        if (msg != null && !msg.Read) { msg.Read = true; Main.I.Save(); Callable.From(() => Shell.I?.RefreshSide()).CallDeferred(); }
        Ui.Clear(_list);
        foreach (var x in C.Messages.OrderByDescending(x => x.Id))
        {
            bool on = x.Id == _sel;
            var row = new PanelContainer { MouseDefaultCursorShape = CursorShape.PointingHand };
            var sb = Ui.Box(on ? new Color(Ui.AccentBright, 0.16f) : new Color(0, 0, 0, 0), 0, null, 14);
            sb.ContentMarginTop = sb.ContentMarginBottom = 9; sb.BorderColor = Ui.Line; sb.BorderWidthBottom = 1;
            if (on) { sb.BorderColor = Ui.AccentBright; sb.BorderWidthLeft = 3; }
            row.AddThemeStyleboxOverride("panel", sb);
            var h = Ui.H(10, Ui.Icon(IconOf(x.Kind), 18, x.Read ? Ui.Dim : Ui.AccentBright),
                Ui.Expand(Ui.V(2,
                    Ui.H(6, Ui.Expand(Ui.L(x.From, 11, Ui.Muted)), Ui.L(Ui.DateShort(x.Date), 11, Ui.Dim)),
                    Ui.L(x.Subject, 13, x.Read ? Ui.Muted : Ui.Text, x.Read ? null : Ui.Bold).Also(l => { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }))));
            h.MouseFilter = MouseFilterEnum.Ignore;
            foreach (var ch in h.GetChildren()) if (ch is Control cc) cc.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(h);
            int id = x.Id;
            row.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { _sel = id; Build(); Shell.I?.RefreshSide(); } };
            _list.AddChild(row);
        }
        Ui.Clear(_view);
        if (msg == null) { _view.AddChild(Ui.L("Писем нет", 14, Ui.Muted)); return; }
        _view.AddChild(Ui.H(10, Ui.Icon(IconOf(msg.Kind), 22, Ui.AccentBright), Ui.L(msg.From, 13, Ui.Muted), Ui.Spacer(), Ui.L(Ui.Date(msg.Date), 13, Ui.Dim)));
        _view.AddChild(Ui.L(msg.Subject, 22, Ui.Text, Ui.Bold).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        _view.AddChild(Ui.Rule());
        _view.AddChild(Ui.L(msg.Body, 14, Ui.Text).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = SizeFlags.ExpandFill; }));
        if (!string.IsNullOrEmpty(msg.Link))
        {
            string text = msg.Link.Split(':')[0] switch { "game" => "Открыть матч", "player" => "Профиль игрока", "table" => "Открыть таблицу", "playoffs" => "Сетка плей-офф", "club" => "Открыть клуб", _ => "Открыть" };
            var link = msg.Link;
            _view.AddChild(Ui.H(0, Ui.B(text, () => P.OpenLink(link), true, 13)));
        }
    }
}
