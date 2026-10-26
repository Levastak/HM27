using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Выбор клуба: 4 дивизиона по 8 клубов, справа — карточка выбранного.</summary>
public partial class ClubSelectScreen : Control
{
    PanelContainer _detail;
    string _selected = "MIN";
    readonly System.Collections.Generic.Dictionary<string, PanelContainer> _rows = new();

    public override void _Ready()
    {
        var d = Main.I.Data;
        var root = Ui.V(18);
        var m = Ui.Margin(root, 40, 28, 40, 28);
        m.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(m);

        var head = Ui.H(12);
        head.AddChild(Ui.B("←", Main.I.ShowMenu));
        var ht = Ui.V(2, Ui.L("Выберите клуб", 30, Ui.Text, Ui.Bold), Ui.L("Карьера начинается с сезона 2026-27: реальные составы и звенья, сила клубов — по модели NHL-STATS.RU", 14, Ui.Muted));
        head.AddChild(ht);
        root.AddChild(head);

        var body = Ui.Expand(Ui.H(20), true, true);
        root.AddChild(body);
        var grid = Ui.Expand(Ui.H(12), true, true);
        body.AddChild(grid);

        var power = d.Teams.OrderByDescending(t => t.Att - t.Dfn).Select((t, i) => (t.Abbr, i + 1)).ToDictionary(x => x.Abbr, x => x.Item2);
        foreach (var conf in d.Teams.Select(t => t.Conference).Distinct().OrderBy(x => x))
            foreach (var div in d.Teams.Where(t => t.Conference == conf).Select(t => t.Division).Distinct().OrderBy(x => x))
            {
                var col = Ui.Expand(Ui.V(6), true, true);
                col.AddChild(Ui.L(div.ToUpper(), 12, Ui.Muted, Ui.Semi));
                col.AddChild(Ui.L($"{conf} конференция", 11, Ui.Dim));
                foreach (var t in d.Teams.Where(t => t.Division == div).OrderBy(t => t.City))
                {
                    var row = ClubRow(t, power[t.Abbr]);
                    _rows[t.Abbr] = row;
                    col.AddChild(row);
                }
                grid.AddChild(col);
            }

        _detail = new PanelContainer { CustomMinimumSize = new Vector2(430, 0) };
        _detail.SizeFlagsVertical = SizeFlags.ExpandFill;
        body.AddChild(_detail);
        Select(_selected);
    }

    PanelContainer ClubRow(TeamData t, int rank)
    {
        var h = Ui.H(10);
        h.AddChild(new TeamBadge(t, 34));
        var names = Ui.Expand(Ui.V(0, Ui.L(t.City, 15, Ui.Text, Ui.Semi), Ui.L(t.Name.Replace(t.City, "").Trim() is var n && n.Length > 0 ? n : t.NameEn, 12, Ui.Muted)));
        h.AddChild(names);
        h.AddChild(Ui.L($"#{rank}", 12, Ui.Dim));
        var p = Ui.Card(h, Ui.Panel, 10);
        p.MouseFilter = MouseFilterEnum.Stop;
        p.MouseDefaultCursorShape = CursorShape.PointingHand;
        p.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { Select(t.Abbr); if (mb.DoubleClick) Main.I.NewCareer(t.Abbr); } };
        return p;
    }

    void Select(string abbr)
    {
        _selected = abbr;
        var d = Main.I.Data;
        var t = d.TeamByAbbr[abbr];
        var c1 = new Color(t.Colors[0]);
        foreach (var (k, row) in _rows)
            row.AddThemeStyleboxOverride("panel", Ui.Box(k == abbr ? Ui.Panel2 : Ui.Panel, 10, k == abbr ? c1.Lightened(0.2f) : null, 10));

        Ui.Clear(_detail);
        _detail.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 12, Ui.Line, 22));
        var v = Ui.V(12);
        _detail.AddChild(Ui.Scroll(v));
        var stripe = new ColorRect { Color = c1, CustomMinimumSize = new Vector2(0, 4) };
        v.AddChild(stripe);
        var top = Ui.H(16);
        top.AddChild(new TeamBadge(t, 92));
        top.AddChild(Ui.V(2, Ui.L(t.Name, 24, Ui.Text, Ui.Bold), Ui.L($"{t.Division} дивизион · {t.Conference} конференция", 13, Ui.Muted),
            Ui.L(t.Record != null ? $"Сейчас в НХЛ: {t.Record.W}-{t.Record.L}-{t.Record.Otl}, {t.Record.Pts} {Ui.Plural(t.Record.Pts, "очко", "очка", "очков")}" : "", 13, Ui.Muted)));
        v.AddChild(top);

        var teams = d.Teams;
        int Rank(System.Func<TeamData, double> f) => teams.OrderByDescending(f).ToList().IndexOf(t) + 1;
        int pr = Rank(x => x.Att - x.Dfn), ar = Rank(x => x.Att), dr = Rank(x => -x.Dfn);
        v.AddChild(Ui.Rule());
        v.AddChild(StatLine("Сила по модели", pr));
        v.AddChild(StatLine("Атака", ar));
        v.AddChild(StatLine("Оборона", dr));
        v.AddChild(Ui.Rule());
        v.AddChild(Ui.L("ЛИДЕРЫ", 12, Ui.Muted, Ui.Semi));
        var inj = d.Teams.SelectMany(x => x.Injured).ToHashSet();
        foreach (var p in d.Roster(abbr).Where(p => !p.IsGoalie).OrderByDescending(p => p.Ovr).Take(6))
            v.AddChild(PlayerLine(p, inj.Contains(p.Id)));
        var g = d.Roster(abbr).Where(p => p.IsGoalie).OrderByDescending(p => p.Ovr).FirstOrDefault();
        if (g != null) v.AddChild(PlayerLine(g, inj.Contains(g.Id)));
        v.AddChild(Ui.Spacer());
        var go = Ui.B($"Начать карьеру за «{t.City}»", () => Main.I.NewCareer(abbr), true, 17);
        go.CustomMinimumSize = new Vector2(0, 50);
        v.AddChild(go);
    }

    static Control StatLine(string name, int rank)
    {
        var h = Ui.H(10);
        h.AddChild(Ui.Expand(Ui.L(name, 14, Ui.Text)));
        var c = rank <= 8 ? Ui.Win : rank <= 24 ? Ui.Otl : Ui.Loss;
        h.AddChild(Ui.Bar((33 - rank) / 32.0, c, 140, 6));
        h.AddChild(Ui.L($"{rank}-е", 13, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(44, 0)));
        return h;
    }

    static Control PlayerLine(PlayerData p, bool injured)
    {
        var h = Ui.H(10);
        h.AddChild(Ui.L(p.Num?.ToString() ?? "", 13, Ui.Dim).Also(l => { l.CustomMinimumSize = new Vector2(26, 0); l.HorizontalAlignment = HorizontalAlignment.Right; }));
        h.AddChild(Ui.Expand(Ui.L(p.NameRu + (injured ? " (травма)" : ""), 14, injured ? Ui.Loss : Ui.Text)));
        h.AddChild(Ui.L(Ui.PosRu(p.Pos), 12, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(26, 0)));
        h.AddChild(Ui.Rating(p.Ovr));
        return h;
    }
}
