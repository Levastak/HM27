using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Оболочка карьеры в духе FM24: шапка в цветах клуба (назад/вперёд, эмблема, заголовок, поиск, дата, «Продолжить»),
/// меню слева с иконками, вкладки раздела, страница. История переходов — как в браузере.
/// </summary>
public partial class Shell : Control
{
    public static Shell I { get; private set; }
    Career C => Main.I.Career;

    public readonly record struct Nav(string Page, string Sub, string Arg);
    readonly Stack<Nav> _back = new(), _fwd = new();
    Nav _cur;
    Control _header, _sideBox, _subBar;
    MarginContainer _page;

    static readonly (string key, string icon, string name)[] Menu =
    {
        ("home", "house", "Главная"), ("inbox", "inbox", "Входящие"), ("squad", "users", "Состав"), ("lines", "list-ordered", "Звенья"),
        ("schedule", "calendar-days", "Расписание"), ("comp", "trophy", "Соревнования"), ("club", "landmark", "Клуб"), ("medical", "cross", "Лазарет"),
    };

    static readonly Dictionary<string, (string key, string name)[]> Subs = new()
    {
        ["squad"] = new[] { ("players", "Игроки"), ("stats", "Статистика") },
        ["comp"] = new[] { ("table", "Таблица"), ("stats", "Статистика лиги"), ("playoffs", "Плей-офф") },
        ["club"] = new[] { ("overview", "Обзор"), ("players", "Игроки"), ("schedule", "Расписание") },
    };

    public Shell(string page, string sub = null, string arg = null) { _cur = new Nav(page, sub, arg); }
    public Shell() { _cur = new Nav("home", null, null); }

    public override void _Ready()
    {
        I = this;
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);
        _header = new PanelContainer();
        root.AddChild(_header);
        var body = Ui.Expand(Ui.H(0), true, true);
        root.AddChild(body);
        var side = new PanelContainer { CustomMinimumSize = new Vector2(214, 0) };
        side.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Ui.Side });
        _sideBox = Ui.V(2);
        side.AddChild(Ui.Margin(_sideBox, 8, 10, 8, 10));
        body.AddChild(side);
        var right = Ui.Expand(Ui.V(0), true, true);
        body.AddChild(right);
        _subBar = new PanelContainer();
        right.AddChild(_subBar);
        _page = Ui.Expand(new MarginContainer(), true, true);
        right.AddChild(_page);
        Render();
    }

    public override void _ExitTree() { if (I == this) I = null; }

    // ------------------------------------------------------------------ навигация
    public void Go(string page, string sub = null, string arg = null)
    {
        var n = new Nav(page, sub ?? DefaultSub(page), arg);
        if (n == _cur) return;
        _back.Push(_cur); _fwd.Clear();
        _cur = n;
        Render();
    }

    void Back() { if (_back.Count == 0) return; _fwd.Push(_cur); _cur = _back.Pop(); Render(); }
    void Fwd() { if (_fwd.Count == 0) return; _back.Push(_cur); _cur = _fwd.Pop(); Render(); }
    public void Refresh() => Render();
    public void RefreshSide() => BuildSide();

    static string DefaultSub(string page) => Subs.TryGetValue(page, out var s) ? s[0].key : null;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.Xbutton1) Back();
            if (mb.ButtonIndex == MouseButton.Xbutton2) Fwd();
        }
        if (e is InputEventKey { Pressed: true } k)
        {
            if (k.Keycode == Key.Space && !k.Echo) OnContinue();
            if (k.Keycode == Key.Left && k.AltPressed) Back();
            if (k.Keycode == Key.Right && k.AltPressed) Fwd();
        }
    }

    void Render()
    {
        if (_cur.Sub == null && Subs.ContainsKey(_cur.Page)) _cur = _cur with { Sub = DefaultSub(_cur.Page) };
        BuildHeader();
        BuildSide();
        BuildSubs();
        Ui.Clear(_page);
        Control view = _cur.Page switch
        {
            "inbox" => new InboxPage(_cur.Arg),
            "squad" => _cur.Sub == "stats" ? Embedded("stats-mine") : new SquadPage(C.UserTeam),
            "lines" => Ui.Margin(new LinesView(), 16, 12, 16, 12).Also(m => { m.SizeFlagsHorizontal = m.SizeFlagsVertical = SizeFlags.ExpandFill; }),
            "schedule" => Embedded("calendar"),
            "comp" => Embedded(_cur.Sub == "stats" ? "stats" : _cur.Sub),
            "player" => new PlayerPage(int.Parse(_cur.Arg)),
            "club" => _cur.Sub == "schedule" ? Embedded("calendar:" + (_cur.Arg ?? C.UserTeam)) : _cur.Sub == "players" ? new SquadPage(_cur.Arg ?? C.UserTeam) : new ClubPage(_cur.Arg ?? C.UserTeam),
            "medical" => new MedicalPage(),
            _ => new HomePage(),
        };
        _page.AddChild(view);
    }

    static Control Embedded(string tab) => new HubScreen(tab, embedded: true);

    // ------------------------------------------------------------------ шапка
    void BuildHeader()
    {
        var t = C.Team(_cur.Page == "club" ? (_cur.Arg ?? C.UserTeam) : _cur.Page == "player" ? C.Data.PlayerById[int.Parse(_cur.Arg)].Team : C.UserTeam);
        var c1 = new Color(t.Colors[0]);
        if (c1.Luminance < 0.05f) c1 = c1.Lightened(0.12f);
        var c2 = new Color(t.Colors.Count > 1 ? t.Colors[1] : t.Colors[0]);
        var sb = new StyleBoxFlat { BgColor = c1, BorderColor = c2, BorderWidthBottom = 3 };
        sb.ContentMarginLeft = 12; sb.ContentMarginRight = 14; sb.ContentMarginTop = 8; sb.ContentMarginBottom = 8;
        _header.AddThemeStyleboxOverride("panel", sb);
        Ui.Clear(_header);
        var fg = Ui.ReadableOn(c1);
        var h = Ui.H(10);
        _header.AddChild(h);
        h.AddChild(IconButton("chevron-left", Back, fg, _back.Count > 0));
        h.AddChild(IconButton("chevron-right", Fwd, fg, _fwd.Count > 0));
        h.AddChild(Ui.Spacer(false, 4));
        h.AddChild(new TeamBadge(t, 46));
        var (title, sub) = Titles(t);
        h.AddChild(Ui.V(0, Ui.L(title, 20, fg, Ui.Bold), Ui.L(sub, 12, new Color(fg, 0.75f))));
        h.AddChild(Ui.Spacer());
        h.AddChild(SearchBox(fg, c1));
        h.AddChild(Ui.Spacer(false, 6));
        string phase = C.Phase switch { Phase.Regular => "Регулярный чемпионат", Phase.Playoffs => "Плей-офф", _ => "Сезон завершён" };
        h.AddChild(Ui.V(0, Ui.L(Ui.Date(C.Today), 15, fg, Ui.Semi, HorizontalAlignment.Right), Ui.L(phase, 11, new Color(fg, 0.7f), null, HorizontalAlignment.Right)));
        h.AddChild(ContinueButton());
    }

    (string, string) Titles(TeamData t)
    {
        var row = C.Table[t.Abbr];
        int dplace = C.Sorted(x => x.Division == t.Division).FindIndex(r => r.Team == t.Abbr) + 1;
        string divGen = t.Division switch { "Центральный" => "Центральном", "Тихоокеанский" => "Тихоокеанском", "Атлантический" => "Атлантическом", "Столичный" => "Столичном", _ => t.Division };
        var nx = C.NextGame(t.Abbr);
        string next = "";
        if (nx != null)
        {
            int dd = nx.Date.DayNumber - C.Today.DayNumber;
            next = $" · След. матч: {C.Team(nx.Home == t.Abbr ? nx.Away : nx.Home).City} ({(nx.Home == t.Abbr ? "Д" : "Г")}) {(dd == 0 ? "сегодня" : dd == 1 ? "завтра" : $"через {dd} {Ui.Plural(dd, "день", "дня", "дней")}")}";
        }
        string rec = row.Gp == 0 ? $"{t.Name}{next}" : $"{dplace}-е в {divGen} · {row.W}-{row.L}-{row.Otl}, {row.Pts} {Ui.Plural(row.Pts, "очко", "очка", "очков")}{next}";
        return _cur.Page switch
        {
            "inbox" => ("Входящие", rec),
            "squad" => ("Состав", rec),
            "lines" => ("Звенья", rec),
            "schedule" => ("Расписание", rec),
            "comp" => ("Соревнования", "НХЛ · сезон 2026-27"),
            "player" => (C.Data.PlayerById[int.Parse(_cur.Arg)].NameRu, rec),
            "club" => (t.Name, rec),
            "medical" => ("Медицинский центр", rec),
            _ => ("Главная", rec),
        };
    }

    static Button IconButton(string icon, Action a, Color fg, bool enabled)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 30), Flat = true, Disabled = !enabled, MouseDefaultCursorShape = CursorShape.PointingHand };
        var ic = Ui.Icon(icon, 20, new Color(fg, enabled ? 0.95f : 0.3f));
        ic.SetAnchorsPreset(LayoutPreset.FullRect);
        b.AddChild(ic);
        b.AddThemeStyleboxOverride("hover", Ui.Box(new Color(0, 0, 0, 0.18f), 4, null, 0));
        b.AddThemeStyleboxOverride("pressed", Ui.Box(new Color(0, 0, 0, 0.3f), 4, null, 0));
        b.Pressed += a;
        return b;
    }

    Control SearchBox(Color fg, Color bg)
    {
        var box = new LineEdit { PlaceholderText = "Поиск игрока или клуба", CustomMinimumSize = new Vector2(250, 32), ClearButtonEnabled = true };
        var sb = Ui.Box(new Color(0, 0, 0, 0.22f), 4, null, 10);
        sb.ContentMarginLeft = 32;
        box.AddThemeStyleboxOverride("normal", sb);
        box.AddThemeStyleboxOverride("focus", Ui.Box(new Color(0, 0, 0, 0.32f), 4, new Color(fg, 0.5f), 10).Also(s => s.ContentMarginLeft = 32));
        box.AddThemeColorOverride("font_color", fg);
        box.AddThemeColorOverride("font_placeholder_color", new Color(fg, 0.55f));
        box.AddChild(Ui.Icon("search", 16, new Color(fg, 0.7f)).Also(i => i.Position = new Vector2(10, 8)));
        var popup = new PopupPanel();
        popup.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, Ui.Line, 6));
        var list = Ui.V(2);
        popup.AddChild(list);
        box.AddChild(popup);
        box.TextChanged += q =>
        {
            Ui.Clear(list);
            q = q.Trim().ToLowerInvariant();
            if (q.Length < 2) { popup.Hide(); return; }
            var teams = C.Data.Teams.Where(t => t.Name.ToLowerInvariant().Contains(q) || t.NameEn.ToLowerInvariant().Contains(q) || t.Abbr.ToLowerInvariant() == q).Take(4);
            var players = C.Data.Players.Where(p => (p.NameRu ?? "").ToLowerInvariant().Contains(q) || (p.Name ?? "").ToLowerInvariant().Contains(q)).OrderByDescending(p => p.Ovr).Take(10);
            foreach (var t in teams) list.AddChild(SearchRow(new TeamBadge(t, 20), t.Name, $"{t.Division} дивизион", () => { popup.Hide(); box.Text = ""; Go("club", null, t.Abbr); }));
            foreach (var p in players) list.AddChild(SearchRow(new TeamBadge(C.Team(p.Team), 20), p.NameRu, $"{Ui.PosRu(p.Pos)} · {C.Team(p.Team).City}", () => { popup.Hide(); box.Text = ""; Go("player", null, p.Id.ToString()); }));
            if (list.GetChildCount() == 0) list.AddChild(Ui.L("Ничего не найдено", 13, Ui.Muted));
            var gp = box.GetScreenPosition();
            popup.Popup(new Rect2I((int)gp.X, (int)(gp.Y + box.Size.Y + 4), 340, 10));
            box.CallDeferred(Control.MethodName.GrabFocus);
        };
        return box;
    }

    static Control SearchRow(Control icon, string name, string sub, Action a)
    {
        var b = new Button { Flat = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(320, 36), MouseDefaultCursorShape = CursorShape.PointingHand };
        b.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Panel2, 4, null, 6));
        var h = Ui.H(8, icon, Ui.L(name, 13, Ui.Text, Ui.Semi), Ui.L(sub, 11, Ui.Muted));
        h.MouseFilter = MouseFilterEnum.Ignore;
        h.SetAnchorsPreset(LayoutPreset.FullRect); h.OffsetLeft = 8;
        b.AddChild(h);
        b.Pressed += a;
        return b;
    }

    Control ContinueButton()
    {
        string label, sub;
        bool enabled = C.Phase != Phase.Finished;
        var g = C.UserGameToday();
        if (!enabled) { label = "Сезон завершён"; sub = C.Champion != null ? $"Кубок: «{C.Team(C.Champion).City}»" : ""; }
        else if (g != null) { label = "Матч дня"; sub = g.Home == C.UserTeam ? $"с «{C.Team(g.Away).City}»" : $"в гостях у «{C.Team(g.Home).City}»"; }
        else
        {
            var next = C.NextGame(C.UserTeam);
            label = "Продолжить";
            sub = next != null ? $"следующий матч {Ui.DateShort(next.Date)}" : "следующий день";
        }
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(210, 48), Disabled = !enabled, MouseDefaultCursorShape = CursorShape.PointingHand };
        var bg = new Color("2F9E5B");
        b.AddThemeStyleboxOverride("normal", Ui.Box(bg, 4, null, 12));
        b.AddThemeStyleboxOverride("hover", Ui.Box(bg.Lightened(0.1f), 4, null, 12));
        b.AddThemeStyleboxOverride("pressed", Ui.Box(bg.Darkened(0.15f), 4, null, 12));
        b.AddThemeStyleboxOverride("disabled", Ui.Box(new Color(0, 0, 0, 0.25f), 4, null, 12));
        var v = Ui.V(0, Ui.H(6, Ui.L(label, 15, Colors.White, Ui.Bold), Ui.Spacer(), Ui.Icon("arrow-right", 18, Colors.White)), Ui.L(sub, 11, new Color(1, 1, 1, 0.8f)));
        v.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var ch in v.GetChildren()) if (ch is Control cc) cc.MouseFilter = MouseFilterEnum.Ignore;
        v.SetAnchorsPreset(LayoutPreset.FullRect); v.OffsetLeft = 12; v.OffsetRight = -12; v.OffsetTop = 5; v.OffsetBottom = -5;
        b.AddChild(v);
        b.Pressed += OnContinue;
        return b;
    }

    void OnContinue()
    {
        if (C.Phase == Phase.Finished) return;
        var g = C.UserGameToday();
        if (g == null)
        {
            C.SimToUserGame();
            Main.I.Save();
            g = C.UserGameToday();
        }
        if (g != null) Main.I.ShowMatch(g);
        else Render();
    }

    // ------------------------------------------------------------------ меню и вкладки
    void BuildSide()
    {
        Ui.Clear(_sideBox);
        foreach (var (key, icon, name) in Menu)
        {
            bool on = _cur.Page == key && (key != "club" || _cur.Arg == null || _cur.Arg == C.UserTeam);
            var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), Flat = true, MouseDefaultCursorShape = CursorShape.PointingHand };
            var st = Ui.Box(on ? new Color(1, 1, 1, 0.07f) : new Color(0, 0, 0, 0), 4, null, 10);
            if (on) { st.BorderColor = Ui.AccentBright; st.BorderWidthLeft = 3; }
            b.AddThemeStyleboxOverride("normal", st);
            b.AddThemeStyleboxOverride("hover", Ui.Box(new Color(1, 1, 1, 0.05f), 4, null, 10));
            b.AddThemeStyleboxOverride("pressed", st);
            var h = Ui.H(12, Ui.Icon(icon, 18, on ? Ui.Text : Ui.Muted), Ui.Expand(Ui.L(name, 14, on ? Ui.Text : Ui.Muted, on ? Ui.Semi : Ui.Medium)));
            if (key == "inbox" && C.Unread > 0)
                h.AddChild(Ui.Card(Ui.L(C.Unread.ToString(), 11, Colors.White, Ui.Bold), new Color("D9534F"), 6));
            int injN = key == "medical" ? C.Injured.Count(id => C.Data.PlayerById[id].Team == C.UserTeam) : 0;
            if (injN > 0) h.AddChild(Ui.Card(Ui.L(injN.ToString(), 11, Colors.White, Ui.Bold), new Color("7A3B3B"), 6));
            h.MouseFilter = MouseFilterEnum.Ignore;
            foreach (var ch in h.GetChildren()) if (ch is Control cc) cc.MouseFilter = MouseFilterEnum.Ignore;
            h.SetAnchorsPreset(LayoutPreset.FullRect); h.OffsetLeft = 12; h.OffsetRight = -8;
            b.AddChild(h);
            b.Pressed += () => Go(key, null, key == "club" ? C.UserTeam : null);
            _sideBox.AddChild(b);
        }
        _sideBox.AddChild(Ui.Spacer());
        _sideBox.AddChild(Ui.L($"HM27 {Main.Version} · данные на {Ui.DateShort(DateOnly.Parse(C.Data.DataDate))}", 11, Ui.Dim));
        _sideBox.AddChild(Ui.L("Пробел — продолжить · Alt+← назад", 11, Ui.Dim));
        var menu = Ui.B("Главное меню", () => { Main.I.Save(); Main.I.ShowMenu(); });
        menu.AddThemeFontSizeOverride("font_size", 13);
        _sideBox.AddChild(menu);
    }

    void BuildSubs()
    {
        Ui.Clear(_subBar);
        var sb = new StyleBoxFlat { BgColor = new Color("20242B"), BorderColor = Ui.Line, BorderWidthBottom = 1 };
        sb.ContentMarginLeft = 18; sb.ContentMarginRight = 18;
        _subBar.AddThemeStyleboxOverride("panel", sb);
        _subBar.Visible = Subs.ContainsKey(_cur.Page);
        if (!_subBar.Visible) return;
        var h = Ui.H(4);
        _subBar.AddChild(h);
        foreach (var (key, name) in Subs[_cur.Page])
        {
            bool on = _cur.Sub == key;
            var b = new Button { Text = name, FocusMode = FocusModeEnum.None, Flat = true, CustomMinimumSize = new Vector2(0, 40), MouseDefaultCursorShape = CursorShape.PointingHand };
            var st = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = on ? Ui.AccentBright : new Color(0, 0, 0, 0), BorderWidthBottom = 3 };
            st.ContentMarginLeft = st.ContentMarginRight = 12;
            b.AddThemeStyleboxOverride("normal", st); b.AddThemeStyleboxOverride("hover", st); b.AddThemeStyleboxOverride("pressed", st);
            b.AddThemeColorOverride("font_color", on ? Ui.Text : Ui.Muted);
            b.AddThemeColorOverride("font_hover_color", Ui.Text);
            b.AddThemeFontSizeOverride("font_size", 14);
            b.Pressed += () => Go(_cur.Page, key, _cur.Arg);
            h.AddChild(b);
        }
    }
}
