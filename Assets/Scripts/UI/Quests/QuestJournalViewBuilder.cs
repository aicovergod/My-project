using System;
using UI;
using UI.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Quests
{
    /// <summary>
    /// Generates the QuestList and QuestInfo panels at runtime so the quest journal can be spawned without manual prefab authoring.
    /// </summary>
    public static class QuestJournalViewBuilder
    {
        /// <summary>
        /// Bundles references to the runtime-generated quest journal UI.
        /// </summary>
        public readonly struct QuestJournalViewReferences
        {
            public QuestJournalViewReferences(
                RectTransform root,
                RectTransform listPanel,
                RectTransform listContent,
                Text listHeader,
                QuestInfoPanelView infoPanel,
                Button closeButton)
            {
                Root = root;
                ListPanel = listPanel;
                ListContent = listContent;
                ListHeader = listHeader;
                InfoPanel = infoPanel;
                CloseButton = closeButton;
            }

            public RectTransform Root { get; }
            public RectTransform ListPanel { get; }
            public RectTransform ListContent { get; }
            public Text ListHeader { get; }
            public QuestInfoPanelView InfoPanel { get; }
            public Button CloseButton { get; }
        }

        /// <summary>
        /// Builds the QuestList and QuestInfo panels under the supplied parent transform.
        /// </summary>
        public static QuestJournalViewReferences Build(Transform parent)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            var frame = new GameObject("QuestJournalFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(parent, false);

            var frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.sizeDelta = new Vector2(720f, 512f);
            frameRect.anchoredPosition = Vector2.zero;

            var frameImage = frame.GetComponent<Image>();
            frameImage.color = new Color32(0x1B, 0x1B, 0x1B, 0xF0);

            var listPanel = CreateListPanel(frameRect);
            var infoPanel = CreateInfoPanel(frameRect);
            var closeButton = CloseButtonBuilder.Build(frameRect, () => { }, new CloseButtonBuilder.Options
            {
                ButtonName = "CloseWindowButton",
                AnchoredPosition = new Vector2(-12f, -12f),
                Size = new Vector2(24f, 24f),
                BackgroundColor = new Color32(0x66, 0x0C, 0x0C, 0xFF),
                TextColor = Color.white
            });

            // Hooked up later by QuestUI so the button closes the window. We just need the reference now.
            closeButton.onClick.RemoveAllListeners();

            var listHeader = listPanel.transform.Find("Header").GetComponent<Text>();
            var listContent = listPanel.transform.Find("ScrollArea/Viewport/Content").GetComponent<RectTransform>();

            return new QuestJournalViewReferences(frameRect, listPanel, listContent, listHeader, infoPanel, closeButton);
        }

        /// <summary>
        /// Creates a quest list entry button beneath the supplied parent.
        /// </summary>
        public static QuestListEntryView CreateListEntry(RectTransform listContent)
        {
            if (listContent == null)
                throw new ArgumentNullException(nameof(listContent));

            var entryGO = new GameObject("QuestEntry", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(QuestListEntryView));
            entryGO.transform.SetParent(listContent, false);

            var rect = entryGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(0f, 48f);

            var image = entryGO.GetComponent<Image>();
            image.color = new Color32(0x30, 0x30, 0x30, 0xF0);

            var layout = entryGO.GetComponent<LayoutElement>();
            layout.minHeight = 48f;
            layout.flexibleHeight = 0f;

            var button = entryGO.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = new Color32(0x30, 0x30, 0x30, 0xF0);
            colors.highlightedColor = new Color32(0x45, 0x45, 0x45, 0xFF);
            colors.pressedColor = new Color32(0x22, 0x22, 0x22, 0xFF);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color32(0x20, 0x20, 0x20, 0xAA);
            button.colors = colors;

            var labelGO = new GameObject("Title", typeof(RectTransform), typeof(Text));
            labelGO.transform.SetParent(entryGO.transform, false);
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(12f, 4f);
            labelRect.offsetMax = new Vector2(-12f, -4f);

            var label = labelGO.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(label);
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.raycastTarget = false;

            var entryView = entryGO.GetComponent<QuestListEntryView>();
            entryView.AssignTitleLabel(label);

            return entryView;
        }

        private static RectTransform CreateListPanel(RectTransform parent)
        {
            var panel = new GameObject("QuestListPanel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0.38f, 1f);
            panel.offsetMin = new Vector2(16f, 16f);
            panel.offsetMax = new Vector2(-8f, -16f);

            var image = panel.GetComponent<Image>();
            image.color = new Color32(0x26, 0x26, 0x26, 0xF0);

            var header = CreateText(
                "Header",
                panel,
                new Vector2(0f, 0.88f),
                new Vector2(1f, 1f),
                new RectOffset(12, 12, 12, 12));
            header.text = "Quest List";
            header.fontStyle = FontStyle.Bold;

            var scrollRoot = new GameObject("ScrollArea", typeof(RectTransform), typeof(ScrollRect));
            scrollRoot.transform.SetParent(panel, false);
            var scrollRectTransform = scrollRoot.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(8f, 8f);
            scrollRectTransform.offsetMax = new Vector2(-8f, -72f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Mask), typeof(Image));
            viewport.transform.SetParent(scrollRoot.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color32(0x18, 0x18, 0x18, 0xF0);
            viewportImage.raycastTarget = true;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 4f;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scrollRect = scrollRoot.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            return panel;
        }

        private static QuestInfoPanelView CreateInfoPanel(RectTransform parent)
        {
            var panelGO = new GameObject("QuestInfoPanel", typeof(RectTransform), typeof(Image), typeof(QuestInfoPanelView));
            panelGO.transform.SetParent(parent, false);

            var rect = panelGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.38f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(8f, 16f);
            rect.offsetMax = new Vector2(-16f, -16f);

            var image = panelGO.GetComponent<Image>();
            image.color = new Color32(0x26, 0x26, 0x26, 0xF0);

            var title = CreateText(
                "Title",
                rect,
                new Vector2(0f, 0.82f),
                new Vector2(1f, 1f),
                new RectOffset(16, 16, 8, 16));
            title.fontStyle = FontStyle.Bold;

            var description = CreateText(
                "Description",
                rect,
                new Vector2(0f, 0.52f),
                new Vector2(1f, 0.82f),
                new RectOffset(16, 16, 12, 12));

            var objectives = CreateText(
                "Objectives",
                rect,
                new Vector2(0f, 0.28f),
                new Vector2(1f, 0.52f),
                new RectOffset(16, 16, 12, 12));

            var rewards = CreateText(
                "Rewards",
                rect,
                new Vector2(0f, 0.08f),
                new Vector2(1f, 0.28f),
                new RectOffset(16, 16, 12, 12));

            var backButton = CreateBackButton(rect);

            var infoPanel = panelGO.GetComponent<QuestInfoPanelView>();
            infoPanel.Configure(title, description, objectives, rewards, backButton);
            infoPanel.ShowPlaceholder();

            return infoPanel;
        }

        private static Text CreateText(
            string name,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            RectOffset margins)
        {
            var textGO = new GameObject(name, typeof(RectTransform), typeof(Text));
            textGO.transform.SetParent(parent, false);

            var rect = textGO.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(margins.left, margins.bottom);
            rect.offsetMax = new Vector2(-margins.right, -margins.top);

            var text = textGO.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(text);
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return text;
        }

        private static Button CreateBackButton(RectTransform parent)
        {
            var buttonGO = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(parent, false);

            var rect = buttonGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(16f, 16f);
            rect.sizeDelta = new Vector2(120f, 36f);

            var image = buttonGO.GetComponent<Image>();
            image.color = new Color32(0x33, 0x33, 0x33, 0xFF);

            var button = buttonGO.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = new Color32(0x44, 0x44, 0x44, 0xFF);
            colors.pressedColor = new Color32(0x22, 0x22, 0x22, 0xFF);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color32(0x20, 0x20, 0x20, 0xAA);
            button.colors = colors;

            var label = CreateText("Label", rect, Vector2.zero, Vector2.one, new RectOffset(8, 8, 6, 6));
            label.text = "Back";
            label.alignment = TextAnchor.MiddleCenter;

            return button;
        }
    }
}
