using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Письмо как журнальная статья: рубрика, крупный заголовок, подводка, плашка цифр, текст с подзаголовками,
/// цитата и герои материала. Узкая колонка текста (до ~75 знаков в строке) — читается, а не просматривается.
/// </summary>
public static class Article
{
    public static Color TagColor(string tag) => tag switch
    {
        "Матч" => Ui.Blue,
        "Лазарет" => Ui.Red,
        "Вечер в НХЛ" or "Неделя в НХЛ" or "Лига" or "Разговор" or "Пресс-конференция" or "Раздевалка" or "Погоня" => Ui.Gold,
        "Превью" or "Рейтинг силы" => Ui.Blue,
        "Цифры" or "Неделя" => Ui.Win,
        _ => Ui.Muted,
    };

    /// <summary>Аватар журналиста: кружок в цвете издания с инициалами.</summary>
    public static Control Avatar(Journalist j, int size = 28)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(size, size), TooltipText = $"{j.Name}, {j.Outlet} — {j.Bio}" };
        var col = new Color(j.Color);
        var sb = new StyleBoxFlat { BgColor = col.Lerp(Ui.Panel, 0.25f), BorderColor = col.Lightened(0.3f) };
        sb.SetCornerRadiusAll(size / 2); sb.SetBorderWidthAll(1);
        p.AddThemeStyleboxOverride("panel", sb);
        p.AddChild(Ui.L(Voices.Initials(j.Name), Math.Max(9, size * 2 / 5), Ui.ReadableOn(col), Ui.CondBold, HorizontalAlignment.Center).Also(l => l.VerticalAlignment = VerticalAlignment.Center));
        return p;
    }

    /// <summary>Подпись материала: аватар и имя автора с изданием (или просто «от кого»), дата.</summary>
    public static Control Byline(Career c, Message m, int size = 12)
    {
        var j = Voices.ByLine(c, m.From);
        if (j == null || j.Kind == Voices.WIRE) return Ui.L($"{m.From} · {Ui.Date(m.Date, false)}", size, Ui.Dim);
        return Ui.H(8, Avatar(j, size * 2 + 2), Ui.V(-3, Ui.L(j.Name, size + 1, Ui.Text, Ui.Semi), Ui.L($"{j.Outlet} · {Ui.Date(m.Date, false)}", size - 1, Ui.Dim)));
    }

    /// <summary>Рубрика: цветная метка и название.</summary>
    public static Control Tag(string tag, int size = 13)
    {
        var c = TagColor(tag);
        var h = Ui.H(6, new ColorRect { Color = c, CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }, Ui.L(tag ?? "Письмо", size, c, Ui.Cond));
        h.MouseFilter = Control.MouseFilterEnum.Ignore;
        return h;
    }

    public static Control Make(Career c, Message m, Action onAnswer = null)
    {
        var col = Ui.V(14);
        col.CustomMinimumSize = new Vector2(0, 0);

        // шапка: полоса в цветах клуба героя
        if (m.Team != null && c.Data.TeamByAbbr.TryGetValue(m.Team, out var t))
        {
            var c1 = new Color(t.Colors[0]); var c2 = new Color(t.Colors.Count > 1 ? t.Colors[1] : t.Colors[0]);
            var band = new PanelContainer();
            var grad = new Gradient();
            grad.SetColor(0, c1.Lerp(Ui.Panel, 0.15f)); grad.SetColor(1, c1.Lerp(Ui.Panel, 0.92f));
            var st = new StyleBoxTexture { Texture = new GradientTexture2D { Gradient = grad, Width = 256, Height = 4 } };
            st.ContentMarginLeft = 18; st.ContentMarginRight = 18; st.ContentMarginTop = 12; st.ContentMarginBottom = 12;
            band.AddThemeStyleboxOverride("panel", st);
            var fg = Ui.ReadableOn(c1.Lerp(Ui.Panel, 0.15f));
            var jj = Voices.ByLine(c, m.From);
            var bh = Ui.H(12, new TeamBadge(t, 44), Ui.Expand(Ui.V(-2, Ui.L(t.Name, 22, fg, Ui.CondBold), Ui.L(m.From + " · " + Ui.Date(m.Date), 12, new Color(fg, 0.75f)))));
            if (jj != null && jj.Kind != Voices.WIRE) bh.AddChild(Avatar(jj, 40).Also(x => x.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter));
            band.AddChild(bh);
            col.AddChild(Ui.V(0, band, new ColorRect { Color = c2, CustomMinimumSize = new Vector2(0, 3) }));
        }
        else col.AddChild(Ui.H(10, Byline(c, m, 13), Ui.Spacer()));

        var text = Ui.V(12);
        text.AddChild(Tag(m.Tag, 15));
        text.AddChild(Wrap(Ui.L(m.Subject, 38, Ui.Text, Ui.CondBold)));
        if (!string.IsNullOrEmpty(m.Lead)) text.AddChild(Wrap(Ui.L(m.Lead, 17, new Color(Ui.Text, 0.86f), Ui.Medium)));
        col.AddChild(Narrow(text));

        if (m.Facts is { Count: > 0 })
        {
            var facts = Ui.H(8);
            foreach (var f in m.Facts) facts.AddChild(Fact(f));
            col.AddChild(facts);
        }

        if (!string.IsNullOrEmpty(m.Body)) col.AddChild(Narrow(Body(m.Body)));

        if (!string.IsNullOrEmpty(m.Quote)) col.AddChild(Narrow(Quote(m.Quote, m.QuoteBy)));

        if (m.Players is { Count: > 0 })
        {
            var ps = Ui.H(8);
            foreach (var id in m.Players.Take(4))
                if (c.Data.PlayerById.TryGetValue(id, out var p)) ps.AddChild(PlayerChip(c, p));
            col.AddChild(Ui.V(6, Ui.L(m.Tag == "Матч" ? "Звёзды матча" : "Герои материала", 16, Ui.Muted, Ui.Cond), ps));
        }

        if (m.Options != null) col.AddChild(Narrow(Choices(c, m, onAnswer)));

        if (!string.IsNullOrEmpty(m.Link) && !m.Link.StartsWith("press:") && m.Options == null)
        {
            string label = m.Link.Split(':')[0] switch
            {
                "game" => "Открыть протокол матча", "player" => "Карточка игрока", "table" => "Открыть таблицу", "playoffs" => "Сетка плей-офф",
                "club" => "Карточка клуба", _ => "Открыть",
            };
            var link = m.Link;
            col.AddChild(Ui.H(0, Ui.B(label, () => P.OpenLink(link), true, 14)));
        }
        return col;
    }

    /// <summary>Варианты ответа: кнопка, под ней — чем обернётся; после ответа — выбранный вариант и что было дальше.</summary>
    static Control Choices(Career c, Message m, Action onAnswer)
    {
        var v = Ui.V(8);
        if (m.Pending)
        {
            var head = Ui.H(8, Ui.Icon("message-circle", 18, Ui.Gold), Ui.L(m.Required ? "Нужно решение" : "Ваш ответ", 20, Ui.Gold, Ui.CondBold));
            if (m.Due.HasValue)
            {
                int d = m.Due.Value.DayNumber - c.Today.DayNumber;
                head.AddChild(Ui.L(d <= 0 ? "ответить сегодня" : $"срок — {Ui.DateShort(m.Due.Value)}", 12, Ui.Muted));
            }
            v.AddChild(head);
            foreach (var o in m.Options)
            {
                var b = new Button { FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 58) };
                var st = Ui.Box(Ui.Panel2, 3, Ui.Line, 14);
                b.AddThemeStyleboxOverride("normal", st);
                b.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Panel2.Lightened(0.06f), 3, Ui.Blue, 14));
                b.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Blue.Darkened(0.45f), 3, Ui.Blue, 14));
                var inner = Ui.V(0, Ui.L(o.Label, 16, Ui.Text, Ui.Semi), Ui.L(o.Hint ?? "", 12, Ui.Muted).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }));
                inner.MouseFilter = Control.MouseFilterEnum.Ignore;
                foreach (var ch in inner.GetChildren()) if (ch is Control cc) cc.MouseFilter = Control.MouseFilterEnum.Ignore;
                var mm = Ui.Margin(inner, 14, 8, 14, 8);
                mm.MouseFilter = Control.MouseFilterEnum.Ignore;
                mm.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                b.AddChild(mm);
                b.CustomMinimumSize = new Vector2(0, string.IsNullOrEmpty(o.Hint) ? 44 : o.Hint.Length > 90 ? 76 : 60);
                var id = o.Id;
                b.Pressed += () => { Decisions.Answer(c, m, id); Main.I.Save(); onAnswer?.Invoke(); };
                v.AddChild(b);
            }
            if (!m.Required) v.AddChild(Ui.L("Можно не отвечать сразу: после срока помощник решит сам, по умолчанию.", 11, Ui.Dim));
        }
        else
        {
            var chosen = m.Options.FirstOrDefault(o => o.Id == m.Choice);
            var p = new PanelContainer();
            var sb = new StyleBoxFlat { BgColor = new Color(Ui.Blue, 0.08f), BorderColor = Ui.Blue, BorderWidthLeft = 3 };
            sb.ContentMarginLeft = 16; sb.ContentMarginRight = 16; sb.ContentMarginTop = 10; sb.ContentMarginBottom = 10;
            p.AddThemeStyleboxOverride("panel", sb);
            p.AddChild(Ui.V(4, Ui.L("Ваш ответ: " + (chosen?.Label ?? "—"), 15, Ui.Text, Ui.Semi),
                Wrap(Ui.L(m.Reply ?? "", 14, Ui.Muted))));
            v.AddChild(p);
        }
        return v;
    }

    static Label Wrap(Label l)
    {
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return l;
    }

    /// <summary>Колонка текста не шире ~780 пикселей: длинные строки на широком экране не читаются.</summary>
    static Control Narrow(Control c)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        var inner = new MarginContainer { CustomMinimumSize = new Vector2(0, 0) };
        inner.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        inner.SizeFlagsStretchRatio = 1;
        inner.AddChild(c);
        box.AddChild(inner);
        var rest = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.25f, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddChild(rest);
        return box;
    }

    static Control Fact(MsgFact f)
    {
        var p = new PanelContainer();
        var sb = Ui.Box(new Color(0, 0, 0, 0.22f), 3, null, 12);
        sb.BorderColor = Ui.Line; sb.BorderWidthTop = 2;
        sb.ContentMarginTop = 8; sb.ContentMarginBottom = 8;
        p.AddThemeStyleboxOverride("panel", sb);
        var col = f.Tone == "good" ? Ui.Win : f.Tone == "bad" ? Ui.Loss : Ui.Text;
        var v = Ui.V(-2, Ui.L(f.Label, 11, Ui.Muted), Ui.L(f.Value, 26, col, Ui.CondBold));
        if (!string.IsNullOrEmpty(f.Sub)) v.AddChild(Ui.L(f.Sub, 11, Ui.Dim).Also(l => { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }));
        p.AddChild(v);
        p.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return p;
    }

    /// <summary>Текст: абзацы через пустую строку, «## » — подзаголовок, «• » — пункт списка.</summary>
    public static Control Body(string body)
    {
        var v = Ui.V(8);
        foreach (var line in body.Replace("\r", "").Split('\n'))
        {
            var s = line.TrimEnd();
            if (s.Length == 0) { v.AddChild(Ui.Spacer(false, 2)); continue; }
            if (s.StartsWith("## ")) { v.AddChild(Ui.Spacer(false, 4)); v.AddChild(Ui.L(s[3..], 21, Ui.Text, Ui.CondBold)); continue; }
            if (s.StartsWith("• "))
            {
                var dot = new ColorRect { Color = Ui.Blue, CustomMinimumSize = new Vector2(6, 6), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
                var h = Ui.H(10, Ui.Margin(dot, 0, 8, 0, 0), Wrap(Ui.L(s[2..], 15, Ui.Text)));
                v.AddChild(h);
                continue;
            }
            var l = Wrap(Ui.L(s, 15, Ui.Text));
            l.AddThemeConstantOverride("line_spacing", 4);
            v.AddChild(l);
        }
        return v;
    }

    static Control Quote(string q, string by)
    {
        var p = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = new Color(Ui.Gold, 0.06f), BorderColor = Ui.Gold, BorderWidthLeft = 3 };
        sb.ContentMarginLeft = 18; sb.ContentMarginRight = 18; sb.ContentMarginTop = 12; sb.ContentMarginBottom = 12;
        p.AddThemeStyleboxOverride("panel", sb);
        var v = Ui.V(6, Wrap(Ui.L($"«{q}»", 22, Ui.Text, Ui.Cond)));
        if (!string.IsNullOrEmpty(by)) v.AddChild(Ui.L("— " + by, 13, Ui.Muted));
        p.AddChild(v);
        return p;
    }

    public static Control PlayerChip(Career c, PlayerData p)
    {
        var t = c.Team(p.Team);
        var b = new PanelContainer { MouseDefaultCursorShape = Control.CursorShape.PointingHand, MouseFilter = Control.MouseFilterEnum.Stop };
        var sb = Ui.Box(new Color(1, 1, 1, 0.05f), 14, Ds.Edge, 10);
        sb.ContentMarginTop = sb.ContentMarginBottom = 8;
        b.AddThemeStyleboxOverride("panel", sb);
        string line;
        if (p.IsGoalie)
        {
            var g = c.Goalies.GetValueOrDefault(p.Id);
            line = g == null ? Ui.PosRu(p.Pos) : $"{g.Gp} И · {(g.Sa > 0 ? g.SvPct.ToString(".000", System.Globalization.CultureInfo.InvariantCulture) : "—")}";
        }
        else
        {
            var s = c.Skaters.GetValueOrDefault(p.Id);
            line = s == null ? Ui.PosRu(p.Pos) : $"{s.Gp} И · {s.G}+{s.A}={s.Pts}";
        }
        var h = Ui.H(10, new FaceDot(c, p, 40), Ui.V(-2, Ui.L(p.NameRu, 14, Ui.Text, Ui.Semi), Ui.L($"{Ui.PosRu(p.Pos)} · {t.City} · {line}", 11, Ui.Muted)));
        h.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var ch in h.GetChildren()) if (ch is Control cc) cc.MouseFilter = Control.MouseFilterEnum.Ignore;
        b.AddChild(h);
        b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        b.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Shell.I?.Go("player", null, p.Id.ToString()); };
        return b;
    }
}
