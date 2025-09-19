using System.Collections.Generic;
using UnityEngine;
using Core.Save;
using World;

namespace Books
{
    public class BookProgressManager : MonoBehaviour, ISaveable
    {
        public static BookProgressManager Instance => PersistentSceneSingleton<BookProgressManager>.Instance;

        private Dictionary<string, int> progress = new Dictionary<string, int>();
        private const string SaveKey = "BookProgress";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            PersistentSceneSingleton<BookProgressManager>.Bootstrap(CreateSingleton);
        }

        private static BookProgressManager CreateSingleton()
        {
            var go = new GameObject(nameof(BookProgressManager));
            return go.AddComponent<BookProgressManager>();
        }

        private void Awake()
        {
            if (!PersistentSceneSingleton<BookProgressManager>.HandleAwake(this))
                return;

            SaveManager.Register(this);
        }

        private void OnDestroy()
        {
            if (!PersistentSceneSingleton<BookProgressManager>.HandleOnDestroy(this))
                return;

            SaveManager.Unregister(this);
        }

        public int GetPage(string id)
        {
            return progress.TryGetValue(id, out var page) ? page : 0;
        }

        public void SetPage(string id, int page)
        {
            progress[id] = page;
        }

        [System.Serializable]
        private class Data
        {
            public List<string> ids = new List<string>();
            public List<int> pages = new List<int>();
        }

        public void Save()
        {
            var data = new Data();
            foreach (var kv in progress)
            {
                data.ids.Add(kv.Key);
                data.pages.Add(kv.Value);
            }
            SaveManager.Save(SaveKey, data);
        }

        public void Load()
        {
            var data = SaveManager.Load<Data>(SaveKey);
            progress.Clear();
            if (data?.ids != null && data.pages != null)
            {
                for (int i = 0; i < data.ids.Count && i < data.pages.Count; i++)
                    progress[data.ids[i]] = data.pages[i];
            }
        }
    }
}
