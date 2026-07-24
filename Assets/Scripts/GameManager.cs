using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public GameObject linePrefab;

    // Per-player colors are configured in the inspector and fed into the IPlayer
    // instances constructed in Awake.
    [SerializeField] private Color player1Color = Color.red;
    [SerializeField] private Color player2Color = Color.green;

    // Player abstraction: index 0 = player 1, index 1 = player 2.
    private IPlayer[] players = new IPlayer[2];
    private int[] scores = new int[2];
    private int currentPlayerIndex = 0; // 0 or 1

    private IPlayer CurrentPlayer => players[currentPlayerIndex];
    private Color CurrentColor => CurrentPlayer.Color;

    // Backward-compatible public API used by TurnUI (thin delegates over the arrays).
    public int currentPlayer => currentPlayerIndex + 1; // 1 or 2
    public int player1Score => scores[0];
    public int player2Score => scores[1];

    public GameOverUI gameOverUI;
    public bool isGameOver = false;

    // Enable verbose console logs for the "any legal moves left?" check.
    // Temporarily enabled to diagnose end-of-game regressions.
    public bool debugEndGame = true;

    private PointClick firstPoint = null;
    private List<GameObject> allLines = new List<GameObject>();

    // Frame in which a click was already handled. One physical click = one frame,
    // so OnPointClicked does not run multiple times per frame (several
    // PointClick.Update may dispatch the same click — see Bug 2).
    private int lastClickFrame = -1;

    // All edges: a point pair is one line
    private List<(PointClick, PointClick)> edges = new List<(PointClick, PointClick)>();

    // Closed shapes already filled (avoid scoring twice)
    private List<List<PointClick>> closedShapes = new List<List<PointClick>>();

    // Claimed (filled) regions — lines cannot pass through them.
    public class ClaimedRegion
    {
        public List<PointClick> boundaryPoints;   // perimeter order
        public List<Vector2> boundaryWorld;       // same vertices in world space
        public int owner;                          // 1 or 2
        public GameObject visual;
    }
    private List<ClaimedRegion> claimedRegions = new List<ClaimedRegion>();

    void Awake()
    {
        Instance = this;

        // Construct the two human players. Colors come from the inspector fields.
        players[0] = new HumanPlayer(1, player1Color);
        players[1] = new HumanPlayer(2, player2Color);
        // TODO: AIPlayer — to add a computer opponent, replace one slot with
        //       new AIPlayer(id, color, ...) implementing IPlayer. No other GameManager
        //       change is required for turn/score/color handling.
    }

    void Start()
    {
        if (debugEndGame) RunSegmentSelfTest();
    }

    void RunSegmentSelfTest()
    {
        // 1) Diagonals (0,0)-(1,1) and (0,1)-(1,0) MUST intersect.
        bool t1 = SegmentsIntersect(new Vector2(0, 0), new Vector2(1, 1),
                                    new Vector2(0, 1), new Vector2(1, 0));
        Debug.Log($"[SelfTest] diagonal X-cross: {(t1 ? "PASS" : "FAIL")} (expected true)");

        // 2) Adjacent horizontals (0,0)-(1,0) and (1,0)-(2,0) — shared vertex, NOT intersection.
        bool t2 = SegmentsIntersect(new Vector2(0, 0), new Vector2(1, 0),
                                    new Vector2(1, 0), new Vector2(2, 0));
        Debug.Log($"[SelfTest] shared vertex: {(!t2 ? "PASS" : "FAIL")} (expected false)");

        // 3) Parallel non-adjacent (0,0)-(1,0) and (0,1)-(1,1) — do not intersect.
        bool t3 = SegmentsIntersect(new Vector2(0, 0), new Vector2(1, 0),
                                    new Vector2(0, 1), new Vector2(1, 1));
        Debug.Log($"[SelfTest] parallel separate: {(!t3 ? "PASS" : "FAIL")} (expected false)");
    }

    // Move validation order is strict (early exits). Adjacency is checked FIRST,
    // before draw/face detection/scoring — so a long line through an intermediate
    // grid point (e.g. (0,2)-(2,0) through (1,1)) cannot be added.
    public void OnPointClicked(PointClick point)
    {
        if (isGameOver) return;

        // Bug 2: at most one click processed per frame.
        if (Time.frameCount == lastClickFrame) return;
        lastClickFrame = Time.frameCount;

        // (a) first click — select a point.
        if (firstPoint == null)
        {
            firstPoint = point;
            point.SetSelected(true, GetCurrentColor());
            return;
        }

        // (b) second click on the same point — clear selection.
        if (firstPoint == point)
        {
            point.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (c) points must be adjacent (8 directions, max(|dx|,|dy|) == 1).
        //     WITHOUT switching player — invalid input, not a completed move.
        if (!AreAdjacent(firstPoint, point))
        {
            Debug.Log($"[Reject:NotAdjacent] Points are not adjacent: ({firstPoint.gridX},{firstPoint.gridY})-({point.gridX},{point.gridY})");
            firstPoint.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (d) that edge is already drawn.
        if (EdgeExists(firstPoint, point))
        {
            Debug.Log($"[Reject:EdgeExists] Line already exists: ({firstPoint.gridX},{firstPoint.gridY})-({point.gridX},{point.gridY})");
            firstPoint.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (e) new line crosses an existing one.
        if (WouldCrossExistingEdge(firstPoint, point))
        {
            Debug.Log($"[Reject:Crossing] Line crosses another: ({firstPoint.gridX},{firstPoint.gridY})-({point.gridX},{point.gridY})");
            firstPoint.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (f) new line passes through a claimed region.
        if (IsInsideAnyClaimedArea(firstPoint, point))
        {
            Debug.Log($"[Reject:InClaimed] Line passes through claimed area: ({firstPoint.gridX},{firstPoint.gridY})-({point.gridX},{point.gridY})");
            firstPoint.SetSelected(false, Color.white);
            firstPoint = null;
            return;
        }

        // (g) legal move — draw edge, find new faces, award points.
        DrawLine(firstPoint, point);
        edges.Add((firstPoint, point));

        // One line closes only NEW minimal faces of the planar graph
        // (at most two — one on each side of the edge). No composite cycles.
        List<List<PointClick>> awarded = FindAllNewClosedShapes(firstPoint, point);
        bool gotShape = false;
        foreach (var shape in awarded)
        {
            // A shape has at most 3–4 boundary points. Count ALL points on the face
            // boundary (shape.Count — before collinear simplification). If more than 4 —
            // a "large" shape stretched through intermediate grid points (e.g. a right
            // triangle with a 2-cell side through the middle point). Do NOT fill or score,
            // even if collinear simplification would reduce it to a triangle/quadrilateral.
            if (shape.Count > 4)
            {
                if (debugEndGame) Debug.Log($"[Face] too many dots ({shape.Count}), ignoring: {FormatFace(shape)}");
                continue;
            }

            // Shape is NOT complete if a free grid point lies strictly inside it.
            // E.g. diamond (1,1)-(2,2)-(1,3)-(0,2) with √2 side encloses center (1,2):
            // not a minimal game shape but an "unfinished" region that must be split by
            // connecting the inner point to vertices. Do not fill or score — like Unknown,
            // leave it splittable into valid shapes.
            if (EnclosesGridPoint(shape))
            {
                if (debugEndGame) Debug.Log($"[Face] encloses a free grid point, unfinished, ignoring: {FormatFace(shape)}");
                continue;
            }

            ShapeRecognizer.ShapeType type = ShapeRecognizer.Recognize(shape);

            // Fill and score ONLY shapes from the list:
            //   right/acute triangle, square, parallelogram.
            // Any other closed region (Unknown: trapezoid, etc.) is NOT a shape:
            // do not fill, score, or mark closed so it can still be split into valid shapes.
            if (type == ShapeRecognizer.ShapeType.Unknown)
            {
                if (debugEndGame) Debug.Log($"[Face] not a scorable figure, ignoring: {FormatFace(shape)}");
                continue;
            }

            // Idempotency: even if a face arrives twice, we won't score it again
            // (it is added to closedShapes immediately below).
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

        // If the player closed at least one shape — same player moves again.
        if (!gotShape)
            SwitchPlayer();

        if (debugEndGame) DiagnoseBoard();

        if (!isGameOver && !AnyLegalMoveRemains())
        {
            Debug.Log("No legal moves remain — game over.");
            EndGame();
        }
    }

    // Check whether any legal move remains.
    // Legal <=> points adjacent (8 directions) AND edge not drawn AND it does not
    // cross existing edges (shared vertex is not an intersection).
    //
    // Self-test (mental sanity check):
    //   3x3 grid, all 12 orthogonal edges drawn.
    //     -> Diagonals free and none drawn yet, so moves remain (YES).
    //   Plus all diagonals of the 4 corner cells
    //   ((0,0)-(1,1),(1,0)-(0,1),(1,0)-(2,1),(2,0)-(1,1),(0,1)-(1,2),(1,1)-(0,2),
    //    (1,1)-(2,2),(2,1)-(1,2)) — each cell has both diagonals crossing,
    //     so the second diagonal per cell cannot be added without intersection;
    //     no free adjacent pairs remain -> AnyLegalMoveRemains() = false.
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

    // Full board diagnostic: iterate all adjacent point pairs; for each unconnected pair,
    // determine whether it is free or blocked by an edge.
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

        // Consistency check: AnyLegalMoveRemains must agree with DiagnoseBoard.
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
        // Shared vertex is not an intersection (otherwise adjacent lines meeting at
        // one grid point could not coexist).
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

        if (scores[0] > scores[1])
            gameOverUI.Show(1, players[0].Color, scores[0], false);
        else if (scores[1] > scores[0])
            gameOverUI.Show(2, players[1].Color, scores[1], false);
        else
            gameOverUI.Show(0, Color.white, scores[0], true);
    }

    void DrawLine(PointClick a, PointClick b)
    {
        GameObject lineObj = Instantiate(linePrefab);
        LineRenderer lr = lineObj.GetComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, a.transform.position);
        lr.SetPosition(1, b.transform.position);
        // Points (sortingOrder = 10) must render above lines.
        lr.sortingOrder = 0;

        Color c = GetCurrentColor();
        lr.startColor = c;
        lr.endColor = c;

        allLines.Add(lineObj);
    }

    void SwitchPlayer()
    {
        currentPlayerIndex = 1 - currentPlayerIndex;
    }

    void AddScore(int points)
    {
        scores[currentPlayerIndex] += points;
    }

    public Color GetCurrentColor()
    {
        return CurrentColor;
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

        Color owner = CurrentColor;
        Color fill = new Color(owner.r, owner.g, owner.b, 0.4f);

        GameObject visual = PolygonFill.Create(world, fill, -1); // below lines (0), points — 10

        ClaimedRegion region = new ClaimedRegion {
            boundaryPoints = new List<PointClick>(boundary),
            boundaryWorld = world,
            owner = currentPlayer,
            visual = visual
        };
        claimedRegions.Add(region);
    }

    // A line passes "through" a claimed region if its midpoint (and/or quarter points)
    // lie strictly inside some claimed polygon.
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
            // Skip region whose border includes this edge.
            if (IsEdgeOnBorder(a, b, region)) continue;

            foreach (var s in samples)
                if (PolygonFill.PointInPolygon(s, region.boundaryWorld)) return true;
        }
        return false;
    }

    // Edge matches one side of the given region's boundary.
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
    //  PLANAR FACE DETECTION  (fix for "too much fill" bug)
    // ----------------------------------------------------------------------------
    //  Root cause:
    //    Old code enumerated ALL simple cycles through the new edge (FindAllPaths),
    //    then heuristically picked "minimal" ones. On a dense board this returned LARGE
    //    composite cycles covering already filled neighbor cells and empty space that
    //    is not a real closed cell. Result: fill spilled past the new face, shapes were
    //    classified as Unknown (+1), and incorrectly "occupied" regions broke end-game.
    //
    //  Fix:
    //    Treat drawn edges as a PLANAR graph. New edge (a,b) creates exactly two
    //    adjacent faces — left of a→b and left of b→a. Walk each using the
    //    "next clockwise edge" rule and keep only BOUNDED faces — the new minimal cells.
    //    The outer (infinite) face is dropped by signed area sign:
    //    with y-up, bounded faces are CCW (area > 0), outer is CW (area < 0).
    //
    //  Returns new bounded faces (0, 1, or 2), ready for fill and recognition. Each
    //  face is already minimal (planar graph face), usually a triangle or quadrilateral —
    //  no composite shapes.
    // ============================================================================
    List<List<PointClick>> FindAllNewClosedShapes(PointClick a, PointClick b)
    {
        var faces = new List<List<PointClick>>();

        // Two sides of the new edge: face left of a→b and face left of b→a.
        TryAddFace(TraceFace(b, a), $"{a.gridX},{a.gridY}->{b.gridX},{b.gridY}", faces);
        TryAddFace(TraceFace(a, b), $"{b.gridX},{b.gridY}->{a.gridX},{a.gridY}", faces);

        return faces;
    }

    // Validates one traced face and adds it if new and bounded.
    void TryAddFace(List<PointClick> face, string walkLabel, List<List<PointClick>> faces)
    {
        if (face == null || face.Count < 3) return;

        float area = SignedAreaGrid(face);
        // area <= 0  =>  outer (infinite) or degenerate face — discard.
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

        // Dedup when both sides of the edge yield the same face (bridge/degeneracy).
        // Compare NORMALIZED vertex sequences, not sets, to avoid merging distinct faces
        // that share vertices.
        foreach (var kept in faces)
            if (FacesEqual(kept, face)) return;

        faces.Add(face);
    }

    // Walks a planar graph face starting at directed edge prevVertex->startVertex,
    // always taking the "next clockwise edge" at each vertex.
    // Returns ordered face vertices, or null on break/infinite loop.
    List<PointClick> TraceFace(PointClick startVertex, PointClick prevVertex)
    {
        var face = new List<PointClick>();
        PointClick u = prevVertex;   // where we came from
        PointClick v = startVertex;  // current vertex

        int guard = 0;
        int maxSteps = edges.Count * 2 + 16; // at most twice the edge count
        while (true)
        {
            face.Add(v);

            List<PointClick> sorted = GetSortedNeighborsByAngle(v);
            if (sorted.Count == 0) return null; // dangling vertex — no face

            int idx = sorted.IndexOf(u);
            if (idx < 0) return null; // u must be a neighbor of v; otherwise graph is inconsistent

            // "Next clockwise" = neighbor immediately BEFORE (v->u) in increasing angle order (wrap).
            PointClick w = sorted[(idx - 1 + sorted.Count) % sorted.Count];

            u = v;
            v = w;

            // Back at start directed edge prev->start — face closed.
            if (u == prevVertex && v == startVertex) break;

            if (++guard > maxSteps) return null; // guard against infinite loop
        }
        return face;
    }

    // Neighbors of v sorted by edge angle (v -> neighbor).
    // Angle in grid coords (y-up, same as world): atan2(dy, dx).
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

    // Signed area of face vertices in grid coords (shoelace).
    // Positive => CCW traversal (bounded face with y-up).
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

    // True if at least one grid point lies strictly inside the face (not on the boundary)
    // and is not a face vertex. Such a face is incomplete — the enclosed free point
    // must still be connected (split the face).
    bool EnclosesGridPoint(List<PointClick> face)
    {
        PointClick[] all = FindObjectsByType<PointClick>(FindObjectsSortMode.None);
        foreach (var p in all)
        {
            if (face.Contains(p)) continue; // boundary vertices are not interior
            if (PointStrictlyInsideFace(p, face)) return true;
        }
        return false;
    }

    // "Point strictly inside polygon" in grid coords (ray casting).
    // Face vertices are excluded by caller, so boundary cases don't matter.
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

    // "(x1,y1)-(x2,y2)-...-(xN,yN)" for diagnostic logs.
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

    // --- Face normalization for stable comparison (Bug 2) ---
    // Canon: start at vertex with minimum key (gridX+gridY, then gridX, then gridY);
    // of the two walk directions take lexicographically smaller. Two records of the same
    // face (any rotation/direction) yield the same normalized sequence.
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
        return false; // sequences equal
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

    // Edge neighbors without duplicates (in case edges accidentally contains
    // a duplicate — otherwise face walk could double back).
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
        // Compare normalized vertex sequences (Bug 2).
        foreach (var shape in closedShapes)
            if (FacesEqual(shape, candidate)) return true;
        return false;
    }
}