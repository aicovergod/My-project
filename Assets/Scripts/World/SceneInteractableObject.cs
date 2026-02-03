using System.Collections;
using System.Collections.Generic;
using Companions;
using Inventory;
using UI.Chat;
using UnityEngine;

namespace World
{
    /// <summary>
    /// Provides companion arrival dialogue for scene-linked interactables such as banks or shops.
    /// When a scene finishes loading, the component rolls a 1-in-20 chance to have the companion
    /// comment on the destination using context-aware flavour lines from <see cref="CompanionChatLibrary"/>.
    /// Designers can toggle the supported areas directly from the inspector.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("World/Scene Interactable Object")]
    public sealed class SceneInteractableObject : MonoBehaviour
    {
        private enum SceneArea
        {
            Bank,
            MiningShop
        }

        private const int DialogueChanceDenominator = 20;
        private const float CompanionResolutionTimeoutSeconds = 8f;
        private const string DebugLogPrefix = "[SceneInteractable]";

        /// <summary>
        /// Global toggle that mirrors internal state changes to the Unity console for QA workflows.
        /// Hooked into the Admin F2 menu so verbose logging can be enabled on demand.
        /// </summary>
        public static bool EnableDebugLogging { get; set; }

        [Header("Arrival Dialogue Flags")]
        [Tooltip("Mark this interactable as leading into a bank interior so the companion can react on arrival.")]
        [SerializeField]
        private bool isBank;

        [Tooltip("Mark this interactable as opening into the mining equipment shop so the companion can react on arrival.")]
        [SerializeField]
        private bool isMiningShop;

        /// <summary>Tracks whether arrival dialogue was already evaluated for the current scene.</summary>
        private bool hasEvaluatedArrivalDialogue;

        /// <summary>Stores the coroutine responsible for deferring the arrival roll until spawn completes.</summary>
        private Coroutine pendingArrivalRoutine;

        /// <summary>Reusable buffer that caches the active scene areas when building dialogue.</summary>
        private readonly List<SceneArea> activeAreas = new List<SceneArea>(2);

        private void OnEnable()
        {
            hasEvaluatedArrivalDialogue = false;
            SceneTransitionManager.TransitionCompleted += HandleTransitionCompleted;

            if (!SceneTransitionManager.IsTransitioning)
                BeginArrivalDialogueEvaluation();
        }

        private void OnDisable()
        {
            SceneTransitionManager.TransitionCompleted -= HandleTransitionCompleted;
            CancelPendingArrivalRoutine();
        }

        private void HandleTransitionCompleted()
        {
            BeginArrivalDialogueEvaluation();
        }

        /// <summary>
        /// Schedules the companion dialogue roll once player spawn and persistent services are ready.
        /// Guards against duplicate evaluations so only one roll occurs per scene entry.
        /// </summary>
        private void BeginArrivalDialogueEvaluation()
        {
            if (hasEvaluatedArrivalDialogue)
            {
                LogDebug("Arrival dialogue already evaluated for this scene; ignoring duplicate request.");
                return;
            }

            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                LogDebug("GameObject inactive; deferring arrival dialogue evaluation.");
                return;
            }

            if (!HasAnyAreaFlag())
            {
                LogDebug("No area flags enabled; arrival dialogue is disabled for this interactable.");
                hasEvaluatedArrivalDialogue = true;
                return;
            }

            CancelPendingArrivalRoutine();
            pendingArrivalRoutine = StartCoroutine(RunArrivalDialogueRoutine());
        }

        /// <summary>
        /// Cancels the queued coroutine when the component is disabled so it does not continue evaluating.
        /// </summary>
        private void CancelPendingArrivalRoutine()
        {
            if (pendingArrivalRoutine == null)
                return;

            StopCoroutine(pendingArrivalRoutine);
            pendingArrivalRoutine = null;
        }

        /// <summary>
        /// Waits until the player, companion, and chat service are ready before executing the dialogue roll.
        /// Ensures the companion only speaks once the spawn transition has fully settled.
        /// </summary>
        private IEnumerator RunArrivalDialogueRoutine()
        {
            hasEvaluatedArrivalDialogue = true;

            float startTime = Time.unscaledTime;
            try
            {
                yield return WaitForPlayerSpawn(startTime);
                yield return WaitForCompanion(startTime);
                yield return WaitForChatService(startTime);

                // Allow one extra frame after the required services come online so spawn placement finishes.
                yield return null;

                if (!RollDialogueChance())
                {
                    LogDebug("1-in-20 arrival roll failed; companion remains silent.");
                    yield break;
                }

                string message = BuildArrivalDialogue();
                if (string.IsNullOrWhiteSpace(message))
                {
                    LogDebug("No arrival dialogue could be resolved for the active area flags.");
                    yield break;
                }

                var chat = ChatService.Instance;
                if (chat == null)
                {
                    LogDebug("ChatService unavailable after wait; aborting arrival dialogue broadcast.");
                    yield break;
                }

                string speaker = CompanionManager.GetCompanionDisplayName();
                chat.PublishCompanionMessage(speaker, message);
                LogDebug($"Arrival dialogue broadcast: \"{message}\"");
            }
            finally
            {
                pendingArrivalRoutine = null;
            }
        }

        /// <summary>
        /// Yields until the player object exists or a timeout is reached.
        /// </summary>
        private IEnumerator WaitForPlayerSpawn(float startTime)
        {
            while (GameObject.FindGameObjectWithTag("Player") == null)
            {
                if (HasTimedOut(startTime))
                {
                    LogDebug("Timed out while waiting for player spawn; aborting arrival dialogue.");
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// Yields until an active companion is available or the timeout expires.
        /// </summary>
        private IEnumerator WaitForCompanion(float startTime)
        {
            while (!CompanionManager.HasActiveCompanion)
            {
                if (HasTimedOut(startTime))
                {
                    LogDebug("Timed out while waiting for active companion; aborting arrival dialogue.");
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// Yields until the chat service singleton is ready or the timeout expires.
        /// </summary>
        private IEnumerator WaitForChatService(float startTime)
        {
            while (ChatService.Instance == null)
            {
                if (HasTimedOut(startTime))
                {
                    LogDebug("Timed out while waiting for ChatService; aborting arrival dialogue.");
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>Returns <c>true</c> when the wait operations exceeded the configured timeout.</summary>
        private static bool HasTimedOut(float startTime)
        {
            return Time.unscaledTime - startTime >= CompanionResolutionTimeoutSeconds;
        }

        /// <summary>
        /// Determines whether any contextual area flag is active on this interactable.
        /// </summary>
        private bool HasAnyAreaFlag()
        {
            return isBank || isMiningShop;
        }

        /// <summary>
        /// Rolls the 1-in-20 dialogue chance used to decide whether the companion should speak.
        /// </summary>
        private static bool RollDialogueChance()
        {
            return Random.Range(0, DialogueChanceDenominator) == 0;
        }

        /// <summary>
        /// Builds the dialogue line appropriate for the currently configured scene area flags.
        /// </summary>
        private string BuildArrivalDialogue()
        {
            activeAreas.Clear();
            if (isBank)
                activeAreas.Add(SceneArea.Bank);
            if (isMiningShop)
                activeAreas.Add(SceneArea.MiningShop);

            if (activeAreas.Count == 0)
                return string.Empty;

            SceneArea selectedArea = activeAreas[Random.Range(0, activeAreas.Count)];
            switch (selectedArea)
            {
                case SceneArea.Bank:
                    return CompanionChatLibrary.GetRandomBankAwarenessLine(IsCompanionInventoryFull());
                case SceneArea.MiningShop:
                    return CompanionChatLibrary.GetRandomMiningShopAwarenessLine();
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Evaluates whether the companion inventory exists and all slots are filled.
        /// Mirrors the checks performed by <see cref="CompanionEnvironmentAwarenessZone"/>.
        /// </summary>
        private static bool IsCompanionInventoryFull()
        {
            var companion = CompanionManager.ActiveCompanion;
            var inventory = companion?.Inventory?.InventoryComponent;
            if (inventory == null)
                return false;

            var model = inventory.Model;
            if (model == null)
                return false;

            int slotCount = model.Size;
            for (int i = 0; i < slotCount; i++)
            {
                InventoryEntry entry = model.GetEntry(i);
                if (entry.item == null)
                    return false;

                if (entry.item.stackable && entry.count < entry.item.MaxStack)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Emits a formatted debug log when verbose output is enabled via <see cref="EnableDebugLogging"/>.
        /// </summary>
        private void LogDebug(string message)
        {
            if (!EnableDebugLogging)
                return;

            Debug.Log($"{DebugLogPrefix} {name}: {message}", this);
        }
    }
}
