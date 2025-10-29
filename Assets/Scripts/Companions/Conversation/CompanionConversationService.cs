using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using Combat;
using Companions;
using Inventory;
using Skills;
using Skills.Common;
using Skills.Mining;
using UI.Chat;
using UnityEngine;
using Companions.Equipment;
using UnityEngine.SceneManagement;
using Util;
using World;
using NPC;
using RuntimeInventory = global::Inventory.Inventory;

namespace Companions.Conversation
{
    /// <summary>
    /// Persistent service that listens to the companion chat channel, analyses player-authored messages,
    /// and orchestrates context-aware companion responses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionConversationService : SceneGatedSingletonBehaviour<CompanionConversationService>, ITickable
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Optional explicit reference to the conversation memory component.")]
        private CompanionConversationMemory conversationMemory;

        [SerializeField, Tooltip("Response templates that power the companion's dialogue.")]
        private CompanionDialogueResponseLibrary responseLibrary = new CompanionDialogueResponseLibrary();

        [Header("Parsing & Rules")]
        [SerializeField, Tooltip("Score thresholds per dialogue intent. Values below zero are clamped to zero.")]
        private List<IntentScoreThreshold> intentScoreThresholds = new List<IntentScoreThreshold>
        {
            new IntentScoreThreshold(CompanionDialogueIntent.Greeting, 1f),
            new IntentScoreThreshold(CompanionDialogueIntent.SmallTalk, 1.2f),
            new IntentScoreThreshold(CompanionDialogueIntent.StatusQuery, 2.2f),
            new IntentScoreThreshold(CompanionDialogueIntent.SkillLevelQuery, 2.1f),
            new IntentScoreThreshold(CompanionDialogueIntent.Gratitude, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.Farewell, 1.4f),
            new IntentScoreThreshold(CompanionDialogueIntent.Compliment, 1.8f),
            new IntentScoreThreshold(CompanionDialogueIntent.RequestAssistance, 1.8f),
            new IntentScoreThreshold(CompanionDialogueIntent.PlayerSkillProposal, 1.9f),
            new IntentScoreThreshold(CompanionDialogueIntent.AcknowledgeRecentEvent, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.ProactiveSkillQuestion, 1f),
            new IntentScoreThreshold(CompanionDialogueIntent.AcceptSkillPlan, 1.7f),
            new IntentScoreThreshold(CompanionDialogueIntent.DeclineSkillPlan, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.DeferSkillPlan, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.RequestAlternateSkill, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.CompanionSuggestionRequest, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.CompanionSuggestionReminder, 1.3f),
            new IntentScoreThreshold(CompanionDialogueIntent.PlayerApology, 1.25f)
        };

        private readonly List<CompanionDialogueRule> rules = new List<CompanionDialogueRule>();

        [Header("Typing Behaviour")]
        [SerializeField, Tooltip("Base delay applied before the companion responds (seconds).")]
        private float baseTypingDelaySeconds = 0.35f;

        [SerializeField, Tooltip("Randomised delay per word (seconds). X = minimum, Y = maximum.")]
        private Vector2 perWordTypingDelayRange = new Vector2(0.05f, 0.11f);

        [Header("Debug")]
        [SerializeField, Tooltip("When enabled (alongside the global companion debug flag) the service logs rule matches.")]
        private bool enableRuleTracing;

        [SerializeField, Tooltip("When enabled (alongside the global companion debug flag) the service logs response assembly.")]
        private bool enableResponseTracing;

        [SerializeField, Tooltip("When enabled (alongside the global companion debug flag) the service logs memory updates.")]
        private bool enableMemoryTracing;

        [Header("Status Composition")]
        [SerializeField, Tooltip("Descriptor pool used to describe the companion's current mood in replies.")]
        private string[] companionMoodDescriptors =
        {
            "alert",
            "ready",
            "steady",
            "focused",
            "sharp",
            "energised"
        };

        [SerializeField, Tooltip("Minimum minutes before the same status line can repeat.")]
        private float statusRepeatCooldownMinutes = 5f;

        [Header("Proactive Skill Scheduler")]
        [SerializeField, Tooltip("Number of idle ticks before the companion considers prompting about a skill."), Min(1)]
        private int proactiveIdleTickThreshold = 6;

        [SerializeField, Tooltip("Cooldown applied between proactive skill questions (minutes)."), Min(0.1f)]
        private float proactiveQuestionCooldownMinutes = 6f;

        [SerializeField, Tooltip("Maximum age (in minutes) of a skill event that can seed a proactive prompt."), Min(0.1f)]
        private float minimumSkillEventFreshnessMinutes = 12f;

        [Header("Small Talk")]
        [SerializeField, Tooltip("Number of idle ticks before the companion drifts into small talk."), Min(1)]
        private int smallTalkIdleTickThreshold = 8;

        [SerializeField, Tooltip("Cooldown applied between proactive small-talk beats (minutes)."), Min(0.1f)]
        private float smallTalkCooldownMinutes = 4f;

        [Header("Suggestion Prompt")]
        [SerializeField, Tooltip("Cooldown applied after answering a suggestion request (minutes)."), Min(0.1f)]
        private float suggestionCooldownMinutes = 5f;

        [SerializeField, Tooltip("Maximum age (minutes) for NPC kill history considered in suggestions."), Min(0.1f)]
        private float npcKillRetentionMinutes = 6f;

        private DateTime? lastSuggestionAnsweredUtc;
        private string lastSuggestionMessage = string.Empty;
        private bool playerRepeatedSuggestionRequest;
        private bool playerNeededSkillReminderApology;
        private SuggestionPayload lastSuggestionPayload = SuggestionPayload.Empty;
        private readonly LinkedList<NpcKillRecord> recentNpcKills = new();

        private static readonly Dictionary<string, TokenSkillMapping> SkillTokenMappings =
            new Dictionary<string, TokenSkillMapping>(StringComparer.Ordinal)
            {
                { "mine", TokenSkillMapping.ForSkills(1.2f, SkillType.Mining) },
                { "mining", TokenSkillMapping.ForSkills(1.4f, SkillType.Mining) },
                { "miner", TokenSkillMapping.ForSkills(1.1f, SkillType.Mining) },
                { "mined", TokenSkillMapping.ForSkills(1f, SkillType.Mining) },
                { "mines", TokenSkillMapping.ForSkills(1f, SkillType.Mining) },
                { "ore", TokenSkillMapping.ForSkills(0.8f, SkillType.Mining) },
                { "ores", TokenSkillMapping.ForSkills(0.8f, SkillType.Mining) },
                { "rock", TokenSkillMapping.ForSkills(0.9f, SkillType.Mining) },
                { "rocks", TokenSkillMapping.ForSkills(0.9f, SkillType.Mining) },
                { "pick", TokenSkillMapping.ForSkills(1.1f, SkillType.Mining) },
                { "pickaxe", TokenSkillMapping.ForSkills(1.1f, SkillType.Mining) },
                { "prospect", TokenSkillMapping.ForSkills(1f, SkillType.Mining) },
                { "chop", TokenSkillMapping.ForSkills(1.2f, SkillType.Woodcutting) },
                { "woodcut", TokenSkillMapping.ForSkills(1.3f, SkillType.Woodcutting) },
                { "woodcutting", TokenSkillMapping.ForSkills(1.4f, SkillType.Woodcutting) },
                { "wood", TokenSkillMapping.ForSkills(0.9f, SkillType.Woodcutting) },
                { "tree", TokenSkillMapping.ForSkills(0.9f, SkillType.Woodcutting) },
                { "trees", TokenSkillMapping.ForSkills(0.9f, SkillType.Woodcutting) },
                { "wc", TokenSkillMapping.ForSkills(1.1f, SkillType.Woodcutting) },
                { "lumber", TokenSkillMapping.ForSkills(1f, SkillType.Woodcutting) },
                { "axe", TokenSkillMapping.ForSkills(1f, SkillType.Woodcutting) },
                { "axes", TokenSkillMapping.ForSkills(1f, SkillType.Woodcutting) },
                { "logs", new TokenSkillMapping(1.1f, "firemaking", "firemaking", SkillType.Woodcutting, SkillType.Firemaking) },
                { "fish", TokenSkillMapping.ForSkills(1.2f, SkillType.Fishing) },
                { "fishing", TokenSkillMapping.ForSkills(1.4f, SkillType.Fishing) },
                { "net", TokenSkillMapping.ForSkills(0.9f, SkillType.Fishing) },
                { "angler", TokenSkillMapping.ForSkills(0.9f, SkillType.Fishing) },
                { "fishin", TokenSkillMapping.ForSkills(1f, SkillType.Fishing) },
                { "rod", TokenSkillMapping.ForSkills(0.8f, SkillType.Fishing) },
                { "harpoon", TokenSkillMapping.ForSkills(0.9f, SkillType.Fishing) },
                { "bait", TokenSkillMapping.ForSkills(0.7f, SkillType.Fishing) },
                { "catch", TokenSkillMapping.ForSkills(0.8f, SkillType.Fishing) },
                { "cook", TokenSkillMapping.ForSkills(1.2f, SkillType.Cooking) },
                { "cooking", TokenSkillMapping.ForSkills(1.3f, SkillType.Cooking) },
                { "cookin", TokenSkillMapping.ForSkills(1f, SkillType.Cooking) },
                { "stew", TokenSkillMapping.ForSkills(0.6f, SkillType.Cooking) },
                { "meal", TokenSkillMapping.ForSkills(0.6f, SkillType.Cooking) },
                { "chef", TokenSkillMapping.ForSkills(0.9f, SkillType.Cooking) },
                { "kitchen", TokenSkillMapping.ForSkills(0.8f, SkillType.Cooking) },
                { "firemake", TokenSkillMapping.ForSkills(1.2f, SkillType.Firemaking) },
                { "firemaking", TokenSkillMapping.ForSkills(1.3f, SkillType.Firemaking) },
                { "bonfire", TokenSkillMapping.ForSkills(1.1f, SkillType.Firemaking) },
                { "light", TokenSkillMapping.ForSkills(0.7f, SkillType.Firemaking) },
                { "fire", TokenSkillMapping.ForSkills(0.8f, SkillType.Firemaking) },
                { "burn", TokenSkillMapping.ForSkills(0.8f, SkillType.Firemaking) },
                { "burning", TokenSkillMapping.ForSkills(0.8f, SkillType.Firemaking) },
                { "fm", TokenSkillMapping.ForSkills(1.1f, SkillType.Firemaking) },
                { "smith", TokenSkillMapping.ForFallback(1.2f, "smithing", "smithing") },
                { "smithing", TokenSkillMapping.ForFallback(1.4f, "smithing", "smithing") },
                { "smelt", TokenSkillMapping.ForFallback(1.1f, "smithing", "smithing") },
                { "craft", TokenSkillMapping.ForFallback(1.2f, "crafting", "crafting") },
                { "crafting", TokenSkillMapping.ForFallback(1.4f, "crafting", "crafting") },
                { "fletch", TokenSkillMapping.ForFallback(1.2f, "fletching", "fletching") },
                { "magic", TokenSkillMapping.ForSkills(1.1f, SkillType.Magic) },
                { "spell", TokenSkillMapping.ForSkills(0.9f, SkillType.Magic) },
                { "rune", TokenSkillMapping.ForSkills(0.8f, SkillType.Magic) },
                { "cast", TokenSkillMapping.ForSkills(0.9f, SkillType.Magic) },
                { "mage", TokenSkillMapping.ForSkills(1.1f, SkillType.Magic) },
                { "wizard", TokenSkillMapping.ForSkills(0.9f, SkillType.Magic) },
                { "wiz", TokenSkillMapping.ForSkills(0.9f, SkillType.Magic) },
                { "sorc", TokenSkillMapping.ForSkills(0.8f, SkillType.Magic) },
                { "range", TokenSkillMapping.ForSkills(0.8f, SkillType.Ranged) },
                { "ranged", TokenSkillMapping.ForSkills(0.9f, SkillType.Ranged) },
                { "bow", TokenSkillMapping.ForSkills(0.7f, SkillType.Ranged) },
                { "bows", TokenSkillMapping.ForSkills(0.7f, SkillType.Ranged) },
                { "arrow", TokenSkillMapping.ForSkills(0.7f, SkillType.Ranged) },
                { "arrows", TokenSkillMapping.ForSkills(0.7f, SkillType.Ranged) },
                { "rng", TokenSkillMapping.ForSkills(1.1f, SkillType.Ranged) },
                { "archer", TokenSkillMapping.ForSkills(1f, SkillType.Ranged) },
                { "archery", TokenSkillMapping.ForSkills(1f, SkillType.Ranged) },
                { "attack", TokenSkillMapping.ForSkills(0.7f, SkillType.Attack) },
                { "atk", TokenSkillMapping.ForSkills(1.2f, SkillType.Attack) },
                { "att", TokenSkillMapping.ForSkills(1f, SkillType.Attack) },
                { "melee", TokenSkillMapping.ForSkills(0.9f, SkillType.Attack) },
                { "strength", TokenSkillMapping.ForSkills(0.7f, SkillType.Strength) },
                { "str", TokenSkillMapping.ForSkills(1.2f, SkillType.Strength) },
                { "strenght", TokenSkillMapping.ForSkills(0.9f, SkillType.Strength) },
                { "muscle", TokenSkillMapping.ForSkills(0.8f, SkillType.Strength) },
                { "power", TokenSkillMapping.ForSkills(0.7f, SkillType.Strength) },
                { "defence", TokenSkillMapping.ForSkills(0.7f, SkillType.Defence) },
                { "defense", TokenSkillMapping.ForSkills(0.7f, SkillType.Defence) },
                { "def", TokenSkillMapping.ForSkills(1.2f, SkillType.Defence) },
                { "tank", TokenSkillMapping.ForSkills(0.8f, SkillType.Defence) },
                { "armor", TokenSkillMapping.ForSkills(0.8f, SkillType.Defence) },
                { "armour", TokenSkillMapping.ForSkills(0.8f, SkillType.Defence) },
                { "hp", TokenSkillMapping.ForSkills(1.4f, SkillType.Hitpoints) },
                { "hitpoint", TokenSkillMapping.ForSkills(1.3f, SkillType.Hitpoints) },
                { "hitpoints", TokenSkillMapping.ForSkills(1.4f, SkillType.Hitpoints) },
                { "health", TokenSkillMapping.ForSkills(1.2f, SkillType.Hitpoints) },
                { "heals", TokenSkillMapping.ForSkills(0.8f, SkillType.Hitpoints) },
                { "heart", TokenSkillMapping.ForSkills(0.8f, SkillType.Hitpoints) },
                { "hearts", TokenSkillMapping.ForSkills(0.8f, SkillType.Hitpoints) },
                { "life", TokenSkillMapping.ForSkills(0.7f, SkillType.Hitpoints) },
                { "lifepoint", TokenSkillMapping.ForSkills(1.1f, SkillType.Hitpoints) },
                { "lifepoints", TokenSkillMapping.ForSkills(1.2f, SkillType.Hitpoints) },
                { "vitality", TokenSkillMapping.ForSkills(0.9f, SkillType.Hitpoints) },
                { "beast", TokenSkillMapping.ForSkills(1.2f, SkillType.Beastmaster) },
                { "beasts", TokenSkillMapping.ForSkills(1.2f, SkillType.Beastmaster) },
                { "beastmaster", TokenSkillMapping.ForSkills(1.4f, SkillType.Beastmaster) },
                { "beastmastery", TokenSkillMapping.ForSkills(1.2f, SkillType.Beastmaster) },
                { "pet", TokenSkillMapping.ForSkills(1.1f, SkillType.Beastmaster) },
                { "pets", TokenSkillMapping.ForSkills(1.1f, SkillType.Beastmaster) },
                { "handler", TokenSkillMapping.ForSkills(0.9f, SkillType.Beastmaster) }
            };

        private readonly Queue<PendingResponse> pendingResponses = new Queue<PendingResponse>();
        private CompanionDialogueParser parser;
        private Coroutine responseRoutine;
        private Coroutine chatSubscriptionRoutine;
        private string lastCompanionMoodDescriptor = string.Empty;
        private CombatController playerCombatController;
        private SkillManager playerSkillManager;
        private bool playerInCombat;
        private Coroutine playerBindingRoutine;
        private readonly LinkedList<SkillActionRecord> recentSkillActions = new LinkedList<SkillActionRecord>();
        private string lastStatusTemplateKey = string.Empty;
        private string lastPlayerMessage = string.Empty;
        private DateTime? lastPlayerMessageUtc;
        private string lastProactiveQuestion = string.Empty;
        private DateTime? lastProactiveQuestionUtc;
        private string lastProactiveQuestionTemplateKey = string.Empty;
        private ActiveSkillQuestion activeSkillQuestion = ActiveSkillQuestion.Empty;

        /// <summary>
        /// Indicates whether the service is currently waiting for the player to respond to a
        /// proactive skill plan question posed by the companion.
        /// </summary>
        public static bool IsAwaitingSkillPlanResponse
        {
            get
            {
                var instance = Instance;
                if (instance == null)
                    return false;

                return instance.activeSkillQuestion.IsActive;
            }
        }
        private readonly LinkedList<SkillQuestionCandidate> skillQuestionCandidates = new LinkedList<SkillQuestionCandidate>();
        private int idleTickCounter;
        private int smallTalkIdleTicks;
        private DateTime? lastSmallTalkQueuedUtc;
        private string lastSmallTalkTemplateKey = string.Empty;
        private bool tickerSubscribed;
        private Coroutine tickerSubscriptionRoutine;
        private DateTime? lastCompanionCombatUtc;
        private Dictionary<string, ItemData> pickaxeItemCache;

        private const int MaxTrackedSkillActions = 6;
        private const int MaxSkillQuestionCandidates = 6;
        /// <summary>Storage key mirrored from <see cref="CompanionSkillCooldownTimers"/> for combat decline timers.</summary>
        private const SkillType CombatDeclineCooldownStorageKey = SkillType.Hitpoints;
        private static readonly TimeSpan SkillActionRetention = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan CompanionCombatActivityWindow = TimeSpan.FromSeconds(5);
        private const float SkillProposalFollowUpChance = 0.65f;
        private const int MaxTrackedNpcKills = 16;

        private static readonly SkillType[] SuggestibleSkills =
        {
            SkillType.Mining,
            SkillType.Cooking,
            SkillType.Firemaking,
            SkillType.Woodcutting,
            SkillType.Beastmaster,
            SkillType.Hitpoints,
            SkillType.Attack,
            SkillType.Strength,
            SkillType.Ranged,
            SkillType.Defence,
            SkillType.Magic,
            SkillType.Fishing
        };

        /// <summary>
        /// Probability that the companion politely declines a player-initiated training request even when prepared.
        /// </summary>
        private const float SkillProposalDeclineChance = 0.15f;

        /// <summary>
        /// Probability that the companion follows a decline with an alternate skill suggestion.
        /// </summary>
        private const float SkillProposalDeclineSuggestionChance = 0.5f;

        /// <summary>
        /// Reduced probability used for the generic ready/missing tool follow-up pools so they only
        /// trigger roughly thirty percent of the time, keeping those beats feeling occasional.
        /// </summary>
        private const float SkillProposalLightFollowUpChance = 0.3f;

        private bool ResponseRoutineActive => responseRoutine != null;

        private bool ShouldTraceRules => CompanionManager.EnableDebugLogging && enableRuleTracing;

        private bool ShouldTraceResponses => CompanionManager.EnableDebugLogging && enableResponseTracing;

        private bool ShouldTraceMemory => CompanionManager.EnableDebugLogging && enableMemoryTracing;

        /// <summary>
        /// Indicates whether the companion has provided a suggestion that is still under its cooldown window.
        /// Exposed for debug tooling so QA can verify the lockout without digging through logs.
        /// </summary>
        public static bool CompanionHasAnsweredSuggestionQuestion =>
            Instance != null && Instance.HasActiveSuggestion(DateTime.UtcNow);

        /// <summary>
        /// Tracks whether the player repeated the suggestion request during the active cooldown window.
        /// Used by debug menus to highlight when reminder phrases should work.
        /// </summary>
        public static bool PlayerHasAskedCompanionSuggestionQuestionAgain =>
            Instance != null && Instance.playerRepeatedSuggestionRequest;

        /// <summary>
        /// Retrieves the live suggestion debug state so tooling can display cooldown and history information.
        /// </summary>
        public static SuggestionDebugState GetSuggestionDebugState()
        {
            var instance = Instance;
            return instance != null
                ? instance.BuildSuggestionDebugState(DateTime.UtcNow)
                : SuggestionDebugState.Empty;
        }

        /// <summary>
        /// Records an NPC kill so the companion can surface it in later activity suggestions.
        /// The killing player is currently unused but kept for parity with other combat hooks.
        /// </summary>
        public static void RegisterNpcKill(NpcCombatant npc, GameObject killingPlayer)
        {
            if (npc == null)
                return;

            var instance = Instance;
            instance?.HandleNpcKill(npc);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateSingleton);
        }

        private static CompanionConversationService CreateSingleton()
        {
            var go = new GameObject(nameof(CompanionConversationService));
            return go.AddComponent<CompanionConversationService>();
        }

        /// <summary>
        /// Allows gameplay systems to register recent events that the companion can mention in dialogue.
        /// </summary>
        public static void RegisterEvent(string summary, CompanionEventType eventType, CompanionEventMetadata? metadata = null)
        {
            var instance = Instance;
            if (instance == null)
                return;

            instance.RegisterEventInternal(summary, eventType, metadata);
        }

        /// <summary>
        /// Forces the companion to ask a proactive question immediately, bypassing normal cooldowns.
        /// Primarily intended for developer testing workflows triggered through privileged commands.
        /// </summary>
        /// <param name="overrideDescription">Optional context that should seed the generated question.</param>
        /// <param name="failureReason">Human-readable reason describing why the request failed.</param>
        /// <returns>True when a question was successfully queued for delivery.</returns>
        public bool TryForceDeveloperQuestion(string overrideDescription, out string failureReason)
        {
            failureReason = string.Empty;

            if (!isActiveAndEnabled)
            {
                // The service must be active in the scene before it can enqueue dialogue.
                failureReason = "Companion conversation service is not active.";
                return false;
            }

            if (!CompanionManager.HasActiveCompanion)
            {
                // There is no follower available to deliver the prompted question.
                failureReason = "You must have a companion summoned before prompting them.";
                return false;
            }

            var chat = ChatService.Instance;
            if (chat == null)
            {
                // Without a chat service the line would never render, so short-circuit early.
                failureReason = "Chat service is not initialised yet.";
                return false;
            }

            DateTime nowUtc = DateTime.UtcNow;
            SkillQuestionCandidate candidate;

            if (!string.IsNullOrWhiteSpace(overrideDescription))
            {
                // Developers can supply ad-hoc context that should influence the generated question.
                candidate = SkillQuestionCandidate.CreateFromDescription(overrideDescription.Trim(), nowUtc);
            }
            else if (!TryGetBestSkillCandidate(nowUtc, out candidate))
            {
                // Fall back to recent skill events so the question still feels grounded in gameplay.
                if (!TryBuildFallbackSkillCandidate(nowUtc, out candidate))
                    candidate = SkillQuestionCandidate.Empty;
            }

            // Clear any lingering active prompt so the forced question becomes the current focus.
            ClearActiveSkillQuestion();

            int previousCount = pendingResponses.Count;
            ScheduleSkillQuestion(candidate, nowUtc);

            if (pendingResponses.Count > previousCount)
                return true;

            // If the queue count never changed the dialogue library could not produce a template.
            failureReason = "No suitable question template was available.";
            return false;
        }

        /// <inheritdoc />
        protected override void OnSingletonAwake()
        {
            base.OnSingletonAwake();

            responseLibrary ??= new CompanionDialogueResponseLibrary();
            responseLibrary.EnsureDefaults();

            BuildRuleProfile();
            parser = new CompanionDialogueParser(rules);

            EnsureConversationMemoryBound();

            EnsureChatSubscription();
            EnsurePlayerContextBindings();
            SubscribeToTicker();

            SceneTransitionManager.TransitionCompleted -= HandleSceneTransitionCompleted;
            SceneTransitionManager.TransitionCompleted += HandleSceneTransitionCompleted;
        }

        /// <summary>
        /// Ensures <see cref="conversationMemory"/> references the live memory component
        /// so responses can log mood and status updates immediately after bootstrap.
        /// </summary>
        private void EnsureConversationMemoryBound()
        {
            if (conversationMemory != null)
                return;

            conversationMemory = FindObjectOfType<CompanionConversationMemory>(true);
            if (conversationMemory != null && ShouldTraceMemory)
                Debug.Log("[CompanionConversationService] Rebound conversation memory instance after bootstrap.");
        }

        /// <summary>
        /// Forwards an event registration to the conversation memory when it is available.
        /// </summary>
        private void RegisterEventInternal(string summary, CompanionEventType eventType, CompanionEventMetadata? metadata)
        {
            EnsureConversationMemoryBound();
            conversationMemory?.RegisterEvent(summary, eventType, metadata);

            if ((eventType == CompanionEventType.Gathering || eventType == CompanionEventType.Crafting) && metadata.HasValue)
                RegisterSkillEventCandidate(summary, metadata.Value);

            if (eventType == CompanionEventType.Combat && metadata.HasValue)
                TrackCompanionCombat(metadata.Value);
        }

        /// <inheritdoc />
        protected override void OnSingletonDestroyed()
        {
            UnsubscribeFromChat();
            UnsubscribeFromTicker();

            SceneTransitionManager.TransitionCompleted -= HandleSceneTransitionCompleted;
            UnbindPlayerContext();

            if (chatSubscriptionRoutine != null)
                StopCoroutine(chatSubscriptionRoutine);

            if (responseRoutine != null)
                StopCoroutine(responseRoutine);

            pendingResponses.Clear();
            skillQuestionCandidates.Clear();
            activeSkillQuestion = ActiveSkillQuestion.Empty;
            lastPlayerMessage = string.Empty;
            lastPlayerMessageUtc = null;
            lastProactiveQuestion = string.Empty;
            lastProactiveQuestionUtc = null;
            lastProactiveQuestionTemplateKey = string.Empty;
            idleTickCounter = 0;
            smallTalkIdleTicks = 0;
            lastSmallTalkQueuedUtc = null;
            lastSmallTalkTemplateKey = string.Empty;
            tickerSubscriptionRoutine = null;
            tickerSubscribed = false;
            lastCompanionCombatUtc = null;
            ResetSuggestionState();
            recentNpcKills.Clear();

            base.OnSingletonDestroyed();
        }

        private void EnsureChatSubscription()
        {
            var chat = ChatService.Instance;
            if (chat != null)
            {
                chat.MessageReceived -= HandleMessageReceived;
                chat.MessageReceived += HandleMessageReceived;
                return;
            }

            if (chatSubscriptionRoutine == null)
                chatSubscriptionRoutine = StartCoroutine(WaitForChatService());
        }

        private IEnumerator WaitForChatService()
        {
            while (ChatService.Instance == null)
                yield return null;

            chatSubscriptionRoutine = null;
            EnsureChatSubscription();
        }

        private void UnsubscribeFromChat()
        {
            var chat = ChatService.Instance;
            if (chat != null)
                chat.MessageReceived -= HandleMessageReceived;
        }

        private void SubscribeToTicker()
        {
            if (Ticker.Instance != null)
            {
                Ticker.Instance.Subscribe(this);
                tickerSubscribed = true;
                return;
            }

            if (tickerSubscriptionRoutine == null)
                tickerSubscriptionRoutine = StartCoroutine(WaitForTicker());
        }

        private IEnumerator WaitForTicker()
        {
            while (Ticker.Instance == null)
                yield return null;

            tickerSubscriptionRoutine = null;
            SubscribeToTicker();
        }

        private void UnsubscribeFromTicker()
        {
            if (Ticker.Instance != null && tickerSubscribed)
                Ticker.Instance.Unsubscribe(this);

            tickerSubscribed = false;

            if (tickerSubscriptionRoutine != null)
            {
                StopCoroutine(tickerSubscriptionRoutine);
                tickerSubscriptionRoutine = null;
            }
        }

        private void EnsurePlayerContextBindings()
        {
            if (!isActiveAndEnabled)
                return;

            if (TryBindToPlayer())
            {
                if (playerBindingRoutine != null)
                {
                    StopCoroutine(playerBindingRoutine);
                    playerBindingRoutine = null;
                }

                return;
            }

            if (playerBindingRoutine == null)
                playerBindingRoutine = StartCoroutine(WaitForPlayerBinding());
        }

        private bool TryBindToPlayer()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                return false;

            var combat = player.GetComponent<CombatController>() ?? player.GetComponentInChildren<CombatController>();
            BindCombatController(combat);

            var skills = player.GetComponent<SkillManager>() ?? player.GetComponentInChildren<SkillManager>();
            BindSkillManager(skills);

            return playerCombatController != null || playerSkillManager != null;
        }

        private IEnumerator WaitForPlayerBinding()
        {
            while (!TryBindToPlayer())
                yield return null;

            playerBindingRoutine = null;
        }

        private void BindCombatController(CombatController controller)
        {
            if (playerCombatController == controller)
                return;

            if (playerCombatController != null)
            {
                playerCombatController.OnCombatTargetChanged -= HandlePlayerCombatTargetChanged;
                playerCombatController.OnAttackStart -= HandlePlayerAttackStart;
            }

            playerCombatController = controller;
            playerInCombat = false;

            if (playerCombatController != null)
            {
                playerCombatController.OnCombatTargetChanged += HandlePlayerCombatTargetChanged;
                playerCombatController.OnAttackStart += HandlePlayerAttackStart;
            }
        }

        private void BindSkillManager(SkillManager skills)
        {
            if (playerSkillManager == skills)
                return;

            if (playerSkillManager != null)
                playerSkillManager.LevelChanged -= HandlePlayerSkillLevelChanged;

            playerSkillManager = skills;

            if (playerSkillManager != null)
                playerSkillManager.LevelChanged += HandlePlayerSkillLevelChanged;
        }

        private void UnbindPlayerContext()
        {
            if (playerBindingRoutine != null)
            {
                StopCoroutine(playerBindingRoutine);
                playerBindingRoutine = null;
            }

            if (playerCombatController != null)
            {
                playerCombatController.OnCombatTargetChanged -= HandlePlayerCombatTargetChanged;
                playerCombatController.OnAttackStart -= HandlePlayerAttackStart;
                playerCombatController = null;
            }

            if (playerSkillManager != null)
            {
                playerSkillManager.LevelChanged -= HandlePlayerSkillLevelChanged;
                playerSkillManager = null;
            }

            playerInCombat = false;
        }

        private void HandleSceneTransitionCompleted()
        {
            EnsurePlayerContextBindings();
        }

        private void HandlePlayerCombatTargetChanged(CombatTarget target)
        {
            playerInCombat = target != null;
        }

        private void HandlePlayerAttackStart()
        {
            playerInCombat = true;
        }

        private void HandlePlayerSkillLevelChanged(SkillType skill, int level)
        {
            string skillName = SkillNameUtility.GetDisplayName(skill);
            RecordSkillAction($"Reached level {level} in {skillName}");
        }

        /// <inheritdoc />
        public void OnTick()
        {
            DateTime nowUtc = DateTime.UtcNow;
            EnsureSuggestionStateFresh(nowUtc);
            PruneNpcKills(nowUtc);
            PruneSkillActions(nowUtc);
            ExpireActiveSkillQuestionIfStale(nowUtc);
            MaybeScheduleProactiveQuestion(nowUtc);
            MaybeScheduleSmallTalk(nowUtc);
        }

        private void RecordSkillAction(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return;

            var record = new SkillActionRecord(description.Trim(), DateTime.UtcNow);
            recentSkillActions.AddFirst(record);

            while (recentSkillActions.Count > MaxTrackedSkillActions)
                recentSkillActions.RemoveLast();

            DateTime nowUtc = DateTime.UtcNow;
            PruneSkillActions(nowUtc);
            PruneSkillQuestionCandidates(nowUtc);
        }

        private void PruneSkillActions(DateTime nowUtc)
        {
            var node = recentSkillActions.Last;
            while (node != null)
            {
                var previous = node.Previous;
                if ((nowUtc - node.Value.TimestampUtc) > SkillActionRetention)
                    recentSkillActions.Remove(node);

                node = previous;
            }

            PruneSkillQuestionCandidates(nowUtc);
        }

        private void PruneSkillQuestionCandidates(DateTime nowUtc)
        {
            TimeSpan freshnessWindow = TimeSpan.FromMinutes(Mathf.Max(0.1f, minimumSkillEventFreshnessMinutes));
            var node = skillQuestionCandidates.Last;
            while (node != null)
            {
                var previous = node.Previous;
                if ((nowUtc - node.Value.TimestampUtc) > freshnessWindow)
                    skillQuestionCandidates.Remove(node);

                node = previous;
            }

            while (skillQuestionCandidates.Count > MaxSkillQuestionCandidates)
                skillQuestionCandidates.RemoveLast();
        }

        private void RegisterSkillEventCandidate(string summary, CompanionEventMetadata metadata)
        {
            if (!metadata.Skill.HasValue)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            string skillName = SkillNameUtility.GetDisplayName(metadata.Skill.Value);
            string description = BuildSkillActionDescription(summary, metadata, skillName);
            if (!string.IsNullOrWhiteSpace(description))
                RecordSkillAction(description);

            var candidate = SkillQuestionCandidate.CreateForSkill(metadata.Skill.Value, skillName, description, nowUtc);

            for (var node = skillQuestionCandidates.First; node != null; node = node.Next)
            {
                if (node.Value.Skill.HasValue && node.Value.Skill.Value == candidate.Skill &&
                    string.Equals(node.Value.Description, candidate.Description, StringComparison.OrdinalIgnoreCase))
                {
                    skillQuestionCandidates.Remove(node);
                    break;
                }
            }

            skillQuestionCandidates.AddFirst(candidate);
            PruneSkillQuestionCandidates(nowUtc);
        }

        private static string BuildSkillActionDescription(string summary, CompanionEventMetadata metadata, string skillName)
        {
            string context = !string.IsNullOrWhiteSpace(metadata.AdditionalContext)
                ? metadata.AdditionalContext.Trim()
                : summary;

            if (string.IsNullOrWhiteSpace(context))
                context = $"Trained {skillName}";
            else if (!context.Contains(skillName, StringComparison.OrdinalIgnoreCase))
                context = $"{skillName}: {context}";

            return context.Trim();
        }

        private void TrackCompanionCombat(CompanionEventMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.PrimaryActor))
                return;

            string companionName = CompanionManager.GetCompanionDisplayName();
            if (string.IsNullOrWhiteSpace(companionName))
                companionName = "Companion";

            if (string.Equals(metadata.PrimaryActor.Trim(), companionName, StringComparison.OrdinalIgnoreCase))
                lastCompanionCombatUtc = DateTime.UtcNow;
        }

        private void MaybeScheduleProactiveQuestion(DateTime nowUtc)
        {
            if (!isActiveAndEnabled)
                return;

            if (!CompanionManager.HasActiveCompanion)
            {
                idleTickCounter = 0;
                smallTalkIdleTicks = 0;
                return;
            }

            if (ResponseRoutineActive || pendingResponses.Count > 0)
            {
                idleTickCounter = 0;
                smallTalkIdleTicks = 0;
                return;
            }

            if (playerInCombat || IsCompanionInCombat(nowUtc))
            {
                idleTickCounter = 0;
                smallTalkIdleTicks = 0;
                return;
            }

            if (activeSkillQuestion.IsActive)
            {
                idleTickCounter = 0;
                smallTalkIdleTicks = 0;
                return;
            }

            if (conversationMemory != null && conversationMemory.LastQuestionUtc.HasValue)
            {
                double minutesSinceQuestion = (nowUtc - conversationMemory.LastQuestionUtc.Value).TotalMinutes;
                if (minutesSinceQuestion < Mathf.Max(0.1f, proactiveQuestionCooldownMinutes))
                {
                    idleTickCounter = 0;
                    smallTalkIdleTicks = 0;
                    return;
                }
            }

            if (lastProactiveQuestionUtc.HasValue)
            {
                double minutesSincePrompt = (nowUtc - lastProactiveQuestionUtc.Value).TotalMinutes;
                if (minutesSincePrompt < Mathf.Max(0.1f, proactiveQuestionCooldownMinutes))
                {
                    idleTickCounter = 0;
                    smallTalkIdleTicks = 0;
                    return;
                }
            }

            idleTickCounter++;
            if (idleTickCounter < Mathf.Max(1, proactiveIdleTickThreshold))
                return;

            bool blockedByCooldown;
            if (!TryGetBestSkillCandidate(nowUtc, out var candidate, (SkillType?)null, out blockedByCooldown))
            {
                if (blockedByCooldown)
                    return;

                if (!TryBuildFallbackSkillCandidate(nowUtc, out candidate))
                    return;
            }

            ScheduleSkillQuestion(candidate, nowUtc);
            idleTickCounter = 0;
            smallTalkIdleTicks = 0;
        }

        private void MaybeScheduleSmallTalk(DateTime nowUtc)
        {
            if (!isActiveAndEnabled)
                return;

            if (!CompanionManager.HasActiveCompanion)
            {
                idleTickCounter = 0;
                smallTalkIdleTicks = 0;
                return;
            }

            if (ResponseRoutineActive || pendingResponses.Count > 0)
            {
                smallTalkIdleTicks = 0;
                return;
            }

            if (playerInCombat || IsCompanionInCombat(nowUtc))
            {
                smallTalkIdleTicks = 0;
                return;
            }

            if (activeSkillQuestion.IsActive)
            {
                smallTalkIdleTicks = 0;
                return;
            }

            if (skillQuestionCandidates.Count > 0)
            {
                smallTalkIdleTicks = 0;
                return;
            }

            EnsureConversationMemoryBound();
            if (conversationMemory != null && conversationMemory.LastSmallTalkUtc.HasValue)
            {
                double minutesSinceLast = (nowUtc - conversationMemory.LastSmallTalkUtc.Value).TotalMinutes;
                if (minutesSinceLast < Mathf.Max(0.1f, smallTalkCooldownMinutes))
                {
                    smallTalkIdleTicks = 0;
                    return;
                }
            }

            if (lastSmallTalkQueuedUtc.HasValue)
            {
                double minutesSinceQueued = (nowUtc - lastSmallTalkQueuedUtc.Value).TotalMinutes;
                if (minutesSinceQueued < Mathf.Max(0.1f, smallTalkCooldownMinutes * 0.5f))
                {
                    smallTalkIdleTicks = 0;
                    return;
                }
            }

            smallTalkIdleTicks++;
            if (smallTalkIdleTicks < Mathf.Max(1, smallTalkIdleTickThreshold))
                return;

            if (!TryComposeSmallTalk(nowUtc, out var pending))
            {
                smallTalkIdleTicks = 0;
                return;
            }

            pendingResponses.Enqueue(pending);
            if (!ResponseRoutineActive)
                responseRoutine = StartCoroutine(DrainResponseQueue());

            smallTalkIdleTicks = 0;
            lastSmallTalkQueuedUtc = nowUtc;
        }

        private bool TryComposeSmallTalk(DateTime nowUtc, out PendingResponse response)
        {
            response = default;

            var context = BuildResponseContext();
            string playerName = ResolvePlayerName(string.Empty);
            string companionMood = ResolveCompanionMoodDescriptor();
            string recentEvent = ResolveRecentEventSummary();

            var options = new List<CompanionSmallTalkDialogueBlocks.SmallTalkEntry>(
                CompanionSmallTalkDialogueBlocks.TimeOfDayEntries.Count +
                CompanionSmallTalkDialogueBlocks.LocationEntries.Count +
                CompanionSmallTalkDialogueBlocks.MemoryEntries.Count);

            options.AddRange(CompanionSmallTalkDialogueBlocks.TimeOfDayEntries);

            if (context.HasAmbientLocation)
                options.AddRange(CompanionSmallTalkDialogueBlocks.LocationEntries);

            if (context.HasMemorySummary)
                options.AddRange(CompanionSmallTalkDialogueBlocks.MemoryEntries);

            if (options.Count == 0)
                return false;

            if (!string.IsNullOrWhiteSpace(lastSmallTalkTemplateKey))
                options.RemoveAll(entry => string.Equals(entry.Template, lastSmallTalkTemplateKey, StringComparison.OrdinalIgnoreCase));

            int attempts = 0;
            while (options.Count > 0 && attempts < 4)
            {
                attempts++;
                var selection = ChooseSmallTalkEntry(options);
                string eventForTemplate = selection.Category == CompanionSmallTalkDialogueBlocks.SmallTalkCategory.Memory && context.HasMemorySummary
                    ? context.MemorySummary
                    : recentEvent;

                string formatted = FormatTemplate(
                    selection.Template,
                    playerName,
                    companionMood,
                    eventForTemplate,
                    context);

                formatted = CompactWhitespace(formatted);
                if (string.IsNullOrWhiteSpace(formatted))
                {
                    options.Remove(selection);
                    continue;
                }

                if (conversationMemory != null && conversationMemory.LastSmallTalkUtc.HasValue)
                {
                    double minutesSince = (nowUtc - conversationMemory.LastSmallTalkUtc.Value).TotalMinutes;
                    if (minutesSince < Mathf.Max(0.1f, smallTalkCooldownMinutes * 1.5f) &&
                        !string.IsNullOrWhiteSpace(conversationMemory.LastSmallTalkResponse) &&
                        string.Equals(formatted, conversationMemory.LastSmallTalkResponse, StringComparison.OrdinalIgnoreCase))
                    {
                        options.Remove(selection);
                        continue;
                    }
                }

                response = new PendingResponse(formatted, string.Empty, intent: CompanionDialogueIntent.SmallTalk);
                lastSmallTalkTemplateKey = selection.Template;
                return true;
            }

            response = default;
            return false;
        }

        private static CompanionSmallTalkDialogueBlocks.SmallTalkEntry ChooseSmallTalkEntry(IReadOnlyList<CompanionSmallTalkDialogueBlocks.SmallTalkEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return default;

            if (entries.Count == 1)
                return entries[0];

            float totalWeight = 0f;
            for (int i = 0; i < entries.Count; i++)
                totalWeight += Mathf.Max(0.0001f, entries[i].Weight);

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                float weight = Mathf.Max(0.0001f, entries[i].Weight);
                cumulative += weight;
                if (roll <= cumulative)
                    return entries[i];
            }

            return entries[entries.Count - 1];
        }

        private bool TryGetBestSkillCandidate(DateTime nowUtc, out SkillQuestionCandidate candidate, SkillType? excludeSkill = null)
        {
            return TryGetBestSkillCandidate(nowUtc, out candidate, excludeSkill, out _);
        }

        /// <summary>
        /// Attempts to retrieve the freshest skill candidate, reporting whether any entries were skipped due to cooldowns.
        /// </summary>
        private bool TryGetBestSkillCandidate(
            DateTime nowUtc,
            out SkillQuestionCandidate candidate,
            SkillType? excludeSkill,
            out bool blockedByCooldown)
        {
            PruneSkillQuestionCandidates(nowUtc);

            blockedByCooldown = false;
            var node = skillQuestionCandidates.First;
            TimeSpan freshnessWindow = TimeSpan.FromMinutes(Mathf.Max(0.1f, minimumSkillEventFreshnessMinutes));
            while (node != null)
            {
                var next = node.Next;
                SkillQuestionCandidate value = node.Value;
                if (excludeSkill.HasValue && value.Skill.HasValue && value.Skill.Value == excludeSkill.Value)
                {
                    node = next;
                    continue;
                }

                if (value.Skill.HasValue && IsSkillUnderDeclineCooldown(value.Skill.Value))
                {
                    blockedByCooldown = true;
                    node = next;
                    continue;
                }

                if ((nowUtc - value.TimestampUtc) <= freshnessWindow)
                {
                    skillQuestionCandidates.Remove(node);
                    candidate = value;
                    return true;
                }

                node = next;
            }

            candidate = default;
            return false;
        }

        private bool TryBuildFallbackSkillCandidate(DateTime nowUtc, out SkillQuestionCandidate candidate)
        {
            var node = recentSkillActions.First;
            TimeSpan freshnessWindow = TimeSpan.FromMinutes(Mathf.Max(0.1f, minimumSkillEventFreshnessMinutes));
            while (node != null)
            {
                var record = node.Value;
                if ((nowUtc - record.TimestampUtc) <= freshnessWindow)
                {
                    candidate = SkillQuestionCandidate.CreateFromDescription(record.Description, record.TimestampUtc);
                    return true;
                }

                node = node.Next;
            }

            candidate = default;
            return false;
        }

        /// <summary>
        /// Determines whether the supplied skill is currently throttled by a decline cooldown.
        /// </summary>
        private static bool IsSkillUnderDeclineCooldown(SkillType skill)
        {
            var tracker = CompanionManager.CompanionSkillCooldowns;
            if (tracker == null)
                return false;

            if (skill == SkillType.Mining)
            {
                return tracker.TryGetRemaining(SkillType.Mining, out var remaining) && remaining > TimeSpan.Zero;
            }

            if (skill == SkillType.Woodcutting)
            {
                return tracker.TryGetRemaining(SkillType.Woodcutting, out var remaining) && remaining > TimeSpan.Zero;
            }

            if (skill == SkillType.Fishing)
            {
                return tracker.TryGetRemaining(SkillType.Fishing, out var remaining) && remaining > TimeSpan.Zero;
            }

            if (!IsCombatSkill(skill))
                return false;

            return tracker.TryGetRemaining(CombatDeclineCooldownStorageKey, out var combatRemaining) &&
                   combatRemaining > TimeSpan.Zero;
        }

        private void ScheduleSkillQuestion(SkillQuestionCandidate candidate, DateTime nowUtc)
        {
            EnsureConversationMemoryBound();

            SkillQuestionCandidate? optionalCandidate = candidate.IsValid ? candidate : (SkillQuestionCandidate?)null;
            var context = BuildResponseContext(optionalCandidate);

            bool responseSelected = responseLibrary.TrySelectResponse(
                CompanionDialogueIntent.ProactiveSkillQuestion,
                context,
                lastProactiveQuestionTemplateKey,
                out var selection);

            if (!responseSelected)
            {
                if (string.IsNullOrEmpty(lastProactiveQuestionTemplateKey))
                    return;

                if (!responseLibrary.TrySelectResponse(
                        CompanionDialogueIntent.ProactiveSkillQuestion,
                        context,
                        string.Empty,
                        out selection))
                {
                    return;
                }
            }

            string playerName = ResolvePlayerName(string.Empty);
            string companionMood = ResolveCompanionMoodDescriptor();
            string formatted = FormatTemplate(
                selection.PrimarySegment,
                playerName,
                companionMood,
                string.Empty,
                context);

            formatted = EnsureQuestionMark(formatted);
            if (string.IsNullOrWhiteSpace(formatted))
                return;

            pendingResponses.Enqueue(new PendingResponse(formatted, string.Empty, intent: CompanionDialogueIntent.ProactiveSkillQuestion));
            if (!ResponseRoutineActive)
                responseRoutine = StartCoroutine(DrainResponseQueue());

            idleTickCounter = 0;
            smallTalkIdleTicks = 0;
            lastProactiveQuestion = formatted;
            lastProactiveQuestionUtc = nowUtc;
            lastProactiveQuestionTemplateKey = selection.TemplateKey;
            activeSkillQuestion = new ActiveSkillQuestion(candidate, selection.TemplateKey, nowUtc);
        }

        private static string EnsureQuestionMark(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            if (trimmed.EndsWith("?", StringComparison.Ordinal))
                return trimmed;

            trimmed = trimmed.TrimEnd('.', '!');
            return trimmed + "?";
        }

        private void ExpireActiveSkillQuestionIfStale(DateTime nowUtc)
        {
            if (!activeSkillQuestion.IsActive)
                return;

            TimeSpan cooldown = TimeSpan.FromMinutes(Mathf.Max(0.1f, proactiveQuestionCooldownMinutes));
            if (activeSkillQuestion.IsExpired(cooldown, nowUtc))
                ClearActiveSkillQuestion();
        }

        private void ClearActiveSkillQuestion()
        {
            activeSkillQuestion = ActiveSkillQuestion.Empty;
        }

        private bool IsCompanionInCombat(DateTime nowUtc)
        {
            if (!lastCompanionCombatUtc.HasValue)
                return false;

            return (nowUtc - lastCompanionCombatUtc.Value) <= CompanionCombatActivityWindow;
        }

        private static string DescribeSkillRecency(TimeSpan age)
        {
            if (age.TotalSeconds <= 30)
                return "just now";
            if (age.TotalMinutes < 2)
                return "a moment ago";
            if (age.TotalMinutes < 10)
                return $"about {Mathf.Max(1, Mathf.RoundToInt((float)age.TotalMinutes))} minutes ago";
            if (age.TotalMinutes < 60)
                return $"around {Mathf.Max(1, Mathf.RoundToInt((float)(age.TotalMinutes / 5f)) * 5)} minutes ago";

            int hours = Mathf.Max(1, Mathf.RoundToInt((float)age.TotalHours));
            return hours == 1 ? "about an hour ago" : $"around {hours} hours ago";
        }

        private void TryScheduleAlternateSkillQuestion(SkillType? excludeSkill)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (TryGetBestSkillCandidate(nowUtc, out var candidate, excludeSkill))
            {
                ScheduleSkillQuestion(candidate, nowUtc);
                return;
            }

            if (TryBuildFallbackSkillCandidate(nowUtc, out candidate))
                ScheduleSkillQuestion(candidate, nowUtc);
        }

        private void HandleMessageReceived(ChatMessage message)
        {
            if (message.Channel != ChatChannel.Companion)
                return;

            if (!message.IsLocalPlayerAuthor)
                return;

            EnsurePlayerContextBindings();
            EnsureSuggestionStateFresh(DateTime.UtcNow);

            lastPlayerMessage = message.Text ?? string.Empty;
            lastPlayerMessageUtc = DateTime.UtcNow;
            idleTickCounter = 0;
            smallTalkIdleTicks = 0;

            string cleaned = NormaliseForParsing(message.Text);
            if (string.IsNullOrEmpty(cleaned))
                return;

            var parseResult = parser.Parse(cleaned);
            parseResult = CompanionIntentDisambiguator.PruneContradictoryIntents(parseResult);
            if (parseResult.IsEmpty)
            {
                if (ShouldTraceRules)
                    Debug.Log($"[CompanionConversationService] No intents matched for '{cleaned}'.");
                return;
            }

            if (ShouldTraceRules)
                LogRuleMatches(cleaned, parseResult);

            string playerName = ResolvePlayerName(message.Sender);
            var response = ComposeResponse(parseResult, playerName);
            if (!response.HasValue || string.IsNullOrWhiteSpace(response.Value.Text))
                return;

            pendingResponses.Enqueue(response.Value);
            idleTickCounter = 0;
            smallTalkIdleTicks = 0;
            if (!ResponseRoutineActive)
                responseRoutine = StartCoroutine(DrainResponseQueue());
        }

        private IEnumerator DrainResponseQueue()
        {
            while (pendingResponses.Count > 0)
            {
                var response = pendingResponses.Dequeue();
                float delay = ResolveTypingDelay(response.Text);
                if (delay > 0f)
                    yield return new WaitForSeconds(delay);

                PublishResponse(response);

                if (pendingResponses.Count == 0 && response.FollowUpSegments != null && response.FollowUpSegments.Count > 0)
                {
                    for (int i = 0; i < response.FollowUpSegments.Count; i++)
                    {
                        string followUp = response.FollowUpSegments[i];
                        if (string.IsNullOrWhiteSpace(followUp))
                            continue;

                        pendingResponses.Enqueue(
                            new PendingResponse(followUp, string.Empty, intent: response.Intent));
                    }
                }

                // Small buffer so rapid-fire messages still feel paced.
                yield return null;
            }

            responseRoutine = null;
        }

        private void PublishResponse(PendingResponse response)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            idleTickCounter = 0;
            smallTalkIdleTicks = 0;
            string companionName = CompanionManager.GetCompanionDisplayName();
            chat.PublishCompanionMessage(companionName, response.Text);

            if (!string.IsNullOrWhiteSpace(response.StatusSegment) && conversationMemory != null)
            {
                conversationMemory.RegisterStatusResponse(response.StatusSegment, DateTime.UtcNow);
                if (ShouldTraceMemory)
                    Debug.Log($"[CompanionConversationService] Registered status response '{response.StatusSegment}'.");
            }

            if (response.Intent == CompanionDialogueIntent.SmallTalk && conversationMemory != null)
            {
                conversationMemory.RegisterSmallTalkResponse(response.Text, DateTime.UtcNow);
                if (ShouldTraceMemory)
                    Debug.Log($"[CompanionConversationService] Logged small talk '{response.Text}'.");
            }

            if (ShouldTraceResponses)
                Debug.Log($"[CompanionConversationService] Companion reply: {response.Text}");
        }

        private PendingResponse? ComposeResponse(CompanionDialogueParseResult parseResult, string playerName)
        {
            var segments = new List<string>();
            var followUps = new List<string>();

            if (conversationMemory == null)
                EnsureConversationMemoryBound();

            string statusSegment = string.Empty;
            string companionMood = ResolveCompanionMoodDescriptor();
            string recentEvent = ResolveRecentEventSummary();
            var context = BuildResponseContext(activeSkillQuestion.TryGetCandidate());
            bool skillResponseResolved = false;

            for (int i = 0; i < parseResult.Matches.Count; i++)
            {
                if (skillResponseResolved)
                    break;

                var match = parseResult.Matches[i];
                switch (match.Intent)
                {
                    case CompanionDialogueIntent.Greeting:
                        TryAddResponse(
                            CompanionDialogueIntent.Greeting,
                            context,
                            segments,
                            followUps,
                            playerName,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.SmallTalk:
                        TryAddResponse(
                            CompanionDialogueIntent.SmallTalk,
                            context,
                            segments,
                            followUps,
                            playerName,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.StatusQuery:
                        statusSegment = BuildStatusSegment(
                            playerName,
                            ref companionMood,
                            context,
                            followUps);
                        if (!string.IsNullOrEmpty(statusSegment))
                            segments.Add(statusSegment);
                        break;

                    case CompanionDialogueIntent.SkillLevelQuery:
                        if (TryHandleSkillLevelQuery(
                                parseResult,
                                context,
                                segments,
                                followUps,
                                playerName,
                                companionMood,
                                recentEvent))
                        {
                            statusSegment = string.Empty;
                            skillResponseResolved = true;
                        }
                        break;

                    case CompanionDialogueIntent.Gratitude:
                        TryAddResponse(
                            CompanionDialogueIntent.Gratitude,
                            context,
                            segments,
                            followUps,
                            playerName,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.Farewell:
                        TryAddResponse(
                            CompanionDialogueIntent.Farewell,
                            context,
                            segments,
                            followUps,
                            playerName,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.Compliment:
                        TryAddResponse(
                            CompanionDialogueIntent.Compliment,
                            context,
                            segments,
                            followUps,
                            playerName,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.AcceptSkillPlan:
                    case CompanionDialogueIntent.DeclineSkillPlan:
                    case CompanionDialogueIntent.DeferSkillPlan:
                    case CompanionDialogueIntent.RequestAlternateSkill:
                        {
                            int segmentCountBefore = segments.Count;
                            int followUpCountBefore = followUps.Count;
                            if (TryHandleSkillPlanIntent(
                                    match.Intent,
                                    context,
                                    segments,
                                    followUps,
                                    playerName,
                                    ref companionMood,
                                    recentEvent))
                            {
                                if (segmentCountBefore > 0)
                                    segments.RemoveRange(0, segmentCountBefore);
                                if (followUpCountBefore > 0)
                                    followUps.RemoveRange(0, followUpCountBefore);
                                statusSegment = string.Empty;
                                skillResponseResolved = true;
                            }
                        }
                        break;

                    case CompanionDialogueIntent.CompanionSuggestionRequest:
                    {
                        int segmentCountBefore = segments.Count;
                        if (TryHandleSuggestionRequest(context, segments, playerName))
                        {
                            if (segmentCountBefore > 0)
                                segments.RemoveRange(0, segmentCountBefore);
                            followUps.Clear();
                            statusSegment = string.Empty;
                            skillResponseResolved = true;
                        }
                        break;
                    }

                    case CompanionDialogueIntent.CompanionSuggestionReminder:
                    {
                        int segmentCountBefore = segments.Count;
                        if (TryHandleSuggestionReminder(segments, playerName))
                        {
                            if (segmentCountBefore > 0)
                                segments.RemoveRange(0, segmentCountBefore);
                            followUps.Clear();
                            statusSegment = string.Empty;
                            skillResponseResolved = true;
                        }
                        break;
                    }

                    case CompanionDialogueIntent.PlayerApology:
                        TryHandlePlayerApology(
                            context,
                            segments,
                            playerName,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.RequestAssistance:
                        TryAddResponse(
                            CompanionDialogueIntent.RequestAssistance,
                            context,
                            segments,
                            followUps,
                            playerName,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.PlayerSkillProposal:
                        {
                            int segmentCountBefore = segments.Count;
                            int followUpCountBefore = followUps.Count;
                            if (TryHandlePlayerSkillProposal(
                                    parseResult,
                                    context,
                                    segments,
                                    followUps,
                                    playerName,
                                    ref companionMood))
                            {
                                if (segmentCountBefore > 0)
                                    segments.RemoveRange(0, segmentCountBefore);
                                if (followUpCountBefore > 0)
                                    followUps.RemoveRange(0, followUpCountBefore);
                                statusSegment = string.Empty;
                                skillResponseResolved = true;
                            }
                        }
                        break;

                    case CompanionDialogueIntent.AcknowledgeRecentEvent:
                        if (!string.IsNullOrEmpty(recentEvent))
                        {
                            TryAddResponse(
                                CompanionDialogueIntent.AcknowledgeRecentEvent,
                                context,
                                segments,
                                followUps,
                                playerName,
                                companionMood,
                                recentEvent);
                        }
                        break;
                }

                if (skillResponseResolved)
                    break;
            }

            if (segments.Count == 0)
                return null;

            string text = CombineSegments(segments);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            IReadOnlyList<string> followUpPayload = followUps.Count > 0 ? followUps : null;
            return new PendingResponse(text, statusSegment, followUpPayload);
        }

        private void TryAddResponse(
            CompanionDialogueIntent intent,
            CompanionResponseContext context,
            List<string> segments,
            List<string> followUps,
            string playerName,
            string companionMood,
            string recentEvent)
        {
            if (!responseLibrary.TrySelectResponse(intent, context, null, out var selection))
                return;

            string formatted = FormatTemplate(
                selection.PrimarySegment,
                playerName,
                companionMood,
                recentEvent,
                context);

            if (string.IsNullOrWhiteSpace(formatted))
                return;

            segments.Add(formatted);
            AppendFollowUps(selection.FollowUpSegments, followUps, playerName, companionMood, recentEvent, context);
        }

        private void AppendFollowUps(
            IReadOnlyList<string> followUpSegments,
            List<string> collector,
            string playerName,
            string companionMood,
            string recentEvent,
            CompanionResponseContext context)
        {
            if (followUpSegments == null || collector == null || followUpSegments.Count == 0)
                return;

            for (int i = 0; i < followUpSegments.Count; i++)
            {
                string formatted = FormatTemplate(
                    followUpSegments[i],
                    playerName,
                    companionMood,
                    recentEvent,
                    context);

                if (!string.IsNullOrWhiteSpace(formatted))
                    collector.Add(formatted);
            }
        }

        private bool TryHandleSkillLevelQuery(
            CompanionDialogueParseResult parseResult,
            CompanionResponseContext context,
            List<string> segments,
            List<string> followUps,
            string playerName,
            string companionMood,
            string recentEvent)
        {
            if (parseResult.IsEmpty)
                return false;

            var skills = CompanionManager.CompanionSkills;
            var analysis = AnalyseSkillLevelQuery(parseResult, lastPlayerMessage, skills);
            var queryContext = context.WithSkillQuery(
                analysis.Skill,
                analysis.SkillDisplayName,
                analysis.SkillSentenceName,
                analysis.SkillLevel);

            if (!responseLibrary.TrySelectResponse(
                    CompanionDialogueIntent.SkillLevelQuery,
                    queryContext,
                    out var selection))
            {
                return false;
            }

            string primary = ApplySkillLevelTokens(
                selection.PrimarySegment,
                playerName,
                companionMood,
                recentEvent,
                queryContext,
                analysis);

            if (string.IsNullOrWhiteSpace(primary))
                return false;

            segments.Add(primary);

            AppendSkillLevelFollowUps(
                selection.FollowUpSegments,
                followUps,
                playerName,
                companionMood,
                recentEvent,
                queryContext,
                analysis);

            return true;
        }

        private void AppendSkillLevelFollowUps(
            IReadOnlyList<string> followUpSegments,
            List<string> collector,
            string playerName,
            string companionMood,
            string recentEvent,
            CompanionResponseContext context,
            SkillLevelQueryResult analysis)
        {
            if (followUpSegments == null || collector == null || followUpSegments.Count == 0)
                return;

            for (int i = 0; i < followUpSegments.Count; i++)
            {
                string formatted = ApplySkillLevelTokens(
                    followUpSegments[i],
                    playerName,
                    companionMood,
                    recentEvent,
                    context,
                    analysis);

                if (!string.IsNullOrWhiteSpace(formatted))
                    collector.Add(formatted);
            }
        }

        private bool TryHandlePlayerSkillProposal(
            CompanionDialogueParseResult parseResult,
            CompanionResponseContext context,
            List<string> segments,
            List<string> followUps,
            string playerName,
            ref string companionMood)
        {
            var analysis = AnalyseSkillProposal(parseResult, lastPlayerMessage);
            if (!analysis.HasProposal)
                return false;

            var toolResult = EvaluateToolAvailability(analysis);

            if (analysis.HasConcreteSkill)
            {
                if (UnityEngine.Random.value < SkillProposalDeclineChance)
                {
                    ComposeSkillProposalDeclineResponse(
                        analysis,
                        segments,
                        followUps,
                        playerName,
                        context);
                    return true;
                }

                if (toolResult.State == SkillToolState.Missing)
                {
                    ComposeMissingToolResponse(analysis, toolResult, segments, followUps, playerName);
                    return true;
                }

                ComposeReadySkillResponse(analysis, toolResult, segments, followUps, playerName, ref companionMood, context);
                return true;
            }

            ComposeAlternateSkillResponse(analysis, context, segments, followUps, playerName, toolResult);
            return true;
        }

        private SkillProposalAnalysis AnalyseSkillProposal(CompanionDialogueParseResult parseResult, string rawMessage)
        {
            if (parseResult.UniqueTokens == null || parseResult.UniqueTokens.Count == 0)
                return SkillProposalAnalysis.Empty;

            var weightBySkill = new Dictionary<SkillType, float>();
            SkillType? bestSkill = null;
            float bestScore = 0f;
            float fallbackScore = 0f;
            string fallbackName = string.Empty;
            string fallbackSentence = string.Empty;

            foreach (string token in parseResult.UniqueTokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                if (!SkillTokenMappings.TryGetValue(token, out var mapping))
                    continue;

                if (mapping.HasSkills)
                {
                    for (int i = 0; i < mapping.Skills.Length; i++)
                    {
                        SkillType skill = mapping.Skills[i];
                        float current = weightBySkill.TryGetValue(skill, out float existing) ? existing : 0f;
                        current += mapping.Weight;
                        weightBySkill[skill] = current;

                        if (current > bestScore)
                        {
                            bestScore = current;
                            bestSkill = skill;
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(mapping.FallbackName))
                {
                    if (mapping.Weight >= fallbackScore)
                    {
                        fallbackScore = mapping.Weight;
                        fallbackName = mapping.FallbackName;
                        fallbackSentence = mapping.FallbackSentence;
                    }
                }
            }

            string displayName = bestSkill.HasValue
                ? SkillNameUtility.GetDisplayName(bestSkill.Value)
                : fallbackName;

            string sentenceName = bestSkill.HasValue
                ? SkillNameUtility.GetSentenceName(bestSkill.Value)
                : (!string.IsNullOrWhiteSpace(fallbackSentence)
                    ? fallbackSentence
                    : (!string.IsNullOrWhiteSpace(displayName)
                        ? displayName.ToLowerInvariant()
                        : string.Empty));

            bool hasProposal = bestSkill.HasValue || !string.IsNullOrWhiteSpace(displayName);

            return new SkillProposalAnalysis(
                hasProposal,
                bestSkill,
                displayName,
                sentenceName,
                rawMessage,
                parseResult.Tokens);
        }

        private SkillLevelQueryResult AnalyseSkillLevelQuery(
            CompanionDialogueParseResult parseResult,
            string rawMessage,
            SkillManager companionSkills)
        {
            if (parseResult.UniqueTokens == null || parseResult.UniqueTokens.Count == 0)
                return SkillLevelQueryResult.Empty;

            var proposal = AnalyseSkillProposal(parseResult, rawMessage ?? string.Empty);
            if (!proposal.HasProposal)
                return SkillLevelQueryResult.Empty;

            SkillType? skill = proposal.HasConcreteSkill ? proposal.Skill : (SkillType?)null;
            string displayName = proposal.HasDisplayName
                ? proposal.SkillDisplayName
                : (skill.HasValue ? SkillNameUtility.GetDisplayName(skill.Value) : string.Empty);
            string sentenceName = !string.IsNullOrWhiteSpace(proposal.SkillSentenceName)
                ? proposal.SkillSentenceName
                : (skill.HasValue
                    ? SkillNameUtility.GetSentenceName(skill.Value)
                    : (!string.IsNullOrWhiteSpace(displayName) ? displayName.ToLowerInvariant() : string.Empty));

            int? level = null;
            if (companionSkills != null && skill.HasValue)
            {
                try
                {
                    level = Mathf.Max(0, companionSkills.GetLevel(skill.Value));
                }
                catch (Exception)
                {
                    level = null;
                }
            }

            return new SkillLevelQueryResult(skill, displayName, sentenceName, level);
        }

        private ToolAvailabilityResult EvaluateToolAvailability(SkillProposalAnalysis analysis)
        {
            if (!analysis.HasConcreteSkill)
                return ToolAvailabilityResult.AssumedReady(string.Empty);

            switch (analysis.Skill.Value)
            {
                case SkillType.Mining:
                    return EvaluateMiningToolAvailability();
                case SkillType.Woodcutting:
                    return ToolAvailabilityResult.AssumedReady("axe");
                case SkillType.Fishing:
                    return ToolAvailabilityResult.AssumedReady("fishing gear");
                case SkillType.Cooking:
                    return ToolAvailabilityResult.AssumedReady("cookware");
                case SkillType.Firemaking:
                    return ToolAvailabilityResult.AssumedReady("tinderbox");
                case SkillType.Magic:
                    return ToolAvailabilityResult.AssumedReady("spellbook");
                case SkillType.Ranged:
                    return ToolAvailabilityResult.AssumedReady("ranged gear");
                case SkillType.Attack:
                case SkillType.Strength:
                case SkillType.Defence:
                    return ToolAvailabilityResult.AssumedReady("training gear");
                case SkillType.Hitpoints:
                    return ToolAvailabilityResult.AssumedReady("supplies");
                case SkillType.Beastmaster:
                    return ToolAvailabilityResult.AssumedReady("beast treats");
                default:
                    return ToolAvailabilityResult.AssumedReady("gear");
            }
        }

        private ToolAvailabilityResult EvaluateMiningToolAvailability()
        {
            var inventoryWrapper = CompanionManager.CompanionInventory;
            RuntimeInventory inventory = inventoryWrapper != null ? inventoryWrapper.InventoryComponent : null;
            var equipment = CompanionManager.CompanionEquipment;
            var skills = CompanionManager.CompanionSkills;
            int miningLevel = skills != null ? skills.GetLevel(SkillType.Mining) : 1;

            var definitions = PickaxeDefinitionRegistry.GetAllDefinitions();
            if (definitions == null || definitions.Count == 0)
            {
                var selectors = FindObjectsOfType<PickaxeToUse>(true);
                for (int i = 0; i < selectors.Length; i++)
                {
                    var selector = selectors[i];
                    if (selector != null)
                        PickaxeDefinitionRegistry.RegisterDefinitions(selector.AllPickaxes);
                }

                definitions = PickaxeDefinitionRegistry.GetAllDefinitions();
            }

            if (definitions == null || definitions.Count == 0)
                return ToolAvailabilityResult.AssumedReady("pickaxe");

            PickaxeDefinition bestOwned = null;
            bool bestOwnedEquipped = false;
            PickaxeDefinition bestEligible = null;

            for (int i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null)
                    continue;

                if (definition.LevelRequirement > miningLevel)
                    continue;

                if (bestEligible == null || definition.Tier > bestEligible.Tier)
                    bestEligible = definition;

                var item = GatheringInventoryHelper.GetItemData(definition.Id, ref pickaxeItemCache);
                bool owns = inventory != null && item != null && inventory.GetItemCount(item) > 0;
                bool equippedTool = false;

                if (equipment != null && item != null)
                {
                    var entry = equipment.GetEquipped(EquipmentSlot.Weapon);
                    equippedTool = entry.item == item;
                }

                if (!owns && !equippedTool)
                    continue;

                bestOwned = definition;
                bestOwnedEquipped = equippedTool;
                break;
            }

            if (bestOwned != null)
            {
                return ToolAvailabilityResult.Ready(
                    bestOwned.DisplayName,
                    "pickaxe",
                    bestOwnedEquipped);
            }

            if (bestEligible != null)
            {
                return ToolAvailabilityResult.Missing(
                    bestEligible.DisplayName,
                    "pickaxe",
                    "We should fetch one before we start.");
            }

            return ToolAvailabilityResult.Missing(string.Empty, "pickaxe", "Let's secure a pickaxe first.");
        }

        private void ComposeReadySkillResponse(
            SkillProposalAnalysis analysis,
            ToolAvailabilityResult toolResult,
            List<string> segments,
            List<string> followUps,
            string playerName,
            ref string companionMood,
            CompanionResponseContext context)
        {
            if (!analysis.HasConcreteSkill)
                return;

            string toolName = ResolveToolName(toolResult);
            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName;
            string skillDisplay = analysis.HasDisplayName
                ? analysis.SkillDisplayName
                : SkillNameUtility.GetDisplayName(analysis.Skill.Value);
            string skillSentence = !string.IsNullOrWhiteSpace(analysis.SkillSentenceName)
                ? analysis.SkillSentenceName
                : SkillNameUtility.GetSentenceName(analysis.Skill.Value);
            string activity = ResolveSkillActivityDescriptor(analysis.Skill.Value);

            var replacements = new Dictionary<string, string>
            {
                { "tool", toolName },
                { "skillName", skillDisplay },
                { "skillSentence", skillSentence },
                { "activity", string.IsNullOrWhiteSpace(activity) ? skillSentence : activity },
                { "playerName", safePlayerName }
            };

            bool toolReady = !string.IsNullOrWhiteSpace(toolName) && toolResult.State == SkillToolState.Ready;

            string[] pool = CompanionSkillProposalDialogueBlocks.PlayerSkillProposalReadyGenericSegments;
            string[] followUpPool = CompanionSkillProposalDialogueBlocks.PlayerSkillProposalReadyGenericFollowUps;
            float followUpChance = SkillProposalLightFollowUpChance;

            if (toolReady && analysis.Skill.HasValue)
            {
                switch (analysis.Skill.Value)
                {
                    case SkillType.Mining:
                        pool = CompanionSkillProposalDialogueBlocks.PlayerMiningProposalReadyWithPickaxeSegments;
                        followUpPool = CompanionSkillProposalDialogueBlocks.PlayerMiningProposalReadyWithToolFollowUps;
                        followUpChance = SkillProposalFollowUpChance;
                        break;
                    case SkillType.Woodcutting:
                        pool = CompanionSkillProposalDialogueBlocks.PlayerWoodcuttingProposalReadyWithAxeSegments;
                        followUpPool = CompanionSkillProposalDialogueBlocks.PlayerWoodcuttingProposalReadyWithToolFollowUps;
                        followUpChance = SkillProposalFollowUpChance;
                        break;
                    case SkillType.Fishing:
                        pool = CompanionSkillProposalDialogueBlocks.PlayerFishingProposalReadyWithGearSegments;
                        followUpPool = CompanionSkillProposalDialogueBlocks.PlayerFishingProposalReadyWithToolFollowUps;
                        followUpChance = SkillProposalFollowUpChance;
                        break;
                }
            }

            string primary = ApplyProposalTokens(ChooseRandom(pool), replacements);
            if (!string.IsNullOrWhiteSpace(primary))
                segments.Add(primary);

            TryAppendSkillProposalFollowUp(followUps, followUpPool, replacements, followUpChance);

            DateTime nowUtc = DateTime.UtcNow;
            lastProactiveQuestionUtc = nowUtc;

            if (conversationMemory != null)
            {
                var metadata = CompanionEventMetadata.Create(
                    primaryActor: playerName,
                    skill: analysis.Skill,
                    additionalContext: analysis.RawMessage);

                string summary = string.IsNullOrWhiteSpace(playerName)
                    ? $"Agreed to train some {skillDisplay}"
                    : $"{playerName} and the companion planned more {skillDisplay}";

                conversationMemory.RegisterEvent(summary, CompanionEventType.Gathering, metadata);
            }

            if (analysis.Skill.HasValue && IsCombatSkill(analysis.Skill.Value))
                CompanionSkillCooldownTimers.ClearCombatDeclineCooldown(CompanionManager.CompanionSkillCooldowns);
        }

        /// <summary>
        /// Composes a decline response when the companion opts out of the requested training session.
        /// </summary>
        private void ComposeSkillProposalDeclineResponse(
            SkillProposalAnalysis analysis,
            List<string> segments,
            List<string> followUps,
            string playerName,
            CompanionResponseContext context)
        {
            if (!analysis.HasConcreteSkill)
                return;

            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName;
            string skillDisplay = analysis.HasDisplayName
                ? analysis.SkillDisplayName
                : SkillNameUtility.GetDisplayName(analysis.Skill.Value);

            bool declinedCombat = IsCombatSkill(analysis.Skill.Value);

            var replacements = new Dictionary<string, string>
            {
                { "playerName", safePlayerName },
                { "skillName", skillDisplay }
            };

            var declinePool = declinedCombat
                ? CompanionSkillProposalDialogueBlocks.PlayerCombatSkillProposalDeclineSegments
                : CompanionSkillProposalDialogueBlocks.PlayerSkillProposalDeclineSegments;

            string primary = ApplyProposalTokens(ChooseRandom(declinePool), replacements);

            if (!string.IsNullOrWhiteSpace(primary))
                segments.Add(primary);

            lastProactiveQuestionUtc = DateTime.UtcNow;

            if (analysis.Skill == SkillType.Mining)
                CompanionSkillCooldownTimers.StartMiningCooldown(CompanionManager.CompanionSkillCooldowns);

            if (analysis.Skill == SkillType.Woodcutting)
                CompanionSkillCooldownTimers.StartWoodcuttingCooldown(CompanionManager.CompanionSkillCooldowns);

            if (analysis.Skill == SkillType.Fishing)
                CompanionSkillCooldownTimers.StartFishingCooldown(CompanionManager.CompanionSkillCooldowns);

            if (declinedCombat)
            {
                CompanionSkillCooldownTimers.StartCombatDeclineCooldown(
                    analysis.Skill.Value,
                    CompanionManager.CompanionSkillCooldowns);
            }

            TryAppendDeclineSuggestion(followUps, analysis, safePlayerName, context, declinedCombat);
        }

        /// <summary>
        /// Attempts to add an alternate skill suggestion after a decline to keep the conversation flowing.
        /// </summary>
        private void TryAppendDeclineSuggestion(
            List<string> followUps,
            SkillProposalAnalysis declinedAnalysis,
            string safePlayerName,
            CompanionResponseContext context,
            bool excludeCombatSkills)
        {
            if (followUps == null)
                return;

            if (UnityEngine.Random.value > SkillProposalDeclineSuggestionChance)
                return;

            string suggestionName = ResolveDeclineSuggestionSkillName(
                declinedAnalysis,
                context,
                excludeCombatSkills);
            if (string.IsNullOrWhiteSpace(suggestionName))
                return;

            var replacements = new Dictionary<string, string>
            {
                { "playerName", safePlayerName },
                { "skillName", suggestionName },
                { "suggestedSkillName", suggestionName }
            };

            string followUp = ApplyProposalTokens(
                ChooseRandom(CompanionSkillProposalDialogueBlocks.PlayerSkillProposalDeclineSuggestionSegments),
                replacements);

            if (!string.IsNullOrWhiteSpace(followUp))
                followUps.Add(followUp);
        }

        /// <summary>
        /// Resolves which skill name should be suggested after the companion declines the proposal.
        /// </summary>
        private string ResolveDeclineSuggestionSkillName(
            SkillProposalAnalysis declinedAnalysis,
            CompanionResponseContext context,
            bool excludeCombatSkills)
        {
            if (context.HasSuggestedSkill &&
                (!declinedAnalysis.HasConcreteSkill || context.SuggestedSkill.Value != declinedAnalysis.Skill.Value))
            {
                if (!excludeCombatSkills || !IsCombatSkill(context.SuggestedSkill.Value))
                {
                    string contextName = !string.IsNullOrWhiteSpace(context.SuggestedSkillName)
                        ? context.SuggestedSkillName
                        : SkillNameUtility.GetDisplayName(context.SuggestedSkill.Value);

                    if (!string.IsNullOrWhiteSpace(contextName))
                        return contextName;
                }
            }

            DateTime nowUtc = context.RequestTimeUtc.Kind == DateTimeKind.Utc && context.RequestTimeUtc != default
                ? context.RequestTimeUtc
                : DateTime.UtcNow;

            SkillType? excludedSkill = declinedAnalysis.HasConcreteSkill ? declinedAnalysis.Skill : (SkillType?)null;
            if (TryGetBestSkillCandidate(nowUtc, out var candidate, excludedSkill))
            {
                if (candidate.HasSkill)
                {
                    if (excludeCombatSkills && candidate.Skill.HasValue && IsCombatSkill(candidate.Skill.Value))
                        goto ResolveFallbackSkill;

                    if (!string.IsNullOrWhiteSpace(candidate.SkillName))
                        return candidate.SkillName;

                    return SkillNameUtility.GetDisplayName(candidate.Skill.Value);
                }
            }

        ResolveFallbackSkill:
            SkillType? fallback = ChooseAlternateSkill(excludedSkill, excludeCombatSkills);
            return fallback.HasValue ? SkillNameUtility.GetDisplayName(fallback.Value) : string.Empty;
        }

        /// <summary>
        /// Chooses a random skill other than the one that was declined so the companion can pivot naturally.
        /// </summary>
        private static SkillType? ChooseAlternateSkill(SkillType? excludedSkill, bool excludeCombatSkills)
        {
            var values = (SkillType[])Enum.GetValues(typeof(SkillType));
            if (values == null || values.Length == 0)
                return null;

            var available = new List<SkillType>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                SkillType candidate = values[i];
                if (excludedSkill.HasValue && candidate == excludedSkill.Value)
                    continue;

                if (excludeCombatSkills && IsCombatSkill(candidate))
                    continue;

                available.Add(candidate);
            }

            if (available.Count == 0)
                return null;

            int index = UnityEngine.Random.Range(0, available.Count);
            return available[index];
        }

        private void ComposeMissingToolResponse(
            SkillProposalAnalysis analysis,
            ToolAvailabilityResult toolResult,
            List<string> segments,
            List<string> followUps,
            string playerName)
        {
            string skillSentence = !string.IsNullOrWhiteSpace(analysis.SkillSentenceName)
                ? analysis.SkillSentenceName
                : (analysis.HasConcreteSkill
                    ? SkillNameUtility.GetSentenceName(analysis.Skill.Value)
                    : analysis.SkillDisplayName.ToLowerInvariant());

            string skillDisplay = analysis.HasDisplayName
                ? analysis.SkillDisplayName
                : (analysis.HasConcreteSkill
                    ? SkillNameUtility.GetDisplayName(analysis.Skill.Value)
                    : skillSentence);

            var replacements = new Dictionary<string, string>
            {
                { "skillSentence", skillSentence },
                { "skillName", skillDisplay },
                { "playerName", string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName },
                { "indefiniteTool", BuildIndefiniteToolName(toolResult) },
                { "definiteTool", BuildDefiniteToolName(toolResult) },
                { "toolPlural", BuildToolPlural(toolResult) }
            };

            string primary = ApplyProposalTokens(ChooseRandom(CompanionSkillProposalDialogueBlocks.PlayerSkillProposalMissingToolSegments), replacements);
            if (!string.IsNullOrWhiteSpace(primary))
                segments.Add(primary);

            TryAppendSkillProposalFollowUp(
                followUps,
                CompanionSkillProposalDialogueBlocks.PlayerSkillProposalMissingToolFollowUps,
                replacements,
                SkillProposalLightFollowUpChance);
        }

        private void ComposeAlternateSkillResponse(
            SkillProposalAnalysis analysis,
            CompanionResponseContext context,
            List<string> segments,
            List<string> followUps,
            string playerName,
            ToolAvailabilityResult toolResult)
        {
            DateTime nowUtc = DateTime.UtcNow;
            SkillQuestionCandidate candidate;
            bool hasCandidate = TryGetBestSkillCandidate(nowUtc, out candidate, analysis.HasConcreteSkill ? analysis.Skill : (SkillType?)null);
            if (!hasCandidate)
                hasCandidate = TryBuildFallbackSkillCandidate(nowUtc, out candidate);

            string alternateName = ResolveAlternateName(analysis, candidate, context);
            string alternateDescription = ResolveAlternateDescription(candidate, alternateName, context);

            var replacements = new Dictionary<string, string>
            {
                { "alternateName", alternateName },
                { "alternateDescription", alternateDescription },
                { "playerName", string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName },
                { "indefiniteTool", BuildIndefiniteToolName(toolResult) },
                { "definiteTool", BuildDefiniteToolName(toolResult) },
                { "skillName", analysis.SkillDisplayName },
                { "skillSentence", analysis.SkillSentenceName }
            };

            string primary = ApplyProposalTokens(ChooseRandom(CompanionSkillProposalDialogueBlocks.PlayerSkillProposalAlternateSkillSegments), replacements);
            if (!string.IsNullOrWhiteSpace(primary))
                segments.Add(primary);

            TryAppendSkillProposalFollowUp(followUps, CompanionSkillProposalDialogueBlocks.PlayerSkillProposalAlternateSkillFollowUps, replacements);

            lastProactiveQuestionUtc = nowUtc;
        }

        private static string ResolveToolName(ToolAvailabilityResult toolResult)
        {
            if (!string.IsNullOrWhiteSpace(toolResult.SpecificToolName))
                return toolResult.SpecificToolName;

            if (!string.IsNullOrWhiteSpace(toolResult.GenericToolName))
                return toolResult.GenericToolName;

            return string.Empty;
        }

        private static string ResolveSkillActivityDescriptor(SkillType skill)
        {
            switch (skill)
            {
                case SkillType.Mining:
                    return "chip into the rocks";
                case SkillType.Woodcutting:
                    return "chop down some trees";
                case SkillType.Fishing:
                    return "cast a line";
                case SkillType.Cooking:
                    return "cook up a meal";
                case SkillType.Firemaking:
                    return "light the logs";
                case SkillType.Magic:
                    return "sling a few spells";
                case SkillType.Ranged:
                    return "loose some arrows";
                case SkillType.Attack:
                    return "run combat drills";
                case SkillType.Strength:
                    return "work those muscles";
                case SkillType.Defence:
                    return "tighten our defenses";
                case SkillType.Hitpoints:
                    return "work on endurance";
                case SkillType.Beastmaster:
                    return "train the beasts";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Determines whether the supplied skill is considered part of the combat suite.
        /// </summary>
        private static bool IsCombatSkill(SkillType skill)
        {
            switch (skill)
            {
                case SkillType.Hitpoints:
                case SkillType.Attack:
                case SkillType.Strength:
                case SkillType.Ranged:
                case SkillType.Defence:
                case SkillType.Magic:
                    return true;
                default:
                    return false;
            }
        }

        private static string ChooseRandom(IReadOnlyList<string> pool)
        {
            if (pool == null || pool.Count == 0)
                return string.Empty;

            int index = UnityEngine.Random.Range(0, pool.Count);
            return pool[index];
        }

        private static string ApplyProposalTokens(string template, IReadOnlyDictionary<string, string> replacements)
        {
            if (string.IsNullOrWhiteSpace(template))
                return string.Empty;

            string result = template;

            if (replacements != null)
            {
                foreach (var pair in replacements)
                {
                    string token = "{" + pair.Key + "}";
                    string value = pair.Value ?? string.Empty;
                    result = result.Replace(token, value);
                }
            }

            return CompactWhitespace(result);
        }

        private void TryAppendSkillProposalFollowUp(
            List<string> followUps,
            string[] pool,
            IReadOnlyDictionary<string, string> replacements,
            float chance = SkillProposalFollowUpChance)
        {
            if (followUps == null || pool == null || pool.Length == 0)
                return;

            if (UnityEngine.Random.value > chance)
                return;

            string followUp = ApplyProposalTokens(ChooseRandom(pool), replacements);
            if (!string.IsNullOrWhiteSpace(followUp))
                followUps.Add(followUp);
        }

        private static string BuildIndefiniteToolName(ToolAvailabilityResult toolResult)
        {
            string baseName = !string.IsNullOrWhiteSpace(toolResult.SpecificToolName)
                ? toolResult.SpecificToolName
                : (!string.IsNullOrWhiteSpace(toolResult.GenericToolName) ? toolResult.GenericToolName : "tool");

            return WithIndefiniteArticle(baseName);
        }

        private static string BuildDefiniteToolName(ToolAvailabilityResult toolResult)
        {
            if (!string.IsNullOrWhiteSpace(toolResult.SpecificToolName))
                return $"the {toolResult.SpecificToolName}";

            if (!string.IsNullOrWhiteSpace(toolResult.GenericToolName))
                return $"the {toolResult.GenericToolName}";

            return "the tool";
        }

        private static string BuildToolPlural(ToolAvailabilityResult toolResult)
        {
            string baseName = !string.IsNullOrWhiteSpace(toolResult.GenericToolName)
                ? toolResult.GenericToolName
                : (!string.IsNullOrWhiteSpace(toolResult.SpecificToolName) ? toolResult.SpecificToolName : "tools");

            if (baseName.EndsWith("x", StringComparison.OrdinalIgnoreCase))
                return baseName + "es";

            if (baseName.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                return baseName;

            return baseName + "s";
        }

        private static string WithIndefiniteArticle(string noun)
        {
            if (string.IsNullOrWhiteSpace(noun))
                return "a tool";

            string trimmed = noun.Trim();
            char first = char.ToLowerInvariant(trimmed[0]);
            bool useAn = first == 'a' || first == 'e' || first == 'i' || first == 'o' || first == 'u';
            string article = useAn ? "an" : "a";
            return $"{article} {trimmed}";
        }

        private static string ResolveAlternateName(
            SkillProposalAnalysis analysis,
            SkillQuestionCandidate candidate,
            CompanionResponseContext context)
        {
            if (candidate.HasSkill)
            {
                if (!string.IsNullOrWhiteSpace(candidate.SkillName))
                    return candidate.SkillName;

                return SkillNameUtility.GetDisplayName(candidate.Skill.Value);
            }

            if (candidate.HasDescription)
                return candidate.Description;

            if (context.HasSuggestedSkill && !string.IsNullOrWhiteSpace(context.SuggestedSkillName))
                return context.SuggestedSkillName;

            if (analysis.HasDisplayName)
                return analysis.SkillDisplayName;

            return "something else";
        }

        private static string ResolveAlternateDescription(
            SkillQuestionCandidate candidate,
            string alternateName,
            CompanionResponseContext context)
        {
            if (candidate.HasDescription)
                return candidate.Description;

            if (candidate.HasSkill)
                return $"more {SkillNameUtility.GetSentenceName(candidate.Skill.Value)}";

            if (context.HasSuggestedSkill && !string.IsNullOrWhiteSpace(context.SuggestedSkillName))
                return context.SuggestedSkillName.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(alternateName))
                return alternateName.ToLowerInvariant();

            return "a different activity";
        }

        private bool TryHandleSkillPlanIntent(
            CompanionDialogueIntent intent,
            CompanionResponseContext context,
            List<string> segments,
            List<string> followUps,
            string playerName,
            ref string companionMood,
            string recentEvent)
        {
            if (!responseLibrary.TrySelectResponse(intent, context, null, out var selection))
                return false;

            string formatted = FormatTemplate(
                selection.PrimarySegment,
                playerName,
                companionMood,
                recentEvent,
                context);

            if (string.IsNullOrWhiteSpace(formatted))
                return false;

            segments.Add(formatted);
            AppendFollowUps(selection.FollowUpSegments, followUps, playerName, companionMood, recentEvent, context);
            ProcessSkillPlanResolution(intent, context, playerName);
            return true;
        }

        private void ProcessSkillPlanResolution(CompanionDialogueIntent intent, CompanionResponseContext context, string playerName)
        {
            DateTime nowUtc = DateTime.UtcNow;
            var candidate = activeSkillQuestion.TryGetCandidate();

            if (activeSkillQuestion.IsActive)
            {
                if (intent == CompanionDialogueIntent.AcceptSkillPlan && candidate.HasValue && candidate.Value.HasSkill && conversationMemory != null)
                {
                    string skillName = context.HasSuggestedSkill ? context.SuggestedSkillName : SkillNameUtility.GetDisplayName(candidate.Value.Skill.Value);
                    string summary = string.IsNullOrWhiteSpace(playerName)
                        ? $"Agreed to train more {skillName}"
                        : $"{playerName} agreed to train more {skillName}";
                    var metadata = CompanionEventMetadata.Create(
                        primaryActor: playerName,
                        skill: candidate.Value.Skill,
                        additionalContext: candidate.Value.Description);
                    conversationMemory.RegisterEvent(summary, CompanionEventType.Gathering, metadata);
                }

                if (intent == CompanionDialogueIntent.RequestAlternateSkill)
                {
                    SkillType? excludeSkill = candidate.HasValue ? candidate.Value.Skill : (SkillType?)null;
                    ClearActiveSkillQuestion();
                    lastProactiveQuestionUtc = nowUtc;
                    TryScheduleAlternateSkillQuestion(excludeSkill);
                    return;
                }

                ClearActiveSkillQuestion();
                lastProactiveQuestionUtc = nowUtc;
                return;
            }

            if (intent == CompanionDialogueIntent.RequestAlternateSkill)
                lastProactiveQuestionUtc = nowUtc;
        }

        private string BuildStatusSegment(
            string playerName,
            ref string companionMood,
            CompanionResponseContext context,
            List<string> followUps)
        {
            string lastStatus = conversationMemory != null ? conversationMemory.LastStatusResponse : string.Empty;
            const int MaxAttempts = 3;

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (!responseLibrary.TrySelectResponse(
                        CompanionDialogueIntent.StatusQuery,
                        context,
                        lastStatusTemplateKey,
                        out var selection))
                {
                    break;
                }

                string formatted = FormatTemplate(
                    selection.PrimarySegment,
                    playerName,
                    companionMood,
                    string.Empty,
                    context);

                if (string.IsNullOrWhiteSpace(formatted))
                    continue;

                bool sameLine = conversationMemory != null &&
                                 !string.IsNullOrEmpty(lastStatus) &&
                                 string.Equals(formatted, lastStatus, StringComparison.OrdinalIgnoreCase);
                bool withinCooldown = conversationMemory != null &&
                                       conversationMemory.LastStatusResponseUtc.HasValue &&
                                       (DateTime.UtcNow - conversationMemory.LastStatusResponseUtc.Value).TotalMinutes <
                                       Math.Max(0.1f, statusRepeatCooldownMinutes);

                if (sameLine && withinCooldown)
                {
                    companionMood = ResolveCompanionMoodDescriptor();
                    formatted = FormatTemplate(
                        selection.PrimarySegment,
                        playerName,
                        companionMood,
                        string.Empty,
                        context);
                }

                AppendFollowUps(selection.FollowUpSegments, followUps, playerName, companionMood, string.Empty, context);
                lastStatusTemplateKey = selection.TemplateKey;
                return formatted;
            }

            string fallback = FormatTemplate(
                "I'm feeling {companionMood}. All systems steady.",
                playerName,
                companionMood,
                string.Empty,
                context);

            lastStatusTemplateKey = string.Empty;
            return fallback;
        }

        private CompanionResponseContext BuildResponseContext(SkillQuestionCandidate? candidate = null)
        {
            DateTime nowUtc = DateTime.UtcNow;
            var recentSkills = ResolveRecentSkillActions(nowUtc);
            string timeOfDay = ResolveTimeOfDayDescriptor(nowUtc);
            bool companionInCombat = IsCompanionInCombat(nowUtc);

            SkillType? suggestedSkill = null;
            string suggestedSkillName = string.Empty;
            string suggestedSkillAction = string.Empty;
            TimeSpan? skillAge = null;
            string skillRecency = string.Empty;

            if (candidate.HasValue && candidate.Value.IsValid)
            {
                var value = candidate.Value;
                suggestedSkill = value.Skill;
                suggestedSkillName = !string.IsNullOrWhiteSpace(value.SkillName) && value.HasSkill
                    ? value.SkillName
                    : value.Skill.HasValue ? SkillNameUtility.GetDisplayName(value.Skill.Value) : string.Empty;
                suggestedSkillAction = value.Description;
                skillAge = nowUtc - value.TimestampUtc;
                if (skillAge.HasValue)
                    skillRecency = DescribeSkillRecency(skillAge.Value);
            }

            string ambientLocation = ResolveAmbientLocationDescriptor();
            string memorySummary = ResolveAmbientMemorySummary();

            return new CompanionResponseContext(
                nowUtc,
                timeOfDay,
                playerInCombat,
                companionInCombat,
                recentSkills,
                pendingResponses.Count,
                suggestedSkill,
                suggestedSkillName,
                suggestedSkillAction,
                skillAge,
                skillRecency,
                ambientLocation: ambientLocation,
                memorySummary: memorySummary);
        }

        private IReadOnlyList<string> ResolveRecentSkillActions(DateTime nowUtc)
        {
            PruneSkillActions(nowUtc);

            if (recentSkillActions.Count == 0)
                return Array.Empty<string>();

            var snapshot = new List<string>(recentSkillActions.Count);
            foreach (var entry in recentSkillActions)
                snapshot.Add(entry.Description);

            return snapshot;
        }

        private static string ResolveTimeOfDayDescriptor(DateTime utcNow)
        {
            int hour = utcNow.Hour;
            if (hour >= 5 && hour < 12)
                return "morning";
            if (hour >= 12 && hour < 17)
                return "afternoon";
            if (hour >= 17 && hour < 21)
                return "evening";
            return "night";
        }

        private string CombineSegments(IEnumerable<string> segments)
        {
            var builder = new StringBuilder();
            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment))
                    continue;

                if (builder.Length > 0 && !builder.ToString().EndsWith(" "))
                    builder.Append(' ');

                builder.Append(segment.Trim());
            }

            return builder.ToString().Trim();
        }

        private string ResolveCompanionMoodDescriptor()
        {
            if (companionMoodDescriptors == null || companionMoodDescriptors.Length == 0)
                return "ready";

            if (companionMoodDescriptors.Length == 1)
            {
                lastCompanionMoodDescriptor = companionMoodDescriptors[0];
                return lastCompanionMoodDescriptor;
            }

            const int MaxAttempts = 4;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                string descriptor = companionMoodDescriptors[UnityEngine.Random.Range(0, companionMoodDescriptors.Length)];
                if (!string.Equals(descriptor, lastCompanionMoodDescriptor, StringComparison.OrdinalIgnoreCase))
                {
                    lastCompanionMoodDescriptor = descriptor;
                    return descriptor;
                }
            }

            lastCompanionMoodDescriptor = companionMoodDescriptors[UnityEngine.Random.Range(0, companionMoodDescriptors.Length)];
            return lastCompanionMoodDescriptor;
        }

        private string ResolveRecentEventSummary()
        {
            if (conversationMemory == null)
            {
                EnsureConversationMemoryBound();
                if (conversationMemory == null)
                    return string.Empty;
            }

            if (conversationMemory.TryGetLatestEvent(out var eventEntry))
                return FormatEventEntry(eventEntry);

            // No gameplay events are available. Avoid falling back to the cached status
            // response or prior conversation lines so repeated greetings remain short.
            return string.Empty;
        }

        private string ResolveAmbientLocationDescriptor()
        {
            EnsureConversationMemoryBound();
            if (conversationMemory != null && conversationMemory.TryGetLatestEvent(out var eventEntry))
            {
                string location = ResolveEventLocation(eventEntry.Metadata);
                if (!string.IsNullOrWhiteSpace(location))
                    return location;
            }

            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrWhiteSpace(scene.name))
                return FormatSceneName(scene.name);

            return string.Empty;
        }

        private string ResolveAmbientMemorySummary()
        {
            EnsureConversationMemoryBound();
            if (conversationMemory != null && conversationMemory.TryGetLatestEvent(out var eventEntry))
            {
                if (!string.IsNullOrWhiteSpace(eventEntry.Summary))
                    return eventEntry.Summary.Trim();

                if (!string.IsNullOrWhiteSpace(eventEntry.Metadata.AdditionalContext))
                    return eventEntry.Metadata.AdditionalContext.Trim();
            }

            if (TryGetLatestNpcKill(out string npcName) && !string.IsNullOrWhiteSpace(npcName))
            {
                string plural = FormatNpcPlural(npcName);
                return string.IsNullOrWhiteSpace(plural) ? npcName.Trim() : $"fighting {plural}";
            }

            if (recentSkillActions.Count > 0)
            {
                var first = recentSkillActions.First;
                if (first != null && !string.IsNullOrWhiteSpace(first.Value.Description))
                    return first.Value.Description.Trim();
            }

            return string.Empty;
        }

        private string FormatEventEntry(CompanionEventEntry entry)
        {
            string actor = !string.IsNullOrWhiteSpace(entry.Metadata.PrimaryActor)
                ? entry.Metadata.PrimaryActor.Trim()
                : "We";

            string summary = !string.IsNullOrWhiteSpace(entry.Summary)
                ? entry.Summary.Trim()
                : "noticed something interesting";

            var builder = new StringBuilder();
            builder.Append(actor);
            if (!summary.StartsWith(" ", StringComparison.Ordinal))
                builder.Append(' ');
            builder.Append(summary);

            if (!string.IsNullOrWhiteSpace(entry.Metadata.SecondaryActor))
                builder.Append($" with {entry.Metadata.SecondaryActor.Trim()}");

            string location = ResolveEventLocation(entry.Metadata);
            if (!string.IsNullOrWhiteSpace(location))
                builder.Append($" near {location}");

            if (entry.Metadata.Skill.HasValue)
                builder.Append($" ({entry.Metadata.Skill.Value} XP)");

            if (!string.IsNullOrWhiteSpace(entry.Metadata.AdditionalContext))
            {
                string trimmedContext = entry.Metadata.AdditionalContext.Trim();
                if (trimmedContext.Length > 0)
                {
                    if (!builder.ToString().TrimEnd().EndsWith(".", StringComparison.Ordinal))
                        builder.Append('.');
                    builder.Append(' ').Append(trimmedContext);
                }
            }

            return builder.ToString().Trim();
        }

        private static string ResolveEventLocation(CompanionEventMetadata metadata)
        {
            if (!string.IsNullOrWhiteSpace(metadata.LocationName))
                return metadata.LocationName.Trim();

            if (metadata.WorldPosition.HasValue)
                return FormatWorldPosition(metadata.WorldPosition.Value);

            return string.Empty;
        }

        private static string FormatWorldPosition(Vector3 position)
        {
            return $"{position.x:0.0}, {position.y:0.0}";
        }

        private static string FormatSceneName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return string.Empty;

            string trimmed = rawName.Replace('_', ' ').Trim();
            if (trimmed.Length == 0)
                return string.Empty;

            // The primary overworld scene should be surfaced to players as "Viosla" to
            // match the worldbuilding terminology used across the UI and narrative.
            // Handle this before applying title casing so variations like "OverWorld"
            // and "overworld" are normalised correctly.
            if (string.Equals(trimmed, "overworld", StringComparison.OrdinalIgnoreCase))
                return "Viosla";

            var textInfo = CultureInfo.InvariantCulture.TextInfo;
            return textInfo.ToTitleCase(trimmed.ToLowerInvariant());
        }

        /// <summary>
        /// Allows <see cref="CompanionConversationMemory"/> to register itself once it becomes
        /// active so the service always has a live reference.
        /// </summary>
        /// <param name="memory">Memory component that should be bound to the service.</param>
        internal void BindConversationMemory(CompanionConversationMemory memory)
        {
            if (memory == null)
                return;

            if (conversationMemory == memory)
                return;

            conversationMemory = memory;

            if (ShouldTraceMemory)
                Debug.Log("[CompanionConversationService] Conversation memory bound by runtime component.");
        }

        /// <summary>
        /// Removes a previously registered memory instance. When the provided memory matches the
        /// active reference the helper clears it so future lookups can bind a new component.
        /// </summary>
        /// <param name="memory">Memory component that is detaching from the service.</param>
        internal void UnbindConversationMemory(CompanionConversationMemory memory)
        {
            if (memory == null || conversationMemory != memory)
                return;

            conversationMemory = null;

            if (ShouldTraceMemory)
                Debug.Log("[CompanionConversationService] Conversation memory unbound. Waiting for replacement.");
        }

        private string FormatTemplate(
            string template,
            string playerName,
            string companionMood,
            string recentEvent,
            CompanionResponseContext context)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            string companionName = CompanionManager.GetCompanionDisplayName();
            string resolvedPlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName;
            string resolvedCompanionMood = string.IsNullOrWhiteSpace(companionMood) ? "ready" : companionMood;
            string resolvedEvent = string.IsNullOrWhiteSpace(recentEvent) ? "that" : recentEvent;
            string resolvedTimeOfDay = string.IsNullOrWhiteSpace(context.TimeOfDayLabel) ? "day" : context.TimeOfDayLabel;
            string resolvedCombatState = string.IsNullOrWhiteSpace(context.CombatStateDescriptor)
                ? "standing down"
                : context.CombatStateDescriptor;
            string suggestedSkillDisplayName = context.HasSuggestedSkill
                ? (!string.IsNullOrWhiteSpace(context.SuggestedSkillName)
                    ? context.SuggestedSkillName
                    : SkillNameUtility.GetDisplayName(context.SuggestedSkill.Value))
                : string.Empty;
            string suggestedSkillSentenceName = context.HasSuggestedSkill
                ? SkillNameUtility.GetSentenceName(context.SuggestedSkill.Value)
                : (!string.IsNullOrWhiteSpace(suggestedSkillDisplayName)
                    ? suggestedSkillDisplayName.ToLowerInvariant()
                    : string.Empty);

            string resolvedSkillAction = !string.IsNullOrWhiteSpace(context.SuggestedSkillActionDescription)
                ? context.SuggestedSkillActionDescription
                : context.HasRecentSkillActions
                    ? context.LatestSkillAction
                    : "keeping skills sharp";

            if (context.HasSuggestedSkill)
            {
                bool suggestionActionEmpty = string.IsNullOrWhiteSpace(context.SuggestedSkillActionDescription);
                bool mentionsDisplayName = !string.IsNullOrWhiteSpace(resolvedSkillAction) &&
                    !string.IsNullOrWhiteSpace(suggestedSkillDisplayName) &&
                    resolvedSkillAction.IndexOf(
                        suggestedSkillDisplayName,
                        StringComparison.OrdinalIgnoreCase) >= 0;
                bool mentionsSentenceName = !string.IsNullOrWhiteSpace(resolvedSkillAction) &&
                    !string.IsNullOrWhiteSpace(suggestedSkillSentenceName) &&
                    resolvedSkillAction.IndexOf(
                        suggestedSkillSentenceName,
                        StringComparison.OrdinalIgnoreCase) >= 0;

                if (suggestionActionEmpty || (!mentionsDisplayName && !mentionsSentenceName))
                {
                    string fallbackSuggestion = !string.IsNullOrWhiteSpace(suggestedSkillSentenceName)
                        ? suggestedSkillSentenceName
                        : suggestedSkillDisplayName;

                    if (!string.IsNullOrWhiteSpace(fallbackSuggestion))
                        resolvedSkillAction = fallbackSuggestion;
                }
            }

            string resolvedSuggestedSkill = context.HasSuggestedSkill
                ? (!string.IsNullOrWhiteSpace(suggestedSkillDisplayName)
                    ? suggestedSkillDisplayName
                    : (!string.IsNullOrWhiteSpace(suggestedSkillSentenceName)
                        ? suggestedSkillSentenceName
                        : (!string.IsNullOrWhiteSpace(context.SuggestedSkillActionDescription)
                            ? context.SuggestedSkillActionDescription
                            : "skilling")))
                : (!string.IsNullOrWhiteSpace(context.SuggestedSkillActionDescription)
                    ? context.SuggestedSkillActionDescription
                    : (context.HasRecentSkillActions ? context.LatestSkillAction : "skilling"));
            string resolvedSkillRecency = context.HasSuggestedSkillRecency
                ? context.SuggestedSkillRecency
                : "recently";
            string resolvedLocation = context.HasAmbientLocation ? context.AmbientLocation : "around here";
            string resolvedMemory = context.HasMemorySummary
                ? context.MemorySummary
                : (!string.IsNullOrWhiteSpace(recentEvent) ? recentEvent : resolvedSuggestedSkill);

            string result = template
                .Replace("{playerName}", resolvedPlayerName)
                .Replace("{companionName}", string.IsNullOrWhiteSpace(companionName) ? "Companion" : companionName)
                .Replace("{companionMood}", resolvedCompanionMood)
                .Replace("{recentEvent}", resolvedEvent)
                .Replace("{timeOfDay}", resolvedTimeOfDay)
                .Replace("{combatState}", resolvedCombatState)
                .Replace("{recentSkillAction}", resolvedSkillAction)
                .Replace("{suggestedSkill}", resolvedSuggestedSkill)
                .Replace("{skillRecency}", resolvedSkillRecency)
                .Replace("{skillAction}", resolvedSkillAction)
                .Replace("{ambientLocation}", resolvedLocation)
                .Replace("{memorySummary}", resolvedMemory);

            return CompactWhitespace(result);
        }

        private string ApplySkillLevelTokens(
            string template,
            string playerName,
            string companionMood,
            string recentEvent,
            CompanionResponseContext context,
            SkillLevelQueryResult analysis)
        {
            string formatted = FormatTemplate(template, playerName, companionMood, recentEvent, context);
            if (string.IsNullOrWhiteSpace(formatted))
                return string.Empty;

            string displayName = !string.IsNullOrWhiteSpace(analysis.SkillDisplayName)
                ? analysis.SkillDisplayName
                : (analysis.Skill.HasValue
                    ? SkillNameUtility.GetDisplayName(analysis.Skill.Value)
                    : string.Empty);
            string sentenceName = !string.IsNullOrWhiteSpace(analysis.SkillSentenceName)
                ? analysis.SkillSentenceName
                : (analysis.Skill.HasValue
                    ? SkillNameUtility.GetSentenceName(analysis.Skill.Value)
                    : (!string.IsNullOrWhiteSpace(displayName)
                        ? displayName.ToLowerInvariant()
                        : string.Empty));

            string safeDisplay = string.IsNullOrWhiteSpace(displayName) ? "that skill" : displayName;
            string safeSentence = string.IsNullOrWhiteSpace(sentenceName)
                ? safeDisplay.ToLowerInvariant()
                : sentenceName;
            string levelText = analysis.SkillLevel.HasValue
                ? analysis.SkillLevel.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            string levelOrUnknown = analysis.SkillLevel.HasValue ? levelText : "unknown";

            formatted = formatted
                .Replace("{skillName}", safeDisplay)
                .Replace("{skillSentence}", safeSentence)
                .Replace("{skillLevel}", levelText)
                .Replace("{skillLevelOrUnknown}", levelOrUnknown);

            return CompactWhitespace(formatted);
        }

        private static string AppendSentence(string source, string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence))
                return source;

            string trimmedSentence = sentence.Trim();
            if (!trimmedSentence.EndsWith(".") && !trimmedSentence.EndsWith("!") && !trimmedSentence.EndsWith("?"))
                trimmedSentence += ".";

            if (string.IsNullOrWhiteSpace(source))
                return trimmedSentence;

            if (!source.EndsWith(" "))
                source += " ";

            return source + trimmedSentence;
        }

        private float ResolveTypingDelay(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Mathf.Max(0f, baseTypingDelaySeconds);

            float perWordDelayMin = Mathf.Min(perWordTypingDelayRange.x, perWordTypingDelayRange.y);
            float perWordDelayMax = Mathf.Max(perWordTypingDelayRange.x, perWordTypingDelayRange.y);
            float perWordDelay = perWordDelayMax <= 0f
                ? 0f
                : UnityEngine.Random.Range(perWordDelayMin, perWordDelayMax);

            int wordCount = Math.Max(1, text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length);
            float totalDelay = Mathf.Max(0f, baseTypingDelaySeconds) + perWordDelay * wordCount;
            return Mathf.Clamp(totalDelay, 0f, 8f);
        }

        private bool TryHandleSuggestionRequest(
            CompanionResponseContext context,
            List<string> segments,
            string playerName)
        {
            DateTime nowUtc = DateTime.UtcNow;
            EnsureSuggestionStateFresh(nowUtc);

            if (HasActiveSuggestion(nowUtc))
            {
                string repeat = ChooseRepeatResponse(playerName);
                if (!string.IsNullOrWhiteSpace(repeat))
                    segments.Add(repeat);

                playerRepeatedSuggestionRequest = true;
                playerNeededSkillReminderApology = false;
                return true;
            }

            if (!TryBuildSuggestionPayload(out var suggestion))
            {
                segments.Add("I'm not sure what I want to do right now—your call.");
                return true;
            }

            segments.Add(suggestion.ResponseText);
            lastSuggestionPayload = suggestion;
            lastSuggestionMessage = suggestion.ResponseText;
            lastSuggestionAnsweredUtc = nowUtc;
            playerRepeatedSuggestionRequest = false;
            playerNeededSkillReminderApology = false;
            return true;
        }

        private bool TryHandleSuggestionReminder(List<string> segments, string playerName)
        {
            DateTime nowUtc = DateTime.UtcNow;
            EnsureSuggestionStateFresh(nowUtc);

            if (!HasActiveSuggestion(nowUtc) || lastSuggestionPayload.Type == SuggestionType.None)
            {
                segments.Add("Ask me what I want to do first, then I can remind you.");
                playerNeededSkillReminderApology = false;
                return true;
            }

            if (!playerRepeatedSuggestionRequest)
            {
                segments.Add("You haven't asked me twice yet, so there's nothing to remind you about.");
                playerNeededSkillReminderApology = false;
                return true;
            }

            string response = BuildReminderResponse();
            if (string.IsNullOrWhiteSpace(response))
                response = "I told you earlier, remember?";

            segments.Add(response);
            playerNeededSkillReminderApology = lastSuggestionPayload.Type == SuggestionType.Skill;
            return true;
        }

        private bool TryHandlePlayerApology(
            CompanionResponseContext context,
            List<string> segments,
            string playerName,
            string companionMood,
            string recentEvent)
        {
            string template = playerNeededSkillReminderApology
                ? CompanionApologyDialogueLibrary.GetReminderAcknowledgementLine()
                : CompanionApologyDialogueLibrary.GetUnpromptedApologyLine();

            if (string.IsNullOrWhiteSpace(template))
                return false;

            string formatted = FormatTemplate(template, playerName, companionMood, recentEvent, context);
            if (string.IsNullOrWhiteSpace(formatted))
                return false;

            segments.Add(formatted);

            if (playerNeededSkillReminderApology)
                playerNeededSkillReminderApology = false;

            return true;
        }

        private string BuildReminderResponse()
        {
            switch (lastSuggestionPayload.Type)
            {
                case SuggestionType.Skill:
                    {
                        string skill = string.IsNullOrWhiteSpace(lastSuggestionPayload.SkillDisplayName)
                            ? "that skill"
                            : lastSuggestionPayload.SkillDisplayName;

                        return FormatReminder(
                            CompanionSuggestionDialogueBlocks.SkillReminderResponses,
                            "{skill}",
                            skill);
                    }

                case SuggestionType.NpcLatest:
                case SuggestionType.NpcHistory:
                    {
                        string npc = FormatNpcPlural(lastSuggestionPayload.NpcName);
                        if (string.IsNullOrWhiteSpace(npc))
                            npc = "those foes";

                        return FormatReminder(
                            CompanionSuggestionDialogueBlocks.NpcReminderResponses,
                            "{npc}",
                            npc);
                    }

                default:
                    return string.Empty;
            }
        }

        private static string FormatReminder(IReadOnlyList<string> templates, string token, string replacement)
        {
            if (templates == null || templates.Count == 0)
                return string.Empty;

            int index = UnityEngine.Random.Range(0, templates.Count);
            string template = templates[index];
            return string.IsNullOrWhiteSpace(template)
                ? string.Empty
                : template.Replace(token, replacement);
        }

        private bool TryBuildSuggestionPayload(out SuggestionPayload suggestion)
        {
            suggestion = SuggestionPayload.Empty;

            var candidates = new List<SuggestionPayload>();

            if (TryResolveSkillSuggestion(out var skillInfo))
            {
                var skillSuggestionTemplates = CompanionSuggestionDialogueBlocks.SkillSuggestionTemplates;
                for (int i = 0; i < skillSuggestionTemplates.Count; i++)
                {
                    string template = skillSuggestionTemplates[i];
                    if (string.IsNullOrWhiteSpace(template))
                        continue;

                    string text = template.Replace("{skill}", skillInfo.SkillDisplayName);
                    candidates.Add(SuggestionPayload.ForSkill(skillInfo.Skill, skillInfo.SkillDisplayName, text));
                }
            }

            if (TryGetLatestNpcKill(out string latestNpc))
            {
                string plural = FormatNpcPlural(latestNpc);
                candidates.Add(SuggestionPayload.ForNpc(
                    SuggestionType.NpcLatest,
                    latestNpc,
                    CompanionSuggestionDialogueBlocks.NpcLatestTemplate.Replace("{npc}", plural)));
            }

            if (TryGetRandomNpcKill(out string randomNpc))
            {
                string plural = FormatNpcPlural(randomNpc);
                candidates.Add(SuggestionPayload.ForNpc(
                    SuggestionType.NpcHistory,
                    randomNpc,
                    CompanionSuggestionDialogueBlocks.NpcRandomTemplate.Replace("{npc}", plural)));
            }

            if (candidates.Count == 0)
                return false;

            suggestion = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            return suggestion.HasValue;
        }

        private bool TryResolveSkillSuggestion(out SkillSuggestionInfo suggestion)
        {
            suggestion = default;
            DateTime nowUtc = DateTime.UtcNow;

            if (TryGetBestSkillCandidate(nowUtc, out var candidate) &&
                candidate.HasSkill &&
                candidate.Skill.HasValue &&
                !IsSkillUnderDeclineCooldown(candidate.Skill.Value))
            {
                suggestion = new SkillSuggestionInfo(candidate.Skill.Value, SkillNameUtility.GetDisplayName(candidate.Skill.Value));
                return true;
            }

            if (TryBuildFallbackSkillCandidate(nowUtc, out candidate) &&
                candidate.HasSkill &&
                candidate.Skill.HasValue &&
                !IsSkillUnderDeclineCooldown(candidate.Skill.Value))
            {
                suggestion = new SkillSuggestionInfo(candidate.Skill.Value, SkillNameUtility.GetDisplayName(candidate.Skill.Value));
                return true;
            }

            if (TryGetFallbackSkill(out var fallback) && !IsSkillUnderDeclineCooldown(fallback))
            {
                suggestion = new SkillSuggestionInfo(fallback, SkillNameUtility.GetDisplayName(fallback));
                return true;
            }

            return false;
        }

        private bool TryGetFallbackSkill(out SkillType skill)
        {
            var available = new List<SkillType>(SuggestibleSkills.Length);
            for (int i = 0; i < SuggestibleSkills.Length; i++)
            {
                SkillType candidate = SuggestibleSkills[i];
                if (IsSkillUnderDeclineCooldown(candidate))
                    continue;

                available.Add(candidate);
            }

            if (available.Count == 0)
            {
                skill = default;
                return false;
            }

            skill = available[UnityEngine.Random.Range(0, available.Count)];
            return true;
        }

        private string ChooseRepeatResponse(string playerName)
        {
            var repeatResponses = CompanionSuggestionDialogueBlocks.RepeatSuggestionResponses;
            if (repeatResponses == null || repeatResponses.Count == 0)
                return "I've already told you what I want to do.";

            int index = UnityEngine.Random.Range(0, repeatResponses.Count);
            string template = repeatResponses[index];
            if (string.IsNullOrWhiteSpace(template))
                return "I've already told you what I want to do.";

            string resolved = string.IsNullOrWhiteSpace(playerName)
                ? ResolvePlayerName(string.Empty)
                : playerName;

            if (string.IsNullOrWhiteSpace(resolved))
                resolved = "friend";

            return template.Replace("{playerName}", resolved);
        }

        private void HandleNpcKill(NpcCombatant npc)
        {
            if (npc == null)
                return;

            string rawName = npc.Profile != null && !string.IsNullOrWhiteSpace(npc.Profile.name)
                ? npc.Profile.name
                : npc.name;

            string sanitized = SanitizeNpcName(rawName);
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "foe";

            DateTime nowUtc = DateTime.UtcNow;
            RecordNpcKill(sanitized, nowUtc);

            EnsureConversationMemoryBound();
            string playerName = ResolvePlayerName(string.Empty);
            string plural = FormatNpcPlural(sanitized);

            var metadata = CompanionEventMetadata.Create(
                primaryActor: string.IsNullOrWhiteSpace(playerName) ? "You" : playerName,
                secondaryActor: plural,
                worldPosition: npc.transform != null ? (Vector3?)npc.transform.position : null);

            conversationMemory?.RegisterEvent($"defeated {plural}", CompanionEventType.Combat, metadata);
        }

        private void RecordNpcKill(string npcName, DateTime timestampUtc)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return;

            recentNpcKills.AddFirst(new NpcKillRecord(npcName, timestampUtc));
            while (recentNpcKills.Count > MaxTrackedNpcKills)
                recentNpcKills.RemoveLast();

            PruneNpcKills(timestampUtc);
        }

        private void PruneNpcKills(DateTime nowUtc)
        {
            TimeSpan retention = TimeSpan.FromMinutes(Mathf.Max(0.1f, npcKillRetentionMinutes));
            var node = recentNpcKills.Last;
            while (node != null)
            {
                var previous = node.Previous;
                if ((nowUtc - node.Value.TimestampUtc) > retention)
                    recentNpcKills.Remove(node);

                node = previous;
            }
        }

        private bool TryGetLatestNpcKill(out string npcName)
        {
            if (recentNpcKills.Count == 0)
            {
                npcName = string.Empty;
                return false;
            }

            npcName = recentNpcKills.First.Value.Name;
            return !string.IsNullOrWhiteSpace(npcName);
        }

        private bool TryGetRandomNpcKill(out string npcName)
        {
            if (recentNpcKills.Count == 0)
            {
                npcName = string.Empty;
                return false;
            }

            int index = UnityEngine.Random.Range(0, recentNpcKills.Count);
            var node = recentNpcKills.First;
            for (int i = 0; i < index && node != null; i++)
                node = node.Next;

            npcName = node != null ? node.Value.Name : recentNpcKills.First.Value.Name;
            return !string.IsNullOrWhiteSpace(npcName);
        }

        private static string SanitizeNpcName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return string.Empty;

            string trimmed = rawName.Trim();

            if (trimmed.StartsWith("NPC_", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(4);

            int parenIndex = trimmed.IndexOf('(');
            if (parenIndex >= 0)
                trimmed = trimmed.Substring(0, parenIndex);

            trimmed = trimmed.Replace('_', ' ').Trim();

            var builder = new StringBuilder(trimmed.Length);
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsLetter(c) || char.IsWhiteSpace(c) || "-'".IndexOf(c) >= 0)
                    builder.Append(c);
            }

            string cleaned = builder.ToString().Trim();
            if (cleaned.Length == 0)
                return string.Empty;

            string lower = cleaned.ToLower(CultureInfo.InvariantCulture);
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
        }

        private static string FormatNpcPlural(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string trimmed = name.Trim();
            if (trimmed.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("z", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed + "es";
            }

            if (trimmed.EndsWith("y", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Length > 1 &&
                !IsVowel(trimmed[trimmed.Length - 2]))
            {
                return trimmed.Substring(0, trimmed.Length - 1) + "ies";
            }

            return trimmed + "s";
        }

        private static bool IsVowel(char c)
        {
            char lower = char.ToLowerInvariant(c);
            return lower == 'a' || lower == 'e' || lower == 'i' || lower == 'o' || lower == 'u';
        }

        private void EnsureSuggestionStateFresh(DateTime nowUtc)
        {
            if (!HasActiveSuggestion(nowUtc))
                ResetSuggestionState();
        }

        private void ResetSuggestionState()
        {
            lastSuggestionAnsweredUtc = null;
            lastSuggestionMessage = string.Empty;
            lastSuggestionPayload = SuggestionPayload.Empty;
            playerRepeatedSuggestionRequest = false;
            playerNeededSkillReminderApology = false;
        }

        private bool HasActiveSuggestion(DateTime nowUtc)
        {
            return GetSuggestionRemaining(nowUtc).HasValue;
        }

        private TimeSpan? GetSuggestionRemaining(DateTime nowUtc)
        {
            if (!lastSuggestionAnsweredUtc.HasValue)
                return null;

            TimeSpan cooldown = TimeSpan.FromMinutes(Mathf.Max(0.1f, suggestionCooldownMinutes));
            TimeSpan elapsed = nowUtc - lastSuggestionAnsweredUtc.Value;
            if (elapsed >= cooldown)
                return null;

            return cooldown - elapsed;
        }

        private SuggestionDebugState BuildSuggestionDebugState(DateTime nowUtc)
        {
            EnsureSuggestionStateFresh(nowUtc);
            TimeSpan? remaining = GetSuggestionRemaining(nowUtc);
            return new SuggestionDebugState(
                remaining.HasValue,
                playerRepeatedSuggestionRequest,
                remaining,
                lastSuggestionMessage);
        }

        private readonly struct NpcKillRecord
        {
            public NpcKillRecord(string name, DateTime timestampUtc)
            {
                Name = name ?? string.Empty;
                TimestampUtc = timestampUtc;
            }

            public string Name { get; }
            public DateTime TimestampUtc { get; }
        }

        private readonly struct SkillSuggestionInfo
        {
            public SkillSuggestionInfo(SkillType skill, string displayName)
            {
                Skill = skill;
                SkillDisplayName = string.IsNullOrWhiteSpace(displayName)
                    ? SkillNameUtility.GetDisplayName(skill)
                    : displayName.Trim();
            }

            public SkillType Skill { get; }
            public string SkillDisplayName { get; }
        }

        private enum SuggestionType
        {
            None = 0,
            Skill = 1,
            NpcLatest = 2,
            NpcHistory = 3
        }

        private readonly struct SuggestionPayload
        {
            public static SuggestionPayload Empty => new SuggestionPayload(SuggestionType.None, null, string.Empty, string.Empty, string.Empty);

            public SuggestionPayload(
                SuggestionType type,
                SkillType? skill,
                string skillDisplayName,
                string npcName,
                string responseText)
            {
                Type = type;
                Skill = skill;
                SkillDisplayName = skillDisplayName ?? string.Empty;
                NpcName = npcName ?? string.Empty;
                ResponseText = responseText ?? string.Empty;
            }

            public SuggestionType Type { get; }
            public SkillType? Skill { get; }
            public string SkillDisplayName { get; }
            public string NpcName { get; }
            public string ResponseText { get; }
            public bool HasValue => Type != SuggestionType.None && !string.IsNullOrWhiteSpace(ResponseText);

            public static SuggestionPayload ForSkill(SkillType skill, string skillDisplayName, string responseText) =>
                new SuggestionPayload(SuggestionType.Skill, skill, skillDisplayName, string.Empty, responseText);

            public static SuggestionPayload ForNpc(SuggestionType type, string npcName, string responseText) =>
                new SuggestionPayload(type, null, string.Empty, npcName, responseText);
        }

        /// <summary>
        /// Lightweight immutable payload mirrored into the admin tooling so QA can inspect suggestion state.
        /// </summary>
        public readonly struct SuggestionDebugState
        {
            public static SuggestionDebugState Empty => new SuggestionDebugState(false, false, null, string.Empty);

            public SuggestionDebugState(
                bool hasActiveSuggestion,
                bool playerAskedAgain,
                TimeSpan? timeRemaining,
                string lastSuggestion)
            {
                HasActiveSuggestion = hasActiveSuggestion;
                PlayerAskedAgain = playerAskedAgain;
                TimeRemaining = timeRemaining;
                LastSuggestion = lastSuggestion ?? string.Empty;
            }

            public bool HasActiveSuggestion { get; }
            public bool PlayerAskedAgain { get; }
            public TimeSpan? TimeRemaining { get; }
            public string LastSuggestion { get; }
        }

        private void LogRuleMatches(string cleaned, CompanionDialogueParseResult result)
        {
            var orderedTokens = result.UniqueTokens.OrderBy(t => t, StringComparer.Ordinal).ToArray();
            var topMatches = result.Matches
                .OrderByDescending(m => m.Score)
                .Take(3)
                .Select(m => $"{m.Intent} (score={m.Score:F2}, p={m.Priority})");

            Debug.Log($"[CompanionConversationService] '{cleaned}' => top intents [{string.Join(", ", topMatches)}] via tokens [{string.Join(", ", orderedTokens)}].");
        }

        private static string ResolvePlayerName(string sender)
        {
            if (!string.IsNullOrWhiteSpace(sender))
                return sender.Trim();

            var chat = ChatService.Instance;
            return chat != null ? chat.ActiveUsername : "Adventurer";
        }

        private static string CompactWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            bool previousWhitespace = false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWhitespace)
                        builder.Append(' ');

                    previousWhitespace = true;
                    continue;
                }

                builder.Append(c);
                previousWhitespace = false;
            }

            return builder.ToString().Trim();
        }

        private static string NormaliseForParsing(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            bool previousSpace = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                    previousSpace = false;
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (!previousSpace)
                        builder.Append(' ');
                    previousSpace = true;
                }
                else if (c == '?')
                {
                    if (!previousSpace && builder.Length > 0)
                        builder.Append(' ');
                    builder.Append('?');
                    previousSpace = false;
                }
                else
                {
                    // skip punctuation
                }
            }

            return builder.ToString().Trim();
        }

        private void BuildRuleProfile()
        {
            rules.Clear();

            var defaultRules = CompanionDialoguePatterns.CreateDefaultProfile();
            EnsureThresholdEntries(defaultRules);

            for (int i = 0; i < defaultRules.Count; i++)
            {
                var rule = defaultRules[i];
                float threshold = ResolveThreshold(rule.Intent, rule.MatchThreshold);
                rule.OverrideMatchThreshold(threshold);
                rules.Add(rule);
            }
        }

        private void EnsureThresholdEntries(IReadOnlyList<CompanionDialogueRule> defaults)
        {
            if (intentScoreThresholds == null)
                intentScoreThresholds = new List<IntentScoreThreshold>();

            var seen = new HashSet<CompanionDialogueIntent>();
            for (int i = intentScoreThresholds.Count - 1; i >= 0; i--)
            {
                var entry = intentScoreThresholds[i];
                if (!seen.Add(entry.Intent))
                {
                    intentScoreThresholds.RemoveAt(i);
                    continue;
                }

                entry.Clamp();
                intentScoreThresholds[i] = entry;
            }

            for (int i = 0; i < defaults.Count; i++)
            {
                var rule = defaults[i];
                bool found = false;
                for (int j = 0; j < intentScoreThresholds.Count; j++)
                {
                    if (intentScoreThresholds[j].Intent == rule.Intent)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    intentScoreThresholds.Add(new IntentScoreThreshold(rule.Intent, Mathf.Max(0f, rule.MatchThreshold)));
                }
            }
        }

        private float ResolveThreshold(CompanionDialogueIntent intent, float defaultValue)
        {
            if (intentScoreThresholds != null)
            {
                for (int i = 0; i < intentScoreThresholds.Count; i++)
                {
                    if (intentScoreThresholds[i].Intent != intent)
                        continue;

                    var entry = intentScoreThresholds[i];
                    entry.Clamp();
                    intentScoreThresholds[i] = entry;
                    return entry.Threshold;
                }
            }

            return Mathf.Max(0f, defaultValue);
        }

        private readonly struct SkillLevelQueryResult
        {
            public static SkillLevelQueryResult Empty => new SkillLevelQueryResult(
                null,
                string.Empty,
                string.Empty,
                null);

            public SkillLevelQueryResult(
                SkillType? skill,
                string skillDisplayName,
                string skillSentenceName,
                int? skillLevel)
            {
                Skill = skill;
                SkillDisplayName = skillDisplayName ?? string.Empty;
                SkillSentenceName = skillSentenceName ?? string.Empty;
                SkillLevel = skillLevel;
            }

            public SkillType? Skill { get; }

            public string SkillDisplayName { get; }

            public string SkillSentenceName { get; }

            public int? SkillLevel { get; }

            public bool HasSkill => Skill.HasValue;

            public bool HasSkillLevel => SkillLevel.HasValue;

            public bool HasSkillName => !string.IsNullOrWhiteSpace(SkillDisplayName) || !string.IsNullOrWhiteSpace(SkillSentenceName);
        }

        private readonly struct SkillProposalAnalysis
        {
            public static SkillProposalAnalysis Empty => new SkillProposalAnalysis(
                hasProposal: false,
                skill: null,
                skillDisplayName: string.Empty,
                skillSentenceName: string.Empty,
                rawMessage: string.Empty,
                tokens: Array.Empty<string>());

            public SkillProposalAnalysis(
                bool hasProposal,
                SkillType? skill,
                string skillDisplayName,
                string skillSentenceName,
                string rawMessage,
                IReadOnlyList<string> tokens)
            {
                HasProposal = hasProposal;
                Skill = skill;
                SkillDisplayName = skillDisplayName ?? string.Empty;
                SkillSentenceName = skillSentenceName ?? string.Empty;
                RawMessage = rawMessage ?? string.Empty;
                Tokens = tokens ?? Array.Empty<string>();
            }

            public bool HasProposal { get; }

            public SkillType? Skill { get; }

            public bool HasConcreteSkill => Skill.HasValue;

            public string SkillDisplayName { get; }

            public string SkillSentenceName { get; }

            public string RawMessage { get; }

            public IReadOnlyList<string> Tokens { get; }

            public bool HasDisplayName => !string.IsNullOrWhiteSpace(SkillDisplayName);
        }

        private readonly struct TokenSkillMapping
        {
            public TokenSkillMapping(float weight, string fallbackName, string fallbackSentence, params SkillType[] skills)
            {
                Weight = Mathf.Max(0f, weight);
                FallbackName = fallbackName ?? string.Empty;
                FallbackSentence = fallbackSentence ?? string.Empty;
                Skills = skills ?? Array.Empty<SkillType>();
            }

            public float Weight { get; }

            public string FallbackName { get; }

            public string FallbackSentence { get; }

            public SkillType[] Skills { get; }

            public bool HasSkills => Skills.Length > 0;

            public static TokenSkillMapping ForSkills(float weight, params SkillType[] skills)
            {
                return new TokenSkillMapping(weight, string.Empty, string.Empty, skills);
            }

            public static TokenSkillMapping ForFallback(float weight, string fallbackName, string fallbackSentence)
            {
                return new TokenSkillMapping(weight, fallbackName, fallbackSentence);
            }
        }

        private enum SkillToolState
        {
            Ready = 0,
            Missing = 1
        }

        private readonly struct ToolAvailabilityResult
        {
            private ToolAvailabilityResult(
                SkillToolState state,
                string specificToolName,
                string genericToolName,
                bool isEquipped,
                string missingHint)
            {
                State = state;
                SpecificToolName = specificToolName ?? string.Empty;
                GenericToolName = genericToolName ?? string.Empty;
                IsEquipped = isEquipped;
                MissingHint = missingHint ?? string.Empty;
            }

            public SkillToolState State { get; }

            public string SpecificToolName { get; }

            public string GenericToolName { get; }

            public bool IsEquipped { get; }

            public string MissingHint { get; }

            public bool HasSpecificTool => !string.IsNullOrWhiteSpace(SpecificToolName);

            public static ToolAvailabilityResult Ready(string specificToolName, string genericToolName, bool isEquipped)
            {
                return new ToolAvailabilityResult(SkillToolState.Ready, specificToolName, genericToolName, isEquipped, string.Empty);
            }

            public static ToolAvailabilityResult Missing(string specificToolName, string genericToolName, string hint)
            {
                return new ToolAvailabilityResult(SkillToolState.Missing, specificToolName, genericToolName, false, hint);
            }

            public static ToolAvailabilityResult AssumedReady(string genericToolName)
            {
                return new ToolAvailabilityResult(SkillToolState.Ready, string.Empty, genericToolName, false, string.Empty);
            }
        }

        private readonly struct SkillQuestionCandidate
        {
            public static SkillQuestionCandidate Empty => new SkillQuestionCandidate(null, string.Empty, string.Empty, DateTime.UtcNow);

            public SkillQuestionCandidate(SkillType? skill, string skillName, string description, DateTime timestampUtc)
            {
                Skill = skill;
                SkillName = skillName ?? string.Empty;
                Description = description ?? string.Empty;
                TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
            }

            public SkillType? Skill { get; }

            public string SkillName { get; }

            public string Description { get; }

            public DateTime TimestampUtc { get; }

            public bool HasSkill => Skill.HasValue;

            public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

            public bool IsValid => HasSkill || HasDescription;

            public static SkillQuestionCandidate CreateForSkill(SkillType skill, string skillName, string description, DateTime timestampUtc)
            {
                return new SkillQuestionCandidate(skill, skillName, description, timestampUtc);
            }

            public static SkillQuestionCandidate CreateFromDescription(string description, DateTime timestampUtc)
            {
                return new SkillQuestionCandidate(null, string.Empty, description, timestampUtc);
            }
        }

        private readonly struct ActiveSkillQuestion
        {
            public static ActiveSkillQuestion Empty => new ActiveSkillQuestion(false);

            private ActiveSkillQuestion(bool _)
            {
                Candidate = SkillQuestionCandidate.Empty;
                TemplateKey = string.Empty;
                TimestampUtc = DateTime.MinValue;
                isActive = false;
            }

            public ActiveSkillQuestion(SkillQuestionCandidate candidate, string templateKey, DateTime timestampUtc)
            {
                Candidate = candidate;
                TemplateKey = templateKey ?? string.Empty;
                TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
                isActive = true;
            }

            private readonly bool isActive;

            public SkillQuestionCandidate Candidate { get; }

            public string TemplateKey { get; }

            public DateTime TimestampUtc { get; }

            public bool IsActive => isActive;

            public bool IsExpired(TimeSpan duration, DateTime nowUtc)
            {
                if (!isActive)
                    return false;

                return (nowUtc - TimestampUtc) >= duration;
            }

            public SkillQuestionCandidate? TryGetCandidate()
            {
                return isActive ? Candidate : (SkillQuestionCandidate?)null;
            }
        }

        private readonly struct SkillActionRecord
        {
            public SkillActionRecord(string description, DateTime timestampUtc)
            {
                Description = description ?? string.Empty;
                TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc
                    ? timestampUtc
                    : timestampUtc.ToUniversalTime();
            }

            public string Description { get; }

            public DateTime TimestampUtc { get; }
        }

        private readonly struct PendingResponse
        {
            public PendingResponse(
                string text,
                string statusSegment,
                IReadOnlyList<string> followUps = null,
                CompanionDialogueIntent? intent = null)
            {
                Text = text ?? string.Empty;
                StatusSegment = statusSegment ?? string.Empty;
                FollowUpSegments = followUps ?? Array.Empty<string>();
                Intent = intent;
            }

            public string Text { get; }

            public string StatusSegment { get; }

            public IReadOnlyList<string> FollowUpSegments { get; }

            public CompanionDialogueIntent? Intent { get; }
        }

        [Serializable]
        private struct IntentScoreThreshold
        {
            [SerializeField]
            private CompanionDialogueIntent intent;

            [SerializeField]
            private float threshold;

            public IntentScoreThreshold(CompanionDialogueIntent intent, float threshold)
            {
                this.intent = intent;
                this.threshold = threshold;
            }

            public CompanionDialogueIntent Intent => intent;

            public float Threshold => threshold;

            public void Clamp()
            {
                threshold = Mathf.Max(0f, threshold);
            }
        }
    }
}
