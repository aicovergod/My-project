using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace World
{
    /// <summary>
    /// Ensures that design-time preview cameras remain visible in the editor
    /// but automatically disable themselves as soon as gameplay begins so the
    /// persistent <see cref="Camera.main"/> instance can drive the scene.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class EditorOnlyCameraHider : MonoBehaviour
    {
#if !UNITY_EDITOR
        /// <summary>
        /// In player builds we never want the helper or its camera to exist, so
        /// destroy the entire object immediately.
        /// </summary>
        private void Awake()
        {
            Destroy(gameObject);
        }
#else
        private bool isSubscribedToEditorEvents;

        /// <summary>
        /// Subscribe to play-mode events and make sure the preview camera is
        /// visible while we are in edit mode.
        /// </summary>
        private void OnEnable()
        {
            if (!isSubscribedToEditorEvents)
            {
                EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
                isSubscribedToEditorEvents = true;
            }

            ApplyDesiredState();
        }

        /// <summary>
        /// When the component is manually disabled (while the GameObject remains
        /// active) we intentionally unsubscribe to avoid leaking delegates. If the
        /// entire GameObject is disabled we stay subscribed so we can bring the
        /// preview camera back once play mode ends.
        /// </summary>
        private void OnDisable()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            UnsubscribeFromEditorEvents();
        }

        /// <summary>
        /// Final cleanup hook so we never leave the static editor event with a
        /// dangling reference when the helper is destroyed in the editor.
        /// </summary>
        private void OnDestroy()
        {
            UnsubscribeFromEditorEvents();
        }

        /// <summary>
        /// Unity calls Update even in edit mode thanks to <see cref="ExecuteAlways"/>.
        /// We watch the play state every frame so that the preview camera flips
        /// between active (edit mode) and inactive (play mode) instantly.
        /// </summary>
        private void Update()
        {
            ApplyDesiredState();
        }

        /// <summary>
        /// Ensures the camera is enabled in edit mode and disabled in play mode.
        /// </summary>
        private void ApplyDesiredState()
        {
            if (Application.isPlaying || EditorApplication.isPlaying)
            {
                DisableForRuntime();
            }
            else
            {
                RestoreForEditMode();
            }
        }

        /// <summary>
        /// Handles the transitions into and out of play mode so the preview
        /// camera comes back automatically once gameplay stops.
        /// </summary>
        /// <param name="state">Unity play-mode transition state.</param>
        private void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    DisableForRuntime();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    RestoreForEditMode();
                    break;
            }
        }

        /// <summary>
        /// Makes sure the preview camera object is active while editing so
        /// designers can frame the scene.
        /// </summary>
        private void RestoreForEditMode()
        {
            if (this == null)
            {
                return;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            var previewCamera = GetComponent<Camera>();
            if (previewCamera != null && !previewCamera.enabled)
            {
                previewCamera.enabled = true;
            }
        }

        /// <summary>
        /// Deactivates the preview camera as soon as play mode begins so the
        /// persistent runtime camera owns the scene without competition.
        /// </summary>
        private void DisableForRuntime()
        {
            if (this == null)
            {
                return;
            }

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Helper that safely drops the editor play-mode subscription if needed.
        /// </summary>
        private void UnsubscribeFromEditorEvents()
        {
            if (!isSubscribedToEditorEvents)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            isSubscribedToEditorEvents = false;
        }
#endif
    }
}
