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
    public sealed class NpcInteractionOptions : MonoBehaviour
    {
        [Header("Right-Click Menu Options")]
        [SerializeField]
        [Tooltip("When enabled the Talk-to option will appear in the right-click menu.")]
        private bool enableTalk = true;

        [SerializeField]
        [Tooltip("When enabled the Trade option will appear in the right-click menu.")]
        private bool enableTrade = false;

        [SerializeField]
        [Tooltip("When enabled the Pickpocket option will appear in the right-click menu.")]
        private bool enablePickpocket = false;

        [SerializeField]
        [Tooltip("When enabled the Examine option will appear in the right-click menu.")]
        private bool enableExamine = true;

        /// <summary>
        /// Enumerates the enabled actions in the order they should appear inside the menu.
        /// </summary>
        public IEnumerable<NpcInteractionAction> GetEnabledActions()
        {
            if (enableTalk)
                yield return NpcInteractionAction.Talk;

            if (enableTrade)
                yield return NpcInteractionAction.Trade;

            if (enablePickpocket)
                yield return NpcInteractionAction.Pickpocket;

            if (enableExamine)
                yield return NpcInteractionAction.Examine;
        }

        /// <summary>
        ///     Gets whether the pickpocket option is currently enabled for the NPC.
        ///     Runtime systems toggle this during lockouts while designers still control the default state via the inspector.
        /// </summary>
        public bool IsPickpocketEnabled => enablePickpocket;

        /// <summary>
        ///     Updates the pickpocket availability at runtime so skills can temporarily disable the option during cooldowns.
        /// </summary>
        /// <param name="enabled">True when pickpocketing should be exposed in the context menu.</param>
        public void SetPickpocketEnabled(bool enabled)
        {
            enablePickpocket = enabled;
        }
    }

    /// <summary>
    /// Identifies the supported right-click actions that can be exposed on the NPC menu.
    /// </summary>
    public enum NpcInteractionAction
    {
        Talk,
        Trade,
        Pickpocket,
        Examine
    }
}
