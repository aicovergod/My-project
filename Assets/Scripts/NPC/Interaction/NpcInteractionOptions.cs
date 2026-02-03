using System.Collections.Generic;
using UnityEngine;

namespace NPC
{
    /// <summary>
    ///     Stores the interaction options that should appear in the NPC right-click menu.
    ///     Designers can tick the available actions per-NPC, and the menu will be generated
    ///     programmatically to match the configuration at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class NpcInteractionOptions : MonoBehaviour
    {
        [Header("Right-Click Menu Options")]
        [SerializeField]
        [Tooltip("When enabled the Attack option will appear in the right-click menu when combat components are present.")]
        private bool enableAttack = false;

        [SerializeField]
        [Tooltip("When enabled the Pet Attack option will be exposed whenever an eligible combat pet is active.")]
        private bool enablePetAttack = true;

        [SerializeField]
        [Tooltip("When enabled the Companion Attack option will be exposed whenever an eligible combat companion is active.")]
        private bool enableCompanionAttack = true;

        [SerializeField]
        [Tooltip("When enabled the Talk-to option will appear in the right-click menu.")]
        private bool enableTalk = true;

        [SerializeField]
        [Tooltip("When enabled the Trade option will appear in the right-click menu.")]
        private bool enableTrade = false;

        [SerializeField]
        [Tooltip("When enabled the Pickpocket option will appear in the right-click menu.")]
        private bool enablePickpocket = false;

        /// <summary>
        /// Enumerates the enabled actions in the order they should appear inside the menu.
        /// </summary>
        public IEnumerable<NpcInteractionAction> GetEnabledActions()
        {
            if (IsAttackEnabled)
                yield return NpcInteractionAction.Attack;

            if (enableTalk)
                yield return NpcInteractionAction.Talk;

            if (enableTrade)
                yield return NpcInteractionAction.Trade;

            if (enablePickpocket)
                yield return NpcInteractionAction.Pickpocket;

            if (IsExamineEnabled)
                yield return NpcInteractionAction.Examine;
        }

        /// <summary>
        ///     Gets whether the pickpocket option is currently enabled for the NPC.
        ///     Runtime systems toggle this during lockouts while designers still control the default state via the inspector.
        /// </summary>
        public bool IsPickpocketEnabled => enablePickpocket;

        /// <summary>
        ///     Gets whether the attack option is exposed for the NPC in the right-click menu.
        /// </summary>
        public bool IsAttackEnabled => enableAttack;

        /// <summary>
        ///     Gets whether the pet attack option is exposed for the NPC when pets are able to fight.
        /// </summary>
        public bool IsPetAttackEnabled => enablePetAttack;

        /// <summary>
        ///     Gets whether the companion attack option is exposed for the NPC when companions are able to fight.
        /// </summary>
        public bool IsCompanionAttackEnabled => enableCompanionAttack;

        /// <summary>
        ///     Updates the pickpocket availability at runtime so skills can temporarily disable the option during cooldowns.
        /// </summary>
        /// <param name="enabled">True when pickpocketing should be exposed in the context menu.</param>
        public void SetPickpocketEnabled(bool enabled)
        {
            enablePickpocket = enabled;
        }

        /// <summary>
        ///     Updates the attack availability at runtime, allowing quests or scripted encounters to temporarily suppress combat.
        /// </summary>
        /// <param name="enabled">True when the attack entry should appear in the right-click menu.</param>
        public void SetAttackEnabled(bool enabled)
        {
            enableAttack = enabled;
        }

        /// <summary>
        ///     Updates the pet attack availability at runtime, allowing scripted encounters to override the default configuration.
        /// </summary>
        /// <param name="enabled">True when the pet attack entry should appear in the right-click menu.</param>
        public void SetPetAttackEnabled(bool enabled)
        {
            enablePetAttack = enabled;
        }

        /// <summary>
        ///     Updates the companion attack availability at runtime, allowing scripted encounters to override the default configuration.
        /// </summary>
        /// <param name="enabled">True when the companion attack entry should appear in the right-click menu.</param>
        public void SetCompanionAttackEnabled(bool enabled)
        {
            enableCompanionAttack = enabled;
        }
    }

    /// <summary>
    /// Identifies the supported right-click actions that can be exposed on the NPC menu.
    /// </summary>
    public enum NpcInteractionAction
    {
        Attack,
        Talk,
        Trade,
        Pickpocket,
        Examine
    }
}
