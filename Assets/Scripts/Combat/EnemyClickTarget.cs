using UnityEngine;
using Player;

namespace Combat
{
    /// <summary>
    /// Enables clicking on an enemy with the left mouse button to begin combat.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyClickTarget : MonoBehaviour
    {
        private Enemy enemy;

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
        }

        private void OnMouseDown()
        {
            if (!Input.GetMouseButtonDown(0) || enemy == null)
                return;

            var player = FindObjectOfType<PlayerCombat>();
            if (player == null)
                return;

            player.SetTarget(enemy);
            CombatManager.Instance?.Engage(player, enemy);
        }
    }
}
