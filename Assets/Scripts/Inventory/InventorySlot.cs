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

        public void OnPointerClick(PointerEventData eventData)
        {
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
