using UnityEngine;
using Combat;
using UI;

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

        private void Awake()
        {
            hitpoints = GetComponent<PlayerHitpoints>();
            damageHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Damage_hitsplat");
            zeroHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Zero_damage_hitsplat");
            burnHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Burn_hitsplat");
        }

        public bool IsAlive => hitpoints.CurrentHp > 0;
        public DamageType PreferredDefenceType => DamageType.Melee;
        public int CurrentHP => hitpoints.CurrentHp;
        public int MaxHP => hitpoints.MaxHp;

        public void ApplyDamage(int amount, DamageType type, object source)
        {
            hitpoints.OnEnemyDealtDamage(amount);
            Sprite sprite;
            if (amount == 0)
                sprite = zeroHitsplat;
            else if (type == DamageType.Burn && burnHitsplat != null)
                sprite = burnHitsplat;
            else
                sprite = damageHitsplat;
            FloatingText.Show(amount.ToString(), transform.position, Color.white, null, sprite);
            Debug.Log($"Player took {amount} damage.");
        }
    }
}
