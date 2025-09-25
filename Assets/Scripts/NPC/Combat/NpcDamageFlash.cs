using System.Collections;
using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Provides a simple sprite flash effect when an NPC takes damage.
    /// The flash colour/duration can be tuned in the inspector and will
    /// automatically scale based on the portion of max health removed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public class NpcDamageFlash : MonoBehaviour
    {
        [Header("Flash Settings")]
        [SerializeField, Tooltip("Tint applied to the sprite while the damage flash is playing.")]
        private Color flashColor = Color.white;

        [SerializeField, Tooltip("Duration of the flash animation in seconds.")]
        private float flashDuration = 0.1f;

        [SerializeField, Tooltip("Progression curve for blending from the flash tint back to the original colour.")]
        private AnimationCurve flashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Intensity Scaling")]
        [SerializeField, Tooltip("When enabled the flash intensity scales with the fraction of health removed by the hit.")]
        private bool scaleWithDamageFraction = true;

        [SerializeField, Tooltip("Curve translating the damage fraction (0-1) into a flash intensity multiplier.")]
        private AnimationCurve damageToFlashIntensity = new AnimationCurve(
            new Keyframe(0f, 0.35f, 0f, 1f),
            new Keyframe(1f, 1f, 0f, 0f));

        private SpriteRenderer spriteRenderer;
        private Color restingColor;
        private Coroutine flashRoutine;

        /// <summary>
        /// Cache the sprite renderer and original tint for restoration.
        /// </summary>
        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            restingColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
        }

        /// <summary>
        /// Reset the sprite colour when the component is disabled to avoid
        /// leaving the sprite tinted when the NPC despawns or is disabled.
        /// </summary>
        private void OnDisable()
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                flashRoutine = null;
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.color = restingColor;
            }
        }

        /// <summary>
        /// Trigger the flash effect for the supplied damage amount.
        /// </summary>
        /// <param name="damageAmount">Amount of damage dealt to the NPC.</param>
        /// <param name="maxHealth">The NPC's maximum health for scaling intensity.</param>
        public void TriggerFlash(int damageAmount, int maxHealth)
        {
            if (!isActiveAndEnabled || spriteRenderer == null)
                return;

            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                spriteRenderer.color = restingColor;
                flashRoutine = null;
            }

            restingColor = spriteRenderer.color;

            float intensity = 1f;
            if (scaleWithDamageFraction && maxHealth > 0)
            {
                float fraction = Mathf.Clamp01(damageAmount / (float)maxHealth);
                intensity = Mathf.Clamp01(damageToFlashIntensity.Evaluate(fraction));
            }

            flashRoutine = StartCoroutine(PlayFlash(intensity));
        }

        /// <summary>
        /// Runs the flash animation using the configured curves and settings.
        /// </summary>
        private IEnumerator PlayFlash(float intensity)
        {
            float duration = Mathf.Max(0.01f, flashDuration);
            float elapsed = 0f;
            Color targetColor = Color.Lerp(restingColor, flashColor, intensity);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float curveValue = Mathf.Clamp01(flashCurve.Evaluate(t));
                spriteRenderer.color = Color.Lerp(restingColor, targetColor, curveValue);
                yield return null;
            }

            spriteRenderer.color = restingColor;
            flashRoutine = null;
        }
    }
}
