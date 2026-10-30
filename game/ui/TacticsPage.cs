using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// «Тактика»: система игры клуба. Слева — четыре рычага (форчек, вход в зону, защитники, броски) с пригодностью состава
/// и выучкой, в центре — тактическая доска выбранного рычага, справа — что система даёт в среднем и против следующего соперника,
/// и кому из игроков она подходит.
/// </summary>
public partial class TacticsPage : Control
{
    Career C => P.C;
    Lineup L => C.Lineups[C.UserTeam];
    static int _sel;
    /// <summary>Всё на странице — глазами помощника: пригодность он оценивает со своей ошибкой.</summary>
    Func<PlayerData, string, double> Eye => _eye ??= Staff.AttrView(C, Staff.Assistant(C));
    Func<PlayerData, string, double> _eye;

    public TacticsPage(string arg = null)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        if (int.TryParse(arg, out var k) && k >= 0 && k < Tactics.Levers.Length) _sel = k;
    }

    public override void _Ready() => Build();

    void Build()
    {
        foreach (var ch in GetChildren()) { RemoveChild(ch); ch.QueueFree(); }
        var row = Ui.H(14);
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 16; row.OffsetRight = -16; row.OffsetTop = 12; row.OffsetBottom = -12;
        AddChild(row);
        row.AddChild(LeversPanel());
        row.AddChild(Ui.Expand(Center(), true, true));
        row.AddChild(EffectPanel());
    }

    void Changed()
    {
        Main.I.Save();
        CallDeferred(nameof(Build));
    }

    // ------------------------------------------------------------------ слева: рычаги
    Control LeversPanel()
    {
        var v = Ui.V(10);
        v.AddChild(Ui.V(0, Ui.L("Система игры", 22, Ui.Text, Ui.CondBold),
            Ui.L("Середина — обычный хоккей, его знают все. Края дают больше, если подходят игрокам, и требуют выучки.", 11, Ui.Dim).Also(Wrap)));
        var views = Tactics.Breakdown(C, L, Eye);
        for (int i = 0; i < Tactics.Levers.Length; i++) v.AddChild(LeverCard(i, views[i]));
        var suggest = Ui.B("Система под состав", () =>
        {
            var s = Tactics.Suggest(C, L, who: Staff.Assistant(C));
            for (int i = 0; i < s.Length; i++) if (s[i] != L.SysOf(i)) Tactics.Set(L, i, s[i]);
            Changed();
        }, false, 13);
        suggest.TooltipText = "Помощник выберет для каждого рычага край, который больше всего подходит игрокам, или середину";
        var reset = Ui.B("Обычная", () => { for (int i = 0; i < Tactics.Levers.Length; i++) Tactics.Set(L, i, 0); Changed(); }, false, 13);
        v.AddChild(Ui.H(8, Ui.Expand(suggest), Ui.Expand(reset)));
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 12));
        panel.AddChild(Ui.Scroll(v));
        return panel;
    }

    static void Wrap(Label l) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(200, 0); }

    Control LeverCard(int i, Tactics.LeverView lv)
    {
        var lever = Tactics.Levers[i];
        int v = L.SysOf(i);
        bool sel = i == _sel;
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        card.AddThemeStyleboxOverride("panel", Ui.Box(sel ? Ui.Panel2 : new Color(0, 0, 0, 0.18f), 3, sel ? Ui.Blue : Ui.Line, 10));
        card.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _sel != i) { _sel = i; CallDeferred(nameof(Build)); } };
        var col = Ui.V(6);
        col.MouseFilter = MouseFilterEnum.Pass;
        card.AddChild(col);
        col.AddChild(Ui.H(8, Ui.Expand(Ui.L(lever.Name, 17, Ui.Text, Ui.Cond)), Ui.L(Tactics.PoleName(lever, v), 12, v == 0 ? Ui.Muted : Ui.Gold, Ui.Semi)));
        col.AddChild(new Segmented(new[] { lever.Minus.Short, lever.MidShort, lever.Plus.Short }, v + 1, k =>
        {
            _sel = i;
            Tactics.Set(L, i, k - 1);
            Changed();
        }));
        // пригодность обоих краёв: состав сам подсказывает, куда тянуть
        var fits = Ui.H(10);
        foreach (int side in new[] { -1, 1 })
        {
            double f = Tactics.Fit(C, L, lever, side, Eye);
            fits.AddChild(Ui.Expand(Ui.V(1,
                Ui.L((side < 0 ? lever.Minus.Short : lever.Plus.Short) + ": " + FitWord(f), 11, FitColor(f)),
                FitBar(f))));
        }
        col.AddChild(fits);
        if (v != 0)
        {
            var learn = new PctBar(lv.Learn) { CustomMinimumSize = new Vector2(0, 14) };
            col.AddChild(Ui.H(8, Ui.L("Выучка", 11, Ui.Dim), Ui.Expand(learn), Ui.L($"{lv.Learn * 100:0}%", 11, lv.Learn >= 0.95 ? Ui.Win : Ui.Text, Ui.Cond)));
        }
        return card;
    }

    public static string FitWord(double f) => f >= 1.2 ? "отлично" : f >= 0.4 ? "подходит" : f > -0.4 ? "средне" : f > -1.2 ? "слабо" : "не подходит";
    public static Color FitColor(double f) => f >= 0.4 ? Ui.Win : f > -0.4 ? Ui.Muted : f > -1.2 ? Ui.Otl : Ui.Loss;

    /// <summary>Полоса пригодности от −2,5 до +2,5 с серединой (средний клуб лиги).</summary>
    static Control FitBar(double f)
    {
        var bar = new FitMeter { Value = f, CustomMinimumSize = new Vector2(0, 6) };
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return bar;
    }

    // ------------------------------------------------------------------ центр: доска
    Control Center()
    {
        var lever = Tactics.Levers[_sel];
        int v = L.SysOf(_sel);
        var pole = Tactics.PoleOf(lever, v);
        var v0 = Ui.V(10);
        var board = new TacticsBoard { Lever = lever.Key, V = v, Club = Ui.Club };
        board.SizeFlagsHorizontal = SizeFlags.ExpandFill; board.SizeFlagsVertical = SizeFlags.ExpandFill;
        board.CustomMinimumSize = new Vector2(520, 380);
        v0.AddChild(Ui.H(10, Ui.L(lever.Name + ": ", 26, Ui.Muted, Ui.CondBold), Ui.L(Tactics.PoleName(lever, v), 26, v == 0 ? Ui.Text : Ui.Gold, Ui.CondBold)));
        v0.AddChild(board);
        string what = pole?.What ?? lever.Key switch
        {
            "fc" => "Двое на шайбе в углу, третий — на подстраховке у синей линии: обычный хоккей, его знают все игроки.",
            "en" => "Есть место — заходим с шайбой, нет — отдаём в угол и боремся.",
            "da" => "Защитники подключаются, когда шайба у своих на синей линии, и успевают назад.",
            _ => "Бросаем, когда есть позиция, и ищем передачу, когда её нет.",
        };
        v0.AddChild(Ui.L(what, 14, Ui.Text).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        v0.AddChild(Legend());
        return v0;
    }

    static Control Legend()
    {
        Control Dot(Color c) => new ColorRect { Color = c, CustomMinimumSize = new Vector2(10, 10) }.Also(r => r.SizeFlagsVertical = SizeFlags.ShrinkCenter);
        return Ui.H(14,
            Ui.H(5, Dot(Ui.Club.Lightened(0.15f)), Ui.L("наши", 11, Ui.Muted)),
            Ui.H(5, Dot(new Color("6B7686")), Ui.L("соперник", 11, Ui.Muted)),
            Ui.H(5, Dot(Ui.Gold), Ui.L("движение", 11, Ui.Muted)),
            Ui.H(5, Dot(new Color("10161F")), Ui.L("шайба и броски", 11, Ui.Muted)));
    }

    // ------------------------------------------------------------------ справа: что даёт и кому подходит
    Control EffectPanel()
    {
        var v = Ui.V(12);
        double gpg = C.Data.League.GoalsPerGame * 0.95;
        var views = Tactics.Breakdown(C, L, Eye);
        double F = views.Sum(x => x.F), A = views.Sum(x => x.A);
        double gf = gpg * (Math.Exp(F) - 1), ga = gpg * (Math.Exp(A) - 1);
        if (Staff.Assistant(C) is { } asst)
            v.AddChild(Card(Ui.V(2, Ui.H(8, Ui.Icon("user", 14, Ui.Gold), Ui.L($"Помощник: {asst.Name}", 14, Ui.Text, Ui.Semi)),
                Ui.L($"Тактика — {Staff.Level(asst.Tac)}, оценка игроков — {Staff.Level(asst.Judge)}; {Staff.StyleText(asst)}", 11, Ui.Muted).Also(Wrap),
                Ui.L("Его советы — его взгляд: сильный помощник ошибается реже, у каждого свои пристрастия.", 11, Ui.Dim).Also(Wrap))));
        v.AddChild(Ui.L("Что даёт система", 18, Ui.Text, Ui.Cond));
        v.AddChild(Ui.H(16,
            Big("Итого за матч", gf - ga, true),
            Big("Забиваем", gf, false),
            Big("Пропускаем", ga, false, true)));
        v.AddChild(Ui.L("Голов за матч против обычной системы по оценке помощника — с этим составом против среднего соперника, с нынешней выучкой.", 11, Ui.Dim).Also(Wrap));

        // против следующего соперника — с его системой и составом
        var next = C.NextGame(C.UserTeam);
        if (next != null)
        {
            var (hs, aws) = C.Sides(next, true);
            bool home = next.Home == C.UserTeam;
            var me = home ? hs : aws;
            var opp = C.Data.TeamByAbbr[home ? next.Away : next.Home];
            var ol = C.Lineups[opp.Abbr];
            var box = Ui.V(4);
            double nf = gpg * (Math.Exp(me.Sys?.For ?? 0) - 1), na = gpg * (Math.Exp(me.Sys?.Against ?? 0) - 1);
            double est = Staff.Estimate(C, Staff.Assistant(C), nf - na, next.Id, 77);
            box.AddChild(Ui.H(8, Ui.Expand(Ui.L($"Против «{opp.Name}»", 14, Ui.Text, Ui.Semi)), Ui.L(Ui.Signed(est), 18, Col(est), Ui.CondBold)));
            box.AddChild(Ui.L("Их система: " + string.Join(", ", Enumerable.Range(0, Tactics.Levers.Length).Select(i => Tactics.PoleName(Tactics.Levers[i], ol.SysOf(i)).ToLowerInvariant())), 11, Ui.Muted).Also(Wrap));
            foreach (var n in me.Sys?.Notes ?? new List<string>()) box.AddChild(Ui.H(6, Ui.Icon("triangle-alert", 13, Ui.Otl), Ui.L(n, 12, Ui.Text).Also(Wrap)));
            if ((me.Sys?.Notes.Count ?? 0) == 0) box.AddChild(Ui.L("Особых контрприёмов у соперника нет.", 12, Ui.Muted));
            v.AddChild(Card(box));
        }

        // кому подходит выбранный рычаг
        var lever = Tactics.Levers[_sel];
        int sv = L.SysOf(_sel);
        int side = sv != 0 ? sv : (Tactics.Fit(C, L, lever, 1, Eye) >= Tactics.Fit(C, L, lever, -1, Eye) ? 1 : -1);
        var pole = Tactics.PoleOf(lever, side);
        var dressed = L.Skaters.Select(id => C.Data.PlayerById[id]).ToList();
        var ranked = dressed.Select(p => (p, f: Tactics.PlayerFit(C, p, lever, side, Eye))).Where(x => !double.IsNaN(x.f)).OrderByDescending(x => x.f).ToList();
        var who = Ui.V(3);
        who.AddChild(Ui.L($"Кому подходит «{pole.Name}»", 14, Ui.Text, Ui.Semi));
        who.AddChild(Ui.L("Нужно: " + string.Join(", ", pole.Fit.Select(x => HM27.Core.Attrs.Skater.First(a => a.Key == x.k).Name.ToLowerInvariant())), 11, Ui.Muted).Also(Wrap));
        foreach (var (p, f) in ranked.Take(4)) who.AddChild(PlayerRow(p, f));
        if (ranked.Count > 7) who.AddChild(Ui.Rule());
        foreach (var (p, f) in ranked.Skip(Math.Max(4, ranked.Count - 3))) who.AddChild(PlayerRow(p, f));
        v.AddChild(Card(who));

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(400, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 14));
        panel.AddChild(Ui.Scroll(v));
        return panel;
    }

    static Color Col(double x) => Math.Abs(x) < 0.005 ? Ui.Muted : x > 0 ? Ui.Win : Ui.Loss;

    static Control Big(string cap, double val, bool main, bool against = false)
    {
        var v = Ui.V(-2, Ui.L(cap, 11, Ui.Muted), Ui.L(Ui.Signed(val), main ? 40 : 26, main ? (Math.Abs(val) < 0.005 ? Ui.Text : val > 0 ? Ui.Gold : Ui.Loss) : Col(against ? -val : val), Ui.CondBlack));
        return v;
    }

    static Control Card(Control content)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, 0.18f), 3, Ui.Line, 10));
        p.AddChild(content);
        return p;
    }

    Control PlayerRow(PlayerData p, double f)
    {
        var name = Links.Player(C, p.Id, C.Short(p.Id), 13);
        name.CustomMinimumSize = new Vector2(120, 0);
        return Ui.H(8, Ui.L(Ui.PosRu(p.Pos), 11, Ui.Dim).Also(l => l.CustomMinimumSize = new Vector2(22, 0)), name,
            Ui.Expand(FitBar(Math.Clamp(f * 2.5, -2.5, 2.5))), Ui.L(FitWord(f * 2.5), 11, FitColor(f * 2.5)).Also(l => l.CustomMinimumSize = new Vector2(76, 0)));
    }
}

/// <summary>Шкала пригодности: середина — средний клуб, вправо зелёным, влево янтарём/красным.</summary>
public partial class FitMeter : Control
{
    public double Value;
    public override void _Draw()
    {
        var s = Size;
        DrawRect(new Rect2(0, 0, s.X, s.Y), Ui.Line);
        float mid = s.X / 2, x = (float)(mid + Math.Clamp(Value, -2.5, 2.5) / 2.5 * mid);
        var c = TacticsPage.FitColor(Value);
        DrawRect(new Rect2(Math.Min(mid, x), 0, Math.Abs(x - mid), s.Y), c);
        DrawRect(new Rect2(mid - 1, -2, 2, s.Y + 4), Ui.Muted);
    }
}

/// <summary>
/// Тактическая доска: зона атаки (атакуем вправо), наши — в цветах клуба, соперник — серые, движение — янтарём,
/// шайба и броски — чёрным. Рисует схему выбранного рычага и полюса.
/// </summary>
public partial class TacticsBoard : Control
{
    public string Lever = "fc";
    public int V;
    public Color Club = Ui.Club;

    static readonly Color Ice = new("E7EEF5"), RedL = new("C8102E", 0.8f), BlueL = new("1A4BB0", 0.85f), Opp = new("6B7686"), Ink = new("10161F");

    Vector2 _o, _sz;
    /// <summary>Точка в долях зоны: x 0 — красная линия, 1 — борт за воротами; y 0 — верхний борт, 1 — нижний.</summary>
    Vector2 At(float x, float y) => _o + new Vector2(x * _sz.X, y * _sz.Y);

    public override void _Draw()
    {
        var s = Size;
        float h = Math.Min(s.Y - 8, (s.X - 8) * 0.62f), w = h / 0.62f;
        _sz = new Vector2(w, h); _o = new Vector2((s.X - w) / 2, (s.Y - h) / 2);
        // лёд: слева прямой край (красная линия), справа закруглённые углы
        var sb = new StyleBoxFlat { BgColor = Ice, BorderColor = Ui.Gold, CornerDetail = 12 };
        sb.SetBorderWidthAll(3);
        sb.CornerRadiusTopRight = sb.CornerRadiusBottomRight = (int)(h * 0.26f);
        DrawStyleBox(sb, new Rect2(_o, _sz));
        DrawLine(At(0.004f, 0), At(0.004f, 1), RedL, 6);
        DrawLine(At(0.36f, 0), At(0.36f, 1), BlueL, 6);
        DrawLine(At(0.905f, 0.06f), At(0.905f, 0.94f), RedL, 2);
        foreach (var fy in new[] { 0.27f, 0.73f })
        {
            DrawArc(At(0.72f, fy), h * 0.17f, 0, Mathf.Tau, 48, RedL, 2);
            DrawCircle(At(0.72f, fy), 4, RedL);
            DrawCircle(At(0.22f, fy), 4, RedL);
        }
        // ворота и площадь
        DrawArc(At(0.905f, 0.5f), h * 0.075f, Mathf.Pi / 2, Mathf.Pi * 1.5f, 24, BlueL, 2);
        DrawRect(new Rect2(At(0.905f, 0.465f), new Vector2(w * 0.03f, h * 0.07f)), RedL, false, 2);
        DrawString(Ui.Semi, At(0.02f, 0.06f) + new Vector2(0, 8), "красная линия", HorizontalAlignment.Left, -1, 11, new Color(0, 0, 0, 0.35f));
        DrawString(Ui.Semi, At(0.375f, 0.06f) + new Vector2(0, 8), "синяя линия", HorizontalAlignment.Left, -1, 11, new Color(0, 0, 0, 0.35f));
        DrawString(Ui.Semi, At(0.84f, 0.97f) + new Vector2(-30, 0), "атака →", HorizontalAlignment.Left, -1, 11, new Color(0, 0, 0, 0.35f));

        switch (Lever)
        {
            case "fc": Forecheck(); break;
            case "en": Entry(); break;
            case "da": Defense(); break;
            default: Shots(); break;
        }
    }

    // ------------------------------------------------------------------ рисование
    void Player(Vector2 p, string label, bool ours = true)
    {
        float r = _sz.Y * 0.042f;
        if (!ours)
        {
            // соперник — без подписи: серый круг, чтобы не путать с нашими
            DrawCircle(p, r * 0.8f + 2, new Color(0, 0, 0, 0.25f));
            DrawCircle(p, r * 0.8f, Opp);
            return;
        }
        DrawCircle(p, r + 2, Colors.White);
        DrawCircle(p, r, Club);
        var f = Ui.CondBold; int fs = (int)(r * 1.05f);
        var tw = f.GetStringSize(label, HorizontalAlignment.Left, -1, fs);
        DrawString(f, p + new Vector2(-tw.X / 2, fs * 0.36f), label, HorizontalAlignment.Left, -1, fs, Ui.ReadableOn(ours ? Club : Opp));
    }

    void Puck(Vector2 p) => DrawCircle(p, _sz.Y * 0.014f, Ink);

    void Arrow(Vector2 a, Vector2 b, Color c, float wdt = 4, bool dashed = false)
    {
        var dir = (b - a).Normalized();
        var end = b - dir * wdt * 2.2f;
        if (dashed) DrawDashedLine(a, end, c, wdt * 0.8f, 10); else DrawLine(a, end, c, wdt);
        var n = new Vector2(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { b, end + n * wdt * 1.8f, end - n * wdt * 1.8f }, c);
    }

    /// <summary>Кривая (квадратичная Безье) со стрелкой: путь шайбы или игрока по дуге.</summary>
    void Curve(Vector2 a, Vector2 ctl, Vector2 b, Color c, float wdt = 3, bool dashed = false)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i <= 24; i++) { float t = i / 24f; pts.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctl + t * t * b); }
        for (int i = 0; i < pts.Count - 2; i++)
            if (!dashed || i % 2 == 0) DrawLine(pts[i], pts[i + 1], c, wdt);
        Arrow(pts[^3], pts[^1], c, wdt);
    }

    void Zone(Vector2 center, float rx, float ry, Color c)
    {
        var pts = new Vector2[32];
        for (int i = 0; i < 32; i++) { float t = i / 32f * Mathf.Tau; pts[i] = center + new Vector2(Mathf.Cos(t) * rx, Mathf.Sin(t) * ry); }
        DrawColoredPolygon(pts, c);
    }

    Color Gold => Ui.Gold;

    void Forecheck()
    {
        // соперник выходит из-за своих ворот: защитник с шайбой, партнёр в другом углу, нападающие у бортов
        var dC = At(0.955f, 0.42f); var d2 = At(0.86f, 0.82f);
        Player(dC, "З", false); Puck(dC + new Vector2(-_sz.Y * 0.05f, 0));
        Player(d2, "З", false);
        Player(At(0.66f, 0.12f), "Н", false); Player(At(0.62f, 0.9f), "Н", false); Player(At(0.5f, 0.5f), "Ц", false);
        switch (V)
        {
            case 1:   // 2-3: двое на защитниках, третий — у борта, защитники прижаты к синей
                Player(At(0.84f, 0.3f), "Н1"); Arrow(At(0.84f, 0.3f), dC + new Vector2(-14, -10), Gold);
                Player(At(0.74f, 0.7f), "Н2"); Arrow(At(0.74f, 0.7f), d2 + new Vector2(-16, -6), Gold);
                Player(At(0.6f, 0.2f), "Н3"); Arrow(At(0.6f, 0.2f), At(0.68f, 0.14f), Gold, 3);
                Player(At(0.42f, 0.18f), "ЛЗ"); Arrow(At(0.42f, 0.18f), At(0.5f, 0.12f), Gold, 3);
                Player(At(0.42f, 0.8f), "ПЗ"); Arrow(At(0.42f, 0.8f), At(0.52f, 0.86f), Gold, 3);
                Zone(At(0.9f, 0.55f), _sz.X * 0.11f, _sz.Y * 0.36f, new Color(Gold, 0.14f));
                break;
            case -1:   // 1-2-2: один направляет к борту, двое у синей, защитники в средней зоне
                Player(At(0.8f, 0.36f), "Н1"); Curve(At(0.8f, 0.36f), At(0.9f, 0.25f), dC + new Vector2(-10, -16), Gold);
                Player(At(0.45f, 0.32f), "Н2"); Player(At(0.45f, 0.68f), "Н3");
                Player(At(0.2f, 0.3f), "ЛЗ"); Player(At(0.2f, 0.7f), "ПЗ");
                Zone(At(0.36f, 0.5f), _sz.X * 0.17f, _sz.Y * 0.42f, new Color(Club, 0.16f));
                DrawString(Ui.Semi, At(0.2f, 0.52f), "середина закрыта", HorizontalAlignment.Left, -1, 12, new Color(Club, 0.9f));
                break;
            default:   // 2-1-2
                Player(At(0.84f, 0.3f), "Н1"); Arrow(At(0.84f, 0.3f), dC + new Vector2(-14, -10), Gold);
                Player(At(0.72f, 0.72f), "Н2"); Arrow(At(0.72f, 0.72f), At(0.8f, 0.8f), Gold, 3);
                Player(At(0.56f, 0.5f), "Н3");
                Player(At(0.4f, 0.25f), "ЛЗ"); Player(At(0.4f, 0.75f), "ПЗ");
                break;
        }
    }

    void Entry()
    {
        Player(At(0.6f, 0.75f), "З", false); Player(At(0.58f, 0.3f), "З", false);
        Player(At(0.48f, 0.5f), "Ц", false);
        switch (V)
        {
            case 1:   // проход с шайбой: игрок ведёт через синюю, партнёр открывается
                Player(At(0.18f, 0.42f), "Ц"); Puck(At(0.21f, 0.45f));
                Arrow(At(0.22f, 0.44f), At(0.66f, 0.52f), Gold, 5);
                Player(At(0.22f, 0.15f), "ЛН"); Arrow(At(0.22f, 0.15f), At(0.62f, 0.15f), Gold, 3);
                Player(At(0.2f, 0.82f), "ПН"); Arrow(At(0.2f, 0.82f), At(0.7f, 0.68f), Gold, 3);
                DrawDashedLine(At(0.66f, 0.52f), At(0.74f, 0.28f), Ink, 2, 6);
                Zone(At(0.37f, 0.45f), _sz.X * 0.05f, _sz.Y * 0.14f, new Color(Ui.Red, 0.18f));
                DrawString(Ui.Semi, At(0.39f, 0.32f), "риск потери", HorizontalAlignment.Left, -1, 11, new Color(Ui.Red, 0.9f));
                break;
            case -1:   // вброс: шайба по борту в дальний угол, двое бегут бороться
                Player(At(0.2f, 0.45f), "Ц"); Puck(At(0.23f, 0.47f));
                Curve(At(0.24f, 0.48f), At(0.75f, 1.05f), At(0.96f, 0.72f), Ink, 3, true);
                Player(At(0.25f, 0.82f), "ПН"); Curve(At(0.25f, 0.82f), At(0.6f, 0.95f), At(0.88f, 0.84f), Gold, 4);
                Player(At(0.22f, 0.18f), "ЛН"); Arrow(At(0.22f, 0.18f), At(0.68f, 0.4f), Gold, 3);
                Zone(At(0.92f, 0.8f), _sz.X * 0.07f, _sz.Y * 0.14f, new Color(Gold, 0.18f));
                DrawString(Ui.Semi, At(0.78f, 0.62f), "борьба в углу", HorizontalAlignment.Left, -1, 11, new Color(0, 0, 0, 0.55f));
                break;
            default:
                Player(At(0.2f, 0.45f), "Ц"); Puck(At(0.23f, 0.47f));
                Arrow(At(0.24f, 0.47f), At(0.5f, 0.42f), Gold, 4, true);
                Curve(At(0.24f, 0.5f), At(0.6f, 0.95f), At(0.9f, 0.82f), Ink, 2, true);
                Player(At(0.22f, 0.18f), "ЛН"); Player(At(0.24f, 0.8f), "ПН");
                break;
        }
    }

    void Defense()
    {
        // атака в зоне: нападающие у ворот и у борта, защитники на синей
        Player(At(0.86f, 0.62f), "Ц"); Player(At(0.76f, 0.16f), "ЛН"); Puck(At(0.79f, 0.17f)); Player(At(0.8f, 0.86f), "ПН");
        Player(At(0.84f, 0.42f), "З", false); Player(At(0.8f, 0.7f), "З", false); Player(At(0.66f, 0.5f), "Ц", false);
        Player(At(0.6f, 0.15f), "Н", false); Player(At(0.6f, 0.85f), "Н", false);
        switch (V)
        {
            case 1:
                Player(At(0.42f, 0.28f), "ЛЗ"); Curve(At(0.42f, 0.28f), At(0.55f, 0.32f), At(0.72f, 0.42f), Gold, 5);
                Player(At(0.42f, 0.72f), "ПЗ"); Arrow(At(0.42f, 0.72f), At(0.5f, 0.6f), Gold, 3);
                Zone(At(0.25f, 0.5f), _sz.X * 0.16f, _sz.Y * 0.36f, new Color(Ui.Red, 0.13f));
                DrawString(Ui.Semi, At(0.12f, 0.52f), "пусто сзади", HorizontalAlignment.Left, -1, 12, new Color(Ui.Red, 0.85f));
                break;
            case -1:
                Player(At(0.38f, 0.3f), "ЛЗ"); Player(At(0.38f, 0.7f), "ПЗ");
                DrawDashedLine(At(0.38f, 0.3f), At(0.38f, 0.7f), Club, 3, 8);
                Zone(At(0.28f, 0.5f), _sz.X * 0.1f, _sz.Y * 0.3f, new Color(Club, 0.14f));
                DrawString(Ui.Semi, At(0.17f, 0.52f), "держат синюю", HorizontalAlignment.Left, -1, 12, new Color(Club, 0.9f));
                break;
            default:
                Player(At(0.41f, 0.28f), "ЛЗ"); Arrow(At(0.41f, 0.28f), At(0.47f, 0.3f), Gold, 3);
                Player(At(0.41f, 0.72f), "ПЗ");
                break;
        }
    }

    void Shots()
    {
        Player(At(0.86f, 0.5f), "Ц"); Player(At(0.74f, 0.2f), "ЛН"); Player(At(0.72f, 0.8f), "ПН");
        Player(At(0.42f, 0.3f), "ЛЗ"); Player(At(0.42f, 0.7f), "ПЗ");
        Player(At(0.84f, 0.38f), "З", false); Player(At(0.8f, 0.66f), "З", false);
        var rnd = new RandomNumberGenerator { Seed = 27 };
        void Mark(Vector2 p, float r) { DrawCircle(p, r + 1.5f, Colors.White); DrawCircle(p, r, Ink); }
        switch (V)
        {
            case 1:   // много бросков отовсюду, игрок на пятаке
                for (int i = 0; i < 16; i++) Mark(At(rnd.RandfRange(0.42f, 0.86f), rnd.RandfRange(0.2f, 0.8f)), 4);
                Arrow(At(0.44f, 0.32f), At(0.88f, 0.47f), Ink, 2, true);
                Arrow(At(0.44f, 0.68f), At(0.88f, 0.53f), Ink, 2, true);
                Zone(At(0.85f, 0.5f), _sz.X * 0.05f, _sz.Y * 0.1f, new Color(Gold, 0.3f));
                DrawString(Ui.Semi, At(0.66f, 0.55f) + new Vector2(0, 30), "добивание", HorizontalAlignment.Left, -1, 11, new Color(0, 0, 0, 0.55f));
                break;
            case -1:   // мало бросков, все из опасной зоны после передачи
                Zone(At(0.8f, 0.5f), _sz.X * 0.1f, _sz.Y * 0.16f, new Color(Ui.Red, 0.14f));
                for (int i = 0; i < 6; i++) Mark(At(rnd.RandfRange(0.74f, 0.86f), rnd.RandfRange(0.4f, 0.6f)), 6);
                Arrow(At(0.74f, 0.22f), At(0.72f, 0.76f), Gold, 3);
                Arrow(At(0.44f, 0.32f), At(0.72f, 0.22f), Gold, 3, true);
                break;
            default:
                for (int i = 0; i < 10; i++) Mark(At(rnd.RandfRange(0.55f, 0.86f), rnd.RandfRange(0.28f, 0.72f)), 5);
                break;
        }
    }
}

/// <summary>
/// «Правила матча»: что помощник делает в перерывах, если… (и в матче «сразу к итогу», и советом в трансляции).
/// Каждый перерыв решение считается заново от плана на матч.
/// </summary>
public partial class RulesPage : Control
{
    Career C => P.C;
    Lineup L => C.Lineups[C.UserTeam];

    public RulesPage() { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready() => Build();

    void Build()
    {
        foreach (var ch in GetChildren()) { RemoveChild(ch); ch.QueueFree(); }
        var v = Ui.V(14);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        L.Rules ??= Rules.Default();
        v.AddChild(Ui.V(2, Ui.L("Если — то", 26, Ui.Text, Ui.CondBold),
            Ui.L("Что помощник меняет в перерывах. В матче «сразу к итогу» он так и сделает, в трансляции — предложит, решать вам. " +
                 "Каждый перерыв решение считается заново от плана на матч: условие перестало выполняться — всё возвращается.", 12, Ui.Muted).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart)));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 10);
        foreach (var (key, text) in Rules.Whens)
        {
            int idx = L.Rules.FindIndex(r => r.When == key);
            var rule = idx >= 0 ? L.Rules[idx] : new Rules.Rule(key, "none", false);
            var cb = new CheckBox { ButtonPressed = rule.On && rule.Then != "none", FocusMode = FocusModeEnum.None };
            var ob = new OptionButton { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(260, 0) };
            for (int k = 0; k < Rules.Thens.Length; k++) { ob.AddItem(Rules.Thens[k].text, k); if (Rules.Thens[k].key == rule.Then) ob.Selected = k; }
            void Save(Rules.Rule r)
            {
                int i = L.Rules.FindIndex(x => x.When == key);
                if (i >= 0) L.Rules[i] = r; else L.Rules.Add(r);
                Main.I.Save();
                CallDeferred(nameof(Build));
            }
            cb.Toggled += on => Save(rule with { On = on });
            ob.ItemSelected += k => Save(rule with { Then = Rules.Thens[(int)k].key, On = Rules.Thens[(int)k].key != "none" });
            grid.AddChild(cb);
            grid.AddChild(Ui.L(text, 15, rule.On ? Ui.Text : Ui.Muted, rule.On ? Ui.Semi : null));
            grid.AddChild(ob);
        }
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 18));
        card.AddChild(grid);
        v.AddChild(card);
        v.AddChild(Ui.H(10,
            Ui.B("Как у тренеров НХЛ", () => { L.Rules = Rules.Default(); Main.I.Save(); CallDeferred(nameof(Build)); }, false, 13),
            Ui.B("Ничего не менять", () => { L.Rules = Rules.Default().Select(r => r with { On = false }).ToList(); Main.I.Save(); CallDeferred(nameof(Build)); }, false, 13)));
        v.AddChild(Ui.L("Закрыть игру — меньше моментов у обеих команд: ведущему проще удержать счёт. Открыть — наоборот, нужно отыгрываться. " +
                        "Прессинг приносит удаления, а смена вратаря иногда встряхивает команду — но сменщик обычно слабее. " +
                        "Клубы ИИ в перерывах только меняют вратаря после четырёх пропущенных.", 12, Ui.Dim).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
    }
}
