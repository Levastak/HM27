using System;
using System.Collections.Generic;
using Godot;

namespace HM27.Match3D;

/// <summary>Строит арену кодом: лёд, борта, стекло, ворота, трибуны с болельщиками, свет, куб-табло.</summary>
public static class ArenaBuilder
{
    public static void Build(Node3D root, Color homeColor, Color homeAlt, Color awayColor)
    {
        root.AddChild(Ice());
        root.AddChild(Boards());
        root.AddChild(Glass());
        root.AddChild(Goal(+1));
        root.AddChild(Goal(-1));
        root.AddChild(Stands(homeColor, homeAlt, awayColor));
        root.AddChild(Jumbotron(homeColor));
        AddLights(root);
        root.AddChild(Environment());
    }

    // ---------- лёд ----------
    static MeshInstance3D Ice()
    {
        var outline = RinkSpec.Outline(0f, 32);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        Vector3 V(Vector2 p) => new(p.X, 0, p.Y);
        Vector2 UV(Vector2 p) => new((p.X / RinkSpec.Ft + RinkSpec.HalfLengthFt) / (2 * RinkSpec.HalfLengthFt),
                                     (p.Y / RinkSpec.Ft + RinkSpec.HalfWidthFt) / (2 * RinkSpec.HalfWidthFt));
        var c = Vector2.Zero;
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % outline.Count];
            // веер из центра (контур выпуклый); порядок вершин — лицом вверх
            st.SetUV(UV(c)); st.AddVertex(V(c));
            st.SetUV(UV(a)); st.AddVertex(V(a));
            st.SetUV(UV(b)); st.AddVertex(V(b));
        }
        st.GenerateTangents();
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = IceTexture.Build(),
            Roughness = 0.08f,
            Metallic = 0.0f,
            MetallicSpecular = 0.6f,
            ClearcoatEnabled = true,
            Clearcoat = 0.6f,
            ClearcoatRoughness = 0.15f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        return new MeshInstance3D { Name = "Ice", Mesh = st.Commit(), MaterialOverride = mat };
    }

    // ---------- борта ----------
    static Node3D Boards()
    {
        var node = new Node3D { Name = "Boards" };
        var inner = RinkSpec.Outline(0f, 32);
        var outer = RinkSpec.Outline(0.12f, 32);
        float kick = 0.24f, top = RinkSpec.BoardHeight;

        node.AddChild(Wall(inner, outer, 0f, kick, new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.78f, 0.12f), Roughness = 0.5f }, "Kick"));
        node.AddChild(Wall(inner, outer, kick, top, new StandardMaterial3D { AlbedoColor = new Color(0.97f, 0.97f, 0.97f), Roughness = 0.35f }, "Board"));
        // Синий поручень сверху
        var cap = Wall(RinkSpec.Outline(-0.02f, 32), RinkSpec.Outline(0.14f, 32), top, top + 0.06f,
                       new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.12f, 0.28f), Roughness = 0.4f }, "Cap");
        node.AddChild(cap);
        return node;
    }

    /// <summary>Стенка между двумя контурами от y0 до y1: внутренняя грань, внешняя и верх.</summary>
    static MeshInstance3D Wall(List<Vector2> inner, List<Vector2> outer, float y0, float y1, Material mat, string name)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int n = inner.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 a0 = new(inner[i].X, y0, inner[i].Y), b0 = new(inner[j].X, y0, inner[j].Y);
            Vector3 a1 = new(inner[i].X, y1, inner[i].Y), b1 = new(inner[j].X, y1, inner[j].Y);
            Vector3 o0 = new(outer[i].X, y0, outer[i].Y), p0 = new(outer[j].X, y0, outer[j].Y);
            Vector3 o1 = new(outer[i].X, y1, outer[i].Y), p1 = new(outer[j].X, y1, outer[j].Y);
            Quad(st, a0, a1, b1, b0);   // внутрь площадки
            Quad(st, p0, p1, o1, o0);   // наружу
            Quad(st, a1, o1, p1, b1);   // верх
        }
        st.GenerateNormals();
        return new MeshInstance3D { Name = name, Mesh = st.Commit(), MaterialOverride = mat };
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    // ---------- стекло ----------
    static MeshInstance3D Glass()
    {
        var pts = RinkSpec.Outline(0.06f, 32);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float y0 = RinkSpec.BoardHeight + 0.06f;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % pts.Count];
            // За воротами стекло выше
            float ha = MathF.Abs(a.X) > RinkSpec.GoalLineX ? 2.4f : RinkSpec.GlassHeight;
            float hb = MathF.Abs(b.X) > RinkSpec.GoalLineX ? 2.4f : RinkSpec.GlassHeight;
            Quad(st, new Vector3(a.X, y0, a.Y), new Vector3(a.X, y0 + ha, a.Y), new Vector3(b.X, y0 + hb, b.Y), new Vector3(b.X, y0, b.Y));
        }
        st.GenerateNormals();
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.85f, 0.93f, 1f, 0.025f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.05f,
            MetallicSpecular = 0.25f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return new MeshInstance3D { Name = "Glass", Mesh = st.Commit(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    // ---------- ворота ----------
    static Node3D Goal(int side)
    {
        var g = new Node3D { Name = side > 0 ? "GoalRight" : "GoalLeft" };
        // Створ на лицевой линии, сетка уходит к борту
        g.Position = new Vector3(side * RinkSpec.GoalLineX, 0, 0);
        g.RotationDegrees = new Vector3(0, side > 0 ? 0 : 180, 0);
        var red = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.05f, 0.08f), Roughness = 0.3f, Metallic = 0.3f };
        float w = RinkSpec.GoalWidth, h = RinkSpec.GoalHeight, d = RinkSpec.GoalDepth, r = 0.03f;

        void Bar(Vector3 from, Vector3 to)
        {
            var len = from.DistanceTo(to);
            var m = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = len, RadialSegments = 12 }, MaterialOverride = red };
            m.Position = (from + to) / 2;
            var dir = (to - from).Normalized();
            var axis = Vector3.Up.Cross(dir);
            if (axis.Length() > 1e-4f) m.Basis = new Basis(axis.Normalized(), Vector3.Up.AngleTo(dir));
            g.AddChild(m);
        }
        float hw = w / 2;
        Bar(new(0, 0, -hw), new(0, h, -hw));
        Bar(new(0, 0, hw), new(0, h, hw));
        Bar(new(0, h, -hw), new(0, h, hw));
        // нижняя рама сзади (дуга упрощена до трёх отрезков)
        Bar(new(0, 0.02f, -hw), new(d * 0.8f, 0.02f, -hw * 0.9f));
        Bar(new(d * 0.8f, 0.02f, -hw * 0.9f), new(d * 0.8f, 0.02f, hw * 0.9f));
        Bar(new(d * 0.8f, 0.02f, hw * 0.9f), new(0, 0.02f, hw));

        // Сетка — полупрозрачные грани
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 tl = new(0, h, -hw), tr = new(0, h, hw), bl = new(d * 0.8f, 0.02f, -hw * 0.9f), br = new(d * 0.8f, 0.02f, hw * 0.9f);
        Vector3 ml = new(d * 0.3f, h * 0.9f, -hw), mr = new(d * 0.3f, h * 0.9f, hw);
        Quad(st, tl, tr, mr, ml); Quad(st, ml, mr, br, bl);
        Quad(st, new(0, 0, -hw), tl, ml, bl); Quad(st, new(0, 0, hw), br, mr, tr);
        st.GenerateNormals();
        var net = new StandardMaterial3D
        {
            AlbedoColor = new Color(1, 1, 1, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.9f,
        };
        g.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = net });
        return g;
    }

    // ---------- трибуны ----------
    static Node3D Stands(Color home, Color homeAlt, Color away)
    {
        var node = new Node3D { Name = "Stands" };
        const int rows = 26;
        const float rowDepth = 0.85f, rowRise = 0.42f, firstOffset = 2.2f, firstY = 0.6f, seatGap = 0.56f;
        var seatMat = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.10f, 0.12f), Roughness = 0.9f };

        var positions = new List<Transform3D>();
        var colors = new List<Color>();
        var rng = new Random(2027);
        Color[] palette = { home, home, home, homeAlt, homeAlt, new Color(0.95f, 0.95f, 0.95f), new Color(0.15f, 0.15f, 0.18f), away };

        for (int row = 0; row < rows; row++)
        {
            float off = firstOffset + row * rowDepth;
            float y = firstY + row * rowRise;
            var inner = RinkSpec.Outline(off, 24);
            var outer = RinkSpec.Outline(off + rowDepth, 24);
            node.AddChild(Wall(inner, outer, y - rowRise, y, seatMat, $"Row{row}"));

            // места вдоль контура
            var path = RinkSpec.Outline(off + rowDepth * 0.55f, 24);
            float carry = (float)rng.NextDouble() * seatGap;
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[i]; var b = path[(i + 1) % path.Count];
                float len = a.DistanceTo(b);
                var dir = (b - a) / len;
                for (float t = carry; t < len; t += seatGap)
                {
                    var p = a + dir * t;
                    // проходы между секторами
                    if (((int)((MathF.Atan2(p.Y, p.X) + MathF.PI) / (2 * MathF.PI) * 36)) % 6 == 0 &&
                        Mathf.PosMod(MathF.Atan2(p.Y, p.X) * 36 / (2 * MathF.PI) * 1f, 1f) < 0.12f) continue;
                    if (rng.NextDouble() < 0.06) continue;   // свободные места
                    var facing = new Vector3(-p.X, 0, -p.Y).Normalized();
                    var basis = Basis.LookingAt(facing, Vector3.Up);
                    float hgt = 0.95f + (float)rng.NextDouble() * 0.25f;
                    basis = basis.Scaled(new Vector3(1f, hgt, 1f));
                    positions.Add(new Transform3D(basis, new Vector3(p.X, y + 0.45f * hgt, p.Y)));
                    var c = palette[rng.Next(palette.Length)];
                    colors.Add(c.Darkened(0.35f + (float)rng.NextDouble() * 0.3f));
                }
                carry = (carry - len) % seatGap; if (carry < 0) carry += seatGap;
            }
        }

        var fan = new CapsuleMesh { Radius = 0.2f, Height = 0.95f, RadialSegments = 6, Rings = 2 };
        var fanMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.85f };
        fan.Material = fanMat;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = fan, InstanceCount = positions.Count };
        for (int i = 0; i < positions.Count; i++) { mm.SetInstanceTransform(i, positions[i]); mm.SetInstanceColor(i, colors[i]); }
        node.AddChild(new MultiMeshInstance3D { Name = "Fans", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        GD.Print($"Болельщиков: {positions.Count}");
        return node;
    }

    // ---------- куб-табло ----------
    static Node3D Jumbotron(Color home)
    {
        var n = new Node3D { Name = "Jumbotron", Position = new Vector3(0, 19f, 0) };
        void NoShadow(Node node) { foreach (var c in node.GetChildren()) if (c is GeometryInstance3D gi) gi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; }
        var body = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.06f), Metallic = 0.6f, Roughness = 0.4f };
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(7.5f, 4.2f, 6f) }, MaterialOverride = body });
        var screen = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.02f, 0.02f, 0.03f),
            EmissionEnabled = true,
            Emission = home.Lightened(0.25f),
            EmissionEnergyMultiplier = 1.4f,
        };
        foreach (var (pos, rot, w) in new (Vector3, float, float)[] { (new(0, 0, 3.01f), 0, 7f), (new(0, 0, -3.01f), 180, 7f), (new(3.76f, 0, 0), 90, 5.6f), (new(-3.76f, 0, 0), -90, 5.6f) })
        {
            var q = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(w, 3.4f) }, MaterialOverride = screen, Position = pos };
            q.RotationDegrees = new Vector3(0, rot, 0);
            n.AddChild(q);
        }
        // Светящиеся кольца сверху и снизу
        var ring = new StandardMaterial3D { EmissionEnabled = true, Emission = new Color(1, 1, 1), EmissionEnergyMultiplier = 2f, AlbedoColor = Colors.White };
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(7.7f, 0.12f, 6.2f) }, MaterialOverride = ring, Position = new Vector3(0, -2.15f, 0) });
        NoShadow(n);
        return n;
    }

    // ---------- свет и окружение ----------
    static void AddLights(Node3D root)
    {
        // Мягкий «ключевой» свет сверху с тенями
        var sun = new DirectionalLight3D
        {
            Name = "KeyLight",
            LightEnergy = 1.1f,
            ShadowEnabled = true,
            LightColor = new Color(1f, 0.98f, 0.95f),
            DirectionalShadowMaxDistance = 90f,
        };
        sun.RotationDegrees = new Vector3(-68, 20, 0);
        root.AddChild(sun);

        // Ряды прожекторов над льдом
        for (int ix = -3; ix <= 3; ix++)
        for (int iz = -1; iz <= 1; iz++)
        {
            var spot = new SpotLight3D
            {
                LightEnergy = 6f,
                SpotRange = 40f,
                SpotAngle = 38f,
                SpotAttenuation = 0.6f,
                LightColor = new Color(1f, 0.97f, 0.92f),
                Position = new Vector3(ix * 9f, 24f, iz * 9f),
            };
            spot.RotationDegrees = new Vector3(-90, 0, 0);
            root.AddChild(spot);
        }
    }

    static WorldEnvironment Environment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.015f, 0.016f, 0.022f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.58f, 0.65f),
            AmbientLightEnergy = 0.12f,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 0.9f,
            SsrEnabled = true,
            SsrMaxSteps = 64,
            SsrFadeIn = 0.15f,
            SsrFadeOut = 2f,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.6f,
            GlowEnabled = true,
            GlowIntensity = 0.35f,
            GlowHdrThreshold = 1.3f,
            GlowBloom = 0.0f,
            FogEnabled = false,
        };
        return new WorldEnvironment { Environment = env };
    }
}
