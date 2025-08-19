using UnityEngine;
using Combat;
using Player;

namespace NPC
{
    /// <summary>
    /// Allows the player to left click an NPC to begin combat.
    /// </summary>
    [RequireComponent(typeof(NpcCombatant))]
    public class NpcAttackOnClick : MonoBehaviour
    {
        private NpcCombatant combatant;

        private void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
        }

        private void OnMouseDown()
        {
            var playerController = FindObjectOfType<CombatController>();
            if (playerController == null)
                return;
            if (playerController.TryAttackTarget(combatant))
            {
                var npcAttack = GetComponent<NpcAttackController>();
                var playerTarget = playerController.GetComponent<PlayerCombatTarget>();
                npcAttack?.BeginAttacking(playerTarget);
            }
        }
    }
}
