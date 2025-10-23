using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Combat;
using Companions;
using UI.Chat;
using UnityEngine;
using World;
using Skills;

namespace Companions.Conversation
{
    /// <summary>
    /// Persistent service that listens to the companion chat channel, analyses player-authored messages,
    /// and orchestrates context-aware companion responses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionConversationService : SceneGatedSingletonBehaviour<CompanionConversationService>
    {
        private static readonly Dictionary<string, string> PlayerMoodLookup = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "tired", "feeling tired" },
            { "sleepy", "a bit sleepy" },
            { "exhausted", "pretty exhausted" },
            { "drained", "drained" },
            { "sad", "a bit down" },
            { "angry", "a little fired up" },
            { "upset", "upset" },
            { "annoyed", "annoyed" },
            { "frustrated", "frustrated" },
            { "good", "doing good" },
            { "great", "feeling great" },
            { "awesome", "feeling awesome" },
            { "okay", "doing okay" },
            { "ok", "doing okay" },
            { "fine", "feeling fine" },
            { "happy", "happy" },
            { "excited", "excited" },
            { "pumped", "pumped" },
            { "nervous", "a little nervous" },
            { "worried", "a bit worried" }
        };

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
            new IntentScoreThreshold(CompanionDialogueIntent.StatusQuery, 2.4f),
            new IntentScoreThreshold(CompanionDialogueIntent.PlayerMoodReport, 2.2f),
            new IntentScoreThreshold(CompanionDialogueIntent.Gratitude, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.Farewell, 1.4f),
            new IntentScoreThreshold(CompanionDialogueIntent.Compliment, 1.8f),
            new IntentScoreThreshold(CompanionDialogueIntent.RequestAssistance, 1.8f),
            new IntentScoreThreshold(CompanionDialogueIntent.AcknowledgeRecentEvent, 1.6f)
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

        private const int MaxTrackedSkillActions = 6;
        private static readonly TimeSpan SkillActionRetention = TimeSpan.FromMinutes(15);

        private bool ResponseRoutineActive => responseRoutine != null;

        private bool ShouldTraceRules => CompanionManager.EnableDebugLogging && enableRuleTracing;

        private bool ShouldTraceResponses => CompanionManager.EnableDebugLogging && enableResponseTracing;

        private bool ShouldTraceMemory => CompanionManager.EnableDebugLogging && enableMemoryTracing;

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
        }

        /// <inheritdoc />
        protected override void OnSingletonDestroyed()
        {
            UnsubscribeFromChat();

            SceneTransitionManager.TransitionCompleted -= HandleSceneTransitionCompleted;
            UnbindPlayerContext();

            if (chatSubscriptionRoutine != null)
                StopCoroutine(chatSubscriptionRoutine);

            if (responseRoutine != null)
                StopCoroutine(responseRoutine);

            pendingResponses.Clear();

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

        private void RecordSkillAction(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return;

            var record = new SkillActionRecord(description.Trim(), DateTime.UtcNow);
            recentSkillActions.AddFirst(record);

            while (recentSkillActions.Count > MaxTrackedSkillActions)
                recentSkillActions.RemoveLast();

            PruneSkillActions(DateTime.UtcNow);
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
        }

        private void HandleMessageReceived(ChatMessage message)
        {
            if (message.Channel != ChatChannel.Companion)
                return;

            if (!message.IsLocalPlayerAuthor)
                return;

            EnsurePlayerContextBindings();

            string cleaned = NormaliseForParsing(message.Text);
            if (string.IsNullOrEmpty(cleaned))
                return;

            var parseResult = parser.Parse(cleaned);
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

                        pendingResponses.Enqueue(new PendingResponse(followUp, string.Empty, string.Empty));
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

            string companionName = CompanionManager.GetCompanionDisplayName();
            chat.PublishCompanionMessage(companionName, response.Text);

            if (!string.IsNullOrWhiteSpace(response.StatusSegment) && conversationMemory != null)
            {
                conversationMemory.RegisterStatusResponse(response.StatusSegment, DateTime.UtcNow);
                if (ShouldTraceMemory)
                    Debug.Log($"[CompanionConversationService] Registered status response '{response.StatusSegment}'.");
            }

            if (!string.IsNullOrWhiteSpace(response.PlayerMood) && conversationMemory != null)
            {
                conversationMemory.SetLastKnownPlayerMood(response.PlayerMood, DateTime.UtcNow);
                if (ShouldTraceMemory)
                    Debug.Log($"[CompanionConversationService] Stored last known player mood '{response.PlayerMood}'.");
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

            string playerMoodFromMemory = conversationMemory != null ? conversationMemory.LastKnownPlayerMood : string.Empty;
            string detectedPlayerMood = string.Empty;
            string statusSegment = string.Empty;
            string companionMood = ResolveCompanionMoodDescriptor();
            string recentEvent = ResolveRecentEventSummary();
            var context = BuildResponseContext();

            for (int i = 0; i < parseResult.Matches.Count; i++)
            {
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
                            playerMoodFromMemory,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.StatusQuery:
                        statusSegment = BuildStatusSegment(
                            playerName,
                            playerMoodFromMemory,
                            ref companionMood,
                            context,
                            followUps);
                        if (!string.IsNullOrEmpty(statusSegment))
                            segments.Add(statusSegment);
                        break;

                    case CompanionDialogueIntent.PlayerMoodReport:
                        detectedPlayerMood = DetectPlayerMood(parseResult.Tokens);
                        string acknowledgedMood = !string.IsNullOrEmpty(detectedPlayerMood)
                            ? detectedPlayerMood
                            : playerMoodFromMemory;

                        TryAddResponse(
                            CompanionDialogueIntent.PlayerMoodReport,
                            context,
                            segments,
                            followUps,
                            playerName,
                            acknowledgedMood,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.Gratitude:
                        TryAddResponse(
                            CompanionDialogueIntent.Gratitude,
                            context,
                            segments,
                            followUps,
                            playerName,
                            playerMoodFromMemory,
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
                            playerMoodFromMemory,
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
                            playerMoodFromMemory,
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
                            playerMoodFromMemory,
                            companionMood,
                            recentEvent);
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
                                playerMoodFromMemory,
                                companionMood,
                                recentEvent);
                        }
                        break;
                }
            }

            if (segments.Count == 0)
                return null;

            string text = CombineSegments(segments);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            IReadOnlyList<string> followUpPayload = followUps.Count > 0 ? followUps : null;
            return new PendingResponse(text, statusSegment, detectedPlayerMood, followUpPayload);
        }

        private void TryAddResponse(
            CompanionDialogueIntent intent,
            CompanionResponseContext context,
            List<string> segments,
            List<string> followUps,
            string playerName,
            string playerMood,
            string companionMood,
            string recentEvent)
        {
            if (!responseLibrary.TrySelectResponse(intent, context, null, out var selection))
                return;

            string formatted = FormatTemplate(
                selection.PrimarySegment,
                playerName,
                playerMood,
                companionMood,
                recentEvent,
                context);

            if (string.IsNullOrWhiteSpace(formatted))
                return;

            segments.Add(formatted);
            AppendFollowUps(selection.FollowUpSegments, followUps, playerName, playerMood, companionMood, recentEvent, context);
        }

        private void AppendFollowUps(
            IReadOnlyList<string> followUpSegments,
            List<string> collector,
            string playerName,
            string playerMood,
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
                    playerMood,
                    companionMood,
                    recentEvent,
                    context);

                if (!string.IsNullOrWhiteSpace(formatted))
                    collector.Add(formatted);
            }
        }

        private string BuildStatusSegment(
            string playerName,
            string playerMood,
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
                    playerMood,
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
                        playerMood,
                        companionMood,
                        string.Empty,
                        context);
                }

                if (!string.IsNullOrEmpty(playerMood))
                    formatted = AppendSentence(formatted, $"Hope you're {playerMood}.");
                else if (conversationMemory != null && !string.IsNullOrEmpty(conversationMemory.LastKnownPlayerMood))
                    formatted = AppendSentence(formatted, $"Still keeping an eye on you being {conversationMemory.LastKnownPlayerMood}.");

                AppendFollowUps(selection.FollowUpSegments, followUps, playerName, playerMood, companionMood, string.Empty, context);
                lastStatusTemplateKey = selection.TemplateKey;
                return formatted;
            }

            string fallback = FormatTemplate(
                "I'm feeling {companionMood}. How are you doing?",
                playerName,
                playerMood,
                companionMood,
                string.Empty,
                context);

            if (!string.IsNullOrEmpty(playerMood))
                fallback = AppendSentence(fallback, $"Hope you're {playerMood}.");
            else if (conversationMemory != null && !string.IsNullOrEmpty(conversationMemory.LastKnownPlayerMood))
                fallback = AppendSentence(fallback, $"Still keeping an eye on you being {conversationMemory.LastKnownPlayerMood}.");

            lastStatusTemplateKey = string.Empty;
            return fallback;
        }

        private CompanionResponseContext BuildResponseContext()
        {
            DateTime nowUtc = DateTime.UtcNow;
            var recentSkills = ResolveRecentSkillActions(nowUtc);
            string timeOfDay = ResolveTimeOfDayDescriptor(nowUtc);
            bool companionInCombat = false;

            return new CompanionResponseContext(
                nowUtc,
                timeOfDay,
                playerInCombat,
                companionInCombat,
                recentSkills,
                pendingResponses.Count);
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

            if (!string.IsNullOrWhiteSpace(conversationMemory.LastStatusResponse))
                return conversationMemory.LastStatusResponse;

            var entries = conversationMemory.GetRecentEntries(6);
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry.Speaker == CompanionConversationMemory.Speaker.Companion && !string.IsNullOrWhiteSpace(entry.Message))
                    return entry.Message;
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
            string playerMood,
            string companionMood,
            string recentEvent,
            CompanionResponseContext context)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            string companionName = CompanionManager.GetCompanionDisplayName();
            string resolvedPlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName;
            string resolvedPlayerMood = string.IsNullOrWhiteSpace(playerMood) ? "doing alright" : playerMood;
            string resolvedCompanionMood = string.IsNullOrWhiteSpace(companionMood) ? "ready" : companionMood;
            string resolvedEvent = string.IsNullOrWhiteSpace(recentEvent) ? "that" : recentEvent;
            string resolvedTimeOfDay = string.IsNullOrWhiteSpace(context.TimeOfDayLabel) ? "day" : context.TimeOfDayLabel;
            string resolvedCombatState = string.IsNullOrWhiteSpace(context.CombatStateDescriptor)
                ? "standing down"
                : context.CombatStateDescriptor;
            string resolvedSkillAction = context.HasRecentSkillActions
                ? context.LatestSkillAction
                : "keeping skills sharp";

            string result = template
                .Replace("{playerName}", resolvedPlayerName)
                .Replace("{companionName}", string.IsNullOrWhiteSpace(companionName) ? "Companion" : companionName)
                .Replace("{playerMood}", resolvedPlayerMood)
                .Replace("{companionMood}", resolvedCompanionMood)
                .Replace("{recentEvent}", resolvedEvent)
                .Replace("{timeOfDay}", resolvedTimeOfDay)
                .Replace("{combatState}", resolvedCombatState)
                .Replace("{recentSkillAction}", resolvedSkillAction);

            return CompactWhitespace(result);
        }

        private string DetectPlayerMood(IReadOnlyList<string> tokens)
        {
            if (tokens == null || tokens.Count == 0)
                return string.Empty;

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrEmpty(token))
                    continue;

                if (PlayerMoodLookup.TryGetValue(token, out string mood))
                    return mood;

                string stemmed = StemTokenForLookup(token);
                if (!string.IsNullOrEmpty(stemmed) && PlayerMoodLookup.TryGetValue(stemmed, out mood))
                    return mood;

                if (token == "not" && i + 1 < tokens.Count)
                {
                    string next = tokens[i + 1];
                    if (PlayerMoodLookup.TryGetValue(next, out string nextMood))
                        return $"not {nextMood}";

                    if (next == "bad")
                        return "doing not bad";
                }
            }

            return string.Empty;
        }

        private static string StemTokenForLookup(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            string result = token;

            if (result.Length > 4 && result.EndsWith("ing", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 3);
            else if (result.Length > 5 && result.EndsWith("ness", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 4);
            else if (result.Length > 4 && result.EndsWith("ly", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 2);
            else if (result.Length > 3 && result.EndsWith("ed", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 2);

            if (result.Length < 2 || string.Equals(result, token, StringComparison.Ordinal))
                return string.Empty;

            return result;
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
                string playerMood,
                IReadOnlyList<string> followUps = null)
            {
                Text = text ?? string.Empty;
                StatusSegment = statusSegment ?? string.Empty;
                PlayerMood = playerMood ?? string.Empty;
                FollowUpSegments = followUps;
            }

            public string Text { get; }

            public string StatusSegment { get; }

            public string PlayerMood { get; }

            public IReadOnlyList<string> FollowUpSegments { get; }
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
