using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using NPC;
using UnityEngine;

/// <summary>
/// Regression tests ensuring diagonal navigation honours blocker tiles and still reaches targets.
/// </summary>
public sealed class NpcPathfindingServiceDiagonalTests
{
    private static readonly FieldInfo AreaSizeField = typeof(NavGridBuilder)
        .GetField("areaSize", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Configures the grid dimensions before forcing a rebuild so tests can operate on compact lattices.
    /// </summary>
    private static void ConfigureGridSize(NavGridBuilder grid, Vector2 areaSize)
    {
        Assert.IsNotNull(grid, "Grid reference must be provided for pathfinding tests.");
        Assert.IsNotNull(AreaSizeField, "NavGridBuilder.areaSize field could not be located via reflection.");
        AreaSizeField.SetValue(grid, areaSize);
        grid.BuildGrid();
    }

    /// <summary>
    /// Executes a path request and advances the service manually until the mover receives a response.
    /// </summary>
    private static TestMover RequestAndResolve(PathfindingService service, NavGridBuilder grid, Vector2Int startCell, Vector2Int goalCell)
    {
        var mover = new TestMover();
        Vector2 startWorld = grid.GetCellCenter(startCell);
        Vector2 goalWorld = grid.GetCellCenter(goalCell);
        int requestId = service.RequestPath(mover, startWorld, goalWorld);

        for (int i = 0; i < 64 && !mover.HasResult; i++)
        {
            service.OnTick();
        }

        Assert.IsTrue(mover.HasResult, "Path request {0} never completed during the test.", requestId);
        return mover;
    }

    /// <summary>
    /// Converts returned world positions into grid coordinates for easier verification.
    /// </summary>
    private static List<Vector2Int> ConvertWorldPathToCells(NavGridBuilder grid, List<Vector2> worldPath)
    {
        var cells = new List<Vector2Int>();
        if (worldPath == null)
        {
            return cells;
        }

        for (int i = 0; i < worldPath.Count; i++)
        {
            cells.Add(grid.WorldToCellClamped(worldPath[i]));
        }

        return cells;
    }

    [Test]
    public void DiagonalMovement_ReachesGoalWhenClear()
    {
        var gridGo = new GameObject("TestNavGrid");
        var serviceGo = new GameObject("TestPathfindingService");
        var grid = gridGo.AddComponent<NavGridBuilder>();
        var service = serviceGo.AddComponent<PathfindingService>();

        try
        {
            ConfigureGridSize(grid, new Vector2(3f, 3f));
            service.RegisterNavGrid(grid);

            Vector2Int startCell = new Vector2Int(1, 1);
            Vector2Int goalCell = new Vector2Int(2, 2);

            TestMover mover = RequestAndResolve(service, grid, startCell, goalCell);

            Assert.AreEqual(PathfindingService.PathStatus.Success, mover.Status, "Path request should succeed when diagonals are open.");

            List<Vector2Int> cells = ConvertWorldPathToCells(grid, mover.Path);
            Assert.AreEqual(1, cells.Count, "Direct diagonal traversal should only require a single waypoint.");
            Assert.AreEqual(goalCell, cells[0], "Mover should step directly into the goal cell via the diagonal.");
        }
        finally
        {
            Object.DestroyImmediate(serviceGo);
            Object.DestroyImmediate(gridGo);
        }
    }

    [Test]
    public void DiagonalMovement_RespectsCornerBlockers()
    {
        var gridGo = new GameObject("TestNavGrid");
        var serviceGo = new GameObject("TestPathfindingService");
        var grid = gridGo.AddComponent<NavGridBuilder>();
        var service = serviceGo.AddComponent<PathfindingService>();

        try
        {
            ConfigureGridSize(grid, new Vector2(3f, 3f));
            service.RegisterNavGrid(grid);

            Vector2Int startCell = new Vector2Int(1, 1);
            Vector2Int goalCell = new Vector2Int(2, 2);
            Vector2Int blockedFlank = new Vector2Int(2, 1);

            bool overrideApplied = grid.TrySetManualOverride(blockedFlank, false);
            Assert.IsTrue(overrideApplied, "Failed to apply manual override that simulates the corner blocker.");

            TestMover mover = RequestAndResolve(service, grid, startCell, goalCell);

            Assert.AreEqual(PathfindingService.PathStatus.Success, mover.Status, "Pathfinder should still reach the goal by routing around the blocker.");

            List<Vector2Int> cells = ConvertWorldPathToCells(grid, mover.Path);
            Assert.AreEqual(2, cells.Count, "The mover should take two orthogonal steps around the obstacle.");
            Assert.AreEqual(new Vector2Int(1, 2), cells[0], "First step should move north to avoid the blocked east tile.");
            Assert.AreEqual(goalCell, cells[1], "Second step should reach the goal after clearing the corner.");
        }
        finally
        {
            Object.DestroyImmediate(serviceGo);
            Object.DestroyImmediate(gridGo);
        }
    }

    /// <summary>
    /// Lightweight mover used by the tests to capture asynchronous path results.
    /// </summary>
    private sealed class TestMover : IPathMoverClient
    {
        public bool HasResult { get; private set; }

        public PathfindingService.PathStatus Status { get; private set; }

        public List<Vector2> Path { get; private set; }

        public Vector2 ResolvedGoal { get; private set; }

        public void HandlePathResult(int requestId, PathfindingService.PathStatus status, List<Vector2> worldPath, Vector2 resolvedGoalWorld)
        {
            HasResult = true;
            Status = status;
            Path = worldPath;
            ResolvedGoal = resolvedGoalWorld;
        }
    }
}
