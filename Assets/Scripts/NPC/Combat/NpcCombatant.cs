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
        [SerializeField, Tooltip("Optional knockback receiver for handling physical impulses when the NPC takes damage.")]
        private NpcKnockbackReceiver knockbackReceiver;

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

        /// <summary>
        /// Tracks whether the global NPC combat damage logging override is enabled. When true all
        /// combatants emit verbose logs regardless of their inspector configuration.
        /// </summary>
        private static bool globalDamageLoggingEnabled;

        public event System.Action<int, int> OnHealthChanged; // current, max
        public event System.Action OnDeath;
        public event System.Action<NpcCombatant, GameObject> OnKilledByPlayer;

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

        /// <summary>
        /// Gets or sets whether NPC combatants should emit damage logs globally. When toggled the
        /// state is immediately pushed to all active combatants and will be applied to any NPCs that
        /// spawn in the future.
        /// </summary>
        public static bool GlobalDamageLoggingEnabled
        {
            get => globalDamageLoggingEnabled;
            set
            {
                if (globalDamageLoggingEnabled == value)
                    return;

                globalDamageLoggingEnabled = value;
                foreach (var combatant in activeCombatants)
                {
                    if (combatant != null)
                        combatant.ApplyGlobalDamageLoggingState();
                }
            }
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
            if (knockbackReceiver == null)
                knockbackReceiver = GetComponent<NpcKnockbackReceiver>();
            flashEffect = GetComponent<NpcFlashEffect>();
            if (flashEffect == null && spriteRenderer != null)
            {
                flashEffect = gameObject.AddComponent<NpcFlashEffect>();
            }
            ClearDamageContributors("Awake initialisation");
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
            ApplyGlobalDamageLoggingState();
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
            // Clamp the outgoing damage so downstream systems only see the actual amount removed.
            int damageToApply = Mathf.Min(finalAmount, currentHp);
            currentHp = Mathf.Max(0, currentHp - damageToApply);
            if (logDamage)
            {
                Debug.Log($"{name} took {damageToApply} damage ({currentHp}/{MaxHP}).", this);
            }
            if (damageToApply > 0)
            {
                flashEffect?.TriggerFlash(damageToApply, MaxHP);
            }
            OnHealthChanged?.Invoke(currentHp, MaxHP);
            if (type == DamageType.Poison && poisonHitsplat != null)
            {
                Vector3 hitsplatPosition = FloatingTextAnchorUtility.ResolveAnchorPosition(transform, hitsplatFallbackOffset, ref hitsplatAnchorCache);
                FloatingText.Show(damageToApply.ToString(), hitsplatPosition, Color.white, null, poisonHitsplat);
            }
            var combat = GetComponent<BaseNpcCombat>();
            var combatSource = source as CombatTarget;
            GameObject killingPlayer = null;
            bool creditedToPlayer = false;
            Transform knockbackSource = null;
            var resolvedPlayer = ResolvePlayerFromDamageSource(source);
            if (resolvedPlayer != null)
            {
                playerDamage += damageToApply;
                creditedToPlayer = true;
                killingPlayer = resolvedPlayer;

                if (source is Component componentSource)
                    knockbackSource = componentSource.transform;
                else if (combatSource is Component combatComponent)
                    knockbackSource = combatComponent.transform;
                else
                    knockbackSource = resolvedPlayer.transform;
            }
            else
            {
                npcDamage += damageToApply;
            }

            if (damageToApply > 0 && knockbackSource != null)
                knockbackReceiver?.ApplyKnockbackFrom(knockbackSource, damageToApply);

            if (combatSource != null)
            {
                combat?.AddThreat(combatSource, damageToApply);
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

            var killedByPlayer = creditedToPlayer;
            if (currentHp <= 0)
            {
                // Trigger drops before other death listeners in case they
                // destroy this NPC immediately (e.g. when killed by pets).
                var dropper = GetComponent<NpcDropper>();
                float dropCreditWindow = profile != null
                    ? Mathf.Max(profile.AggroTimeoutSeconds, CombatMath.TICK_SECONDS)
                    : CombatMath.TICK_SECONDS;
                bool hasRecentPlayerContribution = false;
                float secondsSincePlayerContribution = float.PositiveInfinity;
                if (combat != null)
                    hasRecentPlayerContribution = combat.HasRecentPlayerContribution(dropCreditWindow, out secondsSincePlayerContribution);

                if (logDamage)
                {
                    string contributionSummary = hasRecentPlayerContribution
                        ? $"last player contribution {secondsSincePlayerContribution:F1}s ago (window {dropCreditWindow:F1}s)"
                        : $"no player contribution within the {dropCreditWindow:F1}s window";
                    Debug.Log($"{name} evaluating drop credit: playerDamage={playerDamage}, npcDamage={npcDamage}, {contributionSummary}.", this);
                }

                Debug.Assert(!hasRecentPlayerContribution || playerDamage > 0,
                    $"{name} recorded recent player damage but has no tracked playerDamage. Ensure ClearDamageContributors is invoked when combat resets.");

                if (killedByPlayer && killingPlayer != null)
                    OnKilledByPlayer?.Invoke(this, killingPlayer);

                if (killedByPlayer || playerDamage > npcDamage)
                    dropper?.OnDeath();

                ClearDamageContributors("NPC death resolution");
                combat?.ResetCombatState();
                if (wanderer != null) wanderer.enabled = false;
                OnDeath?.Invoke();
                if (collider2D) collider2D.enabled = false;
                if (spriteRenderer) spriteRenderer.enabled = false;
                if (profile != null && profile.RespawnSeconds > 0f)
                    StartCoroutine(RespawnRoutine());
            }

            return damageToApply;
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
            knockbackReceiver?.CancelKnockback();
            Vector2 spawn = combat != null ? combat.SpawnPosition : (Vector2)transform.position;
            transform.position = spawn;
            wanderer?.SetOrigin(spawn);
            combat?.ResetCombatState(true);
            ClearDamageContributors("Respawned");
            OnHealthChanged?.Invoke(currentHp, MaxHP);
        }

        /// <summary>
        /// Public helper used by other systems to clear any cached damage attribution. This wraps
        /// the legacy <see cref="ResetDamageCounters"/> logic so threat resets, retreats, or
        /// timeout events always wipe player/NPC damage tallies in a single, auditable place.
        /// </summary>
        /// <param name="reason">Optional human readable context written to the console when
        /// <see cref="LogDamage"/> is enabled.</param>
        public void ClearDamageContributors(string reason = null)
        {
            knockbackReceiver?.CancelKnockback();
            ResetDamageCounters();
            if (logDamage)
            {
                string context = string.IsNullOrEmpty(reason) ? "without context" : reason;
                Debug.Log($"{name} cleared damage contributors ({context}).", this);
            }
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

        /// <summary>
        /// Applies the global NPC damage logging override to this combatant, forcing the current
        /// logging flag to match the shared setting. When disabled the NPC will stop emitting
        /// damage logs until a system explicitly re-enables them.
        /// </summary>
        private void ApplyGlobalDamageLoggingState()
        {
            logDamage = globalDamageLoggingEnabled;
        }

        /// <summary>
        /// Resolves the player GameObject responsible for a given damage source. Handles direct players,
        /// pet proxies, and component references so fatal blows can be attributed consistently.
        /// </summary>
        /// <param name="source">Damage source passed to <see cref="ApplyDamage"/>.</param>
        /// <returns>The owning player GameObject when one can be identified; otherwise <c>null</c>.</returns>
        private GameObject ResolvePlayerFromDamageSource(object source)
        {
            if (source == null)
                return null;

            if (source is PlayerCombatTarget directPlayer)
                return directPlayer.gameObject;

            if (source is Component componentSource)
            {
                var player = ResolvePlayerFromComponent(componentSource);
                if (player != null)
                    return player;
            }

            if (source is GameObject sourceObject)
            {
                var player = ResolvePlayerFromComponent(sourceObject.transform);
                if (player != null)
                    return player;
            }

            if (source is CombatTarget combatTarget && combatTarget is Component combatComponent)
            {
                var player = ResolvePlayerFromComponent(combatComponent);
                if (player != null)
                    return player;
            }

            return null;
        }

        /// <summary>
        /// Attempts to locate a player GameObject starting from a component reference, searching both the
        /// component hierarchy and associated pet followers.
        /// </summary>
        private GameObject ResolvePlayerFromComponent(Component component)
        {
            if (component == null)
                return null;

            if (component.TryGetComponent<PlayerCombatTarget>(out var directPlayer))
                return directPlayer.gameObject;

            var parentPlayer = component.GetComponentInParent<PlayerCombatTarget>();
            if (parentPlayer != null)
                return parentPlayer.gameObject;

            var follower = component.GetComponentInParent<PetFollower>();
            if (follower != null && follower.Player != null && follower.Player.TryGetComponent<PlayerCombatTarget>(out _))
                return follower.Player;

            return null;
        }
    }
}
