using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Core;
using World;
using Combat;
using Audio;
using Status;
using Status.Poison;
using Companions;
using UI.Chat;

namespace Player
{
    /// <summary>
    /// Listens for player death and handles respawning with a screen fade.
    /// </summary>
    public class PlayerRespawnSystem : ScenePersistentObject
    {
        public static PlayerRespawnSystem Instance { get; private set; }

        private PlayerHitpoints hitpoints;
        private PlayerMover playerMover;
        private CombatController combatController;
        private PoisonController poisonController;
        private bool isRespawning;
        private string cachedRespawnScene;
        private string cachedSpawnPointId;
        private Vector3 cachedFallbackPosition;
        private bool hasCachedRespawnData;
        /// <summary>Rolling list of recent player death timestamps used to modulate companion tone.</summary>
        private readonly List<float> recentDeathTimestamps = new List<float>();

        /// <summary>Delegate used to fetch the final chat line. Tests can temporarily replace this selector.</summary>
        private static Func<CompanionChatTone, string> playerDeathLineSelector = CompanionChatLibrary.GetRandomPlayerDeathLine;

        /// <summary>Amount of real time the player has to avoid rapid-fire deaths before the tone resets.</summary>
        private const float SupportiveDeathWindowSeconds = 45f;

        /// <summary>Number of deaths inside <see cref="SupportiveDeathWindowSeconds"/> required before switching tones.</summary>
        private const int SupportiveDeathThreshold = 3;

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            base.Awake();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            FindPlayer();

            // When loading into a scene mid-session (e.g. via save), ensure the
            // current respawn marker is captured even if it enabled before the
            // respawn system finished bootstrapping.
            if (RespawnPoint.Current != null)
                RegisterRespawnPoint(RespawnPoint.Current);
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (hitpoints != null)
                hitpoints.OnHealthChanged -= HandleHealthChanged;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            FindPlayer();
        }

        private void FindPlayer()
        {
            if (hitpoints != null)
                hitpoints.OnHealthChanged -= HandleHealthChanged;

            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                hitpoints = playerObj.GetComponent<PlayerHitpoints>();
                playerMover = playerObj.GetComponent<PlayerMover>();
                combatController = playerObj.GetComponent<CombatController>();
                poisonController = playerObj.GetComponent<PoisonController>();
            }
            else
            {
                hitpoints = null;
                playerMover = null;
                combatController = null;
                poisonController = null;
            }
            if (hitpoints != null)
                hitpoints.OnHealthChanged += HandleHealthChanged;
        }

        /// <summary>
        /// Captures the scene, spawn identifier and fallback position exposed by an
        /// overworld <see cref="RespawnPoint"/> so the respawn routine can safely
        /// restore the player even after the original scene unloads.
        /// </summary>
        public void RegisterRespawnPoint(RespawnPoint point)
        {
            if (point == null)
                return;

            cachedRespawnScene = point.SceneName;
            var identifier = point.SpawnIdentifier;
            cachedSpawnPointId = string.IsNullOrWhiteSpace(identifier) ? null : identifier;
            cachedFallbackPosition = point.WorldPosition;
            hasCachedRespawnData = true;
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (!isRespawning && current <= 0)
            {
                playerMover?.StopMovement();
                combatController?.CancelCombat();
                CompanionChatTone tone = RegisterDeathForToneEvaluation(Time.unscaledTime);
                TryPublishCompanionDeathLine(tone);
                if (poisonController == null && hitpoints != null)
                {
                    // Cache the poison controller the first time it is needed so subsequent deaths
                    // can immediately clear lingering poison damage-over-time effects.
                    poisonController = hitpoints.GetComponent<PoisonController>();

                    // If the hitpoints component lives on a different GameObject than the poison
                    // controller, rebuild all cached player references so the lookup succeeds.
                    if (poisonController == null)
                        FindPlayer();
                }
                poisonController?.CurePoison(0f);
                if (BuffTimerService.Instance != null && hitpoints != null)
                    BuffTimerService.Instance.RemoveAllBuffs(hitpoints.gameObject, BuffEndReason.Manual);
                // Play the classic OSRS-style death jingle before beginning the respawn sequence.
                SoundManager.Instance.PlaySfx(SoundEffect.PlayerDeath);
                StartCoroutine(RespawnRoutine());
            }
        }

        /// <summary>
        /// Publishes a random death line from the active companion when the player dies using the supplied tone.
        /// Keeps the companion flavour consistent with the shared chat library.
        /// </summary>
        /// <param name="tone">Tone requested by the recent-death heuristic.</param>
        private static void TryPublishCompanionDeathLine(CompanionChatTone tone)
        {
            if (!CompanionManager.HasActiveCompanion)
                return;

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            var selector = playerDeathLineSelector ?? CompanionChatLibrary.GetRandomPlayerDeathLine;
            string message = selector(tone);
            if (string.IsNullOrWhiteSpace(message))
                return;

            string companionName = CompanionManager.GetCompanionDisplayName();
            chat.PublishCompanionMessage(companionName, message);
        }

        /// <summary>
        /// Registers the latest death timestamp and resolves which tone the companion should use.
        /// </summary>
        /// <param name="timestamp">Unscaled time the death occurred.</param>
        /// <returns>The tone the companion should adopt for the next death quip.</returns>
        private CompanionChatTone RegisterDeathForToneEvaluation(float timestamp)
        {
            // Remove any entries that fall outside of the configured window so stale deaths do not skew the tone.
            for (int i = recentDeathTimestamps.Count - 1; i >= 0; i--)
            {
                if (timestamp - recentDeathTimestamps[i] > SupportiveDeathWindowSeconds)
                    recentDeathTimestamps.RemoveAt(i);
            }

            recentDeathTimestamps.Add(timestamp);

            // Clamp the list to the threshold size to avoid unbounded growth when the player repeatedly dies in-bounds.
            if (recentDeathTimestamps.Count > SupportiveDeathThreshold)
                recentDeathTimestamps.RemoveRange(0, recentDeathTimestamps.Count - SupportiveDeathThreshold);

            return recentDeathTimestamps.Count >= SupportiveDeathThreshold
                ? CompanionChatTone.Supportive
                : CompanionChatTone.Snarky;
        }

        private IEnumerator RespawnRoutine()
        {
            isRespawning = true;

            try
            {
                // Always work with fresh component references because scene reloads or
                // prefab swaps can replace the player object during death sequences.
                FindPlayer();

                var fader = GameManager.ScreenFader;
                string activeScene = SceneManager.GetActiveScene().name;
                bool hasRespawnScene = !string.IsNullOrEmpty(cachedRespawnScene);
                bool requiresSceneSwap = hasRespawnScene && cachedRespawnScene != activeScene;
                bool usedTransitionManager = false;

                // If we are not changing scenes or no transition manager exists, fade
                // out immediately to hide the respawn process.
                if (!requiresSceneSwap || SceneTransitionManager.Instance == null)
                {
                    if (fader != null)
                        yield return fader.FadeOut();
                }

                if (requiresSceneSwap)
                {
                    if (SceneTransitionManager.Instance != null)
                    {
                        usedTransitionManager = true;
                        yield return SceneTransitionManager.Instance.Transition(cachedRespawnScene, cachedSpawnPointId, null, false);
                    }
                    else
                    {
                        yield return LoadRespawnSceneDirectly(cachedRespawnScene);
                    }
                }

                // Ensure our cached references point at any player instance that exists
                // in the now-active scene.
                FindPlayer();

                if (hitpoints == null)
                    yield break;

                Vector3 targetPosition = hasCachedRespawnData ? cachedFallbackPosition : hitpoints.transform.position;
                var currentRespawn = RespawnPoint.Current;
                if (currentRespawn != null)
                    targetPosition = currentRespawn.transform.position;

                if (playerMover != null)
                {
                    playerMover.StopMovement();
                    playerMover.transform.position = targetPosition;
                }

                if (hitpoints.transform.position != targetPosition)
                    hitpoints.transform.position = targetPosition;

                hitpoints.RestoreToFullHealth();

                if (usedTransitionManager)
                {
                    // Wait for the fade handled by the transition manager so the
                    // respawn does not release control while a transition is active.
                    while (SceneTransitionManager.IsTransitioning)
                        yield return null;
                }
                else if (fader != null)
                {
                    yield return fader.FadeIn();
                }
            }
            finally
            {
                isRespawning = false;
            }
        }

        /// <summary>
        /// Fallback scene loading flow used when the <see cref="SceneTransitionManager"/>
        /// singleton is not present in the project.  The routine mirrors the
        /// persistent-object handling that the manager performs so respawns continue
        /// to work in stripped-down scenes (e.g. tests or debug setups).
        /// </summary>
        private IEnumerator LoadRespawnSceneDirectly(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                yield break;

            // Promote existing persistent objects so they survive the scene change and
            // can be moved into the newly loaded scene manually.
            var persistentObjects = FindObjectsOfType<ScenePersistentObject>(true);
            foreach (var persistent in persistentObjects)
            {
                if (persistent != null)
                    persistent.OnBeforeSceneUnload();
            }

            var loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            while (loadOperation != null && !loadOperation.isDone)
                yield return null;

            var loadedScene = SceneManager.GetSceneByName(sceneName);
            if (loadedScene.IsValid())
            {
                SceneManager.SetActiveScene(loadedScene);

                foreach (var persistent in persistentObjects)
                {
                    if (persistent != null)
                        persistent.OnAfterSceneLoad(loadedScene);
                }
            }
        }
    }
}
