using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Combat;
using Companions;
using Companions.Conversation;
using Inventory;
using MyGame.Drops;
using Player;
using Pets;
using UI;
using UI.Chat;
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

        /// <summary>Case-insensitive keyword used to detect ore golem combatants.</summary>
        private const string OreGolemNameKeyword = "Ore Golem";

        /// <summary>Unique item identifier for the Hades fragment drop.</summary>
        private const string HadesFragmentItemId = "Hades Fragment";

        /// <summary>Denominator applied to the ore golem Hades fragment roll (1 in N chance).</summary>
        private const int OreGolemHadesDropDenominator = 40;

        /// <summary>Shared cache of the ground item spawner used when spawning forced drops.</summary>
        private static GroundItemSpawner sharedGroundItemSpawner;

        /// <summary>Dialogue options surfaced when the player receives a fragment with a companion active.</summary>
        private static readonly string[] OreGolemCompanionPlayerDropLines =
        {
            "Damn, lucky sod.",
            "I must be your good luck charm.",
            "Congrats on the Hades Fragment.",
            "I heard those fragments, can upgrade your Ore Bag.",
            "I wonder where the Ore Golem, was hiding that fragment <emoji=02>,",
            "Whoa, no way you actually got one!",
            "Alright, share some of that luck, yeah?",
            "Wait, you just got a Hades Fragment? Seriously?",
            "How the hell did *you* pull that?",
            "Yo, that’s actually rare as hell.",
            "Nice drop, guess the Ore Golem liked you.",
            "See? Told you this spot was worth it.",
            "Don’t mind me, just over here getting nothing.",
            "That’s huge, {playerName}. You lucky git.",
            "Hold up… was that a Hades Fragment? gz!!!",
            "That’ll fetch a nice price.",
            "Okay, that’s actually sick, fair play.",
            "Guess killing Ore Golem's is finally paying off, huh?",
            "Hope you’re not planning to flex that all day.",
            "Alright, now I’m officially jealous.",
            "That’s a solid pull, I’ll give you that."
        };

        /// <summary>Dialogue options surfaced when the companion secures the fragment kill credit.</summary>
        private static readonly string[] OreGolemCompanionSelfDropLines =
        {
            "Must be my lucky day.",
            "I can keep this, right? <emoji=24>",
            "Now, that's lucky.",
            "Wonder where that golem was hiding that, lol.",
            "guess the golem liked me more than you.",
            "hey, look what just dropped for me!",
            "no way, i actually got one!",
            "finally, some good loot for a change.",
            "that’s going straight in my bag.",
            "looks like the gods are smiling on me today.",
            "well, would you look at that.",
            "haha, jackpot.",
            "yep, i’ll be bragging about this one later.",
            "hope you’re not too jealous, {playerName}.",
            "i swear i wasn’t even trying for it.",
            "that’s one for the collection.",
            "gotta love when luck actually shows up.",
            "oh nice, that’s rare, right?",
            "didn’t expect that drop at all.",
            "guess it’s my turn to be the lucky one.",
            "this’ll upgrade my gear nicely.",
            "you saw that, right? i wasn’t imagining it.",
            "finally, the grind pays off.",
            "maybe i should buy a lottery ticket next.",
            "oh damn, that shimmer’s pretty."
        };

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
        [SerializeField, Tooltip("Cached respawn delay used when scheduling or resuming respawns after suppression.")]
        private float pendingRespawnDelaySeconds;
        [SerializeField, Tooltip("Tracks whether respawning is temporarily suppressed by external systems (e.g. personal nodes).")]
        private bool respawnSuppressed;
        private Coroutine respawnCoroutine;
        /// <summary>Prevents the ore golem fragment logic from rolling multiple times per death.</summary>
        private bool oreGolemHadesDropResolved;

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

            hitSplatLibrary = HitSplatLibraryResolver.Resolve(hitSplatLibrary);

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
                {
                    OnKilledByPlayer?.Invoke(this, killingPlayer);
                    CompanionConversationService.RegisterNpcKill(this, killingPlayer);
                }

                if (killedByPlayer || playerDamage > npcDamage)
                    dropper?.OnDeath();

                if (killingPlayer != null)
                    TryAwardOreGolemHadesFragment(dropper, killingPlayer, source, combatSource);

                ClearDamageContributors("NPC death resolution");
                combat?.ResetCombatState();
                if (wanderer != null) wanderer.enabled = false;
                OnDeath?.Invoke();
                if (collider2D) collider2D.enabled = false;
                if (spriteRenderer) spriteRenderer.enabled = false;
                if (profile != null && profile.RespawnSeconds > 0f)
                    ScheduleRespawn(profile.RespawnSeconds);
            }

            return damageToApply;
        }

        /// <summary>Get combat stats for this NPC.</summary>
        public CombatantStats GetCombatantStats()
        {
            return CombatantStats.ForNpc(profile);
        }

        private void ScheduleRespawn(float delaySeconds)
        {
            pendingRespawnDelaySeconds = Mathf.Max(0f, delaySeconds);

            if (respawnCoroutine != null)
            {
                StopCoroutine(respawnCoroutine);
                respawnCoroutine = null;
            }

            if (respawnSuppressed)
                return;

            respawnCoroutine = StartCoroutine(RespawnRoutine(pendingRespawnDelaySeconds));
        }

        private IEnumerator RespawnRoutine(float delaySeconds)
        {
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);
            else
                yield return null;

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
            respawnCoroutine = null;
            pendingRespawnDelaySeconds = 0f;
        }

        /// <summary>
        ///     Prevents the NPC from scheduling or running a respawn routine. Any active
        ///     respawn coroutine is cancelled so external systems can control the lifecycle.
        /// </summary>
        public void SuppressRespawn()
        {
            respawnSuppressed = true;
            if (respawnCoroutine != null)
            {
                StopCoroutine(respawnCoroutine);
                respawnCoroutine = null;
            }
        }

        /// <summary>
        ///     Re-enables respawning after a suppression period. The respawn routine will be
        ///     scheduled using the supplied delay override or the cached pending delay if no
        ///     override is provided.
        /// </summary>
        /// <param name="delayOverride">
        ///     Optional delay in seconds that replaces the cached pending delay when provided.
        /// </param>
        public void ResumeRespawn(float? delayOverride = null)
        {
            respawnSuppressed = false;
            float delay = delayOverride.HasValue ? delayOverride.Value : pendingRespawnDelaySeconds;
            ScheduleRespawn(delay);
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
            ResetDamageCounters(currentHp > 0);
            if (logDamage)
            {
                string context = string.IsNullOrEmpty(reason) ? "without context" : reason;
                Debug.Log($"{name} cleared damage contributors ({context}).", this);
            }
        }

        private void ResetDamageCounters(bool resetOreGolemDrop)
        {
            playerDamage = 0;
            npcDamage = 0;
            if (resetOreGolemDrop)
                oreGolemHadesDropResolved = false;
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
        /// Attempts to award a Hades fragment when an ore golem dies to the player or their companion.
        /// </summary>
        /// <param name="dropper">Dropper component responsible for standard loot resolution.</param>
        /// <param name="killingPlayer">Player GameObject credited with the kill.</param>
        /// <param name="source">Raw damage source passed into <see cref="ApplyDamage"/>.</param>
        /// <param name="combatSource">Combat target derived from <paramref name="source"/> when available.</param>
        private void TryAwardOreGolemHadesFragment(
            NpcDropper dropper,
            GameObject killingPlayer,
            object source,
            CombatTarget combatSource)
        {
            if (oreGolemHadesDropResolved)
                return;

            if (!IsOreGolemCombatant())
                return;

            oreGolemHadesDropResolved = true;

            if (!RollOreGolemHadesFragment())
                return;

            var fragment = ItemDatabase.GetItem(HadesFragmentItemId);
            if (fragment == null)
            {
                Debug.LogError($"NpcCombatant could not locate item '{HadesFragmentItemId}' while spawning an ore golem drop.", this);
                return;
            }

            Vector3 spawnPosition = transform.position;
            var spawner = ResolveGroundItemSpawner(dropper);
            if (spawner != null)
                spawner.Spawn(fragment, 1, spawnPosition);
            else
                InventoryBridge.AddItem(fragment, 1);

            var chat = ChatService.Instance;
            chat?.PublishGameMessage("The ore golem, has dropped a Hades Fragment.");

            CompanionConversationService.RegisterEvent(
                "secured a Hades Fragment",
                CompanionEventType.Loot,
                CompanionEventMetadata.Create("You", name, null, spawnPosition));

            bool companionKill = DetermineCompanionKill(source, combatSource, killingPlayer);
            TryEmitOreGolemCompanionDialogue(chat, companionKill);
        }

        /// <summary>Determines whether this combatant represents an ore golem.</summary>
        private bool IsOreGolemCombatant()
        {
            if (string.IsNullOrWhiteSpace(OreGolemNameKeyword))
                return false;

            if (ContainsIgnoreCase(name, OreGolemNameKeyword))
                return true;

            if (profile != null && ContainsIgnoreCase(profile.name, OreGolemNameKeyword))
                return true;

            return false;
        }

        /// <summary>Executes the 1-in-N roll for the ore golem fragment drop.</summary>
        private bool RollOreGolemHadesFragment()
        {
            if (OreGolemHadesDropDenominator <= 1)
                return true;

            int roll = UnityEngine.Random.Range(1, OreGolemHadesDropDenominator + 1);
            bool success = roll == 1;

            if (logDamage)
            {
                Debug.Log($"{name} rolled Hades fragment drop 1/{OreGolemHadesDropDenominator} (rolled {roll}) => {(success ? "success" : "no drop")}.", this);
            }

            return success;
        }

        /// <summary>Resolves the ground item spawner used when forcing ore golem drops.</summary>
        private GroundItemSpawner ResolveGroundItemSpawner(NpcDropper dropper)
        {
            if (dropper != null && dropper.spawner != null)
                return dropper.spawner;

            if (sharedGroundItemSpawner == null)
                sharedGroundItemSpawner = FindObjectOfType<GroundItemSpawner>();

            return sharedGroundItemSpawner;
        }

        /// <summary>Determines whether the companion delivered the killing blow for dialogue selection.</summary>
        private bool DetermineCompanionKill(object source, CombatTarget combatSource, GameObject killingPlayer)
        {
            if (!CompanionManager.HasActiveCompanion)
                return false;

            var activeCompanion = CompanionManager.ActiveCompanion;
            if (activeCompanion == null)
                return false;

            if (killingPlayer == activeCompanion.gameObject)
                return true;

            if (source is Component sourceComponent && sourceComponent.GetComponentInParent<CompanionController>() != null)
                return true;

            if (source is GameObject sourceObject && sourceObject.GetComponentInParent<CompanionController>() != null)
                return true;

            if (combatSource is Component combatComponent && combatComponent.GetComponentInParent<CompanionController>() != null)
                return true;

            return false;
        }

        /// <summary>Publishes companion dialogue describing the ore golem fragment drop.</summary>
        private void TryEmitOreGolemCompanionDialogue(ChatService chatService, bool companionKill)
        {
            if (!CompanionManager.HasActiveCompanion || chatService == null)
                return;

            string[] sourceLines = companionKill ? OreGolemCompanionSelfDropLines : OreGolemCompanionPlayerDropLines;
            if (sourceLines == null || sourceLines.Length == 0)
                return;

            if (!companionKill && UnityEngine.Random.value > 0.5f)
                return;

            int index = UnityEngine.Random.Range(0, sourceLines.Length);
            string template = sourceLines[index];
            if (string.IsNullOrWhiteSpace(template))
                return;

            string playerName = chatService.ActiveUsername;
            string replacement = string.IsNullOrWhiteSpace(playerName) ? "you" : playerName.Trim();
            string line = template.Replace("{playerName}", replacement);

            string speaker = CompanionManager.GetCompanionDisplayName();
            chatService.PublishCompanionMessage(speaker, line);
        }

        /// <summary>Performs a case-insensitive substring test.</summary>
        private static bool ContainsIgnoreCase(string source, string value)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(value) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
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
                return follower.Player.gameObject;

            return null;
        }
    }
}
