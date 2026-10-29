using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Оболочка карьеры: верхняя полоса с разделами (выпадающие списки страниц), поиском, датой и «Продолжить»;
/// под ней — полоса клуба в его цветах с заголовком страницы; вкладки раздела; страница. История переходов — как в браузере.
/// </summary>
public partial class Shell : Control
{
    public static Shell I { get; private set; }
    Career C => Main.I.Career;

    public readonly record struct Nav(string Page, string Sub, string Arg);
    readonly Stack<Nav> _back = new(), _fwd = new();
    Nav _cur;
    Control _top, _band, _subBar;
    ColorRect _stripe;
    MarginContainer _page;

    /// <summary>Страница раздела: куда ведёт (page/sub) или действие; «скоро» — появится на следующих этапах.</summary>
    sealed record Item(string Name, string Icon, string Page, string Sub, string Hint, Action Act = null, bool Soon = false);
    sealed record Section(string Key, string Name, Item[] Items);

    Section[] _sections;

    Section[] Sections() => _sections ??= new[]
    {
        new Section("portal", "Портал", new[]
        {
            new Item("Обзор", "house", "home", null, "Главное за день: следующий матч, таблица, форма"),
            new Item("Входящие", "inbox", "inbox", null, "Письма, новости и отчёты"),
        }),
        new Section("squad", "Состав", new[]
        {
            new Item("Игроки", "users", "squad", "players", "Весь состав: форма, свежесть, способности"),
            new Item("Звенья", "list-ordered", "lines", null, "Тройки, пары, спецбригады, вратарь"),
            new Item("Статистика", "chart-column", "squad", "stats", "Очки, броски, время на льду"),
            new Item("Лазарет", "cross", "medical", null, "Травмы и сроки возвращения"),
            new Item("Тактика", "swords", null, null, "Этап «Тренер»", Soon: true),
            new Item("Тренировки", "dumbbell", null, null, "Этап «Тренер»", Soon: true),
        }),
        new Section("scout", "Скаутинг", new[]
        {
            new Item("Поиск игроков", "search", null, null, "Этап «Ширина»", Soon: true),
            new Item("Драфт", "graduation-cap", null, null, "Этап «Ширина»", Soon: true),
            new Item("Обмены", "repeat", null, null, "Этап «Ширина»", Soon: true),
        }),
        new Section("match", "Матчи", new[]
        {
            new Item("Календарь", "calendar-days", "schedule", null, "Все матчи клуба, результаты и соперники"),
            new Item("Последний матч", "clock", null, null, "Протокол, статистика и оценки", () => { var g = C.LastGame(C.UserTeam); if (g != null) Main.I.ShowMatch(g); }),
        }),
        new Section("club", "Клуб", new[]
        {
            new Item("Обзор", "landmark", "club", "overview", "Сила, прогноз прессы, лидеры"),
            new Item("Игроки", "users", "club", "players", "Состав клуба"),
            new Item("Статистика", "chart-column", "club", "stats", "Клуб на фоне лиги: xG, реализация, вратари"),
            new Item("Расписание", "calendar-days", "club", "schedule", "Матчи клуба"),
        }),
        new Section("league", "Лига", new[]
        {
            new Item("Таблица", "trophy", "comp", "table", "Дивизионы, конференции, уайлд-кард"),
            new Item("Статистика", "chart-column", "comp", "stats", "Лидеры лиги: бомбардиры, снайперы, вратари"),
            new Item("Клубы", "shield", "comp", "teams", "Продвинутая статистика всех 32 клубов"),
            new Item("Плей-офф", "git-fork", "comp", "playoffs", "Сетка Кубка Стэнли"),
        }),
        new Section("career", "Карьера", new[]
        {
            new Item("Сохранить", "save", null, null, "Карьера сохраняется и сама после каждого дня", () => Main.I.Save()),
            new Item("Главное меню", "log-out", null, null, "Сохранить и выйти в меню", () => { Main.I.Save(); Main.I.ShowMenu(); }),
        }),
    };

    string SectionOf(Nav n) => n.Page switch
    {
        "home" or "inbox" => "portal",
        "squad" or "lines" or "medical" => "squad",
        "schedule" => "match",
        "club" => n.Arg == null || n.Arg == C.UserTeam ? "club" : null,
        "comp" => "league",
        "player" => C.Data.PlayerById[int.Parse(n.Arg)].Team == C.UserTeam ? "squad" : null,
        _ => null,
    };

    static readonly Dictionary<string, (string key, string name)[]> Subs = new()
    {
        ["squad"] = new[] { ("players", "Игроки"), ("stats", "Статистика") },
        ["comp"] = new[] { ("table", "Таблица"), ("stats", "Статистика лиги"), ("teams", "Клубы"), ("playoffs", "Плей-офф") },
        ["club"] = new[] { ("overview", "Обзор"), ("players", "Игроки"), ("stats", "Статистика"), ("schedule", "Расписание") },
        ["player"] = new[] { ("overview", "Обзор"), ("attrs", "Характеристики"), ("stats", "Статистика"), ("career", "Карьера") },
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
        _top = new PanelContainer();
        root.AddChild(_top);
        _band = new PanelContainer();
        root.AddChild(_band);
        _stripe = new ColorRect { CustomMinimumSize = new Vector2(0, 3), MouseFilter = MouseFilterEnum.Ignore };
        root.AddChild(_stripe);
        _subBar = new PanelContainer();
        root.AddChild(_subBar);
        _page = Ui.Expand(new MarginContainer(), true, true);
        root.AddChild(_page);
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
    public void RefreshSide() => BuildTop();

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
        BuildTop();
        BuildBand();
        BuildSubs();
        Ui.Clear(_page);
        Control view = _cur.Page switch
        {
            "inbox" => new InboxPage(_cur.Arg),
            "squad" => _cur.Sub == "stats" ? Embedded("stats-mine") : new SquadPage(C.UserTeam),
            "lines" => Ui.Margin(new LinesView(), 16, 12, 16, 12).Also(m => { m.SizeFlagsHorizontal = m.SizeFlagsVertical = SizeFlags.ExpandFill; }),
            "schedule" => Embedded("calendar"),
            "comp" => _cur.Sub == "teams" ? new LeagueTeamsPage() : Embedded(_cur.Sub == "stats" ? "stats" : _cur.Sub),
            "player" => new PlayerPage(int.Parse(_cur.Arg), _cur.Sub),
            "club" => _cur.Sub == "schedule" ? Embedded("calendar:" + (_cur.Arg ?? C.UserTeam)) : _cur.Sub == "players" ? new SquadPage(_cur.Arg ?? C.UserTeam) : _cur.Sub == "stats" ? new TeamStatsPage(_cur.Arg ?? C.UserTeam) : new ClubPage(_cur.Arg ?? C.UserTeam),
            "medical" => new MedicalPage(),
            _ => new HomePage(),
        };
        _page.AddChild(view);
    }

    static Control Embedded(string tab) => new HubScreen(tab, embedded: true);

    // ------------------------------------------------------------------ верхняя полоса
    void BuildTop()
    {
        var sb = new StyleBoxFlat { BgColor = Ui.Side, BorderColor = Ui.Line, BorderWidthBottom = 1 };
        sb.ContentMarginLeft = 18; sb.ContentMarginRight = 10; sb.ContentMarginTop = 6; sb.ContentMarginBottom = 6;
        _top.AddThemeStyleboxOverride("panel", sb);
        Ui.Clear(_top);
        var h = Ui.H(4);
        _top.AddChild(h);
        var logo = Logo.Make(24);
        logo.MouseFilter = MouseFilterEnum.Stop;
        logo.MouseDefaultCursorShape = CursorShape.PointingHand;
        logo.TooltipText = "Портал";
        logo.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        logo.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Go("home"); };
        h.AddChild(logo);
        h.AddChild(Ui.Spacer(false, 14));
        string active = SectionOf(_cur);
        foreach (var sec in Sections()) h.AddChild(SectionButton(sec, sec.Key == active));
        h.AddChild(Ui.Spacer());
        h.AddChild(SearchBox());
        h.AddChild(Ui.Spacer(false, 10));
        string phase = C.Phase switch { Phase.Regular => "регулярный чемпионат", Phase.Playoffs => "плей-офф", _ => "сезон завершён" };
        var date = Ui.V(-4, Ui.L(Ui.Date(C.Today, false), 19, Ui.Text, Ui.CondBold, HorizontalAlignment.Right),
            Ui.L($"{Ui.DowLong(C.Today)}, {phase}", 11, Ui.Muted, null, HorizontalAlignment.Right));
        date.Alignment = BoxContainer.AlignmentMode.Center;
        h.AddChild(date);
        h.AddChild(Ui.Spacer(false, 10));
        h.AddChild(ContinueButton());
    }

    Control SectionButton(Section sec, bool on)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 44) };
        var st = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = on ? Ui.Blue : new Color(0, 0, 0, 0), BorderWidthBottom = 3 };
        st.ContentMarginLeft = st.ContentMarginRight = 12;
        var hov = (StyleBoxFlat)st.Duplicate(); hov.BgColor = new Color(1, 1, 1, 0.05f);
        b.AddThemeStyleboxOverride("normal", st); b.AddThemeStyleboxOverride("hover", hov); b.AddThemeStyleboxOverride("pressed", hov);
        var h = Ui.H(5, Ui.L(sec.Name, 18, on ? Ui.Text : Ui.Muted, Ui.Cond));
        int badge = sec.Key switch
        {
            "portal" => C.Unread,
            "squad" => C.Injured.Count(id => C.Data.PlayerById[id].Team == C.UserTeam),
            _ => 0,
        };
        if (badge > 0) h.AddChild(Badge(badge, sec.Key == "squad" ? Ui.Red : Ui.Blue));
        h.AddChild(Ui.Icon("chevron-down", 13, on ? Ui.Muted : Ui.Dim));
        h.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var ch in h.GetChildren()) if (ch is Control cc) cc.MouseFilter = MouseFilterEnum.Ignore;
        var m = Ui.Margin(h, 10, 0, 10, 0);
        m.MouseFilter = MouseFilterEnum.Ignore;
        m.SetAnchorsPreset(LayoutPreset.FullRect);
        b.AddChild(m);
        b.CustomMinimumSize = new Vector2(h.GetCombinedMinimumSize().X + 20, 44);
        b.Pressed += () => OpenSection(sec, b);
        return b;
    }

    static Control Badge(int n, Color c)
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var sb = Ui.Box(c, 4, null, 5); sb.ContentMarginTop = sb.ContentMarginBottom = 0;
        sb.SetCornerRadiusAll(8);
        p.AddThemeStyleboxOverride("panel", sb);
        p.AddChild(Ui.L(n.ToString(), 11, Colors.White, Ui.Bold, HorizontalAlignment.Center));
        p.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return p;
    }

    /// <summary>Для снимков экрана: открыть выпадающий список раздела.</summary>
    public void DebugOpen(string key)
    {
        int i = Array.FindIndex(Sections(), x => x.Key == key);
        if (i < 0) return;
        var hb = (HBoxContainer)_top.GetChild(0);
        var btn = hb.GetChildren().OfType<Button>().ElementAtOrDefault(i);
        if (btn != null) OpenSection(Sections()[i], btn);
    }

    void OpenSection(Section sec, Control anchor)
    {
        var pop = new PopupPanel();
        var sb = Ui.Box(Ui.Side, 3, Ui.Line, 6);
        sb.BorderColor = Ui.Line; sb.BorderWidthTop = 2; sb.BorderColor = Ui.Blue;
        sb.BorderWidthLeft = sb.BorderWidthRight = sb.BorderWidthBottom = 0;
        pop.AddThemeStyleboxOverride("panel", sb);
        var v = Ui.V(0);
        pop.AddChild(v);
        foreach (var it in sec.Items)
        {
            var row = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(300, 50), Disabled = it.Soon,
                MouseDefaultCursorShape = it.Soon ? CursorShape.Arrow : CursorShape.PointingHand };
            bool cur = it.Page != null && it.Page == _cur.Page && (it.Sub == null || it.Sub == _cur.Sub);
            var nst = Ui.Box(cur ? new Color(Ui.Blue, 0.14f) : new Color(0, 0, 0, 0), 3, null, 10);
            row.AddThemeStyleboxOverride("normal", nst);
            row.AddThemeStyleboxOverride("disabled", Ui.Box(new Color(0, 0, 0, 0), 3, null, 10));
            row.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Panel2, 3, null, 10));
            row.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Panel2, 3, null, 10));
            var name = Ui.H(6, Ui.L(it.Name, 14, it.Soon ? Ui.Dim : Ui.Text, Ui.Semi));
            if (it.Soon) name.AddChild(Ui.L("скоро", 11, Ui.Gold.Darkened(0.2f), Ui.Semi));
            if (it.Page == "inbox" && C.Unread > 0) name.AddChild(Badge(C.Unread, Ui.Blue));
            var h = Ui.H(12, Ui.Icon(it.Icon, 18, it.Soon ? Ui.Dim : cur ? Ui.Blue : Ui.Muted), Ui.V(0, name, Ui.L(it.Hint, 11, Ui.Dim)));
            h.MouseFilter = MouseFilterEnum.Ignore;
            SetIgnore(h);
            h.SetAnchorsPreset(LayoutPreset.FullRect); h.OffsetLeft = 12; h.OffsetRight = -10;
            row.AddChild(h);
            var item = it;
            row.Pressed += () =>
            {
                pop.Hide();
                if (item.Act != null) item.Act();
                else Go(item.Page, item.Sub, item.Page == "club" ? C.UserTeam : null);
            };
            v.AddChild(row);
        }
        AddChild(pop);
        pop.PopupHide += () => pop.QueueFree();
        var gp = anchor.GetScreenPosition();
        pop.Popup(new Rect2I((int)gp.X, (int)(gp.Y + anchor.Size.Y + 2), 320, 10));
    }

    static void SetIgnore(Control c)
    {
        foreach (var ch in c.GetChildren()) if (ch is Control cc) { cc.MouseFilter = MouseFilterEnum.Ignore; SetIgnore(cc); }
    }

    // ------------------------------------------------------------------ полоса клуба
    void BuildBand()
    {
        var t = C.Team(_cur.Page == "club" ? (_cur.Arg ?? C.UserTeam) : _cur.Page == "player" ? C.Data.PlayerById[int.Parse(_cur.Arg)].Team : C.UserTeam);
        var c1 = new Color(t.Colors[0]);
        var c2 = new Color(t.Colors.Count > 1 ? t.Colors[1] : t.Colors[0]);
        if (c1.Luminance < 0.04f) c1 = c1.Lightened(0.10f);
        // цвет клуба уходит в лёд вправо: шапка остаётся клубной, но не спорит с данными
        var grad = new Gradient();
        grad.SetColor(0, c1.Lerp(Ui.Bg, 0.12f)); grad.SetColor(1, c1.Lerp(Ui.Bg, 0.82f));
        grad.SetOffset(0, 0.0f); grad.SetOffset(1, 0.78f);
        var tex = new StyleBoxTexture { Texture = new GradientTexture2D { Gradient = grad, Width = 256, Height = 4 } };
        tex.ContentMarginLeft = 12; tex.ContentMarginRight = 18; tex.ContentMarginTop = 8; tex.ContentMarginBottom = 8;
        _band.AddThemeStyleboxOverride("panel", tex);
        Ui.Clear(_band);
        var fg = Ui.ReadableOn(c1.Lerp(Ui.Bg, 0.12f));
        var v = Ui.V(0);
        _band.AddChild(v);
        var h = Ui.H(10);
        v.AddChild(h);
        h.AddChild(IconButton("chevron-left", Back, fg, _back.Count > 0));
        h.AddChild(IconButton("chevron-right", Fwd, fg, _fwd.Count > 0));
        h.AddChild(Ui.Spacer(false, 4));
        h.AddChild(new TeamBadge(t, 50));
        var (title, sub) = Titles(t);
        var tv = Ui.V(-2, Ui.L(title, 30, fg, Ui.CondBold), Ui.L(sub, 13, new Color(fg, 0.78f)));
        tv.Alignment = BoxContainer.AlignmentMode.Center;
        h.AddChild(Ui.Expand(tv));
        // вторая краска клуба — тонкая полоса снизу, как кант на свитере
        _stripe.Color = c2;
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
            "comp" => ("Национальная хоккейная лига", "Сезон 2026-27"),
            "player" => (t.Name, rec),
            "club" => (t.Name, rec),
            "medical" => ("Лазарет", rec),
            _ => (t.Name, rec),
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

    Control SearchBox()
    {
        var box = new LineEdit { PlaceholderText = "Игрок или клуб", CustomMinimumSize = new Vector2(210, 34), ClearButtonEnabled = true };
        box.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var sb = Ui.Box(Ui.Panel, 3, Ui.Line, 10);
        sb.ContentMarginLeft = 32;
        box.AddThemeStyleboxOverride("normal", sb);
        box.AddThemeStyleboxOverride("focus", Ui.Box(Ui.Panel, 3, Ui.Blue, 10).Also(s => s.ContentMarginLeft = 32));
        box.AddThemeColorOverride("font_placeholder_color", Ui.Dim);
        box.AddChild(Ui.Icon("search", 16, Ui.Muted).Also(i => i.Position = new Vector2(10, 9)));
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
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(196, 46), Disabled = !enabled, MouseDefaultCursorShape = CursorShape.PointingHand };
        // день матча — янтарь табло, обычный день — синяя линия
        var bg = g != null ? Ui.Gold : Ui.Blue;
        var fg = Ui.ReadableOn(bg);
        b.AddThemeStyleboxOverride("normal", Ui.Box(bg, 3, null, 12));
        b.AddThemeStyleboxOverride("hover", Ui.Box(bg.Lightened(0.1f), 3, null, 12));
        b.AddThemeStyleboxOverride("pressed", Ui.Box(bg.Darkened(0.15f), 3, null, 12));
        b.AddThemeStyleboxOverride("disabled", Ui.Box(Ui.Panel, 3, Ui.Line, 12));
        if (!enabled) fg = Ui.Muted;
        var v = Ui.V(-4, Ui.H(6, Ui.L(label, 19, fg, Ui.CondBold), Ui.Spacer(), Ui.Icon(enabled ? "chevrons-right" : "trophy", 18, fg)), Ui.L(sub, 11, new Color(fg, 0.8f)));
        v.Alignment = BoxContainer.AlignmentMode.Center;
        v.MouseFilter = MouseFilterEnum.Ignore;
        SetIgnore(v);
        v.SetAnchorsPreset(LayoutPreset.FullRect); v.OffsetLeft = 12; v.OffsetRight = -10; v.OffsetTop = 3; v.OffsetBottom = -3;
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

    // ------------------------------------------------------------------ вкладки раздела
    void BuildSubs()
    {
        Ui.Clear(_subBar);
        var sb = new StyleBoxFlat { BgColor = new Color(Ui.Side, 0.6f), BorderColor = Ui.Line, BorderWidthBottom = 1 };
        sb.ContentMarginLeft = 18; sb.ContentMarginRight = 18;
        _subBar.AddThemeStyleboxOverride("panel", sb);
        // вкладки: страницы раздела (как в его выпадающем списке); у чужого клуба — его страницы
        var tabs = new List<(string name, string page, string sub, string arg)>();
        string sec = SectionOf(_cur);
        if (_cur.Page is "club" or "player") tabs.AddRange(Subs[_cur.Page].Select(x => (x.name, _cur.Page, x.key, _cur.Arg)));
        else if (sec != null && _cur.Page != "player")
            tabs.AddRange(Sections().First(x => x.Key == sec).Items.Where(it => it.Page != null && !it.Soon).Select(it => (it.Name, it.Page, it.Sub, (string)null)));
        _subBar.Visible = tabs.Count > 1;
        if (!_subBar.Visible) return;
        var h = Ui.H(4);
        _subBar.AddChild(h);
        foreach (var (name, page, key, arg) in tabs)
        {
            bool on = _cur.Page == page && (key == null || _cur.Sub == key);
            var b = new Button { Text = name, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), MouseDefaultCursorShape = CursorShape.PointingHand };
            var st = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = on ? Ui.Blue : new Color(0, 0, 0, 0), BorderWidthBottom = 2 };
            st.ContentMarginLeft = st.ContentMarginRight = 12;
            b.AddThemeStyleboxOverride("normal", st); b.AddThemeStyleboxOverride("hover", st); b.AddThemeStyleboxOverride("pressed", st);
            b.AddThemeColorOverride("font_color", on ? Ui.Text : Ui.Muted);
            b.AddThemeColorOverride("font_hover_color", Ui.Text);
            b.AddThemeFontSizeOverride("font_size", 13);
            b.AddThemeFontOverride("font", on ? Ui.Semi : Ui.Medium);
            b.Pressed += () => Go(page, key, arg);
            h.AddChild(b);
        }
    }
}
