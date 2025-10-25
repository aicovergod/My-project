using System;
using System.Collections.Generic;
using Companions.Chat;
using Core.Input;
using Player;
using Player.Commands;
using UI;
using UI.Chat;
using UI.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player.Chat
{
    /// <summary>
    /// Handles player-driven chat input, wiring input actions through the existing resolver and
    /// forwarding messages to <see cref="ChatService"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerChatController : MonoBehaviour
    {
        private const float FloatingTextFallbackHeight = 1.6f;

        /// <summary>
        /// Maps supported speech colour prefixes to their OSRS-inspired hues for floating speech bubbles.
        /// </summary>
        private static readonly Dictionary<string, Color> SpeechColorLookup = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "Red", new Color32(255, 46, 36, 255) },
            { "Green", new Color32(76, 255, 0, 255) },
            { "Cyan", new Color32(0, 255, 255, 255) },
            { "Purple", new Color32(170, 85, 255, 255) },
            { "Black", Color.black },
            { "Orange", new Color32(255, 140, 0, 255) },
            { "Yellow", Color.yellow }
        };

        [Header("Input")]
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private InputActionReference openChatAction;
        [SerializeField] private InputActionReference submitChatAction;
        [SerializeField] private InputActionReference cancelChatAction;

        [Header("References")]
        [SerializeField] private ChatHudController chatHud;

        private InputAction resolvedOpenAction;
        private InputAction resolvedSubmitAction;
        private InputAction resolvedCancelAction;
        private bool openActionEnabledByResolver;
        private bool submitActionEnabledByResolver;
        private bool cancelActionEnabledByResolver;

        private readonly PlayerMovementModalLock modalLock = new PlayerMovementModalLock();
        private PlayerMover cachedPlayerMover;
        private FloatingTextAnchorUtility.AnchorCache floatingTextAnchorCache;

        /// <summary>
        /// Indicates whether the controller has an active HUD reference bound.
        /// </summary>
        public bool HasHud(ChatHudController hud) => chatHud == hud && hud != null;

        /// <summary>
        /// Assigns the HUD instance generated at runtime.
        /// </summary>
        public void SetHud(ChatHudController hud)
        {
            if (chatHud == hud)
                return;

            UnsubscribeHudFocus();
            chatHud = hud;
            ReleaseModalLock();
            if (chatHud == null)
                return;

            SubscribeHudFocus();
        }

        private void Reset()
        {
            playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            ResolveActions();
            SubscribeActions();
            SubscribeHudFocus();
        }

        private void OnDisable()
        {
            UnsubscribeActions();
            UnsubscribeHudFocus();
            ReleaseModalLock();
            DisableResolvedActions();
        }

        private void OnDestroy()
        {
            UnsubscribeHudFocus();
            ReleaseModalLock();
        }

        private PlayerMover ResolvePlayerMover()
        {
            if (cachedPlayerMover == null)
                cachedPlayerMover = GetComponent<PlayerMover>();

            return cachedPlayerMover;
        }

        private void SubscribeHudFocus()
        {
            if (chatHud == null)
                return;

            chatHud.InputFocusChanged -= HandleHudInputFocusChanged;
            chatHud.InputFocusChanged += HandleHudInputFocusChanged;

            if (isActiveAndEnabled && chatHud.IsInputFocused)
                modalLock.Acquire(ResolvePlayerMover());
        }

        private void UnsubscribeHudFocus()
        {
            if (chatHud == null)
                return;

            chatHud.InputFocusChanged -= HandleHudInputFocusChanged;
        }

        private void ResolveActions()
        {
            if (playerInput == null)
                playerInput = GetComponent<PlayerInput>();

            resolvedOpenAction = InputActionResolver.Resolve(playerInput, openChatAction, "OpenChat", out openActionEnabledByResolver);
            resolvedSubmitAction = InputActionResolver.Resolve(playerInput, submitChatAction, "SubmitChat", out submitActionEnabledByResolver);
            resolvedCancelAction = InputActionResolver.Resolve(playerInput, cancelChatAction, "CancelChat", out cancelActionEnabledByResolver);
        }

        private void SubscribeActions()
        {
            if (resolvedOpenAction != null)
                resolvedOpenAction.performed += HandleOpenChatPerformed;

            if (resolvedSubmitAction != null)
                resolvedSubmitAction.performed += HandleSubmitChatPerformed;

            if (resolvedCancelAction != null)
                resolvedCancelAction.performed += HandleCancelChatPerformed;
        }

        private void UnsubscribeActions()
        {
            if (resolvedOpenAction != null)
                resolvedOpenAction.performed -= HandleOpenChatPerformed;

            if (resolvedSubmitAction != null)
                resolvedSubmitAction.performed -= HandleSubmitChatPerformed;

            if (resolvedCancelAction != null)
                resolvedCancelAction.performed -= HandleCancelChatPerformed;
        }

        private void DisableResolvedActions()
        {
            if (resolvedOpenAction != null && openActionEnabledByResolver)
                resolvedOpenAction.Disable();
            if (resolvedSubmitAction != null && submitActionEnabledByResolver)
                resolvedSubmitAction.Disable();
            if (resolvedCancelAction != null && cancelActionEnabledByResolver)
                resolvedCancelAction.Disable();
        }

        private void HandleOpenChatPerformed(InputAction.CallbackContext context)
        {
            if (chatHud == null)
            {
                Debug.LogWarning("PlayerChatController: Chat HUD not bound. Unable to open chat input.");
                return;
            }

            if (chatHud.IsInputFocusBlocked)
                return;

            chatHud.FocusInput();
            modalLock.Acquire(ResolvePlayerMover());
        }

        private void HandleSubmitChatPerformed(InputAction.CallbackContext context)
        {
            if (chatHud == null)
                return;

            if (!chatHud.TryConsumeInput(out string message, out ChatChannel channel))
                return;

            var chatService = ChatService.Instance;
            if (chatService == null)
            {
                Debug.LogWarning("PlayerChatController: ChatService unavailable. Cannot send message.");
                chatHud.CancelInput();
                ReleaseModalLock();
                return;
            }

            string sender = chatService.ActiveUsername;

            var commandService = PlayerCommandService.Instance;
            PlayerCommandHandleResult commandResult = commandService != null
                ? commandService.ProcessChatMessage(sender, message)
                : (IsPotentialCommand(message)
                    ? new PlayerCommandHandleResult(true, false, PlayerCommandServiceError.ServiceUnavailable, "Command service is unavailable.")
                    : PlayerCommandHandleResult.NotACommand());

            if (commandResult.IsCommand)
            {
                if (commandResult.Error == PlayerCommandServiceError.ServiceUnavailable && !string.IsNullOrEmpty(commandResult.FeedbackMessage))
                    chatService.PublishGameMessage(commandResult.FeedbackMessage);

                chatHud.CancelInput();
                ReleaseModalLock();
                return;
            }

            // Resolve any speech colour prefix before broadcasting so the chat log remains untouched while bubbles inherit the override.
            bool hasSpeechColor = TryResolveSpeechColor(message, out string sanitisedMessage, out Color speechColor);

            if (channel == ChatChannel.Companion)
            {
                if (CompanionChatCommandProcessor.TryProcessChatCommand(sender, sanitisedMessage))
                {
                    chatHud.CancelInput();
                    ReleaseModalLock();
                    return;
                }
            }

            if (channel == ChatChannel.Companion)
            {
                chatService.PublishCompanionMessage(sender, sanitisedMessage, true);
            }
            else
            {
                chatService.PublishPublicMessage(sender, sanitisedMessage);
                SpawnFloatingSpeech(sanitisedMessage, hasSpeechColor ? speechColor : (Color?)null);
            }

            chatHud.CancelInput();
            ReleaseModalLock();
        }

        private void HandleCancelChatPerformed(InputAction.CallbackContext context)
        {
            if (chatHud != null)
                chatHud.CancelInput();
            ReleaseModalLock();
        }

        private void HandleHudInputFocusChanged(bool focused)
        {
            if (!isActiveAndEnabled)
                return;

            if (focused)
                modalLock.Acquire(ResolvePlayerMover());
            else
                ReleaseModalLock();
        }

        /// <summary>
        /// Attempts to strip a supported <c>&lt;ColourName&gt;:</c> prefix from the supplied message, returning the remaining text
        /// and the associated colour override for floating speech bubbles. The method succeeds only when a known prefix is
        /// provided alongside a non-empty message. When resolution fails the original message is preserved and white is used.
        /// </summary>
        private static bool TryResolveSpeechColor(string rawMessage, out string sanitisedMessage, out Color color)
        {
            sanitisedMessage = rawMessage;
            color = Color.white;

            if (string.IsNullOrEmpty(rawMessage))
                return false;

            int separatorIndex = rawMessage.IndexOf(':');
            if (separatorIndex <= 0)
                return false;

            string potentialPrefix = rawMessage.Substring(0, separatorIndex).Trim();
            if (string.IsNullOrEmpty(potentialPrefix))
                return false;

            if (!SpeechColorLookup.TryGetValue(potentialPrefix, out Color resolvedColor))
                return false;

            string remainder = rawMessage.Substring(separatorIndex + 1);
            string trimmedRemainder = remainder.TrimStart();
            if (string.IsNullOrEmpty(trimmedRemainder))
                return false;

            color = resolvedColor;
            sanitisedMessage = trimmedRemainder;
            return true;
        }

        private void SpawnFloatingSpeech(string message, Color? overrideColor = null)
        {
            if (string.IsNullOrEmpty(message))
                return;

            var position = FloatingTextAnchorUtility.ResolveAnchorPosition(transform, FloatingTextFallbackHeight, ref floatingTextAnchorCache);
            var anchorTransform = floatingTextAnchorCache.anchor != null ? floatingTextAnchorCache.anchor : transform;
            // Preserve the resolved spawn location while ensuring the popup tracks the anchor each frame.
            Vector3 followOffset = position - anchorTransform.position;
            // Chat prefixes control moderator icon rendering, so keep speech bubbles free of badge markup to prevent spoofing.
            var tokens = EmojiMarkupParser.Parse(message ?? string.Empty, allowModeratorIcons: false);
            var colourToApply = overrideColor ?? Color.white;
            FloatingText.ShowAnchored(tokens, anchorTransform, followOffset, colourToApply);
        }

        private void ReleaseModalLock()
        {
            if (modalLock.IsLocked)
                modalLock.Release();
        }

        private static bool IsPotentialCommand(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.TrimStart().StartsWith("::", System.StringComparison.Ordinal);
        }
    }
}
