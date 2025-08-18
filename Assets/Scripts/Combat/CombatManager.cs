using System.Collections;
using UnityEngine;
using Player;

namespace Combat
{
    /// <summary>
    /// Centralised tick based combat resolver. Handles attacks between
    /// a player and a single enemy at a time, using Old School RuneScape's
    /// 0.6 second tick rhythm.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatManager : MonoBehaviour
    {
        public const float TickLength = 0.6f; // seconds
        public static CombatManager Instance { get; private set; }

        public event System.Action<PlayerCombat, Enemy> OnCombatStarted;
        public event System.Action<PlayerCombat, Enemy> OnCombatEnded;

        private Coroutine activeRoutine;
        private PlayerCombat activePlayer;
        private Enemy activeEnemy;

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

        public void Engage(PlayerCombat player, Enemy enemy)
        {
            if (player == null || enemy == null)
                return;

            Disengage();

            activePlayer = player;
            activeEnemy = enemy;
            Debug.Log($"Combat started between {player.name} and {enemy.name}.");
            OnCombatStarted?.Invoke(player, enemy);
            activeRoutine = StartCoroutine(CombatRoutine(player, enemy));
        }

        public void Disengage()
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
                OnCombatEnded?.Invoke(activePlayer, activeEnemy);
                activePlayer = null;
                activeEnemy = null;
                Debug.Log("Combat disengaged.");
            }
        }

        private IEnumerator CombatRoutine(PlayerCombat player, Enemy enemy)
        {
            int playerTimer = 0;
            int enemyTimer = 0;
            var playerHp = player.Hitpoints;

            while (playerHp.CurrentHp > 0 && enemy.CurrentHitpoints > 0)
            {
                yield return new WaitForSeconds(TickLength);
                playerTimer++;
                enemyTimer++;

                if (playerTimer >= player.AttackSpeedTicks)
                {
                    int dmg = Mathf.Max(0, player.AttackRoll() - enemy.Defence);
                    enemy.ApplyDamage(dmg);
                    playerHp.OnPlayerDealtDamage(dmg);
                    Debug.Log($"Player attacked {enemy.name} for {dmg} damage.");
                    playerTimer = 0;
                }

                if (enemyTimer >= enemy.AttackSpeedTicks)
                {
                    int dmg = Mathf.Max(0, enemy.Attack - player.Defence);
                    if (dmg > 0)
                    {
                        enemy.DealDamage(playerHp, dmg);
                        Debug.Log($"{enemy.name} attacked player for {dmg} damage.");
                    }
                    enemyTimer = 0;
                }
            }

            OnCombatEnded?.Invoke(player, enemy);
            activeRoutine = null;
            activePlayer = null;
            activeEnemy = null;
        }
    }
}
