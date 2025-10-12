using UnityEngine;

namespace UI.Utilities
{
    /// <summary>
    /// Base class for UI windows managed by <see cref="UIManager"/>. Handles standard open/close
    /// requests, enforces modal rules through <see cref="UIManager.TryOpenWindow"/>, and exposes
    /// overridable hooks so derived classes can inject custom behaviour during the open/close cycle.
    /// </summary>
    public abstract class ManagedUiWindow : MonoBehaviour, UI.IUIWindow
    {
        [Header("Managed Window")]
        [SerializeField]
        [Tooltip("Root object toggled when the window is opened or closed.")]
        private GameObject windowRoot;

        /// <summary>
        /// Cached state used by <see cref="Open"/> to know whether hooks should fire even when the
        /// window is already active. Derived classes may flip this flag when a refresh is required
        /// despite the window being visible.
        /// </summary>
        private bool forceNextOpenHooks;

        /// <summary>
        /// Returns <c>true</c> when the window should be considered visible.
        /// </summary>
        public bool IsOpen => EvaluateIsOpen();

        /// <summary>
        /// Requests that the window toggles its visibility. When the window is open it will be
        /// closed, otherwise it attempts to open while respecting modal rules.
        /// </summary>
        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        /// <summary>
        /// Attempts to open the window. The request is routed through <see cref="UIManager"/> so
        /// modal interfaces (bank/shop) can reject conflicting windows. When successful the
        /// standard open hooks fire and the configured root becomes active.
        /// </summary>
        public virtual void Open()
        {
            if (!forceNextOpenHooks && IsOpen)
                return;

            forceNextOpenHooks = false;

            if (!CanOpen())
                return;

            var manager = UI.UIManager.Instance;
            if (manager != null && !manager.TryOpenWindow(this))
                return;

            OnBeforeOpen();
            SetWindowActive(true);
            OnAfterOpen();
        }

        /// <summary>
        /// Closes the window and invokes the associated hooks. When the window is already closed
        /// the call is ignored.
        /// </summary>
        public virtual void Close()
        {
            if (!IsOpen)
                return;

            OnBeforeClose();
            SetWindowActive(false);
            OnAfterClose();
        }

        /// <summary>
        /// Registers the window with <see cref="UIManager"/> so it participates in global gating.
        /// </summary>
        protected void RegisterWindow()
        {
            var manager = UI.UIManager.Instance;
            manager?.RegisterWindow(this);
        }

        /// <summary>
        /// Unregisters the window from <see cref="UIManager"/>.
        /// </summary>
        protected void UnregisterWindow()
        {
            var manager = UI.UIManager.Instance;
            manager?.UnregisterWindow(this);
        }

        /// <summary>
        /// Assigns the GameObject that should be toggled when the window opens or closes. The root
        /// is optionally hidden immediately to ensure windows start in a closed state.
        /// </summary>
        /// <param name="root">GameObject representing the window's active hierarchy.</param>
        /// <param name="deactivateOnAssign">
        /// If <c>true</c> the root is deactivated immediately to keep the window closed until an
        /// explicit open request arrives.
        /// </param>
        protected void SetWindowRoot(GameObject root, bool deactivateOnAssign = true)
        {
            windowRoot = root;
            if (deactivateOnAssign && windowRoot != null)
                SetWindowActive(false);
        }

        /// <summary>
        /// Forces the next call to <see cref="Open"/> to run the hook chain even if the window is
        /// already visible. This allows derived classes to trigger refreshes without closing the
        /// window first.
        /// </summary>
        protected void ForceNextOpenHooks()
        {
            forceNextOpenHooks = true;
        }

        /// <summary>
        /// Determines whether the window is allowed to open. Derived classes can override this to
        /// implement custom validation (e.g. required state, cooldowns, or resource checks).
        /// </summary>
        protected virtual bool CanOpen() => true;

        /// <summary>
        /// Evaluates the visibility state. Derived classes may override this when the window relies
        /// on behaviour other than <see cref="GameObject.activeSelf"/> (for example when only the
        /// <see cref="Canvas.enabled"/> flag changes).
        /// </summary>
        protected virtual bool EvaluateIsOpen()
        {
            return windowRoot != null && windowRoot.activeSelf;
        }

        /// <summary>
        /// Activates or deactivates the managed root. Derived classes can override to swap the
        /// behaviour (e.g. toggling a <see cref="Canvas"/> instead of the GameObject itself).
        /// </summary>
        protected virtual void SetWindowActive(bool active)
        {
            if (windowRoot != null)
                windowRoot.SetActive(active);
        }

        /// <summary>Invoked immediately before the window is shown.</summary>
        protected virtual void OnBeforeOpen()
        {
        }

        /// <summary>Invoked immediately after the window becomes visible.</summary>
        protected virtual void OnAfterOpen()
        {
        }

        /// <summary>Invoked immediately before the window is hidden.</summary>
        protected virtual void OnBeforeClose()
        {
        }

        /// <summary>Invoked immediately after the window is hidden.</summary>
        protected virtual void OnAfterClose()
        {
        }

        /// <summary>
        /// Provides read-only access to the configured window root for derived classes.
        /// </summary>
        protected GameObject WindowRoot => windowRoot;
    }
}
