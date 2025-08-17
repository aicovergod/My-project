using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory
{
    /// <summary>
    /// Handles drag and drop interactions for inventory slots.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventoryModel), typeof(InventoryUI))]
    public class InventoryDragHandler : MonoBehaviour
    {
        private InventoryModel model;
        private InventoryUI ui;

        private int draggingIndex = -1;
        private GameObject draggingIcon;

        private void Awake()
        {
            model = GetComponent<InventoryModel>();
            ui = GetComponent<InventoryUI>();
        }

        public void BeginDrag(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= model.Count) return;
            var entry = model.GetEntry(slotIndex);
            var item = entry.item;
            if (item == null) return;

            draggingIndex = slotIndex;

            draggingIcon = new GameObject("DraggingIcon", typeof(Image));
            draggingIcon.transform.SetParent(ui.UIRoot.transform, false);
            var img = draggingIcon.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = item.icon ? item.icon : ui.SlotImages[slotIndex].sprite;
            img.color = Color.white;
            var rect = draggingIcon.GetComponent<RectTransform>();
            rect.sizeDelta = ui.SlotSize;

            if (ui.SlotImages != null && slotIndex < ui.SlotImages.Length && ui.SlotImages[slotIndex] != null)
                ui.SlotImages[slotIndex].enabled = false;
            if (ui.SlotCountTexts != null && slotIndex < ui.SlotCountTexts.Length && ui.SlotCountTexts[slotIndex] != null)
                ui.SlotCountTexts[slotIndex].enabled = false;
        }

        public void Drag(PointerEventData eventData)
        {
            if (draggingIcon != null)
                draggingIcon.transform.position = eventData.position;
        }

        public void Drop(int slotIndex)
        {
            if (draggingIndex == -1)
            {
                EndDrag();
                return;
            }

            if (slotIndex >= 0 && slotIndex < model.Count)
            {
                if (slotIndex != draggingIndex)
                {
                    model.Swap(slotIndex, draggingIndex);
                    ui.UpdateSlotVisual(slotIndex);
                }

                ui.UpdateSlotVisual(draggingIndex);
            }

            EndDrag();
        }

        public void EndDrag()
        {
            if (draggingIndex != -1)
                ui.UpdateSlotVisual(draggingIndex);

            if (draggingIcon != null)
                Object.Destroy(draggingIcon);

            draggingIcon = null;
            draggingIndex = -1;
        }
    }
}
