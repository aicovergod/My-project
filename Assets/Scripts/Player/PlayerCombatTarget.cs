using UnityEngine;
using Combat;
using Skills.Mining;

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

        private void Awake()
        {
            hitpoints = GetComponent<PlayerHitpoints>();
            damageHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Damage_hitsplat");
            zeroHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Zero_damage_hitsplat");
        }

        public bool IsAlive => hitpoints.CurrentHp > 0;
        public DamageType PreferredDefenceType => DamageType.Melee;
        public int CurrentHP => hitpoints.CurrentHp;
        public int MaxHP => hitpoints.MaxHp;

        public void ApplyDamage(int amount, DamageType type, object source)
        {
            hitpoints.OnEnemyDealtDamage(amount);
            var sprite = amount == 0 ? zeroHitsplat : damageHitsplat;
            FloatingText.Show(amount.ToString(), transform.position, Color.white, null, sprite);
            Debug.Log($"Player took {amount} damage.");
        }
    }
}
