using System;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Карьеры: все сохранения — загрузить, переименовать, удалить (с подтверждением). Свежие сверху.</summary>
public partial class SavesScreen : Control
{
    VBoxContainer _list;
    string _confirm;   // слот, который ждёт подтверждения удаления
    string _rename;    // слот, который сейчас переименовывают

    public override void _Ready()
    {
        var latest = Main.I.Latest();
        var glow = latest != null && Main.I.Data.TeamByAbbr.TryGetValue(latest.Team ?? "", out var lt) ? Ds.ClubBright(lt) : Ui.Blue;
        AddChild(new IceBg(glow));
        var root = Ui.V(18);
        var m = Ui.Margin(root, 48, 32, 48, 32);
        m.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(m);
        root.AddChild(Ui.H(14, Ui.B("←", () => { if (Main.I.Career != null && Main.I.CurrentSlot != null) Main.I.ShowHub(); else Main.I.ShowMenu(); }),
            Ui.V(4, Ui.L("Карьеры", 30, Ui.Text, Ds.Display), Ui.L("Карьера сохраняется сама после каждого дня. «Сохранить как» в игре делает копию под новым названием.", 14, Ui.Muted))));
        _list = Ui.V(12);
        root.AddChild(Ui.Expand(Ui.Scroll(_list), true, true));
        Fill();
    }

    void Fill()
    {
        Ui.Clear(_list);
        var saves = Main.I.Saves();
        if (saves.Count == 0)
        {
            _list.AddChild(Ui.L("Сохранённых карьер пока нет — начните новую в главном меню.", 15, Ui.Muted));
            return;
        }
        foreach (var s in saves) _list.AddChild(Row(s));
    }

    Control Row(Main.SaveMeta s)
    {
        var d = Main.I.Data;
        d.TeamByAbbr.TryGetValue(s.Team ?? "", out var t);
        bool current = s.Slot == Main.I.CurrentSlot;
        var card = new PanelContainer();
        var sb = Ui.Box(current ? new Color(Ds.ClubBright(t ?? d.Teams[0]), 0.14f) : Ui.Panel, 18, current ? new Color(Ds.ClubBright(t ?? d.Teams[0]), 0.6f) : Ds.Edge, 16);
        card.AddThemeStyleboxOverride("panel", sb);
        var h = Ui.H(18);
        card.AddChild(h);
        if (t != null)
            h.AddChild(new Paint(new Vector2(72, 72), q => { Face.Glow(q, q.Size / 2, 46, Ds.ClubBright(t), 0.035f); Face.Logo(q, t, new Rect2(6, 6, 60, 60)); }));
        var info = Ui.Expand(Ui.V(4));
        if (_rename == s.Slot)
        {
            var edit = new LineEdit { Text = s.Name, CustomMinimumSize = new Vector2(360, 38), SelectAllOnFocus = true };
            void Apply() { Main.I.RenameSlot(s.Slot, edit.Text); _rename = null; Fill(); }
            edit.TextSubmitted += _ => Apply();
            info.AddChild(Ui.H(8, edit, Ui.B("Готово", Apply, size: 13), Ui.B("Отмена", () => { _rename = null; Fill(); }, size: 13)));
            edit.CallDeferred(Control.MethodName.GrabFocus);
        }
        else info.AddChild(Ui.H(10, Ui.L(s.Name, 18, Ui.Text, Ui.Semi), current ? Ui.L("сейчас", 12, Ui.Gold, Ui.Semi) : new Control()));
        string date = DateOnly.TryParse(s.Date, out var gd) ? Ui.Date(gd, false) + $" {gd.Year}" : "";
        info.AddChild(Ui.L($"{t?.Name ?? s.Team}  ·  в игре {date}{(string.IsNullOrEmpty(s.Record) ? "" : $"  ·  {s.Record}")}", 13, Ui.Muted));
        string saved = DateTime.TryParse(s.SavedAt, out var sd) ? $"сохранено {sd.Day} {Ui.MonthsGen[sd.Month - 1]}, {sd:HH:mm}" : "";
        info.AddChild(Ui.L(saved + (s.Version != null && s.Version != Main.Version ? $"  ·  версия {s.Version}" : ""), 12, Ui.Dim));
        h.AddChild(info);
        if (_confirm == s.Slot)
        {
            h.AddChild(Ui.L("Удалить карьеру насовсем?", 13, Ui.Loss, Ui.Semi));
            var yes = Ui.B("Удалить", () => { Main.I.DeleteSlot(s.Slot); _confirm = null; Fill(); }, size: 13);
            yes.AddThemeColorOverride("font_color", Ui.Loss);
            h.AddChild(yes);
            h.AddChild(Ui.B("Отмена", () => { _confirm = null; Fill(); }, size: 13));
        }
        else
        {
            h.AddChild(Ui.B("Переименовать", () => { _rename = s.Slot; _confirm = null; Fill(); }, size: 13));
            h.AddChild(Ui.B("Удалить", () => { _confirm = s.Slot; _rename = null; Fill(); }, size: 13));
            var load = Ui.B("Загрузить", () => Main.I.LoadSlot(s.Slot), true, 14);
            load.CustomMinimumSize = new Vector2(140, 42);
            h.AddChild(load);
        }
        foreach (var c in h.GetChildren().OfType<Control>()) c.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return card;
    }
}
