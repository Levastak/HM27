using System;
using Godot;

namespace HM27.Match3D;

/// <summary>
/// Разметка льда, нарисованная кодом по правилам НХЛ: красная центральная, синие, лицевые,
/// круги и точки вбрасывания с «усами», зоны вратаря, трапеции, полукруг судьи.
/// Каждый пиксель считается с 4 подвыборками, поэтому линии гладкие на любом масштабе.
/// </summary>
public static class IceTexture
{
    static readonly Color Ice = new(0.94f, 0.955f, 0.975f);
    static readonly Color Red = new(0.78f, 0.07f, 0.12f);
    static readonly Color Blue = new(0.06f, 0.22f, 0.62f);
    static readonly Color CreaseBlue = new(0.52f, 0.74f, 0.95f);

    const float Thin = 2f / 12f;   // 2 дюйма
    const float Wide = 1f;         // 12 дюймов

    public static ImageTexture Build(int pxPerFt = 16)
    {
        int w = (int)(RinkSpec.HalfLengthFt * 2 * pxPerFt);
        int h = (int)(RinkSpec.HalfWidthFt * 2 * pxPerFt);
        var data = new byte[w * h * 4];
        var rng = new Random(27);
        float[] sub = { 0.25f, 0.75f };

        System.Threading.Tasks.Parallel.For(0, h, py =>
        {
            var lr = new Random(27 + py);
            for (int px = 0; px < w; px++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                foreach (var sy in sub)
                foreach (var sx in sub)
                {
                    float x = (px + sx) / pxPerFt - RinkSpec.HalfLengthFt;
                    float z = (py + sy) / pxPerFt - RinkSpec.HalfWidthFt;
                    var c = Sample(x, z);
                    r += c.R; g += c.G; b += c.B; a += c.A;
                }
                // Лёгкая неравномерность льда: лёд не бывает идеально ровного цвета.
                float n = 1f + ((float)lr.NextDouble() - 0.5f) * 0.012f;
                int i = (py * w + px) * 4;
                data[i] = (byte)Math.Clamp(r / 4 * n * 255, 0, 255);
                data[i + 1] = (byte)Math.Clamp(g / 4 * n * 255, 0, 255);
                data[i + 2] = (byte)Math.Clamp(b / 4 * n * 255, 0, 255);
                data[i + 3] = (byte)Math.Clamp(a / 4 * 255, 0, 255);
            }
        });

        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    static Color Sample(float x, float z)
    {
        if (!RinkSpec.InsideFt(x, z)) return new Color(Ice, 1f);
        float ax = MathF.Abs(x), az = MathF.Abs(z);

        // Центральная красная линия (12 дюймов) с белым «пунктиром» внутри, как у НХЛ
        if (ax <= Wide / 2)
        {
            bool notch = MathF.Abs(x) < 0.1f && (MathF.Abs(z) % 2.5f) < 0.6f;
            return notch ? Ice : Red;
        }
        // Синие линии
        if (MathF.Abs(ax - RinkSpec.BlueLineFt) <= Wide / 2) return Blue;

        // Центральный круг и точка
        float rc = MathF.Sqrt(x * x + z * z);
        if (rc <= 0.5f) return Blue;
        if (MathF.Abs(rc - RinkSpec.CircleRadiusFt) <= Thin / 2) return Blue;

        // Зоны атаки/защиты
        float gx = RinkSpec.GoalLineFt;
        float sideX = MathF.Sign(x);
        float dxGoal = gx - ax;              // >0 — перед лицевой (к центру), <0 — за воротами

        // Зона вратаря: прямые стороны 4,5 фута при |z| = 4, дуга радиуса 6
        if (dxGoal >= 0 && dxGoal <= 6.1f && az <= 4.05f)
        {
            float d = MathF.Sqrt(dxGoal * dxGoal + z * z);
            bool insideCrease = d <= 6f && az <= 4f;
            bool edge = (MathF.Abs(d - 6f) <= Thin / 2 && az <= 4f && dxGoal >= 4.5f) ||
                        (MathF.Abs(az - 4f) <= Thin / 2 && dxGoal <= 4.5f);
            if (edge) return Red;
            if (insideCrease) return CreaseBlue;
        }
        // Лицевая линия
        if (MathF.Abs(dxGoal) <= Thin / 2 && RinkSpec.InsideFt(x, z, 0.5f)) return Red;

        // Трапеция за воротами: от |z| = 11 на лицевой до |z| = 14 у борта
        if (dxGoal < 0)
        {
            float t = -dxGoal / (RinkSpec.HalfLengthFt - gx);
            float tz = 11f + 3f * t;
            if (MathF.Abs(az - tz) <= Thin / 2) return Red;
        }

        // Круги вбрасывания в зонах
        float cxz = RinkSpec.ZoneDotXFt, czz = RinkSpec.ZoneDotZFt;
        float lx = ax - cxz, lz = az - czz;
        float dc = MathF.Sqrt(lx * lx + lz * lz);
        if (dc <= 1f) return Red;
        if (MathF.Abs(dc - RinkSpec.CircleRadiusFt) <= Thin / 2) return Red;
        // «Усы» на кругах: отрезки 2 фута наружу от круга, 5 ft 7 in между ними
        if (MathF.Abs(MathF.Abs(lx) - 2.79f) <= Thin / 2 && MathF.Abs(lz) >= RinkSpec.CircleRadiusFt - 0.2f && MathF.Abs(lz) <= RinkSpec.CircleRadiusFt + 2f) return Red;
        // L-образные отметки у точки
        if (InL(MathF.Abs(lx), MathF.Abs(lz))) return Red;

        // Точки в средней зоне
        float nx = ax - RinkSpec.NeutralDotXFt, nz = az - RinkSpec.ZoneDotZFt;
        if (nx * nx + nz * nz <= 1f) return Red;

        // Полукруг судьи у борта (одна сторона, радиус 10)
        if (z > 0)
        {
            float dz = RinkSpec.HalfWidthFt - z;
            float dr = MathF.Sqrt(x * x + dz * dz);
            if (MathF.Abs(dr - 10f) <= Thin / 2) return Red;
        }
        _ = sideX;
        return Ice;
    }

    // L-образные отметки: в 2 футах от точки по x и в 0,75 фута по z, длина 4 и 3 фута
    static bool InL(float ax, float az)
    {
        bool hor = ax >= 2f && ax <= 6f && MathF.Abs(az - 0.75f) <= Thin / 2;
        bool ver = MathF.Abs(ax - 2f) <= Thin / 2 && az >= 0.75f && az <= 3.75f;
        return hor || ver;
    }
}
