using System.Collections.Generic;
using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Shared interface consumed by <see cref="PathfindingService"/> so multiple mover types (NPCs, pets)
    /// can receive asynchronous path results.
    /// </summary>
    public interface IPathMoverClient
    {
        /// <summary>
        /// Consumes the outcome of a path request previously queued with <see cref="PathfindingService"/>.
        /// </summary>
        /// <param name="requestId">Identifier returned when the path was requested.</param>
        /// <param name="status">Result returned by the navigation system.</param>
        /// <param name="worldPath">Resolved world-space waypoints. May be <c>null</c> on failure.</param>
        /// <param name="resolvedGoalWorld">World position considered the final destination.</param>
        void HandlePathResult(int requestId, PathfindingService.PathStatus status, List<Vector2> worldPath, Vector2 resolvedGoalWorld);

        /// <summary>
        /// Radius (in grid cells) that should be reserved around each waypoint while the mover traverses a path.
        /// </summary>
        int GetReservationRadius();

        /// <summary>
        /// Number of ticks a reservation should persist for when no progress is reported. Non-positive values reserve indefinitely.
        /// </summary>
        int GetReservationDurationTicks();

        /// <summary>
        /// Provides the mover with a handle that manages the active reservation for the supplied request.
        /// The mover should call <see cref="DynamicNavOccupancyService.ReservationHandle.MarkWaypointConsumed"/>
        /// whenever it advances to the next waypoint and <see cref="DynamicNavOccupancyService.ReservationHandle.ReleaseAll"/>
        /// when the path is cancelled or completed.
        /// </summary>
        /// <param name="requestId">Identifier associated with the reservation.</param>
        /// <param name="handle">Handle used to release claimed cells. May be <c>null</c> when no reservation is active.</param>
        void BindReservationHandle(int requestId, DynamicNavOccupancyService.ReservationHandle handle);
    }
}
