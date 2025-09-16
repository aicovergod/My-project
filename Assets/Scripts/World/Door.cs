using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace World
{
    /// <summary>
    /// Simple door interaction.  When the player clicks on the door the specified
    /// scene is loaded.  If a required item ID is provided the player must possess
    /// that item in their inventory to use the door.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Door : MonoBehaviour
    {
        [Tooltip("Name of the scene to load when this door is used.")]
        public string sceneToLoad;

        [Tooltip("Optional item ID required to use this door.  Leave empty for no requirement.")]
        public string requiredItemId;

        [Tooltip("If true, the required item will be removed from the player's inventory when used.")]
        public bool removeItemOnUse;

        [Tooltip("Text to display if the player lacks the required item.")]
        public string missingItemMessage;

        [Tooltip("Name of the spawn point in the target scene where the player should appear.")]
        public string spawnPointName;

        [Tooltip("How close the player must be in tiles to use the door.")]
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
                    StartCoroutine(UseDoor());
                    break;
                }
            }
        }

        private IEnumerator UseDoor()
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
        ///     Checks whether the pointer is hovering a UI element managed by the Input System UI module.
        /// </summary>
        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null)
                return false;

            if (!(EventSystem.current.currentInputModule is InputSystemUIInputModule module))
                return false;

            Pointer pointer = Pointer.current;
            if (pointer == null)
                return false;

            return module.IsPointerOverGameObject(pointer.pointerId);
        }
    }
}
