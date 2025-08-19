using System.Collections;
using UnityEngine;
using EquipmentSystem;
using Skills;
using Player;

namespace Combat
{
    /// <summary>
    /// Handles combat resolution and XP assignment using OSRS-style formulas.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatController : MonoBehaviour
    {
        public event System.Action OnAttackStart;
        public event System.Action<int, bool> OnAttackLanded;
        public event System.Action<CombatTarget> OnTargetKilled;

        private SkillManager skills;
        private PlayerHitpoints hitpoints;
        private EquipmentAggregator equipment;
        private Player.PlayerCombatLoadout loadout;

        private void Awake()
        {
            skills = GetComponent<SkillManager>();
            hitpoints = GetComponent<PlayerHitpoints>();
            equipment = GetComponent<EquipmentAggregator>();
            loadout = GetComponent<Player.PlayerCombatLoadout>();
        }

        /// <summary>
        /// Attempt to attack the specified target. Returns false if not ready.
        /// </summary>
        public bool TryAttackTarget(CombatTarget target)
        {
            if (target == null || !target.IsAlive)
                return false;
            if (Vector2.Distance(transform.position, target.transform.position) > CombatMath.MELEE_RANGE)
                return false;
            StopAllCoroutines();
            StartCoroutine(AttackRoutine(target));
            return true;
        }

        private IEnumerator AttackRoutine(CombatTarget target)
        {
            while (target != null && target.IsAlive)
            {
                if (Vector2.Distance(transform.position, target.transform.position) > CombatMath.MELEE_RANGE)
                    yield break;
                OnAttackStart?.Invoke();
                ResolveAttack(target);
                float interval = equipment != null ? equipment.GetCombinedStats().attackSpeedTicks * CombatMath.TICK_SECONDS : 4 * CombatMath.TICK_SECONDS;
                yield return new WaitForSeconds(interval);
            }
        }

        private void ResolveAttack(CombatTarget target)
        {
            var attacker = CombatantStats.ForPlayer(skills, equipment, loadout != null ? loadout.Style : CombatStyle.Accurate, DamageType.Melee);
            var defender = new CombatantStats
            {
                AttackLevel = 1,
                StrengthLevel = 1,
                DefenceLevel = 1,
                Equip = new EquipmentAggregator.CombinedStats(),
                Style = CombatStyle.Defensive,
                DamageType = target.PreferredDefenceType
            };

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
            float chance = CombatMath.ChanceToHit(atkRoll, defRoll);
            bool hit = Random.value < chance;
            int damage = 0;
            var targetMb = target as MonoBehaviour;
            string targetName = targetMb != null ? targetMb.name : "target";
            if (hit)
            {
                int strEff = CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                int maxHit = CombatMath.GetMaxHit(strEff, attacker.Equip.strength);
                damage = CombatMath.RollDamage(maxHit);
                target.ApplyDamage(damage, attacker.DamageType, this);
                AwardXp(damage, attacker.Style);
                if (!target.IsAlive)
                    OnTargetKilled?.Invoke(target);
                Debug.Log($"Player dealt {damage} damage to {targetName}.");
            }
            else
            {
                Debug.Log($"Player missed {targetName}.");
            }
            OnAttackLanded?.Invoke(damage, hit);
        }

        private void AwardXp(int damage, CombatStyle style)
        {
            if (damage <= 0)
                return;
            hitpoints?.GainHitpointsXP(damage * 1.33f);
            switch (style)
            {
                case CombatStyle.Accurate:
                    skills?.AddXP(SkillType.Attack, 4 * damage);
                    break;
                case CombatStyle.Aggressive:
                    skills?.AddXP(SkillType.Strength, 4 * damage);
                    break;
                case CombatStyle.Defensive:
                    skills?.AddXP(SkillType.Defence, 4 * damage);
                    break;
                case CombatStyle.Controlled:
                    float total = 4f * damage;
                    int share = Mathf.FloorToInt(total / 3f);
                    int remainder = Mathf.RoundToInt(total - share * 3);
                    skills?.AddXP(SkillType.Attack, share);
                    skills?.AddXP(SkillType.Strength, share);
                    skills?.AddXP(SkillType.Defence, share + remainder);
                    break;
            }
        }

        [ContextMenu("Test/Do Dummy Swing vs Target")]
        private void DoDummySwing()
        {
            var dummy = new DummyTarget();
            ResolveAttack(dummy);
            Debug.Log("Dummy swing complete");
        }

#if UNITY_EDITOR
        [ContextMenu("Test/Simulate 10000 Swings")]
        private void SimulateSwings()
        {
            var attacker = CombatantStats.ForPlayer(skills, equipment, loadout != null ? loadout.Style : CombatStyle.Accurate, DamageType.Melee);
            int attEff = CombatMath.GetEffectiveAttack(attacker.AttackLevel, attacker.Style);
            int atkRoll = CombatMath.GetAttackRoll(attEff, attacker.Equip.attack);
            int defRoll = CombatMath.GetDefenceRoll(CombatMath.GetEffectiveDefence(1, CombatStyle.Defensive), 0);
            int hitCount = 0;
            int totalDamage = 0;
            for (int i = 0; i < 10000; i++)
            {
                float chance = CombatMath.ChanceToHit(atkRoll, defRoll);
                if (Random.value < chance)
                {
                    int strEff = CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                    int maxHit = CombatMath.GetMaxHit(strEff, attacker.Equip.strength);
                    int dmg = CombatMath.RollDamage(maxHit);
                    totalDamage += dmg;
                    hitCount++;
                }
            }

            float hitRate = hitCount / 10000f;
            float avgDmg = totalDamage / 10000f;
            Debug.Log($"Simulated 10000 swings. HitRate={hitRate:F3} AvgDamage={avgDmg:F2}");
        }
#endif

        private class DummyTarget : CombatTarget
        {
            public Transform transform => null;
            public bool IsAlive => true;
            public DamageType PreferredDefenceType => DamageType.Melee;
            public int CurrentHP => 10;
            public int MaxHP => 10;
            public void ApplyDamage(int amount, DamageType type, object source)
            {
                Debug.Log($"Dummy took {amount} damage");
            }
        }
    }
}
