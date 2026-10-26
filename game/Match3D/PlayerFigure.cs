using Godot;

namespace HM27.Match3D;

/// <summary>Цвета формы клуба.</summary>
public readonly record struct Kit(Color Jersey, Color Trim, Color Pants, Color Helmet, Color Number);

/// <summary>
/// Стилизованная фигура хоккеиста для этапа 4а: из примитивов, в форме клуба, с номером на спине.
/// На этапе 4б заменяется анимированной моделью в экипировке.
/// </summary>
public partial class PlayerFigure : Node3D
{
    public static PlayerFigure Skater(Kit kit, int number, float heightM = 1.85f, bool leftShot = true)
    {
        var p = new PlayerFigure { Name = $"Skater{number}" };
        float s = heightM / 1.85f;
        p.Scale = new Vector3(s, s, s);

        var jersey = Mat(kit.Jersey, 0.75f);
        var trim = Mat(kit.Trim, 0.7f);
        var pants = Mat(kit.Pants, 0.65f);
        var black = Mat(new Color(0.05f, 0.05f, 0.06f), 0.4f);
        var helmet = Mat(kit.Helmet, 0.25f, 0.2f);

        // Коньки
        p.Add(new BoxMesh { Size = new Vector3(0.11f, 0.12f, 0.30f) }, black, new Vector3(-0.12f, 0.06f, 0.02f));
        p.Add(new BoxMesh { Size = new Vector3(0.11f, 0.12f, 0.30f) }, black, new Vector3(0.12f, 0.06f, 0.02f));
        // Гетры (цвет формы) — ноги слегка согнуты: корпус наклонён вперёд
        p.Add(Cap(0.075f, 0.52f), jersey, new Vector3(-0.12f, 0.36f, 0f));
        p.Add(Cap(0.075f, 0.52f), jersey, new Vector3(0.12f, 0.36f, 0f));
        p.Add(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.04f }, trim, new Vector3(-0.12f, 0.42f, 0f));
        p.Add(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.04f }, trim, new Vector3(0.12f, 0.42f, 0f));
        // Шорты
        var shorts = p.Add(Cap(0.2f, 0.42f), pants, new Vector3(0, 0.78f, 0f));
        shorts.Scale = new Vector3(1.25f, 1f, 0.95f);

        // Корпус с наклоном вперёд (поворот вокруг X на +20° — верх уходит к +Z, лицом вперёд)
        var torso = new Node3D { Position = new Vector3(0, 0.92f, 0), RotationDegrees = new Vector3(24, 0, 0) };
        p.AddChild(torso);
        var body = Part(torso, Cap(0.24f, 0.72f), jersey, new Vector3(0, 0.32f, 0));
        body.Scale = new Vector3(1.32f, 1f, 0.92f);
        Part(torso, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 0.07f }, trim, new Vector3(0, 0.08f, 0)).Scale = new Vector3(1.3f, 1, 0.92f);
        // Шлем и визор
        Part(torso, new SphereMesh { Radius = 0.125f, Height = 0.24f }, helmet, new Vector3(0, 0.82f, 0.03f));
        Part(torso, new BoxMesh { Size = new Vector3(0.2f, 0.06f, 0.02f) }, Mat(new Color(0.6f, 0.7f, 0.8f, 0.5f), 0.05f), new Vector3(0, 0.80f, 0.15f));

        // Руки и клюшка: точки в системе игрока. Верхняя рука у бедра, нижняя ниже и дальше, крюк на льду.
        float hand = leftShot ? -1f : 1f;               // сторона крюка
        var tb = torso.Transform;
        Vector3 shL = tb * new Vector3(-0.31f, 0.56f, 0f), shR = tb * new Vector3(0.31f, 0.56f, 0f);
        Vector3 top = new(-hand * 0.10f, 0.98f, 0.42f);  // верхняя рука
        Vector3 heel = new(hand * 0.40f, 0.03f, 1.12f);  // пятка крюка на льду
        Vector3 low = top.Lerp(heel, 0.38f);             // нижняя рука на рукоятке
        var gloveMat = Mat(kit.Pants, 0.6f);
        Limb(p, hand < 0 ? shR : shL, top, 0.07f, jersey);
        Limb(p, hand < 0 ? shL : shR, low, 0.07f, jersey);
        Part(p, new SphereMesh { Radius = 0.075f, Height = 0.15f }, gloveMat, top);
        Part(p, new SphereMesh { Radius = 0.075f, Height = 0.15f }, gloveMat, low);
        Limb(p, top + (top - heel).Normalized() * 0.12f, heel, 0.016f, black);
        var blade = Part(p, new BoxMesh { Size = new Vector3(0.30f, 0.07f, 0.02f) }, black, heel + new Vector3(hand * 0.13f, 0.02f, 0.02f));
        blade.RotationDegrees = new Vector3(0, -hand * 15f, 0);

        // Номер на спине и на рукавах
        var num = new Label3D
        {
            Text = number.ToString(),
            FontSize = 96,
            PixelSize = 0.0028f,
            Modulate = kit.Number,
            OutlineModulate = kit.Trim,
            OutlineSize = 14,
            Position = new Vector3(0, 0.36f, -0.235f),
            RotationDegrees = new Vector3(0, 180, 0),
            DoubleSided = false,
            Shaded = true,
        };
        torso.AddChild(num);
        return p;
    }

    public static PlayerFigure Goalie(Kit kit, int number)
    {
        var g = new PlayerFigure { Name = $"Goalie{number}" };
        var jersey = Mat(kit.Jersey, 0.75f);
        var pads = Mat(new Color(0.96f, 0.96f, 0.96f), 0.5f);
        var trim = Mat(kit.Trim, 0.6f);
        var black = Mat(new Color(0.05f, 0.05f, 0.06f), 0.4f);
        // Щитки — стойка «полубаттерфляй»
        foreach (var sx in new[] { -1f, 1f })
        {
            var pad = g.Add(new BoxMesh { Size = new Vector3(0.28f, 0.82f, 0.22f) }, pads, new Vector3(sx * 0.24f, 0.41f, 0.05f));
            pad.RotationDegrees = new Vector3(-8, 0, sx * -6);
            g.Add(new BoxMesh { Size = new Vector3(0.29f, 0.06f, 0.23f) }, trim, new Vector3(sx * 0.24f, 0.55f, 0.06f));
        }
        var shorts = g.Add(Cap(0.24f, 0.48f), Mat(kit.Pants, 0.6f), new Vector3(0, 0.82f, 0));
        shorts.Scale = new Vector3(1.35f, 1f, 1.05f);
        var torso = new Node3D { Position = new Vector3(0, 0.98f, 0), RotationDegrees = new Vector3(18, 0, 0) };
        g.AddChild(torso);
        var body = Part(torso, Cap(0.30f, 0.78f), jersey, new Vector3(0, 0.3f, 0));
        body.Scale = new Vector3(1.35f, 1f, 1f);
        // Ловушка и блин
        Part(torso, new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.13f, Height = 0.1f }, pads, new Vector3(-0.48f, 0.25f, 0.25f)).RotationDegrees = new Vector3(0, 0, 80);
        Part(torso, new BoxMesh { Size = new Vector3(0.05f, 0.38f, 0.24f) }, pads, new Vector3(0.48f, 0.22f, 0.25f));
        // Маска
        Part(torso, new SphereMesh { Radius = 0.14f, Height = 0.3f }, Mat(kit.Helmet, 0.2f, 0.3f), new Vector3(0, 0.82f, 0.05f));
        Part(torso, new BoxMesh { Size = new Vector3(0.16f, 0.14f, 0.02f) }, black, new Vector3(0, 0.78f, 0.18f));
        // Клюшка вратаря на льду
        Part(g, new BoxMesh { Size = new Vector3(0.09f, 0.02f, 0.5f) }, black, new Vector3(0.12f, 0.03f, 0.42f)).RotationDegrees = new Vector3(0, 70, 0);
        var num = new Label3D
        {
            Text = number.ToString(), FontSize = 96, PixelSize = 0.003f, Modulate = kit.Number, OutlineModulate = kit.Trim, OutlineSize = 14,
            Position = new Vector3(0, 0.36f, -0.31f), RotationDegrees = new Vector3(0, 180, 0), Shaded = true,
        };
        torso.AddChild(num);
        return g;
    }

    public static Node3D Referee()
    {
        var r = new PlayerFigure { Name = "Referee" };
        var stripes = new StandardMaterial3D { AlbedoTexture = StripeTexture(), Roughness = 0.8f, Uv1Scale = new Vector3(4, 1, 1) };
        var black = Mat(new Color(0.06f, 0.06f, 0.07f), 0.6f);
        r.Add(Cap(0.07f, 0.8f), black, new Vector3(-0.1f, 0.42f, 0));
        r.Add(Cap(0.07f, 0.8f), black, new Vector3(0.1f, 0.42f, 0));
        var body = r.Add(Cap(0.22f, 0.75f), stripes, new Vector3(0, 1.18f, 0));
        body.Scale = new Vector3(1.25f, 1f, 0.9f);
        r.Add(new SphereMesh { Radius = 0.12f, Height = 0.24f }, black, new Vector3(0, 1.66f, 0));
        var band = r.Add(new BoxMesh { Size = new Vector3(0.1f, 0.06f, 0.05f) }, Mat(new Color(1f, 0.45f, 0.05f), 0.5f), new Vector3(0.3f, 1.35f, 0));
        return r;
    }

    static ImageTexture StripeTexture()
    {
        var img = Image.CreateEmpty(16, 2, false, Image.Format.Rgb8);
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 2; y++)
            img.SetPixel(x, y, (x / 4) % 2 == 0 ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.05f, 0.05f, 0.05f));
        return ImageTexture.CreateFromImage(img);
    }

    MeshInstance3D Add(Mesh m, Material mat, Vector3 pos) => Part(this, m, mat, pos);

    /// <summary>Капсула между двумя точками (рука, нога, рукоять клюшки).</summary>
    static MeshInstance3D Limb(Node3D parent, Vector3 from, Vector3 to, float r, Material mat)
    {
        float len = from.DistanceTo(to);
        var mi = Part(parent, new CapsuleMesh { Radius = r, Height = len + 2 * r, RadialSegments = 12, Rings = 3 }, mat, (from + to) / 2);
        var dir = (to - from) / len;
        var axis = Vector3.Up.Cross(dir);
        if (axis.Length() > 1e-4f) mi.Basis = new Basis(axis.Normalized(), Vector3.Up.AngleTo(dir));
        return mi;
    }

    static MeshInstance3D Part(Node3D parent, Mesh m, Material mat, Vector3 pos)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos };
        parent.AddChild(mi);
        return mi;
    }

    static CapsuleMesh Cap(float r, float h) => new() { Radius = r, Height = h, RadialSegments = 20, Rings = 6 };

    static StandardMaterial3D Mat(Color c, float rough, float metal = 0f) => new()
    {
        AlbedoColor = c,
        Roughness = rough,
        Metallic = metal,
        Transparency = c.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };
}
