using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Audio;
using EquipmentSystem;
using Skills;
using Player;
using NPC;
using Pets;
using UI;
using Magic;
using Status;
using Status.Freeze;

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

        private SkillManager skills;
        private PlayerHitpoints hitpoints;
        private EquipmentAggregator equipment;
        private Inventory.Equipment equipmentComponent;
        private Player.PlayerCombatLoadout loadout;
        private PlayerCombatBinder combatBinder;
        private PlayerMover mover;
        private Coroutine attackRoutine;
        private CombatTarget currentTarget;
        private float nextAttackTime;

        private CombatStyle pendingStyle;
        private DamageType pendingType;
        private int pendingMaxHit;
        private SpellElement pendingElement;
        private SpellDefinition pendingSpell;
        private bool pendingSpellHit;

        [SerializeField, Tooltip("Centralised hitsplat sprite references assigned via the inspector.")]
        private HitSplatLibrary hitSplatLibrary;

        private Sprite damageHitsplat;
        private Sprite zeroHitsplat;
        private Sprite maxHitHitsplat;
        private IReadOnlyDictionary<SpellElement, Sprite> elementHitsplats;

        private void Awake()
        {
            // Grab required components from this object, falling back to parent/children so the
            // controller still works if supporting components live elsewhere in the hierarchy.
            skills = GetComponent<SkillManager>() ?? GetComponentInParent<SkillManager>() ?? GetComponentInChildren<SkillManager>();
            hitpoints = GetComponent<PlayerHitpoints>() ?? GetComponentInParent<PlayerHitpoints>() ?? GetComponentInChildren<PlayerHitpoints>();
            equipment = GetComponent<EquipmentAggregator>() ?? GetComponentInParent<EquipmentAggregator>() ?? GetComponentInChildren<EquipmentAggregator>();
            equipmentComponent = GetComponent<Inventory.Equipment>() ?? GetComponentInParent<Inventory.Equipment>() ?? GetComponentInChildren<Inventory.Equipment>();
            loadout = GetComponent<Player.PlayerCombatLoadout>() ?? GetComponentInParent<Player.PlayerCombatLoadout>() ?? GetComponentInChildren<Player.PlayerCombatLoadout>();
            combatBinder = GetComponent<PlayerCombatBinder>() ?? GetComponentInParent<PlayerCombatBinder>() ?? GetComponentInChildren<PlayerCombatBinder>();
            mover = GetComponent<PlayerMover>() ?? GetComponentInParent<PlayerMover>() ?? GetComponentInChildren<PlayerMover>();

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
            attackRoutine = StartCoroutine(AttackRoutine(target));
            if (PetDropSystem.GuardModeEnabled)
            {
                var pet = PetDropSystem.ActivePetCombat;
                pet?.CommandAttack(target);
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
            var defender = GetDefenderStats(target, attacker);

            int attEff = attacker.DamageType == DamageType.Magic
                ? CombatMath.GetEffectiveAttack(attacker.MagicLevel, CombatStyle.Accurate)
                : CombatMath.GetEffectiveAttack(attacker.AttackLevel, attacker.Style);
            int defEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            int atkRoll = attacker.DamageType == DamageType.Magic
                ? CombatMath.GetAttackRoll(attEff, attacker.Equip.magic)
                : CombatMath.GetAttackRoll(attEff, attacker.Equip.attack);
            int defBonus = attacker.DamageType switch
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

        private CombatantStats GetDefenderStats(CombatTarget target, CombatantStats attacker)
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
                    DefenceLevel = 1,
                    MagicLevel = 1,
                    Equip = new EquipmentAggregator.CombinedStats(),
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

        private int ApplyDamageResult(CombatTarget target, int damage, bool hit, int maxHit, CombatStyle style, DamageType type, SpellElement element)
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
                if (finalDamage == 0)
                {
                    sprite = zeroHitsplat;
                    FloatingText.Show("0", target.transform.position, textColor, null, sprite);
                }
                else if (type == DamageType.Magic && elementHitsplats != null && elementHitsplats.TryGetValue(element, out var elemSprite) && elemSprite != null)
                {
                    sprite = elemSprite;
                    if (element == SpellElement.Air)
                        textColor = Color.black;
                    FloatingText.Show(finalDamage.ToString(), target.transform.position, textColor, null, sprite);
                }
                else
                {
                    sprite = finalDamage == maxHit ? maxHitHitsplat : damageHitsplat;
                    FloatingText.Show(finalDamage.ToString(), target.transform.position, textColor, null, sprite);
                }
                AwardXp(finalDamage, style, type);
                if (finalDamage > 0 && !target.IsAlive)
                    OnTargetKilled?.Invoke(target);
                Debug.Log($"Player dealt {finalDamage} damage to {targetName}.");
                var applier = GetComponentInChildren<OnHitPoisonApplier>();
                if (applier != null && targetMb != null)
                    applier.TryApply(targetMb.gameObject, finalDamage > 0);
                OnAttackLanded?.Invoke(finalDamage, hit);
            }
            else
            {
                FloatingText.Show("0", target.transform.position, Color.white, null, zeroHitsplat);
                Debug.Log($"Player missed {targetName}.");
                OnAttackLanded?.Invoke(0, false);
            }

            return finalDamage;
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

            pendingSpell = null;
            pendingSpellHit = false;

            var result = CalculateDamage(attacker, target);
            var activeSpell = MagicUI.ActiveSpell;

            if (attacker.DamageType == DamageType.Magic)
            {
                if (activeSpell != null && activeSpell.projectilePrefab != null)
                {
                    pendingStyle = attacker.Style;
                    pendingType = attacker.DamageType;
                    pendingMaxHit = result.maxHit;
                    pendingElement = activeSpell.element;
                    pendingSpell = activeSpell;
                    pendingSpellHit = result.hit;

                    var projObj = Instantiate(activeSpell.projectilePrefab, transform.position, Quaternion.identity);
                    var proj = projObj.GetComponent<Magic.FireProjectile>();
                    if (proj != null)
                    {
                        proj.target = target;
                        proj.damage = result.damage;
                        proj.maxHit = result.maxHit;
                        proj.owner = this;
                        proj.style = attacker.Style;
                        proj.damageType = attacker.DamageType;
                        proj.speed = activeSpell.speed;
                        proj.hitFadeTime = activeSpell.hitFadeTime;
                        if (activeSpell.hitEffectPrefab != null)
                            proj.hitEffectPrefab = activeSpell.hitEffectPrefab;
                    }
                }
                else
                {
                    SpellElement element = activeSpell != null ? activeSpell.element : SpellElement.None;
                    int primaryDamage = ApplyDamageResult(target, result.damage, result.hit, result.maxHit, attacker.Style, attacker.DamageType, element);
                    if (activeSpell != null)
                        TryApplySpellStatusEffects(target, activeSpell, result.hit);
                }
            }
            else
            {
                int primaryDamage = ApplyDamageResult(target, result.damage, result.hit, result.maxHit, attacker.Style, attacker.DamageType, SpellElement.None);
                ApplyHalberdAoe(attacker, target, primaryDamage, result.maxHit);
            }
        }

        public void ApplySpellDamage(CombatTarget target, int damage)
        {
            bool hit = pendingSpellHit;
            int resolvedDamage = hit ? Mathf.Max(0, damage) : 0;
            var spell = pendingSpell;

            ApplyDamageResult(target, resolvedDamage, hit, pendingMaxHit, pendingStyle, pendingType, pendingElement);

            if (hit && spell != null)
                TryApplySpellStatusEffects(target, spell, hit);

            pendingSpell = null;
            pendingSpellHit = false;
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
            Vector2 forward = FacingDirToVector(mover != null ? mover.FacingDir : 0);
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

        private static Vector2 FacingDirToVector(int facingDir)
        {
            switch (facingDir)
            {
                case 1:
                    return Vector2.left;
                case 2:
                    return Vector2.right;
                case 3:
                    return Vector2.up;
                default:
                    return Vector2.down;
            }
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
            public int ApplyDamage(int amount, DamageType type, SpellElement element, object source)
            {
                Debug.Log($"Dummy took {amount} damage");
                return amount;
            }
        }
    }
}
