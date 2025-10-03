using System.Collections.Generic;
using UnityEngine;
using Player;
using Player.Movement;
using Util;
using NPC;

namespace Pets
{
    /// <summary>
    /// Smoothly follows the player with a small trailing offset.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(SpriteDepth))]
    public class PetFollower : MonoBehaviour
    {
        public float followRadius = 0.6f;
        public float maxDistance = 2.0f;
        public float moveSpeed = 6f;
        public float smoothTime = 0.2f;
        public float offsetLerpSpeed = 5f;
        public float headingRefreshAngle = 30f;

        public float idleThreshold = 5f;
        public float wanderRadius = 3f;
        public Vector2 wanderDelayRange = new Vector2(1f, 3f);
        public float wanderMoveSpeed = 2f;

        /// <summary>
        /// When enabled the pet will consult the active NPC navigation grid while wandering so it
        /// never steps through blocked tiles. Disable to revert to freeform radial wandering for
        /// scenes that do not provide nav data.
        /// </summary>
        [SerializeField] private bool respectNavigation = true;

        /// <summary>
        /// Maximum world-space radius used when sampling wander destinations while navigation is
        /// respected. This is clamped against <see cref="wanderRadius"/> so designers can keep both
        /// behaviours aligned.
        /// </summary>
        [SerializeField, Min(0.1f)] private float navigationSampleRadius = 3f;

        /// <summary>
        /// Number of times the component will attempt to locate a valid nav-aware wander destination
        /// before falling back to the legacy random motion. Higher values increase robustness but also
        /// add extra BFS checks.
        /// </summary>
        [SerializeField, Min(1)] private int navigationSampleAttempts = 6;

        /// <summary>
        /// Distance in world units considered close enough to a queued navigation waypoint for it to
        /// be consumed. This mirrors the tolerance used by <see cref="NPC.NpcWanderer"/> so pets feel
        /// consistent with NPC motion.
        /// </summary>
        [SerializeField, Min(0.01f)] private float navigationWaypointArrivalThreshold = 0.05f;

        [SerializeField] private Transform player;
        [SerializeField] private int depthOffset = 1;
        public Transform Player => player;
        private Vector3 offset;
        private Vector3 targetOffset;
        private Vector2 lastHeading;
        private Vector3 lastPlayerPos;
        private Rigidbody2D body;
        private SpriteRenderer sprite;
        private SpriteDepth spriteDepth;
        private PetSpriteAnimator spriteAnimator;
        private IPlayerMovementController movementController;
        private SpriteRenderer playerSprite;
        private Vector3 currentVelocity;
        private float idleTimer;
        private Vector3 wanderTarget;
        private float wanderTimer;
        private bool wandering;
        private bool usingNavPath;
        private Vector2 navFinalDestination;
        private Vector2Int navFinalCell;
        private readonly Queue<Vector2> navWanderWaypoints = new();
        private readonly Queue<Vector2Int> navNearestFrontier = new();
        private readonly HashSet<Vector2Int> navNearestVisited = new();
        private readonly Queue<Vector2Int> navPathFrontier = new();
        private readonly HashSet<Vector2Int> navPathVisited = new();
        private readonly Dictionary<Vector2Int, Vector2Int> navPathCameFrom = new();
        private readonly List<Vector2Int> navPathBuffer = new();

        private static readonly Vector2Int[] FourWayOffsets =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        private void Reset()
        {
            respectNavigation = true;
            navigationSampleRadius = wanderRadius;
            navigationSampleAttempts = 6;
            navigationWaypointArrivalThreshold = 0.05f;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            sprite = GetComponent<SpriteRenderer>();
            spriteDepth = GetComponent<SpriteDepth>();
            if (spriteDepth == null)
                spriteDepth = gameObject.AddComponent<SpriteDepth>();
            spriteDepth.offset = depthOffset;
            spriteAnimator = GetComponent<PetSpriteAnimator>();
            if (player != null)
                SetPlayer(player);
            ChooseOffset(Vector2.right);
            offset = targetOffset;
        }

        public void SetPlayer(Transform newPlayer)
        {
            player = newPlayer;
            movementController = null;
            playerSprite = null;
            if (player != null)
            {
                lastPlayerPos = player.position;
                movementController = player.GetComponent<PlayerMovementController>()
                    ?? player.GetComponent<PlayerMover>()?.MovementController;
                playerSprite = player.GetComponent<SpriteRenderer>();
                if (playerSprite != null && sprite != null)
                    sprite.sortingLayerID = playerSprite.sortingLayerID;
            }
        }

        private void ChooseOffset(Vector2 heading)
        {
            if (heading.sqrMagnitude < 0.01f)
                heading = Vector2.right;
            targetOffset = (Vector3)(-heading.normalized * followRadius);
        }

        private void FixedUpdate()
        {
            if (player == null)
                return;

            Vector3 playerPos = player.position;
            Vector3 playerVel = (playerPos - lastPlayerPos) / Time.fixedDeltaTime;
            lastPlayerPos = playerPos;

            bool playerMoving = playerVel.sqrMagnitude > 0.01f;

            if (playerMoving)
            {
                idleTimer = 0f;
                wandering = false;
                usingNavPath = false;
                navWanderWaypoints.Clear();
                Vector2 heading = ((Vector2)playerVel).normalized;
                if (lastHeading == Vector2.zero || Vector2.Angle(lastHeading, heading) > headingRefreshAngle)
                {
                    ChooseOffset(heading);
                    lastHeading = heading;
                }
            }
            else
            {
                idleTimer += Time.fixedDeltaTime;
                if (!wandering && idleTimer >= idleThreshold)
                {
                    wandering = true;
                    wanderTarget = transform.position;
                    wanderTimer = Random.Range(wanderDelayRange.x, wanderDelayRange.y);
                    usingNavPath = false;
                    navWanderWaypoints.Clear();
                }
            }

            Vector3 newPos;
            Vector2 velocity;

            if (wandering)
            {
                Vector3 currentPosition = transform.position;
                if (Vector3.Distance(currentPosition, wanderTarget) < 0.1f)
                {
                    wanderTimer -= Time.fixedDeltaTime;
                    if (wanderTimer <= 0f)
                    {
                        bool resolvedPath = TryResolveNavWanderPath(playerPos);
                        usingNavPath = resolvedPath && navWanderWaypoints.Count > 0;
                        if (usingNavPath)
                        {
                            wanderTarget = navFinalDestination;
                        }
                        else
                        {
                            wanderTarget = playerPos + (Vector3)Random.insideUnitCircle * wanderRadius;
                        }
                        wanderTimer = Random.Range(wanderDelayRange.x, wanderDelayRange.y);
                    }
                }

                if (usingNavPath)
                {
                    Vector2 current2D = currentPosition;
                    NavGridBuilder grid = respectNavigation ? PathfindingService.Instance?.ActiveGrid : null;
                    if (grid == null || !grid.HasGrid)
                    {
                        usingNavPath = false;
                        navWanderWaypoints.Clear();
                        newPos = Vector3.SmoothDamp(currentPosition, wanderTarget, ref currentVelocity, smoothTime, wanderMoveSpeed, Time.fixedDeltaTime);
                        velocity = currentVelocity;
                    }
                    else
                    {
                        Vector2 nextPos = current2D;
                        if (navWanderWaypoints.Count > 0)
                        {
                            Vector2 waypoint = navWanderWaypoints.Peek();
                            Vector2 stepped = Vector2.MoveTowards(current2D, waypoint, wanderMoveSpeed * Time.fixedDeltaTime);
                            Vector2Int currentCell = grid.TryGetCell(current2D, out var cell) ? cell : grid.WorldToCellClamped(current2D);
                            Vector2Int steppedCell = grid.TryGetCell(stepped, out var steppedLookup) ? steppedLookup : grid.WorldToCellClamped(stepped);
                            nextPos = steppedCell != currentCell ? grid.GetCellCenter(steppedCell) : stepped;

                            if (Vector2.Distance(nextPos, waypoint) <= navigationWaypointArrivalThreshold)
                            {
                                navWanderWaypoints.Dequeue();
                                if (navWanderWaypoints.Count == 0)
                                {
                                    nextPos = grid.GetCellCenter(navFinalCell);
                                }
                            }
                        }
                        else
                        {
                            nextPos = grid.GetCellCenter(navFinalCell);
                        }

                        newPos = new Vector3(nextPos.x, nextPos.y, currentPosition.z);
                        velocity = (nextPos - current2D) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
                        currentVelocity = velocity;
                    }
                }
                else
                {
                    newPos = Vector3.SmoothDamp(currentPosition, wanderTarget, ref currentVelocity, smoothTime, wanderMoveSpeed, Time.fixedDeltaTime);
                    velocity = currentVelocity;
                }

                body.MovePosition(newPos);

                if (spriteAnimator != null)
                    spriteAnimator.UpdateVisuals(velocity);
                else if (sprite != null)
                    sprite.flipX = velocity.x > 0f;

                return;
            }

            offset = Vector3.Lerp(offset, targetOffset, Time.fixedDeltaTime * offsetLerpSpeed);

            Vector3 target = playerPos + offset;
            float dist = Vector3.Distance(transform.position, target);

            if (dist > maxDistance)
                target = playerPos;

            newPos = Vector3.SmoothDamp(transform.position, target, ref currentVelocity, smoothTime, moveSpeed, Time.fixedDeltaTime);

            velocity = currentVelocity;
            body.MovePosition(newPos);

            if (Vector3.Distance(transform.position, playerPos) < followRadius * 0.5f)
                ChooseOffset(lastHeading);

            if (spriteAnimator != null)
            {
                if (!playerMoving && movementController != null)
                    spriteAnimator.SetFacing(movementController.FacingDirection);
                spriteAnimator.UpdateVisuals(playerMoving ? velocity : Vector2.zero);
            }
            else if (sprite != null)
            {
                if (!playerMoving && movementController != null)
                    sprite.flipX = Direction8Utility.IsFacingLeft(movementController.FacingDirection);
                else
                    sprite.flipX = newPos.x > player.position.x;
            }

    }

        /// <summary>
        /// Attempts to build a navigation-aware wander path that keeps the pet aligned with walkable
        /// nav grid tiles. When navigation data is unavailable the method returns <c>false</c> so the
        /// caller can fall back to the legacy freeform wander behaviour.
        /// </summary>
        /// <param name="origin">World position used as the centre for sampling candidate points.</param>
        /// <returns>True if a queued navigation path was produced.</returns>
        private bool TryResolveNavWanderPath(Vector2 origin)
        {
            navWanderWaypoints.Clear();

            if (!respectNavigation)
                return false;

            NavGridBuilder grid = PathfindingService.Instance?.ActiveGrid;
            if (grid == null || !grid.HasGrid)
                return false;

            float baseRadius = Mathf.Max(0.1f, wanderRadius);
            float navRadius = navigationSampleRadius > 0f ? navigationSampleRadius : baseRadius;
            float sampleRadius = Mathf.Min(baseRadius, navRadius > 0f ? navRadius : baseRadius);
            float searchRadius = Mathf.Max(navRadius, sampleRadius);
            int maxCellRadius = Mathf.Max(1, Mathf.CeilToInt(searchRadius / Mathf.Max(grid.TileSize, 0.0001f)));

            Vector2 currentPos = transform.position;
            Vector2Int startCell = grid.TryGetCell(currentPos, out var lookupStart) ? lookupStart : grid.WorldToCellClamped(currentPos);
            if (!grid.IsCellWalkable(startCell) && !TryFindNearestWalkableCell(startCell, grid, maxCellRadius, out startCell))
                return false;

            for (int attempt = 0; attempt < navigationSampleAttempts; attempt++)
            {
                Vector2 candidate = origin + Random.insideUnitCircle * sampleRadius;
                Vector2Int goalCell = grid.TryGetCell(candidate, out var lookupGoal) ? lookupGoal : grid.WorldToCellClamped(candidate);

                if (!grid.IsCellWalkable(goalCell))
                {
                    if (!TryFindNearestWalkableCell(goalCell, grid, maxCellRadius, out goalCell))
                        continue;
                }

                if (!IsWithinCellRadius(startCell, goalCell, maxCellRadius))
                {
                    continue;
                }

                if (!TryBuildPath(startCell, goalCell, grid, maxCellRadius))
                {
                    continue;
                }

                if (navWanderWaypoints.Count == 0)
                {
                    continue;
                }

                navFinalCell = goalCell;
                navFinalDestination = grid.GetCellCenter(goalCell);
                return true;
            }

            navWanderWaypoints.Clear();
            return false;
        }

        /// <summary>
        /// Mirrors the NPC wanderer helper by scanning for the nearest walkable nav cell around a
        /// provided seed.
        /// </summary>
        private bool TryFindNearestWalkableCell(Vector2Int seed, NavGridBuilder grid, int maxCellRadius, out Vector2Int result)
        {
            Queue<Vector2Int> frontier = navNearestFrontier;
            HashSet<Vector2Int> visited = navNearestVisited;
            frontier.Clear();
            visited.Clear();
            frontier.Enqueue(seed);
            visited.Add(seed);

            while (frontier.Count > 0)
            {
                Vector2Int current = frontier.Dequeue();
                if (!grid.IsCellWithinBounds(current))
                    continue;

                if (!IsWithinCellRadius(seed, current, maxCellRadius))
                    continue;

                if (grid.IsCellWalkable(current))
                {
                    result = current;
                    return true;
                }

                for (int i = 0; i < FourWayOffsets.Length; i++)
                {
                    Vector2Int neighbour = current + FourWayOffsets[i];
                    if (!grid.IsCellWithinBounds(neighbour))
                        continue;
                    if (!IsWithinCellRadius(seed, neighbour, maxCellRadius))
                        continue;
                    if (!visited.Add(neighbour))
                        continue;

                    frontier.Enqueue(neighbour);
                }
            }

            result = seed;
            return false;
        }

        /// <summary>
        /// Performs a BFS over four-way neighbours to ensure the sampled wander goal is reachable and
        /// populates the queued waypoints when successful.
        /// </summary>
        private bool TryBuildPath(Vector2Int startCell, Vector2Int goalCell, NavGridBuilder grid, int maxCellRadius)
        {
            if (startCell == goalCell)
                return false;

            Queue<Vector2Int> frontier = navPathFrontier;
            HashSet<Vector2Int> visited = navPathVisited;
            Dictionary<Vector2Int, Vector2Int> cameFrom = navPathCameFrom;
            List<Vector2Int> buffer = navPathBuffer;
            frontier.Clear();
            visited.Clear();
            cameFrom.Clear();
            buffer.Clear();

            frontier.Enqueue(startCell);
            visited.Add(startCell);

            while (frontier.Count > 0)
            {
                Vector2Int current = frontier.Dequeue();
                if (current == goalCell)
                {
                    buffer.Add(current);
                    while (cameFrom.TryGetValue(current, out Vector2Int parent))
                    {
                        buffer.Add(parent);
                        current = parent;
                    }

                    navWanderWaypoints.Clear();
                    for (int i = buffer.Count - 2; i >= 0; i--)
                    {
                        Vector2Int cell = buffer[i];
                        navWanderWaypoints.Enqueue(grid.GetCellCenter(cell));
                    }

                    return navWanderWaypoints.Count > 0;
                }

                for (int i = 0; i < FourWayOffsets.Length; i++)
                {
                    Vector2Int neighbour = current + FourWayOffsets[i];
                    if (!grid.IsCellWithinBounds(neighbour))
                        continue;
                    if (!IsWithinCellRadius(startCell, neighbour, maxCellRadius))
                        continue;
                    if (!grid.IsCellWalkable(neighbour))
                        continue;
                    if (!visited.Add(neighbour))
                        continue;

                    cameFrom[neighbour] = current;
                    frontier.Enqueue(neighbour);
                }
            }

            return false;
        }

        private static bool IsWithinCellRadius(Vector2Int origin, Vector2Int candidate, int radius)
        {
            return Mathf.Abs(candidate.x - origin.x) <= radius && Mathf.Abs(candidate.y - origin.y) <= radius;
        }

}
}
