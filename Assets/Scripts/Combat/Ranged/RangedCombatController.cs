using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Audio;
using Inventory;
using MyGame.Drops;
using UI;
using Util;

namespace Combat.Ranged
{
    /// <summary>
    /// Handles OSRS-style ranged combat including ammo validation, projectile spawning and XP resolution.
    /// The controller delegates final damage application back to <see cref="CombatController"/> so it
    /// remains compatible with the existing combat pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatController))]
    public class RangedCombatController : MonoBehaviour, ITickable
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

        private RangedWeaponData currentWeapon;
        private AmmunitionData currentAmmo;
        private string currentWeaponId;
        private string currentAmmoId;
        private int currentAmmoCount;
        private int initialAmmoCount;
        private float nextUiRefresh;
        private float lastAmmoWarningTime;
        private Coroutine drawRoutine;
        private int attackCooldownTicks;
        private bool definitionsLoaded;
        private bool subscribedToTicker;

        private const int AmmoPerShot = 1;
        private const float WarningCooldownSeconds = 1.5f;

        /// <summary>
        /// Called by <see cref="CombatController.Awake"/> so the ranged system can share runtime state.
        /// </summary>
        public void BindCombatController(CombatController controller)
        {
            combatController = controller;
        }

        private void Awake()
        {
            combatController = combatController != null ? combatController : GetComponent<CombatController>();
            inventory = GetComponent<Inventory.Inventory>() ?? GetComponentInParent<Inventory.Inventory>() ?? GetComponentInChildren<Inventory.Inventory>();
            equipmentComponent = GetComponent<Equipment>() ?? GetComponentInParent<Equipment>() ?? GetComponentInChildren<Equipment>();

            if (projectileSpawnPoint == null)
                projectileSpawnPoint = transform;

            if (groundItemSpawner == null)
                groundItemSpawner = FindObjectOfType<GroundItemSpawner>();

            EnsureDatabasesLoaded();
            RefreshEquipmentState(true);
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

            int effectiveAttack = CombatMath.GetEffectiveRanged(attacker.RangedLevel, attacker.Style);
            int attackBonus = Mathf.Max(0, Mathf.RoundToInt(attacker.Equip.range * accuracyMultiplier));
            int attackRoll = CombatMath.GetAttackRoll(effectiveAttack, attackBonus);

            int defenderEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            int defenderBonus = defender.Equip.rangeDef;
            int defenceRoll = CombatMath.GetDefenceRoll(defenderEff, defenderBonus);
            float chanceToHit = CombatMath.ChanceToHit(attackRoll, defenceRoll);
            bool hit = UnityEngine.Random.value < chanceToHit;

            int effectiveStrength = CombatMath.GetEffectiveRangedStrength(attacker.RangedLevel, attacker.Style);
            int strengthBonus = Mathf.Max(0, attacker.Equip.range);
            int maxHit = CombatMath.GetMaxHit(effectiveStrength, strengthBonus);
            maxHit = Mathf.RoundToInt(maxHit * Mathf.Max(0f, damageMultiplier));
            if (maxHit < 0)
                maxHit = 0;

            int damage = hit ? CombatMath.RollDamage(maxHit) : 0;

            var context = new RangedAttackContext
            {
                combatController = combatController,
                attacker = attacker,
                target = target,
                weapon = currentWeapon,
                ammunition = ammo,
                origin = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position,
                targetPosition = target != null ? target.transform.position : transform.position,
                damageResult = new CombatController.DamageResult { damage = damage, hit = hit, maxHit = maxHit },
                ammoConsumed = false
            };

            ShotPrepared?.Invoke(context);
            currentWeapon.specialEffect?.OnShotPrepared(context);
            ammo?.specialEffect?.OnShotPrepared(context);

            if (attackCooldownTicks <= 0)
                attackCooldownTicks = currentWeapon.attackSpeedTicks;

            bool consumeAmmo = ammoConsumptionMultiplier > 0f && (ammo == null || !ammo.infinite);
            int remaining;
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

            if (context.weapon != null && context.weapon.impactVfxPrefab != null)
            {
                Vector3 vfxPos = context.target != null ? context.target.transform.position : context.targetPosition;
                Instantiate(context.weapon.impactVfxPrefab, vfxPos, Quaternion.identity);
            }

            ApplyRangedDamage(context);
            TryHandleAmmoRecovery(context, context.damageResult.hit);
            ShotResolved?.Invoke(context);
            context.weapon?.specialEffect?.OnImpactResolved(context);
            context.ammunition?.specialEffect?.OnImpactResolved(context);
        }

        private IEnumerator FireAfterDraw(RangedAttackContext context)
        {
            float drawSeconds = Mathf.Max(0f, currentWeapon != null ? currentWeapon.drawTicks * CombatMath.TICK_SECONDS : 0f);
            if (drawSeconds > 0f)
                yield return new WaitForSeconds(drawSeconds);

            LaunchProjectile(context);
            drawRoutine = null;
        }

        private void LaunchProjectile(RangedAttackContext context)
        {
            var prefab = currentWeapon != null ? currentWeapon.projectilePrefab : null;
            if (prefab == null)
            {
                ShotFired?.Invoke(context);
                context.weapon?.specialEffect?.OnProjectileLaunched(context);
                context.ammunition?.specialEffect?.OnProjectileLaunched(context);
                if (context.weapon != null && !string.IsNullOrEmpty(context.weapon.releaseSoundId))
                    SoundManager.Instance?.PlaySfxByFileName(context.weapon.releaseSoundId);

                ApplyRangedDamage(context);
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

            float projectileSpeed = context.weapon != null ? context.weapon.projectileSpeed : currentWeapon != null ? currentWeapon.projectileSpeed : 10f;
            projectile.Initialise(this, context.target, context, projectileSpeed);
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

        private void HandleNoAmmo()
        {
            if (equipmentComponent != null)
                equipmentComponent.OverrideAmmoLabel(ammoEmptyLabel, ammoDepletedColor);

            if (Time.unscaledTime - lastAmmoWarningTime >= WarningCooldownSeconds)
            {
                FloatingText.Show("You have run out of ammo!", transform.position + Vector3.up * 0.5f, Color.white);
                if (!string.IsNullOrEmpty(ammoDepletedSoundId))
                    SoundManager.Instance?.PlaySfxByFileName(ammoDepletedSoundId);
                lastAmmoWarningTime = Time.unscaledTime;
            }
        }

        private bool HasRequiredAmmo(RangedWeaponData weapon, AmmunitionData ammo)
        {
            if (weapon == null)
                return false;

            if (weapon.consumesWeaponStack)
                return currentAmmoCount > 0;

            if (ammo != null && ammo.infinite)
                return true;

            return currentAmmoCount > 0;
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
            if (force || !string.Equals(currentWeaponId, weaponId, StringComparison.Ordinal))
            {
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
            if (force || !string.Equals(currentAmmoId, ammoId, StringComparison.Ordinal))
            {
                currentAmmoId = ammoId;
            currentAmmo = !string.IsNullOrEmpty(ammoId) ? ResolveAmmo(ammoId) : null;
            if (currentAmmo == null && !string.IsNullOrEmpty(ammoId) && currentWeapon != null && !currentWeapon.consumesWeaponStack)
                Log($"No AmmunitionData found for '{ammoId}'.");
            initialAmmoCount = ammoEntry.count;
        }

        currentAmmoCount = ammoEntry.count;
        if (currentAmmoCount > initialAmmoCount)
            initialAmmoCount = currentAmmoCount;
        if (currentWeapon != null && !currentWeapon.consumesWeaponStack && currentAmmo != null && currentAmmo.category != currentWeapon.ammunitionCategory)
        {
            Debug.LogWarning($"Ammunition '{currentAmmo.name}' is not compatible with weapon '{currentWeapon.name}'. Expected {currentWeapon.ammunitionCategory} but received {currentAmmo.category}.");
            currentAmmo = null;
            currentAmmoId = null;
            currentAmmoCount = 0;
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
