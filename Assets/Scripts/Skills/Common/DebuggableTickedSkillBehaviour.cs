using UnityEngine;

namespace Skills.Common
{
    /// <summary>
    ///     Extends <see cref="TickedSkillBehaviour"/> with a shared debug logging toggle so
    ///     gathering skills can consistently control verbose diagnostics through the admin menu.
    /// </summary>
    public abstract class DebuggableTickedSkillBehaviour : TickedSkillBehaviour
    {
        [SerializeField]
        [Tooltip("Enables verbose debug logging for this skill.")]
        protected bool enableDebugLogging;

        /// <summary>
        ///     Gets or sets the runtime flag controlling verbose debug logging for the skill.
        ///     The admin debug menu binds directly to this property so QA can toggle logging at runtime.
        /// </summary>
        public bool EnableDebugLogging
        {
            get => enableDebugLogging;
            set => enableDebugLogging = value;
        }

        /// <summary>
        ///     Routes ticker subscription logging through the shared debug flag so derived skills
        ///     do not need to reimplement the override.
        /// </summary>
        protected override bool LogTickerSubscription => enableDebugLogging;
    }
}
