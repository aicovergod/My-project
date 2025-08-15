using UnityEngine;
using UnityEngine.EventSystems;

namespace Inventory
{
    /// <summary>
    /// Handles pointer hover events for an inventory slot to display tooltips.
    /// </summary>
    public class InventorySlot : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler
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

        public void OnBeginDrag(PointerEventData eventData)
        {
            inventory?.BeginDrag(index);
        }

        public void OnDrag(PointerEventData eventData)
        {
            inventory?.Drag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            inventory?.EndDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            inventory?.Drop(index);
        }
    }
}
