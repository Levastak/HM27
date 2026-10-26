using System;
using Godot;
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
        var svc = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
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

        // Затемнение слева под меню
        var shade = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
        var grad = new Gradient();
        grad.SetColor(0, new Color(Ui.Bg, 0.96f)); grad.SetColor(1, new Color(Ui.Bg, 0.0f));
        grad.SetOffset(0, 0.32f); grad.SetOffset(1, 0.75f);
        shade.Texture = new GradientTexture2D { Gradient = grad, Width = 512, Height = 8 };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var col = Ui.V(14);
        col.AddChild(Ui.L("HM27", 120, Ui.Text, Ui.Black));
        col.AddChild(Ui.L("HOCKEY MANAGER 27", 22, Ui.Muted, Ui.Semi));
        col.AddChild(Ui.Spacer(false, 26));
        var main = Main.I;
        if (main.HasSave)
            col.AddChild(MenuButton("Продолжить карьеру", main.LoadCareer, true));
        col.AddChild(MenuButton("Новая карьера", main.ShowClubSelect, !main.HasSave));
        col.AddChild(MenuButton("Об игре", ShowAbout, false));
        col.AddChild(MenuButton("Выход", () => GetTree().Quit(), false));
        col.AddChild(Ui.Spacer(false, 30));
        col.AddChild(Ui.L($"Версия {Main.Version} · сезон 2026-27 · данные на {main.Data.DataDate}", 13, Ui.Dim));
        col.AddChild(Ui.L("Неофициальная фанатская игра. Не связана с НХЛ и NHLPA.", 13, Ui.Dim));

        var m = Ui.Margin(col, 90, 0, 0, 0);
        m.SetAnchorsPreset(LayoutPreset.LeftWide);
        m.OffsetRight = 640;
        col.Alignment = BoxContainer.AlignmentMode.Center;
        AddChild(m);
    }

    static Button MenuButton(string text, Action a, bool primary)
    {
        var b = Ui.B(text, a, primary, 18);
        b.CustomMinimumSize = new Vector2(340, 52);
        b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        b.Alignment = HorizontalAlignment.Left;
        return b;
    }

    void ShowAbout()
    {
        var d = Main.I.Data;
        var txt = "HM27 — менеджер НХЛ с честным движком матча.\n\n" +
                  "Исход каждого матча считает модель прогнозов NHL-STATS.RU (версия 4.4), проверенная на тысячах реальных матчей. " +
                  "Рейтинги игроков выводятся из настоящей статистики: ожидаемые голы (xG), игровое время, сейвы сверх ожидаемого.\n\n" +
                  $"Источники данных: API НХЛ, MoneyPuck.com (данные для некоммерческого использования), DailyFaceoff (звенья). База собрана {d.Built}.\n\n" +
                  "Неофициальная бесплатная фанатская игра. Не связана с НХЛ, клубами и NHLPA. Названия и эмблемы — собственность их владельцев.\n\n" +
                  "Шрифт Inter — SIL Open Font License. Движок Godot — MIT.";
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
