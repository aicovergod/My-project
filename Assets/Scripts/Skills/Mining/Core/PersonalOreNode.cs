using System;
using Combat;
using Core.Save;
using Skills.Common;
using Skills;
using UI;
using UnityEngine;
using UnityEngine.UI;
using Util;

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

        [Header("Despawn Rules")]
        [Tooltip("Maximum distance (in tiles) the owner can move away before the node despawns.")]
        [SerializeField] private float ownerDespawnDistanceTiles = 12f;

        private static readonly string[] OwnerOverlayPreferredSortingLayerNames = { "UI", "Characters" };
        private static readonly string[] CharacterLayerNames = { "Player", "Pets", "NPC" };

        private const string OwnerOverlayLayerName = "UI";
        private const int OwnerOverlaySortingOrderOffset = 50;
        private const int OwnerOverlayMinimumSortingOrder = 1000;
        private const int CharacterLayerSortingSafetyOffset = 10;

        private static int[] cachedCharacterLayers;
        private static bool characterLayerCacheInitialized;

        private MiningPersonalNodeController ownerController;
        private MineableRock mineableRock;
        private OreMonsterNodeDefinition sourceDefinition;
        private string ownerProfileId = string.Empty;
        private float totalLifetimeSeconds;
        private float remainingLifetimeSeconds;
        private bool isExpired;
        private bool isOwnedByLocalProfile;

        /// <summary>
        ///     Indicates whether the active local profile owns the node. The value updates whenever
        ///     <see cref="ConfigureOwnershipVisibility"/> runs so ownership checks remain cached for
        ///     quick queries.
        /// </summary>
        public bool IsOwnedByLocalProfile => isOwnedByLocalProfile;

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

        /// <summary>
        ///     Canvas that renders the solo-lock icon and lifetime timer for the owning player.
        /// </summary>
        public Canvas OwnerOnlyCanvas => ownerOnlyCanvas;

        /// <summary>
        ///     Sorting layer identifier applied to the ownership canvas so other systems can align
        ///     world-space UI (such as the mining progress bar) without guessing priorities.
        /// </summary>
        public int OwnerOverlaySortingLayerId => ownerOnlyCanvas != null ? ownerOnlyCanvas.sortingLayerID : 0;

        /// <summary>
        ///     Sorting order assigned to the ownership canvas. Returns <see cref="int.MinValue"/>
        ///     when no canvas is configured so callers can detect the absence of overlay data.
        /// </summary>
        public int OwnerOverlaySortingOrder => ownerOnlyCanvas != null ? ownerOnlyCanvas.sortingOrder : int.MinValue;

        private void Awake()
        {
            // Ensure the required mineable rock component exists so the prefab remains valid.
            mineableRock = GetComponent<MineableRock>();

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

            if (mineableRock == null)
                mineableRock = GetComponent<MineableRock>();

            if (mineableRock != null && definition != null && definition.PersonalRockDefinition != null)
                mineableRock.rockDef = definition.PersonalRockDefinition;

            LegacyFontProvider.ApplyTo(ownerOnlyText);

            ApplyDefinitionVisuals();
            ConfigureOwnerOverlaySorting();
            ConfigureOwnershipVisibility();
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
        ///     Allows external systems to change ownership at runtime and immediately updates visibility.
        /// </summary>
        /// <param name="newOwnerId">New owner profile identifier.</param>
        public void UpdateOwnerProfile(string newOwnerId)
        {
            string normalizedOwnerId = string.IsNullOrWhiteSpace(newOwnerId) ? string.Empty : newOwnerId;
            if (string.Equals(ownerProfileId, normalizedOwnerId, StringComparison.Ordinal))
                return;

            ownerProfileId = normalizedOwnerId;
            ConfigureOwnershipVisibility();
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

            RefreshOwnershipCacheIfNeeded();

            if (!string.IsNullOrEmpty(ownerProfileId) && !isOwnedByLocalProfile)
            {
                failureMessage = "Only the victorious miner can claim this ore.";
                return false;
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

            RefreshOwnershipCacheIfNeeded();

            if (ShouldExpireDueToOwnerDistance())
            {
                // Owner moved too far away; silently collapse the node without VFX spam.
                ForceExpire(false);
                return;
            }

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
            ConfigureOwnershipVisibility();

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
        private void ConfigureOwnershipVisibility()
        {
            isOwnedByLocalProfile = DetermineLocalOwnership();

            bool shouldShowOwnerVisuals = isOwnedByLocalProfile && !isExpired;

            if (ownerOnlyCanvas != null)
                ownerOnlyCanvas.enabled = shouldShowOwnerVisuals;
            if (ownerOnlyText != null)
                ownerOnlyText.enabled = shouldShowOwnerVisuals;
            if (ownerOnlyIcon != null)
                ownerOnlyIcon.enabled = shouldShowOwnerVisuals && ownerOnlyIcon.sprite != null;

            if (interactionCollider != null)
                interactionCollider.enabled = shouldShowOwnerVisuals;

            if (ownerOnlySpriteRenderers == null)
                return;

            for (int i = 0; i < ownerOnlySpriteRenderers.Length; i++)
            {
                if (ownerOnlySpriteRenderers[i] != null)
                    ownerOnlySpriteRenderers[i].enabled = shouldShowOwnerVisuals;
            }
        }

        /// <summary>
        ///     Re-evaluates the cached ownership flag and reconfigures visuals when the value changes.
        /// </summary>
        private void RefreshOwnershipCacheIfNeeded()
        {
            bool currentOwnership = DetermineLocalOwnership();
            if (currentOwnership != isOwnedByLocalProfile)
                ConfigureOwnershipVisibility();
        }

        /// <summary>
        ///     Ensures the definition supplied sprite is applied to any configured visuals.
        /// </summary>
        private void ApplyDefinitionVisuals()
        {
            if (sourceDefinition == null)
                return;

            if (ownerOnlyIcon != null)
                ownerOnlyIcon.sprite = sourceDefinition.SoloLockSprite;

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
        ///     Ensures the ownership overlay (solo-lock icon and timer) renders above the rock and
        ///     other character layers so the player can always see their reservation details.
        /// </summary>
        private void ConfigureOwnerOverlaySorting()
        {
            if (ownerOnlyCanvas == null)
                return;

            ApplyOwnerOverlayLayer();
            ownerOnlyCanvas.overrideSorting = true;

            var referenceRenderer = ResolveReferenceRenderer();

            string overlaySortingLayerName = ResolveOwnerOverlaySortingLayerName();

            if (!string.IsNullOrEmpty(overlaySortingLayerName))
            {
                ownerOnlyCanvas.sortingLayerName = overlaySortingLayerName;
            }
            else if (referenceRenderer != null)
            {
                ownerOnlyCanvas.sortingLayerID = referenceRenderer.sortingLayerID;
            }

            int desiredOrder = OwnerOverlayMinimumSortingOrder;
            if (referenceRenderer != null)
            {
                desiredOrder = Mathf.Max(
                    desiredOrder,
                    referenceRenderer.sortingOrder + OwnerOverlaySortingOrderOffset);
            }

            int characterSortingCeiling = ResolveActiveCharacterSortingOrder();
            if (characterSortingCeiling > int.MinValue)
            {
                desiredOrder = Mathf.Max(
                    desiredOrder,
                    characterSortingCeiling + CharacterLayerSortingSafetyOffset);
            }

            ownerOnlyCanvas.sortingOrder = desiredOrder;

            if (ownerOnlySpriteRenderers == null)
                return;

            for (int i = 0; i < ownerOnlySpriteRenderers.Length; i++)
            {
                var renderer = ownerOnlySpriteRenderers[i];
                if (renderer == null)
                    continue;

                if (!string.IsNullOrEmpty(overlaySortingLayerName))
                    renderer.sortingLayerName = overlaySortingLayerName;
                else if (referenceRenderer != null)
                    renderer.sortingLayerID = referenceRenderer.sortingLayerID;

                renderer.sortingOrder = desiredOrder;
            }
        }

        /// <summary>
        ///     Scans active character sprite renderers so other systems can align their sorting
        ///     orders with the currently visible actors.
        /// </summary>
        /// <returns>
        ///     Highest sorting order used by characters on the Player, Pets, or NPC layers, or
        ///     <see cref="int.MinValue"/> when none are active.
        /// </returns>
        public static int ResolveActiveCharacterSortingOrder()
        {
            EnsureCharacterLayerCache();

#if UNITY_2023_1_OR_NEWER
            var renderers = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            var renderers = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
#endif

            int maximumOrder = int.MinValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                // SpriteRenderers do not inherit from Behaviour, so they do not expose
                // isActiveAndEnabled. Instead, validate both the GameObject hierarchy
                // state and the renderer's enabled flag to ensure the sprite is visible.
                if (!renderer.gameObject.activeInHierarchy || !renderer.enabled)
                    continue;

                if (!IsCharacterLayer(renderer.gameObject.layer))
                    continue;

                maximumOrder = Mathf.Max(maximumOrder, renderer.sortingOrder);
            }

            return maximumOrder;
        }

        /// <summary>
        ///     Determines the most appropriate sorting layer for the owner overlay.
        /// </summary>
        private static string ResolveOwnerOverlaySortingLayerName()
        {
            for (int i = 0; i < OwnerOverlayPreferredSortingLayerNames.Length; i++)
            {
                string candidate = OwnerOverlayPreferredSortingLayerNames[i];
                if (SortingLayerExists(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        /// <summary>
        ///     Ensures the character layer indices are cached so repeated lookups remain efficient.
        /// </summary>
        private static void EnsureCharacterLayerCache()
        {
            if (characterLayerCacheInitialized && cachedCharacterLayers != null &&
                cachedCharacterLayers.Length == CharacterLayerNames.Length)
            {
                return;
            }

            if (cachedCharacterLayers == null || cachedCharacterLayers.Length != CharacterLayerNames.Length)
                cachedCharacterLayers = new int[CharacterLayerNames.Length];

            for (int i = 0; i < CharacterLayerNames.Length; i++)
                cachedCharacterLayers[i] = LayerMask.NameToLayer(CharacterLayerNames[i]);

            characterLayerCacheInitialized = true;
        }

        /// <summary>
        ///     Determines whether the supplied layer index corresponds to one of the configured
        ///     character layers.
        /// </summary>
        private static bool IsCharacterLayer(int layer)
        {
            if (layer < 0)
                return false;

            EnsureCharacterLayerCache();

            if (cachedCharacterLayers == null)
                return false;

            for (int i = 0; i < cachedCharacterLayers.Length; i++)
            {
                if (cachedCharacterLayers[i] == layer && cachedCharacterLayers[i] >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Applies the UI physics layer to the ownership overlay so it renders above characters
        ///     and participates in layer-based filtering correctly.
        /// </summary>
        private void ApplyOwnerOverlayLayer()
        {
            if (ownerOnlyCanvas == null)
                return;

            ApplyOwnerOverlayLayer(ownerOnlyCanvas.gameObject);

            if (ownerOnlySpriteRenderers == null)
                return;

            for (int i = 0; i < ownerOnlySpriteRenderers.Length; i++)
            {
                var renderer = ownerOnlySpriteRenderers[i];
                if (renderer != null)
                    ApplyOwnerOverlayLayer(renderer.gameObject);
            }
        }

        /// <summary>
        ///     Applies the configured overlay layer to the provided game object and its children.
        /// </summary>
        private void ApplyOwnerOverlayLayer(GameObject target)
        {
            if (target == null)
                return;

            int overlayLayer = LayerMask.NameToLayer(OwnerOverlayLayerName);
            if (overlayLayer < 0)
                return;

            LayerUtility.SetLayerRecursively(target.transform, overlayLayer);
        }

        /// <summary>
        ///     Attempts to locate the sprite renderer responsible for visualising the rock so the
        ///     overlay can inherit sensible sorting priorities.
        /// </summary>
        private SpriteRenderer ResolveReferenceRenderer()
        {
            if (mineableRock != null)
            {
                var renderer = mineableRock.GetComponent<SpriteRenderer>();
                if (renderer != null)
                    return renderer;
            }

            return GetComponent<SpriteRenderer>();
        }

        /// <summary>
        ///     Determines whether a sorting layer with the supplied name exists in the project.
        /// </summary>
        private static bool SortingLayerExists(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
                return false;

            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (string.Equals(layers[i].name, layerName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Updates the countdown label with the formatted remaining lifetime if a label is present.
        /// </summary>
        private void UpdateCountdownVisuals()
        {
            if (ownerOnlyText == null)
                return;

            int secondsRemaining = Mathf.Max(0, Mathf.CeilToInt(RemainingLifetimeSeconds));
            var remaining = TimeSpan.FromSeconds(secondsRemaining);
            ownerOnlyText.text = remaining.ToString("mm\\:ss");
        }

        /// <summary>
        ///     Evaluates whether the currently active profile owns this personal node.
        /// </summary>
        private bool DetermineLocalOwnership()
        {
            if (string.IsNullOrEmpty(ownerProfileId))
                return true;

            string activeProfileId = SaveManager.ActiveProfileId ?? string.Empty;
            return string.Equals(ownerProfileId, activeProfileId, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Helper that raises the lifetime changed event with the current normalized value.
        /// </summary>
        private void RaiseLifetimeChanged()
        {
            float normalized = totalLifetimeSeconds > 0f
                ? Mathf.Clamp01(RemainingLifetimeSeconds / totalLifetimeSeconds)
                : 0f;

            UpdateCountdownVisuals();
            LifetimeChanged?.Invoke(this, RemainingLifetimeSeconds, normalized);
        }

        /// <summary>
        ///     Determines whether the owning player has exceeded the despawn distance threshold.
        ///     When the owner leaves the allowed radius the node should immediately expire.
        /// </summary>
        private bool ShouldExpireDueToOwnerDistance()
        {
            if (ownerController == null)
                return true;

            var controllerTransform = ownerController.transform;
            if (controllerTransform == null || !ownerController.isActiveAndEnabled)
                return true;

            float maxDistanceTiles = Mathf.Max(0f, ownerDespawnDistanceTiles);
            if (maxDistanceTiles <= 0f)
                return false;

            Vector2 ownerPosition = controllerTransform.position;
            Vector2 nodePosition = transform.position;
            float sqrDistance = (ownerPosition - nodePosition).sqrMagnitude;
            float maxDistanceWorld = maxDistanceTiles; // Tile size is 1 unit in world space.
            float maxDistanceSqr = maxDistanceWorld * maxDistanceWorld;

            return sqrDistance > maxDistanceSqr;
        }
    }
}
