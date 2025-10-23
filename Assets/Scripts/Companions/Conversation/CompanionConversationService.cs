using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Companions;
using UI.Chat;
using UnityEngine;
using World;

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
        [SerializeField, Tooltip("Keyword rules used to detect dialogue intents.")]
        private List<CompanionDialogueRule> rules = new List<CompanionDialogueRule>();

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

        /// <inheritdoc />
        protected override void OnSingletonAwake()
        {
            base.OnSingletonAwake();

            responseLibrary ??= new CompanionDialogueResponseLibrary();
            responseLibrary.EnsureDefaults();

            if (rules == null || rules.Count == 0)
                rules = BuildDefaultRules();

            parser = new CompanionDialogueParser(rules);

            if (conversationMemory == null)
                conversationMemory = FindObjectOfType<CompanionConversationMemory>(true);

            EnsureChatSubscription();
        }

        /// <inheritdoc />
        protected override void OnSingletonDestroyed()
        {
            UnsubscribeFromChat();

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

        private void HandleMessageReceived(ChatMessage message)
        {
            if (message.Channel != ChatChannel.Companion)
                return;

            if (!message.IsLocalPlayerAuthor)
                return;

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
            string playerMoodFromMemory = conversationMemory != null ? conversationMemory.LastKnownPlayerMood : string.Empty;
            string detectedPlayerMood = string.Empty;
            string statusSegment = string.Empty;
            string companionMood = ResolveCompanionMoodDescriptor();
            string recentEvent = ResolveRecentEventSummary();

            for (int i = 0; i < parseResult.Matches.Count; i++)
            {
                var match = parseResult.Matches[i];
                switch (match.Intent)
                {
                    case CompanionDialogueIntent.Greeting:
                        segments.Add(FormatTemplate(
                            responseLibrary.GetRandomTemplate(CompanionDialogueIntent.Greeting),
                            playerName,
                            playerMoodFromMemory,
                            companionMood,
                            recentEvent));
                        break;

                    case CompanionDialogueIntent.StatusQuery:
                        statusSegment = BuildStatusSegment(playerName, playerMoodFromMemory, companionMood);
                        if (!string.IsNullOrEmpty(statusSegment))
                            segments.Add(statusSegment);
                        break;

                    case CompanionDialogueIntent.PlayerMoodReport:
                        detectedPlayerMood = DetectPlayerMood(parseResult.Tokens);
                        string acknowledgedMood = !string.IsNullOrEmpty(detectedPlayerMood)
                            ? detectedPlayerMood
                            : playerMoodFromMemory;

                        var moodTemplate = responseLibrary.GetRandomTemplate(CompanionDialogueIntent.PlayerMoodReport);
                        if (!string.IsNullOrEmpty(moodTemplate))
                        {
                            segments.Add(FormatTemplate(
                                moodTemplate,
                                playerName,
                                acknowledgedMood,
                                companionMood,
                                recentEvent));
                        }

                        break;

                    case CompanionDialogueIntent.Gratitude:
                        segments.Add(FormatTemplate(
                            responseLibrary.GetRandomTemplate(CompanionDialogueIntent.Gratitude),
                            playerName,
                            playerMoodFromMemory,
                            companionMood,
                            recentEvent));
                        break;

                    case CompanionDialogueIntent.Farewell:
                        segments.Add(FormatTemplate(
                            responseLibrary.GetRandomTemplate(CompanionDialogueIntent.Farewell),
                            playerName,
                            playerMoodFromMemory,
                            companionMood,
                            recentEvent));
                        break;

                    case CompanionDialogueIntent.Compliment:
                        segments.Add(FormatTemplate(
                            responseLibrary.GetRandomTemplate(CompanionDialogueIntent.Compliment),
                            playerName,
                            playerMoodFromMemory,
                            companionMood,
                            recentEvent));
                        break;

                    case CompanionDialogueIntent.RequestAssistance:
                        segments.Add(FormatTemplate(
                            responseLibrary.GetRandomTemplate(CompanionDialogueIntent.RequestAssistance),
                            playerName,
                            playerMoodFromMemory,
                            companionMood,
                            recentEvent));
                        break;

                    case CompanionDialogueIntent.AcknowledgeRecentEvent:
                        if (!string.IsNullOrEmpty(recentEvent))
                        {
                            segments.Add(FormatTemplate(
                                responseLibrary.GetRandomTemplate(CompanionDialogueIntent.AcknowledgeRecentEvent),
                                playerName,
                                playerMoodFromMemory,
                                companionMood,
                                recentEvent));
                        }
                        break;
                }
            }

            if (segments.Count == 0)
                return null;

            string text = CombineSegments(segments);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return new PendingResponse(text, statusSegment, detectedPlayerMood);
        }

        private string BuildStatusSegment(string playerName, string playerMood, string companionMood)
        {
            string lastStatus = conversationMemory != null ? conversationMemory.LastStatusResponse : string.Empty;
            string template = responseLibrary.GetRandomTemplate(CompanionDialogueIntent.StatusQuery, lastStatus);
            if (string.IsNullOrEmpty(template))
                template = "I'm feeling {companionMood}. How are you doing?";

            string result = FormatTemplate(template, playerName, playerMood, companionMood, string.Empty);

            if (conversationMemory != null && !string.IsNullOrEmpty(lastStatus))
            {
                bool sameLine = string.Equals(result, lastStatus, StringComparison.OrdinalIgnoreCase);
                bool withinCooldown = conversationMemory.LastStatusResponseUtc.HasValue &&
                                       (DateTime.UtcNow - conversationMemory.LastStatusResponseUtc.Value).TotalMinutes <
                                       Math.Max(0.1f, statusRepeatCooldownMinutes);

                if (sameLine && withinCooldown)
                {
                    // Try a different descriptor to keep things fresh.
                    companionMood = ResolveCompanionMoodDescriptor();
                    result = FormatTemplate(template, playerName, playerMood, companionMood, string.Empty);
                }
            }

            if (!string.IsNullOrEmpty(playerMood))
            {
                result = AppendSentence(result, $"Hope you're {playerMood}.");
            }
            else if (conversationMemory != null && !string.IsNullOrEmpty(conversationMemory.LastKnownPlayerMood))
            {
                result = AppendSentence(result, $"Still keeping an eye on you being {conversationMemory.LastKnownPlayerMood}.");
            }

            return result;
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
                return string.Empty;

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

        private string FormatTemplate(string template, string playerName, string playerMood, string companionMood, string recentEvent)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            string companionName = CompanionManager.GetCompanionDisplayName();
            string resolvedPlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName;
            string resolvedPlayerMood = string.IsNullOrWhiteSpace(playerMood) ? "doing alright" : playerMood;
            string resolvedCompanionMood = string.IsNullOrWhiteSpace(companionMood) ? "ready" : companionMood;
            string resolvedEvent = string.IsNullOrWhiteSpace(recentEvent) ? "that" : recentEvent;

            string result = template
                .Replace("{playerName}", resolvedPlayerName)
                .Replace("{companionName}", string.IsNullOrWhiteSpace(companionName) ? "Companion" : companionName)
                .Replace("{playerMood}", resolvedPlayerMood)
                .Replace("{companionMood}", resolvedCompanionMood)
                .Replace("{recentEvent}", resolvedEvent);

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
            string tokens = string.Join(", ", result.UniqueTokens);
            string intents = string.Join(", ", result.Matches.Select(m => $"{m.Intent} (p={m.Priority})"));
            Debug.Log($"[CompanionConversationService] '{cleaned}' => [{intents}] via tokens [{tokens}].");
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

        private static List<CompanionDialogueRule> BuildDefaultRules()
        {
            return new List<CompanionDialogueRule>
            {
                CompanionDialogueRule.Create(
                    CompanionDialogueIntent.Greeting,
                    0,
                    ToGroups(new[]{"hello","hi","hey","greetings","yo","sup","salutations","hola"})),

                CompanionDialogueRule.Create(
                    CompanionDialogueIntent.StatusQuery,
                    5,
                    ToGroups(new[]{"how","hows"}, new[]{"you","ya"}, new[]{"doing","feeling","are","going"})),

                CompanionDialogueRule.Create(
                    CompanionDialogueIntent.PlayerMoodReport,
                    8,
                    ToGroups(new[]{"im","iam","am","feeling"}, PlayerMoodLookup.Keys.ToArray())),

                CompanionDialogueRule.Create(
                    CompanionDialogueIntent.Gratitude,
                    12,
                    ToGroups(new[]{"thanks","thank","appreciate"}),
                    new[]{"nothing"}),

                CompanionDialogueRule.Create(
                    CompanionDialogueIntent.Farewell,
                    20,
                    ToGroups(new[]{"bye","goodbye","farewell","later","cya","see","catch"})),

                CompanionDialogueRule.Create(
                    CompanionDialogueIntent.Compliment,
                    25,
                    ToGroups(new[]{"good","great","awesome","amazing","nice"}, new[]{"job","work","partner","friend"})),

                CompanionDialogueRule.Create(
                    CompanionDialogueIntent.RequestAssistance,
                    30,
                    ToGroups(new[]{"help","assist","cover","watch"})),

                CompanionDialogueRule.Create(
                    CompanionDialogueIntent.AcknowledgeRecentEvent,
                    35,
                    ToGroups(new[]{"remember","about","that","earlier","last"}, new[]{"fight","battle","event","thing","moment"}))
            };
        }

        private static IEnumerable<string>[] ToGroups(params IEnumerable<string>[] groups)
        {
            return groups;
        }

        private readonly struct PendingResponse
        {
            public PendingResponse(string text, string statusSegment, string playerMood)
            {
                Text = text;
                StatusSegment = statusSegment;
                PlayerMood = playerMood;
            }

            public string Text { get; }

            public string StatusSegment { get; }

            public string PlayerMood { get; }
        }
    }
}
