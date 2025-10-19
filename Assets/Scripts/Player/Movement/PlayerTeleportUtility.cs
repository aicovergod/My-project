using Player;
using Pets;
using UnityEngine;

namespace Player.Movement
{
    /// <summary>
    /// Shared helper that moves the player (and their active pet) to a new world position and persists the result.
    /// Used by debug tooling such as the minimap and chat command system.
    /// </summary>
    public static class PlayerTeleportUtility
    {
        private const float PetFollowOffset = 0.5f;

        /// <summary>
        /// Teleports the player using transient caches created for the duration of the call.
        /// </summary>
        public static bool TryTeleportPlayer(Vector3 worldPosition, out string errorMessage)
        {
            PlayerMover moverCache = null;
            Transform transformCache = null;
            return TryTeleportPlayer(worldPosition, ref moverCache, ref transformCache, out errorMessage);
        }

        /// <summary>
        /// Teleports the player while updating the supplied caches to minimise future lookups.
        /// </summary>
        public static bool TryTeleportPlayer(Vector3 worldPosition, ref PlayerMover moverCache, ref Transform transformCache, out string errorMessage)
        {
            PlayerMover mover = moverCache;
            Transform playerTransform = transformCache;

            if (mover == null)
            {
                if (playerTransform != null)
                {
                    mover = playerTransform.GetComponent<PlayerMover>();
                }

                if (mover == null)
                {
                    GameObject playerObj = playerTransform != null ? playerTransform.gameObject : GameObject.FindGameObjectWithTag("Player");
                    if (playerObj != null)
                    {
                        mover = playerObj.GetComponent<PlayerMover>();
                        playerTransform = playerObj.transform;
                    }
                }
            }
            else if (playerTransform == null)
            {
                playerTransform = mover.transform;
            }

            if (mover == null)
            {
                errorMessage = "Unable to locate the PlayerMover component.";
                return false;
            }

            playerTransform ??= mover.transform;

            mover.StopMovement();

            Vector3 currentPosition = playerTransform.position;
            Vector3 newPosition = new Vector3(worldPosition.x, worldPosition.y, currentPosition.z);
            playerTransform.position = newPosition;

            GameObject pet = PetDropSystem.ActivePetObject;
            if (pet != null)
            {
                Vector3 petPosition = newPosition + Vector3.right * PetFollowOffset;
                petPosition.z = pet.transform.position.z;
                pet.transform.position = petPosition;

                var follower = pet.GetComponent<PetFollower>();
                if (follower != null)
                    follower.SetPlayer(playerTransform);
            }

            mover.SavePosition();

            moverCache = mover;
            transformCache = playerTransform;
            errorMessage = string.Empty;
            return true;
        }
    }
}
