using System.Collections;
using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Applies a material-driven flash overlay to an NPC's sprite whenever they take damage.
    /// The effect is fully data-driven so designers can author shader parameters that pulse,
    /// tint, or overlay additional visuals without mutating the shared material asset.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public class NpcFlashEffect : MonoBehaviour
    {
        [Header("Flash Material")]
        [SerializeField, Tooltip("Material used to render the flash overlay. A runtime clone is created per NPC so shared assets remain untouched.")]
        private Material flashMaterial;

        [SerializeField, Tooltip("Shader property that controls the flash blend amount (commonly _FlashAmount).")]
        private string flashAmountProperty = "_FlashAmount";

        [SerializeField, Tooltip("Shader property that controls the flash tint colour (commonly _FlashColor).")]
        private string flashColorProperty = "_FlashColor";

        [Header("Flash Behaviour")]
        [SerializeField, Tooltip("Colour applied while the flash plays before being tinted by the current intensity.")]
        private Color flashColor = new Color(1f, 0.35f, 0.35f, 1f);

        [SerializeField, Tooltip("Duration of the flash animation in seconds.")]
        private float flashDuration = 0.1f;

        [SerializeField, Tooltip("Curve describing the flash blend progression over time (evaluated 0-1).")]
        private AnimationCurve flashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Damage Scaling")]
        [SerializeField, Tooltip("When enabled, the flash intensity scales with the fraction of maximum health removed by the hit.")]
        private bool scaleWithDamageFraction = true;

        [SerializeField, Tooltip("Maps the damage fraction (0-1) to a flash intensity multiplier.")]
        private AnimationCurve damageToFlashIntensity = new AnimationCurve(
            new Keyframe(0f, 0.35f, 0f, 1f),
            new Keyframe(1f, 1f, 0f, 0f));

        [SerializeField, Range(0f, 5f), Tooltip("Lowest flash intensity allowed so light hits still provide feedback.")]
        private float minimumFlashIntensity = 0.7f;

        [Header("Timing")]
        [SerializeField, Tooltip("If enabled the flash uses unscaled time, allowing it to animate while the game is paused or slowed.")]
        private bool useUnscaledTime = false;

        private SpriteRenderer spriteRenderer;
        private Material runtimeFlashMaterial;
        private Material originalSharedMaterial;
        private MaterialPropertyBlock propertyBlock;
        private Coroutine flashRoutine;

        private int flashAmountPropertyId = -1;
        private int flashColorPropertyId = -1;

        private bool hasLoggedMissingMaterial;

        /// <summary>
        /// Cache component references, clone the flash material, and build shader property IDs for cheap access.
        /// </summary>
        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            originalSharedMaterial = spriteRenderer != null ? spriteRenderer.sharedMaterial : null;
            propertyBlock = new MaterialPropertyBlock();

            CacheShaderPropertyIds();
            InitialiseRuntimeMaterial();
        }

        /// <summary>
        /// Reset the renderer's property block whenever the component becomes active so no stale values persist.
        /// </summary>
        private void OnEnable()
        {
            ApplyBaselinePropertyBlock();
        }

        /// <summary>
        /// Stop running routines and restore the renderer state when disabled to avoid leaking modified materials.
        /// </summary>
        private void OnDisable()
        {
            ResetFlashInstantly();
            ReleaseRuntimeMaterial(); // drop the runtime clone so despawned NPCs do not leak materials
        }

        /// <summary>
        /// Ensure runtime resources are fully cleaned up on destruction.
        /// </summary>
        private void OnDestroy()
        {
            ResetFlashInstantly();
            ReleaseRuntimeMaterial();
        }

        /// <summary>
        /// Public API used by combat scripts to trigger the flash animation with damage scaling support.
        /// </summary>
        /// <param name="damageAmount">Amount of damage dealt.</param>
        /// <param name="maxHealth">Maximum health of the NPC for fraction-based scaling.</param>
        public void TriggerFlash(int damageAmount, int maxHealth)
        {
            if (!isActiveAndEnabled || spriteRenderer == null)
                return;

            if (!EnsureRuntimeMaterial())
                return;

            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                flashRoutine = null;
                ApplyBaselinePropertyBlock(); // ensure we start from a clean property block when retriggering mid-flash
            }

            float intensity = 1f;
            if (scaleWithDamageFraction && maxHealth > 0)
            {
                float fraction = Mathf.Clamp01(damageAmount / (float)maxHealth);
                if (damageToFlashIntensity != null)
                {
                    intensity = damageToFlashIntensity.Evaluate(fraction);
                }
                else
                {
                    intensity = fraction;
                }
            }

            intensity = Mathf.Max(minimumFlashIntensity, intensity); // clamp to a baseline intensity so glancing blows still flash

            flashRoutine = StartCoroutine(AnimateFlash(intensity));
        }

        /// <summary>
        /// Immediately stops any running flash animation and returns the renderer to its original material state.
        /// </summary>
        public void ResetFlashInstantly()
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                flashRoutine = null;
            }

            if (spriteRenderer == null)
                return;

            spriteRenderer.sharedMaterial = originalSharedMaterial;
            ApplyBaselinePropertyBlock();
        }

        /// <summary>
        /// Animate the flash over time by updating the property block using the supplied intensity multiplier.
        /// </summary>
        private IEnumerator AnimateFlash(float intensity)
        {
            float duration = Mathf.Max(0.01f, flashDuration);
            float elapsed = 0f;

            if (runtimeFlashMaterial != null && spriteRenderer.sharedMaterial != runtimeFlashMaterial)
            {
                // Swap in the runtime clone so we can drive shader keywords without mutating shared assets.
                spriteRenderer.sharedMaterial = runtimeFlashMaterial;
            }

            while (elapsed < duration)
            {
                float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                elapsed += delta;
                float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                float curveValue = flashCurve != null ? flashCurve.Evaluate(t) : t;
                float flashAmount = Mathf.Clamp01(Mathf.Clamp01(curveValue) * intensity);

                UpdatePropertyBlock(flashAmount, intensity);

                yield return null;
            }

            ResetFlashInstantly();
        }

        /// <summary>
        /// Cache shader property IDs derived from the configured property names.
        /// </summary>
        private void CacheShaderPropertyIds()
        {
            flashAmountPropertyId = !string.IsNullOrWhiteSpace(flashAmountProperty) ? Shader.PropertyToID(flashAmountProperty) : -1;
            flashColorPropertyId = !string.IsNullOrWhiteSpace(flashColorProperty) ? Shader.PropertyToID(flashColorProperty) : -1;
        }

        /// <summary>
        /// Ensure a runtime clone of the flash material exists so each NPC can animate without mutating shared data.
        /// </summary>
        private void InitialiseRuntimeMaterial()
        {
            ReleaseRuntimeMaterial();

            if (flashMaterial == null)
                return;

            runtimeFlashMaterial = new Material(flashMaterial)
            {
                name = $"{flashMaterial.name} (Runtime NPC Flash)"
            };
        }

        /// <summary>
        /// Creates a runtime material clone if required and logs a warning exactly once when missing.
        /// </summary>
        private bool EnsureRuntimeMaterial()
        {
            if (runtimeFlashMaterial == null)
            {
                if (flashMaterial == null)
                {
                    if (!hasLoggedMissingMaterial)
                    {
                        Debug.LogWarning($"NpcFlashEffect on {name} has no flash material assigned, flash skipped.", this);
                        hasLoggedMissingMaterial = true;
                    }

                    return false;
                }

                InitialiseRuntimeMaterial();
            }

            return runtimeFlashMaterial != null;
        }

        /// <summary>
        /// Apply a zeroed property block to clear any flash visuals and write the baseline state to the renderer.
        /// </summary>
        private void ApplyBaselinePropertyBlock()
        {
            if (spriteRenderer == null || propertyBlock == null)
                return;

            propertyBlock.Clear();

            if (flashAmountPropertyId != -1)
            {
                propertyBlock.SetFloat(flashAmountPropertyId, 0f);
            }

            if (flashColorPropertyId != -1)
            {
                propertyBlock.SetColor(flashColorPropertyId, Color.clear);
            }

            spriteRenderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>
        /// Writes the active flash amount and colour to the property block before applying it to the renderer.
        /// </summary>
        private void UpdatePropertyBlock(float flashAmount, float intensity)
        {
            if (spriteRenderer == null || propertyBlock == null)
                return;

            propertyBlock.Clear();

            if (flashAmountPropertyId != -1)
            {
                propertyBlock.SetFloat(flashAmountPropertyId, flashAmount);
            }

            if (flashColorPropertyId != -1)
            {
                Color tintedColor = flashColor * intensity;
                tintedColor.a = flashColor.a * Mathf.Clamp01(intensity);
                propertyBlock.SetColor(flashColorPropertyId, tintedColor);
            }

            spriteRenderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>
        /// Release the runtime material to prevent leaks when the component is destroyed or reinitialised.
        /// </summary>
        private void ReleaseRuntimeMaterial()
        {
            if (runtimeFlashMaterial == null)
                return;

            if (Application.isPlaying)
            {
                Destroy(runtimeFlashMaterial);
            }
            else
            {
                DestroyImmediate(runtimeFlashMaterial);
            }
            runtimeFlashMaterial = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheShaderPropertyIds();

            if (Application.isPlaying)
                return;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            ApplyBaselinePropertyBlock();
        }
#endif
    }
}

