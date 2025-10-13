using Inventory.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Inventory
{
    /// <summary>
    /// Simple right-click context menu for inventory drop options.
    /// Built entirely in code so no prefab is needed.
    /// </summary>
    public class InventoryDropMenu : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Screen-space padding the cursor can move beyond the menu before the popup auto-closes.")]
        private float closePaddingPixels = 12f;

        private InventoryWindowController controller;
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

            var isCursorWithinSafeZone = IsCursorWithinSafeZone(out var isCursorOverMenu);

            // Immediately hide the menu when the cursor leaves the padded safe zone to keep the OSRS-style feel.
            if (!isCursorWithinSafeZone)
            {
                Hide();
                return;
            }

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                // Only close on clicks that land outside the strict menu rectangle.
                if (!isCursorOverMenu)
                    Hide();
            }
        }

        /// <summary>
        /// Determines if the cursor remains within the menu rectangle plus a tolerance band to prevent accidental closures.
        /// </summary>
        private bool IsCursorWithinSafeZone(out bool insideMenu)
        {
            insideMenu = RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, canvasCamera);

            if (closePaddingPixels <= 0f)
            {
                return insideMenu;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, Input.mousePosition, canvasCamera, out var localPoint))
            {
                // If the conversion fails, fall back to the strict rectangle check to preserve existing behaviour.
                return insideMenu;
            }

            var paddedRect = rect.rect;
            paddedRect.xMin -= closePaddingPixels;
            paddedRect.xMax += closePaddingPixels;
            paddedRect.yMin -= closePaddingPixels;
            paddedRect.yMax += closePaddingPixels;

            return paddedRect.Contains(localPoint);
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

            CreateButton("Drop 1", () => OnSelection(DropMenuSelection.DropOne));
            CreateButton("Drop All", () => OnSelection(DropMenuSelection.DropAll));
            CreateButton("Drop X", () => OnSelection(DropMenuSelection.DropX));
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

        public void Show(InventoryWindowController controller, int index, Vector2 position)
        {
            this.controller = controller;
            slotIndex = index;
            transform.position = position;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            controller = null;
        }

        private void OnSelection(DropMenuSelection selection)
        {
            controller?.HandleDropMenuSelection(slotIndex, selection);
            Hide();
        }
    }
}
