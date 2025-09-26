using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Combat;
using MyGame.Drops;
using Player;
using Pets;
using UI;
using Status.Freeze;

namespace NPC
{
    /// <summary>
    /// Simple adaptor tying an NPC to the combat system using a combat profile.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(NpcDropper)), RequireComponent(typeof(FrozenStatusController))]
    public class NpcCombatant : MonoBehaviour, CombatTarget, IFactionProvider
    {
        /// <summary>
        /// Global cache of NPC combatants that are currently active in the scene. This allows
        /// other systems to iterate nearby NPCs without paying the cost of repeated
        /// <see cref="Object.FindObjectsOfType{T}()"/> calls each frame.
        /// </summary>
        private static readonly List<NpcCombatant> activeCombatants = new();

        /// <summary>
        /// Read-only view of the currently active NPC combatants. Consumers should treat the
        /// collection as volatile; items may be removed if NPCs are disabled or destroyed.
        /// </summary>
        public static IReadOnlyList<NpcCombatant> ActiveCombatants => activeCombatants;

        [SerializeField] private NpcCombatProfile profile;
        [SerializeField, Tooltip("When enabled this NPC will emit detailed damage logs to the console for debugging.")]
        private bool logDamage = false;
        [SerializeField, Tooltip("Centralised hitsplat sprite references assigned via the inspector.")]
        private HitSplatLibrary hitSplatLibrary;

        [SerializeField, Tooltip("Vertical offset applied when no floating text anchor is present.")]
        private float hitsplatFallbackOffset = 1f;

        /// <summary>
        /// Shared cached reference so NPCs can automatically use the global hitsplat
        /// library without paying the cost of repeated Resources lookups.
        /// </summary>
        private static HitSplatLibrary sharedHitSplatLibrary;
        private int currentHp;
        private Collider2D collider2D;
        private SpriteRenderer spriteRenderer;
        private NpcWanderer wanderer;
        private NpcFlashEffect flashEffect; // visual damage feedback handler
        private int playerDamage;
        private int npcDamage;
        private Sprite poisonHitsplat;
        private FloatingTextAnchorUtility.AnchorCache hitsplatAnchorCache;
        private bool isRegisteredWithRegistry;

        public event System.Action<int, int> OnHealthChanged; // current, max
        public event System.Action OnDeath;

        public bool IsAlive => currentHp > 0;
        public DamageType PreferredDefenceType => profile != null ? profile.AttackType : DamageType.Melee;
        public int CurrentHP => currentHp;
        public int MaxHP => profile != null ? profile.HitpointsLevel : currentHp;
        public NpcCombatProfile Profile => profile;

        /// <summary>
        /// When true this NPC will emit verbose console logging whenever damage is applied.
        /// Exposed so the AdminF2Menu can toggle the behaviour at runtime for QA.
        /// </summary>
        public bool LogDamage
        {
            get => logDamage;
            set => logDamage = value;
        }

        /// <summary>Returns true when this NPC can be affected by the frozen status effect.</summary>
        public bool IsFreezable => profile == null || !profile.NotFreezable;

        /// <summary>The faction of this NPC.</summary>
        public FactionId Faction => profile != null ? profile.Faction : FactionId.Neutral;

        /// <inheritdoc />
        public bool IsEnemy(FactionId other) => FactionUtility.IsEnemy(Faction, other);

        private void Awake()
        {
            currentHp = profile != null ? profile.HitpointsLevel : 1;
            collider2D = GetComponent<Collider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            wanderer = GetComponent<NpcWanderer>();
            flashEffect = GetComponent<NpcFlashEffect>();
            if (flashEffect == null && spriteRenderer != null)
            {
                flashEffect = gameObject.AddComponent<NpcFlashEffect>();
            }
            ResetDamageCounters();
            OnHealthChanged?.Invoke(currentHp, MaxHP);

            EnsureHitSplatLibrary();

            if (hitSplatLibrary == null)
            {
                Debug.LogError("NpcCombatant requires a HitSplatLibrary reference. Assign one in the inspector.", this);
            }
            else
            {
                poisonHitsplat = hitSplatLibrary.PoisonHitsplat;
            }

            if (profile != null && profile.IsPoisonous && profile.OnHitPoison != null)
            {
                var applier = GetComponent<OnHitPoisonApplier>();
                if (applier == null)
                    applier = gameObject.AddComponent<OnHitPoisonApplier>();
                applier.poison = profile.OnHitPoison;
                applier.applyChance = profile.PoisonChance;
                applier.requiresDamage = profile.PoisonRequiresDamage;
            }
        }

        private void OnEnable()
        {
            RegisterCombatant();
        }

        private void OnDisable()
        {
            UnregisterCombatant();
        }

        private void OnDestroy()
        {
            // OnDisable is invoked before OnDestroy, but we defensively unregister in case
            // destruction occurs while the component is disabled.
            UnregisterCombatant();
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
            if (logDamage)
            {
                Debug.Log($"{name} took {finalAmount} damage ({currentHp}/{MaxHP}).", this);
            }
            if (finalAmount > 0)
            {
                flashEffect?.TriggerFlash(finalAmount, MaxHP);
            }
            OnHealthChanged?.Invoke(currentHp, MaxHP);
            if (type == DamageType.Poison && poisonHitsplat != null)
            {
                Vector3 hitsplatPosition = FloatingTextAnchorUtility.ResolveAnchorPosition(transform, hitsplatFallbackOffset, ref hitsplatAnchorCache);
                FloatingText.Show(finalAmount.ToString(), hitsplatPosition, Color.white, null, poisonHitsplat);
            }
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
                if (combat != null && !combat.InCombat)
                {
                    float dist = Vector2.Distance(combatSource.transform.position, combat.SpawnPosition);
                    float radius = wanderer != null ? wanderer.AggroRadius : 5f;
                    if (dist > radius)
                        combat.ReengageFromRetreat(combatSource);
                    }
                combat?.BeginAttacking(combatSource);
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
                GetComponent<BaseNpcCombat>()?.ResetCombatState();
                if (wanderer != null) wanderer.enabled = false;
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
            if (wanderer != null) wanderer.enabled = true;
            var combat = GetComponent<BaseNpcCombat>();
            Vector2 spawn = combat != null ? combat.SpawnPosition : (Vector2)transform.position;
            transform.position = spawn;
            wanderer?.SetOrigin(spawn);
            combat?.ResetCombatState(true);
            ResetDamageCounters();
            OnHealthChanged?.Invoke(currentHp, MaxHP);
        }

        private void ResetDamageCounters()
        {
            playerDamage = 0;
            npcDamage = 0;
        }

        /// <summary>
        /// Ensure the NPC has access to a hitsplat library, loading the shared asset from
        /// the Resources folder when no explicit reference is configured in the inspector.
        /// </summary>
        private void EnsureHitSplatLibrary()
        {
            if (hitSplatLibrary != null)
                return;

            if (sharedHitSplatLibrary == null)
                sharedHitSplatLibrary = Resources.Load<HitSplatLibrary>("HitSplatLibrary");

            if (sharedHitSplatLibrary != null)
                hitSplatLibrary = sharedHitSplatLibrary;
        }

        /// <summary>
        /// Adds this combatant to the global registry if it is not already present.
        /// </summary>
        private void RegisterCombatant()
        {
            if (isRegisteredWithRegistry)
                return;

            activeCombatants.Add(this);
            isRegisteredWithRegistry = true;
        }

        /// <summary>
        /// Removes this combatant from the global registry if it is currently registered.
        /// </summary>
        private void UnregisterCombatant()
        {
            if (!isRegisteredWithRegistry)
                return;

            activeCombatants.Remove(this);
            isRegisteredWithRegistry = false;
        }
    }
}
