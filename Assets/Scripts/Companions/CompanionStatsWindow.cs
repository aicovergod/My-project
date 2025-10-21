using System.Collections.Generic;
using Skills;
using UI;
using UI.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace Companions
{
    /// <summary>
    /// Companion-specific variant of the skills window that mirrors the player's stats UI while reading
    /// data from the companion's skill manager.
    /// </summary>
    public sealed class CompanionStatsWindow : SceneGatedManagedUiWindow<CompanionStatsWindow>
    {
        /// <summary>Root GameObject owning the dynamically generated UI.</summary>
        private GameObject uiRoot;

        /// <summary>Skill manager currently providing stat data.</summary>
        private SkillManager boundSkills;

        /// <summary>Lookup mapping each skill to its level text field.</summary>
        private readonly Dictionary<SkillType, Text> levelTexts = new();

        /// <summary>Lookup mapping each skill to its XP text field.</summary>
        private readonly Dictionary<SkillType, Text> xpTexts = new();

        /// <summary>Tracks whether XP text is visible per skill.</summary>
        private readonly Dictionary<SkillType, bool> xpVisibility = new();

        /// <summary>Displays the combined total level across tracked skills.</summary>
        private Text totalLevelText;

        /// <summary>Displays the computed combat level from the companion manager.</summary>
        private Text combatLevelText;

        /// <summary>Skill ordering used to mirror the player stats window layout.</summary>
        private readonly SkillType[] displayOrder =
        {
            SkillType.Hitpoints,
            SkillType.Attack,
            SkillType.Strength,
            SkillType.Ranged,
            SkillType.Defence,
            SkillType.Magic,
            SkillType.Beastmaster,
            SkillType.Fishing,
            SkillType.Cooking,
            SkillType.Firemaking,
            SkillType.Woodcutting,
            SkillType.Mining
        };

        /// <summary>Exposes the singleton instance so the manager can bind skills before opening.</summary>
        public static new CompanionStatsWindow Instance => SceneGatedManagedUiWindow<CompanionStatsWindow>.Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static CompanionStatsWindow CreateInstance()
        {
            var go = new GameObject(nameof(CompanionStatsWindow));
            return go.AddComponent<CompanionStatsWindow>();
        }

        /// <summary>Binds the companion skill manager so refresh calls read from the latest data.</summary>
        public void BindSkills(SkillManager skills)
        {
            boundSkills = skills;
            ForceNextOpenHooks();
        }

        protected override void OnSingletonAwake()
        {
            CreateUi();
            if (uiRoot != null)
                SetWindowRoot(uiRoot);
            RegisterWindow();
        }

        protected override void OnSingletonDestroyed()
        {
            UnregisterWindow();
        }

        protected override void OnBeforeOpen()
        {
            InterfaceTabMutexUtility.CloseAllTabWindowsExcept(this);
            RefreshStats();
        }

        private void Update()
        {
            if (IsOpen)
                RefreshStats();
        }

        private void CreateUi()
        {
            uiRoot = new GameObject("CompanionStatsUIRoot");
            uiRoot.transform.SetParent(transform, false);

            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiRoot.AddComponent<CanvasScaler>();
            uiRoot.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(uiRoot.transform, false);
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.7f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(260f, 520f);
            panelRect.anchoredPosition = Vector2.zero;

            CloseButtonBuilder.Build(panel.transform, Close);

            var headerGo = new GameObject("Header", typeof(Text));
            headerGo.transform.SetParent(panel.transform, false);
            var headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 32f);
            headerRect.anchoredPosition = new Vector2(0f, -12f);
            var headerText = headerGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(headerText);
            headerText.alignment = TextAnchor.MiddleCenter;
            headerText.color = Color.white;
            headerText.text = "Companion Stats";

            var combatGo = new GameObject("CombatLevel", typeof(Text));
            combatGo.transform.SetParent(panel.transform, false);
            var combatRect = combatGo.GetComponent<RectTransform>();
            combatRect.anchorMin = new Vector2(0f, 1f);
            combatRect.anchorMax = new Vector2(1f, 1f);
            combatRect.pivot = new Vector2(0.5f, 1f);
            combatRect.sizeDelta = new Vector2(0f, 28f);
            combatRect.anchoredPosition = new Vector2(0f, -44f);
            combatLevelText = combatGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(combatLevelText);
            combatLevelText.alignment = TextAnchor.MiddleCenter;
            combatLevelText.color = new Color(1f, 0.84f, 0f, 1f);

            var layoutRoot = new GameObject("SkillList", typeof(RectTransform));
            layoutRoot.transform.SetParent(panel.transform, false);
            var layoutRect = layoutRoot.GetComponent<RectTransform>();
            layoutRect.anchorMin = new Vector2(0f, 0f);
            layoutRect.anchorMax = new Vector2(1f, 1f);
            layoutRect.offsetMin = new Vector2(8f, 40f);
            layoutRect.offsetMax = new Vector2(-8f, -80f);

            var layout = layoutRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 6f;

            foreach (var type in displayOrder)
                CreateSkillElement(type, layoutRoot.transform);

            var totalGo = new GameObject("TotalLevel", typeof(Text));
            totalGo.transform.SetParent(panel.transform, false);
            var totalRect = totalGo.GetComponent<RectTransform>();
            totalRect.anchorMin = new Vector2(0f, 0f);
            totalRect.anchorMax = new Vector2(1f, 0f);
            totalRect.pivot = new Vector2(0.5f, 0f);
            totalRect.sizeDelta = new Vector2(0f, 32f);
            totalRect.anchoredPosition = new Vector2(0f, 12f);
            totalLevelText = totalGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(totalLevelText);
            totalLevelText.alignment = TextAnchor.MiddleCenter;
            totalLevelText.color = Color.white;
        }

        private void CreateSkillElement(SkillType type, Transform parent)
        {
            var skillRoot = new GameObject(type + "Row");
            skillRoot.transform.SetParent(parent, false);

            var background = skillRoot.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.35f);

            var layout = skillRoot.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var levelGo = new GameObject("Level", typeof(Text));
            levelGo.transform.SetParent(skillRoot.transform, false);
            var levelText = levelGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(levelText);
            levelText.color = Color.white;
            levelText.alignment = TextAnchor.MiddleCenter;

            var xpGo = new GameObject("Xp", typeof(Text));
            xpGo.transform.SetParent(skillRoot.transform, false);
            var xpText = xpGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(xpText);
            xpText.color = Color.white;
            xpText.alignment = TextAnchor.MiddleCenter;
            xpText.gameObject.SetActive(false);

            var button = skillRoot.AddComponent<Button>();
            button.onClick.AddListener(() => ToggleXpVisibility(type));

            levelTexts[type] = levelText;
            xpTexts[type] = xpText;
            xpVisibility[type] = false;
        }

        private void ToggleXpVisibility(SkillType type)
        {
            bool wasVisible = xpVisibility.TryGetValue(type, out bool visible) && visible;
            foreach (var key in new List<SkillType>(xpVisibility.Keys))
            {
                xpVisibility[key] = false;
                if (xpTexts.TryGetValue(key, out var text))
                    text.gameObject.SetActive(false);
            }

            bool newState = !wasVisible;
            xpVisibility[type] = newState;
            if (xpTexts.TryGetValue(type, out var xpText))
                xpText.gameObject.SetActive(newState);
        }

        private void RefreshStats()
        {
            if (boundSkills == null)
                boundSkills = CompanionManager.CompanionSkills;

            if (combatLevelText != null)
                combatLevelText.text = $"Combat lvl {CompanionManager.CombatLevel}";

            if (boundSkills == null)
                return;

            int totalLevel = 0;
            foreach (var type in displayOrder)
            {
                if (!levelTexts.TryGetValue(type, out var levelText) ||
                    !xpTexts.TryGetValue(type, out var xpText))
                    continue;

                int level = boundSkills.GetLevel(type);
                float xp = boundSkills.GetXp(type);
                levelText.text = $"{type}: {level}";
                xpText.text = $"XP: {xp:F2}";
                xpText.gameObject.SetActive(xpVisibility[type]);
                totalLevel += level;
            }

            if (totalLevelText != null)
                totalLevelText.text = $"Total level: {totalLevel}";
        }
    }
}
