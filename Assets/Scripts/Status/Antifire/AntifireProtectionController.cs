using System;
using Combat;
using Inventory;
using UnityEngine;

namespace Status.Antifire
{
    /// <summary>
    /// Manages the antifire status effect for an entity and exposes helper methods for
    /// mitigating dragonfire damage. The controller queries <see cref="BuffTimerService"/>
    /// for active buffs and inspects equipped shields to determine the correct reduction
    /// to apply when dragonfire damage is received.
    /// </summary>
    [DisallowMultipleComponent]
    public class AntifireProtectionController : MonoBehaviour
    {
        /// <summary>Standard antifire buff duration in seconds (3 minutes).</summary>
        public const float StandardAntifireDurationSeconds = 180f;

        private const float AntifireBuffDamageReduction = 0.25f;
        private const float DragonfireShieldDamageReduction = 0.75f;

        [SerializeField, Tooltip("Equipment component used to query the player's shield slot.")]
        private Equipment equipment;

        [SerializeField, Tooltip("Case-insensitive identifiers that should be treated as a dragonfire shield.")]
        private string[] dragonfireShieldIdentifiers = { "dragonfire_shield", "Dragonfire shield" };

        private void Awake()
        {
            if (equipment == null)
            {
                equipment = GetComponent<Equipment>() ?? GetComponentInParent<Equipment>() ?? GetComponentInChildren<Equipment>();
            }
        }

        /// <summary>
        /// Applies antifire mitigation to an incoming hit and returns the final damage value.
        /// </summary>
        /// <param name="damage">Raw damage rolled by the attacker.</param>
        /// <param name="type">Damage type of the hit.</param>
        public int ModifyDamage(int damage, DamageType type)
        {
            if (damage <= 0)
                return Mathf.Max(0, damage);

            if (type != DamageType.Dragonfire)
                return damage;

            if (HasBuff(BuffType.SuperAntifire))
                return 0;

            bool hasAntifire = HasBuff(BuffType.Antifire);
            bool hasShield = HasDragonfireShieldEquipped();

            if (hasAntifire && hasShield)
                return 0;

            float reduction = 0f;
            if (hasShield)
                reduction = Mathf.Max(reduction, DragonfireShieldDamageReduction);
            if (hasAntifire)
                reduction = Mathf.Max(reduction, AntifireBuffDamageReduction);

            int mitigated = Mathf.FloorToInt(damage * (1f - reduction));
            return Mathf.Clamp(mitigated, 0, damage);
        }

        /// <summary>
        /// Returns true if any antifire style buff is currently active on this entity.
        /// </summary>
        public bool HasActiveAntifireBuff()
        {
            return HasBuff(BuffType.Antifire) || HasBuff(BuffType.SuperAntifire);
        }

        /// <summary>
        /// Returns true if a recognised dragonfire shield is equipped in the shield slot.
        /// </summary>
        public bool HasDragonfireShieldEquipped()
        {
            if (equipment == null)
                return false;

            var entry = equipment.GetEquipped(EquipmentSlot.Shield);
            var item = entry.item;
            if (item == null)
                return false;

            return MatchesIdentifier(item.id) || MatchesIdentifier(item.itemName) || MatchesIdentifier(item.name);
        }

        /// <summary>
        /// Builds the default antifire buff definition used by consumables and debug tools.
        /// </summary>
        public static BuffTimerDefinition BuildStandardAntifireBuffDefinition()
        {
            return new BuffTimerDefinition
            {
                type = BuffType.Antifire,
                displayName = "Antifire",
                iconId = "antifire",
                durationSeconds = StandardAntifireDurationSeconds,
                recurringIntervalSeconds = 0f,
                isRecurring = false,
                showExpiryWarning = true,
                expiryWarningTicks = 0
            };
        }

        /// <summary>
        /// Checks whether the requested buff type is currently active on this GameObject.
        /// </summary>
        private bool HasBuff(BuffType type)
        {
            var service = BuffTimerService.Instance;
            if (service == null)
                return false;
            return service.TryGetBuff(gameObject, type, out _);
        }

        /// <summary>
        /// Compares a candidate identifier against the configured dragonfire shield identifiers.
        /// </summary>
        private bool MatchesIdentifier(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || dragonfireShieldIdentifiers == null)
                return false;

            for (int i = 0; i < dragonfireShieldIdentifiers.Length; i++)
            {
                string identifier = dragonfireShieldIdentifiers[i];
                if (string.IsNullOrEmpty(identifier))
                    continue;

                if (string.Equals(candidate, identifier, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
