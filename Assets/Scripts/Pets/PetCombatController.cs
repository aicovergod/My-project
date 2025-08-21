using System.Collections;
using UnityEngine;
using Combat;
using EquipmentSystem;
using NPC;
using Skills;

namespace Pets
{
    /// <summary>
    /// Handles combat behaviour for pets that can fight alongside the player.
    /// </summary>
    [RequireComponent(typeof(PetFollower))]
    public class PetCombatController : MonoBehaviour, CombatTarget
    {
        public PetDefinition definition;
        public float moveSpeed = 5f;

        private PetFollower follower;
        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private PetSpriteAnimator spriteAnimator;
        private Sprite defaultSprite;
        private Coroutine spriteSwapRoutine;
        private CombatTarget currentTarget;
        private Coroutine attackRoutine;

        public bool IsAlive => true;
        public DamageType PreferredDefenceType => DamageType.Melee;
        public int CurrentHP => 1;
        public int MaxHP => 1;
        public void ApplyDamage(int amount, DamageType type, object source) { }

        private void Awake()
        {
            follower = GetComponent<PetFollower>();
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
            spriteAnimator = GetComponent<PetSpriteAnimator>();
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

            // Prevent restarting the attack routine when already attacking the same target.
            if (currentTarget == target && attackRoutine != null)
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
                Vector3 newPos = Vector3.MoveTowards(pos, targetPos, moveSpeed * Time.deltaTime);
                Vector2 velocity = (newPos - pos) / Time.deltaTime;
                transform.position = newPos;

                if (spriteAnimator != null)
                    spriteAnimator.UpdateVisuals(velocity);
                else if (spriteRenderer != null)
                    spriteRenderer.flipX = velocity.x > 0f;

                float dist = Vector2.Distance(transform.position, currentTarget.transform.position);
                if (dist <= CombatMath.MELEE_RANGE)
                {
                    ResolveAttack(currentTarget);
                    int waitTicks = definition.attackSpeedTicks;
                    if (currentTarget == null || !currentTarget.IsAlive)
                        waitTicks = Mathf.Min(waitTicks, 2);
                    yield return new WaitForSeconds(waitTicks * CombatMath.TICK_SECONDS);
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
            if (spriteAnimator != null)
                spriteAnimator.UpdateVisuals(Vector2.zero);
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
            var exp = GetComponent<PetExperience>();
            float statMult = exp != null ? PetExperience.GetStatMultiplier(exp.Level) : 1f;
            attacker.AttackLevel = Mathf.RoundToInt(attacker.AttackLevel * statMult);
            attacker.StrengthLevel = Mathf.RoundToInt(attacker.StrengthLevel * statMult);
            attacker.Equip.attack = Mathf.RoundToInt(attacker.Equip.attack * statMult);
            attacker.Equip.strength = Mathf.RoundToInt(attacker.Equip.strength * statMult);

            // scale stats based on the owner's Beastmaster level
            var owner = follower != null ? follower.Player : null;
            int beastmasterLevel = 1;
            if (owner != null && owner.TryGetComponent<SkillManager>(out var skills))
                beastmasterLevel = skills.GetLevel(SkillType.Beastmaster);

            if (definition != null)
            {
                if (definition.attackLevelPerBeastmasterLevel != 0f)
                    attacker.AttackLevel = Mathf.RoundToInt(attacker.AttackLevel * (1f + definition.attackLevelPerBeastmasterLevel * beastmasterLevel));
                if (definition.strengthLevelPerBeastmasterLevel != 0f)
                    attacker.StrengthLevel = Mathf.RoundToInt(attacker.StrengthLevel * (1f + definition.strengthLevelPerBeastmasterLevel * beastmasterLevel));
            }

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

            int facingDir = 0;
            Vector2 diff = target.transform.position - transform.position;
            if (Mathf.Abs(diff.x) > Mathf.Abs(diff.y))
                facingDir = diff.x < 0f ? 1 : 2;
            else
                facingDir = diff.y < 0f ? 0 : 3;

            if (spriteAnimator != null)
                spriteAnimator.SetFacing(facingDir);
            else if (spriteRenderer != null)
                spriteRenderer.flipX = facingDir == 2;

            if (animator != null)
                animator.SetTrigger("Attack");
            else if (spriteAnimator != null && spriteAnimator.HasHitAnimation(facingDir))
            {
                if (spriteSwapRoutine != null)
                    StopCoroutine(spriteSwapRoutine);
                spriteSwapRoutine = StartCoroutine(spriteAnimator.PlayHitAnimation(facingDir));
            }
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
                if (definition != null && definition.maxHitPerBeastmasterLevel != 0f)
                    maxHit = Mathf.RoundToInt(maxHit * (1f + definition.maxHitPerBeastmasterLevel * beastmasterLevel));
                int dmg = CombatMath.RollDamage(maxHit);
                target.ApplyDamage(dmg, attacker.DamageType, this);
                if (target is NpcCombatant npc)
                {
                    var npcAttack = npc.GetComponent<NpcAttackController>();
                    npcAttack?.BeginAttacking(this);
                }
                BeastmasterXp.TryGrantFromPetDamage(owner != null ? owner.gameObject : null, dmg);
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
