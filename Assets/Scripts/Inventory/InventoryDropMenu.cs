using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace Inventory
{
    /// <summary>
    /// Simple right-click context menu for inventory drop options.
    /// Built entirely in code so no prefab is needed.
    /// </summary>
    public class InventoryDropMenu : MonoBehaviour
    {
        private Inventory inventory;
        private int slotIndex;
        private Font font;
        private RectTransform rect;
        private Canvas menuCanvas;
        private Camera canvasCamera;

        public static InventoryDropMenu Create(Transform parent, Font font)
        {
            var go = new GameObject("InventoryDropMenu", typeof(Image), typeof(InventoryDropMenu));
            go.transform.SetParent(parent, false);
            var menu = go.GetComponent<InventoryDropMenu>();
            menu.font = font;
            menu.rect = go.GetComponent<RectTransform>();
            menu.BuildUI();
            go.SetActive(false);
            return menu;
        }

        private void Awake()
        {
            rect ??= GetComponent<RectTransform>();
            menuCanvas = GetComponentInParent<Canvas>();

            // Cache the correct camera reference so hover checks work with any canvas render mode.
            if (menuCanvas != null && menuCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                canvasCamera = menuCanvas.worldCamera;
            }
            else
            {
                canvasCamera = null;
            }
        }

        private void Update()
        {
            if (!gameObject.activeSelf)
                return;

            var isCursorOverMenu = RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, canvasCamera);

            // Immediately hide the menu when the cursor leaves so it behaves like the OSRS context menu.
            if (!isCursorOverMenu)
            {
                Hide();
                return;
            }

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                // Reuse the hover state so clicks outside still close the menu without recalculating the hit test.
                if (!isCursorOverMenu)
                    Hide();
            }
        }

        private void BuildUI()
        {
            var bg = GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.9f);
            rect.pivot = new Vector2(0f, 1f);

            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 2f;
            layout.padding = new RectOffset(2, 2, 2, 2);

            CreateButton("Drop 1", () => { inventory?.DropItem(slotIndex, 1); Hide(); });
            CreateButton("Drop All", () => { if (inventory != null) inventory.DropItem(slotIndex, inventory.GetSlot(slotIndex).count); Hide(); });
            CreateButton("Drop X", () => { inventory?.PromptStackSplit(slotIndex, StackSplitType.Drop); Hide(); });
        }

        private void CreateButton(string label, UnityAction onClick)
        {
            var btnGO = new GameObject(label, typeof(Image), typeof(Button));
            btnGO.transform.SetParent(transform, false);
            var img = btnGO.GetComponent<Image>();
            img.sprite = Resources.Load<Sprite>("Sprites/BankUI/Button_1");
            img.color = new Color(0f, 0f, 0f, 0f);
            var btn = btnGO.GetComponent<Button>();
            btn.onClick.AddListener(onClick);

            var txtGO = new GameObject("Text", typeof(Text));
            txtGO.transform.SetParent(btnGO.transform, false);
            var txt = txtGO.GetComponent<Text>();
            txt.font = font;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = Color.white;
            txt.text = label;

            var btnRect = btnGO.GetComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(100f, 20f);
            btnRect.pivot = new Vector2(0f, 1f);

            var txtRect = txtGO.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
        }

        public void Show(Inventory inventory, int index, Vector2 position)
        {
            this.inventory = inventory;
            slotIndex = index;
            transform.position = position;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            inventory = null;
        }
    }
}
