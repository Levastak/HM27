using System;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// «Вариант помощника»: его звенья против текущих — кто куда переходит, что это даёт по способностям,
/// сколько стоит сыгранности и кого обидит. Помощник предлагает, решает тренер.
/// </summary>
public static class AssistantReview
{
    public static void Open(Control host, Career c, string abbr, Action<Lineup> accept)
    {
        var rv = Coach.Propose(c, abbr);
        var cur = Coach.Baseline(c, abbr);
        var pop = new PopupPanel();
        pop.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Side, 3, Ui.Blue, 18));
        var v = Ui.V(14);
        v.CustomMinimumSize = new Vector2(760, 0);
        pop.AddChild(v);

        v.AddChild(Ui.V(2,
            Ui.L("Вариант помощника", 26, Ui.Text, Ui.CondBold),
            Note(rv.By == null ? "Лучшие по способностям и свежести." :
                $"{rv.By.Name}: оценка игроков — {Staff.Level(rv.By.Judge)}, {Staff.StyleText(rv.By)}. Это его взгляд на силу игроков — он может ошибаться. " +
                "Сыгранность, настроение и минуты молодых он не учитывает — решаете вы.", 12)));

        double ability = rv.Gf - rv.Ga, fam = rv.FamGf - rv.FamGa, net = ability + fam;
        bool same = rv.Changes.Count == 0;
        var tiles = Ui.H(10,
            Tile("По его оценке", ability, "гола за матч"),
            Tile("Сыгранность сейчас", fam, rv.Moved + rv.Changes.Count(x => x.StartsWith("В состав")) > 0 ? "новые звенья сыграются за 15–20 матчей" : "звенья те же"),
            Tile("Итого в ближайших матчах", net, net >= 0 ? "выгоднее текущего" : "пока хуже текущего", true));
        v.AddChild(tiles);

        if (same)
            v.AddChild(Ui.L("Помощник не нашёл, что поменять: текущие звенья он считает лучшими.", 14, Ui.Muted));
        else
            v.AddChild(Board(c, cur, rv.Lineup));

        var risks = Ui.V(4);
        foreach (var (id, t) in rv.Upset) risks.AddChild(Risk("triangle-alert", Ui.Otl, c.Short(id), t));
        foreach (var (id, t) in rv.Youth) risks.AddChild(Risk("user", Ui.Muted, c.Short(id), t));
        if (risks.GetChildCount() > 0) v.AddChild(Ui.V(6, Ui.L("Чего помощник не учёл", 15, Ui.Text, Ui.Cond), risks));

        var keep = Ui.B("Оставить как есть", () => pop.Hide(), false, 14);
        var ok = Ui.B("Принять вариант", () => { pop.Hide(); accept(rv.Lineup); }, true, 14);
        ok.Disabled = same;
        v.AddChild(Ui.H(10, Ui.Expand(new Control()), keep, ok));

        host.AddChild(pop);
        pop.PopupHide += () => pop.QueueFree();
        pop.PopupCentered(new Vector2I(800, 10));
    }

    static Label Note(string t, int size = 11) => Ui.L(t, size, Ui.Muted).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart);

    static Control Tile(string title, double v, string hint, bool main = false)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Ui.Box(main ? Ui.Panel2 : Ui.Panel, 3, main ? Ui.Gold : null, 12));
        var col = Math.Abs(v) < 0.005 ? Ui.Muted : v > 0 ? Ui.Win : Ui.Loss;
        p.AddChild(Ui.V(0,
            Ui.L(title, 12, Ui.Muted),
            Ui.L(Ui.Signed(v, "0.00"), 34, col, Ui.CondBlack),
            Ui.L(hint, 11, Ui.Dim)));
        return Ui.Expand(p);
    }

    static Control Risk(string icon, Color col, string who, string text) =>
        Ui.H(8, Ui.Icon(icon, 15, col), Ui.L(who, 13, Ui.Text, Ui.Semi), Ui.L(text, 13, Ui.Muted));

    /// <summary>Звенья до и после: перешедшие — синим, ушедшие — бледно; справа сыгранность нового звена.</summary>
    static Control Board(Career c, Lineup cur, Lineup nw)
    {
        var g = new GridContainer { Columns = 4 };
        g.AddThemeConstantOverride("h_separation", 18);
        g.AddThemeConstantOverride("v_separation", 6);
        g.AddChild(Ui.L("", 11)); g.AddChild(Ui.L("Сейчас", 11, Ui.Dim)); g.AddChild(Ui.L("Вариант помощника", 11, Ui.Dim)); g.AddChild(Ui.L("Сыгранность", 11, Ui.Dim));
        void Row(string name, int[] a, int[] b)
        {
            g.AddChild(Ui.L(name, 13, Ui.Muted, Ui.Cond));
            var left = Ui.H(6);
            foreach (var id in a) left.AddChild(Ui.L(c.Short(id), 13, b.Contains(id) ? Ui.Text : Ui.Dim));
            g.AddChild(left);
            var right = Ui.H(6);
            foreach (var id in b) right.AddChild(Ui.L(c.Short(id), 13, a.Contains(id) ? Ui.Text : Ui.Blue, a.Contains(id) ? null : Ui.Semi));
            g.AddChild(right);
            double f = c.UnitFam(b);
            g.AddChild(Ui.H(6, Ui.Bar(f, LinesView.FamColor(f), 70, 6), Ui.L($"{f * 100:0}%", 12, LinesView.FamColor(f), Ui.Cond)));
        }
        for (int i = 0; i < Math.Min(cur.F.Count, nw.F.Count); i++) Row($"{i + 1}-я тройка", cur.F[i], nw.F[i]);
        for (int i = 0; i < Math.Min(cur.D.Count, nw.D.Count); i++) Row($"{i + 1}-я пара", cur.D[i], nw.D[i]);
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 3, null, 14));
        p.AddChild(g);
        return p;
    }
}
