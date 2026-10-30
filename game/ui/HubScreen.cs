using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Главный экран карьеры: верхняя панель, меню слева, вкладки.</summary>
public partial class HubScreen : Control
{
    string _tab;
    Control _content;
    VBoxContainer _nav;
    Career C => Main.I.Career;
    TeamData MyTeam => C.Team(C.UserTeam);

    static readonly (string key, string name)[] Tabs =
    {
        ("overview", "Обзор"), ("lines", "Состав и звенья"), ("calendar", "Календарь"), ("table", "Таблица"),
        ("stats", "Статистика"), ("playoffs", "Плей-офф"),
    };

    readonly bool _embedded;
    string _calTeam;

    public HubScreen(string tab, bool embedded = false) { _tab = tab; _embedded = embedded; }
    public HubScreen() { _tab = "overview"; }

    public override void _Ready() => Build();

    void Build()
    {
        Ui.Clear(this);
        if (_embedded)
        {
            // внутри оболочки FM24: только страница
            SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
            _content = Ui.Expand(new MarginContainer(), true, true);
            foreach (var (k, v) in new[] { ("margin_left", 18), ("margin_right", 18), ("margin_top", 14), ("margin_bottom", 14) })
                _content.AddThemeConstantOverride(k, v);
            _content.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_content);
            if (_tab.StartsWith("calendar:")) { _calTeam = _tab[9..]; _tab = "calendar"; }
            _content.AddChild(_tab switch
            {
                "calendar" => Calendar(),
                "table" => TableView(0),
                "stats" => StatsView(0, false),
                "stats-mine" => StatsView(0, true),
                "playoffs" => PlayoffsView(),
                _ => Overview(),
            });
            return;
        }
        var root = Ui.V(0);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);
        root.AddChild(TopBar());
        var body = Ui.Expand(Ui.H(0), true, true);
        root.AddChild(body);

        _nav = Ui.V(4);
        var navPanel = new PanelContainer { CustomMinimumSize = new Vector2(230, 0) };
        navPanel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 0, null, 14));
        navPanel.AddChild(_nav);
        body.AddChild(navPanel);
        foreach (var (key, name) in Tabs)
        {
            var b = Ui.B(name, () => { _tab = key; Build(); });
            b.Alignment = HorizontalAlignment.Left;
            b.CustomMinimumSize = new Vector2(0, 40);
            bool on = key == _tab;
            b.AddThemeStyleboxOverride("normal", Ui.Box(on ? new Color(Ui.Accent, 0.22f) : new Color(0, 0, 0, 0), 6, null, 14));
            b.AddThemeStyleboxOverride("hover", Ui.Box(new Color(1, 1, 1, 0.05f), 6, null, 14));
            b.AddThemeColorOverride("font_color", on ? Colors.White : Ui.Muted);
            _nav.AddChild(b);
        }
        _nav.AddChild(Ui.Spacer());
        _nav.AddChild(Ui.L($"Сохранение — автоматически после каждого игрового дня.", 11, Ui.Dim).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        _nav.AddChild(Ui.B("Главное меню", () => { Main.I.Save(); Main.I.ShowMenu(); }));

        _content = Ui.Expand(new MarginContainer(), true, true);
        foreach (var (k, v) in new[] { ("margin_left", 26), ("margin_right", 26), ("margin_top", 22), ("margin_bottom", 22) })
            _content.AddThemeConstantOverride(k, v);
        body.AddChild(_content);
        Control view = _tab switch
        {
            "lines" => new LinesView(),
            "calendar" => Calendar(),
            "table" => TableView(0),
            "stats" => StatsView(0, false),
            "playoffs" => PlayoffsView(),
            _ => Overview(),
        };
        _content.AddChild(view);
    }

    // ------------------------------------------------------------------ верхняя панель
    Control TopBar()
    {
        var bar = new PanelContainer();
        var sb = Ui.Box(Ui.Panel2, 0, null, 20); sb.ContentMarginTop = sb.ContentMarginBottom = 12;
        sb.BorderColor = new Color(Ui.Accent, 0.9f); sb.BorderWidthBottom = 3;
        bar.AddThemeStyleboxOverride("panel", sb);
        var h = Ui.H(14);
        bar.AddChild(h);
        h.AddChild(new TeamBadge(MyTeam, 52));
        var row = C.Table[C.UserTeam];
        string rec = $"{row.W}-{row.L}-{row.Otl} · {row.Pts} {Ui.Plural(row.Pts, "очко", "очка", "очков")}";
        var divRank = C.Sorted(t => t.Division == MyTeam.Division).FindIndex(r => r.Team == C.UserTeam) + 1;
        h.AddChild(Ui.V(0, Ui.L(MyTeam.Name, 22, Ui.Text, Ui.Bold), Ui.L($"{rec} · {divRank}-е место в дивизионе", 13, Ui.Muted)));
        h.AddChild(Ui.Spacer());
        string phase = C.Phase switch { Phase.Regular => "Регулярный чемпионат", Phase.Playoffs => "Плей-офф", _ => "Сезон завершён" };
        h.AddChild(Ui.V(0, Ui.L(Ui.Date(C.Today), 18, Ui.Text, Ui.Semi, HorizontalAlignment.Right), Ui.L($"{phase} 2026-27", 12, Ui.Muted, null, HorizontalAlignment.Right)));
        h.AddChild(Ui.Spacer(false, 10));
        var (label, enabled) = ContinueLabel();
        var go = Ui.B(label, OnContinue, true, 16);
        go.Disabled = !enabled;
        go.CustomMinimumSize = new Vector2(260, 46);
        h.AddChild(go);
        return bar;
    }

    (string, bool) ContinueLabel()
    {
        if (C.Phase == Phase.Finished) return ("Сезон завершён", false);
        var g = C.UserGameToday();
        if (g != null) return ($"Матч дня: {Opp(g)}", true);
        var next = C.NextGame(C.UserTeam);
        if (next != null) return ($"Дальше: к матчу {Ui.DateShort(next.Date)}", true);
        return ("Дальше: следующий день", true);
    }

    string Opp(ScheduledGame g) => g.Home == C.UserTeam ? $"с «{C.Team(g.Away).City}»" : $"в гостях у «{C.Team(g.Home).City}»";

    void OnContinue()
    {
        var g = C.UserGameToday();
        if (g == null)
        {
            C.SimToUserGame();
            Main.I.Save();
            g = C.UserGameToday();
        }
        if (g != null) Main.I.ShowMatch(g);
        else Build();
    }

    // ------------------------------------------------------------------ обзор
    Control Overview()
    {
        var v = Ui.V(18);
        if (C.Phase == Phase.Finished && C.Champion != null)
        {
            var ch = C.Team(C.Champion);
            v.AddChild(Ui.Card(Ui.H(16, new TeamBadge(ch, 64), Ui.V(2, Ui.L("Обладатель Кубка Стэнли 2027", 14, Ui.Muted, Ui.Semi), Links.Club(ch, ch.Name, 28, Ui.Text, Ui.Black),
                Ui.L(C.Champion == C.UserTeam ? "Это ваш клуб! Многосезонная карьера появится в следующих версиях." : "Многосезонная карьера появится в следующих версиях — можно начать новую.", 14, Ui.Muted))), Ui.Panel2, 20, new Color(Ui.Accent, 0.6f)));
        }
        var cols = Ui.Expand(Ui.H(18), true, true);
        v.AddChild(cols);
        var left = Ui.Expand(Ui.V(18), true, true);
        var right = Ui.V(18); right.CustomMinimumSize = new Vector2(420, 0);
        cols.AddChild(left); cols.AddChild(right);

        // Следующий матч
        var next = C.NextGame(C.UserTeam);
        if (next != null)
        {
            var o = C.Preview(next);
            bool home = next.Home == C.UserTeam;
            var opp = C.Team(home ? next.Away : next.Home);
            double p = home ? o.PHome : 1 - o.PHome;
            var h = Ui.H(16);
            h.AddChild(new TeamBadge(MyTeam, 64));
            h.AddChild(Ui.L(home ? "дома" : "в гостях", 13, Ui.Muted));
            h.AddChild(new TeamBadge(opp, 64));
            h.AddChild(Ui.Expand(Ui.V(4, Ui.H(0, Ui.L(home ? "" : "@ ", 20, Ui.Text, Ui.Bold), Links.Club(opp, opp.Name, 20, Ui.Text, Ui.Bold)),
                Ui.L($"{Ui.Date(next.Date)}{(next.Playoff ? " · плей-офф" : "")}", 14, Ui.Muted),
                Ui.H(6, Ui.Icon("mail-open", 14, Ui.Muted), Ui.L(Pundits.Forecast(C.Data, next, o), 13, Ui.Muted)))));
            left.AddChild(Section("Следующий матч", h));
        }
        // Последние матчи
        var last = C.TeamGames(C.UserTeam).Where(g => g.Played).OrderByDescending(g => g.Date).Take(6).ToList();
        var lt = new Table(new[] { "Дата", "", "Соперник", "Счёт", "" }, new[] { 90, 40, 0, 80, 40 });
        foreach (var g in last) lt.Add(ResultCells(g, C.UserTeam), false, () => Main.I.ShowMatch(g));
        if (last.Count == 0) lt.Add(new Control[] { Ui.L("Сезон ещё не начался — нажмите «Дальше».", 14, Ui.Muted) });
        left.AddChild(Section("Последние матчи", lt));
        // Результаты лиги за последний игровой день
        var lastDay = C.Games.Where(g => g.Played).Select(g => g.Date).DefaultIfEmpty().Max();
        if (lastDay != default)
        {
            var rt = new Table(new[] { "Гости", "", "Хозяева", "" }, new[] { 0, 90, 0, 40 }, new[] { HorizontalAlignment.Left, HorizontalAlignment.Center, HorizontalAlignment.Left, HorizontalAlignment.Left });
            foreach (var g in C.GamesOn(lastDay).Where(g => g.Played))
            {
                var r = g.Result;
                rt.Add(new Control[] { TeamCell(g.Away), Ui.L($"{r.AwayGoals} : {r.HomeGoals}", 15, Ui.Text, Ui.Bold), TeamCell(g.Home), Ui.L(r.End == "REG" ? "" : r.End == "OT" ? "ОТ" : "Б", 12, Ui.Muted) },
                    g.Home == C.UserTeam || g.Away == C.UserTeam, () => Main.I.ShowMatch(g));
            }
            left.AddChild(Section($"Результаты: {Ui.Date(lastDay)}", rt));
        }
        // Дивизион
        var dt = new Table(new[] { "#", "Клуб", "И", "О" }, new[] { 26, 0, 40, 40 });
        int i = 0;
        foreach (var r in C.Sorted(t => t.Division == MyTeam.Division))
            dt.Add(new Control[] { Ui.L($"{++i}", 13, Ui.Muted), TeamCell(r.Team), Ui.L($"{r.Gp}", 14), Ui.L($"{r.Pts}", 14, Ui.Text, Ui.Bold) }, r.Team == C.UserTeam);
        right.AddChild(Section($"{MyTeam.Division} дивизион", dt));
        // Лидеры клуба
        var lead = C.Skaters.Where(kv => C.Data.PlayerById.TryGetValue(kv.Key, out var p) && p.Team == C.UserTeam)
            .OrderByDescending(kv => kv.Value.Pts).ThenByDescending(kv => kv.Value.G).Take(5).ToList();
        var pt = new Table(new[] { "Игрок", "Г", "П", "О" }, new[] { 0, 36, 36, 40 });
        foreach (var (id, s) in lead.Select(kv => (kv.Key, kv.Value)))
            pt.Add(new Control[] { Links.Player(C.Data.PlayerById[id], null, 14), Ui.L($"{s.G}", 14), Ui.L($"{s.A}", 14), Ui.L($"{s.Pts}", 14, Ui.Text, Ui.Bold) }, false,
                () => PlayerCard.Show(this, C.Data.PlayerById[id], C));
        if (lead.Count == 0) pt.Add(new Control[] { Ui.L("Пока нет сыгранных матчей", 14, Ui.Muted) });
        right.AddChild(Section("Лидеры клуба", pt));
        return Ui.Scroll(v);
    }

    Control[] ResultCells(ScheduledGame g, string team)
    {
        var r = g.Result;
        bool home = g.Home == team;
        int my = home ? r.HomeGoals : r.AwayGoals, op = home ? r.AwayGoals : r.HomeGoals;
        bool win = my > op;
        string tag = win ? "В" : r.End == "REG" ? "П" : "ПО";
        var col = win ? Ui.Win : r.End == "REG" || g.Playoff ? Ui.Loss : Ui.Otl;
        return new Control[]
        {
            Ui.L(Ui.DateShort(g.Date), 13, Ui.Muted), Ui.L(home ? "" : "@", 13, Ui.Muted), TeamCell(home ? g.Away : g.Home),
            Ui.L($"{my}:{op}{(r.End == "REG" ? "" : r.End == "OT" ? " ОТ" : " Б")}", 14, Ui.Text, Ui.Semi), Ui.L(tag, 13, col, Ui.Bold),
        };
    }

    Control TeamCell(string abbr, int size = 22)
    {
        var t = C.Team(abbr);
        var h = Ui.H(8, new TeamBadge(t, size), Links.Club(t, t.City, 14, abbr == C.UserTeam ? Colors.White : Ui.Text, abbr == C.UserTeam ? Ui.Semi : null));
        h.MouseFilter = MouseFilterEnum.Ignore;
        return h;
    }

    static Control Section(string title, Control content)
    {
        var v = Ui.V(10, Ui.L(title, 17, Ui.Text, Ui.Semi), content);
        return Ui.Card(v, Ui.Panel, 16);
    }

    // ------------------------------------------------------------------ календарь
    Control Calendar() => Calendar(_calTeam ?? C.UserTeam);

    static bool _calList;   // false — сетка месяца, true — список

    Control Calendar(string team)
    {
        var v = Ui.V(12);
        if (team != C.UserTeam) v.AddChild(Ui.L($"Календарь: {C.Team(team).Name}", 20, Ui.Text, Ds.Display));
        v.AddChild(new Segmented(new[] { "Месяц", "Список" }, _calList ? 1 : 0, k => { _calList = k == 1; CallDeferred(nameof(Build)); }) { SizeFlagsHorizontal = SizeFlags.ShrinkBegin });
        if (!_calList)
        {
            v.AddChild(Ui.Card(new CalendarGrid(C, team), Ui.Panel, 16, Ds.Edge));
            return Ui.Scroll(v);
        }
        var t = new Table(new[] { "Дата", "", "Соперник", "Результат", "" }, new[] { 140, 30, 0, 170, 50 });
        int lastMonth = 0;
        foreach (var g in C.TeamGames(team).OrderBy(g => g.Date))
        {
            if (g.Date.Month != lastMonth)
            {
                lastMonth = g.Date.Month;
                t.Add(new Control[] { Ui.Margin(Ui.L(MonthName(g.Date), 15, Ui.Text, Ds.DisplayLight), 0, 10, 0, 4) });
            }
            if (g.Played) t.Add(ResultCells(g, team).Select((c, i) => i == 0 ? Ui.L(Ui.Date(g.Date), 13, Ui.Muted) : c).ToArray(), false, () => Main.I.ShowMatch(g));
            else
            {
                bool home = g.Home == team;
                bool b2b = C.PlayedYesterday(team, g.Date);
                t.Add(new Control[] { Ui.L(Ui.Date(g.Date), 13, g.Date == C.Today ? Ui.Text : Ui.Muted), Ui.L(home ? "" : "@", 13, Ui.Muted), TeamCell(home ? g.Away : g.Home),
                    Ui.L("—", 13, Ui.Dim), Ui.L(b2b ? "2-й день" : "", 11, Ui.Otl) }, g.Date == C.Today);
            }
        }
        v.AddChild(Ui.Card(t, Ui.Panel, 16, Ds.Edge));
        return Ui.Scroll(v);
    }

    static string MonthName(DateOnly d) => new[] { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" }[d.Month - 1] + $" {d.Year}";

    // ------------------------------------------------------------------ таблица
    Control TableView(int mode)
    {
        var v = Ui.V(14);
        var head = Ui.H(16, Ui.Expand(Ui.L("Турнирная таблица", 20, Ui.Text, Ds.Display)));
        head.AddChild(new Segmented(new[] { "Дивизионы", "Конференции", "Лига", "Уайлд-кард" }, mode, k => Swap(TableView(k))));
        v.AddChild(head);
        var groups = new List<(string, List<StandingRow>, int cut)>();
        switch (mode)
        {
            case 0:
                foreach (var conf in C.Conferences) foreach (var div in C.Divisions(conf)) groups.Add(($"{div} дивизион", C.Sorted(t => t.Division == div), 3));
                break;
            case 1:
                foreach (var conf in C.Conferences) groups.Add(($"{conf} конференция", C.Sorted(t => t.Conference == conf), 0));
                break;
            case 2: groups.Add(("Лига", C.Sorted(), 0)); break;
            default:
                foreach (var conf in C.Conferences)
                {
                    var (d1, d2, wild) = C.PlayoffPicture(conf);
                    var divs = C.Divisions(conf).ToList();
                    groups.Add(($"{divs[0]} дивизион", d1.Take(3).ToList(), 0));
                    groups.Add(($"{divs[1]} дивизион", d2.Take(3).ToList(), 0));
                    groups.Add(($"{conf}: уайлд-кард", wild, 2));
                }
                break;
        }
        var grid = new GridContainer { Columns = mode == 2 ? 1 : 2 };
        grid.AddThemeConstantOverride("h_separation", 18); grid.AddThemeConstantOverride("v_separation", 18);
        foreach (var (title, rows, cut) in groups)
        {
            var t = new Table(new[] { "#", "Клуб", "И", "В", "П", "ПО", "О", "%О", "Ш", "Серия" }, new[] { 24, 0, 32, 32, 32, 32, 38, 50, 64, 50 });
            int i = 0;
            foreach (var r in rows)
            {
                i++;
                t.Add(new Control[] { Ui.L($"{i}", 13, Ui.Muted), TeamCell(r.Team, 20), Ui.L($"{r.Gp}", 13), Ui.L($"{r.W}", 13), Ui.L($"{r.L}", 13), Ui.L($"{r.Otl}", 13),
                    Ui.L($"{r.Pts}", 14, Ui.Text, Ui.Bold), Ui.L(r.Gp == 0 ? "—" : $"{r.PtsPct:.000}", 13, Ui.Muted), Ui.L($"{r.Gf}-{r.Ga}", 13, Ui.Muted), Ui.L(r.Streak, 12, Ui.Muted) }, r.Team == C.UserTeam, () => Shell.I?.Go("club", null, r.Team));
                if (cut > 0 && i == cut) t.AddChild(Ui.Rule(new Color(Ui.Accent, 0.6f), 2));
            }
            grid.AddChild(Ui.Expand(Section(title, t)));
        }
        v.AddChild(grid);
        v.AddChild(Ui.L("Порядок НХЛ: очки, меньше игр, победы в основное время, победы без буллитов, все победы, разница шайб.", 12, Ui.Dim));
        return Ui.Scroll(v);
    }

    void Swap(Control view)
    {
        Ui.Clear(_content);
        _content.AddChild(view);
    }

    // ------------------------------------------------------------------ статистика
    Control StatsView(int mode, bool mine)
    {
        var v = Ui.V(14);
        var head = Ui.H(16, Ui.Expand(Ui.L(mine ? "Статистика клуба" : "Статистика лиги", 20, Ui.Text, Ds.Display)));
        if (!_embedded)
        {
            var mineBox = new CheckBox { Text = "Только мой клуб", ButtonPressed = mine, FocusMode = FocusModeEnum.None };
            mineBox.Toggled += on => Swap(StatsView(mode, on));
            head.AddChild(mineBox);
        }
        head.AddChild(new Segmented(new[] { "Очки", "Голы", "Передачи", "Вратари", "Плей-офф" }, mode, k => Swap(StatsView(k, mine))));
        v.AddChild(head);
        bool po = mode == 4;
        var sk = po ? C.PoSkaters : C.Skaters;
        if (mode == 3)
        {
            int minGp = Math.Max(1, C.Table.Values.Max(r => r.Gp) / 4);
            var rows = C.Goalies.Where(kv => kv.Value.Gp >= (mine ? 1 : minGp) && (!mine || C.Data.PlayerById[kv.Key].Team == C.UserTeam))
                .OrderByDescending(kv => kv.Value.SvPct).Take(40).ToList();
            var t = new Table(new[] { "#", "Вратарь", "Клуб", "И", "В", "П", "ПО", "%ОБ", "КН", "Сух" }, new[] { 30, 0, 140, 36, 36, 36, 36, 60, 50, 40 });
            int i = 0;
            foreach (var (id, s) in rows.Select(kv => (kv.Key, kv.Value)))
            {
                var p = C.Data.PlayerById[id];
                t.Add(new Control[] { Ui.L($"{++i}", 13, Ui.Muted), Links.Player(p, null, 14), TeamCell(p.Team, 18), Ui.L($"{s.Gp}", 13), Ui.L($"{s.W}", 13), Ui.L($"{s.L}", 13), Ui.L($"{s.Otl}", 13),
                    Ui.L($"{s.SvPct:.000}", 14, Ui.Text, Ui.Bold), Ui.L($"{s.Gaa:0.00}", 13), Ui.L($"{s.So}", 13) }, p.Team == C.UserTeam, () => PlayerCard.Show(this, p, C));
            }
            if (rows.Count == 0) t.Add(new Control[] { Ui.L($"Пока мало матчей (нужно от {minGp})", 14, Ui.Muted) });
            v.AddChild(t);
            v.AddChild(Ui.L($"В таблице вратари с {minGp}+ матчами.", 12, Ui.Dim));
        }
        else
        {
            Func<SkaterLine, int> key = mode == 1 ? s => s.G : mode == 2 ? s => s.A : s => s.Pts;
            var rows = sk.Where(kv => C.Data.PlayerById.TryGetValue(kv.Key, out var p) && !p.IsGoalie && (!mine || p.Team == C.UserTeam))
                .OrderByDescending(kv => key(kv.Value)).ThenByDescending(kv => kv.Value.G).ThenBy(kv => kv.Value.Gp).Take(50).ToList();
            var t = new Table(new[] { "#", "Игрок", "Поз", "Клуб", "И", "Г", "П", "О", "+/−", "Бр", "Вр", "ГБ", "ПГ" }, new[] { 30, 0, 40, 140, 36, 36, 36, 44, 44, 40, 52, 40, 40 });
            int i = 0;
            foreach (var (id, s) in rows.Select(kv => (kv.Key, kv.Value)))
            {
                var p = C.Data.PlayerById[id];
                t.Add(new Control[] { Ui.L($"{++i}", 13, Ui.Muted), Links.Player(p, null, 14), Ui.L(Ui.PosRu(p.Pos), 12, Ui.Muted), TeamCell(p.Team, 18), Ui.L($"{s.Gp}", 13),
                    Ui.L($"{s.G}", 13, mode == 1 ? Ui.Text : Ui.Muted, mode == 1 ? Ui.Bold : null), Ui.L($"{s.A}", 13, mode == 2 ? Ui.Text : Ui.Muted, mode == 2 ? Ui.Bold : null),
                    Ui.L($"{s.Pts}", 14, mode == 0 || po ? Ui.Text : Ui.Muted, mode == 0 || po ? Ui.Bold : null),
                    Ui.L(s.Toi > 0 ? Ui.Signed(s.Pm, "0") : "—", 13, Ui.Muted), Ui.L(s.Toi > 0 ? $"{s.S}" : "—", 13, Ui.Muted), Ui.L(Ui.Toi(s.Gp > 0 ? s.Toi / (double)s.Gp : 0), 13, Ui.Muted),
                    Ui.L($"{s.Ppg}", 13, Ui.Muted), Ui.L($"{s.Gwg}", 13, Ui.Muted) },
                    p.Team == C.UserTeam, () => PlayerCard.Show(this, p, C));
            }
            if (rows.Count == 0) t.Add(new Control[] { Ui.L(po ? "Плей-офф ещё не начался" : "Пока нет сыгранных матчей", 14, Ui.Muted) });
            v.AddChild(t);
            v.AddChild(Ui.L("Бр — броски в створ, Вр — среднее время на льду, ГБ — голы в большинстве, ПГ — победные голы.", 12, Ui.Dim));
        }
        return Ui.Scroll(v);
    }

    // ------------------------------------------------------------------ плей-офф
    Control PlayoffsView()
    {
        var v = Ui.V(16);
        v.AddChild(Ui.L(C.Phase == Phase.Regular ? "Плей-офф: расклад, если бы сезон закончился сегодня" : "Плей-офф Кубка Стэнли 2027", 20, Ui.Text, Ds.Display));
        if (C.Phase == Phase.Regular)
        {
            v.AddChild(Ui.L("Топ-3 каждого дивизиона и две «уайлд-кард» в конференции. Подробно — вкладка «Таблица» → «Уайлд-кард».", 14, Ui.Muted));
            var h = Ui.Expand(Ui.H(18), true, true);
            foreach (var conf in C.Conferences)
            {
                var (d1, d2, wild) = C.PlayoffPicture(conf);
                var list = Ui.V(6);
                foreach (var (a, b) in Pairs(conf, d1, d2, wild)) list.AddChild(SeriesCard(a, b, 0, 0, false));
                h.AddChild(Ui.Expand(Section($"{conf} конференция", list)));
            }
            v.AddChild(h);
            return Ui.Scroll(v);
        }
        var grid = Ui.Expand(Ui.H(14), true, true);
        string[] rounds = { "1-й раунд", "2-й раунд", "Финал конференции", "Финал Кубка Стэнли" };
        for (int r = 1; r <= 4; r++)
        {
            var col = Ui.Expand(Ui.V(10), true, false);
            col.AddChild(Ui.L(rounds[r - 1], 16, Ui.Text, Ui.Semi));
            var ss = C.Bracket.Where(s => s.Round == r).OrderBy(s => s.Slot).ToList();
            if (ss.Count == 0) col.AddChild(Ui.L("—", 14, Ui.Dim));
            foreach (var s in ss)
            {
                if (r <= 2 && s.Slot == (r == 1 ? 4 : 2)) col.AddChild(Ui.Spacer(false, 16));
                col.AddChild(SeriesCard(s.High, s.Low, s.WinsHigh, s.WinsLow, true));
            }
            grid.AddChild(col);
        }
        v.AddChild(grid);
        if (C.Champion != null) v.AddChild(Ui.H(0, Ui.L("Кубок Стэнли выиграл ", 18, Ui.Text, Ui.Bold), Links.Club(C.Team(C.Champion), $"«{C.Team(C.Champion).Name}»", 18, Ui.Text, Ui.Bold)));
        return Ui.Scroll(v);
    }

    IEnumerable<(string, string)> Pairs(string conf, List<StandingRow> d1, List<StandingRow> d2, List<StandingRow> wild)
    {
        if (d1.Count < 3 || d2.Count < 3 || wild.Count < 2) yield break;
        var all = C.Sorted(t => t.Conference == conf);
        bool d1Best = all.IndexOf(d1[0]) < all.IndexOf(d2[0]);
        var (wa, wb) = d1Best ? (wild[1], wild[0]) : (wild[0], wild[1]);
        yield return (d1[0].Team, wa.Team); yield return (d1[1].Team, d1[2].Team);
        yield return (d2[0].Team, wb.Team); yield return (d2[1].Team, d2[2].Team);
    }

    Control SeriesCard(string a, string b, int wa, int wb, bool score)
    {
        var v = Ui.V(4);
        foreach (var (t, w, other) in new[] { (a, wa, wb), (b, wb, wa) })
        {
            var h = Ui.H(8, TeamCell(t, 22));
            h.AddChild(Ui.Spacer());
            if (score) h.AddChild(Ui.L($"{w}", 16, w == 4 ? Ui.Win : w > other ? Ui.Text : Ui.Muted, Ui.Bold));
            v.AddChild(h);
        }
        bool mine = a == C.UserTeam || b == C.UserTeam;
        return Ui.Card(v, Ui.Panel2, 12, mine ? Ui.Accent : null);
    }
}
