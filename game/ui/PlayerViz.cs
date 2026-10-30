using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Графика карточки игрока: профиль-паутинка, плитки состояния, перцентили «на фоне лиги», сильные и слабые стороны.
/// </summary>
public static class PlayerViz
{
    // ---------------------------------------------------------------- профиль
    /// <summary>Оси профиля: название и характеристики, из которых она складывается (среднее по шкале 1–20).</summary>
    static readonly (string name, string[] keys)[] SkaterAxes =
    {
        ("Бросок", new[] { "fin", "pwr", "lng", "net" }),
        ("Созидание", new[] { "pas", "hnd", "vis" }),
        ("Катание", new[] { "spd", "acc" }),
        ("Оборона", new[] { "dfn", "tka", "blk", "pk" }),
        ("Борьба", new[] { "hit", "str" }),
        ("Мотор", new[] { "sta", "wrk" }),
        ("Голова", new[] { "dec", "tm", "dis", "cmp", "con" }),
    };
    static readonly (string name, string[] keys)[] GoalieAxes =
    {
        ("Реакция", new[] { "rfx" }), ("Позиция", new[] { "gpos" }), ("Отскок", new[] { "reb" }), ("Заслон", new[] { "scr" }),
        ("Выносливость", new[] { "gsta" }), ("Хладнокровие", new[] { "gcmp" }), ("Стабильность", new[] { "gcon" }),
    };

    static readonly Dictionary<string, Dictionary<string, double>> _avg = new();

    /// <summary>Группа игрока для сравнений: нападающие, защитники, вратари.</summary>
    public static string Group(PlayerData p) => p.IsGoalie ? "G" : p.IsDefense ? "D" : "F";

    /// <summary>Средняя характеристика по данным у игроков той же группы (основа «сильных сторон» и контура на профиле).</summary>
    public static double GroupAvg(GameData d, PlayerData p, string key)
    {
        string g = Group(p);
        if (!_avg.TryGetValue(g, out var m))
        {
            m = new Dictionary<string, double>();
            var pool = d.Players.Where(x => Group(x) == g && x.A != null).ToList();
            foreach (var def in HM27.Core.Attrs.For(p))
            {
                var vals = pool.Where(x => x.A.ContainsKey(def.Key)).Select(x => x.A[def.Key][0]).ToList();
                m[def.Key] = vals.Count > 0 ? vals.Average() : 11;
            }
            _avg[g] = m;
        }
        return m.GetValueOrDefault(key, 11);
    }

    public static Control Radar(Career c, PlayerData p)
    {
        var axes = p.IsGoalie ? GoalieAxes : SkaterAxes;
        var vals = axes.Select(a => (a.name, a.keys.Average(k => PlayerModel.Visible(c, p, k)), a.keys.Average(k => GroupAvg(c.Data, p, k)))).ToList();
        string who = p.IsGoalie ? "вратарь" : p.IsDefense ? "защитник" : "нападающий";
        var v = Ui.V(4, new RadarChart(vals));
        v.AddChild(Ui.H(14, Legend("Игрок", Ui.Blue), Legend($"Средний {who} НХЛ", Ui.Muted, true)).Also(h => h.Alignment = BoxContainer.AlignmentMode.Center));
        return v;
    }

    static Control Legend(string text, Color c, bool dashed = false) =>
        Ui.H(6, new ColorRect { Color = dashed ? new Color(c, 0.6f) : c, CustomMinimumSize = new Vector2(14, dashed ? 2 : 4), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }, Ui.L(text, 11, Ui.Muted));

    // ---------------------------------------------------------------- сильные и слабые стороны
    public static Control ProsCons(Career c, PlayerData p)
    {
        var rows = HM27.Core.Attrs.For(p).Where(d => d.Key != "stk")
            .Select(d => (d, v: PlayerModel.Visible(c, p, d.Key), diff: PlayerModel.Visible(c, p, d.Key) - GroupAvg(c.Data, p, d.Key))).ToList();
        var pros = rows.Where(r => r.v >= 13 && r.diff >= 1.5).OrderByDescending(r => r.diff).Take(4).ToList();
        var cons = rows.Where(r => r.v <= 9.5 && r.diff <= -2).OrderBy(r => r.diff).Take(3).ToList();
        var v = Ui.V(4);
        v.AddChild(Ui.L("Сильные стороны", 15, Ui.Win, Ui.Cond));
        if (pros.Count == 0) v.AddChild(Ui.L("Ничем не выделяется на фоне лиги", 13, Ui.Muted));
        foreach (var r in pros) v.AddChild(Line(r.d, r.v, true));
        v.AddChild(Ui.Spacer(false, 8));
        v.AddChild(Ui.L("Слабые места", 15, Ui.Loss, Ui.Cond));
        if (cons.Count == 0) v.AddChild(Ui.L("Явных слабостей нет", 13, Ui.Muted));
        foreach (var r in cons) v.AddChild(Line(r.d, r.v, false));
        return v;

        Control Line(AttrDef d, double val, bool good) =>
            Ui.H(8, Ui.Icon(good ? "chevron-up" : "chevron-down", 14, good ? Ui.Win : Ui.Loss), Ui.Expand(Ui.L(d.Name, 13)), Ui.AttrV(val, 14))
                .Also(h => h.TooltipText = d.Tip);
    }

    // ---------------------------------------------------------------- плитки состояния
    public static Control Tile(string icon, string caption, string value, Color valueCol, string sub, string tip = null)
    {
        var p = new PanelContainer { TooltipText = tip ?? "", MouseFilter = Control.MouseFilterEnum.Stop };
        var sb = Ui.Box(Ui.Panel, 14, null, 14);
        sb.BorderColor = Ui.Line; sb.BorderWidthTop = 1;
        sb.ContentMarginTop = 10; sb.ContentMarginBottom = 10;
        p.AddThemeStyleboxOverride("panel", sb);
        var v = Ui.V(0);
        v.MouseFilter = Control.MouseFilterEnum.Ignore;
        v.AddChild(Ui.H(6, Ui.Icon(icon, 15, Ui.Muted), Ui.L(caption, 12, Ui.Muted)));
        v.AddChild(Ui.L(value, 26, valueCol, Ui.CondBold).Also(l => l.ClipText = true));
        v.AddChild(Ui.L(sub, 11, Ui.Dim).Also(l => { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }));
        p.AddChild(v);
        p.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return p;
    }

    public static Control ConditionTiles(Career c, PlayerData p)
    {
        var h = Ui.H(10);
        var st = PlayerModel.State(c, p.Id);
        var f = PlayerForm.Of(c, p.Id);
        // форма
        string fw = f.Games < 3 ? "—" : (f.Arrow > 0 ? "↑ " : f.Arrow < 0 ? "↓ " : "") + f.Word;
        var fc = f.Word switch { "Отличная" => Ui.Win, "Хорошая" => new Color("8BD3A0"), "Обычная" => Ui.Text, "Спад" => Ui.Otl, _ => Ui.Loss };
        h.AddChild(Tile("flame", "Форма", fw, f.Games < 3 ? Ui.Muted : fc,
            f.Games < 3 ? "мало матчей" : $"оценка {f.Recent:0.0} при обычной {f.Recent - f.Score:0.0}",
            "Форма: средняя оценка последних матчей против его обычного уровня в сезоне"));
        // свежесть
        double fresh = st?.Fresh ?? 100;
        string word = Fatigue.Word(st);
        var frc = word switch { "Свежий" => Ui.Win, "В норме" => Ui.Text, "Уставший" => Ui.Otl, _ => Ui.Loss };
        h.AddChild(Tile(fresh >= 85 ? "battery-full" : fresh >= 60 ? "battery-medium" : "battery-low", "Свежесть", $"{fresh:0}%", frc, word.ToLowerInvariant(),
            "Свежесть: нагрузка за последние дни (минуты, матчи подряд). Ниже 75% — игрок слабее и чаще травмируется"));
        // здоровье
        var inj = InjuryModel.Active(c, p.Id);
        h.AddChild(Tile("heart-pulse", "Здоровье", inj != null ? "Травма" : "Здоров", inj != null ? Ui.Loss : Ui.Win,
            inj != null ? $"до {Ui.DateShort(inj.Until)}" : "готов играть"));
        // настрой
        float mor = st?.Morale ?? Morale.BASE;
        h.AddChild(Tile("heart-pulse", "Настрой", Morale.Word(mor), RoomPage.MoodColor(mor),
            st != null && st.Grief >= 4 ? "хочет больше времени" : $"{mor:0} из 100", "Настрой: роль и минуты, результаты, свои оценки, решения тренера"));
        // место в составе
        string unitSub = p.IsGoalie ? "вратарь" : p.IsDefense ? "защита" : "нападение";
        var lu = c.Lineups[p.Team];
        var unit = lu.F.Concat(lu.D).FirstOrDefault(u => u.Contains(p.Id));
        if (unit != null && !p.IsGoalie) unitSub = $"сыгранность звена {c.UnitFam(unit) * 100:0}%";
        h.AddChild(Tile("shirt", "В составе", P.Role(p.Id, p.Team), Ui.Text, unitSub,
            "Сыгранность: растёт от минут вместе на льду. Новое звено теряет до 2% голов, сыгранное весь сезон прибавляет около 1%"));
        if (p.IsGoalie)
        {
            var g = c.Goalies.GetValueOrDefault(p.Id);
            h.AddChild(Tile("shield", "Сейвы сверх ожидаемых", g == null || g.Sa == 0 ? "—" : Ui.Signed(g.Gsax, "0.0"),
                g == null || g.Sa == 0 ? Ui.Muted : g.Gsax >= 0 ? Ui.Win : Ui.Loss, g == null ? "в сезоне не играл" : $"{g.Sa} {Ui.Plural(g.Sa, "бросок", "броска", "бросков")} в створ",
                "Ожидаемые голы по броскам в створ минус пропущенные: сколько голов вратарь «украл» сверх среднего"));
            h.AddChild(Tile("gauge", "Средняя оценка", g is { Rated: > 0 } ? $"{g.AvgRating:0.00}" : "—", Ui.Gold, g == null ? "" : $"{g.Gp} {Ui.Plural(g.Gp, "матч", "матча", "матчей")}"));
        }
        else
        {
            var s = c.Skaters.GetValueOrDefault(p.Id);
            h.AddChild(Tile("clock", "Время на льду", s is { Gp: > 0, Toi: > 0 } ? Ui.Toi(s.Toi / (double)s.Gp) : "—", Ui.Text, "в среднем за матч"));
            h.AddChild(Tile("gauge", "Средняя оценка", s is { Rated: > 0 } ? $"{s.AvgRating:0.00}" : "—", Ui.Gold, s == null ? "в сезоне не играл" : $"{s.Gp} {Ui.Plural(s.Gp, "матч", "матча", "матчей")}"));
        }
        return h;
    }

    // ---------------------------------------------------------------- сезон крупно
    public static Control SeasonNumbers(Career c, PlayerData p)
    {
        var g = Ui.H(0);
        void Num(string cap, string val, Color? col = null, string tip = null)
        {
            var v = Ui.V(-4, Ui.L(val, 34, col ?? Ui.Text, Ui.CondBold, HorizontalAlignment.Center), Ui.L(cap, 12, Ui.Muted, null, HorizontalAlignment.Center));
            v.TooltipText = tip ?? ""; v.MouseFilter = Control.MouseFilterEnum.Stop;
            g.AddChild(Ui.Expand(v));
        }
        if (p.IsGoalie)
        {
            var s = c.Goalies.GetValueOrDefault(p.Id) ?? new GoalieLine();
            Num("матчей", $"{s.Gp}"); Num("побед", $"{s.W}"); Num("% отражённых", s.Sa > 0 ? $"{s.SvPct:.000}" : "—", Ui.Gold);
            Num("пропускает", s.Gp > 0 ? $"{s.Gaa:0.00}" : "—", null, "Коэффициент надёжности: пропущено за матч"); Num("на ноль", $"{s.So}");
        }
        else
        {
            var s = c.Skaters.GetValueOrDefault(p.Id) ?? new SkaterLine();
            Num("матчей", $"{s.Gp}"); Num("голов", $"{s.G}"); Num("передач", $"{s.A}"); Num("очков", $"{s.Pts}", Ui.Gold);
            Num("+/−", Ui.Signed(s.Pm, "0"), s.Pm > 0 ? Ui.Win : s.Pm < 0 ? Ui.Loss : Ui.Text);
            Num("xG", s.Ixg > 0 ? $"{s.Ixg:0.0}" : "—", null, "Ожидаемые голы его бросков в створ: сколько забил бы средний снайпер с тех же позиций");
        }
        return g;
    }

    // ---------------------------------------------------------------- на фоне лиги
    public sealed record Pct(string Name, string Value, double P, string Tip);

    /// <summary>Перцентили сезона в игре против своей группы (нападающие / защитники / вратари) с достаточным временем.</summary>
    public static List<Pct> Percentiles(Career c, PlayerData p, out string note)
    {
        var res = new List<Pct>();
        string g = Group(p);
        if (p.IsGoalie)
        {
            var pool = c.Goalies.Where(kv => c.Data.PlayerById.TryGetValue(kv.Key, out var x) && x.IsGoalie).Select(kv => kv.Value).ToList();
            int maxGp = pool.Count > 0 ? pool.Max(x => x.Gp) : 0;
            pool = pool.Where(x => x.Gp >= Math.Max(3, maxGp / 4)).ToList();
            note = $"Среди {pool.Count} {Ui.Plural(pool.Count, "вратаря", "вратарей", "вратарей")} с {Math.Max(3, maxGp / 4)}+ матчами";
            if (!c.Goalies.TryGetValue(p.Id, out var me) || me.Gp == 0) return res;
            void Add(string name, Func<GoalieLine, double> f, Func<double, string> fmt, bool lowGood = false, string tip = null)
            {
                double v = f(me);
                res.Add(new Pct(name, fmt(v), Rank(pool.Select(f).ToList(), v, lowGood), tip));
            }
            Add("% отражённых", x => x.SvPct, v => $"{v:.000}");
            Add("Пропускает за матч", x => x.Gaa, v => $"{v:0.00}", true);
            Add("Сейвы сверх ожидаемых", x => x.Gsax, v => Ui.Signed(v, "0.0"), false, "Ожидаемые голы по броскам в створ минус пропущенные");
            Add("Сейвы сверх ожидаемых за 30 бросков", x => x.Sa > 0 ? x.Gsax / x.Sa * 30 : 0, v => Ui.Signed(v, "0.00"));
            Add("Доля побед", x => x.Gp > 0 ? x.W / (double)x.Gp : 0, v => $"{v * 100:0}%");
            Add("Средняя оценка", x => x.AvgRating, v => $"{v:0.00}");
            return res;
        }
        var all = c.Skaters.Where(kv => c.Data.PlayerById.TryGetValue(kv.Key, out var x) && Group(x) == g && kv.Value.Toi > 0).Select(kv => kv.Value).ToList();
        double maxToi = all.Count > 0 ? all.Max(x => x.Toi) : 0;
        var sp = all.Where(x => x.Toi >= maxToi * 0.25).ToList();
        string who = g == "D" ? "защитников" : "нападающих";
        note = $"Среди {sp.Count} {who} с достаточным временем на льду; «за 60» — на 60 минут игрового времени";
        if (!c.Skaters.TryGetValue(p.Id, out var m) || m.Toi == 0) return res;
        static double Per60(int n, SkaterLine x) => x.Toi > 0 ? n * 3600.0 / x.Toi : 0;
        void A(string name, Func<SkaterLine, double> f, Func<double, string> fmt, bool lowGood = false, string tip = null, List<SkaterLine> pool = null)
        {
            double v = f(m);
            res.Add(new Pct(name, fmt(v), Rank((pool ?? sp).Select(f).ToList(), v, lowGood), tip));
        }
        A("Очки за 60", x => Per60(x.Pts, x), v => $"{v:0.00}");
        A("Голы за 60", x => Per60(x.G, x), v => $"{v:0.00}");
        A("Передачи за 60", x => Per60(x.A, x), v => $"{v:0.00}");
        A("xG за 60", x => x.Toi > 0 ? x.Ixg * 3600 / x.Toi : 0, v => $"{v:0.00}", false, "Ожидаемые голы своих бросков в створ");
        A("Голы сверх xG", x => x.G - x.Ixg, v => Ui.Signed(v, "0.0"), false, "Забил больше или меньше, чем средний игрок с тех же бросков: завершение плюс удача");
        A("Броски в створ за 60", x => Per60(x.S, x), v => $"{v:0.0}");
        A("+/− за 60", x => Per60(x.Pm, x), v => Ui.Signed(v, "0.00"));
        A("Блоки за 60", x => Per60(x.Blk, x), v => $"{v:0.0}");
        A("Силовые за 60", x => Per60(x.Hit, x), v => $"{v:0.0}");
        A("Отборы за 60", x => Per60(x.Tk, x), v => $"{v:0.0}");
        A("Потери за 60", x => Per60(x.Gv, x), v => $"{v:0.0}", true, "Меньше — лучше");
        A("Штрафные минуты за 60", x => Per60(x.Pim, x), v => $"{v:0.0}", true, "Меньше — лучше");
        A("Время на льду за матч", x => x.Gp > 0 ? x.Toi / (double)x.Gp : 0, v => Ui.Toi(v));
        if (p.Pos == "C" && m.Fow + m.Fol >= 30)
            A("Выигранные вбрасывания", x => x.FoPct, v => $"{v * 100:0.0}%", false, null, sp.Where(x => x.Fow + x.Fol >= 30).ToList());
        return res;
    }

    static double Rank(List<double> pool, double v, bool lowGood)
    {
        if (pool.Count == 0) return 0.5;
        double below = pool.Count(x => lowGood ? x > v : x < v), eq = pool.Count(x => Math.Abs(x - v) < 1e-9);
        return Math.Clamp((below + 0.5 * eq) / pool.Count, 0, 1);
    }

    public static Color PctColor(double p) => p >= 0.8 ? new Color("35C985") : p >= 0.6 ? new Color("8BD3A0") : p >= 0.4 ? new Color("C9D3E0") : p >= 0.2 ? Ui.Otl : Ui.Loss;

    public static Control PercentileList(List<Pct> list, string note)
    {
        var v = Ui.V(0);
        if (list.Count == 0) { v.AddChild(Ui.L("В этом сезоне ещё не играл", 13, Ui.Muted)); return v; }
        foreach (var x in list)
        {
            var row = Ui.H(10);
            row.TooltipText = x.Tip ?? ""; row.MouseFilter = Control.MouseFilterEnum.Stop;
            row.AddChild(Ui.L(x.Name, 13).Also(l => l.CustomMinimumSize = new Vector2(230, 26)));
            row.AddChild(Ui.L(x.Value, 14, Ui.Text, Ui.Cond, HorizontalAlignment.Right).Also(l => l.CustomMinimumSize = new Vector2(64, 0)));
            row.AddChild(Ui.Expand(new PctBar(x.P)));
            int pc = (int)Math.Round(x.P * 100);
            row.AddChild(Ui.L($"{Math.Clamp(pc, 1, 99)}", 15, PctColor(x.P), Ui.CondBold, HorizontalAlignment.Right).Also(l => l.CustomMinimumSize = new Vector2(34, 0)));
            v.AddChild(row);
        }
        v.AddChild(Ui.Spacer(false, 6));
        v.AddChild(Ui.L($"Перцентиль: доля игроков, у которых показатель хуже. {note}.", 11, Ui.Dim).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        return v;
    }
}

/// <summary>Полоса перцентиля: дорожка, отметка медианы, заливка цветом уровня.</summary>
public partial class PctBar : Control
{
    readonly double _p;
    public PctBar() { }
    public PctBar(double p) { _p = p; CustomMinimumSize = new Vector2(120, 26); MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        float h = 8, y = (Size.Y - h) / 2, w = Size.X;
        DrawRect(new Rect2(0, y, w, h), new Color(Ui.Line, 0.8f));
        DrawRect(new Rect2(0, y, (float)(w * Math.Clamp(_p, 0.02, 1)), h), PlayerViz.PctColor(_p));
        DrawLine(new Vector2(w / 2, y - 3), new Vector2(w / 2, y + h + 3), new Color(Ui.Text, 0.35f), 1);
    }
}

/// <summary>Паутинка профиля: игрок (синий) поверх среднего игрока его группы (пунктир). Шкала 1–20.</summary>
public partial class RadarChart : Control
{
    readonly List<(string name, double v, double avg)> _axes;
    public RadarChart() { }
    public RadarChart(List<(string, double, double)> axes) { _axes = axes; CustomMinimumSize = new Vector2(320, 280); MouseFilter = MouseFilterEnum.Stop; }

    Vector2 Pt(int i, double v, Vector2 c, float r)
    {
        float a = -Mathf.Pi / 2 + i * Mathf.Tau / _axes.Count;
        float k = (float)(Math.Clamp(v, 0, 20) / 20.0);
        return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * k;
    }

    public override void _Draw()
    {
        if (_axes == null || _axes.Count < 3) return;
        var c = Size / 2 + new Vector2(0, 4);
        float r = Math.Min(Size.X / 2 - 70, Size.Y / 2 - 28);
        int n = _axes.Count;
        // кольца 5/10/15/20
        for (int ring = 1; ring <= 4; ring++)
        {
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = Pt(i % n, ring * 5, c, r);
            DrawPolyline(pts, new Color(Ui.Line, ring == 4 ? 1f : 0.7f), 1);
        }
        for (int i = 0; i < n; i++) DrawLine(c, Pt(i, 20, c, r), new Color(Ui.Line, 0.7f), 1);
        // средний игрок группы — пунктир
        for (int i = 0; i < n; i++)
            DrawDashedLine(Pt(i, _axes[i].avg, c, r), Pt((i + 1) % n, _axes[(i + 1) % n].avg, c, r), new Color(Ui.Muted, 0.7f), 1.5f, 4);
        // игрок
        var poly = new Vector2[n];
        for (int i = 0; i < n; i++) poly[i] = Pt(i, _axes[i].v, c, r);
        DrawColoredPolygon(poly, new Color(Ui.Blue, 0.22f));
        var outline = poly.Append(poly[0]).ToArray();
        DrawPolyline(outline, Ui.Blue, 2, true);
        foreach (var q in poly) DrawCircle(q, 3, Ui.Blue);
        // подписи осей
        var font = Ui.Cond;
        for (int i = 0; i < n; i++)
        {
            var q = Pt(i, 23.5, c, r);
            string label = _axes[i].name;
            float tw = font.GetStringSize(label, HorizontalAlignment.Left, -1, 14).X;
            float dx = Math.Abs(q.X - c.X) < 6 ? -tw / 2 : q.X < c.X ? -tw : 0;
            DrawString(font, new Vector2(q.X + dx, q.Y + 5), label, HorizontalAlignment.Left, -1, 14, Ui.Text);
            string val = $"{_axes[i].v:0}";
            DrawString(Ui.CondBold, new Vector2(q.X + dx, q.Y + 20), val, HorizontalAlignment.Left, -1, 13, Ui.AttrColor((int)Math.Round(_axes[i].v)));
        }
    }

    public override string _GetTooltip(Vector2 at) =>
        _axes == null ? "" : string.Join("\n", _axes.Select(a => $"{a.name}: {a.v:0.0} (средний {a.avg:0.0})"));
}

/// <summary>Полоса «наши — их»: доля слева синим, остальное — приглушённо.</summary>
public partial class SplitBar : Control
{
    readonly double _p;
    readonly Color _a, _b;
    public SplitBar() { }
    public SplitBar(double p, Color a, Color b, int h = 8) { _p = p; _a = a; _b = b; CustomMinimumSize = new Vector2(60, h + 6); MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        float h = CustomMinimumSize.Y - 6, y = (Size.Y - h) / 2, w = Size.X;
        float x = (float)(w * Math.Clamp(_p, 0, 1));
        DrawRect(new Rect2(0, y, x - 1, h), _a);
        DrawRect(new Rect2(x + 1, y, w - x - 1, h), _b);
    }
}
