using Books;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class BookUI : MonoBehaviour, IUIWindow
    {
        public static BookUI Instance { get; private set; }

        public Text titleText;
        public Text pageText;
        public Button nextButton;
        public Button prevButton;
        public Button closeButton;

        private BookData currentBook;
        private int currentPage;

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
                foreach (var t in GetComponentsInChildren<Text>(true))
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
                foreach (var t in GetComponentsInChildren<Text>(true))
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
            panelImage.color = new Color(0f, 0f, 0f, 0.5f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(300f, 200f);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var titleGO = new GameObject("TitleText", typeof(Text));
            titleGO.transform.SetParent(panel.transform, false);
            titleText = titleGO.GetComponent<Text>();
            titleText.font = font;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(0f, 30f);
            titleRect.anchoredPosition = Vector2.zero;

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
            var closeTextGO = new GameObject("Text", typeof(Text));
            closeTextGO.transform.SetParent(closeGO.transform, false);
            var closeText = closeTextGO.GetComponent<Text>();
            closeText.font = font;
            closeText.alignment = TextAnchor.MiddleCenter;
            closeText.color = Color.white;
            closeText.text = "X";
            var closeTextRect = closeText.rectTransform;
            closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
            closeTextRect.offsetMin = Vector2.zero;
            closeTextRect.offsetMax = Vector2.zero;

            var pageGO = new GameObject("PageText", typeof(Text));
            pageGO.transform.SetParent(panel.transform, false);
            pageText = pageGO.GetComponent<Text>();
            pageText.font = font;
            pageText.alignment = TextAnchor.UpperLeft;
            pageText.color = Color.white;
            var pageRect = pageText.rectTransform;
            pageRect.anchorMin = new Vector2(0f, 0f);
            pageRect.anchorMax = new Vector2(1f, 1f);
            pageRect.offsetMin = new Vector2(10f, 40f);
            pageRect.offsetMax = new Vector2(-10f, -40f);

            var prevGO = new GameObject("PrevButton", typeof(Image), typeof(Button));
            prevGO.transform.SetParent(panel.transform, false);
            prevButton = prevGO.GetComponent<Button>();
            var prevRect = prevGO.GetComponent<RectTransform>();
            prevRect.anchorMin = new Vector2(0f, 0f);
            prevRect.anchorMax = new Vector2(0f, 0f);
            prevRect.pivot = new Vector2(0f, 0f);
            prevRect.sizeDelta = new Vector2(60f, 25f);
            prevRect.anchoredPosition = new Vector2(10f, 10f);
            var prevImg = prevGO.GetComponent<Image>();
            prevImg.color = Color.gray;
            var prevTextGO = new GameObject("Text", typeof(Text));
            prevTextGO.transform.SetParent(prevGO.transform, false);
            var prevText = prevTextGO.GetComponent<Text>();
            prevText.font = font;
            prevText.alignment = TextAnchor.MiddleCenter;
            prevText.color = Color.white;
            prevText.text = "Prev";
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
            nextRect.anchoredPosition = new Vector2(-10f, 10f);
            var nextImg = nextGO.GetComponent<Image>();
            nextImg.color = Color.gray;
            var nextTextGO = new GameObject("Text", typeof(Text));
            nextTextGO.transform.SetParent(nextGO.transform, false);
            var nextText = nextTextGO.GetComponent<Text>();
            nextText.font = font;
            nextText.alignment = TextAnchor.MiddleCenter;
            nextText.color = Color.white;
            nextText.text = "Next";
            var nextTextRect = nextText.rectTransform;
            nextTextRect.anchorMin = Vector2.zero;
            nextTextRect.anchorMax = Vector2.one;
            nextTextRect.offsetMin = Vector2.zero;
            nextTextRect.offsetMax = Vector2.zero;
        }

        public void Open(BookData data, int startPage)
        {
            if (data == null)
                return;
            currentBook = data;
            currentPage = Mathf.Clamp(startPage, 0, data.pages.Count > 0 ? data.pages.Count - 1 : 0);
            UpdatePage();
            UIManager.Instance.OpenWindow(this);
            gameObject.SetActive(true);
        }

        private void UpdatePage()
        {
            if (currentBook == null)
                return;
            if (titleText != null)
                titleText.text = currentBook.title;
            if (pageText != null)
                pageText.text = currentBook.pages.Count > 0 ? currentBook.pages[currentPage] : string.Empty;
            if (prevButton != null)
                prevButton.interactable = currentPage > 0;
            if (nextButton != null)
                nextButton.interactable = currentPage < currentBook.pages.Count - 1;
        }

        private void NextPage()
        {
            if (currentBook == null)
                return;
            if (currentPage < currentBook.pages.Count - 1)
            {
                currentPage++;
                UpdatePage();
            }
        }

        private void PreviousPage()
        {
            if (currentBook == null)
                return;
            if (currentPage > 0)
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
