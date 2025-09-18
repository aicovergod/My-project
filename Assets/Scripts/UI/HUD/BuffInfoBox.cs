using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Status;
using Status.Antifire;
using Status.Poison;
using Util;

namespace UI.HUD
{
    /// <summary>
    /// Displays a single buff infobox mirroring the Old School RuneScape UI style.
    /// </summary>
    public class BuffInfoBox : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image frameImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private Text nameText;
        [SerializeField] private Text timerText;
        [SerializeField] private Color normalTimerColor = Color.white;
        [SerializeField] private Color warningTimerColor = new Color32(255, 196, 0, 255);
        [SerializeField] private CanvasGroup canvasGroup;

        private Sprite loadedIcon;

        // Cached references used for tooltip positioning and contextual data.
        private RectTransform rectTransform;
        private RectTransform cachedCanvasRect;
        private BuffTooltipController tooltipController;

        // Tracks whether the pointer is currently hovering this infobox.
        private bool pointerHovering;

        private const float DefaultPoisonIntervalSeconds = 15f;

        // Cached antifire controller so the tooltip can display live mitigation values.
        private AntifireProtectionController antifireProtection;
        private GameObject antifireTarget;

        // Cached poison controller used to expose live tick damage values.
        private PoisonController poisonController;
        private GameObject poisonTarget;

        // Stores the last tooltip payload so we only refresh when something actually changes.
        private string lastTooltipTitle;
        private string lastTooltipBody;

        public BuffTimerInstance BoundBuff { get; private set; }

        /// <summary>
        /// Creates a fully wired buff infobox at runtime using the expected OSRS styling.
        /// </summary>
        /// <param name="parent">Container that will hold the created infobox.</param>
        public static BuffInfoBox Create(RectTransform parent)
        {
            if (parent == null)
                throw new System.ArgumentNullException(nameof(parent));

            const float slotSize = 32f;
            const float iconSize = 28f;
            const float textPadding = 1f;

            // Root object that mimics the prefab layout.  The anchors/pivot align
            // with the HUD expectation of stacking boxes downward from the
            // minimap's top-right corner.
            var rootGO = new GameObject("BuffInfoBox", typeof(RectTransform), typeof(CanvasGroup));
            int parentLayer = parent.gameObject.layer;
            rootGO.layer = parentLayer;

            var rootRect = rootGO.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.sizeDelta = new Vector2(slotSize, slotSize);

            var component = rootGO.AddComponent<BuffInfoBox>();

            var canvasGroup = rootGO.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0.92f;
            canvasGroup.interactable = false;
            // Enable raycasts so pointer hover events can be detected for tooltips.
            canvasGroup.blocksRaycasts = true;
            component.canvasGroup = canvasGroup;

            var frameGO = new GameObject("Slot", typeof(Image));
            frameGO.layer = parentLayer;
            frameGO.transform.SetParent(rootGO.transform, false);
            var frameImage = frameGO.GetComponent<Image>();
            // Allow the frame to receive pointer events for tooltip display.
            frameImage.raycastTarget = true;
            frameImage.sprite = Resources.Load<Sprite>("Interfaces/Equipment/Empty_Slot");
            frameImage.color = Color.white;
            var frameRect = frameImage.rectTransform;
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = Vector2.zero;
            component.frameImage = frameImage;

            var iconGO = new GameObject("Icon", typeof(Image));
            iconGO.layer = parentLayer;
            iconGO.transform.SetParent(frameGO.transform, false);
            var iconImage = iconGO.GetComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconRect = iconImage.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);
            iconRect.anchoredPosition = Vector2.zero;
            component.iconImage = iconImage;
            iconImage.color = Color.clear;

            var legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var nameGO = new GameObject("Name", typeof(Text));
            nameGO.layer = parentLayer;
            nameGO.transform.SetParent(frameGO.transform, false);
            var nameText = nameGO.GetComponent<Text>();
            nameText.font = legacyFont;
            nameText.fontSize = 7;
            nameText.alignment = TextAnchor.UpperCenter;
            nameText.color = new Color32(255, 240, 187, 255);
            nameText.raycastTarget = false;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.verticalOverflow = VerticalWrapMode.Truncate;
            nameText.supportRichText = false;
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = new Vector2(0.5f, 1f);
            nameRect.anchorMax = new Vector2(0.5f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -textPadding);
            nameRect.sizeDelta = new Vector2(slotSize - (textPadding * 2f), 9f);
            component.nameText = nameText;

            nameText.text = string.Empty;

            var timerGO = new GameObject("Timer", typeof(Text));
            timerGO.layer = parentLayer;
            timerGO.transform.SetParent(frameGO.transform, false);

            var timerText = timerGO.GetComponent<Text>();
            timerText.font = legacyFont;
            timerText.fontSize = 6;
            timerText.alignment = TextAnchor.LowerCenter;
            timerText.color = new Color32(212, 212, 212, 255);
            timerText.raycastTarget = false;
            timerText.horizontalOverflow = HorizontalWrapMode.Overflow;
            timerText.verticalOverflow = VerticalWrapMode.Truncate;
            timerText.supportRichText = false;
            timerText.text = string.Empty;
            var timerRect = timerText.rectTransform;
            timerRect.anchorMin = new Vector2(0.5f, 0f);
            timerRect.anchorMax = new Vector2(0.5f, 0f);
            timerRect.pivot = new Vector2(0.5f, 0f);
            timerRect.anchoredPosition = new Vector2(0f, textPadding);
            timerRect.sizeDelta = new Vector2(slotSize - (textPadding * 2f), 9f);
            component.timerText = timerText;

            component.normalTimerColor = timerText.color;

            component.ResetVisuals();

            return component;
        }

        private void Awake()
        {
            var legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (nameText != null && nameText.font == null)
                nameText.font = legacyFont;
            if (timerText != null && timerText.font == null)
                timerText.font = legacyFont;
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            if (canvasGroup != null)
                canvasGroup.blocksRaycasts = true;

            if (frameImage != null)
                frameImage.raycastTarget = true;

            if (rectTransform == null)
                rectTransform = GetComponent<RectTransform>();

            if (cachedCanvasRect == null)
            {
                var canvas = GetComponentInParent<Canvas>();
                if (canvas != null)
                    cachedCanvasRect = canvas.transform as RectTransform;
            }
        }

        /// <summary>
        /// Initialise the infobox with the provided buff instance.
        /// </summary>
        public void Bind(BuffTimerInstance instance)
        {
            BoundBuff = instance;
            if (nameText != null)
                nameText.text = instance.DisplayName;
            ApplyIcon(instance.Definition.iconId);
            UpdateTimer(instance);
            SetWarning(false);
            CacheTooltipSources(instance);
            ClearTooltipTracking();
            if (pointerHovering)
                UpdateTooltip(true);
        }

        /// <summary>
        /// Update the timer text using the buff's remaining ticks.
        /// </summary>
        public void UpdateTimer(BuffTimerInstance instance)
        {
            if (timerText == null)
                return;

            if (instance.IsIndefinite && !instance.IsRecurring)
            {
                timerText.text = "--";
                return;
            }

            float seconds = Mathf.Max(0f, instance.RemainingTicks) * Ticker.TickDuration;
            timerText.text = FormatTime(seconds);

            if (pointerHovering)
                UpdateTooltip();
        }

        /// <summary>
        /// Highlight the timer when an expiry warning is issued.
        /// </summary>
        public void SetWarning(bool active)
        {
            if (timerText != null)
                timerText.color = active ? warningTimerColor : normalTimerColor;
            if (canvasGroup != null)
                canvasGroup.alpha = active ? 1f : 0.92f;
        }

        /// <summary>
        /// Resets any temporary styling and returns to the default state.
        /// </summary>
        public void ResetVisuals()
        {
            SetWarning(false);
        }

        private void LateUpdate()
        {
            if (pointerHovering)
                UpdateTooltip();
        }

        /// <summary>
        /// Displays the tooltip when the pointer starts hovering the buff icon.
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            pointerHovering = true;
            ClearTooltipTracking();
            UpdateTooltip(true);
        }

        /// <summary>
        /// Hides the tooltip when the pointer leaves the buff icon.
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            pointerHovering = false;
            tooltipController ??= ResolveTooltipController();
            tooltipController?.Hide();
            ClearTooltipTracking();
        }

        private void OnDisable()
        {
            if (!pointerHovering)
                return;

            pointerHovering = false;
            tooltipController ??= ResolveTooltipController();
            tooltipController?.Hide();
            ClearTooltipTracking();
        }

        /// <summary>
        /// Refreshes the tooltip content if it has changed or if a reposition is requested.
        /// </summary>
        private void UpdateTooltip(bool forcePosition = false)
        {
            if (BoundBuff == null)
                return;

            tooltipController ??= ResolveTooltipController();
            if (tooltipController == null || rectTransform == null)
                return;

            string title = BoundBuff.DisplayName;
            string body = BuildTooltipBody(BoundBuff);
            bool contentChanged = forcePosition
                                  || !string.Equals(title, lastTooltipTitle)
                                  || !string.Equals(body, lastTooltipBody);

            if (!contentChanged)
                return;

            tooltipController.Show(rectTransform, title, body);
            lastTooltipTitle = title;
            lastTooltipBody = body;
        }

        /// <summary>
        /// Clears cached tooltip state so the next refresh rebuilds the content.
        /// </summary>
        private void ClearTooltipTracking()
        {
            lastTooltipTitle = null;
            lastTooltipBody = null;
        }

        /// <summary>
        /// Caches contextual data used for tooltip messaging based on the bound buff.
        /// </summary>
        private void CacheTooltipSources(BuffTimerInstance instance)
        {
            if (instance == null)
            {
                ClearAntifireCache();
                ClearPoisonCache();
                return;
            }

            switch (instance.Definition.type)
            {
                case BuffType.Antifire:
                case BuffType.SuperAntifire:
                    CacheAntifireController(instance.Target);
                    ClearPoisonCache();
                    break;
                case BuffType.Poison:
                    CachePoisonController(instance.Target);
                    ClearAntifireCache();
                    break;
                default:
                    ClearAntifireCache();
                    ClearPoisonCache();
                    break;
            }
        }

        /// <summary>
        /// Retrieves the antifire controller from the buff target so mitigation can be queried.
        /// </summary>
        private void CacheAntifireController(GameObject target)
        {
            if (antifireTarget == target && antifireProtection != null)
                return;

            antifireTarget = target;
            antifireProtection = null;

            if (target != null)
            {
                antifireProtection = target.GetComponent<AntifireProtectionController>()
                    ?? target.GetComponentInChildren<AntifireProtectionController>()
                    ?? target.GetComponentInParent<AntifireProtectionController>();
            }

            if (antifireProtection == null)
            {
#if UNITY_2022_2_OR_NEWER
                antifireProtection = UnityEngine.Object.FindFirstObjectByType<AntifireProtectionController>();
#else
                antifireProtection = UnityEngine.Object.FindObjectOfType<AntifireProtectionController>();
#endif
            }
        }

        /// <summary>
        /// Resets cached antifire lookup data when no longer required.
        /// </summary>
        private void ClearAntifireCache()
        {
            antifireTarget = null;
            antifireProtection = null;
        }

        /// <summary>
        /// Retrieves the poison controller so the tooltip can display live tick damage values.
        /// </summary>
        private void CachePoisonController(GameObject target)
        {
            if (poisonTarget == target && poisonController != null)
                return;

            poisonTarget = target;
            poisonController = null;

            if (target != null)
            {
                poisonController = target.GetComponent<PoisonController>()
                    ?? target.GetComponentInChildren<PoisonController>()
                    ?? target.GetComponentInParent<PoisonController>();
            }
        }

        /// <summary>
        /// Clears cached poison lookups when the tooltip is no longer displaying poison.
        /// </summary>
        private void ClearPoisonCache()
        {
            poisonTarget = null;
            poisonController = null;
        }

        /// <summary>
        /// Builds the tooltip description text for the current buff.
        /// </summary>
        private string BuildTooltipBody(BuffTimerInstance instance)
        {
            if (instance == null)
                return string.Empty;

            switch (instance.Definition.type)
            {
                case BuffType.Antifire:
                case BuffType.SuperAntifire:
                    if (antifireProtection == null && instance.Target != null)
                        CacheAntifireController(instance.Target);

                    if (antifireProtection == null)
                    {
                        return instance.Definition.type == BuffType.SuperAntifire
                            ? "100% protection"
                            : "Protection data unavailable";
                    }

                    float mitigation = antifireProtection.GetProtectionPercentage();
                    int percent = Mathf.Clamp(Mathf.RoundToInt(mitigation * 100f), 0, 100);
                    return $"{percent}% protection";
                case BuffType.Poison:
                    if (poisonController == null && instance.Target != null)
                        CachePoisonController(instance.Target);

                    if (poisonController == null)
                        return "Damage data unavailable";

                    var effect = poisonController.ActiveEffect;
                    if (effect == null || !effect.IsActive || effect.Config == null)
                        return "Damage data unavailable";

                    int damage = Mathf.Max(0, effect.CurrentDamage);
                    float intervalSeconds = effect.Config.tickIntervalSeconds > 0f
                        ? effect.Config.tickIntervalSeconds
                        : DefaultPoisonIntervalSeconds;
                    string intervalLabel = intervalSeconds % 1f == 0f
                        ? intervalSeconds.ToString("0", CultureInfo.InvariantCulture)
                        : intervalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
                    return $"{damage} Damage every {intervalLabel} seconds";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Finds or creates the tooltip controller used to display buff hover details.
        /// </summary>
        private BuffTooltipController ResolveTooltipController()
        {
            if (BuffTooltipController.Instance != null)
                return BuffTooltipController.Instance;

            if (cachedCanvasRect == null)
            {
                var canvas = GetComponentInParent<Canvas>();
                if (canvas != null)
                    cachedCanvasRect = canvas.transform as RectTransform;
            }

            return BuffTooltipController.GetOrCreate(cachedCanvasRect);
        }

        /// <summary>
        /// Loads an icon from Resources/UI/Buffs when an explicit sprite is not assigned via the inspector.
        /// </summary>
        private void ApplyIcon(string iconId)
        {
            if (iconImage == null)
                return;

            loadedIcon = null;
            if (!string.IsNullOrEmpty(iconId))
            {
                string[] candidatePaths =
                {
                    $"UI/Buffs/{iconId}",
                    $"ui/{iconId}"
                };

                for (int i = 0; i < candidatePaths.Length; i++)
                {
                    loadedIcon = Resources.Load<Sprite>(candidatePaths[i]);
                    if (loadedIcon != null)
                        break;
                }
            }

            iconImage.sprite = loadedIcon;
            iconImage.color = loadedIcon != null ? Color.white : Color.clear;
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int totalSeconds = Mathf.RoundToInt(seconds);
            int minutes = totalSeconds / 60;
            int secs = totalSeconds % 60;
            if (minutes > 0)
                return $"{minutes:00}:{secs:00}";
            return secs >= 10 ? secs.ToString() : $"0{secs}";
        }
    }
}
