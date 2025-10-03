// Assets/Scripts/Player/Input/PlayerMovementInput.cs
using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using Core.Input;
#endif

namespace Player.Input
{
    /// <summary>
    ///     Centralises the player's locomotion input handling. The component resolves the shared Move action via
    ///     <see cref="InputActionResolver"/>, snaps analog input to OSRS-style cardinals, and raises change events
    ///     so movement controllers can react without owning Input System subscriptions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMovementInput : MonoBehaviour
    {
        [Tooltip("Deadzone used when reading analog sticks to snap to -1/0/1.")]
        [SerializeField] private float gamepadDeadzone = 0.3f;

#if ENABLE_INPUT_SYSTEM
        [Header("Input")]
        [Tooltip("PlayerInput component that owns the Player action map.")]
        [SerializeField] private PlayerInput playerInput;

        [Tooltip("Reference to the Player/Move action inside the shared input asset.")]
        [SerializeField] private InputActionReference moveActionReference;

        private InputAction moveAction;
        private bool moveActionEnabledByResolver;
#endif

        /// <summary>Raised whenever the sanitised movement vector changes.</summary>
        public event Action<Vector2> MoveVectorChanged;

        /// <summary>Most recent sanitised movement vector emitted by the action map.</summary>
        public Vector2 CurrentValue { get; private set; }

#if ENABLE_INPUT_SYSTEM
        private void Awake()
        {
            if (playerInput == null)
                playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            moveAction = InputActionResolver.Resolve(playerInput, moveActionReference, "Move", out moveActionEnabledByResolver);
            if (moveAction == null)
                return;

            moveAction.performed += HandleMovePerformed;
            moveAction.canceled += HandleMoveCanceled;
            UpdateValue(moveAction.ReadValue<Vector2>());
        }

        private void OnDisable()
        {
            if (moveAction != null)
            {
                moveAction.performed -= HandleMovePerformed;
                moveAction.canceled -= HandleMoveCanceled;

                if (moveActionEnabledByResolver)
                    moveAction.Disable();
            }

            moveAction = null;
            moveActionEnabledByResolver = false;
            UpdateValue(Vector2.zero);
        }

        private void HandleMovePerformed(InputAction.CallbackContext context)
        {
            UpdateValue(context.ReadValue<Vector2>());
        }

        private void HandleMoveCanceled(InputAction.CallbackContext context)
        {
            UpdateValue(Vector2.zero);
        }
#else
        private void OnEnable()
        {
            UpdateValue(Vector2.zero);
        }

        private void OnDisable()
        {
            UpdateValue(Vector2.zero);
        }

        private void Update()
        {
            Vector2 raw = new Vector2(UnityEngine.Input.GetAxisRaw("Horizontal"), UnityEngine.Input.GetAxisRaw("Vertical"));
            UpdateValue(raw);
        }
#endif

        /// <summary>
        ///     Applies deadzone snapping, updates the cached value, and fires change events when the effective input vector changes.
        /// </summary>
        private void UpdateValue(Vector2 rawValue)
        {
            Vector2 sanitised = new Vector2(SnapAxis(rawValue.x), SnapAxis(rawValue.y));
            if (sanitised == CurrentValue)
                return;

            CurrentValue = sanitised;
            MoveVectorChanged?.Invoke(CurrentValue);
        }

        /// <summary>Snaps an individual axis to -1, 0, or 1 using the configured deadzone.</summary>
        private float SnapAxis(float value)
        {
            if (Mathf.Abs(value) < gamepadDeadzone)
                return 0f;

            return Mathf.Sign(value);
        }
    }
}
