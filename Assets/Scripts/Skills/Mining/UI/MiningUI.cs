using UnityEngine;
using UnityEngine.UI;

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
        private readonly Vector3 offset = new Vector3(0f, 1.5f, 0f);

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
            var bgRect = bgImage.rectTransform;
            bgRect.sizeDelta = new Vector2(150f, 25f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(bg.transform, false);
            progressImage = fill.AddComponent<Image>();
            progressImage.color = Color.green;
            progressImage.type = Image.Type.Filled;
            progressImage.fillMethod = Image.FillMethod.Horizontal;
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
            progressRoot.SetActive(true);
        }

        private void HandleStop()
        {
            target = null;
            progressRoot.SetActive(false);
        }

        private void Update()
        {
            if (target == null || progressImage == null || skill == null)
                return;

            progressRoot.transform.position = target.position + offset;
            progressImage.fillAmount = skill.SwingProgressNormalized;
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
