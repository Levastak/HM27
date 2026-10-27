using System;
using System.Collections.Generic;
using Godot;

namespace HM27.UI;

/// <summary>
/// Визуальный стиль HM27 в духе FM24: тёмно-серые панели, плотные таблицы, шапка в цветах клуба.
/// Шрифт Inter (OFL), иконки Lucide (ISC).
/// </summary>
public static class Ui
{
    public static readonly Color Bg = new("141B25");       // фон страниц (сине-серый, как у WTCS)
    public static readonly Color Side = new("0E131A");     // левое меню
    public static readonly Color Panel = new("1D2633");    // панели-виджеты
    public static readonly Color Panel2 = new("263142");   // шапки панелей, кнопки
    public static readonly Color Line = new("2F3B4D");
    public static readonly Color Text = new("ECEFF4");
    public static readonly Color Muted = new("A0A8B5");
    public static readonly Color Dim = new("6E7785");
    public static readonly Color Gold = new("F2C14E");
    public static readonly Color Win = new("3FB97A");
    public static readonly Color Loss = new("E25B5B");
    public static readonly Color Otl = new("E2A93B");
    public static Color Accent = new("4C8DFF");
    /// <summary>Акцент, достаточно светлый для полос и подсветки на тёмном фоне.</summary>
    public static Color AccentBright
    {
        get
        {
            var c = Accent;
            for (int i = 0; i < 6 && c.Luminance < 0.42f; i++) c = c.Lightened(0.18f);
            return c;
        }
    }

    static FontFile _regular, _medium, _semi, _bold, _black;
    public static FontFile Regular => _regular ??= Load("Inter-Regular");
    public static FontFile Medium => _medium ??= Load("Inter-Medium");
    public static FontFile Semi => _semi ??= Load("Inter-SemiBold");
    public static FontFile Bold => _bold ??= Load("Inter-Bold");
    public static FontFile Black => _black ??= Load("InterDisplay-Black");

    static FontFile Load(string name)
    {
        var f = GD.Load<FontFile>($"res://ui/fonts/{name}.ttf");
        f.Antialiasing = TextServer.FontAntialiasing.Gray;
        f.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        f.Hinting = TextServer.Hinting.Light;
        return f;
    }

    public static Theme MakeTheme()
    {
        var t = new Theme { DefaultFont = Regular, DefaultFontSize = 13 };
        foreach (var type in new[] { "Label", "Button", "LineEdit", "CheckBox", "OptionButton", "ItemList", "TooltipLabel" })
            t.SetColor("font_color", type, Text);
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_pressed_color", "Button", Colors.White);
        t.SetColor("font_disabled_color", "Button", Dim);
        t.SetFont("font", "Button", Semi);
        t.SetStylebox("normal", "Button", Box(Panel2, 6, Line));
        t.SetStylebox("hover", "Button", Box(Panel2.Lightened(0.08f), 6, Accent.Darkened(0.2f)));
        t.SetStylebox("pressed", "Button", Box(Accent.Darkened(0.35f), 6, Accent));
        t.SetStylebox("disabled", "Button", Box(Panel, 6, Line));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetStylebox("panel", "PanelContainer", Box(Panel, 10));
        t.SetStylebox("panel", "TooltipPanel", Box(Panel2, 6, Line));
        t.SetStylebox("grabber", "VScrollBar", Box(Line, 3, null, 0).Also(b => b.ContentMarginTop = b.ContentMarginBottom = 0));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(Dim, 3, null, 0).Also(b => b.ContentMarginTop = b.ContentMarginBottom = 0));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(Muted, 3, null, 0).Also(b => b.ContentMarginTop = b.ContentMarginBottom = 0));
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        track.ContentMarginLeft = track.ContentMarginRight = 3;
        t.SetStylebox("scroll", "VScrollBar", track);
        t.SetConstant("separation", "HBoxContainer", 8);
        t.SetConstant("separation", "VBoxContainer", 8);
        return t;
    }

    public static StyleBoxFlat Box(Color bg, int radius = 8, Color? border = null, int pad = 12)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(Math.Min(radius, 4));   // FM: почти прямые углы
        if (border.HasValue) { s.BorderColor = border.Value; s.SetBorderWidthAll(1); }
        s.ContentMarginLeft = s.ContentMarginRight = pad;
        s.ContentMarginTop = s.ContentMarginBottom = Math.Max(4, pad - 4);
        return s;
    }

    // ---------------------------------------------------------------- фабрики
    public static Label L(string text, int size = 15, Color? color = null, FontFile font = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        if (size != 15) l.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        if (font != null) l.AddThemeFontOverride("font", font);
        return l;
    }

    public static Button B(string text, Action onClick, bool primary = false, int size = 15)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        if (size != 15) b.AddThemeFontSizeOverride("font_size", size);
        if (primary)
        {
            b.AddThemeStyleboxOverride("normal", Box(Accent, 6, null, 18));
            b.AddThemeStyleboxOverride("hover", Box(Accent.Lightened(0.12f), 6, null, 18));
            b.AddThemeStyleboxOverride("pressed", Box(Accent.Darkened(0.15f), 6, null, 18));
            b.AddThemeColorOverride("font_color", ReadableOn(Accent));
            b.AddThemeColorOverride("font_hover_color", ReadableOn(Accent));
        }
        if (onClick != null) b.Pressed += onClick;
        return b;
    }

    /// <summary>Иконка Lucide заданного цвета и размера.</summary>
    public static TextureRect Icon(string name, int size = 18, Color? color = null)
    {
        return new TextureRect
        {
            Texture = Icons.Get(name, filled: false), CustomMinimumSize = new Vector2(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Modulate = color ?? Muted, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>Характеристика 1–20 (как в FM) из перцентиля 1–99.</summary>
    public static int Attr20(int pct) => Math.Clamp((int)Math.Round(1 + (pct - 1) * 19 / 98.0), 1, 20);

    public static Color AttrColor(int v20) => v20 >= 16 ? new Color("5DD879") : v20 >= 11 ? new Color("B7E06A") : v20 >= 6 ? new Color("E8C25A") : new Color("E0705A");

    /// <summary>Характеристика по шкале 1–20 (модель игрока).</summary>
    public static Label AttrV(double v, int size = 13)
    {
        int i = Math.Clamp((int)Math.Round(v), 1, 20);
        return L(i.ToString(), size, AttrColor(i), Bold, HorizontalAlignment.Center);
    }

    public static Label Attr(int pct, int size = 13)
    {
        int v = Attr20(pct);
        return L(v.ToString(), size, AttrColor(v), Bold, HorizontalAlignment.Center);
    }

    /// <summary>Звёзды способностей (0,5–5), как оценка тренеров в FM.</summary>
    public static Control Stars(int ovrPct, int size = 13)
    {
        double st = Math.Clamp(Math.Round(ovrPct / 20.0 * 2) / 2, 0.5, 5);
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 1);
        for (int i = 1; i <= 5; i++)
        {
            bool full = st >= i, half = !full && st >= i - 0.5;
            var r = new TextureRect
            {
                Texture = half ? Icons.Get("star-half", true) : Icons.Get("star", full), CustomMinimumSize = new Vector2(size, size),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = full || half ? Gold : new Color(Dim, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            h.AddChild(r);
        }
        return h;
    }

    public static Color ReadableOn(Color bg) => bg.Luminance > 0.55f ? new Color("0B0F14") : Colors.White;

    public static HBoxContainer H(int sep = 8, params Control[] kids)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", sep);
        foreach (var k in kids) h.AddChild(k);
        return h;
    }

    public static VBoxContainer V(int sep = 8, params Control[] kids)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", sep);
        foreach (var k in kids) v.AddChild(k);
        return v;
    }

    public static PanelContainer Card(Control content, Color? bg = null, int pad = 16, Color? border = null)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Box(bg ?? Panel, 10, border, pad));
        p.AddChild(content);
        return p;
    }

    public static MarginContainer Margin(Control c, int l, int t, int r, int b)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", l); m.AddThemeConstantOverride("margin_top", t);
        m.AddThemeConstantOverride("margin_right", r); m.AddThemeConstantOverride("margin_bottom", b);
        m.AddChild(c);
        return m;
    }

    public static Control Spacer(bool expand = true, int min = 0)
    {
        var c = new Control { CustomMinimumSize = new Vector2(min, min) };
        if (expand) c.SizeFlagsHorizontal = c.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return c;
    }

    public static T Expand<T>(T c, bool h = true, bool v = false) where T : Control
    {
        if (h) c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (v) c.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return c;
    }

    public static ColorRect Rule(Color? c = null, int h = 1) => new() { Color = c ?? Line, CustomMinimumSize = new Vector2(0, h) };

    public static ScrollContainer Scroll(Control content)
    {
        var s = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        s.SizeFlagsHorizontal = s.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        s.AddChild(content);
        return s;
    }

    /// <summary>Цветная плашка рейтинга 1–99.</summary>
    public static PanelContainer Rating(int v, int size = 13, int width = 34)
    {
        var c = v >= 85 ? new Color("2FBF71") : v >= 70 ? new Color("7BC043") : v >= 50 ? new Color("D9B23C") : v >= 30 ? new Color("E07B39") : new Color("D9534F");
        var l = L(v.ToString(), size, Ui.ReadableOn(c), Bold, HorizontalAlignment.Center);
        var p = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        p.AddThemeStyleboxOverride("panel", Box(c, 4, null, 4));
        p.AddChild(l);
        return p;
    }

    /// <summary>Полоса значения 0–1 с подписью.</summary>
    public static Control Bar(double frac, Color c, int w = 120, int h = 8)
    {
        var bg = new ColorRect { Color = Line, CustomMinimumSize = new Vector2(w, h) };
        var fg = new ColorRect { Color = c, Size = new Vector2((float)(w * Math.Clamp(frac, 0, 1)), h) };
        bg.AddChild(fg);
        return bg;
    }

    public static string Pct(double p) => $"{Math.Round(p * 100):0}%";

    /// <summary>Число со знаком без «−0,00».</summary>
    public static string Signed(double v, string fmt = "0.00") => Math.Abs(v) < 0.005 ? (0.0).ToString(fmt) : (v > 0 ? "+" : "−") + Math.Abs(v).ToString(fmt);

    static readonly string[] MonthsGen = { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };
    static readonly string[] MonthsShort = { "янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек" };
    static readonly string[] Dow = { "вс", "пн", "вт", "ср", "чт", "пт", "сб" };
    public static string Date(DateOnly d, bool dow = true) => $"{d.Day} {MonthsGen[d.Month - 1]}{(dow ? ", " + Dow[(int)d.DayOfWeek] : "")}";
    public static string DateShort(DateOnly d) => $"{d.Day} {MonthsShort[d.Month - 1]}";

    public static string Plural(int n, string one, string few, string many)
    {
        int a = Math.Abs(n) % 100, b = a % 10;
        if (a is > 10 and < 20) return many;
        return b == 1 ? one : b is >= 2 and <= 4 ? few : many;
    }

    public static string PosRu(string pos) => pos switch { "C" => "Ц", "LW" => "ЛН", "RW" => "ПН", "D" => "З", "G" => "В", _ => pos };

    public static string Clock(int period, int sec)
    {
        string p = period <= 3 ? $"{period}-й" : period == 4 ? "ОТ" : $"{period - 3}-й ОТ";
        return $"{p} · {sec / 60:00}:{sec % 60:00}";
    }

    public static void Clear(Node n)
    {
        foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }
}
