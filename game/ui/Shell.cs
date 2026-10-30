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
            new Item("Обзор", "house", "home", null, "Главное за день: решения, матч, новости"),
            new Item("Входящие", "inbox", "inbox", null, "Письма, решения и новости лиги"),
        }),
        new Section("team", "Команда", new[]
        {
            new Item("Игроки", "users", "squad", "players", "Состав: форма, свежесть, настрой, способности"),
            new Item("Звенья", "list-ordered", "lines", null, "Тройки, пары, спецбригады, вратари"),
            new Item("Раздевалка", "heart-pulse", "room", null, "Настрой игроков и команды: кто доволен, кто нет"),
            new Item("Статистика", "chart-column", "squad", "stats", "Очки, броски, время на льду"),
            new Item("Лазарет", "cross", "medical", null, "Травмы и сроки возвращения"),
            new Item("Тактика", "swords", "tactics", "system", "Система игры: девять рычагов, готовые стили"),
            new Item("Что сработало", "notebook-pen", "tactics", "journal", "Журнал: что схемы дали в наших матчах"),
            new Item("Правила матча", "clipboard-list", "tactics", "rules", "Что помощник меняет в перерывах"),
        }),
        new Section("training", "Тренировки", new[]
        {
            new Item("Неделя", "dumbbell", "training", "week", "Нагрузка и упор недели"),
            new Item("Индивидуальные", "target", "training", "individual", "Одна характеристика — вдвое быстрее"),
            new Item("Развитие молодых", "trending-up", "training", "youth", "Что помогает и что мешает расти"),
        }),
        new Section("calendar", "Календарь", new[]
        {
            new Item("Календарь", "calendar-days", "schedule", null, "Матчи клуба, результаты, события сезона"),
            new Item("Последний матч", "clock", null, null, "Протокол, карта бросков и оценки", () => { var g = C.LastGame(C.UserTeam); if (g != null) Main.I.ShowMatch(g); }),
        }),
        new Section("deals", "Сделки", new[]
        {
            new Item("Обмены", "repeat", null, null, "Этап «Менеджер»", Soon: true),
            new Item("Свободные агенты", "user", null, null, "Этап «Менеджер»", Soon: true),
            new Item("Контракты", "newspaper", null, null, "Этап «Менеджер»: продления и потолок зарплат", Soon: true),
            new Item("Драфт отказов", "log-out", null, null, "Этап «Менеджер»", Soon: true),
        }),
        new Section("scout", "Скаутинг", new[]
        {
            new Item("Поиск игроков", "search", null, null, "Этап «Менеджер»", Soon: true),
            new Item("Проспекты", "graduation-cap", null, null, "Этап «Менеджер»", Soon: true),
            new Item("Драфт", "trophy", null, null, "Этап «Менеджер»", Soon: true),
        }),
        new Section("club", "Клуб", new[]
        {
            new Item("Обзор", "landmark", "club", "overview", "Цели, сила, прогноз прессы, история"),
            new Item("Статистика", "chart-column", "club", "stats", "Клуб на фоне лиги: xG, реализация, вратари"),
            new Item("Персонал", "users", null, null, "Этап «Тренер»: ассистенты, тренер вратарей, скауты, врачи", Soon: true),
        }),
        new Section("league", "Лига", new[]
        {
            new Item("Таблица", "trophy", "comp", "table", "Дивизионы, конференции, уайлд-кард"),
            new Item("Статистика", "chart-column", "comp", "stats", "Лидеры лиги: бомбардиры, снайперы, вратари"),
            new Item("Клубы", "shield", "comp", "teams", "Продвинутая статистика всех 32 клубов"),
            new Item("Плей-офф", "git-fork", "comp", "playoffs", "Сетка Кубка Стэнли"),
        }),
    };

    string SectionOf(Nav n) => n.Page switch
    {
        "home" or "inbox" => "portal",
        "squad" or "lines" or "medical" or "room" or "tactics" => "team",
        "training" => "training",
        "schedule" => "calendar",
        "club" => n.Arg == null || n.Arg == C.UserTeam ? "club" : null,
        "comp" => "league",
        "player" => C.Data.PlayerById[int.Parse(n.Arg)].Team == C.UserTeam ? "team" : null,
        _ => null,
    };

    static readonly Dictionary<string, (string key, string name)[]> Subs = new()
    {
        ["squad"] = new[] { ("players", "Игроки"), ("stats", "Статистика") },
        ["comp"] = new[] { ("table", "Таблица"), ("stats", "Статистика лиги"), ("teams", "Клубы"), ("playoffs", "Плей-офф") },
        ["club"] = new[] { ("overview", "Обзор"), ("players", "Игроки"), ("stats", "Статистика"), ("schedule", "Расписание") },
        ["player"] = new[] { ("overview", "Обзор"), ("attrs", "Характеристики"), ("stats", "Статистика"), ("career", "Карьера") },
        ["tactics"] = new[] { ("system", "Система"), ("journal", "Что сработало"), ("rules", "Правила матча") },
    };

    public Shell(string page, string sub = null, string arg = null) { _cur = new Nav(page, sub, arg); }
    public Shell() { _cur = new Nav("home", null, null); }

    IceBg _bg;
    string _open;   // раскрытый раздел в боковой панели (по умолчанию — раздел текущей страницы)

    public override void _Ready()
    {
        I = this;
        _bg = new IceBg(Ui.Club);
        AddChild(_bg);
        var root = new HBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);
        _top = new PanelContainer { CustomMinimumSize = new Vector2(236, 0) };
        root.AddChild(_top);
        var main = Ui.Expand(new VBoxContainer(), true, true);
        main.AddThemeConstantOverride("separation", 0);
        root.AddChild(main);
        _band = new PanelContainer();
        main.AddChild(_band);
        _stripe = new ColorRect { CustomMinimumSize = new Vector2(0, 0), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        main.AddChild(_stripe);
        _subBar = new PanelContainer();
        main.AddChild(_subBar);
        _page = Ui.Expand(new MarginContainer(), true, true);
        main.AddChild(_page);
        Render();
    }

    public override void _ExitTree() { if (I == this) I = null; }

    // ------------------------------------------------------------------ навигация
    public void Go(string page, string sub = null, string arg = null)
    {
        var n = new Nav(page, sub ?? DefaultSub(page), arg);
        if (n == _cur) return;
        _back.Push(_cur); _fwd.Clear();
        _cur = n; _open = null;
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
            "lines" => Ui.Margin(new LinesView(), 24, 8, 24, 16).Also(m => { m.SizeFlagsHorizontal = m.SizeFlagsVertical = SizeFlags.ExpandFill; }),
            "schedule" => Embedded("calendar"),
            "comp" => _cur.Sub == "teams" ? new LeagueTeamsPage() : Embedded(_cur.Sub == "stats" ? "stats" : _cur.Sub),
            "player" => new PlayerPage(int.Parse(_cur.Arg), _cur.Sub),
            "club" => _cur.Sub == "schedule" ? Embedded("calendar:" + (_cur.Arg ?? C.UserTeam)) : _cur.Sub == "players" ? new SquadPage(_cur.Arg ?? C.UserTeam) : _cur.Sub == "stats" ? new TeamStatsPage(_cur.Arg ?? C.UserTeam) : new ClubPage(_cur.Arg ?? C.UserTeam),
            "medical" => new MedicalPage(),
            "room" => new RoomPage(),
            "tactics" => _cur.Sub == "rules" ? new RulesPage() : _cur.Sub == "journal" ? new JournalPage(_cur.Arg) : new TacticsPage(_cur.Arg),
            "training" => new TrainingPage(_cur.Sub),
            _ => new HomePage(),
        };
        _page.AddChild(view);
    }

    static Control Embedded(string tab) => new HubScreen(tab, embedded: true);

    // ------------------------------------------------------------------ боковая панель
    /// <summary>Боковая панель: клуб, разделы (раскрытый — со страницами), дата и «Продолжить».</summary>
    void BuildTop()
    {
        var sb = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.3f), BorderColor = new Color(1, 1, 1, 0.06f), BorderWidthRight = 1 };
        sb.ContentMarginLeft = 12; sb.ContentMarginRight = 12; sb.ContentMarginTop = 18; sb.ContentMarginBottom = 18;
        _top.AddThemeStyleboxOverride("panel", sb);
        Ui.Clear(_top);
        var v = Ui.V(2);
        _top.AddChild(v);
        // свой клуб: эмблема со свечением, название, место
        var my = C.Team(C.UserTeam);
        var row = C.Table[my.Abbr];
        int dplace = C.Sorted(x => x.Division == my.Division).FindIndex(r => r.Team == my.Abbr) + 1;
        var club = new Paint(new Vector2(212, 70), q =>
        {
            Face.Glow(q, new Vector2(30, 32), 34, Ds.ClubBright(my), 0.03f);
            Face.Logo(q, my, new Rect2(8, 10, 44, 44));
            q.Text(Ui.Semi, 15, 64, 12, Paint.Trim(Ui.Semi, my.Name, 15, 146), Ui.Text);
            q.Text(Ui.Regular, 12, 64, 34, row.Gp == 0 ? "сезон 2026-27" : $"{row.W}-{row.L}-{row.Otl}  ·  {dplace}-е в дивизионе", Ui.Muted);
        }) { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = "Клуб: цели, сила, история" };
        club.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Go("club", null, C.UserTeam); };
        v.AddChild(club);
        v.AddChild(Ui.Spacer(false, 10));
        string active = _open ?? SectionOf(_cur);
        foreach (var sec in Sections())
        {
            bool open = sec.Key == active;
            v.AddChild(SectionButton(sec, open));
            if (!open) continue;
            foreach (var it in sec.Items) v.AddChild(SideItem(it));
            v.AddChild(Ui.Spacer(false, 6));
        }
        v.AddChild(Ui.Spacer());
        // поиск и настройки
        v.AddChild(Ui.H(4, TopIcon("search", "Поиск игрока или клуба", b => OpenSearch(b)), TopIcon("settings", "Сохранить, настройки, главное меню", b => OpenSettings(b)), Ui.Spacer(),
            Logo.Make(18).Also(l => { l.SizeFlagsVertical = SizeFlags.ShrinkCenter; l.Modulate = new Color(1, 1, 1, 0.45f); })));
        v.AddChild(Ui.Spacer(false, 10));
        string phase = C.Phase switch { Phase.Regular => "регулярный чемпионат", Phase.Playoffs => "плей-офф", _ => "сезон завершён" };
        v.AddChild(Ui.Margin(Ui.V(0, Ui.L(Ui.Date(C.Today, false), 16, Ui.Text, Ui.Semi), Ui.L($"{Ui.DowLong(C.Today)}, {phase}", 12, Ui.Muted)), 8, 0, 0, 10));
        v.AddChild(Ui.H(8, Ui.Expand(ContinueButton()), ToMatchButton()));
    }

    Control SideItem(Item it)
    {
        bool cur = it.Page != null && it.Page == _cur.Page && (it.Sub == null || it.Sub == _cur.Sub) && (it.Page != "club" || _cur.Arg == null || _cur.Arg == C.UserTeam);
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 32), Disabled = it.Soon, TooltipText = it.Hint,
            MouseDefaultCursorShape = it.Soon ? CursorShape.Arrow : CursorShape.PointingHand };
        var e = new StyleBoxEmpty();
        var hov = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.04f) }; hov.SetCornerRadiusAll(8);
        b.AddThemeStyleboxOverride("normal", e); b.AddThemeStyleboxOverride("disabled", e); b.AddThemeStyleboxOverride("hover", hov); b.AddThemeStyleboxOverride("pressed", hov);
        var h = Ui.H(8, Ui.L(it.Name, 14, it.Soon ? new Color(Ui.Dim, 0.8f) : cur ? Ui.Text : Ui.Muted, cur ? Ui.Semi : Ui.Regular));
        if (it.Soon) h.AddChild(Ui.L("скоро", 10, new Color(Ui.Gold, 0.55f), Ui.Semi));
        if (it.Page == "inbox" && C.Unread > 0) h.AddChild(Badge(C.Unread, Ui.Blue));
        h.SetAnchorsPreset(LayoutPreset.FullRect); h.OffsetLeft = 46;
        SetIgnore(h); h.MouseFilter = MouseFilterEnum.Ignore;
        b.AddChild(h);
        if (cur) b.AddChild(new Paint(new Vector2(0, 0), q => q.DrawLine(new Vector2(33, 8), new Vector2(33, 24), Ds.ClubBright(C.Team(C.UserTeam)).Lightened(0.15f), 2)).Also(x => x.SetAnchorsPreset(LayoutPreset.FullRect)));
        var item = it;
        b.Pressed += () => { if (item.Act != null) item.Act(); else Go(item.Page, item.Sub, item.Page == "club" ? C.UserTeam : null); };
        return b;
    }

    static Button TopIcon(string icon, string tip, Action<Button> onPress)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(38, 38), TooltipText = tip, MouseDefaultCursorShape = CursorShape.PointingHand, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var e = Ui.Box(new Color(0, 0, 0, 0), 10, null, 0);
        b.AddThemeStyleboxOverride("normal", e);
        b.AddThemeStyleboxOverride("hover", Ui.Box(new Color(1, 1, 1, 0.06f), 10, null, 0));
        b.AddThemeStyleboxOverride("pressed", Ui.Box(new Color(1, 1, 1, 0.08f), 10, null, 0));
        var ic = Ui.Icon(icon, 19, Ui.Muted);
        ic.SetAnchorsPreset(LayoutPreset.FullRect);
        ic.OffsetLeft = ic.OffsetTop = 9; ic.OffsetRight = ic.OffsetBottom = -9;
        b.AddChild(ic);
        b.Pressed += () => onPress(b);
        return b;
    }

    void OpenSearch(Control anchor)
    {
        var pop = new PopupPanel();
        pop.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Side, 3, Ui.Blue, 8));
        var box = SearchBox();
        pop.AddChild(box);
        AddChild(pop);
        pop.PopupHide += () => pop.QueueFree();
        var gp = anchor.GetScreenPosition();
        pop.Popup(new Rect2I((int)gp.X, (int)(gp.Y - 64), 300, 10));
        box.CallDeferred(Control.MethodName.GrabFocus);
    }

    void OpenSettings(Control anchor)
    {
        var pop = new PopupPanel();
        pop.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Side, 3, Ui.Line, 6));
        var v = Ui.V(0);
        pop.AddChild(v);
        void Row(string icon, string text, string hint, Action a)
        {
            var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(300, 48), MouseDefaultCursorShape = CursorShape.PointingHand };
            b.AddThemeStyleboxOverride("normal", Ui.Box(new Color(0, 0, 0, 0), 3, null, 10));
            b.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Panel2, 10, null, 10));
            b.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Panel2, 10, null, 10));
            var hh = Ui.H(12, Ui.Icon(icon, 18, Ui.Muted), Ui.V(0, Ui.L(text, 14, Ui.Text, Ui.Semi), Ui.L(hint, 11, Ui.Dim)));
            hh.MouseFilter = MouseFilterEnum.Ignore; SetIgnore(hh);
            hh.SetAnchorsPreset(LayoutPreset.FullRect); hh.OffsetLeft = 12;
            b.AddChild(hh);
            b.Pressed += () => { pop.Hide(); a(); };
            v.AddChild(b);
        }
        Row("save", "Сохранить", "Карьера сохраняется и сама после каждого дня", () => Main.I.Save());
        Row("user", Names.English ? "Имена: English" : "Имена: по-русски", "Как писать игроков и клубы", () => { Names.English = !Names.English; Main.SaveSettings(); Render(); });
        Row("star", StarScale.Relative ? "Звёзды: относительно клуба" : "Звёзды: по шкале лиги", "Шкала способностей и потенциала", () => { StarScale.Relative = !StarScale.Relative; Main.SaveSettings(); Render(); });
        Row("log-out", "Главное меню", "Сохранить и выйти в меню", () => { Main.I.Save(); Main.I.ShowMenu(); });
        AddChild(pop);
        pop.PopupHide += () => pop.QueueFree();
        var gp = anchor.GetScreenPosition();
        pop.Popup(new Rect2I((int)gp.X, (int)(gp.Y - 210), 310, 10));
    }

    Control SectionButton(Section sec, bool on)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 42) };
        var st = new StyleBoxFlat { BgColor = on ? new Color(1, 1, 1, 0.07f) : new Color(0, 0, 0, 0) }; st.SetCornerRadiusAll(12);
        var hov = new StyleBoxFlat { BgColor = new Color(1, 1, 1, on ? 0.09f : 0.04f) }; hov.SetCornerRadiusAll(12);
        b.AddThemeStyleboxOverride("normal", st); b.AddThemeStyleboxOverride("hover", hov); b.AddThemeStyleboxOverride("pressed", hov);
        var icon = sec.Items.FirstOrDefault()?.Icon ?? "house";
        icon = sec.Key switch { "portal" => "house", "team" => "users", "training" => "dumbbell", "calendar" => "calendar-days", "deals" => "handshake", "scout" => "binoculars", "club" => "landmark", "league" => "trophy", _ => icon };
        var h = Ui.H(12, Ui.Icon(icon, 18, on ? Ui.Text : Ui.Muted), Ui.Expand(Ui.L(sec.Name, 15, on ? Ui.Text : Ui.Muted, on ? Ui.Semi : Ui.Regular)));
        int badge = sec.Key switch
        {
            "portal" => C.Unread,
            "team" => C.Injured.Count(id => C.Data.PlayerById[id].Team == C.UserTeam),
            _ => 0,
        };
        if (badge > 0) h.AddChild(Badge(badge, sec.Key == "team" ? Ui.Red : Ui.Blue));
        h.SetAnchorsPreset(LayoutPreset.FullRect); h.OffsetLeft = 14; h.OffsetRight = -10;
        SetIgnore(h); h.MouseFilter = MouseFilterEnum.Ignore;
        b.AddChild(h);
        b.Pressed += () =>
        {
            // нажатие на раздел: открыть его первую страницу (у разделов «скоро» — только раскрыть список)
            var first = sec.Items.FirstOrDefault(x => x.Page != null && !x.Soon);
            if (first != null) { _open = null; Go(first.Page, first.Sub, first.Page == "club" ? C.UserTeam : null); }
            else { _open = _open == sec.Key ? null : sec.Key; BuildTop(); }
        };
        return b;
    }

    /// <summary>Лента ближайших дней: матчи (дома/в гостях, соперник), второй матч за два дня, сегодня.</summary>
    Control DayStrip()
    {
        var h = Ui.H(4);
        h.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        string[] dow = { "вс", "пн", "вт", "ср", "чт", "пт", "сб" };
        for (int i = 0; i < 7; i++)
        {
            var d = C.Today.AddDays(i);
            var g = C.TeamGames(C.UserTeam).FirstOrDefault(x => x.Date == d);
            bool b2b = g != null && C.TeamGames(C.UserTeam).Any(x => x.Date == d.AddDays(-1));
            var p = new PanelContainer { CustomMinimumSize = new Vector2(54, 58), MouseFilter = MouseFilterEnum.Stop };
            var sb = Ui.Box(g != null ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0.02f), 12, i == 0 ? new Color(Ui.Gold, 0.8f) : g != null ? Ds.Edge : null, 3);
            sb.ContentMarginTop = sb.ContentMarginBottom = 4;
            p.AddThemeStyleboxOverride("panel", sb);
            var v = Ui.V(-1);
            v.MouseFilter = MouseFilterEnum.Ignore;
            v.AddChild(Ui.L(i == 0 ? "сегодня" : $"{dow[(int)d.DayOfWeek]} {d.Day}", 10, i == 0 ? Ui.Gold : new Color(1, 1, 1, 0.7f), Ui.Semi, HorizontalAlignment.Center));
            if (g != null)
            {
                bool home = g.Home == C.UserTeam;
                var opp = C.Team(home ? g.Away : g.Home);
                v.AddChild(new TeamBadge(opp, 22) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
                v.AddChild(Ui.L(home ? "дома" : "в гостях", 9, b2b ? Ui.Otl : new Color(1, 1, 1, 0.65f), null, HorizontalAlignment.Center));
                p.TooltipText = $"{Ui.Date(d)}: {(home ? "дома" : "в гостях")} — «{opp.Name}»{(b2b ? "\nВторой матч за два дня" : "")}";
                p.MouseDefaultCursorShape = CursorShape.PointingHand;
                var abbr = opp.Abbr;
                p.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Go("club", null, abbr); };
            }
            else
            {
                v.AddChild(Ui.L("·", 16, new Color(1, 1, 1, 0.35f), null, HorizontalAlignment.Center));
                p.TooltipText = $"{Ui.Date(d)}: без матча";
            }
            foreach (var ch in v.GetChildren()) if (ch is Control cc) cc.MouseFilter = MouseFilterEnum.Ignore;
            p.AddChild(v);
            h.AddChild(p);
        }
        return h;
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

    /// <summary>Для снимков экрана: раскрыть раздел в боковой панели.</summary>
    public void DebugOpen(string key) { _open = key; BuildTop(); }

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
            row.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Panel2, 10, null, 10));
            row.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Panel2, 10, null, 10));
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
        var glow = Ds.ClubBright(t);
        var c2 = new Color(t.Colors.Count > 1 ? t.Colors[1] : t.Colors[0]);
        _bg?.SetGlow(glow, c2.Luminance < 0.1f ? glow : c2);
        var sb = new StyleBoxEmpty { ContentMarginLeft = 22, ContentMarginRight = 24, ContentMarginTop = 18, ContentMarginBottom = 8 };
        _band.AddThemeStyleboxOverride("panel", sb);
        Ui.Clear(_band);
        var h = Ui.H(10);
        _band.AddChild(h);
        h.AddChild(IconButton("chevron-left", Back, Ui.Text, _back.Count > 0));
        h.AddChild(IconButton("chevron-right", Fwd, Ui.Text, _fwd.Count > 0));
        h.AddChild(Ui.Spacer(false, 4));
        h.AddChild(new Paint(new Vector2(58, 58), q =>
        {
            Face.Glow(q, new Vector2(29, 29), 40, glow, 0.035f);
            Face.Logo(q, t, new Rect2(4, 4, 50, 50));
        }));
        var (title, sub) = Titles(t);
        var tv = Ui.V(4, Ui.L(title, 26, Ui.Text, Ds.Display), Ui.L(sub, 13, Ui.Muted));
        tv.Alignment = BoxContainer.AlignmentMode.Center;
        h.AddChild(Ui.Expand(tv));
        h.AddChild(DayStrip());
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
            "room" => ("Раздевалка", rec),
            "tactics" => (_cur.Sub == "rules" ? "Правила матча" : _cur.Sub == "journal" ? "Что сработало" : "Тактика", rec),
            "training" => (_cur.Sub == "individual" ? "Индивидуальная работа" : _cur.Sub == "youth" ? "Развитие молодых" : "Тренировки", rec),
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
        var box = new LineEdit { PlaceholderText = "Игрок или клуб", CustomMinimumSize = new Vector2(280, 36), ClearButtonEnabled = true };
        box.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var sb = Ui.Box(Ui.Panel, 14, Ui.Line, 10);
        sb.ContentMarginLeft = 32;
        box.AddThemeStyleboxOverride("normal", sb);
        box.AddThemeStyleboxOverride("focus", Ui.Box(Ui.Panel, 14, Ui.Blue, 10).Also(s => s.ContentMarginLeft = 32));
        box.AddThemeColorOverride("font_placeholder_color", Ui.Dim);
        box.AddChild(Ui.Icon("search", 16, Ui.Muted).Also(i => i.Position = new Vector2(10, 9)));
        var popup = new PopupPanel();
        popup.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 14, Ui.Line, 6));
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
        b.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Panel2, 10, null, 6));
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
        int need = Decisions.Blocking(C);
        bool block = enabled && need > 0;
        if (!enabled) { label = "Сезон завершён"; sub = C.Champion != null ? $"Кубок: «{C.Team(C.Champion).City}»" : ""; }
        else if (block) { label = "Нужно решение"; sub = need == 1 ? "одно письмо ждёт ответа" : $"{need} письма ждут ответа"; }
        else if (g != null) { label = "Матч дня"; sub = g.Home == C.UserTeam ? $"с «{C.Team(g.Away).City}»" : $"в гостях у «{C.Team(g.Home).City}»"; }
        else
        {
            label = "Продолжить";
            var tomorrow = C.Today.AddDays(1);
            sub = $"к {Ui.DateShort(tomorrow)}{(C.TeamGames(C.UserTeam).Any(x => x.Date == tomorrow) ? " · матч" : "")}";
        }
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 52), Disabled = !enabled, MouseDefaultCursorShape = CursorShape.PointingHand };
        // день матча — янтарь табло, ждёт решения — красный, обычный день — цвет клуба со свечением
        var bg = block ? Ui.Red : g != null ? Ui.Gold : Ds.ClubBright(C.Team(C.UserTeam)).Darkened(0.15f);
        var fg = Ui.ReadableOn(bg);
        StyleBoxFlat St(Color c) { var x = Ui.Box(c, 16, null, 12); x.ShadowColor = new Color(bg, 0.4f); x.ShadowSize = 16; return x; }
        b.AddThemeStyleboxOverride("normal", St(bg));
        b.AddThemeStyleboxOverride("hover", St(bg.Lightened(0.1f)));
        b.AddThemeStyleboxOverride("pressed", St(bg.Darkened(0.15f)));
        b.AddThemeStyleboxOverride("disabled", Ui.Box(Ui.Panel, 16, Ui.Line, 12));
        if (!enabled) fg = Ui.Muted;
        var v = Ui.V(-2, Ui.H(6, Ui.L(label, 15, fg, Ui.Bold), Ui.Spacer(), Ui.Icon(!enabled ? "trophy" : block ? "message-circle" : "chevrons-right", 16, fg)), Ui.L(sub, 11, new Color(fg, 0.8f)));
        v.Alignment = BoxContainer.AlignmentMode.Center;
        v.MouseFilter = MouseFilterEnum.Ignore;
        SetIgnore(v);
        v.SetAnchorsPreset(LayoutPreset.FullRect); v.OffsetLeft = 12; v.OffsetRight = -10; v.OffsetTop = 3; v.OffsetBottom = -3;
        b.AddChild(v);
        b.Pressed += OnContinue;
        return b;
    }

    /// <summary>«Продолжить»: обязательное письмо → к нему; день матча → матч; иначе — следующий день.</summary>
    void OnContinue()
    {
        if (C.Phase == Phase.Finished) return;
        var need = C.Messages.Where(m => m.Required && m.Pending).OrderBy(m => m.Id).FirstOrDefault();
        if (need != null) { Go("inbox", null, need.Id.ToString()); return; }
        var g = C.UserGameToday();
        if (g != null) { Main.I.ShowMatch(g); return; }
        C.SimDay();
        Main.I.Save();
        Render();
    }

    /// <summary>«До матча»: дни без матчей подряд, но с остановкой на обязательном письме.</summary>
    void OnToMatch()
    {
        if (C.Phase == Phase.Finished) return;
        if (Decisions.Blocking(C) > 0 || C.UserGameToday() != null) { OnContinue(); return; }
        C.SimToUserGame(stopOnDecision: true);
        Main.I.Save();
        Render();
    }

    Control ToMatchButton()
    {
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(46, 52), TooltipText = "До следующего матча (остановится, если понадобится ваше решение)",
            MouseDefaultCursorShape = CursorShape.PointingHand, Disabled = C.Phase == Phase.Finished || C.UserGameToday() != null || Decisions.Blocking(C) > 0 };
        var bg = new Color(1, 1, 1, 0.08f);
        b.AddThemeStyleboxOverride("normal", Ui.Box(bg, 16, Ds.Edge, 0));
        b.AddThemeStyleboxOverride("hover", Ui.Box(new Color(1, 1, 1, 0.13f), 16, Ds.Edge, 0));
        b.AddThemeStyleboxOverride("pressed", Ui.Box(new Color(1, 1, 1, 0.16f), 16, Ds.Edge, 0));
        b.AddThemeStyleboxOverride("disabled", Ui.Box(new Color(1, 1, 1, 0.03f), 16, null, 0));
        var ic = Ui.Icon("skip-forward", 18, b.Disabled ? Ui.Dim : Colors.White);
        ic.SetAnchorsPreset(LayoutPreset.FullRect);
        ic.OffsetLeft = 14; ic.OffsetRight = -14; ic.OffsetTop = 17; ic.OffsetBottom = -17;
        b.AddChild(ic);
        b.Pressed += OnToMatch;
        return b;
    }

    // ------------------------------------------------------------------ вкладки раздела
    void BuildSubs()
    {
        Ui.Clear(_subBar);
        _subBar.AddThemeStyleboxOverride("panel", new StyleBoxEmpty { ContentMarginLeft = 26, ContentMarginRight = 24, ContentMarginTop = 4, ContentMarginBottom = 6 });
        // страницы разделов — в боковой панели; здесь — вкладки чужого клуба, игрока и страниц с подразделами
        var tabs = new List<(string name, string page, string sub, string arg)>();
        bool own = _cur.Page == "club" && (_cur.Arg == null || _cur.Arg == C.UserTeam);
        if (_cur.Page is "club" or "player" && !own) tabs.AddRange(Subs[_cur.Page].Select(x => (x.name, _cur.Page, x.key, _cur.Arg)));
        else if (_cur.Page == "player") tabs.AddRange(Subs["player"].Select(x => (x.name, _cur.Page, x.key, _cur.Arg)));
        _subBar.Visible = tabs.Count > 1;
        if (!_subBar.Visible) return;
        var pills = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        pills.AddThemeStyleboxOverride("panel", Ds.Pill(new Color(1, 1, 1, 0.045f), 4, 4, Ds.Edge));
        var h = Ui.H(2);
        pills.AddChild(h);
        _subBar.AddChild(pills);
        foreach (var (name, page, key, arg) in tabs)
        {
            bool on = _cur.Page == page && (key == null || _cur.Sub == key);
            var b = new Button { Text = name, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34), MouseDefaultCursorShape = CursorShape.PointingHand };
            var st = Ds.Pill(on ? new Color(1, 1, 1, 0.12f) : new Color(0, 0, 0, 0), 16, 4);
            var hv = Ds.Pill(new Color(1, 1, 1, on ? 0.14f : 0.05f), 16, 4);
            b.AddThemeStyleboxOverride("normal", st); b.AddThemeStyleboxOverride("hover", hv); b.AddThemeStyleboxOverride("pressed", hv);
            b.AddThemeColorOverride("font_color", on ? Ui.Text : Ui.Muted);
            b.AddThemeColorOverride("font_hover_color", Ui.Text);
            b.AddThemeFontSizeOverride("font_size", 13);
            b.AddThemeFontOverride("font", on ? Ui.Semi : Ui.Medium);
            b.Pressed += () => Go(page, key, arg);
            h.AddChild(b);
        }
    }
}
