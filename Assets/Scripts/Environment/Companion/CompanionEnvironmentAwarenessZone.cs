using System;
using System.Collections.Generic;
using Companions;
using Inventory;
using UI.Chat;
using UnityEngine;

namespace Environment.Companion
{
    /// <summary>
    /// Detects when the active companion enters a tagged environmental zone and, with a 1-in-20 chance,
    /// prompts them to react with flavour dialogue pulled from <see cref="CompanionChatLibrary"/>.
    /// The component should be attached to trigger colliders that bound contextual points of interest
    /// such as banks, caves, or coastal regions.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class CompanionEnvironmentAwarenessZone : MonoBehaviour
    {
        private enum AwarenessArea
        {
            Bank,
            MiningCave,
            MiningShop,
            GoblinCave,
            Ocean,
            Graveyard
        }

        private const int TriggerChanceDenominator = 20;

        /// <summary>
        /// Global toggle that controls whether this component mirrors its decision making to the console.
        /// The Admin F2 menu wires directly into this flag so QA can inspect why a companion did or did not
        /// react when entering an awareness zone.
        /// </summary>
        public static bool EnableDebugLogging { get; set; }

        /// <summary>Prefix included in every debug log so the output is easy to filter in the Unity console.</summary>
        private const string DebugLogPrefix = "[CompanionAwarenessZone]";

        /// <summary>Minimum velocity a companion must have for directional entry checks to pass.</summary>
        private const float MinimumDirectionalVelocity = 0.05f;

        [Header("Area Flags"), Tooltip("Mark the contextual identity of this zone so companions know how to react.")]
        [SerializeField]
        private bool areaIsBank;

        [SerializeField]
        private bool areaIsMiningCave;

        [SerializeField]
        private bool areaIsMiningShop;

        [SerializeField]
        private bool areaIsGoblinCave;

        [SerializeField]
        private bool areaIsOcean;

        [SerializeField]
        private bool areaIsGraveyard;

        [Header("Directional Entry"), Tooltip("Toggle whether the companion must enter along a specific heading before reactions are allowed.")]
        [SerializeField]
        private bool requireDirectionalEntry;

        [Tooltip("Normalized local-space vector describing the heading companions must travel along to trigger this zone.")]
        [SerializeField]
        private Vector2 requiredEntryDirection = Vector2.right;

        [Tooltip("Minimum dot product required between the companion velocity and the world-space heading to permit entry."), Range(0f, 1f)]
        [SerializeField]
        private float directionalDotProductTolerance = 0.75f;

        [Tooltip("Color used for the Scene view gizmo that visualises the required entry heading.")]
        [SerializeField]
        private Color directionalGizmoColor = Color.cyan;

        [Tooltip("Length of the Scene view gizmo arrow that illustrates the entry heading.")]
        [SerializeField, Min(0f)]
        private float directionalGizmoLength = 1.5f;

        [Header("Cooldown"), Tooltip("Minimum number of seconds before the same companion can react again inside this zone.")]
        [SerializeField, Min(0f)]
        private float retriggerCooldownSeconds = 10f;

        /// <summary>Keeps track of the last time each companion reacted so we can apply per-zone cooldowns.</summary>
        private readonly Dictionary<CompanionController, float> lastReactionTimes = new Dictionary<CompanionController, float>();

        /// <summary>Reusable buffer that stores the active flags for the current evaluation.</summary>
        private readonly List<AwarenessArea> activeAreas = new List<AwarenessArea>(6);

        /// <summary>Cached collider reference so we can confirm trigger state during <see cref="Awake"/>.</summary>
        private Collider2D cachedCollider;

        /// <summary>Stores the most recent awareness area used to generate a reaction so debug logs can report it.</summary>
        private AwarenessArea lastSelectedArea;

        /// <summary>Caches the most recent world-space heading evaluated for directional entry checks.</summary>
        private Vector2 cachedWorldEntryDirection = Vector2.right;

        private void Awake()
        {
            cachedCollider = GetComponent<Collider2D>();
            if (cachedCollider == null)
                return;

            if (!cachedCollider.isTrigger)
            {
                cachedCollider.isTrigger = true;
                Debug.LogWarning(
                    $"{nameof(CompanionEnvironmentAwarenessZone)} on {name} requires its collider to be marked as a trigger. The flag has been enabled automatically.",
                    this);
            }
        }

        private void OnDisable()
        {
            lastReactionTimes.Clear();
        }

        private void OnValidate()
        {
            if (requiredEntryDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                requiredEntryDirection = Vector2.right;
            }
            else
            {
                requiredEntryDirection = requiredEntryDirection.normalized;
            }

            directionalDotProductTolerance = Mathf.Clamp01(directionalDotProductTolerance);
            directionalGizmoLength = Mathf.Max(0f, directionalGizmoLength);
            cachedWorldEntryDirection = ComputeWorldEntryDirection();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var controller = ResolveCompanionController(other);
            if (controller == null)
            {
                LogDebug("Ignored trigger enter because no companion controller was found on the collider hierarchy.");
                return;
            }

            LogDebug($"Companion {controller.name} entered zone; evaluating awareness reaction.", controller);

            if (!EvaluateDirectionalEntryRequirement(other, controller))
                return;

            if (!HasAnyAreaFlag())
            {
                LogDebug("Zone has no area flags configured; skipping reaction.", controller);
                return;
            }

            bool shouldReact = RollTrigger();
            if (!shouldReact)
            {
                LogDebug("1-in-20 awareness roll failed; companion remains silent.", controller);
                return;
            }

            float now = Time.time;
            if (IsOnCooldown(controller, now, out float remainingCooldown))
            {
                LogDebug($"Companion is on cooldown for another {Mathf.Max(remainingCooldown, 0f):0.00}s; reaction aborted.", controller);
                return;
            }

            string message = BuildReaction(controller);
            if (string.IsNullOrWhiteSpace(message))
            {
                LogDebug("No awareness message could be resolved for the current zone configuration.", controller);
                return;
            }

            LogDebug($"Awareness roll succeeded for area {lastSelectedArea}; broadcasting: \"{message}\".", controller);

            var chat = ChatService.Instance;
            if (chat == null)
            {
                LogDebug("ChatService instance is unavailable; cannot publish awareness message.", controller);
                return;
            }

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
            lastReactionTimes[controller] = now;
            LogDebug("Awareness message published successfully; cooldown timer refreshed.", controller);
        }

        /// <summary>Resolves the <see cref="CompanionController"/> tied to the supplied collider.</summary>
        private static CompanionController ResolveCompanionController(Collider2D collider)
        {
            if (collider == null)
                return null;

            return collider.GetComponent<CompanionController>() ?? collider.GetComponentInParent<CompanionController>();
        }

        /// <summary>Returns <c>true</c> when any area flag is active on this zone.</summary>
        private bool HasAnyAreaFlag()
        {
            return areaIsBank || areaIsMiningCave || areaIsMiningShop || areaIsGoblinCave || areaIsOcean || areaIsGraveyard;
        }

        /// <summary>Rolls the 1-in-20 entry chance that governs whether the companion speaks.</summary>
        private static bool RollTrigger()
        {
            return UnityEngine.Random.Range(0, TriggerChanceDenominator) == 0;
        }

        /// <summary>Determines if the supplied companion is still cooling down from a previous reaction.</summary>
        private bool IsOnCooldown(CompanionController controller, float now, out float remainingCooldownSeconds)
        {
            remainingCooldownSeconds = 0f;
            if (controller == null)
                return true;

            if (!lastReactionTimes.TryGetValue(controller, out float lastTime))
                return false;

            float elapsed = now - lastTime;
            remainingCooldownSeconds = retriggerCooldownSeconds - elapsed;
            return remainingCooldownSeconds > 0f;
        }

        /// <summary>Builds the flavour message appropriate for the currently active zone flags.</summary>
        private string BuildReaction(CompanionController controller)
        {
            lastSelectedArea = default;
            activeAreas.Clear();
            if (areaIsBank)
                activeAreas.Add(AwarenessArea.Bank);
            if (areaIsMiningCave)
                activeAreas.Add(AwarenessArea.MiningCave);
            if (areaIsMiningShop)
                activeAreas.Add(AwarenessArea.MiningShop);
            if (areaIsGoblinCave)
                activeAreas.Add(AwarenessArea.GoblinCave);
            if (areaIsOcean)
                activeAreas.Add(AwarenessArea.Ocean);
            if (areaIsGraveyard)
                activeAreas.Add(AwarenessArea.Graveyard);

            if (activeAreas.Count == 0)
                return string.Empty;

            AwarenessArea selected = activeAreas[UnityEngine.Random.Range(0, activeAreas.Count)];
            lastSelectedArea = selected;
            switch (selected)
            {
                case AwarenessArea.Bank:
                    return CompanionChatLibrary.GetRandomBankAwarenessLine(IsInventoryFull(controller?.Inventory?.InventoryComponent));
                case AwarenessArea.MiningCave:
                    return CompanionChatLibrary.GetRandomMiningCaveAwarenessLine();
                case AwarenessArea.MiningShop:
                    return CompanionChatLibrary.GetRandomMiningShopAwarenessLine();
                case AwarenessArea.GoblinCave:
                    return CompanionChatLibrary.GetRandomGoblinCaveAwarenessLine();
                case AwarenessArea.Ocean:
                    return CompanionChatLibrary.GetRandomOceanAwarenessLine();
                case AwarenessArea.Graveyard:
                    return CompanionChatLibrary.GetRandomGraveyardAwarenessLine();
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the companion inventory exists and every slot is filled to capacity.
        /// Stackable entries must also be capped at their maximum stack size.
        /// </summary>
        private static bool IsInventoryFull(Inventory.Inventory inventory)
        {
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
        /// Emits a formatted debug log when <see cref="EnableDebugLogging"/> is active.
        /// Provides the zone name and companion context so QA can trace behaviour in the console.
        /// </summary>
        private void LogDebug(string message, CompanionController controller = null)
        {
            if (!EnableDebugLogging)
                return;

            string companionName = controller != null ? controller.name : "None";
            Debug.Log($"{DebugLogPrefix} Zone: {name}, Companion: {companionName} => {message}", this);
        }

        /// <summary>
        /// Validates the companion entry direction when directional gating is enabled.
        /// Blocks the trigger if the companion is moving too slowly or approaching from the wrong heading.
        /// </summary>
        private bool EvaluateDirectionalEntryRequirement(Collider2D other, CompanionController controller)
        {
            if (!requireDirectionalEntry)
                return true;

            Vector2 worldEntryDirection = ComputeWorldEntryDirection();
            cachedWorldEntryDirection = worldEntryDirection;

            Rigidbody2D body = other != null ? other.attachedRigidbody : null;
            if (body == null && controller != null)
                body = controller.GetComponent<Rigidbody2D>();

            if (body == null)
            {
                LogDebug("Directional entry is required but no Rigidbody2D velocity could be located; trigger blocked.", controller);
                return false;
            }

            Vector2 velocity = body.velocity;
            if (velocity.sqrMagnitude < MinimumDirectionalVelocity * MinimumDirectionalVelocity)
            {
                LogDebug("Directional entry blocked because the companion is moving too slowly to determine heading.", controller);
                return false;
            }

            Vector2 normalizedVelocity = velocity.normalized;
            float dot = Vector2.Dot(normalizedVelocity, worldEntryDirection);
            if (dot < directionalDotProductTolerance)
            {
                LogDebug($"Directional entry blocked; velocity alignment {dot:0.000} fell below tolerance {directionalDotProductTolerance:0.000}. Required heading: {cachedWorldEntryDirection}.", controller);
                return false;
            }

            return true;
        }

        /// <summary>Calculates the normalized world-space heading used for directional gating and gizmo rendering.</summary>
        private Vector2 ComputeWorldEntryDirection()
        {
            Vector3 local = new Vector3(requiredEntryDirection.x, requiredEntryDirection.y, 0f);
            Vector3 world = transform.TransformDirection(local);
            Vector2 planar = new Vector2(world.x, world.y);
            if (planar.sqrMagnitude <= Mathf.Epsilon)
                return Vector2.right;

            return planar.normalized;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (directionalGizmoLength <= 0f)
                return;

            Vector3 origin = transform.position;
            Vector2 worldDirection = ComputeWorldEntryDirection();
            cachedWorldEntryDirection = worldDirection;
            Vector3 direction3D = new Vector3(worldDirection.x, worldDirection.y, 0f);
            if (direction3D.sqrMagnitude <= Mathf.Epsilon)
                direction3D = Vector3.right;

            Vector3 normalizedDirection = direction3D.normalized;
            Vector3 tip = origin + normalizedDirection * directionalGizmoLength;

            Gizmos.color = directionalGizmoColor;
            Gizmos.DrawLine(origin, tip);

            const float arrowHeadAngle = 25f;
            float arrowHeadLength = Mathf.Max(0.1f, directionalGizmoLength * 0.25f);
            Quaternion rightRotation = Quaternion.AngleAxis(180f - arrowHeadAngle, Vector3.forward);
            Quaternion leftRotation = Quaternion.AngleAxis(-(180f - arrowHeadAngle), Vector3.forward);
            Gizmos.DrawLine(tip, tip + (rightRotation * normalizedDirection) * arrowHeadLength);
            Gizmos.DrawLine(tip, tip + (leftRotation * normalizedDirection) * arrowHeadLength);
        }
#endif
    }
}
