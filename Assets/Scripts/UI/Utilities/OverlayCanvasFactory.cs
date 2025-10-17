using System;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Utilities
{
    /// <summary>
    /// Provides a shared helper for constructing overlay canvases that follow the
    /// project's default configuration (screen space overlay, pixel perfect, and
    /// scale with screen size). Centralising this logic ensures every runtime
    /// window honours the same scaling rules without duplicating boilerplate in
    /// individual UI controllers.
    /// </summary>
    public static class OverlayCanvasFactory
    {
        /// <summary>
        /// Represents the components created for an overlay canvas. The tuple-style
        /// container keeps method signatures clean while still allowing callers to
        /// capture whichever references they need to continue generating children.
        /// </summary>
        public readonly struct OverlayCanvasComponents
        {
            /// <summary>
            /// Initialises a new container storing the generated objects.
            /// </summary>
            /// <param name="root">Root GameObject that owns the canvas hierarchy.</param>
            /// <param name="canvas">Canvas component attached to <paramref name="root"/>.</param>
            /// <param name="scaler">Canvas scaler configured for screen space overlay usage.</param>
            public OverlayCanvasComponents(GameObject root, Canvas canvas, CanvasScaler scaler)
            {
                Root = root;
                Canvas = canvas;
                Scaler = scaler;
            }

            /// <summary>
            /// Gets the created GameObject that should receive all generated UI children.
            /// </summary>
            public GameObject Root { get; }

            /// <summary>
            /// Gets the configured canvas component for the overlay hierarchy.
            /// </summary>
            public Canvas Canvas { get; }

            /// <summary>
            /// Gets the canvas scaler configured for the overlay hierarchy.
            /// </summary>
            public CanvasScaler Scaler { get; }
        }

        /// <summary>
        /// Generates a configured screen-space overlay canvas using the shared UI defaults.
        /// Callers can optionally parent the hierarchy, mark it as persistent, or force a
        /// specific layer assignment (for example, the global "UI" layer).
        /// </summary>
        /// <param name="canvasName">Name assigned to the generated GameObject.</param>
        /// <param name="referenceResolution">Reference resolution applied to the scaler.</param>
        /// <param name="parent">Optional parent transform for the created root.</param>
        /// <param name="dontDestroyOnLoad">When true, marks the root as persistent across scenes.</param>
        /// <param name="pixelPerfect">When true, enables pixel perfect rendering on the canvas.</param>
        /// <param name="matchWidthOrHeight">Value forwarded to <see cref="CanvasScaler.matchWidthOrHeight"/>.</param>
        /// <param name="assignToUiLayer">When true, automatically assigns the "UI" layer.</param>
        /// <param name="explicitLayer">Optional explicit layer index to apply instead of resolving the "UI" layer.</param>
        /// <param name="renderMode">Render mode for the canvas. Defaults to <see cref="RenderMode.ScreenSpaceOverlay"/>.</param>
        /// <returns>A container describing the created root, canvas, and scaler components.</returns>
        public static OverlayCanvasComponents CreateOverlayCanvas(
            string canvasName,
            Vector2 referenceResolution,
            Transform parent = null,
            bool dontDestroyOnLoad = false,
            bool pixelPerfect = true,
            float matchWidthOrHeight = 0f,
            bool assignToUiLayer = false,
            int? explicitLayer = null,
            RenderMode renderMode = RenderMode.ScreenSpaceOverlay)
        {
            if (string.IsNullOrWhiteSpace(canvasName))
                throw new ArgumentException("Canvas name must be supplied when creating an overlay canvas.", nameof(canvasName));

            var root = new GameObject(canvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            if (parent != null)
                root.transform.SetParent(parent, false);

            if (dontDestroyOnLoad)
                UnityEngine.Object.DontDestroyOnLoad(root);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = renderMode;
            canvas.pixelPerfect = pixelPerfect;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = Mathf.Clamp01(matchWidthOrHeight);

            int layerToApply = -1;
            if (explicitLayer.HasValue)
            {
                layerToApply = explicitLayer.Value;
            }
            else if (assignToUiLayer)
            {
                layerToApply = LayerMask.NameToLayer("UI");
            }

            if (layerToApply >= 0)
                root.layer = layerToApply;

            return new OverlayCanvasComponents(root, canvas, scaler);
        }
    }
}
