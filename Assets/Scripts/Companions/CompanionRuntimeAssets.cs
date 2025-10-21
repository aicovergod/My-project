using Pets;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Builds runtime ScriptableObject data used when spawning the companion so no manual inspector
    /// wiring is required. The assets are created once and cached in memory for the duration of the session.
    /// </summary>
    internal static class CompanionRuntimeAssets
    {
        /// <summary>Runtime-generated definition cached for repeated summons.</summary>
        private static PetDefinition cachedDefinition;

        /// <summary>Procedurally generated sprite so the companion is visible without manual assets.</summary>
        private static Sprite cachedSprite;

        /// <summary>Resolves the definition describing the companion's baseline visuals and combat data.</summary>
        public static PetDefinition ResolveDefinition()
        {
            if (cachedDefinition != null)
                return cachedDefinition;

            cachedDefinition = ScriptableObject.CreateInstance<PetDefinition>();
            cachedDefinition.hideFlags = HideFlags.HideAndDontSave;
            cachedDefinition.id = "Companion";
            cachedDefinition.displayName = "Companion";
            cachedDefinition.possessivePronoun = "their";
            cachedDefinition.hasInventory = false;
            cachedDefinition.canFight = true;
            cachedDefinition.petAttackLevel = 1;
            cachedDefinition.petStrengthLevel = 1;
            cachedDefinition.attackSpeedTicks = 4;
            cachedDefinition.accuracyBonus = 0;
            cachedDefinition.damageBonus = 0;
            cachedDefinition.pixelsPerUnit = 64f;
            cachedDefinition.sprite = ResolveSprite();

            return cachedDefinition;
        }

        /// <summary>Generates a simple 64×64 sprite so the companion is visible on spawn.</summary>
        private static Sprite ResolveSprite()
        {
            if (cachedSprite != null)
                return cachedSprite;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color bodyColour = new Color(0.45f, 0.75f, 1f, 1f);
            Color outlineColour = new Color(0.1f, 0.2f, 0.35f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool outline = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    texture.SetPixel(x, y, outline ? outlineColour : bodyColour);
                }
            }

            texture.Apply();
            cachedSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            return cachedSprite;
        }
    }
}
