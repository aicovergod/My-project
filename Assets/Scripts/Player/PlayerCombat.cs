using UnityEngine;
using Inventory;
using Combat;

namespace Player
{
    /// <summary>
    /// Handles player initiated combat interactions and forwards
    /// attacks to the <see cref="CombatManager"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private PlayerHitpoints hitpoints;
        [SerializeField] private Equipment equipment;
        [SerializeField] private int baseAttack = 1;
        [SerializeField] private int baseDefence = 1;

        private Enemy currentTarget;

        public PlayerHitpoints Hitpoints => hitpoints;
        public int Attack => baseAttack + (equipment != null ? equipment.TotalAttackBonus : 0);
        public int Defence => baseDefence + (equipment != null ? equipment.TotalDefenceBonus : 0);

        public int AttackSpeedTicks
        {
            get
            {
                var weapon = equipment != null ? equipment.GetEquipped(EquipmentSlot.Weapon).item : null;
                return weapon != null ? Mathf.Max(1, weapon.attackSpeed) : 4;
            }
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0) && currentTarget != null)
            {
                CombatManager.Instance?.Engage(this, currentTarget);
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CombatManager.Instance?.Disengage();
                currentTarget = null;
            }
        }

        public void SetTarget(Enemy enemy)
        {
            currentTarget = enemy;
        }

        public int AttackRoll()
        {
            return Attack;
        }
    }
}
