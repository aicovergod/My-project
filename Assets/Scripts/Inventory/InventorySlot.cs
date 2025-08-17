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
        IDropHandler,
        IPointerClickHandler
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
            if (inventory != null && inventory.BankOpen) return;
            inventory?.BeginDrag(index);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (inventory != null && inventory.BankOpen) return;
            inventory?.Drag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (inventory != null && inventory.BankOpen) return;
            inventory?.EndDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (inventory != null && inventory.BankOpen) return;
            inventory?.Drop(index);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (inventory != null && inventory.BankOpen)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                    BankSystem.BankUI.Instance?.DepositFromInventory(index);
                else if (eventData.button == PointerEventData.InputButton.Right)
                    BankSystem.BankUI.Instance?.ShowDepositMenu(index, eventData.position);
                return;
            }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (!eventData.dragging)
                {
                    if (shift)
                        inventory?.PromptStackSplit(index, StackSplitType.Sell);
                    else
                        inventory?.SellItem(index, 1);
                }
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (shift)
                    inventory?.PromptStackSplit(index, StackSplitType.Drop);
                else
                    inventory?.DropItem(index, 1);
            }
        }
    }
}
