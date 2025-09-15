using UnityEngine;
using Combat;
using UI;
using System.Collections.Generic;
using EquipmentSystem;
using Skills;

namespace Player
{
    /// <summary>
    /// Adapts the player into a CombatTarget so NPCs can attack.
    /// </summary>
    [RequireComponent(typeof(PlayerHitpoints))]
    public class PlayerCombatTarget : MonoBehaviour, CombatTarget
    {
        private PlayerHitpoints hitpoints;
        private Sprite damageHitsplat;
        private Sprite zeroHitsplat;
        private Sprite burnHitsplat;
        private Sprite poisonHitsplat;
        private Dictionary<SpellElement, Sprite> elementHitsplats;

        private void Awake()
        {
            hitpoints = GetComponent<PlayerHitpoints>();
            damageHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Damage_hitsplat");
            zeroHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Zero_damage_hitsplat");
            burnHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Burn_hitsplat");
            poisonHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Poison_hitsplat");
            elementHitsplats = new Dictionary<SpellElement, Sprite>
            {
                { SpellElement.Air, Resources.Load<Sprite>("Sprites/HitSplats/Air_hitsplat") },
                { SpellElement.Water, Resources.Load<Sprite>("Sprites/HitSplats/Water_hitsplat") },
                { SpellElement.Earth, Resources.Load<Sprite>("Sprites/HitSplats/Earth_hitsplat") },
                { SpellElement.Electric, Resources.Load<Sprite>("Sprites/HitSplats/Electrocute_hitsplat") },
                { SpellElement.Ice, Resources.Load<Sprite>("Sprites/HitSplats/Water_hitsplat") },
                { SpellElement.Fire, burnHitsplat }
            };
        }

        public bool IsAlive => hitpoints.CurrentHp > 0;
        public DamageType PreferredDefenceType => DamageType.Melee;
        public int CurrentHP => hitpoints.CurrentHp;
        public int MaxHP => hitpoints.MaxHp;

        /// <summary>Get combat stats representing the player's current defensive state.</summary>
        public CombatantStats GetCombatantStats()
        {
            var binder = GetComponent<PlayerCombatBinder>();
            if (binder != null)
                return binder.GetCombatantStats();

            var loadout = GetComponent<PlayerCombatLoadout>();
            if (loadout != null)
                return loadout.GetCombatantStats();

            var skills = GetComponent<SkillManager>();
            var equipment = GetComponent<EquipmentAggregator>();
            return CombatantStats.ForPlayer(skills, equipment, CombatStyle.Defensive, PreferredDefenceType);
        }

        public int ApplyDamage(int amount, DamageType type, SpellElement element, object source)
        {
            hitpoints.OnEnemyDealtDamage(amount);
            Sprite sprite;
            Color textColor = Color.white;
            if (amount == 0)
                sprite = zeroHitsplat;
            else if (type == DamageType.Burn)
                sprite = burnHitsplat;
            else if (type == DamageType.Poison)
                sprite = poisonHitsplat;
            else if (type == DamageType.Magic && elementHitsplats != null && elementHitsplats.TryGetValue(element, out var elemSprite) && elemSprite != null)
            {
                sprite = elemSprite;
                if (element == SpellElement.Air)
                    textColor = Color.black;
            }
            else
                sprite = damageHitsplat;
            FloatingText.Show(amount.ToString(), transform.position, textColor, null, sprite);
            Debug.Log($"Player took {amount} damage.");
            return amount;
        }
    }
}
