using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// «На льду» в трансляции: схема площадки, гости слева (защищают левые ворота), хозяева справа — как на табло и карте бросков.
/// У каждого — фото в кольце свежести, фамилия на плашке цвета клуба и оценка за матч по ходу игры.
/// Наведение — время на льду, +/−, броски; щелчок — карточка игрока. Только показывает состояние движка.
/// </summary>
public partial class MatchRink : Control
{
    readonly Career C;
    readonly ScheduledGame _g;
    readonly Color _ca, _ch;
    ShiftEngine.Sim _sim;
    readonly List<(Vector2 at, float r, int id, bool home)> _tokens = new();
    int _hover;

    public MatchRink() { }
    public MatchRink(Career c, ScheduledGame g)
    {
        C = c; _g = g;
        (_ca, _ch) = MatchColors.Fill(c.Team(g.Away), c.Team(g.Home));
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, 290);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public void Set(ShiftEngine.Sim sim) { _sim = sim; QueueRedraw(); }

    public static Color EnergyColor(double e) => e >= 0.8 ? Ui.Win : e >= ShiftEngine.FAT_AT ? Ui.Text : e >= 0.5 ? Ui.Otl : Ui.Loss;
    public static Color RatingColor(double v) => v >= 7.5 ? new Color("43D17A") : v >= 6.8 ? Ui.Text : v >= 6.3 ? Ui.Otl : Ui.Loss;

    /// <summary>Оценка по ходу матча — та же формула, что после матча (Chronicle.Rate), итог матча — по текущему счёту с весом прошедшего времени.</summary>
    public static double LiveRating(Career c, ShiftEngine.Sim sim, bool home, int id)
    {
        var r = sim.Result;
        int gf = home ? sim.HomeGoals : sim.AwayGoals, ga = home ? sim.AwayGoals : sim.HomeGoals;
        double share = Math.Clamp(sim.Clock / 3600.0, 0, 1);
        double lead = gf > ga ? 0.25 : gf < ga ? -0.2 : 0;
        if (c.Data.PlayerById.TryGetValue(id, out var p) && p.IsGoalie)
        {
            var gb = sim.GoalieBox(home);
            if (gb == null) return 6.7;
            return Math.Round(Math.Clamp(6.7 + 0.42 * (gb.Xga - gb.Ga) + lead * share, 5, 10), 1);
        }
        var b = sim.BoxOf(home, id);
        if (b == null) return 6.6;
        int goals = r.Goals.Count(e => e.Scorer == id && e.Kind != "so"), assists = r.Goals.Count(e => e.A1 == id || e.A2 == id);
        double v = 6.55 + 0.85 * goals + 0.5 * assists + 0.07 * b.S + 0.06 * b.Blk - 0.075 * b.Pim + 0.12 * b.Pm
                   + 0.08 * b.Tk - 0.06 * b.Gv + 0.02 * b.Hit + (b.Fow + b.Fol >= 8 ? 0.6 * (b.Fow / (double)(b.Fow + b.Fol) - 0.5) : 0) + lead * share;
        return Math.Round(Math.Clamp(v, 5, 10), 1);
    }

    /// <summary>Места на схеме в футах (x от центра, y поперёк) для своей половины слева; хозяевам — зеркально.</summary>
    static float[] Spread(int n, float w) => n switch
    {
        0 => Array.Empty<float>(),
        1 => new[] { 0f },
        2 => new[] { -w * 0.7f, w * 0.7f },
        3 => new[] { -w, 0, w },
        _ => Enumerable.Range(0, n).Select(i => -w * 1.1f + 2.2f * w * i / (n - 1)).ToArray(),
    };

    static int Slot(string pos) => pos switch { "LW" => 0, "C" => 1, "RW" => 2, _ => 1 };

    public override void _Draw()
    {
        _tokens.Clear();
        var s = Size;
        float k = Math.Min((s.X - 8) / 200f, (s.Y - 8) / 85f);
        var o = s / 2;
        Vector2 P(float x, float y) => o + new Vector2(x * k, y * k);
        ShotMap.DrawRink(this, o, k);
        // эмблемы клубов на своих половинах
        foreach (var (t, x) in new[] { (C.Team(_g.Away), -50f), (C.Team(_g.Home), 50f) })
        {
            float lw = 34 * k;
            var tex = LogoCache.I?.Get(t);
            if (tex != null) DrawTextureRect(tex, new Rect2(P(x, 0) - new Vector2(lw, lw) / 2, new Vector2(lw, lw)), false, new Color(1, 1, 1, 0.06f));
        }
        DrawString(Ui.Semi, new Vector2(4, 16), "На льду", HorizontalAlignment.Left, -1, 14, Ui.Muted);
        if (_sim == null) return;

        float tr = Math.Clamp(8.2f * k, 16, 24);   // радиус фото
        foreach (bool home in new[] { false, true })
        {
            float sx = home ? -1 : 1;          // гости — слева (−x), хозяева — справа
            var col = home ? _ch : _ca;
            var ids = _sim.OnIce(home);
            var d = ids.Where(id => _sim.IsD(home, id)).ToList();
            var f = ids.Where(id => !_sim.IsD(home, id))
                .OrderBy(id => C.Data.PlayerById.TryGetValue(id, out var p) ? Slot(p.Pos) : 1).ToList();
            var place = new List<(int id, Vector2 at)>();
            var fy = Spread(f.Count, 26); for (int i = 0; i < f.Count; i++) place.Add((f[i], P(-sx * 20, fy[i])));
            var dy = d.Count == 2 ? new[] { -21f, 21f } : Spread(d.Count, 26); for (int i = 0; i < d.Count; i++) place.Add((d[i], P(-sx * 56, dy[i])));
            int g = _sim.IsPulled(home) ? 0 : _sim.GoalieOn(home);
            if (g != 0) place.Add((g, P(-sx * 84, 0)));
            else
            {
                var at = P(-sx * 86, 0);
                DrawString(Ui.Semi, at + new Vector2(-40, 4), "пустые", HorizontalAlignment.Center, 80, 11, Ui.Loss);
            }
            foreach (var (id, at) in place) Token(id, home, at, tr, col);
        }
        // состав сейчас: 5×5, большинство, 4×4, шестой полевой
        int na = _sim.OnIce(false).Count, nh = _sim.OnIce(true).Count;
        if (na > 0)
        {
            string st = _sim.IsPulled(false) || _sim.IsPulled(true) ? $"{na} × {nh} · пустые ворота" : na == nh ? $"{na} × {nh}" : na > nh ? $"{na} × {nh} · большинство {_g.Away}" : $"{na} × {nh} · большинство {_g.Home}";
            bool special = na != nh || _sim.IsPulled(false) || _sim.IsPulled(true);
            var fsz = Ui.Semi.GetStringSize(st, HorizontalAlignment.Left, -1, 12);
            var pill = new Rect2(new Vector2(o.X - fsz.X / 2 - 12, 6), new Vector2(fsz.X + 24, 22));
            DrawStyleBox(Ds.Pill(special ? new Color(Ui.Gold, 0.9f) : new Color(0, 0, 0, 0.55f), 0, 0, special ? null : Ds.Edge), pill);
            DrawString(Ui.Semi, new Vector2(pill.Position.X, pill.Position.Y + 15.5f), st, HorizontalAlignment.Center, pill.Size.X, 12, special ? new Color("0B0F14") : Ui.Text);
        }
    }

    void Token(int id, bool home, Vector2 at, float r, Color col)
    {
        if (!C.Data.PlayerById.TryGetValue(id, out var p)) return;
        var t = C.Team(p.Team);
        bool hov = _hover == id;
        _tokens.Add((at, r + 4, id, home));
        // тень и кольцо свежести (у вратаря — без свежести)
        DrawCircle(at + new Vector2(0, 2), r + 5, new Color(0, 0, 0, 0.35f));
        DrawCircle(at, r + 4, new Color("0B1220"));
        if (!p.IsGoalie)
        {
            double e = _sim.Energy(home, id);
            DrawArc(at, r + 2.5f, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.12f), 3, true);
            DrawArc(at, r + 2.5f, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (float)Math.Clamp(e, 0, 1), 48, EnergyColor(e), 3, true);
        }
        else DrawArc(at, r + 2.5f, 0, Mathf.Tau, 48, new Color(col, 0.9f), 3, true);
        Face.Player(this, p, t, new Rect2(at - new Vector2(r, r), new Vector2(r * 2, r * 2)), Face.Circle(at, r));
        if (hov) DrawArc(at, r + 5.5f, 0, Mathf.Tau, 48, Colors.White, 1.5f, true);
        // оценка за матч — плашка справа сверху
        double rt = LiveRating(C, _sim, home, id);
        string rs = rt.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var rb = new Rect2(at + new Vector2(r * 0.45f, -r - 6), new Vector2(26, 15));
        DrawStyleBox(Ds.Pill(new Color("0B1220"), 0, 0, new Color(RatingColor(rt), 0.7f)), rb);
        DrawString(Ui.CondBold, new Vector2(rb.Position.X, rb.Position.Y + 12), rs, HorizontalAlignment.Center, rb.Size.X, 12, RatingColor(rt));
        // фамилия на плашке цвета клуба: длинная — сокращается, плашка не шире места между игроками
        int fs = 12;
        var font = Ui.Semi;
        const float maxW = 80;
        while (fs > 10 && font.GetStringSize(p.LastRu, HorizontalAlignment.Left, -1, fs).X > maxW) fs--;
        string name = Trim(font, p.LastRu, fs, maxW);
        float tw = font.GetStringSize(name, HorizontalAlignment.Left, -1, fs).X;
        var plate = new Rect2(at + new Vector2(-tw / 2 - 6, r + 6), new Vector2(tw + 12, 17));
        DrawStyleBox(Ds.Pill(new Color(col, 0.92f), 0, 0), plate);
        DrawString(font, new Vector2(plate.Position.X + 6, plate.Position.Y + 12.5f), name, HorizontalAlignment.Left, -1, fs, Ui.ReadableOn(col));
    }

    static string Trim(Font f, string s, int size, float w)
    {
        if (f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X <= w) return s;
        for (int n = s.Length - 1; n > 1; n--)
        {
            string t = s[..n].TrimEnd() + "…";
            if (f.GetStringSize(t, HorizontalAlignment.Left, -1, size).X <= w) return t;
        }
        return s[..1] + "…";
    }

    int At(Vector2 pos)
    {
        foreach (var (at, r, id, _) in _tokens) if (at.DistanceTo(pos) <= r + 2) return id;
        return 0;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            int h = At(mm.Position);
            if (h != _hover) { _hover = h; MouseDefaultCursorShape = h != 0 ? CursorShape.PointingHand : CursorShape.Arrow; QueueRedraw(); }
        }
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            int id = At(mb.Position);
            if (id != 0 && C.Data.PlayerById.TryGetValue(id, out var p)) { Links.OpenPlayer(this, p); AcceptEvent(); }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _hover != 0) { _hover = 0; QueueRedraw(); }
    }

    public override string _GetTooltip(Vector2 at)
    {
        int id = At(at);
        if (id == 0 || _sim == null || !C.Data.PlayerById.TryGetValue(id, out var p)) return "";
        bool home = _tokens.First(x => x.id == id).home;
        double rt = LiveRating(C, _sim, home, id);
        if (p.IsGoalie)
        {
            var (ga, sa) = _sim.GoalieLine(home);
            return $"{p.NameRu}, вратарь\nСейвы {sa - ga} из {sa} · оценка {rt:0.0}";
        }
        var b = _sim.BoxOf(home, id);
        double e = _sim.Energy(home, id);
        string pm = b == null ? "0" : b.Pm > 0 ? $"+{b.Pm}" : b.Pm.ToString();
        return $"{p.NameRu}, {Ui.PosRu(p.Pos)}\nСвежесть {100 * e:0}% · на льду {(b?.Toi ?? 0) / 60}:{(b?.Toi ?? 0) % 60:00} · +/− {pm} · броски {b?.S ?? 0}\nОценка по ходу матча {rt:0.0}";
    }
}
