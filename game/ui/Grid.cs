using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace HM27.UI;

/// <summary>Колонка плотной таблицы.</summary>
public sealed class Col<T>
{
    public string Title;
    public int Width;                         // 0 — растягивается
    public HorizontalAlignment Align = HorizontalAlignment.Center;
    public Func<T, Control> Cell;
    public Func<T, IComparable> Sort;         // null — колонка не сортируется
    public bool Desc = true;                  // первая сортировка по убыванию
    public string Tip;
}

/// <summary>
/// Плотная таблица как в FM: строки 28 пикселей, полосы, подсветка при наведении, сортировка щелчком по заголовку.
/// </summary>
public sealed class Grid<T>
{
    readonly List<Col<T>> _cols;
    readonly List<T> _rows;
    readonly Func<T, bool> _highlight;
    readonly Action<T> _click;
    int _sortCol = -1;
    bool _desc;
    public readonly VBoxContainer Root = new();

    public Grid(List<Col<T>> cols, IEnumerable<T> rows, Action<T> click = null, Func<T, bool> highlight = null, int sortCol = -1)
    {
        _cols = cols; _rows = rows.ToList(); _click = click; _highlight = highlight;
        Root.AddThemeConstantOverride("separation", 0);
        Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (sortCol >= 0) { _sortCol = sortCol; _desc = cols[sortCol].Desc; }
        Render();
    }

    void Render()
    {
        Ui.Clear(Root);
        Root.AddChild(Header());
        IEnumerable<T> rows = _rows;
        if (_sortCol >= 0 && _cols[_sortCol].Sort != null)
            rows = _desc ? rows.OrderByDescending(_cols[_sortCol].Sort) : rows.OrderBy(_cols[_sortCol].Sort);
        int i = 0;
        foreach (var r in rows) Root.AddChild(Row(r, i++));
        if (i == 0) Root.AddChild(Ui.Margin(Ui.L("Нет данных", 13, Ui.Muted), 10, 8, 10, 8));
    }

    Control Header()
    {
        var p = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = Ui.Panel2, BorderColor = Ui.Line, BorderWidthBottom = 1 };
        sb.ContentMarginLeft = sb.ContentMarginRight = 8; sb.ContentMarginTop = sb.ContentMarginBottom = 0;
        p.AddThemeStyleboxOverride("panel", sb);
        var h = Ui.H(6);
        p.AddChild(h);
        for (int c = 0; c < _cols.Count; c++)
        {
            var col = _cols[c];
            int ci = c;
            string arrow = _sortCol == c ? (_desc ? " ▼" : " ▲") : "";
            var b = new Button { Text = col.Title + arrow, Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(col.Width, 28), Alignment = col.Align, TooltipText = col.Tip ?? "" };
            b.AddThemeFontSizeOverride("font_size", 11);
            b.AddThemeFontOverride("font", Ui.Semi);
            b.AddThemeColorOverride("font_color", _sortCol == c ? Ui.Text : Ui.Muted);
            b.AddThemeColorOverride("font_hover_color", Ui.Text);
            var empty = new StyleBoxEmpty();
            b.AddThemeStyleboxOverride("normal", empty); b.AddThemeStyleboxOverride("hover", empty); b.AddThemeStyleboxOverride("pressed", empty);
            if (col.Width == 0) b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (col.Sort != null)
            {
                b.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
                b.Pressed += () => { if (_sortCol == ci) _desc = !_desc; else { _sortCol = ci; _desc = col.Desc; } Render(); };
            }
            else b.MouseFilter = Control.MouseFilterEnum.Ignore;
            h.AddChild(b);
        }
        return p;
    }

    Control Row(T r, int i)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(0, 28) };
        bool hi = _highlight?.Invoke(r) ?? false;
        var bg = hi ? new Color(Ui.AccentBright, 0.16f) : i % 2 == 1 ? new Color(1, 1, 1, 0.025f) : new Color(0, 0, 0, 0);
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.ContentMarginLeft = sb.ContentMarginRight = 8; sb.ContentMarginTop = sb.ContentMarginBottom = 2;
        p.AddThemeStyleboxOverride("panel", sb);
        var h = Ui.H(6);
        h.MouseFilter = Control.MouseFilterEnum.Ignore;
        p.AddChild(h);
        foreach (var col in _cols)
        {
            var c = col.Cell(r) ?? new Control();
            c.MouseFilter = Control.MouseFilterEnum.Ignore;
            if (c is Label l) { l.HorizontalAlignment = col.Align; l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }
            Control cell = c;
            if (c is not Label)
            {
                var box = new HBoxContainer { Alignment = col.Align == HorizontalAlignment.Left ? BoxContainer.AlignmentMode.Begin : col.Align == HorizontalAlignment.Right ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
                box.AddChild(c);
                cell = box;
            }
            if (col.Width == 0) cell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            else cell.CustomMinimumSize = new Vector2(col.Width, 0);
            h.AddChild(cell);
        }
        if (_click != null)
        {
            p.MouseFilter = Control.MouseFilterEnum.Stop;
            p.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            p.MouseEntered += () => sb.BgColor = new Color(1, 1, 1, 0.07f);
            p.MouseExited += () => sb.BgColor = bg;
            p.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _click(r); };
        }
        return p;
    }
}

/// <summary>Виджет FM: панель с полосой-заголовком и ссылкой «Подробнее».</summary>
public static class Widget
{
    public static PanelContainer Make(string title, Control content, string link = null, Action onLink = null, int pad = 12)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 0).Also(s => { s.ContentMarginTop = s.ContentMarginBottom = 0; }));
        var v = Ui.V(0);
        p.AddChild(v);
        var head = new PanelContainer();
        var hs = new StyleBoxFlat { BgColor = Ui.Panel2 };
        hs.CornerRadiusTopLeft = hs.CornerRadiusTopRight = 4;
        hs.ContentMarginLeft = hs.ContentMarginRight = 12; hs.ContentMarginTop = hs.ContentMarginBottom = 6;
        head.AddThemeStyleboxOverride("panel", hs);
        var hh = Ui.H(8, Ui.Expand(Ui.L(title.ToUpperInvariant(), 11, Ui.Muted, Ui.Bold)));
        if (link != null)
        {
            var b = new Button { Text = link, Flat = true, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            b.AddThemeFontSizeOverride("font_size", 11);
            b.AddThemeColorOverride("font_color", Ui.AccentBright);
            b.AddThemeColorOverride("font_hover_color", Colors.White);
            var e = new StyleBoxEmpty();
            b.AddThemeStyleboxOverride("normal", e); b.AddThemeStyleboxOverride("hover", e); b.AddThemeStyleboxOverride("pressed", e);
            b.Pressed += onLink;
            hh.AddChild(b);
        }
        head.AddChild(hh);
        v.AddChild(head);
        var body = pad > 0 ? Ui.Margin(content, pad, pad - 2, pad, pad) : content;
        if ((content.SizeFlagsVertical & Control.SizeFlags.Expand) != 0) { body.SizeFlagsVertical = Control.SizeFlags.ExpandFill; v.SizeFlagsVertical = Control.SizeFlags.ExpandFill; }
        v.AddChild(body);
        return p;
    }
}
