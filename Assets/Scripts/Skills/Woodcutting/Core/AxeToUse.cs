using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Inventory;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Chooses the best axe available in the inventory that the player can use.
    /// </summary>
    [DisallowMultipleComponent]
    public class AxeToUse : MonoBehaviour
    {
        [SerializeField] private List<AxeDefinition> allAxes = new List<AxeDefinition>();
        [SerializeField] private Inventory inventory;
        [SerializeField] private WoodcuttingSkill skill;

        public AxeDefinition Current { get; private set; }

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory>();
            if (skill == null)
                skill = GetComponent<WoodcuttingSkill>();
        }

        /// <summary>
        /// Returns the best usable axe. Refreshes the current axe cache.
        /// </summary>
        public AxeDefinition GetBestAxe()
        {
            Refresh();
            return Current;
        }

        /// <summary>
        /// Refreshes the cached axe from inventory.
        /// </summary>
        public void Refresh()
        {
            Current = null;
            if (inventory == null || skill == null)
                return;

            foreach (var axe in allAxes.OrderByDescending(a => a.Power))
            {
                var item = Resources.Load<ItemData>("Item/" + axe.Id);
                if (item == null)
                    continue;
                if (inventory.GetItemCount(item) > 0 && skill.Level >= axe.RequiredWoodcuttingLevel)
                {
                    Current = axe;
                    break;
                }
            }
        }
    }
}
