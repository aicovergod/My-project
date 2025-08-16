using UnityEngine;
using UnityEngine.UI;
using Util;

namespace Skills.Mining
{
    /// <summary>
    /// Displays mining progress above the current rock.
    /// </summary>
    public class MiningUI : MonoBehaviour
    {
        private MiningSkill skill;
        private Transform target;
        private Image progressImage;
        private GameObject progressRoot;
        // Offset from the targeted rock's position where the progress bar will appear.
        // Reduced the vertical component to half of its previous value so the bar sits closer to the object.
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float step;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            var go = new GameObject("MiningUI");
            DontDestroyOnLoad(go);
            go.AddComponent<MiningUI>();
        }

        private void Awake()
        {
            skill = FindObjectOfType<MiningSkill>();

            if (skill != null)
            {
                skill.OnStartMining += HandleStart;
                skill.OnStopMining += HandleStop;
            }

            CreateProgressBar();
        }

        private void CreateProgressBar()
        {
            progressRoot = new GameObject("MiningProgress");
            progressRoot.transform.SetParent(transform);

            var canvas = progressRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
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

        private void HandleStart(MineableRock rock)
        {
            target = rock.transform;
            progressImage.fillAmount = 0f;
            currentFill = 0f;
            tickTimer = 0f;
            step = skill.CurrentSwingSpeedTicks > 0 ? 1f / skill.CurrentSwingSpeedTicks : 0f;
            nextFill = step;
            progressRoot.SetActive(true);
            if (Ticker.Instance != null)
                Ticker.Instance.OnTick += HandleTick;
        }

        private void HandleStop()
        {
            target = null;
            progressRoot.SetActive(false);
            if (Ticker.Instance != null)
                Ticker.Instance.OnTick -= HandleTick;
        }

        private void Update()
        {
            if (target == null || progressImage == null || skill == null)
                return;

            progressRoot.transform.position = target.position + offset;

            tickTimer += Time.deltaTime;
            float t = Mathf.Clamp01(tickTimer / Ticker.TickDuration);
            progressImage.fillAmount = Mathf.Lerp(currentFill, nextFill, t);
        }

        private void HandleTick()
        {
            if (target == null || skill == null || !skill.IsMining)
                return;

            tickTimer = 0f;
            currentFill = nextFill;

            if (currentFill >= 1f - step)
            {
                currentFill = 0f;
                nextFill = step;
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
                skill.OnStartMining -= HandleStart;
                skill.OnStopMining -= HandleStop;
            }
        }
    }
}
