using System.Collections.Generic;
using Skills.Common.ToolSelection;
using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    /// Chooses the best pickaxe available in the inventory that the player can use.
    /// </summary>
    [DisallowMultipleComponent]
    public class PickaxeToUse : GatheringToolSelectorBase<PickaxeDefinition, MiningSkill>
    {
        [SerializeField] private List<PickaxeDefinition> allPickaxes = new List<PickaxeDefinition>();

        public new PickaxeDefinition Current => base.Current;

        /// <summary>
        /// Provides read-only access to the serialized pickaxe definitions so registries can cache them.
        /// </summary>
        public IReadOnlyList<PickaxeDefinition> AllPickaxes => allPickaxes;

        /// <inheritdoc />
        protected override void Awake()
        {
            base.Awake();
            PickaxeDefinitionRegistry.RegisterDefinitions(allPickaxes);
        }

        /// <summary>
        /// Returns the best usable pickaxe. Refreshes the current pickaxe cache.
        /// </summary>
        public PickaxeDefinition GetBestPickaxe()
        {
            return base.GetBestTool();
        }

        /// <summary>
        /// Refreshes the cached pickaxe from inventory.
        /// </summary>
        public void Refresh()
        {
            base.Refresh();
        }

        protected override IReadOnlyList<PickaxeDefinition> OrderedTools => allPickaxes;

        protected override string GetItemId(PickaxeDefinition definition)
        {
            return definition != null ? definition.Id : null;
        }

        protected override float GetSortKey(PickaxeDefinition definition)
        {
            return definition != null ? definition.Tier : 0f;
        }

        protected override int GetRequiredLevel(PickaxeDefinition definition)
        {
            return definition != null ? definition.LevelRequirement : int.MaxValue;
        }

        protected override int GetCurrentSkillLevel()
        {
            return SkillComponent != null ? SkillComponent.Level : 0;
        }
    }
}
