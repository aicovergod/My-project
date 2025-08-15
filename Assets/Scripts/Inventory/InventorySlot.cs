using UnityEngine;
using UnityEngine.EventSystems;

namespace Inventory
{
    /// <summary>
    /// Handles pointer hover events for an inventory slot to display tooltips.
    /// </summary>
    public class InventorySlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [HideInInspector]
        public Inventory inventory;
        [HideInInspector]
        public int index;

        public void OnPointerEnter(PointerEventData eventData)
        {
            inventory?.ShowTooltip(index, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            inventory?.HideTooltip();
        }
    }
}
