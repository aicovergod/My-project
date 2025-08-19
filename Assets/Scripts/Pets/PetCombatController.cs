using System.Collections;
using UnityEngine;
using Combat;
using EquipmentSystem;
using NPC;

namespace Pets
{
    /// <summary>
    /// Handles combat behaviour for pets that can fight alongside the player.
    /// </summary>
    [RequireComponent(typeof(PetFollower))]
    public class PetCombatController : MonoBehaviour
    {
        public PetDefinition definition;
        public float moveSpeed = 5f;

        private PetFollower follower;
        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private Sprite defaultSprite;
        private Coroutine spriteSwapRoutine;
        private CombatTarget currentTarget;
        private Coroutine attackRoutine;

        private void Awake()
        {
            follower = GetComponent<PetFollower>();
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null)
                defaultSprite = spriteRenderer.sprite;
            if (TryGetComponent<Collider>(out var col))
                col.isTrigger = true;
            if (TryGetComponent<Collider2D>(out var col2d))
                col2d.isTrigger = true;
        }

        /// <summary>Returns true if this pet has combat capabilities.</summary>
        public bool CanFight => definition != null && definition.canFight;

        /// <summary>Order the pet to attack the given combat target.</summary>
        public void CommandAttack(CombatTarget target)
        {
            if (!CanFight || target == null || !target.IsAlive)
                return;

            currentTarget = target;
            if (attackRoutine != null)
                StopCoroutine(attackRoutine);
            attackRoutine = StartCoroutine(AttackRoutine());
        }

        private IEnumerator AttackRoutine()
        {
            follower.enabled = false;
            while (currentTarget != null && currentTarget.IsAlive)
            {
                Vector3 pos = transform.position;
                Vector3 targetPos = currentTarget.transform.position;
                transform.position = Vector3.MoveTowards(pos, targetPos, moveSpeed * Time.deltaTime);
                float dist = Vector2.Distance(transform.position, currentTarget.transform.position);
                if (dist <= CombatMath.MELEE_RANGE)
                {
                    ResolveAttack(currentTarget);
                    yield return new WaitForSeconds(definition.attackSpeedTicks * CombatMath.TICK_SECONDS);
                }
                else if (dist > CombatMath.MELEE_RANGE * 5f)
                {
                    break;
                }
                else
                {
                    yield return null;
                }
            }
            follower.enabled = true;
            attackRoutine = null;
            currentTarget = null;
        }

        private void ResolveAttack(CombatTarget target)
        {
            var attacker = new CombatantStats
            {
                AttackLevel = definition.petAttackLevel,
                StrengthLevel = definition.petStrengthLevel,
                DefenceLevel = 1,
                Equip = new EquipmentAggregator.CombinedStats
                {
                    attack = definition.accuracyBonus,
                    strength = definition.damageBonus,
                    attackSpeedTicks = definition.attackSpeedTicks
                },
                Style = CombatStyle.Accurate,
                DamageType = DamageType.Melee
            };

            CombatantStats defender;
            if (target is NpcCombatant npc)
                defender = npc.GetCombatantStats();
            else
                defender = new CombatantStats
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
            if (animator != null)
                animator.SetTrigger("Attack");
            else if (spriteRenderer != null && definition != null && definition.attackSprite != null)
            {
                if (spriteSwapRoutine != null)
                    StopCoroutine(spriteSwapRoutine);
                spriteSwapRoutine = StartCoroutine(AttackSpriteSwap());
            }
            if (hit)
            {
                int strEff = CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                int maxHit = CombatMath.GetMaxHit(strEff, attacker.Equip.strength);
                int dmg = CombatMath.RollDamage(maxHit);
                target.ApplyDamage(dmg, attacker.DamageType, this);
            }
        }

        private void OnDisable()
        {
            if (attackRoutine != null)
                StopCoroutine(attackRoutine);
            if (spriteSwapRoutine != null)
                StopCoroutine(spriteSwapRoutine);
            currentTarget = null;
        }

        private IEnumerator AttackSpriteSwap()
        {
            spriteRenderer.sprite = definition.attackSprite;
            yield return new WaitForSeconds(0.2f);
            spriteRenderer.sprite = defaultSprite;
            spriteSwapRoutine = null;
        }
    }
}
