using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// «Трибуна» во весь экран: настроение болельщиков по ходу сезона (график после каждого своего матча) и все посты,
/// сгруппированные по матчам, с фильтром «радуются / ругаются». Только отражение результатов — на игру не влияет.
/// </summary>
public partial class StandsPage : Control
{
    Career C => P.C;
    static int _filter;
    VBoxContainer _posts;

    public StandsPage() { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        var v = Ui.V(14);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        var me = C.UserTeam;
        var t = C.Team(me);
        float m = C.FanMood;

        var tiles = Ui.H(10);
        tiles.AddChild(PlayerViz.Tile("megaphone", "Трибуны", Voices.MoodWord(m), MoodCol(m), $"{m:0} из 100",
            "Настроение болельщиков: результат против прогноза, серии, место против ожиданий прессы. Один матч мало что меняет, серия — меняет"));
        int exp = Pundits.DivisionPick(C, me);
        int now = C.Sorted(x => x.Division == t.Division).FindIndex(x => x.Team == me) + 1;
        tiles.AddChild(PlayerViz.Tile("newspaper", "Пресса ждала", exp > 0 ? $"{exp}-е место" : "—", Ui.Text,
            exp > 0 ? $"в дивизионе; сейчас {now}-е" : "прогноз появится перед сезоном", "Где пресса ставила клуб в дивизионе до сезона"));
        var row = C.Table.GetValueOrDefault(me);
        string streak = row == null || row.Gp == 0 || string.IsNullOrEmpty(row.Streak) ? "—" : StreakWord(row.Streak);
        tiles.AddChild(PlayerViz.Tile("flame", "Серия", streak, row?.Streak?.StartsWith("В") == true ? Ui.Win : row?.Streak?.StartsWith("П") == true ? Ui.Loss : Ui.Text,
            row == null ? "" : $"{row.W}-{row.L}-{row.Otl} в сезоне"));
        var mine = C.Fans.Where(f => f.Team == me).ToList();
        var best = mine.OrderByDescending(f => f.Likes).FirstOrDefault();
        tiles.AddChild(PlayerViz.Tile("star", "Самый популярный пост", best == null ? "—" : $"♥ {best.Likes}", Ui.Gold, best?.Text ?? "постов пока нет", best?.Text));
        v.AddChild(tiles);

        // график настроения
        var pts = Trend();
        Control chart = pts.Count < 2
            ? Ui.L("График появится после пары матчей: точка — настроение трибун после каждого нашего матча.", 13, Ui.Muted).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart)
            : new FanChart(pts) { CustomMinimumSize = new Vector2(0, 230), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddChild(Widget.Make("Настроение по ходу сезона", chart, null, null, 12));

        // посты
        var head = Ui.H(10, Ui.Expand(Ui.L($"Посты болельщиков · {mine.Count}", 17, Ui.Text, Ui.Semi)),
            new Segmented(new[] { "Все", "Радуются", "Ругаются" }, _filter, k => { _filter = k; FillPosts(); }));
        v.AddChild(head);
        _posts = Ui.V(12);
        v.AddChild(_posts);
        FillPosts();
    }

    static string StreakWord(string s)
    {
        if (s.Length < 2 || !int.TryParse(s[1..].TrimStart('О'), out var n)) return s;
        return s[0] == 'В' ? $"{n} {Ui.Plural(n, "победа", "победы", "побед")}" : s.Contains('О') ? $"{n} без побед" : $"{n} {Ui.Plural(n, "поражение", "поражения", "поражений")}";
    }

    /// <summary>Стеклянная карточка без шапки (как Widget, но заголовок — внутри содержимого).</summary>
    public static PanelContainer Glass(Control content, int pad = 18)
    {
        var p = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var sb = Ds.GlassBoxStyle(Ds.R_L, 0, 0.05f);
        sb.BgColor = Ui.Panel;
        sb.ContentMarginLeft = sb.ContentMarginRight = pad; sb.ContentMarginTop = sb.ContentMarginBottom = pad - 4;
        p.AddThemeStyleboxOverride("panel", sb);
        p.AddChild(content);
        return p;
    }

    public static Color MoodCol(float m) => m >= 65 ? Ui.Win : m >= 45 ? Ui.Text : m >= 30 ? Ui.Otl : Ui.Loss;

    /// <summary>Точки графика: этот сезон, со счётом матча. Для старых сохранений (до записи истории) — пусто.</summary>
    List<FanChart.Pt> Trend()
    {
        var from = C.Games.Count == 0 ? DateOnly.MinValue : C.Games.Min(g => g.Date);
        var byId = C.Games.ToDictionary(g => g.Id);
        var res = new List<FanChart.Pt>();
        foreach (var f in C.FanTrend.Where(f => f.Date >= from))
        {
            if (!byId.TryGetValue(f.Game, out var g) || !g.Played) continue;
            var (gf, ga, r) = Score(g);
            res.Add(new FanChart.Pt(f.Mood, r, $"{Ui.DateShort(f.Date)} · {(g.Home == C.UserTeam ? "дома" : "в гостях")} с {C.Team(g.Home == C.UserTeam ? g.Away : g.Home).City} {gf}:{ga}{(g.Result.End == "REG" ? "" : g.Result.End == "SO" ? " Б" : " ОТ")}\nтрибуны: {Voices.MoodWord(f.Mood).ToLowerInvariant()} ({f.Mood:0})"));
        }
        return res;
    }

    (int gf, int ga, char r) Score(ScheduledGame g)
    {
        bool home = g.Home == C.UserTeam;
        int gf = home ? g.Result.HomeGoals : g.Result.AwayGoals, ga = home ? g.Result.AwayGoals : g.Result.HomeGoals;
        return (gf, ga, gf > ga ? 'W' : g.Result.End != "REG" ? 'O' : 'L');
    }

    void FillPosts()
    {
        Ui.Clear(_posts);
        var me = C.UserTeam;
        var posts = C.Fans.Where(f => f.Team == me && (_filter == 0 || (_filter == 1 ? f.Mood > 0 : f.Mood < 0))).OrderByDescending(f => f.Date).ThenByDescending(f => f.Likes).ToList();
        if (posts.Count == 0)
        {
            _posts.AddChild(Ui.L(C.Fans.Any(f => f.Team == me) ? "Таких постов нет." : "Трибуны ждут первого матча.", 13, Ui.Muted));
            return;
        }
        // по матчам: шапка с соперником и счётом, под ней посты этого вечера; две колонки карточек
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        foreach (var day in posts.GroupBy(f => f.Date).Take(40))
        {
            var g = C.Games.FirstOrDefault(x => x.Played && x.Date == day.Key && (x.Home == me || x.Away == me));
            var card = Ui.V(10);
            if (g != null)
            {
                var (gf, ga, r) = Score(g);
                var opp = C.Team(g.Home == me ? g.Away : g.Home);
                var rc = r == 'W' ? Ui.Win : r == 'O' ? Ui.Otl : Ui.Loss;
                var hh = Ui.H(8, new TeamBadge(opp, 22).Also(x => x.SizeFlagsVertical = SizeFlags.ShrinkCenter),
                    Ui.Expand(Ui.V(-2, Ui.L($"{(g.Home == me ? "Дома" : "В гостях")} с {opp.City}", 13, Ui.Text, Ui.Semi), Ui.L(Ui.DateShort(day.Key), 11, Ui.Dim))),
                    Ui.L($"{gf}:{ga}{(g.Result.End == "REG" ? "" : g.Result.End == "SO" ? " Б" : " ОТ")}", 22, rc, Ui.CondBold));
                hh.MouseFilter = MouseFilterEnum.Stop;
                hh.MouseDefaultCursorShape = CursorShape.PointingHand;
                hh.TooltipText = "Открыть матч";
                hh.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Main.I.ShowMatch(g); };
                card.AddChild(hh);
                card.AddChild(new ColorRect { Color = new Color(1, 1, 1, 0.06f), CustomMinimumSize = new Vector2(0, 1) });
            }
            else card.AddChild(Ui.L(Ui.DateShort(day.Key), 12, Ui.Dim));
            foreach (var f in day.OrderByDescending(f => f.Likes)) card.AddChild(Post(f));
            grid.AddChild(Glass(card, 16));
        }
        _posts.AddChild(grid);
    }

    static Control Post(FanPost f)
    {
        var mc = f.Mood > 0 ? Ui.Win : f.Mood < 0 ? Ui.Loss : Ui.Muted;
        var av = new Paint(new Vector2(30, 30), q =>
        {
            q.DrawCircle(q.Size / 2, 15, new Color(mc, 0.18f));
            q.DrawArc(q.Size / 2, 14.5f, 0, Mathf.Tau, 32, new Color(mc, 0.6f), 1.2f, true);
            var s = Voices.Initials(f.Handle.TrimStart('@')).ToUpperInvariant();
            q.DrawString(Ui.Semi, new Vector2(0, 20), s, HorizontalAlignment.Center, 30, 11, Ui.Text);
        });
        av.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        return Ui.H(10, av, Ui.Expand(Ui.V(2,
            Ui.H(6, Ui.L(f.Handle, 12, Ui.Blue, Ui.Semi), Ui.Spacer(), Ui.L($"♥ {f.Likes}", 11, f.Likes >= 250 ? Ui.Gold : Ui.Dim)),
            Ui.L(f.Text, 14, Ui.Text).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart))));
    }
}

/// <summary>График настроения трибун: полосы «В ярости … Эйфория», линия и точки по результату (наведение — матч).</summary>
public partial class FanChart : Control
{
    public readonly record struct Pt(float Mood, char Res, string Tip);
    readonly List<Pt> _pts;
    int _hover = -1;
    public FanChart() { }
    public FanChart(List<Pt> pts) { _pts = pts; MouseFilter = MouseFilterEnum.Stop; }

    static readonly (float lo, float hi, string word)[] Bands = { (0, 30, "В ярости"), (30, 45, "Ворчат"), (45, 65, "Спокойны"), (65, 80, "Довольны"), (80, 100, "Эйфория") };
    const float L = 86, R = 12, T = 8, B = 22;

    float X(int i) => L + (Size.X - L - R) * (_pts.Count == 1 ? 0.5f : i / (float)(_pts.Count - 1));
    float Y(float m) => T + (Size.Y - T - B) * (1 - m / 100f);

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseMotion mm || _pts.Count == 0) return;
        int k = (int)Math.Round((mm.Position.X - L) / Math.Max(1, Size.X - L - R) * (_pts.Count - 1));
        k = Math.Clamp(k, 0, _pts.Count - 1);
        if (k != _hover) { _hover = k; TooltipText = _pts[k].Tip; QueueRedraw(); }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _hover >= 0) { _hover = -1; QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (_pts == null || _pts.Count == 0) return;
        foreach (var (lo, hi, word) in Bands)
        {
            var col = StandsPage.MoodCol((lo + hi) / 2);
            float y0 = Y(hi), y1 = Y(lo);
            DrawRect(new Rect2(L, y0, Size.X - L - R, y1 - y0), new Color(col, 0.045f));
            DrawLine(new Vector2(L, y1), new Vector2(Size.X - R, y1), new Color(1, 1, 1, 0.06f), 1);
            DrawString(Ui.Regular, new Vector2(0, (y0 + y1) / 2 + 4), word, HorizontalAlignment.Left, L - 10, 11, new Color(col, 0.75f));
        }
        // линия: мягкая заливка под ней и сама линия
        var line = new Vector2[_pts.Count];
        for (int i = 0; i < _pts.Count; i++) line[i] = new Vector2(X(i), Y(_pts[i].Mood));
        if (line.Length >= 2)
        {
            var poly = new List<Vector2>(line) { new(line[^1].X, Y(0)), new(line[0].X, Y(0)) };
            DrawColoredPolygon(poly.ToArray(), new Color(1, 1, 1, 0.035f));
            DrawPolyline(line, new Color(1, 1, 1, 0.85f), 2f, true);
        }
        float r = _pts.Count > 60 ? 2.5f : 3.5f;
        for (int i = 0; i < _pts.Count; i++)
        {
            var c = _pts[i].Res == 'W' ? Ui.Win : _pts[i].Res == 'O' ? Ui.Otl : Ui.Loss;
            if (i == _hover)
            {
                DrawLine(new Vector2(line[i].X, T), new Vector2(line[i].X, Size.Y - B), new Color(1, 1, 1, 0.18f), 1);
                DrawCircle(line[i], r + 3, new Color(c, 0.3f));
            }
            DrawCircle(line[i], r, c);
        }
        DrawString(Ui.Cond, new Vector2(L, Size.Y - 4), "первый матч", HorizontalAlignment.Left, 120, 11, Ui.Dim);
        DrawString(Ui.Cond, new Vector2(Size.X - R - 160, Size.Y - 4), $"матчей: {_pts.Count}", HorizontalAlignment.Right, 160, 11, Ui.Dim);
    }
}

/// <summary>
/// «Гонка за призами» во весь экран: шесть призов, у каждого лидер крупно и четверо преследователей; ниже — призы месяцев сезона
/// и победители прошлых сезонов карьеры.
/// </summary>
public partial class RacesPage : Control
{
    Career C => P.C;
    public RacesPage() { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        var v = Ui.V(16);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        var races = News.Races(C, 5);
        if (races.All(r => r.top.Count == 0) || C.Table.Values.Max(r => r.Gp) < 5)
            v.AddChild(Ui.L("Гонка за призами начнётся, когда клубы сыграют по пять матчей.", 14, Ui.Muted));
        else
        {
            v.AddChild(Ui.L("Кто впереди сейчас: по статистике сезона в игре. У Харта учитываются и успехи клуба, у Норриса — плюс-минус, у Везины — сейвы сверх ожидаемых.", 12, Ui.Dim)
                .Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
            var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 14);
            grid.AddThemeConstantOverride("v_separation", 14);
            foreach (var (award, what, top) in races) grid.AddChild(Award(award, what, top));
            v.AddChild(grid);
        }

        // призы месяцев этого сезона
        var months = C.Honors.Where(h => h.Season == C.Season && h.Kind is "pom" or "gom" or "rom" && C.Data.PlayerById.ContainsKey(h.Player)).OrderByDescending(h => h.Date).ToList();
        if (months.Count > 0)
        {
            v.AddChild(Ui.L("Призы месяца", 20, Ui.Text, Ds.Display));
            var mg = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            mg.AddThemeConstantOverride("h_separation", 14);
            mg.AddThemeConstantOverride("v_separation", 14);
            foreach (var mon in months.GroupBy(h => h.Date))
            {
                var col = Ui.V(10);
                foreach (var h in mon.OrderBy(h => h.Kind == "pom" ? 0 : h.Kind == "gom" ? 1 : 2))
                    col.AddChild(HonorRow(h, h.Kind == "pom" ? "Игрок" : h.Kind == "gom" ? "Вратарь" : "Новичок"));
                string title = mon.First().Name.Split(':').Last().Trim();
                var w = Widget.Make(char.ToUpper(title[0]) + title[1..], col, null, null, 10);
                w.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                mg.AddChild(w);
            }
            v.AddChild(mg);
        }

        // прошлые сезоны карьеры
        string[] kinds = { "hart", "ross", "richard", "norris", "vezina", "calder" };
        var past = C.Honors.Where(h => kinds.Contains(h.Kind) && C.Data.PlayerById.ContainsKey(h.Player) && (h.Season < C.Season || C.Phase != Phase.Regular)).ToList();
        if (past.Count > 0)
        {
            v.AddChild(Ui.L("Призы сезона", 20, Ui.Text, Ds.Display));
            foreach (var s in past.GroupBy(h => h.Season).OrderByDescending(s => s.Key))
            {
                var g = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                g.AddThemeConstantOverride("h_separation", 18);
                g.AddThemeConstantOverride("v_separation", 10);
                foreach (var h in s.OrderBy(h => Array.IndexOf(kinds, h.Kind))) g.AddChild(Ui.Expand(HonorRow(h, h.Name)));
                v.AddChild(Widget.Make($"Сезон {s.Key}-{(s.Key + 1) % 100:00}", g, null, null, 12));
            }
        }
    }

    Control HonorRow(Honor h, string label)
    {
        var p = C.Data.PlayerById[h.Player];
        return Ui.H(10, new FaceDot(C, p, 40, false, true),
            Ui.Expand(Ui.V(-1, Ui.L(label, 11, Ui.Gold, Ui.Semi), Name(p, 14), Ui.L($"{C.Team(p.Team).City} · {Line(p, h.Text)}", 11, Ui.Muted))));
    }

    /// <summary>У Харта и Колдера строка кончается городом — на странице город уже есть под именем.</summary>
    string Line(PlayerData p, string line)
    {
        string city = ", " + C.Team(p.Team).City;
        return line.EndsWith(city) ? line[..^city.Length] : line;
    }

    Control Name(PlayerData p, int size)
    {
        bool ours = p.Team == C.UserTeam;
        var name = Links.Player(C, p.Id, p.NameRu, size, ours ? Colors.White : Ui.Text, ours ? Ui.Bold : Ui.Semi);
        if (!ours) return name;
        var tag = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        tag.AddThemeStyleboxOverride("panel", Ds.Pill(new Color(Ds.ClubBright(C.Team(p.Team)), 0.35f), 7, 1));
        tag.AddChild(Ui.L("наш", 10, Colors.White, Ui.Semi));
        return Ui.H(6, name, tag);
    }

    Control Award(string award, string what, List<(int id, string line)> top)
    {
        var v = Ui.V(10);
        if (top.Count == 0)
        {
            v.AddChild(Ui.L("Претендентов пока нет.", 13, Ui.Muted));
        }
        else
        {
            var p = C.Data.PlayerById[top[0].id];
            var t = C.Team(p.Team);
            var lead = Ui.H(14, new Paint(new Vector2(84, 84), q =>
            {
                Face.Glow(q, q.Size / 2, 58, Ds.ClubBright(t), 0.05f);
                var c = q.Size / 2;
                Face.Player(q, p, t, new Rect2(c.X - 36, c.Y - 36, 72, 72), Face.Circle(c, 36));
                Face.Ring(q, c, 40, Ds.Ovr(C, p), 2.5f, false);
            }), Ui.Expand(Ui.V(2,
                Name(p, 17),
                Ui.H(6, new TeamBadge(t, 16).Also(x => x.SizeFlagsVertical = SizeFlags.ShrinkCenter), Ui.L(t.City, 12, Ui.Muted)),
                Ui.L(Line(p, top[0].line), 19, Ui.Gold, Ui.CondBold).Also(l => { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }))));
            foreach (var c in lead.GetChildren().OfType<Control>()) c.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            v.AddChild(lead);
            v.AddChild(new ColorRect { Color = new Color(1, 1, 1, 0.06f), CustomMinimumSize = new Vector2(0, 1) });
            for (int i = 1; i < top.Count; i++)
            {
                var q = C.Data.PlayerById[top[i].id];
                v.AddChild(Ui.H(10, Ui.L($"{i + 1}", 15, Ui.Dim, Ui.CondBold).Also(l => { l.CustomMinimumSize = new Vector2(16, 0); l.SizeFlagsVertical = SizeFlags.ShrinkCenter; }),
                    new FaceDot(C, q, 30, false, false),
                    Ui.Expand(Ui.V(-2, Name(q, 13), Ui.L(C.Team(q.Team).City, 11, Ui.Dim))),
                    Ui.L(Line(q, top[i].line), 13, Ui.Muted, Ui.Cond).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter)));
            }
        }
        var head = Ui.H(8, Ui.Icon("trophy", 16, Ui.Gold), Ui.V(-2, Ui.L(award, 16, Ui.Text, Ui.Semi), Ui.L(what, 11, Ui.Dim)));
        var box = Ui.V(12, head, v);
        return StandsPage.Glass(box);
    }
}
