using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Матч: перед матчем — шансы по модели, вратари, лидеры; во время — табло с часами и лентой голов
/// (исход уже посчитан движком, часы показывают его ход); после — итог, по периодам, три звезды.
/// </summary>
public partial class MatchScreen : Control
{
    readonly ScheduledGame _g;
    Career C => Main.I.Career;
    bool _fresh;           // матч сыгран только что (а не открыт из истории)
    double _clock = -1;    // игровые секунды во время показа
    double _speed = 120;   // игровых секунд в секунду
    Label _scoreL, _clockL;
    VBoxContainer _feed;
    int _shown;
    Control _actions;

    public MatchScreen(ScheduledGame g) { _g = g; }
    public MatchScreen() { }

    public override void _Ready()
    {
        if (_g.Played) ShowResult(); else ShowPreview();
    }

    TeamData Home => C.Team(_g.Home);
    TeamData Away => C.Team(_g.Away);

    Control Frame(out VBoxContainer body)
    {
        Ui.Clear(this);
        body = Ui.V(18);
        var m = Ui.Margin(Ui.Scroll(body), 60, 30, 60, 30);
        m.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(m);
        string title = _g.Playoff ? $"Плей-офф · {SeriesTitle()}" : "Регулярный чемпионат";
        body.AddChild(Ui.H(12, Ui.B("← К клубу", Back), Ui.L($"{title} · {Ui.Date(_g.Date)}", 15, Ui.Muted)));
        return m;
    }

    string SeriesTitle()
    {
        var s = C.Bracket.FirstOrDefault(x => x.Games.Contains(_g.Id));
        if (s == null) return "";
        int n = s.Games.IndexOf(_g.Id) + 1;
        return $"матч {n} · серия {C.Team(s.High).City} — {C.Team(s.Low).City} {s.WinsHigh}:{s.WinsLow}";
    }

    void Back()
    {
        if (_fresh) { C.SimDay(); Main.I.Save(); }
        Main.I.ShowHub();
    }

    // ------------------------------------------------------------------ перед матчем
    void ShowPreview()
    {
        Frame(out var body);
        var o = C.Preview(_g);
        var hs = C.Side(_g.Home, _g.Date); var aws = C.Side(_g.Away, _g.Date);

        var row = Ui.H(30);
        row.AddChild(Ui.Expand(TeamColumn(Away, aws, false)));
        var mid = Ui.V(10);
        mid.CustomMinimumSize = new Vector2(380, 0);
        mid.AddChild(Ui.L("ШАНСЫ ПО МОДЕЛИ", 12, Ui.Muted, Ui.Semi, HorizontalAlignment.Center));
        mid.AddChild(Ui.H(10, Ui.L(Ui.Pct(1 - o.PHome), 34, Ui.Text, Ui.Black), Ui.Spacer(), Ui.L(Ui.Pct(o.PHome), 34, Ui.Text, Ui.Black)));
        mid.AddChild(SplitBar(1 - o.PHome, new Color(Away.Colors[0]), new Color(Home.Colors[0])));
        mid.AddChild(Ui.L($"Ожидаемые голы: {o.LamAway:0.0} — {o.LamHome:0.0}", 14, Ui.Muted, null, HorizontalAlignment.Center));
        mid.AddChild(Ui.L($"Ничья в основное время: {Ui.Pct(o.PTie)}", 14, Ui.Muted, null, HorizontalAlignment.Center));
        if (hs.B2B || aws.B2B)
            mid.AddChild(Ui.L($"Второй матч за два дня: {(aws.B2B ? Away.City : "")}{(aws.B2B && hs.B2B ? ", " : "")}{(hs.B2B ? Home.City : "")}", 13, Ui.Otl, null, HorizontalAlignment.Center));
        row.AddChild(mid);
        row.AddChild(Ui.Expand(TeamColumn(Home, hs, true)));
        body.AddChild(Ui.Card(row, Ui.Panel, 26));

        var actions = Ui.H(12);
        actions.Alignment = BoxContainer.AlignmentMode.Center;
        bool mine = _g.Home == C.UserTeam || _g.Away == C.UserTeam;
        if (mine)
        {
            actions.AddChild(Ui.B("Состав и звенья", () => Main.I.ShowHub("lines")));
            actions.AddChild(Ui.B("Арена в 3Д", () => Main.I.ShowArena(_g)));
            var go = Ui.B("Начать матч", StartMatch, true, 18);
            go.CustomMinimumSize = new Vector2(260, 52);
            actions.AddChild(go);
        }
        body.AddChild(actions);
        body.AddChild(Ui.L("Исход считает движок на модели NHL-STATS.RU: сила клубов по xG, ваши звенья и минуты, вратари, усталость. Матч в 3Д появится в следующих версиях.", 13, Ui.Dim, null, HorizontalAlignment.Center).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
    }

    Control TeamColumn(TeamData t, MatchSide side, bool home)
    {
        var v = Ui.V(10);
        v.Alignment = BoxContainer.AlignmentMode.Center;
        var badge = new TeamBadge(t, 120) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        v.AddChild(badge);
        v.AddChild(Ui.L(t.Name, 24, Ui.Text, Ui.Bold, HorizontalAlignment.Center));
        var r = C.Table[t.Abbr];
        v.AddChild(Ui.L($"{(home ? "Хозяева" : "Гости")} · {r.W}-{r.L}-{r.Otl} · {r.Pts} {Ui.Plural(r.Pts, "очко", "очка", "очков")}", 14, Ui.Muted, null, HorizontalAlignment.Center));
        var g = C.Data.PlayerById.GetValueOrDefault(side.Goalie);
        if (g != null) v.AddChild(Ui.H(8, Ui.Spacer(), Ui.L($"Вратарь: {g.NameRu}", 14), Ui.Rating(g.Ovr, 12, 30), Ui.Spacer()));
        var top = side.Lineup.Skaters.Select(id => C.Data.PlayerById[id]).OrderByDescending(p => p.Ovr).Take(3);
        v.AddChild(Ui.L("Лидеры: " + string.Join(", ", top.Select(p => p.LastRu)), 13, Ui.Muted, null, HorizontalAlignment.Center));
        return v;
    }

    static Control SplitBar(double left, Color a, Color b)
    {
        var h = new HBoxContainer { CustomMinimumSize = new Vector2(360, 10) };
        h.AddThemeConstantOverride("separation", 2);
        h.AddChild(new ColorRect { Color = a.Luminance < 0.08f ? a.Lightened(0.3f) : a, CustomMinimumSize = new Vector2((float)(356 * left), 10) });
        h.AddChild(new ColorRect { Color = b.Luminance < 0.08f ? b.Lightened(0.3f) : b, CustomMinimumSize = new Vector2((float)(356 * (1 - left)), 10) });
        return h;
    }

    // ------------------------------------------------------------------ матч
    void StartMatch()
    {
        C.Play(_g);
        _fresh = true;
        Frame(out var body);
        body.AddChild(Scoreboard(0, 0, "1-й период · 00:00"));
        var ctl = Ui.H(10);
        ctl.Alignment = BoxContainer.AlignmentMode.Center;
        ctl.AddChild(Ui.L("Скорость:", 13, Ui.Muted));
        foreach (var (n, sp) in new[] { ("×1", 60.0), ("×2", 120.0), ("×4", 240.0) })
            ctl.AddChild(Ui.B(n, () => _speed = sp));
        ctl.AddChild(Ui.B("Сразу к итогу", PlayInstant));
        _actions = ctl;
        body.AddChild(ctl);
        _feed = Ui.V(6);
        body.AddChild(Ui.Card(_feed, Ui.Panel, 18));
        _feed.AddChild(Ui.L("Вбрасывание!", 15, Ui.Muted));
        _clock = 0; _shown = 0;
    }

    /// <summary>Для автоматических снимков: начать матч и перемотать часы.</summary>
    public void StartLive(double atSec) { StartMatch(); _clock = atSec; }

    public void PlayInstant()
    {
        if (!_g.Played) { C.Play(_g); _fresh = true; }
        _clock = -1;
        ShowResult();
    }

    public override void _Process(double delta)
    {
        if (_clock < 0 || _g.Result == null) return;
        var r = _g.Result;
        _clock += delta * _speed;
        // голы до текущего момента
        while (_shown < r.Goals.Count && GoalTime(r.Goals[_shown]) <= _clock)
        {
            var e = r.Goals[_shown++];
            _feed.AddChild(GoalRow(e));
        }
        int hg = r.Goals.Take(_shown).Count(e => e.Team == _g.Home), ag = r.Goals.Take(_shown).Count(e => e.Team == _g.Away);
        int t = (int)Math.Min(_clock, 3600);
        _scoreL.Text = $"{ag} : {hg}";
        _clockL.Text = _clock < 3600 ? $"{t / 1200 + 1}-й период · {(t % 1200) / 60:00}:{t % 60:00}" : r.End == "REG" ? "Матч окончен" : r.End == "OT" ? "Овертайм" : "Буллиты";
        double end = r.Goals.Count > 0 ? Math.Max(3600, GoalTime(r.Goals[^1])) : 3600;
        if (_clock >= end + 60) { _clock = -1; ShowResult(); }
    }

    static double GoalTime(GoalEvent e) => e.Kind == "so" ? 3600 + 400 : (e.Period - 1) * 1200 + e.Sec;

    Control Scoreboard(int ag, int hg, string clock)
    {
        var h = Ui.H(26);
        h.Alignment = BoxContainer.AlignmentMode.Center;
        h.AddChild(Ui.V(6, new TeamBadge(Away, 96) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter }, Ui.L(Away.Name, 18, Ui.Text, Ui.Semi, HorizontalAlignment.Center)).Also(x => x.CustomMinimumSize = new Vector2(300, 0)));
        var mid = Ui.V(4);
        _scoreL = Ui.L($"{ag} : {hg}", 72, Ui.Text, Ui.Black, HorizontalAlignment.Center);
        _clockL = Ui.L(clock, 16, Ui.Muted, Ui.Semi, HorizontalAlignment.Center);
        mid.AddChild(_scoreL); mid.AddChild(_clockL);
        mid.CustomMinimumSize = new Vector2(280, 0);
        h.AddChild(mid);
        h.AddChild(Ui.V(6, new TeamBadge(Home, 96) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter }, Ui.L(Home.Name, 18, Ui.Text, Ui.Semi, HorizontalAlignment.Center)).Also(x => x.CustomMinimumSize = new Vector2(300, 0)));
        return Ui.Card(h, Ui.Panel2, 24);
    }

    Control GoalRow(GoalEvent e)
    {
        var t = C.Team(e.Team);
        var h = Ui.H(12);
        h.AddChild(Ui.L(e.Kind == "so" ? "Буллиты" : Ui.Clock(e.Period, e.Sec), 13, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(120, 0)));
        h.AddChild(new TeamBadge(t, 26));
        if (e.Kind == "so") h.AddChild(Ui.L($"Победный буллит — «{t.City}»", 15, Ui.Text, Ui.Semi));
        else
        {
            var p = C.Data.PlayerById.GetValueOrDefault(e.Scorer);
            var assists = new[] { e.A1, e.A2 }.Where(x => x != 0).Select(x => C.Short(x)).ToList();
            h.AddChild(Ui.L($"ГОЛ! {p?.NameRu ?? "—"}", 15, Ui.Text, Ui.Bold));
            h.AddChild(Ui.L(assists.Count > 0 ? $"({string.Join(", ", assists)})" : "(без передач)", 14, Ui.Muted));
            if (e.Kind == "pp") h.AddChild(Pill("в большинстве", Ui.Otl));
            if (e.Kind == "sh") h.AddChild(Pill("в меньшинстве", Ui.Win));
            if (e.Period >= 4) h.AddChild(Pill("овертайм", Ui.Accent));
        }
        return h;
    }

    static Control Pill(string text, Color c) => Ui.Card(Ui.L(text, 11, Ui.ReadableOn(c), Ui.Semi), c, 6);

    // ------------------------------------------------------------------ итог
    void ShowResult()
    {
        Frame(out var body);
        var r = _g.Result;
        string end = r.End == "REG" ? "Матч окончен" : r.End == "OT" ? "Победа в овертайме" : "Победа по буллитам";
        body.AddChild(Scoreboard(r.AwayGoals, r.HomeGoals, end));

        var cols = Ui.H(18);
        var left = Ui.Expand(Ui.V(10));
        foreach (var e in r.Goals) left.AddChild(GoalRow(e));
        if (r.Goals.Count == 0) left.AddChild(Ui.L("Голов не было.", 14, Ui.Muted));
        cols.AddChild(Ui.Expand(Ui.Card(Ui.V(10, Ui.L("ГОЛЫ", 12, Ui.Muted, Ui.Semi), left), Ui.Panel, 18)));

        var right = Ui.V(12);
        right.CustomMinimumSize = new Vector2(400, 0);
        int periods = Math.Max(3, r.Goals.Where(e => e.Kind != "so").Select(e => e.Period).DefaultIfEmpty(3).Max());
        var heads = new List<string> { "" };
        for (int p = 1; p <= periods; p++) heads.Add(p <= 3 ? $"{p}" : p == 4 ? "ОТ" : $"ОТ{p - 3}");
        if (r.End == "SO") heads.Add("Б");
        heads.Add("И");
        var widths = new List<int> { 0 }; widths.AddRange(Enumerable.Repeat(36, heads.Count - 1));
        var pt = new Table(heads.ToArray(), widths.ToArray());
        foreach (var t in new[] { _g.Away, _g.Home })
        {
            var cells = new List<Control> { Ui.H(8, new TeamBadge(C.Team(t), 22), Ui.L(C.Team(t).City, 14)) };
            for (int p = 1; p <= periods; p++) cells.Add(Ui.L($"{r.Goals.Count(e => e.Team == t && e.Period == p && e.Kind != "so")}", 14));
            if (r.End == "SO") cells.Add(Ui.L($"{r.Goals.Count(e => e.Team == t && e.Kind == "so")}", 14));
            cells.Add(Ui.L($"{(t == _g.Home ? r.HomeGoals : r.AwayGoals)}", 15, Ui.Text, Ui.Bold));
            pt.Add(cells.ToArray(), t == C.UserTeam);
        }
        right.AddChild(Ui.Card(Ui.V(10, Ui.L("ПО ПЕРИОДАМ", 12, Ui.Muted, Ui.Semi), pt), Ui.Panel, 18));
        var st = new Table(new[] { "", "Гости", "Хозяева" }, new[] { 0, 90, 90 });
        st.Add(new Control[] { Ui.L("Броски в створ", 14), Ui.L($"{r.AwayShots}", 14), Ui.L($"{r.HomeShots}", 14) });
        st.Add(new Control[] { Ui.L("Вратари", 14), Ui.L(C.Short(r.AwayGoalie), 13), Ui.L(C.Short(r.HomeGoalie), 13) });
        int aSv = r.HomeShots - (r.HomeGoals - (r.End == "SO" && r.HomeGoals > r.AwayGoals ? 1 : 0));
        int hSv = r.AwayShots - (r.AwayGoals - (r.End == "SO" && r.AwayGoals > r.HomeGoals ? 1 : 0));
        st.Add(new Control[] { Ui.L("Отражено", 14), Ui.L($"{aSv} из {r.HomeShots}", 13), Ui.L($"{hSv} из {r.AwayShots}", 13) });
        st.Add(new Control[] { Ui.L("Шанс до матча", 14), Ui.L(Ui.Pct(1 - r.PHome), 13, Ui.Muted), Ui.L(Ui.Pct(r.PHome), 13, Ui.Muted) });
        right.AddChild(Ui.Card(Ui.V(10, Ui.L("СТАТИСТИКА", 12, Ui.Muted, Ui.Semi), st), Ui.Panel, 18));
        right.AddChild(Ui.Card(Ui.V(8, new Control[] { Ui.L("ТРИ ЗВЕЗДЫ", 12, Ui.Muted, Ui.Semi) }.Concat(Stars(r)).ToArray()), Ui.Panel, 18));
        cols.AddChild(right);
        body.AddChild(cols);

        var act = Ui.H(12);
        act.Alignment = BoxContainer.AlignmentMode.Center;
        var next = Ui.B(_fresh ? "Дальше" : "К клубу", Back, true, 17);
        next.CustomMinimumSize = new Vector2(240, 50);
        act.AddChild(next);
        body.AddChild(act);
    }

    IEnumerable<Control> Stars(MatchResult r)
    {
        var pts = new Dictionary<int, double>();
        foreach (var e in r.Goals.Where(e => e.Kind != "so" && e.Scorer != 0))
        {
            pts[e.Scorer] = pts.GetValueOrDefault(e.Scorer) + 1.1;
            if (e.A1 != 0) pts[e.A1] = pts.GetValueOrDefault(e.A1) + 0.8;
            if (e.A2 != 0) pts[e.A2] = pts.GetValueOrDefault(e.A2) + 0.6;
        }
        // вратари: отражённые сверх 25 + бонус за «сухарь»
        int hGa = r.AwayGoals, aGa = r.HomeGoals;
        pts[r.HomeGoalie] = Math.Max(0, (r.AwayShots - hGa - 24) * 0.12) + (hGa == 0 ? 2.5 : 0) + (r.HomeGoals > r.AwayGoals ? 0.4 : 0);
        pts[r.AwayGoalie] = Math.Max(0, (r.HomeShots - aGa - 24) * 0.12) + (aGa == 0 ? 2.5 : 0) + (r.AwayGoals > r.HomeGoals ? 0.4 : 0);
        int i = 0;
        foreach (var (id, _) in pts.Where(kv => kv.Key != 0).OrderByDescending(kv => kv.Value).Take(3).Select(kv => (kv.Key, kv.Value)))
        {
            var p = C.Data.PlayerById[id];
            string line;
            if (p.IsGoalie)
            {
                bool home = id == r.HomeGoalie;
                int sh = home ? r.AwayShots : r.HomeShots, ga = home ? hGa : aGa;
                line = $"{sh - ga} сейвов из {sh}";
            }
            else
            {
                int g = r.Goals.Count(e => e.Scorer == id && e.Kind != "so"), a = r.Goals.Count(e => e.A1 == id || e.A2 == id);
                line = $"{g} + {a}";
            }
            yield return Ui.H(10, Ui.L(new string('*', 3 - i++), 15, Ui.Otl, Ui.Bold).Also(l => l.CustomMinimumSize = new Vector2(30, 0)),
                new TeamBadge(C.Team(p.Team), 22), Ui.Expand(Ui.L(p.NameRu, 14, Ui.Text, Ui.Semi)), Ui.L(line, 13, Ui.Muted));
        }
    }
}
