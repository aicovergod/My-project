using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    ///     Utility behaviour that ensures a target sprite renderer displays the generated solo-lock sprite. This keeps
    ///     VFX and world indicators in sync with the runtime-generated texture without requiring binary art assets.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SoloLockSpriteApplier : MonoBehaviour
    {
        [Tooltip("Renderer that should display the solo-lock sprite. Defaults to the component on this GameObject.")]
        [SerializeField] private SpriteRenderer targetRenderer;

        [Tooltip("When enabled the sprite will be applied during OnEnable. Disable to only apply once during Awake.")]
        [SerializeField] private bool applyOnEnable = true;

        private void Awake()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<SpriteRenderer>();

            if (!applyOnEnable)
                ApplySprite();
        }

        private void OnEnable()
        {
            if (applyOnEnable)
                ApplySprite();
        }

        private void ApplySprite()
        {
            if (targetRenderer == null)
                return;

            var sprite = SoloLockSpriteLibrary.DefaultSprite;
            targetRenderer.sprite = sprite;
            targetRenderer.enabled = sprite != null;
        }
    }
}
