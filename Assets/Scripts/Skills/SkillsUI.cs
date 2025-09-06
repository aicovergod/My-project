using System;
using System.Collections.Generic;
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
        private SkillManager skillManager;

        private readonly Dictionary<SkillType, Text> levelTexts = new();
        private readonly Dictionary<SkillType, Text> xpTexts = new();
        private readonly Dictionary<SkillType, bool> xpVisibility = new();

        private readonly SkillType[] displayOrder =
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

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            foreach (var type in displayOrder)
                CreateSkillElement(type, panel.transform);
        }

        private void CreateSkillElement(SkillType type, Transform parent)
        {
            var skillGo = new GameObject($"{type}Skill");
            skillGo.transform.SetParent(parent, false);

            var image = skillGo.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.25f);

            var button = skillGo.AddComponent<Button>();
            button.onClick.AddListener(() => OnSkillClicked(type));

            var layout = skillGo.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;

            var levelGo = new GameObject("LevelText");
            levelGo.transform.SetParent(skillGo.transform, false);
            var levelText = levelGo.AddComponent<Text>();
            levelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            levelText.color = Color.white;

            var xpGo = new GameObject("XpText");
            xpGo.transform.SetParent(skillGo.transform, false);
            var xpText = xpGo.AddComponent<Text>();
            xpText.font = levelText.font;
            xpText.color = Color.white;
            xpText.gameObject.SetActive(false);

            levelTexts[type] = levelText;
            xpTexts[type] = xpText;
            xpVisibility[type] = false;
        }

        private void OnSkillClicked(SkillType type)
        {
            if (!xpTexts.ContainsKey(type))
                return;

            xpVisibility[type] = !xpVisibility[type];
            xpTexts[type].gameObject.SetActive(xpVisibility[type]);
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
            if (uiRoot != null && uiRoot.activeSelf && skillManager != null)
            {
                foreach (var type in displayOrder)
                {
                    if (!levelTexts.TryGetValue(type, out var levelText) ||
                        !xpTexts.TryGetValue(type, out var xpText))
                        continue;

                    levelText.text = $"{type} Level: {skillManager.GetLevel(type)}";
                    xpText.text = $"XP: {skillManager.GetXp(type):F2}";
                    xpText.gameObject.SetActive(xpVisibility[type]);
                }
            }
        }

        public void Close()
        {
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }
    }
}
