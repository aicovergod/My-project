using Inventory;
using UnityEngine;
using UnityEngine.UI;
using Util;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Displays woodcutting progress above the current tree.
    /// </summary>
    public class WoodcuttingHUD : MonoBehaviour, ITickable
    {
        private WoodcuttingSkill skill;
        private Transform target;
        private Image progressImage;
        private GameObject progressRoot;
        private GameObject axeRoot;
        private SpriteRenderer axeRenderer;
        private Canvas progressCanvas;
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);
        private readonly Vector3 axeOffset = Vector3.zero;

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float step;
        // Tracks how long the current interpolation segment should last so we sync perfectly with tick events.
        private float segmentDuration = Ticker.TickDuration;
        // Flag used so we can hold the progress bar at 100% for a full tick before resetting back to 0.
        private bool awaitingResetTick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            if (UnityEngine.Object.FindObjectOfType<WoodcuttingHUD>() != null)
                return;

            var go = new GameObject("WoodcuttingHUD");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<WoodcuttingHUD>();
        }

        private void Awake()
        {
            skill = FindObjectOfType<WoodcuttingSkill>();

            if (skill != null)
            {
                skill.OnStartChopping += HandleStart;
                skill.OnStopChopping += HandleStop;
            }

            CreateProgressBar();
            CreateAxeSprite();
        }

        private void CreateProgressBar()
        {
            progressRoot = new GameObject("WoodcuttingProgress");
            progressRoot.transform.SetParent(transform);

            progressCanvas = progressRoot.AddComponent<Canvas>();
            progressCanvas.renderMode = RenderMode.WorldSpace;
            progressCanvas.overrideSorting = true;
            progressRoot.AddComponent<CanvasScaler>();
            progressRoot.AddComponent<GraphicRaycaster>();
            progressRoot.transform.localScale = Vector3.one * 0.01f;

            var bg = new GameObject("Background");
            bg.transform.SetParent(progressRoot.transform, false);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.5f);
            var bgSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            bgImage.sprite = bgSprite;
            var bgRect = bgImage.rectTransform;
            bgRect.sizeDelta = new Vector2(150f, 25f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(bg.transform, false);
            progressImage = fill.AddComponent<Image>();
            progressImage.color = Color.green;
            progressImage.sprite = bgSprite;
            progressImage.type = Image.Type.Filled;
            progressImage.fillMethod = Image.FillMethod.Horizontal;
            progressImage.fillAmount = 0f;
            var fillRect = progressImage.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            progressRoot.SetActive(false);
        }

        private void CreateAxeSprite()
        {
            axeRoot = new GameObject("WoodcuttingAxe");
            axeRoot.transform.SetParent(transform);
            axeRenderer = axeRoot.AddComponent<SpriteRenderer>();
            axeRenderer.sortingOrder = 100;
            axeRoot.SetActive(false);
        }

        private void HandleStart(TreeNode tree)
        {
            target = tree.transform;
            progressImage.fillAmount = 0f;
            currentFill = 0f;
            tickTimer = 0f;
            step = skill.CurrentChopIntervalTicks > 0 ? 1f / skill.CurrentChopIntervalTicks : 0f;
            nextFill = step;
            awaitingResetTick = false;
            segmentDuration = ResolveInitialSegmentDuration();
            progressRoot.SetActive(true);

            var axe = skill.CurrentAxe;
            if (axe != null && axeRenderer != null)
            {
                var item = Resources.Load<ItemData>("Item/" + axe.Id);
                if (item != null && item.icon != null)
                {
                    axeRenderer.sprite = item.icon;
                    axeRoot.SetActive(true);
                }
            }

            var targetRenderer = tree.GetComponent<SpriteRenderer>();
            if (targetRenderer != null)
            {
                if (progressCanvas != null)
                {
                    progressCanvas.sortingLayerID = targetRenderer.sortingLayerID;
                    progressCanvas.sortingOrder = targetRenderer.sortingOrder + 1;
                }
                if (axeRenderer != null)
                {
                    axeRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                    axeRenderer.sortingOrder = targetRenderer.sortingOrder + 2;
                }
            }

            if (Ticker.Instance != null)
                Ticker.Instance.Subscribe(this);
        }

        private void HandleStop()
        {
            target = null;
            progressRoot.SetActive(false);
            awaitingResetTick = false;
            segmentDuration = Ticker.TickDuration;
            if (axeRoot != null)
            {
                axeRoot.SetActive(false);
                if (axeRenderer != null)
                    axeRenderer.sprite = null;
            }
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
        }

        private void Update()
        {
            if (target == null || progressImage == null || skill == null)
                return;

            progressRoot.transform.position = target.position + offset;
            if (axeRoot != null && axeRoot.activeSelf)
                axeRoot.transform.position = target.position + axeOffset;

            tickTimer += Time.deltaTime;
            if (segmentDuration <= 0f)
            {
                progressImage.fillAmount = nextFill;
                return;
            }

            float t = Mathf.Clamp01(tickTimer / segmentDuration);
            progressImage.fillAmount = Mathf.Lerp(currentFill, nextFill, t);
        }

        public void OnTick()
        {
            segmentDuration = Ticker.TickDuration;
            if (target == null || skill == null || !skill.IsChopping)
                return;

            tickTimer = 0f;
            // When no valid interval exists we keep the bar hidden at 0.
            if (step <= 0f)
            {
                currentFill = 0f;
                nextFill = 0f;
                awaitingResetTick = false;
                if (progressImage != null)
                    progressImage.fillAmount = 0f;
                return;
            }

            if (awaitingResetTick)
            {
                // The previous tick finished a cycle so we now snap back to the start.
                awaitingResetTick = false;
                currentFill = 0f;
                nextFill = step;
                if (progressImage != null)
                    progressImage.fillAmount = 0f;
                return;
            }

            currentFill = nextFill;

            if (currentFill >= 1f)
            {
                // Hold the bar at 100% for a full tick so the player sees a complete animation.
                currentFill = 1f;
                nextFill = 1f;
                awaitingResetTick = true;
            }
            else
            {
                nextFill = Mathf.Min(1f, currentFill + step);
            }
        }

        private void OnDestroy()
        {
            if (skill != null)
            {
                skill.OnStartChopping -= HandleStart;
                skill.OnStopChopping -= HandleStop;
            }

            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
        }

        /// <summary>
        /// Determines how long the first interpolation segment should last based on the ticker's remaining time.
        /// </summary>
        private float ResolveInitialSegmentDuration()
        {
            if (Ticker.Instance == null)
            {
                return Ticker.TickDuration;
            }

            float remaining = Ticker.Instance.TimeUntilNextTick;
            return remaining > 0f ? remaining : Ticker.TickDuration;
        }
    }
}
