using Books;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class BookUI : MonoBehaviour, IUIWindow
    {
        public static BookUI Instance { get; private set; }

        public TextMeshProUGUI titleText;
        public TextMeshProUGUI pageText;
        public Button nextButton;
        public Button prevButton;
        public Button closeButton;

        private BookData currentBook;
        private int currentPage;
        private const string BackgroundSpritePath = "Interfaces/BookUI/Parchment";

        public bool IsOpen => gameObject.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (FindObjectOfType<BookUI>() != null)
                return;
            var go = new GameObject("BookUI");
            DontDestroyOnLoad(go);
            go.AddComponent<BookUI>();
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
            if (titleText == null || pageText == null || nextButton == null || prevButton == null || closeButton == null)
                CreateUI();
            // Attempt to auto-bind UI components if they haven't been set in the Inspector
            if (titleText == null)
            {
                foreach (var t in GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (t.name == "TitleText")
                    {
                        titleText = t;
                        break;
                    }
                }
            }
            if (pageText == null)
            {
                foreach (var t in GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (t.name == "PageText")
                    {
                        pageText = t;
                        break;
                    }
                }
            }
            if (nextButton == null)
            {
                foreach (var b in GetComponentsInChildren<Button>(true))
                {
                    if (b.name == "NextButton")
                    {
                        nextButton = b;
                        break;
                    }
                }
            }
            if (prevButton == null)
            {
                foreach (var b in GetComponentsInChildren<Button>(true))
                {
                    if (b.name == "PrevButton")
                    {
                        prevButton = b;
                        break;
                    }
                }
            }
            if (closeButton == null)
            {
                foreach (var b in GetComponentsInChildren<Button>(true))
                {
                    if (b.name == "CloseButton")
                    {
                        closeButton = b;
                        break;
                    }
                }
            }
            if (titleText != null)
                titleText.fontSize = 18;
            if (pageText != null)
                pageText.fontSize = 14;
            if (nextButton != null)
            {
                var nextText = nextButton.GetComponentInChildren<TextMeshProUGUI>();
                if (nextText != null)
                    nextText.fontSize = 18;
            }
            if (prevButton != null)
            {
                var prevText = prevButton.GetComponentInChildren<TextMeshProUGUI>();
                if (prevText != null)
                    prevText.fontSize = 18;
            }
            if (closeButton != null)
            {
                var cText = closeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (cText != null)
                    cText.fontSize = 14;
            }
            if (pageText != null)
                pageText.overflowMode = TextOverflowModes.Page;
            if (nextButton != null) nextButton.onClick.AddListener(NextPage);
            if (prevButton != null) prevButton.onClick.AddListener(PreviousPage);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            gameObject.SetActive(false);
            UIManager.Instance.RegisterWindow(this);
        }

        private void CreateUI()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(Image));
            panel.transform.SetParent(transform, false);
            var panelImage = panel.GetComponent<Image>();
            var parchmentSprite = Resources.Load<Sprite>(BackgroundSpritePath);
            if (parchmentSprite != null)
            {
                panelImage.sprite = parchmentSprite;
                panelImage.color = Color.white;
            }
            else
            {
                panelImage.color = new Color(0f, 0f, 0f, 0.5f);
            }
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(512f, 512f);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(-150f, 0f);

            var font = TMP_Settings.defaultFontAsset;

            var titleGO = new GameObject("TitleText", typeof(TextMeshProUGUI));
            titleGO.transform.SetParent(panel.transform, false);
            titleText = titleGO.GetComponent<TextMeshProUGUI>();
            titleText.font = font;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;
            titleText.fontSize = 18f;
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(0f, 30f);
            titleRect.anchoredPosition = new Vector2(0f, -50f);

            var closeGO = new GameObject("CloseButton", typeof(Image), typeof(Button));
            closeGO.transform.SetParent(panel.transform, false);
            closeButton = closeGO.GetComponent<Button>();
            var closeRect = closeGO.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(20f, 20f);
            closeRect.anchoredPosition = new Vector2(-5f, -5f);
            var closeImg = closeGO.GetComponent<Image>();
            closeImg.color = Color.red;
            var closeTextGO = new GameObject("Text", typeof(TextMeshProUGUI));
            closeTextGO.transform.SetParent(closeGO.transform, false);
            var closeText = closeTextGO.GetComponent<TextMeshProUGUI>();
            closeText.font = font;
            closeText.alignment = TextAlignmentOptions.Center;
            closeText.color = Color.white;
            closeText.text = "X";
            closeText.fontSize = 14f;
            var closeTextRect = closeText.rectTransform;
            closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
            closeTextRect.offsetMin = Vector2.zero;
            closeTextRect.offsetMax = Vector2.zero;

            var pageGO = new GameObject("PageText", typeof(TextMeshProUGUI));
            pageGO.transform.SetParent(panel.transform, false);
            pageText = pageGO.GetComponent<TextMeshProUGUI>();
            pageText.font = font;
            pageText.alignment = TextAlignmentOptions.TopLeft;
            pageText.color = Color.white;
            pageText.fontSize = 14f;
            var pageRect = pageText.rectTransform;
            pageRect.anchorMin = new Vector2(0f, 0f);
            pageRect.anchorMax = new Vector2(1f, 1f);
            pageRect.anchoredPosition3D = Vector3.zero;
            pageRect.offsetMin = new Vector2(90f, 40f);
            pageRect.offsetMax = new Vector2(-70f, -90f);

            var prevGO = new GameObject("PrevButton", typeof(Image), typeof(Button));
            prevGO.transform.SetParent(panel.transform, false);
            prevButton = prevGO.GetComponent<Button>();
            var prevRect = prevGO.GetComponent<RectTransform>();
            prevRect.anchorMin = new Vector2(0f, 0f);
            prevRect.anchorMax = new Vector2(0f, 0f);
            prevRect.pivot = new Vector2(0f, 0f);
            prevRect.sizeDelta = new Vector2(60f, 25f);
            prevRect.anchoredPosition = new Vector2(40f, 40f);
            var prevImg = prevGO.GetComponent<Image>();
            prevImg.color = Color.gray;
            var prevTextGO = new GameObject("Text", typeof(TextMeshProUGUI));
            prevTextGO.transform.SetParent(prevGO.transform, false);
            var prevText = prevTextGO.GetComponent<TextMeshProUGUI>();
            prevText.font = font;
            prevText.alignment = TextAlignmentOptions.Center;
            prevText.color = Color.white;
            prevText.text = "Prev";
            prevText.fontSize = 18f;
            var prevTextRect = prevText.rectTransform;
            prevTextRect.anchorMin = Vector2.zero;
            prevTextRect.anchorMax = Vector2.one;
            prevTextRect.offsetMin = Vector2.zero;
            prevTextRect.offsetMax = Vector2.zero;

            var nextGO = new GameObject("NextButton", typeof(Image), typeof(Button));
            nextGO.transform.SetParent(panel.transform, false);
            nextButton = nextGO.GetComponent<Button>();
            var nextRect = nextGO.GetComponent<RectTransform>();
            nextRect.anchorMin = new Vector2(1f, 0f);
            nextRect.anchorMax = new Vector2(1f, 0f);
            nextRect.pivot = new Vector2(1f, 0f);
            nextRect.sizeDelta = new Vector2(60f, 25f);
            nextRect.anchoredPosition = new Vector2(-40f, 40f);
            var nextImg = nextGO.GetComponent<Image>();
            nextImg.color = Color.gray;
            var nextTextGO = new GameObject("Text", typeof(TextMeshProUGUI));
            nextTextGO.transform.SetParent(nextGO.transform, false);
            var nextText = nextTextGO.GetComponent<TextMeshProUGUI>();
            nextText.font = font;
            nextText.alignment = TextAlignmentOptions.Center;
            nextText.color = Color.white;
            nextText.text = "Next";
            nextText.fontSize = 18f;
            var nextTextRect = nextText.rectTransform;
            nextTextRect.anchorMin = Vector2.zero;
            nextTextRect.anchorMax = Vector2.one;
            nextTextRect.offsetMin = Vector2.zero;
            nextTextRect.offsetMax = Vector2.zero;
        }

        public void Open(BookData data)
        {
            if (data == null)
                return;
            currentBook = data;
            currentPage = 1;
            if (titleText != null)
                titleText.text = currentBook.title;
            if (pageText != null)
            {
                pageText.text = currentBook.content;
                pageText.pageToDisplay = currentPage;
                pageText.ForceMeshUpdate();
            }
            UpdatePage();
            UIManager.Instance.OpenWindow(this);
            gameObject.SetActive(true);
        }

        private void UpdatePage()
        {
            if (currentBook == null || pageText == null)
                return;

            pageText.pageToDisplay = currentPage;
            pageText.ForceMeshUpdate();
            int pageCount = pageText.textInfo.pageCount;

            if (prevButton != null)
                prevButton.interactable = currentPage > 1;
            if (nextButton != null)
                nextButton.interactable = currentPage < pageCount;
        }

        private void NextPage()
        {
            if (pageText == null)
                return;
            pageText.ForceMeshUpdate();
            if (currentPage < pageText.textInfo.pageCount)
            {
                currentPage++;
                UpdatePage();
            }
        }

        private void PreviousPage()
        {
            if (pageText == null)
                return;
            if (currentPage > 1)
            {
                currentPage--;
                UpdatePage();
            }
        }

        public void Close()
        {
            if (currentBook != null)
                BookProgressManager.Instance.SetPage(currentBook.id, currentPage);
            gameObject.SetActive(false);
        }
    }
}
