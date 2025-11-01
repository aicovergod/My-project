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
            /// Optional sprite assigned to the button background image. When
            /// omitted the builder falls back to Unity's built-in UI sprite so
            /// the entire red square can receive raycasts rather than relying
            /// solely on the text glyph.
            /// </summary>
            public Sprite BackgroundSprite { get; set; } = null;

            /// <summary>
            /// Image type applied to the background when a sprite is present.
            /// Defaults to <see cref="Image.Type.Sliced"/> to match the
            /// standard UI sprite behaviour while allowing callers to override
            /// when they need a simple quad.
            /// </summary>
            public Image.Type BackgroundImageType { get; set; } = Image.Type.Sliced;

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
            var backgroundSprite = ResolveBackgroundSprite(options);
            image.sprite = backgroundSprite;
            if (backgroundSprite != null)
            {
                image.type = options.BackgroundImageType;
            }
            image.color = options.BackgroundColor;
            image.raycastTarget = options.ImageRaycastTarget;

            var button = closeButtonGO.GetComponent<Button>();
            button.targetGraphic = image;
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

        /// <summary>
        /// Attempts to resolve the sprite assigned to the close button background, falling
        /// back to a procedural sprite when Unity's built-in UI sprite cannot be located.
        /// </summary>
        /// <param name="options">Caller supplied options that may contain a custom sprite.</param>
        /// <returns>A sprite that can be safely assigned to the close button background.</returns>
        private static Sprite ResolveBackgroundSprite(Options options)
        {
            if (options.BackgroundSprite != null)
                return options.BackgroundSprite;

            Sprite builtinSprite = null;
            try
            {
                builtinSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            }
            catch (Exception exception)
            {
                LogBuiltinLookupFailure(exception);
            }

            if (builtinSprite != null)
                return builtinSprite;

            LogBuiltinLookupFailure();
            return ProceduralSpriteCache.GetFallbackSprite();
        }

        /// <summary>
        /// Emits a single warning when the built-in sprite lookup fails so designers know the
        /// project is relying on a procedural fallback sprite.
        /// </summary>
        /// <param name="exception">Optional exception raised by the resource lookup.</param>
        private static void LogBuiltinLookupFailure(Exception exception = null)
        {
            if (ProceduralSpriteCache.HasLoggedLookupFailure)
                return;

            ProceduralSpriteCache.HasLoggedLookupFailure = true;
            if (exception != null)
            {
                Debug.LogWarning($"CloseButtonBuilder: Failed to load Unity's built-in UISprite. Falling back to a procedural sprite. Exception: {exception}");
            }
            else
            {
                Debug.LogWarning("CloseButtonBuilder: Built-in UISprite unavailable. Falling back to a procedural sprite.");
            }
        }

        /// <summary>
        ///     Local cache that stores the procedural fallback sprite. The cache ensures we do not
        ///     allocate a new sprite every time a close button is instantiated while still letting
        ///     the image tint honour the caller's requested background colour.
        /// </summary>
        private static class ProceduralSpriteCache
        {
            internal static bool HasLoggedLookupFailure { get; set; }

            /// <summary>
            /// Returns a cached sprite backed by <see cref="Texture2D.whiteTexture"/>. The caller's
            /// desired colour is respected via the <see cref="Image.color"/> tint that is already
            /// applied when the button is configured, mirroring how the built-in UI sprite is used.
            /// </summary>
            internal static Sprite GetFallbackSprite()
            {
                if (cachedSprite == null)
                {
                    var sourceTexture = Texture2D.whiteTexture;
                    cachedSprite = Sprite.Create(
                        sourceTexture,
                        new Rect(0f, 0f, sourceTexture.width, sourceTexture.height),
                        new Vector2(0.5f, 0.5f),
                        64f);
                    cachedSprite.name = "CloseButtonBuilder_FallbackSprite";
                    cachedSprite.hideFlags = HideFlags.HideAndDontSave;
                }

                return cachedSprite;
            }

            private static Sprite cachedSprite;
        }
    }
}
