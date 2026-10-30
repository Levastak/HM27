using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI.Lab;

/// <summary>
/// Дизайн-лаборатория: два направления оформления на настоящих данных карьеры, чтобы выбрать стиль до переделки игры.
///   A «Трансляция» — графика ТВ-трансляций: узкий наклонный шрифт, цвета клуба во весь экран, косые срезы, табло.
///   B «Премиум» — спокойствие и воздух: широкий гротеск, стеклянные панели, тонкие кольца рейтинга, лёд под светом.
/// Общее: шкала рейтинга OVR 50–99 из КА (как в спортивных симуляторах) и четыре ступени цвета — бронза, серебро, золото, лёд.
/// </summary>
public sealed class Kit
{
    public bool A;
    public Color Bg, Panel, Panel2, Line, Text, Muted, Dim, Club, Club2, Accent;
    public Font Display, DisplayIt, Heavy, Body, BodySemi, BodyBold, Num;

    static readonly Dictionary<string, FontFile> Files = new();
    static FontFile File(string name)
    {
        if (Files.TryGetValue(name, out var f)) return f;
        f = GD.Load<FontFile>($"res://ui/fonts/{name}.ttf");
        f.Antialiasing = TextServer.FontAntialiasing.Gray;
        f.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        f.Hinting = TextServer.Hinting.Light;
        return Files[name] = f;
    }

    /// <summary>Вариативный шрифт с весом и моноширинными цифрами.</summary>
    public static Font V(string file, int wght)
    {
        var ts = TextServerManager.GetPrimaryInterface();
        return new FontVariation
        {
            BaseFont = File(file),
            VariationOpentype = new Godot.Collections.Dictionary { { ts.NameToTag("wght"), wght } },
            OpentypeFeatures = new Godot.Collections.Dictionary { { ts.NameToTag("tnum"), 1 } },
        };
    }

    public static Kit Make(bool a, TeamData team)
    {
        var c1 = new Color(team.Colors[0]);
        var c2 = new Color(team.Colors.Count > 1 ? team.Colors[1] : team.Colors[0]);
        // цвет клуба, который читается на тёмном фоне
        var club = c1;
        for (int i = 0; i < 6 && club.Luminance < 0.22f; i++) club = club.Lightened(0.12f);
        var club2 = c2.Luminance < 0.15f ? c2.Lightened(0.35f) : c2;
        if (a)
            return new Kit
            {
                A = true,
                Bg = new Color("07090D"), Panel = new Color("10141B"), Panel2 = new Color("171C25"), Line = new Color("232A35"),
                Text = new Color("F3F5F8"), Muted = new Color("98A2B3"), Dim = new Color("5D6778"),
                Club = club, Club2 = club2, Accent = club,
                Display = V("SofiaSansExtraCondensed-VF", 800), DisplayIt = V("SofiaSansExtraCondensed-Italic-VF", 900),
                Heavy = V("SofiaSansExtraCondensed-Italic-VF", 700),
                Body = V("Onest-VF", 400), BodySemi = V("Onest-VF", 600), BodyBold = V("Onest-VF", 700),
                Num = V("SofiaSansExtraCondensed-VF", 900),
            };
        return new Kit
        {
            A = false,
            Bg = new Color("0B1119"), Panel = new Color(1, 1, 1, 0.045f), Panel2 = new Color(1, 1, 1, 0.07f), Line = new Color(1, 1, 1, 0.09f),
            Text = new Color("EEF2F7"), Muted = new Color("9AA6B6"), Dim = new Color("5F6B7C"),
            Club = club, Club2 = club2, Accent = club,
            Display = V("Unbounded-VF", 600), DisplayIt = V("Unbounded-VF", 300), Heavy = V("Unbounded-VF", 700),
            Body = V("Onest-VF", 400), BodySemi = V("Onest-VF", 500), BodyBold = V("Onest-VF", 600),
            Num = V("Unbounded-VF", 300),
        };
    }

    // ------------------------------------------------------------------ рейтинг
    /// <summary>OVR 50–99 из КА (1–200): медиана лиги ~69, звёзды 88+.</summary>
    public static int Ovr(Career c, PlayerData p) => (int)Math.Round(Math.Clamp(50 + (PlayerModel.Visible(c, p, "ca") - 74) / 114.0 * 47, 40, 99));

    /// <summary>Ступени: бронза &lt;65, серебро 65–74, золото 75–84, лёд 85+.</summary>
    public static Color Tier(int ovr) => ovr >= 85 ? new Color("7FE6FF") : ovr >= 75 ? new Color("F5C24C") : ovr >= 65 ? new Color("C9D3DE") : new Color("C48A5A");
    public static string TierName(int ovr) => ovr >= 85 ? "элита" : ovr >= 75 ? "золото" : ovr >= 65 ? "серебро" : "бронза";

    /// <summary>Характеристика 1–20 — пять ступеней от тусклого к яркому, верх — золото.</summary>
    public static Color Attr(double v) => v >= 18 ? new Color("F5C24C") : v >= 15 ? new Color("5BE0A0") : v >= 11 ? new Color("E6EDF5") : v >= 7 ? new Color("8E99AB") : new Color("5B6577");

    // ------------------------------------------------------------------ фабрики
    public Label L(string text, int size, Color? color = null, Font font = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Text);
        l.AddThemeFontOverride("font", font ?? Body);
        return l;
    }

    public StyleBoxFlat Glass(int radius = 18, float alpha = 0.045f, bool border = true)
    {
        var s = new StyleBoxFlat { BgColor = new Color(1, 1, 1, alpha) };
        s.SetCornerRadiusAll(radius);
        if (border) { s.BorderColor = new Color(1, 1, 1, 0.08f); s.SetBorderWidthAll(1); s.BorderWidthTop = 1; }
        s.ShadowColor = new Color(0, 0, 0, 0.35f); s.ShadowSize = 24; s.ShadowOffset = new Vector2(0, 10);
        s.ContentMarginLeft = s.ContentMarginRight = 22; s.ContentMarginTop = s.ContentMarginBottom = 18;
        return s;
    }

    public StyleBoxFlat Slab(Color bg, int pad = 16)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.ContentMarginLeft = s.ContentMarginRight = pad; s.ContentMarginTop = s.ContentMarginBottom = pad - 4;
        return s;
    }

    public static T Also<T>(T x, Action<T> a) { a(x); return x; }
}

/// <summary>Фон-шейдер: A — тёмная студия с косой штриховкой, свечением клуба и бегущим бликом; B — лёд под светом арены.</summary>
public partial class LabBg : ColorRect
{
    public LabBg(Kit k)
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var sh = new Shader { Code = k.A ? CodeA : CodeB };
        var m = new ShaderMaterial { Shader = sh };
        m.SetShaderParameter("base_col", k.Bg);
        m.SetShaderParameter("glow", k.Club);
        m.SetShaderParameter("glow2", k.Club2);
        Material = m;
    }

    public override void _Process(double delta) => (Material as ShaderMaterial)?.SetShaderParameter("size", Size);

    const string Noise = @"
float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p){ vec2 i = floor(p), f = fract(p); vec2 u = f*f*(3.0-2.0*f);
  return mix(mix(hash(i), hash(i+vec2(1,0)), u.x), mix(hash(i+vec2(0,1)), hash(i+vec2(1,1)), u.x), u.y); }
";

    const string CodeA = @"shader_type canvas_item;
uniform vec4 base_col : source_color; uniform vec4 glow : source_color; uniform vec4 glow2 : source_color; uniform vec2 size = vec2(1600.0, 900.0);
" + Noise + @"
void fragment(){
  vec2 uv = UV; vec2 px = UV * size; float asp = size.x / size.y;
  vec3 c = base_col.rgb;
  float g = exp(-pow(length((uv - vec2(0.0, -0.05)) * vec2(asp * 0.75, 1.0)) / 0.9, 2.0));
  c = mix(c, glow.rgb * 0.55, g * 0.55);
  float g2 = exp(-pow(length((uv - vec2(1.05, 1.1)) * vec2(asp * 0.6, 1.0)) / 0.7, 2.0));
  c = mix(c, glow2.rgb * 0.5, g2 * 0.22);
  float st = step(0.5, fract((px.x + px.y) / 9.0));
  c += st * 0.009;
  float sweep = smoothstep(0.18, 0.0, abs(fract(TIME * 0.025) * 2.2 - 0.6 - (uv.x - uv.y * 0.45)));
  c += sweep * glow.rgb * 0.05;
  c += (hash(floor(px)) - 0.5) * 0.022;
  c *= 1.0 - 0.45 * pow(length((uv - vec2(0.5, 0.45)) * vec2(1.0, 1.1)), 2.2);
  COLOR = vec4(c, 1.0);
}";

    const string CodeB = @"shader_type canvas_item;
uniform vec4 base_col : source_color; uniform vec4 glow : source_color; uniform vec4 glow2 : source_color; uniform vec2 size = vec2(1600.0, 900.0);
" + Noise + @"
float streaks(vec2 p, float ang, float sc){
  float s = sin(ang), co = cos(ang); vec2 q = vec2(co * p.x - s * p.y, s * p.x + co * p.y);
  float n = vnoise(vec2(q.x * sc * 0.02, q.y * sc));
  return smoothstep(0.82, 1.0, n);
}
void fragment(){
  vec2 uv = UV; vec2 px = UV * size; float asp = size.x / size.y;
  vec3 c = base_col.rgb;
  // лёд: мягкая неровность и царапины коньков в разных направлениях
  float frost = vnoise(px / 180.0) * 0.6 + vnoise(px / 45.0) * 0.4;
  c += (frost - 0.5) * 0.03;
  float sc = streaks(px, 0.35, 0.9) + streaks(px + 333.0, -0.6, 0.7) + streaks(px + 777.0, 1.3, 1.1) * 0.6;
  c += sc * 0.022;
  // свет арены сверху и цвет клуба
  float top = exp(-pow((uv.y + 0.15) / 0.75, 2.0));
  c += top * 0.05;
  float g = exp(-pow(length((uv - vec2(0.82, 0.08)) * vec2(asp * 0.7, 1.0)) / 0.7, 2.0));
  c = mix(c, glow.rgb * 0.45, g * 0.35);
  float g2 = exp(-pow(length((uv - vec2(0.1, 1.0)) * vec2(asp * 0.7, 1.0)) / 0.6, 2.0));
  c = mix(c, glow2.rgb * 0.35, g2 * 0.12);
  c += (hash(floor(px)) - 0.5) * 0.012;
  c *= 1.0 - 0.3 * pow(length(uv - vec2(0.5, 0.4)), 2.0);
  COLOR = vec4(c, 1.0);
}";
}

/// <summary>Логотип клуба огромным полупрозрачным водяным знаком.</summary>
public partial class Watermark : Control
{
    readonly TeamData _t; readonly float _alpha;
    public Watermark(TeamData t, float alpha) { _t = t; _alpha = alpha; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Ready() { if (LogoCache.I != null) LogoCache.I.Loaded += _ => QueueRedraw(); }
    public override void _Draw()
    {
        var tex = LogoCache.I?.Get(_t);
        if (tex == null) return;
        var ts = tex.GetSize();
        float k = Math.Min(Size.X / ts.X, Size.Y / ts.Y);
        var sz = ts * k;
        DrawTextureRect(tex, new Rect2((Size - sz) / 2, sz), false, new Color(1, 1, 1, _alpha));
    }
}

/// <summary>Рисование лиц и логотипов внутри произвольной фигуры (косой срез, круг): текстура натягивается по UV.</summary>
public static class Face
{
    /// <summary>Текстура, вписанная «с заполнением» в прямоугольник place, рисуется только внутри многоугольника poly.</summary>
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

    /// <summary>Фото игрока внутри poly; без фото — инициалы на цвете клуба. Фото подрезано так, чтобы голова была в верхней трети.</summary>
    public static void Player(CanvasItem ci, Kit k, PlayerData p, TeamData t, Rect2 box, Vector2[] poly, bool initials = true)
    {
        var tex = LogoCache.I?.Photo(p);
        if (tex != null)
        {
            var ts = tex.GetSize();
            float sc = Math.Max(box.Size.X / ts.X, box.Size.Y / ts.Y) * 1.08f;
            var sz = ts * sc;
            var place = new Rect2(box.Position + new Vector2((box.Size.X - sz.X) / 2, box.Size.Y - sz.Y + sz.Y * 0.04f), sz);
            TexInPoly(ci, tex, place, poly);
            return;
        }
        var jersey = new Color(t.Colors[0]);
        ci.DrawColoredPolygon(poly, jersey.Lerp(k.Bg, 0.45f));
        if (!initials) return;
        string ini = (p.Name.Split(' ').FirstOrDefault() is { Length: > 0 } f ? f[..1] : "") + (p.Name.Split(' ').LastOrDefault() is { Length: > 0 } l ? l[..1] : "");
        int fs = (int)(Math.Min(box.Size.X, box.Size.Y) * 0.34f);
        var font = k.A ? k.DisplayIt : k.Display;
        float tw = font.GetStringSize(ini, HorizontalAlignment.Left, -1, fs).X;
        var c = box.GetCenter();
        ci.DrawString(font, new Vector2(c.X - tw / 2, c.Y + font.GetAscent(fs) * 0.36f), ini, HorizontalAlignment.Left, -1, fs, new Color(1, 1, 1, 0.55f));
    }

    /// <summary>Логотип клуба в прямоугольнике (вписать); без логотипа — аббревиатура.</summary>
    public static void Logo(CanvasItem ci, Kit k, TeamData t, Rect2 r, float alpha = 1)
    {
        var tex = LogoCache.I?.Get(t);
        if (tex != null)
        {
            var ts = tex.GetSize(); float sc = Math.Min(r.Size.X / ts.X, r.Size.Y / ts.Y); var sz = ts * sc;
            ci.DrawTextureRect(tex, new Rect2(r.Position + (r.Size - sz) / 2, sz), false, new Color(1, 1, 1, alpha));
            return;
        }
        int fs = (int)(r.Size.Y * 0.42f);
        var font = k.A ? k.DisplayIt : k.Heavy;
        float tw = font.GetStringSize(t.Abbr, HorizontalAlignment.Left, -1, fs).X;
        ci.DrawString(font, new Vector2(r.GetCenter().X - tw / 2, r.GetCenter().Y + font.GetAscent(fs) * 0.36f), t.Abbr, HorizontalAlignment.Left, -1, fs, new Color(1, 1, 1, 0.9f * alpha));
    }
}
