using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Core;
using World;
using Combat;

namespace Player
{
    /// <summary>
    /// Listens for player death and handles respawning with a screen fade.
    /// </summary>
    public class PlayerRespawnSystem : MonoBehaviour
    {
        public static PlayerRespawnSystem Instance { get; private set; }

        private PlayerHitpoints hitpoints;
        private PlayerMover playerMover;
        private CombatController combatController;
        private bool isRespawning;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            FindPlayer();
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
            }
            else
            {
                hitpoints = null;
                playerMover = null;
                combatController = null;
            }
            if (hitpoints != null)
                hitpoints.OnHealthChanged += HandleHealthChanged;
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (!isRespawning && current <= 0)
            {
                playerMover?.StopMovement();
                combatController?.CancelCombat();
                StartCoroutine(RespawnRoutine());
            }
        }

        private IEnumerator RespawnRoutine()
        {
            isRespawning = true;

            var fader = GameManager.ScreenFader;
            if (fader != null)
                yield return fader.FadeOut();

            if (RespawnPoint.Current != null && hitpoints != null)
                hitpoints.transform.position = RespawnPoint.Current.transform.position;

            if (hitpoints != null)
                hitpoints.Heal(hitpoints.MaxHp);

            if (fader != null)
                yield return fader.FadeIn();

            isRespawning = false;
        }
    }
}
