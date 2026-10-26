using System;
using System.Collections.Generic;
using Godot;

namespace HM27.Match3D;

/// <summary>
/// Размеры площадки НХЛ (Официальные правила НХЛ, правило 1). Внутри — футы, наружу — метры.
/// Ось X — вдоль площадки (от ворот к воротам), Z — поперёк, Y — вверх. Центр площадки — (0, 0, 0).
/// </summary>
public static class RinkSpec
{
    public const float Ft = 0.3048f;

    // Площадка 200 × 85 футов, радиус углов 28 футов.
    public const float HalfLengthFt = 100f;
    public const float HalfWidthFt = 42.5f;
    public const float CornerRadiusFt = 28f;

    public const float GoalLineFt = 89f;           // 11 футов от борта
    public const float BlueLineFt = 25f;           // центр синей линии от центра площадки
    public const float ZoneDotXFt = 69f;           // точки вбрасывания в зонах: 20 футов от лицевой
    public const float ZoneDotZFt = 22f;
    public const float NeutralDotXFt = 20f;        // точки в средней зоне: 5 футов от синей
    public const float CircleRadiusFt = 15f;

    public const float BoardHeight = 42f / 12f * Ft;   // 42 дюйма
    public const float GlassHeight = 1.8f;             // стекло над бортом (по бокам ниже, за воротами выше)
    public const float GoalWidth = 6f * Ft;
    public const float GoalHeight = 4f * Ft;
    public const float GoalDepth = 44f / 12f * Ft;

    public static float HalfLength => HalfLengthFt * Ft;
    public static float HalfWidth => HalfWidthFt * Ft;
    public static float CornerRadius => CornerRadiusFt * Ft;
    public static float GoalLineX => GoalLineFt * Ft;

    /// <summary>Внутри ли точка (в футах) скруглённого прямоугольника площадки, с отступом inset.</summary>
    public static bool InsideFt(float x, float z, float inset = 0f)
    {
        float hx = HalfLengthFt - inset, hz = HalfWidthFt - inset, r = CornerRadiusFt - inset;
        float ax = MathF.Abs(x), az = MathF.Abs(z);
        if (ax > hx || az > hz) return false;
        float cx = hx - r, cz = hz - r;
        if (ax > cx && az > cz)
        {
            float dx = ax - cx, dz = az - cz;
            return dx * dx + dz * dz <= r * r;
        }
        return true;
    }

    /// <summary>Контур площадки (в метрах), против часовой стрелки если смотреть сверху, с отступом offset наружу.</summary>
    public static List<Vector2> Outline(float offset = 0f, int segPerCorner = 20)
    {
        float r = CornerRadius + offset;
        float cx = HalfLength - CornerRadius, cz = HalfWidth - CornerRadius;
        var pts = new List<Vector2>();
        // Четыре угла: (+,+) → (−,+) → (−,−) → (+,−)
        (float sx, float sz, float a0)[] corners = { (1, 1, 0f), (-1, 1, 90f), (-1, -1, 180f), (1, -1, 270f) };
        foreach (var (sx, sz, a0) in corners)
        {
            for (int i = 0; i <= segPerCorner; i++)
            {
                float a = Mathf.DegToRad(a0 + 90f * i / segPerCorner);
                pts.Add(new Vector2(sx * cx + r * MathF.Cos(a), sz * cz + r * MathF.Sin(a)));
            }
        }
        return pts;
    }
}
