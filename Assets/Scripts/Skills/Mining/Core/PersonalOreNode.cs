using System;
using Combat;
using Core.Save;
using Skills.Common;
using Skills;
using UnityEngine;
using UnityEngine.UI;

namespace Skills.Mining
{
    /// <summary>
    ///     Personal ore node spawned after defeating an ore monster encounter. The node exposes
    ///     ownership information, counts down via the global ticker, and raises events so UI can
    ///     respond to remaining lifetime changes.
    /// </summary>
    [RequireComponent(typeof(MineableRock))]
    [DisallowMultipleComponent]
    public sealed class PersonalOreNode : TickedSkillBehaviour
    {
        [Header("Interaction")]
        [Tooltip("Collider used for mining interaction checks. Falls back to the attached collider when omitted.")]
        [SerializeField] private Collider2D interactionCollider;

        [Header("Presentation")]
        [Tooltip("Optional VFX prefab spawned when the node despawns.")]
        [SerializeField] private GameObject despawnVfxPrefab;
        [Tooltip("Canvas that communicates ownership to the player. Only enabled for the owner.")]
        [SerializeField] private Canvas ownerOnlyCanvas;
        [Tooltip("Text element that will eventually display the owner name or timer.")]
        [SerializeField] private Text ownerOnlyText;
        [Tooltip("Icon that displays the solo-lock sprite supplied by the node definition.")]
        [SerializeField] private Image ownerOnlyIcon;
        [Tooltip("Sprite renderers that should only be visible to the owning player.")]
        [SerializeField] private SpriteRenderer[] ownerOnlySpriteRenderers;

        private MiningPersonalNodeController ownerController;
        private OreMonsterNodeDefinition sourceDefinition;
        private string ownerProfileId = string.Empty;
        private float totalLifetimeSeconds;
        private float remainingLifetimeSeconds;
        private bool isExpired;

        /// <summary>
        ///     Raised whenever the node's remaining lifetime changes. Provides the node instance,
        ///     the remaining lifetime in seconds, and the normalized (0-1) value relative to the
        ///     original total lifetime.
        /// </summary>
        public event Action<PersonalOreNode, float, float> LifetimeChanged;

        /// <summary>
        ///     Raised when the node expires either naturally or via a forced despawn call.
        /// </summary>
        public event Action<PersonalOreNode> Expired;

        /// <summary>
        ///     Reference back to the controller that spawned this node so other systems can query
        ///     additional context when required.
        /// </summary>
        public MiningPersonalNodeController OwnerController => ownerController;

        /// <summary>
        ///     Definition that produced this personal node. Useful when UI needs access to
        ///     presentation data such as the solo-lock sprite.
        /// </summary>
        public OreMonsterNodeDefinition SourceDefinition => sourceDefinition;

        /// <summary>
        ///     Profile ID for the owning player. The value is stored as an empty string when no
        ///     owner is provided to keep comparisons lightweight.
        /// </summary>
        public string OwnerProfileId => ownerProfileId;

        /// <summary>
        ///     Total lifetime originally assigned to the node (in seconds).
        /// </summary>
        public float TotalLifetimeSeconds => totalLifetimeSeconds;

        /// <summary>
        ///     Remaining lifetime for the node (in seconds). Returns zero once the node has expired.
        /// </summary>
        public float RemainingLifetimeSeconds => Mathf.Max(0f, remainingLifetimeSeconds);

        /// <summary>
        ///     Indicates whether the node has already expired.
        /// </summary>
        public bool IsExpired => isExpired;

        private void Awake()
        {
            // Ensure the required mineable rock component exists so the prefab remains valid.
            GetComponent<MineableRock>();

            // Fall back to the attached collider when no explicit interaction collider has been
            // assigned via the inspector. This keeps prefabs flexible while enforcing safe mining.
            if (interactionCollider == null)
                interactionCollider = GetComponent<Collider2D>();
        }

        /// <summary>
        ///     Configures the personal ore node with ownership data and lifetime details.
        /// </summary>
        /// <param name="controller">Controller responsible for spawning this node.</param>
        /// <param name="definition">Definition that produced the node.</param>
        /// <param name="ownerId">Profile ID for the owning player.</param>
        /// <param name="lifetimeSeconds">Total lifetime assigned to the node in seconds.</param>
        public void Initialise(
            MiningPersonalNodeController controller,
            OreMonsterNodeDefinition definition,
            string ownerId,
            float lifetimeSeconds)
        {
            ownerController = controller;
            sourceDefinition = definition;
            ownerProfileId = string.IsNullOrWhiteSpace(ownerId) ? string.Empty : ownerId;
            totalLifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            remainingLifetimeSeconds = totalLifetimeSeconds;
            isExpired = false;

            if (interactionCollider != null)
                interactionCollider.enabled = true;

            UpdateOwnerOnlyVisuals();
            ApplyDefinitionVisuals();
            RaiseLifetimeChanged();

            if (totalLifetimeSeconds <= 0f)
            {
                // Immediately expire without visuals when lifetime is zero to keep behaviour deterministic.
                ForceExpire(false);
            }
            else
            {
                TrySubscribeToTicker();
            }
        }

        /// <summary>
        ///     Determines whether the provided mining skill is allowed to mine this node.
        /// </summary>
        /// <param name="miningSkill">Mining skill attempting to mine the node.</param>
        /// <param name="failureMessage">Output message describing why mining is not allowed.</param>
        /// <returns>True when mining is permitted, otherwise false.</returns>
        public bool CanMine(MiningSkill miningSkill, out string failureMessage)
        {
            failureMessage = null;

            if (isExpired)
            {
                failureMessage = "The vein has already collapsed.";
                return false;
            }

            if (!string.IsNullOrEmpty(ownerProfileId))
            {
                string activeProfileId = SaveManager.ActiveProfileId ?? string.Empty;
                if (!string.Equals(ownerProfileId, activeProfileId, StringComparison.Ordinal))
                {
                    failureMessage = "Only the victorious miner can claim this ore.";
                    return false;
                }
            }

            if (sourceDefinition != null && sourceDefinition.RequiresCombatLevel)
            {
                SkillManager skills = null;
                if (miningSkill != null)
                    skills = miningSkill.GetComponent<SkillManager>();

                int combatLevel = CombatLevelUtility.CalculateCombatLevel(skills);
                if (combatLevel < sourceDefinition.RequiredCombatLevel)
                {
                    failureMessage = $"Requires combat level {sourceDefinition.RequiredCombatLevel}.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Counts down the remaining lifetime each tick and expires the node once it reaches zero.
        /// </summary>
        protected override void HandleTick()
        {
            if (isExpired)
                return;

            remainingLifetimeSeconds = Mathf.Max(0f, remainingLifetimeSeconds - Util.Ticker.TickDuration);
            RaiseLifetimeChanged();

            if (remainingLifetimeSeconds > 0f)
                return;

            ForceExpire(true);
        }

        /// <summary>
        ///     Forces the node to expire immediately, optionally playing a despawn VFX.
        /// </summary>
        /// <param name="playVfx">True to instantiate the configured VFX prefab.</param>
        public void ForceExpire(bool playVfx)
        {
            if (isExpired)
                return;

            isExpired = true;
            bool lifetimeAlreadyZero = remainingLifetimeSeconds <= 0f;
            remainingLifetimeSeconds = 0f;
            if (!lifetimeAlreadyZero)
                RaiseLifetimeChanged();

            CancelTickerSubscription();

            if (interactionCollider != null)
                interactionCollider.enabled = false;

            if (ownerOnlyCanvas != null)
                ownerOnlyCanvas.enabled = false;
            if (ownerOnlyText != null)
                ownerOnlyText.enabled = false;
            if (ownerOnlyIcon != null)
                ownerOnlyIcon.enabled = false;
            if (ownerOnlySpriteRenderers != null)
            {
                for (int i = 0; i < ownerOnlySpriteRenderers.Length; i++)
                {
                    if (ownerOnlySpriteRenderers[i] != null)
                        ownerOnlySpriteRenderers[i].enabled = false;
                }
            }

            if (playVfx && despawnVfxPrefab != null)
            {
                var vfxInstance = Instantiate(
                    despawnVfxPrefab,
                    transform.position,
                    despawnVfxPrefab.transform.rotation);
                Destroy(vfxInstance, 5f);
            }

            Expired?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>
        ///     Applies the ownership visibility rules based on whether the active profile matches the owner.
        /// </summary>
        private void UpdateOwnerOnlyVisuals()
        {
            bool isOwner = string.IsNullOrEmpty(ownerProfileId) ||
                           string.Equals(ownerProfileId, SaveManager.ActiveProfileId ?? string.Empty, StringComparison.Ordinal);

            if (ownerOnlyCanvas != null)
                ownerOnlyCanvas.enabled = isOwner;
            if (ownerOnlyText != null)
                ownerOnlyText.enabled = isOwner;
            if (ownerOnlyIcon != null)
                ownerOnlyIcon.enabled = isOwner && ownerOnlyIcon.sprite != null;

            if (ownerOnlySpriteRenderers == null)
                return;

            for (int i = 0; i < ownerOnlySpriteRenderers.Length; i++)
            {
                if (ownerOnlySpriteRenderers[i] != null)
                    ownerOnlySpriteRenderers[i].enabled = isOwner;
            }
        }

        /// <summary>
        ///     Ensures the definition supplied sprite is applied to any configured visuals.
        /// </summary>
        private void ApplyDefinitionVisuals()
        {
            if (sourceDefinition == null)
                return;

            if (ownerOnlyIcon != null && sourceDefinition.SoloLockSprite != null)
            {
                ownerOnlyIcon.sprite = sourceDefinition.SoloLockSprite;
                ownerOnlyIcon.enabled = ownerOnlyCanvas == null || ownerOnlyCanvas.enabled;
            }

            if (ownerOnlySpriteRenderers == null)
                return;

            for (int i = 0; i < ownerOnlySpriteRenderers.Length; i++)
            {
                var renderer = ownerOnlySpriteRenderers[i];
                if (renderer != null && sourceDefinition.SoloLockSprite != null)
                    renderer.sprite = sourceDefinition.SoloLockSprite;
            }
        }

        /// <summary>
        ///     Helper that raises the lifetime changed event with the current normalized value.
        /// </summary>
        private void RaiseLifetimeChanged()
        {
            float normalized = totalLifetimeSeconds > 0f
                ? Mathf.Clamp01(RemainingLifetimeSeconds / totalLifetimeSeconds)
                : 0f;

            LifetimeChanged?.Invoke(this, RemainingLifetimeSeconds, normalized);
        }
    }
}
