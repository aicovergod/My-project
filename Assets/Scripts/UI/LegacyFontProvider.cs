using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Provides cached access to the LegacyRuntime.ttf font that powers the OSRS-inspired UI skin.
    /// Falls back to Arial if Unity cannot locate the built-in legacy asset so text rendering never breaks.
    /// The helper also exposes convenience methods for wiring the font to Text and TMP components.
    /// </summary>
    public static class LegacyFontProvider
    {
        private const string LegacyFontResourceName = "LegacyRuntime.ttf";
        private const string ArialFallbackResourceName = "Arial.ttf";

        private static Font cachedLegacyFont;
        private static bool attemptedLegacyFontLoad;

        private static TMP_FontAsset cachedTmpFontAsset;
        private static bool attemptedTmpFontLoad;

        /// <summary>
        /// Retrieve the cached LegacyRuntime font or load it from Unity's built-in resources when needed.
        /// A single shared instance is reused so that UI construction scripts avoid redundant resource lookups.
        /// </summary>
        public static Font GetLegacyFont()
        {
            if (!attemptedLegacyFontLoad || cachedLegacyFont == null)
            {
                attemptedLegacyFontLoad = true;
                cachedLegacyFont = TryLoadBuiltinFont(LegacyFontResourceName) ?? TryLoadBuiltinFont(ArialFallbackResourceName);
            }

            return cachedLegacyFont;
        }

        /// <summary>
        /// Retrieve a cached TextMeshPro font asset that mirrors the legacy font. The helper lazily builds a
        /// font asset from the underlying <see cref="Font"/> and reuses it across requests. If TextMeshPro is
        /// not configured it gracefully falls back to the default TMP font asset so text continues to render.
        /// </summary>
        public static TMP_FontAsset GetLegacyTmpFontAsset()
        {
            if (!attemptedTmpFontLoad || cachedTmpFontAsset == null)
            {
                attemptedTmpFontLoad = true;

                var legacyFont = GetLegacyFont();
                if (legacyFont != null)
                {
                    cachedTmpFontAsset = TMP_FontAsset.CreateFontAsset(legacyFont);
                    if (cachedTmpFontAsset != null)
                        cachedTmpFontAsset.name = "LegacyRuntimeTMP";
                }

                if (cachedTmpFontAsset == null)
                    cachedTmpFontAsset = TMP_Settings.defaultFontAsset;
            }

            return cachedTmpFontAsset;
        }

        /// <summary>
        /// Assign the legacy font to a standard Unity <see cref="Text"/> component.
        /// </summary>
        /// <param name="uiText">Target component.</param>
        /// <param name="overwriteExistingFont">When false the assignment is skipped if the component already exposes a font.</param>
        /// <returns>True when a font was applied to the target.</returns>
        public static bool ApplyTo(Text uiText, bool overwriteExistingFont = true)
        {
            if (uiText == null)
                return false;

            if (!overwriteExistingFont && uiText.font != null)
                return false;

            uiText.font = GetLegacyFont();
            return uiText.font != null;
        }

        /// <summary>
        /// Assign the legacy font to a <see cref="TMP_Text"/> component while respecting the overwrite flag.
        /// </summary>
        /// <param name="tmpText">Target TextMeshPro component.</param>
        /// <param name="overwriteExistingFont">When false the helper will not replace an already-assigned asset.</param>
        /// <returns>True when a font asset was assigned.</returns>
        public static bool ApplyTo(TMP_Text tmpText, bool overwriteExistingFont = true)
        {
            if (tmpText == null)
                return false;

            if (!overwriteExistingFont && tmpText.font != null)
                return false;

            tmpText.font = GetLegacyTmpFontAsset();
            return tmpText.font != null;
        }

        /// <summary>
        /// Safely request a built-in font from Unity, catching the ArgumentException that is thrown when the
        /// engine cannot locate the resource. Returning null allows the caller to choose a graceful fallback.
        /// </summary>
        private static Font TryLoadBuiltinFont(string resourceName)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(resourceName);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
