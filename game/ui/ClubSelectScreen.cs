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

    IceBg _bg;

    public override void _Ready()
    {
        var d = Main.I.Data;
        _bg = new IceBg(Ds.ClubBright(d.TeamByAbbr[_selected]));
        AddChild(_bg);
        var root = Ui.V(18);
        var m = Ui.Margin(root, 40, 28, 40, 28);
        m.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(m);

        var head = Ui.H(12);
        head.AddChild(Ui.B("←", Main.I.ShowMenu));
        var ht = Ui.V(2, Ui.L("Выберите клуб", 30, Ui.Text, Ds.Display), Ui.L("Карьера начинается с сезона 2026-27: реальные составы и звенья, сила клубов — из игроков базы (данные НХЛ, MoneyPuck, NHL-STATS.RU)", 14, Ui.Muted));
        head.AddChild(ht);
        root.AddChild(head);

        var body = Ui.Expand(Ui.H(20), true, true);
        root.AddChild(body);
        var grid = Ui.Expand(Ui.H(12), true, true);
        body.AddChild(grid);

        var status = Pundits.StatusAll(d, new System.DateOnly(2026, 10, 6));
        foreach (var conf in d.Teams.Select(t => t.Conference).Distinct().OrderBy(x => x))
            foreach (var div in d.Teams.Where(t => t.Conference == conf).Select(t => t.Division).Distinct().OrderBy(x => x))
            {
                var col = Ui.Expand(Ui.V(6), true, true);
                col.AddChild(Ui.L($"{div} дивизион", 13, Ui.Text, Ui.Semi));
                col.AddChild(Ui.L($"{conf} конференция", 11, Ui.Dim));
                foreach (var t in d.Teams.Where(t => t.Division == div).OrderBy(t => t.City))
                {
                    var row = ClubRow(t, status[t.Abbr].label);
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

    PanelContainer ClubRow(TeamData t, string status)
    {
        var h = Ui.H(10);
        h.AddChild(new TeamBadge(t, 34));
        var names = Ui.Expand(Ui.V(0, Ui.L(t.City, 15, Ui.Text, Ui.Semi), Ui.L(t.Name.Replace(t.City, "").Trim() is var n && n.Length > 0 ? n : t.NameEn, 12, Ui.Muted)));
        h.AddChild(names);
        h.AddChild(Ui.L(status, 11, StatusColor(status)));
        var p = Ui.Card(h, Ui.Panel, 10);
        p.MouseEntered += () => { if (_selected != t.Abbr) p.AddThemeStyleboxOverride("panel", Ui.Box(new Color(1, 1, 1, 0.08f), 14, Ds.Edge, 10)); };
        p.MouseExited += () => { if (_selected != t.Abbr) p.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 14, Ds.Edge, 10)); };
        p.MouseFilter = MouseFilterEnum.Stop;
        p.MouseDefaultCursorShape = CursorShape.PointingHand;
        p.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { Select(t.Abbr); if (mb.DoubleClick) Main.I.NewCareer(t.Abbr, Main.I.DefaultName(t.Abbr)); } };
        return p;
    }

    void Select(string abbr)
    {
        _selected = abbr;
        var d = Main.I.Data;
        var t = d.TeamByAbbr[abbr];
        var glow = Ds.ClubBright(t);
        _bg?.SetGlow(glow, new Color(t.Colors.Count > 1 ? t.Colors[1] : t.Colors[0]));
        foreach (var (k, row) in _rows)
        {
            var sb = Ui.Box(k == abbr ? new Color(glow, 0.2f) : Ui.Panel, 14, k == abbr ? glow : Ds.Edge, 10);
            if (k == abbr) { sb.ShadowColor = new Color(glow, 0.3f); sb.ShadowSize = 12; }
            row.AddThemeStyleboxOverride("panel", sb);
        }

        Ui.Clear(_detail);
        _detail.AddThemeStyleboxOverride("panel", Ds.GlassBoxStyle(Ds.R_L, 24, 0.06f).Also(x => x.BgColor = new Color(0.1f, 0.13f, 0.17f, 0.75f)));
        var v = Ui.V(12);
        _detail.AddChild(Ui.Scroll(v));
        var top = Ui.H(16);
        top.AddChild(new Paint(new Vector2(110, 110), q =>
        {
            Face.Glow(q, new Vector2(55, 55), 70, glow, 0.04f);
            Face.Logo(q, t, new Rect2(8, 8, 94, 94));
        }));
        top.AddChild(Ui.V(4, Ui.L(t.Name, 22, Ui.Text, Ds.Display).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(240, 0); }), Ui.L($"{t.Division} дивизион · {t.Conference} конференция", 13, Ui.Muted),
            Ui.L(t.Record != null ? $"Сейчас в НХЛ: {t.Record.W}-{t.Record.L}-{t.Record.Otl}, {t.Record.Pts} {Ui.Plural(t.Record.Pts, "очко", "очка", "очков")}" : "", 13, Ui.Muted)));
        v.AddChild(top);

        var st = Pundits.StatusAll(d, new System.DateOnly(2026, 10, 6))[abbr];
        v.AddChild(Ui.Rule());
        v.AddChild(Ui.H(10, Ui.Expand(Ui.L("Статус", 14, Ui.Text)), Ui.L(st.label, 15, StatusColor(st.label), Ui.Bold)));
        v.AddChild(Ui.L(st.note, 12, Ui.Muted));
        v.AddChild(Ui.H(10, Ui.Expand(Ui.L("Прогноз прессы", 14, Ui.Text)), Ui.L($"{Pundits.DivisionPick(d)[abbr]}-е место в дивизионе", 13, Ui.Muted)));
        v.AddChild(Ui.Rule());
        // без цифр, как в FM: кто ведёт клуб и на кого клуб надеется
        var inj = d.Teams.SelectMany(x => x.Injured).ToHashSet();
        var on = new System.DateOnly(2026, 10, 6);
        var roster = d.Roster(abbr).ToList();
        var key = roster.Where(p => !p.IsGoalie).OrderByDescending(p => p.CaEst).Take(4)
            .Concat(roster.Where(p => p.IsGoalie).OrderBy(p => inj.Contains(p.Id) ? 1 : 0).ThenByDescending(p => p.CaEst).Take(1)).ToList();
        v.AddChild(Ui.L("Ключевые игроки", 14, Ui.Text, Ui.Semi));
        foreach (var p in key) v.AddChild(PlayerLine(p, inj.Contains(p.Id), on, false));
        var young = roster.Where(p => !key.Contains(p) && (p.Age(on) ?? 30) <= 22).OrderByDescending(p => p.CaEst).Take(3).ToList();
        if (young.Count > 0)
        {
            v.AddChild(Ui.L("Перспективы", 14, Ui.Text, Ui.Semi));
            foreach (var p in young) v.AddChild(PlayerLine(p, inj.Contains(p.Id), on, true));
        }
        v.AddChild(Ui.Spacer());
        var nameEdit = new LineEdit { Text = Main.I.DefaultName(abbr), CustomMinimumSize = new Vector2(0, 40), PlaceholderText = "Название карьеры" };
        v.AddChild(Ui.V(6, Ui.L("Название карьеры", 13, Ui.Muted), nameEdit));
        var go = Ui.B($"Начать карьеру за «{t.City}»", () => Main.I.NewCareer(abbr, nameEdit.Text), true, 17);
        go.CustomMinimumSize = new Vector2(0, 52);
        StyleBoxFlat Gs(Color c) { var x = Ui.Box(c, 16, null, 22); x.ShadowColor = new Color(glow, 0.45f); x.ShadowSize = 18; return x; }
        var gc = glow.Darkened(0.1f);
        go.AddThemeStyleboxOverride("normal", Gs(gc)); go.AddThemeStyleboxOverride("hover", Gs(gc.Lightened(0.1f))); go.AddThemeStyleboxOverride("pressed", Gs(gc.Darkened(0.15f)));
        go.AddThemeColorOverride("font_color", Ui.ReadableOn(gc)); go.AddThemeColorOverride("font_hover_color", Ui.ReadableOn(gc));
        go.AddThemeFontOverride("font", Ui.Bold);
        v.AddChild(go);
    }

    static Color StatusColor(string s) => s switch { "Элита" => Ui.Gold, "Претендент" => Ui.Win, "На подъёме" or "Борьба за плей-офф" => Ui.Otl, _ => Ui.Muted };

    static Control StatLine(string name, int rank)
    {
        var h = Ui.H(10);
        h.AddChild(Ui.Expand(Ui.L(name, 14, Ui.Text)));
        var c = rank <= 8 ? Ui.Win : rank <= 24 ? Ui.Otl : Ui.Loss;
        h.AddChild(Ui.Bar((33 - rank) / 32.0, c, 140, 6));
        h.AddChild(Ui.L($"{rank}-е", 13, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(44, 0)));
        return h;
    }

    static Control PlayerLine(PlayerData p, bool injured, System.DateOnly on, bool prospect)
    {
        var h = Ui.H(10);
        var team = Main.I.Data.TeamByAbbr[p.Team];
        h.AddChild(new Paint(new Vector2(34, 34), q => { var c = new Vector2(17, 17); Face.Player(q, p, team, new Rect2(2, 2, 30, 30), Face.Circle(c, 15)); q.DrawArc(c, 16, 0, Mathf.Tau, 40, new Color(Ds.ClubBright(team), 0.7f), 1.5f, true); }));
        h.AddChild(Ui.L(Ui.PosRu(p.Pos), 12, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(26, 0)));
        h.AddChild(Ui.Expand(Ui.L(p.NameRu + (injured ? " (травма)" : ""), 14, injured ? Ui.Loss : Ui.Text)));
        // роль (из характеристик) или, у молодых, возраст и номер драфта
        string role = p.Roles.FirstOrDefault()?.Name ?? (p.IsGoalie ? "Вратарь" : "");
        string pick = p.Fx?.Pick is int pk && pk > 0 ? $", драфт №{pk}" : "";
        string note = prospect ? $"{p.Age(on)} лет{pick}" : role;
        h.AddChild(Ui.L(note, 12, Ui.Muted));
        return h;
    }
}
