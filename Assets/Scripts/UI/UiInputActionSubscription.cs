using System;
using Core.Input;
using UnityEngine.InputSystem;

namespace UI
{
    /// <summary>
    /// Helper that resolves <see cref="InputAction"/> instances for UI widgets and manages
    /// subscription lifecycles so context menus can hook into the Input System without
    /// duplicating boilerplate.
    /// </summary>
    [Serializable]
    public sealed class UiInputActionSubscription
    {
        [NonSerialized]
        private InputAction resolvedAction;

        [NonSerialized]
        private bool actionEnabledByResolver;

        /// <summary>
        /// Gets the resolved action instance so callers can read values directly when needed.
        /// </summary>
        public InputAction Action => resolvedAction;

        /// <summary>
        /// Resolves the action using the shared <see cref="InputActionResolver"/> helper and keeps
        /// track of whether the resolver enabled it so we can cleanly restore the previous state.
        /// </summary>
        /// <param name="playerInput">Player input component supplying the default bindings.</param>
        /// <param name="reference">Optional override reference exposed on the UI behaviour.</param>
        /// <param name="actionName">Fallback name used when the reference is not populated.</param>
        public void Resolve(PlayerInput playerInput, InputActionReference reference, string actionName)
        {
            Release();
            resolvedAction = InputActionResolver.Resolve(playerInput, reference, actionName, out actionEnabledByResolver);
        }

        /// <summary>
        /// Subscribes the supplied handler to the action's performed callback when the action is valid.
        /// </summary>
        public void Subscribe(Action<InputAction.CallbackContext> handler)
        {
            if (resolvedAction != null)
                resolvedAction.performed += handler;
        }

        /// <summary>
        /// Removes the handler from the action's performed callback.
        /// </summary>
        public void Unsubscribe(Action<InputAction.CallbackContext> handler)
        {
            if (resolvedAction != null)
                resolvedAction.performed -= handler;
        }

        /// <summary>
        /// Releases the resolved action and disables it again when this helper enabled it originally.
        /// </summary>
        public void Release()
        {
            if (resolvedAction != null && actionEnabledByResolver)
                resolvedAction.Disable();

            resolvedAction = null;
            actionEnabledByResolver = false;
        }
    }
}
