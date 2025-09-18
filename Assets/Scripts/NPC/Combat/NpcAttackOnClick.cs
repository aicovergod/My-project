using System.Collections;
using UnityEngine;
using Combat;
using Player;
using Status.Freeze;
using UI;

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
            if (heldAttackRoutine != null)
            {
                StopCoroutine(heldAttackRoutine);
                heldAttackRoutine = null;
            }
        }

        private void OnMouseDown()
        {
            var playerController = FindObjectOfType<CombatController>();
            if (playerController == null)
                return;
            var playerMover = playerController.GetComponent<PlayerMover>();
            if (playerMover == null)
                return;

            if (heldAttackRoutine != null)
            {
                StopCoroutine(heldAttackRoutine);
                heldAttackRoutine = null;
            }

            // Determine whether the player is currently frozen so we can decide how to handle
            // the attack click. Frozen players should not be able to move but should retain the
            // attack command so it can fire if the NPC walks into range.
            var freezeController = playerController.GetComponent<FrozenStatusController>()
                ?? playerMover.GetComponent<FrozenStatusController>();
            bool playerFrozen = freezeController != null && freezeController.IsFrozen;

            float range = MagicUI.GetActiveSpellRange();
            float distance = Vector2.Distance(playerController.transform.position, transform.position);

            // Always try to attack immediately when already in range; this covers both frozen and
            // unfrozen states.
            if (distance <= range)
            {
                playerController.TryAttackTarget(combatant);
                return;
            }

            if (playerFrozen)
            {
                // The player is frozen and out of range. Hold the attack command and repeatedly
                // check whether the NPC moves into range or the freeze expires.
                heldAttackRoutine = StartCoroutine(HoldAttackWhileFrozen(
                    playerController,
                    playerMover,
                    freezeController,
                    combatant));
                return;
            }

            // Default behaviour for mobile players remains unchanged – auto walk into range and
            // fire once close enough.
            playerMover.MoveTo(transform, range, () => playerController.TryAttackTarget(combatant));
        }

        /// <summary>
        /// Waits while the player is frozen and out of spell range. Once the NPC moves close
        /// enough or the freeze ends the stored attack command is executed automatically.
        /// </summary>
        private IEnumerator HoldAttackWhileFrozen(
            CombatController playerController,
            PlayerMover playerMover,
            FrozenStatusController freezeController,
            NpcCombatant target)
        {
            // Small guard to avoid running the routine when any critical dependency is missing.
            if (playerController == null || playerMover == null || freezeController == null || target == null)
            {
                heldAttackRoutine = null;
                yield break;
            }

            while (playerController != null && freezeController != null && freezeController.IsFrozen && target != null && target.IsAlive)
            {
                float range = MagicUI.GetActiveSpellRange();
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
                float range = MagicUI.GetActiveSpellRange();
                float distance = Vector2.Distance(playerController.transform.position, target.transform.position);

                if (distance <= range)
                {
                    playerController.TryAttackTarget(target);
                }
                else if (!freezeController.IsFrozen && playerMover != null)
                {
                    // Once the freeze effect ends resume the standard auto movement logic so the
                    // player chases the NPC if they are still out of range.
                    playerMover.MoveTo(target.transform, range, () => playerController.TryAttackTarget(target));
                }
            }

            heldAttackRoutine = null;
        }
    }
}
