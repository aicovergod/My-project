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
            StartCoroutine(CombatRoutine(player, enemy));
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
                    playerTimer = 0;
                }

                if (enemyTimer >= enemy.AttackSpeedTicks)
                {
                    int dmg = Mathf.Max(0, enemy.Attack - player.Defence);
                    if (dmg > 0)
                        enemy.DealDamage(playerHp, dmg);
                    enemyTimer = 0;
                }
            }
        }
    }
}
