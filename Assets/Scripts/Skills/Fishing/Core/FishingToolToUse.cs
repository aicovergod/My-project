using System.Collections.Generic;
using Skills.Common.ToolSelection;
using UnityEngine;

namespace Skills.Fishing
{
    [DisallowMultipleComponent]
    public class FishingToolToUse : GatheringToolSelectorBase<FishingToolDefinition, FishingSkill>
    {
        [SerializeField] private List<FishingToolDefinition> allTools = new List<FishingToolDefinition>();

        public new FishingToolDefinition Current => base.Current;

        public new FishingToolDefinition GetBestTool(IEnumerable<FishingToolDefinition> allowed = null)
        {
            return base.GetBestTool(allowed);
        }

        public new void Refresh(IEnumerable<FishingToolDefinition> allowed = null)
        {
            base.Refresh(allowed);
        }

        protected override IReadOnlyList<FishingToolDefinition> OrderedTools => allTools;

        protected override string GetItemId(FishingToolDefinition definition)
        {
            return definition != null ? definition.Id : null;
        }

        protected override float GetSortKey(FishingToolDefinition definition)
        {
            return definition != null ? definition.CatchBonus : 0f;
        }

        protected override int GetRequiredLevel(FishingToolDefinition definition)
        {
            return definition != null ? definition.RequiredLevel : int.MaxValue;
        }

        protected override int GetCurrentSkillLevel()
        {
            return SkillComponent != null ? SkillComponent.Level : 0;
        }
    }
}
