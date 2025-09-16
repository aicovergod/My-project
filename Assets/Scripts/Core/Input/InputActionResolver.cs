using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Input
{
    /// <summary>
    /// Helper utilities that bridge serialized <see cref="InputActionReference"/> assets and runtime
    /// <see cref="PlayerInput"/> components.  Centralizing the resolution logic keeps gathering and NPC
    /// controllers lightweight while still supporting inspector based overrides.
    /// </summary>
    public static class InputActionResolver
    {
        /// <summary>
        /// Resolves an <see cref="InputAction"/> either from a direct <paramref name="reference"/> or from
        /// the provided <paramref name="playerInput"/> action map.  When the action is currently disabled
        /// it will be enabled and the <paramref name="actionEnabledByResolver"/> flag is set so the caller
        /// knows whether it should be disabled again during cleanup.
        /// </summary>
        /// <param name="playerInput">Player input component supplying the default action map.</param>
        /// <param name="reference">Optional reference overriding the action lookup.</param>
        /// <param name="actionName">Name of the action to locate within the input asset.</param>
        /// <param name="actionEnabledByResolver">Outputs whether the helper enabled the action.</param>
        public static InputAction Resolve(PlayerInput playerInput, InputActionReference reference, string actionName,
            out bool actionEnabledByResolver)
        {
            actionEnabledByResolver = false;

            // Prefer the explicit reference so designers can swap action maps per prefab if required.
            InputAction action = reference != null ? reference.action : null;

            if (action == null && playerInput != null)
            {
                // PlayerInput.FindAction throws if the action is missing, therefore use the safe lookup variant.
                action = playerInput.actions != null
                    ? playerInput.actions.FindAction(actionName, throwIfNotFound: false)
                    : null;
            }

            if (action != null && !action.enabled)
            {
                action.Enable();
                actionEnabledByResolver = true;
            }

            return action;
        }

        /// <summary>
        /// Returns the current pointer position in screen space using whichever pointing device is active.
        /// Falls back to the supplied <paramref name="fallback"/> when no pointer style device is detected
        /// (e.g. controller only setups).
        /// </summary>
        public static Vector2 GetPointerScreenPosition(Vector2 fallback)
        {
            if (Mouse.current != null)
                return Mouse.current.position.ReadValue();

            if (Pen.current != null)
                return Pen.current.position.ReadValue();

            if (Touchscreen.current != null)
            {
                var primaryTouch = Touchscreen.current.primaryTouch;
                if (primaryTouch.press.isPressed)
                    return primaryTouch.position.ReadValue();
            }

            return fallback;
        }
    }
}
