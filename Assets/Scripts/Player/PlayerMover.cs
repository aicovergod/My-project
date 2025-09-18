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
    public class PlayerMover : ScenePersistentObject
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

            LoadPosition();

            SceneTransitionManager.TransitionStarted += OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted += OnTransitionCompleted;
        }

#if ENABLE_INPUT_SYSTEM
        void OnEnable()
        {
            moveAction = InputActionResolver.Resolve(playerInput, moveActionReference, "Move", out moveActionEnabledByResolver);

            if (moveAction != null)
            {
                moveAction.performed += OnMovePerformed;
                moveAction.canceled += OnMoveCanceled;
                moveActionValue = moveAction.ReadValue<Vector2>();
            }
        }

        void OnDisable()
        {
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
        }

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
            if (instance == this)
                instance = null;

            SceneTransitionManager.TransitionStarted -= OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted -= OnTransitionCompleted;
        }

        void Update()
        {
            if (isTransitioning)
            {
                moveDir = Vector2.zero;
                rb.linearVelocity = Vector2.zero;
                anim.SetBool("IsMoving", false);
                return;
            }

            if (movementFrozen)
            {
                moveDir = Vector2.zero;
                if (rb != null)
                    rb.linearVelocity = Vector2.zero;
                anim.SetBool("IsMoving", false);
                return;
            }

            if (inventory != null && inventory.BankOpen)
            {
                moveDir = Vector2.zero;
                rb.linearVelocity = Vector2.zero;
                anim.SetBool("IsMoving", false);
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
        }

        void OnApplicationQuit()
        {
            SavePosition();
        }

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
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
        }

        private void OnTransitionStarted()
        {
            isTransitioning = true;
        }

        private void OnTransitionCompleted()
        {
            isTransitioning = false;
        }

        private void ApplySavedPosition()
        {
            var data = SaveManager.Load<PositionData>(PositionKey);
            if (data != null)
                transform.position = new Vector3(data.x, data.y, data.z);
        }

        public override void OnBeforeSceneUnload()
        {
            base.OnBeforeSceneUnload();
        }

        public override void OnAfterSceneLoad(Scene scene)
        {
            base.OnAfterSceneLoad(scene);

            var spawnId = SceneTransitionManager.NextSpawnPoint;
            if (!string.IsNullOrEmpty(spawnId))
            {
                var points = GameObject.FindObjectsOfType<SpawnPoint>();
                foreach (var p in points)
                {
                    if (p.id == spawnId)
                    {
                        transform.position = p.transform.position;
                        break;
                    }
                }
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

            SavePosition();
        }
    }
}
