using Inventory;
using Skills.Common.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Util;
using World;

namespace Skills.Mining
{
    /// <summary>
    /// Displays mining progress above the current rock.
    /// </summary>
    public class MiningUI : GatheringSkillHudBase<MiningUI, MiningSkill>, ITickable
    {
        private bool sceneLoadedSubscribed;

        private Transform target;
        private Image progressImage;
        private GameObject progressRoot;
        private GameObject pickaxeRoot;
        private SpriteRenderer pickaxeRenderer;
        private Canvas progressCanvas;
        private const string ProgressLayerName = "UI";
        // Offset from the targeted rock's position where the progress bar will appear.
        // Reduced the vertical component to half of its previous value so the bar sits closer to the object.
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);
        private readonly Vector3 pickaxeOffset = Vector3.zero;

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float step;
        // Captures how long the current interpolation segment should last to align the UI with tick cadence.
        private float segmentDuration = Ticker.TickDuration;
        // Tracks whether the bar should be reset after spending one full tick at 100%.
        private bool awaitingResetTick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static MiningUI CreateInstance()
        {
            var go = new GameObject(nameof(MiningUI));
            return go.AddComponent<MiningUI>();
        }

        protected override void OnSingletonAwake()
        {
            EnsureSceneLoadedSubscription();
            EnsureProgressObjects();
            RefreshSkillSubscription();
        }

        private void OnEnable()
        {
            EnsureSceneLoadedSubscription();
            RefreshSkillSubscription();
        }

        private void OnDisable()
        {
            if (sceneLoadedSubscribed)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedSubscribed = false;
            }

            HandleStop();
            DetachFromSkill();
            CancelSkillRefreshRoutine();
        }

        private void EnsureProgressObjects()
        {
            if (progressRoot == null)
                CreateProgressBar();
            if (pickaxeRoot == null)
                CreatePickaxeSprite();
        }

        private void CreateProgressBar()
        {
            if (progressRoot != null)
                return;

            progressRoot = new GameObject("MiningProgress");
            progressRoot.transform.SetParent(transform);
            ApplyProgressLayer(progressRoot);

            progressCanvas = progressRoot.AddComponent<Canvas>();
            progressCanvas.renderMode = RenderMode.WorldSpace;
            progressCanvas.overrideSorting = true;
            progressRoot.AddComponent<CanvasScaler>();
            progressRoot.AddComponent<GraphicRaycaster>();
            progressRoot.transform.localScale = Vector3.one * 0.01f;

            var bg = new GameObject("Background");
            bg.transform.SetParent(progressRoot.transform, false);
            ApplyProgressLayer(bg);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.5f);
            var bgSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            bgImage.sprite = bgSprite;
            var bgRect = bgImage.rectTransform;
            bgRect.sizeDelta = new Vector2(150f, 25f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(bg.transform, false);
            ApplyProgressLayer(fill);
            progressImage = fill.AddComponent<Image>();
            progressImage.color = SkillingProgressColorGradient.Evaluate(0f);
            progressImage.sprite = bgSprite;
            progressImage.type = Image.Type.Filled;
            progressImage.fillMethod = Image.FillMethod.Horizontal;
            SetProgressFill(0f);
            var fillRect = progressImage.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            progressRoot.SetActive(false);
        }

        private void CreatePickaxeSprite()
        {
            if (pickaxeRoot != null)
                return;

            pickaxeRoot = new GameObject("MiningPickaxe");
            pickaxeRoot.transform.SetParent(transform);
            ApplyProgressLayer(pickaxeRoot);
            pickaxeRenderer = pickaxeRoot.AddComponent<SpriteRenderer>();
            pickaxeRenderer.sortingOrder = 100;
            pickaxeRoot.SetActive(false);
        }

        /// <summary>
        ///     Applies the configured UI physics layer to the provided game object so the HUD
        ///     participates in layer-based filtering correctly.
        /// </summary>
        private void ApplyProgressLayer(GameObject target)
        {
            if (target == null)
                return;

            int uiLayer = LayerMask.NameToLayer(ProgressLayerName);
            if (uiLayer < 0)
                return;

            SetLayerRecursively(target.transform, uiLayer);
        }

        /// <summary>
        ///     Recursively applies the given layer index to the provided transform and all children.
        /// </summary>
        private static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
                return;

            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child != null)
                    SetLayerRecursively(child, layer);
            }
        }

        private void HandleStart(MineableRock rock)
        {
            EnsureProgressObjects();
            target = rock.transform;
            SetProgressFill(0f);
            currentFill = 0f;
            tickTimer = 0f;
            step = skill.CurrentSwingSpeedTicks > 0 ? 1f / skill.CurrentSwingSpeedTicks : 0f;
            nextFill = step;
            awaitingResetTick = false;
            segmentDuration = ResolveInitialSegmentDuration();
            progressRoot.SetActive(true);
            var pick = skill.CurrentPickaxe;
            if (pick != null && pickaxeRenderer != null)
            {
                var item = Resources.Load<ItemData>("Item/" + pick.Id);
                if (item != null && item.icon != null)
                {
                    pickaxeRenderer.sprite = item.icon;
                    pickaxeRoot.SetActive(true);
                }
            }

            var targetRenderer = rock.GetComponent<SpriteRenderer>();
            var personalNode = rock.GetComponent<PersonalOreNode>();
            if (personalNode == null)
                personalNode = rock.GetComponentInParent<PersonalOreNode>();

            if (targetRenderer != null && pickaxeRenderer != null)
            {
                pickaxeRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                pickaxeRenderer.sortingOrder = targetRenderer.sortingOrder + 2;
            }

            if (progressCanvas != null)
            {
                int progressLayerId = progressCanvas.sortingLayerID;
                int progressOrder = progressCanvas.sortingOrder;
                int minimumOrder = int.MinValue;
                int overlayOrder = int.MinValue;
                int characterSortingCeiling = PersonalOreNode.ResolveActiveCharacterSortingOrder();

                if (targetRenderer != null)
                {
                    progressLayerId = targetRenderer.sortingLayerID;
                    minimumOrder = targetRenderer.sortingOrder + 1;
                    progressOrder = minimumOrder;
                }

                if (characterSortingCeiling > int.MinValue)
                {
                    if (minimumOrder == int.MinValue)
                        minimumOrder = characterSortingCeiling;
                    else
                        minimumOrder = Mathf.Max(minimumOrder, characterSortingCeiling);
                }

                if (personalNode != null)
                {
                    var overlayCanvas = personalNode.OwnerOnlyCanvas;
                    if (overlayCanvas != null)
                    {
                        int overlayLayerId = personalNode.OwnerOverlaySortingLayerId;
                        if (overlayLayerId != 0)
                        {
                            progressLayerId = overlayLayerId;
                        }
                        else if (!string.IsNullOrEmpty(overlayCanvas.sortingLayerName))
                        {
                            progressCanvas.sortingLayerName = overlayCanvas.sortingLayerName;
                            progressLayerId = progressCanvas.sortingLayerID;
                        }

                        overlayOrder = personalNode.OwnerOverlaySortingOrder;
                    }
                }

                if (overlayOrder > int.MinValue + 1)
                {
                    int maxOrder = overlayOrder - 1;
                    if (minimumOrder != int.MinValue)
                    {
                        if (maxOrder >= minimumOrder)
                            progressOrder = Mathf.Clamp(progressOrder, minimumOrder, maxOrder);
                        else
                            progressOrder = minimumOrder;
                    }
                    else
                    {
                        progressOrder = Mathf.Min(progressOrder, maxOrder);
                    }
                }
                else if (minimumOrder != int.MinValue)
                {
                    progressOrder = Mathf.Max(progressOrder, minimumOrder);
                }

                progressCanvas.sortingLayerID = progressLayerId;
                progressCanvas.sortingOrder = progressOrder;
            }

            if (Ticker.Instance != null)
                Ticker.Instance.Subscribe(this);
        }

        private void HandleStop()
        {
            target = null;
            if (progressRoot != null)
                progressRoot.SetActive(false);
            awaitingResetTick = false;
            segmentDuration = Ticker.TickDuration;
            if (pickaxeRoot != null)
            {
                pickaxeRoot.SetActive(false);
                if (pickaxeRenderer != null)
                    pickaxeRenderer.sprite = null;
            }
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureProgressObjects();
            RefreshSkillSubscription();
        }

        private void Update()
        {
            if (skill == null)
            {
                EnsureSkillRefreshRoutine();
                return;
            }

            if (target == null || progressImage == null)
                return;

            progressRoot.transform.position = target.position + offset;
            if (pickaxeRoot != null && pickaxeRoot.activeSelf)
                pickaxeRoot.transform.position = target.position + pickaxeOffset;

            tickTimer += Time.deltaTime;
            if (segmentDuration <= 0f)
            {
                SetProgressFill(nextFill);
                return;
            }

            float t = Mathf.Clamp01(tickTimer / segmentDuration);
            SetProgressFill(Mathf.Lerp(currentFill, nextFill, t));
        }

        public void OnTick()
        {
            segmentDuration = Ticker.TickDuration;
            if (target == null || skill == null || !skill.IsMining)
                return;

            tickTimer = 0f;
            // No valid swing speed means we cannot animate progress, so stay at zero.
            if (step <= 0f)
            {
                currentFill = 0f;
                nextFill = 0f;
                awaitingResetTick = false;
                SetProgressFill(0f);
                return;
            }

            if (awaitingResetTick)
            {
                awaitingResetTick = false;
                currentFill = 0f;
                nextFill = step;
                SetProgressFill(0f);
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

        private void EnsureSceneLoadedSubscription()
        {
            if (sceneLoadedSubscribed)
                return;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            sceneLoadedSubscribed = true;
        }

        private void SetProgressFill(float normalizedValue)
        {
            if (progressImage == null)
                return;

            float clamped = Mathf.Clamp01(normalizedValue);
            progressImage.fillAmount = clamped;
            progressImage.color = SkillingProgressColorGradient.Evaluate(clamped);
        }

        protected override void OnSingletonDestroyed()
        {
            if (sceneLoadedSubscribed)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedSubscribed = false;
            }

            HandleStop();
            DetachFromSkill();
            CancelSkillRefreshRoutine();

            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);

            if (progressRoot != null)
            {
                Destroy(progressRoot);
                progressRoot = null;
            }

            if (pickaxeRoot != null)
            {
                Destroy(pickaxeRoot);
                pickaxeRoot = null;
            }

            progressImage = null;
            pickaxeRenderer = null;
            progressCanvas = null;
            target = null;
        }

        /// <summary>
        /// Computes the first interpolation window using the ticker's remaining time so partial ticks animate correctly.
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

        /// <summary>
        /// Hooks mining events so the HUD can mirror the player's gathering progress.
        /// </summary>
        /// <param name="located">The mining skill belonging to the active player.</param>
        protected override void OnSkillLocated(MiningSkill located)
        {
            located.OnStartMining += HandleStart;
            located.OnStopMining += HandleStop;
        }

        /// <summary>
        /// Removes event hooks from the mining skill when the HUD is shutting down.
        /// </summary>
        /// <param name="previous">The mining skill that is no longer being tracked.</param>
        protected override void OnSkillDetached(MiningSkill previous)
        {
            previous.OnStartMining -= HandleStart;
            previous.OnStopMining -= HandleStop;
        }
    }
}
