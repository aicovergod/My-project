using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Combat;
using EquipmentSystem;
using Skills;
using Player;
using Pets;

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
        protected NpcFacing npcFacing;
        protected Coroutine spriteSwapRoutine;

        protected readonly Dictionary<CombatTarget, float> threatLevels = new();
        protected readonly Dictionary<CombatTarget, Coroutine> activeAttacks = new();
        // Tracks the last time each target dealt damage to this NPC.
        protected readonly Dictionary<CombatTarget, float> lastDamageTimes = new();

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
            combatant = GetComponent<NpcCombatant>();
            wanderer = GetComponent<NpcWanderer>();
            playerTarget = FindObjectOfType<PlayerCombatTarget>();
            spawnPosition = transform.position;
            npcFacing = GetComponent<NpcFacing>();
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

        protected virtual void Update()
        {
            var profile = combatant.Profile;
            if (profile == null || !profile.IsAggressive)
                return;

            if (playerTarget == null)
                playerTarget = FindObjectOfType<PlayerCombatTarget>();

            float npcChaseDist = Vector2.Distance(transform.position, spawnPosition);
            foreach (var t in new List<CombatTarget>(threatLevels.Keys))
            {
                bool remove = t == null || !t.IsAlive;
                if (!remove)
                {
                    float targetDist = Vector2.Distance(t.transform.position, spawnPosition);
                    if (targetDist > profile.AggroRange)
                    {
                        float last;
                        if (!lastDamageTimes.TryGetValue(t, out last))
                            last = float.NegativeInfinity;
                        remove = Time.time - last > profile.AggroTimeoutSeconds;
                    }
                }
                if (remove)
                {
                    threatLevels.Remove(t);
                    lastDamageTimes.Remove(t);
                    if (activeAttacks.TryGetValue(t, out var c))
                    {
                        if (c != null)
                            StopCoroutine(c);
                        activeAttacks.Remove(t);
                        wanderer?.ExitCombat(t.transform);
                    }
                }
            }

            if (threatLevels.Count == 0 &&
                activeAttacks.Count == 0 &&
                npcChaseDist > profile.AggroRange)
            {
                // Without targets, send the NPC back toward its spawn when it has wandered too far.
                ResetCombatState();
            }
            else if (activeAttacks.Count == 0)
            {
                SetCombatState(false);
            }

            var potentials = new List<CombatTarget>();

            if (playerTarget != null && playerTarget.IsAlive)
            {
                float playerDist = Vector2.Distance(playerTarget.transform.position, spawnPosition);
                if (playerDist <= profile.AggroRange)
                    potentials.Add(playerTarget);
            }

            var myFaction = combatant as IFactionProvider;
            if (myFaction != null)
            {
                foreach (var npc in FindObjectsOfType<NpcCombatant>())
                {
                    if (npc == combatant || !npc.IsAlive)
                        continue;
                    var otherFaction = npc as IFactionProvider;
                    if (otherFaction == null || !myFaction.IsEnemy(otherFaction.Faction))
                        continue;
                    float dist = Vector2.Distance(npc.transform.position, spawnPosition);
                    if (dist <= profile.AggroRange)
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
                float bestDist = float.MaxValue;
                foreach (var t in activeAttacks.Keys)
                {
                    float dist = Vector2.Distance(t.transform.position, transform.position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        closest = t;
                    }
                }
                if (closest != null)
                    npcFacing?.FaceTarget(closest.transform);
            }
        }

        public virtual void BeginAttacking(CombatTarget target)
        {
            if (target == null || activeAttacks.ContainsKey(target))
                return;
            if (activeAttacks.Count == 0)
                hasHitPlayer = false;
            wanderer?.EnterCombat(target.transform);
            var routine = StartCoroutine(AttackRoutine(target));
            activeAttacks[target] = routine;
            if (activeAttacks.Count == 1)
                SetCombatState(true);
        }

        protected virtual IEnumerator AttackRoutine(CombatTarget target)
        {
            var wait = new WaitForSeconds(4 * CombatMath.TICK_SECONDS);

            // Wait until the player is within melee range before performing the first attack.
            while (combatant.IsAlive && target != null && target.IsAlive)
            {
                var profile = combatant.Profile;
                float spawnDist = Vector2.Distance(target.transform.position, spawnPosition);
                if (spawnDist > profile.AggroRange)
                {
                    float last;
                    if (!lastDamageTimes.TryGetValue(target, out last))
                        last = float.NegativeInfinity;
                    if (Time.time - last > profile.AggroTimeoutSeconds)
                        break;
                }

                float distance = Vector2.Distance(target.transform.position, transform.position);
                if (distance <= CombatMath.MELEE_RANGE)
                {
                    ResolveAttack(target);
                    yield return wait;
                }
                else
                {
                    npcFacing?.FaceTarget(target.transform);
                    yield return null;
                }
            }

            wanderer?.ExitCombat(target.transform);
            activeAttacks.Remove(target);
            threatLevels.Remove(target);
            lastDamageTimes.Remove(target);
            if (activeAttacks.Count == 0)
                SetCombatState(false);
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
                    DamageType.Melee);
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
            bool hit = Random.value < CombatMath.ChanceToHit(atkRoll, defRoll);
            int damage = 0;

            npcFacing?.FaceTarget(target.transform);
            var animator = npcFacing?.Animator;
            if (animator != null)
            {
                int facingDir = npcFacing.FacingDirection;
                if (animator.HasAttackAnimation(facingDir))
                {
                    if (spriteSwapRoutine != null)
                        StopCoroutine(spriteSwapRoutine);
                    spriteSwapRoutine = StartCoroutine(animator.PlayAttackAnimation(facingDir));
                }
            }

            if (hit)
            {
                int strEff = CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                int maxHit = CombatMath.GetMaxHit(strEff, attacker.Equip.strength);
                damage = CombatMath.RollDamage(maxHit);
                int finalDamage = target.ApplyDamage(damage, attacker.DamageType, SpellElement.None, this);
                var targetName = (target as MonoBehaviour)?.name ?? "target";
                Debug.Log($"{name} dealt {finalDamage} damage to {targetName}.");
            }
            else
            {
                var targetName = (target as MonoBehaviour)?.name ?? "target";
                Debug.Log($"{name} missed {targetName}.");
            }

            if (!hasHitPlayer && playerTarget != null && PetDropSystem.GuardModeEnabled)
            {
                var pet = PetDropSystem.ActivePetCombat;
                pet?.CommandAttack(combatant);
                hasHitPlayer = true;
            }
        }
    }
}
