using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Редактор состава: щёлкните игрока в звене, затем другого (в звене или в списке справа) — они поменяются.
/// Минуты по звеньям и звенья большинства влияют на силу команды в движке.
/// </summary>
public partial class LinesView : HBoxContainer
{
    Career C => Main.I.Career;
    Lineup L => C.Lineups[C.UserTeam];
    int _sel;   // выбранный игрок

    public LinesView() { }

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 20);
        Build();
    }

    void Build()
    {
        Ui.Clear(this);
        var left = Ui.V(14);
        AddChild(Ui.Expand(Ui.Scroll(left), true, true));

        var head = Ui.H(10, Ui.Expand(Ui.V(2, Ui.L("Звенья на матч", 20, Ui.Text, Ui.Bold),
            Ui.L(_sel == 0 ? "Щёлкните игрока, затем того, с кем его поменять (в звене или в списке справа)." : $"Выбран: {C.Data.PlayerById[_sel].NameRu}. Теперь щёлкните, с кем поменять.", 13, _sel == 0 ? Ui.Muted : Ui.AccentBright))));
        head.AddChild(Ui.B("Авто-состав", () => { Set(Lineup.Auto(C.Data, C.UserTeam, C.Injured.ToHashSet())); }));
        head.AddChild(Ui.B("Как в НХЛ", () => { Set(Lineup.FromTeam(C.Data, C.Team(C.UserTeam), C.Injured.ToHashSet())); }));
        left.AddChild(head);

        // Эффект состава в движке
        double dOff = MatchEngine.OffIndex(C.Data, L) - C.BaseOff[C.UserTeam];
        double dDef = MatchEngine.DefIndex(C.Data, L) - C.BaseDef[C.UserTeam];
        double gF = MatchEngine.K_FOR * dOff + dDef, gA = -(MatchEngine.K_AGAINST * dOff + dDef);
        var eff = Ui.H(18, Ui.L("Эффект состава по сравнению с базовым:", 13, Ui.Muted),
            Ui.L($"забиваем {Ui.Signed(gF)} гола за матч", 14, gF >= 0 ? Ui.Win : Ui.Loss, Ui.Semi),
            Ui.L($"пропускаем {Ui.Signed(gA)}", 14, gA <= 0 ? Ui.Win : Ui.Loss, Ui.Semi));
        left.AddChild(Ui.Card(eff, Ui.Panel, 14));

        // Минуты
        var presets = new (string name, double[] f, double[] d)[]
        {
            ("Ровно", new double[] { 17, 16, 15, 12 }, new double[] { 21, 20, 19 }),
            ("Стандарт", new double[] { 20, 17, 14, 9 }, new double[] { 24, 20, 16 }),
            ("Ставка на лидеров", new double[] { 23, 19, 12, 6 }, new double[] { 26, 21, 13 }),
        };
        int cur = Array.FindIndex(presets, p => p.f.SequenceEqual(L.FMin) && p.d.SequenceEqual(L.DMin));
        left.AddChild(Ui.H(12, Ui.L("Игровое время звеньев:", 13, Ui.Muted),
            new Segmented(presets.Select(p => p.name).ToArray(), Math.Max(cur, 0), k => { L.FMin = (double[])presets[k].f.Clone(); L.DMin = (double[])presets[k].d.Clone(); Changed(); })));

        // Тройки
        var fBox = Ui.V(8);
        for (int i = 0; i < L.F.Count; i++)
        {
            var row = Ui.H(10);
            row.AddChild(Ui.V(0, Ui.L($"{i + 1}-е звено", 14, Ui.Text, Ui.Semi), Ui.L($"{L.FMin[i]:0} мин", 12, Ui.Muted)).Also(x => x.CustomMinimumSize = new Vector2(96, 0)));
            for (int s = 0; s < 3; s++) row.AddChild(Ui.Expand(Slot(L.F[i][s], Lineup.FSlotNames[s], s == 1 ? "C" : "W")));
            fBox.AddChild(row);
        }
        left.AddChild(Section("Нападение", fBox));
        var dBox = Ui.V(8);
        for (int i = 0; i < L.D.Count; i++)
        {
            var row = Ui.H(10);
            row.AddChild(Ui.V(0, Ui.L($"{i + 1}-я пара", 14, Ui.Text, Ui.Semi), Ui.L($"{L.DMin[i]:0} мин", 12, Ui.Muted)).Also(x => x.CustomMinimumSize = new Vector2(96, 0)));
            for (int s = 0; s < 2; s++) row.AddChild(Ui.Expand(Slot(L.D[i][s], Lineup.DSlotNames[s], "D")));
            row.AddChild(Ui.Expand(new Control()));
            dBox.AddChild(row);
        }
        left.AddChild(Section("Защита", dBox));
        var gRow = Ui.H(10);
        gRow.AddChild(Ui.Expand(Slot(L.G1, "Основной", "G")));
        gRow.AddChild(Ui.Expand(Slot(L.G2, "Запасной", "G")));
        var b2b = new CheckBox { Text = "Запасной во втором матче за два дня", ButtonPressed = L.BackupOnB2B, FocusMode = FocusModeEnum.None };
        b2b.Toggled += on => { L.BackupOnB2B = on; Main.I.Save(); };
        left.AddChild(Section("Вратари", Ui.V(8, gRow, b2b)));
        var ppBox = Ui.V(8);
        foreach (var (name, unit) in new[] { ("1-е большинство", L.Pp1), ("2-е большинство", L.Pp2) })
        {
            var row = Ui.H(8);
            row.AddChild(Ui.L(name, 13, Ui.Text, Ui.Semi).Also(x => x.CustomMinimumSize = new Vector2(126, 0)));
            foreach (var id in unit) row.AddChild(Ui.Expand(Slot(id, "", "", compact: true)));
            ppBox.AddChild(row);
        }
        left.AddChild(Section("Большинство", ppBox));

        // Список игроков клуба
        var right = Ui.V(6);
        var rightPanel = Ui.Card(Ui.Scroll(right), Ui.Panel, 14);
        rightPanel.CustomMinimumSize = new Vector2(380, 0);
        rightPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(rightPanel);
        right.AddChild(Ui.L("ИГРОКИ КЛУБА", 12, Ui.Muted, Ui.Semi));
        var inLineup = L.All.ToHashSet();
        foreach (var grp in new[] { ("Нападающие", (Func<PlayerData, bool>)(p => p.IsForward)), ("Защитники", p => p.IsDefense), ("Вратари", p => p.IsGoalie) })
        {
            right.AddChild(Ui.L(grp.Item1, 13, Ui.Dim, Ui.Semi));
            foreach (var p in C.Roster(C.UserTeam).Where(grp.Item2).OrderByDescending(p => p.Ovr))
            {
                bool inj = C.Injured.Contains(p.Id);
                string where = inj ? "травма" : inLineup.Contains(p.Id) ? Where(p.Id) : "запас";
                var h = Ui.H(8);
                h.AddChild(Ui.L(p.Num?.ToString() ?? "", 12, Ui.Dim).Also(l => { l.CustomMinimumSize = new Vector2(22, 0); l.HorizontalAlignment = HorizontalAlignment.Right; }));
                h.AddChild(Ui.Expand(Ui.L(p.NameRu, 14, inj ? Ui.Loss : inLineup.Contains(p.Id) ? Ui.Text : Ui.Muted)));
                h.AddChild(Ui.L(where, 11, inj ? Ui.Loss : Ui.Dim).Also(l => l.CustomMinimumSize = new Vector2(70, 0)));
                h.AddChild(Ui.L(Ui.PosRu(p.Pos), 11, Ui.Muted).Also(l => l.CustomMinimumSize = new Vector2(24, 0)));
                h.AddChild(Ui.Stars(p.Ovr, 10));
                var card = Ui.Card(h, p.Id == _sel ? new Color(Ui.Accent, 0.25f) : Ui.Panel2, 8, p.Id == _sel ? Ui.Accent : null);
                card.MouseFilter = MouseFilterEnum.Stop;
                card.MouseDefaultCursorShape = CursorShape.PointingHand;
                int id = p.Id;
                card.GuiInput += e => OnClick(e, id, inj);
                right.AddChild(card);
            }
        }
    }

    string Where(int id)
    {
        for (int i = 0; i < L.F.Count; i++) if (L.F[i].Contains(id)) return $"{i + 1}-е зв.";
        for (int i = 0; i < L.D.Count; i++) if (L.D[i].Contains(id)) return $"{i + 1}-я пара";
        if (id == L.G1) return "основной";
        if (id == L.G2) return "запасной";
        return "";
    }

    Control Slot(int id, string slotName, string want, bool compact = false)
    {
        var p = id != 0 ? C.Data.PlayerById.GetValueOrDefault(id) : null;
        var v = Ui.V(2);
        if (!compact) v.AddChild(Ui.L(slotName, 11, Ui.Dim, Ui.Semi));
        if (p == null) v.AddChild(Ui.L("— пусто —", 14, Ui.Loss));
        else
        {
            bool off = want switch { "C" => p.Pos != "C", "W" => !(p.Pos is "LW" or "RW" or "C"), "D" => !p.IsDefense, "G" => !p.IsGoalie, _ => false };
            var h = Ui.H(6);
            h.AddChild(Ui.L(p.Num?.ToString() ?? "", 12, Ui.Dim));
            h.AddChild(Ui.Expand(Ui.L(compact ? p.LastRu : p.NameRu, compact ? 13 : 14, Ui.Text, Ui.Semi).Also(l => { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; })));
            h.AddChild(Ui.Stars(p.Ovr, 11));
            v.AddChild(h);
            if (!compact)
            {
                string sub = p.IsGoalie ? $"вратарь {Ui.Attr20(p.Rating("gk"))} · сейвы сверх ожидаемого {p.Gsax * 100:+0.0;-0.0}%" : $"{Ui.PosRu(p.Pos)} · атака {Ui.Attr20(p.Rating("att"))} · пас {Ui.Attr20(p.Rating("pas"))} · оборона {Ui.Attr20(p.Rating("dfn"))}";
                v.AddChild(Ui.L(sub + (off ? " · не своя позиция" : ""), 11, off ? Ui.Otl : Ui.Muted));
            }
        }
        var card = Ui.Card(v, id != 0 && id == _sel ? new Color(Ui.Accent, 0.25f) : Ui.Panel2, compact ? 8 : 10, id != 0 && id == _sel ? Ui.Accent : Ui.Line);
        card.MouseFilter = MouseFilterEnum.Stop;
        card.MouseDefaultCursorShape = CursorShape.PointingHand;
        card.GuiInput += e => OnClick(e, id, false);
        return card;
    }

    void OnClick(InputEvent e, int id, bool injured)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right && id != 0)
        {
            PlayerCard.Show(this, C.Data.PlayerById[id], C);
            return;
        }
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b) return;
        if (b.DoubleClick && id != 0) { PlayerCard.Show(this, C.Data.PlayerById[id], C); return; }
        if (id == 0) return;
        if (_sel == 0) { _sel = id; CallDeferred(nameof(Rebuild)); return; }
        if (_sel == id) { _sel = 0; CallDeferred(nameof(Rebuild)); return; }
        var a = C.Data.PlayerById[_sel]; var p = C.Data.PlayerById[id];
        bool aIn = L.All.Contains(_sel), pIn = L.All.Contains(id);
        if ((C.Injured.Contains(id) && aIn) || (C.Injured.Contains(_sel) && pIn)) { _sel = 0; CallDeferred(nameof(Rebuild)); return; }   // травмированных не ставим
        if (a.IsGoalie != p.IsGoalie && (aIn || pIn)) { _sel = id; CallDeferred(nameof(Rebuild)); return; }   // вратаря с полевым не меняем
        L.Swap(_sel, id);
        _sel = 0;
        L.FixPowerPlay(C.Data);
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
        C.Lineups[C.UserTeam] = l;
        _sel = 0;
        Changed();
    }

    static Control Section(string title, Control content) => Ui.Card(Ui.V(10, Ui.L(title.ToUpper(), 12, Ui.Muted, Ui.Semi), content), Ui.Panel, 16);
}
