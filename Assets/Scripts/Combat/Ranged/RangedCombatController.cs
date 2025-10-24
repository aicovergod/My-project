using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Audio;
using Inventory;
using MyGame.Drops;
using UI;
using Util;
using Status.Poison;
using Player;

namespace Combat.Ranged
{
    /// <summary>
    /// Handles OSRS-style ranged combat including ammo validation, projectile spawning and XP resolution.
    /// The controller delegates final damage application back to <see cref="CombatController"/> so it
    /// remains compatible with the existing combat pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatController))]
    public class RangedCombatController : MonoBehaviour, ITickable, IRangedProjectileOwner
    {
        [Header("Data Sources")]
        [Tooltip("Explicit ranged weapon definitions. Resources at 'Combat/Ranged/Weapons' are auto-loaded in addition to this list.")]
        [SerializeField] private RangedWeaponData[] weaponDefinitions;
        [Tooltip("Explicit ammunition definitions. Resources at 'Combat/Ranged/Ammunition' are auto-loaded in addition to this list.")]
        [SerializeField] private AmmunitionData[] ammunitionDefinitions;
        [Tooltip("Resources.LoadAll path searched for weapon ScriptableObjects when entering play mode.")]
        [SerializeField] private string weaponResourcesPath = "Combat/Ranged/Weapons";
        [Tooltip("Resources.LoadAll path searched for ammunition ScriptableObjects when entering play mode.")]
        [SerializeField] private string ammoResourcesPath = "Combat/Ranged/Ammunition";

        [Header("References")]
        [Tooltip("Optional override for the projectile spawn position. Defaults to the owner's transform.")]
        [SerializeField] private Transform projectileSpawnPoint;
        [Tooltip("Spawner used when recovered ammo needs to be dropped on the ground due to full inventories.")]
        [SerializeField] private GroundItemSpawner groundItemSpawner;

        [Header("UI Feedback")]
        [Tooltip("Colour applied to the ammo counter when stacks are running low.")]
        [SerializeField] private Color ammoLowColor = new Color(1f, 0.85f, 0.2f);
        [Tooltip("Colour applied to the ammo counter when the player runs out of ammunition.")]
        [SerializeField] private Color ammoDepletedColor = new Color(1f, 0.35f, 0.35f);
        [Tooltip("Label displayed on the ammo slot when stacks are critically low.")]
        [SerializeField] private string ammoLowLabel = "LOW";
        [Tooltip("Label displayed on the ammo slot when no ammunition is available.")]
        [SerializeField] private string ammoEmptyLabel = "OUT";
        [Tooltip("Percentage (0-1) of the original stack size that should trigger the low ammo warning.")]
        [Range(0f, 1f)]
        [SerializeField] private float ammoWarningThreshold = 0.1f;
        [Tooltip("Minimum seconds between automatic ammo UI refreshes.")]
        [SerializeField] private float ammoUiRefreshInterval = 0.25f;

        [Header("Audio")]
        [Tooltip("Optional sound triggered when the player attempts to fire without ammo. Uses SoundManager.PlaySfxByFileName.")]
        [SerializeField] private string ammoDepletedSoundId = "ui_no_ammo";

        [Header("Debugging")]
        [SerializeField] private bool enableDebugLogging;

        /// <summary>Raised when the draw animation should begin.</summary>
        public event Action<RangedAttackContext> ShotPrepared;
        /// <summary>Raised when the projectile is spawned.</summary>
        public event Action<RangedAttackContext> ShotFired;
        /// <summary>Raised when the projectile lands and damage has been resolved.</summary>
        public event Action<RangedAttackContext> ShotResolved;

        private readonly Dictionary<string, RangedWeaponData> weaponLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AmmunitionData> ammoLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<IRangedStatModifierProvider> modifierBuffer = new();

        private CombatController combatController;
        private Inventory.Inventory inventory;
        private Equipment equipmentComponent;
        private PlayerCombatLoadout playerCombatLoadout;
        private PlayerCombatBinder combatBinder;

        private RangedWeaponData currentWeapon;
        private AmmunitionData currentAmmo;
        private string currentWeaponId;
        private string currentAmmoId;
        private int currentAmmoCount;
        private string lastRejectedAmmoId;
        private string lastRejectedWeaponId;
        private int initialAmmoCount;
        private float nextUiRefresh;
        private float lastAmmoWarningTime;
        private Coroutine drawRoutine;
        private int attackCooldownTicks;
        private bool definitionsLoaded;
        private bool subscribedToTicker;
        private CombatTarget attackerCombatTarget;
        private PoisonConfig cachedDefaultPoisonConfig;
        private bool attemptedDefaultPoisonLoad;

        private const int AmmoPerShot = 1;
        private const float WarningCooldownSeconds = 1.5f;
        private const string DefaultPoisonResourcePath = "Status/Poison/Poison_p";

        /// <summary>
        /// Called by <see cref="CombatController.Awake"/> so the ranged system can share runtime state.
        /// </summary>
        public void BindCombatController(CombatController controller)
        {
            combatController = controller;
            attackerCombatTarget = null;
            ResolveAttackerCombatTarget();
        }

        private void Awake()
        {
            combatController = combatController != null ? combatController : GetComponent<CombatController>();
            inventory = GetComponent<Inventory.Inventory>() ?? GetComponentInParent<Inventory.Inventory>() ?? GetComponentInChildren<Inventory.Inventory>();
            equipmentComponent = GetComponent<Equipment>() ?? GetComponentInParent<Equipment>() ?? GetComponentInChildren<Equipment>();
            playerCombatLoadout = GetComponent<PlayerCombatLoadout>() ?? GetComponentInParent<PlayerCombatLoadout>() ?? GetComponentInChildren<PlayerCombatLoadout>();
            combatBinder = GetComponent<PlayerCombatBinder>() ?? GetComponentInParent<PlayerCombatBinder>() ?? GetComponentInChildren<PlayerCombatBinder>();

            if (projectileSpawnPoint == null)
                projectileSpawnPoint = transform;

            if (groundItemSpawner == null)
                groundItemSpawner = FindObjectOfType<GroundItemSpawner>();

            EnsureDatabasesLoaded();
            RefreshEquipmentState(true);
            ResolveAttackerCombatTarget();
        }

        private void OnEnable()
        {
            TrySubscribeToTicker();
        }

        private void OnDisable()
        {
            if (Ticker.Instance != null && subscribedToTicker)
                Ticker.Instance.Unsubscribe(this);
            subscribedToTicker = false;
            if (drawRoutine != null)
            {
                StopCoroutine(drawRoutine);
                drawRoutine = null;
            }
        }

        private void Start()
        {
            TrySubscribeToTicker();
        }

        private void Update()
        {
            TrySubscribeToTicker();

            if (Time.unscaledTime >= nextUiRefresh)
            {
                RefreshEquipmentState(false);
                nextUiRefresh = Time.unscaledTime + ammoUiRefreshInterval;
            }
        }

        private void TrySubscribeToTicker()
        {
            var ticker = Ticker.Instance;
            if (ticker != null && !subscribedToTicker)
            {
                ticker.Subscribe(this);
                subscribedToTicker = true;
            }
        }

        /// <summary>
        /// Allows the combat controller to resolve projectile range using weapon/ammo data.
        /// </summary>
        public float ResolveRangedRange(float fallbackRange)
        {
            float baseRange = fallbackRange;
            if (currentWeapon != null)
                baseRange = Mathf.Max(baseRange, currentWeapon.baseRangeTiles);

            float rangeBonus = 0f;
            if (currentWeapon != null)
                rangeBonus += currentWeapon.rangeBonusTiles;
            if (currentAmmo != null)
                rangeBonus += currentAmmo.rangeBonusTiles;

            CacheModifierProviders();
            for (int i = 0; i < modifierBuffer.Count; i++)
                rangeBonus += modifierBuffer[i].GetAdditionalRangeTiles();

            if (ResolveActiveCombatStyle() == CombatStyle.Longrange)
                rangeBonus += 2f;

            return Mathf.Max(0.1f, baseRange + rangeBonus);
        }

        /// <summary>
        /// Primary entry point used by <see cref="CombatController"/> when a ranged attack should be resolved.
        /// </summary>
        public void ResolveRangedAttack(CombatantStats attacker, CombatTarget target, CombatController.DamageResult _)
        {
            if (combatController == null)
                combatController = GetComponent<CombatController>();

            RefreshEquipmentState(false);

            if (currentWeapon == null)
            {
                Log("ResolveRangedAttack aborted: no ranged weapon definition found.");
                return;
            }

            AmmunitionData ammo = currentAmmo;
            if (!HasRequiredAmmo(currentWeapon, ammo))
            {
                HandleNoAmmo();
                return;
            }

            CacheModifierProviders();
            float accuracyMultiplier = Mathf.Max(0f, currentWeapon.accuracyMultiplier);
            float damageMultiplier = Mathf.Max(0f, currentWeapon.damageMultiplier);
            float ammoConsumptionMultiplier = 1f;

            if (ammo != null)
            {
                accuracyMultiplier *= Mathf.Max(0f, ammo.accuracyMultiplier);
                damageMultiplier *= Mathf.Max(0f, ammo.damageMultiplier);
            }

            for (int i = 0; i < modifierBuffer.Count; i++)
            {
                var provider = modifierBuffer[i];
                accuracyMultiplier *= Mathf.Max(0f, provider.GetAccuracyMultiplier());
                damageMultiplier *= Mathf.Max(0f, provider.GetDamageMultiplier());
                ammoConsumptionMultiplier *= Mathf.Max(0f, provider.GetAmmoConsumptionMultiplier());
            }

            var defender = combatController.GetDefenderStats(target, attacker);
            attacker.DamageType = DamageType.Ranged;

            // CombatMath layers in defensive/longrange style bonuses here so accuracy rolls stay in
            // sync with the player's chosen style without mutating aggregated equipment stats.
            int effectiveAttack = CombatMath.GetEffectiveRanged(attacker.RangedLevel, attacker.Style);
            int attackBonus = Mathf.RoundToInt(attacker.Equip.range * accuracyMultiplier);
            int attackRoll = CombatMath.GetAttackRoll(effectiveAttack, attackBonus);

            int defenderEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            int defenderBonus = defender.Equip.rangeDef;
            int defenceRoll = CombatMath.GetDefenceRoll(defenderEff, defenderBonus);
            float chanceToHit = CombatMath.ChanceToHit(attackRoll, defenceRoll);
            bool hit = UnityEngine.Random.value < chanceToHit;

            // Ranged strength retains the Accurate +3 bonus through CombatMath so the style still
            // rewards damage rolls without influencing the hit chance traced above.
            int effectiveStrength = CombatMath.GetEffectiveRangedStrength(attacker.RangedLevel, attacker.Style);
            int strengthBonus = Mathf.Max(0, attacker.Equip.rangeStrength);
            int maxHit = CombatMath.GetMaxHit(effectiveStrength, strengthBonus);
            maxHit = Mathf.RoundToInt(maxHit * Mathf.Max(0f, damageMultiplier));
            if (maxHit < 0)
                maxHit = 0;

            int damage = hit ? CombatMath.RollDamage(maxHit) : 0;

            var context = new RangedAttackContext
            {
                combatController = combatController,
                rangedController = this,
                attacker = attacker,
                target = target,
                weaponId = currentWeapon != null ? currentWeapon.WeaponId : null,
                weapon = currentWeapon,
                ammunition = ammo,
                origin = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position,
                targetPosition = target != null ? target.transform.position : transform.position,
                damageResult = new CombatController.DamageResult { damage = damage, hit = hit, maxHit = maxHit },
                ammoConsumed = false,
                finalAccuracyMultiplier = accuracyMultiplier,
                finalDamageMultiplier = damageMultiplier
            };

            ShotPrepared?.Invoke(context);
            currentWeapon.specialEffect?.OnShotPrepared(context);
            ammo?.specialEffect?.OnShotPrepared(context);

            if (attackCooldownTicks <= 0)
                attackCooldownTicks = currentWeapon.attackSpeedTicks;

            bool consumeAmmo = ammoConsumptionMultiplier > 0f && (ammo == null || !ammo.infinite);
            int remaining = currentAmmoCount;
            if (consumeAmmo && !ConsumeAmmo(currentWeapon, ammo, out remaining))
            {
                HandleNoAmmo();
                return;
            }

            if (consumeAmmo)
            {
                context.ammoConsumed = true;
                currentAmmoCount = remaining;
                UpdateAmmoUi();
            }

            if (drawRoutine != null)
                StopCoroutine(drawRoutine);
            drawRoutine = StartCoroutine(FireAfterDraw(context));
        }

        public void OnTick()
        {
            if (attackCooldownTicks > 0)
                attackCooldownTicks--;
        }

        /// <summary>
        /// Invoked by <see cref="RangedProjectile"/> when the projectile reaches its destination.
        /// </summary>
        public void HandleProjectileImpact(RangedAttackContext context, RangedProjectile projectile)
        {
            if (projectile != null)
                Destroy(projectile.gameObject);

            RangedAttackContext resolvedContext = EnsureContextWeapon(context);
            var resolvedWeapon = resolvedContext.weapon;

            if (resolvedWeapon != null && resolvedWeapon.impactVfxPrefab != null)
            {
                Vector3 vfxPos = resolvedContext.target != null ? resolvedContext.target.transform.position : resolvedContext.targetPosition;
                Instantiate(resolvedWeapon.impactVfxPrefab, vfxPos, Quaternion.identity);
            }

            ApplyRangedDamage(resolvedContext);
            TryApplyAmmoPoison(resolvedContext);
            TryHandleAmmoRecovery(resolvedContext, resolvedContext.damageResult.hit);
            ShotResolved?.Invoke(resolvedContext);
            resolvedWeapon?.specialEffect?.OnImpactResolved(resolvedContext);
            resolvedContext.ammunition?.specialEffect?.OnImpactResolved(resolvedContext);
        }

        private IEnumerator FireAfterDraw(RangedAttackContext context)
        {
            RangedAttackContext resolvedContext = EnsureContextWeapon(context);

            float drawSeconds = 0f;
            if (resolvedContext.weapon != null)
                drawSeconds = Mathf.Max(0f, resolvedContext.weapon.drawTicks * CombatMath.TICK_SECONDS);
            if (drawSeconds > 0f)
                yield return new WaitForSeconds(drawSeconds);

            LaunchProjectile(resolvedContext);
            drawRoutine = null;
        }

        private void LaunchProjectile(RangedAttackContext context)
        {
            context = EnsureContextWeapon(context);
            RangedWeaponData weaponReference = context.weapon;
            GameObject prefab = null;
            if (context.ammunition != null && context.ammunition.projectilePrefab != null)
                prefab = context.ammunition.projectilePrefab;
            else if (weaponReference != null)
                prefab = weaponReference.projectilePrefab;
            if (prefab == null)
            {
                ShotFired?.Invoke(context);
                context.weapon?.specialEffect?.OnProjectileLaunched(context);
                context.ammunition?.specialEffect?.OnProjectileLaunched(context);
                if (context.weapon != null && !string.IsNullOrEmpty(context.weapon.releaseSoundId))
                    SoundManager.Instance?.PlaySfxByFileName(context.weapon.releaseSoundId);

                ApplyRangedDamage(context);
                TryApplyAmmoPoison(context);
                TryHandleAmmoRecovery(context, context.damageResult.hit);
                ShotResolved?.Invoke(context);
                context.weapon?.specialEffect?.OnImpactResolved(context);
                context.ammunition?.specialEffect?.OnImpactResolved(context);
                return;
            }

            Vector3 spawnPos = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;
            var instance = Instantiate(prefab, spawnPos, Quaternion.identity);
            var projectile = instance.GetComponent<RangedProjectile>();
            if (projectile == null)
            {
                Debug.LogError($"Projectile prefab '{prefab.name}' is missing a RangedProjectile component.", prefab);
                Destroy(instance);
                ApplyRangedDamage(context);
                TryApplyAmmoPoison(context);
                TryHandleAmmoRecovery(context, context.damageResult.hit);
                ShotResolved?.Invoke(context);
                context.weapon?.specialEffect?.OnImpactResolved(context);
                context.ammunition?.specialEffect?.OnImpactResolved(context);
                return;
            }

            ShotFired?.Invoke(context);
            context.weapon?.specialEffect?.OnProjectileLaunched(context);
            context.ammunition?.specialEffect?.OnProjectileLaunched(context);

            if (context.weapon != null && !string.IsNullOrEmpty(context.weapon.releaseSoundId))
                SoundManager.Instance?.PlaySfxByFileName(context.weapon.releaseSoundId);

            float projectileSpeed = weaponReference != null ? weaponReference.projectileSpeed : 10f;
            if (context.ammunition != null && context.ammunition.projectileSpeedOverride > 0f)
                projectileSpeed = context.ammunition.projectileSpeedOverride;
            projectile.Initialise(this, context.target, context, projectileSpeed);
        }

        /// <summary>
        /// Helper used by splash effects (such as chinchompas) to roll additional ranged hits
        /// against nearby targets without committing the results. The method replays the accuracy
        /// calculation using the cached multipliers from <see cref="RangedAttackContext"/> so
        /// secondary damage honours the same gear, prayer, and ammo modifiers as the primary shot.
        /// Callers are expected to forward the returned result to <see cref="CombatController.ApplyDamageResult"/>
        /// when they wish to apply the splash damage.
        /// </summary>
        /// <param name="context">Context captured when the primary projectile was fired.</param>
        /// <param name="secondaryTarget">Additional target receiving the splash roll.</param>
        /// <param name="damageScale">Multiplier applied to the cached max hit before rolling damage.</param>
        internal CombatController.DamageResult RollSecondaryRangedDamage(in RangedAttackContext context, CombatTarget secondaryTarget, float damageScale)
        {
            var controller = context.combatController != null ? context.combatController : combatController;
            if (controller == null || secondaryTarget == null)
                return default;

            var attackerStats = context.attacker;
            if (attackerStats == null)
                return default;

            damageScale = Mathf.Max(0f, damageScale);

            float accuracyMultiplier = context.finalAccuracyMultiplier;
            if (accuracyMultiplier <= 0f)
                accuracyMultiplier = context.rangedController == null ? 1f : 0f;
            accuracyMultiplier = Mathf.Max(0f, accuracyMultiplier);
            // Secondary rolls use the same CombatMath helpers so splash damage respects the chosen
            // style bonuses without ever altering cached equipment stats from the context payload.
            int effectiveAttack = CombatMath.GetEffectiveRanged(attackerStats.RangedLevel, attackerStats.Style);
            int attackBonus = Mathf.RoundToInt(attackerStats.Equip.range * accuracyMultiplier);
            int attackRoll = CombatMath.GetAttackRoll(effectiveAttack, attackBonus);

            var defender = controller.GetDefenderStats(secondaryTarget, attackerStats);
            if (defender == null)
                return default;
            int defenderEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            int defenderBonus = defender.Equip.rangeDef;
            int defenceRoll = CombatMath.GetDefenceRoll(defenderEff, defenderBonus);
            float chanceToHit = CombatMath.ChanceToHit(attackRoll, defenceRoll);
            bool hit = UnityEngine.Random.value < chanceToHit;

            int effectiveStrength = CombatMath.GetEffectiveRangedStrength(attackerStats.RangedLevel, attackerStats.Style);
            int strengthBonus = Mathf.Max(0, attackerStats.Equip.rangeStrength);
            int baseMaxHit = CombatMath.GetMaxHit(effectiveStrength, strengthBonus);
            float damageMultiplier = Mathf.Max(0f, context.finalDamageMultiplier);
            float scaledDamageMultiplier = Mathf.Max(0f, damageScale) * damageMultiplier;
            int scaledMaxHit = Mathf.RoundToInt(baseMaxHit * scaledDamageMultiplier);
            if (scaledMaxHit < 0)
                scaledMaxHit = 0;

            int damage = hit ? CombatMath.RollDamage(scaledMaxHit) : 0;
            return new CombatController.DamageResult
            {
                damage = damage,
                hit = hit,
                maxHit = scaledMaxHit
            };
        }

        private void ApplyRangedDamage(RangedAttackContext context)
        {
            CombatStyle style = context.attacker.Style;
            var result = context.damageResult;
            combatController.ApplyDamageResult(context.target, result.damage, result.hit, result.maxHit, style, DamageType.Ranged, SpellElement.None);
        }

        private bool ConsumeAmmo(RangedWeaponData weapon, AmmunitionData ammo, out int remaining)
        {
            remaining = currentAmmoCount;
            if (equipmentComponent == null)
                return false;

            EquipmentSlot slot = weapon.consumesWeaponStack ? EquipmentSlot.Weapon : EquipmentSlot.Arrow;
            return equipmentComponent.ConsumeEquipped(slot, AmmoPerShot, out remaining);
        }

        private void TryHandleAmmoRecovery(RangedAttackContext context, bool hit)
        {
            if (!context.ammoConsumed)
                return;

            float chance = 0f;
            bool spawnAsGroundItem = true;
            if (context.ammunition != null)
            {
                chance = hit ? context.ammunition.recoveryChanceOnHit : context.ammunition.recoveryChanceOnMiss;
                spawnAsGroundItem = context.ammunition.spawnRecoveryAsGroundItem;
            }
            if (chance <= 0f && context.weapon != null)
            {
                chance = hit ? context.weapon.recoveryChanceOnHit : context.weapon.recoveryChanceOnMiss;
                spawnAsGroundItem = context.weapon.spawnRecoveryAsGroundItem;
            }

            if (chance <= 0f || UnityEngine.Random.value > chance)
                return;

            ItemData item = context.ammunition != null ? context.ammunition.AmmoItem : context.weapon != null ? context.weapon.WeaponItem : null;
            if (item == null)
                return;

            if (inventory != null && inventory.AddItem(item, 1))
                return;

            if (!spawnAsGroundItem || groundItemSpawner == null)
                return;

            Vector3 spawnPos = context.target != null ? context.target.transform.position : context.targetPosition;
            groundItemSpawner.Spawn(item, 1, spawnPos);
        }

        /// <summary>
        /// Attempts to apply an ammunition-driven poison effect after a successful ranged hit.
        /// </summary>
        private void TryApplyAmmoPoison(in RangedAttackContext context)
        {
            if (!context.damageResult.hit)
                return;

            var ammo = context.ammunition;
            if (ammo == null || !ammo.appliesPoison)
                return;

            float chance = Mathf.Clamp01(ammo.poisonApplyChance);
            if (chance <= 0f || UnityEngine.Random.value > chance)
                return;

            var targetComponent = context.target as Component;
            var targetObject = targetComponent != null ? targetComponent.gameObject : null;
            if (targetObject == null)
                return;

            var poisonController = ResolvePoisonController(targetObject);
            if (poisonController == null || poisonController.IsImmune)
                return;

            var config = ResolveAmmoPoisonConfig(ammo);
            if (config == null)
                return;

            var source = ResolveAttackerCombatTarget();
            poisonController.ApplyPoison(config, source);
        }

        /// <summary>
        /// Resolves the poison payload for the supplied ammunition, falling back to the linked item or default config.
        /// </summary>
        private PoisonConfig ResolveAmmoPoisonConfig(AmmunitionData ammo)
        {
            if (ammo == null)
                return null;

            var config = ammo.PoisonConfig;
            if (config != null)
                return config;

            var item = ammo.AmmoItem;
            if (item != null && item.onHitPoison != null)
                return item.onHitPoison;

            return ResolveDefaultPoisonConfig();
        }

        /// <summary>
        /// Loads the shared fallback poison configuration when ammo does not provide an explicit payload.
        /// </summary>
        private PoisonConfig ResolveDefaultPoisonConfig()
        {
            if (cachedDefaultPoisonConfig == null && !attemptedDefaultPoisonLoad)
            {
                attemptedDefaultPoisonLoad = true;
                cachedDefaultPoisonConfig = Resources.Load<PoisonConfig>(DefaultPoisonResourcePath);
                if (cachedDefaultPoisonConfig == null)
                    Debug.LogError($"RangedCombatController could not load default poison config at '{DefaultPoisonResourcePath}'.", this);
            }

            return cachedDefaultPoisonConfig;
        }

        /// <summary>
        /// Attempts to resolve the owning combatant's <see cref="CombatTarget"/> so poison ticks can attribute damage correctly.
        /// </summary>
        private CombatTarget ResolveAttackerCombatTarget()
        {
            if (attackerCombatTarget != null)
                return attackerCombatTarget;

            if (combatController == null)
                combatController = GetComponent<CombatController>();

            if (combatController == null)
                return null;

            attackerCombatTarget = combatController.GetComponent<CombatTarget>()
                ?? combatController.GetComponentInParent<CombatTarget>()
                ?? combatController.GetComponentInChildren<CombatTarget>();
            return attackerCombatTarget;
        }

        /// <summary>
        /// Resolves the currently active combat style so range extensions can respond to player
        /// selections (e.g., Longrange adding defensive XP and extending attack distance).
        /// Falls back to Accurate when the owning object has no explicit combat profile.
        /// </summary>
        private CombatStyle ResolveActiveCombatStyle()
        {
            if (playerCombatLoadout == null)
            {
                playerCombatLoadout = GetComponent<PlayerCombatLoadout>()
                    ?? GetComponentInParent<PlayerCombatLoadout>()
                    ?? GetComponentInChildren<PlayerCombatLoadout>();
            }

            if (playerCombatLoadout != null)
                return playerCombatLoadout.Style;

            if (combatBinder == null)
            {
                combatBinder = GetComponent<PlayerCombatBinder>()
                    ?? GetComponentInParent<PlayerCombatBinder>()
                    ?? GetComponentInChildren<PlayerCombatBinder>();

                if (combatBinder == null && combatController != null)
                {
                    combatBinder = combatController.GetComponent<PlayerCombatBinder>()
                        ?? combatController.GetComponentInParent<PlayerCombatBinder>()
                        ?? combatController.GetComponentInChildren<PlayerCombatBinder>();
                }
            }

            if (combatBinder != null)
            {
                CombatantStats stats = combatBinder.GetCombatantStats();
                if (stats != null)
                    return stats.Style;
            }

            return CombatStyle.Accurate;
        }

        /// <summary>
        /// Attempts to locate a <see cref="PoisonController"/> on the supplied target object.
        /// </summary>
        private static PoisonController ResolvePoisonController(GameObject targetObject)
        {
            if (targetObject == null)
                return null;

            return targetObject.GetComponent<PoisonController>()
                ?? targetObject.GetComponentInChildren<PoisonController>()
                ?? targetObject.GetComponentInParent<PoisonController>();
        }

        /// <summary>
        /// Ensures the ranged attack context references the correct weapon definition even if the
        /// player swapped equipment after the shot was prepared. The method re-resolves the weapon
        /// using the cached identifier so asynchronous stages (draw coroutine, projectile travel)
        /// remain deterministic.
        /// </summary>
        private RangedAttackContext EnsureContextWeapon(RangedAttackContext context)
        {
            if (context.weapon != null || string.IsNullOrEmpty(context.weaponId))
                return context;

            context.weapon = ResolveWeaponForContext(context.weaponId);
            return context;
        }

        /// <summary>
        /// Resolves a ranged weapon definition for the supplied identifier, preferring the current
        /// equipped weapon when it matches so we avoid redundant dictionary lookups.
        /// </summary>
        private RangedWeaponData ResolveWeaponForContext(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId))
                return null;

            if (!string.IsNullOrEmpty(currentWeaponId) && string.Equals(currentWeaponId, weaponId, StringComparison.Ordinal))
                return currentWeapon ?? ResolveWeapon(weaponId);

            return ResolveWeapon(weaponId);
        }

        private void HandleNoAmmo()
        {
            if (equipmentComponent != null)
                equipmentComponent.OverrideAmmoLabel(ammoEmptyLabel, ammoDepletedColor);

            ShowAmmoWarning("You have run out of ammo!", true);
            combatController?.CancelCombat();
        }

        private bool HasRequiredAmmo(RangedWeaponData weapon, AmmunitionData ammo)
        {
            if (weapon == null)
                return false;

            if (ammo != null && !weapon.IsAmmoAllowed(ammo))
            {
                if (currentAmmo == ammo)
                {
                    RejectCurrentAmmo(
                        $"Ammunition '{ammo.name}' is blocked by '{weapon.name}' restriction data.",
                        "Your weapon cannot use that ammo.");
                }
                return false;
            }

            if (weapon.consumesWeaponStack)
                return currentAmmoCount > 0;

            if (ammo != null && ammo.infinite)
                return true;

            return currentAmmoCount > 0;
        }

        private void ShowAmmoWarning(string message, bool playDepletedSound)
        {
            if (string.IsNullOrEmpty(message))
                return;

            if (Time.unscaledTime - lastAmmoWarningTime < WarningCooldownSeconds)
                return;

            FloatingText.Show(message, transform.position + Vector3.up * 0.5f, Color.white);
            if (playDepletedSound && !string.IsNullOrEmpty(ammoDepletedSoundId))
                SoundManager.Instance?.PlaySfxByFileName(ammoDepletedSoundId);
            lastAmmoWarningTime = Time.unscaledTime;
        }

        private void RejectCurrentAmmo(string debugMessage, string playerMessage, bool playAmmoDepletedSound = false, bool refreshUi = true)
        {
            if (!string.IsNullOrEmpty(debugMessage))
            {
                Debug.LogWarning(debugMessage, this);
                Log(debugMessage);
            }

            if (!string.IsNullOrEmpty(currentWeaponId))
                lastRejectedWeaponId = currentWeaponId;
            if (!string.IsNullOrEmpty(currentAmmoId))
                lastRejectedAmmoId = currentAmmoId;

            currentAmmo = null;
            currentAmmoId = null;
            currentAmmoCount = 0;

            if (equipmentComponent != null)
                equipmentComponent.OverrideAmmoLabel(ammoEmptyLabel, ammoDepletedColor);

            ShowAmmoWarning(playerMessage, playAmmoDepletedSound);
            if (refreshUi)
                UpdateAmmoUi();
        }

        private void CacheModifierProviders()
        {
            modifierBuffer.Clear();
            GetComponents(modifierBuffer);
        }

        private void RefreshEquipmentState(bool force)
        {
            if (equipmentComponent == null)
                return;

            var weaponEntry = equipmentComponent.GetEquipped(EquipmentSlot.Weapon);
            string weaponId = weaponEntry.item != null ? weaponEntry.item.id : null;
            bool weaponChanged = !string.Equals(currentWeaponId, weaponId, StringComparison.Ordinal);
            if (force || weaponChanged)
            {
                if (weaponChanged)
                {
                    lastRejectedAmmoId = null;
                    lastRejectedWeaponId = null;
                }

                currentWeaponId = weaponId;
                currentWeapon = !string.IsNullOrEmpty(weaponId) ? ResolveWeapon(weaponId) : null;
                if (currentWeapon == null && !string.IsNullOrEmpty(weaponId))
                    Log($"No RangedWeaponData found for '{weaponId}'.");
                initialAmmoCount = weaponEntry.count;
            }

            Inventory.InventoryEntry ammoEntry;
            if (currentWeapon != null && currentWeapon.consumesWeaponStack)
            {
                ammoEntry = weaponEntry;
            }
            else
            {
                ammoEntry = equipmentComponent.GetEquipped(EquipmentSlot.Arrow);
            }

            string ammoId = ammoEntry.item != null ? ammoEntry.item.id : null;
            bool ammoChanged = !string.Equals(currentAmmoId, ammoId, StringComparison.Ordinal);
            if (force || ammoChanged)
            {
                if (ammoChanged && !string.Equals(ammoId, lastRejectedAmmoId, StringComparison.Ordinal))
                {
                    lastRejectedAmmoId = null;
                    lastRejectedWeaponId = null;
                }

                currentAmmoId = ammoId;
                currentAmmo = !string.IsNullOrEmpty(ammoId) ? ResolveAmmo(ammoId) : null;
                if (currentAmmo == null && !string.IsNullOrEmpty(ammoId) && currentWeapon != null && !currentWeapon.consumesWeaponStack)
                    Log($"No AmmunitionData found for '{ammoId}'.");
                initialAmmoCount = ammoEntry.count;
            }

            currentAmmoCount = ammoEntry.count;
            if (currentAmmoCount > initialAmmoCount)
                initialAmmoCount = currentAmmoCount;

            if (currentWeapon != null && currentAmmo != null)
            {
                bool alreadyRejected = string.Equals(lastRejectedWeaponId, currentWeaponId, StringComparison.Ordinal) &&
                    string.Equals(lastRejectedAmmoId, currentAmmoId, StringComparison.Ordinal);

                if (!currentWeapon.consumesWeaponStack && currentAmmo.category != currentWeapon.ammunitionCategory)
                {
                    if (alreadyRejected)
                    {
                        currentAmmo = null;
                        currentAmmoId = null;
                        currentAmmoCount = 0;
                        if (equipmentComponent != null)
                            equipmentComponent.OverrideAmmoLabel(ammoEmptyLabel, ammoDepletedColor);
                    }
                    else
                    {
                        RejectCurrentAmmo(
                            $"Ammunition '{currentAmmo.name}' is not compatible with weapon '{currentWeapon.name}'. Expected {currentWeapon.ammunitionCategory} but received {currentAmmo.category}.",
                            "That ammo type doesn't fit your weapon.",
                            false,
                            false);
                    }
                }
                else if (!currentWeapon.IsAmmoAllowed(currentAmmo))
                {
                    if (alreadyRejected)
                    {
                        currentAmmo = null;
                        currentAmmoId = null;
                        currentAmmoCount = 0;
                        if (equipmentComponent != null)
                            equipmentComponent.OverrideAmmoLabel(ammoEmptyLabel, ammoDepletedColor);
                    }
                    else
                    {
                        RejectCurrentAmmo(
                            $"Ammunition '{currentAmmo.name}' is rejected by weapon '{currentWeapon.name}' due to explicit restriction lists.",
                            "Your weapon cannot use that ammo.",
                            false,
                            false);
                    }
                }
            }

            UpdateAmmoUi();
        }

        private void UpdateAmmoUi()
        {
            if (equipmentComponent == null)
                return;

            if (currentAmmoCount <= 0)
            {
                equipmentComponent.OverrideAmmoLabel(ammoEmptyLabel, ammoDepletedColor);
                return;
            }

            if (initialAmmoCount <= 0)
                initialAmmoCount = currentAmmoCount;

            bool showWarning = initialAmmoCount > 1;
            int warningThreshold = showWarning ? Mathf.Max(1, Mathf.RoundToInt(initialAmmoCount * ammoWarningThreshold)) : 0;
            if (showWarning && currentAmmoCount <= warningThreshold)
            {
                equipmentComponent.OverrideAmmoLabel(ammoLowLabel, ammoLowColor);
            }
            else
            {
                equipmentComponent.OverrideAmmoLabel(null);
            }
        }

        private RangedWeaponData ResolveWeapon(string weaponId)
        {
            EnsureDatabasesLoaded();
            return weaponLookup.TryGetValue(weaponId, out var data) ? data : null;
        }

        private AmmunitionData ResolveAmmo(string ammoId)
        {
            EnsureDatabasesLoaded();
            return ammoLookup.TryGetValue(ammoId, out var data) ? data : null;
        }

        private void EnsureDatabasesLoaded()
        {
            if (definitionsLoaded)
                return;

            definitionsLoaded = true;
            CacheWeapons(weaponDefinitions);
            CacheAmmunition(ammunitionDefinitions);

            if (!string.IsNullOrEmpty(weaponResourcesPath))
                CacheWeapons(Resources.LoadAll<RangedWeaponData>(weaponResourcesPath));
            if (!string.IsNullOrEmpty(ammoResourcesPath))
                CacheAmmunition(Resources.LoadAll<AmmunitionData>(ammoResourcesPath));
        }

        private void CacheWeapons(IEnumerable<RangedWeaponData> defs)
        {
            if (defs == null)
                return;

            foreach (var def in defs)
            {
                if (def == null)
                    continue;
                string id = def.WeaponId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogWarning($"RangedWeaponData '{def.name}' has no weapon id.", def);
                    continue;
                }
                weaponLookup[id] = def;
            }
        }

        private void CacheAmmunition(IEnumerable<AmmunitionData> defs)
        {
            if (defs == null)
                return;

            foreach (var def in defs)
            {
                if (def == null)
                    continue;
                string id = def.AmmoId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogWarning($"AmmunitionData '{def.name}' has no ammo id.", def);
                    continue;
                }
                ammoLookup[id] = def;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (weaponDefinitions != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var def in weaponDefinitions)
                {
                    if (def == null)
                        continue;
                    string id = def.WeaponId;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        Debug.LogWarning($"Ranged weapon asset '{def.name}' is missing an id.", def);
                        continue;
                    }
                    if (!seen.Add(id))
                        Debug.LogWarning($"Duplicate weapon id '{id}' detected in RangedCombatController.", def);
                }
            }

            if (ammunitionDefinitions != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var def in ammunitionDefinitions)
                {
                    if (def == null)
                        continue;
                    string id = def.AmmoId;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        Debug.LogWarning($"Ammunition asset '{def.name}' is missing an id.", def);
                        continue;
                    }
                    if (!seen.Add(id))
                        Debug.LogWarning($"Duplicate ammunition id '{id}' detected in RangedCombatController.", def);
                }
            }
        }
#endif

        private void Log(string message)
        {
            if (enableDebugLogging)
                Debug.Log($"[RangedCombat] {message}", this);
        }
    }
}
