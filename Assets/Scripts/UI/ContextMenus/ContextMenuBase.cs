using Core.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UI.ContextMenus
{
    /// <summary>
    /// Base behaviour for OSRS-style context menus that automatically handles safe padding, pointer
    /// lookups, and close-on-click behaviours so individual menus can focus on rendering their
    /// bespoke options.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class ContextMenuBase : MonoBehaviour
    {
        [Header("Context Menu Base")]
        [SerializeField]
        [Tooltip("Additional pixel padding around the menu that still counts as hovering the menu.")]
        private float safePaddingPixels = 12f;

        [SerializeField]
        [Tooltip("Automatically close the menu when the pointer clicks outside of the strict rectangle.")]
        private bool closeOnPointerClick = true;

        private RectTransform menuRect;
        private Canvas cachedCanvas;
        private Camera cachedCanvasCamera;
        private bool deferSafeZoneEvaluation;

        /// <summary>
        /// Gets or sets the pixel padding that allows small pointer movements without closing the menu.
        /// </summary>
        public float SafePaddingPixels
        {
            get => safePaddingPixels;
            set => safePaddingPixels = Mathf.Max(0f, value);
        }

        /// <summary>Cached <see cref="Canvas"/> controlling camera selection for hit tests.</summary>
        protected Canvas MenuCanvas
        {
            get
            {
                if (cachedCanvas == null)
                {
                    AssignCanvas(GetComponentInParent<Canvas>());
                }

                return cachedCanvas;
            }
        }

        /// <summary>Camera used for pointer hit testing when the canvas is not overlay rendered.</summary>
        protected Camera MenuCanvasCamera => cachedCanvasCamera;

        /// <summary>Root rectangle used for pointer containment checks.</summary>
        protected RectTransform MenuRectTransform
        {
            get
            {
                if (menuRect == null)
                {
                    menuRect = GetComponent<RectTransform>();
                }

                return menuRect;
            }
        }

        /// <summary>Ensures the supplied menu rectangle is used for hover checks.</summary>
        protected void SetMenuRectTransform(RectTransform rectTransform)
        {
            menuRect = rectTransform;
        }

        /// <summary>Caches the canvas reference and resolves the correct camera for hit tests.</summary>
        protected void AssignCanvas(Canvas canvas)
        {
            cachedCanvas = canvas;
            if (cachedCanvas != null && cachedCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cachedCanvasCamera = cachedCanvas.worldCamera;
            }
            else
            {
                cachedCanvasCamera = null;
            }
        }

        /// <summary>Defers the safe-zone evaluation until the next frame.</summary>
        protected void DeferSafeZoneCheck()
        {
            deferSafeZoneEvaluation = true;
        }

        /// <summary>Returns whether the menu is currently visible and should process pointer checks.</summary>
        protected virtual bool IsMenuVisible()
        {
            return gameObject.activeSelf;
        }

        /// <summary>Unity lifecycle hook used to refresh cached components.</summary>
        protected virtual void Awake()
        {
            _ = MenuRectTransform;
            _ = MenuCanvas;
        }

        /// <summary>Unity lifecycle hook used to defer the initial safe-zone check.</summary>
        protected virtual void OnEnable()
        {
            DeferSafeZoneCheck();
        }

        /// <summary>Unity lifecycle hook used to re-evaluate canvas assignments when parenting changes.</summary>
        protected virtual void OnTransformParentChanged()
        {
            AssignCanvas(GetComponentInParent<Canvas>());
        }

        /// <summary>Per-frame pointer containment evaluation shared across all context menus.</summary>
        protected virtual void Update()
        {
            if (!isActiveAndEnabled)
                return;

            if (!IsMenuVisible())
                return;

            var targetRect = MenuRectTransform;
            if (targetRect == null)
                return;

            if (deferSafeZoneEvaluation)
            {
                deferSafeZoneEvaluation = false;
                return;
            }

            Vector2 pointerPosition = InputActionResolver.GetPointerScreenPosition(Input.mousePosition);
            bool insideStrict = RectTransformUtility.RectangleContainsScreenPoint(targetRect, pointerPosition, MenuCanvasCamera);
            bool insideSafeZone = insideStrict;

            if (safePaddingPixels > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(targetRect, pointerPosition, MenuCanvasCamera, out Vector2 localPoint))
            {
                Rect paddedRect = targetRect.rect;
                paddedRect.xMin -= safePaddingPixels;
                paddedRect.xMax += safePaddingPixels;
                paddedRect.yMin -= safePaddingPixels;
                paddedRect.yMax += safePaddingPixels;
                insideSafeZone = paddedRect.Contains(localPoint);
            }

            if (!insideSafeZone)
            {
                RequestClose();
                return;
            }

            if (closeOnPointerClick && !insideStrict && DetectPointerPressedThisFrame())
            {
                RequestClose();
            }
        }

        /// <summary>Detects pointer clicks using the Input System with a legacy input fallback.</summary>
        private bool DetectPointerPressedThisFrame()
        {
            bool pointerPressed = false;

            if (Mouse.current != null)
            {
                pointerPressed |= Mouse.current.leftButton.wasPressedThisFrame;
                pointerPressed |= Mouse.current.rightButton.wasPressedThisFrame;
                pointerPressed |= Mouse.current.middleButton.wasPressedThisFrame;
            }

            if (!pointerPressed && Pen.current != null)
            {
                pointerPressed |= Pen.current.tip.wasPressedThisFrame;
            }

            if (!pointerPressed && Touchscreen.current != null)
            {
                pointerPressed |= Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
            }

            if (!pointerPressed)
            {
                pointerPressed = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            }

            return pointerPressed;
        }

        /// <summary>Invokes the derived close handling logic when the base determines the menu should hide.</summary>
        private void RequestClose()
        {
            if (!IsMenuVisible())
                return;

            OnCloseRequested();
        }

        /// <summary>Derived classes implement the actual hide behaviour and any cleanup.</summary>
        protected abstract void OnCloseRequested();
    }
}
