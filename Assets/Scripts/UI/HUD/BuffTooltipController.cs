using System;
using UnityEngine;
using UnityEngine.UI;

namespace UI.HUD
{
    /// <summary>
    /// Creates and manages a lightweight tooltip used by the buff HUD to display
    /// contextual information when the player hovers status icons.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuffTooltipController : MonoBehaviour
    {
        private static BuffTooltipController instance;

        /// <summary>
        /// Global accessor so individual buff boxes can reuse the same tooltip instance.
        /// </summary>
        public static BuffTooltipController Instance => instance;

        [SerializeField, Tooltip("Screen space offset applied from the buff icon towards the tooltip.")]
        private Vector2 screenOffset = new Vector2(-12f, -8f);

        [SerializeField, Tooltip("Background colour of the tooltip panel.")]
        private Color backgroundColor = new Color32(0, 0, 0, 220);

        [SerializeField, Tooltip("Text colour for the tooltip title line.")]
        private Color titleColor = new Color32(255, 240, 187, 255);

        [SerializeField, Tooltip("Text colour for the tooltip description line.")]
        private Color bodyColor = new Color32(212, 212, 212, 255);

        private RectTransform rectTransform;
        private RectTransform parentRect;
        private Canvas parentCanvas;
        private CanvasGroup canvasGroup;
        private Text titleText;
        private Text bodyText;
        private bool configured;

        private readonly Vector3[] worldCorners = new Vector3[4];

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            rectTransform = transform as RectTransform;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>
        /// Finds an existing tooltip controller or creates a new instance under the supplied parent rect.
        /// </summary>
        public static BuffTooltipController GetOrCreate(RectTransform parent)
        {
            if (instance != null)
                return instance;

            if (parent == null)
                return null;

            var go = new GameObject("BuffTooltip", typeof(RectTransform));
            var controller = go.AddComponent<BuffTooltipController>();
            controller.Configure(parent);
            return controller;
        }

        /// <summary>
        /// Configures the tooltip to live under the provided parent rect and builds its UI hierarchy.
        /// </summary>
        public void Configure(RectTransform parent)
        {
            if (configured)
                return;

            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            parentRect = parent;
            parentCanvas = parent.GetComponentInParent<Canvas>();

            if (rectTransform == null)
                rectTransform = transform as RectTransform;

            rectTransform.SetParent(parent, false);
            rectTransform.anchorMin = new Vector2(1f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(1f, 1f);
            rectTransform.anchoredPosition = Vector2.zero;

            BuildVisuals();
            Hide();
            configured = true;
        }

        /// <summary>
        /// Displays the tooltip near the supplied source rect using the provided title and body text.
        /// </summary>
        public void Show(RectTransform source, string title, string body)
        {
            if (!configured || canvasGroup == null)
                return;

            bool hasTitle = !string.IsNullOrEmpty(title);
            bool hasBody = !string.IsNullOrEmpty(body);

            if (!hasTitle && !hasBody)
            {
                Hide();
                return;
            }

            if (titleText != null)
            {
                titleText.gameObject.SetActive(hasTitle);
                titleText.text = hasTitle ? title : string.Empty;
            }

            if (bodyText != null)
            {
                bodyText.gameObject.SetActive(hasBody);
                bodyText.text = hasBody ? body : string.Empty;
            }

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            PositionRelativeTo(source);
        }

        /// <summary>
        /// Hides the tooltip without destroying it so it is ready for the next hover event.
        /// </summary>
        public void Hide()
        {
            if (canvasGroup == null)
                return;

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            if (titleText != null)
                titleText.text = string.Empty;
            if (bodyText != null)
                bodyText.text = string.Empty;
        }

        /// <summary>
        /// Creates the background, layout components and text elements that make up the tooltip.
        /// </summary>
        private void BuildVisuals()
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0f;

            var background = gameObject.AddComponent<Image>();
            background.color = backgroundColor;
            background.raycastTarget = false;

            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            var fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var titleGO = new GameObject("Title", typeof(Text));
            titleGO.transform.SetParent(transform, false);
            titleText = titleGO.GetComponent<Text>();
            titleText.font = legacyFont;
            titleText.fontSize = 11;
            titleText.alignment = TextAnchor.UpperLeft;
            titleText.color = titleColor;
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;
            titleText.supportRichText = false;
            titleText.raycastTarget = false;
            titleText.text = string.Empty;

            var bodyGO = new GameObject("Body", typeof(Text));
            bodyGO.transform.SetParent(transform, false);
            bodyText = bodyGO.GetComponent<Text>();
            bodyText.font = legacyFont;
            bodyText.fontSize = 10;
            bodyText.alignment = TextAnchor.UpperLeft;
            bodyText.color = bodyColor;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            bodyText.supportRichText = false;
            bodyText.raycastTarget = false;
            bodyText.text = string.Empty;
        }

        /// <summary>
        /// Positions the tooltip relative to the hovered buff icon while respecting the configured offset.
        /// </summary>
        private void PositionRelativeTo(RectTransform source)
        {
            if (source == null || parentRect == null || rectTransform == null)
                return;

            source.GetWorldCorners(worldCorners);
            Vector3 topRight = worldCorners[2];

            Camera cam = parentCanvas != null ? parentCanvas.worldCamera : null;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, topRight);
            screenPoint += screenOffset;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parentRect, screenPoint, cam, out Vector3 worldPos))
            {
                rectTransform.position = worldPos;
            }
            else
            {
                rectTransform.position = topRight + (Vector3)screenOffset;
            }
        }
    }
}
