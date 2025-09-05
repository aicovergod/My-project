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
            if (inventory != null && inventory.InShop && eventData.button == PointerEventData.InputButton.Left)
            {
                if (shift) inventory.PromptStackSplit(index, StackSplitType.Sell);
                else       inventory.SellItem(index, 1);
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                var entry = inventory.GetSlot(index);
                if (inventory.selectedIndex < 0)
                {
                    if (entry.item != null)
                    {
                        if (entry.item.equipmentSlot != EquipmentSlot.None)
                        {
                            inventory.EquipItem(index);
                            return;
                        }

                        inventory.selectedIndex = index;
                        inventory.UpdateSlotVisual(index);
                    }
                }
                else if (inventory.selectedIndex == index)
                {
                    inventory.ClearSelection();
                    inventory.UpdateSlotVisual(index);
                }
                else
                {
                    inventory.CombineItems(inventory.selectedIndex, index);
                    int prev = inventory.selectedIndex;
                    inventory.ClearSelection();
                    inventory.UpdateSlotVisual(prev);
                    inventory.UpdateSlotVisual(index);
                }
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (shift)
                {
                    inventory?.PromptStackSplit(index, StackSplitType.Drop);
                }
                else
                {
                    var entry = inventory != null ? inventory.GetSlot(index) : default;
                    if (inventory != null && entry.count > 1)
                        inventory.ShowDropMenu(index, eventData.position);
                    else
                        inventory?.DropItem(index, 1);
                }
            }
        }
    }
}
