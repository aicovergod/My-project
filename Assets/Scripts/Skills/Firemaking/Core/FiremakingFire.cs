using System;
using Inventory;
using MyGame.Drops;
using Skills.Common;
using UnityEngine;

namespace Skills.Firemaking
{
    /// <summary>
    ///     Runtime component spawned for each active fire. The fire counts down via the global ticker
    ///     and notifies listeners when it expires so ashes can be spawned and UI updated.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FiremakingFire : TickedSkillBehaviour
    {
        [SerializeField] private Collider2D blockingCollider;
        [SerializeField] private Animator animator;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private AudioSource loopedAudio;

        private int ticksRemaining;
        private int lifetimeCap;
        private ItemData ashesItem;
        private GroundItemSpawner groundItemSpawner;
        private AudioClip igniteClip;
        private AudioClip extinguishClip;
        private FiremakingSkill ownerSkill;

        /// <summary>
        ///     Tracks whether the fire has already been extinguished so additional fuel is ignored.
        /// </summary>
        public bool IsExtinguished { get; private set; }

        /// <summary>
        ///     Definition that produced this fire. Useful for UI when showing remaining lifetime.
        /// </summary>
        public FiremakingLogDefinition SourceLog { get; private set; }

        /// <summary>
        ///     Raised when the fire burns out and the component is about to be destroyed.
        /// </summary>
        public event Action<FiremakingFire> Extinguished;

        /// <summary>
        ///     Provides read-only access to the skill that spawned this fire for debugging and cleanup.
        /// </summary>
        public FiremakingSkill OwnerSkill => ownerSkill;

        /// <summary>
        ///     Configures the fire with the lifetime, loot and audio data for the originating log.
        /// </summary>
        /// <param name="definition">Definition describing the log that ignited.</param>
        /// <param name="initialTicks">Initial lifetime contribution from the log.</param>
        /// <param name="maxTicks">Lifetime cap for stacking additional logs.</param>
        /// <param name="ashesItem">Ashes item to spawn once the fire dies.</param>
        /// <param name="spawner">World drop spawner used for ashes.</param>
        /// <param name="igniteSound">Optional sound played when the fire starts.</param>
        /// <param name="extinguishSound">Optional sound played when the fire ends.</param>
        public void Initialise(
            FiremakingLogDefinition definition,
            int initialTicks,
            int maxTicks,
            ItemData ashesItem,
            GroundItemSpawner spawner,
            AudioClip igniteSound,
            AudioClip extinguishSound)
        {
            // Cache everything so subsequent fuel additions share the same configuration.
            SourceLog = definition;
            this.ashesItem = ashesItem;
            groundItemSpawner = spawner;
            igniteClip = igniteSound;
            extinguishClip = extinguishSound;

            // Convert the configured maximum into an easy-to-use cap. <=0 means unlimited stacking.
            lifetimeCap = maxTicks > 0 ? Mathf.Max(1, maxTicks) : int.MaxValue;
            ticksRemaining = Mathf.Clamp(initialTicks, 1, lifetimeCap);
            IsExtinguished = false;

            // Kick the ambient loop if one is configured on the prefab.
            if (loopedAudio != null && !loopedAudio.isPlaying)
                loopedAudio.Play();

            // Play a one-shot ignition sound immediately so the action feels responsive.
            if (igniteClip != null)
            {
                if (loopedAudio != null)
                    loopedAudio.PlayOneShot(igniteClip);
                else
                    AudioSource.PlayClipAtPoint(igniteClip, transform.position);
            }

            // Ensure we are subscribed in case the object was instantiated while disabled.
            TrySubscribeToTicker();
        }

        /// <summary>
        ///     Stores a reference to the owning skill so it can detach event listeners safely.
        /// </summary>
        /// <param name="owner">Skill responsible for managing this fire.</param>
        public void SetOwner(FiremakingSkill owner)
        {
            ownerSkill = owner;
        }

        /// <summary>
        ///     Adds additional lifetime to the fire. Requests after the fire has gone out are ignored.
        /// </summary>
        /// <param name="ticks">Lifetime contribution from the newly added log.</param>
        /// <param name="maxTicksOverride">Optional cap to enforce when different log types are added.</param>
        /// <param name="igniteClipOverride">Sound clip to play when the log successfully catches.</param>
        public void AddFuel(int ticks, int maxTicksOverride, AudioClip igniteClipOverride = null)
        {
            if (IsExtinguished)
                return;

            if (ticks <= 0)
                return;

            // Update the lifetime cap when the caller provides a stricter value.
            if (maxTicksOverride > 0)
            {
                int normalized = Mathf.Max(1, maxTicksOverride);
                lifetimeCap = lifetimeCap == int.MaxValue
                    ? normalized
                    : Mathf.Max(lifetimeCap, normalized);
            }

            int cap = lifetimeCap > 0 ? lifetimeCap : int.MaxValue;
            ticksRemaining = Mathf.Clamp(ticksRemaining + ticks, 1, cap);

            // Play any provided sound effect so fuelling feels reactive.
            var clip = igniteClipOverride ?? igniteClip;
            if (clip != null)
            {
                if (loopedAudio != null)
                    loopedAudio.PlayOneShot(clip);
                else
                    AudioSource.PlayClipAtPoint(clip, transform.position);
            }

            // Make sure the fire is ticking in case it was paused after running out of fuel previously.
            TrySubscribeToTicker();
        }

        /// <summary>
        ///     Fires do not log ticker subscriptions by default unless explicitly enabled.
        /// </summary>
        protected override bool LogTickerSubscription => false;

        /// <summary>
        ///     Counts down the remaining lifetime and triggers extinction once the timer hits zero.
        /// </summary>
        protected override void HandleTick()
        {
            if (IsExtinguished)
                return;

            if (ticksRemaining > 0)
                ticksRemaining--;

            if (ticksRemaining > 0)
                return;

            Extinguish();
        }

        /// <summary>
        ///     Handles the teardown workflow when the fire has burned out.
        /// </summary>
        private void Extinguish()
        {
            if (IsExtinguished)
                return;

            IsExtinguished = true;

            // Stop receiving tick events immediately so we do not run this twice.
            CancelTickerSubscription();

            // Release any collision the fire was providing so the tile becomes walkable again.
            if (blockingCollider != null)
                blockingCollider.enabled = false;

            // Stop looping audio and play the final extinguish sound if provided.
            if (loopedAudio != null)
                loopedAudio.Stop();

            if (extinguishClip != null)
            {
                if (loopedAudio != null)
                    loopedAudio.PlayOneShot(extinguishClip);
                else
                    AudioSource.PlayClipAtPoint(extinguishClip, transform.position);
            }

            // Spawn ashes when an item definition was supplied.
            if (groundItemSpawner != null && ashesItem != null)
                groundItemSpawner.Spawn(ashesItem, 1, transform.position);

            // Notify listeners before the GameObject is destroyed.
            Extinguished?.Invoke(this);

            // Destroy the visual so the world is cleared once the fire expires.
            Destroy(gameObject);
        }
    }
}
