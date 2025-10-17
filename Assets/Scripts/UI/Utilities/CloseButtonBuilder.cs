using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UI.Utilities
{
    /// <summary>
    /// Centralises the creation of OSRS-style close buttons so every window can
    /// reuse the same styling and behaviour. The builder exposes layout and
    /// raycast configuration so existing panels keep their bespoke anchoring
    /// without duplicating setup code.
    /// </summary>
    public static class CloseButtonBuilder
    {
        /// <summary>
        /// Describes the layout, styling, and behaviour overrides that can be
        /// applied when generating a close button. Defaults mirror the common
        /// top-right 16×16 red button used across legacy windows.
        /// </summary>
        public sealed class Options
        {
            /// <summary>
            /// Name assigned to the instantiated button GameObject.
            /// </summary>
            public string ButtonName { get; set; } = "CloseButton";

            /// <summary>
            /// Name assigned to the generated text child GameObject.
            /// </summary>
            public string TextName { get; set; } = "Text";

            /// <summary>
            /// Anchor minimum applied to the button's <see cref="RectTransform"/>.
            /// </summary>
            public Vector2 AnchorMin { get; set; } = new Vector2(1f, 1f);

            /// <summary>
            /// Anchor maximum applied to the button's <see cref="RectTransform"/>.
            /// </summary>
            public Vector2 AnchorMax { get; set; } = new Vector2(1f, 1f);

            /// <summary>
            /// Pivot used when positioning the button.
            /// </summary>
            public Vector2 Pivot { get; set; } = new Vector2(1f, 1f);

            /// <summary>
            /// Anchored position offset applied after anchoring.
            /// </summary>
            public Vector2 AnchoredPosition { get; set; } = new Vector2(-4f, -4f);

            /// <summary>
            /// Optional offsets applied when the layout relies on Stretch anchors.
            /// Leave null to keep Unity's defaults.
            /// </summary>
            public Vector2? OffsetMin { get; set; } = null;

            /// <summary>
            /// Optional offsets applied when the layout relies on Stretch anchors.
            /// Leave null to keep Unity's defaults.
            /// </summary>
            public Vector2? OffsetMax { get; set; } = null;

            /// <summary>
            /// Size delta assigned to the button.
            /// </summary>
            public Vector2 Size { get; set; } = new Vector2(16f, 16f);

            /// <summary>
            /// Whether the image component should block raycasts. Inventory closes
            /// rely on this while tooltip overlays need raycasts disabled.
            /// </summary>
            public bool ImageRaycastTarget { get; set; } = true;

            /// <summary>
            /// Whether the text graphic should block raycasts. Some windows expect
            /// this to be disabled so drags continue to register.
            /// </summary>
            public bool TextRaycastTarget { get; set; } = true;

            /// <summary>
            /// Custom font used for the button label. When omitted the builder
            /// automatically falls back to <see cref="LegacyFontProvider"/>.
            /// </summary>
            public Font Font { get; set; } = null;

            /// <summary>
            /// Text rendered on the button. Defaults to the classic "X".
            /// </summary>
            public string Text { get; set; } = "X";

            /// <summary>
            /// Colour assigned to the button background.
            /// </summary>
            public Color BackgroundColor { get; set; } = Color.red;

            /// <summary>
            /// Colour assigned to the button text.
            /// </summary>
            public Color TextColor { get; set; } = Color.white;
        }

        /// <summary>
        /// Build a close button under <paramref name="parent"/> and wire
        /// <paramref name="onClick"/> as the button's callback.
        /// </summary>
        /// <param name="parent">Transform that will contain the generated button.</param>
        /// <param name="onClick">Action invoked when the button is pressed.</param>
        /// <param name="options">Optional layout overrides.</param>
        /// <returns>The configured <see cref="Button"/> component.</returns>
        public static Button Build(Transform parent, UnityAction onClick, Options options = null)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (onClick == null)
                throw new ArgumentNullException(nameof(onClick));

            options ??= new Options();

            var closeButtonGO = new GameObject(options.ButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
            closeButtonGO.transform.SetParent(parent, false);

            var rect = closeButtonGO.GetComponent<RectTransform>();
            rect.anchorMin = options.AnchorMin;
            rect.anchorMax = options.AnchorMax;
            rect.pivot = options.Pivot;
            rect.anchoredPosition = options.AnchoredPosition;
            rect.sizeDelta = options.Size;

            if (options.OffsetMin.HasValue)
                rect.offsetMin = options.OffsetMin.Value;
            if (options.OffsetMax.HasValue)
                rect.offsetMax = options.OffsetMax.Value;

            var image = closeButtonGO.GetComponent<Image>();
            image.color = options.BackgroundColor;
            image.raycastTarget = options.ImageRaycastTarget;

            var button = closeButtonGO.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            var textGO = new GameObject(options.TextName, typeof(Text));
            textGO.transform.SetParent(closeButtonGO.transform, false);

            var text = textGO.GetComponent<Text>();
            text.font = options.Font != null ? options.Font : LegacyFontProvider.GetLegacyFont();
            text.text = options.Text;
            text.color = options.TextColor;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = options.TextRaycastTarget;

            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            textRect.pivot = new Vector2(0.5f, 0.5f);

            return button;
        }
    }
}
