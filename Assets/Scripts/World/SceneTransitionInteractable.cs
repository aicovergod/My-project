using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using Core.Input;
using Skills;

namespace World
{
    /// <summary>
    ///     Generic scene transition interactable that can represent doors, ladders, cave entrances or any
    ///     similar prop. When clicked (or tapped) it optionally validates inventory/skill requirements before
    ///     triggering a scene change through the <see cref="SceneTransitionManager"/>.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SceneTransitionInteractable : MonoBehaviour
    {
        [Tooltip("Name of the scene to load when this interactable is used.")]
        public string sceneToLoad;

        [Tooltip("Optional item ID required to use this interactable. Leave empty for no requirement.")]
        public string requiredItemId;

        [Tooltip("If true, the required item will be removed from the player's inventory when used.")]
        public bool removeItemOnUse;

        [Tooltip("Text to display if the player lacks the required item.")]
        public string missingItemMessage;

        [Header("Skill Requirement")]
        [Tooltip("If enabled, the player must reach the specified skill level to use this interactable.")]
        public bool requireSkillLevel;

        [Tooltip("Skill that must meet the required level. Only used when Require Skill Level is enabled.")]
        public SkillType requiredSkill;

        [Tooltip("Minimum level in the required skill needed to use this interactable.")]
        public int requiredSkillLevel = 1;

        [Tooltip("Name of the spawn point in the target scene where the player should appear.")]
        public string spawnPointName;

        [Tooltip("How close the player must be in tiles to activate this interactable.")]
        public float useRadius = 2f;

        [Header("Input")]
        [SerializeField]
        [Tooltip("Player input component supplying the default action map. Auto-resolved when omitted.")]
        private PlayerInput playerInput;

        [SerializeField]
        [Tooltip("Optional override for the interact/confirm action used to trigger the transition.")]
        private InputActionReference interactActionReference;

        private bool _transitioning;
        private InputAction interactAction;
        private bool interactActionOwned;
        private bool _hasPendingInteractRequest;
        private Vector2 _pendingScreenPosition;
        private bool _pendingFromTouch;
        private int _pendingTouchId = -1;

        private void OnEnable()
        {
            SceneTransitionManager.TransitionStarted += OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted += OnTransitionCompleted;
            SubscribeToInput();
        }

        private void OnDisable()
        {
            SceneTransitionManager.TransitionStarted -= OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted -= OnTransitionCompleted;
            UnsubscribeFromInput();
            ClearPendingInteractRequest();
        }

        private void HandleInteractAction(InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            if (_transitioning)
                return;

            if (TryQueueTouchRequest(context))
                return;

            if (IsPointerBlockedByUI(context))
                return;

            Vector2 screenPosition = ResolveScreenPosition(context);
            TryResolveInteractRequest(screenPosition);
        }

        private void Update()
        {
            if (!_hasPendingInteractRequest)
                return;

            if (_transitioning)
            {
                ClearPendingInteractRequest();
                return;
            }

            bool pointerBlocked = false;
            if (EventSystem.current != null && _pendingFromTouch && _pendingTouchId >= 0)
                pointerBlocked = EventSystem.current.IsPointerOverGameObject(_pendingTouchId);

            if (pointerBlocked)
            {
                ClearPendingInteractRequest();
                return;
            }

            TryResolveInteractRequest(_pendingScreenPosition);
            ClearPendingInteractRequest();
        }

        private IEnumerator UseInteractable()
        {
            if (_transitioning)
                yield break;

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) yield break;

            if (Vector2.Distance(player.transform.position, transform.position) > useRadius)
                yield break;

            Inventory.Inventory inv = player.GetComponent<Inventory.Inventory>();
            if (!string.IsNullOrEmpty(requiredItemId))
            {
                if (inv == null || !inv.HasItem(requiredItemId))
                {
                    if (!string.IsNullOrEmpty(missingItemMessage))
                        PopupText.Show(missingItemMessage, player.transform);
                    yield break;
                }

            }

            if (requireSkillLevel)
            {
                // Validate the player's skill level before allowing the transition.
                SkillManager skillManager = player.GetComponent<SkillManager>();
                if (skillManager == null)
                {
                    // Failing silently would be confusing, so log a warning for designers.
                    Debug.LogWarning($"Interactable {name} requires a skill check but the player is missing a SkillManager component.");
                    yield break;
                }

                int requiredLevel = Mathf.Max(1, requiredSkillLevel);
                int currentLevel = skillManager.GetLevel(requiredSkill);
                if (currentLevel < requiredLevel)
                {
                    PopupText.Show($"You need {requiredLevel} {requiredSkill} to enter", player.transform);
                    yield break;
                }
            }

            if (!string.IsNullOrEmpty(sceneToLoad))
            {
                if (SceneTransitionManager.Instance == null)
                    new GameObject("SceneTransitionManager").AddComponent<SceneTransitionManager>();

                if (SceneTransitionManager.Instance != null)
                {
                    yield return SceneTransitionManager.Instance.Transition(
                        sceneToLoad,
                        spawnPointName,
                        requiredItemId,
                        removeItemOnUse);
                }
            }
        }

        private void OnTransitionStarted() => _transitioning = true;

        private void OnTransitionCompleted() => _transitioning = false;

        /// <summary>
        ///     Checks whether the pointer is hovering a UI element registered with the active <see cref="EventSystem"/>.
        /// </summary>
        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null)
                return false;

            // Evaluate active touches first so mobile presses correctly block interactable usage.
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var touchControl = touches[i];
                    if (!touchControl.press.isPressed)
                        continue;

                    if (EventSystem.current.IsPointerOverGameObject(touchControl.touchId.ReadValue()))
                        return true;
                }
            }

            // If a mouse or pen pointer is available, rely on the default EventSystem behaviour.
            Pointer pointer = Pointer.current;
            if (pointer != null && !(pointer is Touchscreen))
                return EventSystem.current.IsPointerOverGameObject(PointerId.mousePointerId);

            return false;
        }

        /// <summary>
        ///     Determines whether the current interaction press should be blocked because the pointer is over a UI element.
        ///     Uses touch specific pointer IDs when available so mobile presses continue to honour EventSystem filtering.
        /// </summary>
        private static bool IsPointerBlockedByUI(InputAction.CallbackContext context)
        {
            if (EventSystem.current == null)
                return false;

            if (context.control != null)
            {
                if (context.control.parent is TouchControl touchControl)
                {
                    int touchId = touchControl.touchId.ReadValue();
                    if (EventSystem.current.IsPointerOverGameObject(touchId))
                        return true;
                }
                else if (context.control.device is Pointer pointer && !(pointer is Touchscreen))
                {
                    if (EventSystem.current.IsPointerOverGameObject(PointerId.mousePointerId))
                        return true;
                }
            }

            return IsPointerOverUI();
        }

        /// <summary>
        ///     Resolves the screen position associated with the current input context, falling back to the active pointer device
        ///     when the action originates from a non-pointer binding (e.g. controller confirm).
        /// </summary>
        private static Vector2 ResolveScreenPosition(InputAction.CallbackContext context)
        {
            if (context.control != null)
            {
                if (context.control.parent is TouchControl touchControl)
                    return touchControl.position.ReadValue();
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
                return mouse.position.ReadValue();

            if (context.control != null)
            {
                if (context.control.device is Pointer pointerDevice)
                    return pointerDevice.position.ReadValue();
            }

            Pointer pointer = Pointer.current;
            if (pointer != null)
                return pointer.position.ReadValue();

            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        /// <summary>
        ///     Attempts to queue a pointer-driven interaction so UI checks can be evaluated safely during Update.
        /// </summary>
        private bool TryQueueTouchRequest(InputAction.CallbackContext context)
        {
            if (context.control == null)
                return false;

            if (context.control.parent is TouchControl touchControl)
            {
                QueuePendingInteractRequest(
                    touchControl.position.ReadValue(),
                    touchControl.touchId.ReadValue());
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Executes the collider hit test using a cached screen position and begins the transition coroutine when valid.
        /// </summary>
        private void TryResolveInteractRequest(Vector2 screenPosition)
        {
            Camera activeCamera = Camera.main;
            if (activeCamera == null)
                return;

            Vector2 worldPoint = activeCamera.ScreenToWorldPoint(screenPosition);

            foreach (var col in Physics2D.OverlapPointAll(worldPoint))
            {
                if (col.gameObject == gameObject)
                {
                    StartCoroutine(UseInteractable());
                    break;
                }
            }
        }

        /// <summary>
        ///     Stores the pending interaction data until it can be processed during Update.
        /// </summary>
        private void QueuePendingInteractRequest(Vector2 screenPosition, int touchId)
        {
            _pendingScreenPosition = screenPosition;
            _pendingFromTouch = true;
            _pendingTouchId = touchId;
            _hasPendingInteractRequest = true;
        }

        /// <summary>
        ///     Clears any cached interaction data to avoid leaking callbacks across scene transitions or disables.
        /// </summary>
        private void ClearPendingInteractRequest()
        {
            _hasPendingInteractRequest = false;
            _pendingScreenPosition = default;
            _pendingFromTouch = false;
            _pendingTouchId = -1;
        }

        /// <summary>
        ///     Resolves and subscribes to the configured interact action so pointer and controller inputs remain functional.
        /// </summary>
        private void SubscribeToInput()
        {
            UnsubscribeFromInput();

            if (playerInput == null)
            {
                playerInput = GetComponent<PlayerInput>();
                if (playerInput == null)
                    playerInput = GetComponentInParent<PlayerInput>();
                if (playerInput == null)
                    playerInput = FindObjectOfType<PlayerInput>();
                if (playerInput == null)
                    Debug.LogWarning($"SceneTransitionInteractable on {name} could not locate a PlayerInput in the scene. Interactions will be disabled until one becomes available.");
            }

            interactAction = InputActionResolver.Resolve(playerInput, interactActionReference, "Interact", out interactActionOwned);
            if (interactAction != null)
                interactAction.performed += HandleInteractAction;
        }

        /// <summary>
        ///     Cleans up input callbacks and disables actions that were enabled through the resolver.
        /// </summary>
        private void UnsubscribeFromInput()
        {
            if (interactAction != null)
            {
                interactAction.performed -= HandleInteractAction;
                if (interactActionOwned)
                    interactAction.Disable();
                interactAction = null;
                interactActionOwned = false;
            }
        }
    }
}
