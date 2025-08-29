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
            if (nextButton != null) nextButton.onClick.AddListener(NextPage);
            if (prevButton != null) prevButton.onClick.AddListener(PreviousPage);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            gameObject.SetActive(false);
            UIManager.Instance.RegisterWindow(this);
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
