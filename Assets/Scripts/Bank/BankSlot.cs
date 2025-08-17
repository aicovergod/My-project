using UnityEngine;
using UnityEngine.EventSystems;

namespace BankSystem
{
    /// <summary>
    /// Handles click events for bank slots to withdraw items.
    /// </summary>
    public class BankSlot : MonoBehaviour, IPointerClickHandler
    {
        [HideInInspector] public BankUI bank;
        [HideInInspector] public int index;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                bank?.Withdraw(index);
        }
    }
}
