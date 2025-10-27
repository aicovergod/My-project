using System;
using UnityEngine;
using Combat;
using Companions;
using Companions.Conversation;
using Inventory;
using MyGame.Drops;
using Pets;
using UI.Chat;

namespace NPC.Combat.Mechanics
{
    /// <summary>
    /// Handles the special Hades fragment drop awarded by ore golems. This logic lives on a dedicated
    /// component so <see cref="NpcCombatant"/> can focus on the generic combat pipeline while ore golem
    /// prefabs opt into their bespoke behaviour through composition.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcCombatant))]
    [RequireComponent(typeof(NpcDropper))]
    public sealed class OreGolemHadesFragmentDropper : MonoBehaviour
    {
        [Header("Ore Golem Detection")]
        [SerializeField, Tooltip("Case-insensitive keyword used to verify this combatant represents an ore golem.")]
        private string oreGolemNameKeyword = "Ore Golem";

        [Header("Drop Configuration")]
        [SerializeField, Tooltip("Item identifier for the Hades fragment awarded on success.")]
        private string fragmentItemId = "Hades Fragment";

        [SerializeField, Tooltip("Denominator for the 1-in-N drop roll. A value of 40 means a 1/40 chance."), Min(1)]
        private int dropDenominator = 40;

        [Header("Dialogue Lines")]
        [SerializeField, Tooltip("Lines companions can say when the player receives the fragment."), TextArea]
        private string[] companionPlayerDropLines =
        {
            "Damn, lucky sod.",
            "I must be your good luck charm.",
            "Congrats on the Hades Fragment.",
            "I heard those fragments, can upgrade your Ore Bag.",
            "I wonder where the Ore Golem, was hiding that fragment <emoji=02>,",
            "Whoa, no way you actually got one!",
            "Alright, share some of that luck, yeah?",
            "Wait, you just got a Hades Fragment? Seriously?",
            "How the hell did *you* pull that?",
            "Yo, that’s actually rare as hell.",
            "Nice drop, guess the Ore Golem liked you.",
            "See? Told you this spot was worth it.",
            "Don’t mind me, just over here getting nothing.",
            "That’s huge, {playerName}. You lucky git.",
            "Hold up… was that a Hades Fragment? gz!!!",
            "That’ll fetch a nice price.",
            "Okay, that’s actually sick, fair play.",
            "Guess killing Ore Golem's is finally paying off, huh?",
            "Hope you’re not planning to flex that all day.",
            "Alright, now I’m officially jealous.",
            "That’s a solid pull, I’ll give you that."
        };

        [SerializeField, Tooltip("Lines companions can say when they secure the fragment themselves."), TextArea]
        private string[] companionSelfDropLines =
        {
            "Must be my lucky day.",
            "I can keep this, right? <emoji=24>",
            "Now, that's lucky.",
            "Wonder where that golem was hiding that, lol.",
            "guess the golem liked me more than you.",
            "hey, look what just dropped for me!",
            "no way, i actually got one!",
            "finally, some good loot for a change.",
            "that’s going straight in my bag.",
            "looks like the gods are smiling on me today.",
            "well, would you look at that.",
            "haha, jackpot.",
            "yep, i’ll be bragging about this one later.",
            "hope you’re not too jealous, {playerName}.",
            "i swear i wasn’t even trying for it.",
            "that’s one for the collection.",
            "gotta love when luck actually shows up.",
            "oh nice, that’s rare, right?",
            "didn’t expect that drop at all.",
            "guess it’s my turn to be the lucky one.",
            "this’ll upgrade my gear nicely.",
            "you saw that, right? i wasn’t imagining it.",
            "finally, the grind pays off.",
            "maybe i should buy a lottery ticket next.",
            "oh damn, that shimmer’s pretty."
        };

        private static GroundItemSpawner sharedGroundItemSpawner;

        private NpcCombatant combatant;
        private NpcDropper dropper;
        private bool dropRolledThisLife;
        private bool awaitingRespawn;
        private object lastDamageSource;
        private CombatTarget lastCombatSource;

        private void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
            dropper = GetComponent<NpcDropper>();
        }

        private void OnEnable()
        {
            if (combatant == null)
                combatant = GetComponent<NpcCombatant>();
            if (dropper == null)
                dropper = GetComponent<NpcDropper>();

            combatant.OnKilledByPlayer += HandleKilledByPlayer;
            combatant.OnDeath += HandleDeath;
            combatant.OnHealthChanged += HandleHealthChanged;
            combatant.OnDamageAttributed += HandleDamageAttributed;

            ResetDropState();
        }

        private void OnDisable()
        {
            if (combatant != null)
            {
                combatant.OnKilledByPlayer -= HandleKilledByPlayer;
                combatant.OnDeath -= HandleDeath;
                combatant.OnHealthChanged -= HandleHealthChanged;
                combatant.OnDamageAttributed -= HandleDamageAttributed;
            }
        }

        private void HandleDamageAttributed(object source, CombatTarget combatSource)
        {
            lastDamageSource = source;
            lastCombatSource = combatSource;
        }

        private void HandleKilledByPlayer(NpcCombatant sender, GameObject killingPlayer)
        {
            if (sender != combatant)
                return;

            TryAwardFragment(killingPlayer);
        }

        private void HandleDeath()
        {
            awaitingRespawn = true;
        }

        private void HandleHealthChanged(int currentHp, int maxHp)
        {
            if (!awaitingRespawn)
                return;

            if (currentHp <= 0)
                return;

            ResetDropState();
        }

        private void ResetDropState()
        {
            dropRolledThisLife = false;
            awaitingRespawn = false;
            lastDamageSource = null;
            lastCombatSource = null;
        }

        private void TryAwardFragment(GameObject killingPlayer)
        {
            if (dropRolledThisLife)
                return;

            if (!IsOreGolem())
                return;

            dropRolledThisLife = true;

            if (!RollForFragment())
                return;

            var fragment = ItemDatabase.GetItem(fragmentItemId);
            if (fragment == null)
            {
                Debug.LogError($"{name} could not locate item '{fragmentItemId}' while attempting to award a Hades Fragment.", this);
                return;
            }

            Vector3 spawnPosition = transform.position;
            var spawner = ResolveGroundItemSpawner();
            if (spawner != null)
                spawner.Spawn(fragment, 1, spawnPosition);
            else
                InventoryBridge.AddItem(fragment, 1);

            var chat = ChatService.Instance;
            chat?.PublishGameMessage("The ore golem, has dropped a Hades Fragment.");

            CompanionConversationService.RegisterEvent(
                "secured a Hades Fragment",
                CompanionEventType.Loot,
                CompanionEventMetadata.Create("You", combatant != null ? combatant.name : name, null, spawnPosition));

            bool companionKill = DetermineCompanionKill(killingPlayer);
            TryEmitCompanionDialogue(chat, companionKill);
        }

        private bool IsOreGolem()
        {
            if (string.IsNullOrWhiteSpace(oreGolemNameKeyword))
                return true; // Allow disabling the check via inspector.

            string keyword = oreGolemNameKeyword.Trim();
            if (string.IsNullOrEmpty(keyword))
                return true;

            if (ContainsIgnoreCase(gameObject.name, keyword))
                return true;

            if (combatant != null && combatant.Profile != null && ContainsIgnoreCase(combatant.Profile.name, keyword))
                return true;

            return false;
        }

        private bool RollForFragment()
        {
            if (dropDenominator <= 1)
                return true;

            int roll = UnityEngine.Random.Range(1, dropDenominator + 1);
            bool success = roll == 1;

            if (combatant != null && combatant.LogDamage)
            {
                Debug.Log($"{name} rolled Hades fragment drop 1/{dropDenominator} (rolled {roll}) => {(success ? "success" : "no drop")}.", this);
            }

            return success;
        }

        private GroundItemSpawner ResolveGroundItemSpawner()
        {
            if (dropper != null && dropper.spawner != null)
                return dropper.spawner;

            if (sharedGroundItemSpawner == null)
                sharedGroundItemSpawner = FindObjectOfType<GroundItemSpawner>();

            return sharedGroundItemSpawner;
        }

        private bool DetermineCompanionKill(GameObject killingPlayer)
        {
            if (!CompanionManager.HasActiveCompanion)
                return false;

            var activeCompanion = CompanionManager.ActiveCompanion;
            if (activeCompanion == null)
                return false;

            if (killingPlayer == activeCompanion.gameObject)
                return true;

            if (lastDamageSource is Component sourceComponent && sourceComponent.GetComponentInParent<CompanionController>() != null)
                return true;

            if (lastDamageSource is GameObject sourceObject && sourceObject.GetComponentInParent<CompanionController>() != null)
                return true;

            if (lastCombatSource is Component combatComponent && combatComponent.GetComponentInParent<CompanionController>() != null)
                return true;

            return false;
        }

        private void TryEmitCompanionDialogue(ChatService chatService, bool companionKill)
        {
            if (!CompanionManager.HasActiveCompanion || chatService == null)
                return;

            string[] sourceLines = companionKill ? companionSelfDropLines : companionPlayerDropLines;
            if (sourceLines == null || sourceLines.Length == 0)
                return;

            if (!companionKill && UnityEngine.Random.value > 0.5f)
                return;

            int index = UnityEngine.Random.Range(0, sourceLines.Length);
            if (index < 0 || index >= sourceLines.Length)
                return;

            string template = sourceLines[index];
            if (string.IsNullOrWhiteSpace(template))
                return;

            string playerName = chatService.ActiveUsername;
            string replacement = string.IsNullOrWhiteSpace(playerName) ? "you" : playerName.Trim();
            string line = template.Replace("{playerName}", replacement);

            string speaker = CompanionManager.GetCompanionDisplayName();
            chatService.PublishCompanionMessage(speaker, line);
        }

        private static bool ContainsIgnoreCase(string source, string value)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(value) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
