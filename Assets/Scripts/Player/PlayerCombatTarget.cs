using UnityEngine;
using Combat;
using UI;
using System.Collections.Generic;

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
        private Dictionary<SpellElement, Sprite> elementHitsplats;

        private void Awake()
        {
            hitpoints = GetComponent<PlayerHitpoints>();
            damageHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Damage_hitsplat");
            zeroHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Zero_damage_hitsplat");
            burnHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Burn_hitsplat");
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

        public int ApplyDamage(int amount, DamageType type, SpellElement element, object source)
        {
            hitpoints.OnEnemyDealtDamage(amount);
            Sprite sprite;
            Color textColor = Color.white;
            if (amount == 0)
                sprite = zeroHitsplat;
            else if (type == DamageType.Burn)
                sprite = burnHitsplat;
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
