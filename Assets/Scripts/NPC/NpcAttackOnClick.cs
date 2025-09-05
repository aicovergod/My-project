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
            var playerMover = playerController.GetComponent<PlayerMover>();
            if (playerMover == null)
                return;

            void AttemptAttack()
            {
                if (playerController.TryAttackTarget(combatant))
                {
                    var npcAttack = GetComponent<NpcAttackController>();
                    var playerTarget = playerController.GetComponent<PlayerCombatTarget>();
                    npcAttack?.BeginAttacking(playerTarget);
                }
            }

            if (Vector2.Distance(playerController.transform.position, transform.position) > CombatMath.MELEE_RANGE)
            {
                playerMover.MoveTo(transform.position, CombatMath.MELEE_RANGE, AttemptAttack);
            }
            else
            {
                AttemptAttack();
            }
        }
    }
}
