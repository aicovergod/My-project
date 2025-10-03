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
    }
}
