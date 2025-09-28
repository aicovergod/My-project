using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Combat;
using EquipmentSystem;
using NPC;
using Skills;
using UI;
using Player;

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

        [SerializeField, Tooltip("Centralised hitsplat sprite references assigned via the inspector.")]
        private HitSplatLibrary hitSplatLibrary;

        [SerializeField, Tooltip("Vertical offset for hitsplats when no floating text anchor exists.")]
        private float hitsplatFallbackOffset = 1f;

        /// <summary>
        /// Shared cache so pets reuse a single hitsplat library loaded from the Resources
        /// folder whenever no explicit reference has been assigned in the inspector.
        /// </summary>
        private static HitSplatLibrary sharedHitSplatLibrary;

        private PetFollower follower;
        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private PetSpriteAnimator spriteAnimator;
        private Sprite defaultSprite;
        private Coroutine spriteSwapRoutine;
        private CombatTarget currentTarget;
        private Coroutine attackRoutine;
        private float nextAttackTime;

        private Sprite damageHitsplat;
        private Sprite zeroHitsplat;
        private Sprite maxHitHitsplat;
        private readonly Dictionary<Transform, FloatingTextAnchorUtility.AnchorCache> hitsplatAnchorCache = new Dictionary<Transform, FloatingTextAnchorUtility.AnchorCache>();

        public bool IsAlive => true;
        public DamageType PreferredDefenceType => DamageType.Melee;
        public int CurrentHP => 1;
        public int MaxHP => 1;
        public int ApplyDamage(int amount, DamageType type, SpellElement element, object source) { return 0; }

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

            EnsureHitSplatLibrary();

            if (hitSplatLibrary == null)
            {
                Debug.LogError("PetCombatController requires a HitSplatLibrary reference. Assign one in the inspector.", this);
            }
            else
            {
                damageHitsplat = hitSplatLibrary.DamageHitsplat;
                zeroHitsplat = hitSplatLibrary.ZeroDamageHitsplat;
                maxHitHitsplat = hitSplatLibrary.MaxHitHitsplat;
            }
        }

        /// <summary>Returns true if this pet has combat capabilities.</summary>
        public bool CanFight => definition != null && definition.canFight;

        /// <summary>Order the pet to attack the given combat target.</summary>
        public void CommandAttack(CombatTarget target)
        {
            if (!CanFight || target == null || !target.IsAlive)
            {
                CancelAttack();
                return;
            }

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
                    if (Time.time < nextAttackTime)
                    {
                        // Continue chasing the target but hold attacks until the shared cooldown expires.
                        yield return null;
                        continue;
                    }

                    ResolveAttack(currentTarget);
                    nextAttackTime = Time.time + definition.attackSpeedTicks * CombatMath.TICK_SECONDS;
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
            CancelAttackInternal(false);
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

            var npc = target as NPCCombatHandler;
            CombatantStats defender;
            if (npc != null)
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
                object source = this;
                if (owner != null && owner.TryGetComponent<PlayerCombatTarget>(out var ownerTarget))
                    source = ownerTarget;
                int finalDamage = target.ApplyDamage(dmg, attacker.DamageType, SpellElement.None, source);
                var sprite = finalDamage == maxHit ? maxHitHitsplat : damageHitsplat;
                Vector3 hitsplatPosition = FloatingTextAnchorUtility.ResolveAnchorPosition(target.transform, hitsplatFallbackOffset, hitsplatAnchorCache);
                FloatingText.Show(finalDamage.ToString(), hitsplatPosition, Color.white, null, sprite);
                if (npc != null)
                {
                    var npcAttack = npc.GetComponent<NPCAttackController>();
                    npcAttack?.BeginAttacking(this);
                }
                BeastmasterXp.TryGrantFromPetDamage(owner != null ? owner.gameObject : null, finalDamage);
            }
            else
            {
                Vector3 hitsplatPosition = FloatingTextAnchorUtility.ResolveAnchorPosition(target.transform, hitsplatFallbackOffset, hitsplatAnchorCache);
                FloatingText.Show("0", hitsplatPosition, Color.white, null, zeroHitsplat);
            }
        }

        /// <summary>
        /// Ensures a shared hitsplat library is available, falling back to the
        /// Resources asset when no explicit assignment exists in the inspector.
        /// </summary>
        private void EnsureHitSplatLibrary()
        {
            if (hitSplatLibrary != null)
                return;

            if (sharedHitSplatLibrary == null)
                sharedHitSplatLibrary = Resources.Load<HitSplatLibrary>("HitSplatLibrary");

            if (sharedHitSplatLibrary != null)
                hitSplatLibrary = sharedHitSplatLibrary;
        }

        private void OnDisable()
        {
            CancelAttack();
        }

        private IEnumerator AttackSpriteSwap()
        {
            spriteRenderer.sprite = definition.attackSprite;
            yield return new WaitForSeconds(0.2f);
            spriteRenderer.sprite = defaultSprite;
            spriteSwapRoutine = null;
        }

        /// <summary>
        /// Stops any active attack behaviour and restores the follower state so pets resume
        /// trailing their owner immediately after being hidden or disabled.
        /// </summary>
        private void CancelAttack()
        {
            CancelAttackInternal(true);
        }

        /// <summary>
        /// Shared cancellation logic used by both external callers and the attack coroutine
        /// itself. When <paramref name="stopCoroutine"/> is true the active attack coroutine
        /// is halted, otherwise the caller is responsible for exiting the routine gracefully.
        /// </summary>
        private void CancelAttackInternal(bool stopCoroutine)
        {
            if (stopCoroutine && attackRoutine != null)
                StopCoroutine(attackRoutine);

            attackRoutine = null;

            if (spriteSwapRoutine != null)
            {
                StopCoroutine(spriteSwapRoutine);
                spriteSwapRoutine = null;
            }

            currentTarget = null;

            if (spriteAnimator != null)
                spriteAnimator.UpdateVisuals(Vector2.zero);

            if (spriteRenderer != null && defaultSprite != null)
                spriteRenderer.sprite = defaultSprite;

            if (follower != null)
                follower.enabled = true;
        }
    }
}
