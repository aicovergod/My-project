using UnityEngine;
using UnityEngine.UI;
using UI;
using UI.Utilities;
using Companions;
using Companions.Inventory;
using Companions.UI;

namespace Pets
{
    /// <summary>
    /// Simple context menu for the pet level bar.
    /// </summary>
    public partial class PetLevelBarMenu : MonoBehaviour
    {
        private Button xpButton;
        private Button guardButton;

        /// <summary>Label reflecting the current guard mode state.</summary>
        private Text guardText;

        private Button inventoryButton;

        /// <summary>Label reflecting the current inventory visibility state.</summary>
        private Text inventoryText;

        /// <summary>Button that toggles the companion equipment window.</summary>
        private Button equipmentButton;

        /// <summary>Label reflecting the current equipment visibility state.</summary>
        private Text equipmentText;

        /// <summary>Button that cancels the companion's current action.</summary>
        private Button stopActionButton;

        /// <summary>Label that reflects the action-specific stop command.</summary>
        private Text stopActionText;

        /// <summary>Button that opens the companion stats window.</summary>
        private Button statsButton;

        /// <summary>Button that allows companions to dump their inventory into the bank.</summary>
        private Button bankButton;

        [SerializeField]
        private Button commandButton;

        /// <summary>HUD currently owning the menu so callbacks can target the right entity.</summary>
        private PetLevelBarHUD current;

        private static PetLevelBarMenu instance;

        /// <summary>Canvas hosting the floating menu so click detection can reference it.</summary>
        private static Canvas menuCanvas;

        public static void Show(PetLevelBarHUD hud, Vector2 position)
        {
            if (instance == null)
                CreateInstance();
            instance.current = hud;
            bool isCompanion = hud != null && hud.IsCompanionHud;
            instance.statsButton.gameObject.SetActive(isCompanion);
            instance.xpButton.gameObject.SetActive(!isCompanion);
            if (instance.commandButton != null)
                instance.commandButton.gameObject.SetActive(isCompanion);
            if (instance.bankButton != null)
                instance.bankButton.gameObject.SetActive(isCompanion);

            if (isCompanion)
            {
                instance.guardText.text = CompanionManager.GuardModeEnabled ? "Guard Mode: On" : "Guard Mode: Off";
                instance.inventoryButton.gameObject.SetActive(true);
                instance.inventoryText.text = CompanionManager.IsInventoryVisible() ? "Inventory: On" : "Inventory: Off";
                instance.equipmentButton.gameObject.SetActive(true);
                instance.equipmentText.text = CompanionManager.IsEquipmentVisible() ? "Equipment: On" : "Equipment: Off";
                instance.UpdateStopActionButton(true);
            }
            else
            {
                CompanionCommandMenu.Hide();
                instance.guardText.text = PetDropSystem.GuardModeEnabled ? "Guard Mode: On" : "Guard Mode: Off";
                var pet = PetDropSystem.ActivePetObject;
                var storage = pet != null ? pet.GetComponent<PetStorage>() : null;
                var inv = pet != null ? pet.GetComponent<Inventory.Inventory>() : null;
                bool hasInventory = storage != null && inv != null;
                instance.inventoryButton.gameObject.SetActive(hasInventory);
                if (hasInventory)
                    instance.inventoryText.text = PetDropSystem.PetInventoryVisible ? "Inventory: On" : "Inventory: Off";
                instance.equipmentButton.gameObject.SetActive(false);
                instance.UpdateStopActionButton(false);
            }
            instance.transform.position = position;
            instance.gameObject.SetActive(true);
            instance.OnMenuShown();
            instance.UpdateStopActionButton(isCompanion);
        }

        private static void CreateInstance()
        {
            var overlay = OverlayCanvasFactory.CreateOverlayCanvas(
                "PetBarMenuCanvas",
                new Vector2(1920f, 1080f),
                dontDestroyOnLoad: true,
                assignToUiLayer: true,
                overrideSorting: true,
                sortingOrder: short.MaxValue);

            menuCanvas = overlay.Canvas;

            var menuGO = new GameObject(
                "PetLevelBarMenu",
                typeof(Image),
                typeof(PetLevelBarMenu),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            menuGO.transform.SetParent(overlay.Root.transform, false);
            var img = menuGO.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.8f);

            var layout = menuGO.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = true;
            var fitter = menuGO.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            instance = menuGO.GetComponent<PetLevelBarMenu>();
            instance.xpButton = CreateButton(menuGO.transform, "XP Till Next Level");
            instance.xpButton.onClick.AddListener(() =>
            {
                instance.current?.ShowXpToNextLevel();
                instance.Hide();
            });

            instance.statsButton = CreateButton(menuGO.transform, "Stats");
            instance.statsButton.onClick.AddListener(() =>
            {
                CompanionManager.OpenStats();
                instance.Hide();
            });

            instance.commandButton = CreateButton(menuGO.transform, "Command");
            instance.commandButton.onClick.AddListener(() =>
            {
                if (instance.commandButton == null)
                    return;

                var rect = instance.commandButton.GetComponent<RectTransform>();
                if (CompanionCommandMenu.IsVisible)
                {
                    CompanionCommandMenu.Hide();
                }
                else
                {
                    CompanionCommandMenu.Show(rect);
                }
            });

            instance.stopActionButton = CreateButton(menuGO.transform, "Stop");
            instance.stopActionText = instance.stopActionButton.GetComponentInChildren<Text>();
            instance.stopActionButton.onClick.AddListener(() =>
            {
                instance.OnStopActionClicked();
            });

            instance.guardButton = CreateButton(menuGO.transform, "Guard Mode");
            instance.guardText = instance.guardButton.GetComponentInChildren<Text>();
            instance.guardButton.onClick.AddListener(() =>
            {
                instance.current?.ToggleGuardMode();
                instance.Hide();
            });

            instance.inventoryButton = CreateButton(menuGO.transform, "Inventory");
            instance.inventoryText = instance.inventoryButton.GetComponentInChildren<Text>();
            instance.inventoryButton.onClick.AddListener(() =>
            {
                instance.current?.ToggleInventory();
                instance.Hide();
            });

            instance.equipmentButton = CreateButton(menuGO.transform, "Equipment");
            instance.equipmentText = instance.equipmentButton.GetComponentInChildren<Text>();
            instance.equipmentButton.onClick.AddListener(() =>
            {
                instance.current?.ToggleEquipment();
                instance.Hide();
            });

            instance.bankButton = CreateButton(menuGO.transform, "Bank");
            instance.bankButton.onClick.AddListener(() =>
            {
                CompanionBankDepositService.TryDepositCompanionInventoryToBank(
                    CompanionManager.ActiveCompanion,
                    CompanionManager.EnableDebugLogging,
                    CompanionManager.GetCompanionDisplayName());
                instance.Hide();
            });

            instance.OnMenuCreated(menuGO.transform);

            menuGO.SetActive(false);
        }

        private static Button CreateButton(Transform parent, string label)
        {
            var go = new GameObject(label, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.preferredWidth = 160f;
            var btn = go.GetComponent<Button>();

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(go.transform, false);
            var txt = textGO.GetComponent<Text>();
            txt.text = label;
            LegacyFontProvider.ApplyTo(txt);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            var rect = txt.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return btn;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            current = null;
            CompanionCommandMenu.Hide();
        }

        internal static void HideActiveMenu()
        {
            if (instance != null)
                instance.Hide();
        }

        private void Update()
        {
            if (!gameObject.activeSelf)
                return;

            UpdateStopActionButton(current != null && current.IsCompanionHud);

            if (!Input.GetMouseButtonDown(0))
                return;

            var rect = GetComponent<RectTransform>();
            var camera = menuCanvas != null ? menuCanvas.worldCamera : null;
            bool clickInsidePetMenu = RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
            bool clickInsideCommandMenu = CompanionCommandMenu.ContainsScreenPoint(Input.mousePosition);

            if (!clickInsidePetMenu && !clickInsideCommandMenu)
                Hide();
        }

        partial void OnMenuCreated(Transform menuRoot);
        partial void OnMenuShown();

        private void UpdateStopActionButton(bool isCompanionHud)
        {
            if (stopActionButton == null)
                return;

            if (!isCompanionHud)
            {
                stopActionButton.gameObject.SetActive(false);
                return;
            }

            bool show = CompanionManager.HasActiveAction;
            stopActionButton.gameObject.SetActive(show);

            if (!show || stopActionText == null)
                return;

            stopActionText.text = CompanionDisplayUtility.GetStopActionLabel(CompanionManager.GetActiveAction());
        }

        private void OnStopActionClicked()
        {
            bool cancelled = CompanionManager.TryCancelCurrentAction();

            if (CompanionManager.EnableDebugLogging)
                Debug.Log($"[PetLevelBarMenu] Stop action button clicked. Cancelled={cancelled}.");

            Hide();
        }
    }
}
