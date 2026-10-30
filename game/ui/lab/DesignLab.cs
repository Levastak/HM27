using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI.Lab;

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
}

/// <summary>Стеклянная панель: размытый фон под ней (экранная текстура), скругление, тонкий светлый край.</summary>
public partial class GlassBox : Container
{
    readonly ColorRect _bg;
    readonly float _m;
    public GlassBox(Kit k, float margin = 22, float radius = 20, float tintA = 0.05f, Color? glow = null)
    {
        _m = margin;
        _bg = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
        var m = new ShaderMaterial { Shader = new Shader { Code = Code } };
        m.SetShaderParameter("radius", radius);
        m.SetShaderParameter("tint", new Color(0.75f, 0.85f, 1f, tintA));
        m.SetShaderParameter("glow", glow ?? new Color(0, 0, 0, 0));
        _bg.Material = m;
        AddChild(_bg);
    }
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

    const string Code = @"shader_type canvas_item;
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
}";
}

/// <summary>
/// Экран лаборатории: оболочка направления (A — табло-рейка слева, B — боковая панель) и страница «Звенья» или «Портал».
/// Всё на настоящих данных карьеры; лица и логотипы — из вшитого пака, пока его нет — инициалы и аббревиатуры.
/// </summary>
public partial class DesignLab : Control
{
    readonly Career C;
    readonly Kit K;
    readonly TeamData My;
    readonly string _page;
    readonly List<Control> _reveal = new();

    public DesignLab(Career c, bool a, string page)
    {
        C = c; My = c.Team(c.UserTeam); K = Kit.Make(a, My); _page = page;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = K.Bg, MouseFilter = MouseFilterEnum.Ignore }.Also(r => r.SetAnchorsPreset(LayoutPreset.FullRect)));
        AddChild(new LabBg(K));
        var root = new HBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);
        root.AddChild(K.A ? RailA() : SideB());
        var main = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        main.AddThemeConstantOverride("separation", 0);
        root.AddChild(main);
        if (K.A) main.AddChild(HeaderA(_page == "lines" ? "Звенья" : "Портал"));
        var body = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        int pad = K.A ? 24 : 32;
        body.AddThemeConstantOverride("margin_left", pad); body.AddThemeConstantOverride("margin_right", pad);
        body.AddThemeConstantOverride("margin_top", K.A ? 18 : 34); body.AddThemeConstantOverride("margin_bottom", K.A ? 18 : 30);
        main.AddChild(body);
        body.AddChild(_page == "lines" ? (K.A ? LinesA() : LinesB()) : (K.A ? HomeA() : HomeB()));
        if (K.A && _page != "lines") main.AddChild(TickerA());
        // одно появление на весь экран: карточки проявляются волной слева направо
        for (int i = 0; i < _reveal.Count; i++)
        {
            var x = _reveal[i];
            x.Modulate = new Color(1, 1, 1, 0);
            var tw = CreateTween();
            tw.TweenInterval(0.05 + i * 0.025);
            tw.TweenProperty(x, "modulate:a", 1.0, 0.28).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
    }

    // ================================================================== общие данные
    PlayerData P(int id) => id != 0 && C.Data.PlayerById.TryGetValue(id, out var p) ? p : null;
    static string PosRu(string pos) => pos switch { "C" => "ЦН", "LW" => "ЛН", "RW" => "ПН", "D" => "З", "G" => "ВР", _ => pos };
    static readonly Dictionary<string, string> Short = new()
    {
        ["fin"] = "Завершение", ["pwr"] = "Мощь", ["lng"] = "Издали", ["net"] = "У ворот", ["pas"] = "Пас", ["hnd"] = "Шайба", ["fo"] = "Вбрасыв.",
        ["dfn"] = "Позиция", ["tka"] = "Отбор", ["blk"] = "Блоки", ["hit"] = "Силовые", ["pk"] = "Меньшинство", ["spd"] = "Скорость", ["acc"] = "Ускорение",
        ["str"] = "Сила", ["dec"] = "Решения", ["vis"] = "Видение", ["wrk"] = "Труд", ["pp"] = "Большинство",
        ["rfx"] = "Реакция", ["gpos"] = "Позиция", ["reb"] = "Отскок", ["scr"] = "Заслон",
    };
    static readonly string[] GKeys = { "rfx", "gpos", "reb", "scr" };
    List<(string key, double v)> Top(PlayerData p, int n) =>
        (p.IsGoalie ? GKeys : Short.Keys.Except(GKeys)).Select(k => (k, PlayerModel.Visible(C, p, k))).OrderByDescending(x => x.Item2).Take(n).ToList();
    float Fresh(int id) => PlayerModel.State(C, id)?.Fresh ?? 100;
    static Color FreshCol(float f) => f >= 85 ? new Color("5BE0A0") : f >= 70 ? new Color("F5C24C") : new Color("FF5A5F");
    string RoleOf(PlayerData p) => p.Roles is { Count: > 0 } ? p.Roles[0].Name : PosRu(p.Pos);
    Lineup L => C.Lineups[C.UserTeam];
    int TeamOvr() { var ids = L.Skaters.Append(L.G1).Select(P).Where(p => p != null).ToList(); return (int)Math.Round(ids.Average(p => Kit.Ovr(C, p))); }
    int Place() => C.Sorted(t => t.Division == My.Division).FindIndex(r => r.Team == My.Abbr) + 1;
    string Rec() { var r = C.Table[My.Abbr]; return $"{r.W}-{r.L}-{r.Otl}"; }
    string DivName() => My.Division switch { "Central" => "Центральном", "Pacific" => "Тихоокеанском", "Atlantic" => "Атлантическом", "Metropolitan" => "Столичном", _ => My.Division };

    T Rev<T>(T c) where T : Control { _reveal.Add(c); return c; }

    // ================================================================== A «Трансляция»: оболочка
    Control RailA()
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(92, 0) };
        v.AddThemeConstantOverride("separation", 0);
        var bg = new Paint(new Vector2(92, 0), p => p.DrawRect(new Rect2(Vector2.Zero, p.Size), new Color("05070A")));
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        var wrap = new Control { CustomMinimumSize = new Vector2(92, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        wrap.AddChild(bg);
        // эмблема на плашке цвета клуба — продолжение шапки
        v.AddChild(new Paint(new Vector2(92, 84), p =>
        {
            p.DrawRect(new Rect2(0, 0, 92, 84), K.Club.Darkened(0.25f));
            Face.Logo(p, K, My, new Rect2(14, 10, 64, 64));
        }));
        var items = new (string icon, string label, bool on)[]
        {
            ("house", "Портал", _page != "lines"), ("users", "Команда", _page == "lines"), ("dumbbell", "Тренировки", false), ("calendar-days", "Календарь", false),
            ("handshake", "Сделки", false), ("binoculars", "Скаутинг", false), ("landmark", "Клуб", false), ("trophy", "Лига", false),
        };
        foreach (var it in items)
            v.AddChild(new Paint(new Vector2(92, 74), p =>
            {
                if (it.on)
                {
                    p.DrawRect(new Rect2(0, 0, 92, 74), new Color(1, 1, 1, 0.05f));
                    p.DrawColoredPolygon(new[] { new Vector2(0, 8), new Vector2(5, 4), new Vector2(5, 70), new Vector2(0, 66) }, K.Club);
                }
                var tex = Icons.Get(it.icon);
                var col = it.on ? K.Text : K.Dim;
                if (tex != null) p.DrawTextureRect(tex, new Rect2(34, 14, 24, 24), false, col);
                p.Text(K.BodySemi, 11, 0, 44, it.label, col, HorizontalAlignment.Center, 92);
            }));
        wrap.AddChild(v);
        return wrap;
    }

    /// <summary>Шапка-табло: полоса цвета клуба со штриховкой, название раздела, рекорд, дата и «Продолжить».</summary>
    Control HeaderA(string title)
    {
        var row = C.Table[My.Abbr];
        var p = new Paint(new Vector2(0, 84), q =>
        {
            float w = q.Size.X, h = q.Size.Y;
            var c0 = K.Club.Darkened(0.25f); var c1 = K.Club.Darkened(0.62f);
            q.DrawPolygon(new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) }, new[] { c0, c1, c1, c0 });
            for (float x = -h; x < w; x += 14) q.DrawLine(new Vector2(x, h), new Vector2(x + h, 0), new Color(1, 1, 1, 0.035f), 5);
            q.DrawRect(new Rect2(0, h - 3, w, 3), K.Club2);
            q.Text(K.Body, 14, 28, 12, My.Name, new Color(1, 1, 1, 0.75f));
            q.Text(K.DisplayIt, 46, 26, 24, title.ToUpperInvariant(), Colors.White);
            // справа: рекорд и место, дата, кнопка
            float bx = w - 236;
            var btn = new[] { new Vector2(bx + 14, 18), new Vector2(w - 24, 18), new Vector2(w - 38, h - 18), new Vector2(bx, h - 18) };
            q.DrawColoredPolygon(btn, Colors.White);
            q.Text(K.DisplayIt, 24, bx + 6, 27, "ПРОДОЛЖИТЬ", K.Club.Darkened(0.35f), HorizontalAlignment.Center, w - 24 - bx - 6 - 14);
            float x2 = bx - 30;
            string date = Ui.Date(C.Today);
            float dw = K.Body.GetStringSize(date, HorizontalAlignment.Left, -1, 14).X;
            q.Text(K.Body, 14, x2 - dw, 20, date, new Color(1, 1, 1, 0.75f));
            q.Text(K.Body, 14, x2 - dw, 42, "сезон 2026-27", new Color(1, 1, 1, 0.45f));
            x2 -= dw + 40;
            string rec = Rec(); float rw = K.Num.GetStringSize(rec, HorizontalAlignment.Left, -1, 40).X;
            q.Text(K.Num, 40, x2 - rw, 14, rec, Colors.White);
            string pl = $"{Place()}-е в {DivName()}";
            float pw = K.Body.GetStringSize(pl, HorizontalAlignment.Left, -1, 13).X;
            q.Text(K.Body, 13, x2 - pw, 58, pl, new Color(1, 1, 1, 0.6f));
        });
        return p;
    }

    // ================================================================== A: звенья
    const float AW = 244, AH = 146;

    Control LinesA()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 14);
        // вкладки и сводка
        v.AddChild(new Paint(new Vector2(0, 40), q =>
        {
            float x = 0;
            foreach (var (t, on) in new[] { ("Пятёрки", true), ("Доска", false), ("Спецбригады", false), ("Вратари", false) })
            {
                float tw = q.Text(K.Heavy, 22, x, 4, t.ToUpperInvariant(), on ? K.Text : K.Dim);
                if (on) q.DrawColoredPolygon(new[] { new Vector2(x + 3, 34), new Vector2(x + tw + 3, 34), new Vector2(x + tw, 38), new Vector2(x, 38) }, K.Club);
                x += tw + 34;
            }
            float w = q.Size.X;
            int ovr = TeamOvr(); double chem = Enumerable.Range(0, L.F.Count).Average(i => C.UnitFam(L.F[i]));
            string s1 = $"{ovr}", s2 = $"{Math.Round(chem * 100)}%";
            float a = K.Num.GetStringSize(s2, HorizontalAlignment.Left, -1, 34).X;
            q.Text(K.Num, 34, w - a, -2, s2, K.Text);
            q.Text(K.Body, 12, w - a - 92, 12, "сыгранность", K.Muted);
            float b = K.Num.GetStringSize(s1, HorizontalAlignment.Left, -1, 34).X;
            q.Text(K.Num, 34, w - a - 120 - b, -2, s1, Kit.Tier(ovr));
            q.Text(K.Body, 12, w - a - 120 - b - 92, 12, "общий рейтинг", K.Muted);
        }));
        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 34);
        v.AddChild(cols);
        var fwd = new VBoxContainer(); fwd.AddThemeConstantOverride("separation", 12);
        var dfn = new VBoxContainer(); dfn.AddThemeConstantOverride("separation", 12);
        cols.AddChild(fwd); cols.AddChild(dfn);
        for (int i = 0; i < Math.Min(4, L.F.Count); i++) fwd.AddChild(UnitRowA((i + 1).ToString(), L.F[i], L.RoleF(i)));
        for (int i = 0; i < Math.Min(3, L.D.Count); i++) dfn.AddChild(UnitRowA((i + 1).ToString(), L.D[i], L.RoleD(i)));
        dfn.AddChild(UnitRowA("В", new[] { L.G1, L.G2 }, -1));
        return v;
    }

    Control UnitRowA(string num, int[] ids, int role)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 10);
        bool goalies = num == "В";
        double fam = goalies ? 0 : C.UnitFam(ids);
        h.AddChild(new Paint(new Vector2(58, AH), q =>
        {
            q.Text(K.DisplayIt, 76, 0, -8, num, goalies ? K.Dim : K.Club);
            if (goalies) return;
            // сыгранность — пять сегментов как индикатор сигнала
            int segs = (int)Math.Round(fam * 5);
            for (int s = 0; s < 5; s++)
                q.DrawColoredPolygon(new[] { new Vector2(4 + s * 10, 84), new Vector2(12 + s * 10, 84), new Vector2(10 + s * 10, 92), new Vector2(2 + s * 10, 92) },
                    s < segs ? FamCol(fam) : new Color(1, 1, 1, 0.1f));
            q.Text(K.Body, 11, 2, 96, $"сыгр. {Math.Round(fam * 100)}%", K.Muted);
            if (role > 0) q.Text(K.Heavy, 13, 2, 116, role == 1 ? "АТАКА" : "ОБОРОНА", role == 1 ? new Color("F5C24C") : new Color("7FE6FF"));
        }));
        foreach (var id in ids) h.AddChild(Rev(CardA(P(id))));
        return h;
    }

    static Color FamCol(double f) => f >= 0.8 ? new Color("5BE0A0") : f >= 0.7 ? new Color("E6EDF5") : new Color("F5C24C");

    Control CardA(PlayerData p) => new Paint(new Vector2(AW, AH), q =>
    {
        float w = AW, h = AH, cut = 20;
        var poly = new[] { new Vector2(0, 0), new Vector2(w - cut, 0), new Vector2(w, cut), new Vector2(w, h), new Vector2(0, h) };
        q.DrawColoredPolygon(poly, K.Panel);
        if (p == null) { q.Text(K.Heavy, 18, 0, h / 2 - 12, "ПУСТО", K.Dim, HorizontalAlignment.Center, w); return; }
        var t = C.Team(p.Team);
        // портрет справа — в косом срезе карточки, растворяется влево
        float px = w * 0.5f;
        var pp = new[] { new Vector2(px, 0), new Vector2(w - cut, 0), new Vector2(w, cut), new Vector2(w, h), new Vector2(px, h) };
        Face.Player(q, K, p, t, new Rect2(px, 0, w - px, h), pp);
        var a0 = new Color(K.Panel, 1); var a1 = new Color(K.Panel, 0);
        q.DrawPolygon(new[] { new Vector2(px, 0), new Vector2(px + 70, 0), new Vector2(px + 70, h), new Vector2(px, h) }, new[] { a0, a1, a1, a0 });
        // номер на фоне
        if (p.Num is int n) { int fs = 70; string s = n.ToString(); float tw = K.DisplayIt.GetStringSize(s, HorizontalAlignment.Left, -1, fs).X; q.Text(K.DisplayIt, fs, w - tw - 8, -6, s, new Color(1, 1, 1, 0.07f)); }
        // кромка цвета клуба
        q.DrawRect(new Rect2(0, 0, 4, h), K.Club);
        int ovr = Kit.Ovr(C, p); var tier = Kit.Tier(ovr);
        q.Text(K.Num, 50, 16, 2, ovr.ToString(), tier);
        // позиция — плашка
        string pos = PosRu(p.Pos);
        q.DrawColoredPolygon(new[] { new Vector2(76, 14), new Vector2(108, 14), new Vector2(104, 32), new Vector2(72, 32) }, new Color(1, 1, 1, 0.1f));
        q.Text(K.Heavy, 15, 72, 14, pos, K.Text, HorizontalAlignment.Center, 36);
        q.Text(K.Body, 11, 74, 37, RoleOf(p), K.Muted);
        // три сильные стороны
        float x = 16;
        foreach (var (key, v) in Top(p, 2))
        {
            string val = Math.Round(v).ToString();
            q.Text(K.Heavy, 17, x, 62, val, Kit.Attr(v));
            float vw = K.Heavy.GetStringSize(val, HorizontalAlignment.Left, -1, 17).X;
            x += q.Text(K.Body, 10, x + vw + 3, 67, Short[key], K.Muted) + vw + 12;
            
        }
        // имя
        var parts = p.NameRu.Split(' ', 2);
        q.Text(K.Body, 12, 16, 88, parts.Length > 1 ? parts[0] : "", new Color(K.Text, 0.7f));
        string last = (parts.Length > 1 ? parts[1] : parts[0]).ToUpperInvariant();
        int ls = Paint.Fit(K.DisplayIt, last, 32, w * 0.66f);
        q.Text(K.DisplayIt, ls, 15, 100 + (32 - ls) * 0.6f, last, K.Text);
        // свежесть — полоса по низу
        float f = Fresh(p.Id);
        q.DrawRect(new Rect2(4, h - 4, w - 4, 4), new Color(1, 1, 1, 0.07f));
        q.DrawRect(new Rect2(4, h - 4, (w - 4) * f / 100f, 4), FreshCol(f));
    });

    // ================================================================== A: портал
    Control HomeA()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 18);
        v.AddChild(Rev(MatchupA()));
        var h = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        h.AddThemeConstantOverride("separation", 18);
        v.AddChild(h);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 18);
        left.AddChild(Rev(LeadA()).Also(x => x.SizeFlagsVertical = SizeFlags.ExpandFill));
        left.AddChild(LeadersA());
        h.AddChild(left);
        h.AddChild(Rev(TableA()));
        return v;
    }

    Control MatchupA()
    {
        var g = C.NextGame(C.UserTeam);
        bool home = g.Home == C.UserTeam;
        var ht = C.Team(g.Home); var at = C.Team(g.Away);
        var o = C.Preview(g);
        var picks = News.Picks(C, g);
        int dd = g.Date.DayNumber - C.Today.DayNumber;
        return new Paint(new Vector2(0, 318), q =>
        {
            float w = q.Size.X, h = q.Size.Y - 58;
            Color hc = Readable(ht), ac = Readable(at);
            float mid = w * 0.5f, sk = 70;
            q.DrawPolygon(new[] { new Vector2(0, 0), new Vector2(mid + sk, 0), new Vector2(mid - sk, h), new Vector2(0, h) }, new[] { hc.Darkened(0.35f), hc.Darkened(0.05f), hc.Darkened(0.45f), hc.Darkened(0.7f) });
            q.DrawPolygon(new[] { new Vector2(mid + sk, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(mid - sk, h) }, new[] { ac.Darkened(0.05f), ac.Darkened(0.35f), ac.Darkened(0.7f), ac.Darkened(0.45f) });
            for (float x = -h; x < w; x += 16) q.DrawLine(new Vector2(x, h), new Vector2(x + h, 0), new Color(1, 1, 1, 0.03f), 6);
            // бегущий блик по диагонали
            float sx = (q.T * 220) % (w + 600) - 300;
            q.DrawPolygon(new[] { new Vector2(sx, 0), new Vector2(sx + 90, 0), new Vector2(sx - 30, h), new Vector2(sx - 120, h) },
                new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 0.07f), new Color(1, 1, 1, 0.07f), new Color(1, 1, 1, 0) });
            // разделитель
            q.DrawLine(new Vector2(mid + sk, 0), new Vector2(mid - sk, h), new Color(1, 1, 1, 0.9f), 3);
            Face.Logo(q, K, ht, new Rect2(w * 0.16f - 100, h / 2 - 108, 200, 170));
            Face.Logo(q, K, at, new Rect2(w * 0.84f - 100, h / 2 - 108, 200, 170));
            q.Text(K.DisplayIt, 34, w * 0.16f - 200, h - 70, ht.Name.ToUpperInvariant(), Colors.White, HorizontalAlignment.Center, 400);
            q.Text(K.DisplayIt, 34, w * 0.84f - 200, h - 70, at.Name.ToUpperInvariant(), Colors.White, HorizontalAlignment.Center, 400);
            q.Text(K.Body, 13, w * 0.16f - 200, h - 32, $"{C.Table[ht.Abbr].W}-{C.Table[ht.Abbr].L}-{C.Table[ht.Abbr].Otl}", new Color(1, 1, 1, 0.7f), HorizontalAlignment.Center, 400);
            q.Text(K.Body, 13, w * 0.84f - 200, h - 32, $"{C.Table[at.Abbr].W}-{C.Table[at.Abbr].L}-{C.Table[at.Abbr].Otl}", new Color(1, 1, 1, 0.7f), HorizontalAlignment.Center, 400);
            // центр: плашка с датой
            float bw = 300, bh = 96, bx = mid - bw / 2, by = h / 2 - bh / 2;
            q.DrawColoredPolygon(new[] { new Vector2(bx + 16, by), new Vector2(bx + bw, by), new Vector2(bx + bw - 16, by + bh), new Vector2(bx, by + bh) }, new Color("05070A"));
            q.Text(K.Body, 12, bx, by + 12, dd == 0 ? "сегодня" : dd == 1 ? "завтра" : Ui.Date(g.Date), K.Muted, HorizontalAlignment.Center, bw);
            q.Text(K.DisplayIt, 48, bx, by + 30, "19:00", Colors.White, HorizontalAlignment.Center, bw);
            // нижняя полоса: шансы и эксперты
            float y = h;
            q.DrawRect(new Rect2(0, y, w, 58), new Color("05070A"));
            float ph = (float)o.PHome;
            string l = $"{Math.Round(ph * 100)}%", r = $"{Math.Round((1 - ph) * 100)}%";
            q.Text(K.Num, 30, 20, y + 10, l, Colors.White);
            float barX = 90, barW = w * 0.42f;
            q.DrawRect(new Rect2(barX, y + 26, barW * ph, 8), hc);
            q.DrawRect(new Rect2(barX + barW * ph + 3, y + 26, barW * (1 - ph) - 3, 8), ac);
            q.Text(K.Num, 30, barX + barW + 12, y + 10, r, Colors.White);
            q.Text(K.Body, 11, barX, y + 38, "шансы на победу по оценке аналитиков", K.Dim);
            float ex = w - 24;
            foreach (var pk in picks.AsEnumerable().Reverse())
            {
                var t = C.Team(pk.Team);
                Face.Logo(q, K, t, new Rect2(ex - 30, y + 14, 30, 30));
                ex -= 36;
                var col = new Color(pk.Who.Color);
                q.DrawCircle(new Vector2(ex - 14, y + 29), 15, col.Darkened(0.2f));
                string ini = string.Concat(pk.Who.Name.Split(' ').Select(s => s[..1]));
                q.Text(K.BodyBold, 12, ex - 29, y + 21, ini, Colors.White, HorizontalAlignment.Center, 30);
                ex -= 44;
            }
            q.Text(K.Body, 12, ex - 140, y + 21, "прогнозы экспертов", K.Muted, HorizontalAlignment.Right, 140);
        }) { OnTick = (_, _) => { } };
    }

    static Color Readable(TeamData t)
    {
        var c = new Color(t.Colors[0]);
        if (c.Luminance < 0.08f && t.Colors.Count > 1) c = new Color(t.Colors[1]);
        return c.Luminance < 0.1f ? c.Lightened(0.2f) : c;
    }

    Message LeadMsg() => C.Messages.Where(x => x.Lead != null).OrderByDescending(x => x.Weight * 10 - (C.Today.DayNumber - x.Date.DayNumber) * 2).ThenByDescending(x => x.Id).FirstOrDefault();

    Control LeadA()
    {
        var m = LeadMsg();
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", K.Slab(K.Panel, 26));
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 10);
        box.AddChild(v);
        if (m == null) return box;
        var tag = new Paint(new Vector2(0, 26), q =>
        {
            string s = (m.Tag ?? "Новости").ToUpperInvariant();
            float tw = K.Heavy.GetStringSize(s, HorizontalAlignment.Left, -1, 16).X + 26;
            q.DrawColoredPolygon(new[] { new Vector2(8, 0), new Vector2(tw + 8, 0), new Vector2(tw, 26), new Vector2(0, 26) }, K.Club);
            q.Text(K.Heavy, 16, 4, 3, s, Ui.ReadableOn(K.Club), HorizontalAlignment.Center, tw);
            q.Text(K.Body, 12, tw + 22, 5, $"{m.From}  ·  {Ui.Date(m.Date, false)}", K.Muted);
        });
        v.AddChild(tag);
        v.AddChild(K.L(m.Subject, 40, K.Text, K.DisplayIt).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.AddThemeConstantOverride("line_spacing", -8); }));
        v.AddChild(K.L(m.Lead, 15, new Color(K.Text, 0.8f)).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.MaxLinesVisible = 3; }));
        if (m.Facts is { Count: > 0 })
        {
            var f = new HBoxContainer(); f.AddThemeConstantOverride("separation", 34);
            foreach (var x in m.Facts.Take(4))
            {
                var col = x.Tone == "good" ? new Color("5BE0A0") : x.Tone == "bad" ? new Color("FF5A5F") : K.Text;
                var fv = new VBoxContainer(); fv.AddThemeConstantOverride("separation", -6);
                fv.AddChild(K.L(x.Value, 34, col, K.Num)); fv.AddChild(K.L(x.Label, 11, K.Muted));
                f.AddChild(fv);
            }
            v.AddChild(f);
        }
        return box;
    }

    /// <summary>Лидеры клуба — полоса портретов с очками, как графика перед матчем на ТВ.</summary>
    Control LeadersA()
    {
        var top = C.Skaters.Where(kv => P(kv.Key)?.Team == My.Abbr).OrderByDescending(kv => kv.Value.G + kv.Value.A).ThenByDescending(kv => kv.Value.G).Take(5).ToList();
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10);
        foreach (var kv in top)
        {
            var p = P(kv.Key); var st = kv.Value;
            h.AddChild(Rev(new Paint(new Vector2(0, 120), q =>
            {
                float w = q.Size.X, hh = q.Size.Y, cut = 16;
                var poly = new[] { new Vector2(0, 0), new Vector2(w - cut, 0), new Vector2(w, cut), new Vector2(w, hh), new Vector2(0, hh) };
                q.DrawColoredPolygon(poly, K.Panel);
                float px = w * 0.42f;
                Face.Player(q, K, p, My, new Rect2(px, 0, w - px, hh), new[] { new Vector2(px, 0), new Vector2(w - cut, 0), new Vector2(w, cut), new Vector2(w, hh), new Vector2(px, hh) });
                var a0 = new Color(K.Panel, 1); var a1 = new Color(K.Panel, 0);
                q.DrawPolygon(new[] { new Vector2(px, 0), new Vector2(px + 50, 0), new Vector2(px + 50, hh), new Vector2(px, hh) }, new[] { a0, a1, a1, a0 });
                q.DrawRect(new Rect2(0, 0, 4, hh), K.Club);
                q.Text(K.Num, 46, 14, 4, (st.G + st.A).ToString(), Colors.White);
                q.Text(K.Body, 11, 14, 54, $"{st.G} г  ·  {st.A} п", K.Muted);
                var parts = p.NameRu.Split(' ', 2);
                string last = (parts.Length > 1 ? parts[1] : parts[0]).ToUpperInvariant();
                int ls = Paint.Fit(K.DisplayIt, last, 24, w * 0.7f);
                q.Text(K.DisplayIt, ls, 13, 80, last, K.Text);
            })).Also(x => x.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        }
        return h;
    }

    Control TableA()
    {
        var rows = C.Sorted(t => t.Division == My.Division);
        return new Paint(new Vector2(470, 0), q =>
        {
            float w = q.Size.X;
            q.DrawRect(new Rect2(0, 0, w, q.Size.Y), K.Panel);
            q.DrawRect(new Rect2(0, 0, w, 44), new Color("05070A"));
            q.Text(K.Heavy, 20, 18, 9, $"{DivName().Replace("ом", "ый").ToUpperInvariant()} ДИВИЗИОН", K.Text);
            q.Text(K.Body, 11, w - 128, 16, "И", K.Dim, HorizontalAlignment.Center, 30);
            q.Text(K.Body, 11, w - 86, 16, "О", K.Dim, HorizontalAlignment.Center, 40);
            float y = 50;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; var t = C.Team(r.Team); bool me = r.Team == My.Abbr;
                float rh = 34;
                if (me) q.DrawColoredPolygon(new[] { new Vector2(0, y), new Vector2(w, y), new Vector2(w - 8, y + rh), new Vector2(0, y + rh) }, K.Club.Darkened(0.3f));
                if (i == 3) q.DrawLine(new Vector2(14, y - 1), new Vector2(w - 14, y - 1), new Color(1, 1, 1, 0.18f), 1);
                q.Text(K.Num, 20, 14, y + 4, (i + 1).ToString(), me ? Colors.White : K.Muted, HorizontalAlignment.Center, 24);
                Face.Logo(q, K, t, new Rect2(46, y + 4, 26, 26));
                q.Text(me ? K.BodyBold : K.BodySemi, 14, 82, y + 8, t.City, K.Text);
                q.Text(K.Body, 13, w - 128, y + 8, r.Gp.ToString(), K.Muted, HorizontalAlignment.Center, 30);
                q.Text(K.Num, 22, w - 86, y + 3, r.Pts.ToString(), Colors.White, HorizontalAlignment.Center, 40);
                // последние пять: зелёный — победа, янтарь — очко, красный — поражение
                for (int s = 0; s < 5; s++)
                {
                    char c = r.Last.Count > s ? r.Last[r.Last.Count - 1 - s] : ' ';
                    var col = c == 'W' ? new Color("5BE0A0") : c == 'L' ? new Color("FF5A5F") : c == 'O' ? new Color("F5C24C") : new Color(1, 1, 1, 0.08f);
                    q.DrawColoredPolygon(new[] { new Vector2(w - 222 + s * 14, y + 12), new Vector2(w - 212 + s * 14, y + 12), new Vector2(w - 214 + s * 14, y + 22), new Vector2(w - 224 + s * 14, y + 22) }, col);
                }
                y += rh + 2;
            }
        });
    }

    Control TickerA()
    {
        var items = C.Messages.Where(m => m.IsNews).OrderByDescending(m => m.Id).Take(10).Select(m => m.Subject).ToList();
        if (items.Count == 0) items.Add("Сезон 2026-27 стартовал");
        string text = string.Join("      ◆      ", items);
        float speed = 70;
        return new Paint(new Vector2(0, 40), q =>
        {
            float w = q.Size.X;
            q.DrawRect(new Rect2(0, 0, w, 40), new Color("05070A"));
            float tw = K.BodySemi.GetStringSize(text + "      ◆      ", HorizontalAlignment.Left, -1, 14).X;
            float off = -(q.T * speed % tw);
            for (float x = 150 + off; x < w; x += tw) q.Text(K.BodySemi, 14, x, 10, text, new Color(K.Text, 0.85f));
            q.DrawColoredPolygon(new[] { new Vector2(0, 0), new Vector2(150, 0), new Vector2(136, 40), new Vector2(0, 40) }, K.Club);
            q.Text(K.Heavy, 18, 14, 8, "ЛЕНТА НХЛ", Ui.ReadableOn(K.Club));
        }) { OnTick = (_, _) => { }, ClipContents = true };
    }

    // ================================================================== B «Премиум»: оболочка
    Control SideB()
    {
        var wrap = new Control { CustomMinimumSize = new Vector2(224, 0) };
        wrap.AddChild(new Paint(Vector2.Zero, q =>
        {
            q.DrawRect(new Rect2(Vector2.Zero, q.Size), new Color(0, 0, 0, 0.28f));
            q.DrawLine(new Vector2(q.Size.X - 0.5f, 0), new Vector2(q.Size.X - 0.5f, q.Size.Y), new Color(1, 1, 1, 0.06f), 1);
        }).Also(x => x.SetAnchorsPreset(LayoutPreset.FullRect)));
        var v = new VBoxContainer();
        v.SetAnchorsPreset(LayoutPreset.FullRect);
        v.AddThemeConstantOverride("separation", 2);
        wrap.AddChild(v);
        v.AddChild(new Paint(new Vector2(224, 104), q =>
        {
            // мягкое свечение цвета клуба за эмблемой
            for (int i = 8; i > 0; i--) q.DrawCircle(new Vector2(52, 52), 14 + i * 5, new Color(K.Club, 0.025f));
            Face.Logo(q, K, My, new Rect2(28, 28, 48, 48));
            q.Text(K.Display, 15, 88, 32, My.Name, K.Text);
            q.Text(K.Body, 12, 88, 54, $"{Rec()}  ·  {Place()}-е место", K.Muted);
        }));
        var nav = new (string icon, string label, bool on, string[] subs)[]
        {
            ("house", "Портал", _page != "lines", null),
            ("users", "Команда", _page == "lines", new[] { "Состав", "Звенья", "Тактика", "Раздевалка" }),
            ("dumbbell", "Тренировки", false, null), ("calendar-days", "Календарь", false, null), ("handshake", "Сделки", false, null),
            ("binoculars", "Скаутинг", false, null), ("landmark", "Клуб", false, null), ("trophy", "Лига", false, null),
        };
        foreach (var n in nav)
        {
            v.AddChild(new Paint(new Vector2(224, 44), q =>
            {
                if (n.on)
                {
                    var sb = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.07f) }; sb.SetCornerRadiusAll(12);
                    q.DrawStyleBox(sb, new Rect2(12, 2, 200, 40));
                    q.DrawCircle(new Vector2(28, 22), 3, K.Club.Lightened(0.2f));
                }
                var tex = Icons.Get(n.icon);
                if (tex != null) q.DrawTextureRect(tex, new Rect2(42, 12, 20, 20), false, n.on ? K.Text : K.Muted);
                q.Text(n.on ? K.BodySemi : K.Body, 15, 74, 12, n.label, n.on ? K.Text : K.Muted);
            }));
            if (n.on && n.subs != null)
                foreach (var s in n.subs)
                    v.AddChild(new Paint(new Vector2(224, 32), q =>
                    {
                        bool cur = s == "Звенья" && _page == "lines";
                        q.Text(cur ? K.BodySemi : K.Body, 14, 74, 6, s, cur ? K.Text : K.Dim);
                        if (cur) q.DrawLine(new Vector2(60, 9), new Vector2(60, 25), K.Club.Lightened(0.2f), 2);
                    }));
        }
        v.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        v.AddChild(new Paint(new Vector2(224, 124), q =>
        {
            q.Text(K.Body, 12, 28, 0, "сегодня", K.Dim);
            q.Text(K.BodySemi, 15, 28, 18, Ui.Date(C.Today), K.Text);
            var sb = new StyleBoxFlat { BgColor = K.Club.Lightened(0.05f) }; sb.SetCornerRadiusAll(14);
            sb.ShadowColor = new Color(K.Club, 0.45f); sb.ShadowSize = 18;
            q.DrawStyleBox(sb, new Rect2(22, 54, 180, 48));
            q.Text(K.BodyBold, 15, 22, 67, "Продолжить", Ui.ReadableOn(K.Club), HorizontalAlignment.Center, 180);
        }));
        return wrap;
    }

    Control TitleB(string title, string sub, Control right = null)
    {
        var h = new HBoxContainer();
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 6);
        v.AddChild(K.L(title, 34, K.Text, K.Display));
        v.AddChild(K.L(sub, 14, K.Muted));
        h.AddChild(v);
        h.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        if (right != null) h.AddChild(right);
        return h;
    }

    Control Segmented(string[] items, int on) => new Paint(new Vector2(items.Sum(s => K.BodySemi.GetStringSize(s, HorizontalAlignment.Left, -1, 14).X + 36) + 8, 44), q =>
    {
        var sb = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.05f), BorderColor = new Color(1, 1, 1, 0.08f) }; sb.SetCornerRadiusAll(22); sb.SetBorderWidthAll(1);
        q.DrawStyleBox(sb, new Rect2(Vector2.Zero, q.Size));
        float x = 4;
        for (int i = 0; i < items.Length; i++)
        {
            float tw = K.BodySemi.GetStringSize(items[i], HorizontalAlignment.Left, -1, 14).X + 36;
            if (i == on) { var a = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.12f) }; a.SetCornerRadiusAll(18); q.DrawStyleBox(a, new Rect2(x, 4, tw, 36)); }
            q.Text(K.BodySemi, 14, x, 12, items[i], i == on ? K.Text : K.Muted, HorizontalAlignment.Center, tw);
            x += tw;
        }
    }) { SizeFlagsVertical = SizeFlags.ShrinkCenter };

    // ================================================================== B: звенья
    const float BW = 196, BH = 112, BG = 26;

    Control LinesB()
    {
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 26);
        double chem = Enumerable.Range(0, L.F.Count).Average(i => C.UnitFam(L.F[i]));
        v.AddChild(TitleB("Звенья", $"Общий рейтинг {TeamOvr()}  ·  сыгранность {Math.Round(chem * 100)}%  ·  до матча 1 день", Segmented(new[] { "Пятёрки", "Доска", "Спецбригады" }, 0)));
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 22);
        v.AddChild(h);
        var fg = new GlassBox(K, 24, 24, 0.05f, new Color(K.Club, 0.12f));
        var fv = new VBoxContainer(); fv.AddThemeConstantOverride("separation", 14); fg.AddChild(fv);
        fv.AddChild(K.L("Нападение", 13, K.Muted, K.BodySemi));
        for (int i = 0; i < Math.Min(4, L.F.Count); i++) fv.AddChild(UnitRowB(i + 1, L.F[i], L.RoleF(i)));
        h.AddChild(Rev(fg));
        var dg = new GlassBox(K, 24, 24, 0.05f);
        var dv = new VBoxContainer(); dv.AddThemeConstantOverride("separation", 14); dg.AddChild(dv);
        dv.AddChild(K.L("Оборона и вратари", 13, K.Muted, K.BodySemi));
        for (int i = 0; i < Math.Min(3, L.D.Count); i++) dv.AddChild(UnitRowB(i + 1, L.D[i], L.RoleD(i)));
        dv.AddChild(UnitRowB(0, new[] { L.G1, L.G2 }, -1));
        h.AddChild(Rev(dg));
        return v;
    }

    Control UnitRowB(int num, int[] ids, int role)
    {
        bool g = num == 0;
        double fam = g ? 0 : C.UnitFam(ids);
        float w = 64 + ids.Length * BW + (ids.Length - 1) * BG;
        var row = new Control { CustomMinimumSize = new Vector2(w, BH) };
        row.AddChild(new Paint(new Vector2(w, BH), q =>
        {
            q.Text(K.Num, 30, 0, 18, g ? "Вр" : num.ToString(), g ? K.Dim : K.Text);
            if (!g)
            {
                q.Text(K.Body, 12, 0, 62, $"{Math.Round(fam * 100)}%", FamCol(fam));
                if (role > 0)
                {
                    string s = role == 1 ? "атака" : "оборона";
                    var sb = new StyleBoxFlat { BgColor = new Color(role == 1 ? new Color("F5C24C") : new Color("7FE6FF"), 0.14f) }; sb.SetCornerRadiusAll(8);
                    float tw = K.BodySemi.GetStringSize(s, HorizontalAlignment.Left, -1, 11).X + 14;
                    q.DrawStyleBox(sb, new Rect2(0, 82, tw, 20));
                    q.Text(K.BodySemi, 11, 0, 85, s, role == 1 ? new Color("F5C24C") : new Color("7FE6FF"), HorizontalAlignment.Center, tw);
                }
            }
            // связи между партнёрами: линия в зазоре, цвет — сыгранность пары
            for (int i = 0; i + 1 < ids.Length && !g; i++)
            {
                double f = ids[i] != 0 && ids[i + 1] != 0 ? C.Fam(ids[i], ids[i + 1]) : 0;
                float x0 = 64 + (i + 1) * BW + i * BG, x1 = x0 + BG, y = BH / 2;
                var col = new Color(FamCol(f), 0.85f);
                q.DrawLine(new Vector2(x0 - 2, y), new Vector2(x1 + 2, y), col, 2, true);
                q.DrawCircle(new Vector2((x0 + x1) / 2, y), 4, col);
            }
        }));
        for (int i = 0; i < ids.Length; i++)
        {
            var card = Rev(CardB(P(ids[i])));
            card.Position = new Vector2(64 + i * (BW + BG), 0);
            row.AddChild(card);
        }
        return row;
    }

    Control CardB(PlayerData p)
    {
        var box = new GlassBox(K, 0, 18, 0.06f) { CustomMinimumSize = new Vector2(BW, BH), Size = new Vector2(BW, BH) };
        box.AddChild(new Paint(new Vector2(BW, BH), q =>
        {
            if (p == null) { q.Text(K.Body, 13, 0, BH / 2 - 9, "свободно", K.Dim, HorizontalAlignment.Center, BW); return; }
            var t = C.Team(p.Team);
            int ovr = Kit.Ovr(C, p); var tier = Kit.Tier(ovr);
            var c = new Vector2(44, 40);
            Face.Player(q, K, p, t, new Rect2(c.X - 25, c.Y - 25, 50, 50), Face.Circle(c, 25));
            // кольцо рейтинга: доля шкалы 40–99, число — на плашке под кольцом
            float frac = Math.Clamp((ovr - 40) / 59f, 0, 1);
            q.DrawArc(c, 30, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.08f), 3, true);
            q.DrawArc(c, 30, Mathf.Pi / 2 + 0.55f, Mathf.Pi / 2 + 0.55f + (Mathf.Tau - 1.1f) * frac, 64, tier, 3, true);
            var pill = new StyleBoxFlat { BgColor = new Color("0B1119"), BorderColor = new Color(tier, 0.5f) }; pill.SetCornerRadiusAll(9); pill.SetBorderWidthAll(1);
            q.DrawStyleBox(pill, new Rect2(c.X - 19, c.Y + 21, 38, 20));
            q.Text(K.Num, 13, c.X - 19, c.Y + 23, ovr.ToString(), tier, HorizontalAlignment.Center, 38);
            var parts = p.NameRu.Split(' ', 2);
            string last = parts.Length > 1 ? parts[1] : parts[0];
            float tx = 88, tw = BW - tx - 12;
            q.Text(K.Body, 11, tx, 16, parts.Length > 1 ? parts[0] : "", K.Muted);
            int ls = Paint.Fit(K.BodySemi, last, 17, tw, 12);
            q.Text(K.BodySemi, ls, tx, 31, last, K.Text);
            var (key, v) = Top(p, 1)[0];
            q.DrawCircle(new Vector2(tx + 3, 64), 3, Kit.Attr(v));
            float aw = q.Text(K.Body, 11, tx + 11, 57, Short[key], K.Muted);
            q.Text(K.BodySemi, 11, tx + 15 + aw, 57, Math.Round(v).ToString(), Kit.Attr(v));
            q.Text(K.Body, 11, 14, 87, $"{PosRu(p.Pos)}  ·  {RoleOf(p)}", K.Muted);
            // свежесть — тонкая линия по низу
            float f = Fresh(p.Id);
            q.DrawLine(new Vector2(14, BH - 8), new Vector2(BW - 14, BH - 8), new Color(1, 1, 1, 0.07f), 2);
            q.DrawLine(new Vector2(14, BH - 8), new Vector2(14 + (BW - 28) * f / 100f, BH - 8), FreshCol(f), 2);
        }));
        return box;
    }

    // ================================================================== B: портал
    Control HomeB()
    {
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 22);
        v.AddChild(TitleB("Портал", $"{Ui.Date(C.Today)}  ·  сезон 2026-27"));
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 22);
        v.AddChild(top);
        top.AddChild(Rev(MatchB()));
        top.AddChild(Rev(LeadB()).Also(x => x.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        var bot = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; bot.AddThemeConstantOverride("separation", 22);
        v.AddChild(bot);
        bot.AddChild(Rev(TableB()).Also(x => x.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        bot.AddChild(Rev(LeadersB()).Also(x => x.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        bot.AddChild(Rev(InboxB()).Also(x => x.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        return v;
    }

    Control MatchB()
    {
        var g = C.NextGame(C.UserTeam);
        var ht = C.Team(g.Home); var at = C.Team(g.Away);
        var o = C.Preview(g);
        int dd = g.Date.DayNumber - C.Today.DayNumber;
        var box = new GlassBox(K, 26, 24, 0.05f, new Color(Readable(g.Home == C.UserTeam ? at : ht), 0.18f));
        box.AddChild(new Paint(new Vector2(500, 260), q =>
        {
            float w = q.Size.X;
            q.Text(K.Body, 13, 0, 0, "Следующий матч", K.Muted);
            q.Text(K.BodySemi, 13, 0, 0, $"{(dd == 0 ? "сегодня" : dd == 1 ? "завтра" : Ui.Date(g.Date))}  ·  19:00", K.Text, HorizontalAlignment.Right, w);
            foreach (var (t, cx) in new[] { (ht, w * 0.2f), (at, w * 0.8f) })
            {
                var col = Readable(t);
                for (int i = 10; i > 0; i--) q.DrawCircle(new Vector2(cx, 104), 18 + i * 6, new Color(col, 0.022f));
                Face.Logo(q, K, t, new Rect2(cx - 52, 52, 104, 104));
                q.Text(K.BodySemi, 15, cx - 110, 166, t.Name, K.Text, HorizontalAlignment.Center, 220);
                var r = C.Table[t.Abbr];
                q.Text(K.Body, 12, cx - 110, 188, $"{r.W}-{r.L}-{r.Otl}", K.Muted, HorizontalAlignment.Center, 220);
            }
            q.Text(K.DisplayIt, 18, 0, 92, "против", K.Dim, HorizontalAlignment.Center, w);
            float ph = (float)o.PHome;
            float y = 232;
            q.Text(K.Num, 18, 0, y - 6, $"{Math.Round(ph * 100)}%", K.Text);
            q.Text(K.Num, 18, 0, y - 6, $"{Math.Round((1 - ph) * 100)}%", K.Text, HorizontalAlignment.Right, w);
            float bx = 64, bw = w - 128;
            q.DrawLine(new Vector2(bx, y + 6), new Vector2(bx + bw * ph - 2, y + 6), Readable(ht).Lightened(0.15f), 4, true);
            q.DrawLine(new Vector2(bx + bw * ph + 2, y + 6), new Vector2(bx + bw, y + 6), Readable(at).Lightened(0.15f), 4, true);
        }));
        return box;
    }

    Control LeadB()
    {
        var m = LeadMsg();
        var box = new GlassBox(K, 30, 24, 0.05f);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 12);
        box.AddChild(v);
        if (m == null) return box;
        v.AddChild(K.L(m.Tag ?? "Новости", 13, K.Club.Lightened(0.35f), K.BodySemi));
        v.AddChild(K.L(m.Subject, 26, K.Text, K.Display).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.AddThemeConstantOverride("line_spacing", 2); l.MaxLinesVisible = 3; }));
        v.AddChild(K.L(m.Lead, 15, new Color(K.Text, 0.72f)).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.MaxLinesVisible = 3; }));
        v.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        v.AddChild(K.L($"{m.From}  ·  {Ui.Date(m.Date, false)}", 12, K.Muted));
        return box;
    }

    GlassBox CardShell(string title, Control body)
    {
        var box = new GlassBox(K, 24, 24, 0.045f);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 14);
        v.AddChild(K.L(title, 13, K.Muted, K.BodySemi));
        v.AddChild(body);
        box.AddChild(v);
        return box;
    }

    Control TableB()
    {
        var rows = C.Sorted(t => t.Division == My.Division);
        return CardShell($"{DivName().Replace("ом", "ый")} дивизион", new Paint(new Vector2(0, rows.Count * 34), q =>
        {
            float w = q.Size.X, y = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; var t = C.Team(r.Team); bool me = r.Team == My.Abbr;
                if (me) { var sb = new StyleBoxFlat { BgColor = new Color(K.Club, 0.22f) }; sb.SetCornerRadiusAll(10); q.DrawStyleBox(sb, new Rect2(-8, y, w + 16, 32)); }
                q.Text(K.Body, 13, 0, y + 7, (i + 1).ToString(), K.Muted);
                Face.Logo(q, K, t, new Rect2(24, y + 5, 22, 22));
                q.Text(me ? K.BodySemi : K.Body, 14, 56, y + 7, t.City, K.Text);
                q.Text(K.BodySemi, 14, w - 40, y + 7, r.Pts.ToString(), K.Text, HorizontalAlignment.Right, 40);
                q.Text(K.Body, 12, w - 80, y + 8, r.Gp.ToString(), K.Dim, HorizontalAlignment.Right, 30);
                y += 34;
            }
        }));
    }

    Control LeadersB()
    {
        var top = C.Skaters.Where(kv => P(kv.Key)?.Team == My.Abbr).OrderByDescending(kv => kv.Value.G + kv.Value.A).ThenByDescending(kv => kv.Value.G).Take(5).ToList();
        return CardShell("Лидеры клуба", new Paint(new Vector2(0, 5 * 46), q =>
        {
            float w = q.Size.X, y = 0;
            foreach (var (id, s) in top.Select(kv => (kv.Key, kv.Value)))
            {
                var p = P(id);
                var c = new Vector2(18, y + 19);
                Face.Player(q, K, p, My, new Rect2(0, y + 1, 36, 36), Face.Circle(c, 18));
                q.Text(K.BodySemi, 14, 50, y + 2, p.NameRu, K.Text);
                q.Text(K.Body, 12, 50, y + 21, $"{PosRu(p.Pos)}  ·  {s.G} г  {s.A} п", K.Muted);
                q.Text(K.Num, 22, w - 50, y + 4, (s.G + s.A).ToString(), K.Text, HorizontalAlignment.Right, 50);
                y += 46;
            }
        }));
    }

    Control InboxB()
    {
        var ms = C.Messages.Where(m => !m.IsNews).OrderByDescending(m => m.Id).Take(5).ToList();
        return CardShell("Входящие", new Paint(new Vector2(0, 5 * 46), q =>
        {
            float w = q.Size.X, y = 0;
            foreach (var m in ms)
            {
                if (!m.Read) q.DrawCircle(new Vector2(4, y + 12), 3.5f, K.Club.Lightened(0.3f));
                int fs = Paint.Fit(m.Read ? K.Body : K.BodySemi, m.Subject, 14, w - 18);
                string subj = m.Subject;
                while (fs <= 11 && subj.Length > 8 && K.Body.GetStringSize(subj + "…", HorizontalAlignment.Left, -1, 13).X > w - 18) subj = subj[..^1];
                if (subj != m.Subject) { subj += "…"; fs = 13; }
                q.Text(m.Read ? K.Body : K.BodySemi, fs, 16, y + 2, subj, m.Read ? K.Muted : K.Text);
                q.Text(K.Body, 12, 16, y + 22, $"{m.From}  ·  {Ui.Date(m.Date, false)}", K.Dim);
                y += 46;
            }
        }));
    }
}
