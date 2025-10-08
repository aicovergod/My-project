using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Audio;
using Combat.Ranged;
using EquipmentSystem;
using Skills;
using Skills.Common;
using Skills.Mining;
using Player;
using Player.Movement;
using NPC;
using Pets;
using UI;
using Magic;
using Status;
using Status.Freeze;
using Util;

namespace Combat
{
    /// <summary>
    /// Captures the data required to apply a single spell impact. The payload persists while a
    /// projectile travels so the combat controller can resolve hits consistently regardless of
    /// whether the spell lands instantly or after a delay.
    /// </summary>
    public struct SpellImpactContext
    {
        /// <summary>True when the accuracy roll succeeded and damage should be applied.</summary>
        public bool hit;

        /// <summary>The raw damage value rolled for this spell impact.</summary>
        public int damage;

        /// <summary>The maximum possible hit for the spell when it was cast.</summary>
        public int maxHit;

        /// <summary>The combat style that awarded XP for the cast.</summary>
        public CombatStyle style;

        /// <summary>The damage category associated with the cast.</summary>
        public DamageType damageType;

        /// <summary>The elemental type of the spell that landed.</summary>
        public SpellElement element;

        /// <summary>The definition backing the spell, used for status effect application.</summary>
        public SpellDefinition spell;
    }

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

        /// <summary>
        /// Broadcasts a combat driven buff so shared systems like the HUD can react without tight
        /// coupling. The timer definition mirrors the payload sent through <see cref="BuffEvents"/>.
        /// </summary>
        public void ReportStatusEffectApplied(BuffTimerDefinition definition, string sourceId = null, bool refreshTimer = true)
        {
            var context = new BuffEventContext
            {
                target = gameObject,
                definition = definition,
                sourceType = BuffSourceType.Combat,
                sourceId = string.IsNullOrEmpty(sourceId) ? name : sourceId,
                resetTimer = refreshTimer
            };

            if (refreshTimer)
                BuffEvents.RaiseBuffApplied(context);
            else
                BuffEvents.RaiseBuffRefreshed(context);
        }

        /// <summary>
        /// Broadcasts that a combat driven buff has ended on the owning player.
        /// </summary>
        public void ReportStatusEffectRemoved(BuffTimerDefinition definition, string sourceId = null)
        {
            var context = new BuffEventContext
            {
                target = gameObject,
                definition = definition,
                sourceType = BuffSourceType.Combat,
                sourceId = string.IsNullOrEmpty(sourceId) ? name : sourceId
            };

            BuffEvents.RaiseBuffRemoved(context);
        }

        private const float TILE_SIZE = 1f;
        private const float DEFAULT_RANGED_RANGE_TILES = 5f;
        private const float HALBERD_EXTRA_RANGE_TILES = 1f;

        private SkillManager skills;
        private PlayerHitpoints hitpoints;
        private EquipmentAggregator equipment;
        private Inventory.Equipment equipmentComponent;
        private Player.PlayerCombatLoadout loadout;
        private PlayerCombatBinder combatBinder;
        private IPlayerMovementController movementController;
        private Coroutine attackRoutine;
        private CombatTarget currentTarget;
        private float nextAttackTime;
        private RangedCombatController rangedController;

        [SerializeField, Tooltip("Centralised hitsplat sprite references assigned via the inspector.")]
        private HitSplatLibrary hitSplatLibrary;

        [SerializeField, Tooltip("Vertical offset used when no dedicated floating text anchor is found.")]
        private float hitsplatFallbackOffset = 1.1f;

        [Header("Line of Sight")]
        private static readonly string[] DefaultObstructionLayers = { "Obstacles", "Obstacle", "Physical Objects" };

        private static readonly string[] RangedWeaponNameKeywords =
        {
            "crossbow",
            "dart",
            "javelin",
            "throwing knife",
            "shortbow",
            "longbow"
        };

        [SerializeField, Tooltip("Layers considered solid when checking whether swings or spells have a clear path to the target.")]
        private LayerMask obstructionMask;

        private Sprite damageHitsplat;
        private Sprite zeroHitsplat;
        private Sprite maxHitHitsplat;
        private IReadOnlyDictionary<SpellElement, Sprite> elementHitsplats;
        private readonly Dictionary<Transform, FloatingTextAnchorUtility.AnchorCache> hitsplatAnchorCache = new Dictionary<Transform, FloatingTextAnchorUtility.AnchorCache>();

        [Header("Feedback")]
        [SerializeField, Tooltip("Anchor used when displaying combat restriction feedback popups.")]
        private Transform floatingTextAnchor;

        private const string PickaxeRequirementMessage = "This golem, can only be harmed with a pickaxe";

        private void Awake()
        {
            EnsureObstructionMaskConfigured();

            // Grab required components from this object, falling back to parent/children so the
            // controller still works if supporting components live elsewhere in the hierarchy.
            skills = GetComponent<SkillManager>() ?? GetComponentInParent<SkillManager>() ?? GetComponentInChildren<SkillManager>();
            hitpoints = GetComponent<PlayerHitpoints>() ?? GetComponentInParent<PlayerHitpoints>() ?? GetComponentInChildren<PlayerHitpoints>();
            equipment = GetComponent<EquipmentAggregator>() ?? GetComponentInParent<EquipmentAggregator>() ?? GetComponentInChildren<EquipmentAggregator>();
            equipmentComponent = GetComponent<Inventory.Equipment>() ?? GetComponentInParent<Inventory.Equipment>() ?? GetComponentInChildren<Inventory.Equipment>();
            loadout = GetComponent<Player.PlayerCombatLoadout>() ?? GetComponentInParent<Player.PlayerCombatLoadout>() ?? GetComponentInChildren<Player.PlayerCombatLoadout>();
            combatBinder = GetComponent<PlayerCombatBinder>() ?? GetComponentInParent<PlayerCombatBinder>() ?? GetComponentInChildren<PlayerCombatBinder>();
            movementController = GetComponent<PlayerMovementController>()
                ?? GetComponentInParent<PlayerMovementController>()
                ?? GetComponentInChildren<PlayerMovementController>();

            rangedController = rangedController
                ?? GetComponent<RangedCombatController>()
                ?? GetComponentInParent<RangedCombatController>()
                ?? GetComponentInChildren<RangedCombatController>();

            if (movementController == null)
            {
                var moverFacade = GetComponent<PlayerMover>() ?? GetComponentInParent<PlayerMover>() ?? GetComponentInChildren<PlayerMover>();
                movementController = moverFacade != null ? moverFacade.MovementController : null;
            }

            if (rangedController != null)
                rangedController.BindCombatController(this);

            if (skills == null)
                Debug.LogWarning("CombatController could not find a SkillManager; damage will use level 1 stats.", this);
            if (equipment == null)
                Debug.LogWarning("CombatController could not find an EquipmentAggregator; equipment bonuses will be ignored.", this);

            if (skills != null)
                skills.LevelChanged += OnSkillLevelChanged;

            if (hitSplatLibrary == null)
            {
                Debug.LogError("CombatController requires a HitSplatLibrary reference. Assign one in the inspector.", this);
            }
            else
            {
                damageHitsplat = hitSplatLibrary.DamageHitsplat;
                zeroHitsplat = hitSplatLibrary.ZeroDamageHitsplat;
                maxHitHitsplat = hitSplatLibrary.MaxHitHitsplat;
                elementHitsplats = hitSplatLibrary.ElementHitsplats;
            }

            if (floatingTextAnchor == null)
            {
                floatingTextAnchor = transform.Find("FloatingTextAnchor");
                if (floatingTextAnchor == null)
                    floatingTextAnchor = transform;
            }
        }

        private void OnValidate()
        {
            EnsureObstructionMaskConfigured();
        }

        /// <summary>
        /// Ensures the obstruction mask contains the expected default layers when unset.
        /// </summary>
        private void EnsureObstructionMaskConfigured()
        {
            int defaultMask = LayerMask.GetMask(DefaultObstructionLayers);

            // Always merge in the default layers so inspector overrides can't accidentally drop
            // physical blockers like AntiMeleeObstacle colliders from the mask.
            int combinedMask = obstructionMask.value | defaultMask;
            obstructionMask = combinedMask;
        }

        /// <summary>
        /// Attempt to attack the specified target. Returns false if not ready.
        /// </summary>
        public bool TryAttackTarget(CombatTarget target)
        {
            if (target == null || !target.IsAlive)
                return false;
            if (RequiresPickaxeForTarget(target) && !HasPickaxeEquipped())
            {
                ShowPickaxeRequirementFeedback();
                return false;
            }
            if (Vector2.Distance(transform.position, target.transform.position) > GetCurrentAttackRange())
                return false;
            if (!HasLineOfSight(target.transform))
            {
                nextAttackTime = Mathf.Max(nextAttackTime, Time.time + CombatMath.TICK_SECONDS);
                return false;
            }
            if (Time.time < nextAttackTime && attackRoutine == null)
                return false;
            if (attackRoutine != null)
            {
                if (currentTarget == target)
                    return false;
                StopCoroutine(attackRoutine);
            }
            attackRoutine = StartCoroutine(AttackRoutine(target));
            if (PetDropSystem.GuardModeEnabled)
            {
                var pet = PetDropSystem.ActivePetCombat;
                pet?.CommandAttack(target, false);
            }
            return true;
        }

        private void OnDestroy()
        {
            if (skills != null)
                skills.LevelChanged -= OnSkillLevelChanged;
        }

        private void OnSkillLevelChanged(SkillType type, int level)
        {
            switch (type)
            {
                case SkillType.Magic:
                    // Keep spell damage data in sync and play the corresponding level-up chime.
                    MagicUI.UpdateStrikeMaxHits(level);
                    SoundManager.Instance.PlaySfx(SoundEffect.MagicLevelUp);
                    break;
                case SkillType.Attack:
                    SoundManager.Instance.PlaySfx(SoundEffect.AttackLevelUp);
                    break;
                case SkillType.Defence:
                    SoundManager.Instance.PlaySfx(SoundEffect.DefenceLevelUp);
                    break;
                case SkillType.Mining:
                    SoundManager.Instance.PlaySfx(SoundEffect.MiningLevelUp);
                    break;
                case SkillType.Woodcutting:
                    SoundManager.Instance.PlaySfx(SoundEffect.WoodcuttingLevelUp);
                    break;
                case SkillType.Fishing:
                    SoundManager.Instance.PlaySfx(SoundEffect.FishingLevelUp);
                    break;
                case SkillType.Cooking:
                    SoundManager.Instance.PlaySfx(SoundEffect.CookingLevelUp);
                    break;
                case SkillType.Beastmaster:
                    SoundManager.Instance.PlaySfx(SoundEffect.BeastmasterLevelUp);
                    break;
            }
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

        /// <summary>Exposes the currently expected attack range in world units.</summary>
        public float CurrentAttackRange => GetCurrentAttackRange();

        /// <summary>
        /// Determine the effective range for the next attack based on the combat data returned by the
        /// loadout and currently equipped weapon. Melee styles respect the melee reach, ranged defaults to
        /// a configured projectile span, and magic defers to the active spell definition.
        /// </summary>
        private float GetCurrentAttackRange()
        {
            DamageType damageType = DetermineActiveDamageType();
            switch (damageType)
            {
                case DamageType.Magic:
                    return GetMagicRange();
                case DamageType.Ranged:
                    return GetRangedWeaponRange();
                default:
                    return GetMeleeWeaponRange();
            }
        }

        /// <summary>
        /// Resolve the most accurate damage type for the upcoming attack. The active combat stats
        /// dictate the preferred type, falling back to the active spell, loadout, or equipped weapon
        /// when necessary.
        /// </summary>
        private DamageType DetermineActiveDamageType()
        {
            CombatantStats stats = null;
            if (combatBinder != null)
                stats = combatBinder.GetCombatantStats();
            else if (loadout != null)
                stats = loadout.GetCombatantStats();

            // Cache the currently equipped weapon once so multiple checks all reference
            // the same item data. This avoids recomputing lookups when we need to cross
            // validate the combat profile with the actual equipment.
            var weapon = GetEquippedWeapon();

            if (stats != null)
            {
                var reportedType = stats.DamageType;

                if (reportedType == DamageType.Melee && weapon != null)
                {
                    // Some merged combat profiles default to melee even when the player
                    // equips a ranged or magic weapon (for example when using pet merge
                    // loadouts). In those cases we trust the weapon's combat stats over
                    // the profile so movement and range checks line up with the equipped
                    // item.
                    if (weapon.combat.Magic > 0)
                        return DamageType.Magic;

                    if (WeaponNameIndicatesRanged(weapon))
                        return DamageType.Ranged;

                    if (weapon.combat.Range > 0 || weapon.combat.RangeStrength > 0)
                        return DamageType.Ranged;
                }

                // Pet merge profiles that explicitly flag ranged or magic combat types
                // should retain their declared damage category, so only melee defaults
                // are adjusted above.
                return reportedType;
            }

            var activeSpell = MagicUI.ActiveSpell;
            if (activeSpell != null)
                return DamageType.Magic;

            if (weapon != null)
            {
                if (weapon.combat.Magic > 0)
                    return DamageType.Magic;

                if (WeaponNameIndicatesRanged(weapon))
                    return DamageType.Ranged;
                if (weapon.combat.Range > 0 || weapon.combat.RangeStrength > 0)
                    return DamageType.Ranged;
            }

            return DamageType.Melee;
        }

        /// <summary>
        /// Inspect the equipped weapon's display name to catch ranged weapons that lack
        /// explicit ranged stat blocks. The lookup is case-insensitive so variations in
        /// item naming (Longbow vs. longbow) still flag the weapon as ranged.
        /// </summary>
        private bool WeaponNameIndicatesRanged(Inventory.ItemData weapon)
        {
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.itemName))
                return false;

            foreach (string keyword in RangedWeaponNameKeywords)
            {
                if (weapon.itemName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Fetch the currently equipped weapon, returning null when unarmed.
        /// </summary>
        internal Inventory.ItemData GetEquippedWeapon()
        {
            if (equipmentComponent == null)
                return null;
            Inventory.InventoryEntry entry = equipmentComponent.GetEquipped(Inventory.EquipmentSlot.Weapon);
            return entry.item;
        }

        /// <summary>
        /// Calculate melee reach, applying any halberd-style extension the weapon might provide.
        /// </summary>
        private float GetMeleeWeaponRange()
        {
            float range = CombatMath.MELEE_RANGE;
            var weapon = GetEquippedWeapon();
            if (weapon != null && weapon.isHalberd)
                range = Mathf.Max(range, CombatMath.MELEE_RANGE + HALBERD_EXTRA_RANGE_TILES * TILE_SIZE);
            return range;
        }

        /// <summary>
        /// Determine the distance allowed for ranged weapons. Defaults to an OSRS-style projectile
        /// span while still allowing future weapon overrides via scriptable data.
        /// </summary>
        private float GetRangedWeaponRange()
        {
            float defaultRange = DEFAULT_RANGED_RANGE_TILES * TILE_SIZE;
            if (rangedController != null)
                return rangedController.ResolveRangedRange(defaultRange);

            return defaultRange;
        }

        /// <summary>
        /// Retrieve the effective spell range while providing a melee fallback when no spell is
        /// selected.
        /// </summary>
        private static float GetMagicRange()
        {
            float range = MagicUI.GetActiveSpellRange();
            return range > 0f ? range : CombatMath.MELEE_RANGE;
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
                if (Vector2.Distance(transform.position, target.transform.position) > GetCurrentAttackRange())
                    break;
                if (!HasLineOfSight(target.transform))
                {
                    yield return new WaitForSeconds(CombatMath.TICK_SECONDS * 0.25f);
                    continue;
                }
                if (RequiresPickaxeForTarget(target) && !HasPickaxeEquipped())
                {
                    ShowPickaxeRequirementFeedback();
                    break;
                }
                movementController?.FaceTarget(target.transform);
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

        /// <summary>
        /// Represents the outcome of a combat damage roll so that other combat systems like
        /// ranged projectiles can forward the result without duplicating the calculation logic.
        /// </summary>
        public struct DamageResult
        {
            public int damage;
            public bool hit;
            public int maxHit;
        }

        internal DamageResult CalculateDamage(CombatantStats attacker, CombatTarget target)
        {
            var defender = GetDefenderStats(target, attacker);

            // Effective level helpers bake in combat style bonuses so combined equipment stats
            // remain purely gear driven. This keeps UI/tooling aligned with the aggregated stats
            // while ensuring combat rolls still honour style-specific boosts.
            int attEff = attacker.DamageType switch
            {
                DamageType.Magic => CombatMath.GetEffectiveAttack(attacker.MagicLevel, CombatStyle.Accurate),
                DamageType.Ranged => CombatMath.GetEffectiveRanged(attacker.RangedLevel, attacker.Style),
                _ => CombatMath.GetEffectiveAttack(attacker.AttackLevel, attacker.Style)
            };
            // CombatMath handles defensive style bonuses (Longrange/Defensive/Controlled) instead
            // of mutating equipment stats, keeping defender equipment values gear-accurate.
            int defEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            // Mirror OSRS combat by selecting the correct offensive bonus based on the damage type.
            // Melee continues to rely on the weapon's attack rating, magic uses spell accuracy, and
            // ranged now honours the weapon's range accuracy bonus instead of the melee attack value.
            int attackBonus = attacker.DamageType switch
            {
                DamageType.Magic => attacker.Equip.magic,
                DamageType.Ranged => attacker.Equip.range,
                _ => attacker.Equip.attack
            };
            int atkRoll = CombatMath.GetAttackRoll(attEff, attackBonus);
            int defBonus = attacker.DamageType switch
            {
                DamageType.Magic => defender.Equip.magicDef,
                DamageType.Ranged => defender.Equip.rangeDef,
                _ => defender.Equip.meleeDef
            };
            int defRoll = CombatMath.GetDefenceRoll(defEff, defBonus);
            float chance = CombatMath.ChanceToHit(atkRoll, defRoll);
            bool hit = UnityEngine.Random.value < chance;

            int maxHit;
            if (attacker.DamageType == DamageType.Magic)
            {
                maxHit = MagicUI.ActiveSpellMaxHit + Mathf.FloorToInt(attacker.Equip.magic / 10f);
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
            int damage = hit ? CombatMath.RollDamage(maxHit) : 0;
            return new DamageResult { damage = damage, hit = hit, maxHit = maxHit };
        }

        internal CombatantStats GetDefenderStats(CombatTarget target, CombatantStats attacker)
        {
            CombatantStats stats = null;
            DamageType incomingType = attacker != null ? attacker.DamageType : DamageType.Melee;

            if (target is NpcCombatant npc)
            {
                stats = npc.GetCombatantStats();
            }
            else if (target is PlayerCombatTarget playerTarget)
            {
                stats = playerTarget.GetCombatantStats();
            }
            else if (target is MonoBehaviour targetBehaviour)
            {
                var profile = targetBehaviour.GetComponent<ICombatProfile>();
                if (profile != null)
                    stats = profile.GetCombatStats();
            }

            if (stats == null)
            {
                stats = new CombatantStats
                {
                    AttackLevel = 1,
                    StrengthLevel = 1,
                    RangedLevel = 1,
                    DefenceLevel = 1,
                    MagicLevel = 1,
                    Equip = new EquipmentAggregator.CombinedStats { rangeStrength = 0 },
                    Style = CombatStyle.Defensive,
                    DamageType = target != null ? target.PreferredDefenceType : incomingType
                };
            }
            else
            {
                stats.DamageType = incomingType;
            }

            return stats;
        }

        internal int ApplyDamageResult(CombatTarget target, int damage, bool hit, int maxHit, CombatStyle style, DamageType type, SpellElement element)
        {
            var targetMb = target as MonoBehaviour;
            string targetName = targetMb != null ? targetMb.name : "target";
            int finalDamage = 0;
            if (hit)
            {
                var source = GetComponent<Player.PlayerCombatTarget>();
                finalDamage = target.ApplyDamage(damage, type, element, source);
                Sprite sprite;
                Color textColor = Color.white;
                Vector3 hitsplatPosition = FloatingTextAnchorUtility.ResolveAnchorPosition(target.transform, hitsplatFallbackOffset, hitsplatAnchorCache);
                if (finalDamage == 0)
                {
                    sprite = zeroHitsplat;
                    FloatingText.Show("0", hitsplatPosition, textColor, null, sprite);
                }
                else if (type == DamageType.Magic && elementHitsplats != null && elementHitsplats.TryGetValue(element, out var elemSprite) && elemSprite != null)
                {
                    sprite = elemSprite;
                    if (element == SpellElement.Air)
                        textColor = Color.black;
                    FloatingText.Show(finalDamage.ToString(), hitsplatPosition, textColor, null, sprite);
                }
                else
                {
                    sprite = finalDamage == maxHit ? maxHitHitsplat : damageHitsplat;
                    FloatingText.Show(finalDamage.ToString(), hitsplatPosition, textColor, null, sprite);
                }
                AwardXp(finalDamage, style, type);
                if (finalDamage > 0 && !target.IsAlive)
                    OnTargetKilled?.Invoke(target);
                Debug.Log($"Player dealt {finalDamage} damage to {targetName}.");
                var applier = GetComponentInChildren<OnHitPoisonApplier>();
                if (applier != null && targetMb != null)
                    applier.TryApply(targetMb.gameObject, finalDamage > 0, source);
                OnAttackLanded?.Invoke(finalDamage, hit);
            }
            else
            {
                Vector3 hitsplatPosition = FloatingTextAnchorUtility.ResolveAnchorPosition(target.transform, hitsplatFallbackOffset, hitsplatAnchorCache);
                FloatingText.Show("0", hitsplatPosition, Color.white, null, zeroHitsplat);
                Debug.Log($"Player missed {targetName}.");
                OnAttackLanded?.Invoke(0, false);
            }

            return finalDamage;
        }

        protected virtual bool HasLineOfSight(Transform targetTransform)
        {
            if (targetTransform == null)
                return false;

            Vector2 origin = transform.position;
            Vector2 destination = targetTransform.position;

            return LineOfSightUtility.HasLineOfSight(
                origin,
                destination,
                obstructionMask,
                transform,
                targetTransform);
        }

        protected virtual void ResolveAttack(CombatTarget target)
        {
            CombatantStats attacker;
            if (combatBinder != null)
                attacker = combatBinder.GetCombatantStats();
            else if (loadout != null)
                attacker = loadout.GetCombatantStats();
            else
                attacker = CombatantStats.ForPlayer(skills, equipment, CombatStyle.Accurate, DamageType.Melee);

            var result = CalculateDamage(attacker, target);
            var activeSpell = MagicUI.ActiveSpell;

            if (attacker.DamageType == DamageType.Magic)
            {
                SpellImpactContext context = new SpellImpactContext
                {
                    hit = result.hit,
                    damage = result.damage,
                    maxHit = result.maxHit,
                    style = attacker.Style,
                    damageType = attacker.DamageType,
                    element = activeSpell != null ? activeSpell.element : SpellElement.None,
                    spell = activeSpell
                };

                if (activeSpell != null && activeSpell.projectilePrefab != null)
                {
                    var projObj = Instantiate(activeSpell.projectilePrefab, transform.position, Quaternion.identity);
                    var proj = projObj.GetComponent<Magic.FireProjectile>();
                    if (proj != null)
                    {
                        proj.Initialise(this, target, context);
                        if (activeSpell.speed > 0f)
                            proj.speed = activeSpell.speed;
                        proj.hitFadeTime = activeSpell.hitFadeTime;
                        if (activeSpell.hitEffectPrefab != null)
                            proj.hitEffectPrefab = activeSpell.hitEffectPrefab;
                    }
                }
                else
                {
                    ApplySpellDamage(target, context);
                }
            }
            else if (attacker.DamageType == DamageType.Ranged && rangedController != null)
            {
                rangedController.ResolveRangedAttack(attacker, target, result);
            }
            else
            {
                int primaryDamage = ApplyDamageResult(target, result.damage, result.hit, result.maxHit, attacker.Style, attacker.DamageType, SpellElement.None);
                ApplyHalberdAoe(attacker, target, primaryDamage, result.maxHit);
            }
        }

        /// <summary>
        /// Applies the outcome of a spell cast to the supplied target using a prepared impact
        /// context. Shared by both instant-hit and projectile-driven spells so damage, XP, and
        /// status effects remain consistent.
        /// </summary>
        public void ApplySpellDamage(CombatTarget target, SpellImpactContext context)
        {
            if (target == null)
                return;

            bool hit = context.hit;
            int resolvedDamage = hit ? Mathf.Max(0, context.damage) : 0;

            ApplyDamageResult(target, resolvedDamage, hit, context.maxHit, context.style, context.damageType, context.element);

            if (hit && context.spell != null)
                TryApplySpellStatusEffects(target, context.spell, hit);
        }

        private void TryApplySpellStatusEffects(CombatTarget target, SpellDefinition spell, bool hit)
        {
            if (!hit || target == null || spell == null)
                return;

            if (spell.appliesFreeze && spell.freezeDurationTicks > 0)
                TryApplyFreeze(target, spell);
        }

        /// <summary>
        /// Applies the frozen status effect to the supplied combat target when allowed.
        /// </summary>
        private void TryApplyFreeze(CombatTarget target, SpellDefinition spell)
        {
            var behaviour = target as MonoBehaviour;
            if (behaviour == null)
                return;

            var npc = behaviour.GetComponent<NpcCombatant>() ?? behaviour.GetComponentInParent<NpcCombatant>();
            if (npc != null && !npc.IsFreezable)
                return;

            var freezeController = behaviour.GetComponent<FrozenStatusController>() ??
                behaviour.GetComponentInParent<FrozenStatusController>() ??
                behaviour.GetComponentInChildren<FrozenStatusController>();

            if (freezeController == null)
            {
                Debug.LogWarning($"CombatController attempted to freeze '{behaviour.name}' but it does not have a FrozenStatusController component.", behaviour);
                return;
            }

            FreezeUtility.ApplyFreezeTicks(freezeController.gameObject, spell.freezeDurationTicks, BuffSourceType.Combat, spell.name);
        }

        private bool RequiresPickaxeForTarget(CombatTarget target)
        {
            if (target == null)
                return false;

            if (target is NpcCombatant npcCombatant)
                return npcCombatant.GetComponent<OreMonsterRewardController>() != null;

            if (target is MonoBehaviour behaviour)
            {
                return behaviour.GetComponent<OreMonsterRewardController>() != null;
            }

            return false;
        }

        private bool HasPickaxeEquipped()
        {
            if (equipmentComponent == null)
                return false;

            Inventory.InventoryEntry weaponEntry = equipmentComponent.GetEquipped(Inventory.EquipmentSlot.Weapon);
            var item = weaponEntry.item;
            if (item == null)
                return false;

            return PickaxeUtility.IsPickaxe(item);
        }

        private void ShowPickaxeRequirementFeedback()
        {
            var anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
            if (anchor == null)
                anchor = transform;

            GatheringFloatingTextService.TryShowAtAnchor(PickaxeRequirementMessage, anchor);
        }

        private void ApplyHalberdAoe(CombatantStats attacker, CombatTarget primaryTarget, int primaryDamage, int maxHit)
        {
            if (equipmentComponent == null || primaryTarget == null || primaryDamage <= 0)
                return;

            Inventory.InventoryEntry weaponEntry = equipmentComponent.GetEquipped(Inventory.EquipmentSlot.Weapon);
            var weaponData = weaponEntry.item;
            if (weaponData == null || !weaponData.isHalberd)
                return;

            if (weaponData.aoeRadiusTiles <= 0f || weaponData.aoeMultiplier <= 0f || weaponData.aoeMaxTargets <= 0)
                return;

            float radiusTiles = weaponData.aoeRadiusTiles;
            float radius = radiusTiles * TILE_SIZE;
            if (radius <= 0f)
                return;

            Vector2 origin = transform.position;
            Direction8 forwardDir = movementController != null ? movementController.FacingDirection : Direction8.Down;
            Vector2 forward = FacingDirToVector(forwardDir);
            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector2.down;

            float maxAngle = weaponData.coneAngleDeg > 0f ? weaponData.coneAngleDeg * 0.5f : 180f;
            int layerMask = LayerMask.GetMask("NPC", "Enemy", "Hostile");
            if (layerMask == 0)
                layerMask = ~LayerMask.GetMask("Player", "UI", "Pets");

            Collider2D[] hits = Physics2D.OverlapCircleAll(origin, radius, layerMask);
            if (hits == null || hits.Length == 0)
                return;

            var processedTargets = new HashSet<CombatTarget> { primaryTarget };
            var playerTarget = GetComponent<Player.PlayerCombatTarget>();
            int applied = 0;

            foreach (var hit in hits)
            {
                if (applied >= weaponData.aoeMaxTargets)
                    break;
                if (hit == null)
                    continue;

                CombatTarget otherTarget = hit.GetComponent<CombatTarget>() ?? hit.GetComponentInParent<CombatTarget>();
                if (otherTarget == null || processedTargets.Contains(otherTarget) || !otherTarget.IsAlive)
                    continue;

                if (otherTarget == primaryTarget || otherTarget == playerTarget)
                    continue;

                if (otherTarget is PetCombatController)
                    continue;

                Vector2 toTarget = (Vector2)otherTarget.transform.position - origin;
                float distanceWorld = toTarget.magnitude;
                if (distanceWorld <= Mathf.Epsilon)
                    continue;

                float distanceTiles = distanceWorld / TILE_SIZE;
                if (distanceTiles > radiusTiles)
                    continue;

                float angleDeg = Vector2.Angle(forward, toTarget);
                if (angleDeg > maxAngle)
                    continue;

                float falloffAngle = Mathf.Max(0f, Mathf.Cos(angleDeg * Mathf.Deg2Rad));
                if (falloffAngle <= 0f)
                    continue;

                float falloffDist = Mathf.Clamp01(1f - (distanceTiles / radiusTiles));
                if (falloffDist <= 0f)
                    continue;

                float scaledDamage = primaryDamage * weaponData.aoeMultiplier * falloffAngle * falloffDist;
                int secondaryDamage = Mathf.CeilToInt(scaledDamage);
                if (secondaryDamage <= 0)
                    continue;

                int minDamage = Mathf.CeilToInt(primaryDamage * 0.15f);
                int maxDamageClamp = Mathf.CeilToInt(primaryDamage * 0.80f);
                secondaryDamage = Mathf.Clamp(secondaryDamage, minDamage, maxDamageClamp);

                ApplyDamageResult(otherTarget, secondaryDamage, secondaryDamage > 0, maxHit, attacker.Style, attacker.DamageType, SpellElement.None);
                processedTargets.Add(otherTarget);
                applied++;
            }
        }

        private static Vector2 FacingDirToVector(Direction8 facingDir)
        {
            return Direction8Utility.ToVector(facingDir);
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
            if (type == DamageType.Ranged)
            {
                float total = 4f * damage;
                switch (style)
                {
                    case CombatStyle.Defensive:
                    case CombatStyle.Controlled:
                    case CombatStyle.Longrange:
                        float split = total * 0.5f;
                        skills?.AddXP(SkillType.Ranged, split);
                        skills?.AddXP(SkillType.Defence, split);
                        break;
                    default:
                        skills?.AddXP(SkillType.Ranged, total);
                        break;
                }
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
                if (UnityEngine.Random.value < chance)
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
            public int ApplyDamage(int amount, DamageType type, SpellElement element, object source)
            {
                Debug.Log($"Dummy took {amount} damage");
                return amount;
            }
        }
    }
}
