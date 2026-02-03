using System.Collections.Generic;
using NPC;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Util;

namespace UI
{
    /// <summary>
    ///     Programmatically builds and displays the OSRS-style right-click menu for NPCs.
    ///     Buttons are generated at runtime according to the <see cref="NpcInteractionOptions"/>
    ///     attached to the selected NPC.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RightClickMenu : MonoBehaviour
    {
        private const float ButtonSpacing = 4f;
        private const float ButtonMinWidth = 200f;
        private const float ButtonMinHeight = 44f;

        private static readonly Dictionary<NpcInteractionAction, string> LabelLookup = new()
        {
            { NpcInteractionAction.Attack, "Attack" },
            { NpcInteractionAction.Talk, "Talk-to" },
            { NpcInteractionAction.Trade, "Trade" },
            { NpcInteractionAction.Pickpocket, "Pickpocket" },
            { NpcInteractionAction.Examine, "Examine" }
        };

        private readonly Dictionary<NpcInteractionAction, UnityAction> handlerLookup = new();
        private readonly List<Button> activeButtons = new();
        private readonly List<Vector2> pointerPressPositions = new();

        private RectTransform cachedRectTransform;
        private Canvas parentCanvas;
        private bool suppressCloseUntilNextFrame;
        private NpcInteractable current;
        private Font menuFont;
        private NpcFollowerAttackType followerAttackType = NpcFollowerAttackType.None;

        /// <summary>
        ///     Factory helper that constructs a new menu instance beneath the supplied parent
        ///     transform and wires the required UI components.
        /// </summary>
        public static RightClickMenu Create(Transform parent)
        {
            if (parent == null)
                return null;

            var menuRoot = new GameObject(
                "RightClickMenu",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            menuRoot.transform.SetParent(parent, false);
            return menuRoot.AddComponent<RightClickMenu>();
        }

        private void Awake()
        {
            CacheRectTransformAndCanvas();
            menuFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (menuFont == null)
                menuFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            EnsureVisualHierarchy();
            CacheHandlers();
            Hide();
        }

        /// <summary>
        ///     Unity callback invoked whenever this transform is reparented. The menu caches
        ///     its <see cref="RectTransform"/> and <see cref="Canvas"/> references so we refresh
        ///     them whenever the hierarchy changes.
        /// </summary>
        private void OnTransformParentChanged()
        {
            CacheRectTransformAndCanvas();
        }

        /// <summary>
        ///     Displays the menu at the supplied screen position and rebuilds the available
        ///     buttons based on the NPC's interaction configuration.
        /// </summary>
        public void Show(NpcInteractable npc, Vector2 position, NpcInteractionOptions options)
        {
            current = npc;

            int optionCount = RebuildButtons(options);
            if (optionCount == 0)
            {
                Hide();
                return;
            }

            CacheRectTransformAndCanvas();
            gameObject.SetActive(true);
            suppressCloseUntilNextFrame = true;

            // The menu renders on a screen-space overlay canvas, so convert the incoming
            // screen coordinate into the appropriate world point for the RectTransform.
            var targetRect = cachedRectTransform != null ? cachedRectTransform : (RectTransform)transform;
            var canvasCamera = parentCanvas != null ? parentCanvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(targetRect, position, canvasCamera, out var worldPoint))
            {
                targetRect.position = worldPoint;
            }
            else
            {
                // Fall back to the previous behaviour if the conversion fails for any reason.
                transform.position = position;
            }
        }

        /// <summary>Hides the menu and clears the active NPC reference.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
            current = null;
            followerAttackType = NpcFollowerAttackType.None;
            suppressCloseUntilNextFrame = false;
        }

        /// <summary>
        ///     Unity update loop that monitors for pointer presses outside of the menu bounds.
        ///     When an input is detected outside the cached rectangle the menu closes
        ///     immediately to mirror OSRS context menu behaviour.
        /// </summary>
        private void Update()
        {
            if (!gameObject.activeSelf)
                return;

            // Skip closing logic for the frame in which the menu just opened so the opening
            // click does not instantly hide the menu again.
            if (suppressCloseUntilNextFrame)
            {
                suppressCloseUntilNextFrame = false;
                return;
            }

            // Ensure the cached references stay valid if the hierarchy changes at runtime.
            if (cachedRectTransform == null)
                CacheRectTransformAndCanvas();

            pointerPressPositions.Clear();
            CollectPointerPresses(pointerPressPositions);
            if (pointerPressPositions.Count == 0)
                return;

            var canvasCamera = parentCanvas != null ? parentCanvas.worldCamera : null;
            foreach (var screenPosition in pointerPressPositions)
            {
                if (cachedRectTransform == null)
                    break;

                bool pointerInside = RectTransformUtility.RectangleContainsScreenPoint(
                    cachedRectTransform,
                    screenPosition,
                    canvasCamera);

                if (pointerInside)
                    continue;

                Hide();
                break;
            }
        }

        /// <summary>
        ///     Ensures the background image, layout, and supporting UI components exist on this
        ///     GameObject. The menu is entirely programmatic, so all styling is configured here.
        /// </summary>
        private void EnsureVisualHierarchy()
        {
            var rectTransform = cachedRectTransform ??= GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.anchoredPosition = Vector2.zero;

            var group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            var background = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            background.color = new Color32(28, 28, 28, 255);

            var layout = GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = ButtonSpacing;
            layout.padding = new RectOffset(10, 10, 10, 10);

            var fitter = GetComponent<ContentSizeFitter>() ?? gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                LayerUtility.SetLayerRecursively(transform, uiLayer);
        }

        /// <summary>
        ///     Populates the lookup table that associates each interaction type with its handler.
        /// </summary>
        private void CacheHandlers()
        {
            handlerLookup[NpcInteractionAction.Attack] = HandleAttackPressed;
            handlerLookup[NpcInteractionAction.Talk] = HandleTalkPressed;
            handlerLookup[NpcInteractionAction.Trade] = HandleTradePressed;
            handlerLookup[NpcInteractionAction.Pickpocket] = HandlePickpocketPressed;
            handlerLookup[NpcInteractionAction.Examine] = HandleExaminePressed;
        }

        /// <summary>
        ///     Captures references to the menu's <see cref="RectTransform"/> and parent
        ///     <see cref="Canvas"/> so input checks and positioning logic can use cached
        ///     values without repeated component lookups.
        /// </summary>
        private void CacheRectTransformAndCanvas()
        {
            cachedRectTransform = GetComponent<RectTransform>();
            parentCanvas = cachedRectTransform != null
                ? cachedRectTransform.GetComponentInParent<Canvas>(true)
                : GetComponentInParent<Canvas>(true);
        }

        /// <summary>
        ///     Collects screen positions for any pointer presses that began this frame across
        ///     mouse, pen, and touch devices. A legacy input fallback ensures environments still
        ///     configured for the old <see cref="Input"/> API behave correctly.
        /// </summary>
        private static void CollectPointerPresses(ICollection<Vector2> positions)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    positions.Add(mouse.position.ReadValue());
                if (mouse.rightButton.wasPressedThisFrame)
                    positions.Add(mouse.position.ReadValue());
                if (mouse.middleButton.wasPressedThisFrame)
                    positions.Add(mouse.position.ReadValue());
            }

            var pen = Pen.current;
            if (pen != null && pen.tip != null && pen.tip.wasPressedThisFrame)
                positions.Add(pen.position.ReadValue());

            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    if (touch.press.wasPressedThisFrame)
                        positions.Add(touch.position.ReadValue());
                }
            }

            // Fallback for any scenes still relying on the legacy input system bindings.
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
                positions.Add(Input.mousePosition);
        }

        /// <summary>
        ///     Destroys any existing buttons and regenerates the menu entries for the supplied
        ///     interaction configuration.
        /// </summary>
        private int RebuildButtons(NpcInteractionOptions options)
        {
            ClearButtons();
            followerAttackType = NpcFollowerAttackType.None;

            if (options == null)
                return 0;

            foreach (var action in options.GetEnabledActions())
            {
                if (action == NpcInteractionAction.Attack)
                {
                    TryAddAttackButtons();
                    continue;
                }

                if (!handlerLookup.TryGetValue(action, out var handler) || handler == null)
                    continue;

                string label = LabelLookup.TryGetValue(action, out var mappedLabel) ? mappedLabel : action.ToString();
                var button = CreateButton($"{action}Button", label, handler);
                activeButtons.Add(button);
            }

            return activeButtons.Count;
        }

        /// <summary>
        ///     Creates a fully styled button matching the OSRS-inspired UI palette and wires the
        ///     requested click handler.
        /// </summary>
        private Button CreateButton(string objectName, string label, UnityAction handler)
        {
            var buttonRoot = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonRoot.transform.SetParent(transform, false);

            var rect = buttonRoot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var background = buttonRoot.GetComponent<Image>();
            background.color = new Color32(48, 48, 48, 255);

            var layoutElement = buttonRoot.GetComponent<LayoutElement>();
            layoutElement.minWidth = ButtonMinWidth;
            layoutElement.minHeight = ButtonMinHeight;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            var button = buttonRoot.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = new Color32(48, 48, 48, 255);
            colors.highlightedColor = new Color32(68, 68, 68, 255);
            colors.pressedColor = new Color32(32, 32, 32, 255);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color32(30, 30, 30, 255);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var textRoot = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textRoot.transform.SetParent(buttonRoot.transform, false);

            var textRect = textRoot.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textRoot.GetComponent<Text>();
            text.text = label;
            text.font = menuFont;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color32(222, 211, 172, 255);
            text.fontSize = 22;
            text.resizeTextForBestFit = false;

            button.onClick.AddListener(handler);

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                LayerUtility.SetLayerRecursively(buttonRoot.transform, uiLayer);

            return button;
        }

        /// <summary>
        ///     Removes and destroys the currently active buttons so the menu can be rebuilt cleanly.
        /// </summary>
        private void ClearButtons()
        {
            for (int i = 0; i < activeButtons.Count; i++)
            {
                var button = activeButtons[i];
                if (button == null)
                    continue;

                button.onClick.RemoveAllListeners();
                Destroy(button.gameObject);
            }

            activeButtons.Clear();
        }

        /// <summary>
        ///     Adds the Attack option (and any follower attack options) to the menu when eligible.
        /// </summary>
        private void TryAddAttackButtons()
        {
            if (current == null || !current.CanPlayerAttack())
                return;

            string label = LabelLookup.TryGetValue(NpcInteractionAction.Attack, out var mappedLabel)
                ? mappedLabel
                : "Attack";
            var attackButton = CreateButton("AttackButton", label, HandleAttackPressed);
            activeButtons.Add(attackButton);

            if (current.TryGetFollowerAttackOption(out var type, out string followerLabel) && !string.IsNullOrEmpty(followerLabel))
            {
                followerAttackType = type;
                var followerButton = CreateButton("FollowerAttackButton", followerLabel, HandleFollowerAttackPressed);
                activeButtons.Add(followerButton);
            }
        }

        /// <summary>Handles the Talk option by forwarding to the interactable and hiding the menu.</summary>
        private void HandleTalkPressed()
        {
            current?.Talk();
            Hide();
        }

        /// <summary>Handles the Trade option by forwarding to the interactable and hiding the menu.</summary>
        private void HandleTradePressed()
        {
            if (current == null)
            {
                Hide();
                return;
            }

            // Always delegate to the interactable so any attached NpcShopOpener can
            // guide the player into the correct interaction radius before the shop opens.
            current.OpenShop();
            Hide();
        }

        /// <summary>Handles the Pickpocket option by forwarding to the interactable and hiding the menu.</summary>
        private void HandlePickpocketPressed()
        {
            current?.Pickpocket();
            Hide();
        }

        /// <summary>Handles the Examine option by forwarding to the interactable and hiding the menu.</summary>
        private void HandleExaminePressed()
        {
            current?.Examine();
            Hide();
        }

        /// <summary>
        ///     Handles the Attack option by delegating to the NPC interactable and closing the menu.
        /// </summary>
        private void HandleAttackPressed()
        {
            current?.TryCommandPlayerAttack();
            Hide();
        }

        /// <summary>
        ///     Handles the Pet/Companion Attack option and resets the cached follower state.
        /// </summary>
        private void HandleFollowerAttackPressed()
        {
            var type = followerAttackType;
            followerAttackType = NpcFollowerAttackType.None;
            current?.ExecuteFollowerAttack(type);
            Hide();
        }
    }
}
