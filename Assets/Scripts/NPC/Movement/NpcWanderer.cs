using UnityEngine;
using Util;
using Combat;
using System.Collections;
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

        private static readonly Vector2Int[] FourWayOffsets =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        private Rigidbody2D _rb;
        private Vector2 _origin;
        private bool _originInitialized;
        private Vector2 _target;
        private bool _waiting;
        private float _waitTimer;
        private Vector2 _lastPos;
        private readonly System.Collections.Generic.List<Transform> _combatTargets = new();
        private NpcCombatant _combatant;

        // Per-tick interpolation
        private Vector2 _from;
        private Vector2 _to;
        private float _lerpTime;
        private bool _frozen;

        // Knockback state
        private bool _knockbackActive;
        private float _knockbackTimer;
        private float _knockbackDuration;
        private Vector2 _knockbackStart;
        private Vector2 _knockbackEnd;
        private AnimationCurve _knockbackCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        private bool _knockbackClamp;

        // Ticker subscription management
        private Coroutine _tickerSubscriptionRoutine;
        private bool _tickerSubscribed;

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
            _combatant = GetComponent<NpcCombatant>();
            if (spriteAnimator == null)
                spriteAnimator = GetComponent<NpcSpriteAnimator>() ?? GetComponentInChildren<NpcSpriteAnimator>();
        }

        private void Start()
        {
            _origin = _rb != null ? _rb.position : (Vector2)transform.position;
            _originInitialized = true;
            _lastPos = _origin;
            _from = _to = _origin;
            _lerpTime = Ticker.TickDuration;

            chaseRadius = ComputeChaseRadius();

            BeginIdle();
        }

        public void SetOrigin(Vector2 origin)
        {
            CancelKnockback();
            _origin = origin;
            _originInitialized = true;
            _lastPos = origin;
            _from = _to = origin;
            chaseRadius = ComputeChaseRadius();
        }

        private void OnEnable()
        {
            EnsureTickerSubscription();
        }

        private void OnDisable()
        {
            CancelKnockback();
            ReleaseTickerSubscription();
        }

        private void BeginIdle()
        {
            CancelKnockback();
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
            _target = ClampToMovementBounds(_target);
            _waiting = false;
        }

        public void EnterCombat(Transform target)
        {
            CancelKnockback();
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
            CancelKnockback();
            _target = ClampToMovementBounds(_origin);
            _waiting = false;
        }

        /// <summary>
        /// Synchronises the wanderer to an externally supplied world position (for example when a
        /// navigation system teleports or walks the NPC). This ensures future wander ticks start from
        /// the provided coordinate instead of continuing toward an obsolete target.
        /// </summary>
        /// <param name="worldPosition">The desired world position to align to the wander loop.</param>
        public void SyncToExternalPosition(Vector2 worldPosition)
        {
            CancelKnockback();

            Vector2 clamped = ClampToMovementBounds(worldPosition);

            if (_rb != null)
            {
                _rb.position = clamped;
#if UNITY_2023_1_OR_NEWER
                _rb.linearVelocity = Vector2.zero;
#else
                _rb.velocity = Vector2.zero;
#endif
            }
            else
            {
                transform.position = new Vector3(clamped.x, clamped.y, transform.position.z);
            }

            _from = clamped;
            _to = clamped;
            _lastPos = clamped;
            _target = clamped;
            _lerpTime = Ticker.TickDuration;
            _waiting = false;
            _waitTimer = 0f;
            spriteAnimator?.UpdateVisuals(Vector2.zero);
        }

        /// <summary>
        /// Applies a knockback impulse handled by this wanderer. The NPC will interpolate
        /// between the current position and the resolved destination using the supplied curve.
        /// </summary>
        public void ApplyKnockback(Vector2 direction, float distance, float duration, bool clamp, AnimationCurve curve)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon || distance <= 0f)
                return;

            Vector2 current = _rb != null ? _rb.position : (Vector2)transform.position;
            CancelKnockback();

            Vector2 normalisedDirection = direction.normalized;
            Vector2 destination = current + normalisedDirection * distance;
            if (clamp)
                destination = ClampToMovementBounds(destination);

            if (Vector2.Distance(current, destination) <= Mathf.Epsilon)
                return;

            _knockbackStart = current;
            _knockbackEnd = destination;
            _knockbackDuration = Mathf.Max(0.01f, duration);
            _knockbackTimer = 0f;
            _knockbackCurve = curve != null && curve.length > 0 ? curve : AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            _knockbackClamp = clamp;
            _knockbackActive = true;
            _waiting = false;
            _from = current;
            _to = current;
            _lerpTime = Ticker.TickDuration;
        }

        /// <summary>
        /// Aborts any active knockback motion and locks the NPC to its current position.
        /// </summary>
        public void CancelKnockback()
        {
            bool wasActive = _knockbackActive;
            _knockbackActive = false;
            _knockbackTimer = 0f;

            if (!wasActive)
                return;

            Vector2 current = _rb != null ? _rb.position : (Vector2)transform.position;
            _knockbackStart = current;
            _knockbackEnd = current;
            _from = current;
            _to = current;
            _lerpTime = Ticker.TickDuration;
            _lastPos = current;
            spriteAnimator?.UpdateVisuals(Vector2.zero);
        }

        /// <summary>True when a freeze effect is preventing the NPC from moving.</summary>
        public bool IsFrozen => _frozen;

        /// <summary>
        /// Enables or disables the frozen state, halting movement immediately when active.
        /// </summary>
        public void SetFrozen(bool frozen)
        {
            if (_frozen == frozen)
                return;

            _frozen = frozen;
            if (_frozen)
            {
                _from = _rb != null ? _rb.position : (Vector2)transform.position;
                _to = _from;
#if UNITY_2023_1_OR_NEWER
                if (_rb != null)
                    _rb.linearVelocity = Vector2.zero;
#else
                if (_rb != null)
                    _rb.velocity = Vector2.zero;
#endif
                spriteAnimator?.UpdateVisuals(Vector2.zero);
            }
            else
            {
                _lerpTime = Ticker.TickDuration;
            }
        }

        /// <summary>
        /// Ensures the component is registered with the global <see cref="Ticker"/>. When the ticker
        /// has not spawned yet (for example, immediately after returning from the login scene) this will
        /// wait until the singleton becomes available before subscribing so wandering resumes normally.
        /// </summary>
        private void EnsureTickerSubscription()
        {
            if (_tickerSubscribed)
                return;

            if (Ticker.Instance != null)
            {
                Ticker.Instance.Subscribe(this);
                _tickerSubscribed = true;
            }
            else if (_tickerSubscriptionRoutine == null && isActiveAndEnabled)
            {
                _tickerSubscriptionRoutine = StartCoroutine(WaitForTickerAndSubscribe());
            }
        }

        /// <summary>
        /// Cancels any pending ticker waiters and unsubscribes from the <see cref="Ticker"/> when the
        /// component is disabled so it does not continue receiving ticks unexpectedly.
        /// </summary>
        private void ReleaseTickerSubscription()
        {
            if (_tickerSubscriptionRoutine != null)
            {
                StopCoroutine(_tickerSubscriptionRoutine);
                _tickerSubscriptionRoutine = null;
            }

            if (_tickerSubscribed && Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }

            _tickerSubscribed = false;
        }

        /// <summary>
        /// Coroutine that blocks until the ticker singleton becomes available and then registers this
        /// wanderer so it continues to receive movement ticks once the overworld scene is active.
        /// </summary>
        private IEnumerator WaitForTickerAndSubscribe()
        {
            while (Ticker.Instance == null)
                yield return null;

            _tickerSubscriptionRoutine = null;

            if (!isActiveAndEnabled)
                yield break;

            Ticker.Instance.Subscribe(this);
            _tickerSubscribed = true;
        }

        /// <summary>
        /// Resolves the preferred attack distance for the attached combatant so pursuit stops at
        /// the same range the combat loop expects when firing attacks.
        /// </summary>
        private float GetPreferredAttackRange()
        {
            if (_combatant == null)
                _combatant = GetComponent<NpcCombatant>();

            var profile = _combatant != null ? _combatant.Profile : null;
            if (profile == null)
                return CombatMath.MELEE_RANGE;

            float range = profile.GetPreferredAttackRange();
            return range > 0f ? range : CombatMath.MELEE_RANGE;
        }

        public void OnTick()
        {
            float delta = Ticker.TickDuration;
            const float DISTANCE_EPSILON = 0.05f;

            if (_knockbackActive)
            {
                _lerpTime = Ticker.TickDuration;
                return;
            }

            if (_frozen)
            {
                _from = _rb != null ? _rb.position : (Vector2)transform.position;
                _to = _from;
                _lerpTime = Ticker.TickDuration;
                spriteAnimator?.UpdateVisuals(Vector2.zero);
                return;
            }

            if (_combatTargets.Count > 0)
            {
                _from = _rb != null ? _rb.position : (Vector2)transform.position;
                Transform closest = null;
                float best = float.MaxValue;
                for (int i = _combatTargets.Count - 1; i >= 0; i--)
                {
                    Transform t = _combatTargets[i];
                    if (t == null)
                    {
                        _combatTargets.RemoveAt(i);
                        continue;
                    }

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
                    float desiredRange = Mathf.Max(GetPreferredAttackRange(), DISTANCE_EPSILON);
                    if (best > desiredRange + DISTANCE_EPSILON)
                    {
                        Vector2 direction = (targetPos - _from).normalized;
                        Vector2 desired = targetPos - direction * desiredRange;
                        desired = ClampToMovementBounds(desired);
                        Vector2 step = Vector2.MoveTowards(_from, desired, moveSpeed * delta);
                        _to = ClampToMovementBounds(step);
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
            _to = ClampToMovementBounds(_to);
            _lerpTime = 0f;

            if (Vector2.Distance(_to, _target) <= arriveDistance)
                BeginIdle();
        }

        private void Update()
        {
            if (_knockbackActive)
            {
                _knockbackTimer += Time.deltaTime;
                float progress = _knockbackDuration > 0f ? Mathf.Clamp01(_knockbackTimer / _knockbackDuration) : 1f;
                float eased = _knockbackCurve != null && _knockbackCurve.length > 0 ? _knockbackCurve.Evaluate(progress) : progress;
                Vector2 target = Vector2.LerpUnclamped(_knockbackStart, _knockbackEnd, eased);
                if (_knockbackClamp)
                    target = ClampToMovementBounds(target);

                if (_rb != null) _rb.MovePosition(target);
                else transform.position = target;

                Vector2 knockbackVelocity = (target - _lastPos) / Mathf.Max(Time.deltaTime, 0.0001f);
                spriteAnimator?.UpdateVisuals(knockbackVelocity);
                _lastPos = target;

                if (_knockbackTimer >= _knockbackDuration)
                {
                    _knockbackActive = false;
                    _from = target;
                    _to = target;
                    _lerpTime = Ticker.TickDuration;
                }
                return;
            }

            if (_frozen)
            {
                // Maintain the frozen position without reusing the interpolated movement variable name below.
                Vector2 frozenPosition = _rb != null ? _rb.position : (Vector2)transform.position;
                if (_rb != null)
                    _rb.MovePosition(frozenPosition);
                spriteAnimator?.UpdateVisuals(Vector2.zero);
                _lastPos = frozenPosition;
                return;
            }

            if (_lerpTime >= Ticker.TickDuration)
            {
                spriteAnimator?.UpdateVisuals(Vector2.zero);
                return;
            }

            _lerpTime += Time.deltaTime;
            float t = Mathf.Clamp01(_lerpTime / Ticker.TickDuration);
            Vector2 pos = Vector2.Lerp(_from, _to, t);
            pos = ClampToMovementBounds(pos);
            if (_rb != null) _rb.MovePosition(pos);
            else transform.position = pos;

            Vector2 velocity = (pos - _lastPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            spriteAnimator?.UpdateVisuals(velocity);
            _lastPos = pos;
        }

        /// <summary>
        /// Clamps the provided world position to the wanderer's configured movement bounds so the
        /// NPC never leaves its permitted patrol area, even when external systems (such as
        /// knockback) attempt to move it beyond the limits. When a navigation grid is available the
        /// result is additionally snapped to the nearest walkable nav-cell so the wanderer honours
        /// baked blockers and does not drift into invalid tiles.
        /// </summary>
        /// <param name="worldPosition">Target position in world space.</param>
        /// <returns>The clamped world position respecting the configured bounds.</returns>
        public Vector2 ClampToMovementBounds(Vector2 worldPosition)
        {
            Vector2 origin = _originInitialized ? _origin : (_rb != null ? _rb.position : (Vector2)transform.position);

            Vector2 clamped = ClampWithinConfiguredBounds(origin, worldPosition, out float minX, out float maxX, out float minY, out float maxY);

            NavGridBuilder grid = PathfindingService.Instance?.ActiveGrid;
            if (grid == null || !grid.HasGrid)
            {
                return clamped;
            }

            Vector2Int cell;
            if (!grid.TryGetCell(clamped, out cell))
            {
                cell = grid.WorldToCellClamped(clamped);
            }

            if (!grid.IsCellWithinBounds(cell))
            {
                return clamped;
            }

            Vector2 cellCenter = grid.GetCellCenter(cell);
            if (grid.IsCellWalkable(cell) && IsWithinBounds(cellCenter, minX, maxX, minY, maxY))
            {
                return cellCenter;
            }

            Queue<Vector2Int> frontier = new Queue<Vector2Int>();
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            frontier.Enqueue(cell);
            visited.Add(cell);

            while (frontier.Count > 0)
            {
                Vector2Int current = frontier.Dequeue();
                for (int i = 0; i < FourWayOffsets.Length; i++)
                {
                    Vector2Int neighbour = current + FourWayOffsets[i];
                    if (!grid.IsCellWithinBounds(neighbour) || !visited.Add(neighbour))
                    {
                        continue;
                    }

                    Vector2 neighbourCenter = grid.GetCellCenter(neighbour);
                    if (!IsWithinBounds(neighbourCenter, minX, maxX, minY, maxY))
                    {
                        continue;
                    }

                    if (grid.IsCellWalkable(neighbour))
                    {
                        return neighbourCenter;
                    }

                    frontier.Enqueue(neighbour);
                }
            }

            return clamped;
        }

        /// <summary>
        /// Clamps the provided world position to the wanderer's configured movement bounds without
        /// performing any navgrid snapping. This is used when interpolation needs to remain smooth
        /// between tiles while still respecting the patrol rectangle.
        /// </summary>
        /// <param name="worldPosition">Target position in world space.</param>
        /// <returns>The clamped position limited to the configured patrol bounds.</returns>
        public Vector2 ClampToMovementBoundsNoSnap(Vector2 worldPosition)
        {
            Vector2 origin = _originInitialized ? _origin : (_rb != null ? _rb.position : (Vector2)transform.position);
            return ClampWithinConfiguredBounds(origin, worldPosition, out _, out _, out _, out _);
        }

        /// <summary>
        /// Internal helper that clamps the world position against the configured patrol bounds and
        /// exposes the resolved min/max values for optional navgrid snapping.
        /// </summary>
        private Vector2 ClampWithinConfiguredBounds(Vector2 origin, Vector2 worldPosition, out float minX, out float maxX, out float minY, out float maxY)
        {
            ResolveMovementBounds(origin, out minX, out maxX, out minY, out maxY);

            float clampedX = Mathf.Clamp(worldPosition.x, minX, maxX);
            float clampedY = Mathf.Clamp(worldPosition.y, minY, maxY);
            return new Vector2(clampedX, clampedY);
        }

        private static bool IsWithinBounds(Vector2 position, float minX, float maxX, float minY, float maxY)
        {
            return position.x >= minX && position.x <= maxX && position.y >= minY && position.y <= maxY;
        }

        /// <summary>
        /// Calculates the minimum and maximum world-space bounds the wanderer is allowed to move
        /// within based on the configured offset or area settings.
        /// </summary>
        private void ResolveMovementBounds(Vector2 origin, out float minX, out float maxX, out float minY, out float maxY)
        {
            if (useAreaSize)
            {
                Vector2 absArea = new Vector2(Mathf.Abs(areaSize.x), Mathf.Abs(areaSize.y));
                Vector2 half = absArea * 0.5f;
                minX = origin.x - half.x;
                maxX = origin.x + half.x;
                minY = origin.y - half.y;
                maxY = origin.y + half.y;
                return;
            }

            float offsetMinX = Mathf.Min(minOffset.x, maxOffset.x);
            float offsetMaxX = Mathf.Max(minOffset.x, maxOffset.x);
            float offsetMinY = Mathf.Min(minOffset.y, maxOffset.y);
            float offsetMaxY = Mathf.Max(minOffset.y, maxOffset.y);

            minX = origin.x + offsetMinX;
            maxX = origin.x + offsetMaxX;
            minY = origin.y + offsetMinY;
            maxY = origin.y + offsetMaxY;
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

