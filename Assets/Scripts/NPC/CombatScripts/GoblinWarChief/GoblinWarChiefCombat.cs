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
    /// Combat controller for the Goblin War Chief. Performs standard melee attacks
    /// and executes a periodic slam dealing area damage with visual effects.
    /// </summary>
    [RequireComponent(typeof(NpcCombatant))]
    public class GoblinWarChiefCombat : MonoBehaviour
    {
        private NpcCombatant combatant;
        private NpcWanderer wanderer;
        private CombatTarget currentTarget;
        private PlayerCombatTarget playerTarget;
        private bool hasHitPlayer;
        private Vector2 spawnPosition;

        [SerializeField] private float slamInterval = 10f;
        [SerializeField] private int slamDamage = 10;
        [SerializeField] private GameObject slamDustPrefab;
        [SerializeField] private float slamRange = 1.5f;
        [SerializeField] private float shakeDuration = 0.2f;
        [SerializeField] private float shakeMagnitude = 0.1f;

        private void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
            wanderer = GetComponent<NpcWanderer>();
            playerTarget = FindObjectOfType<PlayerCombatTarget>();
            spawnPosition = transform.position;
        }

        private void Update()
        {
            var profile = combatant.Profile;
            if (profile == null || !profile.IsAggressive)
                return;

            if (playerTarget == null)
                playerTarget = FindObjectOfType<PlayerCombatTarget>();

            if (playerTarget == null)
                return;

            float playerDistFromSpawn = Vector2.Distance(playerTarget.transform.position, spawnPosition);
            float npcDistFromSpawn = Vector2.Distance(transform.position, spawnPosition);
            if (currentTarget == null)
            {
                if (playerDistFromSpawn <= profile.AggroRange)
                    BeginAttacking(playerTarget);
            }
            else if (playerDistFromSpawn > profile.AggroRange || npcDistFromSpawn > profile.AggroRange)
            {
                BeginAttacking(null);
            }
        }

        public void BeginAttacking(CombatTarget target)
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
                StartCoroutine(SlamRoutine(target));
            }
        }

        private IEnumerator AttackRoutine(CombatTarget target)
        {
            var wait = new WaitForSeconds(4 * CombatMath.TICK_SECONDS);

            // Wait until the player is within melee range before performing the first attack.
            while (target != null && target.IsAlive && combatant.IsAlive &&
                   Vector2.Distance(target.transform.position, transform.position) > CombatMath.MELEE_RANGE)
            {
                // Poll each frame until the target is close enough or combat ends.
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

        private IEnumerator SlamRoutine(CombatTarget target)
        {
            var wait = new WaitForSeconds(slamInterval);
            while (target != null && target.IsAlive && combatant.IsAlive)
            {
                yield return wait;
                if (target == null || !target.IsAlive || !combatant.IsAlive)
                    break;
                PerformSlam(target);
            }
        }

        private void ResolveAttack(CombatTarget target)
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

        private void PerformSlam(CombatTarget target)
        {
            var targetBehaviour = target as MonoBehaviour;
            if (targetBehaviour != null)
            {
                Vector2 npcPos = transform.position;
                Vector2 targetPos = targetBehaviour.transform.position;
                Vector2 delta = targetPos - npcPos;
                if (Mathf.Abs(delta.x) <= slamRange && Mathf.Abs(delta.y) <= slamRange)
                {
                    target.ApplyDamage(slamDamage, DamageType.Melee, this);
                }
            }

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (slamDustPrefab != null)
                        Instantiate(slamDustPrefab, (Vector2)transform.position + new Vector2(x, y), Quaternion.identity);
                }
            }

            StartCoroutine(ScreenShake(shakeDuration, shakeMagnitude));
        }

        private IEnumerator ScreenShake(float duration, float magnitude)
        {
            var cam = Camera.main;
            if (cam == null)
                yield break;

            Vector3 originalPos = cam.transform.localPosition;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float offsetX = Random.Range(-1f, 1f) * magnitude;
                float offsetY = Random.Range(-1f, 1f) * magnitude;
                cam.transform.localPosition = new Vector3(originalPos.x + offsetX, originalPos.y + offsetY, originalPos.z);
                elapsed += Time.deltaTime;
                yield return null;
            }

            cam.transform.localPosition = originalPos;
        }
    }
}
