using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UI;

namespace Skills
{
    /// <summary>
    /// Displays basic skill information such as mining and woodcutting levels and XP
    /// and can be toggled with the 'O' key.
    /// </summary>
    public class SkillsUI : MonoBehaviour, IUIWindow
    {
        private GameObject uiRoot;
        private Text skillText;
        private SkillManager skillManager;

        public static SkillsUI Instance { get; private set; }

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            if (Instance != null || UnityEngine.Object.FindObjectOfType<SkillsUI>() != null)
                return;

            var go = new GameObject("SkillsUI");
            DontDestroyOnLoad(go);
            go.AddComponent<SkillsUI>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            skillManager = FindObjectOfType<SkillManager>();
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
            UIManager.Instance.RegisterWindow(this);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
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
            panelRect.sizeDelta = new Vector2(200f, 400f);
            panelRect.anchoredPosition = Vector2.zero;

            var textGo = new GameObject("SkillText");
            textGo.transform.SetParent(panel.transform, false);
            skillText = textGo.AddComponent<Text>();
            skillText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            skillText.alignment = TextAnchor.MiddleCenter;
            skillText.color = Color.white;
            var textRect = skillText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            UIManager.Instance.OpenWindow(this);
            if (uiRoot != null)
            {
                var inv = UnityEngine.Object.FindObjectOfType<Inventory.Inventory>();
                if (inv != null && inv.IsOpen)
                    inv.CloseUI();
                var eq = UnityEngine.Object.FindObjectOfType<Inventory.Equipment>();
                if (eq != null && eq.IsOpen)
                    eq.CloseUI();
                uiRoot.SetActive(true);
            }
        }

        private void Update()
        {
            // Removed O key toggle

            if (uiRoot != null && uiRoot.activeSelf && skillManager != null)
            {
                var sb = new StringBuilder();
                var displayOrder = new[]
                {
                    SkillType.Hitpoints,
                    SkillType.Attack,
                    SkillType.Strength,
                    SkillType.Defence,
                    SkillType.Beastmaster,
                    SkillType.Fishing,
                    SkillType.Woodcutting,
                    SkillType.Mining
                };
                foreach (var type in displayOrder)
                {
                    if (sb.Length > 0)
                        sb.Append('\n');
                    sb.Append($"{type} Level: {skillManager.GetLevel(type)}  XP: {skillManager.GetXp(type):F2}");
                }
                skillText.text = sb.ToString();
            }
        }

        public void Close()
        {
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }
    }
}
