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

        private Button stopButton;
        private Text stopButtonText;
        private Button mineButton;
        private Button chopButton;
        private Button fishButton;
        private Button cookButton;

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
            instance.RefreshStopButton();
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

            RefreshStopButton();

            if (Input.GetMouseButtonDown(0) && !ContainsScreenPoint(Input.mousePosition))
                InternalHide();
        }

        private void ConfigureButtons(Transform parent)
        {
            stopButton = CreateButton(parent, "Stop");
            stopButtonText = stopButton.GetComponentInChildren<Text>();
            stopButton.onClick.AddListener(OnStopActionClicked);

            mineButton = CreateButton(parent, "Mine Rocks");
            mineButton.onClick.AddListener(OnMineRocksClicked);

            chopButton = CreateButton(parent, "Chop Trees");
            chopButton.onClick.AddListener(OnChopTreesClicked);

            fishButton = CreateButton(parent, "Go Fishing");
            fishButton.onClick.AddListener(OnGoFishingClicked);

            cookButton = CreateButton(parent, "Cook Food");
            cookButton.onClick.AddListener(OnCookFoodClicked);
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

        private void RefreshStopButton()
        {
            if (stopButton == null)
                return;

            bool show = CompanionManager.HasActiveAction;
            stopButton.gameObject.SetActive(show);
            stopButton.interactable = show;

            if (!show || stopButtonText == null)
                return;

            stopButtonText.text = CompanionManager.GetStopActionLabel();
        }

        private void OnStopActionClicked()
        {
            bool cancelled = CompanionManager.TryCancelCurrentAction();

            if (CompanionManager.EnableDebugLogging)
                Debug.Log($"[Companion UI] Stop command invoked. Cancelled={cancelled}.");

            CloseAllMenus();
        }

        private void OnMineRocksClicked()
        {
            Debug.Log("[Companion UI] Mine Rocks button clicked.");
            bool accepted = CompanionManager.TryCommandMineNearby(out var failureReason);
            Debug.Log($"[Companion UI] Mine Rocks command result: success={accepted}, failureReason={failureReason}.");
            if (!accepted)
            {
                string playerName = GetActivePlayerName();

                if (!CompanionManager.HasActiveCompanion)
                {
                    PublishPlaceholderMessage(CompanionChatLibrary.GetRandomMiningSummonRequiredLine(playerName));
                }
                else if (ShouldPublishFallbackForFailure(failureReason))
                {
                    // Surface a short chat line when the command cannot be fulfilled so the player
                    // understands why both menus closed without triggering companion behaviour.
                    PublishPlaceholderMessage(CompanionChatLibrary.GetRandomMiningGenericFailureLine(playerName));
                }
            }

            // Always close every pet-related menu after a command click so the PetLevelBarMenu
            // and the command sub-menu never remain visible simultaneously.
            CloseAllMenus();
        }

        private void OnChopTreesClicked()
        {
            Debug.Log("[Companion UI] Chop Trees button clicked.");
            bool accepted = CompanionManager.TryCommandChopNearby(out var failureReason);
            Debug.Log($"[Companion UI] Chop Trees command result: success={accepted}, failureReason={failureReason}.");
            if (!accepted)
            {
                string playerName = GetActivePlayerName();

                if (!CompanionManager.HasActiveCompanion)
                {
                    PublishPlaceholderMessage(CompanionChatLibrary.GetRandomWoodcuttingSummonRequiredLine(playerName));
                }
                else if (ShouldPublishWoodcuttingFallback(failureReason))
                {
                    PublishPlaceholderMessage(CompanionChatLibrary.GetRandomWoodcuttingGenericFailureLine(playerName));
                }
            }

            CloseAllMenus();
        }

        private void OnGoFishingClicked()
        {
            Debug.Log("[Companion UI] Go Fishing button clicked.");
            bool accepted = CompanionManager.TryCommandFishNearby(out var failureReason);
            Debug.Log($"[Companion UI] Go Fishing command result: success={accepted}, failureReason={failureReason}.");
            if (!accepted)
            {
                string playerName = GetActivePlayerName();

                if (!CompanionManager.HasActiveCompanion)
                {
                    PublishPlaceholderMessage(CompanionChatLibrary.GetRandomFishingSummonRequiredLine(playerName));
                }
                else if (ShouldPublishFishingFallback(failureReason))
                {
                    switch (failureReason)
                    {
                        case CompanionFishingCommandResult.RequirementsNotMet:
                        case CompanionFishingCommandResult.Unreachable:
                            PublishPlaceholderMessage(CompanionFishingDialogueLibrary.GetRandomNoSpotsLine());
                            break;
                        default:
                            PublishPlaceholderMessage(CompanionChatLibrary.GetRandomFishingGenericFailureLine(playerName));
                            break;
                    }
                }
            }

            CloseAllMenus();
        }

        private void OnCookFoodClicked()
        {
            Debug.Log("[Companion UI] Cook Food button clicked.");
            bool accepted = CompanionManager.TryCommandCookNearby(out var failureReason);
            Debug.Log($"[Companion UI] Cook Food command result: success={accepted}, failureReason={failureReason}.");
            if (!accepted)
            {
                string playerName = GetActivePlayerName();

                if (!CompanionManager.HasActiveCompanion)
                {
                    PublishPlaceholderMessage(CompanionChatLibrary.GetRandomCookingSummonRequiredLine(playerName));
                }
                else if (ShouldPublishCookingFallback(failureReason))
                {
                    switch (failureReason)
                    {
                        case CompanionCookingCommandResult.MissingIngredients:
                            PublishPlaceholderMessage(CompanionCookingDialogueLibrary.GetRandomMissingIngredientLine());
                            break;
                        case CompanionCookingCommandResult.MissingTool:
                            PublishPlaceholderMessage(CompanionCookingDialogueLibrary.GetRandomMissingToolLine());
                            break;
                        case CompanionCookingCommandResult.PlayerBusy:
                            PublishPlaceholderMessage(CompanionCookingDialogueLibrary.GetRandomPlayerBusyLine());
                            break;
                        default:
                            PublishPlaceholderMessage(CompanionChatLibrary.GetRandomCookingGenericFailureLine(playerName));
                            break;
                    }
                }
            }

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
                case CompanionMiningCommandResult.Declined:
                    // The mining controller publishes its own descriptive chat lines for these cases.
                    return false;
                default:
                    return true;
            }
        }

        private bool ShouldPublishWoodcuttingFallback(CompanionWoodcuttingCommandResult failureReason)
        {
            switch (failureReason)
            {
                case CompanionWoodcuttingCommandResult.InventoryFull:
                case CompanionWoodcuttingCommandResult.NoAxe:
                case CompanionWoodcuttingCommandResult.BlockedByPlayer:
                case CompanionWoodcuttingCommandResult.RequirementsNotMet:
                case CompanionWoodcuttingCommandResult.Declined:
                case CompanionWoodcuttingCommandResult.AlreadyChopping:
                    return false;
                default:
                    return true;
            }
        }

        private bool ShouldPublishFishingFallback(CompanionFishingCommandResult failureReason)
        {
            switch (failureReason)
            {
                case CompanionFishingCommandResult.InventoryFull:
                case CompanionFishingCommandResult.NoTool:
                case CompanionFishingCommandResult.NoBait:
                case CompanionFishingCommandResult.BlockedByPlayer:
                case CompanionFishingCommandResult.RequirementsNotMet:
                case CompanionFishingCommandResult.Declined:
                case CompanionFishingCommandResult.AlreadyFishing:
                    return false;
                default:
                    return true;
            }
        }

        private bool ShouldPublishCookingFallback(CompanionCookingCommandResult failureReason)
        {
            switch (failureReason)
            {
                case CompanionCookingCommandResult.InventoryFull:
                case CompanionCookingCommandResult.MissingIngredients:
                case CompanionCookingCommandResult.MissingTool:
                case CompanionCookingCommandResult.PlayerBusy:
                case CompanionCookingCommandResult.RequirementsNotMet:
                case CompanionCookingCommandResult.StationUnavailable:
                case CompanionCookingCommandResult.StationOccupied:
                case CompanionCookingCommandResult.Declined:
                case CompanionCookingCommandResult.AlreadyCooking:
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

        private static string GetActivePlayerName()
        {
            var chat = ChatService.Instance;
            return chat != null ? chat.ActiveUsername : string.Empty;
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
