using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;
using HM27.Match3D;

namespace HM27.UI;

/// <summary>Обзор клуба в духе WTCS: эмблема и факты, плитки матчей, график места, 3Д-арена, лидеры.</summary>
public partial class ClubPage : Control
{
    Career C => P.C;
    readonly string _abbr;
    public ClubPage() { }
    public ClubPage(string abbr) { _abbr = abbr; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        var t = C.Team(_abbr);
        var v = Ui.V(12);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);

        // ---------- верх: эмблема, факты, арена
        var top = Ui.H(12);
        var crest = Frame(new TeamBadge(t, 150) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, SizeFlagsVertical = SizeFlags.ShrinkCenter }, 18);
        crest.CustomMinimumSize = new Vector2(190, 190);
        top.AddChild(crest);
        top.AddChild(Ui.Expand(Frame(Facts(t), 16)));
        top.AddChild(Frame(Arena(t), 0));
        v.AddChild(top);

        // ---------- плитки матчей
        v.AddChild(Fixtures(t));

        // ---------- история клуба
        v.AddChild(Widget.Make("История клуба", BioWidgets.ClubHistory(C, t)));

        // ---------- график места и лидеры
        var row = Ui.H(12);
        row.AddChild(Ui.Expand(Widget.Make($"Место в конференции по ходу сезона", PositionChart(t))));
        var right = Ui.V(12); right.CustomMinimumSize = new Vector2(420, 0);
        right.AddChild(Leaders(t));
        row.AddChild(right);
        v.AddChild(row);
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

    Control Facts(TeamData t)
    {
        var row = C.Table[t.Abbr];
        var conf = C.Sorted(x => x.Conference == t.Conference);
        int cplace = conf.FindIndex(r => r.Team == t.Abbr) + 1;
        int lplace = C.Sorted().FindIndex(r => r.Team == t.Abbr) + 1;
        var view = Pundits.ViewAll(C)[t.Abbr];
        int power = view.All, atk = view.Att, dfn = view.Dfn;
        var status = (view.Label, view.Note);
        var g = new GridContainer { Columns = 4 };
        g.AddThemeConstantOverride("h_separation", 34); g.AddThemeConstantOverride("v_separation", 10);
        void F(string k, string v, Color? c = null) => g.AddChild(Ui.V(0, Ui.L(k.ToUpperInvariant(), 10, Ui.Dim, Ui.Bold), Ui.L(v, 14, c ?? Ui.Text, Ui.Semi)));
        F("Конференция", $"{t.Conference} · {cplace}-е");
        F("Дивизион", t.Division);
        F("Место в лиге", row.Gp > 0 ? $"{lplace}-е" : "—");
        F("Очки", $"{row.Pts} в {row.Gp} {Ui.Plural(row.Gp, "матче", "матчах", "матчах")}");
        F("Статус", status.Label, status.Label switch { "Элита" => Ui.Gold, "Претендент" => Ui.Win, "На подъёме" or "Борьба за плей-офф" => Ui.Otl, _ => Ui.Muted });
        F("Прогноз прессы", $"{Pundits.DivisionPick(C, t.Abbr)}-е в дивизионе");
        F("Шайбы", $"{row.Gf}–{row.Ga} ({Ui.Signed(row.Gf - row.Ga, "0")})");
        F("Серия", string.IsNullOrEmpty(row.Streak) ? "—" : row.Streak);
        var l = C.Lineups[t.Abbr];
        void FL(string k, Control val) => g.AddChild(Ui.V(0, Ui.L(k.ToUpperInvariant(), 10, Ui.Dim, Ui.Bold), val));
        FL("Основной вратарь", Links.Player(C, l.G1, C.Short(l.G1), 14, null, Ui.Semi));
        var best = C.Skaters.Where(kv => C.Data.PlayerById[kv.Key].Team == t.Abbr).OrderByDescending(kv => kv.Value.Pts).FirstOrDefault();
        FL("Лучший бомбардир", best.Key != 0 ? Links.Player(C, best.Key, $"{C.Short(best.Key)} ({best.Value.Pts})", 14, null, Ui.Semi) : Ui.L("—", 14, Ui.Text, Ui.Semi));
        F("В лазарете", $"{C.Injured.Count(id => C.Data.PlayerById[id].Team == t.Abbr)}");
        F("Темп игры", l.Tempo switch { -1 => "Закрытый", 1 => "Открытый", _ => "Обычный" });
        var v2 = Ui.V(10, Ui.L(t.Name, 26, Ui.Text, Ui.Bold), g);
        if (t.Abbr != C.UserTeam)
        {
            var next = C.TeamGames(C.UserTeam).Where(x => !x.Played && (x.Home == t.Abbr || x.Away == t.Abbr)).OrderBy(x => x.Date).FirstOrDefault();
            if (next != null)
            {
                v2.AddChild(Ui.L($"Ваш следующий матч с ними: {Ui.Date(next.Date)} ({(next.Home == C.UserTeam ? "дома" : "в гостях")}). {Pundits.Forecast(C.Data, next, C.Preview(next))}", 12, Ui.AccentBright));
            }
        }
        return v2;
    }

    /// <summary>Живая 3Д-арена клуба: трибуны в цветах хозяев, камера медленно облетает площадку.</summary>
    Control Arena(TeamData t)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(380, 190), ClipContents = true };
        var svc = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        svc.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.AddChild(svc);
        var vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X, HandleInputLocally = false, Size = new Vector2I(380, 190) };
        svc.AddChild(vp);
        var world = new Node3D();
        vp.AddChild(world);
        ArenaBuilder.Build(world, new Color(t.Colors[0]), new Color(t.Colors.Count > 1 ? t.Colors[1] : t.Colors[0]), new Color("888888"));
        var cam = new OrbitCam { Fov = 46, Current = true };
        world.AddChild(cam);
        var cap = Ui.V(0, Ui.L("ДОМАШНЯЯ АРЕНА", 10, new Color(1, 1, 1, 0.75f), Ui.Bold), Ui.L(t.Hist != null ? $"{t.Hist.Arena} · {t.City}" : $"{t.City} · 3Д-движок HM27", 13, Colors.White, Ui.Semi));
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(LayoutPreset.BottomWide); shade.OffsetTop = -44;
        holder.AddChild(shade);
        cap.Position = new Vector2(12, 150); cap.MouseFilter = MouseFilterEnum.Ignore;
        holder.AddChild(cap);
        return holder;
    }

    Control Fixtures(TeamData t)
    {
        var games = C.TeamGames(t.Abbr).OrderBy(g => g.Date).ToList();
        var past = games.Where(g => g.Played).TakeLast(5);
        var next = games.Where(g => !g.Played).Take(5);
        var h = Ui.H(8);
        foreach (var g in past.Concat(next)) h.AddChild(Tile(g, t.Abbr));
        if (h.GetChildCount() == 0) h.AddChild(Ui.L("Нет матчей", 13, Ui.Muted));
        return Widget.Make("Матчи: 5 последних и 5 ближайших", h, "Расписание →", () => Shell.I.Go(t.Abbr == C.UserTeam ? "schedule" : "club", t.Abbr == C.UserTeam ? null : "schedule", t.Abbr == C.UserTeam ? null : t.Abbr));
    }

    Control Tile(ScheduledGame g, string me)
    {
        bool home = g.Home == me;
        var opp = C.Team(home ? g.Away : g.Home);
        var v = Ui.V(3);
        v.Alignment = BoxContainer.AlignmentMode.Center;
        v.AddChild(new TeamBadge(opp, 40) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
        v.AddChild(Ui.L($"{opp.City} ({(home ? "Д" : "Г")})", 12, Ui.Text, Ui.Semi, HorizontalAlignment.Center).Also(l => { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }));
        Color edge;
        if (g.Played)
        {
            var r = g.Result; int my = home ? r.HomeGoals : r.AwayGoals, op = home ? r.AwayGoals : r.HomeGoals;
            edge = my > op ? Ui.Win : r.End == "REG" || g.Playoff ? Ui.Loss : Ui.Otl;
            v.AddChild(Ui.H(4, Ui.Spacer(), new ColorRect { Color = edge, CustomMinimumSize = new Vector2(8, 8) }, Ui.L($"{my}–{op}{(r.End == "OT" ? " ОТ" : r.End == "SO" ? " Б" : "")}", 14, Ui.Text, Ui.Bold), Ui.Spacer()));
        }
        else
        {
            edge = Ui.Line;
            int days = g.Date.DayNumber - C.Today.DayNumber;
            v.AddChild(Ui.L(days == 0 ? "сегодня" : days == 1 ? "завтра" : $"через {days} {Ui.Plural(days, "день", "дня", "дней")}", 12, Ui.AccentBright, null, HorizontalAlignment.Center));
        }
        v.AddChild(Ui.L(Ui.DateShort(g.Date), 11, Ui.Dim, null, HorizontalAlignment.Center));
        var p = new PanelContainer { CustomMinimumSize = new Vector2(112, 0), MouseDefaultCursorShape = CursorShape.PointingHand, MouseFilter = MouseFilterEnum.Stop };
        p.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var sb = Ui.Box(Ui.Panel2, 4, null, 6); sb.BorderColor = edge; sb.BorderWidthTop = 3;
        p.AddThemeStyleboxOverride("panel", sb);
        v.MouseFilter = MouseFilterEnum.Ignore;
        p.AddChild(v);
        p.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { if (g.Played) Main.I.ShowMatch(g); else Shell.I.Go("club", null, opp.Abbr); } };
        return p;
    }

    Control PositionChart(TeamData t)
    {
        // место в конференции после каждого игрового дня
        var conf = C.Data.Teams.Where(x => x.Conference == t.Conference).Select(x => x.Abbr).ToHashSet();
        var pts = conf.ToDictionary(x => x, _ => 0); var gp = conf.ToDictionary(x => x, _ => 0);
        var series = new List<(DateOnly d, int place)>();
        foreach (var day in C.Games.Where(g => g.Played && !g.Playoff).GroupBy(g => g.Date).OrderBy(x => x.Key))
        {
            foreach (var g in day)
            {
                bool hw = g.Result.HomeGoals > g.Result.AwayGoals;
                foreach (var (team, won) in new[] { (g.Home, hw), (g.Away, !hw) })
                {
                    if (!conf.Contains(team)) continue;
                    gp[team]++;
                    pts[team] += won ? 2 : g.Result.End != "REG" ? 1 : 0;
                }
            }
            if (!day.Any(g => g.Home == t.Abbr || g.Away == t.Abbr)) continue;
            int place = conf.OrderByDescending(x => pts[x]).ThenBy(x => gp[x]).ToList().IndexOf(t.Abbr) + 1;
            series.Add((day.Key, place));
        }
        return new PlaceChart(series, conf.Count);
    }

    Control Leaders(TeamData t)
    {
        var rows = C.Skaters.Where(kv => C.Data.PlayerById.TryGetValue(kv.Key, out var p) && p.Team == t.Abbr)
            .OrderByDescending(kv => kv.Value.Pts).ThenByDescending(kv => kv.Value.G).Take(7).Select(kv => (p: C.Data.PlayerById[kv.Key], s: kv.Value)).ToList();
        var grid = new Grid<(PlayerData p, SkaterLine s)>(new List<Col<(PlayerData p, SkaterLine s)>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.p.NameRu, 13) },
            new() { Title = "И", Width = 28, Cell = x => Ui.L($"{x.s.Gp}", 12, Ui.Muted) },
            new() { Title = "Г", Width = 28, Cell = x => Ui.L($"{x.s.G}", 12) },
            new() { Title = "П", Width = 28, Cell = x => Ui.L($"{x.s.A}", 12) },
            new() { Title = "О", Width = 30, Cell = x => Ui.L($"{x.s.Pts}", 13, Ui.Text, Ui.Bold) },
            new() { Title = "Ср. оц.", Width = 52, Cell = x => Ui.L(x.s.Rated > 0 ? $"{x.s.AvgRating:0.00}" : "—", 12, Ui.Muted) },
        }, rows, x => Shell.I.Go("player", null, x.p.Id.ToString()));
        return Widget.Make("Лидеры клуба", grid.Root, "Состав →", () => Shell.I.Go("club", "players", t.Abbr), 0);
    }
}

/// <summary>Камера, медленно облетающая арену.</summary>
public partial class OrbitCam : Camera3D
{
    float _t = 0.6f;
    public override void _Process(double delta)
    {
        _t += (float)delta * 0.05f;
        Position = new Vector3(MathF.Cos(_t) * 38f, 14f, MathF.Sin(_t) * 26f);
        LookAt(new Vector3(0, 0, 0), Vector3.Up);
    }
}

/// <summary>График места в конференции (1 — вверху), с зоной плей-офф.</summary>
public partial class PlaceChart : Control
{
    readonly List<(DateOnly d, int place)> _s;
    readonly int _n;
    public PlaceChart() { }
    public PlaceChart(List<(DateOnly d, int place)> s, int n) { _s = s; _n = n; CustomMinimumSize = new Vector2(0, 220); }

    public override void _Draw()
    {
        var sz = Size;
        float left = 28, right = sz.X - 8, top = 8, bottom = sz.Y - 20;
        float Y(int place) => top + (place - 1) / (float)(_n - 1) * (bottom - top);
        DrawRect(new Rect2(left, top, right - left, Y(8) - top + (bottom - top) / (_n - 1) / 2), new Color(Ui.Win, 0.08f));
        for (int p = 1; p <= _n; p += p == 1 ? 3 : 4)
        {
            DrawLine(new Vector2(left, Y(p)), new Vector2(right, Y(p)), new Color(1, 1, 1, 0.06f), 1);
            DrawString(Ui.Regular, new Vector2(0, Y(p) + 4), p.ToString(), HorizontalAlignment.Left, -1, 10, Ui.Dim);
        }
        DrawString(Ui.Regular, new Vector2(right - 120, Y(8) - 4), "зона плей-офф", HorizontalAlignment.Left, -1, 10, new Color(Ui.Win, 0.7f));
        if (_s.Count < 2) { DrawString(Ui.Regular, new Vector2(left + 10, (top + bottom) / 2), "Сезон только начался", HorizontalAlignment.Left, -1, 12, Ui.Muted); return; }
        var pts = new Vector2[_s.Count];
        for (int i = 0; i < _s.Count; i++) pts[i] = new Vector2(left + i / (float)(_s.Count - 1) * (right - left), Y(_s[i].place));
        DrawPolyline(pts, Ui.Gold, 2.5f, true);
        DrawCircle(pts[^1], 4, Ui.Gold);
        // месяцы по оси
        int lastM = 0;
        for (int i = 0; i < _s.Count; i++)
            if (_s[i].d.Month != lastM)
            {
                lastM = _s[i].d.Month;
                DrawString(Ui.Regular, new Vector2(pts[i].X, sz.Y - 4), Ui.DateShort(_s[i].d).Split(' ')[1], HorizontalAlignment.Left, -1, 10, Ui.Dim);
            }
    }
}
