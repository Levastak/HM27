using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Дизайн-система HM27 «Премиум»: лёд под светом арены, матовое стекло, воздух; цвет клуба — свечение, а не заливка.
/// Моменты (заставка матча, табло, гол) — «Трансляция»: узкий наклонный шрифт, косые срезы, цвета клуба во весь экран.
///
/// Токены:
///   цвета — фон <see cref="Bg"/>, поверхности (стекло) <see cref="Glass"/>, текст Text/Muted/Dim (в <see cref="Ui"/>);
///   рейтинг OVR 50–99 из КА — ступени <see cref="Tier"/>: бронза &lt;65, серебро 65–74, золото 75–84, лёд 85+;
///   характеристики 1–20 — <see cref="Attr"/>: тусклый → белый → зелёный → золото;
///   свежесть — <see cref="FreshCol"/>; сыгранность — <see cref="FamCol"/>.
///   шрифты — Unbounded (заголовки, <see cref="Display"/>), Onest (текст, Ui.Regular…), Sofia Sans Extra Condensed
///   (трансляция — <see cref="Tv"/>: только заглавные и цифры, строчная кириллица у него болгарская), числа и таблицы — Fira Sans Extra Condensed (Ui.Cond…).
///   скругления R_S/R_M/R_L, отступы GAP_*, движение FAST/BASE/SLOW (кубическое замедление).
/// </summary>
public static class Ds
{
    public static readonly Color Bg = new("0B1119");
    public static readonly Color Ink = new("070B11");          // чёрный трансляции
    public static readonly Color Glass = new(1, 1, 1, 0.045f);
    public static readonly Color GlassHi = new(1, 1, 1, 0.08f);
    public static readonly Color Edge = new(1, 1, 1, 0.075f);

    public const int R_S = 8, R_M = 14, R_L = 20;
    public const int GAP_S = 8, GAP_M = 16, GAP_L = 24, GAP_XL = 32;
    public const float FAST = 0.15f, BASE = 0.28f, SLOW = 0.5f;

    // ------------------------------------------------------------------ шрифты
    static readonly Dictionary<string, FontFile> Files = new();
    static readonly Dictionary<(string, int), Font> Vars = new();
    static FontFile File(string name)
    {
        if (Files.TryGetValue(name, out var f)) return f;
        f = GD.Load<FontFile>($"res://ui/fonts/{name}.ttf");
        f.Antialiasing = TextServer.FontAntialiasing.Gray;
        f.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        f.Hinting = TextServer.Hinting.Light;
        return Files[name] = f;
    }

    /// <summary>Вариативный шрифт заданной насыщенности с моноширинными цифрами.</summary>
    public static Font V(string file, int wght)
    {
        if (Vars.TryGetValue((file, wght), out var v)) return v;
        var ts = TextServerManager.GetPrimaryInterface();
        return Vars[(file, wght)] = new FontVariation
        {
            BaseFont = File(file),
            VariationOpentype = new Godot.Collections.Dictionary { { ts.NameToTag("wght"), wght } },
            OpentypeFeatures = new Godot.Collections.Dictionary { { ts.NameToTag("tnum"), 1 } },
        };
    }

    public static Font Display => V("Unbounded-VF", 600);
    public static Font DisplayLight => V("Unbounded-VF", 300);
    public static Font DisplayBold => V("Unbounded-VF", 700);
    /// <summary>Трансляция: узкий наклонный.</summary>
    public static Font Tv => V("SofiaSansExtraCondensed-Italic-VF", 900);
    public static Font TvSemi => V("SofiaSansExtraCondensed-Italic-VF", 700);
    public static Font TvNum => V("SofiaSansExtraCondensed-VF", 900);

    // ------------------------------------------------------------------ шкалы
    /// <summary>OVR 50–99 из КА клуба игрока (как видит клуб): медиана лиги ~69, звёзды 88+.</summary>
    public static int Ovr(Career c, PlayerData p) => p == null ? 0 : (int)Math.Round(Math.Clamp(50 + (PlayerModel.Visible(c, p, "ca") - 74) / 114.0 * 47, 40, 99));

    public static readonly Color Bronze = new("C48A5A"), Silver = new("C9D3DE"), GoldT = new("F5C24C"), Elite = new("7FE6FF");
    public static Color Tier(int ovr) => ovr >= 85 ? Elite : ovr >= 75 ? GoldT : ovr >= 65 ? Silver : Bronze;
    public static string TierName(int ovr) => ovr >= 85 ? "элита" : ovr >= 75 ? "золото" : ovr >= 65 ? "серебро" : "бронза";

    /// <summary>Характеристика 1–20: пять ступеней от тусклого к яркому, верх — золото.</summary>
    public static Color Attr(double v) => v >= 18 ? GoldT : v >= 15 ? new Color("5BE0A0") : v >= 11 ? new Color("E6EDF5") : v >= 7 ? new Color("8E99AB") : new Color("5B6577");
    public static Color FreshCol(double f) => f >= 85 ? new Color("5BE0A0") : f >= 70 ? GoldT : new Color("FF5A5F");
    public static Color FamCol(double f) => f >= 0.8 ? new Color("5BE0A0") : f >= 0.7 ? new Color("E6EDF5") : GoldT;

    /// <summary>Цвет клуба, который читается на тёмном фоне (свечение, кольца, акценты).</summary>
    public static Color ClubBright(TeamData t)
    {
        var c = new Color(t.Colors[0]);
        if (c.Luminance < 0.08f && t.Colors.Count > 1 && new Color(t.Colors[1]).Luminance > c.Luminance) c = new Color(t.Colors[1]);
        for (int i = 0; i < 6 && c.Luminance < 0.24f; i++) c = c.Lightened(0.12f);
        return c;
    }

    /// <summary>Короткое имя характеристики для карточек.</summary>
    public static string AttrShort(string key) => key switch
    {
        "fin" => "Завершение", "pwr" => "Мощь броска", "lng" => "Бросок издали", "net" => "У ворот", "pas" => "Передача", "hnd" => "Шайба", "fo" => "Вбрасывание",
        "dfn" => "Позиция", "tka" => "Отбор", "blk" => "Блоки", "hit" => "Силовые", "pk" => "Меньшинство", "spd" => "Скорость", "acc" => "Ускорение",
        "str" => "Сила", "sta" => "Выносливость", "dec" => "Решения", "vis" => "Видение", "dis" => "Дисциплина", "wrk" => "Труд", "tm" => "Команда",
        "pp" => "Большинство", "cmp" => "Хладнокровие", "con" => "Стабильность",
        "rfx" => "Реакция", "gpos" => "Позиция", "reb" => "Отскок", "scr" => "Заслон", "gsta" => "Выносливость", "stk" => "Клюшка", _ => key,
    };

    public static string PosShort(string pos) => pos switch { "C" => "ЦН", "LW" => "ЛН", "RW" => "ПН", "D" => "З", "G" => "ВР", _ => pos };

    /// <summary>Стиль «стекло»: полупрозрачная поверхность со светлым краем.</summary>
    public static StyleBoxFlat GlassBoxStyle(int radius = R_L, int pad = 18, float alpha = 0.045f)
    {
        var s = new StyleBoxFlat { BgColor = new Color(0.62f, 0.72f, 0.86f, alpha), BorderColor = Edge };
        s.SetCornerRadiusAll(radius); s.SetBorderWidthAll(1);
        s.ContentMarginLeft = s.ContentMarginRight = pad; s.ContentMarginTop = s.ContentMarginBottom = pad;
        return s;
    }

    /// <summary>Таблетка (кнопка-пилюля, плашка).</summary>
    public static StyleBoxFlat Pill(Color bg, int padX = 14, int padY = 6, Color? border = null)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(99);
        if (border.HasValue) { s.BorderColor = border.Value; s.SetBorderWidthAll(1); }
        s.ContentMarginLeft = s.ContentMarginRight = padX; s.ContentMarginTop = s.ContentMarginBottom = padY;
        return s;
    }

    /// <summary>Одно появление: элементы проявляются волной (каждый следующий чуть позже).</summary>
    public static void Reveal(Node owner, IReadOnlyList<CanvasItem> items, float step = 0.025f, float start = 0.03f)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var x = items[i];
            x.Modulate = new Color(1, 1, 1, 0);
            var tw = owner.CreateTween();
            tw.TweenInterval(start + i * step);
            tw.TweenProperty(x, "modulate:a", 1.0, BASE).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
    }
}

/// <summary>Фон «лёд под светом арены»: мягкая неровность, царапины коньков, свет сверху и свечение цвета клуба.</summary>
public partial class IceBg : ColorRect
{
    public IceBg() : this(Ui.Club) { }
    public IceBg(Color glow, Color? glow2 = null)
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Material = new ShaderMaterial { Shader = Shader };
        SetGlow(glow, glow2 ?? glow);
        (Material as ShaderMaterial).SetShaderParameter("base_col", Ds.Bg);
    }
    public void SetGlow(Color a, Color b) { var m = (ShaderMaterial)Material; m.SetShaderParameter("glow", a); m.SetShaderParameter("glow2", b); }
    public override void _Notification(int what) { if (what == NotificationResized) (Material as ShaderMaterial)?.SetShaderParameter("size", Size); }

    static Shader _shader;
    static Shader Shader => _shader ??= new Shader { Code = @"shader_type canvas_item;
uniform vec4 base_col : source_color; uniform vec4 glow : source_color; uniform vec4 glow2 : source_color; uniform vec2 size = vec2(1600.0, 900.0);
float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p){ vec2 i = floor(p), f = fract(p); vec2 u = f*f*(3.0-2.0*f);
  return mix(mix(hash(i), hash(i+vec2(1,0)), u.x), mix(hash(i+vec2(0,1)), hash(i+vec2(1,1)), u.x), u.y); }
float streaks(vec2 p, float ang, float sc){
  float s = sin(ang), co = cos(ang); vec2 q = vec2(co * p.x - s * p.y, s * p.x + co * p.y);
  return smoothstep(0.82, 1.0, vnoise(vec2(q.x * sc * 0.02, q.y * sc)));
}
void fragment(){
  vec2 uv = UV; vec2 px = UV * size; float asp = size.x / size.y;
  vec3 c = base_col.rgb;
  float frost = vnoise(px / 180.0) * 0.6 + vnoise(px / 45.0) * 0.4;
  c += (frost - 0.5) * 0.03;
  c += (streaks(px, 0.35, 0.9) + streaks(px + 333.0, -0.6, 0.7) + streaks(px + 777.0, 1.3, 1.1) * 0.6) * 0.022;
  c += exp(-pow((uv.y + 0.15) / 0.75, 2.0)) * 0.05;
  float g = exp(-pow(length((uv - vec2(0.85, 0.02)) * vec2(asp * 0.7, 1.0)) / 0.7, 2.0));
  c = mix(c, glow.rgb * 0.45, g * 0.3);
  float g2 = exp(-pow(length((uv - vec2(0.12, 1.0)) * vec2(asp * 0.7, 1.0)) / 0.6, 2.0));
  c = mix(c, glow2.rgb * 0.35, g2 * 0.1);
  c += (hash(floor(px)) - 0.5) * 0.012;
  c *= 1.0 - 0.3 * pow(length(uv - vec2(0.5, 0.4)), 2.0);
  COLOR = vec4(c, 1.0);
}" };
}

/// <summary>Элемент, который рисует себя функцией: карточки, табло, полосы — всё, где нужна точная графика.</summary>
public partial class Paint : Control
{
    public Action<Paint> OnDraw;
    public Action<Paint, double> OnTick;
    public float T;   // время с появления — для анимации
    public Paint() { }
    public Paint(Vector2 min, Action<Paint> draw) { CustomMinimumSize = min; OnDraw = draw; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw() => OnDraw?.Invoke(this);
    public override void _Process(double d) { T += (float)d; if (OnTick != null) { OnTick(this, d); QueueRedraw(); } }

    public float Text(Font f, int size, float x, float top, string s, Color c, HorizontalAlignment al = HorizontalAlignment.Left, float w = -1)
    {
        DrawString(f, new Vector2(x, top + f.GetAscent(size)), s, al, w, size, c);
        return f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
    }
    /// <summary>Самый крупный кегль не больше max, при котором строка влезает в ширину.</summary>
    public static int Fit(Font f, string s, int max, float w, int min = 10)
    {
        int fs = max;
        while (fs > min && f.GetStringSize(s, HorizontalAlignment.Left, -1, fs).X > w) fs--;
        return fs;
    }
    /// <summary>Строка, обрезанная многоточием по ширине.</summary>
    public static string Trim(Font f, string s, int size, float w)
    {
        if (f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X <= w) return s;
        while (s.Length > 1 && f.GetStringSize(s + "…", HorizontalAlignment.Left, -1, size).X > w) s = s[..^1];
        return s.TrimEnd() + "…";
    }
}

/// <summary>Стеклянная панель с настоящим размытием фона (экранная текстура), скруглением и светлым краем.</summary>
public partial class GlassBox : Container
{
    readonly ColorRect _bg;
    readonly float _m;
    public GlassBox() : this(18) { }
    public GlassBox(float margin, float radius = Ds.R_L, float tintA = 0.05f, Color? glow = null)
    {
        _m = margin;
        _bg = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
        var m = new ShaderMaterial { Shader = Shader };
        m.SetShaderParameter("radius", radius);
        m.SetShaderParameter("tint", new Color(0.75f, 0.85f, 1f, tintA));
        m.SetShaderParameter("glow", glow ?? new Color(0, 0, 0, 0));
        _bg.Material = m;
        AddChild(_bg);
    }
    public void SetGlow(Color c) => (_bg.Material as ShaderMaterial)?.SetShaderParameter("glow", c);
    public override Vector2 _GetMinimumSize()
    {
        var s = Vector2.Zero;
        foreach (var ch in GetChildren().OfType<Control>().Where(x => x != _bg && x.Visible)) s = s.Max(ch.GetCombinedMinimumSize());
        return s + new Vector2(_m * 2, _m * 2);
    }
    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren) return;
        FitChildInRect(_bg, new Rect2(Vector2.Zero, Size));
        (_bg.Material as ShaderMaterial)?.SetShaderParameter("size", Size);
        foreach (var ch in GetChildren().OfType<Control>().Where(x => x != _bg)) FitChildInRect(ch, new Rect2(new Vector2(_m, _m), Size - new Vector2(_m * 2, _m * 2)));
    }

    static Shader _shader;
    static Shader Shader => _shader ??= new Shader { Code = @"shader_type canvas_item;
uniform sampler2D scr : hint_screen_texture, filter_linear_mipmap;
uniform vec2 size = vec2(100.0);
uniform float radius = 20.0;
uniform vec4 tint : source_color;
uniform vec4 glow : source_color;
float rbox(vec2 p, vec2 b, float r){ vec2 q = abs(p) - b + r; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r; }
void fragment(){
  vec2 p = UV * size - size * 0.5;
  float d = rbox(p, size * 0.5, radius);
  float a = 1.0 - smoothstep(-0.6, 0.6, d);
  vec3 b = textureLod(scr, SCREEN_UV, 3.2).rgb;
  vec3 c = mix(b, tint.rgb, tint.a) * 0.9 + tint.a * 0.35;
  c += (1.0 - UV.y) * 0.022;
  c = mix(c, glow.rgb, glow.a * exp(-pow(length((UV - vec2(0.15, 0.0)) * vec2(1.0, 1.4)) / 0.6, 2.0)));
  float edge = 1.0 - smoothstep(0.0, 1.3, abs(d + 0.7));
  c += edge * (0.13 - UV.y * 0.07);
  COLOR = vec4(c, a);
}" };
}

/// <summary>Лица и эмблемы внутри любой фигуры (круг, косой срез): текстура натягивается по UV.</summary>
public static class Face
{
    public static void TexInPoly(CanvasItem ci, Texture2D tex, Rect2 place, Vector2[] poly, Color? mod = null)
    {
        var uvs = poly.Select(p => (p - place.Position) / place.Size).ToArray();
        ci.DrawPolygon(poly, new[] { mod ?? Colors.White }, uvs, tex);
    }

    public static Vector2[] Circle(Vector2 c, float r, int n = 56)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        return pts;
    }

    /// <summary>Фото игрока внутри poly (голова в верхней части рамки); без фото — инициалы на цвете клуба.</summary>
    public static void Player(CanvasItem ci, PlayerData p, TeamData t, Rect2 box, Vector2[] poly, Font font = null, bool initials = true)
    {
        var tex = LogoCache.I?.Photo(p);
        if (tex != null)
        {
            var ts = tex.GetSize();
            // квадратная рамка (круг, аватар) — крупный план лица; вытянутая (полоса гола) — портрет по плечи
            bool close = Math.Abs(box.Size.X - box.Size.Y) < box.Size.Y * 0.2f;
            float sc = Math.Max(box.Size.X / ts.X, box.Size.Y / ts.Y) * (close ? 1.5f : 1.08f);
            var sz = ts * sc;
            var place = close
                ? new Rect2(box.Position + new Vector2((box.Size.X - sz.X) / 2, -sz.Y * 0.07f), sz)
                : new Rect2(box.Position + new Vector2((box.Size.X - sz.X) / 2, box.Size.Y - sz.Y + sz.Y * 0.04f), sz);
            ci.DrawColoredPolygon(poly, new Color(t.Colors[0]).Lerp(Ds.Bg, 0.6f));
            TexInPoly(ci, tex, place, poly);
            return;
        }
        ci.DrawColoredPolygon(poly, new Color(t.Colors[0]).Lerp(Ds.Bg, 0.45f));
        if (!initials) return;
        var w = p.Name.Split(' ');
        string ini = (w.FirstOrDefault() is { Length: > 0 } f ? f[..1] : "") + (w.LastOrDefault() is { Length: > 0 } l ? l[..1] : "");
        int fs = (int)(Math.Min(box.Size.X, box.Size.Y) * 0.34f);
        font ??= Ds.Display;
        float tw = font.GetStringSize(ini, HorizontalAlignment.Left, -1, fs).X;
        var c = box.GetCenter();
        ci.DrawString(font, new Vector2(c.X - tw / 2, c.Y + font.GetAscent(fs) * 0.36f), ini, HorizontalAlignment.Left, -1, fs, new Color(1, 1, 1, 0.55f));
    }

    /// <summary>Логотип клуба (вписать в прямоугольник); без логотипа — аббревиатура.</summary>
    public static void Logo(CanvasItem ci, TeamData t, Rect2 r, float alpha = 1, Font font = null)
    {
        var tex = LogoCache.I?.GetDark(t) ?? LogoCache.I?.Get(t);
        if (tex != null)
        {
            var ts = tex.GetSize(); float sc = Math.Min(r.Size.X / ts.X, r.Size.Y / ts.Y); var sz = ts * sc;
            ci.DrawTextureRect(tex, new Rect2(r.Position + (r.Size - sz) / 2, sz), false, new Color(1, 1, 1, alpha));
            return;
        }
        // без эмблемы — круг в цветах клуба с аббревиатурой (как TeamBadge)
        float d = Math.Min(r.Size.X, r.Size.Y);
        var c1 = new Color(t.Colors.Count > 0 ? t.Colors[0] : "#444444"); var c2 = new Color(t.Colors.Count > 1 ? t.Colors[1] : "#888888");
        ci.DrawCircle(r.GetCenter(), d / 2, new Color(c2, alpha));
        ci.DrawCircle(r.GetCenter(), d / 2 - Math.Max(2, d * 0.06f), new Color(c1, alpha));
        int fs = (int)(d * 0.3f);
        font ??= Ds.DisplayBold;
        float tw = font.GetStringSize(t.Abbr, HorizontalAlignment.Left, -1, fs).X;
        ci.DrawString(font, new Vector2(r.GetCenter().X - tw / 2, r.GetCenter().Y + font.GetAscent(fs) * 0.36f), t.Abbr, HorizontalAlignment.Left, -1, fs, new Color(Ui.ReadableOn(c1), alpha));
    }

    /// <summary>Мягкое свечение цвета (за эмблемой, за кольцом).</summary>
    public static void Glow(CanvasItem ci, Vector2 c, float r, Color col, float strength = 0.025f, int steps = 9)
    {
        for (int i = steps; i > 0; i--) ci.DrawCircle(c, r * (0.35f + 0.65f * i / steps), new Color(col, strength));
    }

    /// <summary>Кольцо рейтинга: дуга по доле шкалы 40–99 с разрывом снизу под плашку с числом.</summary>
    public static void Ring(CanvasItem ci, Vector2 c, float r, int ovr, float width = 3, bool pill = true, Font font = null)
    {
        var tier = Ds.Tier(ovr);
        float frac = Math.Clamp((ovr - 40) / 59f, 0, 1);
        float gap = pill ? 0.55f : 0;
        ci.DrawArc(c, r, Mathf.Pi / 2 + gap, Mathf.Pi / 2 + Mathf.Tau - gap, 64, new Color(1, 1, 1, 0.08f), width, true);
        ci.DrawArc(c, r, Mathf.Pi / 2 + gap, Mathf.Pi / 2 + gap + (Mathf.Tau - gap * 2) * frac, 64, tier, width, true);
        if (!pill) return;
        font ??= Ds.DisplayLight;
        int fs = Math.Max(11, (int)(r * 0.42f));
        float w = Math.Max(30, fs * 2.6f), h = fs + 7;
        var sb = new StyleBoxFlat { BgColor = Ds.Bg, BorderColor = new Color(tier, 0.55f) }; sb.SetCornerRadiusAll(99); sb.SetBorderWidthAll(1);
        var rect = new Rect2(c.X - w / 2, c.Y + r - h / 2 - 1, w, h);
        ci.DrawStyleBox(sb, rect);
        string s = ovr.ToString();
        float tw = font.GetStringSize(s, HorizontalAlignment.Left, -1, fs).X;
        ci.DrawString(font, new Vector2(c.X - tw / 2, rect.Position.Y + (h - fs) / 2 + font.GetAscent(fs) * 0.92f), s, HorizontalAlignment.Left, -1, fs, tier);
    }
}
