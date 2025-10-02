// Assets/Scripts/Player/PlayerMover.cs
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using Core.Input;
using Core.Save;
using World;
using Pets;
using Util;
using Combat;
using Skills;
using Status.Freeze;

namespace Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(FrozenStatusController))]
    public class PlayerMover : ScenePersistentObject, ISaveable
    {
        [Header("Movement")]
        public float moveSpeed = 3.5f;
        public bool fourWayOnly = true;
        [Tooltip("Deadzone used when reading analog sticks to snap to -1/0/1.")]
        public float gamepadDeadzone = 0.3f;

        [HideInInspector]
        public bool CanDrop = true;

        [HideInInspector]
        public bool freezeSprite = false;

        [Header("(Optional) Direct Sprite Override")]
        [Tooltip("If assigned, these sprites will be applied directly each frame based on Dir/IsMoving. Leave null to rely on Animator clips.")]
        public Sprite idleDown, idleDownRight, idleRight, idleUpRight, idleUp, idleUpLeft, idleLeft, idleDownLeft;
        public Sprite walkDown, walkDownRight, walkRight, walkUpRight, walkUp, walkUpLeft, walkLeft, walkDownLeft;
        [Header("Mirroring")]
        [Tooltip("If true, reuse right-facing sprites for any left-facing orientation (including diagonals).")]
        [SerializeField]
        private bool useFlipXForLeft;
        [Tooltip("If true, reuse left-facing sprites for any right-facing orientation (including diagonals).")]
        [SerializeField]
        private bool useFlipXForRight;
        [Tooltip("If true, reuse Down-Right sprites for Down-Left facings by mirroring them.")]
        [SerializeField]
        private bool useFlipXForDownLeft = true;
        [Tooltip("If true, reuse Up-Right sprites for Up-Left facings by mirroring them.")]
        [SerializeField]
        private bool useFlipXForUpLeft = true;
        [Tooltip("If true, reuse Up-Left sprites for Up-Right facings by mirroring them.")]
        [SerializeField]
        private bool useFlipXForUpRight;
        [Tooltip("If true, reuse Down-Left sprites for Down-Right facings by mirroring them.")]
        [SerializeField]
        private bool useFlipXForDownRight;

#if ENABLE_INPUT_SYSTEM
        [Header("Input")]
        [Tooltip("PlayerInput component that owns the Player action map.")]
        [SerializeField]
        private PlayerInput playerInput;

        [Tooltip("Reference to the Player/Move action inside the shared input asset.")]
        [SerializeField]
        private InputActionReference moveActionReference;
#endif

        private Rigidbody2D rb;
        private Animator anim;
        private SpriteRenderer sr;
        private Inventory.Inventory inventory;
        private CombatController combat;
        private GameObject petToMove;
        private bool isTransitioning;
        private bool isAutoMoving;
        private Coroutine moveRoutine;
        private bool movementFrozen;
        private bool freezeSpriteStateBeforeFreeze;
        /// <summary>Tracks whether the previous frame considered the player to be moving.</summary>
        private bool wasMoving;
        /// <summary>
        /// Timer used while the player is moving to trigger periodic position saves so autosaves are
        /// always close to the most recent location.
        /// </summary>
        private float movementSaveTimer;
        /// <summary>
        /// When true this mover has been registered with the <see cref="SaveManager"/> and should
        /// be unregistered during teardown.
        /// </summary>
        private bool registeredWithSaveManager;
        /// <summary>
        /// Interval in seconds used to persist the position while movement is in progress. This
        /// keeps the stored location reasonably fresh without hammering the save file every frame.
        /// </summary>
        private const float MovementSaveInterval = 2f;

        // Ensure only one player persists across scene loads.
        private static PlayerMover instance;

        [Serializable]
        private class PositionData
        {
            public float x;
            public float y;
            public float z;
            public string scene;
        }

        private const string PositionKey = "PlayerPosition";

        private Direction8 facingDir = Direction8.Down;
        private Vector2 moveDir;

        /// <summary>Current facing direction.</summary>
        public Direction8 FacingDir => facingDir;

        public bool IsMoving => moveDir.sqrMagnitude > 0f;

        /// <summary>True while external systems have frozen player movement.</summary>
        public bool IsMovementFrozen => movementFrozen;

        /// <summary>
        ///     Indicates whether the mover is currently following an auto-move request issued through
        ///     <see cref="MoveTo(Vector2,float,System.Action)"/> or <see cref="MoveTo(Transform,float,System.Action)"/>.
        ///     Gathering controllers use this to determine if an automatically initiated walk is still in progress.
        /// </summary>
        public bool IsAutoMoving => isAutoMoving;

        /// <summary>
        ///     Allows external systems to check or override whether the mover mirrors right-facing sprites when
        ///     travelling left. Beastmaster visual swaps rely on being able to persist and restore this value when
        ///     temporarily applying a pet's appearance to the player.
        /// </summary>
        public bool UseFlipXForLeft
        {
            get => useFlipXForLeft;
            set => useFlipXForLeft = value;
        }

        /// <summary>
        ///     Allows external systems to check or override whether the mover mirrors left-facing sprites when
        ///     travelling right. This mirrors <see cref="UseFlipXForLeft"/> for the opposite direction and is exposed
        ///     for the same Beastmaster visual swap workflow.
        /// </summary>
        public bool UseFlipXForRight
        {
            get => useFlipXForRight;
            set => useFlipXForRight = value;
        }

#if ENABLE_INPUT_SYSTEM
        private InputAction moveAction;
        private bool moveActionEnabledByResolver;
        private Vector2 moveActionValue;
#endif

        protected override void Awake()
        {
            base.Awake();

            // Destroy any duplicate player instances that might exist in
            // newly loaded scenes before they can register themselves as
            // persistent objects.  This prevents two players from
            // destroying each other during scene transitions and also
            // avoids multiple AudioListeners.
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            rb = GetComponent<Rigidbody2D>();
            anim = GetComponent<Animator>();
            sr  = GetComponent<SpriteRenderer>();
            inventory = GetComponent<Inventory.Inventory>();
            combat = GetComponent<CombatController>();
#if ENABLE_INPUT_SYSTEM
            // Fallback to the local PlayerInput if one exists and no explicit reference was supplied.
            if (playerInput == null)
                playerInput = GetComponent<PlayerInput>();
#endif
            var depth = GetComponent<SpriteDepth>();
            if (depth != null)
                depth.directionOffset = 1;

            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.WakeUp();

            SaveManager.Register(this);
            registeredWithSaveManager = true;

            SceneTransitionManager.TransitionStarted += OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted += OnTransitionCompleted;
        }

        private void OnEnable()
        {
            // Register this mover with the SceneTransitionManager so the player persists across scene swaps.
            SceneTransitionManager.RegisterPersistentObject(this);

#if ENABLE_INPUT_SYSTEM
            moveAction = InputActionResolver.Resolve(playerInput, moveActionReference, "Move", out moveActionEnabledByResolver);

            if (moveAction != null)
            {
                moveAction.performed += OnMovePerformed;
                moveAction.canceled += OnMoveCanceled;
                moveActionValue = moveAction.ReadValue<Vector2>();
            }
#endif
        }

        private void OnDisable()
        {
            HandleForcedIdle();
#if ENABLE_INPUT_SYSTEM
            if (moveAction != null)
            {
                moveAction.performed -= OnMovePerformed;
                moveAction.canceled -= OnMoveCanceled;

                if (moveActionEnabledByResolver)
                    moveAction.Disable();
            }

            moveAction = null;
            moveActionEnabledByResolver = false;
            moveActionValue = Vector2.zero;
#endif

            // Remove this mover from the persistence registry when disabled so duplicates do not accumulate.
            SceneTransitionManager.UnregisterPersistentObject(this);
        }

#if ENABLE_INPUT_SYSTEM
        private void OnMovePerformed(InputAction.CallbackContext context)
        {
            // Cache the most recent movement vector supplied by the Player action map.
            moveActionValue = context.ReadValue<Vector2>();
        }

        private void OnMoveCanceled(InputAction.CallbackContext context)
        {
            moveActionValue = Vector2.zero;
        }
#endif

        private void OnDestroy()
        {
            HandleForcedIdle();
            if (instance == this)
                instance = null;

            SceneTransitionManager.TransitionStarted -= OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted -= OnTransitionCompleted;

            if (registeredWithSaveManager)
            {
                SaveManager.Unregister(this);
                registeredWithSaveManager = false;
            }
        }

        void Update()
        {
            if (isTransitioning)
            {
                moveDir = Vector2.zero;
                rb.linearVelocity = Vector2.zero;
                anim.SetBool("IsMoving", false);
                HandleForcedIdle();
                return;
            }

            if (movementFrozen)
            {
                moveDir = Vector2.zero;
                if (rb != null)
                    rb.linearVelocity = Vector2.zero;
                anim.SetBool("IsMoving", false);
                HandleForcedIdle();
                return;
            }

            if (Inventory.InventoryDebugMenu.HasTextInputFocus || AdminF2Menu.HasTextInputFocus)
            {
                moveDir = Vector2.zero;
                rb.linearVelocity = Vector2.zero;
                anim.SetBool("IsMoving", false);
                HandleForcedIdle();
                return;
            }

            if (inventory != null && inventory.BankOpen)
            {
                moveDir = Vector2.zero;
                rb.linearVelocity = Vector2.zero;
                anim.SetBool("IsMoving", false);
                HandleForcedIdle();
                return;
            }

            Vector2 inputDir = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            Vector2 raw = moveAction != null ? moveActionValue : Vector2.zero;
            // Snap analog to -1/0/1 so animations are stable
            raw.x = Mathf.Abs(raw.x) < gamepadDeadzone ? 0f : Mathf.Sign(raw.x);
            raw.y = Mathf.Abs(raw.y) < gamepadDeadzone ? 0f : Mathf.Sign(raw.y);
#else
            // Legacy input fallback if project uses Old/Both
            Vector2 raw = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif

            if (raw.sqrMagnitude > 0f)
            {
                Direction8 inputFacing = Direction8Utility.FromVector(raw, !fourWayOnly, facingDir);
                inputDir = fourWayOnly ? Direction8Utility.ToCardinalVector(inputFacing) : Direction8Utility.ToVector(inputFacing);
            }

            if (inputDir.sqrMagnitude > 0f)
            {
                if (isAutoMoving)
                {
                    isAutoMoving = false;
                    if (moveRoutine != null)
                    {
                        StopCoroutine(moveRoutine);
                        moveRoutine = null;
                    }
                }
                combat?.CancelCombat();
            }

            if (!isAutoMoving)
                moveDir = inputDir;

            if (moveDir.sqrMagnitude > 0f)
            {
                facingDir = Direction8Utility.FromVector(moveDir, allowDiagonals: true, fallback: facingDir);
            }

            // Drive Animator (kept for future use and for state visibility)
            bool isMoving = moveDir.sqrMagnitude > 0f;
            RefreshAnimator(isMoving);
            HandleMovementPersistenceAfterUpdate(isMoving);
        }

        private void RefreshAnimator(bool isMoving)
        {
            if (anim == null)
                return;

            Direction8 visualDir = facingDir;
            int animatorDir = Direction8Utility.ToAnimatorIndex(visualDir);
            Direction8 spriteDir = visualDir;

            anim.SetBool("IsMoving", isMoving);
            anim.SetInteger("Dir", animatorDir);

            if (sr == null)
                return;

            Sprite desired = null;
            bool flip = false;
            desired = ResolveOverrideSprite(spriteDir, isMoving, out flip);

            if (desired != null && !freezeSprite)
            {
                if (sr.flipX != flip)
                    sr.flipX = flip;
                if (sr.sprite != desired)
                    sr.sprite = desired;
            }
        }

        /// <summary>
        ///     Resolves the sprite to display for the supplied direction using the optional override fields. The lookup
        ///     respects diagonal-specific art, mirrors configured directions when requested, and gracefully falls back to
        ///     cardinal frames when bespoke assets are unavailable.
        /// </summary>
        private Sprite ResolveOverrideSprite(Direction8 direction, bool moving, out bool flip)
        {
            foreach (var lookup in Direction8Utility.BuildSpriteFallbackOrder(direction, ShouldMirrorOverride))
            {
                Sprite sprite = GetSpriteForDirection(lookup.Direction, moving);
                if (sprite != null)
                {
                    flip = lookup.FlipX;
                    return sprite;
                }
            }

            flip = false;
            return null;
        }

        /// <summary>Returns true when the supplied direction is configured to borrow sprites from its mirrored counterpart.</summary>
        private bool ShouldMirrorOverride(Direction8 direction)
        {
            switch (direction)
            {
                case Direction8.Left:
                    return useFlipXForLeft;
                case Direction8.Right:
                    return useFlipXForRight;
                case Direction8.DownLeft:
                    return useFlipXForDownLeft;
                case Direction8.UpLeft:
                    return useFlipXForUpLeft;
                case Direction8.UpRight:
                    return useFlipXForUpRight;
                case Direction8.DownRight:
                    return useFlipXForDownRight;
                default:
                    return false;
            }
        }

        /// <summary>
        ///     Retrieves the idle/walk sprite assigned for the requested direction. When one of the pair is missing the
        ///     available sprite is returned regardless of movement state so fallback logic still has something to render.
        /// </summary>
        private Sprite GetSpriteForDirection(Direction8 direction, bool moving)
        {
            GetOverrideSprites(direction, out Sprite idleSprite, out Sprite walkSprite);
            if (moving)
            {
                if (walkSprite != null)
                    return walkSprite;
                if (idleSprite != null)
                    return idleSprite;
            }
            else
            {
                if (idleSprite != null)
                    return idleSprite;
                if (walkSprite != null)
                    return walkSprite;
            }

            return null;
        }

        /// <summary>Populates the idle and walk sprite references for a given direction.</summary>
        private void GetOverrideSprites(Direction8 direction, out Sprite idleSprite, out Sprite walkSprite)
        {
            idleSprite = null;
            walkSprite = null;

            switch (direction)
            {
                case Direction8.Down:
                    idleSprite = idleDown;
                    walkSprite = walkDown;
                    break;
                case Direction8.DownRight:
                    idleSprite = idleDownRight;
                    walkSprite = walkDownRight;
                    break;
                case Direction8.Right:
                    idleSprite = idleRight;
                    walkSprite = walkRight;
                    break;
                case Direction8.UpRight:
                    idleSprite = idleUpRight;
                    walkSprite = walkUpRight;
                    break;
                case Direction8.Up:
                    idleSprite = idleUp;
                    walkSprite = walkUp;
                    break;
                case Direction8.UpLeft:
                    idleSprite = idleUpLeft;
                    walkSprite = walkUpLeft;
                    break;
                case Direction8.Left:
                    idleSprite = idleLeft;
                    walkSprite = walkLeft;
                    break;
                case Direction8.DownLeft:
                    idleSprite = idleDownLeft;
                    walkSprite = walkDownLeft;
                    break;
            }
        }

        void FixedUpdate()
        {
            rb.linearVelocity = moveDir * moveSpeed;
        }

        /// <summary>
        /// Enables or disables external freezing of the player's movement.
        /// </summary>
        /// <param name="frozen">When true input and auto movement are halted.</param>
        public void SetMovementFrozen(bool frozen)
        {
            if (movementFrozen == frozen)
                return;

            movementFrozen = frozen;
            if (movementFrozen)
            {
                freezeSpriteStateBeforeFreeze = freezeSprite;
                StopMovement();
                freezeSprite = true;
            }
            else
            {
                freezeSprite = freezeSpriteStateBeforeFreeze;
            }
        }

        public void FaceTarget(Transform target)
        {
            if (target == null)
                return;

            Vector2 dir = (Vector2)target.position - (Vector2)transform.position;
            facingDir = Direction8Utility.FromVector(dir, allowDiagonals: true, fallback: facingDir);

            RefreshAnimator(moveDir.sqrMagnitude > 0f);
        }

        public void MoveTo(Vector2 target, float stopDistance, Action onComplete = null)
        {
            if (moveRoutine != null)
                StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(MoveToRoutine(target, stopDistance, onComplete));
        }

        public void MoveTo(Transform target, float stopDistance, Action onComplete = null)
        {
            if (moveRoutine != null)
                StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(MoveToRoutine(target, stopDistance, onComplete));
        }

        private IEnumerator MoveToRoutine(Vector2 target, float stopDistance, Action onComplete)
        {
            isAutoMoving = true;
            while (Vector2.Distance(transform.position, target) > stopDistance)
            {
                Vector2 dir = (target - (Vector2)transform.position).normalized;
                moveDir = dir;
                yield return null;
            }
            StopMovement();
            isAutoMoving = false;
            moveRoutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator MoveToRoutine(Transform target, float stopDistance, Action onComplete)
        {
            isAutoMoving = true;
            while (target != null && Vector2.Distance(transform.position, target.position) > stopDistance)
            {
                Vector2 dir = ((Vector2)target.position - (Vector2)transform.position).normalized;
                moveDir = dir;
                yield return null;
            }
            StopMovement();
            isAutoMoving = false;
            moveRoutine = null;
            if (target != null)
                onComplete?.Invoke();
        }

        /// <summary>
        /// Immediately halts any current movement and updates animation state.
        /// </summary>
        public void StopMovement()
        {
            moveDir = Vector2.zero;
            if (rb != null)
                rb.linearVelocity = Vector2.zero;
            if (anim != null)
                anim.SetBool("IsMoving", false);
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }
            isAutoMoving = false;
            HandleForcedIdle();
        }

        void OnApplicationQuit()
        {
            SavePosition();
        }

        /// <summary>
        /// Persist the player's current position to the active save profile. The data is queued for
        /// asynchronous persistence by <see cref="SaveManager"/> so callers should not assume the
        /// save has reached disk immediately after this method returns.
        /// </summary>
        public void SavePosition()
        {
            Vector3 pos = transform.position;
            var data = new PositionData
            {
                x = pos.x,
                y = pos.y,
                z = pos.z,
                scene = SceneManager.GetActiveScene().name
            };
            SaveManager.Save(PositionKey, data);
            SaveManager.UpdateLastKnownLocation(data.scene, pos);
        }

        /// <summary>
        /// Invoked by <see cref="SaveManager"/> during autosaves to capture the latest player position.
        /// </summary>
        public void Save()
        {
            SavePosition();
        }

        /// <summary>
        /// Invoked by <see cref="SaveManager"/> when the profile changes or during initialisation to
        /// restore the player to the saved location.
        /// </summary>
        public void Load()
        {
            LoadPosition();
        }

        private void LoadPosition()
        {
            var data = SaveManager.Load<PositionData>(PositionKey);
            if (data == null)
                return;
            if (isTransitioning)
                return;
            if (SceneTransitionManager.IsTransitioning)
                return;

            if (SceneManager.GetActiveScene().name == data.scene)
            {
                ApplySavedPosition();

                var pet = PetDropSystem.ActivePetObject;
                if (pet != null)
                {
                    pet.transform.position = transform.position;
                    var follower = pet.GetComponent<PetFollower>();
                    if (follower != null)
                        follower.SetPlayer(transform);
                }
            }
            else
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                var pet = PetDropSystem.ActivePetObject;
                if (pet != null)
                {
                    petToMove = pet;
                    DontDestroyOnLoad(pet);
                }
                SceneManager.LoadScene(data.scene);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var data = SaveManager.Load<PositionData>(PositionKey);
            if (data != null && scene.name == data.scene)
            {
                ApplySavedPosition();
                if (petToMove != null)
                {
                    petToMove.transform.position = transform.position;
                    var follower = petToMove.GetComponent<PetFollower>();
                    if (follower != null)
                        follower.SetPlayer(transform);
                    SceneManager.MoveGameObjectToScene(petToMove, scene);
                    petToMove = null;
                }
            }

            // Always unsubscribe after handling the scene load so duplicate registrations cannot accumulate.
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnTransitionStarted()
        {
            isTransitioning = true;
            HandleForcedIdle();
        }

        private void OnTransitionCompleted()
        {
            isTransitioning = false;
        }

        /// <summary>
        /// Attempts to move the player to the coordinates stored in the active save profile.
        /// </summary>
        /// <returns>True when a saved location existed and was applied.</returns>
        private bool ApplySavedPosition()
        {
            var data = SaveManager.Load<PositionData>(PositionKey);
            if (data == null)
                return false;

            transform.position = new Vector3(data.x, data.y, data.z);
            return true;
        }

        public override void OnBeforeSceneUnload()
        {
            base.OnBeforeSceneUnload();
        }

        public override void OnAfterSceneLoad(Scene scene)
        {
            base.OnAfterSceneLoad(scene);

            // Track which positioning strategy ended up being used so we only persist when a valid
            // location has been resolved for this scene.
            bool positionedFromSpawn = false;
            bool positionedFromSave = false;

            var spawnId = SceneTransitionManager.NextSpawnPoint;
            bool spawnRequested = !string.IsNullOrEmpty(spawnId);
            if (spawnRequested)
            {
                var points = GameObject.FindObjectsOfType<SpawnPoint>();
                foreach (var p in points)
                {
                    if (p.id == spawnId)
                    {
                        transform.position = p.transform.position;
                        positionedFromSpawn = true;
                        break;
                    }
                }

                if (!positionedFromSpawn)
                {
                    Debug.LogWarning($"SceneTransitionManager requested spawn point '{spawnId}' but no matching SpawnPoint was found in scene '{scene.name}'. Falling back to saved position if available.", this);
                }
            }

            if (!positionedFromSpawn)
                positionedFromSave = ApplySavedPosition();

            bool shouldPersistPosition = positionedFromSpawn || positionedFromSave;

            // Realign an active pet so it appears beside the player rather than
            // lingering at the origin during scene swaps.  Without this pass the
            // pet spawns at (0,0) because the player may not exist yet when the
            // pet system restores its position during the load sequence.
            var activePet = PetDropSystem.ActivePetObject;
            if (activePet != null)
            {
                activePet.transform.position = transform.position;
                var follower = activePet.GetComponent<PetFollower>();
                if (follower != null)
                    follower.SetPlayer(transform);

                if (activePet.scene != scene)
                    SceneManager.MoveGameObjectToScene(activePet, scene);
            }

            var players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var p in players)
            {
                if (p != gameObject)
                    Destroy(p);
            }

            // Remove any extra AudioListeners that may have been loaded with the
            // new scene.  Unity requires exactly one listener and keeping only the
            // player's avoids console warnings and audio glitches.
            var myListener = GetComponentInChildren<AudioListener>();
            if (myListener != null)
            {
                var listeners = GameObject.FindObjectsOfType<AudioListener>();
                foreach (var l in listeners)
                {
                    if (l != myListener)
                        Destroy(l);
                }
            }

            if (shouldPersistPosition)
                SavePosition();
        }

        /// <summary>
        /// Handles persistence updates while movement input is processed so the saved location stays
        /// current without incurring unnecessary writes.
        /// </summary>
        /// <param name="isMoving">Whether the player is considered to be moving this frame.</param>
        private void HandleMovementPersistenceAfterUpdate(bool isMoving)
        {
            if (isMoving)
            {
                movementSaveTimer += Time.unscaledDeltaTime;
                if (movementSaveTimer >= MovementSaveInterval)
                {
                    SavePosition();
                    movementSaveTimer = 0f;
                }
            }
            else if (wasMoving)
            {
                SavePosition();
                movementSaveTimer = 0f;
            }

            wasMoving = isMoving;
        }

        /// <summary>
        /// Ensures that any forced stop caused by transitions, menus, or scripted calls immediately
        /// persists the current position so reloads resume from the correct spot.
        /// </summary>
        private void HandleForcedIdle()
        {
            if (wasMoving)
                SavePosition();

            wasMoving = false;
            movementSaveTimer = 0f;
        }
    }
}
