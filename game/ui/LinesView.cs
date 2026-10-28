using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Звенья как тактический экран FM: слева стиль игры, в центре площадка с карточками выбранного звена
/// и список всех звеньев, справа кандидаты на выбранное место с пригодностью к роли.
/// Щелчок по карточке на площадке выбирает место, щелчок по игроку справа ставит его туда.
/// </summary>
public partial class LinesView : HBoxContainer
{
    Career C => Main.I.Career;
    Lineup L => C.Lineups[C.UserTeam];

    // выбранное звено: F0..F3, PP0, PP1, PK0, PK1
    string _unit = "F0";
    int _slot;

    public LinesView() { }

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 12);
        Build();
    }

    // ------------------------------------------------------------------ модель звеньев
    static readonly (string key, string name)[] Units =
    {
        ("F0", "1 звено"), ("F1", "2 звено"), ("F2", "3 звено"), ("F3", "4 звено"),
        ("PP0", "Больш. 1"), ("PP1", "Больш. 2"), ("PK0", "Меньш. 1"), ("PK1", "Меньш. 2"),
    };

    /// <summary>Места на площадке выбранного звена: (подпись, тип места, id, позиция на схеме 0..1).</summary>
    List<(string label, string kind, int id, Vector2 at)> Slots(string unit)
    {
        var res = new List<(string, string, int, Vector2)>();
        int Pair(int i) => Math.Min(i, L.D.Count - 1);
        if (unit.StartsWith("F"))
        {
            int i = unit[1] - '0';
            var f = L.F[i]; var d = L.D[Pair(i)];
            res.Add(("ЛН", "W", f[0], new(0.70f, 0.20f))); res.Add(("Ц", "C", f[1], new(0.66f, 0.50f))); res.Add(("ПН", "W", f[2], new(0.70f, 0.80f)));
            res.Add(("ЛЗ", "D", d[0], new(0.42f, 0.30f))); res.Add(("ПЗ", "D", d[1], new(0.42f, 0.70f)));
            res.Add(("В", "G", L.G1, new(0.09f, 0.5f)));
        }
        else if (unit.StartsWith("PP"))
        {
            var u = unit == "PP0" ? L.Pp1 : L.Pp2;
            var at = new[] { new Vector2(0.72f, 0.16f), new Vector2(0.84f, 0.5f), new Vector2(0.72f, 0.84f), new Vector2(0.60f, 0.5f), new Vector2(0.44f, 0.5f) };
            string[] lab = { "Фланг", "Пятак", "Фланг", "Слот", "Синяя" };
            for (int k = 0; k < 5; k++) res.Add((lab[k], k == 4 ? "PPD" : "PPF", u[k], at[k]));
            res.Add(("В", "G", L.G1, new(0.09f, 0.5f)));
        }
        else
        {
            var u = unit == "PK0" ? L.Pk1 : L.Pk2;
            var at = new[] { new Vector2(0.52f, 0.30f), new Vector2(0.52f, 0.70f), new Vector2(0.28f, 0.30f), new Vector2(0.28f, 0.70f) };
            string[] lab = { "Нап.", "Нап.", "Защ.", "Защ." };
            for (int k = 0; k < u.Length && k < 4; k++) res.Add((lab[k], k < 2 ? "PKF" : "PKD", u[k], at[k]));
            res.Add(("В", "G", L.G1, new(0.09f, 0.5f)));
        }
        return res;
    }

    /// <summary>Пригодность игрока к типу места (1–99).</summary>
    static int Fit(PlayerData p, string kind)
    {
        int Role(params string[] names) => p.Roles.Where(r => names.Contains(r.Name)).Select(r => r.V).DefaultIfEmpty(p.Ovr).Max();
        if (kind == "G") return p.IsGoalie ? p.Ovr : 1;
        if (p.IsGoalie) return 1;
        int v = kind switch
        {
            "C" => p.Pos == "C" ? Math.Max(Role("Центр-распасовщик", "Оборонительный центр"), p.Ovr) : p.Ovr - 10,
            "W" => Math.Max(p.Ovr, Role("Снайпер", "Плеймейкер", "Двусторонний форвард", "Скоростной форвард")) - (p.Pos == "C" ? 4 : 0),
            "D" => p.IsDefense ? p.Ovr : p.Ovr - 40,
            "PPF" => Role("Снайпер", "Плеймейкер", "Центр-распасовщик", "Атакующий защитник"),
            "PPD" => p.IsDefense ? Role("Атакующий защитник") : Role("Плеймейкер") - 10,
            "PKF" => p.IsForward ? Role("Двусторонний форвард", "Оборонительный центр") : Role("Защитник-домосед") - 10,
            "PKD" => p.IsDefense ? Role("Защитник-домосед", "Универсальный защитник") : 10,
            _ => p.Ovr,
        };
        if ((kind is "C" or "W") && p.IsDefense) v -= 35;
        return Math.Clamp(v, 1, 99);
    }

    // ------------------------------------------------------------------ экран
    void Build()
    {
        Ui.Clear(this);
        AddChild(StylePanel());
        var center = Ui.Expand(Ui.V(10), true, true);
        AddChild(center);
        center.AddChild(new Segmented(Units.Select(u => u.name).ToArray(), Array.FindIndex(Units, u => u.key == _unit), k => { _unit = Units[k].key; _slot = 0; CallDeferred(nameof(Rebuild)); }));
        center.AddChild(RinkBoard());
        center.AddChild(Ui.Expand(Overview(), true, true));
        AddChild(Candidates());
    }

    // ------------------------------------------------------------------ слева: стиль игры
    Control StylePanel()
    {
        var v = Ui.V(12);
        v.AddChild(Section("Темп игры",
            new Segmented(new[] { "Закрытый", "Обычный", "Открытый" }, L.Tempo + 1, k => { L.Tempo = k - 1; Changed(); }),
            Note(L.Tempo switch
            {
                -1 => "Меньше моментов у обеих команд (−7% голов). Чаще овертаймы — это помогает слабому выстоять против сильного.",
                1 => "Больше моментов у обеих команд (+7% голов). Фаворит реже отдаёт очки в овертаймах.",
                _ => "Обычный хоккей: темп как у среднего клуба НХЛ.",
            })));
        var pull = new CheckBox { Text = "Снимать вратаря раньше", ButtonPressed = L.EarlyPull, FocusMode = FocusModeEnum.None };
        pull.Toggled += on => { L.EarlyPull = on; Changed(); };
        v.AddChild(Section("Концовка матча", pull,
            Note("При −1 в конце: +3,5% сравнять счёт и +7% пропустить в пустые. Аналитика НХЛ: тренеры снимают вратаря слишком поздно, раннее снятие в среднем приносит очки.")));
        var presets = new (string name, double[] f, double[] d)[]
        {
            ("Ровно", new double[] { 17, 16, 15, 12 }, new double[] { 21, 20, 19 }),
            ("Стандарт", new double[] { 20, 17, 14, 9 }, new double[] { 24, 20, 16 }),
            ("На лидеров", new double[] { 23, 19, 12, 6 }, new double[] { 26, 21, 13 }),
        };
        int cur = Array.FindIndex(presets, p => p.f.SequenceEqual(L.FMin) && p.d.SequenceEqual(L.DMin));
        v.AddChild(Section("Игровое время",
            new Segmented(presets.Select(p => p.name).ToArray(), Math.Max(cur, 0), k => { L.FMin = (double[])presets[k].f.Clone(); L.DMin = (double[])presets[k].d.Clone(); Changed(); }),
            Note($"Тройки: {string.Join(" / ", L.FMin.Select(x => x.ToString("0")))} мин · пары: {string.Join(" / ", L.DMin.Select(x => x.ToString("0")))} мин")));
        var b2b = new CheckBox { Text = "Запасной во 2-м матче за 2 дня", ButtonPressed = L.BackupOnB2B, FocusMode = FocusModeEnum.None };
        b2b.Toggled += on => { L.BackupOnB2B = on; Main.I.Save(); };
        var g2 = C.Data.PlayerById.GetValueOrDefault(L.G2);
        v.AddChild(Section("Вратари", Ui.H(0, Ui.L("Основной: ", 12, Ui.Text), Links.Player(C, L.G1, C.Short(L.G1), 12), Ui.L(" · запасной: ", 12, Ui.Text), Links.Player(C, L.G2, C.Short(L.G2), 12)), b2b, Note("Усталость во втором матче подряд стоит команде около 5% голов. Поменять вратарей — во вкладке любого звена, место «В».")));

        var (gF, gA) = C.LineupEffect(C.UserTeam);
        v.AddChild(Section("Анализ состава",
            Ui.H(8, Ui.Expand(Ui.L("Забиваем за матч", 12, Ui.Muted)), Ui.L(Ui.Signed(gF), 14, gF >= -0.005 ? Ui.Win : Ui.Loss, Ui.Bold)),
            Ui.H(8, Ui.Expand(Ui.L("Пропускаем за матч", 12, Ui.Muted)), Ui.L(Ui.Signed(gA), 14, gA <= 0.005 ? Ui.Win : Ui.Loss, Ui.Bold)),
            Note("По сравнению со звеньями, с которыми клуб начал сезон. Считает движок матча.")));
        var auto = Ui.B("Авто-состав", () => Set(Lineup.Auto(C.Data, C.UserTeam, C.Injured.ToHashSet())));
        var nhl = Ui.B("Как в НХЛ", () => Set(Lineup.FromTeam(C.Data, C.Team(C.UserTeam), C.Injured.ToHashSet())));
        auto.AddThemeFontSizeOverride("font_size", 12); nhl.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(Ui.H(8, Ui.Expand(auto), Ui.Expand(nhl)));
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(250, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 12));
        panel.AddChild(Ui.Scroll(v));
        return panel;
    }

    static Control Section(string title, params Control[] kids)
    {
        var v = Ui.V(6, Ui.L(title.ToUpperInvariant(), 11, Ui.Muted, Ui.Bold));
        foreach (var k in kids) v.AddChild(k);
        return v;
    }

    static Label Note(string t) => Ui.L(t, 11, Ui.Dim).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(210, 0); });

    // ------------------------------------------------------------------ площадка
    Control RinkBoard()
    {
        var board = new RinkBoardBg { CustomMinimumSize = new Vector2(0, 340), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var slots = Slots(_unit);
        board.Resized += () => Place(board, slots);
        for (int i = 0; i < slots.Count; i++)
        {
            var (label, kind, id, _) = slots[i];
            int k = i;
            var card = SlotCard(label, kind, id, k == _slot);
            card.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
                if (mb.DoubleClick && id != 0) Shell.I?.Go("player", null, id.ToString());
                else { _slot = k; CallDeferred(nameof(Rebuild)); }
            };
            board.AddChild(card);
        }
        return board;
    }

    static void Place(Control board, List<(string label, string kind, int id, Vector2 at)> slots)
    {
        var s = board.Size;
        if (s.X < 200 || s.Y < 100) return;
        int i = 0;
        foreach (var ch in board.GetChildren().OfType<Control>())
        {
            if (i >= slots.Count) break;
            var at = slots[i++].at;
            ch.Position = new Vector2(Math.Clamp(at.X * s.X - 80, 4, s.X - 164), Math.Clamp(at.Y * s.Y - 32, 4, s.Y - 68));
        }
    }

    Control SlotCard(string label, string kind, int id, bool selected)
    {
        var p = id != 0 ? C.Data.PlayerById.GetValueOrDefault(id) : null;
        var card = new PanelContainer { CustomMinimumSize = new Vector2(160, 64), MouseDefaultCursorShape = CursorShape.PointingHand, MouseFilter = MouseFilterEnum.Stop };
        var bg = selected ? new Color("123A5E") : new Color("0E141C", 0.94f);
        var sb = Ui.Box(bg, 4, selected ? Ui.AccentBright : new Color(1, 1, 1, 0.18f), 6);
        sb.ContentMarginTop = sb.ContentMarginBottom = 4;
        if (selected) sb.SetBorderWidthAll(2);
        card.AddThemeStyleboxOverride("panel", sb);
        if (p == null)
        {
            card.AddChild(Ui.V(0, Ui.L(label, 10, Ui.Dim, Ui.Bold), Ui.L("— пусто —", 13, Ui.Loss)).Also(x => x.MouseFilter = MouseFilterEnum.Ignore));
            return card;
        }
        bool inj = C.Injured.Contains(id);
        int fit = Fit(p, kind);
        var t = C.Team(p.Team);
        var h = Ui.H(6);
        h.AddChild(new PlayerPhoto(p, 44, new Color(t.Colors[0]).Darkened(0.3f)));
        var info = Ui.Expand(Ui.V(0));
        info.AddChild(Ui.H(4, Ui.L(label, 10, Ui.Dim, Ui.Bold), Ui.L(p.Num?.ToString() ?? "", 10, Ui.Muted)));
        info.AddChild(Ui.L(p.LastRu, 13, inj ? Ui.Loss : Ui.Text, Ui.Semi).Also(l => { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; l.CustomMinimumSize = new Vector2(96, 0); }));
        info.AddChild(Ui.Stars(fit, 10));
        h.AddChild(info);
        h.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var c in h.GetChildren().OfType<Control>()) c.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(h);
        card.TooltipText = $"{p.NameRu} · {Ui.PosRu(p.Pos)}\nПригодность к месту: {fit}/99{(p.Roles.Count > 0 ? "\nЛучшая роль: " + p.Roles[0].Name : "")}\nДвойной щелчок — профиль";
        return card;
    }

    // ------------------------------------------------------------------ все звенья списком
    Control Overview()
    {
        var v = Ui.V(0);
        void Row(string key, string name, string mins, IEnumerable<int> ids)
        {
            bool on = key == _unit;
            var p = new PanelContainer { MouseDefaultCursorShape = CursorShape.PointingHand, MouseFilter = MouseFilterEnum.Stop };
            var sb = Ui.Box(on ? new Color(Ui.AccentBright, 0.14f) : new Color(0, 0, 0, 0), 0, null, 10);
            sb.ContentMarginTop = sb.ContentMarginBottom = 5; sb.BorderColor = Ui.Line; sb.BorderWidthBottom = 1;
            p.AddThemeStyleboxOverride("panel", sb);
            var h = Ui.H(8, Ui.L(name, 12, on ? Ui.Text : Ui.Muted, Ui.Semi).Also(l => l.CustomMinimumSize = new Vector2(100, 0)), Ui.L(mins, 11, Ui.Dim).Also(l => l.CustomMinimumSize = new Vector2(42, 0)));
            foreach (var id in ids)
            {
                var pl = C.Data.PlayerById.GetValueOrDefault(id);
                h.AddChild(Ui.L(pl?.LastRu ?? "—", 12, pl == null || C.Injured.Contains(id) ? Ui.Loss : Ui.Text).Also(l => { l.CustomMinimumSize = new Vector2(80, 0); l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }));
            }
            h.MouseFilter = MouseFilterEnum.Ignore;
            foreach (var c in h.GetChildren().OfType<Control>()) c.MouseFilter = MouseFilterEnum.Ignore;
            p.AddChild(h);
            p.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { _unit = key == "G" ? "F0" : key; _slot = key == "G" ? 5 : 0; CallDeferred(nameof(Rebuild)); } };
            v.AddChild(p);
        }
        for (int i = 0; i < L.F.Count; i++) Row($"F{i}", $"{i + 1}-е звено", $"{L.FMin[i]:0} мин", L.F[i].Concat(i < L.D.Count ? L.D[i] : Array.Empty<int>()));
        Row("PP0", "Большинство 1", "", L.Pp1); Row("PP1", "Большинство 2", "", L.Pp2);
        Row("PK0", "Меньшинство 1", "", L.Pk1); Row("PK1", "Меньшинство 2", "", L.Pk2);
        Row("G", "Вратари", "", new[] { L.G1, L.G2 });
        return Widget.Make("Все звенья", Ui.Scroll(v), null, null, 0);
    }

    // ------------------------------------------------------------------ справа: кандидаты
    Control Candidates()
    {
        var slots = Slots(_unit);
        if (_slot >= slots.Count) _slot = 0;
        var (label, kind, cur, _) = slots[_slot];
        string unitName = Units.FirstOrDefault(u => u.key == _unit).name;
        var pool = C.Roster(C.UserTeam).Where(p => kind == "G" ? p.IsGoalie : !p.IsGoalie).ToList();
        var dressed = L.All.ToHashSet();
        var grid = new Grid<PlayerData>(new List<Col<PlayerData>>
        {
            new() { Title = "Игрок", Width = 0, Align = HorizontalAlignment.Left, Sort = p => p.LastRu, Desc = false, Cell = p =>
                Ui.L(p.NameRu, 12, C.Injured.Contains(p.Id) ? Ui.Loss : p.Id == cur ? Colors.White : dressed.Contains(p.Id) ? Ui.Text : Ui.Muted, p.Id == cur ? Ui.Bold : null) },
            new() { Title = "Поз", Width = 28, Cell = p => Ui.L(Ui.PosRu(p.Pos), 11, Ui.Muted) },
            new() { Title = "Сейчас", Width = 64, Align = HorizontalAlignment.Left, Cell = p => Ui.L(P.Role(p.Id, C.UserTeam), 11, C.Injured.Contains(p.Id) ? Ui.Loss : Ui.Dim) },
            new() { Title = "Место", Width = 72, Cell = p => Ui.Stars(Fit(p, kind), 11), Sort = p => Fit(p, kind), Tip = "Пригодность к выбранному месту" },
        }, pool, p => Assign(p.Id), p => p.Id == cur, sortCol: 3);
        var v = Ui.V(8);
        v.AddChild(Ui.L($"КАНДИДАТЫ: {label} · {(kind == "G" ? "вратарь" : unitName)}".ToUpperInvariant(), 11, Ui.Muted, Ui.Bold));
        v.AddChild(Ui.L(kind.StartsWith("P") ? "В спецбригады — только игроки, заявленные на матч." : "Щёлкните игрока, чтобы поставить его на выбранное место.", 11, Ui.Dim).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        v.AddChild(Ui.Expand(Ui.Scroll(grid.Root), true, true));
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 4, null, 10));
        panel.AddChild(v);
        return panel;
    }

    void Assign(int id)
    {
        var slots = Slots(_unit);
        var (_, kind, cur, _) = slots[_slot];
        if (id == cur || C.Injured.Contains(id)) return;
        if (kind == "G")
        {
            if (L.G2 == id) L.G2 = L.G1;
            L.G1 = id;
        }
        else if (_unit.StartsWith("F"))
        {
            if (cur != 0) L.SwapEven(cur, id);
            else
            {
                int i = _unit[1] - '0';
                if (_slot < 3) L.F[i][_slot] = id; else L.D[Math.Min(i, L.D.Count - 1)][_slot - 3] = id;
            }
            L.FixPowerPlay(C.Data);
        }
        else
        {
            if (!L.Skaters.Contains(id)) return;
            var unit = _unit switch { "PP0" => L.Pp1, "PP1" => L.Pp2, "PK0" => L.Pk1, _ => L.Pk2 };
            Lineup.SetUnit(unit, _slot, id);
        }
        Changed();
    }

    void Rebuild() => Build();

    void Changed()
    {
        Main.I.Save();
        CallDeferred(nameof(Rebuild));
    }

    void Set(Lineup l)
    {
        l.Tempo = L.Tempo; l.EarlyPull = L.EarlyPull; l.BackupOnB2B = L.BackupOnB2B;
        C.Lineups[C.UserTeam] = l;
        Changed();
    }
}

/// <summary>Площадка для тактической доски: лёд, линии, зоны (атака — вправо).</summary>
public partial class RinkBoardBg : Control
{
    public override void _Draw()
    {
        var s = Size;
        var rect = new Rect2(2, 2, s.X - 4, s.Y - 4);
        var sb = new StyleBoxFlat { BgColor = new Color("E7EEF5"), BorderColor = new Color("F2C14E"), CornerDetail = 12 };
        sb.SetBorderWidthAll(3); sb.SetCornerRadiusAll((int)(s.Y * 0.28f));
        DrawStyleBox(sb, rect);
        var red = new Color("C8102E", 0.75f); var blue = new Color("1A4BB0", 0.85f);
        DrawLine(new Vector2(s.X / 2, 4), new Vector2(s.X / 2, s.Y - 4), red, 5);
        DrawLine(new Vector2(s.X * 0.355f, 4), new Vector2(s.X * 0.355f, s.Y - 4), blue, 5);
        DrawLine(new Vector2(s.X * 0.645f, 4), new Vector2(s.X * 0.645f, s.Y - 4), blue, 5);
        DrawLine(new Vector2(s.X * 0.055f, 20), new Vector2(s.X * 0.055f, s.Y - 20), red, 2);
        DrawLine(new Vector2(s.X * 0.945f, 20), new Vector2(s.X * 0.945f, s.Y - 20), red, 2);
        DrawArc(new Vector2(s.X / 2, s.Y / 2), s.Y * 0.17f, 0, Mathf.Tau, 48, blue, 2);
        foreach (var (fx, fy) in new[] { (0.17f, 0.27f), (0.17f, 0.73f), (0.83f, 0.27f), (0.83f, 0.73f) })
        {
            DrawArc(new Vector2(s.X * fx, s.Y * fy), s.Y * 0.17f, 0, Mathf.Tau, 48, red, 2);
            DrawCircle(new Vector2(s.X * fx, s.Y * fy), 4, red);
        }
        DrawString(Ui.Semi, new Vector2(s.X * 0.86f, 24), "АТАКА →", HorizontalAlignment.Left, -1, 11, new Color(0, 0, 0, 0.35f));
    }
}
