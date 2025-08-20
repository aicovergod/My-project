using UnityEngine;
using Util;

namespace NPC
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class NpcWanderer : MonoBehaviour, ITickable
    {
        [Header("Movement Bounds")]
        [Tooltip("Local-space minimum offset from the start position where the NPC may wander.")]
        public Vector2 minOffset = new Vector2(-5f, -5f);
        [Tooltip("Local-space maximum offset from the start position where the NPC may wander.")]
        public Vector2 maxOffset = new Vector2(5f, 5f);

        [Header("Movement")]
        public float moveSpeed = 2f;
        [Tooltip("Consider we have arrived when within this distance to the target.")]
        public float arriveDistance = 0.05f;
        [Tooltip("Minimum idle time before choosing a new target.")]
        public float minIdleTime = 0.5f;
        [Tooltip("Maximum idle time before choosing a new target.")]
        public float maxIdleTime = 2f;

        [Header("Visuals")]
        [Tooltip("Component handling sprite animation/animator updates.")]
        public NpcSpriteAnimator spriteAnimator;

        private Rigidbody2D _rb;
        private Vector2 _origin;
        private Vector2 _target;
        private bool _waiting;
        private float _waitTimer;
        private Vector2 _lastPos;
        private Transform _combatTarget;

        private void Reset()
        {
            spriteAnimator = GetComponent<NpcSpriteAnimator>() ?? GetComponentInChildren<NpcSpriteAnimator>();
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null) _rb.bodyType = RigidbodyType2D.Kinematic;
            if (spriteAnimator == null)
                spriteAnimator = GetComponent<NpcSpriteAnimator>() ?? GetComponentInChildren<NpcSpriteAnimator>();
        }

        private void Start()
        {
            _origin = _rb != null ? _rb.position : (Vector2)transform.position;
            _lastPos = _origin;
            BeginIdle();
        }

        private void OnEnable()
        {
            Ticker.Instance?.Subscribe(this);
        }

        private void OnDisable()
        {
            Ticker.Instance?.Unsubscribe(this);
        }

        private void BeginIdle()
        {
            _waiting = true;
            _waitTimer = Random.Range(minIdleTime, maxIdleTime);
            spriteAnimator?.UpdateVisuals(Vector2.zero);
        }

        private void ChooseNewTarget()
        {
            float x = Random.Range(minOffset.x, maxOffset.x);
            float y = Random.Range(minOffset.y, maxOffset.y);
            _target = _origin + new Vector2(x, y);
            _waiting = false;
        }

        public void EnterCombat(Transform target)
        {
            _combatTarget = target;
            _target = _rb != null ? _rb.position : (Vector2)transform.position;
            _waiting = false;
        }

        public void ExitCombat()
        {
            _combatTarget = null;
            BeginIdle();
        }

        public void OnTick()
        {
            if (_combatTarget != null)
            {
                Vector2 current = _rb != null ? _rb.position : (Vector2)transform.position;
                Vector2 dir = ((Vector2)_combatTarget.position) - current;
                if (spriteAnimator != null)
                {
                    spriteAnimator.UpdateVisuals(dir);
                    spriteAnimator.UpdateVisuals(Vector2.zero);
                }
                _lastPos = current;
                return;
            }

            float delta = Ticker.TickDuration;
            if (_waiting)
            {
                _waitTimer -= delta;
                if (_waitTimer <= 0f)
                    ChooseNewTarget();
                spriteAnimator?.UpdateVisuals(Vector2.zero);
                return;
            }

            Vector2 currentPos = _rb != null ? _rb.position : (Vector2)transform.position;
            Vector2 next = Vector2.MoveTowards(currentPos, _target, moveSpeed * delta);
            Vector2 velocity = (next - _lastPos) / delta;
            if (_rb != null) _rb.MovePosition(next);
            else transform.position = next;
            if (Vector2.Distance(next, _target) <= arriveDistance)
                BeginIdle();
            spriteAnimator?.UpdateVisuals(velocity);
            _lastPos = next;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Vector2 center = Application.isPlaying ? _origin : (Vector2)transform.position;
            Vector2 size = maxOffset - minOffset;
            Vector2 gizmoCenter = center + (minOffset + maxOffset) * 0.5f;
            Gizmos.DrawWireCube(gizmoCenter, size);
        }
#endif
    }
}

