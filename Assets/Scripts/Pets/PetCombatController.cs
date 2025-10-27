using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Combat;
using Combat.Ranged;
using EquipmentSystem;
using NPC;
using Skills;
using Skills.Common;
using Skills.Mining;
using UI;
using Player;
using Util;
using Companions;
using Companions.Combat;
using Companions.Equipment;
using Inventory;
using Inventory.Core;
using UI.Chat;

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

        [SerializeField, Tooltip("Controller that maintains the floating text anchor for this pet.")]
        private PetFloatingTextController floatingTextController;


        private PetFollower follower;
        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private PetSpriteAnimator spriteAnimator;
        private Sprite defaultSprite;
        private Coroutine spriteSwapRoutine;
        private PetPathMover pathMover;
        private Rigidbody2D petRigidbody;
        private bool hasRigidbody2D;
        private CombatTarget currentTarget;
        private Coroutine attackRoutine;
        private Coroutine pendingOreGolemHarvestRoutine;
        private float nextAttackTime;
        private CompanionController companionController;
        private CompanionEquipment subscribedEquipment;
        private CompanionCombatBridge companionCombatBridge;
        private CompanionRangedCombatController rangedCombatController;

        private const float TileSize = 1f;
        private const float HalberdExtraRangeTiles = 1f;
        private const float DefaultRangedRangeTiles = 5f;
        private const string RangedWeaponResourcePath = "Combat/Ranged/Weapons";
        private const string AmmunitionResourcePath = "Combat/Ranged/Ammunition";
        private const string OreGolemPickaxeReminderThrottleKey = "OreGolemPickaxeReminder";

        private static readonly Dictionary<string, RangedWeaponData> RangedWeaponCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AmmunitionData> AmmunitionCache = new(StringComparer.OrdinalIgnoreCase);
        private static bool rangedCachesLoaded;

        /// <summary>
        /// Tracks the most recent non-zero chase velocity so the sprite animator can
        /// continue playing a directional idle when the pet is in melee range but
        /// waiting for its cooldown to expire.
        /// </summary>
        private Vector2 lastNonZeroChaseVelocity;

        /// <summary>
        /// Flag indicating whether <see cref="lastNonZeroChaseVelocity"/> has been
        /// populated. This prevents accidental use of the default zero vector before
        /// the pet has actually moved.
        /// </summary>
        private bool hasLastNonZeroChaseVelocity;

        /// <summary>
        /// Cached target position from the previous frame so we can infer the
        /// opponent's movement while the pet is idling in range.
        /// </summary>
        private Vector3 previousTargetPosition;

        /// <summary>
        /// Indicates whether <see cref="previousTargetPosition"/> currently stores a
        /// valid reading. Reset whenever combat is cancelled so future engagements
        /// start fresh.
        /// </summary>
        private bool hasPreviousTargetPosition;

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
            pathMover = GetComponent<PetPathMover>();
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
            spriteAnimator = GetComponent<PetSpriteAnimator>();
            if (spriteRenderer != null)
                defaultSprite = spriteRenderer.sprite;
            if (TryGetComponent<Collider>(out var col))
                col.isTrigger = true;
            if (TryGetComponent<Collider2D>(out var col2d))
                col2d.isTrigger = true;
            if (TryGetComponent(out petRigidbody))
            {
                hasRigidbody2D = true;
            }

            companionController = GetComponent<CompanionController>();
            companionCombatBridge = GetComponent<CompanionCombatBridge>();
            rangedCombatController = GetComponent<CompanionRangedCombatController>();

            hitSplatLibrary = HitSplatLibraryResolver.Resolve(hitSplatLibrary);

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

            if (floatingTextController == null)
                floatingTextController = GetComponent<PetFloatingTextController>() ?? GetComponentInChildren<PetFloatingTextController>();
        }

        /// <summary>Returns true if this pet has combat capabilities.</summary>
        public bool CanFight => definition != null && definition.canFight;

        /// <summary>
        /// True when the combat controller currently has an active, living target assigned.
        /// Used by the companion manager to expose stop commands while engaged in combat.
        /// </summary>
        public bool HasActiveTarget => currentTarget != null && currentTarget.IsAlive;

        /// <summary>Order the pet to attack the given combat target.</summary>
        public void CommandAttack(CombatTarget target, bool fromDirectCommand = false)
        {
            if (!CanFight || target == null || !target.IsAlive)
            {
                CancelAttack();
                return;
            }

            // Ensure the companion controller reference is refreshed before validating ore golem rules.
            companionController ??= GetComponent<CompanionController>();
            TrySubscribeToEquipmentChanges();

            if (ShouldBlockOreGolemAttack(target, fromDirectCommand))
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
            pathMover?.ResetAttackTracking();
            hasLastNonZeroChaseVelocity = false;
            hasPreviousTargetPosition = false;
            while (currentTarget != null && currentTarget.IsAlive)
            {
                float navDeltaTime = hasRigidbody2D
                    ? Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon)
                    : Mathf.Max(Time.deltaTime, Mathf.Epsilon);
                Vector3 startingPosition = transform.position;
                Vector3 currentTargetPosition = currentTarget.transform.position;
                Vector3 targetDelta = hasPreviousTargetPosition ? currentTargetPosition - previousTargetPosition : Vector3.zero;
                previousTargetPosition = currentTargetPosition;
                hasPreviousTargetPosition = true;

                var attackerStats = BuildAttackerStats(out var ownerTransform, out int beastmasterLevel, out ItemData equippedWeapon);
                int attackSpeedTicks = Mathf.Max(1, attackerStats.Equip.attackSpeedTicks);
                float effectiveRange = ResolveEffectiveRange(attackerStats, equippedWeapon);
                float disengageDistance = Mathf.Max(CombatMath.MELEE_RANGE * 5f, effectiveRange * 5f);
                float stopDistance = Mathf.Clamp(effectiveRange - CombatMath.MELEE_RANGE * 0.25f, CombatMath.MELEE_RANGE * 0.5f, effectiveRange);
                float replanDistance = Mathf.Max(CombatMath.MELEE_RANGE * 0.75f, effectiveRange * 0.75f);
                float teleportDistance = Mathf.Max(CombatMath.MELEE_RANGE * 6f, effectiveRange * 6f);

                Vector2 movementVelocity = Vector2.zero;
                bool hasChaseVelocity = false;
                Vector2 visualVelocity;
                bool navigationStepTaken = false;
                bool goalUnreachable;

                if (pathMover != null && pathMover.isActiveAndEnabled)
                {
                    bool teleported;
                    Vector2 nextPosition;
                    Vector2 navVelocity;
                    navigationStepTaken = pathMover.TryStepAttack(
                        navDeltaTime,
                        moveSpeed,
                        stopDistance,
                        0.1f,
                        ResolveAttackTargetPosition,
                        replanDistance,
                        teleportDistance,
                        out nextPosition,
                        out navVelocity,
                        out teleported,
                        out goalUnreachable);

                    if (goalUnreachable)
                    {
                        ApplyVisualVelocity(Vector2.zero);
                        break;
                    }

                    if (navigationStepTaken)
                    {
                        if (hasRigidbody2D)
                        {
                            if (teleported)
                            {
                                petRigidbody.position = nextPosition;
                                petRigidbody.linearVelocity = Vector2.zero;
                            }
                            else
                            {
                                petRigidbody.MovePosition(nextPosition);
                                petRigidbody.linearVelocity = navVelocity;
                            }
                        }
                        else
                        {
                            transform.position = nextPosition;
                        }

                        movementVelocity = navVelocity;
                        hasChaseVelocity = movementVelocity.sqrMagnitude > 0.0001f;
                    }
                }
                else
                {
                    goalUnreachable = false;
                }

                bool navigationUnavailable = pathMover == null || !pathMover.isActiveAndEnabled || !pathMover.HasActiveNavigationGrid;

                if (!navigationStepTaken && navigationUnavailable)
                {
                    Vector3 fallbackTargetPosition = currentTarget.transform.position;
                    Vector3 newPos = Vector3.MoveTowards(startingPosition, fallbackTargetPosition, moveSpeed * navDeltaTime);
                    movementVelocity = (newPos - startingPosition) / navDeltaTime;

                    if (hasRigidbody2D)
                    {
                        petRigidbody.MovePosition(newPos);
                        petRigidbody.linearVelocity = movementVelocity;
                    }
                    else
                    {
                        transform.position = newPos;
                    }

                    hasChaseVelocity = movementVelocity.sqrMagnitude > 0.0001f;
                }

                if (!navigationStepTaken && !navigationUnavailable)
                {
                    movementVelocity = Vector2.zero;
                    if (hasRigidbody2D)
                    {
                        petRigidbody.linearVelocity = Vector2.zero;
                    }
                }

                if (hasChaseVelocity)
                {
                    lastNonZeroChaseVelocity = movementVelocity;
                    hasLastNonZeroChaseVelocity = true;
                }

                visualVelocity = movementVelocity;
                bool targetMovedWhileWaiting = targetDelta.sqrMagnitude > 0.0001f;
                Vector2 targetMovementVelocity = targetMovedWhileWaiting
                    ? (Vector2)(targetDelta / navDeltaTime)
                    : Vector2.zero;

                float dist = Vector2.Distance(transform.position, currentTarget.transform.position);
                if (dist <= effectiveRange)
                {
                    if (Time.time < nextAttackTime)
                    {
                        if (!hasChaseVelocity)
                        {
                            if (targetMovedWhileWaiting)
                                visualVelocity = targetMovementVelocity;
                            else if (hasLastNonZeroChaseVelocity)
                                visualVelocity = lastNonZeroChaseVelocity;
                        }

                        ApplyVisualVelocity(visualVelocity);
                        // Continue chasing the target but hold attacks until the shared cooldown expires.
                        yield return null;
                        continue;
                    }

                    ApplyVisualVelocity(visualVelocity);
                    int resolvedTicks = ResolveAttack(currentTarget, attackerStats, ownerTransform, beastmasterLevel);
                    if (resolvedTicks > 0)
                        attackSpeedTicks = resolvedTicks;

                    // Reset any residual velocity so the pet remains parked next to the target while waiting for the next swing.
                    movementVelocity = Vector2.zero;
                    visualVelocity = Vector2.zero;

                    if (hasRigidbody2D)
                        petRigidbody.linearVelocity = Vector2.zero;

                    ApplyVisualVelocity(visualVelocity);
                    float attackInterval = attackSpeedTicks * CombatMath.TICK_SECONDS;
                    nextAttackTime = Time.time + attackInterval;
                    int waitTicks = attackSpeedTicks;
                    if (currentTarget == null || !currentTarget.IsAlive)
                        waitTicks = Mathf.Min(waitTicks, 2);
                    waitTicks = Mathf.Max(1, waitTicks);
                    yield return new WaitForSeconds(waitTicks * CombatMath.TICK_SECONDS);
                }
                else if (dist > disengageDistance)
                {
                    ApplyVisualVelocity(visualVelocity);
                    break;
                }
                else
                {
                    ApplyVisualVelocity(visualVelocity);
                    yield return null;
                }
            }
            CancelAttackInternal(false);
        }

        private CombatantStats BuildAttackerStats(out Transform ownerTransform, out int beastmasterLevel, out ItemData equippedWeapon)
        {
            int baseAttack = definition != null ? Mathf.Max(1, definition.petAttackLevel) : 1;
            int baseStrength = definition != null ? Mathf.Max(1, definition.petStrengthLevel) : 1;
            int baseAccuracy = definition != null ? definition.accuracyBonus : 0;
            int baseDamage = definition != null ? definition.damageBonus : 0;
            int baseSpeed = definition != null ? definition.attackSpeedTicks : 4;

            var attacker = new CombatantStats
            {
                AttackLevel = baseAttack,
                StrengthLevel = baseStrength,
                DefenceLevel = 1,
                RangedLevel = 1,
                MagicLevel = 1,
                Equip = new EquipmentAggregator.CombinedStats
                {
                    attack = baseAccuracy,
                    strength = baseDamage,
                    rangeStrength = baseDamage,
                    attackSpeedTicks = baseSpeed
                },
                Style = CombatStyle.Accurate,
                DamageType = DamageType.Melee
            };

            companionCombatBridge ??= GetComponent<CompanionCombatBridge>();
            ownerTransform = follower != null ? follower.Player : null;
            beastmasterLevel = ResolveBeastmasterLevel(ownerTransform);
            equippedWeapon = ResolveEquippedWeapon();

            bool statsOverridden = companionCombatBridge != null && companionCombatBridge.TryOverrideStats(ref attacker);

            if (!statsOverridden)
            {
                var exp = GetComponent<PetExperience>();
                float statMult = exp != null ? PetExperience.GetStatMultiplier(exp.Level) : 1f;
                attacker.AttackLevel = Mathf.RoundToInt(attacker.AttackLevel * statMult);
                attacker.StrengthLevel = Mathf.RoundToInt(attacker.StrengthLevel * statMult);
                attacker.Equip.attack = Mathf.RoundToInt(attacker.Equip.attack * statMult);
                attacker.Equip.strength = Mathf.RoundToInt(attacker.Equip.strength * statMult);
                attacker.Equip.rangeStrength = Mathf.RoundToInt(attacker.Equip.rangeStrength * statMult);

                if (definition != null)
                {
                    if (definition.attackLevelPerBeastmasterLevel != 0f)
                        attacker.AttackLevel = Mathf.RoundToInt(attacker.AttackLevel * (1f + definition.attackLevelPerBeastmasterLevel * beastmasterLevel));
                    if (definition.strengthLevelPerBeastmasterLevel != 0f)
                        attacker.StrengthLevel = Mathf.RoundToInt(attacker.StrengthLevel * (1f + definition.strengthLevelPerBeastmasterLevel * beastmasterLevel));
                }
            }

            attacker.Equip.attackSpeedTicks = Mathf.Max(1, attacker.Equip.attackSpeedTicks);
            return attacker;
        }

        private float ResolveEffectiveRange(CombatantStats attacker, ItemData equippedWeapon)
        {
            if (attacker == null)
                return CombatMath.MELEE_RANGE;

            return attacker.DamageType switch
            {
                DamageType.Ranged => ResolveCompanionRangedRange(attacker.Style, equippedWeapon),
                DamageType.Magic => ResolveCompanionMagicRange(),
                _ => ResolveCompanionMeleeRange(equippedWeapon)
            };
        }

        private float ResolveCompanionMeleeRange(ItemData equippedWeapon)
        {
            float range = CombatMath.MELEE_RANGE;
            if (equippedWeapon != null && equippedWeapon.isHalberd)
                range = Mathf.Max(range, CombatMath.MELEE_RANGE + HalberdExtraRangeTiles * TileSize);
            return range;
        }

        private float ResolveCompanionMagicRange()
        {
            // Companions currently do not cast spells, so staffs default to melee reach until spell support arrives.
            return CombatMath.MELEE_RANGE;
        }

        private float ResolveCompanionRangedRange(CombatStyle style, ItemData equippedWeapon)
        {
            EnsureRangedCaches();

            float baseRangeTiles = DefaultRangedRangeTiles;
            float rangeBonusTiles = 0f;

            if (equippedWeapon != null && TryGetRangedWeaponData(equippedWeapon, out var weaponData))
            {
                baseRangeTiles = Mathf.Max(baseRangeTiles, weaponData.baseRangeTiles);
                rangeBonusTiles += weaponData.rangeBonusTiles;

                if (weaponData.consumesWeaponStack && TryGetAmmunitionData(equippedWeapon, out var thrownAmmo))
                    rangeBonusTiles += thrownAmmo.rangeBonusTiles;
            }

            var ammoItem = ResolveEquippedAmmo();
            if (ammoItem != null && TryGetAmmunitionData(ammoItem, out var ammoData))
                rangeBonusTiles += ammoData.rangeBonusTiles;

            if (style == CombatStyle.Longrange)
                rangeBonusTiles += 2f;

            float resolvedTiles = Mathf.Max(0.1f, baseRangeTiles + rangeBonusTiles);
            return Mathf.Max(CombatMath.MELEE_RANGE, resolvedTiles * TileSize);
        }

        private ItemData ResolveEquippedWeapon()
        {
            companionController ??= GetComponent<CompanionController>();
            var equipment = companionController != null ? companionController.Equipment : null;
            if (equipment == null)
                return null;

            var entry = equipment.GetEquipped(EquipmentSlot.Weapon);
            return entry.item;
        }

        private ItemData ResolveEquippedAmmo()
        {
            companionController ??= GetComponent<CompanionController>();
            var equipment = companionController != null ? companionController.Equipment : null;
            if (equipment == null)
                return null;

            var entry = equipment.GetEquipped(EquipmentSlot.Arrow);
            return entry.item;
        }

        private static void EnsureRangedCaches()
        {
            if (rangedCachesLoaded)
                return;

            rangedCachesLoaded = true;
            CacheRangedWeapons(Resources.LoadAll<RangedWeaponData>(RangedWeaponResourcePath));
            CacheAmmunition(Resources.LoadAll<AmmunitionData>(AmmunitionResourcePath));
        }

        private static void CacheRangedWeapons(IEnumerable<RangedWeaponData> definitions)
        {
            if (definitions == null)
                return;

            foreach (var def in definitions)
            {
                if (def == null)
                    continue;

                string id = def.WeaponId;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                RangedWeaponCache[id] = def;
            }
        }

        private static void CacheAmmunition(IEnumerable<AmmunitionData> definitions)
        {
            if (definitions == null)
                return;

            foreach (var def in definitions)
            {
                if (def == null)
                    continue;

                string id = def.AmmoId;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                AmmunitionCache[id] = def;
            }
        }

        private static bool TryGetRangedWeaponData(ItemData weapon, out RangedWeaponData data)
        {
            data = null;
            if (weapon == null)
                return false;

            if (!string.IsNullOrWhiteSpace(weapon.id) && RangedWeaponCache.TryGetValue(weapon.id, out data))
                return true;

            return RangedWeaponCache.TryGetValue(weapon.name, out data);
        }

        private static bool TryGetAmmunitionData(ItemData ammo, out AmmunitionData data)
        {
            data = null;
            if (ammo == null)
                return false;

            if (!string.IsNullOrWhiteSpace(ammo.id) && AmmunitionCache.TryGetValue(ammo.id, out data))
                return true;

            return AmmunitionCache.TryGetValue(ammo.name, out data);
        }

        /// <summary>
        /// Lazily resolves the ranged combat controller so late-added components are
        /// available when ranged attacks attempt to execute.
        /// </summary>
        private void EnsureRangedController()
        {
            rangedCombatController ??= GetComponent<CompanionRangedCombatController>();
        }

        private int ResolveAttack(CombatTarget target, CombatantStats attacker, Transform ownerTransform, int beastmasterLevel)
        {
            EnsureRangedController();

            if (target == null)
                return Mathf.Max(1, attacker.Equip.attackSpeedTicks);

            var npc = target as NpcCombatant;
            CombatantStats defender;
            if (npc != null)
                defender = npc.GetCombatantStats();
            else
                defender = new CombatantStats
                {
                    AttackLevel = 1,
                    StrengthLevel = 1,
                    DefenceLevel = 1,
                    Equip = new EquipmentAggregator.CombinedStats { rangeStrength = 0 },
                    Style = CombatStyle.Defensive,
                    DamageType = target.PreferredDefenceType
                };

            Vector2 diff = target.transform.position - transform.position;
            Direction8 facingDir = Direction8Utility.FromVector(diff, allowDiagonals: true, fallback: Direction8.Down);
            if (spriteAnimator != null)
                spriteAnimator.SetFacing(facingDir);
            else if (spriteRenderer != null)
                spriteRenderer.flipX = Direction8Utility.IsFacingRight(facingDir);

            if (animator != null)
            {
                animator.SetInteger("Dir", Direction8Utility.ToAnimatorIndex8(facingDir));
                animator.SetTrigger("Attack");
            }
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

            if (attacker.DamageType == DamageType.Ranged && rangedCombatController != null)
            {
                if (rangedCombatController.TryResolveAttack(attacker, target, ownerTransform, beastmasterLevel, out int cooldownTicks))
                    return Mathf.Max(1, cooldownTicks);

                return Mathf.Max(1, attacker.Equip.attackSpeedTicks);
            }

            int attEff = attacker.DamageType switch
            {
                DamageType.Magic => CombatMath.GetEffectiveAttack(attacker.MagicLevel, CombatStyle.Accurate),
                DamageType.Ranged => CombatMath.GetEffectiveRanged(attacker.RangedLevel, attacker.Style),
                _ => CombatMath.GetEffectiveAttack(attacker.AttackLevel, attacker.Style)
            };
            int defEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);

            int atkBonus = attacker.DamageType switch
            {
                DamageType.Magic => attacker.Equip.magic,
                DamageType.Ranged => attacker.Equip.range,
                _ => attacker.Equip.attack
            };
            int atkRoll = CombatMath.GetAttackRoll(attEff, atkBonus);

            int defBonus = attacker.DamageType switch
            {
                DamageType.Magic => defender.Equip.magicDef,
                DamageType.Ranged => defender.Equip.rangeDef,
                _ => defender.Equip.meleeDef
            };
            int defRoll = CombatMath.GetDefenceRoll(defEff, defBonus);
            float chance = CombatMath.ChanceToHit(atkRoll, defRoll);
            bool hit = UnityEngine.Random.value < chance;

            companionCombatBridge ??= GetComponent<CompanionCombatBridge>();

            int maxHit;
            if (hit)
            {
                if (attacker.DamageType == DamageType.Magic)
                {
                    maxHit = Mathf.Max(0, Mathf.FloorToInt(attacker.MagicLevel * 0.2f + attacker.Equip.magic * 0.1f));
                }
                else
                {
                    int strEff = attacker.DamageType == DamageType.Ranged
                        ? CombatMath.GetEffectiveRangedStrength(attacker.RangedLevel, attacker.Style)
                        : CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                    int strengthBonus = attacker.DamageType == DamageType.Ranged
                        ? Mathf.Max(0, attacker.Equip.rangeStrength)
                        : attacker.Equip.strength;
                    maxHit = CombatMath.GetMaxHit(strEff, strengthBonus);
                }
                if (definition != null && definition.maxHitPerBeastmasterLevel != 0f)
                    maxHit = Mathf.RoundToInt(maxHit * (1f + definition.maxHitPerBeastmasterLevel * beastmasterLevel));
                int dmg = CombatMath.RollDamage(maxHit);
                var damageResult = new CombatController.DamageResult
                {
                    damage = dmg,
                    hit = true,
                    maxHit = maxHit
                };
                ApplyDamageResult(target, attacker, damageResult, ownerTransform, beastmasterLevel);
            }
            else
            {
                var damageResult = new CombatController.DamageResult
                {
                    damage = 0,
                    hit = false,
                    maxHit = 0
                };
                ApplyDamageResult(target, attacker, damageResult, ownerTransform, beastmasterLevel);
            }
            return Mathf.Max(1, attacker.Equip.attackSpeedTicks);
        }

        /// <summary>
        /// Applies the supplied damage result to the current target, resolving hitsplats, XP, and follow-up hooks.
        /// </summary>
        public void ApplyDamageResult(
            CombatTarget target,
            CombatantStats attacker,
            CombatController.DamageResult result,
            Transform ownerTransform,
            int beastmasterLevel)
        {
            if (target == null)
                return;

            bool hit = result.hit;
            int damage = Mathf.Max(0, result.damage);
            int maxHit = Mathf.Max(0, result.maxHit);

            if (hit && definition != null && definition.maxHitPerBeastmasterLevel != 0f)
                damage = Mathf.RoundToInt(damage * (1f + definition.maxHitPerBeastmasterLevel * beastmasterLevel));

            object source = this;
            if (ownerTransform != null && ownerTransform.TryGetComponent<PlayerCombatTarget>(out var ownerTarget))
                source = ownerTarget;

            int finalDamage = hit
                ? target.ApplyDamage(damage, attacker.DamageType, SpellElement.None, source)
                : 0;

            Sprite hitsplatSprite;
            string hitsplatText;
            if (!hit || finalDamage <= 0)
            {
                hitsplatSprite = zeroHitsplat;
                hitsplatText = "0";
            }
            else if (finalDamage == Mathf.Max(maxHit, damage))
            {
                hitsplatSprite = maxHitHitsplat;
                hitsplatText = finalDamage.ToString();
            }
            else
            {
                hitsplatSprite = damageHitsplat;
                hitsplatText = finalDamage.ToString();
            }

            Vector3 hitsplatPosition = FloatingTextAnchorUtility.ResolveAnchorPosition(target.transform, hitsplatFallbackOffset, hitsplatAnchorCache);
            FloatingText.Show(hitsplatText, hitsplatPosition, Color.white, null, hitsplatSprite);

            if (hit)
            {
                if (target is NpcCombatant npcCombatant)
                {
                    var npcAttack = npcCombatant.GetComponent<NpcAttackController>();
                    npcAttack?.BeginAttacking(this);
                }

                BeastmasterXp.TryGrantFromPetDamage(ownerTransform != null ? ownerTransform.gameObject : null, finalDamage);
                companionCombatBridge?.NotifyDamageDealt(finalDamage, attacker.Style, attacker.DamageType, target);

                if (finalDamage > 0 && !target.IsAlive && IsOreGolemTarget(target))
                    TryScheduleOreGolemHarvest(ownerTransform);
            }
        }

        private bool IsOreGolemTarget(CombatTarget target)
        {
            if (target == null)
                return false;

            if (target is NpcCombatant npcCombatant)
                return npcCombatant.GetComponent<OreMonsterRewardController>() != null;

            if (target is MonoBehaviour behaviour)
                return behaviour.GetComponent<OreMonsterRewardController>() != null;

            return false;
        }

        /// <summary>
        /// Schedules a delayed mining command after the companion defeats an ore golem so the
        /// reward node can spawn before the mining controller engages it.
        /// </summary>
        /// <param name="ownerTransform">Transform representing the owning player.</param>
        private void TryScheduleOreGolemHarvest(Transform ownerTransform)
        {
            if (companionController == null)
                return;

            var miningController = companionController.MiningController;
            if (miningController == null || !miningController.isActiveAndEnabled)
                return;

            GameObject ownerObject = ownerTransform != null ? ownerTransform.gameObject : null;
            if (ownerObject == null)
                ownerObject = GameObject.FindGameObjectWithTag("Player");

            if (ownerObject == null)
                return;

            if (pendingOreGolemHarvestRoutine != null)
            {
                StopCoroutine(pendingOreGolemHarvestRoutine);
                pendingOreGolemHarvestRoutine = null;
            }

            pendingOreGolemHarvestRoutine = StartCoroutine(
                AutoMineOreGolemNodeRoutine(ownerObject, miningController));
        }

        /// <summary>
        /// Waits a random 1-3 tick window before attempting to mine the personal node produced by
        /// a defeated ore golem. The delay allows the reward prefab to spawn reliably.
        /// </summary>
        private IEnumerator AutoMineOreGolemNodeRoutine(
            GameObject ownerObject,
            CompanionMiningController miningController)
        {
            IDisposable followerHold = miningController?.EnterTemporaryFollowerHold();
            bool waitForFollowerHandOff = false;
            try
            {
                int waitTicks = UnityEngine.Random.Range(1, 4);
                float waitSeconds = Mathf.Max(0f, waitTicks * CombatMath.TICK_SECONDS);

                if (waitSeconds > 0f)
                    yield return new WaitForSeconds(waitSeconds);
                else
                    yield return null;

                if (miningController == null || !miningController.isActiveAndEnabled)
                    yield break;

                if (ownerObject == null)
                    ownerObject = GameObject.FindGameObjectWithTag("Player");

                if (ownerObject == null)
                    yield break;

                var personalController = ResolvePersonalNodeController(ownerObject);
                if (personalController == null)
                    yield break;

                var node = personalController.ActiveNode;
                if (node == null || node.IsExpired)
                    yield break;

                var rock = node.GetComponent<MineableRock>();
                if (rock == null || rock.IsDepleted)
                    yield break;

                // Skip the automated mining coroutine entirely when the companion has no space left for the
                // ore reward. This prevents the follower hold from lingering and keeps the companion mobile.
                var oreDefinition = rock.RockDef != null ? rock.RockDef.Ore : null;
                if (!miningController.HasInventoryCapacityForOre(oreDefinition))
                {
                    miningController.PublishInventoryFullMessage();
                    yield break;
                }

                var commandResult = CompanionMiningCommandResult.RequirementsNotMet;
                bool commandAccepted = miningController.TryCommandMine(
                    rock,
                    out commandResult,
                    preserveFollowerHold: true);

                waitForFollowerHandOff = commandAccepted || commandResult == CompanionMiningCommandResult.InventoryFull;

                if (waitForFollowerHandOff)
                {
                    // Allow the mining coroutine to acquire its follower hold before we release the
                    // temporary automation lock so the follower counter never drops to zero mid-handoff.
                    yield return null;
                }
            }
            finally
            {
                followerHold?.Dispose();
                pendingOreGolemHarvestRoutine = null;
            }
        }

        /// <summary>
        /// Attempts to locate the mining personal node controller responsible for the supplied owner
        /// so the companion can interact with the freshly spawned reward rock.
        /// </summary>
        private static MiningPersonalNodeController ResolvePersonalNodeController(GameObject potentialOwner)
        {
            if (potentialOwner == null)
                return null;

            if (potentialOwner.TryGetComponent(out MiningPersonalNodeController directController))
                return directController;

            var fromParent = potentialOwner.GetComponentInParent<MiningPersonalNodeController>();
            if (fromParent != null)
                return fromParent;

            var root = potentialOwner.transform.root;
            if (root != null)
            {
                if (root.TryGetComponent(out MiningPersonalNodeController rootController))
                    return rootController;

                var rootChildController = root.GetComponentInChildren<MiningPersonalNodeController>();
                if (rootChildController != null)
                    return rootChildController;
            }

            return potentialOwner.GetComponentInChildren<MiningPersonalNodeController>();
        }

        /// <summary>
        /// Determines whether ore golem attacks should be blocked based on companion equipment and inventory state.
        /// </summary>
        private bool ShouldBlockOreGolemAttack(CombatTarget target, bool fromDirectCommand)
        {
            if (!IsOreGolemTarget(target))
                return false;

            if (companionController == null)
            {
                if (fromDirectCommand)
                    ShowOreGolemBlockedFeedback();
                return true;
            }

            if (CompanionHasPickaxe(companionController))
                return false;

            if (fromDirectCommand)
                PublishOreGolemPickaxeReminder();
            return true;
        }

        /// <summary>
        /// Evaluates the companion equipment to confirm whether a pickaxe is currently wielded.
        /// </summary>
        /// <param name="controller">Controller representing the active companion.</param>
        /// <returns>True when the companion has a pickaxe equipped.</returns>
        private bool CompanionHasPickaxe(CompanionController controller)
        {
            if (controller == null)
                return false;

            var equipment = controller.Equipment;
            if (equipment == null)
                return false;

            var equippedEntry = equipment.GetEquipped(EquipmentSlot.Weapon);
            return equippedEntry.item != null && PickaxeUtility.IsPickaxe(equippedEntry.item);
        }

        private void ShowOreGolemBlockedFeedback()
        {
            string petName = definition != null && !string.IsNullOrWhiteSpace(definition.displayName)
                ? definition.displayName
                : name;
            string message = $"\"{petName}\" cannot harm the Golem, due to its magical properties";
            Transform anchor = floatingTextController != null ? floatingTextController.FloatingTextAnchor : transform;
            if (anchor == null)
                anchor = transform;

            GatheringFloatingTextService.TryShowAtAnchor(message, anchor);
        }

        /// <summary>
        /// Exposes the companion controller so external systems can supply a runtime binding when the
        /// controller is initialised. This ensures the combat controller can subscribe to equipment events
        /// regardless of component execution order.
        /// </summary>
        /// <param name="controller">Controller representing the active companion.</param>
        public void BindCompanionController(CompanionController controller)
        {
            if (companionController == controller)
            {
                TrySubscribeToEquipmentChanges();
                return;
            }

            UnsubscribeFromEquipmentChanges();
            companionController = controller;
            EnsureRangedController();
            TrySubscribeToEquipmentChanges();
        }

        private void OnEnable()
        {
            TrySubscribeToEquipmentChanges();
        }

        private void OnDisable()
        {
            UnsubscribeFromEquipmentChanges();
            CancelAttack();

            if (pendingOreGolemHarvestRoutine != null)
            {
                StopCoroutine(pendingOreGolemHarvestRoutine);
                pendingOreGolemHarvestRoutine = null;
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromEquipmentChanges();
        }

        private IEnumerator AttackSpriteSwap()
        {
            spriteRenderer.sprite = definition.attackSprite;
            yield return new WaitForSeconds(0.2f);
            spriteRenderer.sprite = defaultSprite;
            spriteSwapRoutine = null;
        }

        /// <summary>
        /// Resolves the owner's Beastmaster level, defaulting to one when the skill data is unavailable.
        /// </summary>
        /// <param name="ownerTransform">Transform of the owning player.</param>
        private static int ResolveBeastmasterLevel(Transform ownerTransform)
        {
            if (ownerTransform != null && ownerTransform.TryGetComponent<SkillManager>(out var skills))
                return Mathf.Max(1, skills.GetLevel(SkillType.Beastmaster));

            return 1;
        }

        /// <summary>
        /// Stops any active attack behaviour and restores the follower state so pets resume
        /// trailing their owner immediately after being hidden or disabled.
        /// </summary>
        public void CancelCombat()
        {
            CancelAttack();
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

            hasLastNonZeroChaseVelocity = false;
            hasPreviousTargetPosition = false;
            lastNonZeroChaseVelocity = Vector2.zero;
            previousTargetPosition = Vector3.zero;
            pathMover?.ResetAttackTracking();
            pathMover?.ResetCachedVelocity();

            if (hasRigidbody2D)
            {
                petRigidbody.linearVelocity = Vector2.zero;
            }

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
            {
                companionController ??= GetComponent<CompanionController>();
                var miningController = companionController != null ? companionController.MiningController : null;

                if (miningController == null || !miningController.HasActiveFollowerHold)
                    follower.enabled = true;
            }
        }

        /// <summary>
        /// Applies the supplied velocity to whichever sprite system the pet is using
        /// so facing and animation continue to reflect the last movement direction.
        /// </summary>
        /// <param name="velocity">Velocity to forward to the animator/renderer.</param>
        private void ApplyVisualVelocity(Vector2 velocity)
        {
            if (spriteAnimator != null)
                spriteAnimator.UpdateVisuals(velocity);
            else if (spriteRenderer != null)
                spriteRenderer.flipX = velocity.x > 0f;
        }

        private Vector2 ResolveAttackTargetPosition()
        {
            if (currentTarget == null)
            {
                return transform.position;
            }

            return currentTarget.transform.position;
        }

        private void TrySubscribeToEquipmentChanges()
        {
            if (!isActiveAndEnabled)
                return;

            if (companionController == null)
                return;

            var equipment = companionController.Equipment;
            if (equipment == null)
                return;

            if (subscribedEquipment == equipment)
                return;

            if (subscribedEquipment != null)
                subscribedEquipment.EquipmentSlotChanged -= OnCompanionEquipmentSlotChanged;

            subscribedEquipment = equipment;
            subscribedEquipment.EquipmentSlotChanged += OnCompanionEquipmentSlotChanged;
        }

        private void UnsubscribeFromEquipmentChanges()
        {
            if (subscribedEquipment == null)
                return;

            subscribedEquipment.EquipmentSlotChanged -= OnCompanionEquipmentSlotChanged;
            subscribedEquipment = null;
        }

        private void OnCompanionEquipmentSlotChanged(EquipmentSlot slot, InventoryEntry _)
        {
            if (slot != EquipmentSlot.Weapon)
                return;

            if (companionController == null)
                return;

            if (CompanionHasPickaxe(companionController))
                return;

            bool hadOreGolemTarget = currentTarget != null && IsOreGolemTarget(currentTarget);
            if (!hadOreGolemTarget)
                return;

            CancelAttack();
            PublishOreGolemPickaxeRemovalMessage();
            ShowOreGolemBlockedFeedback();
        }

        /// <summary>
        /// Publishes a chat line acknowledging that the player removed the companion pickaxe mid combat
        /// while the companion was attacking an ore golem. Keeps the response flavourful and informative.
        /// </summary>
        private void PublishOreGolemPickaxeRemovalMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string reminder = CompanionChatLibrary.GetRandomCompanionOreGolemMidCombatPickaxeRemovalMessage();
            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), reminder);
        }

        private void PublishOreGolemPickaxeReminder()
        {
            if (!CompanionDialogueThrottle.TryConsume(
                    OreGolemPickaxeReminderThrottleKey,
                    CompanionDialogueThrottle.DefaultDelaySeconds))
            {
                return;
            }

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string reminder = CompanionChatLibrary.GetRandomCompanionOreGolemPickaxeReminder();
            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), reminder);
        }
    }
}
