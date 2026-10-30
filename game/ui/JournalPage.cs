using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// «Что сработало»: журнал тренера. По каждому рычагу — матчи с каждым вариантом, очки, моменты и то, что схема меняет
/// в статистике, против остальных матчей. Только факты своих матчей; выводы — тренеру (помощник предупреждает, когда матчей мало).
/// </summary>
public partial class JournalPage : Control
{
    Career C => P.C;
    int _sel;

    public JournalPage(string arg = null)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        _sel = int.TryParse(arg, out var k) && k >= 0 && k < Tactics.Levers.Length ? k : 0;
    }

    public override void _Ready() => Build();

    void Build()
    {
        foreach (var ch in GetChildren()) { RemoveChild(ch); ch.QueueFree(); }
        var row = Ui.H(14);
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 16; row.OffsetRight = -16; row.OffsetTop = 12; row.OffsetBottom = -12;
        AddChild(row);
        var entries = Journal.Of(C);
        row.AddChild(LeverList(entries));
        row.AddChild(Ui.Expand(Ui.Scroll(Detail(entries)), true, true));
        row.AddChild(Recent(entries));
    }

    // ------------------------------------------------------------------ слева: рычаги
    Control LeverList(List<JournalEntry> entries)
    {
        var v = Ui.V(8);
        v.AddChild(Ui.V(0, Ui.L("Что сработало", 22, Ui.Text, Ui.CondBold),
            Ui.L($"Наши матчи: {entries.Count}. Сравнение — с остальными нашими матчами.", 11, Ui.Dim).Also(Wrap)));
        foreach (var (phase, levers) in Tactics.Phases)
        {
            v.AddChild(Ui.L(phase, 14, Ui.Gold, Ui.Cond));
            foreach (var i in levers)
            {
                var lv = Tactics.Levers[i];
                int used = entries.Select(e => e.SysOf(i)).Distinct().Count();
                bool sel = i == _sel;
                var card = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
                card.AddThemeStyleboxOverride("panel", Ui.Box(sel ? Ui.Panel2 : new Color(0, 0, 0, 0.18f), 3, sel ? Ui.Blue : Ui.Line, 8));
                int li = i;
                card.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { _sel = li; CallDeferred(nameof(Build)); } };
                var h = Ui.H(8, Ui.Expand(Ui.L(lv.Name, 15, Ui.Text, Ui.Cond)),
                    Ui.L(used <= 1 ? "один вариант" : $"{used} варианта", 11, used <= 1 ? Ui.Dim : Ui.Muted));
                h.MouseFilter = MouseFilterEnum.Pass;
                card.AddChild(h);
                v.AddChild(card);
            }
        }
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(270, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 12));
        panel.AddChild(Ui.Scroll(v));
        return panel;
    }

    static void Wrap(Label l) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(180, 0); }

    // ------------------------------------------------------------------ центр: варианты рычага
    Control Detail(List<JournalEntry> entries)
    {
        var lv = Tactics.Levers[_sel];
        var v = Ui.V(12);
        v.AddChild(Ui.H(10, Ui.L(lv.Name, 26, Ui.Text, Ui.CondBold), Ui.Spacer(),
            Ui.B("Настроить", () => Main.I.ShowHub("tactics:system", _sel.ToString()), false, 12)));
        if (entries.Count == 0)
        {
            v.AddChild(Ui.L("Сыграйте несколько матчей — журнал заполняется после каждого нашего матча. Меняйте схемы и смотрите, что они меняют в игре.", 14, Ui.Muted).Also(Wrap));
            return v;
        }
        var rows = Journal.Rows(C, _sel);
        foreach (var r in rows)
        {
            var box = Ui.V(6);
            bool cur = C.Lineups[C.UserTeam].SysOf(_sel) == r.V;
            box.AddChild(Ui.H(10,
                Ui.L(Tactics.PoleName(lv, r.V), 18, r.V == 0 ? Ui.Text : Ui.Gold, Ui.CondBold),
                cur ? Ui.L("сейчас", 11, Ui.Blue, Ui.Semi) : new Control(),
                Ui.Spacer(),
                Ui.L($"{r.N} {Ui.Plural(r.N, "матч", "матча", "матчей")}", 13, Ui.Muted)));
            var head = Ui.H(22,
                Stat("Очки", $"{r.Pts} из {r.Max}", r.Max == 0 ? Ui.Text : r.Pts >= 0.55 * r.Max ? Ui.Win : r.Pts <= 0.45 * r.Max ? Ui.Loss : Ui.Text),
                Stat("Разница шайб за матч", Ui.Signed(r.GDiff, "0.0"), Col(r.GDiff)),
                Stat("Разница моментов (xG) за матч", Ui.Signed(r.XgDiff, "0.00"), Col(r.XgDiff)));
            box.AddChild(head);
            var g = new GridContainer { Columns = 3 };
            g.AddThemeConstantOverride("h_separation", 16); g.AddThemeConstantOverride("v_separation", 4);
            g.AddChild(Ui.L("", 11)); g.AddChild(Ui.L("с этим вариантом", 11, Ui.Dim)); g.AddChild(Ui.L("в остальных", 11, Ui.Dim));
            foreach (var line in r.Sign)
            {
                g.AddChild(Ui.L(line.M.Name, 12, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(260, 0)));
                g.AddChild(Ui.L(Fmt(line.Val, line.M), 14, Ui.Text, Ui.Semi));
                g.AddChild(Ui.L(Fmt(line.Rest, line.M), 13, Ui.Muted));
            }
            box.AddChild(g);
            v.AddChild(Card(box, cur));
        }
        var asst = Staff.Assistant(C);
        v.AddChild(Card(Ui.V(4,
            Ui.H(8, Ui.Icon("user", 14, Ui.Gold), Ui.L($"{asst?.Name ?? "Помощник"} читает журнал", 14, Ui.Text, Ui.Semi)),
            Ui.L(Journal.Read(C, _sel), 13, Ui.Text).Also(Wrap),
            Ui.L("Моменты зависят и от соперников, и от формы, и от удачи: за один матч разница xG гуляет больше чем на гол. Схему видно в её статистике сразу, а окупается ли она — за десятки матчей.", 11, Ui.Dim).Also(Wrap)), false));
        return v;
    }

    static string Fmt(double x, Journal.Metric m) => double.IsNaN(x) ? "—" : m.Share ? $"{100 * x:0}%" : x.ToString(m.Fmt);
    static Color Col(double x) => Math.Abs(x) < 0.05 ? Ui.Text : x > 0 ? Ui.Win : Ui.Loss;

    static Control Stat(string cap, string val, Color c) => Ui.V(-2, Ui.L(cap, 11, Ui.Muted), Ui.L(val, 22, c, Ui.CondBold));

    static Control Card(Control content, bool hi)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Ui.Box(hi ? Ui.Panel2 : Ui.Panel, 4, hi ? Ui.Blue : Ui.Line, 14));
        p.AddChild(content);
        return p;
    }

    // ------------------------------------------------------------------ справа: последние матчи
    Control Recent(List<JournalEntry> entries)
    {
        var v = Ui.V(6);
        v.AddChild(Ui.L("Последние матчи", 16, Ui.Text, Ui.Cond));
        foreach (var e in entries.OrderByDescending(x => x.Date).Take(14))
        {
            var res = e.GF > e.GA ? Ui.Win : e.End != "REG" ? Ui.Otl : Ui.Loss;
            var sys = string.Join(" · ", Tactics.Phases.SelectMany(p => p.levers).Where(i => e.SysOf(i) != 0).Select(i => Tactics.PoleShort(Tactics.Levers[i], e.SysOf(i))));
            v.AddChild(Ui.V(0,
                Ui.H(6, Ui.L(Ui.Date(e.Date), 11, Ui.Dim).Also(l => l.CustomMinimumSize = new Vector2(70, 0)), new TeamBadge(C.Team(e.Opp), 16),
                    Ui.L((e.Home ? "" : "@ ") + C.Team(e.Opp).City, 12, Ui.Text), Ui.Spacer(),
                    Ui.L($"{e.GF}:{e.GA}{(e.End == "OT" ? " ОТ" : e.End == "SO" ? " Б" : "")}", 13, res, Ui.CondBold)),
                Ui.L(sys.Length == 0 ? "обычная система" : sys, 10, Ui.Muted).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(250, 0); })));
        }
        if (entries.Count == 0) v.AddChild(Ui.L("Пока пусто.", 12, Ui.Muted));
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 12));
        panel.AddChild(Ui.Scroll(v));
        return panel;
    }
}
