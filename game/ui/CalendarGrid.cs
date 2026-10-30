using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Календарь сеткой месяца: дни недели столбцами, в дне матча — эмблема соперника, «дома/в гостях», счёт или время до матча;
/// второй матч за два дня — янтарной меткой, сегодня — золотой рамкой. Щелчок по сыгранному — протокол, по будущему — клуб соперника.
/// </summary>
public partial class CalendarGrid : VBoxContainer
{
    readonly Career C;
    readonly string _team;
    static DateOnly _month;   // первое число показанного месяца (запоминается между заходами)
    VBoxContainer _body;
    Label _title;

    public CalendarGrid() { }
    public CalendarGrid(Career c, string team)
    {
        C = c; _team = team;
        AddThemeConstantOverride("separation", 12);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (_month == default || Math.Abs(_month.DayNumber - c.Today.DayNumber) > 400) _month = new DateOnly(c.Today.Year, c.Today.Month, 1);
    }

    public override void _Ready()
    {
        _title = Ui.L("", 20, Ui.Text, Ds.Display);
        var games = C.TeamGames(_team).Select(g => g.Date).ToList();
        var first = games.Count > 0 ? games.Min() : C.Today; var last = games.Count > 0 ? games.Max() : C.Today;
        AddChild(Ui.H(10, Ui.B("‹", () => Shift(-1, first), size: 16), _title, Ui.B("›", () => Shift(1, last), size: 16), Ui.Spacer(),
            Ui.B("Сегодня", () => { _month = new DateOnly(C.Today.Year, C.Today.Month, 1); Fill(); }, size: 13)));
        _body = Ui.V(6);
        AddChild(_body);
        Fill();
    }

    void Shift(int d, DateOnly limit)
    {
        var m = _month.AddMonths(d);
        if (d < 0 && m.AddMonths(1) <= new DateOnly(limit.Year, limit.Month, 1)) return;
        if (d > 0 && m > new DateOnly(limit.Year, limit.Month, 1)) return;
        _month = m; Fill();
    }

    static readonly string[] Months = { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };

    void Fill()
    {
        Ui.Clear(_body);
        _title.Text = $"{Months[_month.Month - 1]} {_month.Year}";
        var head = new GridContainer { Columns = 7 };
        head.AddThemeConstantOverride("h_separation", 8);
        foreach (var d in new[] { "пн", "вт", "ср", "чт", "пт", "сб", "вс" })
            head.AddChild(Ui.L(d, 12, Ui.Muted, Ui.Semi, HorizontalAlignment.Center).Also(l => l.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        _body.AddChild(head);
        var grid = new GridContainer { Columns = 7, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8); grid.AddThemeConstantOverride("v_separation", 8);
        int lead = ((int)_month.DayOfWeek + 6) % 7;   // понедельник — первый
        int days = DateTime.DaysInMonth(_month.Year, _month.Month);
        var byDate = C.TeamGames(_team).ToDictionary(g => g.Date, g => g);
        for (int i = 0; i < lead; i++) grid.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        for (int d = 1; d <= days; d++)
        {
            var date = new DateOnly(_month.Year, _month.Month, d);
            grid.AddChild(Cell(date, byDate.GetValueOrDefault(date), byDate.ContainsKey(date.AddDays(-1))));
        }
        _body.AddChild(grid);
    }

    Control Cell(DateOnly date, ScheduledGame g, bool b2b)
    {
        bool today = date == C.Today, past = date < C.Today;
        var p = new PanelContainer { CustomMinimumSize = new Vector2(0, 92), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        Color edge = today ? Ui.Gold : g != null ? Ds.Edge : new Color(1, 1, 1, 0.03f);
        Color bg = g == null ? new Color(1, 1, 1, 0.015f) : new Color(1, 1, 1, past ? 0.035f : 0.06f);
        if (g != null && g.Played)
        {
            bool home = g.Home == _team; var r = g.Result; int my = home ? r.HomeGoals : r.AwayGoals, op = home ? r.AwayGoals : r.HomeGoals;
            var c = my > op ? Ui.Win : r.End == "REG" || g.Playoff ? Ui.Loss : Ui.Otl;
            bg = new Color(c, 0.08f);
        }
        var sb = Ui.Box(bg, 12, edge, 8); if (today) sb.SetBorderWidthAll(2);
        p.AddThemeStyleboxOverride("panel", sb);
        if (g == null)
        {
            p.AddChild(Ui.L(date.Day.ToString(), 12, past ? new Color(Ui.Dim, 0.6f) : Ui.Dim).Also(l => { l.VerticalAlignment = VerticalAlignment.Top; l.MouseFilter = MouseFilterEnum.Ignore; }));
            return p;
        }
        bool hm = g.Home == _team;
        var opp = C.Team(hm ? g.Away : g.Home);
        var v = Ui.V(2);
        v.MouseFilter = MouseFilterEnum.Ignore;
        v.AddChild(Ui.H(4, Ui.L(date.Day.ToString(), 12, today ? Ui.Gold : Ui.Muted, Ui.Semi), Ui.Spacer(),
            Ui.L(hm ? "дома" : "в гостях", 10, hm ? Ui.Text : Ui.Muted)));
        v.AddChild(new TeamBadge(opp, 30) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
        string line; Color col;
        if (g.Played)
        {
            var r = g.Result; int my = hm ? r.HomeGoals : r.AwayGoals, op = hm ? r.AwayGoals : r.HomeGoals;
            line = $"{my}:{op}{(r.End == "OT" ? " ОТ" : r.End == "SO" ? " Б" : "")}";
            col = my > op ? Ui.Win : r.End == "REG" || g.Playoff ? Ui.Loss : Ui.Otl;
        }
        else { line = opp.Abbr; col = Ui.Text; }
        v.AddChild(Ui.L(line, 13, col, Ui.Bold, HorizontalAlignment.Center));
        if (b2b && !g.Played) v.AddChild(Ui.L("2-й день", 10, Ui.Otl, Ui.Semi, HorizontalAlignment.Center));
        foreach (var ch in v.GetChildren().OfType<Control>()) ch.MouseFilter = MouseFilterEnum.Ignore;
        p.AddChild(v);
        p.MouseDefaultCursorShape = CursorShape.PointingHand;
        p.TooltipText = $"{Ui.Date(date)}: {(hm ? "дома" : "в гостях")} — «{opp.Name}»{(b2b ? "\nВторой матч за два дня" : "")}";
        p.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { if (g.Played) Main.I.ShowMatch(g); else Shell.I?.Go("club", null, opp.Abbr); } };
        return p;
    }
}
