using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Status.Freeze
{
    /// <summary>
    /// Listens for freeze buff timers applied to the owning object and pauses movement while the
    /// effect is active. The controller works with both the player and NPCs so long as the
    /// relevant locomotion components are referenced.
    /// </summary>
    [DisallowMultipleComponent]
    public class FrozenStatusController : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional explicit player movement component to control when frozen.")]
        private Player.PlayerMover playerMover;

        [SerializeField, Tooltip("Optional NPC wanderer component that should pause when frozen.")]
        private NPC.NpcWanderer npcWanderer;

        [SerializeField, Tooltip("Rigid body whose velocity should be cleared when the freeze begins.")]
        private Rigidbody2D rigidBody;

        /// <summary>Tracks active freeze buff instances so overlapping timers keep the target locked.</summary>
        private readonly HashSet<Status.BuffTimerInstance> activeFreezes = new();

        /// <summary>Reusable buffer used when querying the timer service for existing buffs.</summary>
        private readonly List<Status.BuffTimerInstance> queryBuffer = new();

        /// <summary>Reference to the timer service we are currently subscribed to.</summary>
        private Status.BuffTimerService subscribedService;

        /// <summary>Coroutine used to wait for the timer service when it has not spawned yet.</summary>
        private Coroutine waitRoutine;

        /// <summary>Flag indicating whether the entity is currently frozen.</summary>
        private bool frozen;

        /// <summary>Caches the previous value of <see cref="Player.PlayerMover.freezeSprite"/> so it can be restored.</summary>
        private bool cachedFreezeSpriteValue;

        /// <summary>Provides external visibility of the frozen state for debugging.</summary>
        public bool IsFrozen => frozen;

        private void Reset()
        {
            playerMover = GetComponent<Player.PlayerMover>();
            npcWanderer = GetComponent<NPC.NpcWanderer>();
            rigidBody = GetComponent<Rigidbody2D>();
        }

        private void Awake()
        {
            if (playerMover == null)
                playerMover = GetComponent<Player.PlayerMover>();
            if (npcWanderer == null)
                npcWanderer = GetComponent<NPC.NpcWanderer>();
            if (rigidBody == null)
                rigidBody = GetComponent<Rigidbody2D>();
        }

        private void OnEnable()
        {
            SubscribeToService();
            SyncWithExistingBuffs();
        }

        private void OnDisable()
        {
            if (waitRoutine != null)
            {
                StopCoroutine(waitRoutine);
                waitRoutine = null;
            }
            UnsubscribeFromService();
            activeFreezes.Clear();
            SetFrozen(false);
        }

        /// <summary>
        /// Applies the frozen state when a relevant buff begins or updates.
        /// </summary>
        private void HandleBuffStarted(Status.BuffTimerInstance instance)
        {
            if (!IsRelevant(instance))
                return;

            activeFreezes.Add(instance);
            SetFrozen(true);
        }

        /// <summary>
        /// Ensures refreshed freeze timers maintain the frozen state.
        /// </summary>
        private void HandleBuffUpdated(Status.BuffTimerInstance instance)
        {
            if (!IsRelevant(instance))
                return;

            activeFreezes.Add(instance);
            SetFrozen(true);
        }

        /// <summary>
        /// Lifts the frozen state once all freeze timers have ended.
        /// </summary>
        private void HandleBuffEnded(Status.BuffTimerInstance instance, Status.BuffEndReason reason)
        {
            if (!IsRelevant(instance))
                return;

            activeFreezes.Remove(instance);
            if (activeFreezes.Count == 0)
                SetFrozen(false);
        }

        /// <summary>
        /// Subscribes to <see cref="Status.BuffTimerService"/> events, waiting for the service to
        /// spawn when required.
        /// </summary>
        private void SubscribeToService()
        {
            var service = Status.BuffTimerService.Instance;
            if (service == null)
            {
                if (waitRoutine == null)
                    waitRoutine = StartCoroutine(WaitForService());
                return;
            }

            if (service == subscribedService)
                return;

            UnsubscribeFromService();

            subscribedService = service;
            subscribedService.BuffStarted += HandleBuffStarted;
            subscribedService.BuffUpdated += HandleBuffUpdated;
            subscribedService.BuffEnded += HandleBuffEnded;
        }

        /// <summary>
        /// Removes any active subscriptions from the timer service.
        /// </summary>
        private void UnsubscribeFromService()
        {
            if (subscribedService == null)
                return;

            subscribedService.BuffStarted -= HandleBuffStarted;
            subscribedService.BuffUpdated -= HandleBuffUpdated;
            subscribedService.BuffEnded -= HandleBuffEnded;
            subscribedService = null;
        }

        /// <summary>
        /// Waits until the timer service is available before subscribing to its events.
        /// </summary>
        private IEnumerator WaitForService()
        {
            while (Status.BuffTimerService.Instance == null)
                yield return null;

            waitRoutine = null;
            if (!enabled)
                yield break;

            SubscribeToService();
            SyncWithExistingBuffs();
        }

        /// <summary>
        /// Ensures the local frozen state matches any freeze buffs that were already active when
        /// this component became enabled.
        /// </summary>
        private void SyncWithExistingBuffs()
        {
            var service = Status.BuffTimerService.Instance;
            if (service == null)
                return;

            queryBuffer.Clear();
            service.GetBuffsFor(gameObject, queryBuffer);

            bool hasFreeze = false;
            for (int i = 0; i < queryBuffer.Count; i++)
            {
                var instance = queryBuffer[i];
                if (!IsRelevant(instance))
                    continue;

                hasFreeze = true;
                activeFreezes.Add(instance);
            }

            if (hasFreeze)
                SetFrozen(true);
            else
                SetFrozen(false);

            queryBuffer.Clear();
        }

        /// <summary>
        /// Checks whether a buff timer instance applies to this object and represents a freeze.
        /// </summary>
        private bool IsRelevant(Status.BuffTimerInstance instance)
        {
            return instance != null && instance.Target == gameObject && instance.Definition.type == Status.BuffType.Freeze;
        }

        /// <summary>
        /// Enables or disables the frozen state, updating linked movement components accordingly.
        /// </summary>
        private void SetFrozen(bool shouldFreeze)
        {
            if (frozen == shouldFreeze)
                return;

            frozen = shouldFreeze;

            if (playerMover != null)
            {
                if (frozen)
                {
                    cachedFreezeSpriteValue = playerMover.freezeSprite;
                    playerMover.SetMovementFrozen(true);
                }
                else
                {
                    playerMover.SetMovementFrozen(false);
                    playerMover.freezeSprite = cachedFreezeSpriteValue;
                }
            }

            if (npcWanderer != null)
                npcWanderer.SetFrozen(frozen);

            if (rigidBody != null)
            {
#if UNITY_2023_1_OR_NEWER
                rigidBody.linearVelocity = Vector2.zero;
#else
                rigidBody.velocity = Vector2.zero;
#endif
            }
        }
    }
}
