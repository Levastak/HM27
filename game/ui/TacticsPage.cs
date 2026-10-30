using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// «Тактика»: система игры клуба. Слева — готовые стили и девять рычагов по фазам (без шайбы, с шайбой, спецбригады)
/// с пригодностью состава и выучкой; в центре — доска выбранного рычага, что он меняет в игре и мнение помощника — словами,
/// без голов за матч (цену схемы игрок узнаёт из журнала и советов, которые могут ошибаться); справа — кому подходит.
/// Всё про конкретного соперника — в письме скаута и на экране «до матча».
/// </summary>
public partial class TacticsPage : Control
{
    Career C => P.C;
    Lineup L => C.Lineups[C.UserTeam];
    static int _sel;
    /// <summary>Всё на странице — глазами помощника: пригодность он оценивает со своей ошибкой.</summary>
    Func<PlayerData, string, double> Eye => _eye ??= Staff.AttrView(C, Staff.Assistant(C));
    Func<PlayerData, string, double> _eye;
    StaffMember Asst => Staff.Assistant(C);

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
        row.AddChild(Ui.Expand(Ui.Scroll(Center()), true, true));
        row.AddChild(SidePanel());
    }

    void Changed()
    {
        Main.I.Save();
        CallDeferred(nameof(Build));
    }

    // ------------------------------------------------------------------ мнение помощника (словами)
    /// <summary>Как помощник оценивает край против середины для этого состава: его ошибка и любимый форчек.</summary>
    double Opinion(int lever, int v)
    {
        if (v == 0) return 0;
        var who = Asst;
        double x = Tactics.Value(C, L, lever, v, Eye);
        if (who == null) return x;
        return x + Staff.TacSd(who) / 3 * Staff.Noise(C, who, 900 + lever, v) + (lever == 0 && who.Press == v ? 0.006 : 0);
    }

    double SysOpinion(int[] sys) => Enumerable.Range(0, Tactics.Levers.Length).Sum(i => Opinion(i, i < sys.Length ? sys[i] : 0));

    // ------------------------------------------------------------------ слева: стили и рычаги
    Control LeversPanel()
    {
        var v = Ui.V(10);
        v.AddChild(Ui.V(0, Ui.L("Система игры", 20, Ui.Text, Ds.Display),
            Ui.L("Середина — обычный хоккей, его знают все. Края дают больше, если подходят игрокам, и требуют выучки.", 11, Ui.Dim).Also(Wrap)));
        // готовые стили
        var cur = Enumerable.Range(0, Tactics.Levers.Length).Select(i => L.SysOf(i)).ToArray();
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6); flow.AddThemeConstantOverride("v_separation", 6);
        foreach (var (name, what, sys) in Tactics.Styles)
        {
            bool on = sys.SequenceEqual(cur);
            var b = Ui.B(name, () => { for (int i = 0; i < sys.Length; i++) if (sys[i] != L.SysOf(i)) Tactics.Set(L, i, sys[i]); Changed(); }, false, 12);
            b.TooltipText = what + (Asst != null ? $"\n{Asst.Name}: для нашего состава — {Staff.GainVs(SysOpinion(sys) - SysOpinion(cur), "нынешней системы", "нынешняя система")}." : "");
            if (on) { b.AddThemeStyleboxOverride("normal", Ui.Box(new Color(Ui.Accent, 0.28f), 6, Ui.AccentBright, 9)); b.AddThemeColorOverride("font_color", Colors.White); }
            flow.AddChild(b);
        }
        var plain = Ui.B("Обычная", () => { for (int i = 0; i < Tactics.Levers.Length; i++) Tactics.Set(L, i, 0); Changed(); }, false, 12);
        flow.AddChild(plain);
        var suggest = Ui.B("Под состав", () =>
        {
            var s = Tactics.Suggest(C, L, who: Asst);
            for (int i = 0; i < s.Length; i++) if (s[i] != L.SysOf(i)) Tactics.Set(L, i, s[i]);
            Changed();
        }, false, 12);
        suggest.TooltipText = "Помощник выберет для каждого рычага край, который, по его мнению, больше всего подходит игрокам, или середину";
        flow.AddChild(suggest);
        v.AddChild(Ui.V(4, Ui.L("Готовые стили", 13, Ui.Muted, Ui.Semi), flow));
        var views = Tactics.Breakdown(C, L, Eye);
        foreach (var (phase, levers) in Tactics.Phases)
        {
            v.AddChild(Ui.L(phase, 15, Ui.Gold, Ui.Cond).Also(l => l.CustomMinimumSize = new Vector2(0, 22)));
            foreach (var i in levers) v.AddChild(LeverCard(i, views[i]));
        }
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(370, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 14, null, 12));
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
        card.AddThemeStyleboxOverride("panel", Ui.Box(sel ? Ui.Panel2 : new Color(0, 0, 0, 0.18f), 3, sel ? Ui.Blue : Ui.Line, 9));
        card.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _sel != i) { _sel = i; CallDeferred(nameof(Build)); } };
        var col = Ui.V(5);
        col.MouseFilter = MouseFilterEnum.Pass;
        card.AddChild(col);
        var head = Ui.H(8, Ui.Expand(Ui.L(lever.Name, 15, Ui.Text, Ui.Semi)));
        if (v != 0)
        {
            string lw = lv.Learn >= 0.95 ? "выучена" : lv.Learn >= 0.6 ? "почти выучена" : "учим";
            head.AddChild(Ui.H(5, Ui.Bar(lv.Learn, lv.Learn >= 0.95 ? Ui.Win : Ui.Otl, 46, 5).Also(b => b.SizeFlagsVertical = SizeFlags.ShrinkCenter), Ui.L(lw, 11, lv.Learn >= 0.95 ? Ui.Muted : Ui.Otl)));
        }
        col.AddChild(head);
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

    // ------------------------------------------------------------------ центр: доска, что меняет, мнение помощника
    Control Center()
    {
        var lever = Tactics.Levers[_sel];
        int v = L.SysOf(_sel);
        var pole = Tactics.PoleOf(lever, v);
        var v0 = Ui.V(10);
        var board = new TacticsBoard { Lever = lever.Key, V = v, Club = Ui.Club };
        board.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        board.CustomMinimumSize = new Vector2(520, 360);
        v0.AddChild(Ui.H(10, Ui.L(lever.Name + ": ", 20, Ui.Muted, Ds.Display), Ui.L(Tactics.PoleName(lever, v), 20, v == 0 ? Ui.Text : Ui.Gold, Ds.Display)));
        v0.AddChild(board);
        v0.AddChild(Legend(lever.Key));
        v0.AddChild(Ui.L(pole?.What ?? lever.MidWhat, 15, Ui.Text).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        // что увидите в статистике — по обоим краям
        var watch = Ui.V(4);
        foreach (int side in new[] { -1, 1 })
        {
            var pl = Tactics.PoleOf(lever, side);
            watch.AddChild(Ui.H(8, Ui.L(pl.Short, 12, side == v ? Ui.Gold : Ui.Muted, Ui.Semi).Also(l => l.CustomMinimumSize = new Vector2(110, 0)),
                Ui.Expand(Ui.L(Cap(pl.Watch), 12, side == v ? Ui.Text : Ui.Muted).Also(Wrap))));
        }
        v0.AddChild(Card(Ui.V(6, Ui.L("Что меняется в игре", 14, Ui.Text, Ui.Semi), watch,
            Ui.L("Насколько это окупается — зависит от игроков и соперника. Смотрите журнал: что схемы дали в наших матчах.", 11, Ui.Dim).Also(Wrap),
            Ui.B("Журнал: что сработало", () => Main.I.ShowHub("tactics:journal"), false, 12).Also(b => b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin))));
        // мнение помощника — словами
        if (Asst is { } a)
        {
            var op = Ui.V(4);
            op.AddChild(Ui.H(8, Ui.Icon("user", 14, Ui.Gold), Ui.L($"{a.Name} о рычаге «{lever.Name.ToLowerInvariant()}»", 14, Ui.Text, Ui.Semi)));
            foreach (int side in new[] { -1, 1 })
            {
                double est = Opinion(_sel, side);
                op.AddChild(Ui.H(8, Ui.L(Tactics.PoleOf(lever, side).Short, 12, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(110, 0)),
                    Ui.L(Cap(Staff.GainVs(est, "обычного", "обычный")), 13, est >= 0.02 ? Ui.Win : est <= -0.01 ? Ui.Otl : Ui.Text, Ui.Semi),
                    Ui.L("— " + Staff.Confidence(a, est), 12, Ui.Dim)));
            }
            op.AddChild(Ui.L("Против среднего соперника, с выученной схемой. Это его взгляд: помощник со слабой тактикой ошибается чаще, и у каждого свои пристрастия.", 11, Ui.Dim).Also(Wrap));
            v0.AddChild(Card(op));
        }
        return v0;
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    static Control Legend(string lever)
    {
        Control Dot(Color c) => new ColorRect { Color = c, CustomMinimumSize = new Vector2(10, 10) }.Also(r => r.SizeFlagsVertical = SizeFlags.ShrinkCenter);
        bool own = TacticsBoard.OwnZone(lever);
        return Ui.H(14,
            Ui.H(5, Dot(Ui.Club.Lightened(0.15f)), Ui.L("наши", 11, Ui.Muted)),
            Ui.H(5, Dot(new Color("6B7686")), Ui.L("соперник", 11, Ui.Muted)),
            Ui.H(5, Dot(Ui.Gold), Ui.L("движение", 11, Ui.Muted)),
            Ui.H(5, Dot(new Color("10161F")), Ui.L("шайба, передачи и броски", 11, Ui.Muted)),
            Ui.L(own ? "ворота — наши" : "ворота — соперника", 11, Ui.Dim));
    }

    // ------------------------------------------------------------------ справа: помощник и кому подходит
    Control SidePanel()
    {
        var v = Ui.V(12);
        if (Asst is { } asst)
        {
            var cur = Enumerable.Range(0, Tactics.Levers.Length).Select(i => L.SysOf(i)).ToArray();
            double all = SysOpinion(cur);
            v.AddChild(Card(Ui.V(3, Ui.H(8, Ui.Icon("user", 14, Ui.Gold), Ui.L($"Помощник: {asst.Name}", 14, Ui.Text, Ui.Semi)),
                Ui.L($"Тактика — {Staff.Level(asst.Tac)}, оценка игроков — {Staff.Level(asst.Judge)}; {Staff.StyleText(asst)}", 11, Ui.Muted).Also(Wrap),
                Ui.L($"Система в целом, по его мнению, {Staff.GainVs(all, "обычной", "обычная")} ({Staff.Confidence(asst, all)}).", 12, Ui.Text).Also(Wrap))));
        }
        var style = Tactics.Styles.FirstOrDefault(x => x.sys.SequenceEqual(Enumerable.Range(0, Tactics.Levers.Length).Select(i => L.SysOf(i))));
        if (style.name != null) v.AddChild(Card(Ui.V(3, Ui.L($"Стиль «{style.name}»", 14, Ui.Gold, Ui.Semi), Ui.L(style.what, 12, Ui.Text).Also(Wrap))));

        var lever = Tactics.Levers[_sel];
        int sv = L.SysOf(_sel);
        int side = sv != 0 ? sv : (Tactics.Fit(C, L, lever, 1, Eye) >= Tactics.Fit(C, L, lever, -1, Eye) ? 1 : -1);
        var pole = Tactics.PoleOf(lever, side);
        var pool = pole.Group == 'P' ? (L.Pp1 ?? Array.Empty<int>()).Concat(L.Pp2 ?? Array.Empty<int>())
                 : pole.Group == 'K' ? (L.Pk1 ?? Array.Empty<int>()).Concat(L.Pk2 ?? Array.Empty<int>()) : L.Skaters;
        var dressed = pool.Where(id => id != 0).Distinct().Select(id => C.Data.PlayerById[id]).ToList();
        var ranked = dressed.Select(p => (p, f: Tactics.PlayerFit(C, p, lever, side, Eye))).Where(x => !double.IsNaN(x.f)).OrderByDescending(x => x.f).ToList();
        var who = Ui.V(3);
        who.AddChild(Ui.L($"Кому подходит «{pole.Short}»", 14, Ui.Text, Ui.Semi));
        who.AddChild(Ui.L("Нужно: " + string.Join(", ", pole.Fit.Select(x => HM27.Core.Attrs.Skater.First(a => a.Key == x.k).Name.ToLowerInvariant())), 11, Ui.Muted).Also(Wrap));
        if (pole.Group is 'P' or 'K') who.AddChild(Ui.L(pole.Group == 'P' ? "Смотрим звенья большинства." : "Смотрим звенья меньшинства.", 11, Ui.Dim));
        foreach (var (p, f) in ranked.Take(4)) who.AddChild(PlayerRow(p, f));
        if (ranked.Count > 7) who.AddChild(Ui.Rule());
        foreach (var (p, f) in ranked.Skip(Math.Max(4, ranked.Count - 3))) who.AddChild(PlayerRow(p, f));
        v.AddChild(Card(who));

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 14, null, 14));
        panel.AddChild(Ui.Scroll(v));
        return panel;
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
        DrawString(Ui.Semi, At(0.8f, 0.97f) + new Vector2(-30, 0), OwnZone(Lever) ? "наши ворота →" : "атака →", HorizontalAlignment.Left, -1, 11, new Color(0, 0, 0, 0.35f));
        if (OwnZone(Lever)) Goalie();

        switch (Lever)
        {
            case "fc": Forecheck(); break;
            case "en": Entry(); break;
            case "da": Defense(); break;
            case "agg": Physical(); break;
            case "dz": OwnZoneD(); break;
            case "bo": Breakout(); break;
            case "pp": PowerPlay(); break;
            case "pk": PenaltyKill(); break;
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

    /// <summary>Рычаги своей зоны: ворота на доске — наши.</summary>
    public static bool OwnZone(string lever) => lever is "dz" or "bo" or "pk";

    void Goalie()
    {
        var p = At(0.88f, 0.5f);
        float r = _sz.Y * 0.04f;
        DrawRect(new Rect2(p - new Vector2(r * 0.7f, r * 1.1f), new Vector2(r * 1.4f, r * 2.2f)), Club.Darkened(0.2f));
        DrawRect(new Rect2(p - new Vector2(r * 0.7f, r * 1.1f), new Vector2(r * 1.4f, r * 2.2f)), Colors.White, false, 2);
    }

    void Burst(Vector2 p, Color c)
    {
        float r = _sz.Y * 0.035f;
        var pts = new Vector2[16];
        for (int i = 0; i < 16; i++) { float t = i / 16f * Mathf.Tau; float rr = i % 2 == 0 ? r : r * 0.45f; pts[i] = p + new Vector2(Mathf.Cos(t) * rr, Mathf.Sin(t) * rr); }
        DrawColoredPolygon(pts, c);
    }

    void Tag(Vector2 p, string text, Color c) => DrawString(Ui.Semi, p, text, HorizontalAlignment.Left, -1, 12, c);

    void Physical()
    {
        // у борта: соперник с шайбой, второй — у ворот; наши — в борьбе
        var a = At(0.8f, 0.14f); var b = At(0.64f, 0.72f);
        Player(a, "", false); Puck(a + new Vector2(_sz.Y * 0.05f, 0)); Player(b, "", false);
        Player(At(0.84f, 0.5f), "", false);
        switch (V)
        {
            case 1:
                Player(At(0.66f, 0.2f), "Н1"); Arrow(At(0.66f, 0.2f), a + new Vector2(-14, 4), Gold, 5); Burst(a + new Vector2(-6, 10), new Color(Ui.Red, 0.8f));
                Player(At(0.5f, 0.66f), "З"); Arrow(At(0.5f, 0.66f), b + new Vector2(-14, 0), Gold, 5); Burst(b + new Vector2(-8, 0), new Color(Ui.Red, 0.8f));
                Player(At(0.72f, 0.4f), "Н2"); Arrow(At(0.72f, 0.4f), At(0.8f, 0.48f), Gold, 3);
                Tag(At(0.38f, 0.42f), "силовой — при каждой возможности", new Color(Ui.Red, 0.85f));
                break;
            case -1:
                Player(At(0.7f, 0.22f), "Н1"); DrawLine(At(0.7f, 0.22f), a + new Vector2(_sz.Y * 0.05f, 0), Ink, 2); 
                Player(At(0.72f, 0.66f), "З"); DrawDashedLine(At(0.72f, 0.66f), b, Club, 2, 6);
                Player(At(0.74f, 0.42f), "Н2");
                Zone(At(0.78f, 0.5f), _sz.X * 0.08f, _sz.Y * 0.2f, new Color(Club, 0.14f));
                Tag(At(0.36f, 0.42f), "клюшкой и положением, между ним и воротами", new Color(Club, 0.95f));
                break;
            default:
                Player(At(0.66f, 0.2f), "Н1"); Arrow(At(0.66f, 0.2f), a + new Vector2(-14, 4), Gold, 4); Burst(a + new Vector2(-6, 10), new Color(Ui.Red, 0.6f));
                Player(At(0.72f, 0.66f), "З"); DrawDashedLine(At(0.72f, 0.66f), b, Club, 2, 6);
                Player(At(0.74f, 0.42f), "Н2");
                break;
        }
    }

    void OwnZoneD()
    {
        // атакуют соперники: двое на синей, трое внизу; ворота наши
        var o = new[] { At(0.42f, 0.24f), At(0.42f, 0.76f), At(0.74f, 0.12f), At(0.76f, 0.86f), At(0.8f, 0.42f) };
        foreach (var p in o) Player(p, "", false);
        Puck(o[0] + new Vector2(_sz.Y * 0.05f, 0));
        switch (V)
        {
            case -1:
                Zone(At(0.76f, 0.5f), _sz.X * 0.12f, _sz.Y * 0.2f, new Color(Club, 0.18f));
                Player(At(0.66f, 0.36f), "ЛН"); Player(At(0.66f, 0.64f), "ПН"); Player(At(0.83f, 0.34f), "ЛЗ"); Player(At(0.83f, 0.66f), "ПЗ"); Player(At(0.74f, 0.5f), "Ц");
                Arrow(o[0] + new Vector2(10, 6), At(0.86f, 0.47f), Ink, 2, true); Arrow(o[1] + new Vector2(10, -6), At(0.86f, 0.53f), Ink, 2, true);
                Tag(At(0.5f, 0.52f), "пятак закрыт, бросают издали", new Color(Club, 0.95f));
                break;
            case 1:
                Player(At(0.47f, 0.28f), "ЛН"); Player(At(0.47f, 0.72f), "ПН"); Player(At(0.72f, 0.18f), "ЛЗ"); Player(At(0.73f, 0.8f), "ПЗ"); Player(At(0.77f, 0.47f), "Ц");
                foreach (var (a, b) in new[] { (0.47f, 0.28f, 0), (0.47f, 0.72f, 1), (0.72f, 0.18f, 2), (0.73f, 0.8f, 3), (0.77f, 0.47f, 4) }.Select(x => (At(x.Item1, x.Item2), o[x.Item3])))
                    DrawDashedLine(a, b, Gold, 2, 5);
                Tag(At(0.5f, 0.52f), "каждый держит своего", new Color(Gold.Darkened(0.4f), 1));
                break;
            default:
                Player(At(0.52f, 0.3f), "ЛН"); Player(At(0.52f, 0.7f), "ПН"); Player(At(0.73f, 0.18f), "ЛЗ"); Player(At(0.8f, 0.6f), "ПЗ"); Player(At(0.72f, 0.46f), "Ц");
                DrawDashedLine(At(0.73f, 0.18f), o[2], Gold, 2, 5);
                Zone(At(0.8f, 0.52f), _sz.X * 0.07f, _sz.Y * 0.13f, new Color(Club, 0.14f));
                break;
        }
    }

    void Breakout()
    {
        // выход из своей зоны: защитник с шайбой за воротами, выходим влево
        var d = At(0.96f, 0.42f);
        Player(d, "З"); Puck(d + new Vector2(-_sz.Y * 0.05f, _sz.Y * 0.03f));
        Player(At(0.7f, 0.3f), "", false); Player(At(0.62f, 0.7f), "", false); Player(At(0.3f, 0.5f), "", false);
        switch (V)
        {
            case -1:
                Player(At(0.4f, 0.08f), "ЛН");
                Curve(d + new Vector2(-6, -12), At(0.9f, 0.0f), At(0.42f, 0.1f), Ink, 3, true);
                Arrow(At(0.4f, 0.08f), At(0.14f, 0.08f), Gold, 3);
                Player(At(0.74f, 0.62f), "Ц"); Player(At(0.86f, 0.82f), "З");
                Arrow(At(0.36f, 0.13f), At(0.02f, 0.2f), Ink, 2, true);
                Tag(At(0.1f, 0.3f), "по борту, при давлении — выбросить", new Color(Club, 0.95f));
                break;
            case 1:
                Player(At(0.08f, 0.4f), "ЛН"); Arrow(At(0.08f, 0.4f), At(0.0f, 0.36f), Gold, 4);
                Arrow(d + new Vector2(-12, 0), At(0.1f, 0.42f), Ink, 3, true);
                Player(At(0.66f, 0.5f), "Ц"); Player(At(0.44f, 0.86f), "ПН"); Arrow(At(0.44f, 0.86f), At(0.14f, 0.8f), Gold, 3);
                Tag(At(0.16f, 0.62f), "первым пасом — на ход", new Color(Gold.Darkened(0.4f), 1));
                break;
            default:
                Player(At(0.72f, 0.55f), "Ц"); Arrow(d + new Vector2(-10, 4), At(0.73f, 0.53f), Ink, 2, true); Arrow(At(0.72f, 0.55f), At(0.4f, 0.5f), Gold, 4);
                Player(At(0.46f, 0.16f), "ЛН"); Player(At(0.42f, 0.88f), "ПН");
                break;
        }
    }

    void PowerPlay()
    {
        // четверо соперника — коробкой
        foreach (var (x, y) in new[] { (0.66f, 0.36f), (0.66f, 0.64f), (0.82f, 0.36f), (0.82f, 0.64f) }) Player(At(x, y), "", false);
        switch (V)
        {
            case -1:   // зонтик: бросок с синей, заслон и подправление
                Player(At(0.4f, 0.5f), "З"); Puck(At(0.43f, 0.5f));
                Player(At(0.5f, 0.16f), "Н"); Player(At(0.5f, 0.84f), "Н"); Player(At(0.88f, 0.44f), "Н"); Player(At(0.74f, 0.5f), "Ц");
                Arrow(At(0.44f, 0.5f), At(0.88f, 0.5f), Ink, 3, true);
                Zone(At(0.88f, 0.48f), _sz.X * 0.04f, _sz.Y * 0.09f, new Color(Gold, 0.35f));
                Tag(At(0.5f, 0.62f), "бросок сквозь заслон", new Color(0, 0, 0, 0.6f));
                break;
            case 1:    // 1-3-1: передачи поперёк и бросок в одно касание
                Player(At(0.4f, 0.5f), "З"); Player(At(0.62f, 0.12f), "Н"); Player(At(0.74f, 0.5f), "Ц"); Player(At(0.62f, 0.88f), "Н"); Player(At(0.88f, 0.5f), "Н");
                Puck(At(0.65f, 0.14f));
                Arrow(At(0.64f, 0.16f), At(0.6f, 0.84f), Ink, 2, true);
                Arrow(At(0.62f, 0.84f), At(0.88f, 0.55f), Ink, 3, true);
                Tag(At(0.3f, 0.3f), "передача — бросок в одно касание", new Color(0, 0, 0, 0.6f));
                break;
            default:
                Player(At(0.42f, 0.32f), "З"); Player(At(0.48f, 0.78f), "Н"); Player(At(0.62f, 0.14f), "Н"); Player(At(0.88f, 0.46f), "Н"); Player(At(0.74f, 0.56f), "Ц");
                Puck(At(0.45f, 0.33f));
                Arrow(At(0.45f, 0.34f), At(0.62f, 0.16f), Ink, 2, true);
                break;
        }
    }

    void PenaltyKill()
    {
        // у соперника пятеро: зонтик; у нас четверо
        var o = new[] { At(0.42f, 0.5f), At(0.56f, 0.14f), At(0.56f, 0.86f), At(0.86f, 0.42f), At(0.74f, 0.6f) };
        foreach (var p in o) Player(p, "", false);
        Puck(o[1] + new Vector2(_sz.Y * 0.05f, 0));
        switch (V)
        {
            case -1:
                Zone(At(0.74f, 0.5f), _sz.X * 0.1f, _sz.Y * 0.2f, new Color(Club, 0.18f));
                Player(At(0.66f, 0.36f), "Н"); Player(At(0.66f, 0.64f), "Н"); Player(At(0.82f, 0.34f), "З"); Player(At(0.82f, 0.66f), "З");
                Tag(At(0.3f, 0.32f), "коробка: периметр — им", new Color(Club, 0.95f));
                break;
            case 1:
                Player(At(0.6f, 0.26f), "Н"); Arrow(At(0.6f, 0.26f), o[1] + new Vector2(6, 12), Gold, 4);
                Player(At(0.5f, 0.4f), "Н"); Arrow(At(0.5f, 0.4f), At(0.46f, 0.47f), Gold, 3);
                Player(At(0.8f, 0.38f), "З"); Player(At(0.8f, 0.64f), "З");
                Tag(At(0.24f, 0.72f), "давим на шайбу — ловим на ошибке", new Color(Gold.Darkened(0.4f), 1));
                break;
            default:
                Player(At(0.6f, 0.5f), "Н"); Player(At(0.7f, 0.3f), "Н"); Player(At(0.7f, 0.7f), "З"); Player(At(0.78f, 0.56f), "З");
                break;
        }
    }

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
        v.AddChild(Ui.V(2, Ui.L("Если — то", 20, Ui.Text, Ds.Display),
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
        card.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 14, null, 18));
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
