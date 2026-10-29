using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Матч в духе FM: до матча — шансы, форма, вратари, лидеры, травмы; во время — табло с часами,
/// трансляция, график давления, живая статистика; после — итог, оценки игроков, три звезды.
/// Исход посчитан движком заранее; трансляция показывает его ход (хроника согласована со счётом).
/// </summary>
public partial class MatchScreen : Control
{
    readonly ScheduledGame _g;
    Career C => Main.I.Career;
    bool _fresh;
    double _clock = -1;
    double _speed = 60;
    int _shown;
    bool _keyOnly;
    List<MatchEvent> _ev;
    Label _scoreL, _clockL;
    VBoxContainer _feed;
    MomentumChart _chart;
    XgChart _xg;
    ShotMap _map;
    Dictionary<string, (Label a, Label h)> _stat;
    VBoxContainer _lineupBox;

    public MatchScreen(ScheduledGame g) { _g = g; }
    public MatchScreen() { }

    TeamData Home => C.Team(_g.Home);
    TeamData Away => C.Team(_g.Away);

    public override void _Ready()
    {
        if (_g.Played) ShowResult(); else ShowPreview();
    }

    // ------------------------------------------------------------------ каркас
    VBoxContainer Frame()
    {
        Ui.Clear(this);
        var root = Ui.V(0);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);
        root.AddChild(ScoreBar());
        var body = Ui.V(12);
        var m = Ui.Margin(body, 16, 12, 16, 12);
        m.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(m);
        return body;
    }

    string SeriesTitle()
    {
        var s = C.Bracket.FirstOrDefault(x => x.Games.Contains(_g.Id));
        if (s == null) return "";
        int n = s.Games.IndexOf(_g.Id) + 1;
        return $"Плей-офф · матч {n} · серия {C.Team(s.High).City} — {C.Team(s.Low).City} {s.WinsHigh}:{s.WinsLow}";
    }

    void Back()
    {
        if (_fresh) { C.SimDay(); Main.I.Save(); }
        Main.I.ShowHub();
    }

    /// <summary>Табло сверху, как в FM: цвета формы, названия, счёт, часы.</summary>
    Control ScoreBar()
    {
        var bar = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = Ui.Side, BorderColor = Ui.Line, BorderWidthBottom = 1 };
        sb.ContentMarginLeft = sb.ContentMarginRight = 14; sb.ContentMarginTop = sb.ContentMarginBottom = 8;
        bar.AddThemeStyleboxOverride("panel", sb);
        var h = Ui.H(12);
        bar.AddChild(h);
        h.AddChild(Ui.B("← К клубу", Back).Also(b => b.AddThemeFontSizeOverride("font_size", 12)));
        h.AddChild(Ui.L(_g.Playoff ? SeriesTitle() : $"Регулярный чемпионат · {Ui.Date(_g.Date)}", 12, Ui.Muted));
        h.AddChild(Ui.Spacer());
        h.AddChild(TeamPlate(Away, false));
        var mid = Ui.V(-2);
        mid.CustomMinimumSize = new Vector2(190, 0);
        // табло: янтарные цифры в тёмном окне, как знак HM27
        var board = new PanelContainer();
        var bsb = new StyleBoxFlat { BgColor = new Color("040A14"), BorderColor = new Color(Ui.Gold, 0.45f) };
        bsb.SetBorderWidthAll(1); bsb.SetCornerRadiusAll(4);
        bsb.ContentMarginLeft = bsb.ContentMarginRight = 18; bsb.ContentMarginTop = 0; bsb.ContentMarginBottom = 0;
        board.AddThemeStyleboxOverride("panel", bsb);
        _scoreL = Ui.L(_g.Played ? $"{_g.Result.AwayGoals}:{_g.Result.HomeGoals}" : "–:–", 44, Ui.Gold, Ui.CondBlack, HorizontalAlignment.Center);
        _scoreL.AddThemeColorOverride("font_shadow_color", new Color(Ui.Gold, 0.4f));
        _scoreL.AddThemeConstantOverride("shadow_outline_size", 6);
        _scoreL.AddThemeConstantOverride("shadow_offset_x", 0); _scoreL.AddThemeConstantOverride("shadow_offset_y", 0);
        board.AddChild(_scoreL);
        board.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _clockL = Ui.L(_g.Played ? EndText(_g.Result) : "до матча", 12, Ui.Muted, Ui.Semi, HorizontalAlignment.Center);
        mid.AddChild(board); mid.AddChild(_clockL);
        h.AddChild(mid);
        h.AddChild(TeamPlate(Home, true));
        h.AddChild(Ui.Spacer());
        h.AddChild(new Control { CustomMinimumSize = new Vector2(150, 0) });
        h.Alignment = BoxContainer.AlignmentMode.Center;
        return bar;
    }

    static string EndText(MatchResult r) => r.End == "REG" ? "Матч окончен" : r.End == "OT" ? "Матч окончен · ОТ" : "Матч окончен · буллиты";

    Control TeamPlate(TeamData t, bool home)
    {
        var c1 = new Color(t.Colors[0]); var c2 = new Color(t.Colors.Count > 1 ? t.Colors[1] : t.Colors[0]);
        var kit = Ui.V(0, new ColorRect { Color = c1.Luminance < 0.06f ? c1.Lightened(0.2f) : c1, CustomMinimumSize = new Vector2(8, 22) }, new ColorRect { Color = c2, CustomMinimumSize = new Vector2(8, 10) });
        kit.AddThemeConstantOverride("separation", 0);
        var name = Ui.V(-4, Ui.L(t.City, 24, Ui.Text, Ui.CondBold, home ? HorizontalAlignment.Left : HorizontalAlignment.Right),
            Ui.L(t.Name.Replace(t.City, "").Trim(), 11, Ui.Muted, null, home ? HorizontalAlignment.Left : HorizontalAlignment.Right));
        name.CustomMinimumSize = new Vector2(170, 0);
        var badge = new TeamBadge(t, 40) { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        kit.SizeFlagsVertical = SizeFlags.ShrinkCenter; name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return home ? Ui.H(8, kit, badge, name) : Ui.H(8, name, badge, kit);
    }

    // ------------------------------------------------------------------ до матча
    void ShowPreview()
    {
        var body = Frame();
        var o = C.Preview(_g);
        var hs = C.Side(_g.Home, _g.Date); var aws = C.Side(_g.Away, _g.Date);
        var row = Ui.H(12);
        row.AddChild(Ui.Expand(TeamColumn(Away, aws, false)));
        var mid = Ui.V(12); mid.CustomMinimumSize = new Vector2(420, 0);
        var chances = Ui.V(8);
        chances.AddChild(Ui.H(8, Ui.Icon("mail-open", 16, Ui.Muted), Ui.Expand(Ui.L(Pundits.Forecast(C.Data, _g, o), 15, Ui.Text, Ui.Semi).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart))));
        chances.AddChild(Ui.L($"Статусы: «{Away.City}» — {Pundits.Status(C, Away.Abbr).label.ToLowerInvariant()}, «{Home.City}» — {Pundits.Status(C, Home.Abbr).label.ToLowerInvariant()}", 12, Ui.Muted));
        if (hs.B2B || aws.B2B) chances.AddChild(Ui.L($"Второй матч за два дня: {string.Join(", ", new[] { aws.B2B ? Away.City : null, hs.B2B ? Home.City : null }.Where(x => x != null))}", 12, Ui.Otl));
        mid.AddChild(Widget.Make("Прогноз журналистов", chances));
        bool mine = _g.Home == C.UserTeam || _g.Away == C.UserTeam;
        if (mine)
        {
            var my = C.Lineups[C.UserTeam];
            var tac = Ui.V(4,
                Ui.H(8, Ui.Expand(Ui.L("Темп игры", 12, Ui.Muted)), Ui.L(my.Tempo switch { -1 => "Закрытый", 1 => "Открытый", _ => "Обычный" }, 13, Ui.Text, Ui.Semi)),
                Ui.H(8, Ui.Expand(Ui.L("Снятие вратаря", 12, Ui.Muted)), Ui.L(my.EarlyPull ? "Раньше обычного" : "Как обычно", 13, Ui.Text, Ui.Semi)),
                Ui.H(8, Ui.Expand(Ui.L("Вратарь на матч", 12, Ui.Muted)), Ui.L(C.Short((_g.Home == C.UserTeam ? hs : aws).Goalie), 13, Ui.Text, Ui.Semi)));
            mid.AddChild(Widget.Make("Ваш план на матч", tac, "Звенья →", () => Main.I.ShowHub("lines")));
            var go = Ui.B("Начать матч", StartMatch, true, 18);
            go.CustomMinimumSize = new Vector2(0, 52);
            mid.AddChild(go);
            var quick = Ui.B("Сразу результат", PlayInstant);
            quick.AddThemeFontSizeOverride("font_size", 13);
            mid.AddChild(quick);
            mid.AddChild(Ui.B("Арена в 3Д", () => Main.I.ShowArena(_g)).Also(b => b.AddThemeFontSizeOverride("font_size", 13)));
        }
        row.AddChild(mid);
        row.AddChild(Ui.Expand(TeamColumn(Home, hs, true)));
        body.AddChild(Ui.Scroll(row));
    }

    Control TeamColumn(TeamData t, MatchSide side, bool home)
    {
        var v = Ui.V(12);
        var r = C.Table[t.Abbr];
        var info = Ui.V(6);
        info.AddChild(Ui.L($"{(home ? "Хозяева" : "Гости")} · {r.W}-{r.L}-{r.Otl}, {r.Pts} {Ui.Plural(r.Pts, "очко", "очка", "очков")} · {C.Sorted(x => x.Conference == t.Conference).FindIndex(x => x.Team == t.Abbr) + 1}-е в конференции", 12, Ui.Muted));
        info.AddChild(Ui.H(8, Ui.L("Форма", 12, Ui.Muted), P.Form(t.Abbr, 5, 18)));
        var g = C.Data.PlayerById.GetValueOrDefault(side.Goalie);
        if (g != null) info.AddChild(Ui.H(8, Ui.L("Вратарь:", 12, Ui.Muted), Links.Player(g, null, 13, Ui.Text, Ui.Semi), Ui.Stars(StarScale.StarsV(C, g, PlayerModel.Visible(C, g, "ca")), 11)));
        v.AddChild(Widget.Make(t.Name, info));
        var l = side.Lineup;
        var lines = Ui.V(2);
        for (int i = 0; i < Math.Min(2, l.F.Count); i++)
            lines.AddChild(Ui.H(0, Ui.L($"{i + 1}: ", 12, Ui.Text), Links.Row(l.F[i].Select(id => (Control)Links.Player(C, id, C.Short(id), 12)))));
        for (int i = 0; i < Math.Min(2, l.D.Count); i++)
            lines.AddChild(Ui.H(0, Ui.L($"З{i + 1}: ", 12, Ui.Muted), Links.Row(l.D[i].Select(id => (Control)Links.Player(C, id, C.Short(id), 12, Ui.Muted)))));
        v.AddChild(Widget.Make("Звенья", lines));
        var inj = C.Injured.Where(id => C.Data.PlayerById[id].Team == t.Abbr).Select(id => C.Data.PlayerById[id]).OrderByDescending(p => p.Ovr).Take(5).ToList();
        v.AddChild(Widget.Make("Не сыграют", inj.Count == 0 ? Ui.L("Все здоровы", 12, Ui.Muted) : Ui.V(2, inj.Select(p => (Control)Ui.H(6, Ui.Icon("cross", 12, Ui.Loss), Links.Player(p, null, 12), Ui.Stars(StarScale.StarsV(C, p, PlayerModel.Visible(C, p, "ca")), 10))).ToArray())));
        return v;
    }

    static Control SplitBar(double left, Color a, Color b)
    {
        var h = new HBoxContainer { CustomMinimumSize = new Vector2(380, 10) };
        h.AddThemeConstantOverride("separation", 2);
        h.AddChild(new ColorRect { Color = a.Luminance < 0.08f ? a.Lightened(0.3f) : a, CustomMinimumSize = new Vector2((float)(376 * left), 10) });
        h.AddChild(new ColorRect { Color = b.Luminance < 0.08f ? b.Lightened(0.3f) : b, CustomMinimumSize = new Vector2((float)(376 * (1 - left)), 10) });
        return h;
    }

    // ------------------------------------------------------------------ матч
    void StartMatch()
    {
        C.Play(_g);
        _fresh = true;
        BuildLive();
        _clock = 0;
    }

    public void StartLive(double atSec) { StartMatch(); _clock = atSec; }

    public void PlayInstant()
    {
        if (!_g.Played) { C.Play(_g); _fresh = true; }
        _clock = -1;
        ShowResult();
    }

    List<MatchEvent> Events()
    {
        var r = _g.Result;
        // вбрасывания, силовые, отборы и потери — в статистике, в ленте только броски, голы и удаления
        if (r.Events.Count > 0) return r.Events.Where(e => e.Kind is not ("fo" or "hit" or "tk" or "gv")).ToList();
        // чужой матч без хроники: только голы
        return r.Goals.Where(e => e.Kind != "so").Select(e => new MatchEvent { T = (e.Period - 1) * 1200 + e.Sec, Team = e.Team, Kind = "goal", P1 = e.Scorer, P2 = e.A1, Note = e.Kind }).ToList();
    }

    void BuildLive()
    {
        _ev = Events();
        _shown = 0;
        var body = Frame();
        _scoreL.Text = "0:0"; _clockL.Text = "1-й период · 00:00";
        var row = Ui.Expand(Ui.H(12), true, true);
        body.AddChild(row);
        row.AddChild(StatsPanel());
        var center = Ui.Expand(Ui.V(10), true, true);
        _xg = new XgChart(_g.Result.Events, _g, C) { CustomMinimumSize = new Vector2(0, 140) };
        if (_xg.HasXg) { _xg.SetClock(0); center.AddChild(Widget.Make("Ожидаемые голы по ходу матча", _xg, null, null, 8)); }
        else
        {
            _xg = null;
            _chart = new MomentumChart(_ev, _g, C) { CustomMinimumSize = new Vector2(0, 120) };
            center.AddChild(Widget.Make("Давление (попытки бросков)", _chart, null, null, 8));
        }
        _feed = Ui.V(0);
        var feedHead = Ui.H(8, Ui.L("Трансляция", 12, Ui.Muted, Ui.Bold), Ui.Spacer());
        var filter = new Segmented(new[] { "Всё", "Ключевые моменты" }, _keyOnly ? 1 : 0, k => { _keyOnly = k == 1; RefillFeed(); });
        feedHead.AddChild(filter);
        var feedPanel = Widget.Make("Ход матча", Ui.V(8, feedHead, Ui.Expand(Ui.Scroll(_feed), true, true)).Also(x => x.SizeFlagsVertical = SizeFlags.ExpandFill), null, null, 10);
        feedPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        center.AddChild(feedPanel);
        var ctl = Ui.H(8);
        ctl.Alignment = BoxContainer.AlignmentMode.Center;
        ctl.AddChild(Ui.L("Скорость:", 12, Ui.Muted));
        foreach (var (n, sp) in new[] { ("×1", 30.0), ("×2", 60.0), ("×4", 120.0), ("×8", 300.0) })
            ctl.AddChild(Ui.B(n, () => _speed = sp).Also(b => b.AddThemeFontSizeOverride("font_size", 12)));
        ctl.AddChild(Ui.B("Сразу к итогу", PlayInstant).Also(b => b.AddThemeFontSizeOverride("font_size", 12)));
        center.AddChild(ctl);
        row.AddChild(center);
        _lineupBox = Ui.V(2);
        var rightCol = Ui.V(12); rightCol.CustomMinimumSize = new Vector2(420, 0);
        _map = new ShotMap(_g.Result.Events, _g, C) { CustomMinimumSize = new Vector2(0, 220) };
        _map.SetClock(0);
        if (!_map.Empty) rightCol.AddChild(Widget.Make("Карта бросков", _map, null, null, 8));
        else _map = null;
        var right = Widget.Make("Отличились", Ui.Scroll(_lineupBox), null, null, 10);
        right.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightCol.AddChild(right);
        row.AddChild(rightCol);
        AddFeed(new MatchEvent { T = 0, Kind = "start" });
    }

    Control StatsPanel()
    {
        _stat = new();
        var g = new GridContainer { Columns = 3 };
        g.AddThemeConstantOverride("h_separation", 10); g.AddThemeConstantOverride("v_separation", 6);
        g.AddChild(new TeamBadge(Away, 22)); g.AddChild(Ui.L("", 12)); g.AddChild(new TeamBadge(Home, 22));
        foreach (var k in new[] { "Броски в створ", "Ожидаемые голы", "Попытки бросков", "Заблокировано", "Удаления", "Голы в большинстве", "Сейвы", "Выиграно вбрасываний", "Силовые приёмы", "Отборы", "Потери" })
        {
            var a = Ui.L("0", 15, Ui.Text, Ui.Bold, HorizontalAlignment.Center); var h = Ui.L("0", 15, Ui.Text, Ui.Bold, HorizontalAlignment.Center);
            a.CustomMinimumSize = h.CustomMinimumSize = new Vector2(40, 0);
            g.AddChild(a); g.AddChild(Ui.L(k, 12, Ui.Muted, null, HorizontalAlignment.Center).Also(l => l.CustomMinimumSize = new Vector2(140, 0))); g.AddChild(h);
            _stat[k] = (a, h);
        }
        var w = Widget.Make("Статистика", g);
        w.CustomMinimumSize = new Vector2(280, 0);
        return Ui.V(12, w);
    }

    void UpdateStats(double t)
    {
        if (_stat == null) return;
        var past = _ev.Where(e => e.T <= t).ToList();
        var pastAll = _g.Result.Events.Where(e => e.T <= t).ToList();   // со вбрасываниями и силовыми
        int Count(string team, Func<MatchEvent, bool> f) => past.Count(e => e.Team == team && f(e));
        int CountAll(string team, string kind) => pastAll.Count(e => e.Team == team && e.Kind == kind);
        foreach (var (team, idx) in new[] { (_g.Away, 0), (_g.Home, 1) })
        {
            string opp = team == _g.Home ? _g.Away : _g.Home;
            var vals = new Dictionary<string, string>
            {
                ["Броски в створ"] = Count(team, e => e.Kind == "shot" || (e.Kind == "goal" && e.Note != "en")).ToString(),
                ["Ожидаемые голы"] = past.Where(e => e.Team == team && e.Kind is "shot" or "goal").Sum(e => e.Xg).ToString("0.0"),
                ["Попытки бросков"] = Count(team, e => e.Kind is "shot" or "miss" or "block" or "goal").ToString(),
                ["Заблокировано"] = Count(opp, e => e.Kind == "block").ToString(),
                ["Удаления"] = Count(team, e => e.Kind == "pen").ToString(),
                ["Голы в большинстве"] = $"{Count(team, e => e.Kind == "goal" && e.Note == "pp")}/{Count(opp, e => e.Kind == "pen")}",
                ["Сейвы"] = Count(opp, e => e.Kind == "shot").ToString(),
                ["Выиграно вбрасываний"] = CountAll(team, "fo").ToString(),
                ["Силовые приёмы"] = CountAll(team, "hit").ToString(),
                ["Отборы"] = CountAll(team, "tk").ToString(),
                ["Потери"] = CountAll(team, "gv").ToString(),
            };
            foreach (var (k, v) in vals) (idx == 0 ? _stat[k].a : _stat[k].h).Text = v;
        }
    }

    public override void _Process(double delta)
    {
        if (_clock < 0 || _g.Result == null || _ev == null) return;
        var r = _g.Result;
        _clock += delta * _speed;
        while (_shown < _ev.Count && _ev[_shown].T <= _clock) AddFeed(_ev[_shown++]);
        int hg = _ev.Take(_shown).Count(e => e.Kind == "goal" && e.Team == _g.Home), ag = _ev.Take(_shown).Count(e => e.Kind == "goal" && e.Team == _g.Away);
        _scoreL.Text = $"{ag}:{hg}";
        int t = (int)Math.Min(_clock, 3600);
        _clockL.Text = _clock < 3600 ? $"{t / 1200 + 1}-й период · {(t % 1200) / 60:00}:{t % 60:00}" : r.End == "REG" ? "Конец матча" : r.End == "OT" ? "Овертайм" : "Буллиты";
        _chart?.SetClock(_clock);
        _xg?.SetClock(_clock);
        _map?.SetClock(_clock);
        UpdateStats(_clock);
        double end = Math.Max(3600, _ev.Count > 0 ? _ev[^1].T : 3600) + (r.End == "SO" ? 120 : 0);
        if (_clock >= end + 30) { _clock = -1; ShowResult(); }
    }

    static bool IsKey(MatchEvent e) => e.Kind is "goal" or "pen" or "start" or "period";

    void RefillFeed()
    {
        if (_feed == null) return;
        Ui.Clear(_feed);
        AddFeed(new MatchEvent { T = 0, Kind = "start" });
        for (int i = 0; i < _shown; i++) AddFeed(_ev[i]);
    }

    int _lastPeriod = 1;
    void AddFeed(MatchEvent e)
    {
        int period = e.T / 1200 + 1;
        if (period > _lastPeriod && e.Kind != "start")
        {
            for (int p = _lastPeriod + 1; p <= period; p++) Put(new MatchEvent { T = (p - 1) * 1200, Kind = "period", Note = p <= 3 ? $"Начало {p}-го периода" : "Овертайм" });
            _lastPeriod = period;
        }
        if (_keyOnly && !IsKey(e)) return;
        Put(e);
        if (e.Kind == "goal") RefreshScorers();
    }

    void Put(MatchEvent e)
    {
        var (text, color, font, icon) = Describe(e);
        string clock = e.Kind == "start" ? "00:00" : $"{(e.T % 1200) / 60:00}:{e.T % 60:00}";
        var h = Ui.H(10);
        h.AddChild(Ui.L(clock, 12, Ui.Dim).Also(l => l.CustomMinimumSize = new Vector2(44, 0)));
        h.AddChild(e.Team != null ? new TeamBadge(C.Team(e.Team), 18) : (Control)Ui.Icon(icon, 16, Ui.Muted));
        h.AddChild(Ui.Expand(Ui.L(text, e.Kind == "goal" ? 14 : 13, color, font).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart)));
        var row = new PanelContainer();
        var sb = Ui.Box(e.Kind == "goal" ? new Color(Ui.Gold, 0.13f) : e.Kind == "period" || e.Kind == "start" ? new Color(1, 1, 1, 0.04f) : new Color(0, 0, 0, 0), 2, null, 8);
        sb.ContentMarginTop = sb.ContentMarginBottom = e.Kind == "goal" ? 7 : 3;
        if (e.Kind == "goal") { sb.BorderColor = Ui.Gold; sb.BorderWidthLeft = 3; }
        row.AddThemeStyleboxOverride("panel", sb);
        row.AddChild(h);
        _feed.AddChild(row);
        _feed.MoveChild(row, 0);   // свежее — сверху
    }

    (string, Color, Font, string) Describe(MatchEvent e)
    {
        string N(int id) => id != 0 && C.Data.PlayerById.TryGetValue(id, out var p) ? p.NameRu : "—";
        string S(int id) => C.Short(id);
        int h = e.T % 7;
        switch (e.Kind)
        {
            case "start": return ($"Вбрасывание! «{Home.City}» принимает «{Away.City}».", Ui.Muted, Ui.Semi, "activity");
            case "period": return (e.Note, Ui.Muted, Ui.Semi, "activity");
            case "goal":
                var gl = _g.Result.Goals.FirstOrDefault(x => x.Scorer == e.P1 && (x.Period - 1) * 1200 + x.Sec == e.T);
                var assists = gl == null ? new List<string>() : new[] { gl.A1, gl.A2 }.Where(x => x != 0).Select(S).ToList();
                string tag = e.Note switch { "pp" => " в большинстве", "sh" => " в меньшинстве", "en" => " в пустые ворота", "ev6" => " с шестым полевым!", _ => "" };
                return ($"ГОЛ{tag}! {N(e.P1)}{(assists.Count > 0 ? $" ({string.Join(", ", assists)})" : " (без передач)")}", Colors.White, Ui.Bold, "trophy");
            case "shot":
                string[] sv = { "отражает", "ловит шайбу", "отбивает щитком", "спасает", "забирает шайбу", "в отличной позиции — сейв", "отбивает блином" };
                return ($"{S(e.P1)}: {e.Note} — {S(e.P2)} {sv[h]}", Ui.Text, null, "");
            case "miss": return ($"{S(e.P1)} бросает — {e.Note}", Ui.Muted, null, "");
            case "block": return ($"{S(e.P1)} бросает — блокирует {S(e.P2)}", Ui.Muted, null, "");
            case "pen": int pm = e.P2 is 4 or 5 ? e.P2 : 2; return ($"Удаление: {N(e.P1)}, {pm} {(pm == 5 ? "минут" : "минуты")} ({e.Note})", Ui.Otl, Ui.Semi, "");
            default: return (e.Kind, Ui.Muted, null, "");
        }
    }

    void RefreshScorers()
    {
        if (_lineupBox == null) return;
        Ui.Clear(_lineupBox);
        var goals = _ev.Take(_shown).Where(e => e.Kind == "goal").ToList();
        if (goals.Count == 0) { _lineupBox.AddChild(Ui.L("Пока без голов", 12, Ui.Muted)); return; }
        foreach (var e in goals)
            _lineupBox.AddChild(Ui.H(6, new TeamBadge(C.Team(e.Team), 16), Ui.L($"{(e.T % 1200) / 60:00}:{e.T % 60:00}", 11, Ui.Dim), Links.Player(C, e.P1, C.Short(e.P1), 13, Ui.Text, Ui.Semi)));
    }

    // ------------------------------------------------------------------ итог
    void ShowResult()
    {
        _ev = Events();
        var body = Frame();
        var r = _g.Result;
        var row = Ui.Expand(Ui.H(12), true, true);
        body.AddChild(row);

        var left = Ui.V(12); left.CustomMinimumSize = new Vector2(300, 0);
        var sp = StatsPanel();
        left.AddChild(sp);
        UpdateStats(1e9);
        int periods = Math.Max(3, r.Goals.Where(e => e.Kind != "so").Select(e => e.Period).DefaultIfEmpty(3).Max());
        var pg = new GridContainer { Columns = periods + 2 + (r.End == "SO" ? 1 : 0) };
        pg.AddThemeConstantOverride("h_separation", 12);
        pg.AddChild(Ui.L("", 11));
        for (int p = 1; p <= periods; p++) pg.AddChild(Ui.L(p <= 3 ? $"{p}" : "ОТ", 11, Ui.Muted, Ui.Bold, HorizontalAlignment.Center));
        if (r.End == "SO") pg.AddChild(Ui.L("Б", 11, Ui.Muted, Ui.Bold, HorizontalAlignment.Center));
        pg.AddChild(Ui.L("И", 11, Ui.Muted, Ui.Bold, HorizontalAlignment.Center));
        foreach (var t in new[] { _g.Away, _g.Home })
        {
            pg.AddChild(Ui.H(6, new TeamBadge(C.Team(t), 18), Ui.L(C.Team(t).City, 12)));
            for (int p = 1; p <= periods; p++) pg.AddChild(Ui.L($"{r.Goals.Count(e => e.Team == t && e.Period == p && e.Kind != "so")}", 13, Ui.Text, null, HorizontalAlignment.Center));
            if (r.End == "SO") pg.AddChild(Ui.L($"{r.Goals.Count(e => e.Team == t && e.Kind == "so")}", 13, Ui.Text, null, HorizontalAlignment.Center));
            pg.AddChild(Ui.L($"{(t == _g.Home ? r.HomeGoals : r.AwayGoals)}", 14, Ui.Text, Ui.Bold, HorizontalAlignment.Center));
        }
        left.AddChild(Widget.Make("По периодам", pg));
        left.AddChild(Widget.Make("Три звезды", Ui.V(6, Stars(r).ToArray())));
        row.AddChild(Ui.Scroll(left).Also(s => s.CustomMinimumSize = new Vector2(310, 0)));

        var center = Ui.Expand(Ui.V(10), true, true);
        var xg = new XgChart(r.Events, _g, C);
        if (xg.HasXg)
        {
            center.AddChild(Widget.Make("Ожидаемые голы по ходу матча", xg, null, null, 8));
            center.AddChild(Widget.Make("Карта бросков", new ShotMap(r.Events, _g, C) { CustomMinimumSize = new Vector2(0, 250) }, null, null, 8));
        }
        else
        {
            var chart = new MomentumChart(_ev, _g, C) { CustomMinimumSize = new Vector2(0, 120) };
            chart.SetClock(1e9);
            center.AddChild(Widget.Make("Давление (попытки бросков)", chart, null, null, 8));
        }
        var goals = Ui.V(6);
        foreach (var e in r.Goals) goals.AddChild(GoalRow(e));
        if (r.Goals.Count == 0) goals.AddChild(Ui.L("Голов не было", 13, Ui.Muted));
        if (r.PullNote == "tie") goals.AddChild(Ui.L("Раннее снятие вратаря сработало: гол с шестым полевым.", 12, Ui.Win));
        if (r.PullNote == "en") goals.AddChild(Ui.L("Вратаря сняли раньше — пропустили в пустые ворота.", 12, Ui.Muted));
        center.AddChild(Widget.Make("Голы", goals));
        var act = Ui.H(12);
        act.Alignment = BoxContainer.AlignmentMode.Center;
        var next = Ui.B(_fresh ? "Продолжить" : "К клубу", Back, true, 16);
        next.CustomMinimumSize = new Vector2(240, 46);
        act.AddChild(next);
        center.AddChild(act);
        row.AddChild(Ui.Scroll(center).Also(s => s.SizeFlagsHorizontal = SizeFlags.ExpandFill));

        var right = Ui.V(12); right.CustomMinimumSize = new Vector2(440, 0);
        foreach (var (t, dressed) in new[] { (_g.Away, r.AwayDressed), (_g.Home, r.HomeDressed) })
            right.AddChild(Widget.Make($"Протокол: {C.Team(t).City}", RatingsTable(dressed), null, null, 0));
        row.AddChild(Ui.Scroll(right).Also(s => s.CustomMinimumSize = new Vector2(450, 0)));
    }

    Control RatingsTable(List<int> dressed)
    {
        var r = _g.Result;
        var rows = dressed.Where(id => r.Ratings.ContainsKey(id)).Select(id => C.Data.PlayerById[id]).OrderByDescending(p => r.Ratings[p.Id]).ToList();
        if (rows.Count == 0) return Ui.Margin(Ui.L("Оценки есть только у матчей, сыгранных в этой версии", 12, Ui.Muted), 10, 8, 10, 8);
        return new Grid<PlayerData>(new List<Col<PlayerData>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = p => Ui.L(p.NameRu, 12) },
            new() { Title = "Поз", Width = 28, Cell = p => Ui.L(Ui.PosRu(p.Pos), 11, Ui.Muted) },
            new() { Title = "Г+П", Width = 40, Cell = p => { int g = r.Goals.Count(e => e.Scorer == p.Id && e.Kind != "so"), a = r.Goals.Count(e => e.A1 == p.Id || e.A2 == p.Id); return Ui.L(g + a > 0 ? $"{g}+{a}" : "", 12, Ui.Text, Ui.Semi); } },
            new() { Title = "Вр", Width = 44, Cell = p => Ui.L(r.Box.TryGetValue(p.Id, out var b) ? Ui.Toi(b.Toi) : "", 11, Ui.Muted) },
            new() { Title = "Бр", Width = 26, Cell = p => Ui.L(r.Box.TryGetValue(p.Id, out var b) && !p.IsGoalie ? $"{b.S}" : "", 11, Ui.Muted) },
            new() { Title = "+/−", Width = 32, Cell = p => Ui.L(r.Box.TryGetValue(p.Id, out var b) && !p.IsGoalie ? Ui.Signed(b.Pm, "0") : "", 11, Ui.Muted) },
            new() { Title = "Оц.", Width = 40, Cell = p => { var v = r.Ratings[p.Id]; return Ui.L($"{v:0.0}", 13, v >= 7.5 ? new Color("43D17A") : v >= 6.8 ? Ui.Text : v >= 6.3 ? Ui.Otl : Ui.Loss, Ui.Bold); } },
        }, rows, p => Shell.I?.Go("player", null, p.Id.ToString())).Root;
    }

    Control GoalRow(GoalEvent e)
    {
        var t = C.Team(e.Team);
        var h = Ui.H(10);
        h.AddChild(Ui.L(e.Kind == "so" ? "Буллиты" : Ui.Clock(e.Period, e.Sec), 12, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(110, 0)));
        h.AddChild(new TeamBadge(t, 22));
        if (e.Kind == "so") h.AddChild(Ui.L($"Победный буллит — «{t.City}»", 14, Ui.Text, Ui.Semi));
        else
        {
            var assists = new[] { e.A1, e.A2 }.Where(x => x != 0).ToList();
            h.AddChild(Links.Player(C, e.Scorer, null, 14, Ui.Text, Ui.Bold));
            if (assists.Count > 0) h.AddChild(Ui.H(0, Ui.L("(", 13, Ui.Muted), Links.Row(assists.Select(x => (Control)Links.Player(C, x, C.Short(x), 13, Ui.Muted)), ", ", 13), Ui.L(")", 13, Ui.Muted)));
            else h.AddChild(Ui.L("(без передач)", 13, Ui.Muted));
            string tag = e.Kind switch { "pp" => "в большинстве", "sh" => "в меньшинстве", "en" => "в пустые", "ev6" => "6 на 5", _ => null };
            if (tag != null) h.AddChild(Ui.Card(Ui.L(tag, 10, Colors.Black, Ui.Bold), e.Kind == "sh" ? Ui.Win : Ui.Otl, 6));
            if (e.Period >= 4) h.AddChild(Ui.Card(Ui.L("овертайм", 10, Colors.White, Ui.Bold), Ui.Accent, 6));
        }
        return h;
    }

    IEnumerable<Control> Stars(MatchResult r)
    {
        var top = r.Ratings.Count > 0
            ? r.Ratings.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToList()
            : r.Goals.Where(e => e.Scorer != 0).GroupBy(e => e.Scorer).OrderByDescending(x => x.Count()).Take(3).Select(x => x.Key).ToList();
        int i = 0;
        foreach (var id in top)
        {
            var p = C.Data.PlayerById[id];
            string line;
            if (p.IsGoalie)
            {
                bool home = id == r.HomeGoalie;
                int sh = home ? r.AwayShots : r.HomeShots, ga = home ? r.AwayGoals : r.HomeGoals;
                line = $"{sh - ga} сейвов";
            }
            else line = $"{r.Goals.Count(e => e.Scorer == id && e.Kind != "so")}+{r.Goals.Count(e => e.A1 == id || e.A2 == id)}";
            var st = Ui.H(1);
            for (int k = 0; k < 3 - i; k++) st.AddChild(Ui.Icon("star", 13, Ui.Gold).Also(x => x.Texture = Icons.Get("star", true)));
            st.CustomMinimumSize = new Vector2(44, 0);
            i++;
            yield return Ui.H(8, st, new TeamBadge(C.Team(p.Team), 20), Ui.Expand(Links.Player(p, null, 13, Ui.Text, Ui.Semi)), Ui.L(line, 12, Ui.Muted),
                r.Ratings.TryGetValue(id, out var rt) ? Ui.L($"{rt:0.0}", 13, new Color("43D17A"), Ui.Bold) : new Control());
        }
    }
}

/// <summary>График давления: разница попыток бросков по минутам (вверх — гости, вниз — хозяева, как в табло), голы — отметки.</summary>
public partial class MomentumChart : Control
{
    readonly List<MatchEvent> _ev;
    readonly ScheduledGame _g;
    readonly Color _ca, _ch;
    double _clock;
    readonly double _end;
    public MomentumChart() { }
    public MomentumChart(List<MatchEvent> ev, ScheduledGame g, Career c)
    {
        _ev = ev; _g = g;
        Color Vis(Color x) { for (int i = 0; i < 6 && x.Luminance < 0.35f; i++) x = x.Lightened(0.15f); return x; }
        _ca = Vis(new Color(c.Team(g.Away).Colors[0])); _ch = Vis(new Color(c.Team(g.Home).Colors[0]));
        if (_ca.IsEqualApprox(_ch)) _ch = Vis(new Color(c.Team(g.Home).Colors.Count > 1 ? c.Team(g.Home).Colors[1] : "#CCCCCC"));
        _end = Math.Max(3600, ev.Count > 0 ? ev.Max(e => e.T) + 30 : 3600);
    }

    public void SetClock(double t) { _clock = t; QueueRedraw(); }

    public override void _Draw()
    {
        var s = Size;
        float mid = s.Y / 2;
        DrawLine(new Vector2(0, mid), new Vector2(s.X, mid), new Color(1, 1, 1, 0.12f), 1);
        for (int p = 1; p < 3; p++) DrawLine(new Vector2((float)(p * 1200 / _end * s.X), 0), new Vector2((float)(p * 1200 / _end * s.X), s.Y), new Color(1, 1, 1, 0.08f), 1);
        if (_ev == null) return;
        int bins = (int)Math.Ceiling(_end / 60);
        var diff = new double[bins];
        foreach (var e in _ev.Where(e => e.T <= _clock && e.Kind is "shot" or "miss" or "block" or "goal"))
            diff[Math.Min(bins - 1, e.T / 60)] += e.Team == _g.Away ? 1 : -1;
        // сглаживание
        var sm = new double[bins];
        for (int i = 0; i < bins; i++) { double a = 0, w = 0; for (int k = -2; k <= 2; k++) if (i + k >= 0 && i + k < bins) { double ww = 3 - Math.Abs(k); a += diff[i + k] * ww; w += ww; } sm[i] = a / w; }
        double max = Math.Max(1.2, sm.Select(Math.Abs).DefaultIfEmpty(1).Max());
        float bw = s.X / bins;
        int upto = (int)Math.Min(bins, Math.Ceiling(_clock / 60));
        for (int i = 0; i < upto; i++)
        {
            float h = (float)(sm[i] / max * (mid - 6));
            var col = h > 0 ? _ca : _ch;
            DrawRect(new Rect2(i * bw + 0.5f, h > 0 ? mid - h : mid, bw - 1, Math.Abs(h)), new Color(col, 0.85f));
        }
        foreach (var e in _ev.Where(e => e.Kind == "goal" && e.T <= _clock))
        {
            float x = (float)(e.T / _end * s.X);
            bool away = e.Team == _g.Away;
            DrawLine(new Vector2(x, away ? 2 : mid), new Vector2(x, away ? mid : s.Y - 2), Ui.Gold, 2);
            DrawCircle(new Vector2(x, away ? 6 : s.Y - 6), 5, Ui.Gold);
        }
        if (_clock < _end) DrawLine(new Vector2((float)(_clock / _end * s.X), 0), new Vector2((float)(_clock / _end * s.X), s.Y), new Color(1, 1, 1, 0.5f), 1);
        DrawString(Ui.Semi, new Vector2(6, 14), "гости", HorizontalAlignment.Left, -1, 10, new Color(_ca, 0.9f));
        DrawString(Ui.Semi, new Vector2(6, s.Y - 6), "хозяева", HorizontalAlignment.Left, -1, 10, new Color(_ch, 0.9f));
    }
}
