using Pets;
using UI;
using UI.Chat;
using UI.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace Companions.UI
{
    /// <summary>
    /// Companion-specific command menu that appears alongside the pet level bar context menu and
    /// routes quick orders (mine, placeholders for other skills) to the companion manager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionCommandMenu : MonoBehaviour
    {
        private const float ButtonWidth = 160f;
        private const float ButtonHeight = 24f;
        private const float HorizontalPadding = 8f;

        private static CompanionCommandMenu instance;
        private static Canvas menuCanvas;

        private RectTransform rectTransform;
        private RectTransform canvasRect;
        private GameObject menuRoot;

        private Button mineButton;
        private Button chopButton;
        private Button fishButton;

        private readonly Vector3[] anchorCorners = new Vector3[4];

        /// <summary>Indicates whether the command menu is currently visible.</summary>
        public static bool IsVisible => instance != null && instance.menuRoot != null && instance.menuRoot.activeSelf;

        /// <summary>
        /// Shows the command menu next to the supplied button anchor.
        /// </summary>
        /// <param name="anchor">Button transform used to position the menu.</param>
        public static void Show(RectTransform anchor)
        {
            if (anchor == null)
                return;

            EnsureInstance();
            if (instance == null)
                return;

            instance.PositionBeside(anchor);
            instance.menuRoot.SetActive(true);
        }

        /// <summary>Hides the command menu if it is visible.</summary>
        public static void Hide()
        {
            if (instance == null)
                return;

            instance.InternalHide();
        }

        /// <summary>
        /// Determines whether the supplied screen position falls within the active command menu.
        /// </summary>
        /// <param name="screenPosition">Screen-space position to evaluate.</param>
        /// <returns>
        /// True when the menu is visible and the screen position lies within the menu rectangle; otherwise false.
        /// </returns>
        internal static bool ContainsScreenPoint(Vector2 screenPosition)
        {
            if (!IsVisible || instance.rectTransform == null)
                return false;

            var camera = menuCanvas != null ? menuCanvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(instance.rectTransform, screenPosition, camera);
        }

        private static void EnsureInstance()
        {
            if (instance != null)
                return;

            var overlay = OverlayCanvasFactory.CreateOverlayCanvas(
                "CompanionCommandMenuCanvas",
                new Vector2(1920f, 1080f),
                dontDestroyOnLoad: true,
                assignToUiLayer: true,
                overrideSorting: true,
                sortingOrder: short.MaxValue);

            menuCanvas = overlay.Canvas;

            var menuGO = new GameObject(
                "CompanionCommandMenu",
                typeof(Image),
                typeof(CompanionCommandMenu),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            menuGO.transform.SetParent(overlay.Root.transform, false);

            var background = menuGO.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.8f);

            var layout = menuGO.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 1f;

            var fitter = menuGO.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            instance = menuGO.GetComponent<CompanionCommandMenu>();
            instance.menuRoot = menuGO;
            instance.rectTransform = menuGO.GetComponent<RectTransform>();
            instance.canvasRect = menuCanvas != null ? menuCanvas.transform as RectTransform : null;
            instance.ConfigureButtons(menuGO.transform);

            menuGO.SetActive(false);
        }

        private void Awake()
        {
            if (instance == null)
                instance = this;

            if (rectTransform == null)
                rectTransform = GetComponent<RectTransform>();

            if (canvasRect == null && menuCanvas != null)
                canvasRect = menuCanvas.transform as RectTransform;
        }

        private void Update()
        {
            if (!menuRoot.activeSelf)
                return;

            if (Input.GetMouseButtonDown(0) && !ContainsScreenPoint(Input.mousePosition))
                InternalHide();
        }

        private void ConfigureButtons(Transform parent)
        {
            mineButton = CreateButton(parent, "Mine Rocks");
            mineButton.onClick.AddListener(OnMineRocksClicked);

            chopButton = CreateButton(parent, "Chop Trees");
            chopButton.onClick.AddListener(() => OnPlaceholderClicked("I can't do that yet"));

            fishButton = CreateButton(parent, "Go Fishing");
            fishButton.onClick.AddListener(() => OnPlaceholderClicked("I can't do that yet"));
        }

        private Button CreateButton(Transform parent, string label)
        {
            var go = new GameObject(label, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = ButtonHeight;
            le.preferredWidth = ButtonWidth;

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
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return btn;
        }

        private void PositionBeside(RectTransform anchor)
        {
            if (rectTransform == null || canvasRect == null)
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

            anchor.GetWorldCorners(anchorCorners);
            Vector3 topRight = anchorCorners[2];
            Vector3 bottomRight = anchorCorners[3];
            Vector3 rightEdgeCenter = (topRight + bottomRight) * 0.5f;

            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(menuCanvas != null ? menuCanvas.worldCamera : null, rightEdgeCenter);
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, menuCanvas != null ? menuCanvas.worldCamera : null, out localPoint);

            float halfWidth = rectTransform.rect.width * 0.5f;
            localPoint.x += halfWidth + HorizontalPadding;

            rectTransform.anchoredPosition = localPoint;
        }

        private void OnMineRocksClicked()
        {
            Debug.Log("[Companion UI] Mine Rocks button clicked.");
            bool accepted = CompanionManager.TryCommandMineNearby(out var failureReason);
            Debug.Log($"[Companion UI] Mine Rocks command result: success={accepted}, failureReason={failureReason}.");
            if (!accepted)
            {
                if (!CompanionManager.HasActiveCompanion)
                {
                    PublishPlaceholderMessage("You need to summon me before I can go mining.");
                }
                else if (ShouldPublishFallbackForFailure(failureReason))
                {
                    // Surface a short chat line when the command cannot be fulfilled so the player
                    // understands why both menus closed without triggering companion behaviour.
                    PublishPlaceholderMessage("I can't find any rocks to mine right now.");
                }
            }

            // Always close every pet-related menu after a command click so the PetLevelBarMenu
            // and the command sub-menu never remain visible simultaneously.
            CloseAllMenus();
        }

        private bool ShouldPublishFallbackForFailure(CompanionMiningCommandResult failureReason)
        {
            switch (failureReason)
            {
                case CompanionMiningCommandResult.InventoryFull:
                case CompanionMiningCommandResult.NoPickaxe:
                case CompanionMiningCommandResult.BlockedByPlayer:
                case CompanionMiningCommandResult.RequirementsNotMet:
                case CompanionMiningCommandResult.Unreachable:
                    // The mining controller publishes its own descriptive chat lines for these cases.
                    return false;
                default:
                    return true;
            }
        }

        private void OnPlaceholderClicked(string message)
        {
            PublishPlaceholderMessage(message);
            CloseAllMenus();
        }

        private void PublishPlaceholderMessage(string message)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
        }

        private void InternalHide()
        {
            if (!menuRoot.activeSelf)
                return;

            menuRoot.SetActive(false);
        }

        private void CloseAllMenus()
        {
            InternalHide();
            PetLevelBarMenu.HideActiveMenu();
        }
    }
}
