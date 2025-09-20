using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Core;
using World;
using Combat;
using Audio;
using Status;
using Status.Poison;

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
                poisonController?.CurePoison(0f);
                if (BuffTimerService.Instance != null && hitpoints != null)
                    BuffTimerService.Instance.RemoveAllBuffs(hitpoints.gameObject, BuffEndReason.Manual);
                // Play the classic OSRS-style death jingle before beginning the respawn sequence.
                SoundManager.Instance.PlaySfx(SoundEffect.PlayerDeath);
                StartCoroutine(RespawnRoutine());
            }
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
