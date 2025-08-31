using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Inventory;

namespace Skills.Fishing
{
    [DisallowMultipleComponent]
    public class FishingToolToUse : MonoBehaviour
    {
        [SerializeField] private List<FishingToolDefinition> allTools = new List<FishingToolDefinition>();
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Inventory.Equipment equipment;
        [SerializeField] private FishingSkill skill;

        public FishingToolDefinition Current { get; private set; }

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Inventory.Equipment>();
            if (skill == null)
                skill = GetComponent<FishingSkill>();
        }

        public FishingToolDefinition GetBestTool(IEnumerable<FishingToolDefinition> allowed = null)
        {
            Refresh(allowed);
            return Current;
        }

        public void Refresh(IEnumerable<FishingToolDefinition> allowed = null)
        {
            Current = null;
            if (inventory == null || skill == null)
                return;

            IEnumerable<FishingToolDefinition> tools = allTools;
            if (allowed != null)
            {
                var allowedSet = new HashSet<FishingToolDefinition>(allowed.Where(t => t != null));
                tools = tools.Where(t => allowedSet.Contains(t));
            }

            foreach (var tool in tools.OrderByDescending(t => t.CatchBonus))
            {
                var item = Resources.Load<ItemData>("Item/" + tool.Id);
                if (item == null)
                    continue;
                if (inventory.GetItemCount(item) > 0 && skill.Level >= tool.RequiredLevel)
                {
                    Current = tool;
                    break;
                }
                else if (equipment != null)
                {
                    var entry = equipment.GetEquipped(EquipmentSlot.Weapon);
                    if (entry.item == item && skill.Level >= tool.RequiredLevel)
                    {
                        Current = tool;
                        break;
                    }
                }
            }
        }
    }
}
