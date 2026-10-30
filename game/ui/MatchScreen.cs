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
        var (fa, fh) = MatchColors.Fill(Away, Home);
        AddChild(new IceBg(fh, fa));
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
        if (_live != null && !_g.Played) { C.FinishLive(_live); _live = null; }   // ушли посреди трансляции — помощник доиграет
        if (_fresh) { C.SimDay(); Main.I.Save(); }
        Main.I.ShowHub();
    }

    /// <summary>Табло сверху, как в FM: цвета формы, названия, счёт, часы.</summary>
    Control ScoreBar()
    {
        var bar = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.35f), BorderColor = new Color(1, 1, 1, 0.06f), BorderWidthBottom = 1 };
        sb.ContentMarginLeft = sb.ContentMarginRight = 16; sb.ContentMarginTop = sb.ContentMarginBottom = 8;
        bar.AddThemeStyleboxOverride("panel", sb);
        var h = Ui.H(12);
        bar.AddChild(h);
        h.AddChild(Ui.B("← К клубу", Back).Also(b => b.AddThemeFontSizeOverride("font_size", 12)));
        h.AddChild(Ui.L(_g.Playoff ? SeriesTitle() : $"Регулярный чемпионат · {Ui.Date(_g.Date)}", 12, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(200, 0)));
        // табло трансляции: счёт и часы — подписи ниже (их меняет экран), табло рисует их в своём стиле
        _scoreL = Ui.L(_g.Played ? $"{_g.Result.AwayGoals}:{_g.Result.HomeGoals}" : "–:–", 12);
        _clockL = Ui.L(_g.Played ? EndText(_g.Result) : "до матча", 12);
        _scoreL.Visible = _clockL.Visible = false;
        h.AddChild(_scoreL); h.AddChild(_clockL);
        h.AddChild(Ui.Expand(new Scorebug(Away, Home, _scoreL, _clockL)));
        h.AddChild(new Control { CustomMinimumSize = new Vector2(260, 0) });
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
        var (hs, aws) = C.Sides(_g, true);
        bool mine = _g.Home == C.UserTeam || _g.Away == C.UserTeam;
        if (!mine) { ShowNeutralPreview(body, o, hs, aws); return; }
        bool home = _g.Home == C.UserTeam;
        var rep = Scout.For(C, _g);
        // экран «до матча» помещается без прокрутки: слева прогноз и старт, справа вкладки — план, задания, соперник, концовка, наш состав
        var plan = PlanColumn(rep, o, home);
        var parts = plan.GetChildren().OfType<Control>().ToList();
        foreach (var x in parts) plan.RemoveChild(x);
        var row = Ui.Expand(Ui.H(16), true, true);
        body.AddChild(row);
        var left = Ui.V(14); left.CustomMinimumSize = new Vector2(400, 0);
        left.AddChild(parts[0]);
        foreach (var x in parts.Skip(3)) left.AddChild(x);
        var team = TeamColumn(home ? Home : Away, home ? hs : aws, home);
        left.AddChild(Ui.Expand(Ui.Scroll(team), true, true));
        row.AddChild(left);
        var tabs = new (string name, Func<Control> build)[]
        {
            ("План на матч", () => parts[1]),
            ("Задания", () => parts[2]),
            ("Соперник", () => ScoutColumn(rep, home ? aws : hs)),
            ("Концовка и правила", () => Ui.H(16, Ui.Expand(EndgameWidget(hs, aws, home)), Ui.Expand(RulesWidget()))),
        };
        _pvTab = Math.Clamp(_pvTab, 0, tabs.Length - 1);
        var right = Ui.Expand(Ui.V(14), true, true);
        var content = Ui.Expand(new MarginContainer(), true, true);
        var pages = tabs.Select(t => Ui.Scroll(t.build())).ToList();
        foreach (var pg in pages) content.AddChild(pg);
        void ShowTab(int k)
        {
            _pvTab = k;
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == k;
        }
        right.AddChild(new Segmented(tabs.Select(t => t.name).ToArray(), _pvTab, ShowTab));
        right.AddChild(content);
        row.AddChild(right);
        ShowTab(_pvTab);
    }
    static int _pvTab;

    /// <summary>Темп и снятие вратаря на этот матч: выбор помощника (словами) и шанс отыграться (это важно знать в цифрах).</summary>
    Control EndgameWidget(MatchSide hs, MatchSide aws, bool home)
    {
        var l = C.Lineups[C.UserTeam];
        var asst = Staff.Assistant(C);
        string opp = "«" + (home ? Away : Home).Name + "»";
        var v = Ui.V(6);
        if (l.TempoAuto)
        {
            var tp = Coach.Tempo(C, _g, hs, aws, home);
            v.AddChild(Ui.L($"Темп: {(tp.tempo < 0 ? "закрытый" : tp.tempo > 0 ? "открытый" : "обычный")}", 13, Ui.Text, Ui.Semi));
            v.AddChild(Ui.L($"{asst?.Name ?? "Помощник"} ({tp.conf}): " + Coach.TempoText(tp.tempo, tp.gain, opp, _g.Playoff), 11, Ui.Muted).Also(Wrap));
        }
        else v.AddChild(Ui.L($"Темп: {(l.Tempo < 0 ? "закрытый" : l.Tempo > 0 ? "открытый" : "обычный")} — ваш выбор", 13, Ui.Text, Ui.Semi));
        var pp = Coach.Pull(C, _g, hs, aws, home);
        Control Cell(string k, string val, Color col) => Ui.V(-2, Ui.L(k, 11, Ui.Dim), Ui.L(val, 20, col, Ui.CondBold));
        if (l.Pull == Coach.ASSIST)
        {
            v.AddChild(Ui.H(18, Cell("при −1", Coach.Clock(pp.T1), Ui.Text), Cell("при −2", Coach.Clock(pp.T2), Ui.Text),
                Cell("сравнять", $"{100 * pp.Tie1:0}%", Ui.Gold), Cell("если за 2:00", $"{100 * pp.TieUsual1:0}%", Ui.Muted)));
            v.AddChild(Ui.L(Coach.PullWhy(pp, opp) + " «Сравнять» — шанс отыграться при −1 за 10 минут до конца.", 11, Ui.Muted).Also(Wrap));
        }
        else v.AddChild(Ui.L(l.Pull == Coach.EARLY ? $"Вратаря снимаем рано: за {Coach.Clock(Coach.EARLY1)} при −1 и за {Coach.Clock(Coach.EARLY2)} при −2." : $"Вратаря снимаем как в НХЛ: за 2:00. Шанс сравнять — {100 * pp.TieUsual1:0}%.", 11, Ui.Muted).Also(Wrap));
        return Widget.Make("Концовка", v, "Настроить →", () => Main.I.ShowHub("lines"));
    }

    /// <summary>Правила помощника в перерывах (сводка; редактируются на странице «Тактика»).</summary>
    Control RulesWidget()
    {
        var rules = Rules.Of(C.Lineups[C.UserTeam]).Where(r => r.On && r.Then != "none").ToList();
        var rl = Ui.V(4);
        foreach (var r in rules) rl.AddChild(Ui.V(0, Ui.L(Rules.WhenText(r.When), 11, Ui.Muted), Ui.L("→ " + Rules.ThenText(r.Then).ToLowerInvariant(), 12, Ui.Text)));
        if (rules.Count == 0) rl.AddChild(Ui.L("Правил нет — в перерывах помощник ничего не меняет.", 12, Ui.Muted));
        return Widget.Make("В перерывах помощник", rl, "Правила →", () => Main.I.ShowHub("tactics"));
    }

    void ShowNeutralPreview(VBoxContainer body, MatchOdds o, MatchSide hs, MatchSide aws)
    {
        var row = Ui.H(12);
        row.AddChild(Ui.Expand(TeamColumn(Away, aws, false)));
        var mid = Ui.V(12); mid.CustomMinimumSize = new Vector2(420, 0);
        mid.AddChild(Widget.Make("Прогноз журналистов", Ui.L(Pundits.Forecast(C.Data, _g, o), 15, Ui.Text, Ui.Semi).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart)));
        row.AddChild(mid);
        row.AddChild(Ui.Expand(TeamColumn(Home, hs, true)));
        body.AddChild(Ui.Scroll(row));
    }

    // ------------------------------------------------------------------ скаут: соперник
    Control ScoutColumn(Scout.Report rep, MatchSide op)
    {
        var v = Ui.V(12);
        var sys = Ui.V(4);
        for (int i = 0; i < Tactics.Levers.Length; i++)
        {
            int pv = rep.OppSys[i];
            sys.AddChild(Ui.H(8, Ui.L(Tactics.Levers[i].Name, 12, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(110, 0)),
                Ui.L(Tactics.PoleName(Tactics.Levers[i], pv), 13, pv == 0 ? Ui.Text : Ui.Gold, Ui.Semi)));
        }
        v.AddChild(Widget.Make($"Скаут: {rep.Opp}", Ui.V(10,
            Ui.L("Система", 14, Ui.Text, Ui.Semi), sys,
            Ui.L("Опасны", 14, Ui.Text, Ui.Semi),
            Ui.V(4, rep.Threats.Select(t => (Control)Ui.V(0,
                Links.Player(C, t.Id, C.Data.PlayerById[t.Id].NameRu, 13, Ui.Text, Ui.Semi),
                Ui.L(t.Why, 11, Ui.Muted))).ToArray()),
            Ui.L("Вратарь", 14, Ui.Text, Ui.Semi),
            Ui.V(0, Links.Player(C, rep.Goalie, C.Data.PlayerById.GetValueOrDefault(rep.Goalie)?.NameRu ?? "—", 13, Ui.Text, Ui.Semi), Ui.L(rep.GoalieText ?? "", 11, Ui.Muted).Also(Wrap)),
            Ui.L("Спецбригады", 14, Ui.Text, Ui.Semi),
            Ui.L($"Большинство: {rep.PpFor:0.00} гола за матч, {rep.PpRank}-е в лиге", 12, rep.PpRank <= 8 ? Ui.Otl : Ui.Muted),
            Ui.L($"Меньшинство: пропускают {rep.PkAgainst:0.00} за матч, {rep.PkRank}-е", 12, rep.PkRank >= 25 ? Ui.Win : Ui.Muted))));
        var l = op.Lineup;
        var lines = Ui.V(2);
        for (int i = 0; i < Math.Min(3, l.F.Count); i++)
            lines.AddChild(Ui.H(6, Ui.L($"{i + 1}", 12, Ui.Dim).Also(x => x.CustomMinimumSize = new Vector2(14, 0)),
                Ui.L(l.RoleF(i) == 1 ? "атака" : l.RoleF(i) == 2 ? "оборона" : "", 10, LinesView.RoleColor(l.RoleF(i))).Also(x => x.CustomMinimumSize = new Vector2(46, 0)),
                Links.Row(l.F[i].Select(id => (Control)Links.Player(C, id, C.Short(id), 12)))));
        v.AddChild(Widget.Make("Их звенья", lines));
        return v;
    }

    static void Wrap(Label l) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(220, 0); }

    // ------------------------------------------------------------------ план на матч
    MatchPlan EnsurePlan()
    {
        var club = C.Lineups[C.UserTeam].Sys ?? new int[Tactics.Levers.Length];
        if (C.Plan == null || C.Plan.Game != _g.Id) C.Plan = new MatchPlan { Game = _g.Id, Sys = (int[])club.Clone() };
        C.Plan.Sys ??= (int[])club.Clone();
        return C.Plan;
    }

    void PlanChanged() { Main.I.Save(); CallDeferred(nameof(ShowPreview)); }

    Control PlanColumn(Scout.Report rep, MatchOdds o, bool home)
    {
        var v = Ui.V(12);
        // прогноз
        double pw = home ? o.PHome : 1 - o.PHome;
        var fc = Ui.V(6,
            Ui.H(8, Ui.Expand(Ui.L(Pundits.Forecast(C.Data, _g, o), 14, Ui.Text, Ui.Semi).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart)),
                Ui.L($"{100 * pw:0}%", 30, Ui.Gold, Ui.CondBlack)),
            SplitBar(pw, home ? MatchColors.Of(C, _g).home : MatchColors.Of(C, _g).away, home ? MatchColors.Of(C, _g).away : MatchColors.Of(C, _g).home),
            Ui.L("шанс на победу по прогнозу — уже с вашим планом", 11, Ui.Dim));
        v.AddChild(Widget.Make("Прогноз", fc));

        // система на матч: советы скаута — словами, без голов за матч
        var plan = C.Plan != null && C.Plan.Game == _g.Id ? C.Plan : null;
        var club = C.Lineups[C.UserTeam].Sys ?? new int[Tactics.Levers.Length];
        var cur = plan?.Sys ?? club;
        string by = rep.By?.Name ?? "Помощник";
        var sys = Ui.V(6);
        foreach (var (phase, levers) in Tactics.Phases)
        {
            sys.AddChild(Ui.L(phase, 12, Ui.Gold, Ui.Semi));
            foreach (var i in levers)
            {
                int li = i;
                var lv = Tactics.Levers[i];
                var adv = rep.Advice.FirstOrDefault(a => a.Lever == i);
                int cv = i < cur.Length ? cur[i] : 0, kv = i < club.Length ? club[i] : 0;
                var seg = new Segmented(new[] { lv.Minus.Short, lv.MidShort, lv.Plus.Short }, cv + 1, k => { var p = EnsurePlan(); p.Sys[li] = k - 1; PlanChanged(); });
                var r = Ui.H(10, Ui.L(lv.Name, 13, Ui.Text, Ui.Semi).Also(l => l.CustomMinimumSize = new Vector2(120, 0)), seg);
                if (cv != kv) r.AddChild(Ui.L("только на матч", 11, Ui.Gold));
                sys.AddChild(r);
                if (adv != null)
                {
                    var tip = Ui.H(8, Ui.Icon("lightbulb", 13, Ui.Gold),
                        Ui.Expand(Ui.L($"{by} ({adv.Conf}): {Tactics.PoleName(lv, adv.V).ToLowerInvariant()} — {Staff.GainWord(adv.Gain)}. {Cap(adv.Why)}" + (adv.Learn < 0.95 ? " Схема ещё не выучена — это он учёл." : ""), 11, Ui.Muted).Also(Wrap)),
                        Ui.B("Принять", () => { var p = EnsurePlan(); p.Sys[li] = adv.V; PlanChanged(); }, false, 11));
                    sys.AddChild(tip);
                }
            }
        }
        if (rep.Advice.Count == 0) sys.AddChild(Ui.L($"{by} не видит, что менять: система клуба против них подходит.", 11, Ui.Muted));
        if (plan != null && !plan.Sys.SequenceEqual(club))
            sys.AddChild(Ui.B("Вернуть систему клуба", () => { plan.Sys = (int[])club.Clone(); PlanChanged(); }, false, 11));
        v.AddChild(Widget.Make("Система на матч", sys));

        // задания на соперника: до трёх, у каждого своя цена
        var tasks = Ui.V(8);
        var opp = home ? _g.Away : _g.Home;
        var ol = C.Lineups[opp];
        var pool = ol.F.SelectMany(x => x).Concat(ol.D.SelectMany(x => x)).Where(id => id != 0).Distinct().ToList();
        var targets = plan?.All() ?? new Dictionary<int, string>();
        foreach (var (id, kind) in targets.ToList())
        {
            int tid = id;
            var kob = new OptionButton { FocusMode = FocusModeEnum.None };
            for (int k = 0; k < Targeting.Kinds.Length; k++) { kob.AddItem(Targeting.Kinds[k].Name, k); if (Targeting.Kinds[k].Key == kind) kob.Selected = k; }
            kob.ItemSelected += k => { EnsurePlan().Set(tid, Targeting.Kinds[(int)k].Key); PlanChanged(); };
            var kd = Targeting.Get(kind);
            tasks.AddChild(Ui.V(1,
                Ui.H(10, Links.Player(C, id, C.Short(id), 13, Ui.Text, Ui.Semi).Also(l => l.CustomMinimumSize = new Vector2(130, 0)), kob, Ui.Spacer(),
                    Ui.B("Снять", () => { EnsurePlan().Set(tid, null); PlanChanged(); }, false, 11)),
                Ui.L(kd == null ? "" : $"{char.ToUpper(kd.What[0])}{kd.What[1..]}; {kd.Cost}.", 11, Ui.Muted)));
        }
        if (targets.Count < MatchPlan.MAX_TARGETS)
        {
            var pob = new OptionButton { FocusMode = FocusModeEnum.None };
            pob.AddItem("Добавить задание: выберите игрока", 0);
            var free = pool.Where(id => !targets.ContainsKey(id)).ToList();
            for (int k = 0; k < free.Count; k++) pob.AddItem($"{C.Short(free[k])} ({Ui.PosRu(C.Data.PlayerById[free[k]].Pos)})", k + 1);
            pob.Selected = 0;
            pob.ItemSelected += idx => { if (idx > 0) { EnsurePlan().Set(free[(int)idx - 1], "shadow"); PlanChanged(); } };
            tasks.AddChild(pob);
        }
        else tasks.AddChild(Ui.L("Три задания — больше команда не удержит в голове.", 11, Ui.Dim));
        if (targets.Count >= 1) tasks.AddChild(Ui.L("Внимание делится: чем больше заданий, тем слабее каждое, а цена у каждого своя.", 11, Ui.Dim).Also(Wrap));
        foreach (var tip in rep.Tips.Where(t => !targets.ContainsKey(t.Id)).Take(3))
        {
            var kd = Targeting.Get(tip.Kind);
            var tid = tip.Id; var tk = tip.Kind;
            var b = Ui.B("Принять", () => { if (EnsurePlan().Set(tid, tk)) PlanChanged(); }, false, 11);
            b.Disabled = targets.Count >= MatchPlan.MAX_TARGETS;
            tasks.AddChild(Ui.H(8, Ui.Icon("lightbulb", 13, Ui.Gold),
                Ui.Expand(Ui.L($"{by} ({tip.Conf}): {kd.Name.ToLowerInvariant()} — {C.Data.PlayerById[tip.Id].NameRu}, {tip.Why}.", 11, Ui.Muted).Also(Wrap)), b));
        }
        v.AddChild(Widget.Make("Задания на соперника", tasks));

        var go = Ui.B("Начать матч", StartMatch, true, 18);
        go.CustomMinimumSize = new Vector2(0, 52);
        v.AddChild(go);
        var quick = Ui.B("Сразу результат", PlayInstant);
        quick.AddThemeFontSizeOverride("font_size", 13);
        v.AddChild(Ui.H(10, Ui.Expand(quick), Ui.Expand(Ui.B("Арена в 3Д", () => Main.I.ShowArena(_g)).Also(b => b.AddThemeFontSizeOverride("font_size", 13)))));
        return v;
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..] + (s.EndsWith(".") ? "" : ".");

    VBoxContainer TeamColumn(TeamData t, MatchSide side, bool home)
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
    Career.LiveGame _live;
    bool _paused;
    int _evIdx;
    VBoxContainer _adviceBox, _iceBox;
    Button _pauseBtn;
    readonly List<Button> _speedBtns = new();
    /// <summary>Игровых секунд в реальную секунду: ×1 — матч примерно за три минуты.</summary>
    static readonly (string name, double sp)[] Speeds = { ("×1", 20), ("×2", 40), ("×4", 80), ("×8", 200) };

    void StartMatch()
    {
        _live = C.BeginLive(_g);
        _fresh = true;
        _speed = Speeds[0].sp;
        if (_live == null) { C.Play(_g); BuildLive(); _clock = 0; return; }   // старые сохранения: показ готового матча
        _evIdx = 0; _paused = false;
        BuildLive();
        _clock = 0;
        Intro();
    }

    // ------------------------------------------------------------------ трансляция: заставка и гол
    double _introLeft;
    void Intro()
    {
        AddChild(new MatchIntro(Away, Home, _g.Playoff ? SeriesTitle() : "Регулярный чемпионат", Ui.Date(_g.Date, false).ToUpperInvariant()));
        _introLeft = MatchIntro.LEN - 0.3;
    }

    /// <summary>Гол в трансляции: полоса в цветах клуба с автором (на самой быстрой скорости — без полосы).</summary>
    void Celebrate(MatchEvent e)
    {
        if (e.Kind != "goal" || _speed >= 200 || e.Team == null) return;
        foreach (var old in GetChildren().OfType<GoalBanner>()) old.QueueFree();
        var gl = Res.Goals.FirstOrDefault(x => x.Scorer == e.P1 && (x.Period - 1) * 1200 + x.Sec == e.T);
        var assists = gl == null ? new List<string>() : new[] { gl.A1, gl.A2 }.Where(x => x != 0).Select(C.Short).ToList();
        string tag = e.Note switch { "pp" => "в большинстве", "sh" => "в меньшинстве", "en" => "в пустые ворота", "ev6" => "с шестым полевым", _ => "" };
        string sub = $"{(e.T % 1200) / 60:00}:{e.T % 60:00}  ·  " + (assists.Count > 0 ? "передачи: " + string.Join(", ", assists) : "без передач");
        AddChild(new GoalBanner(C.Team(e.Team), C.Data.PlayerById.GetValueOrDefault(e.P1), sub, tag));
    }

    public void StartLive(double atSec) { StartMatch(); _clock = atSec; }

    /// <summary>Снимки: полоса гола (по голу из прошлого матча клуба), без заставки.</summary>
    public void DebugGoal()
    {
        _introLeft = 0;
        foreach (var x in GetChildren().OfType<MatchIntro>()) x.QueueFree();
        var last = C.LastGame(C.UserTeam);
        var gl = last?.Result?.Goals.FirstOrDefault(x => x.Team == C.UserTeam && x.Kind != "so") ?? last?.Result?.Goals.FirstOrDefault();
        if (gl == null) return;
        var keep = _speed; _speed = 20;
        var b = new MatchEvent { T = (gl.Period - 1) * 1200 + gl.Sec, Team = gl.Team, Kind = "goal", P1 = gl.Scorer, Note = gl.Kind };
        var assists = new[] { gl.A1, gl.A2 }.Where(x => x != 0).Select(C.Short).ToList();
        AddChild(new GoalBanner(C.Team(gl.Team), C.Data.PlayerById.GetValueOrDefault(gl.Scorer), $"{gl.Sec / 60:00}:{gl.Sec % 60:00}  ·  " + (assists.Count > 0 ? "передачи: " + string.Join(", ", assists) : "без передач"), gl.Kind == "pp" ? "в большинстве" : ""));
        _speed = keep;
        _paused = true;
    }

    /// <summary>Снимки: пауза и панель тренера на вкладке.</summary>
    public void DebugCoach(string tab) { if (_live == null) return; _tab = tab; if (!_paused) TogglePause(); else FillCoach(); }

    public void PlayInstant()
    {
        if (_live != null && !_g.Played) C.FinishLive(_live);
        else if (!_g.Played) C.Play(_g);
        _fresh = true;
        _live = null;
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

    MatchResult Res => _live?.Sim.Result ?? _g.Result;

    /// <summary>Все события для статистики: в трансляции — те, что движок уже сыграл.</summary>
    List<MatchEvent> AllEvents => _live != null ? _live.Sim.Result.Events : _g.Result.Events;

    void BuildLive()
    {
        _ev = _live != null ? new List<MatchEvent>() : Events();
        _shown = 0; _lastPeriod = 1;
        var body = Frame();
        _scoreL.Text = "0:0"; _clockL.Text = "1-й период · 00:00";
        var row = Ui.Expand(Ui.H(12), true, true);
        body.AddChild(row);
        var left = StatsPanel();
        _iceBox = Ui.V(4);
        if (_live != null) ((VBoxContainer)left).AddChild(Widget.Make("На льду", _iceBox));
        row.AddChild(left);
        var center = Ui.Expand(Ui.V(10), true, true);
        _xg = new XgChart(AllEvents, _g, C) { CustomMinimumSize = new Vector2(0, 140) };
        if (_live != null || _xg.HasXg) { _xg.SetClock(0); center.AddChild(Widget.Make("Ожидаемые голы по ходу матча", _xg, null, null, 8)); }
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
        _feedPanel = Widget.Make("Ход матча", Ui.V(8, feedHead, Ui.Expand(Ui.Scroll(_feed), true, true)).Also(x => x.SizeFlagsVertical = SizeFlags.ExpandFill), null, null, 10);
        _feedPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        center.AddChild(_feedPanel);
        if (_live != null)
        {
            // панель тренера: на паузе и в перерыве вместо ленты
            _coachBody = Ui.V(10);
            _coachPanel = new PanelContainer { Visible = false, SizeFlagsVertical = SizeFlags.ExpandFill };
            _coachPanel.AddThemeStyleboxOverride("panel", Ds.GlassBoxStyle(Ds.R_L, 18, 0.06f).Also(s => { s.BgColor = new Color(0.1f, 0.12f, 0.16f, 0.94f); s.BorderColor = new Color(Ui.Gold, 0.55f); }));
            _coachPanel.AddChild(Ui.Scroll(_coachBody));
            center.AddChild(_coachPanel);
        }
        var ctl = Ui.H(8);
        ctl.Alignment = BoxContainer.AlignmentMode.Center;
        if (_live != null)
        {
            _pauseBtn = Ui.B("Пауза · тренер", TogglePause, false, 13);
            _pauseBtn.CustomMinimumSize = new Vector2(140, 0);
            ctl.AddChild(_pauseBtn);
            _timeoutBtn = Ui.B("Тайм-аут", () => { if (_live.Sim.Timeout(_live.UserHome)) { SyncFeed(); UpdateControls(); if (_paused) FillCoach(); } }, false, 12);
            _timeoutBtn.TooltipText = "Один за матч: все отдохнут — свежесть первых звеньев вернётся";
            ctl.AddChild(_timeoutBtn);
            _videoBtn = Ui.B("Видеопросмотр", Challenge, false, 12);
            _videoBtn.AddThemeColorOverride("font_color", Ui.Gold);
            ctl.AddChild(_videoBtn);
        }
        ctl.AddChild(Ui.L("Скорость:", 12, Ui.Muted));
        _speedBtns.Clear();
        foreach (var (n, sp) in Speeds)
        {
            var b = Ui.B(n, null).Also(x => x.AddThemeFontSizeOverride("font_size", 12));
            b.Pressed += () => { _speed = sp; MarkSpeed(); };
            _speedBtns.Add(b); ctl.AddChild(b);
        }
        MarkSpeed();
        ctl.AddChild(Ui.B("Сразу к итогу", PlayInstant).Also(b => { b.AddThemeFontSizeOverride("font_size", 12); b.TooltipText = "Помощник доиграет матч по вашим правилам"; }));
        center.AddChild(ctl);
        UpdateControls();
        row.AddChild(center);
        _lineupBox = Ui.V(2);
        var rightCol = Ui.V(12); rightCol.CustomMinimumSize = new Vector2(420, 0);
        _map = new ShotMap(AllEvents, _g, C) { CustomMinimumSize = new Vector2(0, 220) };
        _map.SetClock(0);
        if (_live != null || !_map.Empty) rightCol.AddChild(Widget.Make("Карта бросков", _map, null, null, 8));
        else _map = null;
        if (_live != null)
        {
            _adviceBox = Ui.V(6);
            rightCol.AddChild(Widget.Make("Помощник", _adviceBox));
            FillAdvice(false);
        }
        var right = Widget.Make("Отличились", Ui.Scroll(_lineupBox), null, null, 10);
        right.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightCol.AddChild(right);
        row.AddChild(rightCol);
        AddFeed(new MatchEvent { T = 0, Kind = "start" });
    }

    void MarkSpeed()
    {
        for (int i = 0; i < _speedBtns.Count; i++)
        {
            bool on = Math.Abs(Speeds[i].sp - _speed) < 0.01;
            _speedBtns[i].AddThemeColorOverride("font_color", on ? Ui.Gold : Ui.Text);
        }
    }

    void TogglePause()
    {
        if (_live == null) return;
        _paused = !_paused;
        ShowCoach(_paused);
        FillAdvice(_paused);
    }

    PanelContainer _coachPanel; Control _feedPanel; VBoxContainer _coachBody;
    Button _timeoutBtn, _videoBtn;
    string _tab = "advice"; int _pick; string _shoutRes;

    void ShowCoach(bool on)
    {
        if (_coachPanel == null) return;
        _coachPanel.Visible = on; _feedPanel.Visible = !on;
        _pauseBtn.Text = on ? "Играть" : "Пауза · тренер";
        if (on) { _pick = 0; FillCoach(); }
    }

    /// <summary>Тайм-аут и видеопросмотр: доступны ли сейчас.</summary>
    void UpdateControls()
    {
        if (_live == null || _timeoutBtn == null) return;
        _timeoutBtn.Disabled = _live.Sim.TimeoutUsed(_live.UserHome) || !_live.Sim.InPeriod;
        _videoBtn.Visible = _live.Sim.ChallengeFor(_live.UserHome) >= 0;
        if (_videoBtn.Visible) _videoBtn.TooltipText = Shouts.VideoRead(C, _live).text;
    }

    void Challenge()
    {
        if (_live == null) return;
        bool ok = _live.Sim.Challenge(_live.UserHome);
        _scoreL.Text = $"{_live.Sim.AwayGoals}:{_live.Sim.HomeGoals}";
        SyncFeed(); RefreshScorers(); UpdateControls(); UpdateStats(_live.Sim.Clock);
        _xg?.SetEvents(_live.Sim.Result.Events); _map?.SetEvents(_live.Sim.Result.Events);
        if (_paused) FillCoach();
    }

    /// <summary>Новые события движка — в ленту (после решений тренера на паузе тоже).</summary>
    void SyncFeed()
    {
        var all = _live.Sim.Result.Events;
        for (; _evIdx < all.Count; _evIdx++)
        {
            var e = all[_evIdx];
            if (e.Kind is "fo" or "hit" or "tk" or "gv") continue;
            _ev.Add(e);
        }
        while (_shown < _ev.Count) AddFeed(_ev[_shown++]);
    }

    /// <summary>Справа: короткая сводка помощника — есть ли советы (решения — на паузе, в панели тренера).</summary>
    void FillAdvice(bool active)
    {
        if (_adviceBox == null || _live == null) return;
        Ui.Clear(_adviceBox);
        var me = _live.Me;
        string tempo = me.Tempo switch { -1 => "закрытый", 1 => "открытый", _ => "обычный" };
        var sys = me.Plan ?? me.Lineup.Sys ?? new int[Tactics.Levers.Length];
        var edges = Enumerable.Range(0, Tactics.Levers.Length).Where(i => i < sys.Length && sys[i] != 0).Select(i => Tactics.PoleShort(Tactics.Levers[i], sys[i])).ToList();
        _adviceBox.AddChild(Ui.L($"Темп: {tempo}. Система: " + (edges.Count == 0 ? "обычная" : string.Join(" · ", edges)), 11, Ui.Muted).Also(Wrap));
        if (active) { _adviceBox.AddChild(Ui.L("Панель тренера открыта — решения там.", 12, Ui.Dim)); return; }
        if (_pendingAdvice > 0)
        {
            var b = Ui.B($"Помощник: есть {_pendingAdvice} {(_pendingAdvice == 1 ? "совет" : _pendingAdvice < 5 ? "совета" : "советов")} — пауза", TogglePause, false, 12);
            b.AddThemeColorOverride("font_color", Ui.Gold);
            _adviceBox.AddChild(b);
        }
        else _adviceBox.AddChild(Ui.L("Пауза — панель тренера: звенья, тактика, задания, указания. В перерывах она откроется сама.", 12, Ui.Dim).Also(Wrap));
    }

    void Act(Action a) { a(); Main.I.Save(); SyncFeed(); UpdateControls(); FillCoach(); FillAdvice(true); }

    /// <summary>Панель тренера: вкладки «Советы», «Звенья», «Состояние», «Тактика», «Задания».</summary>
    void FillCoach()
    {
        if (_coachBody == null || _live == null) return;
        Ui.Clear(_coachBody);
        var sim = _live.Sim;
        int per = sim.Period;
        string title = _live.AtBreak ? $"Перерыв после {(per <= 3 ? per + "-го периода" : "овертайма")}" : $"Пауза · {(per <= 3 ? per + "-й период" : "овертайм")}";
        var head = Ui.H(10, Ui.L(title, 18, Ui.Gold, Ui.Bold), Ui.Spacer());
        if (_live.AtBreak)
            head.AddChild(Ui.B(sim.Period >= 3 ? "Начать овертайм" : $"Начать {sim.Period + 1}-й период", () => { _live.AtBreak = false; _paused = false; ShowCoach(false); FillAdvice(false); }, true, 14));
        else head.AddChild(Ui.B("Играть", TogglePause, true, 14));
        _coachBody.AddChild(head);
        var tabs = new (string key, string name)[] { ("advice", "Советы и указания"), ("lines", "Звенья"), ("state", "Состояние"), ("tactics", "Тактика"), ("targets", "Задания") };
        _coachBody.AddChild(new Segmented(tabs.Select(t => t.name).ToArray(), Math.Max(0, Array.FindIndex(tabs, t => t.key == _tab)), k => { _tab = tabs[k].key; _pick = 0; FillCoach(); }));
        switch (_tab)
        {
            case "lines": TabLines(); break;
            case "state": TabState(); break;
            case "tactics": TabTactics(); break;
            case "targets": TabTargets(); break;
            default: TabAdvice(); break;
        }
    }

    void TabAdvice()
    {
        var sim = _live.Sim; var me = _live.Me;
        if (_live.AtBreak || sim.Period >= 1)
        {
            int per = sim.Period, from = (per - 1) * 1200, to = per * 1200;
            var pe = sim.Result.Events.Where(e => e.T >= from && e.T < to).ToList();
            string Line(string team) => $"броски {pe.Count(e => e.Team == team && (e.Kind == "shot" || e.Kind == "goal"))}, xG {pe.Where(e => e.Team == team && e.Kind is "shot" or "goal").Sum(e => e.Xg):0.0}, удаления {pe.Count(e => e.Team == team && e.Kind == "pen")}, голы с хода {pe.Count(e => e.Team == team && e.Kind == "goal" && e.R == 1)}";
            _coachBody.AddChild(Ui.V(2, Ui.L(per <= 3 ? $"{per}-й период" : "Овертайм", 14, Ui.Text, Ui.Cond),
                Ui.H(6, new TeamBadge(Away, 16), Ui.L(Line(_g.Away), 12, Ui.Muted)),
                Ui.H(6, new TeamBadge(Home, 16), Ui.L(Line(_g.Home), 12, Ui.Muted))));
        }
        var items = LiveAdvice.For(C, _live);
        var adv = Ui.V(6);
        if (items.Count == 0) adv.AddChild(Ui.L("Помощник: менять ничего не нужно, всё идёт по плану.", 13, Ui.Text));
        foreach (var it in items)
            adv.AddChild(Ui.H(8, Ui.Icon("lightbulb", 14, Ui.Gold),
                Ui.Expand(Ui.V(0, Ui.L(it.Title, 13, Ui.Text, Ui.Semi), Ui.L(it.Why, 11, Ui.Muted).Also(Wrap))),
                Ui.B("Принять", () => Act(it.Apply), false, 11)));
        _coachBody.AddChild(Widget.Make($"{Staff.Assistant(C)?.Name ?? "Помощник"} советует", adv));
        // указания с бровки
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6); flow.AddThemeConstantOverride("v_separation", 6);
        foreach (var sh in Shouts.All)
        {
            bool said = sh.Talk && _live.Talks.Contains(sh.Key + ":" + sim.Period);
            var b = Ui.B(sh.Name, () => Act(() => _shoutRes = $"«{sh.Name}» — {Shouts.Apply(C, _live, sh.Key)}"), false, 12);
            b.TooltipText = Cap(sh.What) + (sh.Talk ? " Один раз за период." : "");
            b.Disabled = said;
            if (sh.Talk) b.AddThemeColorOverride("font_color", Ui.Gold);
            flow.AddChild(b);
        }
        var shouts = Ui.V(6, flow);
        if (_shoutRes != null) shouts.AddChild(Ui.L(_shoutRes, 12, Ui.Text, Ui.Semi));
        shouts.AddChild(Ui.L($"Настроение раздевалки: {Morale.Word(Morale.Team(C, me.Team.Abbr)).ToLowerInvariant()}. Команда в духе отвечает на требования, в унынии — сникает.", 11, Ui.Dim).Also(Wrap));
        _coachBody.AddChild(Widget.Make("Указания с бровки", shouts));
        // темп и вратарь
        var tseg = new Segmented(new[] { "Закрытый", "Обычный", "Открытый" }, me.Tempo + 1, k => Act(() =>
        {
            me.Tempo = k - 1; sim.Note(_live.UserHome, k == 0 ? "закрываем игру" : k == 2 ? "открываем игру" : "обычный темп");
            Tactics.Prepare(C, sim.HomeSide, sim.AwaySide); sim.Apply();
        }));
        var gk = Ui.V(6, Ui.H(8, Ui.L("Темп", 12, Ui.Muted), tseg));
        int backup = me.Goalie == me.Lineup.G1 ? me.Lineup.G2 : me.Lineup.G1;
        var (ga, sa) = sim.GoalieLine(_live.UserHome);
        gk.AddChild(Ui.L($"В воротах {C.Short(sim.GoalieOn(_live.UserHome))}: пропустил {ga} из {sa}", 12, Ui.Text));
        if (backup != 0 && sim.GoalieOn(_live.UserHome) == sim.Result.GoalieStart(_live.UserHome))
            gk.AddChild(Ui.B($"Сменить вратаря: {C.Short(backup)}", () => Act(() => { me.Goalie = backup; sim.Note(_live.UserHome, "в воротах " + C.Short(backup)); sim.Apply(); }), false, 11).Also(b => b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin));
        if (sim.ChallengeFor(_live.UserHome) >= 0)
        {
            var (yes, text) = Shouts.VideoRead(C, _live);
            gk.AddChild(Ui.H(8, Ui.Icon("video", 14, Ui.Gold), Ui.Expand(Ui.L(text, 12, yes ? Ui.Text : Ui.Muted).Also(Wrap)), Ui.B("Оспорить гол", Challenge, false, 11)));
        }
        if (!sim.TimeoutUsed(_live.UserHome) && sim.InPeriod)
            gk.AddChild(Ui.B("Тайм-аут (один за матч)", () => Act(() => sim.Timeout(_live.UserHome)), false, 11).Also(b => b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin));
        _coachBody.AddChild(Widget.Make("Темп, вратарь, тайм-аут", gk));
    }

    Color EnergyColor(double e) => e >= 0.8 ? Ui.Win : e >= ShiftEngine.FAT_AT ? Ui.Text : e >= 0.5 ? Ui.Otl : Ui.Loss;

    void TabLines()
    {
        var sim = _live.Sim; var l = _live.Me.Lineup; bool home = _live.UserHome;
        _coachBody.AddChild(Ui.L(_pick == 0 ? "Нажмите на игрока, потом на того, с кем его поменять (нападающих — с нападающими, защитников — с защитниками)."
            : $"{C.Short(_pick)}: с кем поменять?", 12, _pick == 0 ? Ui.Dim : Ui.Gold));
        var g = new GridContainer { Columns = 5 };
        g.AddThemeConstantOverride("h_separation", 10); g.AddThemeConstantOverride("v_separation", 6);
        Control Cell(int id)
        {
            if (id == 0) return Ui.L("—", 12, Ui.Dim);
            double e = sim.Energy(home, id);
            var b = Ui.B(C.Short(id), () =>
            {
                if (_pick == 0) { _pick = id; FillCoach(); return; }
                if (_pick == id) { _pick = 0; FillCoach(); return; }
                int a = _pick; _pick = 0;
                Act(() => { if (sim.SwapLines(home, a, id)) { sim.Note(home, $"{C.Short(a)} и {C.Short(id)} меняются местами"); sim.Apply(); } });
            }, false, 12);
            if (_pick == id) b.AddThemeColorOverride("font_color", Ui.Gold);
            b.CustomMinimumSize = new Vector2(120, 0);
            return Ui.V(2, b, Ui.Bar(e, EnergyColor(e), 120, 4));
        }
        for (int i = 0; i < l.F.Count; i++)
        {
            g.AddChild(Ui.L($"{i + 1}-я тройка", 13, Ui.Muted, Ui.Cond));
            foreach (var id in l.F[i]) g.AddChild(Cell(id));
            g.AddChild(Ui.L($"{(i < l.FMin.Length ? l.FMin[i] : 0):0} мин", 11, Ui.Dim));
        }
        for (int i = 0; i < l.D.Count; i++)
        {
            g.AddChild(Ui.L($"{i + 1}-я пара", 13, Ui.Muted, Ui.Cond));
            foreach (var id in l.D[i]) g.AddChild(Cell(id));
            g.AddChild(new Control());
            g.AddChild(Ui.L($"{(i < l.DMin.Length ? l.DMin[i] : 0):0} мин", 11, Ui.Dim));
        }
        _coachBody.AddChild(g);
        var presets = new (string name, double[] f, double[] d, string what)[]
        {
            ("Ровно", new double[] { 17, 16, 15, 12 }, new double[] { 21, 20, 19 }, "все звенья свежее"),
            ("Стандарт", new double[] { 20, 17, 14, 9 }, new double[] { 24, 20, 16 }, "как обычно"),
            ("На лидеров", new double[] { 23, 19, 12, 6 }, new double[] { 26, 21, 13 }, "лучшие играют больше, но устают"),
            ("Три тройки", new double[] { 24, 21, 15, 0 }, new double[] { 26, 21, 13 }, "четвёртая сидит — концовка, когда надо отыгрываться"),
        };
        int cur = Array.FindIndex(presets, p => p.f.SequenceEqual(l.FMin) && p.d.SequenceEqual(l.DMin));
        _coachBody.AddChild(Ui.V(4, Ui.L("Игровое время до конца матча", 14, Ui.Text, Ui.Cond),
            new Segmented(presets.Select(p => p.name).ToArray(), cur, k => Act(() => { sim.SetMinutes(home, presets[k].f, presets[k].d); sim.Note(home, "игровое время: " + presets[k].name.ToLowerInvariant()); })),
            Ui.L(cur >= 0 ? Cap(presets[cur].what) : "Своё распределение минут.", 11, Ui.Dim)));
    }

    void TabState()
    {
        var sim = _live.Sim; var me = _live.Me; bool home = _live.UserHome;
        var g = new GridContainer { Columns = 7 };
        g.AddThemeConstantOverride("h_separation", 14); g.AddThemeConstantOverride("v_separation", 5);
        foreach (var h in new[] { "Игрок", "Место", "Свежесть", "Время", "+/−", "Броски", "Настрой" }) g.AddChild(Ui.L(h, 11, Ui.Dim));
        var l = me.Lineup;
        string Slot(int id)
        {
            for (int i = 0; i < l.F.Count; i++) if (l.F[i].Contains(id)) return $"{i + 1}-я тройка";
            for (int i = 0; i < l.D.Count; i++) if (l.D[i].Contains(id)) return $"{i + 1}-я пара";
            return "";
        }
        foreach (var id in l.Skaters.Where(x => x != 0))
        {
            var b = sim.BoxOf(home, id);
            if (b == null) continue;
            double e = sim.Energy(home, id);
            int toi = b.Toi;
            g.AddChild(Links.Player(C, id, C.Short(id), 13, Ui.Text, Ui.Semi));
            g.AddChild(Ui.L(Slot(id), 11, Ui.Muted));
            g.AddChild(Ui.H(6, Ui.Bar(e, EnergyColor(e), 80, 6).Also(x => x.SizeFlagsVertical = SizeFlags.ShrinkCenter), Ui.L($"{100 * e:0}%", 11, EnergyColor(e))));
            g.AddChild(Ui.L($"{toi / 60}:{toi % 60:00}", 12, Ui.Text));
            g.AddChild(Ui.L(b.Pm > 0 ? $"+{b.Pm}" : b.Pm.ToString(), 12, b.Pm > 0 ? Ui.Win : b.Pm < 0 ? Ui.Loss : Ui.Muted));
            g.AddChild(Ui.L($"{b.S}", 12, Ui.Text));
            float m = C.Players.TryGetValue(id, out var st) ? st.Morale : 60;
            g.AddChild(Ui.L(Morale.Word(m), 11, m >= 67 ? Ui.Win : m >= 47 ? Ui.Muted : Ui.Loss));
        }
        _coachBody.AddChild(g);
        _coachBody.AddChild(Ui.L("Свежесть ниже 70% — игрок слабее, и чем ниже, тем сильнее, пока не отдохнёт. Тайм-аут вернёт силы всем.", 11, Ui.Dim).Also(Wrap));
    }

    void TabTactics()
    {
        var sim = _live.Sim; var me = _live.Me;
        var cur = (int[])(me.Plan ?? me.Lineup.Sys ?? new int[Tactics.Levers.Length]).Clone();
        if (cur.Length < Tactics.Levers.Length) Array.Resize(ref cur, Tactics.Levers.Length);
        var v = Ui.V(6);
        foreach (var (phase, levers) in Tactics.Phases)
        {
            v.AddChild(Ui.L(phase, 12, Ui.Gold, Ui.Semi));
            foreach (var i in levers)
            {
                int li = i; var lv = Tactics.Levers[i];
                v.AddChild(Ui.H(10, Ui.L(lv.Name, 13, Ui.Text, Ui.Semi).Also(x => x.CustomMinimumSize = new Vector2(130, 0)),
                    new Segmented(new[] { lv.Minus.Short, lv.MidShort, lv.Plus.Short }, cur[i] + 1, k => Act(() =>
                    {
                        var s2 = (int[])cur.Clone(); s2[li] = k - 1; me.Plan = s2;
                        sim.Note(_live.UserHome, $"{lv.Name.ToLowerInvariant()} — {Tactics.PoleName(lv, k - 1).ToLowerInvariant()}");
                        Tactics.Prepare(C, sim.HomeSide, sim.AwaySide); sim.Apply();
                    }))));
            }
        }
        _coachBody.AddChild(v);
        var notes = me.Sys?.Notes ?? new List<string>();
        var nb = Ui.V(4);
        foreach (var n in notes.Distinct()) nb.AddChild(Ui.H(6, Ui.Icon("triangle-alert", 13, Ui.Otl), Ui.Expand(Ui.L(Cap(n), 12, Ui.Text).Also(Wrap))));
        if (notes.Count == 0) nb.AddChild(Ui.L("Особых контрприёмов против нашей системы помощник не видит.", 12, Ui.Muted));
        _coachBody.AddChild(Widget.Make("Против их нынешней игры", nb));
        _coachBody.AddChild(Ui.L("Новая для команды схема по ходу матча работает хуже выученной — это учтено.", 11, Ui.Dim).Also(Wrap));
    }

    void TabTargets()
    {
        var sim = _live.Sim; var me = _live.Me; var op = _live.Opp; bool home = _live.UserHome;
        me.Targets ??= new();
        var v = Ui.V(8);
        foreach (var (id, kind) in me.Targets.ToList())
        {
            int tid = id;
            var kob = new OptionButton { FocusMode = FocusModeEnum.None };
            for (int k = 0; k < Targeting.Kinds.Length; k++) { kob.AddItem(Targeting.Kinds[k].Name, k); if (Targeting.Kinds[k].Key == kind) kob.Selected = k; }
            kob.ItemSelected += k => Act(() => { me.Targets[tid] = Targeting.Kinds[(int)k].Key; sim.Note(home, $"{Targeting.Kinds[(int)k].Name.ToLowerInvariant()}: {C.Short(tid)}"); sim.Apply(); });
            var ob = sim.BoxOf(!home, id);
            v.AddChild(Ui.H(10, Links.Player(C, id, C.Short(id), 13, Ui.Text, Ui.Semi).Also(x => x.CustomMinimumSize = new Vector2(130, 0)), kob,
                Ui.L(ob == null ? "" : $"бросков {ob.S}, потерь {ob.Gv}, {(ob.Pm > 0 ? "+" : "")}{ob.Pm}", 11, Ui.Muted), Ui.Spacer(),
                Ui.B("Снять", () => Act(() => { me.Targets.Remove(tid); sim.Note(home, $"{C.Short(tid)} — без задания"); sim.Apply(); }), false, 11)));
        }
        if (me.Targets.Count < MatchPlan.MAX_TARGETS)
        {
            var ol = op.Lineup;
            var pool = ol.F.SelectMany(x => x).Concat(ol.D.SelectMany(x => x)).Where(id => id != 0 && !me.Targets.ContainsKey(id)).Distinct().ToList();
            var pob = new OptionButton { FocusMode = FocusModeEnum.None };
            pob.AddItem("Добавить задание: выберите игрока соперника", 0);
            for (int k = 0; k < pool.Count; k++)
            {
                var b = sim.BoxOf(!home, pool[k]);
                pob.AddItem($"{C.Short(pool[k])} ({Ui.PosRu(C.Data.PlayerById[pool[k]].Pos)})" + (b == null ? "" : $" — бр. {b.S}, пот. {b.Gv}"), k + 1);
            }
            pob.ItemSelected += idx => { if (idx > 0) Act(() => { me.Targets[pool[(int)idx - 1]] = "shadow"; sim.Note(home, "опекаем " + C.Short(pool[(int)idx - 1])); sim.Apply(); }); };
            v.AddChild(pob);
        }
        else v.AddChild(Ui.L("Три задания — больше команда не удержит в голове.", 11, Ui.Dim));
        var help = Ui.V(2);
        foreach (var k in Targeting.Kinds) help.AddChild(Ui.L($"{k.Name}: {k.What}; {k.Cost}.", 11, Ui.Muted).Also(Wrap));
        help.AddChild(Ui.L("Внимание делится: чем больше заданий, тем слабее каждое.", 11, Ui.Dim));
        v.AddChild(help);
        _coachBody.AddChild(v);
    }

    void UpdateIce()
    {
        if (_iceBox == null || _live == null) return;
        Ui.Clear(_iceBox);
        var sim = _live.Sim;
        foreach (bool home in new[] { false, true })
        {
            var t = home ? Home : Away;
            var ids = sim.OnIce(home);
            var row = Ui.H(4, new TeamBadge(t, 16));
            foreach (var id in ids)
            {
                double e = sim.Energy(home, id);
                var c = e >= 0.7 ? Ui.Text : e >= 0.45 ? Ui.Otl : Ui.Loss;
                row.AddChild(Ui.L(C.Short(id), 11, c));
            }
            _iceBox.AddChild(row);
        }
        string st = sim.Strength;
        if (st != "" && st != "5×5") _iceBox.AddChild(Ui.L(st, 12, Ui.Gold, Ui.Semi));
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
        var pastAll = AllEvents.Where(e => e.T <= t).ToList();   // со вбрасываниями и силовыми
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

    double _uiTick;
    int _pendingAdvice, _adviceAt;
    public override void _Process(double delta)
    {
        if (_live != null) { ProcessLive(delta); return; }
        if (_clock < 0 || _g.Result == null || _ev == null) return;
        var r = _g.Result;
        if (_introLeft > 0) { _introLeft -= delta; return; }
        _clock += delta * _speed;
        while (_shown < _ev.Count && _ev[_shown].T <= _clock) { Celebrate(_ev[_shown]); AddFeed(_ev[_shown++]); }
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

    /// <summary>Трансляция: движок идёт вместе с часами; перерыв — пауза с советами помощника.</summary>
    void ProcessLive(double delta)
    {
        if (_clock < 0 || _paused || _live.AtBreak) return;
        if (_introLeft > 0) { _introLeft -= delta; return; }
        var sim = _live.Sim;
        if (sim.Finished)
        {
            C.FinishLive(_live);
            _live = null; _clock = -1;
            ShowResult();
            return;
        }
        _clock += delta * _speed;
        sim.RunTo((int)_clock);
        if (sim.Clock + 1 < (int)_clock && !sim.InPeriod) _clock = sim.Clock;   // перерыв или конец: часы стоят
        // новые события
        var all = sim.Result.Events;
        bool added = false;
        for (; _evIdx < all.Count; _evIdx++)
        {
            var e = all[_evIdx];
            if (e.Kind is "fo" or "hit" or "tk" or "gv") continue;
            _ev.Add(e); added = true;
        }
        while (_shown < _ev.Count) { Celebrate(_ev[_shown]); AddFeed(_ev[_shown++]); }
        _scoreL.Text = $"{sim.AwayGoals}:{sim.HomeGoals}";
        int t = sim.Clock, per = sim.Period, inPer = t - (per - 1) * 1200;
        _clockL.Text = !sim.InPeriod && _live.AtBreak ? $"Перерыв после {(per <= 3 ? per + "-го периода" : "овертайма")}" : per <= 3 ? $"{per}-й период · {inPer / 60:00}:{inPer % 60:00}" : $"Овертайм · {inPer / 60:00}:{inPer % 60:00}";
        if (added) { _xg?.SetEvents(all); _map?.SetEvents(all); }
        _xg?.SetClock(t); _map?.SetClock(t);
        _uiTick += delta;
        if (_uiTick > 0.25 || added) { _uiTick = 0; UpdateStats(t); UpdateIce(); }
        // раз в две игровые минуты помощник смотрит, есть ли что сказать (решения — только на паузе)
        if (t - _adviceAt >= 120 && sim.InPeriod)
        {
            _adviceAt = t;
            int n = LiveAdvice.For(C, _live).Count;
            if (n != _pendingAdvice) { _pendingAdvice = n; FillAdvice(false); }
        }
        if (added) UpdateControls();
        if (_live.AtBreak)
        {
            _paused = false;
            UpdateStats(t);
            AddFeed(new MatchEvent { T = t - 1, Kind = "period", Note = $"Перерыв. Счёт {Away.City} {sim.AwayGoals}:{sim.HomeGoals} {Home.City}" });
            _tab = "advice"; _shoutRes = null;
            ShowCoach(true);
            FillAdvice(true);
            UpdateControls();
        }
        if (sim.Finished) { _clock = sim.Clock; }
    }

    static bool IsKey(MatchEvent e) => e.Kind is "goal" or "pen" or "start" or "period" or "coach" or "goalie";

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
                var gl = Res.Goals.FirstOrDefault(x => x.Scorer == e.P1 && (x.Period - 1) * 1200 + x.Sec == e.T);
                var assists = gl == null ? new List<string>() : new[] { gl.A1, gl.A2 }.Where(x => x != 0).Select(S).ToList();
                string tag = e.Note switch { "pp" => " в большинстве", "sh" => " в меньшинстве", "en" => " в пустые ворота", "ev6" => " с шестым полевым!", _ => "" };
                return ($"ГОЛ{tag}! {N(e.P1)}{(assists.Count > 0 ? $" ({string.Join(", ", assists)})" : " (без передач)")}" + (e.Xg > 0 ? $"  · xG {e.Xg:0.00}" : ""), Colors.White, Ui.Bold, "trophy");
            case "shot":
                string[] sv = { "отражает", "ловит шайбу", "отбивает щитком", "спасает", "забирает шайбу", "в отличной позиции — сейв", "отбивает блином" };
                string zone = e.Z switch { 0 => " у ворот", 1 => " со средней дистанции", 2 => " издали", _ => "" };
                return ($"{S(e.P1)}: {e.Note}{zone} — {S(e.P2)} {sv[h]}" + (e.Xg > 0 ? $"  · xG {e.Xg:0.00}" : ""), e.Xg >= 0.15 ? Colors.White : Ui.Text, e.Xg >= 0.15 ? Ui.Semi : null, "");
            case "miss": return ($"{S(e.P1)} бросает — {e.Note}", Ui.Muted, null, "");
            case "block": return ($"{S(e.P1)} бросает — блокирует {S(e.P2)}", Ui.Muted, null, "");
            case "pen": int pm = e.P2 is 4 or 5 ? e.P2 : 2; return ($"Удаление: {N(e.P1)}, {pm} {(pm == 5 ? "минут" : "минуты")} ({e.Note})", Ui.Otl, Ui.Semi, "");
            case "coach": return ($"Тренер «{C.Team(e.Team).City}»: {e.Note}", Ui.Gold, Ui.Semi, "clipboard-list");
            case "nogoal": return ($"Гол {N(e.P1)} отменён после видеопросмотра", Ui.Muted, Ui.Semi, "video");
            case "goalie": return ($"Замена вратаря у «{C.Team(e.Team).City}»: вместо {S(e.P2)} выходит {N(e.P1)}", Ui.Gold, Ui.Semi, "");
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
        // финальное табло и три звезды — во всю ширину, как графика трансляции после матча
        body.AddChild(FinalBoard.Hero(C, _g));
        if (r.Ratings.Count > 0 || r.Goals.Count > 0) body.AddChild(FinalBoard.Stars(C, r));
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
        row.AddChild(Ui.Scroll(left).Also(s => { s.CustomMinimumSize = new Vector2(310, 0); s.SizeFlagsHorizontal = SizeFlags.Fill; }));

        var center = Ui.Expand(Ui.V(12), true, true);
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
        if ((_g.Home == C.UserTeam || _g.Away == C.UserTeam) && r.Events.Count > 0) center.AddChild(Widget.Make("Разбор", Review(r)));
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
        row.AddChild(Ui.Scroll(right).Also(s => { s.CustomMinimumSize = new Vector2(450, 0); s.SizeFlagsHorizontal = SizeFlags.Fill; }));
    }

    /// <summary>
    /// Разбор после матча для тренера: моменты против голов (реализация и вратари), что дали решения по ходу матча,
    /// спецбригады. Только факты протокола — без оценок «правильно/неправильно».
    /// </summary>
    Control Review(MatchResult r)
    {
        var v = Ui.V(6);
        string me = C.UserTeam, op = _g.Home == me ? _g.Away : _g.Home;
        bool home = _g.Home == me;
        double xf = r.Events.Where(e => e.Team == me && e.Kind is "shot" or "goal").Sum(e => e.Xg), xa = r.Events.Where(e => e.Team == op && e.Kind is "shot" or "goal").Sum(e => e.Xg);
        int gf = home ? r.HomeGoals : r.AwayGoals, ga = home ? r.AwayGoals : r.HomeGoals;
        void Line(string icon, Color c, string text) => v.AddChild(Ui.H(8, Ui.Icon(icon, 14, c), Ui.Expand(Ui.L(text, 12, Ui.Text).Also(Wrap))));
        string verdict = Math.Abs(xf - xa) < 0.4 ? "по моментам матч равный" : xf > xa ? "по моментам мы были лучше" : "по моментам соперник был лучше";
        Line("activity", Ui.Muted, $"Ожидаемые голы {xf:0.0} — {xa:0.0}: {verdict}; счёт {gf}:{ga}.");
        // вратари: сейвы сверх ожидаемых
        foreach (var (id, mine) in new[] { (home ? r.HomeGoalie : r.AwayGoalie, true), (home ? r.AwayGoalie : r.HomeGoalie, false) })
            if (r.Box.TryGetValue(id, out var b) && b.Sa > 0)
            {
                double gsax = b.Xga - b.Ga;
                if (Math.Abs(gsax) >= 0.8)
                    Line("shield", mine == gsax > 0 ? Ui.Win : Ui.Loss, $"{C.Short(id)}: {b.Sa - b.Ga} из {b.Sa}, {(gsax > 0 ? "спас" : "пропустил")} на {Math.Abs(gsax):0.0} гола {(gsax > 0 ? "больше" : "больше")} ожидаемого.");
            }
        // реализация
        if (gf - xf >= 1.2) Line("crosshair", Ui.Win, $"Реализация выше моментов: забили {gf} при {xf:0.0} ожидаемых.");
        else if (xf - gf >= 1.2) Line("crosshair", Ui.Loss, $"Не реализовали: {gf} при {xf:0.0} ожидаемых.");
        // решения по ходу матча и что было после
        var dec = r.Events.Where(e => e.Kind is "coach" or "goalie" && e.Team == me).OrderBy(e => e.T).ToList();
        foreach (var d in dec)
        {
            int t = d.T;
            int after = r.Goals.Count(g => g.Team == me && g.Kind != "so" && (g.Period - 1) * 1200 + g.Sec >= t), afterA = r.Goals.Count(g => g.Team == op && g.Kind != "so" && (g.Period - 1) * 1200 + g.Sec >= t);
            string what = d.Kind == "goalie" ? $"замена вратаря: {C.Short(d.P1)}" : d.Note;
            Line("clipboard-list", Ui.Gold, $"{(t / 1200 < 3 ? $"После {t / 1200}-го периода" : "Перед овертаймом")} — {what}. Дальше {after}:{afterA}.");
        }
        if (dec.Count == 0) Line("clipboard-list", Ui.Muted, "По ходу матча система и темп не менялись.");
        // спецбригады
        int ppf = r.Goals.Count(g => g.Team == me && g.Kind == "pp"), ppa = r.Goals.Count(g => g.Team == op && g.Kind == "pp");
        int penf = r.Events.Count(e => e.Kind == "pen" && e.Team == op), pena = r.Events.Count(e => e.Kind == "pen" && e.Team == me);
        Line("zap", Ui.Muted, $"Большинство {ppf} из {penf}, меньшинство — пропустили {ppa} из {pena}.");
        return v;
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
        (_ca, _ch) = MatchColors.Of(c, g);
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
