using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Inventory;
using Player;
using UI;

namespace Quests
{
    /// <summary>
    /// Simple quest log UI built entirely in code.
    /// </summary>
    public class QuestUI : MonoBehaviour, IUIWindow
    {
        private RectTransform listContent;
        private Text titleText;
        private Text descriptionText;
        private Text stepsText;
        private Text rewardsText;
        private QuestDefinition selected;

        [SerializeField] private Button questEntryPrefab;

        private Canvas canvas;
        private PlayerMover playerMover;

        public bool IsOpen => canvas != null && canvas.enabled;

        private void Awake()
        {
            name = "QuestUI";
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.AddComponent<GraphicRaycaster>();

            BuildLayout();
            DontDestroyOnLoad(gameObject);
            canvas.enabled = false;
            playerMover = FindObjectOfType<PlayerMover>();
            UIManager.Instance.RegisterWindow(this);
        }

        private void Start()
        {
            if (QuestManager.Instance != null)
                QuestManager.Instance.QuestsUpdated.AddListener(Refresh);
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
            var inv = FindObjectOfType<Inventory.Inventory>();
            if (inv != null && inv.IsOpen)
                inv.CloseUI();
            var eq = FindObjectOfType<Inventory.Equipment>();
            if (eq != null && eq.IsOpen)
                eq.CloseUI();
            canvas.enabled = true;
            Refresh();
            if (playerMover == null)
                playerMover = FindObjectOfType<PlayerMover>();
            if (playerMover != null)
                playerMover.enabled = false;
        }

        public void Close()
        {
            canvas.enabled = false;
            Clear();
            if (playerMover != null)
                playerMover.enabled = true;
        }

        private void Update()
        {
            // Removed Q key toggle
        }

        private void BuildLayout()
        {
            var bg = new GameObject("Background", typeof(Image));
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.SetParent(transform, false);
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Hide the full-screen background to avoid a white overlay
            var bgImage = bg.GetComponent<Image>();
            bgImage.color = Color.clear;

            // Left quest list
            var listGO = new GameObject("QuestList", typeof(Image), typeof(ScrollRect));
            var listRect = listGO.GetComponent<RectTransform>();
            listRect.SetParent(bgRect, false);
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(0.35f, 1f);
            listRect.offsetMin = new Vector2(10f, 10f);
            listRect.offsetMax = new Vector2(-10f, -10f);
            listGO.GetComponent<Image>().color = Color.clear;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Mask), typeof(Image));
            viewport.transform.SetParent(listGO.transform, false);
            var vpRect = viewport.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color32(0x26, 0x26, 0x26, 0xF2);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            listContent = content.GetComponent<RectTransform>();
            listContent.SetParent(viewport.transform, false);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.offsetMin = listContent.offsetMax = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childAlignment = TextAnchor.UpperLeft;
            // Add a bit of padding at the top so the first quest is fully visible
            layout.padding = new RectOffset(0, 0, 5, 0);

            var scroll = listGO.GetComponent<ScrollRect>();
            scroll.viewport = vpRect;
            scroll.content = listContent;
            // Prevent horizontal dragging and excessive movement when
            // the list contains few items so the layout remains stable.
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // Right details panel
            var details = new GameObject("Details", typeof(Image));
            var detRect = details.GetComponent<RectTransform>();
            detRect.SetParent(bgRect, false);
            detRect.anchorMin = new Vector2(0.35f, 0f);
            detRect.anchorMax = new Vector2(1f, 1f);
            detRect.offsetMin = new Vector2(10f, 10f);
            detRect.offsetMax = new Vector2(-40f, -10f);
            details.GetComponent<Image>().color = new Color32(0x26, 0x26, 0x26, 0xF2);

            titleText = CreateText("Title", detRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0, -10));
            titleText.fontStyle = FontStyle.Bold;
            descriptionText = CreateText("Description", detRect, new Vector2(0f, 0.7f), new Vector2(1f, 1f), new Vector2(0, -40));
            stepsText = CreateText("Steps", detRect, new Vector2(0f, 0.3f), new Vector2(1f, 0.7f), Vector2.zero);
            rewardsText = CreateText("Rewards", detRect, new Vector2(0f, 0f), new Vector2(1f, 0.3f), new Vector2(0, 10));

            // Close button
            var closeBtnGO = new GameObject("Close", typeof(Image), typeof(Button));
            var closeRect = closeBtnGO.GetComponent<RectTransform>();
            closeRect.SetParent(bgRect, false);
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(30f, 30f);
            closeRect.anchoredPosition = new Vector2(-15f, -15f);
            var closeText = CreateText("X", closeRect, Vector2.zero, Vector2.one, Vector2.zero);
            closeText.alignment = TextAnchor.MiddleCenter;
            closeBtnGO.GetComponent<Image>().color = Color.clear;
            closeBtnGO.GetComponent<Button>().onClick.AddListener(() => canvas.enabled = false);
        }

        private Text CreateText(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offset)
        {
            var go = new GameObject(name, typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = offset;
            var txt = go.GetComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.alignment = TextAnchor.UpperLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.color = Color.white;
            // Allow buttons beneath the text to receive clicks by disabling
            // raycast targeting on the label itself.
            txt.raycastTarget = false;
            return txt;
        }

        private void Refresh()
        {
            ClearList();

            if (questEntryPrefab == null)
            {
                Debug.LogWarning("Quest entry prefab not assigned.");
                return;
            }

            var allQuests = QuestManager.Instance.GetActiveQuests()
                .Concat(QuestManager.Instance.GetAvailableQuests())
                .Concat(QuestManager.Instance.GetCompletedQuests());
            foreach (var quest in allQuests)
            {
                var btn = Instantiate(questEntryPrefab, listContent);
                btn.name = quest.Title;
                var txt = btn.GetComponentInChildren<Text>();
                txt.text = quest.Title;
                txt.alignment = TextAnchor.MiddleLeft;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                if (QuestManager.Instance.IsQuestCompleted(quest.QuestID))
                    txt.color = Color.green;
                else if (QuestManager.Instance.IsQuestActive(quest.QuestID))
                    txt.color = Color.yellow;
                else
                    txt.color = Color.red;
                var capturedQuest = quest;
                btn.onClick.AddListener(() => SelectQuest(capturedQuest));
            }

            if (selected != null)
                SelectQuest(selected);
        }

        private void Clear()
        {
            ClearList();
            selected = null;
            titleText.text = descriptionText.text = stepsText.text = rewardsText.text = string.Empty;
        }

        private void ClearList()
        {
            if (listContent == null) return;
            foreach (Transform child in listContent)
                Destroy(child.gameObject);
        }

        private void SelectQuest(QuestDefinition quest)
        {
            selected = quest;
            if (quest == null)
            {
                titleText.text = descriptionText.text = stepsText.text = rewardsText.text = string.Empty;
                return;
            }
            titleText.text = quest.Title;
            descriptionText.text = quest.Description;
            stepsText.text = string.Join("\n", quest.Steps.Select(s => $"{(s.IsComplete ? "[\u2714]" : "[ ]")} {s.StepDescription}"));
            rewardsText.text = quest.Rewards;
        }
    }
}
