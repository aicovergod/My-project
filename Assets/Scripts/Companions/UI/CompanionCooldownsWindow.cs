using System;
using System.Collections.Generic;
using Skills;
using UI;
using UI.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace Companions.UI
{
    /// <summary>
    /// Managed UI window that lists every active companion skill cooldown alongside its live
    /// countdown. The window can be opened via the F4 admin menu.
    /// </summary>
    public sealed class CompanionCooldownsWindow : SceneGatedManagedUiWindow<CompanionCooldownsWindow>
    {
        /// <summary>Root game object that owns the dynamically generated UI hierarchy.</summary>
        private GameObject uiRoot;

        /// <summary>Parent transform that holds the dynamically spawned row entries.</summary>
        private Transform listContainer;

        /// <summary>Text element that shows empty-state or error messaging.</summary>
        private Text emptyMessageText;

        /// <summary>Footer text updated with the last refresh timestamp.</summary>
        private Text lastUpdatedText;

        /// <summary>Buffer reused when querying the cooldown tracker for active timers.</summary>
        private readonly List<CompanionSkillCooldownTracker.CooldownState> cooldownBuffer = new();

        /// <summary>Pool of instantiated row widgets reused between refreshes.</summary>
        private readonly List<CooldownRow> rowPool = new();

        /// <summary>Strongly typed accessor mirroring the base singleton property.</summary>
        public static new CompanionCooldownsWindow Instance => SceneGatedManagedUiWindow<CompanionCooldownsWindow>.Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static CompanionCooldownsWindow CreateInstance()
        {
            var go = new GameObject(nameof(CompanionCooldownsWindow));
            return go.AddComponent<CompanionCooldownsWindow>();
        }

        /// <inheritdoc />
        protected override void OnSingletonAwake()
        {
            CreateUi();
            if (uiRoot != null)
                SetWindowRoot(uiRoot);
            RegisterWindow();
        }

        /// <inheritdoc />
        protected override void OnSingletonDestroyed()
        {
            UnregisterWindow();
        }

        /// <inheritdoc />
        protected override void OnBeforeOpen()
        {
            InterfaceTabMutexUtility.CloseAllTabWindowsExcept(this);
            RefreshCooldowns();
        }

        private void Update()
        {
            if (IsOpen)
                RefreshCooldowns();
        }

        /// <summary>Creates the runtime UI hierarchy for the cooldown inspector window.</summary>
        private void CreateUi()
        {
            uiRoot = new GameObject("CompanionCooldownsUIRoot");
            uiRoot.transform.SetParent(transform, false);

            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiRoot.AddComponent<CanvasScaler>();
            uiRoot.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(Image));
            panel.transform.SetParent(uiRoot.transform, false);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.75f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(360f, 360f);
            panelRect.anchoredPosition = Vector2.zero;

            CloseButtonBuilder.Build(panel.transform, Close);

            var headerGo = new GameObject("Header", typeof(Text));
            headerGo.transform.SetParent(panel.transform, false);
            var headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 34f);
            headerRect.anchoredPosition = new Vector2(0f, -14f);
            var headerText = headerGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(headerText);
            headerText.alignment = TextAnchor.MiddleCenter;
            headerText.color = Color.white;
            headerText.text = "Companion Cooldowns";

            var subtitleGo = new GameObject("Subtitle", typeof(Text));
            subtitleGo.transform.SetParent(panel.transform, false);
            var subtitleRect = subtitleGo.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0.5f, 1f);
            subtitleRect.sizeDelta = new Vector2(0f, 22f);
            subtitleRect.anchoredPosition = new Vector2(0f, -52f);
            var subtitleText = subtitleGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(subtitleText);
            subtitleText.alignment = TextAnchor.MiddleCenter;
            subtitleText.color = new Color(1f, 0.84f, 0f, 1f);
            subtitleText.text = "Live skill decline countdown timers";

            var listRoot = new GameObject("CooldownList", typeof(RectTransform));
            listRoot.transform.SetParent(panel.transform, false);
            var listRect = listRoot.GetComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.offsetMin = new Vector2(12f, 20f);
            listRect.offsetMax = new Vector2(-12f, -92f);

            var layout = listRoot.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 6f;

            listContainer = listRoot.transform;

            var footerGo = new GameObject("Footer", typeof(Text));
            footerGo.transform.SetParent(panel.transform, false);
            var footerRect = footerGo.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.sizeDelta = new Vector2(0f, 30f);
            footerRect.anchoredPosition = new Vector2(0f, 12f);
            lastUpdatedText = footerGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(lastUpdatedText);
            lastUpdatedText.alignment = TextAnchor.MiddleCenter;
            lastUpdatedText.color = Color.white;
            lastUpdatedText.text = "Updated --:--:--";

            var emptyGo = new GameObject("EmptyMessage", typeof(Text));
            emptyGo.transform.SetParent(panel.transform, false);
            var emptyRect = emptyGo.GetComponent<RectTransform>();
            emptyRect.anchorMin = new Vector2(0.5f, 0.5f);
            emptyRect.anchorMax = new Vector2(0.5f, 0.5f);
            emptyRect.pivot = new Vector2(0.5f, 0.5f);
            emptyRect.anchoredPosition = new Vector2(0f, -12f);
            emptyRect.sizeDelta = new Vector2(320f, 60f);
            emptyMessageText = emptyGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(emptyMessageText);
            emptyMessageText.alignment = TextAnchor.MiddleCenter;
            emptyMessageText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            emptyGo.SetActive(false);
        }

        /// <summary>Refreshes the cooldown list so the UI mirrors the live tracker state.</summary>
        private void RefreshCooldowns()
        {
            var tracker = CompanionManager.CompanionSkillCooldowns;
            if (tracker == null)
            {
                SetRowCount(0);
                ShowEmptyMessage("Companion cooldown tracker is not available.");
                UpdateFooterTimestamp();
                return;
            }

            tracker.GetActiveCooldowns(cooldownBuffer);
            if (cooldownBuffer.Count == 0)
            {
                SetRowCount(0);
                ShowEmptyMessage("No active companion cooldown timers.");
                UpdateFooterTimestamp();
                return;
            }

            HideEmptyMessage();
            SetRowCount(cooldownBuffer.Count);

            for (int i = 0; i < cooldownBuffer.Count; i++)
            {
                var state = cooldownBuffer[i];
                var row = rowPool[i];
                row.Root.SetActive(true);
                row.SkillText.text = SkillNameUtility.GetDisplayName(state.Skill);
                row.RemainingText.text = FormatRemaining(state.Remaining);
            }

            UpdateFooterTimestamp();
        }

        /// <summary>Ensures the footer displays the latest refresh timestamp.</summary>
        private void UpdateFooterTimestamp()
        {
            if (lastUpdatedText == null)
                return;

            DateTime now = DateTime.Now;
            lastUpdatedText.text = $"Updated {now:HH:mm:ss}";
        }

        /// <summary>Ensures the row pool contains the requested number of active entries.</summary>
        private void SetRowCount(int count)
        {
            EnsureRowPool(count);

            for (int i = 0; i < rowPool.Count; i++)
                rowPool[i].Root.SetActive(i < count);
        }

        /// <summary>Expands the row pool so it can display the requested number of entries.</summary>
        private void EnsureRowPool(int requiredCount)
        {
            if (listContainer == null)
                return;

            while (rowPool.Count < requiredCount)
            {
                var row = CreateRow(listContainer);
                row.Root.SetActive(false);
                rowPool.Add(row);
            }
        }

        /// <summary>Creates a single cooldown row entry.</summary>
        private static CooldownRow CreateRow(Transform parent)
        {
            var rowRoot = new GameObject("CooldownRow", typeof(Image));
            rowRoot.transform.SetParent(parent, false);
            var background = rowRoot.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.35f);

            var layout = rowRoot.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 4, 4);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 6f;

            var skillTextGo = new GameObject("Skill", typeof(Text));
            skillTextGo.transform.SetParent(rowRoot.transform, false);
            var skillLayout = skillTextGo.AddComponent<LayoutElement>();
            skillLayout.flexibleWidth = 1f;
            var skillText = skillTextGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(skillText);
            skillText.alignment = TextAnchor.MiddleLeft;
            skillText.color = Color.white;

            var remainingGo = new GameObject("Remaining", typeof(Text));
            remainingGo.transform.SetParent(rowRoot.transform, false);
            var remainingLayout = remainingGo.AddComponent<LayoutElement>();
            remainingLayout.preferredWidth = 120f;
            remainingLayout.flexibleWidth = 0f;
            var remainingText = remainingGo.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(remainingText);
            remainingText.alignment = TextAnchor.MiddleRight;
            remainingText.color = new Color(0.9f, 0.9f, 0.9f, 1f);

            return new CooldownRow(rowRoot, skillText, remainingText);
        }

        /// <summary>Formats a duration so the UI shows a player-friendly countdown string.</summary>
        private static string FormatRemaining(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero)
                return "Ready";

            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt((float)remaining.TotalSeconds));
            var rounded = TimeSpan.FromSeconds(totalSeconds);

            if (rounded.TotalHours >= 1d)
                return $"{(int)rounded.TotalHours}h {rounded.Minutes:D2}m {rounded.Seconds:D2}s";

            if (rounded.TotalMinutes >= 1d)
                return $"{(int)rounded.TotalMinutes}m {rounded.Seconds:D2}s";

            return $"{rounded.Seconds}s";
        }

        private void ShowEmptyMessage(string message)
        {
            if (emptyMessageText == null)
                return;

            emptyMessageText.text = message;
            if (!emptyMessageText.gameObject.activeSelf)
                emptyMessageText.gameObject.SetActive(true);
        }

        private void HideEmptyMessage()
        {
            if (emptyMessageText != null && emptyMessageText.gameObject.activeSelf)
                emptyMessageText.gameObject.SetActive(false);
        }

        private readonly struct CooldownRow
        {
            public CooldownRow(GameObject root, Text skillText, Text remainingText)
            {
                Root = root;
                SkillText = skillText;
                RemainingText = remainingText;
            }

            public GameObject Root { get; }

            public Text SkillText { get; }

            public Text RemainingText { get; }
        }
    }
}
