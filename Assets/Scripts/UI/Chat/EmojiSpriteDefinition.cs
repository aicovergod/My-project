using UnityEngine;

namespace UI.Chat
{
    /// <summary>
    /// Metadata describing a single emoji sprite exposed by <see cref="EmojiAtlas"/>.
    /// </summary>
    public readonly struct EmojiSpriteDefinition
    {
        public EmojiSpriteDefinition(string key, Sprite sprite)
        {
            Key = key;
            Sprite = sprite;
        }

        /// <summary>Zero-padded key associated with the emoji (e.g. "01").</summary>
        public string Key { get; }

        /// <summary>Sprite instance representing the emoji graphic.</summary>
        public Sprite Sprite { get; }

        private const float DefaultTargetSize = 16f;

        /// <summary>
        /// Applies the sprite to the provided image while enforcing a square rect transform and pixel density.
        /// </summary>
        /// <param name="image">Image that should render the emoji.</param>
        /// <param name="targetSize">
        /// Desired rendered size (in pixels) for the emoji. Defaults to <see cref="DefaultTargetSize"/> so chat output
        /// stays aligned with the 16px font baseline.
        /// </param>
        public void ApplyTo(UnityEngine.UI.Image image, float targetSize = DefaultTargetSize)
        {
            if (image == null || Sprite == null)
                return;

            image.sprite = Sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;

            var rect = image.rectTransform;
            if (rect != null)
            {
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetSize);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetSize);
            }

            // Ensure the rendered size matches the requested target regardless of the sprite's native pixels-per-unit.
            if (Sprite.pixelsPerUnit > 0f)
                image.pixelsPerUnitMultiplier = targetSize / Sprite.pixelsPerUnit;
            else
                image.pixelsPerUnitMultiplier = 1f;
        }
    }
}
