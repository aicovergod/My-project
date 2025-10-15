using System;
using Inventory;

namespace Combat
{
    /// <summary>
    /// Provides shared helpers for classifying weapons into melee, ranged, or magic categories.
    /// Classification relies solely on curated keyword checks so ranged and magic behaviour is
    /// driven by explicit designer intent rather than implicit combat stat values.
    /// </summary>
    public static class WeaponClassificationUtility
    {
        /// <summary>
        /// Keywords used to detect magic weapons when their combat stats are inconclusive.
        /// </summary>
        private static readonly string[] MagicWeaponKeywords =
        {
            "wand",
            "staff",
            "trident",
            "sceptre"
        };

        /// <summary>
        /// Keywords used to identify ranged weapons when structured combat stats are missing.
        /// </summary>
        private static readonly string[] RangedWeaponKeywords =
        {
            "bow",
            "shortbow",
            "longbow",
            "crossbow",
            "dart",
            "javelin",
            "throwing knife",
            "knife",
            "handcannon",
            "blowpipe",
            "chinchompa",
            "thrownaxe"
        };

        /// <summary>
        /// Returns true when the provided weapon should be treated as a magic weapon.
        /// </summary>
        /// <param name="weapon">Equipped weapon data.</param>
        public static bool IsMagicWeapon(ItemData weapon)
        {
            if (weapon == null)
                return false;

            return NameContainsKeyword(weapon, MagicWeaponKeywords);
        }

        /// <summary>
        /// Returns true when the weapon behaves as a ranged weapon.
        /// </summary>
        /// <param name="weapon">Equipped weapon data.</param>
        public static bool IsRangedWeapon(ItemData weapon)
        {
            if (weapon == null)
                return false;

            return NameContainsKeyword(weapon, RangedWeaponKeywords);
        }

        /// <summary>
        /// Resolves the most appropriate <see cref="DamageType"/> for the supplied weapon.
        /// </summary>
        /// <param name="weapon">Equipped weapon data.</param>
        /// <returns>Magic, Ranged, or Melee depending on the detected classification.</returns>
        public static DamageType ResolveDamageType(ItemData weapon)
        {
            if (IsMagicWeapon(weapon))
                return DamageType.Magic;

            if (IsRangedWeapon(weapon))
                return DamageType.Ranged;

            return DamageType.Melee;
        }

        /// <summary>
        /// Check the weapon's designer-facing name (or asset name when missing) for the
        /// provided keywords, ignoring case differences.
        /// </summary>
        /// <param name="weapon">Equipped weapon data.</param>
        /// <param name="keywords">Keywords associated with the desired classification.</param>
        private static bool NameContainsKeyword(ItemData weapon, string[] keywords)
        {
            if (weapon == null || keywords == null || keywords.Length == 0)
                return false;

            string displayName = !string.IsNullOrWhiteSpace(weapon.itemName) ? weapon.itemName : weapon.name;
            if (string.IsNullOrWhiteSpace(displayName))
                return false;

            for (int i = 0; i < keywords.Length; i++)
            {
                if (displayName.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
