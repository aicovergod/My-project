using Inventory.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Inventory
{
    /// <summary>
    /// Handles pointer forwarding for an inventory slot. All gameplay decisions are
    /// routed through <see cref="IInventoryUIActions"/> so the window controller owns
    /// presentation details.
    /// </summary>
    public sealed class InventorySlot : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler,
        IPointerClickHandler
    {
        private IInventoryUIActions actions;
        private int index;

        /// <summary>
        /// Binds the slot to a controller implementation.
        /// </summary>
        public void Initialize(IInventoryUIActions actions, int slotIndex)
        {
            this.actions = actions;
            index = slotIndex;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            var rect = transform as RectTransform;
            actions?.HandlePointerEnter(index, rect);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            actions?.HandlePointerExit(index);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (actions == null || actions.IsBankOpen)
                return;

            actions.HandleBeginDrag(index);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (actions == null || actions.IsBankOpen)
                return;

            actions.HandleDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (actions == null || actions.IsBankOpen)
                return;

            actions.HandleEndDrag(index);
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (actions == null || actions.IsBankOpen)
                return;

            actions.HandleDrop(index);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            actions?.HandlePointerClick(index, eventData);
        }
    }
}
