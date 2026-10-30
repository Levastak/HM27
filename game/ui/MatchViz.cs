using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Цвета двух клубов в матче — «как на льду»: хозяева в своём основном цвете, гости — в своём, если он заметно отличается,
/// иначе во втором цвете, иначе в белом (как выездная форма). Так два «красных» клуба не сливаются в графиках, табло и полосах.
/// </summary>
public static class MatchColors
{
    public static readonly Color Away = new("E6EDF5");
    /// <summary>Тёмный цвет формы — читаемым на тёмном фоне: поднимаем яркость (HSV), не смешивая с белым, иначе все цвета сходятся в серый;
    /// насыщенность снижаем, только если ярче уже некуда (чистый синий).</summary>
    public static Color Vis(Color x)
    {
        float h = x.H, s = x.S, v = x.V;
        for (int i = 0; i < 20 && x.Luminance < 0.35f; i++)
        {
            if (v < 1) v = Math.Min(1, v + 0.05f); else s = Math.Max(0, s - 0.08f);
            x = Color.FromHsv(h, s, v);
        }
        return x;
    }

    /// <summary>Различие цветов 0…~1 (взвешенное по восприятию RGB).</summary>
    public static float Diff(Color a, Color b)
    {
        float rm = (a.R + b.R) / 2, dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Mathf.Sqrt((2 + rm) * dr * dr + 4 * dg * dg + (3 - rm) * db * db) / 3f;
    }

    /// <summary>Какой цвет формы у гостей: 0 — основной, 1 — второй, −1 — белый (оба похожи на основной цвет хозяев).
    /// Один выбор на весь матч — табло, заставка, «На льду», карта бросков и графики в одних цветах.</summary>
    public static int AwayKit(TeamData away, TeamData home)
    {
        var h = Broadcast.Fill(home);
        for (int i = 0; i < Math.Min(2, away.Colors.Count); i++)
        {
            var c = new Color(away.Colors[i]);
            if (c.Luminance < 0.05f) c = c.Lightened(0.18f);
            if (Diff(c, h) >= 0.2f && Diff(Vis(c), Vis(new Color(home.Colors[0]))) >= 0.2f) return i;
        }
        return -1;
    }

    /// <summary>Цвета для линий, полос и текста на тёмном фоне.</summary>
    public static (Color away, Color home) Of(TeamData away, TeamData home)
    {
        int k = AwayKit(away, home);
        return (k < 0 ? Away : Vis(new Color(away.Colors[k])), Vis(new Color(home.Colors[0])));
    }
    public static (Color away, Color home) Of(Career c, ScheduledGame g) => Of(c.Team(g.Away), c.Team(g.Home));

    /// <summary>Заливки плашек (табло, заставка, финал): насыщенные цвета формы, гости — по тому же правилу.</summary>
    public static (Color away, Color home) Fill(TeamData away, TeamData home)
    {
        int k = AwayKit(away, home);
        var c = k < 0 ? new Color("D9DFE7") : new Color(away.Colors[k]);
        if (k >= 0 && c.Luminance < 0.05f) c = c.Lightened(0.18f);
        return (c, Broadcast.Fill(home));
    }
}

/// <summary>
/// Ожидаемые голы по ходу матча: две ступенчатые линии накопленного xG (бросков в створ), голы — точки на линии.
/// Показывает, кто создавал больше и когда, независимо от счёта.
/// </summary>
public partial class XgChart : Control
{
    List<MatchEvent> _ev;
    readonly ScheduledGame _g;
    readonly Color _ca, _ch;
    double _end;
    double _clock = 1e9;
    public XgChart() { }
    public XgChart(List<MatchEvent> ev, ScheduledGame g, Career c)
    {
        _ev = ev.Where(e => e.Kind is "shot" or "goal").OrderBy(e => e.T).ToList(); _g = g;
        (_ca, _ch) = MatchColors.Of(c, g);
        _end = Math.Max(3600, ev.Count > 0 ? ev.Max(e => e.T) + 20 : 3600);
        CustomMinimumSize = new Vector2(0, 170);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void SetClock(double t) { _clock = t; QueueRedraw(); }
    public bool HasXg => _ev != null && _ev.Any(e => e.Xg > 0);
    /// <summary>Трансляция: события дописываются по ходу матча.</summary>
    public void SetEvents(List<MatchEvent> ev)
    {
        _ev = ev.Where(e => e.Kind is "shot" or "goal").OrderBy(e => e.T).ToList();
        _end = Math.Max(3600, ev.Count > 0 ? ev.Max(e => e.T) + 20 : 3600);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var s = Size;
        float left = 30, right = s.X - 54, top = 10, bottom = s.Y - 20;
        double tA = 0, tH = 0;
        foreach (var e in _ev) { if (e.Team == _g.Away) tA += e.Xg; else tH += e.Xg; }
        double max = Math.Max(1.5, Math.Ceiling(Math.Max(tA, tH) * 2) / 2);
        float X(double t) => left + (float)(t / _end * (right - left));
        float Y(double v) => bottom - (float)(v / max * (bottom - top));
        // сетка: периоды и уровни xG
        for (double v = 0; v <= max + 1e-9; v += max > 4 ? 1 : 0.5)
        {
            DrawLine(new Vector2(left, Y(v)), new Vector2(right, Y(v)), new Color(1, 1, 1, v == 0 ? 0.15f : 0.05f), 1);
            DrawString(Ui.Regular, new Vector2(0, Y(v) + 4), v.ToString(v % 1 == 0 ? "0" : "0.0"), HorizontalAlignment.Left, -1, 10, Ui.Dim);
        }
        for (int p = 1; p <= 3; p++)
        {
            float x = X(p * 1200);
            if (p < 3 || _end > 3620) DrawLine(new Vector2(x, top), new Vector2(x, bottom), new Color(1, 1, 1, 0.08f), 1);
            DrawString(Ui.Regular, new Vector2(X((p - 1) * 1200 + 540), s.Y - 4), $"{p}-й", HorizontalAlignment.Left, -1, 10, Ui.Dim);
        }
        if (_end > 3620) DrawString(Ui.Regular, new Vector2(X(3600) + 4, s.Y - 4), "ОТ", HorizontalAlignment.Left, -1, 10, Ui.Dim);
        // линии
        foreach (var (team, col) in new[] { (_g.Away, _ca), (_g.Home, _ch) })
        {
            var pts = new List<Vector2> { new(X(0), Y(0)) };
            double acc = 0;
            var goals = new List<Vector2>();
            foreach (var e in _ev.Where(e => e.Team == team && e.T <= _clock))
            {
                pts.Add(new Vector2(X(e.T), Y(acc)));
                acc += e.Xg;
                pts.Add(new Vector2(X(e.T), Y(acc)));
                if (e.Kind == "goal") goals.Add(new Vector2(X(e.T), Y(acc)));
            }
            pts.Add(new Vector2(X(Math.Min(_clock, _end)), Y(acc)));
            DrawPolyline(pts.ToArray(), col, 2.5f, true);
            foreach (var gp in goals) { DrawCircle(gp, 6, Ui.Gold); DrawCircle(gp, 3, col); }
            if (_clock >= _end - 30)
                DrawString(Ui.CondBold, new Vector2(right + 6, Y(acc) + 5), acc.ToString("0.0"), HorizontalAlignment.Left, -1, 15, col);
        }
        if (_clock < _end) DrawLine(new Vector2(X(_clock), top), new Vector2(X(_clock), bottom), new Color(1, 1, 1, 0.4f), 1);
    }

    public override string _GetTooltip(Vector2 at)
    {
        if (_ev == null) return "";
        double t = (at.X - 30) / (Size.X - 84) * _end;
        double a = _ev.Where(e => e.T <= t && e.Team == _g.Away).Sum(e => e.Xg), h = _ev.Where(e => e.T <= t && e.Team == _g.Home).Sum(e => e.Xg);
        int tt = (int)Math.Clamp(t, 0, _end);
        return $"{Math.Min(4, tt / 1200 + 1)}-й период, {(tt % 1200) / 60:00}:{tt % 60:00}\nxG гостей {a:0.00}, хозяев {h:0.00}";
    }
}

/// <summary>
/// Карта бросков: площадка 200 × 85 футов, как на табло — гости слева (атакуют вправо), хозяева справа (атакуют влево).
/// Наведение на бросок — кто, когда и с каким xG; щелчок — карточка игрока. Зона броска — из движка
/// (у ворот / средняя / издали), точка внутри зоны — детерминированно от события. Размер — xG, золото — гол.
/// </summary>
public partial class ShotMap : Control
{
    List<MatchEvent> _ev;
    readonly ScheduledGame _g;
    readonly Color _ca, _ch;
    double _clock = 1e9;
    public ShotMap() { }
    public ShotMap(List<MatchEvent> ev, ScheduledGame g, Career c)
    {
        _ev = ev.Where(e => e.Z >= 0 && e.Kind is "shot" or "goal" or "miss" or "block").ToList(); _g = g;
        (_ca, _ch) = MatchColors.Of(c, g);
        CustomMinimumSize = new Vector2(0, 230);
        MouseFilter = MouseFilterEnum.Stop;
    }
    public bool Empty => _ev == null || _ev.Count == 0;
    public void SetClock(double t) { _clock = t; QueueRedraw(); }
    public void SetEvents(List<MatchEvent> ev) { _ev = ev.Where(e => e.Z >= 0 && e.Kind is "shot" or "goal" or "miss" or "block").ToList(); QueueRedraw(); }

    readonly List<(Vector2 pt, MatchEvent e)> _pts = new();
    MatchEvent Near(Vector2 at)
    {
        MatchEvent best = null; float bd = 10;
        foreach (var (pt, e) in _pts) { float d = pt.DistanceTo(at) - (e.Kind == "goal" ? 3 : 0); if (d < bd) { bd = d; best = e; } }
        return best;
    }

    public override string _GetTooltip(Vector2 at)
    {
        var e = Near(at);
        if (e == null || Main.I?.Career == null) return "";
        var c = Main.I.Career;
        string what = e.Kind switch { "goal" => "ГОЛ", "shot" => "сейв", "miss" => "мимо", _ => "заблокирован" };
        int t = e.T % 1200;
        return $"{c.Short(e.P1)} — {what}\n{Math.Min(4, e.T / 1200 + 1)}-й период, {t / 60:00}:{t % 60:00}" + (e.Xg > 0 ? $" · xG {e.Xg:0.00}" : "");
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseMotion mm) { MouseDefaultCursorShape = Near(mm.Position) != null ? CursorShape.PointingHand : CursorShape.Arrow; return; }
        if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        var e = Near(mb.Position);
        if (e != null && Main.I?.Career?.Data.PlayerById.TryGetValue(e.P1, out var p) == true) { Links.OpenPlayer(this, p); AcceptEvent(); }
    }


    /// <summary>Точка броска в футах: x от центра (−100…100), y поперёк (−42…42), атака вправо.</summary>
    static Vector2 Spot(MatchEvent e)
    {
        uint h = (uint)(e.T * 2654435761u) ^ (uint)(e.P1 * 40503) ^ (uint)(e.Z * 977);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        double u1 = (h & 0xFFFF) / 65535.0, u2 = (h >> 16) / 65535.0;
        double d = e.Z switch { 0 => 5 + 15 * u1, 1 => 20 + 20 * u1, _ => 40 + 22 * u1 };
        double maxA = e.Z switch { 0 => 65, 1 => 58, _ => 42 } * Math.PI / 180;
        double a = (u2 * 2 - 1) * maxA;
        double x = 89 - d * Math.Cos(a), y = d * Math.Sin(a);
        return new Vector2((float)Math.Max(26, x), (float)Math.Clamp(y, -40, 40));
    }

    /// <summary>Площадка 200 × 85 футов: центр o, масштаб k пикселей на фут (общая для карты бросков и «На льду»).</summary>
    public static void DrawRink(CanvasItem ci, Vector2 o, float k)
    {
        Vector2 P(float x, float y) => o + new Vector2(x * k, y * k);
        // площадка
        var ice = new Color("0F2036");
        var rink = new Rect2(P(-100, -42.5f), new Vector2(200 * k, 85 * k));
        var sb = new StyleBoxFlat { BgColor = ice, BorderColor = new Color(Ui.Muted, 0.5f) };
        sb.SetBorderWidthAll(2); sb.SetCornerRadiusAll((int)(28 * k));
        ci.DrawStyleBox(sb, rink);
        var red = new Color("C8102E", 0.55f); var blue = new Color("2F6BD8", 0.7f);
        ci.DrawLine(P(0, -42.5f), P(0, 42.5f), red, 2);
        foreach (float bx in new[] { -25f, 25f }) ci.DrawLine(P(bx, -42.5f), P(bx, 42.5f), blue, 3);
        foreach (float gx in new[] { -89f, 89f })
        {
            float half = (float)Math.Sqrt(Math.Max(0, 42.5 * 42.5 - Math.Pow(Math.Max(0, Math.Abs(gx) - 72), 2) * 0.9));
            ci.DrawLine(P(gx, -half + 4), P(gx, half - 4), red, 1.2f);
            float dir = gx > 0 ? -1 : 1;
            ci.DrawArc(P(gx, 0), 6 * k, gx > 0 ? Mathf.Pi / 2 : -Mathf.Pi / 2, gx > 0 ? 3 * Mathf.Pi / 2 : Mathf.Pi / 2, 16, new Color("5BA4E6", 0.5f), 1.5f);
            ci.DrawRect(new Rect2(P(gx - (gx > 0 ? 0 : 3.3f), -3), new Vector2(3.3f * k, 6 * k)), new Color(1, 1, 1, 0.35f), false, 1);
            _ = dir;
        }
        ci.DrawArc(P(0, 0), 15 * k, 0, Mathf.Tau, 40, blue, 1.2f);
        foreach (float fx in new[] { -69f, 69f })
            foreach (float fy in new[] { -22f, 22f })
            {
                ci.DrawArc(P(fx, fy), 15 * k, 0, Mathf.Tau, 40, red, 1.2f);
                ci.DrawCircle(P(fx, fy), 1.4f * k, red);
            }
    }

    public override void _Draw()
    {
        var s = Size;
        float k = Math.Min((s.X - 8) / 200f, (s.Y - 28) / 85f);
        var o = new Vector2(s.X / 2, (s.Y - 20) / 2 + 2);
        Vector2 P(float x, float y) => o + new Vector2(x * k, y * k);
        DrawRink(this, o, k);
        // кто куда атакует
        // как на табло: гости слева (защищают левые ворота, атакуют вправо), хозяева справа
        DrawString(Ui.Cond, P(-97, -44.5f) + new Vector2(0, -2), $"{_g.Away} →", HorizontalAlignment.Left, -1, 13, _ca);
        DrawString(Ui.Cond, P(97, -44.5f) + new Vector2(-80, -2), $"← {_g.Home}", HorizontalAlignment.Right, 80, 13, _ch);
        if (Empty)
        {
            DrawString(Ui.Regular, o + new Vector2(-100, 4), "Бросков пока не было", HorizontalAlignment.Center, 200, 12, Ui.Muted);
            return;
        }
        // броски: промахи и блоки — мелко, сейвы — кольцо, голы — золото
        _pts.Clear();
        foreach (var e in _ev.Where(e => e.T <= _clock).OrderBy(e => e.Kind == "goal" ? 2 : e.Kind == "shot" ? 1 : 0))
        {
            bool home = e.Team == _g.Home;
            var sp = Spot(e);
            if (home) sp = new Vector2(-sp.X, -sp.Y);
            var pt = P(sp.X, sp.Y);
            _pts.Add((pt, e));
            var col = home ? _ch : _ca;
            float r = 2.5f + (float)Math.Sqrt(Math.Max(0, e.Xg)) * 13f;
            switch (e.Kind)
            {
                case "goal":
                    DrawCircle(pt, Math.Max(6, r + 1), Ui.Gold);
                    DrawArc(pt, Math.Max(6, r + 1), 0, Mathf.Tau, 20, col, 2);
                    break;
                case "shot":
                    DrawArc(pt, Math.Max(3.5f, r), 0, Mathf.Tau, 18, new Color(col, 0.95f), 1.6f);
                    break;
                case "miss":
                    DrawLine(pt + new Vector2(-2.5f, -2.5f), pt + new Vector2(2.5f, 2.5f), new Color(col, 0.45f), 1.2f);
                    DrawLine(pt + new Vector2(-2.5f, 2.5f), pt + new Vector2(2.5f, -2.5f), new Color(col, 0.45f), 1.2f);
                    break;
                default:
                    DrawRect(new Rect2(pt - new Vector2(2, 2), new Vector2(4, 4)), new Color(col, 0.35f));
                    break;
            }
        }
        // легенда
        float ly = s.Y - 6, lx = 4;
        void Leg(string t, Action<Vector2> mark)
        {
            mark(new Vector2(lx + 5, ly - 4));
            DrawString(Ui.Regular, new Vector2(lx + 14, ly), t, HorizontalAlignment.Left, -1, 11, Ui.Muted);
            lx += 22 + Ui.Regular.GetStringSize(t, HorizontalAlignment.Left, -1, 11).X;
        }
        Leg("гол", p => DrawCircle(p, 5, Ui.Gold));
        Leg("сейв (размер — xG)", p => DrawArc(p, 5, 0, Mathf.Tau, 16, Ui.Text, 1.5f));
        Leg("мимо", p => { DrawLine(p + new Vector2(-3, -3), p + new Vector2(3, 3), Ui.Muted, 1.2f); DrawLine(p + new Vector2(-3, 3), p + new Vector2(3, -3), Ui.Muted, 1.2f); });
        Leg("заблокирован", p => DrawRect(new Rect2(p - new Vector2(2, 2), new Vector2(4, 4)), Ui.Muted));
    }
}
