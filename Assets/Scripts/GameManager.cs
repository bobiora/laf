using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public GameObject linePrefab;

    public Color player1Color = Color.red;
    public Color player2Color = Color.green;

    public int currentPlayer = 1;
    public int player1Score = 0;
    public int player2Score = 0;

    public GameOverUI gameOverUI;
    public bool isGameOver = false;

    private PointClick firstPoint = null;
    private List<GameObject> allLines = new List<GameObject>();

    // Список всех рёбер: пара точек = одна линия
    private List<(PointClick, PointClick)> edges = new List<(PointClick, PointClick)>();

    // Замкнутые фигуры, которые уже "закрашены" (чтобы не начислять очки дважды)
    private List<List<PointClick>> closedShapes = new List<List<PointClick>>();

    void Awake()
    {
        Instance = this;
    }

    public void OnPointClicked(PointClick point)
    {
        if (isGameOver) return;

        if (firstPoint == null)
        {
            firstPoint = point;
            point.SetSelected(true, GetCurrentColor());
        }
        else if (firstPoint == point)
        {
            point.SetSelected(false, Color.white);
            firstPoint = null;
        }
        else
        {
            // Проверяем, нет ли уже такой линии
            if (EdgeExists(firstPoint, point))
            {
                Debug.Log("Такая линия уже есть!");
                firstPoint.SetSelected(false, Color.white);
                firstPoint = null;
                return;
            }

            DrawLine(firstPoint, point);
            edges.Add((firstPoint, point));

            // Проверяем, замкнулась ли фигура
            List<PointClick> shape = FindNewClosedShape(firstPoint, point);
            bool gotShape = false;
            if (shape != null)
            {
                closedShapes.Add(shape);

                // Распознаём тип фигуры
                ShapeRecognizer.ShapeType type = ShapeRecognizer.Recognize(shape);
                int points = ShapeRecognizer.GetPoints(type);
                string name = ShapeRecognizer.GetName(type);

                AddScore(points);
                gotShape = true;
                Debug.Log($"Игрок {currentPlayer} построил {name}! +{points} очков");
            }

            firstPoint.SetSelected(false, Color.white);
            firstPoint = null;

            // Если игрок замкнул фигуру — ходит ещё раз (как в "Точки и квадраты")
            if (!gotShape)
                SwitchPlayer();

            if (!HasAnyValidMove())
                EndGame();
        }
    }

    bool HasAnyValidMove()
    {
        PointClick[] points = FindObjectsByType<PointClick>(FindObjectsSortMode.None);
        for (int i = 0; i < points.Length; i++)
        {
            for (int j = i + 1; j < points.Length; j++)
            {
                if (EdgeExists(points[i], points[j])) continue;
                if (EdgeCrossesExisting(points[i], points[j])) continue;
                return true;
            }
        }
        return false;
    }

    bool EdgeCrossesExisting(PointClick a, PointClick b)
    {
        Vector2 p1 = a.transform.position;
        Vector2 p2 = b.transform.position;
        foreach (var e in edges)
        {
            if (e.Item1 == a || e.Item1 == b || e.Item2 == a || e.Item2 == b) continue;
            Vector2 p3 = e.Item1.transform.position;
            Vector2 p4 = e.Item2.transform.position;
            if (SegmentsIntersect(p1, p2, p3, p4)) return true;
        }
        return false;
    }

    public static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        int o1 = Orientation(p1, p2, p3);
        int o2 = Orientation(p1, p2, p4);
        int o3 = Orientation(p3, p4, p1);
        int o4 = Orientation(p3, p4, p2);

        if (o1 != o2 && o3 != o4) return true;

        if (o1 == 0 && OnSegment(p1, p3, p2)) return true;
        if (o2 == 0 && OnSegment(p1, p4, p2)) return true;
        if (o3 == 0 && OnSegment(p3, p1, p4)) return true;
        if (o4 == 0 && OnSegment(p3, p2, p4)) return true;

        return false;
    }

    static int Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        float val = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        if (Mathf.Abs(val) < 1e-6f) return 0;
        return val > 0 ? 1 : 2;
    }

    static bool OnSegment(Vector2 a, Vector2 b, Vector2 c)
    {
        return b.x <= Mathf.Max(a.x, c.x) + 1e-6f && b.x >= Mathf.Min(a.x, c.x) - 1e-6f
            && b.y <= Mathf.Max(a.y, c.y) + 1e-6f && b.y >= Mathf.Min(a.y, c.y) - 1e-6f;
    }

    void EndGame()
    {
        isGameOver = true;

        if (gameOverUI == null) return;

        if (player1Score > player2Score)
            gameOverUI.Show(1, player1Color, player1Score, false);
        else if (player2Score > player1Score)
            gameOverUI.Show(2, player2Color, player2Score, false);
        else
            gameOverUI.Show(0, Color.white, player1Score, true);
    }

    void DrawLine(PointClick a, PointClick b)
    {
        GameObject lineObj = Instantiate(linePrefab);
        LineRenderer lr = lineObj.GetComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, a.transform.position);
        lr.SetPosition(1, b.transform.position);

        Color c = GetCurrentColor();
        lr.startColor = c;
        lr.endColor = c;

        allLines.Add(lineObj);
    }

    void SwitchPlayer()
    {
        currentPlayer = (currentPlayer == 1) ? 2 : 1;
    }

    void AddScore(int points)
    {
        if (currentPlayer == 1) player1Score += points;
        else player2Score += points;
    }

    public Color GetCurrentColor()
    {
        return currentPlayer == 1 ? player1Color : player2Color;
    }

    bool EdgeExists(PointClick a, PointClick b)
    {
        foreach (var e in edges)
        {
            if ((e.Item1 == a && e.Item2 == b) || (e.Item1 == b && e.Item2 == a))
                return true;
        }
        return false;
    }

    // Ищем ВСЕ простые циклы, включающие ребро a-b, и возвращаем самый маленький новый
    List<PointClick> FindNewClosedShape(PointClick a, PointClick b)
    {
        List<List<PointClick>> allCycles = new List<List<PointClick>>();

        // Ищем все простые пути от b к a, не используя ребро a-b
        List<PointClick> currentPath = new List<PointClick> { b };
        FindAllPaths(b, a, currentPath, allCycles, a, b);

        // Отфильтруем те, что уже были закрашены
        List<PointClick> best = null;
        foreach (var cycle in allCycles)
        {
            if (ShapeAlreadyClosed(cycle)) continue;
            if (cycle.Count < 3) continue;

            // Берём цикл с наименьшим количеством точек (самый маленький)
            if (best == null || cycle.Count < best.Count)
                best = cycle;
        }
        return best;
    }

    // Рекурсивный поиск всех простых путей от current до target
    void FindAllPaths(PointClick current, PointClick target,
                      List<PointClick> path, List<List<PointClick>> result,
                      PointClick forbiddenA, PointClick forbiddenB)
    {
        // Ограничим глубину, чтобы не зависнуть на больших сетках
        if (path.Count > 8) return;

        foreach (PointClick neighbor in GetNeighbors(current))
        {
            // Не идём по запрещённому ребру (то, которое только что нарисовали)
            if ((current == forbiddenA && neighbor == forbiddenB) ||
                (current == forbiddenB && neighbor == forbiddenA))
                continue;

            if (neighbor == target && path.Count >= 2)
            {
                // Нашли цикл — включаем целевую точку, иначе квадрат сохранится как 3 точки
                var cycle = new List<PointClick>(path);
                cycle.Add(target);
                result.Add(cycle);
                continue;
            }

            if (!path.Contains(neighbor))
            {
                path.Add(neighbor);
                FindAllPaths(neighbor, target, path, result, forbiddenA, forbiddenB);
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    List<PointClick> GetNeighbors(PointClick p)
    {
        List<PointClick> neighbors = new List<PointClick>();
        foreach (var e in edges)
        {
            if (e.Item1 == p) neighbors.Add(e.Item2);
            else if (e.Item2 == p) neighbors.Add(e.Item1);
        }
        return neighbors;
    }

    bool ShapeAlreadyClosed(List<PointClick> candidate)
    {
        // Сравниваем как множества точек
        foreach (var shape in closedShapes)
        {
            if (shape.Count != candidate.Count) continue;
            bool same = true;
            foreach (var p in candidate)
            {
                if (!shape.Contains(p)) { same = false; break; }
            }
            if (same) return true;
        }
        return false;
    }
}