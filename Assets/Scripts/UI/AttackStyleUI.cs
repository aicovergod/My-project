using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Combat;
using Player;
using World;

namespace UI
{
    /// <summary>
    /// Simple interface for selecting the player's combat style.
    /// </summary>
    public class AttackStyleUI : MonoBehaviour, IUIWindow
    {
        public static AttackStyleUI Instance => PersistentSceneSingleton<AttackStyleUI>.Instance;

        private GameObject uiRoot;
        private PlayerCombatLoadout loadout;
        private Transform buttonContainer;
        private readonly Dictionary<CombatStyle, Button> styleButtons = new();

        private static readonly CombatStyle[] DefaultMeleeStyles =
        {
            CombatStyle.Accurate,
            CombatStyle.Aggressive,
            CombatStyle.Defensive,
            CombatStyle.Controlled
        };

        private static readonly Dictionary<CombatStyle, string> StyleSpriteMap = new()
        {
            { CombatStyle.Accurate, "Accurate" },
            { CombatStyle.Aggressive, "Aggressive" },
            { CombatStyle.Defensive, "Defensive" },
            { CombatStyle.Controlled, "Controlled" },
            { CombatStyle.Rapid, "Rapid" },
            { CombatStyle.Longrange, "Longrange" }
        };

        /// <summary>Whether the interface is currently visible.</summary>
        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            PersistentSceneSingleton<AttackStyleUI>.Bootstrap(CreateSingleton);
        }

        private static AttackStyleUI CreateSingleton()
        {
            var go = new GameObject(nameof(AttackStyleUI));
            go.AddComponent<ScenePersistentObject>();
            return go.AddComponent<AttackStyleUI>();
        }

        private void Awake()
        {
            if (!PersistentSceneSingleton<AttackStyleUI>.HandleAwake(this))
                return;

            loadout = FindObjectOfType<PlayerCombatLoadout>();
            CreateUI();
            EnsureLoadoutBound();
            if (uiRoot != null)
            {
                uiRoot.SetActive(false);
                RefreshStyleButtons();
                UpdateSelection();
            }
            if (UIManager.Instance != null)
                UIManager.Instance.RegisterWindow(this);
        }

        private void OnDestroy()
        {
            if (!PersistentSceneSingleton<AttackStyleUI>.HandleOnDestroy(this))
                return;

            if (loadout != null)
            {
                loadout.StyleChanged -= HandleLoadoutStyleChanged;
                loadout.DamageTypeChanged -= HandleLoadoutDamageTypeChanged;
            }
            if (UIManager.Instance != null)
                UIManager.Instance.UnregisterWindow(this);
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("AttackStyleUIRoot");
            uiRoot.transform.SetParent(transform, false);

            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiRoot.AddComponent<CanvasScaler>();
            uiRoot.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(uiRoot.transform, false);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.5f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(170f, 220f);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(295f, -75f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = -25f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            buttonContainer = panel.transform;
        }

        private Button CreateStyleButton(Transform parent, CombatStyle style)
        {
            if (!StyleSpriteMap.TryGetValue(style, out string spriteName))
                return null;

            var sprite = Resources.Load<Sprite>("Interfaces/AttackStyle/" + spriteName);
            if (sprite == null)
                Debug.LogWarning($"AttackStyleUI: Missing sprite for style {style} ({spriteName}).");
            var go = new GameObject(spriteName, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(30f, 30f);
            rect.localScale = new Vector3(2f, 1f, 1f);
            var btn = go.GetComponent<Button>();
            return btn;
        }

        /// <summary>Rebuild the style buttons using the current loadout configuration.</summary>
        private void RefreshStyleButtons()
        {
            if (buttonContainer == null)
                return;

            EnsureLoadoutBound();

            var toDestroy = new List<GameObject>();
            foreach (Transform child in buttonContainer)
                toDestroy.Add(child.gameObject);
            foreach (var child in toDestroy)
                Destroy(child);

            styleButtons.Clear();

            foreach (var style in GetAvailableStyles())
            {
                var button = CreateStyleButton(buttonContainer, style);
                if (button == null)
                    continue;
                var capturedStyle = style;
                button.onClick.AddListener(() => SetStyle(capturedStyle));
                styleButtons[capturedStyle] = button;
            }

            if (buttonContainer is RectTransform rectTransform)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        /// <summary>Retrieve the attack styles that should be exposed in the UI.</summary>
        private IReadOnlyList<CombatStyle> GetAvailableStyles()
        {
            return loadout != null ? loadout.GetAvailableStyles() : DefaultMeleeStyles;
        }

        /// <summary>Find and subscribe to the player's combat loadout component if needed.</summary>
        private bool EnsureLoadoutBound()
        {
            if (loadout != null)
                return false;
            loadout = FindObjectOfType<PlayerCombatLoadout>();
            if (loadout == null)
                return false;
            loadout.StyleChanged += HandleLoadoutStyleChanged;
            loadout.DamageTypeChanged += HandleLoadoutDamageTypeChanged;
            return true;
        }

        private void HandleLoadoutStyleChanged(CombatStyle _)
        {
            UpdateSelection();
        }

        private void HandleLoadoutDamageTypeChanged(DamageType _)
        {
            RefreshStyleButtons();
            UpdateSelection();
        }

        /// <summary>Toggle the visibility of the UI.</summary>
        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        /// <summary>Open the attack style interface.</summary>
        public void Open()
        {
            var uiManager = UIManager.Instance;
            if (uiManager == null)
                return;

            if (!uiManager.TryOpenWindow(this))
                return;

            if (uiRoot != null)
            {
                uiRoot.SetActive(true);
                RefreshStyleButtons();
                UpdateSelection();
            }
        }

        /// <summary>Close the attack style interface.</summary>
        public void Close()
        {
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        private void SetStyle(CombatStyle style)
        {
            EnsureLoadoutBound();
            if (loadout == null || !loadout.IsStyleAvailable(style))
                return;
            loadout.Style = style;
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            bool boundNow = EnsureLoadoutBound();
            if (boundNow)
                RefreshStyleButtons();
            if (loadout == null)
                return;
            foreach (var pair in styleButtons)
                Highlight(pair.Value, loadout.Style == pair.Key);
        }

        private void Highlight(Button btn, bool selected)
        {
            if (btn == null)
                return;
            var colors = btn.colors;
            var color = selected ? Color.green : Color.white;
            colors.normalColor = color;
            colors.highlightedColor = color;
            colors.selectedColor = color;
            colors.pressedColor = color;
            btn.colors = colors;
        }
    }
}

