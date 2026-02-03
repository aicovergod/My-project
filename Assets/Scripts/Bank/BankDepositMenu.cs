using UI.ContextMenus;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace BankSystem
{
    /// <summary>
    /// Simple right-click context menu for bank deposit options.
    /// Built entirely in code so no prefab is needed.
    /// </summary>
    public class BankDepositMenu : ContextMenuBase
    {
        private BankUI bank;
        private int slotIndex;
        private Font font;
        private RectTransform rect;
        private Button transferAllButton;

        public static BankDepositMenu Create(Transform parent, Font font)
        {
            var go = new GameObject("BankDepositMenu", typeof(Image), typeof(BankDepositMenu));
            go.transform.SetParent(parent, false);
            var menu = go.GetComponent<BankDepositMenu>();
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

            CreateButton("Add 1", () => { bank?.DepositFromInventory(slotIndex, 1); Hide(); });
            CreateButton("Add 5", () => { bank?.DepositFromInventory(slotIndex, 5); Hide(); });
            CreateButton("Add 10", () => { bank?.DepositFromInventory(slotIndex, 10); Hide(); });
            CreateButton("Add X", () => { bank?.PromptDepositAmount(slotIndex); Hide(); });
            CreateButton("Add All", () => { bank?.DepositAllFromInventory(slotIndex); Hide(); });
            transferAllButton = CreateButton("Transfer All", HandleTransferAllClicked);
            if (transferAllButton != null)
                transferAllButton.gameObject.SetActive(false);
        }

        private Button CreateButton(string label, UnityAction onClick)
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

            return btn;
        }

        public void Show(BankUI bank, int index, Vector2 position, bool showTransferAll)
        {
            this.bank = bank;
            slotIndex = index;
            transform.position = position;
            gameObject.SetActive(true);
            DeferSafeZoneCheck();
            transform.SetAsLastSibling();
            if (transferAllButton != null)
                transferAllButton.gameObject.SetActive(showTransferAll);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            bank = null;
            if (transferAllButton != null)
                transferAllButton.gameObject.SetActive(false);
        }

        /// <inheritdoc />
        protected override void OnCloseRequested()
        {
            Hide();
        }

        private void HandleTransferAllClicked()
        {
            bank?.TransferAllOreFromBag(slotIndex);
            Hide();
        }
    }
}
