using UnityEngine;
using Util;

namespace NPC
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class NpcRandomMovement : MonoBehaviour, ITickable
    {
        [Header("Movement Area")]
        [Tooltip("Width and height of the wandering area centered on the start position.")]
        public Vector2 areaSize = new Vector2(5f, 5f);

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

        // Per-tick interpolation
        private Vector2 _from;
        private Vector2 _to;
        private float _lerpTime;

        private void Reset()
        {
            spriteAnimator = GetComponent<NpcSpriteAnimator>() ?? GetComponentInChildren<NpcSpriteAnimator>();
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null) _rb.bodyType = RigidbodyType2D.Kinematic;

            if (spriteAnimator == null) spriteAnimator = GetComponent<NpcSpriteAnimator>() ?? GetComponentInChildren<NpcSpriteAnimator>();
        }

        private void Start()
        {
            _origin = _rb != null ? _rb.position : (Vector2)transform.position;
            _lastPos = _origin;
            _from = _to = _origin;
            _lerpTime = Ticker.TickDuration;
            BeginIdle();
        }

        private void BeginIdle()
        {
            _waiting = true;
            _waitTimer = Random.Range(minIdleTime, maxIdleTime);
            if (spriteAnimator != null) spriteAnimator.UpdateVisuals(Vector2.zero);
        }

        private void ChooseNewTarget()
        {
            Vector2 half = areaSize * 0.5f;
            Vector2 randomOffset = new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
            _target = _origin + randomOffset;
            _waiting = false;
        }

        private void OnEnable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Subscribe(this);
        }

        private void OnDisable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
        }

        public void OnTick()
        {
            float delta = Ticker.TickDuration;
            if (_waiting)
            {
                _waitTimer -= delta;
                if (_waitTimer <= 0f)
                    ChooseNewTarget();
                _lerpTime = delta; // idle until next target
                if (spriteAnimator != null) spriteAnimator.UpdateVisuals(Vector2.zero);
                return;
            }

            _from = _rb != null ? _rb.position : (Vector2)transform.position;
            _to = Vector2.MoveTowards(_from, _target, moveSpeed * delta);
            _lerpTime = 0f;

            if (Vector2.Distance(_to, _target) <= arriveDistance)
                BeginIdle();
        }

        private void Update()
        {
            if (_lerpTime >= Ticker.TickDuration)
            {
                if (spriteAnimator != null) spriteAnimator.UpdateVisuals(Vector2.zero);
                return;
            }

            _lerpTime += Time.deltaTime;
            float t = Mathf.Clamp01(_lerpTime / Ticker.TickDuration);
            Vector2 pos = Vector2.Lerp(_from, _to, t);
            if (_rb != null) _rb.MovePosition(pos);
            else transform.position = pos;

            Vector2 velocity = (pos - _lastPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            if (spriteAnimator != null) spriteAnimator.UpdateVisuals(velocity);
            _lastPos = pos;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
            Vector3 center = Application.isPlaying ? (Vector3)_origin : transform.position;
            Gizmos.DrawWireCube(center, new Vector3(areaSize.x, areaSize.y, 0f));
        }
#endif
    }
}

