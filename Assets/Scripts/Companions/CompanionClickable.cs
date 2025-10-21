using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Handles world interaction for the companion so players can open the context menu via right-click
    /// just like standard pets.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class CompanionClickable : MonoBehaviour
    {
        private void Awake()
        {
            var collider = GetComponent<Collider2D>();
            collider.isTrigger = true;
        }

        private void OnMouseOver()
        {
            if (Input.GetMouseButtonDown(1))
                CompanionManager.ShowContextMenu(Input.mousePosition);
        }

        /// <summary>Invoked by context menu actions to store the companion.</summary>
        public void PickUp()
        {
            CompanionManager.SetStored(true);
        }
    }
}
