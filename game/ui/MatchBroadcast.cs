using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// «Трансляция» — моменты матча в стиле ТВ-графики (дизайн-система, направление A): табло со счётом, заставка перед матчем, гол.
/// Только показывает то, что посчитал движок (ТЗ 7.4). Тексты с кириллицей — Fira (Ui.Cond…), узкий наклонный Ds.Tv — заглавные и цифры.
/// </summary>
public static class Broadcast
{
    /// <summary>Цвет клуба для заливки плашек: тёмный основной (чёрный, тёмно-синий) — подсвечиваем.</summary>
    public static Color Fill(TeamData t)
    {
        var c = new Color(t.Colors[0]);
        if (c.Luminance < 0.05f && t.Colors.Count > 1 && new Color(t.Colors[1]).Luminance > 0.05f) c = new Color(t.Colors[1]);
        return c.Luminance < 0.06f ? c.Lightened(0.18f) : c;
    }
    public static Color Trim(TeamData t) => new Color(t.Colors.Count > 1 ? t.Colors[1] : t.Colors[0]);

    public static float EaseOut(float x) { x = Math.Clamp(x, 0, 1); return 1 - (1 - x) * (1 - x) * (1 - x); }
    public static float EaseIn(float x) { x = Math.Clamp(x, 0, 1); return x * x * x; }

    public static void Stripes(CanvasItem ci, Rect2 r, float step = 14, float alpha = 0.04f)
    {
        for (float x = r.Position.X - r.Size.Y; x < r.End.X; x += step)
            ci.DrawLine(new Vector2(x, r.End.Y), new Vector2(x + r.Size.Y, r.Position.Y), new Color(1, 1, 1, alpha), step * 0.35f);
    }

    public static void Txt(CanvasItem ci, Font f, int size, float x, float top, string s, Color c, HorizontalAlignment al = HorizontalAlignment.Left, float w = -1)
        => ci.DrawString(f, new Vector2(x, top + f.GetAscent(size)), s, al, w, size, c);
}

/// <summary>Табло трансляции: плашки клубов в их цветах, счёт, период и время. Счёт и часы — из подписей экрана матча.</summary>
public partial class Scorebug : Control
{
    readonly TeamData _away, _home;
    readonly Label _score, _clock;
    string _last;
    float _flashA, _flashH;

    public Scorebug() { }
    public Scorebug(TeamData away, TeamData home, Label score, Label clock)
    {
        _away = away; _home = home; _score = score; _clock = clock;
        CustomMinimumSize = new Vector2(720, 62);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    (string a, string h) Parse()
    {
        var s = _score?.Text ?? "–:–";
        int i = s.IndexOf(':');
        return i < 0 ? ("–", "–") : (s[..i], s[(i + 1)..]);
    }

    public override void _Process(double d)
    {
        var (a, h) = Parse();
        string now = a + ":" + h;
        if (_last != null && now != _last)
        {
            var (la, lh) = (_last[.._last.IndexOf(':')], _last[(_last.IndexOf(':') + 1)..]);
            if (la != a) _flashA = 1;
            if (lh != h) _flashH = 1;
        }
        _last = now;
        _flashA = Math.Max(0, _flashA - (float)d * 0.9f); _flashH = Math.Max(0, _flashH - (float)d * 0.9f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float w = Size.X, hgt = 50, y = (Size.Y - hgt) / 2, sk = 12;
        float slab = 196, box = 62, gap = 3, clockW = 168;
        float total = slab * 2 + box * 2 + gap * 3 + 10 + clockW;
        float x = (w - total) / 2;
        var (a, h) = Parse();
        var (fa, fh) = MatchColors.Fill(_away, _home);
        void Team(TeamData t, float x0, bool left)
        {
            var c = t == _home ? fh : fa;
            var poly = left
                ? new[] { new Vector2(x0 + sk, y), new Vector2(x0 + slab, y), new Vector2(x0 + slab, y + hgt), new Vector2(x0, y + hgt) }
                : new[] { new Vector2(x0, y), new Vector2(x0 + slab - sk, y), new Vector2(x0 + slab, y + hgt), new Vector2(x0, y + hgt) };
            DrawPolygon(poly, new[] { c.Lightened(0.08f), c.Lightened(0.08f), c.Darkened(0.3f), c.Darkened(0.3f) });
            DrawLine(new Vector2(poly[3].X, y + hgt - 1.5f), new Vector2(poly[2].X, y + hgt - 1.5f), Broadcast.Trim(t), 3);
            var fg = Ui.ReadableOn(c);
            float lx = left ? x0 + 22 : x0 + slab - 22 - 40;
            Face.Logo(this, t, new Rect2(lx, y + 5, 40, 40));
            float tx = left ? x0 + 72 : x0 + 14;
            Broadcast.Txt(this, Ds.Tv, 34, tx, y + 5, t.Abbr, fg, left ? HorizontalAlignment.Left : HorizontalAlignment.Right, slab - 86);
        }
        void Score(string s, float x0, float flash)
        {
            DrawRect(new Rect2(x0, y, box, hgt), Ds.Ink);
            if (flash > 0) DrawRect(new Rect2(x0, y, box, hgt), new Color(Ui.Gold, flash * 0.55f));
            Broadcast.Txt(this, Ds.TvNum, 40, x0, y + 3, s, flash > 0.3f ? Colors.White : Ui.Gold, HorizontalAlignment.Center, box);
        }
        Team(_away, x, true); x += slab + gap;
        Score(a, x, _flashA); x += box + gap;
        Score(h, x, _flashH); x += box + gap;
        Team(_home, x, false); x += slab + 10;
        // часы
        DrawColoredPolygon(new[] { new Vector2(x, y), new Vector2(x + clockW, y), new Vector2(x + clockW - sk, y + hgt), new Vector2(x, y + hgt) }, new Color("0E141D"));
        DrawRect(new Rect2(x, y, 3, hgt), Ui.Gold);
        string clock = _clock?.Text ?? "";
        int fs = Paint.Fit(Ui.CondBold, clock, 18, clockW - 26, 12);
        Broadcast.Txt(this, Ui.CondBold, fs, x + 12, y + (hgt - fs) / 2 - 2, clock, Ui.Text);
    }
}

/// <summary>Заставка перед матчем: две половины в цветах клубов съезжаются по диагонали, эмблемы, дата; потом уходит вверх.</summary>
public partial class MatchIntro : Control
{
    readonly TeamData _away, _home;
    readonly string _line1, _line2;
    float _t;
    public const float LEN = 2.9f;
    public bool Done => _t >= LEN;

    public MatchIntro() { }
    public MatchIntro(TeamData away, TeamData home, string line1, string line2)
    {
        _away = away; _home = home; _line1 = line1; _line2 = line2;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;   // щелчок — пропустить
    }

    public override void _GuiInput(InputEvent e) { if (e is InputEventMouseButton { Pressed: true }) _t = Math.Max(_t, LEN - 0.4f); }

    public override void _Process(double d)
    {
        _t += (float)d;
        if (_t >= LEN) { QueueFree(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        float outK = Broadcast.EaseIn((_t - (LEN - 0.45f)) / 0.45f);   // уход
        float oy = -h * outK;
        float a = Math.Clamp(_t / 0.15f, 0, 1) * (1 - outK);
        DrawRect(new Rect2(0, 0, w, h), new Color(Ds.Ink, 0.92f * a));
        float slide = Broadcast.EaseOut((_t - 0.05f) / 0.55f);
        float mid = w / 2, sk = h * 0.16f;
        var (ca, ch) = MatchColors.Fill(_away, _home);
        // гости слева, хозяева справа (как на табло)
        float dl = -(1 - slide) * w * 0.6f, dr = (1 - slide) * w * 0.6f;
        var left = new[] { new Vector2(dl, oy), new Vector2(mid + sk + dl, oy), new Vector2(mid - sk + dl, h + oy), new Vector2(dl, h + oy) };
        var right = new[] { new Vector2(mid + sk + dr, oy), new Vector2(w + dr, oy), new Vector2(w + dr, h + oy), new Vector2(mid - sk + dr, h + oy) };
        DrawPolygon(left, new[] { ca.Darkened(0.2f), ca, ca.Darkened(0.55f), ca.Darkened(0.75f) });
        DrawPolygon(right, new[] { ch, ch.Darkened(0.2f), ch.Darkened(0.75f), ch.Darkened(0.55f) });
        Broadcast.Stripes(this, new Rect2(0, oy, w, h), 18, 0.03f);
        // блик
        float sx = (_t - 0.4f) / 1.6f * (w + 600) - 300;
        DrawPolygon(new[] { new Vector2(sx, oy), new Vector2(sx + 120, oy), new Vector2(sx - 60, h + oy), new Vector2(sx - 180, h + oy) },
            new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0) });
        DrawLine(new Vector2(mid + sk, oy), new Vector2(mid - sk, h + oy), new Color(1, 1, 1, 0.9f * slide), 4);
        // эмблемы и названия
        float lk = Broadcast.EaseOut((_t - 0.35f) / 0.5f);
        float ls = h * 0.32f * (0.85f + 0.15f * lk);
        Face.Logo(this, _away, new Rect2(w * 0.24f - ls / 2 + dl, h * 0.42f - ls / 2 + oy, ls, ls), lk);
        Face.Logo(this, _home, new Rect2(w * 0.76f - ls / 2 + dr, h * 0.42f - ls / 2 + oy, ls, ls), lk);
        int ns = (int)(h * 0.065f);
        Broadcast.Txt(this, Ds.Tv, ns, w * 0.24f - 300 + dl, h * 0.62f + oy, _away.Abbr == null ? "" : _away.City.ToUpperInvariant(), new Color(1, 1, 1, lk), HorizontalAlignment.Center, 600);
        Broadcast.Txt(this, Ds.Tv, ns, w * 0.76f - 300 + dr, h * 0.62f + oy, _home.City.ToUpperInvariant(), new Color(1, 1, 1, lk), HorizontalAlignment.Center, 600);
        // центр: плашка с подписью
        float bk = Broadcast.EaseOut((_t - 0.6f) / 0.4f);
        float bw = 360 * bk, bh = 104, bx = mid - bw / 2, by = h * 0.42f - bh / 2 + oy;
        if (bw > 20)
        {
            DrawColoredPolygon(new[] { new Vector2(bx + 18, by), new Vector2(bx + bw, by), new Vector2(bx + bw - 18, by + bh), new Vector2(bx, by + bh) }, Ds.Ink);
            DrawRect(new Rect2(bx + 6, by + bh - 4, bw - 12, 4), Ui.Gold);
            if (bk > 0.9f)
            {
                Broadcast.Txt(this, Ui.Semi, 13, bx, by + 14, _line1, Ui.Muted, HorizontalAlignment.Center, bw);
                Broadcast.Txt(this, Ui.CondBlack, 40, bx, by + 36, _line2, Colors.White, HorizontalAlignment.Center, bw);
            }
        }
    }
}

/// <summary>Гол: полоса во всю ширину в цветах забившего клуба, «ГОЛ», фото и имя автора, передачи; уходит через ~3 секунды.</summary>
public partial class GoalBanner : Control
{
    readonly TeamData _t;
    readonly PlayerData _p;
    readonly string _sub, _tag;
    float _t0;
    const float LEN = 3.1f;

    public GoalBanner() { }
    public GoalBanner(TeamData team, PlayerData scorer, string sub, string tag)
    {
        _t = team; _p = scorer; _sub = sub; _tag = tag;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double d)
    {
        _t0 += (float)d;
        if (_t0 >= LEN) { QueueFree(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float w = Size.X, H = Size.Y;
        float hgt = Math.Min(230, H * 0.28f), y = H * 0.36f - hgt / 2, sk = hgt * 0.35f;
        float inK = Broadcast.EaseOut(_t0 / 0.35f), outK = Broadcast.EaseIn((_t0 - (LEN - 0.4f)) / 0.4f);
        float x0 = -w * (1 - inK) + w * outK;
        var c = Broadcast.Fill(_t);
        var poly = new[] { new Vector2(x0 + sk, y), new Vector2(x0 + w + sk, y), new Vector2(x0 + w, y + hgt), new Vector2(x0, y + hgt) };
        // тень и полоса
        DrawColoredPolygon(poly.Select(p => p + new Vector2(10, 12)).ToArray(), new Color(0, 0, 0, 0.35f));
        DrawPolygon(poly, new[] { c.Lightened(0.1f), c.Darkened(0.1f), c.Darkened(0.5f), c.Darkened(0.3f) });
        Broadcast.Stripes(this, new Rect2(x0, y, w + sk, hgt), 16, 0.045f);
        DrawRect(new Rect2(x0 + 6, y + hgt - 5, w, 5), Broadcast.Trim(_t));
        // эмблема-водяной знак
        Face.Logo(this, _t, new Rect2(x0 + w * 0.62f - hgt * 0.8f, y - hgt * 0.3f, hgt * 1.6f, hgt * 1.6f), 0.12f);
        var fg = Ui.ReadableOn(c);
        // «ГОЛ!» с лёгким увеличением в первые доли секунды
        float pop = 1 + 0.25f * (1 - Broadcast.EaseOut((_t0 - 0.1f) / 0.3f));
        int gs = (int)(hgt * 0.62f * pop);
        Broadcast.Txt(this, Ds.Tv, gs, x0 + w * 0.06f, y + hgt * 0.5f - gs * 0.62f, "ГОЛ!", fg);
        float gw = Ds.Tv.GetStringSize("ГОЛ!", HorizontalAlignment.Left, -1, (int)(hgt * 0.62f)).X;
        float nx = x0 + w * 0.06f + gw + 34;
        if (_p != null)
        {
            // фото в косом срезе справа
            float pw = hgt * 1.1f, px = x0 + w - pw - sk * 0.2f;
            var pp = new[] { new Vector2(px + sk * 0.6f, y), new Vector2(px + pw + sk * 0.6f, y), new Vector2(px + pw, y + hgt), new Vector2(px, y + hgt) };
            Face.Player(this, _p, _t, new Rect2(px, y, pw + sk * 0.6f, hgt), pp, Ds.Tv, true, false);
            DrawLine(pp[0], pp[3], new Color(1, 1, 1, 0.85f), 3);
            var parts = _p.NameRu.Split(' ', 2);
            float maxW = px - nx - 24;
            if (parts.Length > 1) Broadcast.Txt(this, Ui.Semi, 18, nx, y + hgt * 0.2f, parts[0], new Color(fg, 0.85f));
            string last = (parts.Length > 1 ? parts[1] : parts[0]).ToUpperInvariant();
            int ls = Paint.Fit(Ds.Tv, last, (int)(hgt * 0.34f), maxW, 24);
            Broadcast.Txt(this, Ds.Tv, ls, nx, y + hgt * 0.32f, last, fg);
            Broadcast.Txt(this, Ui.Semi, 15, nx, y + hgt * 0.32f + ls + 6, _sub, new Color(fg, 0.8f));
            if (!string.IsNullOrEmpty(_tag)) Broadcast.Txt(this, Ui.CondBold, 18, nx, y + 14, _tag.ToUpperInvariant(), Ui.Gold);
        }
    }
}

/// <summary>Итог матча в стиле трансляции: финальное табло и три звезды с лицами.</summary>
public static class FinalBoard
{
    public static Control Hero(Career c, ScheduledGame g)
    {
        var r = g.Result;
        var at = c.Team(g.Away); var ht = c.Team(g.Home);
        string end = r.End == "OT" ? "ФИНАЛ · ОТ" : r.End == "SO" ? "ФИНАЛ · БУЛЛИТЫ" : "ФИНАЛ";
        bool mine = g.Home == c.UserTeam || g.Away == c.UserTeam;
        bool won = mine && (g.Home == c.UserTeam ? r.HomeGoals > r.AwayGoals : r.AwayGoals > r.HomeGoals);
        return new Paint(new Vector2(0, 170), q =>
        {
            float w = q.Size.X, h = q.Size.Y, mid = w / 2, sk = 40, r0 = Ds.R_L;
            var (ca, ch) = MatchColors.Fill(at, ht);
            // две половины в цветах клубов, между ними счёт на чёрном
            var sb = new StyleBoxFlat { BgColor = Ds.Ink }; sb.SetCornerRadiusAll((int)r0);
            q.DrawStyleBox(sb, new Rect2(0, 0, w, h));
            float sw = 300;
            var left = new[] { new Vector2(r0, 0), new Vector2(mid - sw / 2 + sk / 2, 0), new Vector2(mid - sw / 2 - sk / 2, h), new Vector2(r0, h) };
            var right = new[] { new Vector2(mid + sw / 2 + sk / 2, 0), new Vector2(w - r0, 0), new Vector2(w - r0, h), new Vector2(mid + sw / 2 - sk / 2, h) };
            q.DrawPolygon(left, new[] { ca.Darkened(0.15f), ca, ca.Darkened(0.5f), ca.Darkened(0.6f) });
            q.DrawPolygon(right, new[] { ch, ch.Darkened(0.15f), ch.Darkened(0.6f), ch.Darkened(0.5f) });
            // скругления краёв — те же цвета
            q.DrawCircle(new Vector2(r0, r0), r0, ca.Darkened(0.1f)); q.DrawRect(new Rect2(0, r0, r0, h - 2 * r0), ca.Darkened(0.3f)); q.DrawCircle(new Vector2(r0, h - r0), r0, ca.Darkened(0.55f));
            q.DrawCircle(new Vector2(w - r0, r0), r0, ch.Darkened(0.1f)); q.DrawRect(new Rect2(w - r0, r0, r0, h - 2 * r0), ch.Darkened(0.3f)); q.DrawCircle(new Vector2(w - r0, h - r0), r0, ch.Darkened(0.55f));
            Broadcast.Stripes(q, new Rect2(0, 0, w, h), 16, 0.035f);
            Face.Logo(q, at, new Rect2(w * 0.09f, h / 2 - 52, 104, 104));
            Face.Logo(q, ht, new Rect2(w * 0.91f - 104, h / 2 - 52, 104, 104));
            Broadcast.Txt(q, Ds.Tv, 42, w * 0.09f + 120, h / 2 - 34, at.Abbr, Ui.ReadableOn(ca));
            Broadcast.Txt(q, Ui.Semi, 13, w * 0.09f + 122, h / 2 + 14, at.Name, new Color(Ui.ReadableOn(ca), 0.75f));
            float hx = w * 0.91f - 120;
            Broadcast.Txt(q, Ds.Tv, 42, hx - 200, h / 2 - 34, ht.Abbr, Ui.ReadableOn(ch), HorizontalAlignment.Right, 200);
            Broadcast.Txt(q, Ui.Semi, 13, hx - 260, h / 2 + 14, ht.Name, new Color(Ui.ReadableOn(ch), 0.75f), HorizontalAlignment.Right, 258);
            // счёт
            Broadcast.Txt(q, Ds.TvNum, 92, mid - sw / 2, h / 2 - 66, $"{r.AwayGoals}", r.AwayGoals > r.HomeGoals ? Colors.White : new Color(1, 1, 1, 0.55f), HorizontalAlignment.Center, sw / 2 - 16);
            Broadcast.Txt(q, Ds.TvNum, 92, mid + 16, h / 2 - 66, $"{r.HomeGoals}", r.HomeGoals > r.AwayGoals ? Colors.White : new Color(1, 1, 1, 0.55f), HorizontalAlignment.Center, sw / 2 - 16);
            q.DrawRect(new Rect2(mid - 2, h / 2 - 30, 4, 40), new Color(1, 1, 1, 0.25f));
            Broadcast.Txt(q, Ui.CondBold, 18, mid - sw / 2, h - 40, end, Ui.Gold, HorizontalAlignment.Center, sw);
            if (mine) Broadcast.Txt(q, Ui.Semi, 12, mid - sw / 2, 14, won ? "победа" : r.End == "OT" && !g.Playoff ? "очко в овертайме" : r.End == "SO" ? "очко по буллитам" : "поражение", won ? Ui.Win : Ui.Muted, HorizontalAlignment.Center, sw);
        });
    }

    /// <summary>Три звезды матча: фото по плечи в косом срезе, фамилия, вклад.</summary>
    public static Control Stars(Career c, MatchResult r)
    {
        var top = r.Ratings.Count > 0
            ? r.Ratings.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToList()
            : r.Goals.Where(e => e.Scorer != 0).GroupBy(e => e.Scorer).OrderByDescending(x => x.Count()).Take(3).Select(x => x.Key).ToList();
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 14);
        int i = 0;
        foreach (var id in top)
        {
            var p = c.Data.PlayerById[id];
            var t = c.Team(p.Team);
            string line;
            if (p.IsGoalie)
            {
                bool home = id == r.HomeGoalie;
                int sh = home ? r.AwayShots : r.HomeShots, ga = home ? r.AwayGoals : r.HomeGoals;
                line = $"{sh - ga} сейвов";
            }
            else
            {
                int gls = r.Goals.Count(e => e.Scorer == id && e.Kind != "so"), ast = r.Goals.Count(e => e.A1 == id || e.A2 == id);
                line = gls + ast == 0 ? "без очков" : $"{gls} г · {ast} п";
            }
            double rt = r.Ratings.GetValueOrDefault(id);
            int k = i++;
            var tile = new Paint(new Vector2(0, 150), q =>
            {
                float w = q.Size.X, hh = q.Size.Y, sk = 30;
                var col = Broadcast.Fill(t);
                var poly = new[] { new Vector2(sk, 0), new Vector2(w, 0), new Vector2(w - sk, hh), new Vector2(0, hh) };
                q.DrawPolygon(poly, new[] { col.Darkened(0.2f), col.Darkened(0.45f), col.Darkened(0.7f), col.Darkened(0.5f) });
                Broadcast.Stripes(q, new Rect2(0, 0, w, hh), 14, 0.03f);
                float pw = hh * 0.95f, px = w - pw - sk * 0.4f;
                var pp = new[] { new Vector2(px + sk * 0.7f, 0), new Vector2(w, 0), new Vector2(w - sk, hh), new Vector2(px, hh) };
                Face.Player(q, p, t, new Rect2(px, 0, w - px, hh), pp, Ds.Tv, false, false);
                q.DrawLine(pp[0], pp[3], new Color(1, 1, 1, 0.8f), 2, true);
                var fg = Ui.ReadableOn(col.Darkened(0.4f));
                // звёзды
                for (int s = 0; s <= k; s++) q.DrawTextureRect(Icons.Get("star", true), new Rect2(sk + 4 + s * 20, 14, 18, 18), false, Ui.Gold);
                Broadcast.Txt(q, Ui.CondBold, 13, sk + 4, 36, k == 0 ? "ПЕРВАЯ ЗВЕЗДА" : k == 1 ? "ВТОРАЯ ЗВЕЗДА" : "ТРЕТЬЯ ЗВЕЗДА", new Color(fg, 0.7f));
                var parts = p.NameRu.Split(' ', 2);
                string last = (parts.Length > 1 ? parts[1] : parts[0]).ToUpperInvariant();
                int ls = Paint.Fit(Ds.Tv, last, 34, px - sk - 14, 18);
                Broadcast.Txt(q, Ui.Semi, 12, sk - 4, 62, parts.Length > 1 ? parts[0] : "", new Color(fg, 0.8f));
                Broadcast.Txt(q, Ds.Tv, ls, sk - 8, 78, last, fg);
                Broadcast.Txt(q, Ui.Semi, 13, sk - 16, 118, line + (rt > 0 ? $"  ·  оценка {rt:0.0}" : ""), new Color(fg, 0.85f));
            }) { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = $"{p.NameRu} — профиль", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            tile.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Main.I.ShowHub("player", id.ToString()); } };
            h.AddChild(tile);
        }
        return h;
    }
}
