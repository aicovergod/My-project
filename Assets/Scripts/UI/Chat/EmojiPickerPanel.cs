using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.Chat
{
    /// <summary>
    /// Runtime emoji picker that displays every emoji loaded by <see cref="EmojiAtlas"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EmojiPickerPanel : MonoBehaviour
    {
        private static readonly Color32 PanelColor = new Color32(46, 39, 30, 220);
        private static readonly Color32 ScrollColor = new Color32(22, 18, 14, 140);
        private static readonly Color32 ButtonColor = new Color32(48, 38, 28, 200);
        private static readonly Color32 LabelColor = new Color32(255, 238, 170, 255);
        private static readonly Color32 ScrollbarTrackColor = new Color32(33, 27, 21, 220);
        private static readonly Color32 ScrollbarHandleColor = new Color32(206, 180, 108, 255);

        private const float PanelWidth = 256f;
        private const float PanelHeight = 238f;
        private const float ScrollbarWidth = 14f;
        private const float ScrollbarViewportPadding = 4f;
        private const float ScrollbarHandlePadding = 3f;

        private CanvasGroup canvasGroup;
        private RectTransform panelRect;
        private RectTransform gridContent;
        private ScrollRect scrollRect;
        private readonly List<Button> emojiButtons = new List<Button>();
        private Action<string> selectionCallback;
        private bool initialised;

        /// <summary>
        /// Raised whenever the panel fully closes.
        /// </summary>
        public event Action Closed;

        /// <summary>
        /// Indicates whether the picker is currently visible and interactive.
        /// </summary>
        public bool IsOpen => gameObject.activeSelf && canvasGroup != null && canvasGroup.interactable;

        /// <summary>
        /// Factory method that spawns the picker under the supplied parent transform.
        /// </summary>
        public static EmojiPickerPanel Create(Transform parent)
        {
            if (parent == null)
                return null;

            var root = new GameObject(
                "EmojiPickerPanel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Canvas),
                typeof(GraphicRaycaster),
                typeof(LayoutElement));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panel = root.AddComponent<EmojiPickerPanel>();
            panel.canvasGroup = root.GetComponent<CanvasGroup>();
            panel.ConfigureCanvas(parent);
            panel.BuildUi();
            panel.HideImmediate();
            return panel;
        }

        /// <summary>
        /// Ensures the picker renders above chat UI by configuring a local canvas.
        /// </summary>
        /// <param name="parent">Transform the picker was anchored to.</param>
        private void ConfigureCanvas(Transform parent)
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
                return;

            canvas.overrideSorting = true;

            var parentCanvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            if (parentCanvas != null)
            {
                canvas.renderMode = parentCanvas.renderMode;
                canvas.sortingLayerID = parentCanvas.sortingLayerID;
                canvas.sortingOrder = parentCanvas.sortingOrder + 10;
                canvas.worldCamera = parentCanvas.worldCamera;
                canvas.planeDistance = parentCanvas.planeDistance;
                canvas.additionalShaderChannels = parentCanvas.additionalShaderChannels;
            }
            else
            {
                canvas.sortingOrder = 1000;
            }
        }

        /// <summary>
        /// Opens the picker, anchoring it near the supplied button and registering the selection callback.
        /// </summary>
        /// <param name="anchor">UI element the picker should align with.</param>
        /// <param name="onEmojiSelected">Callback invoked when an emoji is chosen.</param>
        public void Open(RectTransform anchor, Action<string> onEmojiSelected)
        {
            EnsureInitialised();

            selectionCallback = onEmojiSelected;
            gameObject.SetActive(true);
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            transform.SetAsLastSibling();

            PositionPanel(anchor);
            if (scrollRect != null)
            {
                // Always reopen with the picker scrolled to the top so the newest emojis stay visible
                // near the anchor button and keyboard/gamepad navigation begins on the first entry.
                scrollRect.StopMovement();
                scrollRect.verticalNormalizedPosition = 1f;
                var bar = scrollRect.verticalScrollbar;
                bar?.SetValueWithoutNotify(1f);
            }
            FocusFirstEmoji();
        }

        /// <summary>
        /// Closes the picker and clears the pending callback.
        /// </summary>
        public void Close()
        {
            if (!IsOpen)
                return;

            selectionCallback = null;
            HideImmediate();
            Closed?.Invoke();
        }

        private void Update()
        {
            if (!IsOpen)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
                Close();
        }

        private void BuildUi()
        {
            if (initialised)
                return;

            initialised = true;
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            var layoutElement = GetComponent<LayoutElement>();
            if (layoutElement != null)
                layoutElement.ignoreLayout = true;

            var overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image), typeof(Button));
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.SetParent(transform, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var overlayImage = overlay.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0f);
            overlayImage.raycastTarget = true;

            var overlayButton = overlay.GetComponent<Button>();
            overlayButton.onClick.AddListener(Close);

            var panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.SetParent(transform, false);
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 0f);
            panelRect.pivot = new Vector2(0f, 0f);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = PanelColor;

            var layout = panelObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var titleObject = new GameObject("Title", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            var titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(panelObject.transform, false);
            var titleLayout = titleObject.GetComponent<LayoutElement>();
            titleLayout.preferredHeight = 24f;
            titleLayout.flexibleHeight = 0f;
            var titleText = titleObject.GetComponent<Text>();
            titleText.text = "Emojis";
            titleText.fontSize = 18;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.color = LabelColor;
            LegacyFontProvider.ApplyTo(titleText);

            var scrollObject = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            var scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.SetParent(panelObject.transform, false);
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = Vector2.zero;
            scrollRectTransform.offsetMax = Vector2.zero;

            var scrollLayout = scrollObject.GetComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.minHeight = 0f;

            var scrollImage = scrollObject.GetComponent<Image>();
            scrollImage.color = ScrollColor;

            scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Build a dedicated vertical scrollbar anchored to the right edge so the picker can
            // surface additional emoji rows without expanding the modal footprint.
            var scrollbarObject = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.SetParent(scrollRectTransform, false);
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.sizeDelta = new Vector2(ScrollbarWidth, 0f);
            scrollbarRect.anchoredPosition = Vector2.zero;

            var scrollbarImage = scrollbarObject.GetComponent<Image>();
            scrollbarImage.color = ScrollbarTrackColor;
            scrollbarImage.raycastTarget = true;

            var slidingArea = new GameObject("SlidingArea", typeof(RectTransform));
            var slidingRect = slidingArea.GetComponent<RectTransform>();
            slidingRect.SetParent(scrollbarRect, false);
            slidingRect.anchorMin = new Vector2(0f, 0f);
            slidingRect.anchorMax = new Vector2(1f, 1f);
            slidingRect.offsetMin = new Vector2(ScrollbarHandlePadding, ScrollbarHandlePadding);
            slidingRect.offsetMax = new Vector2(-ScrollbarHandlePadding, -ScrollbarHandlePadding);

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.SetParent(slidingRect, false);
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            var handleImage = handleObject.GetComponent<Image>();
            handleImage.color = ScrollbarHandleColor;
            handleImage.raycastTarget = true;

            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.value = 1f;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(scrollRectTransform, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            // Reserve horizontal space for the scrollbar so the grid keeps its six-column layout
            // while still leaving a small visual gutter between the track and emoji buttons.
            viewportRect.offsetMax = new Vector2(-(ScrollbarWidth + ScrollbarViewportPadding), 0f);

            var viewportGraphic = viewport.AddComponent<Image>();
            viewportGraphic.color = new Color(0f, 0f, 0f, 0.05f);
            viewportGraphic.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup));
            gridContent = content.GetComponent<RectTransform>();
            gridContent.SetParent(viewportRect, false);
            // Anchor the grid content to the top of the viewport so the first emoji aligns with the
            // top-left corner of the picker rather than being centred within the ScrollRect.
            gridContent.anchorMin = new Vector2(0f, 1f);
            gridContent.anchorMax = new Vector2(1f, 1f);
            gridContent.pivot = new Vector2(0.5f, 1f);
            gridContent.anchoredPosition = Vector2.zero;
            gridContent.offsetMin = Vector2.zero;
            gridContent.offsetMax = Vector2.zero;
            var grid = content.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(34f, 34f);
            grid.spacing = new Vector2(3f, 3f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;
            // Force the grid layout to begin in the upper-left corner so emoji buttons pack from the
            // expected origin when the picker opens.
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;

            scrollRect.viewport = viewportRect;
            scrollRect.content = gridContent;
            // Link the ScrollRect to the new scrollbar so both mouse wheel and direct drag inputs
            // keep the content, handle, and scroll position in sync.
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            PopulateEmojiButtons();
            // Ensure the layout updates immediately with the new anchoring and snap the scroll
            // position so the first emoji sits flush with the picker top edge.
            LayoutRebuilder.ForceRebuildLayoutImmediate(gridContent);
            scrollRect.verticalNormalizedPosition = 1f;
            scrollbar.SetValueWithoutNotify(1f);
        }

        private void PopulateEmojiButtons()
        {
            if (gridContent == null)
                return;

            foreach (Transform child in gridContent)
                Destroy(child.gameObject);
            emojiButtons.Clear();

            var atlas = EmojiAtlas.Instance as IEmojiAtlas;
            var entries = atlas?.GetAllEmojis();
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var definition = entries[i];
                var cellObject = new GameObject($"Emoji_{definition.Key}", typeof(RectTransform), typeof(Image), typeof(Button));
                var cellRect = cellObject.GetComponent<RectTransform>();
                cellRect.SetParent(gridContent, false);
                cellRect.localScale = Vector3.one;

                var cellImage = cellObject.GetComponent<Image>();
                cellImage.color = ButtonColor;

                var button = cellObject.GetComponent<Button>();
                string key = definition.Key;
                button.onClick.AddListener(() => OnEmojiButtonClicked(key));

                var layout = cellObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 2f;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                var iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.SetParent(cellObject.transform, false);
                var iconImage = iconObject.GetComponent<Image>();
                definition.ApplyTo(iconImage, 32f);

                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.SetParent(cellObject.transform, false);
                var labelText = labelObject.GetComponent<Text>();
                labelText.text = key;
                labelText.fontSize = 12;
                labelText.alignment = TextAnchor.MiddleCenter;
                labelText.color = LabelColor;
                LegacyFontProvider.ApplyTo(labelText);

                emojiButtons.Add(button);
            }
        }

        private void OnEmojiButtonClicked(string key)
        {
            selectionCallback?.Invoke(key);
            Close();
        }

        private void PositionPanel(RectTransform anchor)
        {
            if (panelRect == null)
                return;

            Vector2 anchoredPosition = new Vector2(12f, 60f);
            if (anchor != null)
            {
                var rootRect = transform as RectTransform;
                if (rootRect != null)
                {
                    Vector3[] corners = new Vector3[4];
                    anchor.GetWorldCorners(corners);
                    Vector3 target = corners[0];
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, RectTransformUtility.WorldToScreenPoint(null, target), null, out local);
                    anchoredPosition = local + new Vector2(0f, anchor.rect.height);
                }
            }

            panelRect.anchoredPosition = anchoredPosition;
        }

        private void FocusFirstEmoji()
        {
            if (emojiButtons.Count == 0)
                return;

            var button = emojiButtons[0];
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        private void HideImmediate()
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
        }

        private void EnsureInitialised()
        {
            if (!initialised)
                BuildUi();
        }
    }
}
