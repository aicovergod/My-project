using Player;
using UnityEngine;

namespace UI.Utilities
{
    /// <summary>
    /// Helper that freezes the <see cref="PlayerMover"/> while modal UI is displayed and restores
    /// the original locomotion state when the modal closes.
    /// </summary>
    public sealed class PlayerMovementModalLock
    {
        private PlayerMover cachedPlayerMover;
        private bool lockActive;
        private bool movementInputCaptured;
        private bool movementInputWasEnabled;
        private bool canDropWasAllowed;

        /// <summary>
        /// Indicates whether the modal lock currently owns the player's movement state.
        /// </summary>
        public bool IsLocked => lockActive;

        /// <summary>
        /// Captures the player's movement state and disables locomotion/input so modal windows
        /// can operate without the player walking away.
        /// </summary>
        /// <param name="overrideMover">
        /// Optional reference that overrides the cached <see cref="PlayerMover"/> for this acquire
        /// call. When omitted the helper lazily searches the scene.
        /// </param>
        public void Acquire(PlayerMover overrideMover = null)
        {
            if (overrideMover != null)
                cachedPlayerMover = overrideMover;

            if (cachedPlayerMover == null)
                cachedPlayerMover = Object.FindObjectOfType<PlayerMover>();

            if (cachedPlayerMover == null)
                return;

            cachedPlayerMover.StopMovement();

            // Always ensure input is disabled while the modal is active.
            var movementInput = cachedPlayerMover.MovementInput;
            if (!lockActive)
            {
                if (movementInput != null)
                {
                    movementInputWasEnabled = movementInput.enabled;
                    movementInput.enabled = false;
                    movementInputCaptured = true;
                }
                else
                {
                    movementInputCaptured = false;
                }

                canDropWasAllowed = cachedPlayerMover.CanDrop;
                cachedPlayerMover.CanDrop = false;
                lockActive = true;
            }
            else
            {
                if (movementInput != null)
                    movementInput.enabled = false;

                cachedPlayerMover.CanDrop = false;
            }
        }

        /// <summary>
        /// Restores the movement state that was captured by <see cref="Acquire"/>.
        /// </summary>
        public void Release()
        {
            if (!lockActive)
                return;

            if (cachedPlayerMover != null)
            {
                var movementInput = cachedPlayerMover.MovementInput;
                if (movementInputCaptured && movementInput != null)
                    movementInput.enabled = movementInputWasEnabled;

                cachedPlayerMover.CanDrop = canDropWasAllowed;
            }

            lockActive = false;
            movementInputCaptured = false;
        }

        /// <summary>
        /// Clears the cached <see cref="PlayerMover"/> reference. Call this if the player mover is
        /// being destroyed and the helper should re-resolve the instance on the next acquire.
        /// </summary>
        public void ResetCache()
        {
            cachedPlayerMover = null;
        }
    }
}
