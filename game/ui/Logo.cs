using Godot;

namespace HM27.UI;

/// <summary>
/// Знак HM27: «HM» узким жирным шрифтом и «27» янтарными цифрами в окошке табло.
/// Рисуется кодом, поэтому одинаково чёткий в шапке (26 пикс.) и в главном меню (120 пикс.).
/// </summary>
public static class Logo
{
    public static Control Make(int size, bool tagline = false)
    {
        var h = Ui.H(System.Math.Max(2, size / 10));
        h.MouseFilter = Control.MouseFilterEnum.Ignore;
        var hm = Ui.L("HM", size, Ui.Text, Ui.CondBlack);
        hm.AddThemeConstantOverride("line_spacing", 0);
        h.AddChild(hm);

        // окошко табло: тёмная плашка, янтарные цифры со свечением
        var board = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var sb = new StyleBoxFlat { BgColor = new Color("040A14"), BorderColor = new Color(Ui.Gold, 0.55f) };
        sb.SetBorderWidthAll(System.Math.Max(1, size / 26));
        sb.SetCornerRadiusAll(System.Math.Max(2, size / 14));
        sb.ContentMarginLeft = sb.ContentMarginRight = System.Math.Max(3, size / 7);
        sb.ContentMarginTop = sb.ContentMarginBottom = 0;
        board.AddThemeStyleboxOverride("panel", sb);
        var num = Ui.L("27", (int)(size * 0.86f), Ui.Gold, Ui.CondBlack);
        num.AddThemeColorOverride("font_shadow_color", new Color(Ui.Gold, 0.45f));
        num.AddThemeConstantOverride("shadow_outline_size", System.Math.Max(2, size / 9));
        num.AddThemeConstantOverride("shadow_offset_x", 0);
        num.AddThemeConstantOverride("shadow_offset_y", 0);
        board.AddChild(num);
        board.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(board);
        if (!tagline) return h;

        var v = Ui.V(System.Math.Max(0, size / 30));
        v.MouseFilter = Control.MouseFilterEnum.Ignore;
        v.AddChild(h);
        v.AddChild(Ui.L("Hockey Manager", System.Math.Max(12, size / 5), Ui.Muted, Ui.Cond));
        return v;
    }
}
