using UnityEngine;
using Player;
using Player.Movement;
using Util;
using NPC;

namespace Pets
{
    /// <summary>
    /// Smoothly follows the player and transitions into short wander loops whenever the owner remains idle.
    /// Navigation-aware movement is delegated to <see cref="PetPathMover"/> which keeps the component lightweight
    /// while still honouring navigation grids produced by <see cref="PathfindingService"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(SpriteDepth))]
    [RequireComponent(typeof(PetPathMover))]
    public class PetFollower : MonoBehaviour
    {
        [Header("Follow")]
        public float followRadius = 0.6f;
        public float maxDistance = 2.0f;
        public float moveSpeed = 6f;
        public float smoothTime = 0.2f;
        public float offsetLerpSpeed = 5f;
        public float headingRefreshAngle = 30f;

        [Header("Idle Wander")]
        public float idleThreshold = 5f;
        public float wanderRadius = 3f;
        public Vector2 wanderDelayRange = new Vector2(1f, 3f);
        public float wanderMoveSpeed = 2f;

        [Header("Navigation")]
        [Tooltip("When enabled the pet samples wander points that align with the active navigation grid.")]
        [SerializeField] private bool respectNavigation = true;

        [Tooltip("Maximum radial distance used when sampling navigation-aware wander targets.")]
        [SerializeField, Min(0.1f)] private float navigationSampleRadius = 3f;

        [Tooltip("Number of attempts made when trying to locate a navigation friendly wander target.")]
        [SerializeField, Min(1)] private int navigationSampleAttempts = 6;

        [Tooltip("When disabled the follower always relies on smooth damp instead of navigation grids.")]
        [SerializeField] private bool useNavigationForFollowing = true;

        [SerializeField, Min(0.01f)] private float navigationWaypointArrivalThreshold = 0.05f;
        [SerializeField, Min(0.05f)] private float navigationFollowRebuildDistance = 0.75f;
        [SerializeField, Min(0.5f)] private float navigationFollowTeleportThreshold = 3f;

        [SerializeField] private Transform player;
        [SerializeField] private int depthOffset = 1;

        private Vector3 offset;
        private Vector3 targetOffset;
        private Vector2 lastHeading;
        private Vector3 lastPlayerPos;
        private Vector3 followAnchor;

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
        private PetPathMover pathMover;

        private static readonly Vector2Int[] FourWayOffsets =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        public Transform Player => player;

        private void Reset()
        {
            respectNavigation = true;
            navigationSampleRadius = wanderRadius;
            navigationSampleAttempts = 6;
            useNavigationForFollowing = true;
            navigationWaypointArrivalThreshold = 0.05f;
            navigationFollowRebuildDistance = 0.75f;
            navigationFollowTeleportThreshold = 3f;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            sprite = GetComponent<SpriteRenderer>();
            spriteDepth = GetComponent<SpriteDepth>();
            if (spriteDepth == null)
            {
                spriteDepth = gameObject.AddComponent<SpriteDepth>();
            }

            spriteDepth.offset = depthOffset;
            spriteAnimator = GetComponent<PetSpriteAnimator>();
            pathMover = GetComponent<PetPathMover>();
            if (pathMover != null)
            {
                pathMover.FollowAnchorResolver = () => followAnchor;
                pathMover.WanderDestinationResolver = () => (Vector2)wanderTarget;
            }

            if (player != null)
            {
                SetPlayer(player);
            }

            ChooseOffset(Vector2.right);
            offset = targetOffset;
        }

        public void SetPlayer(Transform newPlayer)
        {
            player = newPlayer;
            movementController = null;
            playerSprite = null;
            followAnchor = transform.position;

            if (pathMover != null)
            {
                pathMover.ResetFollowTracking();
                pathMover.ResetWanderTracking();
            }

            if (player != null)
            {
                lastPlayerPos = player.position;
                movementController = player.GetComponent<PlayerMovementController>()
                    ?? player.GetComponent<PlayerMover>()?.MovementController;
                playerSprite = player.GetComponent<SpriteRenderer>();
                if (playerSprite != null && sprite != null)
                {
                    sprite.sortingLayerID = playerSprite.sortingLayerID;
                }
            }
        }

        private void ChooseOffset(Vector2 heading)
        {
            if (heading.sqrMagnitude < 0.01f)
            {
                heading = Vector2.right;
            }

            targetOffset = (Vector3)(-heading.normalized * followRadius);
        }

        private void FixedUpdate()
        {
            if (player == null)
            {
                return;
            }

            Vector3 previousPlayerPos = lastPlayerPos;
            Vector3 playerPos = player.position;
            Vector3 playerVel = (playerPos - previousPlayerPos) / Time.fixedDeltaTime;
            lastPlayerPos = playerPos;
            bool playerMoving = playerVel.sqrMagnitude > 0.01f;

            NavGridBuilder activeGrid = PathfindingService.Instance?.ActiveGrid;
            bool navAvailable = activeGrid != null && activeGrid.HasGrid;

            if (!useNavigationForFollowing)
            {
                pathMover?.ResetFollowTracking();
            }

            if (!respectNavigation || !navAvailable)
            {
                pathMover?.ResetWanderTracking();
            }

            if (playerMoving)
            {
                idleTimer = 0f;
                if (wandering)
                {
                    wandering = false;
                    pathMover?.ResetWanderTracking();
                }

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
                    pathMover?.ResetFollowTracking();
                }
            }

            if (wandering)
            {
                HandleWander(playerPos, activeGrid, navAvailable);
                return;
            }

            HandleFollow(playerPos, playerMoving, navAvailable);
        }

        private void HandleFollow(Vector3 playerPos, bool playerMoving, bool navAvailable)
        {
            offset = Vector3.Lerp(offset, targetOffset, Time.fixedDeltaTime * offsetLerpSpeed);

            Vector3 currentPosition = transform.position;
            Vector3 desiredAnchor = playerPos + offset;
            float distanceToAnchor = Vector3.Distance(currentPosition, desiredAnchor);
            followAnchor = distanceToAnchor > maxDistance ? playerPos : desiredAnchor;

            bool navUsed = false;
            Vector2 navVelocity = Vector2.zero;

            if (useNavigationForFollowing && navAvailable && pathMover != null)
            {
                bool teleported;
                Vector2 navNext;
                if (pathMover.TryStepFollow(
                        Time.fixedDeltaTime,
                        moveSpeed,
                        Mathf.Max(0.1f, followRadius * 0.5f),
                        navigationWaypointArrivalThreshold,
                        navigationFollowRebuildDistance,
                        navigationFollowTeleportThreshold,
                        out navNext,
                        out navVelocity,
                        out teleported))
                {
                    Vector3 navPosition3D = new Vector3(navNext.x, navNext.y, currentPosition.z);
                    if (teleported)
                    {
                        body.position = navPosition3D;
                        transform.position = navPosition3D;
                    }
                    else
                    {
                        body.MovePosition(navPosition3D);
                    }

                    currentVelocity = new Vector3(navVelocity.x, navVelocity.y, 0f);
                    navUsed = true;
                }
            }

            if (!navUsed)
            {
                Vector3 smoothPosition = Vector3.SmoothDamp(
                    currentPosition,
                    followAnchor,
                    ref currentVelocity,
                    smoothTime,
                    moveSpeed,
                    Time.fixedDeltaTime);

                body.MovePosition(smoothPosition);
            }

            if (Vector3.Distance(transform.position, playerPos) < followRadius * 0.5f)
            {
                ChooseOffset(lastHeading);
            }

            Vector2 visualVelocity = navUsed
                ? navVelocity
                : new Vector2(currentVelocity.x, currentVelocity.y);

            UpdateVisuals(visualVelocity, playerMoving, navUsed);
        }

        private void HandleWander(Vector3 playerPos, NavGridBuilder grid, bool navAvailable)
        {
            Vector3 currentPosition = transform.position;

            if (Vector3.Distance(currentPosition, wanderTarget) < 0.1f)
            {
                wanderTimer -= Time.fixedDeltaTime;
                if (wanderTimer <= 0f)
                {
                    wanderTarget = SampleWanderTarget(playerPos, navAvailable && respectNavigation ? grid : null);
                    wanderTimer = Random.Range(wanderDelayRange.x, wanderDelayRange.y);
                    pathMover?.ResetWanderTracking();
                }
            }

            bool navUsed = false;
            Vector2 navVelocity = Vector2.zero;

            if (respectNavigation && navAvailable && pathMover != null)
            {
                Vector2 navNext;
                if (pathMover.TryStepWander(
                        Time.fixedDeltaTime,
                        wanderMoveSpeed,
                        navigationWaypointArrivalThreshold,
                        navigationWaypointArrivalThreshold,
                        out navNext,
                        out navVelocity))
                {
                    Vector3 navPosition3D = new Vector3(navNext.x, navNext.y, currentPosition.z);
                    body.MovePosition(navPosition3D);
                    currentVelocity = new Vector3(navVelocity.x, navVelocity.y, 0f);
                    navUsed = true;
                }

                if (pathMover.HasPendingWanderFailure)
                {
                    pathMover.ConsumePendingWanderFailure();
                    wanderTarget = SampleWanderTarget(playerPos, grid);
                    pathMover.ResetWanderTracking();
                    navUsed = false;
                }
            }

            if (!navUsed)
            {
                Vector3 smoothPosition = Vector3.SmoothDamp(
                    currentPosition,
                    wanderTarget,
                    ref currentVelocity,
                    smoothTime,
                    wanderMoveSpeed,
                    Time.fixedDeltaTime);

                body.MovePosition(smoothPosition);
            }

            Vector2 visualVelocity = navUsed
                ? navVelocity
                : new Vector2(currentVelocity.x, currentVelocity.y);

            UpdateVisuals(visualVelocity, playerMoving: false, navUsed);
        }

        private Vector3 SampleWanderTarget(Vector3 playerPos, NavGridBuilder grid)
        {
            Vector3 origin = transform.position;
            float baseRadius = Mathf.Max(0.1f, wanderRadius);
            float sampleRadius = respectNavigation && grid != null
                ? Mathf.Min(baseRadius, navigationSampleRadius > 0f ? navigationSampleRadius : baseRadius)
                : baseRadius;

            for (int attempt = 0; attempt < navigationSampleAttempts; attempt++)
            {
                Vector2 randomOffset = Random.insideUnitCircle * sampleRadius;
                Vector2 candidate = origin + (Vector3)randomOffset;

                if (grid == null)
                {
                    return candidate;
                }

                if (grid.TryGetCell(candidate, out var cell) && grid.IsCellWalkable(cell))
                {
                    return grid.GetCellCenter(cell);
                }

                Vector2Int clampedCell = grid.WorldToCellClamped(candidate);
                if (grid.IsCellWalkable(clampedCell))
                {
                    return grid.GetCellCenter(clampedCell);
                }
            }

            if (grid != null)
            {
                Vector2Int playerCell = grid.WorldToCellClamped(playerPos);
                if (grid.IsCellWalkable(playerCell))
                {
                    return grid.GetCellCenter(playerCell);
                }

                foreach (Vector2Int offset in FourWayOffsets)
                {
                    Vector2Int neighbour = playerCell + offset;
                    if (grid.IsCellWithinBounds(neighbour) && grid.IsCellWalkable(neighbour))
                    {
                        return grid.GetCellCenter(neighbour);
                    }
                }
            }

            return playerPos;
        }

        private void UpdateVisuals(Vector2 velocity, bool playerMoving, bool navUsed)
        {
            if (spriteAnimator != null)
            {
                if (!playerMoving && movementController != null)
                {
                    spriteAnimator.SetFacing(movementController.FacingDirection);
                }

                spriteAnimator.UpdateVisuals(velocity);
                return;
            }

            if (sprite == null)
            {
                return;
            }

            if (!playerMoving && !navUsed && movementController != null)
            {
                sprite.flipX = Direction8Utility.IsFacingLeft(movementController.FacingDirection);
            }
            else if (navUsed)
            {
                sprite.flipX = velocity.x > 0f;
            }
            else if (player != null)
            {
                sprite.flipX = transform.position.x > player.position.x;
            }
        }
    }
}
