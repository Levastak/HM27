using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HM27.Core;

namespace HM27.Match3D;

/// <summary>
/// Арена в 3Д (этап 4а, витрина): площадка, трибуны в цветах хозяев, расстановка на вбрасывание
/// с реальными номерами из составов. Камеры: трансляционная, у льда, угловая, свободная (мышь).
/// Запуск без игры: -- --shot=путь.png --cam=broadcast|faceoff|corner
/// </summary>
public partial class RinkPreview : Node3D
{
    public static string Home = "MIN", Away = "WSH";
    public static Career PendingCareer;
    public static Action ReturnTo;

    string _shot;
    string _cam = "broadcast";
    int _frame;
    Camera3D _camera;
    float _yaw = 0.0f, _pitch = 0.42f, _dist = 34f;
    bool _free, _drag;

    public static Kit KitOf(GameData data, string abbr, bool home)
    {
        var t = data?.TeamByAbbr.GetValueOrDefault(abbr);
        var c1 = new Color(t?.Colors.ElementAtOrDefault(0) ?? "#154734");
        var c2 = new Color(t?.Colors.ElementAtOrDefault(1) ?? "#A6192E");
        var c3 = new Color(t?.Colors.ElementAtOrDefault(2) ?? "#EAAA00");
        // Хозяева — тёмная форма, гости — белая (как в НХЛ)
        return home
            ? new Kit(c1, c2.Luminance > 0.85f ? c3 : c2, c1.Darkened(0.15f), c1, c1.Luminance > 0.6f ? new Color("111111") : (c3.Luminance > 0.5f ? c3 : Colors.White))
            : new Kit(new Color("F4F4F4"), c1, c1.Luminance > 0.8f ? c2 : c1, c1.Luminance > 0.8f ? c2 : c1, c1.Luminance > 0.8f ? c2 : c1);
    }

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot=")) _shot = a[7..];
            if (a.StartsWith("--cam=")) _cam = a[6..];
        }
        var data = PendingCareer?.Data;
        if (data == null && FileAccess.FileExists("res://data/hm27-data.json"))
            data = GameData.Parse(FileAccess.GetFileAsString("res://data/hm27-data.json"));

        var home = data?.TeamByAbbr.GetValueOrDefault(Home);
        var away = data?.TeamByAbbr.GetValueOrDefault(Away);
        var fanAway = new Color(away?.Colors.ElementAtOrDefault(0) ?? "#C8102E");
        ArenaBuilder.Build(this, new Color(home?.Colors.ElementAtOrDefault(0) ?? "#154734"), new Color(home?.Colors.ElementAtOrDefault(1) ?? "#A6192E"), fanAway);

        Lineup lh = null, la = null;
        if (data != null)
        {
            var inj = new System.Collections.Generic.HashSet<int>(data.Teams.SelectMany(t => t.Injured));
            lh = PendingCareer?.Lineups.GetValueOrDefault(Home) ?? (home != null ? Lineup.FromTeam(data, home, inj) : null);
            la = PendingCareer?.Lineups.GetValueOrDefault(Away) ?? (away != null ? Lineup.FromTeam(data, away, inj) : null);
        }
        PlaceFaceoff(this, KitOf(data, Home, true), KitOf(data, Away, false), Numbers(data, lh), Numbers(data, la));

        _camera = new Camera3D { Current = true, Near = 0.1f, Far = 400f };
        AddChild(_camera);
        SetCamera(_cam);
        if (_shot == null) AddOverlay(home, away);
    }

    /// <summary>Номера первой пятёрки и вратаря: [ЛН, Ц, ПН, ЛЗ, ПЗ, В].</summary>
    static int[] Numbers(GameData data, Lineup l)
    {
        if (data == null || l == null) return null;
        int N(int id) => id != 0 && data.PlayerById.TryGetValue(id, out var p) ? p.Num ?? 0 : 0;
        return new[] { N(l.F[0][0]), N(l.F[0][1]), N(l.F[0][2]), N(l.D[0][0]), N(l.D[0][1]), N(l.G1) };
    }

    void AddOverlay(TeamData home, TeamData away)
    {
        var ui = new CanvasLayer();
        AddChild(ui);
        var root = new Control { Theme = HM27.UI.Ui.MakeTheme(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(root);
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 10);
        bar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        bar.OffsetLeft = 20; bar.OffsetTop = 16; bar.OffsetRight = -20;
        root.AddChild(bar);
        var title = HM27.UI.Ui.L($"{home?.Name ?? Home} — {away?.Name ?? Away}", 22, null, HM27.UI.Ui.Bold);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.AddChild(title);
        foreach (var (name, key) in new[] { ("Трансляция", "broadcast"), ("У льда", "faceoff"), ("Угол", "corner"), ("Свободная", "free") })
            bar.AddChild(HM27.UI.Ui.B(name, () => SetCamera(key)));
        bar.AddChild(HM27.UI.Ui.B("← Назад", () => { if (ReturnTo != null) ReturnTo(); else GetTree().Quit(); }, true));
        var hint = HM27.UI.Ui.L("Арена — витрина 3Д-движка (этап 4а). Свободная камера: тяните мышью, колесо — приближение. Матчи в 3Д — в следующих версиях.", 14, HM27.UI.Ui.Muted);
        hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        hint.OffsetLeft = 20; hint.OffsetTop = -40;
        root.AddChild(hint);
    }

    void SetCamera(string kind)
    {
        _free = kind == "free";
        switch (kind)
        {
            case "faceoff":
                _camera.Fov = 34f;
                _camera.LookAtFromPosition(new Vector3(-6.5f, 3.2f, 9.5f), new Vector3(0, 0.9f, 0), Vector3.Up);
                break;
            case "corner":
                _camera.Fov = 50f;
                _camera.LookAtFromPosition(new Vector3(-36f, 14f, 24f), new Vector3(-4f, 0, 0), Vector3.Up);
                break;
            case "free":
                _camera.Fov = 50f;
                UpdateFree();
                break;
            default:
                _camera.Fov = 50f;
                _camera.LookAtFromPosition(new Vector3(0, 17f, 27f), new Vector3(0, -0.5f, 1.5f), Vector3.Up);
                break;
        }
    }

    void UpdateFree()
    {
        var pos = new Vector3(MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), MathF.Cos(_yaw) * MathF.Cos(_pitch)) * _dist;
        _camera.LookAtFromPosition(pos, Vector3.Zero, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && k.Keycode == Key.Escape) { ReturnTo?.Invoke(); return; }
        if (!_free) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _drag = mb.Pressed;
            if (mb.ButtonIndex == MouseButton.WheelUp) _dist = Math.Max(8f, _dist * 0.92f);
            if (mb.ButtonIndex == MouseButton.WheelDown) _dist = Math.Min(70f, _dist * 1.08f);
            UpdateFree();
        }
        if (e is InputEventMouseMotion mm && _drag)
        {
            _yaw -= mm.Relative.X * 0.006f;
            _pitch = Math.Clamp(_pitch + mm.Relative.Y * 0.004f, 0.06f, 1.45f);
            UpdateFree();
        }
    }

    /// <summary>Расстановка на вбрасывание в центре: хозяева защищают левые ворота. Номера: [ЛН, Ц, ПН, ЛЗ, ПЗ, В].</summary>
    public static void PlaceFaceoff(Node3D root, Kit home, Kit away, int[] hn, int[] an)
    {
        hn ??= new[] { 14, 97, 12, 43, 46, 30 };
        an ??= new[] { 8, 17, 43, 74, 6, 79 };
        int Nz(int[] a, int i) => a[i] != 0 ? a[i] : i + 2;
        void Put(Node3D n, float x, float z, float faceX, float faceZ)
        {
            n.Position = new Vector3(x, 0, z);
            n.RotationDegrees = new Vector3(0, Mathf.RadToDeg(MathF.Atan2(faceX, faceZ)), 0);
            root.AddChild(n);
        }
        Put(PlayerFigure.Skater(home, Nz(hn, 1), 1.84f), -0.75f, 0f, 1, 0);
        Put(PlayerFigure.Skater(away, Nz(an, 1), 1.88f, false), 0.75f, 0f, -1, 0);
        Put(PlayerFigure.Skater(home, Nz(hn, 0), 1.80f), -1.4f, -4.4f, 1, 0.2f);
        Put(PlayerFigure.Skater(home, Nz(hn, 2), 1.86f, false), -1.4f, 4.4f, 1, -0.2f);
        Put(PlayerFigure.Skater(away, Nz(an, 0), 1.90f), 1.4f, 4.4f, -1, -0.2f);
        Put(PlayerFigure.Skater(away, Nz(an, 2), 1.85f, false), 1.4f, -4.4f, -1, 0.2f);
        Put(PlayerFigure.Skater(home, Nz(hn, 3), 1.93f), -8.5f, -3.2f, 1, 0);
        Put(PlayerFigure.Skater(home, Nz(hn, 4), 1.88f, false), -8.5f, 3.2f, 1, 0);
        Put(PlayerFigure.Skater(away, Nz(an, 3), 1.91f), 8.5f, 3.2f, -1, 0);
        Put(PlayerFigure.Skater(away, Nz(an, 4), 1.95f, false), 8.5f, -3.2f, -1, 0);
        Put(PlayerFigure.Goalie(home, Nz(hn, 5)), -RinkSpec.GoalLineX + 0.9f, 0f, 1, 0);
        Put(PlayerFigure.Goalie(away, Nz(an, 5)), RinkSpec.GoalLineX - 0.9f, 0f, -1, 0);
        Put(PlayerFigure.Referee(), 0f, 0.9f, 0, -1);
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.038f, BottomRadius = 0.038f, Height = 0.0254f, RadialSegments = 24 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.03f, 0.03f, 0.03f), Roughness = 0.5f },
            Position = new Vector3(0, 1.2f, 0.45f),
        });
    }

    public override void _Process(double delta)
    {
        if (_shot == null) return;
        if (++_frame == 6)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shot);
            GD.Print($"Кадр сохранён: {_shot}");
            GetTree().Quit();
        }
    }
}
