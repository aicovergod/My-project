using System.Collections;
using UnityEngine;
using EquipmentSystem;
using Skills;
using Player;
using NPC;
using Pets;
using UI;
using Magic;

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
        public event System.Action<CombatTarget> OnCombatTargetChanged;

        private SkillManager skills;
        private PlayerHitpoints hitpoints;
        private EquipmentAggregator equipment;
        private Player.PlayerCombatLoadout loadout;
        private PlayerCombatBinder combatBinder;
        private PlayerMover mover;
        private Coroutine attackRoutine;
        private CombatTarget currentTarget;
        private float nextAttackTime;

        private CombatStyle pendingStyle;
        private DamageType pendingType;
        private int pendingMaxHit;

        private Sprite damageHitsplat;
        private Sprite zeroHitsplat;
        private Sprite maxHitHitsplat;

        private void Awake()
        {
            // Grab required components from this object, falling back to parent/children so the
            // controller still works if supporting components live elsewhere in the hierarchy.
            skills = GetComponent<SkillManager>() ?? GetComponentInParent<SkillManager>() ?? GetComponentInChildren<SkillManager>();
            hitpoints = GetComponent<PlayerHitpoints>() ?? GetComponentInParent<PlayerHitpoints>() ?? GetComponentInChildren<PlayerHitpoints>();
            equipment = GetComponent<EquipmentAggregator>() ?? GetComponentInParent<EquipmentAggregator>() ?? GetComponentInChildren<EquipmentAggregator>();
            loadout = GetComponent<Player.PlayerCombatLoadout>() ?? GetComponentInParent<Player.PlayerCombatLoadout>() ?? GetComponentInChildren<Player.PlayerCombatLoadout>();
            combatBinder = GetComponent<PlayerCombatBinder>() ?? GetComponentInParent<PlayerCombatBinder>() ?? GetComponentInChildren<PlayerCombatBinder>();
            mover = GetComponent<PlayerMover>() ?? GetComponentInParent<PlayerMover>() ?? GetComponentInChildren<PlayerMover>();

            if (skills == null)
                Debug.LogWarning("CombatController could not find a SkillManager; damage will use level 1 stats.", this);
            if (equipment == null)
                Debug.LogWarning("CombatController could not find an EquipmentAggregator; equipment bonuses will be ignored.", this);

            damageHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Damage_hitsplat");
            zeroHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Zero_damage_hitsplat");
            maxHitHitsplat = Resources.Load<Sprite>("Sprites/HitSplats/Damage_hitsplat_maxhit");
        }

        /// <summary>
        /// Attempt to attack the specified target. Returns false if not ready.
        /// </summary>
        public bool TryAttackTarget(CombatTarget target)
        {
            if (target == null || !target.IsAlive)
                return false;
            if (Vector2.Distance(transform.position, target.transform.position) > MagicUI.GetActiveSpellRange())
                return false;
            if (Time.time < nextAttackTime && attackRoutine == null)
                return false;
            if (attackRoutine != null)
            {
                if (currentTarget == target)
                    return false;
                StopCoroutine(attackRoutine);
            }
            var npcAttack = (target as MonoBehaviour)?.GetComponent<NpcAttackController>();
            if (npcAttack != null)
            {
                var playerTarget = GetComponent<PlayerCombatTarget>();
                npcAttack.BeginAttacking(playerTarget);
            }
            attackRoutine = StartCoroutine(AttackRoutine(target));
            if (PetDropSystem.GuardModeEnabled)
            {
                var pet = PetDropSystem.ActivePetCombat;
                pet?.CommandAttack(target);
            }
            return true;
        }

        /// <summary>
        /// Stops any ongoing attack routine and clears the current target.
        /// </summary>
        public void CancelCombat()
        {
            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
                attackRoutine = null;
            }
            if (currentTarget != null)
            {
                OnCombatTargetChanged?.Invoke(null);
                currentTarget = null;
            }
        }

        private IEnumerator AttackRoutine(CombatTarget target)
        {
            currentTarget = target;
            OnCombatTargetChanged?.Invoke(target);
            float delay = Mathf.Max(0f, nextAttackTime - Time.time);
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            while (target != null && target.IsAlive)
            {
                if (Vector2.Distance(transform.position, target.transform.position) > MagicUI.GetActiveSpellRange())
                    break;
                mover?.FaceTarget(target.transform);
                OnAttackStart?.Invoke();
                ResolveAttack(target);
                // If the target died from the attack, exit immediately so listeners are notified
                // without waiting for the next attack interval. This prevents lingering HUD elements
                // like the weapon sprite from staying visible after the enemy is dead.
                if (!target.IsAlive)
                    break;

                float interval = equipment != null ? equipment.GetCombinedStats().attackSpeedTicks * CombatMath.TICK_SECONDS : 4 * CombatMath.TICK_SECONDS;
                nextAttackTime = Time.time + interval;
                yield return new WaitForSeconds(interval);
            }
            OnCombatTargetChanged?.Invoke(null);
            currentTarget = null;
            attackRoutine = null;
        }

        private struct DamageResult
        {
            public int damage;
            public bool hit;
            public int maxHit;
        }

        private DamageResult CalculateDamage(CombatantStats attacker, CombatTarget target)
        {
            var defender = new CombatantStats
            {
                AttackLevel = 1,
                StrengthLevel = 1,
                DefenceLevel = 1,
                Equip = new EquipmentAggregator.CombinedStats(),
                Style = CombatStyle.Defensive,
                DamageType = target.PreferredDefenceType
            };

            int attEff = attacker.DamageType == DamageType.Magic
                ? CombatMath.GetEffectiveAttack(attacker.MagicLevel, CombatStyle.Accurate)
                : CombatMath.GetEffectiveAttack(attacker.AttackLevel, attacker.Style);
            int defEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            int atkRoll = attacker.DamageType == DamageType.Magic
                ? CombatMath.GetAttackRoll(attEff, attacker.Equip.magic)
                : CombatMath.GetAttackRoll(attEff, attacker.Equip.attack);
            int defBonus = defender.DamageType switch
            {
                DamageType.Magic => defender.Equip.magicDef,
                DamageType.Ranged => defender.Equip.rangeDef,
                _ => defender.Equip.meleeDef
            };
            int defRoll = CombatMath.GetDefenceRoll(defEff, defBonus);
            float chance = CombatMath.ChanceToHit(atkRoll, defRoll);
            bool hit = Random.value < chance;

            int maxHit;
            if (attacker.DamageType == DamageType.Magic)
                maxHit = MagicUI.ActiveSpellMaxHit + Mathf.FloorToInt(attacker.Equip.magic / 10f);
            else
            {
                int strEff = CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                maxHit = CombatMath.GetMaxHit(strEff, attacker.Equip.strength);
            }
            int damage = hit ? CombatMath.RollDamage(maxHit) : 0;
            return new DamageResult { damage = damage, hit = hit, maxHit = maxHit };
        }

        private void ApplyDamageResult(CombatTarget target, int damage, bool hit, int maxHit, CombatStyle style, DamageType type)
        {
            var targetMb = target as MonoBehaviour;
            string targetName = targetMb != null ? targetMb.name : "target";
            if (hit)
            {
                target.ApplyDamage(damage, type, this);
                var sprite = damage == maxHit ? maxHitHitsplat : damageHitsplat;
                FloatingText.Show(damage.ToString(), target.transform.position, Color.white, null, sprite);
                AwardXp(damage, style, type);
                if (!target.IsAlive)
                    OnTargetKilled?.Invoke(target);
                Debug.Log($"Player dealt {damage} damage to {targetName}.");
            }
            else
            {
                FloatingText.Show("0", target.transform.position, Color.white, null, zeroHitsplat);
                Debug.Log($"Player missed {targetName}.");
            }
            OnAttackLanded?.Invoke(damage, hit);
        }

        private void ResolveAttack(CombatTarget target)
        {
            CombatantStats attacker;
            if (combatBinder != null)
                attacker = combatBinder.GetCombatantStats();
            else if (loadout != null)
                attacker = loadout.GetCombatantStats();
            else
                attacker = CombatantStats.ForPlayer(skills, equipment, CombatStyle.Accurate, DamageType.Melee);

            var result = CalculateDamage(attacker, target);

            if (attacker.DamageType == DamageType.Magic && MagicUI.ActiveSpell != null && MagicUI.ActiveSpell.projectilePrefab != null)
            {
                pendingStyle = attacker.Style;
                pendingType = attacker.DamageType;
                pendingMaxHit = result.maxHit;
                var projObj = Instantiate(MagicUI.ActiveSpell.projectilePrefab, transform.position, Quaternion.identity);
                var proj = projObj.GetComponent<Magic.FireProjectile>();
                if (proj != null)
                {
                    proj.target = target;
                    proj.damage = result.damage;
                    proj.maxHit = result.maxHit;
                    proj.owner = this;
                    proj.style = attacker.Style;
                    proj.damageType = attacker.DamageType;
                    proj.speed = MagicUI.ActiveSpell.speed;
                    proj.hitFadeTime = MagicUI.ActiveSpell.hitFadeTime;
                    if (MagicUI.ActiveSpell.hitEffectPrefab != null)
                        proj.hitEffectPrefab = MagicUI.ActiveSpell.hitEffectPrefab;
                }
            }
            else
            {
                ApplyDamageResult(target, result.damage, result.hit, result.maxHit, attacker.Style, attacker.DamageType);
            }
        }

        public void ApplySpellDamage(CombatTarget target, int damage)
        {
            bool hit = damage > 0;
            ApplyDamageResult(target, damage, hit, pendingMaxHit, pendingStyle, pendingType);
        }

        private void AwardXp(int damage, CombatStyle style, DamageType type)
        {
            if (damage <= 0)
                return;
            hitpoints?.GainHitpointsXP(damage * 1.33f);
            if (type == DamageType.Magic)
            {
                skills?.AddXP(SkillType.Magic, 4 * damage);
                return;
            }
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
