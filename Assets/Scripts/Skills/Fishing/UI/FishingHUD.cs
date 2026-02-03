using UnityEngine;

namespace Skills.Fishing
{
    /// <summary>
    /// Presents fishing progress above the active spot, including the equipped tool sprite and tick timing.
    /// </summary>
    public class FishingHUD : FishingHudBase<FishingHUD>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static FishingHUD CreateInstance()
        {
            var go = new GameObject(nameof(FishingHUD));
            return go.AddComponent<FishingHUD>();
        }

        protected override string ResolveProgressRootName() => "FishingProgress";

        protected override string ResolveToolRootName() => "FishingTool";

        protected override FishingSkill LocateSkill()
        {
            var skills = Object.FindObjectsOfType<FishingSkill>(true);
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
