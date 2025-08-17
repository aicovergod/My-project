using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Inventory;

namespace Skills.Mining
{
    /// <summary>
    /// Chooses the best pickaxe available in the inventory that the player can use.
    /// </summary>
    [DisallowMultipleComponent]
    public class PickaxeToUse : MonoBehaviour
    {
        [SerializeField] private List<PickaxeDefinition> allPickaxes = new List<PickaxeDefinition>();
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private MiningSkill skill;

        public PickaxeDefinition Current { get; private set; }

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (skill == null)
                skill = GetComponent<MiningSkill>();
        }

        /// <summary>
        /// Returns the best usable pickaxe. Refreshes the current pickaxe cache.
        /// </summary>
        public PickaxeDefinition GetBestPickaxe()
        {
            Refresh();
            return Current;
        }

        /// <summary>
        /// Refreshes the cached pickaxe from inventory.
        /// </summary>
        public void Refresh()
        {
            Current = null;
            if (inventory == null || skill == null)
                return;

            foreach (var pick in allPickaxes.OrderByDescending(p => p.Tier))
            {
                var item = Resources.Load<ItemData>("Item/" + pick.Id);
                if (item == null)
                    continue;
                if (inventory.GetItemCount(item) > 0 && skill.Level >= pick.LevelRequirement)
                {
                    Current = pick;
                    break;
                }
            }
        }
    }
}
