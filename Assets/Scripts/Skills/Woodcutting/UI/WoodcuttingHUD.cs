using UnityEngine;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Displays woodcutting progress above the current tree, mirroring the player's axe and tick cadence.
    /// </summary>
    public class WoodcuttingHUD : WoodcuttingHudBase<WoodcuttingHUD>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static WoodcuttingHUD CreateInstance()
        {
            var go = new GameObject(nameof(WoodcuttingHUD));
            return go.AddComponent<WoodcuttingHUD>();
        }

        protected override string ResolveProgressRootName() => "WoodcuttingProgress";

        protected override string ResolveToolRootName() => "WoodcuttingAxe";

        protected override WoodcuttingSkill LocateSkill()
        {
            var skills = Object.FindObjectsOfType<WoodcuttingSkill>(true);
            if (skills == null || skills.Length == 0)
                return null;

            for (int i = 0; i < skills.Length; i++)
            {
                var candidate = skills[i];
                if (candidate == null)
                    continue;

                if (IsCompanionSkill(candidate))
                    continue;

                if (BelongsToPlayer(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
