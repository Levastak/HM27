using System;
using Godot;
using HM27.Core;
using HM27.Match3D;

namespace HM27.UI;

/// <summary>Главное меню поверх живой 3Д-арены: камера медленно облетает площадку.</summary>
public partial class MenuScreen : Control
{
    Camera3D _cam;
    float _t;

    public override void _Ready()
    {
        // 3Д-фон
        var svc = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(0.78f, 0.82f, 0.9f) };
        svc.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(svc);
        var vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X, HandleInputLocally = false };
        svc.AddChild(vp);
        var world = new Node3D();
        vp.AddChild(world);
        ArenaBuilder.Build(world, new Color("154734"), new Color("A6192E"), new Color("C8102E"));
        RinkPreview.PlaceFaceoff(world, RinkPreview.KitOf(Main.I.Data, "MIN", true), RinkPreview.KitOf(Main.I.Data, "WSH", false), null, null);
        _cam = new Camera3D { Fov = 42, Current = true };
        world.AddChild(_cam);

        // затемнение слева под меню: лёд уходит в арену
        var shade = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
        var grad = new Gradient();
        grad.SetColor(0, new Color(Ui.Bg, 0.97f)); grad.SetColor(1, new Color(Ui.Bg, 0.0f));
        grad.SetOffset(0, 0.34f); grad.SetOffset(1, 0.86f);
        shade.Texture = new GradientTexture2D { Gradient = grad, Width = 512, Height = 8 };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(shade);
        // снизу — тёмная полоса под подписью
        var low = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
        var g2 = new Gradient();
        g2.SetColor(0, new Color(Ui.Bg, 0f)); g2.SetColor(1, new Color(Ui.Bg, 0.9f));
        low.Texture = new GradientTexture2D { Gradient = g2, Width = 8, Height = 256, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1) };
        low.SetAnchorsPreset(LayoutPreset.BottomWide); low.OffsetTop = -160;
        AddChild(low);

        var main = Main.I;
        var col = Ui.V(0);
        col.AddChild(Logo.Make(112, true));
        col.AddChild(Ui.Spacer(false, 44));
        if (main.HasSave)
        {
            var (team, date) = main.SaveInfo();
            string sub = team != null && main.Data.TeamByAbbr.TryGetValue(team, out var t)
                ? $"{t.Name}{(date.HasValue ? ", " + Ui.Date(date.Value, false) : "")}" : null;
            col.AddChild(Item("Продолжить карьеру", sub, main.LoadCareer, true));
        }
        col.AddChild(Item("Новая карьера", main.HasSave ? null : "Выберите клуб НХЛ сезона 2026-27", main.ShowClubSelect, !main.HasSave));
        col.AddChild(Item("Об игре", null, ShowAbout, false));
        col.AddChild(Item("Выход", null, () => GetTree().Quit(), false));
        col.AddChild(Ui.Spacer(false, 28));

        // настройки — тихо, под меню
        col.AddChild(Ui.L("Настройки", 14, Ui.Dim, Ui.Semi));
        col.AddChild(Ui.Spacer(false, 4));
        Button names = null;
        names = Toggle(NamesText(), () => { Names.English = !Names.English; Main.SaveSettings(); names.Text = NamesText(); },
            "Имена игроков и названия клубов: как в НХЛ (по-английски) или по-русски. Остальной текст — по-русски.");
        col.AddChild(names);
        Button stars = null;
        stars = Toggle(StarsText(), () => { StarScale.Relative = !StarScale.Relative; Main.SaveSettings(); stars.Text = StarsText(); },
            "Звёзды способностей и потенциала: относительно вашего клуба (как в FM — 5★ у лучшего в своей группе) или по шкале лиги.");
        col.AddChild(stars);

        var m = Ui.Margin(col, 96, 0, 0, 0);
        m.SetAnchorsPreset(LayoutPreset.LeftWide);
        m.OffsetRight = 700;
        col.Alignment = BoxContainer.AlignmentMode.Center;
        AddChild(m);

        var foot = Ui.V(2, Ui.L($"Версия {Main.Version} · данные НХЛ на {Ui.Date(DateOnly.Parse(main.Data.DataDate), false)}", 12, Ui.Dim),
            Ui.L("Неофициальная фанатская игра. Не связана с НХЛ и NHLPA.", 12, Ui.Dim));
        var fm = Ui.Margin(foot, 96, 0, 40, 26);
        fm.SetAnchorsPreset(LayoutPreset.BottomWide);
        fm.GrowVertical = GrowDirection.Begin;
        AddChild(fm);
    }

    /// <summary>Пункт меню: крупный узкий шрифт без рамки; при наведении — синяя линия слева и белый текст.</summary>
    static Control Item(string text, string sub, Action a, bool main)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        var st = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = main ? Ui.Gold : new Color(0, 0, 0, 0), BorderWidthLeft = 3 };
        st.SetCornerRadiusAll(14); st.ContentMarginLeft = 18;
        var hov = (StyleBoxFlat)st.Duplicate(); hov.BorderColor = main ? Ui.Gold : new Color(1, 1, 1, 0.5f); hov.BgColor = new Color(1, 1, 1, 0.07f);
        b.AddThemeStyleboxOverride("normal", st); b.AddThemeStyleboxOverride("hover", hov); b.AddThemeStyleboxOverride("pressed", hov);
        var title = Ui.L(text, main ? 30 : 26, main ? Ui.Text : new Color(Ui.Text, 0.72f), Ds.Display);
        var v = Ui.V(-4, title);
        if (sub != null) v.AddChild(Ui.L(sub, 14, Ui.Muted));
        v.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var ch in v.GetChildren()) if (ch is Control cc) cc.MouseFilter = MouseFilterEnum.Ignore;
        var mm = Ui.Margin(v, 18, 4, 24, 6);
        mm.MouseFilter = MouseFilterEnum.Ignore;
        mm.SetAnchorsPreset(LayoutPreset.FullRect);
        b.AddChild(mm);
        b.CustomMinimumSize = new Vector2(500, sub != null ? 78 : 58);
        b.MouseEntered += () => title.AddThemeColorOverride("font_color", Colors.White);
        b.MouseExited += () => title.AddThemeColorOverride("font_color", main ? Ui.Text : new Color(Ui.Text, 0.72f));
        b.Pressed += a;
        return b;
    }

    static Button Toggle(string text, Action a, string tip)
    {
        var b = new Button { Text = text, Flat = true, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = tip,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin, Alignment = HorizontalAlignment.Left };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.AddThemeFontOverride("font", Ui.Medium);
        b.AddThemeColorOverride("font_color", Ui.Muted);
        b.AddThemeColorOverride("font_hover_color", Ui.Blue);
        var e = new StyleBoxEmpty { ContentMarginLeft = 0, ContentMarginTop = 2, ContentMarginBottom = 2 };
        b.AddThemeStyleboxOverride("normal", e); b.AddThemeStyleboxOverride("hover", e); b.AddThemeStyleboxOverride("pressed", e);
        b.Pressed += a;
        return b;
    }

    static string StarsText() => StarScale.Relative ? "Звёзды: относительно клуба" : "Звёзды: по шкале лиги";
    static string NamesText() => Names.English ? "Имена: English" : "Имена: по-русски";

    void ShowAbout()
    {
        var d = Main.I.Data;
        var txt = "HM27 — менеджер НХЛ с честным движком матча.\n\n" +
                  "Матч играется посекундно по сменам: звенья и пары по вашим минутам, спецбригады, усталость, броски из зон, вбрасывания, удаления. " +
                  "Характеристики игроков выведены из протоколов НХЛ: ожидаемые голы (xG), вклад на льду (RAPM), игровое время, сейвы сверх ожидаемого. " +
                  "Калибровка — по сезонам НХЛ 2022–2026.\n\n" +
                  $"Источники данных: API НХЛ, MoneyPuck.com (данные для некоммерческого использования), DailyFaceoff (звенья). База собрана {d.Built}.\n\n" +
                  "Неофициальная бесплатная фанатская игра. Не связана с НХЛ, клубами и NHLPA. Названия и эмблемы — собственность их владельцев.\n\n" +
                  "Шрифты Inter и Fira Sans — SIL Open Font License. Иконки Lucide — ISC. Движок Godot — MIT.";
        var dlg = new AcceptDialog { Title = "Об игре", DialogText = txt, OkButtonText = "Закрыть", DialogAutowrap = true, Size = new Vector2I(640, 420) };
        AddChild(dlg);
        dlg.PopupCentered();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta * 0.06f;
        float r = 40f;
        _cam.Position = new Vector3(MathF.Cos(_t) * r, 13f + MathF.Sin(_t * 0.7f) * 2f, MathF.Sin(_t) * r * 0.7f);
        _cam.LookAt(new Vector3(MathF.Cos(_t) * 4f, 0, 0), Vector3.Up);
    }
}
