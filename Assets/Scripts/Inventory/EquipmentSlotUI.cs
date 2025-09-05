// Assets/Scripts/Inventory/EquipmentSlotUI.cs
using UnityEngine;
using UnityEngine.EventSystems;

namespace Inventory
{
    /// <summary>
    /// Handles click events on an equipment slot. Left clicking returns the
    /// item to the inventory.
    /// </summary>
    public class EquipmentSlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [HideInInspector]
        public Equipment equipment;

        [HideInInspector]
        public EquipmentSlot slot;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                equipment?.Unequip(slot);
                equipment?.HideTooltip();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            equipment?.ShowTooltip(slot, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            equipment?.HideTooltip();
        }
    }
}

