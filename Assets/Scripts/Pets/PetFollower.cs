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

        private Transform player;
        private Vector3 offset;
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
            ChooseOffset();
        }

        private void FindPlayer()
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null)
                player = go.transform;
        }

        private void ChooseOffset()
        {
            offset = Random.insideUnitCircle.normalized * followRadius;
        }

        private void LateUpdate()
        {
            if (player == null)
            {
                FindPlayer();
                if (player == null)
                    return;
            }

            Vector3 target = player.position + offset;
            float dist = Vector3.Distance(transform.position, target);

            if (dist > maxDistance)
                target = player.position;

            Vector3 newPos = Vector3.SmoothDamp(transform.position, target, ref currentVelocity, smoothTime, moveSpeed, Time.deltaTime);
            newPos.y += Mathf.Sin(Time.time * 5f) * jitter;
            Vector2 velocity = currentVelocity;
            body.MovePosition(newPos);

            if (Vector3.Distance(transform.position, player.position) < followRadius * 0.5f)
                ChooseOffset();

            if (spriteAnimator != null)
                spriteAnimator.UpdateVisuals(velocity);
            else if (sprite != null)
                sprite.flipX = newPos.x > player.position.x;
        }
    }
}