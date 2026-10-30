using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Общие кусочки страниц FM-оболочки.</summary>
public static class P
{
    public static Career C => Main.I.Career;

    public static MarginContainer Page(Control content, bool scroll = true)
    {
        var m = Ui.Margin(scroll ? Ui.Scroll(content) : content, 16, 14, 16, 14);
        m.SizeFlagsHorizontal = m.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return m;
    }

    public static Control Team(string abbr, int size = 18, bool full = false)
    {
        var t = C.Team(abbr);
        return Ui.H(6, new TeamBadge(t, size), Links.Club(t, full ? t.Name : t.City, 13, abbr == C.UserTeam ? Colors.White : Ui.Text, abbr == C.UserTeam ? Ui.Semi : null));
    }

    /// <summary>Форма: квадратики В/П/О последних матчей (новые справа).</summary>
    public static Control Form(string abbr, int n = 5, int size = 20)
    {
        var h = Ui.H(3);
        foreach (var g in C.TeamGames(abbr).Where(g => g.Played).OrderByDescending(g => g.Date).Take(n).Reverse())
        {
            bool home = g.Home == abbr; var r = g.Result;
            bool win = (home ? r.HomeGoals : r.AwayGoals) > (home ? r.AwayGoals : r.HomeGoals);
            var (txt, col) = win ? ("В", Ui.Win) : r.End == "REG" || g.Playoff ? ("П", Ui.Loss) : ("О", Ui.Otl);
            var p = new PanelContainer { CustomMinimumSize = new Vector2(size, size), TooltipText = $"{Ui.DateShort(g.Date)}: {(home ? "" : "@")}{C.Team(home ? g.Away : g.Home).City} {(home ? r.HomeGoals : r.AwayGoals)}:{(home ? r.AwayGoals : r.HomeGoals)}" };
            p.AddThemeStyleboxOverride("panel", Ui.Box(col, 3, null, 0).Also(s => s.ContentMarginTop = s.ContentMarginBottom = 0));
            p.AddChild(Ui.L(txt, 11, Colors.White, Ui.Bold, HorizontalAlignment.Center));
            h.AddChild(p);
        }
        if (h.GetChildCount() == 0) h.AddChild(Ui.L("—", 13, Ui.Dim));
        return h;
    }

    /// <summary>Где игрок в составе своего клуба.</summary>
    public static string Role(int id, string abbr)
    {
        if (C.Injured.Contains(id)) return "Травма";
        var l = C.Lineups[abbr];
        for (int i = 0; i < l.F.Count; i++) if (l.F[i].Contains(id)) return $"{i + 1}-е звено";
        for (int i = 0; i < l.D.Count; i++) if (l.D[i].Contains(id)) return $"{i + 1}-я пара";
        if (id == l.G1) return "Основной";
        if (id == l.G2) return "Запасной";
        return "Запас";
    }

    public static string PosLong(string pos) => PlayerCard.PosLong(pos);

    public static void OpenLink(string link)
    {
        if (string.IsNullOrEmpty(link)) return;
        var (k, v) = link.Contains(':') ? (link[..link.IndexOf(':')], link[(link.IndexOf(':') + 1)..]) : (link, null);
        switch (k)
        {
            case "game": var g = C.Games.FirstOrDefault(x => x.Id.ToString() == v); if (g != null) Main.I.ShowMatch(g); break;
            case "player": Shell.I?.Go("player", null, v); break;
            case "club": Shell.I?.Go("club", null, v); break;
            case "table": Shell.I?.Go("comp", "table"); break;
            case "playoffs": Shell.I?.Go("comp", "playoffs"); break;
        }
    }
}

// =====================================================================================================
/// <summary>Портал: главная новость, следующий матч, лента лиги, письма, форма, таблица дивизиона и лидеры клуба.</summary>
public partial class HomePage : Control
{
    Career C => P.C;
    public HomePage() { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        var v = Ui.V(12);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);

        if (C.Phase == Phase.Finished && C.Champion != null)
            v.AddChild(Widget.Make("Кубок Стэнли 2027", Ui.H(14, new TeamBadge(C.Team(C.Champion), 56), Ui.V(2, Links.Club(C.Team(C.Champion), C.Team(C.Champion).Name, 24, Ui.Text, Ui.CondBold),
                Ui.L(C.Champion == C.UserTeam ? "Это ваш клуб!" : "Многосезонная карьера — в следующих версиях.", 13, Ui.Muted)))));
        var top = Ui.H(12);
        var lead = Lead();
        if (lead != null) { lead.SizeFlagsStretchRatio = 1.55f; top.AddChild(Ui.Expand(lead)); }
        top.AddChild(Ui.Expand(NextMatch()));
        v.AddChild(top);

        var cols = Ui.H(12);
        var a = Ui.Expand(Ui.V(12)); a.SizeFlagsStretchRatio = 1.2f;
        var b = Ui.Expand(Ui.V(12));
        var c = Ui.Expand(Ui.V(12));
        cols.AddChild(a); cols.AddChild(b); cols.AddChild(c);
        v.AddChild(cols);
        a.AddChild(Feed());
        a.AddChild(Stands());
        b.AddChild(Mail());
        b.AddChild(Results());
        b.AddChild(Races());
        c.AddChild(Division());
        c.AddChild(PowerWidget());
        c.AddChild(Leaders());
    }

    /// <summary>Главная новость: самый весомый свежий материал (за 3 дня).</summary>
    Control Lead()
    {
        var m = C.Messages.Where(x => x.Lead != null && C.Today.DayNumber - x.Date.DayNumber <= 3)
            .OrderByDescending(x => x.Weight * 10 + (x.Read ? 0 : 3) - (C.Today.DayNumber - x.Date.DayNumber) * 2).ThenByDescending(x => x.Id).FirstOrDefault();
        if (m == null) return null;
        var card = new PanelContainer { MouseDefaultCursorShape = CursorShape.PointingHand, MouseFilter = MouseFilterEnum.Stop };
        var sb = Ui.Box(Ui.Panel, 4, null, 22);
        sb.ContentMarginTop = 18; sb.ContentMarginBottom = 18;
        Color stripe = Ui.Blue;
        if (m.Team != null && C.Data.TeamByAbbr.TryGetValue(m.Team, out var t)) stripe = new Color(t.Colors[0]).Luminance < 0.08f && t.Colors.Count > 1 ? new Color(t.Colors[1]) : new Color(t.Colors[0]);
        sb.BorderColor = stripe; sb.BorderWidthLeft = 5;
        card.AddThemeStyleboxOverride("panel", sb);
        var v = Ui.V(8);
        var head = Ui.H(10, Article.Tag(m.Tag, 15), Ui.Spacer(), Article.Byline(C, m, 12));
        v.AddChild(head);
        v.AddChild(Ui.L(m.Subject, 34, Ui.Text, Ui.CondBold).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = SizeFlags.ExpandFill; }));
        v.AddChild(Ui.L(m.Lead, 15, new Color(Ui.Text, 0.85f)).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = SizeFlags.ExpandFill; }));
        if (m.Facts is { Count: > 0 })
        {
            var f = Ui.H(26);
            foreach (var x in m.Facts.Take(4))
            {
                var col = x.Tone == "good" ? Ui.Win : x.Tone == "bad" ? Ui.Loss : Ui.Text;
                f.AddChild(Ui.V(-3, Ui.L(x.Value, 24, col, Ui.CondBold), Ui.L(x.Label, 11, Ui.Muted)));
            }
            v.AddChild(Ui.Spacer(false, 2));
            v.AddChild(f);
        }
        else if (m.Players is { Count: > 0 })
        {
            var ps = Ui.H(8);
            foreach (var pid in m.Players.Take(2))
                if (C.Data.PlayerById.TryGetValue(pid, out var pl)) ps.AddChild(Article.PlayerChip(C, pl));
            v.AddChild(Ui.Spacer(false, 2));
            v.AddChild(ps);
        }
        v.AddChild(Ui.Spacer(false, 2));
        v.AddChild(Ui.L("Читать полностью", 13, Ui.Blue, Ui.Semi));
        v.MouseFilter = MouseFilterEnum.Ignore;
        SetIgnore(v);
        card.AddChild(v);
        int id = m.Id;
        card.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Shell.I.Go("inbox", null, id.ToString()); };
        return card;
    }

    static void SetIgnore(Control c)
    {
        foreach (var ch in c.GetChildren()) if (ch is Control cc) { cc.MouseFilter = MouseFilterEnum.Ignore; SetIgnore(cc); }
    }

    Control MsgRow(Message m, bool showFrom)
    {
        var row = new PanelContainer { MouseDefaultCursorShape = CursorShape.PointingHand };
        var sb = Ui.Box(new Color(0, 0, 0, 0), 0, null, 14); sb.ContentMarginTop = sb.ContentMarginBottom = 8; sb.BorderColor = Ui.Line; sb.BorderWidthBottom = 1;
        row.AddThemeStyleboxOverride("panel", sb);
        var title = Ui.L(m.Subject, 14, m.Read ? Ui.Muted : Ui.Text, m.Read ? Ui.Medium : Ui.Semi);
        title.ClipText = true; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var j = Voices.ByLine(C, m.From);
        var meta = Ui.H(8, Article.Tag(m.Tag ?? "Письмо", 12), Ui.Expand(Ui.L(showFrom || j != null ? (j != null && j.Kind != Voices.WIRE ? j.Name : showFrom ? m.From : "") : "", 11, Ui.Dim)), Ui.L(Ui.DateShort(m.Date), 11, Ui.Dim));
        Control body = Ui.V(1, meta, title);
        var v = j != null && j.Kind != Voices.WIRE ? (Control)Ui.H(10, Article.Avatar(j, 30).Also(x => x.SizeFlagsVertical = SizeFlags.ShrinkCenter), Ui.Expand(body)) : body;
        v.MouseFilter = MouseFilterEnum.Ignore;
        SetIgnore(v);
        row.AddChild(v);
        int id = m.Id;
        row.MouseEntered += () => sb.BgColor = new Color(1, 1, 1, 0.04f);
        row.MouseExited += () => sb.BgColor = new Color(0, 0, 0, 0);
        row.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Shell.I.Go("inbox", null, id.ToString()); };
        return row;
    }

    Control Feed()
    {
        var v = Ui.V(0);
        foreach (var m in C.Messages.Where(x => x.IsNews || x.Tag is "Неделя в НХЛ" or "Цифры").OrderByDescending(x => x.Id).Take(8)) v.AddChild(MsgRow(m, false));
        if (v.GetChildCount() == 0) v.AddChild(Ui.Margin(Ui.L("Сезон только начинается — новости появятся после первых матчей", 13, Ui.Muted).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart), 14, 12, 14, 12));
        return Widget.Make("Лента НХЛ", v, "Все новости", () => Shell.I.Go("inbox"), 0);
    }

    Control Mail()
    {
        var v = Ui.V(0);
        foreach (var m in C.Messages.Where(x => !x.IsNews).OrderBy(x => x.Read).ThenByDescending(x => x.Id).Take(5)) v.AddChild(MsgRow(m, true));
        string t = C.Unread > 0 ? $"Письма · {C.Unread} {Ui.Plural(C.Unread, "новое", "новых", "новых")}" : "Письма";
        return Widget.Make(t, v, "Входящие", () => Shell.I.Go("inbox"), 0);
    }

    Control NextMatch()
    {
        var g = C.NextGame(C.UserTeam);
        if (g == null) return Widget.Make("Следующий матч", Ui.L("Матчей больше нет", 13, Ui.Muted));
        bool home = g.Home == C.UserTeam;
        var opp = C.Team(home ? g.Away : g.Home);
        var o = C.Preview(g);
        double p = home ? o.PHome : 1 - o.PHome;
        var oppRow = C.Table[opp.Abbr];
        var v = Ui.V(12);
        int dd = g.Date.DayNumber - C.Today.DayNumber;
        v.AddChild(Ui.L($"{(dd == 0 ? "Сегодня" : dd == 1 ? "Завтра" : Ui.Date(g.Date))}{(dd <= 1 ? ", " + Ui.Date(g.Date, false) : "")} · {(home ? "дома" : "в гостях")}{(g.Playoff ? " · плей-офф" : "")}", 13, Ui.Muted));
        var h = Ui.H(14);
        h.Alignment = BoxContainer.AlignmentMode.Center;
        var my = C.Team(C.UserTeam);
        h.AddChild(Ui.V(4, new TeamBadge(home ? my : opp, 72) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter }, Ui.L((home ? my : opp).City, 14, Ui.Text, Ui.Cond, HorizontalAlignment.Center)));
        h.AddChild(Ui.V(0, Ui.L("—", 30, Ui.Dim, Ui.CondBold, HorizontalAlignment.Center)).Also(x => x.Alignment = BoxContainer.AlignmentMode.Center));
        h.AddChild(Ui.V(4, new TeamBadge(home ? opp : my, 72) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter }, Ui.L((home ? opp : my).City, 14, Ui.Text, Ui.Cond, HorizontalAlignment.Center)));
        v.AddChild(h);
        // шансы — полоса
        v.AddChild(Ui.H(8, Ui.L($"{Math.Round(p * 100)}%", 18, Ui.Text, Ui.CondBold), Ui.Expand(new SplitBar(p, Ui.Blue, Ui.Line)), Ui.L($"{Math.Round((1 - p) * 100)}%", 18, Ui.Muted, Ui.CondBold)).Also(x => x.TooltipText = "Шансы на победу по оценке аналитиков (с овертаймом и буллитами)"));
        int conf = C.Sorted(t => t.Conference == opp.Conference).FindIndex(r => r.Team == opp.Abbr) + 1;
        v.AddChild(Ui.H(8, Ui.L($"Соперник: {oppRow.W}-{oppRow.L}-{oppRow.Otl}, {conf}-е в конференции", 12, Ui.Muted), Ui.Spacer(), P.Form(opp.Abbr, 5, 16)));
        v.AddChild(Ui.L(Pundits.Forecast(C.Data, g, o), 13, Ui.Text).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        // эксперты: кто за кого
        var picks = News.Picks(C, g);
        var ph = Ui.H(14);
        foreach (var pk in picks)
            ph.AddChild(Ui.H(6, Article.Avatar(pk.Who, 26), new TeamBadge(C.Team(pk.Team), 22)).Also(x => x.TooltipText = $"{pk.Who.Name} ({pk.Who.Outlet}): {pk.Why}"));
        v.AddChild(Ui.H(10, Ui.L("Эксперты:", 12, Ui.Muted), ph));
        bool b2b = C.PlayedYesterday(C.UserTeam, g.Date), ob2b = C.PlayedYesterday(opp.Abbr, g.Date);
        if (b2b || ob2b) v.AddChild(Ui.L(b2b && ob2b ? "Обе команды играли накануне" : b2b ? "У нас второй матч за два дня — подумайте о запасном вратаре" : "Соперник играл накануне — он устал", 12, Ui.Otl));
        return Widget.Make("Следующий матч", v, "Звенья", () => Shell.I.Go("lines"));
    }

    Control Upcoming()
    {
        var games = C.TeamGames(C.UserTeam).Where(g => !g.Played).OrderBy(g => g.Date).Skip(1).Take(5).ToList();
        var grid = new Grid<ScheduledGame>(new List<Col<ScheduledGame>>
        {
            new() { Title = "Дата", Width = 110, Align = HorizontalAlignment.Left, Cell = g => Ui.L(Ui.Date(g.Date), 12, Ui.Muted) },
            new() { Title = "", Width = 20, Cell = g => Ui.L(g.Home == C.UserTeam ? "" : "@", 12, Ui.Muted) },
            new() { Title = "Соперник", Width = 0, Align = HorizontalAlignment.Left, Cell = g => P.Team(g.Home == C.UserTeam ? g.Away : g.Home) },
        }, games, g => P.OpenLink($"club:{(g.Home == C.UserTeam ? g.Away : g.Home)}"));
        return Widget.Make("Ближайшие матчи", grid.Root, "Расписание →", () => Shell.I.Go("schedule"), 0);
    }

    Control Results()
    {
        var games = C.TeamGames(C.UserTeam).Where(g => g.Played).OrderByDescending(g => g.Date).Take(6).ToList();
        var grid = new Grid<ScheduledGame>(new List<Col<ScheduledGame>>
        {
            new() { Title = "Дата", Width = 56, Align = HorizontalAlignment.Left, Cell = g => Ui.L(Ui.DateShort(g.Date), 12, Ui.Muted) },
            new() { Title = "", Width = 16, Cell = g => Ui.L(g.Home == C.UserTeam ? "" : "@", 12, Ui.Muted) },
            new() { Title = "Соперник", Width = 0, Align = HorizontalAlignment.Left, Cell = g => P.Team(g.Home == C.UserTeam ? g.Away : g.Home) },
            new() { Title = "Счёт", Width = 64, Cell = g => { var r = g.Result; bool h = g.Home == C.UserTeam; int my = h ? r.HomeGoals : r.AwayGoals, op = h ? r.AwayGoals : r.HomeGoals;
                var col = my > op ? Ui.Win : r.End == "REG" || g.Playoff ? Ui.Loss : Ui.Otl;
                return Ui.L($"{my}:{op}{(r.End == "OT" ? " ОТ" : r.End == "SO" ? " Б" : "")}", 13, col, Ui.Bold); } },
        }, games, g => Main.I.ShowMatch(g));
        var v = Ui.V(8, Ui.Margin(Ui.H(8, Ui.L("Форма", 12, Ui.Muted), P.Form(C.UserTeam, 10, 18)), 12, 10, 12, 4), grid.Root);
        return Widget.Make("Последние матчи", v, null, null, 0);
    }

    Control Division()
    {
        var t = C.Team(C.UserTeam);
        var rows = C.Sorted(x => x.Division == t.Division).Select((r, i) => (r, i: i + 1)).ToList();
        var grid = new Grid<(StandingRow r, int i)>(new List<Col<(StandingRow r, int i)>>
        {
            new() { Title = "#", Width = 20, Cell = x => Ui.L($"{x.i}", 12, x.i <= 3 ? Ui.Text : Ui.Muted) },
            new() { Title = "Клуб", Width = 0, Align = HorizontalAlignment.Left, Cell = x => P.Team(x.r.Team) },
            new() { Title = "И", Width = 28, Cell = x => Ui.L($"{x.r.Gp}", 12, Ui.Muted) },
            new() { Title = "+/-", Width = 36, Cell = x => Ui.L(Ui.Signed(x.r.Gf - x.r.Ga, "0"), 12, Ui.Muted) },
            new() { Title = "О", Width = 30, Cell = x => Ui.L($"{x.r.Pts}", 13, Ui.Text, Ui.Bold) },
        }, rows, x => P.OpenLink($"club:{x.r.Team}"), x => x.r.Team == C.UserTeam);
        return Widget.Make($"{t.Division} дивизион", grid.Root, "Таблица →", () => Shell.I.Go("comp", "table"), 0);
    }

    /// <summary>Трибуна: настроение болельщиков, ожидания прессы и свежие посты.</summary>
    Control Stands()
    {
        var v = Ui.V(8);
        float m = C.FanMood;
        var col = m >= 65 ? Ui.Win : m >= 45 ? Ui.Text : m >= 30 ? Ui.Otl : Ui.Loss;
        v.AddChild(Ui.H(10, Ui.L(Voices.MoodWord(m), 22, col, Ui.CondBold), Ui.Expand(Ui.Bar(m / 100.0, col, 160, 8).Also(x => x.SizeFlagsVertical = SizeFlags.ShrinkCenter))));
        var t = C.Team(C.UserTeam);
        int exp = Pundits.DivisionPick(C, C.UserTeam);
        int now = C.Sorted(x => x.Division == t.Division).FindIndex(x => x.Team == C.UserTeam) + 1;
        if (exp > 0) v.AddChild(Ui.L($"Пресса до сезона ставила нас {exp}-ми в дивизионе, сейчас — {now}-е.", 12, now <= exp ? Ui.Muted : Ui.Otl).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        var posts = C.Fans.Where(f => f.Team == C.UserTeam).OrderByDescending(f => f.Date).Take(5).ToList();
        if (posts.Count == 0) v.AddChild(Ui.L("Трибуны ждут первого матча.", 12, Ui.Dim));
        foreach (var f in posts)
        {
            var mc = f.Mood > 0 ? Ui.Win : f.Mood < 0 ? Ui.Loss : Ui.Muted;
            v.AddChild(Ui.V(1,
                Ui.H(6, new ColorRect { Color = mc, CustomMinimumSize = new Vector2(3, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter }, Ui.L(f.Handle, 12, Ui.Blue, Ui.Semi), Ui.Spacer(), Ui.L($"♥ {f.Likes}", 11, Ui.Dim), Ui.L(Ui.DateShort(f.Date), 11, Ui.Dim)),
                Ui.L(f.Text, 13, Ui.Text).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart)));
        }
        return Widget.Make("Трибуна", Ui.Margin(v, 14, 10, 14, 12), null, null, 0);
    }

    /// <summary>Рейтинг силы: десятка и мы, со сдвигом за неделю.</summary>
    Control PowerWidget()
    {
        var pr = News.Power(C);
        var v = Ui.V(3);
        if (pr.Count == 0 || C.Table.Values.Min(r => r.Gp) < 3) { v.AddChild(Ui.L("Рейтинг появится через пару недель сезона.", 12, Ui.Muted)); return Widget.Make("Рейтинг силы", Ui.Margin(v, 14, 10, 14, 12), null, null, 0); }
        bool hasPrev = C.PowerWeek.Count > 0;
        foreach (var x in pr.Where(x => x.Rank <= 8 || x.Team == C.UserTeam))
        {
            bool me = x.Team == C.UserTeam;
            string mv = !hasPrev || x.Prev == x.Rank ? "" : x.Prev > x.Rank ? $"↑{x.Prev - x.Rank}" : $"↓{x.Rank - x.Prev}";
            var row = Ui.H(8, Ui.L($"{x.Rank}", 14, me ? Ui.Gold : Ui.Text, Ui.CondBold).Also(l => l.CustomMinimumSize = new Vector2(22, 0)), new TeamBadge(C.Team(x.Team), 18),
                Ui.L(C.Team(x.Team).City, 13, me ? Colors.White : Ui.Text, me ? Ui.Semi : null).Also(l => l.CustomMinimumSize = new Vector2(96, 0)),
                Ui.L(mv, 11, mv.StartsWith("↑") ? Ui.Win : Ui.Loss).Also(l => l.CustomMinimumSize = new Vector2(26, 0)),
                Ui.Expand(Ui.L(x.Note, 11, Ui.Dim).Also(l => { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; })));
            row.TooltipText = x.Note;
            v.AddChild(row);
        }
        var j = Voices.Get(C, Voices.STATS);
        v.AddChild(Ui.H(6, Article.Avatar(j, 18), Ui.L($"{j.Name}, {j.Outlet} · по вторникам", 11, Ui.Dim)));
        return Widget.Make("Рейтинг силы", Ui.Margin(v, 14, 10, 14, 12), null, null, 0);
    }

    /// <summary>Гонка за призами: лидер каждого приза и наш лучший.</summary>
    Control Races()
    {
        var v = Ui.V(6);
        var races = News.Races(C);
        if (races.All(r => r.top.Count == 0) || C.Table.Values.Max(r => r.Gp) < 5) { v.AddChild(Ui.L("Гонка за призами начнётся, когда сыграют первые матчи.", 12, Ui.Muted)); return Widget.Make("Гонка за призами", Ui.Margin(v, 14, 10, 14, 12), null, null, 0); }
        foreach (var (award, what, top) in races)
        {
            if (top.Count == 0) continue;
            var p = C.Data.PlayerById[top[0].id];
            bool ours = p.Team == C.UserTeam;
            v.AddChild(Ui.H(8, Ui.V(-2, Ui.L(award, 14, Ui.Gold, Ui.Cond), Ui.L(what, 10, Ui.Dim)).Also(x => x.CustomMinimumSize = new Vector2(118, 0)),
                new TeamBadge(C.Team(p.Team), 18).Also(x => x.SizeFlagsVertical = SizeFlags.ShrinkCenter),
                Ui.Expand(Ui.V(-2, Links.Player(C, p.Id, p.NameRu, 13, ours ? Colors.White : Ui.Text, Ui.Semi), Ui.L(top[0].line, 11, Ui.Muted)))));
        }
        return Widget.Make("Гонка за призами", Ui.Margin(v, 14, 10, 14, 12), null, null, 0);
    }

    Control Leaders()
    {
        var rows = C.Skaters.Where(kv => C.Data.PlayerById.TryGetValue(kv.Key, out var p) && p.Team == C.UserTeam)
            .OrderByDescending(kv => kv.Value.Pts).ThenByDescending(kv => kv.Value.G).Take(6).Select(kv => (p: C.Data.PlayerById[kv.Key], s: kv.Value)).ToList();
        if (rows.Count == 0)
            rows = C.Roster(C.UserTeam).Where(p => !p.IsGoalie).OrderByDescending(p => p.Ovr).Take(6).Select(p => (p, new SkaterLine())).ToList();
        var grid = new Grid<(PlayerData p, SkaterLine s)>(new List<Col<(PlayerData p, SkaterLine s)>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.L(x.p.NameRu, 13) },
            new() { Title = "Г", Width = 26, Cell = x => Ui.L($"{x.s.G}", 12, Ui.Muted) },
            new() { Title = "П", Width = 26, Cell = x => Ui.L($"{x.s.A}", 12, Ui.Muted) },
            new() { Title = "О", Width = 30, Cell = x => Ui.L($"{x.s.Pts}", 13, Ui.Text, Ui.Bold) },
        }, rows, x => Shell.I.Go("player", null, x.p.Id.ToString()));
        return Widget.Make("Бомбардиры клуба", grid.Root, "Состав →", () => Shell.I.Go("squad"), 0);
    }
}

// =====================================================================================================
/// <summary>Состав клуба: плотная таблица с сортировкой, фильтр по позициям — как «Игроки» в FM.</summary>
public partial class SquadPage : Control
{
    Career C => P.C;
    readonly string _abbr;
    int _filter;
    VBoxContainer _box;
    public SquadPage() { }
    public SquadPage(string abbr) { _abbr = abbr; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        _box = Ui.V(10);
        var page = P.Page(_box);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        Build();
    }

    void Build()
    {
        Ui.Clear(_box);
        var t = C.Team(_abbr);
        var all = C.Roster(_abbr).ToList();
        var head = Ui.H(12);
        head.AddChild(new Segmented(new[] { $"Все ({all.Count})", "Нападающие", "Защитники", "Вратари" }, _filter, k => { _filter = k; Build(); }));
        head.AddChild(Ui.Spacer());
        if (_abbr != C.UserTeam)
        {
            var o = C.NextGame(C.UserTeam);
            head.AddChild(Ui.L($"Статус: {Pundits.Status(C, t.Abbr).label}", 12, Ui.Muted));
        }
        else head.AddChild(Ui.L("Щёлкните игрока — профиль. Заголовок колонки — сортировка.", 12, Ui.Dim));
        _box.AddChild(head);

        Func<PlayerData, bool> f = _filter switch { 1 => p => p.IsForward, 2 => p => p.IsDefense, 3 => p => p.IsGoalie, _ => p => true };
        var rows = all.Where(f).ToList();
        bool gk = _filter == 3;
        var age = C.Today;
        var cols = new List<Col<PlayerData>>
        {
            new() { Title = "№", Width = 30, Cell = p => Ui.L(p.Num?.ToString() ?? "", 12, Ui.Muted), Sort = p => p.Num ?? 99, Desc = false },
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Sort = p => p.LastRu, Desc = false, Cell = p =>
            {
                bool inj = C.Injured.Contains(p.Id);
                var h = Ui.H(6, Links.Player(p, null, 13, inj ? Ui.Loss : Ui.Text, Ui.Medium));
                if (inj) h.AddChild(Ui.Icon("cross", 13, Ui.Loss));
                return h;
            } },
            new() { Title = "Поз", Width = 40, Cell = p => Ui.L(Ui.PosRu(p.Pos), 12, Ui.Muted), Sort = p => "C LWRWD G".IndexOf(p.Pos), Desc = false },
            new() { Title = "Возр", Width = 40, Cell = p => Ui.L(p.Age(age)?.ToString() ?? "", 12), Sort = p => p.Age(age) ?? 0, Desc = false },
            new() { Title = "В составе", Width = 92, Align = HorizontalAlignment.Left, Cell = p => { var r = P.Role(p.Id, _abbr); return Ui.L(r, 12, r == "Травма" ? Ui.Loss : r == "Запас" ? Ui.Dim : Ui.Muted); }, Sort = p => RoleOrder(p.Id), Desc = false },
            new() { Title = "Форма", Width = 96, Align = HorizontalAlignment.Left, Tip = "По оценкам за последние 8 матчей относительно его обычного уровня", Cell = p => Ui.FormCell(PlayerForm.Of(C, p.Id)), Sort = p => PlayerForm.Of(C, p.Id).Score },
            new() { Title = "Настрой", Width = 132, Align = HorizontalAlignment.Left, Tip = "Роль и минуты, результаты, оценки и ваши решения. Подробно — «Раздевалка»", Cell = p => RoomPage.MoodCell(C, p.Id), Sort = p => C.Players.TryGetValue(p.Id, out var s) ? s.Morale : 60 },
            new() { Title = "Свежесть", Width = 70, Align = HorizontalAlignment.Left, Tip = "Нагрузка за сезон: большие минуты и плотный календарь утомляют", Cell = p => Ui.FreshCell(PlayerModel.State(C, p.Id)), Sort = p => PlayerModel.State(C, p.Id)?.Fresh ?? 100 },
            new() { Title = "Способности", Width = 86, Cell = p => Ui.Stars(StarScale.StarsV(C, p, PlayerModel.Visible(C, p, "ca")), 13), Sort = p => PlayerModel.Visible(C, p, "ca"),
                    Tip = StarScale.Relative ? "Оценка штаба относительно вашего клуба: 5★ — уровень лучшего в своей группе" : "Оценка штаба по шкале лиги" },
            new() { Title = "Потенциал", Width = 86, Cell = p => Ui.Stars(StarScale.StarsV(C, p, PlayerModel.PotentialRange(C, p).hi), 13), Sort = p => PlayerModel.PotentialRange(C, p).hi,
                    Tip = "Потолок по оценке скаутов (верхняя граница)" + (StarScale.Relative ? ", относительно вашего клуба" : "") },
        };
        if (!gk)
            foreach (var (key, name, tip) in new[] { ("fin", "Зав", "Завершение"), ("pas", "Пер", "Передача"), ("vis", "Вид", "Видение"), ("dfn", "Обор", "Позиционная оборона"), ("spd", "Скор", "Скорость"), ("sta", "Вын", "Выносливость") })
                cols.Add(new() { Title = name, Width = 38, Tip = tip, Cell = p => p.IsGoalie ? Ui.L("—", 12, Ui.Dim) : Ui.AttrV(PlayerModel.Visible(C, p, key)), Sort = p => p.IsGoalie ? -1 : PlayerModel.Visible(C, p, key) });
        if (gk || _filter == 0)
            foreach (var (key, name, tip) in new[] { ("rfx", "Реак", "Реакция: опасные броски"), ("gpos", "Поз", "Позиция: средние и дальние броски") })
                cols.Add(new() { Title = name, Width = 38, Tip = tip, Cell = p => p.IsGoalie ? Ui.AttrV(PlayerModel.Visible(C, p, key)) : Ui.L("", 12), Sort = p => p.IsGoalie ? PlayerModel.Visible(C, p, key) : -1 });
        if (!gk)
        {
            cols.Add(new() { Title = "И", Width = 30, Cell = p => Ui.L($"{C.Skaters.GetValueOrDefault(p.Id)?.Gp ?? 0}", 12, Ui.Muted), Sort = p => C.Skaters.GetValueOrDefault(p.Id)?.Gp ?? 0 });
            cols.Add(new() { Title = "Г", Width = 30, Cell = p => Ui.L($"{C.Skaters.GetValueOrDefault(p.Id)?.G ?? 0}", 12), Sort = p => C.Skaters.GetValueOrDefault(p.Id)?.G ?? 0 });
            cols.Add(new() { Title = "П", Width = 30, Cell = p => Ui.L($"{C.Skaters.GetValueOrDefault(p.Id)?.A ?? 0}", 12), Sort = p => C.Skaters.GetValueOrDefault(p.Id)?.A ?? 0 });
            cols.Add(new() { Title = "О", Width = 34, Cell = p => Ui.L($"{C.Skaters.GetValueOrDefault(p.Id)?.Pts ?? 0}", 13, Ui.Text, Ui.Bold), Sort = p => C.Skaters.GetValueOrDefault(p.Id)?.Pts ?? 0 });
            cols.Add(new() { Title = "+/−", Width = 38, Cell = p => { var s = C.Skaters.GetValueOrDefault(p.Id); return Ui.L(s == null || s.Toi == 0 ? "" : Ui.Signed(s.Pm, "0"), 12, Ui.Muted); }, Sort = p => C.Skaters.GetValueOrDefault(p.Id)?.Pm ?? -99 });
            cols.Add(new() { Title = "Вр", Width = 46, Tip = "Среднее время на льду", Cell = p => { var s = C.Skaters.GetValueOrDefault(p.Id); return Ui.L(s == null || s.Gp == 0 || s.Toi == 0 ? "" : Ui.Toi(s.Toi / (double)s.Gp), 12, Ui.Muted); }, Sort = p => { var s = C.Skaters.GetValueOrDefault(p.Id); return s == null || s.Gp == 0 ? 0 : s.Toi / (double)s.Gp; } });
        }
        else
        {
            cols.Add(new() { Title = "И", Width = 34, Cell = p => Ui.L($"{C.Goalies.GetValueOrDefault(p.Id)?.Gp ?? 0}", 12, Ui.Muted), Sort = p => C.Goalies.GetValueOrDefault(p.Id)?.Gp ?? 0 });
            cols.Add(new() { Title = "В-П-ПО", Width = 70, Cell = p => { var s = C.Goalies.GetValueOrDefault(p.Id); return Ui.L(s == null ? "—" : $"{s.W}-{s.L}-{s.Otl}", 12); } });
            cols.Add(new() { Title = "%ОБ", Width = 50, Cell = p => { var s = C.Goalies.GetValueOrDefault(p.Id); return Ui.L(s == null || s.Sa == 0 ? "—" : $"{s.SvPct:.000}", 13, Ui.Text, Ui.Bold); }, Sort = p => C.Goalies.GetValueOrDefault(p.Id)?.SvPct ?? 0 });
            cols.Add(new() { Title = "КН", Width = 44, Cell = p => { var s = C.Goalies.GetValueOrDefault(p.Id); return Ui.L(s == null ? "—" : $"{s.Gaa:0.00}", 12); }, Sort = p => -(C.Goalies.GetValueOrDefault(p.Id)?.Gaa ?? 9), Desc = true });
        }
        var grid = new Grid<PlayerData>(cols, rows, p => Shell.I.Go("player", null, p.Id.ToString()), null, sortCol: 6);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 0).Also(s => s.ContentMarginTop = s.ContentMarginBottom = 0));
        panel.AddChild(grid.Root);
        _box.AddChild(panel);
    }

    int RoleOrder(int id)
    {
        var r = P.Role(id, _abbr);
        if (r.EndsWith("звено")) return int.Parse(r[..1]);
        if (r.EndsWith("пара")) return 10 + int.Parse(r[..1]);
        return r switch { "Основной" => 20, "Запасной" => 21, "Запас" => 30, _ => 40 };
    }
}

// =====================================================================================================
/// <summary>Входящие: письма тренеру и лента новостей слева, статья справа.</summary>
public partial class InboxPage : Control
{
    Career C => P.C;
    int _sel;
    static int _filter;   // 0 — письма, 1 — новости, 2 — всё
    VBoxContainer _list, _view;
    Control _head;
    public InboxPage() { }
    public InboxPage(string arg) { _sel = int.TryParse(arg, out var i) ? i : 0; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        if (_sel != 0 && C.Messages.FirstOrDefault(m => m.Id == _sel) is { } sm) _filter = sm.IsNews ? (_filter == 2 ? 2 : 1) : (_filter == 2 ? 2 : 0);
        if (_sel == 0) _sel = Visible().Where(m => !m.Read).OrderBy(m => m.Id).Select(m => m.Id).FirstOrDefault();
        if (_sel == 0) _sel = Visible().Select(m => m.Id).DefaultIfEmpty(0).Max();
        var h = Ui.H(12);
        var m = Ui.Margin(h, 16, 14, 16, 14);
        m.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(m);
        var left = Ui.V(8);
        left.CustomMinimumSize = new Vector2(420, 0);
        left.SizeFlagsVertical = SizeFlags.ExpandFill;
        _head = new Control();
        left.AddChild(_head);
        _list = Ui.V(0);
        var lp = new PanelContainer();
        lp.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 0).Also(s => s.ContentMarginTop = s.ContentMarginBottom = 0));
        lp.SizeFlagsVertical = SizeFlags.ExpandFill;
        lp.AddChild(Ui.Scroll(_list));
        left.AddChild(lp);
        h.AddChild(left);
        _view = Ui.V(12);
        var vp = new PanelContainer();
        vp.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 28).Also(s => { s.ContentMarginTop = 22; s.ContentMarginBottom = 28; }));
        vp.SizeFlagsHorizontal = vp.SizeFlagsVertical = SizeFlags.ExpandFill;
        vp.AddChild(Ui.Scroll(_view));
        h.AddChild(vp);
        Build();
    }

    IEnumerable<Message> Visible() => C.Messages.Where(m => _filter == 2 || (_filter == 1) == m.IsNews);

    void Build()
    {
        var msg = C.Messages.FirstOrDefault(x => x.Id == _sel);
        if (msg != null && !msg.Read) { msg.Read = true; Main.I.Save(); Callable.From(() => Shell.I?.RefreshSide()).CallDeferred(); }
        var parent = _head.GetParent();
        int idx = _head.GetIndex();
        int unreadMail = C.Messages.Count(x => !x.Read && !x.IsNews), unreadNews = C.Messages.Count(x => !x.Read && x.IsNews);
        var seg = new Segmented(new[] { unreadMail > 0 ? $"Письма · {unreadMail}" : "Письма", unreadNews > 0 ? $"Новости · {unreadNews}" : "Новости", "Всё" }, _filter, k =>
        {
            _filter = k;
            _sel = Visible().Where(m => !m.Read).OrderBy(m => m.Id).Select(m => m.Id).FirstOrDefault();
            if (_sel == 0) _sel = Visible().Select(m => m.Id).DefaultIfEmpty(0).Max();
            Build();
        });
        var head = Ui.H(8, seg, Ui.Spacer());
        if (Visible().Any(x => !x.Read))
            head.AddChild(Ui.B("Прочитать все", () => { foreach (var x in Visible()) x.Read = true; Main.I.Save(); Build(); Shell.I?.RefreshSide(); }, false, 12));
        parent.RemoveChild(_head); _head.QueueFree();
        _head = head;
        parent.AddChild(_head); parent.MoveChild(_head, idx);

        Ui.Clear(_list);
        foreach (var x in Visible().OrderByDescending(x => x.Id))
        {
            bool on = x.Id == _sel;
            var row = new PanelContainer { MouseDefaultCursorShape = CursorShape.PointingHand };
            var sb = Ui.Box(on ? new Color(Ui.Blue, 0.14f) : new Color(0, 0, 0, 0), 0, null, 14);
            sb.ContentMarginTop = sb.ContentMarginBottom = 9; sb.BorderColor = Ui.Line; sb.BorderWidthBottom = 1;
            if (on) { sb.BorderColor = Ui.Blue; sb.BorderWidthLeft = 3; }
            row.AddThemeStyleboxOverride("panel", sb);
            var title = Ui.L(x.Subject, 14, x.Read ? Ui.Muted : Ui.Text, x.Read ? Ui.Medium : Ui.Semi);
            title.ClipText = true; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            var v = Ui.V(2,
                Ui.H(8, Article.Tag(x.Tag ?? "Письмо", 13), Ui.Expand(Ui.L(x.From, 11, Ui.Dim)),
                    x.Pending ? Ui.L(x.Required ? "нужно решение" : "ждёт ответа", 11, x.Required ? Ui.Red : Ui.Gold, Ui.Semi) : new Control(),
                    Ui.L(Ui.DateShort(x.Date), 11, Ui.Dim)),
                Ui.H(6, x.Read ? new Control() : new ColorRect { Color = Ui.Blue, CustomMinimumSize = new Vector2(6, 6), SizeFlagsVertical = SizeFlags.ShrinkCenter }, Ui.Expand(title)));
            v.MouseFilter = MouseFilterEnum.Ignore;
            SetIgnore(v);
            row.AddChild(v);
            int id = x.Id;
            row.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { _sel = id; Build(); Shell.I?.RefreshSide(); } };
            _list.AddChild(row);
        }
        if (_list.GetChildCount() == 0) _list.AddChild(Ui.Margin(Ui.L(_filter == 1 ? "Новостей пока нет" : "Писем пока нет", 13, Ui.Muted), 14, 12, 14, 12));
        Ui.Clear(_view);
        if (msg == null) { _view.AddChild(Ui.L("Выберите письмо слева", 14, Ui.Muted)); return; }
        _view.AddChild(Article.Make(C, msg, () => { Build(); Shell.I?.RefreshSide(); }));
    }

    static void SetIgnore(Control c)
    {
        foreach (var ch in c.GetChildren()) if (ch is Control cc) { cc.MouseFilter = MouseFilterEnum.Ignore; SetIgnore(cc); }
    }
}

// =====================================================================================================
/// <summary>Медицинский центр: лазарет клуба и травмы ключевых игроков лиги.</summary>
public partial class MedicalPage : Control
{
    Career C => P.C;
    public MedicalPage() { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready()
    {
        var v = Ui.V(12);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        var mine = Active().Where(x => x.p.Team == C.UserTeam).ToList();
        v.AddChild(Widget.Make($"Лазарет клуба · {mine.Count}", Table(mine, false), null, null, 0));
        var league = Active().Where(x => x.p.Team != C.UserTeam && x.p.Ovr >= 60).OrderByDescending(x => x.p.Ovr).Take(30).ToList();
        v.AddChild(Widget.Make("Травмы ключевых игроков лиги", Table(league, true), null, null, 0));
        var season = C.Injuries.Where(i => C.Data.PlayerById[i.Id].Team == C.UserTeam).ToList();
        v.AddChild(Ui.L($"За сезон у клуба {season.Count} {Ui.Plural(season.Count, "травма", "травмы", "травм")}. Частота и длительность травм — по средним НХЛ; чаще травмируются игроки силового стиля.", 12, Ui.Dim));
    }

    List<(PlayerData p, Injury i)> Active() =>
        C.Injured.Distinct().Select(id => (p: C.Data.PlayerById[id], i: InjuryModel.Active(C, id))).Where(x => x.i != null).OrderBy(x => x.i.Until).ToList();

    Control Table(List<(PlayerData p, Injury i)> rows, bool club)
    {
        var cols = new List<Col<(PlayerData p, Injury i)>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = x => Ui.H(6, Ui.Icon("cross", 13, Ui.Loss), Ui.L(x.p.NameRu, 13)), Sort = x => x.p.LastRu, Desc = false },
        };
        if (club) cols.Add(new() { Title = "Клуб", Width = 150, Align = HorizontalAlignment.Left, Cell = x => P.Team(x.p.Team), Sort = x => x.p.Team, Desc = false });
        cols.Add(new() { Title = "Поз", Width = 40, Cell = x => Ui.L(Ui.PosRu(x.p.Pos), 12, Ui.Muted) });
        cols.Add(new() { Title = "Способности", Width = 90, Cell = x => Ui.Stars(StarScale.StarsV(C, x.p, PlayerModel.Visible(C, x.p, "ca")), 12), Sort = x => PlayerModel.Visible(C, x.p, "ca") });
        cols.Add(new() { Title = "Травма", Width = 230, Align = HorizontalAlignment.Left, Cell = x => Ui.L(Cap(x.i.Kind), 12, Ui.Text) });
        cols.Add(new() { Title = "С", Width = 70, Cell = x => Ui.L(x.i.From > C.Today.AddDays(-400) ? Ui.DateShort(x.i.From) : "—", 12, Ui.Muted) });
        cols.Add(new() { Title = "Вернётся", Width = 140, Cell = x => { int d = x.i.Until.DayNumber - C.Today.DayNumber + 1; return Ui.L($"{Ui.DateShort(x.i.Until.AddDays(1))} ({d} {Ui.Plural(d, "день", "дня", "дней")})", 12, d <= 3 ? Ui.Win : d <= 14 ? Ui.Otl : Ui.Loss); }, Sort = x => x.i.Until.DayNumber, Desc = false });
        return new Grid<(PlayerData p, Injury i)>(cols, rows, x => Shell.I.Go("player", null, x.p.Id.ToString())).Root;
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
