using UnityEngine;
using UnityEngine.EventSystems;

namespace ShopSystem
{
    /// <summary>
    /// Handles click events for a shop slot.  Left click attempts to buy the item.
    /// </summary>
    public class ShopSlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [HideInInspector] public ShopUI shopUI;
        [HideInInspector] public int index;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                shopUI?.Buy(index);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            shopUI?.ShowTooltip(index);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            shopUI?.HideTooltip();
        }
    }
}
