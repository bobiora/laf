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

    // Включить, чтобы в консоль сыпались подробные логи проверки "остались ли ходы".
    // Временно включено, чтобы можно было диагностировать регрессию с концом игры.
    public bool debugEndGame = true;

    private PointClick firstPoint = null;
    private List<GameObject> allLines = new List<GameObject>();

    // Кадр, в котором уже обработали клик. Один физический клик = один кадр,
    // поэтому не даём OnPointClicked отработать несколько раз за кадр (несколько
    // PointClick.Update могут задиспатчить один и тот же клик — см. Bug 2).
    private int lastClickFrame = -1;

    // Список всех рёбер: пара точек = одна линия
    private List<(PointClick, PointClick)> edges = new List<(PointClick, PointClick)>();

    // Замкнутые фигуры, которые уже "закрашены" (чтобы не начислять очки дважды)
    private List<List<PointClick>> closedShapes = new List<List<PointClick>>();

    // Заявленные (закрашенные) области — сквозь них проводить линии нельзя.
    public class ClaimedRegion
    {
        public List<PointClick> boundaryPoints;   // порядок по периметру
        public List<Vector2> boundaryWorld;       // те же вершины в мировых координатах
        public int owner;                          // 1 или 2
        public GameObject visual;
    }
    private List<ClaimedRegion> claimedRegions = new List<ClaimedRegion>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (debugEndGame) RunSegmentSelfTest();
    }

    void RunSegmentSelfTest()
    {
        // 1) Диагонали (0,0)-(1,1) и (0,1)-(1,0) ДОЛЖНЫ пересекаться.
        bool t1 = SegmentsIntersect(new Vector2(0, 0), new Vector2(1, 1),
                                    new Vector2(0, 1), new Vector2(1, 0));
        Debug.Log($"[SelfTest] diagonal X-cross: {(t1 ? "PASS" : "FAIL")} (expected true)");

        // 2) Соседние горизонтальные (0,0)-(1,0) и (1,0)-(2,0) — общая вершина, НЕ пересечение.
        bool t2 = SegmentsIntersect(new Vector2(0, 0), new Vector2(1, 0),
                                    new Vector2(1, 0), new Vector2(2, 0));
        Debug.Log($"[SelfTest] shared vertex: {(!t2 ? "PASS" : "FAIL")} (expected false)");

        // 3) Параллельные несмежные (0,0)-(1,0) и (0,1)-(1,1) — не пересекаются.
        bool t3 = SegmentsIntersect(new Vector2(0, 0), new Vector2(1, 0),
                                    new Vector2(0, 1), new Vector2(1, 1));
        Debug.Log($"[SelfTest] parallel separate: {(!t3 ? "PASS" : "FAIL")} (expected false)");
    }

    // Порядок проверок хода строгий (ранние выходы). Проверка соседства идёт ПЕРВОЙ,
    // до отрисовки/детекции граней/начисления — чтобы длинная линия сквозь промежуточную
    // точку сетки (например (0,2)-(2,0), проходящая через (1,1)) не могла быть добавлена.
    public void OnPointClicked(PointClick point)
    {
        if (isGameOver) return;

        // Bug 2: за один кадр обрабатываем не более одного клика.
        if (Time.frameCount == lastClickFrame) return;
        lastClickFrame = Time.frameCount;

        // (a) первый клик — выбираем точку.
        if (firstPoint == null)
        {
            firstPoint = point;
            point.SetSelected(true, GetCurrentColor());
            return;
        }

        // (b) повторный клик по той же точке — снимаем выбор.
        if (firstPoint == point)
        {
            point.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (c) точки должны быть соседними (8 направлений, max(|dx|,|dy|) == 1).
        //     БЕЗ смены игрока — это некорректный ввод, а не сыгранный ход.
        if (!AreAdjacent(firstPoint, point))
        {
            Debug.Log($"[Reject:NotAdjacent] Points are not adjacent: ({firstPoint.gridX},{firstPoint.gridY})-({point.gridX},{point.gridY})");
            firstPoint.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (d) такое ребро уже нарисовано.
        if (EdgeExists(firstPoint, point))
        {
            Debug.Log($"[Reject:EdgeExists] Line already exists: ({firstPoint.gridX},{firstPoint.gridY})-({point.gridX},{point.gridY})");
            firstPoint.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (e) новая линия пересекает существующую.
        if (WouldCrossExistingEdge(firstPoint, point))
        {
            Debug.Log($"[Reject:Crossing] Line crosses another: ({firstPoint.gridX},{firstPoint.gridY})-({point.gridX},{point.gridY})");
            firstPoint.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (f) новая линия проходит сквозь уже закрашенную область.
        if (IsInsideAnyClaimedArea(firstPoint, point))
        {
            Debug.Log($"[Reject:InClaimed] Line passes through claimed area: ({firstPoint.gridX},{firstPoint.gridY})-({point.gridX},{point.gridY})");
            firstPoint.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (g) ход допустим — рисуем ребро, ищем новые грани, начисляем очки.
        DrawLine(firstPoint, point);
        edges.Add((firstPoint, point));

        // Одна линия закрывает только НОВЫЕ минимальные грани планарного графа
        // (максимум две — по одной с каждой стороны ребра). Никаких составных циклов.
        List<List<PointClick>> awarded = FindAllNewClosedShapes(firstPoint, point);
        bool gotShape = false;
        foreach (var shape in awarded)
        {
            // Фигура может состоять максимум из 3–4 точек. Считаем ВСЕ точки на границе
            // грани (shape.Count — до упрощения коллинеарных). Если их больше 4 — это
            // "большая" фигура, растянутая через промежуточные точки сетки (например
            // прямоугольный треугольник со стороной в 2 клетки, проходящей через среднюю
            // точку). Такую НЕ красим и НЕ начисляем, даже если после упрощения
            // коллинеарных точек она свелась бы к треугольнику/четырёхугольнику.
            if (shape.Count > 4)
            {
                if (debugEndGame) Debug.Log($"[Face] too many dots ({shape.Count}), ignoring: {FormatFace(shape)}");
                continue;
            }

            // Фигура НЕ завершена, если внутри неё (строго) осталась свободная точка сетки.
            // Например, ромб (1,1)-(2,2)-(1,3)-(0,2) со стороной √2 охватывает центральную
            // точку (1,2): это не минимальная фигура игры, а "недостроенная" область, которую
            // ещё нужно разбить, соединив внутреннюю точку с вершинами. Не красим и не
            // начисляем — как и Unknown, оставляем возможность доразбить её на валидные фигуры.
            if (EnclosesGridPoint(shape))
            {
                if (debugEndGame) Debug.Log($"[Face] encloses a free grid point, unfinished, ignoring: {FormatFace(shape)}");
                continue;
            }

            ShapeRecognizer.ShapeType type = ShapeRecognizer.Recognize(shape);

            // Красим и начисляем ТОЛЬКО фигуры из списка:
            //   прямоугольный/остроугольный треугольник, квадрат, параллелограмм.
            // Любая другая замкнутая область (Unknown: трапеция и т.п.) — НЕ фигура:
            // не красим, не начисляем и НЕ помечаем закрытой, чтобы её ещё можно было
            // разбить на валидные фигуры.
            if (type == ShapeRecognizer.ShapeType.Unknown)
            {
                if (debugEndGame) Debug.Log($"[Face] not a scorable figure, ignoring: {FormatFace(shape)}");
                continue;
            }

            // Идемпотентность: даже если грань как-то пришла дважды, второй раз её
            // уже не начислим (она попадёт в closedShapes сразу же ниже).
            if (ShapeAlreadyClosed(shape))
            {
                if (debugEndGame) Debug.Log($"[Face] face already awarded this move, skipping: {FormatFace(shape)}");
                continue;
            }
            closedShapes.Add(shape);
            gotShape = true;

            int pts = ShapeRecognizer.GetPoints(type);
            string name = ShapeRecognizer.GetName(type);

            AddScore(pts);
            Debug.Log($"[Face] new bounded face found: {FormatFace(shape)}, signed area = {SignedAreaGrid(shape):0.###}, classified as {name}, +{pts}");
            Debug.Log($"Player {currentPlayer} built {name} ({shape.Count} vertices)! +{pts} points");

            ClaimRegion(shape);
        }

        firstPoint.SetSelected(false, Color.white);
        firstPoint = null;

        // Если игрок замкнул хотя бы одну фигуру — ходит ещё раз.
        if (!gotShape)
            SwitchPlayer();

        if (debugEndGame) DiagnoseBoard();

        if (!isGameOver && !AnyLegalMoveRemains())
        {
            Debug.Log("No legal moves remain — game over.");
            EndGame();
        }
    }

    // Проверка "остался ли хоть один допустимый ход".
    // Ход допустим <=> точки соседние (8 направлений) И ребра ещё нет И оно
    // не пересекает существующие рёбра (общая вершина не считается пересечением).
    //
    // Self-test (мысленный, чтобы сверить логику):
    //   Сетка 3x3, все 12 ортогональных рёбер нарисованы.
    //     -> Диагонали свободны и ни одну ещё не рисовали, значит остаются ходы (YES).
    //   Дополнительно нарисованы все диагонали 4-х угловых клеток
    //   ((0,0)-(1,1),(1,0)-(0,1),(1,0)-(2,1),(2,0)-(1,1),(0,1)-(1,2),(1,1)-(0,2),
    //    (1,1)-(2,2),(2,1)-(1,2)) — в каждой клетке пересекаются обе диагонали,
    //     поэтому вторая диагональ каждой клетки уже не может быть добавлена без
    //     пересечения; свободных соседних пар не остаётся -> AnyLegalMoveRemains() = false.
    bool AnyLegalMoveRemains()
    {
        PointClick[] points = FindObjectsByType<PointClick>(FindObjectsSortMode.None);
        for (int i = 0; i < points.Length; i++)
        {
            for (int j = i + 1; j < points.Length; j++)
            {
                PointClick a = points[i];
                PointClick b = points[j];

                if (!AreAdjacent(a, b))
                {
                    if (debugEndGame) Debug.Log($"[EndGameCheck] pair ({a.gridX},{a.gridY})-({b.gridX},{b.gridY}): not adjacent");
                    continue;
                }
                if (EdgeExists(a, b))
                {
                    if (debugEndGame) Debug.Log($"[EndGameCheck] pair ({a.gridX},{a.gridY})-({b.gridX},{b.gridY}): edge already drawn");
                    continue;
                }
                if (WouldCrossExistingEdge(a, b))
                {
                    if (debugEndGame) Debug.Log($"[EndGameCheck] pair ({a.gridX},{a.gridY})-({b.gridX},{b.gridY}): crosses existing edge");
                    continue;
                }
                if (IsInsideAnyClaimedArea(a, b))
                {
                    if (debugEndGame) Debug.Log($"[EndGameCheck] pair ({a.gridX},{a.gridY})-({b.gridX},{b.gridY}): inside claimed area");
                    continue;
                }

                if (debugEndGame) Debug.Log($"[EndGameCheck] legal move found: ({a.gridX},{a.gridY})-({b.gridX},{b.gridY})");
                return true;
            }
        }
        if (debugEndGame) Debug.Log("[EndGameCheck] no legal moves found");
        return false;
    }

    // Полная диагностика доски: перебирает все соседние пары точек, для каждой
    // ещё не соединённой пары выясняет — свободна или каким ребром заблокирована.
    void DiagnoseBoard()
    {
        PointClick[] points = FindObjectsByType<PointClick>(FindObjectsSortMode.None);
        int adjacentUnconnected = 0;
        int legalCount = 0;

        Debug.Log($"[Diag] edges={edges.Count}, points={points.Length}");

        for (int i = 0; i < points.Length; i++)
        {
            for (int j = i + 1; j < points.Length; j++)
            {
                PointClick a = points[i];
                PointClick b = points[j];
                if (!AreAdjacent(a, b)) continue;
                if (EdgeExists(a, b)) continue;

                adjacentUnconnected++;
                var blocker = FindBlockingEdge(a, b);
                if (blocker.HasValue)
                {
                    var e = blocker.Value;
                    Debug.Log($"[Diag] ({a.gridX},{a.gridY})-({b.gridX},{b.gridY}) blocked by edge ({e.Item1.gridX},{e.Item1.gridY})-({e.Item2.gridX},{e.Item2.gridY})");
                }
                else if (IsInsideAnyClaimedArea(a, b))
                {
                    Debug.Log($"[Diag] ({a.gridX},{a.gridY})-({b.gridX},{b.gridY}) blocked by claimed area");
                }
                else
                {
                    legalCount++;
                    Debug.Log($"[Diag] !!! LEGAL MOVE EXISTS: ({a.gridX},{a.gridY})-({b.gridX},{b.gridY})");
                }
            }
        }

        Debug.Log($"[Diag] adjacent unconnected pairs: {adjacentUnconnected}, legal moves: {legalCount}");

        // Проверка непротиворечивости: AnyLegalMoveRemains должен согласоваться с diagnostic.
        bool anyLegalByCheck = AnyLegalMoveRemains();
        if (anyLegalByCheck && legalCount == 0)
            Debug.LogError("[Diag] MISMATCH: AnyLegalMoveRemains=true, but diagnostics found no legal moves!");
        if (!anyLegalByCheck && legalCount > 0)
            Debug.LogError("[Diag] MISMATCH: AnyLegalMoveRemains=false, but diagnostics found a legal move!");
    }

    (PointClick, PointClick)? FindBlockingEdge(PointClick a, PointClick b)
    {
        Vector2 p1 = a.transform.position;
        Vector2 p2 = b.transform.position;
        foreach (var e in edges)
        {
            if (e.Item1 == a || e.Item1 == b || e.Item2 == a || e.Item2 == b) continue;
            Vector2 p3 = e.Item1.transform.position;
            Vector2 p4 = e.Item2.transform.position;
            if (SegmentsIntersect(p1, p2, p3, p4)) return e;
        }
        return null;
    }

    bool AreAdjacent(PointClick a, PointClick b)
    {
        return Mathf.Max(Mathf.Abs(a.gridX - b.gridX), Mathf.Abs(a.gridY - b.gridY)) == 1
            && !(a.gridX == b.gridX && a.gridY == b.gridY);
    }

    bool WouldCrossExistingEdge(PointClick a, PointClick b) => EdgeCrossesExisting(a, b);

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
        // Общая вершина не считается пересечением (иначе никакие соседние линии,
        // сходящиеся в одной точке сетки, не могли бы сосуществовать).
        const float eps = 1e-4f;
        if (Approximately(p1, p3, eps) || Approximately(p1, p4, eps) ||
            Approximately(p2, p3, eps) || Approximately(p2, p4, eps))
            return false;

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

    static bool Approximately(Vector2 a, Vector2 b, float eps)
    {
        return Mathf.Abs(a.x - b.x) < eps && Mathf.Abs(a.y - b.y) < eps;
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
        Debug.Log("EndGame() called");
        isGameOver = true;

        if (gameOverUI == null)
        {
            Debug.LogError("GameManager.gameOverUI is not assigned! Drag the GameOverUI object into the gameOverUI field in the inspector.");
            return;
        }

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
        // Точки (sortingOrder = 10) должны быть поверх линий.
        lr.sortingOrder = 0;

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

    void ClaimRegion(List<PointClick> boundary)
    {
        List<Vector2> world = new List<Vector2>(boundary.Count);
        foreach (var p in boundary) world.Add(p.transform.position);

        Color owner = currentPlayer == 1 ? player1Color : player2Color;
        Color fill = new Color(owner.r, owner.g, owner.b, 0.4f);

        GameObject visual = PolygonFill.Create(world, fill, -1); // ниже линий (0), точки — 10

        ClaimedRegion region = new ClaimedRegion {
            boundaryPoints = new List<PointClick>(boundary),
            boundaryWorld = world,
            owner = currentPlayer,
            visual = visual
        };
        claimedRegions.Add(region);
    }

    // Линия проходит "сквозь" закрашенную область, если её середина (и/или четверти)
    // лежат строго внутри полигона какой-либо заявленной области.
    public bool IsInsideAnyClaimedArea(PointClick a, PointClick b)
    {
        if (claimedRegions.Count == 0) return false;

        Vector2 pa = a.transform.position;
        Vector2 pb = b.transform.position;
        Vector2[] samples = {
            Vector2.Lerp(pa, pb, 0.25f),
            Vector2.Lerp(pa, pb, 0.5f),
            Vector2.Lerp(pa, pb, 0.75f)
        };

        foreach (var region in claimedRegions)
        {
            // Пропускаем регион, границей которого сам является данное ребро.
            if (IsEdgeOnBorder(a, b, region)) continue;

            foreach (var s in samples)
                if (PolygonFill.PointInPolygon(s, region.boundaryWorld)) return true;
        }
        return false;
    }

    // Ребро совпадает с одной из сторон границы данной области.
    static bool IsEdgeOnBorder(PointClick a, PointClick b, ClaimedRegion region)
    {
        var bp = region.boundaryPoints;
        for (int i = 0; i < bp.Count; i++)
        {
            PointClick x = bp[i];
            PointClick y = bp[(i + 1) % bp.Count];
            if ((x == a && y == b) || (x == b && y == a)) return true;
        }
        return false;
    }

    public bool IsEdgeOnClaimedBorder(PointClick a, PointClick b)
    {
        foreach (var region in claimedRegions)
            if (IsEdgeOnBorder(a, b, region)) return true;
        return false;
    }

    // ============================================================================
    //  ПЛАНАРНОЕ ОПРЕДЕЛЕНИЕ ГРАНЕЙ  (fix для бага "закрашивается слишком много")
    // ----------------------------------------------------------------------------
    //  Корень проблемы:
    //    Старая версия перебирала ВСЕ простые циклы, проходящие через новое ребро
    //    (FindAllPaths), а затем эвристикой пыталась выбрать "минимальные". На плотной
    //    доске это возвращало БОЛЬШИЕ составные циклы, охватывающие уже закрашенные
    //    соседние клетки и пустое место, не являющееся реально замкнутой ячейкой.
    //    Следствие: заливка выходила за пределы новой грани, фигуры классифицировались
    //    как Unknown (+1), а некорректно "занятые" области ломали проверку конца игры.
    //
    //  Исправление:
    //    Трактуем нарисованные рёбра как ПЛАНАРНЫЙ граф. Новое ребро (a,b) создаёт
    //    ровно две смежные грани — слева от a→b и слева от b→a. Обходим каждую по
    //    правилу "следующее ребро по часовой стрелке" (next clockwise edge) и берём
    //    только ОГРАНИЧЕННЫЕ (bounded) грани — это и есть новые минимальные ячейки.
    //    Внешняя (бесконечная) грань отбрасывается по знаку ориентированной площади:
    //    в системе y-вверх ограниченная грань обходится против часовой (площадь > 0),
    //    внешняя — по часовой (площадь < 0).
    //
    //  Возвращает новые ограниченные грани (0, 1 или 2 штуки), готовые к заливке и
    //  распознаванию. Каждая грань уже минимальна (грань планарного графа), поэтому
    //  обычно это треугольник или четырёхугольник — никаких составных фигур.
    // ============================================================================
    List<List<PointClick>> FindAllNewClosedShapes(PointClick a, PointClick b)
    {
        var faces = new List<List<PointClick>>();

        // Две стороны нового ребра: грань слева от a→b и грань слева от b→a.
        TryAddFace(TraceFace(b, a), $"{a.gridX},{a.gridY}->{b.gridX},{b.gridY}", faces);
        TryAddFace(TraceFace(a, b), $"{b.gridX},{b.gridY}->{a.gridX},{a.gridY}", faces);

        return faces;
    }

    // Проверяет одну прослеженную грань и добавляет её в список, если она новая и ограниченная.
    void TryAddFace(List<PointClick> face, string walkLabel, List<List<PointClick>> faces)
    {
        if (face == null || face.Count < 3) return;

        float area = SignedAreaGrid(face);
        // area <= 0  =>  внешняя (бесконечная) или вырожденная грань — отбрасываем.
        if (area <= 1e-4f)
        {
            if (debugEndGame) Debug.Log($"[Face] discarded outer face from {walkLabel} walk");
            return;
        }

        if (ShapeAlreadyClosed(face))
        {
            if (debugEndGame) Debug.Log($"[Face] face already claimed, skipping: {FormatFace(face)}");
            return;
        }

        // Дедуп на случай, если обе стороны ребра дали одну и ту же грань
        // (мост/вырождение). Сравниваем по НОРМАЛИЗОВАННОЙ последовательности вершин,
        // а не по множеству, чтобы не склеить две разные грани с общими вершинами.
        foreach (var kept in faces)
            if (FacesEqual(kept, face)) return;

        faces.Add(face);
    }

    // Обходит грань планарного графа, начиная с направленного ребра prevVertex->startVertex,
    // всё время выбирая "следующее ребро по часовой стрелке" в каждой вершине.
    // Возвращает упорядоченный список вершин грани, либо null при обрыве/зависании.
    List<PointClick> TraceFace(PointClick startVertex, PointClick prevVertex)
    {
        var face = new List<PointClick>();
        PointClick u = prevVertex;   // откуда пришли
        PointClick v = startVertex;  // где находимся

        int guard = 0;
        int maxSteps = edges.Count * 2 + 16; // граней не больше, чем удвоенное число рёбер
        while (true)
        {
            face.Add(v);

            List<PointClick> sorted = GetSortedNeighborsByAngle(v);
            if (sorted.Count == 0) return null; // висячая вершина — грани нет

            int idx = sorted.IndexOf(u);
            if (idx < 0) return null; // u обязан быть соседом v; если нет — граф несогласован

            // "Следующее по часовой стрелке" = сосед, идущий непосредственно ПЕРЕД (v->u)
            // в порядке возрастания угла (с заворотом).
            PointClick w = sorted[(idx - 1 + sorted.Count) % sorted.Count];

            u = v;
            v = w;

            // Вернулись к стартовому направленному ребру prev->start — грань замкнута.
            if (u == prevVertex && v == startVertex) break;

            if (++guard > maxSteps) return null; // страховка от бесконечного цикла
        }
        return face;
    }

    // Соседи вершины v, отсортированные по возрастанию угла ребра (v -> сосед).
    // Угол считаем в координатах сетки (y-вверх, как и мир): atan2(dy, dx).
    List<PointClick> GetSortedNeighborsByAngle(PointClick v)
    {
        List<PointClick> neighbors = GetNeighbors(v);
        neighbors.Sort((p, q) =>
        {
            float angP = Mathf.Atan2(p.gridY - v.gridY, p.gridX - v.gridX);
            float angQ = Mathf.Atan2(q.gridY - v.gridY, q.gridX - v.gridX);
            return angP.CompareTo(angQ);
        });
        return neighbors;
    }

    // Ориентированная площадь по вершинам грани в координатах сетки (шнуровка).
    // Положительная => обход против часовой (ограниченная грань при y-вверх).
    static float SignedAreaGrid(List<PointClick> poly)
    {
        float a = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            PointClick p = poly[i];
            PointClick q = poly[(i + 1) % poly.Count];
            a += (float)p.gridX * q.gridY - (float)q.gridX * p.gridY;
        }
        return a * 0.5f;
    }

    // Возвращает true, если внутри грани (строго, не на границе) лежит хотя бы одна
    // точка сетки, не являющаяся вершиной грани. Такая грань — не завершённая фигура:
    // охваченная свободная точка ещё должна быть вовлечена (разбить грань на части).
    bool EnclosesGridPoint(List<PointClick> face)
    {
        PointClick[] all = FindObjectsByType<PointClick>(FindObjectsSortMode.None);
        foreach (var p in all)
        {
            if (face.Contains(p)) continue; // вершины границы не считаем внутренними
            if (PointStrictlyInsideFace(p, face)) return true;
        }
        return false;
    }

    // Проверка "точка строго внутри многоугольника" в координатах сетки (ray casting).
    // Вершины грани уже исключены вызывающим кодом, поэтому граничные случаи не важны.
    static bool PointStrictlyInsideFace(PointClick pt, List<PointClick> poly)
    {
        float x = pt.gridX, y = pt.gridY;
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = poly[i].gridX, yi = poly[i].gridY;
            float xj = poly[j].gridX, yj = poly[j].gridY;
            bool crosses = ((yi > y) != (yj > y))
                        && (x < (xj - xi) * (y - yi) / (yj - yi) + xi);
            if (crosses) inside = !inside;
        }
        return inside;
    }

    // "(x1,y1)-(x2,y2)-...-(xN,yN)" для диагностических логов.
    static string FormatFace(List<PointClick> face)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < face.Count; i++)
        {
            if (i > 0) sb.Append('-');
            sb.Append('(').Append(face[i].gridX).Append(',').Append(face[i].gridY).Append(')');
        }
        return sb.ToString();
    }

    // --- Нормализация грани для устойчивого сравнения (Bug 2) ---
    // Канон: старт с вершины с минимальным ключом (gridX+gridY, затем gridX, затем gridY);
    // из двух направлений обхода берём лексикографически меньшее. Две записи одной и той же
    // грани (в любом повороте/направлении) дают одинаковую нормализованную последовательность.
    static List<PointClick> NormalizeFace(List<PointClick> face)
    {
        int n = face.Count;
        int start = 0;
        for (int i = 1; i < n; i++)
            if (LessKey(face[i], face[start])) start = i;

        List<PointClick> fwd = new List<PointClick>(n);
        List<PointClick> bwd = new List<PointClick>(n);
        for (int i = 0; i < n; i++) fwd.Add(face[(start + i) % n]);
        for (int i = 0; i < n; i++) bwd.Add(face[(start - i + n) % n]);

        return SequenceLess(bwd, fwd) ? bwd : fwd;
    }

    static bool LessKey(PointClick a, PointClick b)
    {
        int sa = a.gridX + a.gridY, sb = b.gridX + b.gridY;
        if (sa != sb) return sa < sb;
        if (a.gridX != b.gridX) return a.gridX < b.gridX;
        return a.gridY < b.gridY;
    }

    static bool SequenceLess(List<PointClick> a, List<PointClick> b)
    {
        for (int i = 0; i < a.Count; i++)
        {
            if (LessKey(a[i], b[i])) return true;
            if (LessKey(b[i], a[i])) return false;
        }
        return false; // последовательности равны
    }

    static bool FacesEqual(List<PointClick> a, List<PointClick> b)
    {
        if (a.Count != b.Count) return false;
        var na = NormalizeFace(a);
        var nb = NormalizeFace(b);
        for (int i = 0; i < na.Count; i++)
            if (na[i] != nb[i]) return false;
        return true;
    }

    // Соседи по рёбрам, БЕЗ дублей (на случай, если в edges случайно попало
    // дублирующее ребро — иначе обход грани мог бы задвоиться).
    List<PointClick> GetNeighbors(PointClick p)
    {
        List<PointClick> neighbors = new List<PointClick>();
        foreach (var e in edges)
        {
            PointClick other = null;
            if (e.Item1 == p) other = e.Item2;
            else if (e.Item2 == p) other = e.Item1;
            if (other != null && !neighbors.Contains(other)) neighbors.Add(other);
        }
        return neighbors;
    }

    bool ShapeAlreadyClosed(List<PointClick> candidate)
    {
        // Сравниваем по нормализованной последовательности вершин (Bug 2).
        foreach (var shape in closedShapes)
            if (FacesEqual(shape, candidate)) return true;
        return false;
    }
}