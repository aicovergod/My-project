using System.Collections.Generic;
using System.Linq;
using Inventory;
using Skills.Common;
using UnityEngine;

namespace Skills.Common.ToolSelection
{
    /// <summary>
    ///     Generic helper that resolves the best gathering tool available to the player based on
    ///     inventory, equipment, and skill level checks. Concrete selectors only need to expose
    ///     their definition list and implement the template members that describe how to read
    ///     identifiers, level requirements, and sorting priority from a definition.
    /// </summary>
    /// <typeparam name="TDefinition">Type representing the tool definition (ScriptableObject).</typeparam>
    /// <typeparam name="TSkill">Skill component that exposes the current level.</typeparam>
    [DisallowMultipleComponent]
    public abstract class GatheringToolSelectorBase<TDefinition, TSkill> : MonoBehaviour
        where TDefinition : Object
        where TSkill : Component
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Inventory component used to check whether the tool is owned.")]
        private Inventory.Inventory inventory;

        [SerializeField, Tooltip("Equipment component inspected when the tool is equipped.")]
        private Equipment equipment;

        [SerializeField, Tooltip("Skill component supplying the current level requirement checks.")]
        private TSkill skill;

        // Cached lookup for ItemData assets. Shared with other gathering skills through the helper.
        private Dictionary<string, ItemData> itemCache;

        /// <summary>
        ///     The best currently available tool after <see cref="Refresh"/> has been called.
        /// </summary>
        public TDefinition Current { get; private set; }

        /// <summary>
        ///     Provides access to the serialized inventory component for derived selectors.
        /// </summary>
        protected Inventory.Inventory InventoryComponent => inventory;

        /// <summary>
        ///     Provides access to the serialized equipment component for derived selectors.
        /// </summary>
        protected Equipment EquipmentComponent => equipment;

        /// <summary>
        ///     Provides access to the serialized skill component for derived selectors.
        /// </summary>
        protected TSkill SkillComponent => skill;

        /// <summary>
        ///     Collection of tool definitions to evaluate. Implementations typically expose a serialized
        ///     list in the inspector and return it here. The base class automatically sorts the list using
        ///     <see cref="GetSortKey(TDefinition)"/> before performing inventory checks.
        /// </summary>
        protected abstract IReadOnlyList<TDefinition> OrderedTools { get; }

        /// <summary>
        ///     The equipment slot that should be queried for the tool. Defaults to the weapon slot but
        ///     can be overridden if a future skill equips tools elsewhere.
        /// </summary>
        protected virtual EquipmentSlot EquipmentSlotToCheck => EquipmentSlot.Weapon;

        /// <summary>
        ///     Ensures the inventory, equipment, and skill references are populated when the component awakens.
        /// </summary>
        protected virtual void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            if (skill == null)
                skill = GetComponent<TSkill>();
        }

        /// <summary>
        ///     Returns the best tool that satisfies the player's level requirement and is owned or equipped.
        /// </summary>
        /// <param name="allowed">Optional subset of definitions to consider.</param>
        public TDefinition GetBestTool(IEnumerable<TDefinition> allowed = null)
        {
            Refresh(allowed);
            return Current;
        }

        /// <summary>
        ///     Rebuilds the cached <see cref="Current"/> tool by scanning the ordered definition list, using
        ///     the shared <see cref="GatheringInventoryHelper"/> cache to resolve <see cref="ItemData"/> assets.
        /// </summary>
        /// <param name="allowed">Optional subset of definitions to consider.</param>
        public void Refresh(IEnumerable<TDefinition> allowed = null)
        {
            Current = null;
            if (inventory == null || skill == null)
                return;

            IEnumerable<TDefinition> candidates = OrderedTools != null
                ? OrderedTools.Where(def => def != null)
                : Enumerable.Empty<TDefinition>();

            if (allowed != null)
            {
                var allowedSet = new HashSet<TDefinition>(allowed.Where(def => def != null));
                candidates = candidates.Where(allowedSet.Contains);
            }

            foreach (var tool in candidates.OrderByDescending(GetSortKey))
            {
                if (!MeetsLevelRequirement(tool))
                    continue;

                var item = GatheringInventoryHelper.GetItemData(GetItemId(tool), ref itemCache);
                if (item == null)
                    continue;

                if (inventory.GetItemCount(item) > 0 || IsToolEquipped(item))
                {
                    Current = tool;
                    break;
                }
            }
        }

        /// <summary>
        ///     Checks whether the player satisfies the required level for the supplied definition.
        /// </summary>
        private bool MeetsLevelRequirement(TDefinition definition)
        {
            return GetCurrentSkillLevel() >= GetRequiredLevel(definition);
        }

        /// <summary>
        ///     Determines whether the tool is currently equipped in the configured slot.
        /// </summary>
        /// <param name="item">Item data representing the tool.</param>
        /// <returns>True if the tool is equipped, otherwise false.</returns>
        protected virtual bool IsToolEquipped(ItemData item)
        {
            if (equipment == null || item == null)
                return false;

            var entry = equipment.GetEquipped(EquipmentSlotToCheck);
            return entry.item == item;
        }

        /// <summary>
        ///     Reads the identifier used to resolve <see cref="ItemData"/> assets for a definition.
        /// </summary>
        protected abstract string GetItemId(TDefinition definition);

        /// <summary>
        ///     Provides the sort key that determines tool priority. Higher values are preferred.
        /// </summary>
        protected abstract float GetSortKey(TDefinition definition);

        /// <summary>
        ///     Reads the required skill level for the definition.
        /// </summary>
        protected abstract int GetRequiredLevel(TDefinition definition);

        /// <summary>
        ///     Retrieves the player's current skill level used for tool validation.
        /// </summary>
        protected abstract int GetCurrentSkillLevel();
    }
}
