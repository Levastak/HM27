using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Корень игры: база, карьера, переключение экранов, сохранение.</summary>
public partial class Main : Control
{
    public const string Version = "0.4.0";
    const string SavePath = "user://career.json";

    public static Main I { get; private set; }
    public GameData Data { get; private set; }
    public Career Career { get; private set; }

    Control _screen;

    public override void _Ready()
    {
        I = this;
        GetWindow().Title = "HM27 — Hockey Manager 27";
        LoadSettings();
        Theme = Ui.MakeTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new LogoCache());
        // фон: лёд под светом арены — чуть светлее сверху слева
        var grad = new Gradient();
        grad.SetColor(0, new Color("10203A")); grad.SetColor(1, new Color("07101D"));
        var bg = new TextureRect { Texture = new GradientTexture2D { Gradient = grad, Width = 256, Height = 256, FillFrom = new Vector2(0, 0), FillTo = new Vector2(1, 1) },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Возврат из 3Д-арены: карьера уже в памяти
        if (Match3D.RinkPreview.PendingCareer != null)
        {
            var c = Match3D.RinkPreview.PendingCareer;
            Match3D.RinkPreview.PendingCareer = null;
            Data = c.Data;
            Resume(c);
            return;
        }
        Data = GameData.Parse(FileAccess.GetFileAsString("res://data/hm27-data.json"));

        // Параметры для автоматических снимков экранов: -- --screen=hub --team=MIN --tab=lines --shot=путь
        var args = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--") && a.Contains('=')) args[a[2..a.IndexOf('=')]] = a[(a.IndexOf('=') + 1)..];
        if (args.TryGetValue("smoke", out var smoke)) { Smoke(int.Parse(smoke), args.GetValueOrDefault("smoke-out")); return; }
        if (args.TryGetValue("screen", out var scr)) { DebugStart(scr, args); return; }

        ShowMenu();
    }

    /// <summary>
    /// Самопроверка готовой сборки (CI на настоящих Mac и Windows): база, карьера, N игровых дней,
    /// матч клуба игрока, сохранение и загрузка, построение экранов. Итог — в консоль и в файл.
    /// </summary>
    void Smoke(int days, string outPath)
    {
        string result;
        try
        {
            // иконки (звёзды, меню) читаются из SVG в сборке: без них звёзды КА/ПА пустые
            foreach (var ic in new[] { "star", "star-half", "house", "trophy" })
                if (Icons.Get(ic) == null) throw new System.Exception($"нет иконки {ic} в сборке");
            var c = Career.New(Data, "MIN", 27);
            Career = c;
            for (int i = 0; i < days; i++) c.SimDay();
            c.SimToUserGame();
            var g = c.UserGameToday();
            if (g != null) { c.Play(g); c.SimDay(); }
            Save();
            LoadCareer();
            foreach (var tab in new[] { "overview", "lines", "calendar", "table", "stats", "playoffs", "squad", "inbox", "club", "medical" }) ShowHub(tab, tab == "club" ? "MIN" : null);
            ShowHub("player", Data.Roster("MIN").First(p => !p.IsGoalie).Id.ToString());
            ShowHub("player", Data.Roster("MIN").First(p => p.IsGoalie).Id.ToString());
            var last = Career.LastGame("MIN");
            if (last != null) { ShowMatch(last); }
            ShowClubSelect();
            ShowMenu();
            var row = Career.Table["MIN"];
            result = $"SMOKE OK: {OS.GetName()} {Engine.GetArchitectureName()}, {Data.Players.Count} игроков, {Ui.Date(Career.Today)}, MIN {row.W}-{row.L}-{row.Otl}, сохранение {FileAccess.GetFileAsBytes(SavePath).Length / 1024} КБ (без сжатия {ReadSave().Length / 1024} КБ)";
        }
        catch (Exception e)
        {
            result = $"SMOKE FAIL: {e}";
        }
        GD.Print(result);
        if (!string.IsNullOrEmpty(outPath)) System.IO.File.WriteAllText(outPath, result);
        GetTree().Quit(result.StartsWith("SMOKE OK") ? 0 : 1);
    }

    void DebugStart(string screen, System.Collections.Generic.Dictionary<string, string> args)
    {
        var team = args.GetValueOrDefault("team", "MIN");
        if (screen != "menu" && screen != "clubs")
        {
            Career = Career.New(Data, team, 27);
            ApplyAccent();
            int days = int.Parse(args.GetValueOrDefault("days", "0"));
            for (int i = 0; i < days; i++) Career.SimDay();
            if (args.ContainsKey("sim")) Career.SimToEnd();
        }
        switch (screen)
        {
            case "menu": ShowMenu(); break;
            case "clubs": ShowClubSelect(); break;
            case "match":
                Career.SimToUserGame();
                var g = Career.UserGameToday();
                ShowMatch(g);
                if (args.ContainsKey("played")) (_screen as MatchScreen)?.PlayInstant();
                if (args.ContainsKey("live")) (_screen as MatchScreen)?.StartLive(double.Parse(args["live"]));
                break;
            default:
                if (args.TryGetValue("read", out var rd) && rd == "all") foreach (var m in Career.Messages) m.Read = true;
                ShowHub(args.GetValueOrDefault("tab", "overview"), args.GetValueOrDefault("arg"));
                break;
        }
        if (args.TryGetValue("popup", out var popup) && popup == "assistant")
            GetTree().CreateTimer(0.3).Timeout += () => AssistantReview.Open(_screen, Career, Career.UserTeam, l => { });
        if (args.TryGetValue("open", out var open))
            GetTree().CreateTimer(0.3).Timeout += () => (_screen as Shell)?.DebugOpen(open);
        if (args.TryGetValue("shot", out var shot)) Shooter.Arm(this, shot, int.Parse(args.GetValueOrDefault("frames", "12")));
    }

    void Show(Control c)
    {
        if (_screen != null) { RemoveChild(_screen); _screen.QueueFree(); }
        _screen = c;
        c.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(c);
    }

    public void ApplyAccent()
    {
        if (Career == null) return;
        var t = Data.TeamByAbbr[Career.UserTeam];
        var c = new Color(t.Colors[0]);
        // слишком тёмный основной цвет (чёрный, тёмно-синий) — берём второй
        if (c.Luminance < 0.12f && t.Colors.Count > 1 && new Color(t.Colors[1]).Luminance > 0.12f) c = new Color(t.Colors[1]);
        if (c.Luminance < 0.12f) c = c.Lightened(0.35f);
        Ui.Club = c;
        Theme = Ui.MakeTheme();
    }

    public void ShowMenu() => Show(new MenuScreen());
    public void ShowClubSelect() => Show(new ClubSelectScreen());
    /// <summary>Экраны карьеры — в FM-оболочке. Старые имена вкладок переводятся в разделы оболочки.</summary>
    public void ShowHub(string tab = "overview", string arg = null)
    {
        string forced = null;
        if (tab.Contains(':')) { forced = tab[(tab.IndexOf(':') + 1)..]; tab = tab[..tab.IndexOf(':')]; }
        var (page, sub) = tab switch
        {
            "lines" => ("lines", (string)null), "calendar" => ("schedule", null), "table" => ("comp", "table"), "stats" => ("comp", "stats"),
            "playoffs" => ("comp", "playoffs"), "squad" => ("squad", null), "inbox" => ("inbox", null), "player" => ("player", null), "club" => ("club", null), "medical" => ("medical", null), "room" => ("room", null), "tactics" => ("tactics", "system"),
            _ => ("home", null),
        };
        sub = forced ?? sub;
        if (_screen is Shell sh && sh.IsInsideTree()) sh.Go(page, sub, arg);
        else Show(new Shell(page, sub, arg));
    }
    public void ShowMatch(ScheduledGame g) => Show(new MatchScreen(g));

    public void ShowArena(ScheduledGame g)
    {
        Match3D.RinkPreview.Home = g.Home; Match3D.RinkPreview.Away = g.Away;
        Match3D.RinkPreview.ReturnTo = () => { GetTree().ChangeSceneToFile("res://ui/Main.tscn"); };
        Match3D.RinkPreview.PendingCareer = Career;
        GetTree().ChangeSceneToFile("res://Match3D/RinkPreview.tscn");
    }

    public void NewCareer(string team)
    {
        Career = Career.New(Data, team, (int)(Time.GetUnixTimeFromSystem() % 1_000_000));
        ApplyAccent();
        Save();
        ShowHub();
    }

    public bool HasSave => FileAccess.FileExists(SavePath);

    /// <summary>Клуб и дата сохранённой карьеры — для главного меню, без разбора всего файла.</summary>
    public (string team, DateOnly? date) SaveInfo()
    {
        try
        {
            var head = ReadSave();
            var t = System.Text.RegularExpressions.Regex.Match(head, "\"UserTeam\"\\s*:\\s*\"(\\w+)\"");
            var d = System.Text.RegularExpressions.Regex.Match(head, "\"Today\"\\s*:\\s*\"([0-9-]+)\"");
            return (t.Success ? t.Groups[1].Value : null, d.Success ? DateOnly.Parse(d.Groups[1].Value) : null);
        }
        catch { return (null, null); }
    }

    public void LoadCareer()
    {
        try
        {
            Career = Career.FromJson(ReadSave(), Data);
            ApplyAccent();
            ShowHub();
        }
        catch (Exception e)
        {
            GD.PushError($"Сохранение не читается: {e.Message}");
            ShowMenu();
        }
    }

    /// <summary>После возврата из 3Д-арены карьера уже в памяти.</summary>
    public void Resume(Career c)
    {
        Career = c;
        ApplyAccent();
        ShowHub();
    }

    public void Save()
    {
        if (Career == null) return;
        // сохранение сжато gzip: JSON карьеры за сезон — несколько мегабайт, сжатый — в 8–10 раз меньше
        var raw = System.Text.Encoding.UTF8.GetBytes(Career.ToJson());
        using var ms = new System.IO.MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        f?.StoreBuffer(ms.ToArray());
    }

    /// <summary>Текст сохранения: сжатое (gzip) или старое несжатое.</summary>
    static string ReadSave()
    {
        var bytes = FileAccess.GetFileAsBytes(SavePath);
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var gz = new System.IO.Compression.GZipStream(new System.IO.MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress);
            using var outMs = new System.IO.MemoryStream();
            gz.CopyTo(outMs);
            return System.Text.Encoding.UTF8.GetString(outMs.ToArray());
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
    // ------------------------------------------------------------------ настройки
    const string SettingsPath = "user://settings.cfg";

    /// <summary>Настройки игры: язык имён игроков и клубов (по умолчанию — английский, как в НХЛ).</summary>
    public static void LoadSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) == Error.Ok)
        {
            Names.English = (bool)cfg.GetValue("names", "english", true);
            StarScale.Relative = (bool)cfg.GetValue("stars", "relative", true);
        }
    }

    public static void SaveSettings()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("names", "english", Names.English);
        cfg.SetValue("stars", "relative", StarScale.Relative);
        cfg.Save(SettingsPath);
    }
}

public static class NodeExt
{
    public static T Also<T>(this T node, Action<T> act) { act(node); return node; }

}

/// <summary>Снимок окна через N кадров и выход — для проверки интерфейса без экрана.</summary>
public partial class Shooter : Node
{
    string _path; int _frames;
    public static void Arm(Node root, string path, int frames) => root.AddChild(new Shooter { _path = path, _frames = frames });
    public override void _Process(double delta)
    {
        if (--_frames > 0) return;
        GetViewport().GetTexture().GetImage().SavePng(_path);
        GD.Print($"Кадр сохранён: {_path}");
        GetTree().Quit();
    }

}
