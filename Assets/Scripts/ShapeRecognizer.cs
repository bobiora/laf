using UnityEngine;
using System.Collections.Generic;

public static class ShapeRecognizer
{
    // Типы фигур и очки за них
    public enum ShapeType
    {
        Unknown = 0,
        RightTriangle = 1,      // прямоугольный треугольник
        AcuteTriangle = 2,      // остроугольный треугольник
        Square = 3,             // квадрат
        Parallelogram = 4       // параллелограмм
    }

    // Главный метод: получаем список точек фигуры, возвращаем её тип
    public static ShapeType Recognize(List<PointClick> points)
    {
        // Переводим в координаты сетки
        List<Vector2> coords = new List<Vector2>();
        foreach (var p in points)
            coords.Add(new Vector2(p.gridX, p.gridY));

        // Упорядочиваем по периметру и удаляем коллинеарные промежуточные вершины:
        // если игрок построил треугольник, но провёл сторону через промежуточную точку сетки,
        // цикл содержит 4 вершины, но геометрически это по-прежнему треугольник.
        coords = OrderByAngle(coords);
        coords = SimplifyPolygon(coords);

        if (coords.Count == 3)
            return RecognizeTriangle(coords);
        else if (coords.Count == 4)
            return RecognizeQuadrilateralOrdered(coords);

        return ShapeType.Unknown;
    }

    // Убирает точки, лежащие на прямой между соседями по циклу.
    // Для каждой P с соседями A и B: если (P-A) x (B-P) ≈ 0, то P на отрезке AB.
    public static List<Vector2> SimplifyPolygon(List<Vector2> pts)
    {
        List<Vector2> result = new List<Vector2>(pts);
        bool changed = true;
        while (changed && result.Count > 3)
        {
            changed = false;
            for (int i = 0; i < result.Count; i++)
            {
                Vector2 prev = result[(i - 1 + result.Count) % result.Count];
                Vector2 cur = result[i];
                Vector2 next = result[(i + 1) % result.Count];
                Vector2 v1 = cur - prev;
                Vector2 v2 = next - cur;
                float cross = v1.x * v2.y - v1.y * v2.x;
                if (Mathf.Abs(cross) < 0.01f)
                {
                    result.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
        return result;
    }

    // --- Треугольники ---
    // Считаем три угла напрямую и классифицируем по ним:
    //   - хотя бы один угол ≈ 90° (±1°) → прямоугольный
    //   - все три угла < 90° (с тем же допуском) → остроугольный
    //   - иначе (есть тупой угол) → Unknown
    static ShapeType RecognizeTriangle(List<Vector2> pts)
    {
        const float tolerance = 1f;

        float[] angles = {
            AngleAt(pts[0], pts[1], pts[2]),
            AngleAt(pts[1], pts[0], pts[2]),
            AngleAt(pts[2], pts[0], pts[1])
        };

        for (int i = 0; i < 3; i++)
        {
            if (Mathf.Abs(angles[i] - 90f) <= tolerance)
            {
                // Прямой угол в вершине i. Если два катета равны — это 45-45-90
                // (равнобедренный прямоугольный, "острый" визуально) → AcuteTriangle.
                int j = (i + 1) % 3;
                int k = (i + 2) % 3;
                float leg1 = (pts[j] - pts[i]).sqrMagnitude;
                float leg2 = (pts[k] - pts[i]).sqrMagnitude;
                if (Mathf.Abs(leg1 - leg2) < 0.01f && leg1 > 1.01f)
                    return ShapeType.AcuteTriangle; // крупный равнобедренный прямоугольный
                return ShapeType.RightTriangle;     // включая базовый 1x1 половинчик клетки
            }
        }

        foreach (float a in angles)
            if (a >= 90f - tolerance)
                return ShapeType.Unknown; // тупоугольный — 1 очко по умолчанию

        return ShapeType.AcuteTriangle;
    }

    // Угол в вершине vertex между рёбрами к соседям n1 и n2 (в градусах)
    static float AngleAt(Vector2 vertex, Vector2 n1, Vector2 n2)
    {
        return Vector2.Angle(n1 - vertex, n2 - vertex);
    }

    // --- Четырёхугольники ---
    // На вход приходят точки, уже упорядоченные по периметру (см. Recognize).
    static ShapeType RecognizeQuadrilateralOrdered(List<Vector2> ordered)
    {
        // Векторы сторон
        Vector2 s1 = ordered[1] - ordered[0];
        Vector2 s2 = ordered[2] - ordered[1];
        Vector2 s3 = ordered[3] - ordered[2];
        Vector2 s4 = ordered[0] - ordered[3];

        // Параллелограмм: противоположные стороны равны и параллельны
        // (s1 == -s3) означает s1 + s3 == 0
        bool parallelogram = (s1 + s3).sqrMagnitude < 0.01f
                          && (s2 + s4).sqrMagnitude < 0.01f;

        if (!parallelogram)
            return ShapeType.Unknown;

        // Квадрат: все стороны равны + соседние стороны перпендикулярны
        float len1 = s1.sqrMagnitude;
        float len2 = s2.sqrMagnitude;

        bool equalSides = Mathf.Abs(len1 - len2) < 0.01f;
        bool rightAngle = Mathf.Abs(Vector2.Dot(s1, s2)) < 0.01f;

        if (equalSides && rightAngle)
            return ShapeType.Square;

        return ShapeType.Parallelogram;
    }

    // Упорядочивает точки по углу вокруг их центра (по часовой стрелке)
    static List<Vector2> OrderByAngle(List<Vector2> pts)
    {
        Vector2 center = Vector2.zero;
        foreach (var p in pts) center += p;
        center /= pts.Count;

        List<Vector2> sorted = new List<Vector2>(pts);
        sorted.Sort((a, b) =>
        {
            float angA = Mathf.Atan2(a.y - center.y, a.x - center.x);
            float angB = Mathf.Atan2(b.y - center.y, b.x - center.x);
            return angA.CompareTo(angB);
        });
        return sorted;
    }

    // Получить количество очков за тип фигуры
    public static int GetPoints(ShapeType type)
    {
        switch (type)
        {
            case ShapeType.RightTriangle: return 1;
            case ShapeType.AcuteTriangle: return 2;
            case ShapeType.Square: return 3;
            case ShapeType.Parallelogram: return 4;
            default: return 1;
        }
    }

    public static string GetName(ShapeType type)
    {
        switch (type)
        {
            case ShapeType.RightTriangle: return "right triangle";
            case ShapeType.AcuteTriangle: return "acute triangle";
            case ShapeType.Square: return "square";
            case ShapeType.Parallelogram: return "parallelogram";
            default: return "shape";
        }
    }
}