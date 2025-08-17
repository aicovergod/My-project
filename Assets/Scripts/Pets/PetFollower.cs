using UnityEngine;

namespace Pets
{
    /// <summary>
    /// Smoothly follows the player with a small trailing offset.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class PetFollower : MonoBehaviour
    {
        public float followRadius = 0.6f;
        public float maxDistance = 2.0f;
        public float moveSpeed = 6f;
        public float jitter = 0.05f;
        public float smoothTime = 0.2f;
        public float offsetLerpSpeed = 5f;
        public float headingRefreshAngle = 30f;

        private Transform player;
        private Vector3 offset;
        private Vector3 targetOffset;
        private Vector2 lastHeading;
        private Vector3 lastPlayerPos;
        private Rigidbody2D body;
        private SpriteRenderer sprite;
        private PetSpriteAnimator spriteAnimator;
        private Vector3 currentVelocity;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            sprite = GetComponent<SpriteRenderer>();
            spriteAnimator = GetComponent<PetSpriteAnimator>();
            FindPlayer();
            if (player != null)
                lastPlayerPos = player.position;
            ChooseOffset(Vector2.right);
            offset = targetOffset;
        }

        private void FindPlayer()
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null)
                player = go.transform;
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
            {
                FindPlayer();
                if (player == null)
                    return;
            }

            Vector3 playerPos = player.position;
            Vector3 playerVel = (playerPos - lastPlayerPos) / Time.fixedDeltaTime;
            lastPlayerPos = playerPos;

            if (playerVel.sqrMagnitude > 0.01f)
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
            newPos.y += Mathf.Sin(Time.time * 5f) * jitter;
            Vector2 velocity = currentVelocity;
            body.MovePosition(newPos);

            if (Vector3.Distance(transform.position, playerPos) < followRadius * 0.5f)
                ChooseOffset(lastHeading);

            if (spriteAnimator != null)
                spriteAnimator.UpdateVisuals(velocity);
            else if (sprite != null)
                sprite.flipX = newPos.x > player.position.x;
        }
    }
}