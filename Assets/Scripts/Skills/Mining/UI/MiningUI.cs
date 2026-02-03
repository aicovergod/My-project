using Player;
using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    /// Displays mining progress above the current rock for the player character.
    /// Inherits shared logic from <see cref="MiningHudBase{THud}"/> so the companion variant can
    /// reuse the same pipeline.
    /// </summary>
    public class MiningUI : MiningHudBase<MiningUI>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static MiningUI CreateInstance()
        {
            var go = new GameObject(nameof(MiningUI));
            return go.AddComponent<MiningUI>();
        }

        /// <inheritdoc />
        protected override string ResolveProgressRootName()
        {
            return "MiningProgress";
        }

        /// <inheritdoc />
        protected override string ResolveToolRootName()
        {
            return "MiningPickaxe";
        }

        /// <inheritdoc />
        protected override MiningSkill LocateSkill()
        {
            var skills = Object.FindObjectsOfType<MiningSkill>(true);
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

            // Player has not spawned yet; wait for the retry coroutine to locate the component later.
            return null;
        }

        /// <summary>
        /// Determines whether the supplied skill is attached to the player hierarchy.
        /// </summary>
        private static bool BelongsToPlayer(MiningSkill candidate)
        {
            if (candidate == null)
                return false;

            var transform = candidate.transform;
            if (transform == null)
                return false;

            var root = transform.root;
            if (root != null)
            {
                if (root.CompareTag("Player"))
                    return true;

                if (root.GetComponent<PlayerMover>() != null)
                    return true;

                if (root.GetComponent<MiningPersonalNodeController>() != null)
                    return true;
            }

            var go = transform.gameObject;
            if (go != null && go.CompareTag("Player"))
                return true;

            if (candidate.GetComponent<PlayerMover>() != null || candidate.GetComponentInParent<PlayerMover>() != null)
                return true;

            if (candidate.GetComponent<MiningPersonalNodeController>() != null || candidate.GetComponentInParent<MiningPersonalNodeController>() != null)
                return true;

            return false;
        }
    }
}
