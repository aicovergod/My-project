using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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

        private bool _transitioning;

        private void OnEnable()
        {
            SceneTransitionManager.TransitionStarted += OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted += OnTransitionCompleted;
        }

        private void OnDisable()
        {
            SceneTransitionManager.TransitionStarted -= OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted -= OnTransitionCompleted;
        }

        private void Update()
        {
            if (_transitioning)
                return;

            if (!Input.GetMouseButtonDown(0))
                return;

            if (IsPointerOverUI())
                return;

            var worldPoint = (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);
            foreach (var col in Physics2D.OverlapPointAll(worldPoint))
            {
                if (col.gameObject == gameObject)
                {
                    StartCoroutine(UseInteractable());
                    break;
                }
            }
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
                return EventSystem.current.IsPointerOverGameObject();

            return false;
        }
    }
}
