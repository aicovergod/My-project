using UnityEngine;
using UnityEngine.UI;

namespace Skills.Common.UI
{
    /// <summary>
    /// Provides a reusable factory for building standardised gathering progress bars that sit in world space.
    /// </summary>
    public static class GatheringProgressBarBuilder
    {
        /// <summary>
        /// Represents the trio of components generated for a gathering progress bar.
        /// </summary>
        public readonly struct GatheringProgressBarComponents
        {
            /// <summary>
            /// Creates a new component bundle with the provided references.
            /// </summary>
            /// <param name="root">The root GameObject that houses the progress bar hierarchy.</param>
            /// <param name="canvas">The world-space canvas responsible for rendering the UI.</param>
            /// <param name="fillImage">The Image component that should be driven by progress updates.</param>
            public GatheringProgressBarComponents(GameObject root, Canvas canvas, Image fillImage)
            {
                Root = root;
                Canvas = canvas;
                FillImage = fillImage;
            }

            /// <summary>
            /// Root GameObject that contains the full bar layout.
            /// </summary>
            public GameObject Root { get; }

            /// <summary>
            /// World-space canvas that renders the bar.
            /// </summary>
            public Canvas Canvas { get; }

            /// <summary>
            /// Filled Image that callers animate to represent progress.
            /// </summary>
            public Image FillImage { get; }
        }

        private const float DefaultWorldScale = 0.01f;
        private static readonly Vector2 DefaultBackgroundSize = new Vector2(150f, 25f);
        private static readonly Color DefaultBackgroundColor = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Rect SpriteRect = new Rect(0f, 0f, 1f, 1f);
        private static readonly Vector2 SpritePivot = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// Builds a standardised gathering progress bar hierarchy beneath the provided parent.
        /// </summary>
        /// <param name="rootName">Name assigned to the root GameObject.</param>
        /// <param name="parent">Transform that should own the new hierarchy.</param>
        /// <returns>A bundle containing the root, canvas, and fill Image references.</returns>
        public static GatheringProgressBarComponents Build(string rootName, Transform parent)
        {
            return Build(rootName, parent, DefaultBackgroundSize, DefaultBackgroundColor);
        }

        /// <summary>
        /// Builds a customised gathering progress bar hierarchy beneath the provided parent.
        /// </summary>
        /// <param name="rootName">Name assigned to the root GameObject.</param>
        /// <param name="parent">Transform that should own the new hierarchy.</param>
        /// <param name="backgroundSize">Dimensions applied to the background image.</param>
        /// <param name="backgroundColor">Colour tint applied to the background image.</param>
        /// <returns>A bundle containing the root, canvas, and fill Image references.</returns>
        public static GatheringProgressBarComponents Build(string rootName, Transform parent, Vector2 backgroundSize, Color backgroundColor)
        {
            var root = new GameObject(rootName);
            if (parent != null)
                root.transform.SetParent(parent);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;

            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();
            root.transform.localScale = Vector3.one * DefaultWorldScale;

            var background = new GameObject("Background");
            background.transform.SetParent(root.transform, false);
            var backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = backgroundColor;
            var sprite = Sprite.Create(Texture2D.whiteTexture, SpriteRect, SpritePivot);
            backgroundImage.sprite = sprite;
            backgroundImage.rectTransform.sizeDelta = backgroundSize;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(background.transform, false);
            var fillImage = fill.AddComponent<Image>();
            fillImage.sprite = sprite;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;

            var fillRect = fillImage.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            root.SetActive(false);

            return new GatheringProgressBarComponents(root, canvas, fillImage);
        }
    }
}
