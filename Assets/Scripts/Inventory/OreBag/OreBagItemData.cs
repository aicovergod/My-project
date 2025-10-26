// Assets/Scripts/Inventory/OreBag/OreBagItemData.cs
using Inventory;
using UnityEngine;

namespace Inventory.OreBag
{
    /// <summary>
    /// ScriptableObject describing an ore bag tier. Tiers dictate combined capacity,
    /// upgrade costs, and optionally reference the next tier asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Inventory/Ore Bag Item")]
    public sealed class OreBagItemData : ItemData
    {
        [SerializeField]
        [Range(1, 6)]
        [Tooltip("Tier of the ore bag. Higher tiers raise combined capacity and upgrade cost.")]
        private int tier = 1;

        [SerializeField]
        [Tooltip("Optional capacity override. Leave zero to use the standard value for this tier.")]
        private int customCapacity;

        [SerializeField]
        [Tooltip("Reference to the next tier ore bag. Leave empty for tier six.")]
        private OreBagItemData upgradeTarget;

        /// <summary>Gets the configured tier clamped to the supported 1-6 range.</summary>
        public int Tier => Mathf.Clamp(tier, 1, 6);

        /// <summary>
        /// Returns the maximum combined ore count the bag can hold. Defaults follow the design spec.
        /// </summary>
        public int OreCapacity
        {
            get
            {
                if (customCapacity > 0)
                    return customCapacity;

                return Tier switch
                {
                    1 => 25,
                    2 => 50,
                    3 => 75,
                    4 => 100,
                    5 => 150,
                    6 => 250,
                    _ => 25
                };
            }
        }

        /// <summary>
        /// Number of Hades fragments required to upgrade this bag. Tier six returns zero.
        /// </summary>
        public int UpgradeCost => Tier switch
        {
            1 => 50,
            2 => 100,
            3 => 200,
            4 => 400,
            5 => 750,
            _ => 0
        };

        /// <summary>Next tier bag asset. Null indicates the bag is already max tier.</summary>
        public OreBagItemData UpgradeTarget => upgradeTarget;
    }
}
