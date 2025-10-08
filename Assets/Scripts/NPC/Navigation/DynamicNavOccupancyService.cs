using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;
using World;

namespace NPC
{
    /// <summary>
    /// Tracks lightweight cell reservations so movers can temporarily claim navigation tiles while planning
    /// or traversing a path. Reservations expire automatically after the configured tick window which keeps
    /// searches fair even when multiple NPCs converge on the same choke point.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DynamicNavOccupancyService : ScenePersistentObject, ITickable
    {
        private struct CellReservation
        {
            public WeakReference<IPathMoverClient> MoverReference;
            public int RequestId;
            public int ExpiryTick;
            public int ReferenceCount;
        }

        /// <summary>
        /// Handle returned to movers so they can release reservations as they progress through a path.
        /// </summary>
        public sealed class ReservationHandle
        {
            private readonly DynamicNavOccupancyService owner;
            private readonly WeakReference<IPathMoverClient> moverReference;
            private readonly List<List<Vector2Int>> perStepCells;
            private readonly int requestId;
            private readonly int expiryTick;
            private int nextReleaseIndex;
            private bool released;

            internal ReservationHandle(
                DynamicNavOccupancyService owner,
                IPathMoverClient mover,
                int requestId,
                List<List<Vector2Int>> perStepCells,
                int expiryTick)
            {
                this.owner = owner;
                moverReference = new WeakReference<IPathMoverClient>(mover);
                this.requestId = requestId;
                this.perStepCells = perStepCells ?? new List<List<Vector2Int>>();
                this.expiryTick = expiryTick;
                nextReleaseIndex = 0;
                released = false;
            }

            /// <summary>
            /// Request identifier associated with this reservation.
            /// </summary>
            public int RequestId => requestId;

            /// <summary>
            /// Returns true once the handle has released all reservations and is no longer valid.
            /// </summary>
            public bool IsReleased => released;

            internal bool TryGetMover(out IPathMoverClient mover)
            {
                return moverReference.TryGetTarget(out mover) && mover != null;
            }

            internal IReadOnlyList<List<Vector2Int>> Footprint => perStepCells;

            internal int NextReleaseIndex => nextReleaseIndex;

            internal int ExpiryTick => expiryTick;

            internal void AdvanceReleaseIndex()
            {
                nextReleaseIndex = Mathf.Min(nextReleaseIndex + 1, perStepCells.Count);
            }

            internal void MarkReleased()
            {
                released = true;
            }

            /// <summary>
            /// Releases the next reservation slice, typically after the mover finishes traversing a waypoint.
            /// </summary>
            public void MarkWaypointConsumed()
            {
                if (released)
                {
                    return;
                }

                owner?.ReleaseNextStep(this);
            }

            /// <summary>
            /// Releases all reservations owned by this handle.
            /// </summary>
            public void ReleaseAll()
            {
                if (released)
                {
                    return;
                }

                owner?.ReleaseHandle(this);
            }
        }

        [Header("Debug")]
        [Tooltip("Enables verbose logging for reservation claims and releases.")]
        [SerializeField] private bool enableDebugLogging;

        private readonly Dictionary<Vector2Int, CellReservation> reservations = new Dictionary<Vector2Int, CellReservation>();
        private readonly Dictionary<IPathMoverClient, ReservationHandle> handlesByMover = new Dictionary<IPathMoverClient, ReservationHandle>();
        private readonly List<Vector2Int> footprintBuffer = new List<Vector2Int>();
        private readonly List<ReservationHandle> handleCleanupBuffer = new List<ReservationHandle>();
        private readonly List<Vector2Int> reservationKeySnapshot = new List<Vector2Int>();

        private int currentTick;
        private bool subscribedToTicker;
        private Coroutine tickerSubscriptionRoutine;

        /// <summary>
        /// Current navigation tick processed by the service.
        /// </summary>
        public int CurrentTick => currentTick;

        /// <summary>
        /// Raised whenever reservations are removed or expire so queued requests can re-evaluate.
        /// </summary>
        public event Action ReservationsChanged;

        private void OnEnable()
        {
            SubscribeToTicker();
        }

        private void Start()
        {
            SubscribeToTicker();
        }

        private void OnDisable()
        {
            UnsubscribeFromTicker();
        }

        private void OnDestroy()
        {
            UnsubscribeFromTicker();
            reservations.Clear();
            handlesByMover.Clear();
        }

        /// <inheritdoc />
        public void OnTick()
        {
            currentTick++;
            CleanupOrphanedHandles();

            if (PurgeExpiredReservations())
            {
                ReservationsChanged?.Invoke();
            }
        }

        /// <summary>
        /// Attempts to reserve the supplied grid cells for the mover. The reservation tracks each waypoint in order so
        /// tiles can be released as progress is reported through <see cref="ReservationHandle.MarkWaypointConsumed"/>.
        /// </summary>
        /// <param name="mover">Mover requesting the reservation.</param>
        /// <param name="requestId">Active request identifier assigned by <see cref="PathfindingService"/>.</param>
        /// <param name="orderedCells">Ordered grid cells representing the planned route.</param>
        /// <param name="radius">Radius (in grid cells) applied around each waypoint.</param>
        /// <param name="durationTicks">Duration the reservation should persist without progress. Non-positive values reserve indefinitely.</param>
        public ReservationHandle ReservePath(
            IPathMoverClient mover,
            int requestId,
            IReadOnlyList<Vector2Int> orderedCells,
            int radius,
            int durationTicks)
        {
            if (mover == null || orderedCells == null || orderedCells.Count == 0)
            {
                return null;
            }

            ReleaseReservationsForMover(mover);

            int clampedRadius = Mathf.Max(0, radius);
            int expiryTick = durationTicks > 0 ? currentTick + durationTicks : -1;
            var perStepCells = new List<List<Vector2Int>>(orderedCells.Count);
            bool changed = false;

            for (int i = 0; i < orderedCells.Count; i++)
            {
                Vector2Int coreCell = orderedCells[i];
                BuildFootprint(coreCell, clampedRadius, footprintBuffer);
                var stepFootprint = new List<Vector2Int>(footprintBuffer.Count);

                for (int j = 0; j < footprintBuffer.Count; j++)
                {
                    Vector2Int footprintCell = footprintBuffer[j];
                    if (AddReservation(footprintCell, mover, requestId, expiryTick))
                    {
                        changed = true;
                    }

                    stepFootprint.Add(footprintCell);
                }

                perStepCells.Add(stepFootprint);
            }

            var handle = new ReservationHandle(this, mover, requestId, perStepCells, expiryTick);
            handlesByMover[mover] = handle;

            if (changed)
            {
                ReservationsChanged?.Invoke();
            }

            if (enableDebugLogging)
            {
                Debug.Log($"{GetMoverName(mover)} reserved {orderedCells.Count} cells (radius {clampedRadius}).", this);
            }

            return handle;
        }

        /// <summary>
        /// Returns whether the supplied cell is currently reserved by another mover.
        /// </summary>
        /// <param name="cell">Cell being inspected.</param>
        /// <param name="requester">Mover attempting to use the cell.</param>
        /// <param name="requestId">Active request identifier for the requester.</param>
        /// <param name="expiryTick">Outputs the tick when the reservation expires. Returns -1 for indefinite reservations.</param>
        public bool IsCellReservedForOthers(Vector2Int cell, IPathMoverClient requester, int requestId, out int expiryTick)
        {
            expiryTick = -1;
            if (!reservations.TryGetValue(cell, out var reservation))
            {
                return false;
            }

            if (reservation.ReferenceCount <= 0)
            {
                reservations.Remove(cell);
                return false;
            }

            if (!reservation.MoverReference.TryGetTarget(out var owner) || owner == null)
            {
                reservations.Remove(cell);
                ReservationsChanged?.Invoke();
                return false;
            }

            if (ReferenceEquals(owner, requester) && reservation.RequestId == requestId)
            {
                return false;
            }

            expiryTick = reservation.ExpiryTick;
            return true;
        }

        /// <summary>
        /// Releases any reservations owned by the mover. Called when movers cancel or replace their active path.
        /// </summary>
        public void ReleaseReservationsForMover(IPathMoverClient mover)
        {
            if (mover == null)
            {
                return;
            }

            if (handlesByMover.TryGetValue(mover, out var handle) && handle != null)
            {
                handle.ReleaseAll();
            }
        }

        private bool AddReservation(Vector2Int cell, IPathMoverClient mover, int requestId, int expiryTick)
        {
            if (!reservations.TryGetValue(cell, out var reservation))
            {
                reservation = new CellReservation
                {
                    MoverReference = new WeakReference<IPathMoverClient>(mover),
                    RequestId = requestId,
                    ExpiryTick = expiryTick,
                    ReferenceCount = 1
                };

                reservations[cell] = reservation;
                return true;
            }

            if (!reservation.MoverReference.TryGetTarget(out var owner) || owner == null)
            {
                reservation.MoverReference = new WeakReference<IPathMoverClient>(mover);
                reservation.RequestId = requestId;
                reservation.ReferenceCount = 0;
            }
            else if (!ReferenceEquals(owner, mover) || reservation.RequestId != requestId)
            {
                // Another mover already claimed this cell. Keep the existing reservation so the caller can reconsider later.
                return false;
            }

            reservation.ReferenceCount = Mathf.Max(0, reservation.ReferenceCount) + 1;
            reservation.ExpiryTick = expiryTick;
            reservations[cell] = reservation;
            return true;
        }

        private bool ReleaseReservationCell(Vector2Int cell, ReservationHandle handle)
        {
            if (!reservations.TryGetValue(cell, out var reservation))
            {
                return false;
            }

            if (!handle.TryGetMover(out var mover) || mover == null)
            {
                reservations.Remove(cell);
                return true;
            }

            if (!reservation.MoverReference.TryGetTarget(out var owner) || owner == null || !ReferenceEquals(owner, mover) ||
                reservation.RequestId != handle.RequestId)
            {
                return false;
            }

            reservation.ReferenceCount = Mathf.Max(0, reservation.ReferenceCount - 1);
            if (reservation.ReferenceCount <= 0)
            {
                reservations.Remove(cell);
                return true;
            }

            reservations[cell] = reservation;
            return false;
        }

        private void ReleaseNextStep(ReservationHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            var footprint = handle.Footprint;
            int releaseIndex = handle.NextReleaseIndex;
            if (releaseIndex >= footprint.Count)
            {
                return;
            }

            bool changed = false;
            var slice = footprint[releaseIndex];
            for (int i = 0; i < slice.Count; i++)
            {
                if (ReleaseReservationCell(slice[i], handle))
                {
                    changed = true;
                }
            }

            handle.AdvanceReleaseIndex();

            if (changed)
            {
                ReservationsChanged?.Invoke();
            }
        }

        private void ReleaseHandle(ReservationHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            bool changed = false;
            var footprint = handle.Footprint;
            for (int i = handle.NextReleaseIndex; i < footprint.Count; i++)
            {
                var slice = footprint[i];
                for (int j = 0; j < slice.Count; j++)
                {
                    if (ReleaseReservationCell(slice[j], handle))
                    {
                        changed = true;
                    }
                }
            }

            handle.MarkReleased();

            if (handle.TryGetMover(out var mover) && mover != null)
            {
                handlesByMover.Remove(mover);
            }

            if (changed)
            {
                ReservationsChanged?.Invoke();
            }
        }

        private void CleanupOrphanedHandles()
        {
            handleCleanupBuffer.Clear();
            foreach (var entry in handlesByMover)
            {
                var handle = entry.Value;
                if (!handle.TryGetMover(out var mover) || mover == null)
                {
                    handleCleanupBuffer.Add(handle);
                    continue;
                }

                bool hasActiveReservation = false;
                var footprint = handle.Footprint;
                for (int i = handle.NextReleaseIndex; i < footprint.Count && !hasActiveReservation; i++)
                {
                    var slice = footprint[i];
                    for (int j = 0; j < slice.Count; j++)
                    {
                        if (!reservations.TryGetValue(slice[j], out var reservation))
                        {
                            continue;
                        }

                        if (reservation.ReferenceCount <= 0)
                        {
                            continue;
                        }

                        if (!reservation.MoverReference.TryGetTarget(out var owner) || owner == null)
                        {
                            continue;
                        }

                        if (ReferenceEquals(owner, mover) && reservation.RequestId == handle.RequestId)
                        {
                            hasActiveReservation = true;
                            break;
                        }
                    }
                }

                if (!hasActiveReservation)
                {
                    handleCleanupBuffer.Add(handle);
                }
            }

            if (handleCleanupBuffer.Count == 0)
            {
                return;
            }

            for (int i = 0; i < handleCleanupBuffer.Count; i++)
            {
                ReleaseHandle(handleCleanupBuffer[i]);
            }

            handleCleanupBuffer.Clear();
        }

        private bool PurgeExpiredReservations()
        {
            bool changed = false;
            // Reuse a shared list to avoid repeated allocations when scanning reservations.
            reservationKeySnapshot.Clear();
            reservationKeySnapshot.AddRange(reservations.Keys);

            for (int i = 0; i < reservationKeySnapshot.Count; i++)
            {
                Vector2Int cell = reservationKeySnapshot[i];
                if (!reservations.TryGetValue(cell, out var reservation))
                {
                    continue;
                }

                if (reservation.ExpiryTick >= 0 && reservation.ExpiryTick <= currentTick)
                {
                    reservations.Remove(cell);
                    changed = true;
                }
            }

            reservationKeySnapshot.Clear();

            return changed;
        }

        private void BuildFootprint(Vector2Int center, int radius, List<Vector2Int> buffer)
        {
            buffer.Clear();
            if (radius <= 0)
            {
                buffer.Add(center);
                return;
            }

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    buffer.Add(new Vector2Int(center.x + dx, center.y + dy));
                }
            }
        }

        private void SubscribeToTicker()
        {
            if (subscribedToTicker)
            {
                return;
            }

            if (Ticker.Instance == null)
            {
                if (tickerSubscriptionRoutine == null && isActiveAndEnabled)
                {
                    tickerSubscriptionRoutine = StartCoroutine(WaitForTicker());
                }

                return;
            }

            Ticker.Instance.Subscribe(this);
            subscribedToTicker = true;
        }

        private void UnsubscribeFromTicker()
        {
            if (tickerSubscriptionRoutine != null)
            {
                StopCoroutine(tickerSubscriptionRoutine);
                tickerSubscriptionRoutine = null;
            }

            if (!subscribedToTicker)
            {
                return;
            }

            if (Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }

            subscribedToTicker = false;
        }

        private IEnumerator WaitForTicker()
        {
            while (Ticker.Instance == null)
            {
                yield return null;
            }

            tickerSubscriptionRoutine = null;

            if (!isActiveAndEnabled)
            {
                yield break;
            }

            Ticker.Instance.Subscribe(this);
            subscribedToTicker = true;
        }

        private static string GetMoverName(IPathMoverClient mover)
        {
            if (mover is Component component)
            {
                return component.name;
            }

            return mover != null ? mover.ToString() : "<null>";
        }
    }
}
