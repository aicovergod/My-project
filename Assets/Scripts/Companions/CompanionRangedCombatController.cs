using System;
using System.Collections;
using System.Collections.Generic;
using Audio;
using Combat;
using EquipmentSystem;
using Combat.Ranged;
using Inventory;
using MyGame.Drops;
using NPC;
using Pets;
using UI;
using UnityEngine;
using Util;

namespace Companions
{
    /// <summary>
    /// Companion-specific ranged controller that reuses the player projectile pipeline while sourcing
    /// equipment and inventory data from the companion wrappers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionRangedCombatController : MonoBehaviour, ITickable, IRangedProjectileOwner
    {
        [Header("Data Sources")]
        [SerializeField]
        [Tooltip("Explicit ranged weapon definitions for companions. Resources are auto-loaded in addition to this list.")]
        private RangedWeaponData[] weaponDefinitions;

        [SerializeField]
        [Tooltip("Explicit ammunition definitions for companions. Resources are auto-loaded in addition to this list.")]
        private AmmunitionData[] ammunitionDefinitions;

        [SerializeField]
        [Tooltip("Resources.LoadAll path searched for weapon definitions when entering play mode.")]
        private string weaponResourcesPath = "Combat/Ranged/Weapons";

        [SerializeField]
        [Tooltip("Resources.LoadAll path searched for ammunition definitions when entering play mode.")]
        private string ammoResourcesPath = "Combat/Ranged/Ammunition";

        [Header("References")]
        [SerializeField]
        [Tooltip("Projectile spawn transform. Falls back to the companion root when omitted.")]
        private Transform projectileSpawnPoint;

        [SerializeField]
        [Tooltip("Spawner used when reclaimed ammo cannot be stored in the companion inventory.")]
        private GroundItemSpawner groundItemSpawner;

        [SerializeField]
        [Tooltip("Anchor used for floating text warnings (out of ammo, etc.). Defaults to the companion root.")]
        private Transform floatingTextAnchor;

        [Header("UI Feedback")]
        [SerializeField]
        private Color ammoLowColor = new(1f, 0.85f, 0.2f);

        [SerializeField]
        private Color ammoDepletedColor = new(1f, 0.35f, 0.35f);

        [SerializeField]
        private string ammoLowLabel = "LOW";

        [SerializeField]
        private string ammoEmptyLabel = "OUT";

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Percentage of the original stack that should trigger the low-ammo warning.")]
        private float ammoWarningThreshold = 0.1f;

        [SerializeField]
        [Tooltip("Minimum seconds between automatic ammo UI refreshes.")]
        private float ammoUiRefreshInterval = 0.25f;

        [Header("Audio")]
        [SerializeField]
        [Tooltip("Sound played when the companion attempts to fire without ammo.")]
        private string ammoDepletedSoundId = "ui_no_ammo";

        [Header("Debugging")]
        [SerializeField]
        private bool enableDebugLogging;

        /// <summary>Raised when the draw animation should begin.</summary>
        public event Action<RangedAttackContext> ShotPrepared;

        /// <summary>Raised when the projectile is spawned.</summary>
        public event Action<RangedAttackContext> ShotFired;

        /// <summary>Raised when the projectile lands and damage has been resolved.</summary>
        public event Action<RangedAttackContext> ShotResolved;

        private sealed class PendingShot
        {
            public Transform ownerTransform;
            public int beastmasterLevel;
        }

        private readonly Dictionary<string, RangedWeaponData> weaponLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AmmunitionData> ammoLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<IRangedStatModifierProvider> modifierBuffer = new();
        private readonly Dictionary<RangedProjectile, PendingShot> activeProjectiles = new();

        private CompanionEquipment equipment;
        private CompanionInventory inventory;
        private PetCombatController ownerCombatController;

        private RangedWeaponData currentWeapon;
        private AmmunitionData currentAmmo;
        private string currentWeaponId;
        private string currentAmmoId;
        private string lastRejectedWeaponId;
        private string lastRejectedAmmoId;
        private int currentAmmoCount;
        private int initialAmmoCount;
        private float nextUiRefresh;
        private float lastAmmoWarningTime;
        private Coroutine drawRoutine;
        private int attackCooldownTicks;
        private bool subscribedToTicker;
        private bool definitionsLoaded;
        private bool initialised;

        private const int AmmoPerShot = 1;
        private const float WarningCooldownSeconds = 1.5f;

        /// <summary>
        /// Initialises the ranged adapter with references to the companion wrappers and projectile helpers.
        /// </summary>
        public void Initialise(
            PetCombatController owner,
            CompanionEquipment companionEquipment,
            CompanionInventory companionInventory,
            Transform textAnchor,
            GroundItemSpawner itemSpawner)
        {
            ownerCombatController = owner;
            equipment = companionEquipment;
            inventory = companionInventory;
            floatingTextAnchor = textAnchor != null ? textAnchor : transform;
            groundItemSpawner = itemSpawner != null ? itemSpawner : groundItemSpawner;
            if (projectileSpawnPoint == null)
                projectileSpawnPoint = transform;

            if (equipment != null)
                equipment.EquipmentSlotChanged += HandleEquipmentSlotChanged;

            EnsureDatabasesLoaded();
            RefreshEquipmentState(true);
            initialised = true;
            TrySubscribeToTicker();
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

        private void OnDestroy()
        {
            if (equipment != null)
                equipment.EquipmentSlotChanged -= HandleEquipmentSlotChanged;
            equipment = null;
            inventory = null;
            ownerCombatController = null;
        }

        private void Update()
        {
            if (!initialised)
                return;

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
        /// Attempts to resolve a ranged attack using the provided attacker/target context.
        /// </summary>
        /// <returns>True when the shot was successfully prepared. Outputs the cooldown ticks used.</returns>
        public bool TryResolveAttack(
            CombatantStats attacker,
            CombatTarget target,
            Transform ownerTransform,
            int beastmasterLevel,
            out int cooldownTicks)
        {
            cooldownTicks = Mathf.Max(1, attacker?.Equip.attackSpeedTicks ?? 1);
            if (!initialised || attacker == null || target == null)
                return false;

            RefreshEquipmentState(false);
            if (currentWeapon == null)
            {
                Log("TryResolveAttack aborted: no ranged weapon definition found.");
                return false;
            }

            var ammo = currentAmmo;
            if (!HasRequiredAmmo(currentWeapon, ammo))
            {
                HandleNoAmmo();
                return false;
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

            var defender = ResolveDefenderStats(target, attacker);

            int effectiveAttack = CombatMath.GetEffectiveRanged(attacker.RangedLevel, attacker.Style);
            int attackBonus = Mathf.RoundToInt(attacker.Equip.range * accuracyMultiplier);
            int attackRoll = CombatMath.GetAttackRoll(effectiveAttack, attackBonus);

            int defenderEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            int defenderBonus = defender.Equip.rangeDef;
            int defenceRoll = CombatMath.GetDefenceRoll(defenderEff, defenderBonus);
            float chanceToHit = CombatMath.ChanceToHit(attackRoll, defenceRoll);
            bool hit = UnityEngine.Random.value < chanceToHit;

            int effectiveStrength = CombatMath.GetEffectiveRangedStrength(attacker.RangedLevel, attacker.Style);
            int strengthBonus = Mathf.Max(0, attacker.Equip.rangeStrength);
            int maxHit = CombatMath.GetMaxHit(effectiveStrength, strengthBonus);
            maxHit = Mathf.RoundToInt(maxHit * Mathf.Max(0f, damageMultiplier));
            if (maxHit < 0)
                maxHit = 0;

            if (ownerCombatController != null && ownerCombatController.definition != null)
            {
                float bonusPerLevel = ownerCombatController.definition.maxHitPerBeastmasterLevel;
                if (Mathf.Abs(bonusPerLevel) > Mathf.Epsilon)
                    maxHit = Mathf.RoundToInt(maxHit * (1f + bonusPerLevel * beastmasterLevel));
            }

            int damage = hit ? CombatMath.RollDamage(maxHit) : 0;

            var context = new RangedAttackContext
            {
                rangedController = null,
                attacker = attacker,
                target = target,
                weaponId = currentWeapon != null ? currentWeapon.WeaponId : null,
                weapon = currentWeapon,
                ammunition = ammo,
                origin = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position,
                targetPosition = target.transform.position,
                damageResult = new CombatController.DamageResult
                {
                    damage = damage,
                    hit = hit,
                    maxHit = maxHit
                },
                ammoConsumed = false,
                finalAccuracyMultiplier = accuracyMultiplier,
                finalDamageMultiplier = damageMultiplier
            };

            ShotPrepared?.Invoke(context);
            currentWeapon.specialEffect?.OnShotPrepared(context);
            ammo?.specialEffect?.OnShotPrepared(context);

            attackCooldownTicks = Mathf.Max(1, currentWeapon.attackSpeedTicks);
            cooldownTicks = attackCooldownTicks;

            bool consumeAmmo = ammoConsumptionMultiplier > 0f && (ammo == null || !ammo.infinite);
            EquipmentSlot ammoSlot = ResolveAmmoSlot(currentWeapon);
            int remaining = currentAmmoCount;
            if (consumeAmmo && !ConsumeAmmo(ammoSlot, out remaining))
            {
                HandleNoAmmo();
                return false;
            }

            if (consumeAmmo)
            {
                context.ammoConsumed = true;
                currentAmmoCount = remaining;
                if (currentAmmoCount > initialAmmoCount)
                    initialAmmoCount = currentAmmoCount;
                UpdateAmmoUi();
            }

            if (drawRoutine != null)
            {
                StopCoroutine(drawRoutine);
                drawRoutine = null;
            }

            drawRoutine = StartCoroutine(FireAfterDraw(context, ownerTransform, beastmasterLevel));
            return true;
        }

        public void OnTick()
        {
            if (attackCooldownTicks > 0)
                attackCooldownTicks--;
        }

        /// <inheritdoc />
        public void HandleProjectileImpact(RangedAttackContext context, RangedProjectile projectile)
        {
            if (projectile != null)
                Destroy(projectile.gameObject);

            PendingShot metadata = null;
            if (projectile != null)
            {
                activeProjectiles.TryGetValue(projectile, out metadata);
                activeProjectiles.Remove(projectile);
            }

            ResolveImpact(context, metadata);
        }

        private IEnumerator FireAfterDraw(RangedAttackContext context, Transform ownerTransform, int beastmasterLevel)
        {
            RangedAttackContext resolvedContext = EnsureContextWeapon(context);

            float drawSeconds = 0f;
            if (resolvedContext.weapon != null)
                drawSeconds = Mathf.Max(0f, resolvedContext.weapon.drawTicks * CombatMath.TICK_SECONDS);
            if (drawSeconds > 0f)
                yield return new WaitForSeconds(drawSeconds);

            LaunchProjectile(resolvedContext, ownerTransform, beastmasterLevel);
            drawRoutine = null;
        }

        private void LaunchProjectile(RangedAttackContext context, Transform ownerTransform, int beastmasterLevel)
        {
            context = EnsureContextWeapon(context);
            var weaponReference = context.weapon;
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

                ResolveImpact(context, new PendingShot { ownerTransform = ownerTransform, beastmasterLevel = beastmasterLevel });
                return;
            }

            Vector3 spawnPos = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;
            var instance = Instantiate(prefab, spawnPos, Quaternion.identity);
            var projectile = instance.GetComponent<RangedProjectile>();
            if (projectile == null)
            {
                Debug.LogError($"Projectile prefab '{prefab.name}' is missing a RangedProjectile component.", prefab);
                Destroy(instance);
                ResolveImpact(context, new PendingShot { ownerTransform = ownerTransform, beastmasterLevel = beastmasterLevel });
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

            activeProjectiles[projectile] = new PendingShot
            {
                ownerTransform = ownerTransform,
                beastmasterLevel = beastmasterLevel
            };

            projectile.Initialise(this, context.target, context, projectileSpeed);
        }

        private void ResolveImpact(RangedAttackContext context, PendingShot metadata)
        {
            context = EnsureContextWeapon(context);

            if (context.weapon != null && context.weapon.impactVfxPrefab != null)
            {
                Vector3 vfxPosition = context.target != null ? context.target.transform.position : context.targetPosition;
                Instantiate(context.weapon.impactVfxPrefab, vfxPosition, Quaternion.identity);
            }

            ownerCombatController?.ApplyDamageResult(
                context.target,
                context.attacker,
                context.damageResult,
                metadata != null ? metadata.ownerTransform : null,
                metadata != null ? metadata.beastmasterLevel : 0);

            TryHandleAmmoRecovery(context, context.damageResult.hit);

            ShotResolved?.Invoke(context);
            context.weapon?.specialEffect?.OnImpactResolved(context);
            context.ammunition?.specialEffect?.OnImpactResolved(context);
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

            Vector3 spawnPos = context.target != null ? context.target.transform.position : context.targetPosition;
            bool handled = false;
            if (equipment != null)
                handled = equipment.TryStoreRecoveredAmmo(item, 1, inventory, groundItemSpawner, spawnPos, spawnAsGroundItem);

            if (!handled && inventory != null && inventory.InventoryComponent != null)
                handled = inventory.InventoryComponent.AddItem(item, 1);

            if (!handled && spawnAsGroundItem && groundItemSpawner != null)
                groundItemSpawner.Spawn(item, 1, spawnPos);
        }

        private void CacheModifierProviders()
        {
            modifierBuffer.Clear();
            GetComponents(modifierBuffer);
        }

        private void HandleEquipmentSlotChanged(EquipmentSlot slot, InventoryEntry entry)
        {
            bool force = slot == EquipmentSlot.Weapon;
            RefreshEquipmentState(force);
        }

        private void RefreshEquipmentState(bool force)
        {
            if (equipment == null)
                return;

            var weaponEntry = equipment.GetEquipped(EquipmentSlot.Weapon);
            string weaponId = weaponEntry.item != null
                ? (!string.IsNullOrEmpty(weaponEntry.item.id) ? weaponEntry.item.id : weaponEntry.item.name)
                : null;
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
                if (currentWeapon == null)
                {
                    currentAmmo = null;
                    currentAmmoId = null;
                    currentAmmoCount = 0;
                    equipment.OverrideAmmoLabel(null);
                }
            }

            InventoryEntry ammoEntry;
            if (currentWeapon != null && currentWeapon.consumesWeaponStack)
            {
                ammoEntry = weaponEntry;
            }
            else
            {
                ammoEntry = equipment.GetEquipped(EquipmentSlot.Arrow);
            }

            string ammoId = ammoEntry.item != null
                ? (!string.IsNullOrEmpty(ammoEntry.item.id) ? ammoEntry.item.id : ammoEntry.item.name)
                : null;
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
            UpdateAmmoUi();
        }

        private void UpdateAmmoUi()
        {
            if (equipment == null)
                return;

            if (currentAmmoCount <= 0)
            {
                equipment.OverrideAmmoLabel(ammoEmptyLabel, ammoDepletedColor);
                return;
            }

            float ratio = initialAmmoCount > 0 ? currentAmmoCount / (float)initialAmmoCount : 1f;
            if (ratio <= ammoWarningThreshold)
            {
                equipment.OverrideAmmoLabel(ammoLowLabel, ammoLowColor);
            }
            else
            {
                equipment.OverrideAmmoLabel(null);
            }
        }

        private EquipmentSlot ResolveAmmoSlot(RangedWeaponData weapon)
        {
            return weapon != null && weapon.consumesWeaponStack
                ? EquipmentSlot.Weapon
                : EquipmentSlot.Arrow;
        }

        private bool ConsumeAmmo(EquipmentSlot slot, out int remaining)
        {
            remaining = currentAmmoCount;
            if (equipment == null)
                return false;

            if (!equipment.ConsumeEquipped(slot, AmmoPerShot, out remaining))
                return false;

            // Re-query the equipped stack to ensure the local cache mirrors the actual equipment state.
            remaining = equipment.GetEquipped(slot).count;
            return true;
        }

        private void TryHandleMissingAmmoRestriction(RangedWeaponData weapon, AmmunitionData ammo)
        {
            if (weapon == null || ammo == null)
                return;

            if (currentAmmo == ammo)
            {
                RejectCurrentAmmo(
                    $"Ammunition '{ammo.name}' is blocked by '{weapon.name}' restriction data.",
                    "Your weapon cannot use that ammo.");
            }
        }

        private bool HasRequiredAmmo(RangedWeaponData weapon, AmmunitionData ammo)
        {
            if (weapon == null)
                return false;

            if (ammo != null && !weapon.IsAmmoAllowed(ammo))
            {
                TryHandleMissingAmmoRestriction(weapon, ammo);
                return false;
            }

            if (weapon.consumesWeaponStack)
                return currentAmmoCount > 0;

            if (ammo != null && ammo.infinite)
                return true;

            return currentAmmoCount > 0;
        }

        private void HandleNoAmmo()
        {
            equipment?.OverrideAmmoLabel(ammoEmptyLabel, ammoDepletedColor);
            ShowAmmoWarning("I'm out of ammo!", true);
        }

        private void ShowAmmoWarning(string message, bool playDepletedSound)
        {
            if (string.IsNullOrEmpty(message))
                return;

            if (Time.unscaledTime - lastAmmoWarningTime < WarningCooldownSeconds)
                return;

            var anchor = floatingTextAnchor != null ? floatingTextAnchor.position : transform.position;
            FloatingText.Show(message, anchor, Color.white);
            if (playDepletedSound && !string.IsNullOrEmpty(ammoDepletedSoundId))
                SoundManager.Instance?.PlaySfxByFileName(ammoDepletedSoundId);
            lastAmmoWarningTime = Time.unscaledTime;
        }

        private void RejectCurrentAmmo(string debugMessage, string playerMessage, bool playSound = false)
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

            equipment?.OverrideAmmoLabel(ammoEmptyLabel, ammoDepletedColor);
            ShowAmmoWarning(playerMessage, playSound);
            UpdateAmmoUi();
        }

        private RangedAttackContext EnsureContextWeapon(RangedAttackContext context)
        {
            if (context.weapon != null || string.IsNullOrEmpty(context.weaponId))
                return context;

            context.weapon = ResolveWeaponForContext(context.weaponId);
            return context;
        }

        private RangedWeaponData ResolveWeaponForContext(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId))
                return null;

            if (!string.IsNullOrEmpty(currentWeaponId) && string.Equals(currentWeaponId, weaponId, StringComparison.Ordinal))
                return currentWeapon ?? ResolveWeapon(weaponId);

            return ResolveWeapon(weaponId);
        }

        private CombatantStats ResolveDefenderStats(CombatTarget target, CombatantStats attacker)
        {
            if (target is NpcCombatant npcCombatant)
                return npcCombatant.GetCombatantStats();

            return new CombatantStats
            {
                AttackLevel = 1,
                StrengthLevel = 1,
                DefenceLevel = 1,
                Equip = new EquipmentAggregator.CombinedStats { rangeStrength = 0 },
                Style = CombatStyle.Defensive,
                DamageType = target != null ? target.PreferredDefenceType : DamageType.Melee
            };
        }

        private void EnsureDatabasesLoaded()
        {
            if (definitionsLoaded)
                return;

            definitionsLoaded = true;
            CacheWeapons(weaponDefinitions);
            CacheWeapons(Resources.LoadAll<RangedWeaponData>(weaponResourcesPath));
            CacheAmmo(ammunitionDefinitions);
            CacheAmmo(Resources.LoadAll<AmmunitionData>(ammoResourcesPath));
        }

        private void CacheWeapons(IEnumerable<RangedWeaponData> definitions)
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

                weaponLookup[id] = def;
            }
        }

        private void CacheAmmo(IEnumerable<AmmunitionData> definitions)
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

                ammoLookup[id] = def;
            }
        }

        private RangedWeaponData ResolveWeapon(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId))
                return null;

            if (weaponLookup.TryGetValue(weaponId, out var definition))
                return definition;

            return null;
        }

        private AmmunitionData ResolveAmmo(string ammoId)
        {
            if (string.IsNullOrEmpty(ammoId))
                return null;

            if (ammoLookup.TryGetValue(ammoId, out var definition))
                return definition;

            return null;
        }

        private void Log(string message)
        {
            if (enableDebugLogging)
                Debug.Log($"[Companion Ranged] {message}", this);
        }
    }
}
