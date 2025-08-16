using UnityEngine;
using UnityEngine.UI;

namespace Skills
{
    /// <summary>
    /// Displays basic skill information. Currently shows the mining level and XP
    /// and can be toggled with the 'O' key.
    /// </summary>
    public class SkillsUI : MonoBehaviour
    {
        private GameObject uiRoot;
        private Text miningText;
        private Mining.MiningSkill miningSkill;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            var go = new GameObject("SkillsUI");
            DontDestroyOnLoad(go);
            go.AddComponent<SkillsUI>();
        }

        private void Awake()
        {
            miningSkill = FindObjectOfType<Mining.MiningSkill>();
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("SkillsUIRoot");
            uiRoot.transform.SetParent(transform, false);

            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiRoot.AddComponent<CanvasScaler>();
            uiRoot.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(uiRoot.transform, false);
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.5f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(200f, 60f);
            panelRect.anchoredPosition = Vector2.zero;

            var textGo = new GameObject("MiningText");
            textGo.transform.SetParent(panel.transform, false);
            miningText = textGo.AddComponent<Text>();
            miningText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            miningText.alignment = TextAnchor.MiddleCenter;
            miningText.color = Color.white;
            var textRect = miningText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.O))
            {
                if (uiRoot != null)
                    uiRoot.SetActive(!uiRoot.activeSelf);
            }

            if (uiRoot != null && uiRoot.activeSelf && miningSkill != null)
            {
                miningText.text = $"Mining Level: {miningSkill.Level}  XP: {miningSkill.Xp}";
            }
        }
    }
}
