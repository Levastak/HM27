using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Таблица из строк фиксированной ширины: шапка, полосы, подсветка клуба игрока.</summary>
public partial class Table : VBoxContainer
{
    readonly int[] _w;
    readonly HorizontalAlignment[] _al;
    int _n;

    public Table(string[] headers, int[] widths, HorizontalAlignment[] align = null)
    {
        _w = widths;
        _al = align ?? widths.Select((w, i) => i == 0 || w == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right).ToArray();
        AddThemeConstantOverride("separation", 0);
        var cells = headers.Select(h => (Control)Ui.L(h, 12, Ui.Muted, Ui.Semi)).ToArray();
        AddChild(Row(cells, Colors.Transparent, null));
        AddChild(Ui.Rule());
    }

    public Table() { }

    public Control Add(Control[] cells, bool highlight = false, Action onClick = null)
    {
        var bg = highlight ? new Color(Ui.Club.Lightened(0.15f), 0.2f) : (_n++ % 2 == 0 ? Colors.Transparent : new Color(1, 1, 1, 0.02f));
        var r = Row(cells, bg, onClick);
        AddChild(r);
        return r;
    }

    public Control Add(params string[] texts) => Add(texts.Select(t => (Control)Ui.L(t, 14)).ToArray());

    PanelContainer Row(Control[] cells, Color bg, Action onClick)
    {
        var p = new PanelContainer();
        var sb = Ui.Box(bg, 4, null, 8); sb.ContentMarginTop = sb.ContentMarginBottom = 5;
        p.AddThemeStyleboxOverride("panel", sb);
        var h = Ui.H(10);
        p.AddChild(h);
        for (int i = 0; i < cells.Length && i < _w.Length; i++)
        {
            var c = cells[i];
            if (c is Label l) { l.HorizontalAlignment = _al[i]; l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }
            if (_w[i] == 0) { c.SizeFlagsHorizontal = SizeFlags.ExpandFill; h.AddChild(c); continue; }
            if (c is Label) { c.CustomMinimumSize = new Vector2(_w[i], 0); h.AddChild(c); continue; }
            var box = new HBoxContainer
            {
                CustomMinimumSize = new Vector2(_w[i], 0),
                Alignment = _al[i] == HorizontalAlignment.Left ? BoxContainer.AlignmentMode.Begin : _al[i] == HorizontalAlignment.Right ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Center,
            };
            box.AddChild(c);
            h.AddChild(box);
        }
        if (onClick != null)
        {
            p.MouseFilter = MouseFilterEnum.Stop;
            p.MouseDefaultCursorShape = CursorShape.PointingHand;
            p.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) onClick(); };
        }
        return p;
    }
}

/// <summary>Переключатель вкладок-«таблеток».</summary>
public partial class Segmented : HBoxContainer
{
    readonly List<Button> _buttons = new();
    public Segmented(string[] items, int selected, Action<int> onSelect)
    {
        AddThemeConstantOverride("separation", 2);
        for (int i = 0; i < items.Length; i++)
        {
            int k = i;
            var b = Ui.B(items[i], () => { Select(k); onSelect(k); });
            b.AddThemeFontSizeOverride("font_size", 12);
            b.CustomMinimumSize = new Vector2(0, 32);
            _buttons.Add(b);
            AddChild(b);
        }
        Select(selected);
    }
    public Segmented() { }

    /// <summary>Подложка-таблетка под всеми вариантами (дизайн-система: Ds.Pill).</summary>
    public override void _Draw() => DrawStyleBox(Ds.Pill(new Color(1, 1, 1, 0.04f), 0, 0, Ds.Edge), new Rect2(Vector2.Zero, Size));

    void Select(int k)
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool on = i == k;
            var b = _buttons[i];
            b.AddThemeStyleboxOverride("normal", Ds.Pill(on ? new Color(1, 1, 1, 0.13f) : new Color(0, 0, 0, 0), 14, 4));
            b.AddThemeStyleboxOverride("hover", Ds.Pill(new Color(1, 1, 1, on ? 0.16f : 0.06f), 14, 4));
            b.AddThemeStyleboxOverride("pressed", Ds.Pill(new Color(1, 1, 1, 0.16f), 14, 4));
            b.AddThemeColorOverride("font_color", on ? Colors.White : Ui.Muted);
            b.AddThemeFontOverride("font", on ? Ui.Semi : Ui.Medium);
        }
    }
}

/// <summary>Карточка игрока: био, рейтинги, статистика.</summary>
public static class PlayerCard
{
    public static void Show(Node parent, PlayerData p, Career c)
    {
        if (Shell.I != null) { Shell.I.Go("player", null, p.Id.ToString()); return; }
        var t = c.Team(p.Team);
        var pop = new PopupPanel();
        pop.AddThemeStyleboxOverride("panel", Ui.Box(new Color("111823"), 18, Ds.Edge, 22).Also(x => { x.ShadowColor = new Color(0, 0, 0, 0.5f); x.ShadowSize = 24; }));
        var v = Ui.V(10);
        v.CustomMinimumSize = new Vector2(480, 0);
        pop.AddChild(v);
        // шапка как в карточке-постере: фото в кольце рейтинга (видимая КА клуба), имя, позиция и клуб
        int ovr = Ds.Ovr(c, p);
        var face = new Paint(new Vector2(92, 92), q =>
        {
            var ctr = q.Size / 2;
            Face.Glow(q, ctr, 62, Ds.ClubBright(t), 0.05f);
            Face.Player(q, p, t, new Rect2(ctr.X - 38, ctr.Y - 38, 76, 76), Face.Circle(ctr, 38));
            Face.Ring(q, ctr, 43, ovr, 3);
        });
        var age = p.Age(c.Today);
        var head = Ui.H(16, face, Ui.Expand(Ui.V(3,
            Ui.L(p.NameRu, 21, Ui.Text, Ui.Semi),
            Ui.H(6, new TeamBadge(t, 18).Also(x => x.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter), Ui.L($"{t.Name} · #{p.Num} · {Ui.PosRu(p.Pos)}", 13, Ui.Muted)),
            Ui.L($"{(age.HasValue ? $"{age} {Ui.Plural(age.Value, "год", "года", "лет")} · " : "")}{p.Height} см · {p.Weight} кг · {Country(p.Country)}", 12, Ui.Dim))));
        foreach (var x in head.GetChildren().OfType<Control>()) x.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        v.AddChild(head);
        v.AddChild(Ui.Rule());
        var keys = p.IsGoalie ? new[] { ("rfx", "Реакция"), ("gpos", "Позиция"), ("reb", "Контроль отскока"), ("gcon", "Стабильность") } :
            new[] { ("fin", "Завершение"), ("pas", "Передача"), ("vis", "Видение"), ("dfn", "Позиционная оборона"), ("spd", "Скорость"), ("sta", "Выносливость") };
        foreach (var (k, name) in keys)
        {
            double a = PlayerModel.Visible(c, p, k);
            var h = Ui.H(10);
            h.AddChild(Ui.Expand(Ui.L(name, 13, Ui.Text)));
            h.AddChild(Ui.Bar(Ds.Show(a) / 20.0, Ds.Attr(Ds.ShowI(a)), 170, 6).Also(x => x.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter));
            h.AddChild(Ui.AttrV(a, 14));
            v.AddChild(h);
        }
        v.AddChild(Ui.Rule());
        if (p.IsGoalie)
        {
            v.AddChild(Ui.L($"Сейвы сверх ожидаемого: {p.Gsax * 100:+0.0;-0.0}% от ожидаемых голов (за последние сезоны)", 12, Ui.Muted));
            var s = c.Goalies.GetValueOrDefault(p.Id);
            if (s != null) v.AddChild(Ui.L($"Сезон в игре: {s.Gp} И · {s.W}-{s.L}-{s.Otl} · {s.SvPct:0.000} · КН {s.Gaa:0.00} · {s.So} «сухих»", 13));
        }
        else
        {
            v.AddChild(Ui.L($"Личный xG: {p.IxgPg:0.00} за матч · {p.Ixg60:0.00} за 60 минут · время в НХЛ {p.Toi:0.0} мин", 12, Ui.Muted));
            var s = c.Skaters.GetValueOrDefault(p.Id);
            if (s != null) v.AddChild(Ui.L($"Сезон в игре: {s.Gp} И · {s.G} Г · {s.A} П · {s.Pts} О · {s.Ppg} в большинстве", 13));
        }
        if (c.Injured.Contains(p.Id)) v.AddChild(Ui.L("Травмирован", 14, Ui.Loss, Ui.Semi));
        parent.AddChild(pop);
        pop.PopupCentered();
    }

    public static Color RatingColor(int v) => v >= 85 ? new Color("2FBF71") : v >= 70 ? new Color("7BC043") : v >= 50 ? new Color("D9B23C") : v >= 30 ? new Color("E07B39") : new Color("D9534F");

    public static string PosLong(string pos) => pos switch
    {
        "C" => "центральный нападающий", "LW" => "левый нападающий", "RW" => "правый нападающий", "D" => "защитник", "G" => "вратарь", _ => pos,
    };

    public static string Country(string iso) => iso switch
    {
        "CAN" => "Канада", "USA" => "США", "RUS" => "Россия", "SWE" => "Швеция", "FIN" => "Финляндия", "CZE" => "Чехия",
        "SVK" => "Словакия", "CHE" => "Швейцария", "DEU" => "Германия", "LVA" => "Латвия", "DNK" => "Дания", "NOR" => "Норвегия",
        "AUT" => "Австрия", "BLR" => "Беларусь", "KAZ" => "Казахстан", "UKR" => "Украина", "FRA" => "Франция", "SVN" => "Словения",
        "GBR" => "Великобритания", "AUS" => "Австралия", "NLD" => "Нидерланды", "ITA" => "Италия", "POL" => "Польша", "JPN" => "Япония",
        "KOR" => "Корея", "BRA" => "Бразилия", "UZB" => "Узбекистан", "LTU" => "Литва", "EST" => "Эстония", "HUN" => "Венгрия",
        _ => iso ?? "",
    };
}
