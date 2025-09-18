using Inventory;
using UnityEngine;
using UnityEngine.UI;
using Util;

namespace Skills.Fishing
{
    public class FishingHUD : MonoBehaviour, ITickable
    {
        private FishingSkill skill;
        private Transform target;
        private Image progressImage;
        private GameObject progressRoot;
        private GameObject toolRoot;
        private SpriteRenderer toolRenderer;
        private Canvas progressCanvas;
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);
        private readonly Vector3 toolOffset = Vector3.zero;

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float step;
        // Keeps track of whether the bar should reset after being displayed at full progress for one tick.
        private bool awaitingResetTick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            if (UnityEngine.Object.FindObjectOfType<FishingHUD>() != null)
                return;

            var go = new GameObject("FishingHUD");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<FishingHUD>();
        }

        private void Awake()
        {
            skill = FindObjectOfType<FishingSkill>();

            if (skill != null)
            {
                skill.OnStartFishing += HandleStart;
                skill.OnStopFishing += HandleStop;
            }

            CreateProgressBar();
            CreateToolSprite();
        }

        private void CreateProgressBar()
        {
            progressRoot = new GameObject("FishingProgress");
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
            progressImage.color = Color.blue;
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

        private void CreateToolSprite()
        {
            toolRoot = new GameObject("FishingTool");
            toolRoot.transform.SetParent(transform);
            toolRenderer = toolRoot.AddComponent<SpriteRenderer>();
            toolRenderer.sortingOrder = 100;
            toolRoot.SetActive(false);
        }

        private void HandleStart(FishableSpot spot)
        {
            target = spot.transform;
            progressImage.fillAmount = 0f;
            currentFill = 0f;
            tickTimer = 0f;
            step = skill.CurrentCatchIntervalTicks > 0 ? 1f / skill.CurrentCatchIntervalTicks : 0f;
            nextFill = step;
            awaitingResetTick = false;
            progressRoot.SetActive(true);

            var tool = skill.CurrentTool;
            if (tool != null && toolRenderer != null)
            {
                var item = Resources.Load<ItemData>("Item/" + tool.Id);
                if (item != null && item.icon != null)
                {
                    toolRenderer.sprite = item.icon;
                    toolRoot.SetActive(true);
                }
            }

            var targetRenderer = spot.GetComponent<SpriteRenderer>();
            if (targetRenderer != null)
            {
                if (progressCanvas != null)
                {
                    progressCanvas.sortingLayerID = targetRenderer.sortingLayerID;
                    progressCanvas.sortingOrder = targetRenderer.sortingOrder + 1;
                }
                if (toolRenderer != null)
                {
                    toolRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                    toolRenderer.sortingOrder = targetRenderer.sortingOrder + 2;
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
            if (toolRoot != null)
            {
                toolRoot.SetActive(false);
                if (toolRenderer != null)
                    toolRenderer.sprite = null;
            }
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
        }

        private void Update()
        {
            if (target == null || progressImage == null || skill == null)
                return;

            progressRoot.transform.position = target.position + offset;
            if (toolRoot != null && toolRoot.activeSelf)
                toolRoot.transform.position = target.position + toolOffset;

            tickTimer += Time.deltaTime;
            float t = Mathf.Clamp01(tickTimer / Ticker.TickDuration);
            progressImage.fillAmount = Mathf.Lerp(currentFill, nextFill, t);
        }

        public void OnTick()
        {
            if (target == null || skill == null || !skill.IsFishing)
                return;

            tickTimer = 0f;
            // If the tool cannot catch anything we keep the bar cleared.
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
                skill.OnStartFishing -= HandleStart;
                skill.OnStopFishing -= HandleStop;
            }

            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
        }
    }
}
