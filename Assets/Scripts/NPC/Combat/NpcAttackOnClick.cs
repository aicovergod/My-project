using System.Collections;
using UnityEngine;
using Combat;
using Player;
using Player.Movement;
using Status.Freeze;
using UI.Utilities;

namespace NPC
{
    /// <summary>
    /// Allows the player to left click an NPC to begin combat.
    /// </summary>
    [RequireComponent(typeof(NpcCombatant))]
    public class NpcAttackOnClick : MonoBehaviour
    {
        private NpcCombatant combatant;

        /// <summary>
        /// Tracks any coroutine that is holding an attack command while the player is frozen so
        /// multiple clicks do not spawn duplicate routines.
        /// </summary>
        private Coroutine heldAttackRoutine;

        private void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
        }

        private void OnDisable()
        {
            // Ensure pending routines do not leak when this component is disabled or destroyed.
            CancelHeldAttackRoutine();
        }

        private void OnMouseDown()
        {
            TryCommandPlayerAttack();
        }

        /// <summary>
        /// Attempts to initiate the player's standard combat behaviour against this NPC.
        /// </summary>
        /// <param name="ignorePointerBlockers">
        /// When true the method bypasses UI raycast checks. Context menus set this so the button can be pressed while hovering UI.
        /// </param>
        /// <returns>True when an attack command was issued or queued.</returns>
        public bool TryCommandPlayerAttack(bool ignorePointerBlockers = false)
        {
            if (!ignorePointerBlockers && PointerRaycastUtility.IsPointerOverBlockingUI(Input.mousePosition))
                return false;

            var playerController = FindObjectOfType<CombatController>();
            if (playerController == null)
                return false;

            var movementController = playerController.GetComponent<PlayerMovementController>()
                ?? playerController.GetComponent<PlayerMover>()?.MovementController;
            if (movementController == null)
                return false;

            CancelHeldAttackRoutine();

            var moverFacade = playerController.GetComponent<PlayerMover>() ?? playerController.GetComponentInChildren<PlayerMover>();
            var freezeController = playerController.GetComponent<FrozenStatusController>()
                ?? moverFacade?.GetComponent<FrozenStatusController>();
            bool playerFrozen = freezeController != null && freezeController.IsFrozen;

            float range = playerController.CurrentAttackRange;
            float distance = Vector2.Distance(playerController.transform.position, transform.position);

            if (distance <= range)
            {
                playerController.TryAttackTarget(combatant);
                return true;
            }

            if (playerFrozen)
            {
                heldAttackRoutine = StartCoroutine(HoldAttackWhileFrozen(
                    playerController,
                    movementController,
                    freezeController,
                    combatant));
                return true;
            }

            movementController.MoveTo(transform, range, () => playerController.TryAttackTarget(combatant));
            return true;
        }

        /// <summary>
        /// Stops and clears any active held-attack coroutine so duplicate routines are not queued.
        /// </summary>
        private void CancelHeldAttackRoutine()
        {
            if (heldAttackRoutine == null)
                return;

            StopCoroutine(heldAttackRoutine);
            heldAttackRoutine = null;
        }

        /// <summary>
        /// Waits while the player is frozen and out of spell range. Once the NPC moves close
        /// enough or the freeze ends the stored attack command is executed automatically.
        /// </summary>
        private IEnumerator HoldAttackWhileFrozen(
            CombatController playerController,
            IPlayerMovementController movementController,
            FrozenStatusController freezeController,
            NpcCombatant target)
        {
            // Small guard to avoid running the routine when any critical dependency is missing.
            if (playerController == null || movementController == null || freezeController == null || target == null)
            {
                heldAttackRoutine = null;
                yield break;
            }

            while (playerController != null && freezeController != null && freezeController.IsFrozen && target != null && target.IsAlive)
            {
                float range = playerController.CurrentAttackRange;
                float distance = Vector2.Distance(playerController.transform.position, target.transform.position);

                // Attack immediately if the NPC wanders into range while the player is frozen.
                if (distance <= range)
                {
                    if (playerController.TryAttackTarget(target))
                        break;
                }

                yield return null;
            }

            if (playerController != null && freezeController != null && target != null && target.IsAlive)
            {
                float range = playerController.CurrentAttackRange;
                float distance = Vector2.Distance(playerController.transform.position, target.transform.position);

                if (distance <= range)
                {
                    playerController.TryAttackTarget(target);
                }
                else if (!freezeController.IsFrozen && movementController != null)
                {
                    // Once the freeze effect ends resume the standard auto movement logic so the
                    // player chases the NPC if they are still out of range.
                    movementController.MoveTo(target.transform, range, () => playerController.TryAttackTarget(target));
                }
            }

            heldAttackRoutine = null;
        }
    }
}
