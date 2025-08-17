using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Player;

namespace Pets
{
    /// <summary>
    /// Smoothly follows the player with a small trailing offset.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(Collider2D))]
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

        [SerializeField] private Transform player;
        private Vector3 offset;
        private Vector3 targetOffset;
        private Vector2 lastHeading;
        private Vector3 lastPlayerPos;
        private Rigidbody2D body;
        private SpriteRenderer sprite;
        private PetSpriteAnimator spriteAnimator;
        private Collider2D col;
        private PlayerMover playerMover;
        private Vector3 currentVelocity;
        private float idleTimer;
        private Vector3 wanderTarget;
        private float wanderTimer;
        private bool wandering;
        private int blockedLayers;
        private NavMeshPath navPath;
        private readonly List<Vector3> pathCorners = new List<Vector3>();
        private int pathIndex;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            sprite = GetComponent<SpriteRenderer>();
            spriteAnimator = GetComponent<PetSpriteAnimator>();
            col = GetComponent<Collider2D>();
            if (player != null)
            {
                lastPlayerPos = player.position;
                playerMover = player.GetComponent<PlayerMover>();
                IgnorePlayer();
            }
            IgnoreNPCs();
            ChooseOffset(Vector2.right);
            offset = targetOffset;
            blockedLayers = LayerMask.GetMask("Obstacle", "Interactable");
            navPath = new NavMeshPath();
        }

        public void SetPlayer(Transform newPlayer)
        {
            player = newPlayer;
            if (player != null)
            {
                lastPlayerPos = player.position;
                playerMover = player.GetComponent<PlayerMover>();
                IgnorePlayer();
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
                }
            }

            Vector3 newPos;
            Vector2 velocity;

            if (wandering)
            {
                if (Vector3.Distance(transform.position, wanderTarget) < 0.1f)
                {
                    wanderTimer -= Time.fixedDeltaTime;
                    if (wanderTimer <= 0f)
                    {
                        wanderTarget = GetWanderTarget(playerPos);
                        wanderTimer = Random.Range(wanderDelayRange.x, wanderDelayRange.y);
                    }
                }

                newPos = Vector3.SmoothDamp(transform.position, wanderTarget, ref currentVelocity, smoothTime, wanderMoveSpeed, Time.fixedDeltaTime);
                velocity = currentVelocity;
                newPos = MoveWithCollisions(newPos, out bool hit);
                body.MovePosition(newPos);
                if (hit)
                {
                    wanderTimer = 0f;
                    wanderTarget = transform.position;
                }

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

            bool blocked = PathBlocked(target);
            if (pathCorners.Count == 0 && blocked)
                CalculatePath(playerPos);

            Vector3 moveTarget = pathCorners.Count > 0 ? pathCorners[pathIndex] : target;

            newPos = Vector3.SmoothDamp(transform.position, moveTarget, ref currentVelocity, smoothTime, moveSpeed, Time.fixedDeltaTime);

            velocity = currentVelocity;
            newPos = MoveWithCollisions(newPos, out _);
            body.MovePosition(newPos);

            if (pathCorners.Count > 0)
            {
                if (!blocked)
                {
                    pathCorners.Clear();
                }
                else if (Vector3.Distance(transform.position, moveTarget) < 0.1f)
                {
                    pathIndex++;
                    if (pathIndex >= pathCorners.Count)
                        pathCorners.Clear();
                }
            }

            if (Vector3.Distance(transform.position, playerPos) < followRadius * 0.5f)
                ChooseOffset(lastHeading);

            if (spriteAnimator != null)
            {
                if (!playerMoving && playerMover != null)
                    spriteAnimator.SetFacing(playerMover.FacingDir);
                spriteAnimator.UpdateVisuals(playerMoving ? velocity : Vector2.zero);
            }
            else if (sprite != null)
            {
                if (!playerMoving && playerMover != null)
                    sprite.flipX = playerMover.FacingDir == 1;
                else
                    sprite.flipX = newPos.x > player.position.x;
            }
        }

        private void IgnorePlayer()
        {
            if (player == null || col == null)
                return;
            foreach (var pCol in player.GetComponentsInChildren<Collider2D>())
                Physics2D.IgnoreCollision(col, pCol);
        }

        private void IgnoreNPCs()
        {
            if (col == null)
                return;
            var cols = FindObjectsOfType<Collider2D>();
            foreach (var c in cols)
            {
                if (c == col)
                    continue;
                if (c.gameObject.name.Contains("NPC"))
                    Physics2D.IgnoreCollision(col, c);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.collider.gameObject.name.Contains("NPC"))
                Physics2D.IgnoreCollision(col, collision.collider);
        }

        private Vector3 GetWanderTarget(Vector3 center)
        {
            float radius = col != null ? col.bounds.extents.x : 0.1f;
            for (int i = 0; i < 10; i++)
            {
                Vector3 candidate = center + (Vector3)Random.insideUnitCircle * wanderRadius;
                if (!Physics2D.OverlapCircle(candidate, radius, blockedLayers))
                    return candidate;
            }
            return center;
        }
        private bool PathBlocked(Vector3 targetPos)
        {
            if (col == null)
                return false;
            Vector3 currentPos = transform.position;
            Vector2 dir = targetPos - currentPos;
            float dist = dir.magnitude;
            float radius = col.bounds.extents.x;
            return Physics2D.CircleCast(currentPos, radius, dir.normalized, dist, blockedLayers);
        }

        private void CalculatePath(Vector3 destination)
        {
            if (NavMesh.CalculatePath(transform.position, destination, NavMesh.AllAreas, navPath))
            {
                pathCorners.Clear();
                pathCorners.AddRange(navPath.corners);
                pathIndex = 0;
                for (int i = 0; i < pathCorners.Count; i++)
                {
                    Vector3 corner = pathCorners[i];
                    corner.z = transform.position.z;
                    pathCorners[i] = corner;
                }
            }
        }

        private Vector3 MoveWithCollisions(Vector3 targetPos, out bool hit)
        {
            Vector3 currentPos = transform.position;
            Vector2 dir = targetPos - currentPos;
            float dist = dir.magnitude;
            hit = false;
            if (dist <= 0f || col == null)
                return targetPos;
            float radius = col.bounds.extents.x;
            RaycastHit2D cast = Physics2D.CircleCast(currentPos, radius, dir.normalized, dist, blockedLayers);
            if (cast.collider != null)
            {
                hit = true;
                return cast.point - dir.normalized * radius;
            }
            return targetPos;
        }
    }
}