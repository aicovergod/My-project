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
        [SerializeField] private CombatController playerController;

        private void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
            if (playerController == null)
                playerController = FindObjectOfType<CombatController>();
        }

        private void OnMouseDown()
        {
            if (playerController == null)
                return;
            if (Vector2.Distance(playerController.transform.position, transform.position) > CombatMath.MELEE_RANGE)
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
