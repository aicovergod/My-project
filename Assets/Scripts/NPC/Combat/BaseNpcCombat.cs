using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Combat;
using EquipmentSystem;
using Skills;
using Player;
using Pets;
using Util;

namespace NPC
{
    /// <summary>
    /// Shared combat behaviour for NPCs, handling aggro, targeting and basic attacks.
    /// </summary>
    [RequireComponent(typeof(NpcCombatant), typeof(NpcFacing))]
    public abstract class BaseNpcCombat : MonoBehaviour
    {
        protected NpcCombatant combatant;
        protected NpcWanderer wanderer;
        protected PlayerCombatTarget playerTarget;
        protected bool hasHitPlayer;
        protected Vector2 spawnPosition;
        public Vector2 SpawnPosition => spawnPosition;
        protected NpcFacing npcFacing;
        protected Coroutine spriteSwapRoutine;

        protected readonly Dictionary<CombatTarget, float> threatLevels = new();
        protected readonly Dictionary<CombatTarget, Coroutine> activeAttacks = new();
        // Tracks the last time each target dealt damage to this NPC.
        protected readonly Dictionary<CombatTarget, float> lastDamageTimes = new();

        /// <summary>
        /// Shared timestamp that gates how quickly this NPC can execute successive
        /// attacks regardless of how many simultaneous targets they are facing.
        /// </summary>
        protected float nextAttackTimestamp;

        private static readonly string[] DefaultObstructionLayers = { "Obstacles", "Obstacle", "Physical Objects" };

        [Header("Line of Sight")]
        [SerializeField, Tooltip("Layers treated as solid when determining whether attacks can reach a target.")]
        protected LayerMask obstructionMask;

        private bool inCombat;
        public bool InCombat => inCombat;
        public event System.Action<bool> OnCombatStateChanged;

        private void SetCombatState(bool state)
        {
            if (inCombat == state)
                return;
            inCombat = state;
            OnCombatStateChanged?.Invoke(inCombat);
        }

        protected virtual void Awake()
        {
            EnsureObstructionMaskConfigured();
            combatant = GetComponent<NpcCombatant>();
            wanderer = GetComponent<NpcWanderer>();
            playerTarget = FindObjectOfType<PlayerCombatTarget>();
            spawnPosition = transform.position;
            npcFacing = GetComponent<NpcFacing>();
            nextAttackTimestamp = Time.time;
        }

        protected virtual void OnValidate()
        {
            EnsureObstructionMaskConfigured();
        }

        /// <summary>
        /// Ensures the obstruction mask defaults to the expected obstacle layers when unset.
        /// </summary>
        private void EnsureObstructionMaskConfigured()
        {
            int defaultMask = LayerMask.GetMask(DefaultObstructionLayers);

            // Keep the NPC obstruction mask aligned with the shared defaults while preserving
            // any bespoke overrides applied in prefabs or instances.
            int combinedMask = obstructionMask.value | defaultMask;

            // In play mode we also honour the active navigation grid so line-of-sight checks use
            // the same blockers that pathfinding respects. Guard all lookups to stay editor-safe.
            if (Application.isPlaying)
            {
                var blockingMask = PathfindingService.Instance?.ActiveGrid?.BlockingLayerMask;
                if (blockingMask.HasValue)
                {
                    combinedMask |= blockingMask.Value.value;
                }
            }
            obstructionMask = combinedMask;
        }

        public virtual void ResetCombatState(bool resetSpawnPosition = false)
        {
            foreach (var routine in activeAttacks.Values)
            {
                if (routine != null)
                    StopCoroutine(routine);
            }
            activeAttacks.Clear();
            threatLevels.Clear();
            lastDamageTimes.Clear();
            hasHitPlayer = false;
            nextAttackTimestamp = Time.time;
            combatant?.ClearDamageContributors("ResetCombatState invoked");
            if (resetSpawnPosition)
                spawnPosition = transform.position;
            wanderer?.ExitCombat();
            SetCombatState(false);
        }

        public void AddThreat(CombatTarget target, float amount)
        {
            if (target == null)
                return;
            if (target == playerTarget)
            {
                var profile = combatant.Profile;
                amount *= profile != null ? profile.PlayerAggroWeight : 1f;
            }
            if (threatLevels.ContainsKey(target))
                threatLevels[target] += amount;
            else
                threatLevels[target] = amount;
        }

        /// <summary>
        /// Record that <paramref name="attacker"/> dealt damage to this NPC.
        /// </summary>
        public void RecordDamageFrom(CombatTarget attacker)
        {
            if (attacker == null)
                return;
            lastDamageTimes[attacker] = Time.time;
        }

        /// <summary>
        /// Determines whether any player-controlled combatant has contributed damage within the
        /// supplied time window.
        /// </summary>
        /// <param name="windowSeconds">Time window in seconds. When zero or negative, any recorded
        /// contribution counts regardless of age.</param>
        /// <param name="secondsSinceMostRecent">Outputs the number of seconds since the most recent
        /// qualifying contribution when one is found. Undefined when no contributors are present.</param>
        public bool HasRecentPlayerContribution(float windowSeconds, out float secondsSinceMostRecent)
        {
            secondsSinceMostRecent = float.PositiveInfinity;
            if (lastDamageTimes.Count == 0)
                return false;

            float now = Time.time;
            float allowedWindow = windowSeconds > 0f ? windowSeconds : float.PositiveInfinity;
            bool found = false;

            foreach (var kvp in lastDamageTimes)
            {
                if (!IsPlayerControlled(kvp.Key))
                    continue;

                float elapsed = now - kvp.Value;
                if (elapsed > allowedWindow)
                    continue;

                if (!found || elapsed < secondsSinceMostRecent)
                    secondsSinceMostRecent = elapsed;
                found = true;
            }

            return found;
        }

        private static bool IsPlayerControlled(CombatTarget target)
        {
            return target is PlayerCombatTarget || target is PetCombatController;
        }

        protected virtual void Update()
        {
            if (!combatant.IsAlive)
                return;
            var profile = combatant.Profile;
            if (profile == null)
                return;

            float aggroRadius = wanderer != null ? wanderer.AggroRadius : 5f;
            bool hasThreat = threatLevels.Count > 0;
            var myFaction = combatant as IFactionProvider;

            if (!profile.IsAggressive && !hasThreat)
            {
                if (myFaction == null || !HasAggressiveFactionTargets(myFaction, aggroRadius))
                    return;
            }

            if (playerTarget == null)
                playerTarget = FindObjectOfType<PlayerCombatTarget>();
            foreach (var t in new List<CombatTarget>(threatLevels.Keys))
            {
                var unityTarget = t as Object;
                Transform targetTransform = null;
                bool unityMissing = unityTarget == null || !TryResolveTargetTransform(t, unityTarget, out targetTransform);

                bool remove = unityMissing;
                bool removedForAggroTimeout = false;
                if (!remove && !t.IsAlive)
                    remove = true;

                if (!remove && targetTransform != null)
                {
                    float dist = Vector2.Distance(targetTransform.position, transform.position);
                    if (dist > 15f)
                    {
                        wanderer?.ForceReturnToOrigin();
                        remove = true;
                    }
                    else if (profile != null && profile.AggroTimeoutSeconds > 0f)
                    {
                        float distanceFromSpawn = Vector2.Distance(targetTransform.position, spawnPosition);
                        float chaseRadius = wanderer != null ? wanderer.AggroRadius : 5f;
                        if (distanceFromSpawn > chaseRadius && lastDamageTimes.TryGetValue(t, out float lastDamage))
                        {
                            float elapsed = Time.time - lastDamage;
                            if (elapsed >= profile.AggroTimeoutSeconds)
                            {
                                remove = true;
                                removedForAggroTimeout = true;
                                if (combatant != null && combatant.LogDamage)
                                {
                                    string targetName = targetTransform != null ? targetTransform.name : "unknown";
                                    Debug.Log($"{name} removed threat {targetName} after {elapsed:F1}s outside chase radius (timeout {profile.AggroTimeoutSeconds:F1}s).", this);
                                }
                            }
                        }
                    }
                }

                if (!remove)
                    continue;

                threatLevels.Remove(t);
                lastDamageTimes.Remove(t);

                StopAndRemoveActiveAttack(t);

                // Always notify the wanderer that this combatant is no longer engaged, even when
                // the cached transform instance has been Unity-nullified. Passing the cached
                // reference allows the wanderer to purge any lingering slot keyed to the target.
                wanderer?.ExitCombat(targetTransform);
                if (targetTransform == null)
                    wanderer?.ExitCombat();

                RemoveCachedTargetFromDictionary(threatLevels, t);
                RemoveCachedTargetFromDictionary(lastDamageTimes, t);
                RemoveCachedTargetFromDictionary(activeAttacks, t);

                if (removedForAggroTimeout)
                    combatant?.ClearDamageContributors("Aggro timeout cleared credit");
            }

            if (threatLevels.Count == 0 && activeAttacks.Count == 0)
            {
                wanderer?.ForceReturnToOrigin();
                SetCombatState(false);
            }
            else if (activeAttacks.Count == 0)
            {
                SetCombatState(false);
            }

            var potentials = new List<CombatTarget>();

            if (playerTarget != null && playerTarget.IsAlive)
            {
                bool playerHasThreat = threatLevels.ContainsKey(playerTarget);
                if (profile.IsAggressive || playerHasThreat)
                {
                    float playerDist = Vector2.Distance(playerTarget.transform.position, spawnPosition);
                    if (playerDist <= aggroRadius)
                        potentials.Add(playerTarget);
                }
            }

            if (myFaction != null)
            {
                var activeCombatants = NpcCombatant.ActiveCombatants;
                for (int i = 0; i < activeCombatants.Count; i++)
                {
                    var npc = activeCombatants[i];
                    if (npc == null || npc == combatant)
                        continue;
                    if (!npc.isActiveAndEnabled || !npc.IsAlive)
                        continue;
                    var otherFaction = npc as IFactionProvider;
                    if (otherFaction == null || !myFaction.IsEnemy(otherFaction.Faction))
                        continue;
                    bool hasExistingThreat = threatLevels.ContainsKey(npc);
                    bool factionAggressive = FactionUtility.IsAggressiveTowardFaction(myFaction.Faction, otherFaction.Faction);
                    if (!profile.IsAggressive && !factionAggressive && !hasExistingThreat)
                        continue;
                    float dist = Vector2.Distance(npc.transform.position, spawnPosition);
                    if (dist <= aggroRadius)
                        potentials.Add(npc);
                }
            }

            foreach (var t in potentials)
            {
                float dist = Vector2.Distance(t.transform.position, transform.position);
                AddThreat(t, 1f / Mathf.Max(dist, 0.1f));
            }

            while (activeAttacks.Count < profile.MaxConcurrentTargets)
            {
                CombatTarget next = null;
                float best = float.MinValue;
                foreach (var kv in threatLevels)
                {
                    if (activeAttacks.ContainsKey(kv.Key))
                        continue;
                    if (kv.Value > best)
                    {
                        best = kv.Value;
                        next = kv.Key;
                    }
                }
                if (next == null)
                    break;
                BeginAttacking(next);
            }

            if (activeAttacks.Count > 0)
            {
                CombatTarget closest = null;
                Transform closestTransform = null;
                float bestDist = float.MaxValue;
                foreach (var t in new List<CombatTarget>(activeAttacks.Keys))
                {
                    var unityTarget = t as Object;
                    Transform targetTransform;
                    if (unityTarget == null || !TryResolveTargetTransform(t, unityTarget, out targetTransform))
                    {
                        StopAndRemoveActiveAttack(t);
                        RemoveCachedTargetFromDictionary(threatLevels, t);
                        RemoveCachedTargetFromDictionary(lastDamageTimes, t);
                        continue;
                    }

                    if (!t.IsAlive)
                        continue;

                    float dist = Vector2.Distance(targetTransform.position, transform.position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        closest = t;
                        closestTransform = targetTransform;
                    }
                }
                if (closestTransform != null)
                    npcFacing?.FaceTarget(closestTransform);
            }
        }

        public virtual void ReengageFromRetreat(CombatTarget target)
        {
            if (target == null)
                return;

            var unityTarget = target as Object;
            if (unityTarget == null)
            {
                CleanupDestroyedTargetReferences(target);
                return;
            }

            if (!TryResolveTargetTransform(target, unityTarget, out var targetTransform))
            {
                CleanupDestroyedTargetReferences(target);
                return;
            }
            spawnPosition = transform.position;
            wanderer?.SetOrigin(spawnPosition);
            wanderer?.EnterCombat(targetTransform);
            if (!activeAttacks.ContainsKey(target))
            {
                var routine = StartCoroutine(AttackRoutine(target));
                activeAttacks[target] = routine;
            }
            SetCombatState(true);
        }

        public virtual void BeginAttacking(CombatTarget target)
        {
            if (!combatant.IsAlive)
                return;
            if (target == null || activeAttacks.ContainsKey(target))
                return;

            var unityTarget = target as Object;
            if (unityTarget == null)
            {
                CleanupDestroyedTargetReferences(target);
                return;
            }

            if (!TryResolveTargetTransform(target, unityTarget, out var targetTransform))
            {
                CleanupDestroyedTargetReferences(target);
                return;
            }
            if (activeAttacks.Count == 0)
                hasHitPlayer = false;
            wanderer?.EnterCombat(targetTransform);
            var routine = StartCoroutine(AttackRoutine(target));
            activeAttacks[target] = routine;
            if (activeAttacks.Count == 1)
                SetCombatState(true);
        }

        protected virtual IEnumerator AttackRoutine(CombatTarget target)
        {
            // Cache the original target reference and transform up-front so we can clean up even if
            // Unity nulls the parameter after the target is destroyed. This ensures dictionary keys
            // and wanderer state are always cleared when the coroutine ends.
            CombatTarget cachedTarget = target;
            Object cachedUnityTarget = cachedTarget as Object;
            Transform cachedTargetTransform = null;
            if (cachedUnityTarget != null)
                TryResolveTargetTransform(cachedTarget, cachedUnityTarget, out cachedTargetTransform);

            // Wait until the target is within the preferred range for the active attack style
            // before performing each swing so melee, ranged, and magic NPCs respect their
            // configured stand-off distances. A shared timestamp ensures simultaneous routines
            // cannot exceed the NPC's configured attack speed when swapping targets.
            const float DISTANCE_EPSILON = 0.05f;

            while (combatant.IsAlive)
            {
                var unityTarget = target as Object;
                if (unityTarget == null || !TryResolveTargetTransform(target, unityTarget, out var targetTransform))
                    break;

                cachedTargetTransform = targetTransform ?? cachedTargetTransform;

                if (!target.IsAlive)
                    break;

                // Determine the current attack speed, defaulting to four ticks when no profile
                // data is available, and clamp so that NPCs always attack at least once per tick.
                var profile = combatant.Profile;
                int attackSpeedTicks = profile != null ? profile.AttackSpeedTicks : 4;
                attackSpeedTicks = Mathf.Max(1, attackSpeedTicks);
                float attackIntervalSeconds = attackSpeedTicks * CombatMath.TICK_SECONDS;

                float distance = targetTransform != null
                    ? Vector2.Distance(targetTransform.position, transform.position)
                    : float.MaxValue;
                float desiredDistance = profile != null ? profile.GetPreferredAttackRange() : CombatMath.MELEE_RANGE;
                if (distance > 15f)
                {
                    wanderer?.ForceReturnToOrigin();
                    break;
                }

                if (distance <= desiredDistance + DISTANCE_EPSILON)
                {
                    // Ensure we respect the shared attack timestamp even when swapping
                    // between multiple concurrent targets. New coroutines will wait here
                    // until any outstanding cooldown has elapsed.
                    bool lostTargetDuringCooldown = false;
                    while (Time.time < nextAttackTimestamp)
                    {
                        yield return null;

                        // Break out early if the target is lost while we are waiting for the
                        // shared attack window. This keeps retreat logic responsive when the
                        // coroutine is cancelled mid-wait.
                        unityTarget = target as Object;
                        if (unityTarget == null || !TryResolveTargetTransform(target, unityTarget, out targetTransform))
                        {
                            lostTargetDuringCooldown = true;
                            break;
                        }

                        if (!target.IsAlive)
                        {
                            lostTargetDuringCooldown = true;
                            break;
                        }
                    }

                    if (lostTargetDuringCooldown)
                        break;

                    if (!HasLineOfSight(target, targetTransform))
                    {
                        yield return new WaitForSeconds(CombatMath.TICK_SECONDS * 0.25f);
                        continue;
                    }

                    ResolveAttack(target);

                    // Advance the shared timestamp so every active target respects the NPC's
                    // configured attack speed.
                    float now = Time.time;
                    float baseTimestamp = Mathf.Max(nextAttackTimestamp, now);
                    nextAttackTimestamp = baseTimestamp + attackIntervalSeconds;
                }
                else
                {
                    if (targetTransform != null)
                        npcFacing?.FaceTarget(targetTransform);
                }

                yield return null;
            }

            bool onlyTrackedTarget = activeAttacks.Count <= 1;

            // Always pass the cached transform back to the wanderer (even when it compares equal to
            // null) so any stale combat slot keyed by this target reference is released immediately.
            wanderer?.ExitCombat(cachedTargetTransform);

            if (cachedTargetTransform == null && onlyTrackedTarget)
                wanderer?.ExitCombat();

            RemoveCachedTargetFromDictionary(activeAttacks, cachedTarget);
            RemoveCachedTargetFromDictionary(threatLevels, cachedTarget);
            RemoveCachedTargetFromDictionary(lastDamageTimes, cachedTarget);
            if (activeAttacks.Count == 0)
            {
                wanderer?.ForceReturnToOrigin();
                SetCombatState(false);
            }
        }

        /// <summary>
        /// Determines whether any hostile faction targets that this NPC is configured to
        /// proactively attack are currently within the supplied aggro radius.
        /// </summary>
        /// <param name="myFaction">Faction component representing this NPC.</param>
        /// <param name="aggroRadius">Radius within which aggression should be evaluated.</param>
        private bool HasAggressiveFactionTargets(IFactionProvider myFaction, float aggroRadius)
        {
            var active = NpcCombatant.ActiveCombatants;
            for (int i = 0; i < active.Count; i++)
            {
                var npc = active[i];
                if (npc == null || npc == combatant)
                    continue;
                if (!npc.isActiveAndEnabled || !npc.IsAlive)
                    continue;

                if (npc is not IFactionProvider otherFaction)
                    continue;
                if (!myFaction.IsEnemy(otherFaction.Faction))
                    continue;
                if (!FactionUtility.IsAggressiveTowardFaction(myFaction.Faction, otherFaction.Faction))
                    continue;

                float distance = Vector2.Distance(npc.transform.position, spawnPosition);
                if (distance <= aggroRadius)
                    return true;
            }

            return false;
        }

        private void CleanupDestroyedTargetReferences(CombatTarget target)
        {
            StopAndRemoveActiveAttack(target);
            RemoveCachedTargetFromDictionary(threatLevels, target);
            RemoveCachedTargetFromDictionary(lastDamageTimes, target);
        }

        private void StopAndRemoveActiveAttack(CombatTarget target)
        {
            if (activeAttacks.Count == 0)
                return;

            if (activeAttacks.TryGetValue(target, out var routine))
            {
                if (routine != null)
                    StopCoroutine(routine);
                activeAttacks.Remove(target);
            }
            else
            {
                CombatTarget keyToRemove = null;
                Coroutine routineToStop = null;
                foreach (var kvp in activeAttacks)
                {
                    if (ReferenceEquals(kvp.Key, target))
                    {
                        keyToRemove = kvp.Key;
                        routineToStop = kvp.Value;
                        break;
                    }
                }

                if (routineToStop != null)
                    StopCoroutine(routineToStop);

                if (keyToRemove != null)
                    activeAttacks.Remove(keyToRemove);
            }

            RemoveCachedTargetFromDictionary(activeAttacks, target);
        }

        private static bool TryResolveTargetTransform(CombatTarget target, Object unityTarget, out Transform resolvedTransform)
        {
            resolvedTransform = null;
            if (target == null || unityTarget == null)
                return false;

            resolvedTransform = target.transform;
            return resolvedTransform != null;
        }

        /// <summary>
        /// Ensures dictionary entries keyed by the provided <paramref name="cachedTarget"/> are removed even
        /// when Unity has nullified the reference after the underlying object is destroyed.
        /// </summary>
        private static void RemoveCachedTargetFromDictionary<TValue>(Dictionary<CombatTarget, TValue> dictionary, CombatTarget cachedTarget)
        {
            if (dictionary == null || dictionary.Count == 0)
                return;

            if (dictionary.Remove(cachedTarget))
                return;

            CombatTarget keyToRemove = null;
            foreach (var key in dictionary.Keys)
            {
                if (ReferenceEquals(key, cachedTarget))
                {
                    keyToRemove = key;
                    break;
                }
            }

            if (keyToRemove != null)
                dictionary.Remove(keyToRemove);
        }

        protected virtual bool HasLineOfSight(CombatTarget target, Transform targetTransform)
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
                targetTransform,
                collider => ShouldIgnoreCollider(collider, target));
        }

        private bool ShouldIgnoreCollider(Collider2D collider, CombatTarget target)
        {
            if (collider == null)
                return false;

            var hitTransform = collider.transform;
            if (hitTransform == null)
                return false;

            // Ignore this NPC's own colliders and those belonging to the intended target.
            if (combatant != null && hitTransform.GetComponentInParent<NpcCombatant>() == combatant)
                return true;

            if (target != null)
            {
                var targetCombatant = hitTransform.GetComponentInParent<CombatTarget>();
                if (ReferenceEquals(targetCombatant, target))
                    return true;
            }

            DamageType attackType = DamageType.Melee;
            if (combatant != null)
            {
                var profile = combatant.Profile;
                if (profile != null)
                {
                    attackType = profile.AttackType;
                }
                else
                {
                    var stats = combatant.GetCombatantStats();
                    if (stats != null)
                        attackType = stats.DamageType;
                }
            }

            var bypass = hitTransform.GetComponentInParent<CombatLineOfSightBypass>();
            if (bypass != null && bypass.AllowsDamageType(attackType))
                return true;

            // Allow pets and friendly NPCs to stand between combatants without blocking attacks.
            var pet = hitTransform.GetComponentInParent<PetCombatController>();
            if (pet != null)
                return true;

            if (combatant != null)
            {
                var otherNpc = hitTransform.GetComponentInParent<NpcCombatant>();
                if (otherNpc != null && otherNpc != combatant)
                {
                    if (!combatant.IsEnemy(otherNpc.Faction))
                        return true;
                }
            }

            return false;
        }

        protected virtual void ResolveAttack(CombatTarget target)
        {
            var attacker = combatant.GetCombatantStats();
            CombatantStats defender;
            var playerTarget = target as PlayerCombatTarget;
            if (playerTarget != null)
            {
                var skills = playerTarget.GetComponent<SkillManager>();
                var equipment = playerTarget.GetComponent<EquipmentAggregator>();
                var loadout = playerTarget.GetComponent<PlayerCombatLoadout>();
                defender = CombatantStats.ForPlayer(skills, equipment,
                    loadout != null ? loadout.Style : CombatStyle.Defensive,
                    attacker.DamageType);
            }
            else
            {
                defender = new CombatantStats
                {
                    AttackLevel = 1,
                    StrengthLevel = 1,
                    DefenceLevel = 1,
                    Equip = new EquipmentAggregator.CombinedStats(),
                    Style = CombatStyle.Defensive,
                    DamageType = target.PreferredDefenceType
                };
            }

            defender.DamageType = attacker.DamageType;

            int attEff;
            int attackBonus;
            switch (attacker.DamageType)
            {
                case DamageType.Magic:
                    attEff = CombatMath.GetEffectiveAttack(attacker.MagicLevel, CombatStyle.Accurate);
                    attackBonus = attacker.Equip.magic;
                    break;
                case DamageType.Ranged:
                    attEff = CombatMath.GetEffectiveAttack(attacker.AttackLevel, attacker.Style);
                    attackBonus = attacker.Equip.range;
                    break;
                default:
                    attEff = CombatMath.GetEffectiveAttack(attacker.AttackLevel, attacker.Style);
                    attackBonus = attacker.Equip.attack;
                    break;
            }
            int defEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            int atkRoll = CombatMath.GetAttackRoll(attEff, attackBonus);
            int defBonus = attacker.DamageType switch
            {
                DamageType.Magic => defender.Equip.magicDef,
                DamageType.Ranged => defender.Equip.rangeDef,
                _ => defender.Equip.meleeDef
            };
            int defRoll = CombatMath.GetDefenceRoll(defEff, defBonus);
            bool hit = Random.value < CombatMath.ChanceToHit(atkRoll, defRoll);
            int maxHit;
            switch (attacker.DamageType)
            {
                case DamageType.Magic:
                    maxHit = CombatMath.GetMaxHit(attEff, attacker.Equip.magic);
                    break;
                case DamageType.Ranged:
                {
                    int strEff = CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                    maxHit = CombatMath.GetMaxHit(strEff, attacker.Equip.range);
                    break;
                }
                default:
                {
                    int strEff = CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                    maxHit = CombatMath.GetMaxHit(strEff, attacker.Equip.strength);
                    break;
                }
            }

            npcFacing?.FaceTarget(target.transform);
            var spriteAnimator = npcFacing?.Animator;
            if (spriteAnimator != null)
            {
                Direction8 facingDir = npcFacing.FacingDirection;
                spriteAnimator.animator?.SetInteger(spriteAnimator.dirParam, Direction8Utility.ToAnimatorIndex8(facingDir));
                if (spriteAnimator.HasAttackAnimation(facingDir))
                {
                    if (spriteSwapRoutine != null)
                        StopCoroutine(spriteSwapRoutine);
                    spriteSwapRoutine = StartCoroutine(spriteAnimator.PlayAttackAnimation(facingDir));
                }
            }

            if (hit)
            {
                int damage = CombatMath.RollDamage(maxHit);
                int finalDamage = target.ApplyDamage(damage, attacker.DamageType, SpellElement.None, this);
                var targetName = (target as MonoBehaviour)?.name ?? "target";
                Debug.Log($"{name} dealt {finalDamage} damage to {targetName}.");
                var applier = GetComponentInChildren<OnHitPoisonApplier>();
                var targetMb = target as MonoBehaviour;
                if (applier != null && targetMb != null)
                    applier.TryApply(targetMb.gameObject, finalDamage > 0, combatant);
            }
            else
            {
                var targetName = (target as MonoBehaviour)?.name ?? "target";
                Debug.Log($"{name} missed {targetName}.");
            }

            if (!hasHitPlayer && playerTarget != null && PetDropSystem.GuardModeEnabled)
            {
                var pet = PetDropSystem.ActivePetCombat;
                pet?.CommandAttack(combatant, false);
                hasHitPlayer = true;
            }
        }
    }
}
