using System.Collections.Generic;
using Skills.Common.ToolSelection;
using UnityEngine;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Chooses the best axe available in the inventory that the player can use.
    /// </summary>
    [DisallowMultipleComponent]
    public class AxeToUse : GatheringToolSelectorBase<AxeDefinition, WoodcuttingSkill>
    {
        [SerializeField] private List<AxeDefinition> allAxes = new List<AxeDefinition>();

        public new AxeDefinition Current => base.Current;

        /// <summary>
        /// Returns the best usable axe. Refreshes the current axe cache.
        /// </summary>
        public AxeDefinition GetBestAxe()
        {
            return base.GetBestTool();
        }

        /// <summary>
        /// Refreshes the cached axe from inventory.
        /// </summary>
        public void Refresh()
        {
            base.Refresh();
        }

        protected override IReadOnlyList<AxeDefinition> OrderedTools => allAxes;

        protected override string GetItemId(AxeDefinition definition)
        {
            return definition != null ? definition.Id : null;
        }

        protected override float GetSortKey(AxeDefinition definition)
        {
            return definition != null ? definition.Power : 0f;
        }

        protected override int GetRequiredLevel(AxeDefinition definition)
        {
            return definition != null ? definition.RequiredWoodcuttingLevel : int.MaxValue;
        }

        protected override int GetCurrentSkillLevel()
        {
            return SkillComponent != null ? SkillComponent.Level : 0;
        }
    }
}
