using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Характеристики игрока: ключ, название, группа, как показать сырое значение.</summary>
public static class Attrs
{
    public record Def(string Key, string Name, string Tip, Func<PlayerData, string> Raw);

    static string F(double v, string fmt) => double.IsNaN(v) ? "—" : v.ToString(fmt);

    public static readonly (string title, Def[] defs)[] Skater =
    {
        ("АТАКА", new Def[]
        {
            new("att", "Создание моментов", "Личные ожидаемые голы (xG) за 60 минут", p => $"{p.Ixg60:0.00} xG за 60 мин"),
            new("fin", "Реализация", "Голы относительно ожидаемых (сильно сглажено: в основном это везение)", p => $"{p.Fin:0.00} гола на 1 xG"),
            new("sht", "Броски", "Попытки бросков за 60 минут", p => $"{F(p.Xv("sa_60"), "0.0")} за 60 мин"),
            new("pwr", "Мощь броска", "Самый сильный бросок сезона (NHL EDGE)", p => $"{F(p.Xv("shot"), "0.0")} км/ч"),
            new("pas", "Пас", "Первые передачи за 60 минут и передачи за матч", p => $"{F(p.Xv("a1_60"), "0.00")} первых передач за 60 мин"),
            new("fo", "Вбрасывание", "Процент выигранных вбрасываний", p => $"{F(p.Xv("fo_pct") * 100, "0.0")}% из {F(p.Xv("fo_n"), "0")}"),
        }),
        ("ОБОРОНА", new Def[]
        {
            new("dfn", "Игра в обороне", "xG соперника при игроке на льду против времени без него", p => $"{F(p.Xv("xga_rel"), "+0.00;−0.00")} xG против за 60 мин"),
            new("blk", "Блокировка бросков", "Заблокированные броски за 60 минут", p => $"{F(p.Xv("blk_60"), "0.00")} за 60 мин"),
            new("tka", "Отбор", "Отборы шайбы за 60 минут", p => $"{F(p.Xv("tk_60"), "0.00")} за 60 мин"),
            new("hit", "Силовая борьба", "Силовые приёмы за 60 минут", p => $"{F(p.Xv("hit_60"), "0.0")} за 60 мин"),
            new("hnd", "Контроль шайбы", "Потери шайбы за 60 минут (меньше — лучше)", p => $"{F(p.Xv("gv_60"), "0.00")} потерь за 60 мин"),
        }),
        ("ФИЗИКА И ИГРА", new Def[]
        {
            new("spd", "Скорость", "Максимальная скорость катания (NHL EDGE)", p => $"{F(p.Xv("speed"), "0.0")} км/ч"),
            new("sta", "Выносливость", "Игровое время за матч", p => $"{p.Toi:0.0} мин за матч"),
            new("cor", "Владение", "Доля попыток бросков при нём (5 на 5) против времени без него", p => $"{F(p.Xv("cf_rel") * 100, "+0.0;−0.0")}%"),
            new("imp", "Влияние на игру", "Доля ожидаемых голов команды при нём на льду против времени без него", p => $"{F(p.Xv("xg_rel") * 100, "+0.0;−0.0")}%"),
            new("dis", "Дисциплина", "Заработанные удаления минус свои, за 60 минут", p => $"{F(p.Xv("dis_60"), "+0.00;−0.00")} за 60 мин"),
        }),
    };

    public static readonly (string title, Def[] defs)[] Goalie =
    {
        ("ВРАТАРЬ", new Def[]
        {
            new("gk", "Надёжность", "Сейвы сверх ожидаемого на единицу xG против", p => $"{p.Gsax * 100:+0.0;−0.0}% от xG"),
            new("hd", "Опасные броски", "Сейвы сверх ожидаемого на бросках из опасных зон", p => $"{F(p.Xv("hd") * 100, "+0.0;−0.0")}%"),
            new("ld", "Дальние броски", "Сейвы сверх ожидаемого на бросках издалека", p => $"{F(p.Xv("ldmd") * 100, "+0.0;−0.0")}%"),
            new("reb", "Контроль отскоков", "Отскоки относительно ожидаемых (меньше — лучше)", p => $"{F(p.Xv("reb") * 100, "+0.0;−0.0")}%"),
            new("gsta", "Выносливость", "Матчи за последние сезоны (с весами)", p => $"{F(p.Xv("gp_w"), "0")} матчей"),
        }),
    };

    /// <summary>Какие характеристики важны для роли (подсветка, как в FM).</summary>
    public static readonly Dictionary<string, string[]> RoleKeys = new()
    {
        ["Снайпер"] = new[] { "att", "fin", "sht", "pwr" },
        ["Плеймейкер"] = new[] { "pas", "att", "cor", "hnd" },
        ["Двусторонний форвард"] = new[] { "dfn", "imp", "cor", "tka", "att" },
        ["Силовой форвард"] = new[] { "hit", "blk", "dfn", "att" },
        ["Скоростной форвард"] = new[] { "spd", "att", "tka" },
        ["Центр-распасовщик"] = new[] { "pas", "fo", "att", "cor" },
        ["Оборонительный центр"] = new[] { "fo", "dfn", "blk", "tka" },
        ["Атакующий защитник"] = new[] { "pas", "att", "sht", "cor" },
        ["Защитник-домосед"] = new[] { "dfn", "blk", "hit" },
        ["Универсальный защитник"] = new[] { "imp", "dfn", "pas", "cor", "sta" },
        ["Силовой защитник"] = new[] { "hit", "blk", "dfn" },
        ["Вратарь"] = new[] { "gk", "hd" },
    };

    public static string Verdict(PlayerData p)
    {
        int o = p.Ovr;
        string who = p.IsGoalie ? "вратарей" : p.IsDefense ? "защитников" : "нападающих";
        return o >= 95 ? $"Один из лучших {who} мира" : o >= 85 ? $"Один из лучших {who} лиги" : o >= 70 ? $"Сильный игрок уровня НХЛ" :
               o >= 50 ? "Твёрдый игрок состава НХЛ" : o >= 30 ? "Игрок ротации" : "Игрок глубокого резерва";
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

/// <summary>Карточка игрока в стиле WTCS для FM24.</summary>
public partial class PlayerPage : Control
{
    Career C => P.C;
    readonly int _id;
    public PlayerPage() { }
    public PlayerPage(int id) { _id = id; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; TooltipText = " "; }

    public override void _Ready()
    {
        var p = C.Data.PlayerById[_id];
        var t = C.Team(p.Team);
        var v = Ui.V(12);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        v.AddChild(Hero(p, t));

        var row = Ui.H(12);
        var left = Ui.V(12); left.CustomMinimumSize = new Vector2(290, 0);
        left.AddChild(Widget.Make("Позиции", Ui.V(8, new RinkMini(p.Pos), Ui.H(14, Legend("Основная", new Color("43D17A")), p.IsForward ? Legend("Может играть", new Color("E8C25A")) : new Control()))));
        left.AddChild(Widget.Make("Роли", Roles(p), null, null, 0));
        row.AddChild(left);
        row.AddChild(Ui.Expand(Widget.Make("Характеристики", AttrGrid(p))));
        var right = Ui.V(12); right.CustomMinimumSize = new Vector2(330, 0);
        right.AddChild(Widget.Make("Форма (последние 20 матчей)", Form(p)));
        right.AddChild(Widget.Make("Сравнение в составе", Compare(p), null, null, 0));
        row.AddChild(right);
        v.AddChild(row);

        v.AddChild(Stats(p));
        var games = Games(p);
        if (games != null) v.AddChild(games);
    }

    // ---------------------------------------------------------------- шапка
    Control Hero(PlayerData p, TeamData t)
    {
        var c1 = new Color(t.Colors[0]); var c2 = new Color(t.Colors.Count > 1 ? t.Colors[1] : t.Colors[0]);
        var row = Ui.H(10);

        // портрет на фоне огромного номера
        var portrait = new Control { CustomMinimumSize = new Vector2(250, 190), ClipContents = true };
        var g = new Gradient(); g.SetColor(0, c1.Lightened(0.08f)); g.SetColor(1, c1.Darkened(0.45f));
        portrait.AddChild(new TextureRect { Texture = new GradientTexture2D { Gradient = g, FillFrom = new Vector2(0, 0), FillTo = new Vector2(1, 1), Width = 64, Height = 64 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale }.Also(r => r.SetAnchorsPreset(LayoutPreset.FullRect)));
        var num = Ui.L(p.Num?.ToString() ?? "", 190, new Color(c2.Luminance > 0.8f ? Colors.White : c2, 0.55f), Ui.Black, HorizontalAlignment.Center);
        num.SetAnchorsPreset(LayoutPreset.FullRect); num.OffsetTop = -40; num.OffsetLeft = -30;
        portrait.AddChild(num);
        var surname = Ui.L(p.LastRu.ToUpperInvariant(), 13, new Color(1, 1, 1, 0.8f), Ui.Black);
        surname.Position = new Vector2(12, 8);
        portrait.AddChild(surname);
        var photo = new PlayerPhoto(p, 170, new Color(0, 0, 0, 0)) { Position = new Vector2(70, 20) };
        portrait.AddChild(photo);
        row.AddChild(Frame(portrait, 0));

        // плитки: клуб и страна
        var tiles = Ui.V(10);
        tiles.AddChild(Frame(Ui.V(2, new TeamBadge(t, 54) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter }, Ui.L(t.City, 11, Ui.Muted, null, HorizontalAlignment.Center)).Also(x => x.CustomMinimumSize = new Vector2(96, 0)), 8));
        tiles.AddChild(Frame(Ui.V(0, Ui.L(p.Country ?? "", 22, Ui.Text, Ui.Black, HorizontalAlignment.Center), Ui.L(PlayerCard.Country(p.Country), 11, Ui.Muted, null, HorizontalAlignment.Center)).Also(x => x.CustomMinimumSize = new Vector2(96, 0)), 8));
        row.AddChild(tiles);

        // факты
        var age = p.Age(C.Today);
        var facts = Ui.V(4);
        facts.AddChild(Ui.L(p.NameRu, 24, Ui.Text, Ui.Bold));
        facts.AddChild(Ui.L($"#{p.Num} · {Capital(PlayerCard.PosLong(p.Pos))} · {t.Name}", 13, Ui.Muted));
        facts.AddChild(Ui.Spacer(false, 6));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 30); grid.AddThemeConstantOverride("v_separation", 8);
        void Fact(string k, string val, Color? col = null) => grid.AddChild(Ui.V(0, Ui.L(k.ToUpperInvariant(), 10, Ui.Dim, Ui.Bold), Ui.L(val, 14, col ?? Ui.Text, Ui.Semi)));
        Fact("Возраст", age.HasValue ? $"{age} {Ui.Plural(age.Value, "год", "года", "лет")}" : "—");
        Fact("Дата рождения", DateOnly.TryParse(p.Born, out var bd) ? $"{bd.Day:00}.{bd.Month:00}.{bd.Year}" : "—");
        Fact("Рост / вес", $"{p.Height} см / {p.Weight} кг");
        var inj = InjuryModel.Active(C, p.Id);
        Fact("Состояние", inj != null ? $"Травма до {Ui.DateShort(inj.Until)}" : "Здоров", inj != null ? Ui.Loss : Ui.Win);
        Fact("В составе", P.Role(p.Id, p.Team));
        var rank = C.Roster(p.Team).Where(x => x.IsGoalie == p.IsGoalie).OrderByDescending(x => x.Ovr).ToList().IndexOf(p) + 1;
        Fact("Статус", p.IsGoalie ? (C.Lineups[p.Team].G1 == p.Id || rank == 1 ? "Основной вратарь" : "Сменщик") : rank <= 3 ? "Звезда клуба" : rank <= 9 ? "Ключевой игрок" : rank <= 16 ? "Игрок основы" : "Ротация / резерв");
        facts.AddChild(grid);
        row.AddChild(Ui.Expand(Frame(facts, 16)));

        // способности
        var ab = Ui.V(6);
        ab.AddChild(Ui.L("ТЕКУЩИЕ СПОСОБНОСТИ", 10, Ui.Dim, Ui.Bold));
        ab.AddChild(Ui.Stars(p.Ovr, 22));
        ab.AddChild(Ui.L(Attrs.Verdict(p), 13, Ui.Text, Ui.Semi));
        ab.AddChild(Ui.L($"Общий рейтинг {p.Ovr} из 99", 11, Ui.Muted));
        ab.AddChild(Ui.Spacer(false, 6));
        ab.AddChild(Ui.L("ЛУЧШАЯ РОЛЬ", 10, Ui.Dim, Ui.Bold));
        var best = p.Roles.FirstOrDefault();
        ab.AddChild(Ui.H(8, Ui.L(best?.Name ?? "—", 13, Ui.Text, Ui.Semi), best != null ? Ui.Stars(best.V, 12) : new Control()));
        ab.CustomMinimumSize = new Vector2(250, 0);
        row.AddChild(Frame(ab, 16));
        return row;
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

    // ---------------------------------------------------------------- характеристики
    Control AttrGrid(PlayerData p)
    {
        var groups = p.IsGoalie ? Attrs.Goalie : Attrs.Skater;
        var key = Attrs.RoleKeys.GetValueOrDefault(p.Roles.FirstOrDefault()?.Name ?? "") ?? Array.Empty<string>();
        var h = Ui.H(18);
        foreach (var (title, defs) in groups)
        {
            var col = Ui.Expand(Ui.V(2));
            col.AddChild(Ui.L(title, 11, Ui.Muted, Ui.Bold));
            col.AddChild(Ui.Rule());
            foreach (var d in defs)
            {
                int pct = p.Rating(d.Key);
                int v20 = Ui.Attr20(pct);
                bool est = p.Est.Contains(d.Key) || (p.Est.Contains("mp") && d.Key is "sht" or "hit" or "tka" or "blk" or "dis" or "cor" or "hnd" or "imp");
                var row = new PanelContainer { TooltipText = $"{d.Tip}\n{d.Raw(p)}{(est ? "\nМало данных — оценка" : "")}", MouseFilter = MouseFilterEnum.Stop };
                var sb = Ui.Box(key.Contains(d.Key) ? new Color(Ui.AccentBright, 0.16f) : new Color(0, 0, 0, 0), 2, null, 6);
                sb.ContentMarginTop = sb.ContentMarginBottom = 3;
                row.AddThemeStyleboxOverride("panel", sb);
                var val = Ui.L(est ? "~" + v20 : v20.ToString(), 14, est ? new Color(Ui.AttrColor(v20), 0.55f) : Ui.AttrColor(v20), Ui.Bold, HorizontalAlignment.Right);
                val.CustomMinimumSize = new Vector2(30, 0);
                row.AddChild(Ui.H(6, Ui.Expand(Ui.L(d.Name, 13, Ui.Text)), val).Also(x => x.MouseFilter = MouseFilterEnum.Ignore));
                col.AddChild(row);
            }
            h.AddChild(col);
        }
        var v = Ui.V(10, h);
        v.AddChild(Ui.L("Шкала 1–20: место среди игроков той же позиции в лиге. Наведите на строку — увидите реальную цифру. «~» — мало данных, оценка. Подсвечены важные для лучшей роли.", 11, Ui.Dim)
            .Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        return v;
    }

    // ---------------------------------------------------------------- форма
    Control Form(PlayerData p)
    {
        var games = C.Games.Where(g => g.Played && g.Result.Ratings.ContainsKey(p.Id)).OrderBy(g => g.Date).TakeLast(20).ToList();
        var vals = games.Select(g =>
        {
            var r = g.Result; bool home = g.Home == p.Team;
            return (r.Ratings[p.Id], $"{Ui.DateShort(g.Date)}: {(home ? "" : "@")}{C.Team(home ? g.Away : g.Home).City} {(home ? r.HomeGoals : r.AwayGoals)}:{(home ? r.AwayGoals : r.HomeGoals)} — оценка {r.Ratings[p.Id]:0.0}");
        }).ToList();
        var line = p.IsGoalie ? (C.Goalies.GetValueOrDefault(p.Id)?.AvgRating ?? 0) : (C.Skaters.GetValueOrDefault(p.Id)?.AvgRating ?? 0);
        var v = Ui.V(6, new FormChart(vals));
        v.AddChild(Ui.H(10, Ui.L("Средняя оценка в сезоне", 12, Ui.Muted), Ui.Spacer(), Ui.L(line > 0 ? $"{line:0.00}" : "—", 15, Ui.Text, Ui.Black)));
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
            new() { Title = "Способности", Width = 84, Cell = x => Ui.Stars(x.Ovr, 12) },
        }, list, x => { if (x.Id != p.Id) Shell.I.Go("player", null, x.Id.ToString()); }, x => x.Id == p.Id);
        return grid.Root;
    }

    // ---------------------------------------------------------------- статистика
    Control Stats(PlayerData p)
    {
        var real = p.Stats;
        string realName = "НХЛ 2026-27 (реальность, на " + Ui.DateShort(DateOnly.Parse(C.Data.DataDate)) + ")";
        if (p.IsGoalie)
        {
            var rows = new List<(string name, GoalieLine s, bool real)>();
            if (real.Count > 0) rows.Add((realName, new GoalieLine { Gp = (int)real.GetValueOrDefault("gp"), W = (int)real.GetValueOrDefault("w"), L = (int)real.GetValueOrDefault("l"), Otl = (int)real.GetValueOrDefault("otl") }, true));
            if (C.Goalies.TryGetValue(p.Id, out var s1)) rows.Add(("HM27 · регулярный чемпионат", s1, false));
            if (C.PoGoalies.TryGetValue(p.Id, out var s2)) rows.Add(("HM27 · плей-офф", s2, false));
            var g = new Grid<(string name, GoalieLine s, bool real)>(new List<Col<(string name, GoalieLine s, bool real)>>
            {
                new() { Title = "Турнир", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.name, 13) },
                new() { Title = "И", Width = 40, Cell = x => Ui.L($"{x.s.Gp}", 13) },
                new() { Title = "В-П-ПО", Width = 80, Cell = x => Ui.L($"{x.s.W}-{x.s.L}-{x.s.Otl}", 13) },
                new() { Title = "%ОБ", Width = 60, Cell = x => Ui.L(x.real ? (real.ContainsKey("sv") ? $"{real["sv"]:.000}" : "—") : x.s.Sa > 0 ? $"{x.s.SvPct:.000}" : "—", 13, Ui.Text, Ui.Bold) },
                new() { Title = "КН", Width = 50, Cell = x => Ui.L(x.real ? (real.ContainsKey("gaa") ? $"{real["gaa"]:0.00}" : "—") : $"{x.s.Gaa:0.00}", 13) },
                new() { Title = "Сух", Width = 40, Cell = x => Ui.L(x.real ? "—" : $"{x.s.So}", 13) },
                new() { Title = "Ср. оц.", Width = 60, Cell = x => Ui.L(x.real || x.s.Rated == 0 ? "—" : $"{x.s.AvgRating:0.00}", 13) },
            }, rows);
            return Widget.Make("Статистика", g.Root, null, null, 0);
        }
        var srows = new List<(string name, int gp, int g, int a, int ppg, double avg)>();
        if (real.Count > 0) srows.Add((realName, (int)real.GetValueOrDefault("gp"), (int)real.GetValueOrDefault("g"), (int)real.GetValueOrDefault("a"), -1, 0));
        if (C.Skaters.TryGetValue(p.Id, out var k1)) srows.Add(("HM27 · регулярный чемпионат", k1.Gp, k1.G, k1.A, k1.Ppg, k1.AvgRating));
        if (C.PoSkaters.TryGetValue(p.Id, out var k2)) srows.Add(("HM27 · плей-офф", k2.Gp, k2.G, k2.A, k2.Ppg, k2.AvgRating));
        var grid = new Grid<(string name, int gp, int g, int a, int ppg, double avg)>(new List<Col<(string name, int gp, int g, int a, int ppg, double avg)>>
        {
            new() { Title = "Турнир", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.name, 13) },
            new() { Title = "И", Width = 40, Cell = x => Ui.L($"{x.gp}", 13) },
            new() { Title = "Г", Width = 40, Cell = x => Ui.L($"{x.g}", 13) },
            new() { Title = "П", Width = 40, Cell = x => Ui.L($"{x.a}", 13) },
            new() { Title = "О", Width = 44, Cell = x => Ui.L($"{x.g + x.a}", 13, Ui.Text, Ui.Bold) },
            new() { Title = "ГБ", Width = 40, Cell = x => Ui.L(x.ppg < 0 ? "—" : $"{x.ppg}", 13, Ui.Muted) },
            new() { Title = "О/И", Width = 50, Cell = x => Ui.L(x.gp > 0 ? $"{(x.g + x.a) / (double)x.gp:0.00}" : "—", 13, Ui.Muted) },
            new() { Title = "Ср. оц.", Width = 60, Cell = x => Ui.L(x.avg > 0 ? $"{x.avg:0.00}" : "—", 13) },
        }, srows);
        return Widget.Make("Статистика", grid.Root, null, null, 0);
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
        return Widget.Make("Последние матчи в HM27", gg.Root, null, null, 0);
    }

    static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
