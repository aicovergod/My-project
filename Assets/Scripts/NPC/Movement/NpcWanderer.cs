using UnityEngine;
using Util;
using Combat;
using System.Collections.Generic;

namespace NPC
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class NpcWanderer : MonoBehaviour, ITickable
    {
        [Header("Movement Bounds")]
        [Tooltip("If true, uses an area size centered on the start position instead of explicit offsets.")]
        public bool useAreaSize;
        [Tooltip("Width and height of the wandering area centered on the start position.")]
        public Vector2 areaSize = new Vector2(10f, 10f);
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

        [Header("Chasing")]
        [Tooltip("Maximum distance from the spawn position that the NPC may chase a target.")]
        public float chaseRadius = 5f;
        public float AggroRadius => chaseRadius;

        [Header("Visuals")]
        [Tooltip("Component handling sprite animation/animator updates.")]
        public NpcSpriteAnimator spriteAnimator;

        private Rigidbody2D _rb;
        private Vector2 _origin;
        private Vector2 _target;
        private bool _waiting;
        private float _waitTimer;
        private Vector2 _lastPos;
        private readonly System.Collections.Generic.List<Transform> _combatTargets = new();

        // Per-tick interpolation
        private Vector2 _from;
        private Vector2 _to;
        private float _lerpTime;

        private float ComputeChaseRadius()
        {
            if (useAreaSize)
            {
                Vector2 half = areaSize * 0.5f;
                return half.magnitude;
            }

            Vector2[] corners = new Vector2[4]
            {
                minOffset,
                maxOffset,
                new Vector2(minOffset.x, maxOffset.y),
                new Vector2(maxOffset.x, minOffset.y)
            };
            float max = 0f;
            foreach (var c in corners)
                max = Mathf.Max(max, c.magnitude);
            return max;
        }

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
            _from = _to = _origin;
            _lerpTime = Ticker.TickDuration;

            chaseRadius = ComputeChaseRadius();

            BeginIdle();
        }

        public void SetOrigin(Vector2 origin)
        {
            _origin = origin;
            _lastPos = origin;
            _from = _to = origin;
            chaseRadius = ComputeChaseRadius();
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
            if (useAreaSize)
            {
                Vector2 half = areaSize * 0.5f;
                float x = Random.Range(-half.x, half.x);
                float y = Random.Range(-half.y, half.y);
                _target = _origin + new Vector2(x, y);
            }
            else
            {
                float x = Random.Range(minOffset.x, maxOffset.x);
                float y = Random.Range(minOffset.y, maxOffset.y);
                _target = _origin + new Vector2(x, y);
            }
            _waiting = false;
        }

        public void EnterCombat(Transform target)
        {
            if (!_combatTargets.Contains(target))
                _combatTargets.Add(target);
            _target = _rb != null ? _rb.position : (Vector2)transform.position;
            _waiting = false;
        }

        public void ExitCombat(Transform target)
        {
            _combatTargets.Remove(target);
            if (_combatTargets.Count == 0)
                BeginIdle();
        }

        public void ExitCombat()
        {
            _combatTargets.Clear();
            BeginIdle();
        }

        public void ForceReturnToOrigin()
        {
            _target = _origin;
            _waiting = false;
        }

        public void OnTick()
        {
            float delta = Ticker.TickDuration;

            if (_combatTargets.Count > 0)
            {
                _from = _rb != null ? _rb.position : (Vector2)transform.position;
                Transform closest = null;
                float best = float.MaxValue;
                foreach (var t in _combatTargets)
                {
                    float d = Vector2.Distance(_from, t.position);
                    if (d < best)
                    {
                        best = d;
                        closest = t;
                    }
                }
                if (closest != null)
                {
                    Vector2 targetPos = closest.position;
                    if (best > CombatMath.MELEE_RANGE)
                    {
                        Vector2 direction = (targetPos - _from).normalized;
                        Vector2 desired = targetPos - direction * CombatMath.MELEE_RANGE;
                        Vector2 step = Vector2.MoveTowards(_from, desired, moveSpeed * delta);
                        if (useAreaSize)
                        {
                            Vector2 half = areaSize * 0.5f;
                            float minX = _origin.x - half.x;
                            float maxX = _origin.x + half.x;
                            float minY = _origin.y - half.y;
                            float maxY = _origin.y + half.y;
                            _to = new Vector2(
                                Mathf.Clamp(step.x, minX, maxX),
                                Mathf.Clamp(step.y, minY, maxY));
                        }
                        else
                        {
                            float minX = _origin.x + minOffset.x;
                            float maxX = _origin.x + maxOffset.x;
                            float minY = _origin.y + minOffset.y;
                            float maxY = _origin.y + maxOffset.y;
                            _to = new Vector2(
                                Mathf.Clamp(step.x, minX, maxX),
                                Mathf.Clamp(step.y, minY, maxY));
                        }
                    }
                    else
                    {
                        _to = _from;
                    }
                    _lerpTime = 0f;
                    return;
                }
            }

            if (_waiting)
            {
                _waitTimer -= delta;
                if (_waitTimer <= 0f)
                    ChooseNewTarget();
                _lerpTime = delta; // idle
                spriteAnimator?.UpdateVisuals(Vector2.zero);
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
                spriteAnimator?.UpdateVisuals(Vector2.zero);
                return;
            }

            _lerpTime += Time.deltaTime;
            float t = Mathf.Clamp01(_lerpTime / Ticker.TickDuration);
            Vector2 pos = Vector2.Lerp(_from, _to, t);
            if (_rb != null) _rb.MovePosition(pos);
            else transform.position = pos;

            Vector2 velocity = (pos - _lastPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            spriteAnimator?.UpdateVisuals(velocity);
            _lastPos = pos;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Vector2 center = Application.isPlaying ? _origin : (Vector2)transform.position;
            if (useAreaSize)
            {
                Gizmos.DrawWireCube(center, new Vector3(areaSize.x, areaSize.y, 0f));
            }
            else
            {
                Vector2 size = maxOffset - minOffset;
                Vector2 gizmoCenter = center + (minOffset + maxOffset) * 0.5f;
                Gizmos.DrawWireCube(gizmoCenter, size);
            }
        }
#endif
    }
}

