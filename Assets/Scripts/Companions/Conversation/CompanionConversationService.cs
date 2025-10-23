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
using Util;

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
            new IntentScoreThreshold(CompanionDialogueIntent.StatusQuery, 2.2f),
            new IntentScoreThreshold(CompanionDialogueIntent.PlayerMoodReport, 2.2f),
            new IntentScoreThreshold(CompanionDialogueIntent.Gratitude, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.Farewell, 1.4f),
            new IntentScoreThreshold(CompanionDialogueIntent.Compliment, 1.8f),
            new IntentScoreThreshold(CompanionDialogueIntent.RequestAssistance, 1.8f),
            new IntentScoreThreshold(CompanionDialogueIntent.AcknowledgeRecentEvent, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.ProactiveSkillQuestion, 1f),
            new IntentScoreThreshold(CompanionDialogueIntent.AcceptSkillPlan, 1.7f),
            new IntentScoreThreshold(CompanionDialogueIntent.DeclineSkillPlan, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.DeferSkillPlan, 1.6f),
            new IntentScoreThreshold(CompanionDialogueIntent.RequestAlternateSkill, 1.6f)
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

        [Header("Mood Follow Ups")]
        [SerializeField, Tooltip("Minimum minutes between proactive mood check-ins when the player feels down.")]
        private float moodFollowUpCooldownMinutes = 2.5f;

        [Header("Proactive Skill Scheduler")]
        [SerializeField, Tooltip("Number of idle ticks before the companion considers prompting about a skill."), Min(1)]
        private int proactiveIdleTickThreshold = 6;

        [SerializeField, Tooltip("Cooldown applied between proactive skill questions (minutes)."), Min(0.1f)]
        private float proactiveQuestionCooldownMinutes = 6f;

        [SerializeField, Tooltip("Maximum age (in minutes) of a skill event that can seed a proactive prompt."), Min(0.1f)]
        private float minimumSkillEventFreshnessMinutes = 12f;

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
        private readonly LinkedList<SkillQuestionCandidate> skillQuestionCandidates = new LinkedList<SkillQuestionCandidate>();
        private int idleTickCounter;
        private bool tickerSubscribed;
        private Coroutine tickerSubscriptionRoutine;
        private DateTime? lastCompanionCombatUtc;

        private const int MaxTrackedSkillActions = 6;
        private const int MaxSkillQuestionCandidates = 6;
        private static readonly TimeSpan SkillActionRetention = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan CompanionCombatActivityWindow = TimeSpan.FromSeconds(5);

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
            tickerSubscriptionRoutine = null;
            tickerSubscribed = false;
            lastCompanionCombatUtc = null;

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
            PruneSkillActions(nowUtc);
            ExpireActiveSkillQuestionIfStale(nowUtc);
            MaybeScheduleProactiveQuestion(nowUtc);
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

            if (ResponseRoutineActive || pendingResponses.Count > 0)
            {
                idleTickCounter = 0;
                return;
            }

            if (playerInCombat || IsCompanionInCombat(nowUtc))
            {
                idleTickCounter = 0;
                return;
            }

            if (activeSkillQuestion.IsActive)
            {
                idleTickCounter = 0;
                return;
            }

            if (conversationMemory != null && conversationMemory.LastQuestionUtc.HasValue)
            {
                double minutesSinceQuestion = (nowUtc - conversationMemory.LastQuestionUtc.Value).TotalMinutes;
                if (minutesSinceQuestion < Mathf.Max(0.1f, proactiveQuestionCooldownMinutes))
                {
                    idleTickCounter = 0;
                    return;
                }
            }

            if (lastProactiveQuestionUtc.HasValue)
            {
                double minutesSincePrompt = (nowUtc - lastProactiveQuestionUtc.Value).TotalMinutes;
                if (minutesSincePrompt < Mathf.Max(0.1f, proactiveQuestionCooldownMinutes))
                {
                    idleTickCounter = 0;
                    return;
                }
            }

            idleTickCounter++;
            if (idleTickCounter < Mathf.Max(1, proactiveIdleTickThreshold))
                return;

            if (!TryGetBestSkillCandidate(nowUtc, out var candidate))
            {
                if (!TryBuildFallbackSkillCandidate(nowUtc, out candidate))
                    return;
            }

            ScheduleSkillQuestion(candidate, nowUtc);
            idleTickCounter = 0;
        }

        private bool TryGetBestSkillCandidate(DateTime nowUtc, out SkillQuestionCandidate candidate, SkillType? excludeSkill = null)
        {
            PruneSkillQuestionCandidates(nowUtc);

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

        private void ScheduleSkillQuestion(SkillQuestionCandidate candidate, DateTime nowUtc)
        {
            EnsureConversationMemoryBound();

            SkillQuestionCandidate? optionalCandidate = candidate.IsValid ? candidate : (SkillQuestionCandidate?)null;
            var context = BuildResponseContext(optionalCandidate);

            if (!responseLibrary.TrySelectResponse(
                    CompanionDialogueIntent.ProactiveSkillQuestion,
                    context,
                    lastProactiveQuestionTemplateKey,
                    out var selection))
            {
                return;
            }

            string playerName = ResolvePlayerName(string.Empty);
            string companionMood = ResolveCompanionMoodDescriptor();
            string formatted = FormatTemplate(
                selection.PrimarySegment,
                playerName,
                string.Empty,
                companionMood,
                string.Empty,
                context);

            formatted = EnsureQuestionMark(formatted);
            if (string.IsNullOrWhiteSpace(formatted))
                return;

            pendingResponses.Enqueue(new PendingResponse(formatted, string.Empty, CompanionMoodInterpretation.Empty));
            if (!ResponseRoutineActive)
                responseRoutine = StartCoroutine(DrainResponseQueue());

            idleTickCounter = 0;
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

            lastPlayerMessage = message.Text ?? string.Empty;
            lastPlayerMessageUtc = DateTime.UtcNow;
            idleTickCounter = 0;

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
            idleTickCounter = 0;
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
                            new PendingResponse(followUp, string.Empty, CompanionMoodInterpretation.Empty));
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
            string companionName = CompanionManager.GetCompanionDisplayName();
            chat.PublishCompanionMessage(companionName, response.Text);

            if (!string.IsNullOrWhiteSpace(response.StatusSegment) && conversationMemory != null)
            {
                conversationMemory.RegisterStatusResponse(response.StatusSegment, DateTime.UtcNow);
                if (ShouldTraceMemory)
                    Debug.Log($"[CompanionConversationService] Registered status response '{response.StatusSegment}'.");
            }

            if (conversationMemory != null)
            {
                if (response.PlayerMood.HasMood)
                {
                    conversationMemory.SetLastKnownPlayerMood(response.PlayerMood, DateTime.UtcNow);
                    if (ShouldTraceMemory)
                    {
                        Debug.Log(
                            $"[CompanionConversationService] Stored last known player mood '{response.PlayerMood.Descriptor}' " +
                            $"(valence={response.PlayerMood.Valence}, intensity={response.PlayerMood.Intensity}).");
                    }
                }

                if (response.ShouldRecordMoodFollowUp)
                {
                    conversationMemory.RegisterMoodFollowUp(DateTime.UtcNow);
                    if (ShouldTraceMemory)
                        Debug.Log("[CompanionConversationService] Logged mood follow-up timestamp.");
                }
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

            var memoryMood = conversationMemory != null ? conversationMemory.LastKnownPlayerMood : CompanionMoodInterpretation.Empty;
            var detectedMood = CompanionMoodInterpretation.Empty;
            var empathyMood = CompanionMoodInterpretation.Empty;
            string statusSegment = string.Empty;
            string companionMood = ResolveCompanionMoodDescriptor();
            string recentEvent = ResolveRecentEventSummary();
            var context = BuildResponseContext(activeSkillQuestion.TryGetCandidate());

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
                            memoryMood.Descriptor,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.StatusQuery:
                        var moodForStatus = detectedMood.HasMood ? detectedMood : memoryMood;
                        statusSegment = BuildStatusSegment(
                            playerName,
                            moodForStatus,
                            ref companionMood,
                            context,
                            followUps);
                        if (!string.IsNullOrEmpty(statusSegment))
                        {
                            segments.Add(statusSegment);
                            empathyMood = SelectDominantMood(empathyMood, moodForStatus);
                        }
                        break;

                    case CompanionDialogueIntent.PlayerMoodReport:
                        detectedMood = DetectPlayerMood(parseResult.Tokens);
                        var acknowledgementMood = detectedMood.HasMood ? detectedMood : memoryMood;

                        if (acknowledgementMood.HasMood)
                        {
                            TryAddResponse(
                                CompanionDialogueIntent.PlayerMoodReport,
                                context,
                                segments,
                                followUps,
                                playerName,
                                acknowledgementMood.Descriptor,
                                companionMood,
                                recentEvent);
                            empathyMood = SelectDominantMood(empathyMood, acknowledgementMood);
                        }
                        break;

                    case CompanionDialogueIntent.Gratitude:
                        TryAddResponse(
                            CompanionDialogueIntent.Gratitude,
                            context,
                            segments,
                            followUps,
                            playerName,
                            memoryMood.Descriptor,
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
                            memoryMood.Descriptor,
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
                            memoryMood.Descriptor,
                            companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.AcceptSkillPlan:
                    case CompanionDialogueIntent.DeclineSkillPlan:
                    case CompanionDialogueIntent.DeferSkillPlan:
                    case CompanionDialogueIntent.RequestAlternateSkill:
                        TryHandleSkillPlanIntent(
                            match.Intent,
                            context,
                            segments,
                            followUps,
                            playerName,
                            memoryMood.Descriptor,
                            ref companionMood,
                            recentEvent);
                        break;

                    case CompanionDialogueIntent.RequestAssistance:
                        TryAddResponse(
                            CompanionDialogueIntent.RequestAssistance,
                            context,
                            segments,
                            followUps,
                            playerName,
                            memoryMood.Descriptor,
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
                                memoryMood.Descriptor,
                                companionMood,
                                recentEvent);
                        }
                        break;
                }
            }

            if (segments.Count == 0)
                return null;

            var moodForEmpathy = SelectDominantMood(empathyMood, detectedMood);
            if (!moodForEmpathy.HasMood)
                moodForEmpathy = memoryMood;

            if (moodForEmpathy.HasMood)
                AppendMoodEmpathySegment(moodForEmpathy, segments);

            bool recordFollowUp = MaybeQueueMoodFollowUp(detectedMood, memoryMood, moodForEmpathy, playerName, segments, followUps);

            string text = CombineSegments(segments);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            IReadOnlyList<string> followUpPayload = followUps.Count > 0 ? followUps : null;
            var capturedMood = detectedMood.HasMood ? detectedMood : CompanionMoodInterpretation.Empty;
            return new PendingResponse(text, statusSegment, capturedMood, followUpPayload, recordFollowUp);
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

        private bool TryHandleSkillPlanIntent(
            CompanionDialogueIntent intent,
            CompanionResponseContext context,
            List<string> segments,
            List<string> followUps,
            string playerName,
            string playerMood,
            ref string companionMood,
            string recentEvent)
        {
            if (!responseLibrary.TrySelectResponse(intent, context, null, out var selection))
                return false;

            string formatted = FormatTemplate(
                selection.PrimarySegment,
                playerName,
                playerMood,
                companionMood,
                recentEvent,
                context);

            if (string.IsNullOrWhiteSpace(formatted))
                return false;

            segments.Add(formatted);
            AppendFollowUps(selection.FollowUpSegments, followUps, playerName, playerMood, companionMood, recentEvent, context);
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
            CompanionMoodInterpretation playerMood,
            ref string companionMood,
            CompanionResponseContext context,
            List<string> followUps)
        {
            string lastStatus = conversationMemory != null ? conversationMemory.LastStatusResponse : string.Empty;
            const int MaxAttempts = 3;
            string playerMoodDescriptor = playerMood.HasMood ? playerMood.Descriptor : string.Empty;

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
                    playerMoodDescriptor,
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
                        playerMoodDescriptor,
                        companionMood,
                        string.Empty,
                        context);
                }

                string hopeLine = BuildMoodHopeLine(playerMood);
                if (string.IsNullOrEmpty(hopeLine) && conversationMemory != null)
                    hopeLine = BuildMoodHopeLine(conversationMemory.LastKnownPlayerMood);

                if (!string.IsNullOrEmpty(hopeLine))
                    formatted = AppendSentence(formatted, hopeLine);

                AppendFollowUps(selection.FollowUpSegments, followUps, playerName, playerMoodDescriptor, companionMood, string.Empty, context);
                lastStatusTemplateKey = selection.TemplateKey;
                return formatted;
            }

            string fallback = FormatTemplate(
                "I'm feeling {companionMood}. How are you doing?",
                playerName,
                playerMoodDescriptor,
                companionMood,
                string.Empty,
                context);

            string fallbackHope = BuildMoodHopeLine(playerMood);
            if (string.IsNullOrEmpty(fallbackHope) && conversationMemory != null)
                fallbackHope = BuildMoodHopeLine(conversationMemory.LastKnownPlayerMood);

            if (!string.IsNullOrEmpty(fallbackHope))
                fallback = AppendSentence(fallback, fallbackHope);

            lastStatusTemplateKey = string.Empty;
            return fallback;
        }

        private void AppendMoodEmpathySegment(CompanionMoodInterpretation mood, List<string> segments)
        {
            if (segments == null || !mood.HasMood)
                return;

            string empathyLine;
            switch (mood.Valence)
            {
                case CompanionMoodValence.Negative:
                    empathyLine = mood.Intensity switch
                    {
                        CompanionMoodIntensity.High => $"That sounds rough being {mood.Descriptor}. I'm right here if you need to slow down.",
                        CompanionMoodIntensity.Medium => $"I'll keep watch while you're {mood.Descriptor}. Call it if you need a breather.",
                        _ => $"Take it easy while you're {mood.Descriptor}; I'll cover the small stuff."
                    };
                    break;
                case CompanionMoodValence.Positive:
                    empathyLine = mood.Intensity switch
                    {
                        CompanionMoodIntensity.High => $"Love the spark from you being {mood.Descriptor}! Let's ride it while it lasts.",
                        CompanionMoodIntensity.Medium => $"Great hearing you're {mood.Descriptor}. I'll match that energy.",
                        _ => $"Glad you're {mood.Descriptor}. Let's keep things steady."
                    };
                    break;
                default:
                    empathyLine = $"Thanks for letting me know you're {mood.Descriptor}. I'm keeping tabs.";
                    break;
            }

            if (!string.IsNullOrWhiteSpace(empathyLine))
                segments.Add(empathyLine);
        }

        private string BuildMoodHopeLine(CompanionMoodInterpretation mood)
        {
            if (!mood.HasMood)
                return string.Empty;

            return mood.Valence switch
            {
                CompanionMoodValence.Negative => mood.Intensity switch
                {
                    CompanionMoodIntensity.High => $"Hang in there while you're {mood.Descriptor}. We can pause whenever you need.",
                    CompanionMoodIntensity.Medium => $"Hope being {mood.Descriptor} eases up soon.",
                    _ => $"Rest if you need to while you're {mood.Descriptor}; I've got you."
                },
                CompanionMoodValence.Positive => mood.Intensity switch
                {
                    CompanionMoodIntensity.High => $"Love hearing you're {mood.Descriptor}!",
                    CompanionMoodIntensity.Medium => $"Glad you're {mood.Descriptor}.",
                    _ => $"Good to know you're {mood.Descriptor}."
                },
                _ => $"Hope you stay {mood.Descriptor}."
            };
        }

        private bool MaybeQueueMoodFollowUp(
            CompanionMoodInterpretation detectedMood,
            CompanionMoodInterpretation memoryMood,
            CompanionMoodInterpretation empathyMood,
            string playerName,
            List<string> segments,
            List<string> followUps)
        {
            bool recordedFollowUp = false;

            if (followUps != null && detectedMood.HasMood && detectedMood.Valence == CompanionMoodValence.Negative)
            {
                string immediate = BuildMoodFollowUpPrompt(detectedMood, playerName, true);
                if (!string.IsNullOrEmpty(immediate))
                    followUps.Add(immediate);
            }

            if (conversationMemory == null || !conversationMemory.PendingMoodFollowUp)
                return recordedFollowUp;

            if (detectedMood.HasMood && detectedMood.Valence != CompanionMoodValence.Negative)
                return recordedFollowUp;

            if (!ShouldSendMoodFollowUp())
                return recordedFollowUp;

            var moodToCheck = memoryMood.HasMood ? memoryMood : empathyMood;
            if (!moodToCheck.HasMood || moodToCheck.Valence != CompanionMoodValence.Negative)
                return recordedFollowUp;

            string checkIn = BuildMoodFollowUpPrompt(moodToCheck, playerName, false);
            if (!string.IsNullOrEmpty(checkIn) && segments != null)
            {
                segments.Add(checkIn);
                recordedFollowUp = true;
            }

            return recordedFollowUp;
        }

        private bool ShouldSendMoodFollowUp()
        {
            if (conversationMemory == null)
                return false;

            if (!conversationMemory.LastMoodFollowUpUtc.HasValue)
                return true;

            double minutes = (DateTime.UtcNow - conversationMemory.LastMoodFollowUpUtc.Value).TotalMinutes;
            return minutes >= Math.Max(0.1f, moodFollowUpCooldownMinutes);
        }

        private string BuildMoodFollowUpPrompt(CompanionMoodInterpretation mood, string playerName, bool immediate)
        {
            if (!mood.HasMood || mood.Valence != CompanionMoodValence.Negative)
                return string.Empty;

            string name = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName;

            if (immediate)
            {
                return mood.Intensity switch
                {
                    CompanionMoodIntensity.High => $"If being {mood.Descriptor} gets worse, say the word and we'll make camp, {name}.",
                    CompanionMoodIntensity.Medium => $"Let me know if {mood.Descriptor} sticks around, {name}. We can ease up.",
                    _ => $"Keep me posted if that {mood.Descriptor} shifts at all, {name}."
                };
            }

            return mood.Intensity switch
            {
                CompanionMoodIntensity.High => $"Still watching out while you're {mood.Descriptor}, {name}. Want to slow the pace for a bit?",
                CompanionMoodIntensity.Medium => $"How are you holding up being {mood.Descriptor} today, {name}? Need anything?",
                _ => $"Feeling any better than {mood.Descriptor}, {name}? Happy to take a break." 
            };
        }

        private static CompanionMoodInterpretation SelectDominantMood(CompanionMoodInterpretation current, CompanionMoodInterpretation candidate)
        {
            if (!candidate.HasMood)
                return current;

            if (!current.HasMood)
                return candidate;

            if (candidate.Valence == CompanionMoodValence.Negative && current.Valence != CompanionMoodValence.Negative)
                return candidate;

            if (current.Valence == CompanionMoodValence.Negative && candidate.Valence != CompanionMoodValence.Negative)
                return current;

            if ((int)candidate.Intensity > (int)current.Intensity)
                return candidate;

            return current;
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
                skillRecency);
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
            string resolvedSkillAction = !string.IsNullOrWhiteSpace(context.SuggestedSkillActionDescription)
                ? context.SuggestedSkillActionDescription
                : context.HasRecentSkillActions
                    ? context.LatestSkillAction
                    : "keeping skills sharp";
            string resolvedSuggestedSkill = context.HasSuggestedSkill
                ? context.SuggestedSkillName
                : (!string.IsNullOrWhiteSpace(context.SuggestedSkillActionDescription)
                    ? context.SuggestedSkillActionDescription
                    : (context.HasRecentSkillActions ? context.LatestSkillAction : "skilling"));
            string resolvedSkillRecency = context.HasSuggestedSkillRecency
                ? context.SuggestedSkillRecency
                : "recently";

            string result = template
                .Replace("{playerName}", resolvedPlayerName)
                .Replace("{companionName}", string.IsNullOrWhiteSpace(companionName) ? "Companion" : companionName)
                .Replace("{playerMood}", resolvedPlayerMood)
                .Replace("{companionMood}", resolvedCompanionMood)
                .Replace("{recentEvent}", resolvedEvent)
                .Replace("{timeOfDay}", resolvedTimeOfDay)
                .Replace("{combatState}", resolvedCombatState)
                .Replace("{recentSkillAction}", resolvedSkillAction)
                .Replace("{suggestedSkill}", resolvedSuggestedSkill)
                .Replace("{skillRecency}", resolvedSkillRecency)
                .Replace("{skillAction}", resolvedSkillAction);

            return CompactWhitespace(result);
        }

        private CompanionMoodInterpretation DetectPlayerMood(IReadOnlyList<string> tokens)
        {
            return CompanionMoodInterpreter.Interpret(tokens);
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
            public static ActiveSkillQuestion Empty => new ActiveSkillQuestion();

            private ActiveSkillQuestion()
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
                CompanionMoodInterpretation playerMood,
                IReadOnlyList<string> followUps = null,
                bool shouldRecordMoodFollowUp = false)
            {
                Text = text ?? string.Empty;
                StatusSegment = statusSegment ?? string.Empty;
                PlayerMood = playerMood;
                FollowUpSegments = followUps;
                ShouldRecordMoodFollowUp = shouldRecordMoodFollowUp;
            }

            public string Text { get; }

            public string StatusSegment { get; }

            public CompanionMoodInterpretation PlayerMood { get; }

            public IReadOnlyList<string> FollowUpSegments { get; }

            public bool ShouldRecordMoodFollowUp { get; }
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
