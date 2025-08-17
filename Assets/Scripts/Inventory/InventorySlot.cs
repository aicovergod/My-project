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
        public InventoryModel model;
        [HideInInspector]
        public InventoryDragHandler drag;
        [HideInInspector]
        public InventoryTooltip tooltip;
        [HideInInspector]
        public InventoryUI ui;
        [HideInInspector]
        public int index;

        public void OnPointerEnter(PointerEventData eventData)
        {
            tooltip?.ShowTooltip(index, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            tooltip?.HideTooltip();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            drag?.BeginDrag(index);
        }

        public void OnDrag(PointerEventData eventData)
        {
            drag?.Drag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            drag?.EndDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            drag?.Drop(index);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
              if (eventData.button == PointerEventData.InputButton.Left)
              {
                  if (!eventData.dragging)
                  {
                      if (shift)
                          ui?.PromptStackSplit(index, StackSplitType.Sell);
                      else
                          model?.SellItem(index, 1);
                  }
              }
              else if (eventData.button == PointerEventData.InputButton.Right)
              {
                  if (shift)
                      ui?.PromptStackSplit(index, StackSplitType.Drop);
                  else
                      model?.DropItem(index, 1);
              }
        }
    }
}
