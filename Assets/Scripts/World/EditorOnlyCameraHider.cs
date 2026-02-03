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
        [SerializeField]
        [Tooltip("Optional explicit reference to the preview camera that should be hidden while the game runs.")]
        private Camera previewCamera;

        [SerializeField]
        [Tooltip("Optional explicit reference to the preview audio listener that should be hidden while the game runs.")]
        private AudioListener previewAudioListener;

        private bool isSubscribedToEditorEvents;
        private bool hasRuntimeSuppressionBeenApplied;
        private bool cameraEnabledBeforeRuntime = true;
        private bool audioListenerEnabledBeforeRuntime = true;

        /// <summary>
        /// Subscribe to play-mode events and make sure the preview camera is
        /// visible while we are in edit mode.
        /// </summary>
        private void OnEnable()
        {
            CachePreviewComponents();

            if (!isSubscribedToEditorEvents)
            {
                EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
                isSubscribedToEditorEvents = true;
            }

            if (!Application.isPlaying && !EditorApplication.isPlaying)
            {
                RestoreForEditMode();
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
                if (!hasRuntimeSuppressionBeenApplied)
                {
                    DisableForRuntime();
                }
            }
            else
            {
                if (hasRuntimeSuppressionBeenApplied)
                {
                    RestoreForEditMode();
                }
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
        /// Restores the preview camera (and matching audio listener) for edit mode
        /// while keeping the helper object active and visible in the hierarchy.
        /// </summary>
        private void RestoreForEditMode()
        {
            if (this == null)
            {
                return;
            }

            CachePreviewComponents();

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            gameObject.hideFlags = HideFlags.None;

            if (previewCamera != null)
            {
                previewCamera.enabled = true;
            }

            cameraEnabledBeforeRuntime = true;

            if (previewAudioListener != null)
            {
                previewAudioListener.enabled = true;
            }

            audioListenerEnabledBeforeRuntime = true;

            hasRuntimeSuppressionBeenApplied = false;
        }

        /// <summary>
        /// Disables the preview camera and audio listener during play mode so the
        /// runtime camera/audio stack can drive the scene without interference.
        /// </summary>
        private void DisableForRuntime()
        {
            if (this == null)
            {
                return;
            }

            if (hasRuntimeSuppressionBeenApplied)
            {
                return;
            }

            CachePreviewComponents();

            if (previewCamera != null)
            {
                cameraEnabledBeforeRuntime = previewCamera.enabled;
                if (previewCamera.enabled)
                {
                    previewCamera.enabled = false;
                }
            }

            if (previewAudioListener != null)
            {
                audioListenerEnabledBeforeRuntime = previewAudioListener.enabled;
                if (previewAudioListener.enabled)
                {
                    previewAudioListener.enabled = false;
                }
            }

            gameObject.hideFlags |= HideFlags.HideInHierarchy;
            hasRuntimeSuppressionBeenApplied = true;
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

        /// <summary>
        /// Lazily resolves the preview camera and audio listener references so the
        /// helper does not call <see cref="GetComponent{T}()"/> every frame.
        /// </summary>
        private void CachePreviewComponents()
        {
            if (previewCamera == null)
            {
                TryGetComponent(out previewCamera);
            }

            if (previewAudioListener == null)
            {
                TryGetComponent(out previewAudioListener);
            }
        }
#endif
    }
}
