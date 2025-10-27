using Inventory.UI;
using UI.ContextMenus;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Inventory
{
    /// <summary>
    /// Simple right-click context menu for inventory drop options.
    /// Built entirely in code so no prefab is needed.
    /// </summary>
    public class InventoryDropMenu : ContextMenuBase
    {
        private InventoryWindowController controller;
        private int slotIndex;
        private Font font;
        private RectTransform rect;
        private readonly Vector3[] worldCorners = new Vector3[4];
        private readonly Vector2[] screenCorners = new Vector2[4];

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

        protected override void Awake()
        {
            base.Awake();
            rect ??= GetComponent<RectTransform>();
            AssignCanvas(GetComponentInParent<Canvas>());
            SetMenuRectTransform(rect);
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
            if (rect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            ClampToScreenBounds();
            DeferSafeZoneCheck();
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            controller = null;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled)
                return;

            ClampToScreenBounds();
        }

        /// <inheritdoc />
        protected override void OnCloseRequested()
        {
            Hide();
        }

        private void OnSelection(DropMenuSelection selection)
        {
            controller?.HandleDropMenuSelection(slotIndex, selection);
            Hide();
        }

        /// <summary>
        /// Ensures the menu remains inside the visible screen bounds so the player can always reach each option.
        /// </summary>
        private void ClampToScreenBounds()
        {
            if (!gameObject.activeInHierarchy)
                return;

            var targetRect = rect;
            if (targetRect == null)
                return;

            var canvas = MenuCanvas;
            if (canvas == null)
                return;

            targetRect.GetWorldCorners(worldCorners);
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < worldCorners.Length; i++)
            {
                screenCorners[i] = RectTransformUtility.WorldToScreenPoint(MenuCanvasCamera, worldCorners[i]);
                Vector2 screenCorner = screenCorners[i];
                if (screenCorner.x < minX)
                    minX = screenCorner.x;
                if (screenCorner.x > maxX)
                    maxX = screenCorner.x;
                if (screenCorner.y < minY)
                    minY = screenCorner.y;
                if (screenCorner.y > maxY)
                    maxY = screenCorner.y;
            }

            float menuWidth = Mathf.Max(0f, maxX - minX);
            float menuHeight = Mathf.Max(0f, maxY - minY);
            Vector2 pivotScreenPosition = RectTransformUtility.WorldToScreenPoint(MenuCanvasCamera, targetRect.position);

            float horizontalMax = Mathf.Max(0f, Screen.width - menuWidth);
            float verticalMin = Mathf.Clamp(menuHeight, 0f, Screen.height);

            float clampedX = Mathf.Clamp(pivotScreenPosition.x, 0f, horizontalMax);
            float clampedY = Mathf.Clamp(pivotScreenPosition.y, verticalMin, Screen.height);
            Vector2 clampedScreenPosition = new Vector2(clampedX, clampedY);

            if (Vector2.SqrMagnitude(clampedScreenPosition - pivotScreenPosition) < Mathf.Epsilon)
                return;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(targetRect, clampedScreenPosition, MenuCanvasCamera, out Vector3 worldPosition))
            {
                targetRect.position = worldPosition;
            }
        }
    }
}
