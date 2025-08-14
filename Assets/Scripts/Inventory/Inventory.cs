using UnityEngine;
using UnityEngine.UI;

namespace Inventory
{
    /// <summary>
    /// Component attached to the player that stores a fixed number of items,
    /// generates a basic UI grid at runtime, and allows toggling it with a key
    /// press. The inventory uses simple first-in slot allocation and exposes
    /// helper methods for adding items and checking for a specific item ID.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        [Tooltip("Maximum number of items the inventory can hold.")]
        public int size = 20;

        private Image[] slotImages;
        private ItemData[] items;
        private GameObject uiRoot;

        void Awake()
        {
            items = new ItemData[size];
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false); // hide until toggled
        }

        /// <summary>
        /// Creates a simple canvas and generates slot images at runtime so no
        /// manual setup is required in the editor.
        /// </summary>
        private void CreateUI()
        {
            uiRoot = new GameObject("InventoryUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            GameObject panel = new GameObject("Slots", typeof(RectTransform), typeof(GridLayoutGroup));
            panel.transform.SetParent(uiRoot.transform);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(10, -10);

            var grid = panel.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(32, 32);
            grid.spacing = new Vector2(4, 4);

            slotImages = new Image[size];
            for (int i = 0; i < size; i++)
            {
                GameObject slot = new GameObject($"Slot{i}", typeof(Image));
                slot.transform.SetParent(panel.transform);
                var img = slot.GetComponent<Image>();
                img.enabled = false; // hidden until filled
                slotImages[i] = img;
            }
        }

        /// <summary>
        /// Adds the given item to the first empty slot in the inventory.
        /// Returns true on success, false if the inventory is full.
        /// </summary>
        public bool AddItem(ItemData item)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null)
                {
                    items[i] = item;

                    if (slotImages != null && i < slotImages.Length && slotImages[i] != null)
                    {
                        slotImages[i].sprite = item.icon;
                        slotImages[i].enabled = true;
                    }

                    return true;
                }
            }

            // No free slot
            return false;
        }

        /// <summary>
        /// Checks whether the inventory currently contains an item with the given ID.
        /// </summary>
        public bool HasItem(string id)
        {
            foreach (var item in items)
            {
                if (item != null && item.id == id)
                    return true;
            }

            return false;
        }

        void Update()
        {
            if (uiRoot != null && Input.GetKeyDown(KeyCode.I))
            {
                uiRoot.SetActive(!uiRoot.activeSelf);
            }
        }
    }
}