using Core.Input;
using Player;
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
            chatHud = hud;
        }

        private void Reset()
        {
            playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            ResolveActions();
            SubscribeActions();
        }

        private void OnDisable()
        {
            UnsubscribeActions();
            ReleaseModalLock();
            DisableResolvedActions();
        }

        private void OnDestroy()
        {
            ReleaseModalLock();
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

            chatHud.FocusInput();
            modalLock.Acquire(GetComponent<PlayerMover>());
        }

        private void HandleSubmitChatPerformed(InputAction.CallbackContext context)
        {
            if (chatHud == null)
                return;

            if (!chatHud.TryConsumeInput(out string message))
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
            chatService.PublishPublicMessage(sender, message);
            SpawnFloatingSpeech(message);

            chatHud.CancelInput();
            ReleaseModalLock();
        }

        private void HandleCancelChatPerformed(InputAction.CallbackContext context)
        {
            if (chatHud != null)
                chatHud.CancelInput();
            ReleaseModalLock();
        }

        private void SpawnFloatingSpeech(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            var position = FloatingTextAnchorUtility.ResolveAnchorPosition(transform, FloatingTextFallbackHeight, ref floatingTextAnchorCache);
            FloatingText.Show(message, position, Color.white);
        }

        private void ReleaseModalLock()
        {
            if (modalLock.IsLocked)
                modalLock.Release();
        }
    }
}
