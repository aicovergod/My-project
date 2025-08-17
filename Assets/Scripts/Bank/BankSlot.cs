using UnityEngine;
using UnityEngine.EventSystems;

namespace BankSystem
{
    /// <summary>
    /// Handles pointer events for bank slots including clicks, hover tooltips
    /// and drag-and-drop reordering of items inside the bank.
    /// </summary>
    public class BankSlot : MonoBehaviour,
        IPointerClickHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler
    {
        [HideInInspector] public BankUI bank;
        [HideInInspector] public int index;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging)
                bank?.Withdraw(index);
            else if (eventData.button == PointerEventData.InputButton.Right && !eventData.dragging)
                bank?.ShowWithdrawMenu(index, eventData.position);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            bank?.ShowTooltip(index, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            bank?.HideTooltip();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            bank?.BeginDrag(index);
        }

        public void OnDrag(PointerEventData eventData)
        {
            bank?.Drag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            bank?.EndDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            bank?.Drop(index);
        }
    }
}
