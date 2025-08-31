using UnityEngine;
using Player;
using Util;

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
        private PlayerMover playerMover;
        private SpriteRenderer playerSprite;
        private Vector3 currentVelocity;
        private float idleTimer;
        private Vector3 wanderTarget;
        private float wanderTimer;
        private bool wandering;

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
            playerMover = null;
            playerSprite = null;
            if (player != null)
            {
                lastPlayerPos = player.position;
                playerMover = player.GetComponent<PlayerMover>();
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
                        wanderTarget = playerPos + (Vector3)Random.insideUnitCircle * wanderRadius;
                        wanderTimer = Random.Range(wanderDelayRange.x, wanderDelayRange.y);
                    }
                }

                newPos = Vector3.SmoothDamp(transform.position, wanderTarget, ref currentVelocity, smoothTime, wanderMoveSpeed, Time.fixedDeltaTime);
                velocity = currentVelocity;
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

    }
}
