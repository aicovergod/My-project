using System.Collections;
using UnityEngine;
using Combat;
using EquipmentSystem;
using Skills;
using Player;

namespace NPC
{
    /// <summary>
    /// Handles NPC auto-attacks against the player during combat.
    /// </summary>
    [RequireComponent(typeof(NpcCombatant))]
    public class NpcAttackController : MonoBehaviour
    {
        private NpcCombatant combatant;

        private void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
        }

        public void BeginAttacking(PlayerCombatTarget target)
        {
            StopAllCoroutines();
            if (target != null)
                StartCoroutine(AttackRoutine(target));
        }

        private IEnumerator AttackRoutine(PlayerCombatTarget target)
        {
            var wait = new WaitForSeconds(4 * CombatMath.TICK_SECONDS);

            // Wait until the player is within melee range before performing the first attack.
            while (target != null && target.IsAlive && combatant.IsAlive &&
                   Vector2.Distance(target.transform.position, transform.position) > CombatMath.MELEE_RANGE)
            {
                // Poll each frame until the target is close enough or combat ends.
                yield return null;
            }

            while (target != null && target.IsAlive && combatant.IsAlive)
            {
                // If the player moves out of melee range, stop attacking.
                if (Vector2.Distance(target.transform.position, transform.position) > CombatMath.MELEE_RANGE)
                    yield break;

                ResolveAttack(target);
                yield return wait;
            }
        }

        private void ResolveAttack(PlayerCombatTarget target)
        {
            var attacker = combatant.GetCombatantStats();
            var skills = target.GetComponent<SkillManager>();
            var equipment = target.GetComponent<EquipmentAggregator>();
            var loadout = target.GetComponent<PlayerCombatLoadout>();

            var defender = CombatantStats.ForPlayer(skills, equipment,
                loadout != null ? loadout.Style : CombatStyle.Defensive,
                DamageType.Melee);

            int attEff = CombatMath.GetEffectiveAttack(attacker.AttackLevel, attacker.Style);
            int defEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            int atkRoll = CombatMath.GetAttackRoll(attEff, attacker.Equip.attack);
            int defBonus = defender.DamageType switch
            {
                DamageType.Magic => defender.Equip.magicDef,
                DamageType.Ranged => defender.Equip.rangeDef,
                _ => defender.Equip.meleeDef
            };
            int defRoll = CombatMath.GetDefenceRoll(defEff, defBonus);
            bool hit = Random.value < CombatMath.ChanceToHit(atkRoll, defRoll);
            int damage = 0;
            if (hit)
            {
                int strEff = CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                int maxHit = CombatMath.GetMaxHit(strEff, attacker.Equip.strength);
                damage = CombatMath.RollDamage(maxHit);
                target.ApplyDamage(damage, attacker.DamageType, this);
                Debug.Log($"{name} dealt {damage} damage to player.");
            }
            else
            {
                Debug.Log($"{name} missed player.");
            }
        }
    }
}
