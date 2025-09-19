using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UI;
using World;

namespace Skills
{
    /// <summary>
    /// Displays basic skill information such as mining and woodcutting levels and XP
    /// and can be toggled with the 'O' key.
    /// </summary>
    public class SkillsUI : MonoBehaviour, IUIWindow
    {
        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        private GameObject uiRoot;
        private SkillManager skillManager;

        private readonly Dictionary<SkillType, Text> levelTexts = new();
        private readonly Dictionary<SkillType, Text> xpTexts = new();
        private readonly Dictionary<SkillType, bool> xpVisibility = new();
        private Text totalLevelText;

        private bool sceneGateSubscribed;

        private readonly SkillType[] displayOrder =
        {
            SkillType.Hitpoints,
            SkillType.Attack,
            SkillType.Strength,
            SkillType.Defence,
            SkillType.Magic,
            SkillType.Beastmaster,
            SkillType.Fishing,
            SkillType.Cooking,
            SkillType.Woodcutting,
            SkillType.Mining
        };

        public static SkillsUI Instance { get; private set; }

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !PersistentSceneGate.ShouldSpawnInScene(activeScene))
            {
                BeginWaitingForAllowedScene();
                return;
            }

            CreateOrAdoptInstance();
        }

        private static void CreateOrAdoptInstance()
        {
            if (Instance != null)
                return;

            StopWaitingForAllowedScene();

            var existing = FindExistingInstance();
            if (existing != null)
            {
                Instance = existing;
                if (existing.gameObject.scene.name != "DontDestroyOnLoad")
                    DontDestroyOnLoad(existing.gameObject);
                existing.EnsureSceneGateSubscription();
                existing.RebindSkillManager();
                return;
            }

            var go = new GameObject(nameof(SkillsUI));
            DontDestroyOnLoad(go);
            go.AddComponent<SkillsUI>();
        }

        private static SkillsUI FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<SkillsUI>();
#else
            return UnityEngine.Object.FindObjectOfType<SkillsUI>();
#endif
        }

        private static void BeginWaitingForAllowedScene()
        {
            if (waitingForAllowedScene)
                return;

            waitingForAllowedScene = true;
            PersistentSceneGate.SceneEvaluationChanged += HandleSceneEvaluationForBootstrap;
        }

        private static void StopWaitingForAllowedScene()
        {
            if (!waitingForAllowedScene)
                return;

            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneEvaluationForBootstrap;
            waitingForAllowedScene = false;
        }

        private static void HandleSceneEvaluationForBootstrap(Scene scene, bool allowed)
        {
            if (!allowed)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            CreateOrAdoptInstance();
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

            StopWaitingForAllowedScene();
            EnsureSceneGateSubscription();
            skillManager = FindObjectOfType<SkillManager>();
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
            UIManager.Instance?.RegisterWindow(this);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                UIManager.Instance?.UnregisterWindow(this);

                if (sceneGateSubscribed)
                {
                    PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
                    sceneGateSubscribed = false;
                }

                Instance = null;

                if (!applicationIsQuitting)
                    BeginWaitingForAllowedScene();
            }
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
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

            CreateTotalLevelElement(panel.transform);
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

        private void CreateTotalLevelElement(Transform parent)
        {
            var totalGo = new GameObject("TotalLevel");
            totalGo.transform.SetParent(parent, false);

            totalLevelText = totalGo.AddComponent<Text>();
            totalLevelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            totalLevelText.color = Color.white;
        }

        private void OnSkillClicked(SkillType type)
        {
            if (!xpTexts.ContainsKey(type))
                return;

            bool wasVisible = xpVisibility[type];

            foreach (var kvp in xpTexts)
            {
                xpVisibility[kvp.Key] = false;
                kvp.Value.gameObject.SetActive(false);
            }

            xpVisibility[type] = !wasVisible;
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
                int totalLevel = 0;
                foreach (var type in displayOrder)
                {
                    if (!levelTexts.TryGetValue(type, out var levelText) ||
                        !xpTexts.TryGetValue(type, out var xpText))
                        continue;

                    int level = skillManager.GetLevel(type);
                    levelText.text = $"{type} Level: {level}";
                    xpText.text = $"XP: {skillManager.GetXp(type):F2}";
                    xpText.gameObject.SetActive(xpVisibility[type]);
                    totalLevel += level;
                }

                if (totalLevelText != null)
                    totalLevelText.text = $"Total Level: {totalLevel}";
            }
        }

        public void Close()
        {
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        private void EnsureSceneGateSubscription()
        {
            if (sceneGateSubscribed)
                return;

            PersistentSceneGate.SceneEvaluationChanged += HandleSceneGateEvaluation;
            sceneGateSubscribed = true;
        }

        private void HandleSceneGateEvaluation(Scene scene, bool allowed)
        {
            if (Instance != this)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            if (allowed)
                return;

            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
            sceneGateSubscribed = false;
            Destroy(gameObject);
        }

        private void RebindSkillManager()
        {
            if (skillManager == null)
                skillManager = FindObjectOfType<SkillManager>();
        }
    }
}
