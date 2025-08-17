using UnityEngine;
using UnityEngine.UI;

namespace Inventory
{
    /// <summary>
    /// Displays item information when hovering over inventory slots.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventoryModel), typeof(InventoryUI))]
    public class InventoryTooltip : MonoBehaviour
    {
        private InventoryModel model;
        private InventoryUI ui;

        private void Awake()
        {
            model = GetComponent<InventoryModel>();
            ui = GetComponent<InventoryUI>();
        }

        public void ShowTooltip(int slotIndex, RectTransform slotRect)
        {
            if (ui.TooltipObject == null)
                return;
            if (slotIndex < 0 || slotIndex >= model.Count)
                return;
            var item = model.GetEntry(slotIndex).item;
            if (item == null)
                return;

            var tooltip = ui.TooltipObject;
            var nameText = ui.TooltipNameText;
            var descText = ui.TooltipDescriptionText;
            if (tooltip == null || nameText == null || descText == null)
                return;

            if (model.CurrentShop != null && model.CurrentShop.TryGetSellPrice(item, out int sellPrice))
            {
                string currencyName = model.CurrentShop.currency != null ? model.CurrentShop.currency.itemName : "Coins";
                nameText.text = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
                descText.text = $"Sell for {sellPrice} {currencyName}";
            }
            else
            {
                string name = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
                nameText.text = name;
                descText.text = item.description;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip.GetComponent<RectTransform>());
            tooltip.transform.position = slotRect.position + new Vector3(ui.SlotSize.x, 0f, 0f);
            tooltip.SetActive(true);
        }

        public void HideTooltip()
        {
            if (ui.TooltipObject != null)
                ui.TooltipObject.SetActive(false);
        }
    }
}
