using System.Collections;
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
        protected CombatTarget currentTarget;
        protected PlayerCombatTarget playerTarget;
        protected bool hasHitPlayer;
        protected Vector2 spawnPosition;
        protected NpcFacing npcFacing;
        protected Coroutine spriteSwapRoutine;

        protected virtual void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
            wanderer = GetComponent<NpcWanderer>();
            playerTarget = FindObjectOfType<PlayerCombatTarget>();
            spawnPosition = transform.position;
            npcFacing = GetComponent<NpcFacing>();
        }

        protected virtual void Update()
        {
            var profile = combatant.Profile;
            if (profile == null || !profile.IsAggressive)
                return;

            if (playerTarget == null)
                playerTarget = FindObjectOfType<PlayerCombatTarget>();

            float npcDistFromSpawn = Vector2.Distance(transform.position, spawnPosition);
            float playerDistFromSpawn = playerTarget != null
                ? Vector2.Distance(playerTarget.transform.position, spawnPosition)
                : float.MaxValue;

            if (currentTarget == null)
            {
                if (playerTarget != null && playerDistFromSpawn <= profile.AggroRange)
                {
                    BeginAttacking(playerTarget);
                }
                else
                {
                    var myFaction = combatant as IFactionProvider;
                    if (myFaction != null)
                    {
                        foreach (var npc in FindObjectsOfType<NpcCombatant>())
                        {
                            if (npc == combatant || !npc.IsAlive)
                                continue;
                            var otherFaction = npc as IFactionProvider;
                            if (otherFaction == null)
                                continue;
                            if (!myFaction.IsEnemy(otherFaction.Faction))
                                continue;
                            float dist = Vector2.Distance(npc.transform.position, transform.position);
                            if (dist <= profile.AggroRange)
                            {
                                BeginAttacking(npc);
                                break;
                            }
                        }
                    }
                }
            }
            else if (npcDistFromSpawn > profile.AggroRange ||
                     Vector2.Distance(currentTarget.transform.position, spawnPosition) > profile.AggroRange)
            {
                BeginAttacking(null);
            }

            if (currentTarget != null && combatant.IsAlive && currentTarget.IsAlive)
            {
                npcFacing?.FaceTarget(currentTarget.transform);
            }
        }

        public virtual void BeginAttacking(CombatTarget target)
        {
            if (target != null && currentTarget == target)
                return;
            StopAllCoroutines();
            wanderer?.ExitCombat();
            currentTarget = target;
            hasHitPlayer = false;
            if (target != null)
            {
                wanderer?.EnterCombat(target.transform);
                StartCoroutine(AttackRoutine(target));
            }
        }

        protected virtual IEnumerator AttackRoutine(CombatTarget target)
        {
            var wait = new WaitForSeconds(4 * CombatMath.TICK_SECONDS);

            // Wait until the player is within melee range before performing the first attack.
            while (target != null && target.IsAlive && combatant.IsAlive &&
                   Vector2.Distance(target.transform.position, transform.position) > CombatMath.MELEE_RANGE)
            {
                npcFacing?.FaceTarget(target.transform);
                yield return null;
            }

            while (target != null && target.IsAlive && combatant.IsAlive)
            {
                float distance = Vector2.Distance(target.transform.position, transform.position);
                // If we move beyond aggro bounds or the target leaves melee range, stop attacking.
                var profile = combatant.Profile;
                float npcDistFromSpawn = Vector2.Distance(transform.position, spawnPosition);
                if ((profile != null && npcDistFromSpawn > profile.AggroRange) || distance > CombatMath.MELEE_RANGE)
                    break;

                ResolveAttack(target);
                yield return wait;
            }

            wanderer?.ExitCombat();
            currentTarget = null;
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
                target.ApplyDamage(damage, attacker.DamageType, this);
                var targetName = (target as MonoBehaviour)?.name ?? "target";
                Debug.Log($"{name} dealt {damage} damage to {targetName}.");
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
