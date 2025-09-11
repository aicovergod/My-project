using System.Collections;
using UnityEngine;
using Combat;
using MyGame.Drops;
using Player;
using Pets;

namespace NPC
{
    /// <summary>
    /// Simple adaptor tying an NPC to the combat system using a combat profile.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(NpcDropper))]
    public class NpcCombatant : MonoBehaviour, CombatTarget, IFactionProvider
    {
        [SerializeField] private NpcCombatProfile profile;
        private int currentHp;
        private Collider2D collider2D;
        private SpriteRenderer spriteRenderer;
        private int playerDamage;
        private int npcDamage;

        public event System.Action<int, int> OnHealthChanged; // current, max
        public event System.Action OnDeath;

        public bool IsAlive => currentHp > 0;
        public DamageType PreferredDefenceType => profile != null ? profile.AttackType : DamageType.Melee;
        public int CurrentHP => currentHp;
        public int MaxHP => profile != null ? profile.HitpointsLevel : currentHp;
        public NpcCombatProfile Profile => profile;

        /// <summary>The faction of this NPC.</summary>
        public FactionId Faction => profile != null ? profile.Faction : FactionId.Neutral;

        /// <inheritdoc />
        public bool IsEnemy(FactionId other) => FactionUtility.IsEnemy(Faction, other);

        private void Awake()
        {
            currentHp = profile != null ? profile.HitpointsLevel : 1;
            collider2D = GetComponent<Collider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            ResetDamageCounters();
            OnHealthChanged?.Invoke(currentHp, MaxHP);
        }

        /// <summary>Apply damage to this NPC.</summary>
        public int ApplyDamage(int amount, DamageType type, SpellElement element, object source)
        {
            int finalAmount = amount;
            if (profile != null && profile.elementalModifiers != null)
            {
                foreach (var mod in profile.elementalModifiers)
                {
                    if (mod.element == element)
                    {
                        float adjusted = finalAmount;
                        adjusted *= 1f - mod.protectionPercent / 100f;
                        adjusted *= 1f + mod.bonusPercent / 100f;
                        finalAmount = Mathf.Max(0, Mathf.RoundToInt(adjusted));
                        break;
                    }
                }
            }
            currentHp = Mathf.Max(0, currentHp - finalAmount);
            Debug.Log($"{name} took {finalAmount} damage ({currentHp}/{MaxHP}).");
            OnHealthChanged?.Invoke(currentHp, MaxHP);
            var combatSource = source as CombatTarget;
            bool creditedToPlayer = false;
            if (combatSource != null)
            {
                if (combatSource is PlayerCombatTarget)
                {
                    playerDamage += finalAmount;
                    creditedToPlayer = true;
                }
                else if (combatSource is PetCombatController pet)
                {
                    var owner = pet.GetComponent<PetFollower>()?.Player;
                    if (owner != null && owner.TryGetComponent<PlayerCombatTarget>(out _))
                    {
                        playerDamage += finalAmount;
                        creditedToPlayer = true;
                    }
                    else
                    {
                        npcDamage += finalAmount;
                    }
                }
                else
                {
                    npcDamage += finalAmount;
                }
                var combat = GetComponent<BaseNpcCombat>();
                combat?.AddThreat(combatSource, finalAmount);
                combat?.RecordDamageFrom(combatSource);
            }
            else
            {
                npcDamage += finalAmount;
            }
            var killedByPlayer = creditedToPlayer;
            if (currentHp <= 0)
            {
                // Trigger drops before other death listeners in case they
                // destroy this NPC immediately (e.g. when killed by pets).
                var dropper = GetComponent<NpcDropper>();
                if (killedByPlayer || playerDamage > npcDamage)
                    dropper?.OnDeath();

                ResetDamageCounters();
                OnDeath?.Invoke();
                if (collider2D) collider2D.enabled = false;
                if (spriteRenderer) spriteRenderer.enabled = false;
                if (profile != null && profile.RespawnSeconds > 0f)
                    StartCoroutine(RespawnRoutine());
            }

            return finalAmount;
        }

        /// <summary>Get combat stats for this NPC.</summary>
        public CombatantStats GetCombatantStats()
        {
            return CombatantStats.ForNpc(profile);
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(profile.RespawnSeconds);
            currentHp = profile != null ? profile.HitpointsLevel : 1;
            if (collider2D) collider2D.enabled = true;
            if (spriteRenderer) spriteRenderer.enabled = true;
            GetComponent<BaseNpcCombat>()?.ResetCombatState(true);
            ResetDamageCounters();
            OnHealthChanged?.Invoke(currentHp, MaxHP);
        }

        private void ResetDamageCounters()
        {
            playerDamage = 0;
            npcDamage = 0;
        }
    }
}
