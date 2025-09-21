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
        public Sprite idleDown, idleLeft, idleRight, idleUp;
        public Sprite walkDown, walkLeft, walkRight, walkUp;
        [Tooltip("If true, flip right-facing sprites for left-facing movement/idle.")]
        public bool useFlipXForLeft;
        [Tooltip("If true, flip left-facing sprites for right-facing movement/idle.")]
        public bool useFlipXForRight;

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
        ///     Tracks whether this mover has resolved a position that belongs to the currently
        ///     active scene.  Autosave calls only write while the flag is true so cross-scene
        ///     staging windows cannot overwrite the stored metadata with mismatched scene names.
        /// </summary>
        private bool resolvedActiveScenePosition;
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
        ///     Indicates whether the mover has initiated a scene swap in order to honour the saved
        ///     position payload. While true, any intermediate scene loads must avoid persisting their
        ///     default coordinates, otherwise the saved profile would be overwritten with staging
        ///     scene metadata.
        /// </summary>
        private bool awaitingSavedSceneLoad;
        /// <summary>
        ///     Name of the scene that should become active before persistence resumes. This allows
        ///     the mover to detect when a saved-scene load is still in progress and defer
        ///     SavePosition calls until the correct location is ready.
        /// </summary>
        private string pendingSavedSceneName;
        /// <summary>
        /// Interval in seconds used to persist the position while movement is in progress. This
        /// keeps the stored location reasonably fresh without hammering the save file every frame.
        /// </summary>
        private const float MovementSaveInterval = 2f;

        // Ensure only one player persists across scene loads.
        private static PlayerMover instance;

        /// <summary>
        /// Provides global access to the active <see cref="PlayerMover"/> instance so external
        /// systems can reposition the player without hunting through the scene hierarchy.
        /// </summary>
        public static PlayerMover Instance => instance;

        /// <summary>
        /// Tracks whether the login flow is orchestrating a resume so automatic saves are deferred
        /// until the player has been placed at the desired coordinates.
        /// </summary>
        private static bool loginResumeActive;

        /// <summary>
        /// Snapshot captured during the login-driven resume sequence. This allows the mover to
        /// expose the planned spawn data to other systems while saves are deferred.
        /// </summary>
        private static PlayerPositionSnapshot loginResumeSnapshot = PlayerPositionSnapshot.Empty;

        [Serializable]
        private class PositionData
        {
            public float x;
            public float y;
            public float z;
            public string scene;
        }

        /// <summary>
        /// Data carrier that exposes the last persisted player scene and coordinates so callers can
        /// query the save system without instantiating a mover.
        /// </summary>
        public readonly struct PlayerPositionSnapshot
        {
            /// <summary>
            /// Name of the scene that owns the stored coordinates.
            /// </summary>
            public string SceneName { get; }

            /// <summary>
            /// World position the player occupied when the snapshot was taken.
            /// </summary>
            public Vector3 Position { get; }

            /// <summary>
            /// Indicates whether the snapshot originated from a valid save entry.
            /// </summary>
            public bool HasValidData { get; }

            private PlayerPositionSnapshot(string sceneName, Vector3 position, bool hasValidData)
            {
                SceneName = string.IsNullOrEmpty(sceneName) ? string.Empty : sceneName;
                Position = position;
                HasValidData = hasValidData && !string.IsNullOrEmpty(SceneName);
            }

            /// <summary>
            /// Returns an empty snapshot used when no persisted data exists for the active profile.
            /// </summary>
            public static PlayerPositionSnapshot Empty => new PlayerPositionSnapshot(string.Empty, Vector3.zero, false);

            /// <summary>
            /// Creates a snapshot pointing at the supplied scene and position. The caller controls
            /// whether the payload should be considered a fully valid save entry.
            /// </summary>
            public static PlayerPositionSnapshot Create(string sceneName, Vector3 position, bool hasValidData)
            {
                return new PlayerPositionSnapshot(sceneName, position, hasValidData);
            }
        }

        private const string PositionKey = "PlayerPosition";

        // 0=Down, 1=Left, 2=Right, 3=Up
        private int facingDir = 0;
        private Vector2 moveDir;

        /// <summary>Current facing direction: 0=Down, 1=Left, 2=Right, 3=Up.</summary>
        public int FacingDir => facingDir;

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
        /// Attempts to resolve the last persisted position payload without instantiating a mover.
        /// </summary>
        /// <param name="snapshot">Outputs the stored scene and coordinates when available.</param>
        /// <returns>True when a saved snapshot exists for the active profile.</returns>
        public static bool TryGetLastSavedSnapshot(out PlayerPositionSnapshot snapshot)
        {
            var data = SaveManager.Load<PositionData>(PositionKey);
            if (data == null || string.IsNullOrEmpty(data.scene))
            {
                snapshot = PlayerPositionSnapshot.Empty;
                return false;
            }

            snapshot = PlayerPositionSnapshot.Create(
                data.scene,
                new Vector3(data.x, data.y, data.z),
                hasValidData: true);
            return true;
        }

        /// <summary>
        /// Flags that the login flow is about to resume a gameplay scene so automatic saves are
        /// paused until the external placement logic completes.
        /// </summary>
        /// <param name="snapshot">Snapshot describing the desired resume location.</param>
        public static void BeginLoginResume(PlayerPositionSnapshot snapshot)
        {
            loginResumeSnapshot = snapshot;
            loginResumeActive = true;
        }

        /// <summary>
        /// Clears the login resume guard so autosaves and movement persistence resume as normal.
        /// </summary>
        public static void CompleteLoginResume()
        {
            loginResumeSnapshot = PlayerPositionSnapshot.Empty;
            loginResumeActive = false;
        }

        /// <summary>
        /// Returns the snapshot currently being honoured by the login resume process.
        /// </summary>
        public static PlayerPositionSnapshot CurrentLoginResumeSnapshot => loginResumeSnapshot;

        /// <summary>
        /// Indicates whether the mover should defer automatic save writes while an external login
        /// workflow positions the player.
        /// </summary>
        public static bool IsLoginResumeActive => loginResumeActive;

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

            if (inventory != null && inventory.BankOpen)
            {
                moveDir = Vector2.zero;
                rb.linearVelocity = Vector2.zero;
                anim.SetBool("IsMoving", false);
                HandleForcedIdle();
                return;
            }

            float x = 0f, y = 0f;

#if ENABLE_INPUT_SYSTEM
            Vector2 raw = moveAction != null ? moveActionValue : Vector2.zero;
            // Snap analog to -1/0/1 so animations are stable
            x = Mathf.Abs(raw.x) < gamepadDeadzone ? 0f : Mathf.Sign(raw.x);
            y = Mathf.Abs(raw.y) < gamepadDeadzone ? 0f : Mathf.Sign(raw.y);
#else
            // Legacy input fallback if project uses Old/Both
            x = Input.GetAxisRaw("Horizontal");
            y = Input.GetAxisRaw("Vertical");
#endif

            if (fourWayOnly)
            {
                if (Mathf.Abs(y) > Mathf.Abs(x)) x = 0f;
                else if (Mathf.Abs(x) > Mathf.Abs(y)) y = 0f;
            }

            Vector2 inputDir = new Vector2(x, y).normalized;

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
                if (Mathf.Abs(moveDir.x) > Mathf.Abs(moveDir.y))
                    facingDir = moveDir.x < 0 ? 1 : 2; // left/right
                else
                    facingDir = moveDir.y < 0 ? 0 : 3; // down/up
            }

            // Drive Animator (kept for future use and for state visibility)
            bool isMoving = moveDir.sqrMagnitude > 0f;
            RefreshAnimator(isMoving);
            HandleMovementPersistenceAfterUpdate(isMoving);
        }

        private void RefreshAnimator(bool isMoving)
        {
            anim.SetBool("IsMoving", isMoving);
            anim.SetInteger("Dir", facingDir);

            // --- OPTIONAL: Direct sprite override (solves your 'stuck on IdleDown_0' instantly) ---
            Sprite desired = null;
            bool flip = false;
            if (isMoving)
            {
                switch (facingDir)
                {
                    case 0:
                        desired = walkDown ? walkDown : idleDown;
                        break;
                    case 1:
                        if (useFlipXForLeft)
                        {
                            desired = walkRight ? walkRight : idleRight;
                            flip = true;
                        }
                        else
                        {
                            desired = walkLeft ? walkLeft : idleLeft;
                        }
                        break;
                    case 2:
                        if (useFlipXForRight)
                        {
                            desired = walkLeft ? walkLeft : idleLeft;
                            flip = true;
                        }
                        else
                        {
                            desired = walkRight ? walkRight : idleRight;
                        }
                        break;
                    case 3:
                        desired = walkUp ? walkUp : idleUp;
                        break;
                }
            }
            else
            {
                switch (facingDir)
                {
                    case 0:
                        desired = idleDown;
                        break;
                    case 1:
                        if (useFlipXForLeft)
                        {
                            desired = idleRight ? idleRight : walkRight;
                            flip = true;
                        }
                        else
                        {
                            desired = idleLeft;
                        }
                        break;
                    case 2:
                        if (useFlipXForRight)
                        {
                            desired = idleLeft ? idleLeft : walkLeft;
                            flip = true;
                        }
                        else
                        {
                            desired = idleRight;
                        }
                        break;
                    case 3:
                        desired = idleUp;
                        break;
                }
            }
            if (desired != null && !freezeSprite)
            {
                if (sr.flipX != flip)
                    sr.flipX = flip;
                if (sr.sprite != desired)
                    sr.sprite = desired;
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
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
                facingDir = dir.x < 0 ? 1 : 2;
            else
                facingDir = dir.y < 0 ? 0 : 3;

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
        /// Persist the player's current position to the active save profile.
        /// </summary>
        /// <param name="sceneNameOverride">
        /// Optional scene identifier to store alongside the position. When null the
        /// active scene reported by <see cref="SceneManager"/> is used instead.
        /// </param>
        public void SavePosition(string sceneNameOverride = null, bool allowDuringLoginResume = false)
        {
            if (!resolvedActiveScenePosition)
                return;

            if (loginResumeActive && !allowDuringLoginResume)
                return;

            Vector3 pos = transform.position;
            var data = new PositionData
            {
                x = pos.x,
                y = pos.y,
                z = pos.z,
                scene = string.IsNullOrEmpty(sceneNameOverride)
                    ? SceneManager.GetActiveScene().name
                    : sceneNameOverride
            };
            SaveManager.Save(PositionKey, data);
        }

        /// <summary>
        /// Invoked by <see cref="SaveManager"/> during autosaves to capture the latest player position.
        /// </summary>
        public void Save()
        {
            if (!resolvedActiveScenePosition)
                return;

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
            resolvedActiveScenePosition = false;

            awaitingSavedSceneLoad = false;
            pendingSavedSceneName = null;

            var data = SaveManager.Load<PositionData>(PositionKey);
            if (data == null)
            {
                // No saved metadata exists yet, so treat the authored scene placement as resolved
                // to allow fresh profiles to persist their first position snapshot.
                resolvedActiveScenePosition = true;
                return;
            }
            if (isTransitioning)
                return;
            if (SceneTransitionManager.IsTransitioning)
                return;

            if (SceneManager.GetActiveScene().name == data.scene)
            {
                if (ApplySavedPosition())
                    resolvedActiveScenePosition = true;

                awaitingSavedSceneLoad = false;
                pendingSavedSceneName = null;

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
                awaitingSavedSceneLoad = true;
                pendingSavedSceneName = data.scene;
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
                bool appliedSavedPosition = ApplySavedPosition();
                if (appliedSavedPosition)
                {
                    resolvedActiveScenePosition = true;

                    // Clear the deferred load flags once the saved position has been restored so the
                    // follow-up OnAfterSceneLoad pass can resume normal persistence handling.
                    awaitingSavedSceneLoad = false;
                    pendingSavedSceneName = null;
                }

                if (petToMove != null)
                {
                    petToMove.transform.position = transform.position;
                    var follower = petToMove.GetComponent<PetFollower>();
                    if (follower != null)
                        follower.SetPlayer(transform);
                    SceneManager.MoveGameObjectToScene(petToMove, scene);
                    petToMove = null;
                }

                if (appliedSavedPosition)
                {
                    // The saved-scene handoff has finished so this handler can be removed immediately.
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    return;
                }
            }

            // Always unsubscribe after handling the scene load so duplicate registrations cannot accumulate.
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnTransitionStarted()
        {
            isTransitioning = true;
            resolvedActiveScenePosition = false;
            HandleForcedIdle();
        }

        private void OnTransitionCompleted()
        {
            isTransitioning = false;
        }

        /// <summary>
        /// Applies the provided position payload to the player if valid.
        /// </summary>
        /// <param name="data">Persisted position data captured from <see cref="SavePosition"/>.</param>
        /// <returns>True when a non-null payload was applied to the transform.</returns>
        private bool ApplyPositionData(PositionData data)
        {
            if (data == null)
                return false;

            transform.position = new Vector3(data.x, data.y, data.z);
            return true;
        }

        /// <summary>
        /// Attempts to move the player to the coordinates stored in the active save profile.
        /// </summary>
        /// <returns>True when a saved location existed and was applied.</returns>
        private bool ApplySavedPosition()
        {
            var data = SaveManager.Load<PositionData>(PositionKey);
            return ApplyPositionData(data);
        }

        public override void OnBeforeSceneUnload()
        {
            base.OnBeforeSceneUnload();
        }

        public override void OnAfterSceneLoad(Scene scene)
        {
            base.OnAfterSceneLoad(scene);

            // Track which positioning strategy ended up being used so we only persist when a valid
            // location has been resolved for this scene.  The additional flag ensures we do not
            // clobber saved metadata with coordinates from a different scene when the player is
            // about to transition.
            bool skipPositionResolution = awaitingSavedSceneLoad
                && !string.IsNullOrEmpty(pendingSavedSceneName)
                && !string.Equals(scene.name, pendingSavedSceneName, StringComparison.Ordinal);

            bool resolvedActiveSceneLocation = false;

            if (!skipPositionResolution)
            {
                bool positionedFromSpawn = false;
                bool positionedFromSave = false;

                var savedData = SaveManager.Load<PositionData>(PositionKey);
                bool hasSavedPosition = savedData != null;
                bool savedSceneMatches = hasSavedPosition && savedData.scene == scene.name;

                var spawnId = SceneTransitionManager.NextSpawnPoint;
                bool spawnRequested = !string.IsNullOrEmpty(spawnId);

                // When we're resuming the exact scene captured in the save and no explicit spawn override
                // is active, prefer the persisted coordinates so logout/login flows feel seamless.
                if (savedSceneMatches && !spawnRequested)
                {
                    positionedFromSave = ApplyPositionData(savedData);
                    resolvedActiveSceneLocation = positionedFromSave;
                    if (positionedFromSave)
                        resolvedActiveScenePosition = true;
                }
                else
                {
                    if (!spawnRequested && !hasSavedPosition)
                    {
                        // No saved data or spawn override exists, so the authored scene placement already
                        // represents a valid location for this scene. Treat it as resolved so persistence can begin.
                        resolvedActiveSceneLocation = true;
                        resolvedActiveScenePosition = true;
                    }
                    else
                    {
                        if (spawnRequested)
                        {
                            var points = GameObject.FindObjectsOfType<SpawnPoint>();
                            foreach (var p in points)
                            {
                                if (p.id == spawnId)
                                {
                                    transform.position = p.transform.position;
                                    positionedFromSpawn = true;
                                    resolvedActiveSceneLocation = true;
                                    resolvedActiveScenePosition = true;
                                    break;
                                }
                            }

                            if (!positionedFromSpawn)
                            {
                                Debug.LogWarning($"SceneTransitionManager requested spawn point '{spawnId}' but no matching SpawnPoint was found in scene '{scene.name}'. Falling back to saved position if available.", this);
                            }
                        }

                        if (!positionedFromSpawn && hasSavedPosition)
                        {
                            if (savedSceneMatches)
                            {
                                positionedFromSave = ApplyPositionData(savedData);

                                // Only treat the fallback as a resolved active-scene location when the saved
                                // data already targets the scene we just loaded.  Otherwise we would be
                                // overwriting the saved scene identifier before the deferred LoadPosition call
                                // has a chance to move the player into the correct interior.
                                if (positionedFromSave)
                                {
                                    resolvedActiveSceneLocation = true;
                                    resolvedActiveScenePosition = true;
                                }
                            }
                            // When the saved scene differs keep the authored/default transform so the
                            // deferred LoadPosition flow can relocate the mover after the correct scene loads.
                        }
                    }
                }

                if (awaitingSavedSceneLoad && string.Equals(scene.name, pendingSavedSceneName, StringComparison.Ordinal))
                {
                    awaitingSavedSceneLoad = false;
                    pendingSavedSceneName = null;
                }
            }
            else
            {
                // The saved profile is directing us to another scene, so avoid persisting the staging
                // coordinates. Reset movement tracking so SavePosition remains suppressed until the
                // correct scene becomes active.
                resolvedActiveScenePosition = false;
                wasMoving = false;
                movementSaveTimer = 0f;
            }

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

            // Persist the updated position only when we resolved a location that belongs to the
            // active scene.  When the saved data targets another scene the deferred LoadPosition
            // call will handle the swap without us touching the stored metadata here.
            if (!skipPositionResolution && resolvedActiveSceneLocation)
                SavePosition(scene.name);
        }

        /// <summary>
        /// Handles persistence updates while movement input is processed so the saved location stays
        /// current without incurring unnecessary writes.
        /// </summary>
        /// <param name="isMoving">Whether the player is considered to be moving this frame.</param>
        private void HandleMovementPersistenceAfterUpdate(bool isMoving)
        {
            if (!resolvedActiveScenePosition)
            {
                wasMoving = isMoving;
                movementSaveTimer = 0f;
                return;
            }

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
            if (wasMoving && resolvedActiveScenePosition)
                SavePosition();

            wasMoving = false;
            movementSaveTimer = 0f;
        }
    }
}
