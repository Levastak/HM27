using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Звенья карточками (вид по умолчанию): тройки и пары рядами, у каждого игрока — фото в кольце рейтинга, свежесть, сильная сторона.
/// Перетащить карточку на карточку — поменять игроков местами; из запаса — поставить в состав. Щелчок — список кандидатов на место,
/// двойной щелчок — профиль. Связи между партнёрами окрашены сыгранностью пары. Тактическая доска и настройки — отдельные виды.
/// </summary>
public partial class LinesView
{
    /// <summary>cards — пятёрки карточками, special — спецбригады карточками, board — тактическая доска с кандидатами, settings — темп, снятие, минуты.</summary>
    static string _mode = "cards";
    public static string Mode { set => _mode = value; }
    const float CW = 188, CH = 112, CG = 24, LBL = 60;
    LineCard _sel;

    Control ModeBar()
    {
        var h = Ui.H(12);
        string[] keys = { "cards", "special", "board", "settings" };
        h.AddChild(new Segmented(new[] { "Пятёрки", "Спецбригады", "Доска", "Настройки" }, Array.IndexOf(keys, _mode), k => { _mode = keys[k]; CallDeferred(nameof(Rebuild)); }));
        h.AddChild(Ui.Spacer());
        if (_mode is "cards" or "special")
        {
            var (gF, gA) = C.LineupEffect(C.UserTeam);
            var (fF, fA) = Coach.FamGoals(C, L);
            double ab = gF - gA, fam = fF - fA;
            Control Word(string k, double x) => Ui.V(-2, Ui.L(k, 11, Ui.Dim), Ui.L(Staff.GainWord(x), 13, Math.Abs(x) < 0.01 ? Ui.Text : x > 0 ? Ui.Win : Ui.Loss, Ui.Semi));
            h.AddChild(Word("Звенья против «как в НХЛ»", ab).Also(x => x.TooltipText = $"Способности — глазами {Staff.Assistant(C)?.Name ?? "помощника"}: он может ошибаться"));
            h.AddChild(Word("Сыгранность", fam).Also(x => x.TooltipText = FamTip));
            h.AddChild(Ui.Spacer(false, 8));
            h.AddChild(Ui.B("Вариант помощника", () => AssistantReview.Open(this, C, C.UserTeam, Set), size: 13));
            h.AddChild(Ui.B("Как в НХЛ", () => Set(Lineup.FromTeam(C.Data, C.Team(C.UserTeam), C.Injured.ToHashSet())), size: 13));
        }
        return h;
    }

    void BuildCards()
    {
        var v = Ui.V(18);
        v.AddChild(ModeBar());
        var row = Ui.H(20);
        v.AddChild(row);
        var cards = new List<CanvasItem>();
        if (_mode == "cards")
        {
            var fv = Ui.V(14);
            fv.AddChild(Ui.L("Нападение", 13, Ui.Muted, Ui.Semi));
            for (int i = 0; i < L.F.Count; i++) fv.AddChild(UnitRow((i + 1).ToString(), null, Enumerable.Range(0, 3).Select(s => ($"F{i}", s, L.F[i][s], s == 1 ? "C" : "W")).ToList(), C.UnitFam(L.F[i]), i, false, cards));
            row.AddChild(Glass(fv));
            var dv = Ui.V(14);
            dv.AddChild(Ui.L("Оборона и вратари", 13, Ui.Muted, Ui.Semi));
            for (int i = 0; i < L.D.Count; i++) dv.AddChild(UnitRow((i + 1).ToString(), null, Enumerable.Range(0, 2).Select(s => ($"F{i}", 3 + s, L.D[i][s], "D")).ToList(), C.UnitFam(L.D[i]), i, true, cards));
            dv.AddChild(UnitRow("Вр", null, new List<(string, int, int, string)> { ("F0", 5, L.G1, "G"), ("G2", 0, L.G2, "G") }, -1, -1, false, cards));
            row.AddChild(Glass(dv));
        }
        else
        {
            var sv = Ui.V(14);
            sv.AddChild(Ui.L("Спецбригады: только игроки, заявленные на матч", 13, Ui.Muted, Ui.Semi));
            foreach (var (key, num, cap, unit) in new[] { ("PP0", "1", "большинство", L.Pp1), ("PP1", "2", "большинство", L.Pp2), ("PK0", "1", "меньшинство", L.Pk1), ("PK1", "2", "меньшинство", L.Pk2) })
            {
                var slots = Slots(key);
                var list = slots.Take(unit.Length).Select((s, k) => (key, k, s.id, s.kind)).ToList();
                var ids = unit.Where(id => id != 0).ToArray();
                sv.AddChild(UnitRow(num, cap, list, ids.Length > 1 ? C.UnitFam(ids) : -1, -1, false, cards));
            }
            row.AddChild(Glass(sv));
        }
        v.AddChild(Bench(cards));
        v.AddChild(SettingsChips());
        var scroll = Ui.Scroll(v);
        AddChild(Ui.Expand(scroll, true, true));
        Ds.Reveal(this, cards, 0.012f);
    }

    static GlassBox Glass(Control c) { var g = new GlassBox(22, Ds.R_L, 0.045f); g.AddChild(c); return g; }

    /// <summary>Ряд звена: номер, сыгранность и роль слева; карточки, между ними связи сыгранности пар.</summary>
    Control UnitRow(string num, string caption, List<(string unit, int slot, int id, string kind)> cards, double fam, int roleIdx, bool defense, List<CanvasItem> reveal)
    {
        float w = LBL + cards.Count * CW + (cards.Count - 1) * CG;
        var row = new Control { CustomMinimumSize = new Vector2(w, CH) };
        bool linkable = cards.Count > 1 && cards[0].kind != "G";
        row.AddChild(new Paint(new Vector2(w, CH), q =>
        {
            q.Text(Ds.DisplayLight, caption != null ? 26 : 30, 0, caption != null ? 10 : 16, num, num == "Вр" ? Ui.Dim : Ui.Text);
            if (caption != null) q.Text(Ui.Regular, 10, 0, 46, caption, Ui.Dim);
            if (fam >= 0) q.Text(Ui.Semi, 12, 0, caption != null ? 62 : 58, $"{fam * 100:0}%", Ds.FamCol(fam));
            // связи партнёров: линия в зазоре, цвет — сыгранность пары
            for (int i = 0; i + 1 < cards.Count && linkable; i++)
            {
                int a = cards[i].id, b = cards[i + 1].id;
                double f = a != 0 && b != 0 ? C.Fam(a, b) : 0;
                float x0 = LBL + (i + 1) * CW + i * CG, x1 = x0 + CG, y = CH / 2;
                var col = new Color(Ds.FamCol(f), 0.85f);
                q.DrawLine(new Vector2(x0 - 2, y), new Vector2(x1 + 2, y), col, 2, true);
                q.DrawCircle(new Vector2((x0 + x1) / 2, y), 4, col);
            }
        }));
        if (roleIdx >= 0)
        {
            int role = defense ? L.RoleD(roleIdx) : L.RoleF(roleIdx);
            var rb = RolePill(role, () =>
            {
                EnsureRoles();
                var arr = defense ? L.DRole : L.FRole;
                int k = (arr[roleIdx] + 1) % 3;
                if (k != 0) for (int j = 0; j < arr.Length; j++) if (arr[j] == k) arr[j] = 0;
                arr[roleIdx] = k;
                Changed();
            });
            rb.Position = new Vector2(0, 80);
            rb.TooltipText = RoleTips[role] + "\nЩелчок — сменить роль (атакующая и оборонительная — по одной на клуб)";
            row.AddChild(rb);
        }
        for (int i = 0; i < cards.Count; i++)
        {
            var (unit, slot, id, kind) = cards[i];
            var card = new LineCard(C, this, unit, slot, id, kind) { Position = new Vector2(LBL + i * (CW + CG), 0) };
            if (_sel != null && _sel.Unit == unit && _sel.Slot == slot) { card.Selected = true; _sel = card; }
            row.AddChild(card);
            reveal.Add(card);
        }
        return row;
    }

    static Button RolePill(int role, Action onPress)
    {
        string s = role == 1 ? "атака" : role == 2 ? "оборона" : "обычная";
        var col = role == 1 ? Ds.GoldT : role == 2 ? Ds.Elite : Ui.Dim;
        var b = new Button { Text = s, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 22) };
        b.AddThemeStyleboxOverride("normal", Ds.Pill(new Color(col, role == 0 ? 0.06f : 0.14f), 8, 1));
        b.AddThemeStyleboxOverride("hover", Ds.Pill(new Color(col, 0.24f), 8, 1));
        b.AddThemeStyleboxOverride("pressed", Ds.Pill(new Color(col, 0.24f), 8, 1));
        b.AddThemeColorOverride("font_color", col); b.AddThemeColorOverride("font_hover_color", col);
        b.AddThemeFontSizeOverride("font_size", 11); b.AddThemeFontOverride("font", Ui.Semi);
        b.Pressed += onPress;
        return b;
    }

    /// <summary>Запас и лазарет: кого можно перетащить в состав.</summary>
    Control Bench(List<CanvasItem> reveal)
    {
        var dressed = L.All.ToHashSet();
        var rest = C.Roster(C.UserTeam).Where(p => !dressed.Contains(p.Id)).OrderBy(p => C.Injured.Contains(p.Id)).ThenByDescending(p => Ds.Ovr(C, p)).ToList();
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 10); flow.AddThemeConstantOverride("v_separation", 10);
        foreach (var p in rest)
        {
            var chip = new BenchChip(C, this, p);
            flow.AddChild(chip);
            reveal.Add(chip);
        }
        if (rest.Count == 0) flow.AddChild(Ui.L("Все игроки в составе", 12, Ui.Dim));
        var v = Ui.V(10, Ui.H(8, Ui.L("Запас и лазарет", 13, Ui.Muted, Ui.Semi), Ui.L("перетащите игрока на карточку в составе", 12, Ui.Dim)), flow);
        return Glass(v);
    }

    /// <summary>Настройки состава строкой; подробно — во вкладке «Доска и настройки».</summary>
    Control SettingsChips()
    {
        var h = Ui.H(8);
        string tempo = L.TempoAuto ? "по сопернику" : L.Tempo switch { -1 => "закрытый", 1 => "открытый", _ => "обычный" };
        string pull = L.Pull switch { Coach.ASSIST => "по расчёту", Coach.EARLY => "рано", _ => "как в НХЛ" };
        string time = string.Join("/", L.FMin.Select(x => x.ToString("0")));
        foreach (var (k, val) in new[] { ("Темп", tempo), ("Снятие вратаря", pull), ("Минуты троек", time), ("Расстановка", L.Matchup ? "оборонительная против лучших" : "по очереди"), ("Отдых вратарю", L.BackupOnB2B ? "да" : "нет") })
        {
            var b = new Button { FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = "Изменить — во вкладке «Настройки»" };
            b.AddThemeStyleboxOverride("normal", Ds.Pill(new Color(1, 1, 1, 0.04f), 14, 6, Ds.Edge));
            b.AddThemeStyleboxOverride("hover", Ds.Pill(new Color(1, 1, 1, 0.08f), 14, 6, Ds.Edge));
            b.AddThemeStyleboxOverride("pressed", Ds.Pill(new Color(1, 1, 1, 0.08f), 14, 6, Ds.Edge));
            var hh = Ui.H(6, Ui.L(k, 12, Ui.Dim), Ui.L(val, 12, Ui.Text, Ui.Semi));
            hh.MouseFilter = MouseFilterEnum.Ignore; foreach (var c in hh.GetChildren().OfType<Control>()) c.MouseFilter = MouseFilterEnum.Ignore;
            b.AddChild(Ui.Margin(hh, 14, 6, 14, 6).Also(m => m.MouseFilter = MouseFilterEnum.Ignore));
            b.CustomMinimumSize = hh.GetCombinedMinimumSize() + new Vector2(28, 12);
            b.Pressed += () => { _mode = "settings"; CallDeferred(nameof(Rebuild)); };
            h.AddChild(b);
        }
        return h;
    }

    // ------------------------------------------------------------------ действия с карточками
    internal void CardClicked(LineCard card)
    {
        if (_sel != null && IsInstanceValid(_sel)) { _sel.Selected = false; _sel.QueueRedraw(); }
        _sel = card; card.Selected = true; card.QueueRedraw();
        ShowCandidates(card);
    }

    internal void CardOpen(LineCard card) { if (card.Id != 0) Shell.I?.Go("player", null, card.Id.ToString()); }

    /// <summary>Перетащили игрока (из карточки или запаса) на карточку.</summary>
    internal void DropOn(LineCard dst, int id, LineCard src)
    {
        if (id == 0 || id == dst.Id) return;
        if (C.Injured.Contains(id)) return;
        bool dstSpecial = dst.Unit.StartsWith("P");
        if (dstSpecial)
        {
            if (!L.Skaters.Contains(id)) return;   // в спецбригады — только заявленные
            var du = UnitArr(dst.Unit);
            if (src != null && src.Unit.StartsWith("P") && src.Unit != dst.Unit && dst.Id != 0)
            {
                var su = UnitArr(src.Unit);
                Lineup.SetUnit(du, dst.Slot, id);
                Lineup.SetUnit(su, src.Slot, dst.Id);
            }
            else Lineup.SetUnit(du, dst.Slot, id);
            Changed();
            return;
        }
        if (src != null && src.Unit.StartsWith("P")) return;   // из спецбригады в пятёрку не перетаскиваем
        var p = C.Data.PlayerById[id];
        if (dst.Kind == "G" != p.IsGoalie) return;
        if (dst.Id != 0) L.SwapEven(dst.Id, id);
        else Place(dst, id);
        L.FixPowerPlay(C.Data);
        Changed();
    }

    int[] UnitArr(string unit) => unit switch { "PP0" => L.Pp1, "PP1" => L.Pp2, "PK0" => L.Pk1, _ => L.Pk2 };

    void Place(LineCard dst, int id)
    {
        if (dst.Unit == "G2") { L.G2 = id; return; }
        int i = dst.Unit[1] - '0';
        if (dst.Slot < 3) L.F[i][dst.Slot] = id;
        else if (dst.Slot < 5) L.D[Math.Min(i, L.D.Count - 1)][dst.Slot - 3] = id;
        else L.G1 = id;
    }

    /// <summary>Кандидаты на место: всплывающий список рядом с карточкой — пригодность, сыгранность с партнёрами.</summary>
    void ShowCandidates(LineCard card)
    {
        bool special = card.Unit.StartsWith("P");
        string kind = card.Kind;
        var dressed = L.All.ToHashSet();
        var pool = C.Roster(C.UserTeam).Where(p => kind == "G" ? p.IsGoalie : !p.IsGoalie).Where(p => p.Id != card.Id).Where(p => !special || L.Skaters.Contains(p.Id))
            .OrderBy(p => C.Injured.Contains(p.Id)).ThenByDescending(p => Fit(p, kind)).Take(9).ToList();
        var pop = new PopupPanel();
        pop.AddThemeStyleboxOverride("panel", Ds.GlassBoxStyle(Ds.R_M, 8, 0.0f).Also(s => s.BgColor = new Color("111823")));
        var v = Ui.V(2);
        pop.AddChild(v);
        string where = card.Unit == "G2" ? "запасной вратарь" : kind == "G" ? "основной вратарь" : special ? (card.Unit.StartsWith("PP") ? "большинство" : "меньшинство") : kind == "C" ? "центр" : kind == "W" ? "крайний" : "защитник";
        v.AddChild(Ui.Margin(Ui.L($"Кто на место: {where}", 13, Ui.Muted, Ui.Semi), 8, 4, 8, 6));
        foreach (var p in pool)
        {
            bool inj = C.Injured.Contains(p.Id);
            var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(330, 44), Disabled = inj, MouseDefaultCursorShape = CursorShape.PointingHand };
            b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            b.AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());
            var hv = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.06f) }; hv.SetCornerRadiusAll(10);
            b.AddThemeStyleboxOverride("hover", hv); b.AddThemeStyleboxOverride("pressed", hv);
            int ovr = Ds.Ovr(C, p);
            double mf = special ? -1 : MateFamFor(card, p.Id);
            var pp = p; var t = C.Team(p.Team);
            b.AddChild(new Paint(new Vector2(330, 44), q =>
            {
                var c = new Vector2(24, 22);
                Face.Player(q, pp, t, new Rect2(c.X - 17, c.Y - 17, 34, 34), Face.Circle(c, 17));
                q.DrawArc(c, 19, 0, Mathf.Tau, 48, new Color(Ds.Tier(ovr), 0.8f), 2, true);
                q.Text(Ui.Semi, 13, 52, 5, pp.NameRu, inj ? Ui.Loss : Ui.Text);
                q.Text(Ui.Regular, 11, 52, 24, inj ? "травма" : $"{Ds.PosShort(pp.Pos)}  ·  {P.Role(pp.Id, C.UserTeam)}", Ui.Muted);
                q.Text(Ds.DisplayLight, 15, 222, 12, ovr.ToString(), Ds.Tier(ovr), HorizontalAlignment.Right, 34);
                if (mf >= 0) q.Text(Ui.Semi, 12, 262, 14, $"{mf * 100:0}%", Ds.FamCol(mf), HorizontalAlignment.Right, 56);
            }));
            var id = p.Id;
            b.Pressed += () => { pop.Hide(); DropOn(card, id, null); };
            b.TooltipText = $"Пригодность к месту: {Fit(p, kind)}/99" + (mf >= 0 ? $"\nСыгранность с партнёрами: {mf * 100:0}%" : "");
            v.AddChild(b);
        }
        if (!special && kind != "G" && card.Id != 0 && C.Data.PlayerById.TryGetValue(card.Id, out var who))
            v.AddChild(Ui.Margin(OrderBox(who), 6, 8, 6, 4));
        AddChild(pop);
        pop.PopupHide += () => { pop.QueueFree(); if (_sel != null && IsInstanceValid(_sel)) { _sel.Selected = false; _sel.QueueRedraw(); } _sel = null; };
        var gp = card.GetScreenPosition();
        var vs = GetViewportRect().Size;
        int x = (int)Math.Min(gp.X + CW + 8, vs.X - 350);
        pop.Popup(new Rect2I(x, (int)Math.Clamp(gp.Y - 20, 10, vs.Y - 520), 346, 10));
    }

    double MateFamFor(LineCard card, int id)
    {
        if (!card.Unit.StartsWith("F") || card.Kind == "G") return -1;
        int i = card.Unit[1] - '0';
        var mates = card.Kind == "D" ? (i < L.D.Count ? L.D[i] : Array.Empty<int>()) : L.F[i];
        var others = mates.Where(m => m != 0 && m != card.Id && m != id).ToList();
        return others.Count == 0 ? -1 : others.Average(m => C.Fam(id, m));
    }
}

/// <summary>Карточка игрока в звене: фото в кольце рейтинга, имя, сильная сторона, роль, свежесть. Перетаскивается.</summary>
public partial class LineCard : Control
{
    readonly Career C;
    readonly LinesView _v;
    public readonly string Unit, Kind;
    public readonly int Slot, Id;
    public bool Selected;
    bool _hover, _drop;
    public LineCard() { }
    public LineCard(Career c, LinesView v, string unit, int slot, int id, string kind)
    {
        C = c; _v = v; Unit = unit; Slot = slot; Id = id; Kind = kind;
        CustomMinimumSize = Size = new Vector2(188, 112);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; _drop = false; QueueRedraw(); };
        var p = id != 0 ? C.Data.PlayerById.GetValueOrDefault(id) : null;
        if (p != null) TooltipText = $"{p.NameRu} · {Ds.PosShort(p.Pos)}" + (p.Roles.Count > 0 ? $"\nЛучшая роль: {p.Roles[0].Name}" : "") +
            "\nЩелчок — кандидаты на место, перетащить — поменять местами, двойной щелчок — профиль";
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.DoubleClick) _v.CardOpen(this);
            else _v.CardClicked(this);
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 at)
    {
        if (Id == 0) return default;
        var prev = new LineCard(C, _v, Unit, Slot, Id, Kind) { Modulate = new Color(1, 1, 1, 0.85f), Scale = new Vector2(0.9f, 0.9f), Position = -at * 0.9f };
        var holder = new Control(); holder.AddChild(prev);
        SetDragPreview(holder);
        return this;
    }

    public override bool _CanDropData(Vector2 at, Variant data)
    {
        var o = data.AsGodotObject();
        int id = o is LineCard lc ? lc.Id : o is BenchChip bc ? bc.Id : 0;
        bool ok = id != 0 && o != this && !(o is LineCard l2 && l2.Unit.StartsWith("P") != Unit.StartsWith("P"));
        if (ok != _drop) { _drop = ok; QueueRedraw(); }
        return ok;
    }

    public override void _DropData(Vector2 at, Variant data)
    {
        _drop = false;
        var o = data.AsGodotObject();
        if (o is LineCard lc) _v.DropOn(this, lc.Id, lc);
        else if (o is BenchChip bc) _v.DropOn(this, bc.Id, null);
    }

    public override void _Notification(int what) { if (what == NotificationDragEnd && _drop) { _drop = false; QueueRedraw(); } }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        var p = Id != 0 ? C.Data.PlayerById.GetValueOrDefault(Id) : null;
        var my = C.Team(C.UserTeam);
        var sb = new StyleBoxFlat { BgColor = new Color(1, 1, 1, _hover || _drop ? 0.085f : 0.05f), BorderColor = Selected || _drop ? Ds.ClubBright(my).Lightened(0.2f) : Ds.Edge };
        sb.SetCornerRadiusAll(18); sb.SetBorderWidthAll(Selected || _drop ? 2 : 1);
        if (Selected) { sb.ShadowColor = new Color(Ds.ClubBright(my), 0.35f); sb.ShadowSize = 14; }
        DrawStyleBox(sb, new Rect2(0, 0, w, h));
        if (p == null)
        {
            DrawString(Ui.Regular, new Vector2(0, h / 2 + 5), "свободно — перетащите игрока", HorizontalAlignment.Center, w, 12, Ui.Dim);
            return;
        }
        bool inj = C.Injured.Contains(Id);
        var t = C.Team(p.Team);
        int ovr = Ds.Ovr(C, p);
        var c = new Vector2(42, 40);
        Face.Player(this, p, t, new Rect2(c.X - 23, c.Y - 23, 46, 46), Face.Circle(c, 23));
        Face.Ring(this, c, 28, ovr, 3);
        var parts = p.NameRu.Split(' ', 2);
        string last = parts.Length > 1 ? parts[1] : parts[0];
        float tx = 84, tw = w - tx - 10;
        Txt(Ui.Regular, 11, tx, 12, parts.Length > 1 ? Paint.Trim(Ui.Regular, parts[0], 11, tw) : "", Ui.Muted);
        int ls = Paint.Fit(Ui.Semi, last, 16, tw, 12);
        Txt(Ui.Semi, ls, tx, 27, Paint.Trim(Ui.Semi, last, ls, tw), inj ? Ui.Loss : Ui.Text);
        // сильная сторона (для вратаря — лучшая вратарская)
        var keys = p.IsGoalie ? new[] { "rfx", "gpos", "reb", "scr" } : new[] { "fin", "pwr", "net", "pas", "hnd", "fo", "dfn", "tka", "blk", "hit", "spd", "vis", "pp", "pk" };
        var best = keys.Select(k => (k, v: PlayerModel.Visible(C, p, k))).OrderByDescending(x => x.v).First();
        string an = Ds.AttrShort(best.k);
        int shown = Ds.ShowI(best.v);
        DrawCircle(new Vector2(tx + 3, 60), 3, Ds.Attr(shown));
        float aw = Txt(Ui.Regular, 11, tx + 11, 53, Paint.Trim(Ui.Regular, an, 11, tw - 32), Ui.Muted);
        Txt(Ui.Semi, 11, tx + 15 + aw, 53, shown.ToString(), Ds.Attr(shown));
        // место и роль или предупреждение
        string off = Kind switch
        {
            "C" or "W" when p.IsDefense => "защитник в нападении",
            "C" when p.Pos != "C" => "не центр",
            "D" when !p.IsDefense => "нападающий в защите",
            _ => null,
        };
        string ord = Unit.StartsWith("F") && C.Lineups[C.UserTeam].Orders?.TryGetValue(Id, out var ok) == true ? Orders.Get(ok)?.Name : null;
        string line = inj ? "травма" : off ?? ord ?? $"{Ds.PosShort(p.Pos)}  ·  {(p.Roles.Count > 0 ? p.Roles[0].Name : "")}";
        Txt(Ui.Regular, 11, 12, 80, Paint.Trim(Ui.Regular, line, 11, w - 24), inj ? Ui.Loss : off != null ? Ui.Otl : ord != null ? Ui.Gold : Ui.Muted);
        // свежесть — тонкая линия по низу
        float f = PlayerModel.State(C, Id)?.Fresh ?? 100;
        DrawLine(new Vector2(14, h - 9), new Vector2(w - 14, h - 9), new Color(1, 1, 1, 0.07f), 2);
        DrawLine(new Vector2(14, h - 9), new Vector2(14 + (w - 28) * f / 100f, h - 9), Ds.FreshCol(f), 2);
    }

    float Txt(Font f, int size, float x, float top, string s, Color c)
    {
        DrawString(f, new Vector2(x, top + f.GetAscent(size)), s, HorizontalAlignment.Left, -1, size, c);
        return f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
    }
}

/// <summary>Игрок запаса (или лазарета): лицо, фамилия, рейтинг. Перетаскивается на карточку в составе.</summary>
public partial class BenchChip : Control
{
    readonly Career C;
    readonly PlayerData _p;
    public int Id => _p?.Id ?? 0;
    bool _hover;
    public BenchChip() { }
    public BenchChip(Career c, LinesView v, PlayerData p)
    {
        C = c; _p = p;
        CustomMinimumSize = new Vector2(176, 44);
        MouseFilter = MouseFilterEnum.Stop;
        bool inj = c.Injured.Contains(p.Id);
        MouseDefaultCursorShape = inj ? CursorShape.Arrow : CursorShape.Drag;
        TooltipText = inj ? $"{p.NameRu}: травма" : $"{p.NameRu} · {Ds.PosShort(p.Pos)}\nПеретащите на карточку в составе. Двойной щелчок — профиль";
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left, DoubleClick: true }) Shell.I?.Go("player", null, _p.Id.ToString());
    }

    public override Variant _GetDragData(Vector2 at)
    {
        if (C.Injured.Contains(_p.Id)) return default;
        var prev = new BenchChip(C, null, _p) { Modulate = new Color(1, 1, 1, 0.85f), Position = -at };
        var holder = new Control(); holder.AddChild(prev);
        SetDragPreview(holder);
        return this;
    }

    public override void _Draw()
    {
        bool inj = C.Injured.Contains(_p.Id);
        var sb = Ds.Pill(new Color(1, 1, 1, _hover ? 0.08f : 0.04f), 0, 0, Ds.Edge);
        DrawStyleBox(sb, new Rect2(Vector2.Zero, Size));
        int ovr = Ds.Ovr(C, _p);
        var c = new Vector2(22, 22);
        Face.Player(this, _p, C.Team(_p.Team), new Rect2(c.X - 16, c.Y - 16, 32, 32), Face.Circle(c, 16));
        DrawArc(c, 18, 0, Mathf.Tau, 40, new Color(Ds.Tier(ovr), 0.85f), 2, true);
        DrawString(Ui.Semi, new Vector2(46, 20), Paint.Trim(Ui.Semi, _p.LastRu, 13, 96), HorizontalAlignment.Left, -1, 13, inj ? Ui.Loss : Ui.Text);
        DrawString(Ui.Regular, new Vector2(46, 35), inj ? "травма" : Ds.PosShort(_p.Pos), HorizontalAlignment.Left, -1, 11, inj ? Ui.Loss : Ui.Muted);
        DrawString(Ds.DisplayLight, new Vector2(Size.X - 46, 28), ovr.ToString(), HorizontalAlignment.Right, 34, 14, Ds.Tier(ovr));
    }
}
