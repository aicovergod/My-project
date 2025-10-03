// Assets/Scripts/Player/Movement/PlayerMovementController.cs
using System;
using System.Collections;
using UnityEngine;
using Combat;
using Player.Input;
using Player.Visuals;
using Skills;
using Util;

namespace Player.Movement
{
    /// <summary>
    ///     Handles player locomotion, including manual input, auto-move routines, and tick-aligned persistence prompts.
    ///     The controller focuses purely on movement mechanics while <see cref="Player.PlayerMover"/> orchestrates
    ///     persistence and scene lifecycle concerns.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMovementController : MonoBehaviour, IPlayerMovementController
    {
        [Header("Movement")]
        [SerializeField, Tooltip("Base walking speed in world units per second.")]
        private float moveSpeed = 3.5f;

        [SerializeField, Tooltip("Restricts manual input to the four cardinal directions when enabled.")]
        private bool fourWayOnly = true;

        [SerializeField, Tooltip("Optional explicit reference to the movement input provider.")]
        private PlayerMovementInput movementInput;

        [SerializeField, Tooltip("Sprite controller used to update facing visuals and mirror flags.")]
        private PlayerSpriteController spriteController;

        [SerializeField, Tooltip("Optional combat controller used to abort combat when manual movement occurs.")]
        private CombatController combatController;

        [SerializeField, Tooltip("Inventory reference used to honour bank-open movement locks.")]
        private Inventory.Inventory inventory;

        /// <summary>
        ///     Interval in seconds used to prompt position saves while the player is moving. Matches the legacy behaviour from
        ///     <see cref="Player.PlayerMover"/> so autosaves remain consistent.
        /// </summary>
        private const float MovementSaveInterval = 2f;

        private Rigidbody2D body;
        private Vector2 moveDir;
        private Vector2 pendingInput;
        private Direction8 facingDir = Direction8.Down;
        private Coroutine moveRoutine;
        private bool moveRoutineActive;
        private bool movementFrozen;
        private bool isTransitioning;
        private bool wasMoving;
        private float movementSaveTimer;
        private bool freezeSpriteStateBeforeFreeze;

        /// <summary>Event raised whenever a persistence save should capture the current position.</summary>
        public event Action MovementSaveRequested;

        /// <inheritdoc />
        public Direction8 FacingDirection => facingDir;

        /// <inheritdoc />
        public bool IsAutoMoving => moveRoutineActive;

        /// <inheritdoc />
        public bool IsMovementFrozen => movementFrozen;

        /// <inheritdoc />
        public bool IsMoving => moveDir.sqrMagnitude > 0f;

        /// <inheritdoc />
        public float MoveSpeed
        {
            get => moveSpeed;
            set => moveSpeed = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            if (movementInput == null)
                movementInput = GetComponent<PlayerMovementInput>();
            if (spriteController == null)
                spriteController = GetComponent<PlayerSpriteController>();
            if (combatController == null)
                combatController = GetComponent<CombatController>();
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();

            ConfigureBody();
        }

        private void OnEnable()
        {
            if (movementInput != null)
            {
                movementInput.MoveVectorChanged += HandleMoveVectorChanged;
                pendingInput = movementInput.CurrentValue;
            }
        }

        private void OnDisable()
        {
            if (movementInput != null)
                movementInput.MoveVectorChanged -= HandleMoveVectorChanged;

            CancelAutoMoveRoutine();
            moveDir = Vector2.zero;
            pendingInput = Vector2.zero;
            ApplyForcedIdle();
        }

        private void OnDestroy()
        {
            CancelAutoMoveRoutine();
        }

        private void Update()
        {
            if (isTransitioning)
            {
                HaltForExternalControl();
                return;
            }

            if (movementFrozen)
            {
                HaltForExternalControl();
                return;
            }

            if (Inventory.InventoryDebugMenu.HasTextInputFocus || AdminF2Menu.HasTextInputFocus)
            {
                HaltForExternalControl();
                return;
            }

            if (inventory != null && inventory.BankOpen)
            {
                HaltForExternalControl();
                return;
            }

            ProcessManualInput();
            UpdateFacingFromMovement();
            ApplyVisualsAndPersistence();
        }

        private void FixedUpdate()
        {
            body.linearVelocity = moveDir * moveSpeed;
        }

        /// <inheritdoc />
        public void SetMovementFrozen(bool frozen)
        {
            if (movementFrozen == frozen)
                return;

            movementFrozen = frozen;
            if (movementFrozen)
            {
                freezeSpriteStateBeforeFreeze = spriteController != null && spriteController.FreezeSprite;
                StopMovement();
                if (spriteController != null)
                    spriteController.FreezeSprite = true;
            }
            else if (spriteController != null)
            {
                spriteController.FreezeSprite = freezeSpriteStateBeforeFreeze;
            }
        }

        /// <inheritdoc />
        public void SetTransitioning(bool transitioning)
        {
            if (isTransitioning == transitioning)
                return;

            isTransitioning = transitioning;
            if (isTransitioning)
                StopMovement();
        }

        /// <inheritdoc />
        public void FaceTarget(Transform target)
        {
            if (target == null)
                return;

            Vector2 dir = (Vector2)target.position - (Vector2)transform.position;
            if (dir.sqrMagnitude <= 0.0001f)
                return;

            facingDir = Direction8Utility.FromVector(dir, allowDiagonals: true, fallback: facingDir);
            spriteController?.ApplyMovementVisuals(facingDir, IsMoving);
        }

        /// <inheritdoc />
        public void MoveTo(Vector2 target, float stopDistance, Action onComplete = null)
        {
            CancelAutoMoveRoutine();
            moveRoutine = StartCoroutine(MoveToRoutine(target, stopDistance, onComplete));
            moveRoutineActive = true;
        }

        /// <inheritdoc />
        public void MoveTo(Transform target, float stopDistance, Action onComplete = null)
        {
            CancelAutoMoveRoutine();
            moveRoutine = StartCoroutine(MoveToRoutine(target, stopDistance, onComplete));
            moveRoutineActive = true;
        }

        /// <inheritdoc />
        public void StopMovement()
        {
            moveDir = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            CancelAutoMoveRoutine();
            spriteController?.ApplyMovementVisuals(facingDir, false);
            ApplyForcedIdle();
        }

        private void ProcessManualInput()
        {
            Vector2 desiredDirection = Vector2.zero;
            if (pendingInput.sqrMagnitude > 0f)
            {
                Direction8 inputFacing = Direction8Utility.FromVector(pendingInput, allowDiagonals: !fourWayOnly, fallback: facingDir);
                desiredDirection = fourWayOnly
                    ? Direction8Utility.ToCardinalVector(inputFacing)
                    : Direction8Utility.ToVector(inputFacing);

                if (moveRoutineActive)
                    CancelAutoMoveRoutine();

                combatController?.CancelCombat();
            }

            if (!moveRoutineActive)
                moveDir = desiredDirection;
        }

        private void UpdateFacingFromMovement()
        {
            if (moveDir.sqrMagnitude <= 0f)
                return;

            facingDir = Direction8Utility.FromVector(moveDir, allowDiagonals: true, fallback: facingDir);
        }

        private void ApplyVisualsAndPersistence()
        {
            bool moving = moveDir.sqrMagnitude > 0f;
            spriteController?.ApplyMovementVisuals(facingDir, moving);
            HandleMovementPersistence(moving);
        }

        private void HaltForExternalControl()
        {
            moveDir = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            spriteController?.ApplyMovementVisuals(facingDir, false);
            ApplyForcedIdle();
        }

        private void ApplyForcedIdle()
        {
            if (wasMoving)
                MovementSaveRequested?.Invoke();

            wasMoving = false;
            movementSaveTimer = 0f;
        }

        private void HandleMovementPersistence(bool moving)
        {
            if (moving)
            {
                movementSaveTimer += Time.unscaledDeltaTime;
                if (movementSaveTimer >= MovementSaveInterval)
                {
                    MovementSaveRequested?.Invoke();
                    movementSaveTimer = 0f;
                }
            }
            else if (wasMoving)
            {
                MovementSaveRequested?.Invoke();
                movementSaveTimer = 0f;
            }

            wasMoving = moving;
        }

        private void HandleMoveVectorChanged(Vector2 input)
        {
            pendingInput = input;
        }

        private void ConfigureBody()
        {
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.WakeUp();
        }

        private void CancelAutoMoveRoutine()
        {
            if (moveRoutine != null)
                StopCoroutine(moveRoutine);

            moveRoutine = null;
            moveRoutineActive = false;
        }

        private IEnumerator MoveToRoutine(Vector2 target, float stopDistance, Action onComplete)
        {
            moveRoutineActive = true;
            while (Vector2.Distance(transform.position, target) > stopDistance)
            {
                Vector2 dir = (target - (Vector2)transform.position).normalized;
                moveDir = dir;
                yield return null;
            }

            StopMovement();
            onComplete?.Invoke();
        }

        private IEnumerator MoveToRoutine(Transform target, float stopDistance, Action onComplete)
        {
            moveRoutineActive = true;
            while (target != null && Vector2.Distance(transform.position, target.position) > stopDistance)
            {
                Vector2 dir = ((Vector2)target.position - (Vector2)transform.position).normalized;
                moveDir = dir;
                yield return null;
            }

            StopMovement();
            if (target != null)
                onComplete?.Invoke();
        }

        /// <summary>
        ///     Restores runtime references when the inspector rebinds serialized fields.
        /// </summary>
        private void OnValidate()
        {
            if (movementInput == null)
                movementInput = GetComponent<PlayerMovementInput>();
            if (spriteController == null)
                spriteController = GetComponent<PlayerSpriteController>();
            if (combatController == null)
                combatController = GetComponent<CombatController>();
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
        }
    }

    /// <summary>
    ///     Public contract that external systems depend on when issuing player movement commands.
    /// </summary>
    public interface IPlayerMovementController
    {
        /// <summary>Current facing direction resolved from input or auto-move targets.</summary>
        Direction8 FacingDirection { get; }

        /// <summary>True while an auto-move routine is active.</summary>
        bool IsAutoMoving { get; }

        /// <summary>True while external systems have frozen the controller.</summary>
        bool IsMovementFrozen { get; }

        /// <summary>True while any movement is currently being applied.</summary>
        bool IsMoving { get; }

        /// <summary>Gets or sets the base walking speed applied to manual and auto movement.</summary>
        float MoveSpeed { get; set; }

        /// <summary>Enables or disables external freezing of the player's movement.</summary>
        void SetMovementFrozen(bool frozen);

        /// <summary>Signals that scene transitions are in progress so manual input is ignored.</summary>
        void SetTransitioning(bool transitioning);

        /// <summary>Forces the player to face a supplied transform immediately.</summary>
        void FaceTarget(Transform target);

        /// <summary>Begins walking towards a world-space location until within the supplied distance.</summary>
        void MoveTo(Vector2 target, float stopDistance, Action onComplete = null);

        /// <summary>Begins walking towards a transform until within the supplied distance.</summary>
        void MoveTo(Transform target, float stopDistance, Action onComplete = null);

        /// <summary>Immediately halts all movement, cancelling auto-move routines and clearing velocity.</summary>
        void StopMovement();
    }
}
