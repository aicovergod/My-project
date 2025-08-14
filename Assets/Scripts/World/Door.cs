using UnityEngine;
using UnityEngine.SceneManagement;

namespace World
{
    /// <summary>
    /// Simple door interaction.  When the player clicks on the door the specified
    /// scene is loaded.  If a required item ID is provided the player must possess
    /// that item in their inventory to use the door.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Door : MonoBehaviour
    {
        [Tooltip("Name of the scene to load when this door is used.")]
        public string sceneToLoad;

        [Tooltip("Optional item ID required to use this door.  Leave empty for no requirement.")]
        public string requiredItemId;

        private void OnMouseDown()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            Inventory.Inventory inv = player.GetComponent<Inventory.Inventory>();
            if (!string.IsNullOrEmpty(requiredItemId))
            {
                if (inv == null || !inv.HasItem(requiredItemId))
                {
                    // Player doesn't have the required item
                    return;
                }
            }

            if (!string.IsNullOrEmpty(sceneToLoad))
            {
                SceneManager.LoadScene(sceneToLoad);
            }
        }
    }
}