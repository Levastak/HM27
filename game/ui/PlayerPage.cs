using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Характеристики игрока: ключ, название, группа, как показать сырое значение.</summary>
public static class Attrs
{
    /// <summary>Оценка словами по КА (1–200).</summary>
    public static string Verdict(PlayerData p, double ca)
    {
        string who = p.IsGoalie ? "вратарей" : p.IsDefense ? "защитников" : "нападающих";
        return ca >= 180 ? $"Один из лучших {who} мира" : ca >= 165 ? $"Один из лучших {who} лиги" : ca >= 145 ? "Сильный игрок уровня НХЛ" :
               ca >= 120 ? "Твёрдый игрок состава НХЛ" : ca >= 100 ? "Игрок ротации" : "Игрок глубокого резерва";
    }

    public static double Stars(int v) => Math.Clamp(Math.Round(v / 20.0 * 2) / 2, 0.5, 5);
}

/// <summary>Мини-площадка с позициями игрока (как схема поля в карточке FM).</summary>
public partial class RinkMini : Control
{
    readonly string _pos;
    public RinkMini() { }
    public RinkMini(string pos) { _pos = pos; CustomMinimumSize = new Vector2(250, 120); MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        var s = Size; float r = s.Y * 0.3f;
        var rect = new Rect2(4, 4, s.X - 8, s.Y - 8);
        var ice = new Color("D9E4EE"); var line = new Color("9FB3C8");
        DrawStyleBox(new StyleBoxFlat { BgColor = new Color(ice, 0.12f), BorderColor = new Color(line, 0.5f) }.Also(b => { b.SetBorderWidthAll(2); b.SetCornerRadiusAll((int)r); }), rect);
        float cx = s.X / 2;
        DrawLine(new Vector2(cx, 6), new Vector2(cx, s.Y - 6), new Color("C8102E", 0.6f), 2);
        DrawLine(new Vector2(s.X * 0.37f, 6), new Vector2(s.X * 0.37f, s.Y - 6), new Color("1A4BB0", 0.7f), 2);
        DrawLine(new Vector2(s.X * 0.63f, 6), new Vector2(s.X * 0.63f, s.Y - 6), new Color("1A4BB0", 0.7f), 2);
        DrawArc(new Vector2(cx, s.Y / 2), s.Y * 0.16f, 0, Mathf.Tau, 32, new Color("1A4BB0", 0.5f), 1.5f);
        DrawLine(new Vector2(s.X * 0.08f, 10), new Vector2(s.X * 0.08f, s.Y - 10), new Color("C8102E", 0.4f), 1.5f);
        DrawLine(new Vector2(s.X * 0.92f, 10), new Vector2(s.X * 0.92f, s.Y - 10), new Color("C8102E", 0.4f), 1.5f);
        // атака — вправо. Позиции: ЛН, Ц, ПН (в чужой зоне), ЛЗ, ПЗ (у синей), В (у своих ворот)
        var spots = new Dictionary<string, Vector2>
        {
            ["LW"] = new(0.76f, 0.22f), ["C"] = new(0.72f, 0.5f), ["RW"] = new(0.76f, 0.78f),
            ["LD"] = new(0.58f, 0.3f), ["RD"] = new(0.58f, 0.7f), ["G"] = new(0.11f, 0.5f),
        };
        var natural = _pos switch { "D" => new[] { "LD", "RD" }, "G" => new[] { "G" }, _ => new[] { _pos } };
        var able = _pos switch { "C" => new[] { "LW", "RW" }, "LW" => new[] { "RW", "C" }, "RW" => new[] { "LW", "C" }, _ => Array.Empty<string>() };
        foreach (var (k, v) in spots)
        {
            var c = new Vector2(v.X * s.X, v.Y * s.Y);
            Color col = natural.Contains(k) ? new Color("43D17A") : able.Contains(k) ? new Color("E8C25A") : new Color(1, 1, 1, 0.15f);
            float rad = natural.Contains(k) ? 8 : 6;
            DrawCircle(c, rad + 3, new Color(0, 0, 0, 0.35f));
            DrawCircle(c, rad, col);
        }
    }
}

/// <summary>График формы: оценки за последние матчи (столбики), как «Форма (последние 20 матчей)» в FM.</summary>
public partial class FormChart : Control
{
    readonly List<(double v, string tip)> _vals;
    public FormChart() { }
    public FormChart(List<(double v, string tip)> vals) { _vals = vals; CustomMinimumSize = new Vector2(0, 110); MouseFilter = MouseFilterEnum.Stop; }

    public override void _Draw()
    {
        var s = Size;
        float bottom = s.Y - 14, top = 6;
        for (int i = 5; i <= 10; i++)
        {
            float y = bottom - (i - 5) / 5f * (bottom - top);
            DrawLine(new Vector2(22, y), new Vector2(s.X, y), new Color(1, 1, 1, i == 7 ? 0.12f : 0.05f), 1);
            DrawString(Ui.Regular, new Vector2(0, y + 4), i.ToString(), HorizontalAlignment.Left, -1, 10, Ui.Dim);
        }
        if (_vals.Count == 0) { DrawString(Ui.Regular, new Vector2(30, s.Y / 2), "Пока нет матчей", HorizontalAlignment.Left, -1, 12, Ui.Muted); return; }
        float w = (s.X - 30) / 20f;
        for (int i = 0; i < _vals.Count; i++)
        {
            double v = _vals[i].v;
            float h = (float)((v - 5) / 5 * (bottom - top));
            var col = v >= 7.5 ? new Color("43D17A") : v >= 6.8 ? new Color("B7E06A") : v >= 6.3 ? new Color("E8C25A") : new Color("E0705A");
            DrawRect(new Rect2(26 + i * w + 2, bottom - h, w - 4, h), col);
        }
    }

    public override string _GetTooltip(Vector2 at)
    {
        if (_vals == null || _vals.Count == 0) return "";
        float w = (Size.X - 30) / 20f;
        int i = (int)((at.X - 26) / w);
        return i >= 0 && i < _vals.Count ? _vals[i].tip : "";
    }
}

/// <summary>
/// Карточка игрока: шапка (портрет, имя, факты, способности) и вкладки — Обзор, Характеристики, Статистика, Карьера.
/// Вкладка — часть адреса в оболочке (назад/вперёд помнят её).
/// </summary>
public partial class PlayerPage : Control
{
    Career C => P.C;
    readonly int _id;
    readonly string _tab;
    public PlayerPage() { }
    public PlayerPage(int id, string tab = null) { _id = id; _tab = tab ?? "overview"; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; TooltipText = " "; }

    public override void _Ready()
    {
        var p = C.Data.PlayerById[_id];
        var t = C.Team(p.Team);
        var v = Ui.V(12);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        v.AddChild(Hero(p, t));
        switch (_tab)
        {
            case "attrs": BuildAttrs(v, p); break;
            case "stats": BuildStats(v, p); break;
            case "career": BuildCareer(v, p); break;
            default: BuildOverview(v, p); break;
        }
    }

    // ---------------------------------------------------------------- вкладки
    void BuildOverview(VBoxContainer v, PlayerData p)
    {
        v.AddChild(PlayerViz.ConditionTiles(C, p));
        var row = Ui.H(12);
        var prof = Widget.Make("Профиль", PlayerViz.Radar(C, p));
        prof.CustomMinimumSize = new Vector2(420, 0);
        row.AddChild(prof);
        var pc = Widget.Make("Сильные и слабые стороны", PlayerViz.ProsCons(C, p));
        pc.CustomMinimumSize = new Vector2(330, 0);
        row.AddChild(pc);
        var season = Ui.V(12, PlayerViz.SeasonNumbers(C, p), Ui.Rule(), Ui.L("Оценки за последние матчи", 13, Ui.Muted), Form(p, false));
        row.AddChild(Ui.Expand(Widget.Make($"Сезон {BioText.Season(BioText.GameSeason(C.Data))}", season, "Вся статистика", () => Shell.I.Go("player", "stats", _id.ToString()))));
        v.AddChild(row);
        var row2 = Ui.H(12);
        row2.AddChild(Ui.Expand(Widget.Make("Роли", Roles(p), "Характеристики", () => Shell.I.Go("player", "attrs", _id.ToString()), 0)));
        if (!p.IsGoalie) row2.AddChild(Ui.Expand(Widget.Make("Особенности", Features(p), null, null, 0)));
        row2.AddChild(Widget.Make("Позиции", Ui.V(8, new RinkMini(p.Pos), Ui.H(14, Legend("Основная", new Color("43D17A")), p.IsForward ? Legend("Может играть", new Color("E8C25A")) : new Control()))));
        row2.AddChild(Ui.Expand(Widget.Make("Сравнение в составе", Compare(p), null, null, 0)));
        v.AddChild(row2);
    }

    void BuildAttrs(VBoxContainer v, PlayerData p)
    {
        var row = Ui.H(12);
        row.AddChild(Ui.Expand(Widget.Make("Характеристики", AttrGrid(p))));
        var right = Ui.V(12); right.CustomMinimumSize = new Vector2(330, 0);
        right.AddChild(Widget.Make("Роли", Roles(p), null, null, 0));
        if (!p.IsGoalie) right.AddChild(Widget.Make("Особенности", Features(p), null, null, 0));
        right.AddChild(Widget.Make("Позиции", Ui.V(8, new RinkMini(p.Pos), Ui.H(14, Legend("Основная", new Color("43D17A")), p.IsForward ? Legend("Может играть", new Color("E8C25A")) : new Control()))));
        row.AddChild(right);
        v.AddChild(row);
    }

    void BuildStats(VBoxContainer v, PlayerData p)
    {
        v.AddChild(Stats(p));
        var row = Ui.H(12);
        var list = PlayerViz.Percentiles(C, p, out var note);
        row.AddChild(Ui.Expand(Widget.Make("На фоне лиги", PlayerViz.PercentileList(list, note))));
        var right = Ui.V(12); right.CustomMinimumSize = new Vector2(520, 0);
        right.AddChild(Widget.Make("Оценки за последние 20 матчей", Form(p)));
        var games = Games(p);
        if (games != null) right.AddChild(games);
        row.AddChild(right);
        v.AddChild(row);
    }

    void BuildCareer(VBoxContainer v, PlayerData p)
    {
        var bioRow = Ui.H(12);
        bioRow.AddChild(Ui.Expand(Widget.Make("Биография", BioWidgets.Biography(C, p))));
        var hon = Ui.V(12); hon.CustomMinimumSize = new Vector2(400, 0);
        hon.AddChild(Widget.Make("Достижения", BioWidgets.Honours(p), null, null, 0));
        bioRow.AddChild(hon);
        v.AddChild(bioRow);
        v.AddChild(Widget.Make("Карьера в НХЛ: регулярные чемпионаты", BioWidgets.NhlCareer(C, p), null, null, 0));
    }

    // ---------------------------------------------------------------- шапка
    Control Hero(PlayerData p, TeamData t)
    {
        var c1 = Broadcast.Fill(t); var c2 = Broadcast.Trim(t);
        var row = Ui.H(0);

        // постер: цвет клуба со штриховкой, огромный номер, фото по плечи, эмблема; правый край — косой срез
        var poster = new Paint(new Vector2(310, 236), q =>
        {
            float w = q.Size.X, h = q.Size.Y, sk = 46, r = Ds.R_L;
            var pts = new List<Vector2>();
            for (int k = 0; k <= 8; k++) { float an = Mathf.Pi + Mathf.Pi / 2 * k / 8; pts.Add(new Vector2(r + r * Mathf.Cos(an), r + r * Mathf.Sin(an))); }
            pts.Add(new Vector2(w, 0)); pts.Add(new Vector2(w - sk, h));
            for (int k = 0; k <= 8; k++) { float an = Mathf.Pi / 2 + Mathf.Pi / 2 * k / 8; pts.Add(new Vector2(r + r * Mathf.Cos(an), h - r + r * Mathf.Sin(an))); }
            var poly = pts.ToArray();
            q.DrawPolygon(poly, poly.Select(v2 => c1.Lerp(c1.Darkened(0.55f), Math.Clamp(v2.Y / h * 0.8f + v2.X / w * 0.3f, 0, 1))).ToArray());
            for (float x = -h; x < w; x += 14) q.DrawLine(new Vector2(x, h), new Vector2(x + h, 0), new Color(1, 1, 1, 0.035f), 5);
            string num = p.Num?.ToString() ?? "";
            if (num.Length > 0)
            {
                int fs = 230; float tw = Ds.Tv.GetStringSize(num, HorizontalAlignment.Left, -1, fs).X;
                q.Text(Ds.Tv, fs, w * 0.5f - tw / 2 - 14, -40, num, new Color(c2.Luminance > 0.85f ? Colors.White : c2, 0.33f));
            }
            Face.Player(q, p, t, new Rect2(30, 6, w - 70, h - 6), poly, Ds.Tv, false);
            Face.Logo(q, t, new Rect2(16, h - 54, 42, 42));
            q.DrawLine(new Vector2(w, 0), new Vector2(w - sk, h), new Color(1, 1, 1, 0.7f), 2, true);
        });
        row.AddChild(poster);
        row.AddChild(Ui.Spacer(false, 26));

        // имя и факты
        var age = p.Age(C.Today);
        var facts = Ui.V(2);
        var nameParts = p.NameRu.Split(' ', 2);
        facts.AddChild(Ui.Spacer(false, 16));
        if (nameParts.Length > 1) facts.AddChild(Ui.L(nameParts[0], 16, Ui.Muted, Ui.Medium));
        facts.AddChild(Ui.L(nameParts.Length > 1 ? nameParts[1] : nameParts[0], 34, Ui.Text, Ds.Display));
        facts.AddChild(Ui.H(6, Ui.L($"#{p.Num} · {Capital(PlayerCard.PosLong(p.Pos))} ·", 14, Ui.Muted), new TeamBadge(t, 18), Links.Club(t, t.Name, 14, Ui.Text)));
        facts.AddChild(Ui.Spacer(false, 14));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 34); grid.AddThemeConstantOverride("v_separation", 8);
        void Fact(string k, string val, Color? col = null, string tip = null) =>
            grid.AddChild(Ui.V(-2, Ui.L(k, 11, Ui.Dim), Ui.L(val, 17, col ?? Ui.Text, Ui.Cond)).Also(x => { x.TooltipText = tip ?? ""; x.MouseFilter = MouseFilterEnum.Stop; }));
        Fact("Возраст", age.HasValue ? $"{age} {Ui.Plural(age.Value, "год", "года", "лет")}" : "—", null,
            DateOnly.TryParse(p.Born, out var bd) ? $"Родился {bd.Day} {Ui.MonthsGen[bd.Month - 1]} {bd.Year}" : null);
        Fact("Страна", PlayerCard.Country(p.Country));
        Fact("Рост и вес", $"{p.Height} см, {p.Weight} кг");
        var dr = p.Bio?.Draft;
        Fact("Драфт", dr is { Count: >= 4 } ? $"{dr[0]}, {dr[3]}-й номер" : "не выбран");
        var rank = C.Roster(p.Team).Where(x => x.IsGoalie == p.IsGoalie).OrderByDescending(x => x.Ovr).ToList().IndexOf(p) + 1;
        Fact("Статус", p.IsGoalie ? (C.Lineups[p.Team].G1 == p.Id ? "Основной вратарь" : C.Lineups[p.Team].G2 == p.Id ? "Сменщик" : "Третий вратарь") : rank <= 3 ? "Звезда клуба" : rank <= 9 ? "Ключевой игрок" : rank <= 16 ? "Игрок основы" : "Ротация");
        var best = p.Roles.FirstOrDefault();
        Fact("Лучшая роль", best?.Name ?? "—");
        var st = PlayerModel.State(C, p.Id);
        Fact("Личность", st == null || st.Know < 0.5f ? "пока неизвестна" : PlayerModel.Personality(st), st == null || st.Know < 0.5f ? Ui.Muted : null,
            st == null || st.Know < 0.5f ? "Характер станет известен, когда клуб лучше узнает игрока" :
                $"{PlayerModel.PersonalityInfo(PlayerModel.Personality(st))}\n\nПрофессионализм: {PlayerModel.Word(st.Prof)}\nАмбиции: {PlayerModel.Word(st.Amb)}\nЛояльность: {PlayerModel.Word(st.Loy)}\n" +
                $"Лидерство: {PlayerModel.Word(st.Lead)}\nСамообладание: {PlayerModel.Word(st.Temp)}\nАдаптивность: {PlayerModel.Word(st.Adapt)}\nТравмоустойчивость: {PlayerModel.Word(st.Inj)}");
        facts.AddChild(grid);
        row.AddChild(Ui.Expand(facts));

        // способности: янтарь табло
        var ab = Ui.V(2);
        double ca = PlayerModel.Visible(C, p, "ca");
        int ovr = Ds.Ovr(C, p);
        ab.AddChild(new Paint(new Vector2(260, 128), q =>
        {
            var cc = new Vector2(62, 64);
            Face.Glow(q, cc, 70, Ds.Tier(ovr), 0.025f);
            Face.Ring(q, cc, 54, ovr, 5, false);
            q.Text(Ds.Display, 38, cc.X - 60, cc.Y - 26, ovr.ToString(), Ds.Tier(ovr), HorizontalAlignment.Center, 120);
            q.Text(Ui.Regular, 11, cc.X - 60, cc.Y + 20, "рейтинг", Ui.Dim, HorizontalAlignment.Center, 120);
            q.Text(Ui.Semi, 14, 136, 40, Ds.TierName(ovr), Ds.Tier(ovr));
            q.Text(Ui.Regular, 11, 136, 62, "по шкале лиги 50–99", Ui.Dim);
        }) { MouseFilter = MouseFilterEnum.Stop, TooltipText = "Рейтинг — текущие способности глазами штаба: бронза до 65, серебро 65–74, золото 75–84, элита 85+" });
        var (pcLo, pcHi) = PlayerModel.PotentialRange(C, p);
        double plo = StarScale.Stars(C, p, pcLo), phi = StarScale.Stars(C, p, pcHi);
        ab.AddChild(Ui.L("Способности", 12, Ui.Dim));
        ab.AddChild(Ui.Stars(StarScale.StarsV(C, p, ca), 24).Also(x => x.TooltipText = StarScale.Relative
            ? $"Текущие способности по оценке штаба — относительно вашего клуба (по шкале лиги: {PlayerModel.Stars(ca):0.#} ★)"
            : "Текущие способности по оценке штаба — по шкале лиги"));
        ab.AddChild(Ui.L(StarScale.Class(C.Data, p, ca), 17, Ui.Text, Ui.Cond).Also(x => x.TooltipText = "Класс игрока по меркам лиги"));
        ab.AddChild(Ui.Spacer(false, 10));
        ab.AddChild(Ui.L("Потенциал", 12, Ui.Dim));
        ab.AddChild(Ui.H(6, Ui.Stars((int)Math.Round(phi * 20), 18),
            Ui.L(plo == phi ? "" : $"{plo:0.#}–{phi:0.#} ★", 12, Ui.Muted)).Also(x => x.TooltipText = "Оценка скаутов: чем лучше клуб знает игрока, тем уже диапазон"));
        ab.AddChild(Ui.L($"потолок: {StarScale.Class(C.Data, p, (pcLo + pcHi) / 2).ToLowerInvariant()}", 13, Ui.Muted));
        ab.CustomMinimumSize = new Vector2(260, 0);
        row.AddChild(Ui.Margin(ab, 10, 14, 22, 10));
        var box = new GlassBox(0, Ds.R_L, 0.05f, new Color(Ds.ClubBright(t), 0.14f));
        box.AddChild(row);
        return box;
    }

    static PanelContainer Frame(Control c, int pad)
    {
        var p = new PanelContainer();
        var sb = Ui.Box(Ui.Panel, 4, Ui.Line, pad);
        if (pad == 0) sb.ContentMarginTop = sb.ContentMarginBottom = sb.ContentMarginLeft = sb.ContentMarginRight = 0;
        p.AddThemeStyleboxOverride("panel", sb);
        p.AddChild(c);
        return p;
    }

    static Control Legend(string text, Color c) => Ui.H(5, new ColorRect { Color = c, CustomMinimumSize = new Vector2(10, 10) }, Ui.L(text, 11, Ui.Muted));

    // ---------------------------------------------------------------- роли
    Control Roles(PlayerData p)
    {
        var v = Ui.V(0);
        foreach (var r in p.Roles.Take(5))
        {
            var row = new PanelContainer();
            var sb = Ui.Box(r == p.Roles[0] ? new Color(Ui.AccentBright, 0.14f) : new Color(0, 0, 0, 0), 0, null, 12);
            sb.ContentMarginTop = sb.ContentMarginBottom = 6; sb.BorderColor = Ui.Line; sb.BorderWidthBottom = 1;
            row.AddThemeStyleboxOverride("panel", sb);
            row.AddChild(Ui.H(8, Ui.Stars(r.V, 12), Ui.Expand(Ui.L(r.Name, 13))));
            v.AddChild(row);
        }
        if (p.Roles.Count == 0) v.AddChild(Ui.Margin(Ui.L("Нет данных", 13, Ui.Muted), 12, 8, 12, 8));
        return v;
    }

    /// <summary>Х-фактор и особенности стиля (из данных; подсказка — что именно видно в данных).</summary>
    Control Features(PlayerData p)
    {
        var v = Ui.V(0);
        PanelContainer Row(Control c, Color bg, string tip)
        {
            var row = new PanelContainer { TooltipText = tip, MouseFilter = MouseFilterEnum.Stop };
            var sb = Ui.Box(bg, 0, null, 12);
            sb.ContentMarginTop = sb.ContentMarginBottom = 6; sb.BorderColor = Ui.Line; sb.BorderWidthBottom = 1;
            row.AddThemeStyleboxOverride("panel", sb);
            row.AddChild(c);
            return row;
        }
        var xf = Traits.XFactor(p.Xf);
        if (xf != null)
            v.AddChild(Row(Ui.H(8, Ui.L("Х", 13, Ui.Gold, Ui.Black), Ui.Expand(Ui.L(xf, 13, Ui.Text, Ui.Semi))), new Color(Ui.Gold, 0.12f),
                "Х-фактор: в этой характеристике игрок среди лучших 1% лиги"));
        foreach (var t in Traits.Of(p))
            v.AddChild(Row(Ui.L(t.Name, 13), new Color(0, 0, 0, 0), t.Info));
        if (xf == null && p.Tr is not { Count: > 0 })
            v.AddChild(Ui.Margin(Ui.L("Без ярко выраженных привычек", 13, Ui.Muted), 12, 8, 12, 8));
        return v;
    }

    // ---------------------------------------------------------------- характеристики
    Control AttrGrid(PlayerData p)
    {
        var defs = HM27.Core.Attrs.For(p);
        var columns = p.IsGoalie
            ? new[] { new[] { "Вратарь" } }
            : new[] { new[] { "Атака" }, new[] { "Оборона", "Физика" }, new[] { "Мышление" } };
        var key = HM27.Core.Roles.Key(p.Roles.FirstOrDefault()?.Name ?? "");
        var st = PlayerModel.State(C, p.Id);
        bool known = st != null && st.Know >= 0.6f;
        var h = Ui.H(18);
        foreach (var groups in columns)
        {
            var col = Ui.Expand(Ui.V(2));
            foreach (var grp in groups)
            {
                if (grp != groups[0]) col.AddChild(Ui.Spacer(false, 8));
                col.AddChild(Ui.L(grp, 16, Ui.Text, Ui.Cond));
                col.AddChild(Ui.Rule());
                foreach (var d in defs.Where(d => d.Group == grp))
                {
                    var (lo, hi) = PlayerModel.Range(C, p, d.Key);
                    int av = (int)Math.Round(PlayerModel.Visible(C, p, d.Key));
                    bool est = !p.A.ContainsKey(d.Key) || p.AttrSe(d.Key) >= 2.5;
                    string arrow = "";
                    Color arrowCol = Ui.Muted;
                    if (known && st.A0.TryGetValue(d.Key, out var a0) && st.A.TryGetValue(d.Key, out var a1))
                    {
                        if (a1 - a0 >= 0.5f) { arrow = "▲"; arrowCol = Ui.Win; }
                        else if (a1 - a0 <= -0.5f) { arrow = "▼"; arrowCol = Ui.Loss; }
                    }
                    string data = p.A.TryGetValue(d.Key, out var e) ? $"По данным: {e[0]:0.0} ± {e[1]:0.0}" : "Нет данных — оценка по уровню игрока";
                    var row = new PanelContainer { TooltipText = $"{d.Tip}\n{data}{(lo != hi ? $"\nСкауты: {lo}–{hi}" : "")}", MouseFilter = MouseFilterEnum.Stop };
                    var sb = Ui.Box(key.Contains(d.Key) ? new Color(Ui.AccentBright, 0.16f) : new Color(0, 0, 0, 0), 2, null, 6);
                    sb.ContentMarginTop = sb.ContentMarginBottom = 3;
                    row.AddThemeStyleboxOverride("panel", sb);
                    string text = lo != hi ? $"{lo}–{hi}" : (est ? "~" : "") + av;
                    var val = Ui.L(text, lo != hi ? 12 : 14, lo != hi || est ? new Color(Ui.AttrColor(av), 0.7f) : Ui.AttrColor(av), Ui.Bold, HorizontalAlignment.Right);
                    val.CustomMinimumSize = new Vector2(lo != hi ? 44 : 30, 0);
                    row.AddChild(Ui.H(4, Ui.Expand(Ui.L(d.Name, 13, Ui.Text)), Ui.L(arrow, 10, arrowCol), val).Also(x => x.MouseFilter = MouseFilterEnum.Ignore));
                    col.AddChild(row);
                }
            }
            h.AddChild(col);
        }
        var v = Ui.V(10, h);
        v.AddChild(Ui.L("Шкала 1–20, средний игрок НХЛ ≈ 11. Диапазон — клуб знает игрока не до конца: скауты и время в клубе сужают его. " +
                        "▲▼ — изменения с начала сезона. «~» — мало данных. Подсвечены важные для лучшей роли.", 11, Ui.Dim)
            .Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        return v;
    }

    // ---------------------------------------------------------------- форма
    Control Form(PlayerData p, bool avg = true)
    {
        var games = C.Games.Where(g => g.Played && g.Result.Ratings.ContainsKey(p.Id)).OrderBy(g => g.Date).TakeLast(20).ToList();
        var vals = games.Select(g =>
        {
            var r = g.Result; bool home = g.Home == p.Team;
            return (r.Ratings[p.Id], $"{Ui.DateShort(g.Date)}: {(home ? "" : "@")}{C.Team(home ? g.Away : g.Home).City} {(home ? r.HomeGoals : r.AwayGoals)}:{(home ? r.AwayGoals : r.HomeGoals)} — оценка {r.Ratings[p.Id]:0.0}");
        }).ToList();
        var line = p.IsGoalie ? (C.Goalies.GetValueOrDefault(p.Id)?.AvgRating ?? 0) : (C.Skaters.GetValueOrDefault(p.Id)?.AvgRating ?? 0);
        var v = Ui.V(6, new FormChart(vals));
        if (avg) v.AddChild(Ui.H(10, Ui.L("Средняя оценка в сезоне", 12, Ui.Muted), Ui.Spacer(), Ui.L(line > 0 ? $"{line:0.00}" : "—", 15, Ui.Text, Ui.Black)));
        return v;
    }

    // ---------------------------------------------------------------- сравнение в составе
    Control Compare(PlayerData p)
    {
        Func<PlayerData, bool> same = p.IsGoalie ? x => x.IsGoalie : p.IsDefense ? x => x.IsDefense : x => x.IsForward;
        var list = C.Roster(p.Team).Where(same).OrderByDescending(x => x.Ovr).Take(5).ToList();
        if (!list.Contains(p)) list[^1] = p;
        var grid = new Grid<PlayerData>(new List<Col<PlayerData>>
        {
            new() { Title = "#", Width = 22, Cell = x => Ui.L($"{C.Roster(p.Team).Where(same).OrderByDescending(y => y.Ovr).ToList().IndexOf(x) + 1}", 12, Ui.Muted) },
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.NameRu, 13, x.Id == p.Id ? Colors.White : Ui.Text, x.Id == p.Id ? Ui.Semi : null) },
            new() { Title = "Способности", Width = 84, Cell = x => Ui.Stars(StarScale.StarsV(C, x, PlayerModel.Visible(C, x, "ca")), 12) },
        }, list, x => { if (x.Id != p.Id) Shell.I.Go("player", null, x.Id.ToString()); }, x => x.Id == p.Id);
        return grid.Root;
    }

    // ---------------------------------------------------------------- статистика
    Control Stats(PlayerData p)
    {
        // только сезон в игре: с первого дня карьеры это своя история, реальная статистика 2026-27 сюда не попадает
        string title = $"Сезон {BioText.Season(BioText.GameSeason(C.Data))}";
        if (p.IsGoalie)
        {
            var rows = new List<(string name, GoalieLine s)>();
            if (C.Goalies.TryGetValue(p.Id, out var s1)) rows.Add(("Регулярный чемпионат", s1));
            if (C.PoGoalies.TryGetValue(p.Id, out var s2)) rows.Add(("Плей-офф", s2));
            if (rows.Count == 0) return Widget.Make(title, Ui.L("В этом сезоне ещё не играл", 13, Ui.Muted));
            var g = new Grid<(string name, GoalieLine s)>(new List<Col<(string name, GoalieLine s)>>
            {
                new() { Title = "Турнир", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.name, 13) },
                new() { Title = "И", Width = 40, Cell = x => Ui.L($"{x.s.Gp}", 13) },
                new() { Title = "В-П-ПО", Width = 80, Cell = x => Ui.L($"{x.s.W}-{x.s.L}-{x.s.Otl}", 13) },
                new() { Title = "Бр", Width = 50, Tip = "Броски в створ", Cell = x => Ui.L($"{x.s.Sa}", 13, Ui.Muted) },
                new() { Title = "%ОБ", Width = 60, Cell = x => Ui.L(x.s.Sa > 0 ? $"{x.s.SvPct:.000}" : "—", 13, Ui.Text, Ui.Bold) },
                new() { Title = "КН", Width = 50, Tip = "Коэффициент надёжности: пропущено за матч", Cell = x => Ui.L($"{x.s.Gaa:0.00}", 13) },
                new() { Title = "Сух", Width = 40, Cell = x => Ui.L($"{x.s.So}", 13) },
                new() { Title = "Ср. оц.", Width = 60, Cell = x => Ui.L(x.s.Rated == 0 ? "—" : $"{x.s.AvgRating:0.00}", 13) },
            }, rows);
            return Widget.Make(title, g.Root, null, null, 0);
        }
        var srows = new List<(string name, SkaterLine s)>();
        if (C.Skaters.TryGetValue(p.Id, out var k1)) srows.Add(("Регулярный чемпионат", k1));
        if (C.PoSkaters.TryGetValue(p.Id, out var k2)) srows.Add(("Плей-офф", k2));
        if (srows.Count == 0) return Widget.Make(title, Ui.L("В этом сезоне ещё не играл", 13, Ui.Muted));
        bool c = p.Pos == "C";
        var cols = new List<Col<(string name, SkaterLine s)>>
        {
            new() { Title = "Турнир", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.name, 13) },
            new() { Title = "И", Width = 36, Cell = x => Ui.L($"{x.s.Gp}", 13) },
            new() { Title = "Г", Width = 36, Cell = x => Ui.L($"{x.s.G}", 13) },
            new() { Title = "П", Width = 36, Cell = x => Ui.L($"{x.s.A}", 13) },
            new() { Title = "О", Width = 40, Cell = x => Ui.L($"{x.s.Pts}", 13, Ui.Text, Ui.Bold) },
            new() { Title = "+/−", Width = 44, Cell = x => Ui.L(Ui.Signed(x.s.Pm, "0"), 13, x.s.Pm > 0 ? Ui.Win : x.s.Pm < 0 ? Ui.Loss : Ui.Muted) },
            new() { Title = "Бол", Width = 40, Tip = "Очки в большинстве", Cell = x => Ui.L($"{x.s.Ppp}", 13, Ui.Muted) },
            new() { Title = "Бр", Width = 40, Tip = "Броски в створ", Cell = x => Ui.L($"{x.s.S}", 13, Ui.Muted) },
            new() { Title = "%Р", Width = 50, Tip = "Реализация: голы / броски в створ", Cell = x => Ui.L(x.s.S > 0 ? $"{100.0 * x.s.G / x.s.S:0.0}" : "—", 13, Ui.Muted) },
            new() { Title = "Вр/И", Width = 54, Tip = "Время на льду за матч", Cell = x => Ui.L(x.s.Gp > 0 && x.s.Toi > 0 ? Ui.Toi(x.s.Toi / (double)x.s.Gp) : "—", 13, Ui.Muted) },
            new() { Title = "Блк", Width = 40, Tip = "Заблокированные броски", Cell = x => Ui.L($"{x.s.Blk}", 13, Ui.Muted) },
            new() { Title = "Сил", Width = 40, Tip = "Силовые приёмы", Cell = x => Ui.L($"{x.s.Hit}", 13, Ui.Muted) },
            new() { Title = "Штр", Width = 40, Tip = "Штрафные минуты", Cell = x => Ui.L($"{x.s.Pim}", 13, Ui.Muted) },
        };
        if (c) cols.Add(new() { Title = "Вбр", Width = 50, Tip = "Выигранные вбрасывания", Cell = x => Ui.L(x.s.Fow + x.s.Fol > 0 ? $"{x.s.FoPct * 100:0.0}%" : "—", 13, Ui.Muted) });
        cols.Add(new() { Title = "Ср. оц.", Width = 56, Cell = x => Ui.L(x.s.AvgRating > 0 ? $"{x.s.AvgRating:0.00}" : "—", 13) });
        var grid = new Grid<(string name, SkaterLine s)>(cols, srows);
        return Widget.Make(title, grid.Root, null, null, 0);
    }

    Control Games(PlayerData p)
    {
        var games = C.Games.Where(gm => gm.Played && gm.Result.Ratings.ContainsKey(p.Id)).OrderByDescending(gm => gm.Date).Take(10).ToList();
        if (games.Count == 0) return null;
        var gg = new Grid<ScheduledGame>(new List<Col<ScheduledGame>>
        {
            new() { Title = "Дата", Width = 70, Align = HorizontalAlignment.Left, Cell = gm => Ui.L(Ui.DateShort(gm.Date), 12, Ui.Muted) },
            new() { Title = "Соперник", Width = 0, Align = HorizontalAlignment.Left, Cell = gm => P.Team(gm.Home == p.Team ? gm.Away : gm.Home) },
            new() { Title = "Счёт", Width = 70, Cell = gm => { bool h = gm.Home == p.Team; return Ui.L($"{(h ? gm.Result.HomeGoals : gm.Result.AwayGoals)}:{(h ? gm.Result.AwayGoals : gm.Result.HomeGoals)}", 13); } },
            new() { Title = p.IsGoalie ? "Сейвы" : "Г+П", Width = 70, Cell = gm =>
            {
                var r = gm.Result;
                if (p.IsGoalie)
                {
                    bool h = gm.Home == p.Team;
                    int sh = h ? r.AwayShots : r.HomeShots, ga = h ? r.AwayGoals : r.HomeGoals;
                    return Ui.L($"{sh - ga}/{sh}", 13);
                }
                int g1 = r.Goals.Count(e => e.Scorer == p.Id && e.Kind != "so"), a1 = r.Goals.Count(e => e.A1 == p.Id || e.A2 == p.Id);
                return Ui.L($"{g1}+{a1}", 13, g1 + a1 > 0 ? Ui.Text : Ui.Dim, g1 + a1 > 0 ? Ui.Bold : null);
            } },
            new() { Title = "Оценка", Width = 60, Cell = gm => { var v = gm.Result.Ratings[p.Id]; return Ui.L($"{v:0.0}", 13, v >= 7.5 ? new Color("43D17A") : v >= 6.8 ? Ui.Text : v >= 6.3 ? Ui.Otl : Ui.Loss, Ui.Bold); } },
        }, games, gm => Main.I.ShowMatch(gm));
        return Widget.Make("Последние матчи", gg.Root, null, null, 0);
    }

    static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
