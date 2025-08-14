// Assets/Scripts/Player/PlayerMover.cs
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerMover : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 3.5f;
        public bool fourWayOnly = true;
        [Tooltip("Deadzone used when reading analog sticks to snap to -1/0/1.")]
        public float gamepadDeadzone = 0.3f;

        [Header("(Optional) Direct Sprite Override")]
        [Tooltip("If assigned, these sprites will be applied directly each frame based on Dir/IsMoving. Leave null to rely on Animator clips.")]
        public Sprite idleDown, idleLeft, idleRight, idleUp;
        public Sprite walkDown, walkLeft, walkRight, walkUp;

        private Rigidbody2D rb;
        private Animator anim;
        private SpriteRenderer sr;

        // 0=Down, 1=Left, 2=Right, 3=Up
        private int facingDir = 0;
        private Vector2 moveDir;

#if ENABLE_INPUT_SYSTEM
        private InputAction moveAction;
#endif

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            anim = GetComponent<Animator>();
            sr  = GetComponent<SpriteRenderer>();

            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.WakeUp();
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

        void Update()
        {
            float x = 0f, y = 0f;

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
            if (isMoving)
            {
                switch (facingDir)
                {
                    case 0: desired = walkDown  ? walkDown  : idleDown;  break;
                    case 1: desired = walkLeft  ? walkLeft  : idleLeft;  break;
                    case 2: desired = walkRight ? walkRight : idleRight; break;
                    case 3: desired = walkUp    ? walkUp    : idleUp;    break;
                }
            }
            else
            {
                switch (facingDir)
                {
                    case 0: desired = idleDown;  break;
                    case 1: desired = idleLeft;  break;
                    case 2: desired = idleRight; break;
                    case 3: desired = idleUp;    break;
                }
            }
            if (desired != null && sr.sprite != desired)
                sr.sprite = desired;
            // --------------------------------------------------------------------------------------
        }

        void FixedUpdate()
        {
            rb.velocity = moveDir * moveSpeed;
        }
    }
}
