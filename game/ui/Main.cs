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
            LoadSlot(CurrentSlot);
            foreach (var tab in new[] { "overview", "lines", "calendar", "table", "stats", "playoffs", "squad", "inbox", "club", "medical", "stands", "races" }) ShowHub(tab, tab == "club" ? "MIN" : null);
            ShowHub("player", Data.Roster("MIN").First(p => !p.IsGoalie).Id.ToString());
            ShowHub("player", Data.Roster("MIN").First(p => p.IsGoalie).Id.ToString());
            var last = Career.LastGame("MIN");
            if (last != null) { ShowMatch(last); }
            ShowClubSelect();
            ShowMenu();
            var row = Career.Table["MIN"];
            result = $"SMOKE OK: {OS.GetName()} {Engine.GetArchitectureName()}, {Data.Players.Count} игроков, {Ui.Date(Career.Today)}, MIN {row.W}-{row.L}-{row.Otl}, сохранение {CurrentSaveBytes / 1024} КБ (без сжатия {ReadCurrent().Length / 1024} КБ), карьер: {Saves().Count}";
            DeleteSlot(CurrentSlot);   // проверочная карьера не остаётся в списке
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
        if (screen != "menu" && screen != "clubs" && screen != "saves")
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
            case "saves": ShowSaves(); break;
            case "lab": Show(new Lab.DesignLab(Career, args.GetValueOrDefault("dir", "A") == "A", args.GetValueOrDefault("tab", "lines"))); break;
            case "match":
                Career.SimToUserGame();
                var g = Career.UserGameToday();
                ShowMatch(g);
                if (args.ContainsKey("played")) (_screen as MatchScreen)?.PlayInstant();
                if (args.ContainsKey("live")) (_screen as MatchScreen)?.StartLive(double.Parse(args["live"]));
                if (args.ContainsKey("goal")) GetTree().CreateTimer(0.2).Timeout += () => (_screen as MatchScreen)?.DebugGoal();
                if (args.TryGetValue("coach", out var ct)) GetTree().CreateTimer(0.4).Timeout += () => (_screen as MatchScreen)?.DebugCoach(ct);
                if (args.ContainsKey("card")) GetTree().CreateTimer(0.6).Timeout += () => PlayerCard.Show(_screen, Data.Roster(Career.UserTeam).First(p => !p.IsGoalie), Career);
                break;
            default:
                if (args.TryGetValue("lines", out var lm)) LinesView.Mode = lm;
                if (args.TryGetValue("read", out var rd) && rd == "all") foreach (var m in Career.Messages) m.Read = true;
                ShowHub(args.GetValueOrDefault("tab", "overview"), args.GetValueOrDefault("arg"));
                break;
        }
        if (args.TryGetValue("popup", out var popup) && popup == "assistant")
            GetTree().CreateTimer(0.3).Timeout += () => AssistantReview.Open(_screen, Career, Career.UserTeam, l => { });
        if (args.TryGetValue("search", out var sq))
            GetTree().CreateTimer(0.3).Timeout += () => Shell.I?.DebugSearch(sq);
        if (args.TryGetValue("open", out var open))
            GetTree().CreateTimer(0.3).Timeout += () => (_screen as Shell)?.DebugOpen(open);
        if (args.TryGetValue("shot", out var shot)) Shooter.Arm(this, shot, int.Parse(args.GetValueOrDefault("frames", "12")));
    }

    void Show(Control c)
    {
        // эмблемы и фото растрированы крупно — мелкие копии берутся из мип-карт, иначе на 16–24 px они рябят и мылятся
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
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
            "playoffs" => ("comp", "playoffs"), "squad" => ("squad", null), "inbox" => ("inbox", null), "player" => ("player", null), "club" => ("club", null), "medical" => ("medical", null), "room" => ("room", null), "tactics" => ("tactics", "system"), "training" => ("training", "week"), "stands" => ("stands", null), "races" => ("races", null),
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

    // ------------------------------------------------------------------ сохранения
    // Несколько карьер: user://saves/<слот>.hm27 (JSON карьеры, gzip) и <слот>.meta.json (название, клуб, дата в игре, время сохранения).
    // Текущий слот перезаписывается после каждого дня (автосохранение); «Сохранить как» — новый слот с новым названием.
    const string SaveDir = "user://saves";
    const string OldSavePath = "user://career.json";
    public string CurrentSlot { get; private set; }

    public sealed class SaveMeta
    {
        public string Slot { get; set; }
        public string Name { get; set; }
        public string Team { get; set; }
        public string Date { get; set; }
        public string Record { get; set; }
        public string SavedAt { get; set; }
        public string Version { get; set; }
    }

    static string DataPath(string slot) => $"{SaveDir}/{slot}.hm27";
    static string MetaPath(string slot) => $"{SaveDir}/{slot}.meta.json";
    static string NewSlotId() => "c" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");

    public void NewCareer(string team, string name = null)
    {
        Career = Career.New(Data, team, (int)(Time.GetUnixTimeFromSystem() % 1_000_000));
        ApplyAccent();
        CurrentSlot = NewSlotId();
        _slotName = string.IsNullOrWhiteSpace(name) ? DefaultName(team) : name.Trim();
        Save();
        ShowHub();
    }

    string _slotName;
    public string CurrentName => _slotName;
    public string DefaultName(string team) => Data.TeamByAbbr.TryGetValue(team, out var t) ? $"{t.Name} 2026-27" : team;

    public bool HasSave { get { Migrate(); return Saves().Count > 0; } }

    /// <summary>Все карьеры, свежие сверху.</summary>
    public List<SaveMeta> Saves()
    {
        var res = new List<SaveMeta>();
        if (!DirAccess.DirExistsAbsolute(SaveDir)) return res;
        foreach (var f in DirAccess.GetFilesAt(SaveDir))
        {
            if (!f.EndsWith(".meta.json")) continue;
            try
            {
                var m = System.Text.Json.JsonSerializer.Deserialize<SaveMeta>(FileAccess.GetFileAsString($"{SaveDir}/{f}"));
                if (m != null && FileAccess.FileExists(DataPath(m.Slot))) res.Add(m);
            }
            catch { }
        }
        return res.OrderByDescending(m => m.SavedAt).ToList();
    }

    /// <summary>Старая единственная карьера (user://career.json) становится первым слотом.</summary>
    void Migrate()
    {
        if (!FileAccess.FileExists(OldSavePath)) return;
        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        string slot = NewSlotId();
        var bytes = FileAccess.GetFileAsBytes(OldSavePath);
        using (var f = FileAccess.Open(DataPath(slot), FileAccess.ModeFlags.Write)) f?.StoreBuffer(bytes);
        var text = Unzip(bytes);
        var t = System.Text.RegularExpressions.Regex.Match(text, "\"UserTeam\"\\s*:\\s*\"(\\w+)\"");
        var d = System.Text.RegularExpressions.Regex.Match(text, "\"Today\"\\s*:\\s*\"([0-9-]+)\"");
        string team = t.Success ? t.Groups[1].Value : "";
        WriteMeta(new SaveMeta { Slot = slot, Name = DefaultName(team), Team = team, Date = d.Success ? d.Groups[1].Value : "", SavedAt = DateTime.Now.ToString("s"), Version = Version });
        DirAccess.RemoveAbsolute(OldSavePath);
    }

    static void WriteMeta(SaveMeta m)
    {
        using var f = FileAccess.Open(MetaPath(m.Slot), FileAccess.ModeFlags.Write);
        f?.StoreString(System.Text.Json.JsonSerializer.Serialize(m, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>Последняя по времени карьера — для «Продолжить».</summary>
    public SaveMeta Latest() => Saves().FirstOrDefault();

    /// <summary>Клуб и дата последней карьеры — для главного меню.</summary>
    public (string team, DateOnly? date) SaveInfo()
    {
        var m = Latest();
        return m == null ? (null, null) : (m.Team, DateOnly.TryParse(m.Date, out var d) ? d : null);
    }

    public void LoadCareer() { var m = Latest(); if (m != null) LoadSlot(m.Slot); }

    public void LoadSlot(string slot)
    {
        try
        {
            var meta = Saves().FirstOrDefault(m => m.Slot == slot);
            Career = Career.FromJson(Unzip(FileAccess.GetFileAsBytes(DataPath(slot))), Data);
            CurrentSlot = slot;
            _slotName = meta?.Name ?? DefaultName(Career.UserTeam);
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
        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        CurrentSlot ??= NewSlotId();
        _slotName ??= DefaultName(Career.UserTeam);
        // сохранение сжато gzip: JSON карьеры за сезон — несколько мегабайт, сжатый — в 8–10 раз меньше
        var raw = System.Text.Encoding.UTF8.GetBytes(Career.ToJson());
        using var ms = new System.IO.MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
        using (var f = FileAccess.Open(DataPath(CurrentSlot), FileAccess.ModeFlags.Write)) f?.StoreBuffer(ms.ToArray());
        var row = Career.Table.GetValueOrDefault(Career.UserTeam);
        WriteMeta(new SaveMeta { Slot = CurrentSlot, Name = _slotName, Team = Career.UserTeam, Date = Career.Today.ToString("yyyy-MM-dd"),
            Record = row == null || row.Gp == 0 ? "" : $"{row.W}-{row.L}-{row.Otl}", SavedAt = DateTime.Now.ToString("s"), Version = Version });
    }

    /// <summary>«Сохранить как»: копия карьеры под новым названием; дальше автосохранение идёт в неё.</summary>
    public void SaveAs(string name)
    {
        if (Career == null) return;
        CurrentSlot = NewSlotId();
        _slotName = string.IsNullOrWhiteSpace(name) ? DefaultName(Career.UserTeam) : name.Trim();
        Save();
    }

    public void RenameSlot(string slot, string name)
    {
        var m = Saves().FirstOrDefault(x => x.Slot == slot);
        if (m == null || string.IsNullOrWhiteSpace(name)) return;
        m.Name = name.Trim();
        WriteMeta(m);
        if (slot == CurrentSlot) _slotName = m.Name;
    }

    public void DeleteSlot(string slot)
    {
        DirAccess.RemoveAbsolute(DataPath(slot));
        DirAccess.RemoveAbsolute(MetaPath(slot));
        if (slot == CurrentSlot) { CurrentSlot = null; _slotName = null; }
    }

    public void ShowSaves() => Show(new SavesScreen());

    /// <summary>Размер файла текущего сохранения (для smoke).</summary>
    public long CurrentSaveBytes => CurrentSlot == null ? 0 : FileAccess.GetFileAsBytes(DataPath(CurrentSlot)).Length;
    string ReadCurrent() => Unzip(FileAccess.GetFileAsBytes(DataPath(CurrentSlot)));

    /// <summary>Текст сохранения: сжатое (gzip) или старое несжатое.</summary>
    static string Unzip(byte[] bytes)
    {
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
