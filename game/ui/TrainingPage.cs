using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Тренировки: «Неделя» (нагрузка и упор — у каждого выбора цена), «Индивидуальные» (одна характеристика растёт вдвое
/// быстрее, до 6 игроков), «Развитие молодых» (что помогает и что мешает расти).
/// </summary>
public partial class TrainingPage : Control
{
    readonly string _sub;
    Career C => P.C;
    TrainingPlan T => Training.Of(C);

    public TrainingPage(string sub) { _sub = sub ?? "week"; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override void _Ready() => Build();

    void Build()
    {
        foreach (var ch in GetChildren()) { RemoveChild(ch); ch.QueueFree(); }
        var v = Ui.V(14);
        var page = P.Page(v);
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        switch (_sub)
        {
            case "individual": Individual(v); break;
            case "youth": Youth(v); break;
            default: Week(v); break;
        }
    }

    void Changed() { Main.I.Save(); CallDeferred(nameof(Build)); }

    static void Wrap(Label l) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(160, 0); }

    // ------------------------------------------------------------------ неделя
    void Week(VBoxContainer v)
    {
        v.AddChild(Ui.V(2, Ui.L("Тренировки на неделю", 20, Ui.Text, Ds.Display),
            Ui.L("Нагрузка и упор действуют, пока вы их не поменяете. Бесплатного выбора нет: быстрее растут — быстрее устают, учат схемы — меньше развиваются.", 12, Ui.Muted).Also(Wrap)));
        var row = Ui.H(14);
        var left = Ui.Expand(Ui.V(14));
        row.AddChild(left);

        left.AddChild(Ui.L("Нагрузка", 17, Ui.Text, Ui.Semi));
        var loads = Ui.H(10);
        foreach (var (val, name, eff) in new[]
        {
            (-1, "Лёгкая", "Развитие ×0,8\nСвежесть +1,5 в день\nТравмы −8%\nНастрой чуть выше"),
            (0, "Обычная", "Развитие как обычно\nСвежесть как обычно\nТравмы как обычно\n "),
            (1, "Тяжёлая", "Развитие ×1,25\nСвежесть не выше 90%\nТравмы +12%\nВетераны ворчат"),
        })
            loads.AddChild(Ui.Expand(Card(name, eff, T.Load == val, () => { T.Load = val; Changed(); })));
        left.AddChild(loads);

        left.AddChild(Ui.L("Упор недели", 17, Ui.Text, Ui.Semi));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 10);
        foreach (var f in Training.Foci)
        {
            var key = f.Key;
            var c = Card(f.Name, f.What, T.Focus == key, () => { T.Focus = key; Changed(); });
            c.CustomMinimumSize = new Vector2(220, 118);
            grid.AddChild(c);
        }
        left.AddChild(grid);
        row.AddChild(StatePanel());
        v.AddChild(row);
    }

    /// <summary>Карточка выбора: название, последствия; выбранная — янтарная рамка.</summary>
    static Control Card(string title, string text, bool on, Action pick)
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        p.AddThemeStyleboxOverride("panel", Ui.Box(on ? Ui.Panel2 : Ui.Panel, 3, on ? Ui.Gold : Ui.Line, 12));
        var col = Ui.V(4, Ui.L(title, 17, on ? Ui.Gold : Ui.Text, Ui.CondBold), Ui.L(text, 12, on ? Ui.Text : Ui.Muted).Also(Wrap));
        col.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var ch in col.GetChildren().OfType<Control>()) ch.MouseFilter = MouseFilterEnum.Ignore;
        p.AddChild(col);
        p.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) pick(); };
        return p;
    }

    /// <summary>Справа: состояние команды — свежесть, усталые, выучка схем, сыгранность звеньев, молодые.</summary>
    Control StatePanel()
    {
        var v = Ui.V(10);
        var roster = C.Roster(C.UserTeam).Where(p => C.Players.ContainsKey(p.Id) && !p.IsGoalie).ToList();
        double fresh = roster.Average(p => C.Players[p.Id].Fresh);
        int tired = roster.Count(p => C.Players[p.Id].Fresh < Fatigue.FRESH_OK);
        v.AddChild(Ui.L("Состояние команды", 17, Ui.Text, Ui.Semi));
        v.AddChild(PlayerViz.Tile("battery", "Свежесть", $"{fresh:0}%", fresh >= 85 ? Ui.Win : fresh >= Fatigue.FRESH_OK ? Ui.Text : Ui.Otl,
            tired == 0 ? "уставших нет" : $"уставших: {tired}", "Средняя свежесть полевых. Ниже 75% игрок теряет в уровне и чаще травмируется"));
        var l = C.Lineups[C.UserTeam];
        var learn = Enumerable.Range(0, Tactics.Levers.Length).Where(i => l.SysOf(i) != 0)
            .Select(i => (Tactics.Levers[i], l.Learned(Tactics.Levers[i].Key + (l.SysOf(i) < 0 ? "-" : "+")))).ToList();
        double minL = learn.Count == 0 ? 1 : learn.Min(x => x.Item2);
        v.AddChild(PlayerViz.Tile("swords", "Выучка системы", learn.Count == 0 ? "—" : $"{minL * 100:0}%", minL >= 0.95 ? Ui.Win : minL >= 0.6 ? Ui.Text : Ui.Otl,
            learn.Count == 0 ? "система обычная — учить нечего" : string.Join(", ", learn.Where(x => x.Item2 < 0.95).Select(x => $"{x.Item1.Name.ToLowerInvariant()} {x.Item2 * 100:0}%").DefaultIfEmpty("всё выучено")),
            "Невыученная схема даёт меньше и рискованнее. Упор «Система» учит втрое быстрее"));
        double fam = Enumerable.Range(0, l.F.Count).Average(i => C.UnitFam(l.F[i]));
        v.AddChild(PlayerViz.Tile("users", "Сыгранность троек", $"{fam * 100:0}%", LinesView.FamColor(fam), "растёт от минут вместе и от упора «Сыгранность»"));
        int young = C.Roster(C.UserTeam).Count(p => (p.Age(C.Today) ?? 30) <= 23);
        v.AddChild(PlayerViz.Tile("trending-up", "Молодые", $"{young}", Ui.Text, $"индивидуальная работа: {T.Individual.Count} из {Training.SLOTS}"));
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(340, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 14, null, 14));
        panel.AddChild(v);
        return panel;
    }

    // ------------------------------------------------------------------ индивидуальные
    void Individual(VBoxContainer v)
    {
        v.AddChild(Ui.V(2,
            Ui.L($"Одна характеристика растёт вдвое быстрее, остальные — чуть медленнее. Мест — {Training.SLOTS}: тренеры не разорвутся. Расти может только тот, у кого есть запас до потенциала.", 12, Ui.Muted).Also(Wrap)));
        v.AddChild(Ui.L($"Занято {T.Individual.Count} из {Training.SLOTS}", 14, T.Individual.Count >= Training.SLOTS ? Ui.Gold : Ui.Text, Ui.Semi));
        var rows = C.Roster(C.UserTeam).Where(p => C.Players.ContainsKey(p.Id)).OrderBy(p => T.Individual.ContainsKey(p.Id) ? 0 : 1).ThenBy(p => p.Age(C.Today) ?? 30).ToList();
        var cols = new List<Col<PlayerData>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = p => Links.Player(p, null, 13), Sort = p => p.LastRu, Desc = false },
            new() { Title = "Поз", Width = 36, Cell = p => Ui.L(Ui.PosRu(p.Pos), 12, Ui.Muted) },
            new() { Title = "Возр.", Width = 50, Cell = p => Ui.L($"{p.Age(C.Today)}", 12, Ui.Muted), Sort = p => p.Age(C.Today) ?? 30, Desc = false },
            new() { Title = "Способности", Width = 90, Cell = p => Ui.Stars(StarScale.StarsV(C, p, PlayerModel.Visible(C, p, "ca")), 12), Sort = p => PlayerModel.Visible(C, p, "ca") },
            new() { Title = "Потенциал", Width = 90, Cell = p => Ui.Stars(StarScale.StarsV(C, p, PlayerModel.PotentialRange(C, p).hi), 12), Sort = p => PlayerModel.PotentialRange(C, p).hi },
            new() { Title = "Работа", Width = 260, Align = HorizontalAlignment.Left, Cell = p => Pick(p) },
        };
        v.AddChild(Widget.Make("Игроки", new Grid<PlayerData>(cols, rows, null, p => T.Individual.ContainsKey(p.Id)).Root, null, null, 0));
    }

    Control Pick(PlayerData p)
    {
        var defs = HM27.Core.Attrs.For(p);
        var ob = new OptionButton { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(250, 0) };
        ob.AddItem("—", 0);
        string cur = T.Individual.GetValueOrDefault(p.Id);
        for (int i = 0; i < defs.Length; i++)
        {
            ob.AddItem($"{defs[i].Name} ({Ds.ShowI(PlayerModel.Visible(C, p, defs[i].Key))})", i + 1);
            if (defs[i].Key == cur) ob.Selected = i + 1;
        }
        if (cur == null) ob.Selected = 0;
        bool full = T.Individual.Count >= Training.SLOTS && cur == null;
        ob.Disabled = full;
        if (full) ob.TooltipText = "Все места заняты — освободите одно";
        ob.ItemSelected += idx =>
        {
            if (idx == 0) T.Individual.Remove(p.Id); else T.Individual[p.Id] = defs[(int)idx - 1].Key;
            Changed();
        };
        return ob;
    }

    // ------------------------------------------------------------------ развитие молодых
    void Youth(VBoxContainer v)
    {
        v.AddChild(Ui.V(2,
            Ui.L("Потенциал — потолок, а не обещание: до него доходит примерно каждый четвёртый. Растут от игрового времени и роли, характера, удачи сезона, своих оценок и тренировок; мешают травмы и скамейка.", 12, Ui.Muted).Also(Wrap)));
        var rows = C.Roster(C.UserTeam).Where(p => C.Players.ContainsKey(p.Id) && (p.Age(C.Today) ?? 30) <= 23).OrderBy(p => p.Age(C.Today)).ToList();
        if (rows.Count == 0) { v.AddChild(Ui.L("В клубе нет игроков моложе 24 лет.", 14, Ui.Muted)); return; }
        var cols = new List<Col<PlayerData>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Cell = p => Links.Player(p, null, 13), Sort = p => p.LastRu, Desc = false },
            new() { Title = "Возр.", Width = 50, Cell = p => Ui.L($"{p.Age(C.Today)}", 12, Ui.Muted), Sort = p => p.Age(C.Today) ?? 30, Desc = false },
            new() { Title = "Способности", Width = 90, Cell = p => Ui.Stars(StarScale.StarsV(C, p, PlayerModel.Visible(C, p, "ca")), 12) },
            new() { Title = "Потенциал", Width = 90, Cell = p => Ui.Stars(StarScale.StarsV(C, p, PlayerModel.PotentialRange(C, p).hi), 12) },
            new() { Title = "За сезон", Width = 70, Tip = "Изменение способностей с начала сезона (КА)", Cell = p => { var s = C.Players[p.Id]; double d = s.Ca - s.Ca0; return Ui.L(Ui.Signed(d, "0.0"), 13, d > 0.3 ? Ui.Win : d < -0.3 ? Ui.Loss : Ui.Muted, Ui.Semi); }, Sort = p => C.Players[p.Id].Ca - C.Players[p.Id].Ca0 },
            new() { Title = "Условия", Width = 80, Tip = "Условия развития: 1,0 — как у обычного игрока основы", Cell = p => { double k = Development.Conditions(C, p, C.Players[p.Id]); return Ui.L($"×{k:0.00}", 13, k >= 1.05 ? Ui.Win : k >= 0.9 ? Ui.Text : Ui.Otl); }, Sort = p => Development.Conditions(C, p, C.Players[p.Id]) },
            new() { Title = "Что мешает или помогает", Width = 0, Align = HorizontalAlignment.Left, Cell = p => Ui.L(Why(p), 12, Ui.Muted) },
        };
        v.AddChild(Widget.Make("Молодые игроки", new Grid<PlayerData>(cols, rows, p => Shell.I.Go("player", null, p.Id.ToString())).Root, null, null, 0));
    }

    string Why(PlayerData p)
    {
        var s = C.Players[p.Id];
        var parts = new List<string>();
        if (C.Injured.Contains(p.Id)) parts.Add("травма — не растёт");
        double min = C.Lineups[C.UserTeam].MinutesOf(p.Id);
        var l = C.Lineups[C.UserTeam];
        if (!p.IsGoalie) parts.Add(min <= 0 ? "не в составе — мало практики" : min < 12 ? $"мало времени ({min:0} мин)" : $"играет {min:0} мин");
        else parts.Add(l.G1 == p.Id ? "основной — играет много" : l.G2 == p.Id ? "сменщик — матчей мало" : "не в заявке — без практики");
        if (s.Prof >= 15) parts.Add("профессионал");
        else if (s.Prof <= 7) parts.Add("к работе относится прохладно");
        if (T.Individual.TryGetValue(p.Id, out var k)) parts.Add("индивидуально: " + HM27.Core.Attrs.For(p).FirstOrDefault(a => a.Key == k)?.Name.ToLowerInvariant());
        if (s.Pa - s.Ca < 4) parts.Add("почти у потолка");
        return string.Join(" · ", parts);
    }
}
