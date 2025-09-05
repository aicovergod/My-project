// Assets/Scripts/Player/PlayerMover.cs
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using Core.Save;
using World;
using Pets;
using Util;

namespace Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerMover : MonoBehaviour, IScenePersistent
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

        private Rigidbody2D rb;
        private Animator anim;
        private SpriteRenderer sr;
        private Inventory.Inventory inventory;
        private GameObject petToMove;
        private bool isTransitioning;
        private bool isAutoMoving;
        private Coroutine moveRoutine;

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

#if ENABLE_INPUT_SYSTEM
        private InputAction moveAction;
#endif

        void Awake()
        {
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
            SceneTransitionManager.RegisterPersistentObject(this);
        }

#if ENABLE_INPUT_SYSTEM
        void OnEnable()
        {
            // Self-contained Move action (no .inputactions asset required)
            moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");

            // WASD
            var wasd = moveAction.AddCompositeBinding("2DVector");
            wasd.With("Up", "<Keyboard>/w");
            wasd.With("Down", "<Keyboard>/s");
            wasd.With("Left", "<Keyboard>/a");
            wasd.With("Right", "<Keyboard>/d");

            // Arrow keys
            var arrows = moveAction.AddCompositeBinding("2DVector");
            arrows.With("Up", "<Keyboard>/upArrow");
            arrows.With("Down", "<Keyboard>/downArrow");
            arrows.With("Left", "<Keyboard>/leftArrow");
            arrows.With("Right", "<Keyboard>/rightArrow");

            // Gamepad
            moveAction.AddBinding("<Gamepad>/leftStick");

            moveAction.Enable();
        }

        void OnDisable()
        {
            moveAction?.Disable();
        }
#endif

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            SceneTransitionManager.TransitionStarted -= OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted -= OnTransitionCompleted;
            SceneTransitionManager.UnregisterPersistentObject(this);
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

            if (inventory != null && inventory.BankOpen)
            {
                moveDir = Vector2.zero;
                rb.linearVelocity = Vector2.zero;
                anim.SetBool("IsMoving", false);
                return;
            }

            float x = 0f, y = 0f;

            if (!isAutoMoving)
            {
#if ENABLE_INPUT_SYSTEM
                Vector2 raw = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
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

                moveDir = new Vector2(x, y).normalized;
            }

            if (moveDir.sqrMagnitude > 0f)
            {
                if (Mathf.Abs(moveDir.x) > Mathf.Abs(moveDir.y))
                    facingDir = moveDir.x < 0 ? 1 : 2; // left/right
                else
                    facingDir = moveDir.y < 0 ? 0 : 3; // down/up
            }

            // Drive Animator (kept for future use and for state visibility)
            bool isMoving = moveDir.sqrMagnitude > 0f;
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
            // --------------------------------------------------------------------------------------
        }

        void FixedUpdate()
        {
            rb.linearVelocity = moveDir * moveSpeed;
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

        public void OnBeforeSceneUnload()
        {
            DontDestroyOnLoad(gameObject);
        }

        public void OnAfterSceneLoad(Scene scene)
        {
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

            SceneManager.MoveGameObjectToScene(gameObject, scene);
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
