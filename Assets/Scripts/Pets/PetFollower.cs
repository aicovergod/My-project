using UnityEngine;
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
                Vector2 heading = ((Vector2)playerVel).normalized;
                if (lastHeading == Vector2.zero || Vector2.Angle(lastHeading, heading) > headingRefreshAngle)
                {
                    ChooseOffset(heading);
                    lastHeading = heading;
                }
            }

            offset = Vector3.Lerp(offset, targetOffset, Time.fixedDeltaTime * offsetLerpSpeed);

            Vector3 target = playerPos + offset;
            float dist = Vector3.Distance(transform.position, target);

            if (dist > maxDistance)
                target = playerPos;

            Vector3 newPos = Vector3.SmoothDamp(transform.position, target, ref currentVelocity, smoothTime, moveSpeed, Time.fixedDeltaTime);

            Vector2 velocity = currentVelocity;
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
    }
}