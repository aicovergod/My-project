using System.Collections;
using UnityEngine;
using World;

namespace Skills.Common.UI
{
    /// <summary>
    /// Provides shared logic for gathering HUDs that need to continually retry binding to their associated skill
    /// when the player object spawns after the HUD initialises.
    /// </summary>
    /// <typeparam name="TSkill">The concrete skill component that the HUD observes (e.g. fishing, mining).</typeparam>
    public abstract class GatheringSkillHudBase<TSelf, TSkill> : SceneGatedSingletonBehaviour<TSelf>
        where TSelf : GatheringSkillHudBase<TSelf, TSkill>
        where TSkill : MonoBehaviour
    {
        /// <summary>
        /// Cached reference to the active skill that the HUD is presenting information for.
        /// </summary>
        protected TSkill skill;

        /// <summary>
        /// Coroutine that repeatedly attempts to locate the skill when it is not yet available.
        /// </summary>
        private Coroutine skillRefreshRoutine;

        /// <summary>
        /// Delay between retry attempts so we avoid hammering <see cref="UnityEngine.Object.FindObjectOfType"/> every frame.
        /// </summary>
        private readonly WaitForSecondsRealtime skillRetryDelay = new WaitForSecondsRealtime(0.5f);

        /// <summary>
        /// Ensures the HUD is bound to the live skill instance, starting the retry coroutine if the skill is missing.
        /// </summary>
        protected void RefreshSkillSubscription()
        {
            var current = LocateSkill();
            if (ReferenceEquals(current, skill))
            {
                if (current == null)
                    EnsureSkillRefreshRoutine();
                return;
            }

            DetachFromSkill();

            if (current == null)
            {
                EnsureSkillRefreshRoutine();
                return;
            }

            CancelSkillRefreshRoutine();
            AttachToSkill(current);
        }

        /// <summary>
        /// Unsubscribes from the previously tracked skill and clears the cached reference.
        /// </summary>
        protected void DetachFromSkill()
        {
            if (skill == null)
                return;

            OnSkillDetached(skill);
            skill = null;
        }

        /// <summary>
        /// Starts the coroutine that continually retries locating the gathering skill.
        /// </summary>
        protected void EnsureSkillRefreshRoutine()
        {
            if (!isActiveAndEnabled)
                return;

            if (skillRefreshRoutine != null)
                return;

            skillRefreshRoutine = StartCoroutine(AwaitSkillRoutine());
        }

        /// <summary>
        /// Stops the retry coroutine when the HUD is disabled or once a skill has been located.
        /// </summary>
        protected void CancelSkillRefreshRoutine()
        {
            if (skillRefreshRoutine == null)
                return;

            StopCoroutine(skillRefreshRoutine);
            skillRefreshRoutine = null;
        }

        /// <summary>
        /// Repeatedly attempts to locate the target skill while the HUD remains active.
        /// </summary>
        private IEnumerator AwaitSkillRoutine()
        {
            while (isActiveAndEnabled)
            {
                var current = LocateSkill();
                if (current != null)
                {
                    AttachToSkill(current);
                    break;
                }

                yield return skillRetryDelay;
            }

            skillRefreshRoutine = null;
        }

        /// <summary>
        /// Stores the located skill reference and allows derived HUDs to subscribe to relevant events.
        /// </summary>
        /// <param name="current">The skill instance that was found.</param>
        private void AttachToSkill(TSkill current)
        {
            skill = current;
            OnSkillLocated(current);
        }

        /// <summary>
        /// Locates the skill instance that the HUD should observe. By default this searches the active scene.
        /// </summary>
        /// <returns>The located skill instance, or <c>null</c> if it has not spawned yet.</returns>
        protected virtual TSkill LocateSkill()
        {
            return UnityEngine.Object.FindObjectOfType<TSkill>();
        }

        /// <summary>
        /// Allows derived classes to subscribe to skill events when a skill instance becomes available.
        /// </summary>
        /// <param name="located">The skill instance that the HUD should observe.</param>
        protected abstract void OnSkillLocated(TSkill located);

        /// <summary>
        /// Allows derived classes to unsubscribe from skill events when the tracked instance is removed.
        /// </summary>
        /// <param name="previous">The skill instance that is being detached.</param>
        protected abstract void OnSkillDetached(TSkill previous);
    }
}
