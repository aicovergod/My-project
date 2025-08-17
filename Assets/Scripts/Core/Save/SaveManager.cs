using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Core.Save
{
    /// <summary>
    /// Simple JSON based save manager with versioning and a file backend
    /// stored under Application.persistentDataPath.
    /// </summary>
    public static class SaveManager
    {
        private const int Version = 1;
        private static readonly string FilePath = Path.Combine(Application.persistentDataPath, "save_data.json");

        [Serializable]
        private class Entry
        {
            public string key;
            public string value;
        }

        [Serializable]
        private class SaveData
        {
            public int version;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        private class Wrapper<T>
        {
            public T value;
        }

        private static SaveData cache;

        private static async Task<SaveData> LoadAll()
        {
            if (cache != null)
                return cache;

            if (File.Exists(FilePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(FilePath);
                    cache = JsonUtility.FromJson<SaveData>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to read save file: {e}");
                }
            }

            if (cache == null || cache.version != Version || cache.entries == null)
            {
                cache = new SaveData { version = Version, entries = new List<Entry>() };
            }

            return cache;
        }

        private static async Task SaveAll()
        {
            try
            {
                string json = JsonUtility.ToJson(cache);
                await File.WriteAllTextAsync(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to write save file: {e}");
            }
        }

        public static async Task SaveAsync<T>(string key, T data)
        {
            var all = await LoadAll();
            string json = JsonUtility.ToJson(new Wrapper<T> { value = data });
            var entry = all.entries.Find(e => e.key == key);
            if (entry != null)
                entry.value = json;
            else
                all.entries.Add(new Entry { key = key, value = json });

            await SaveAll();
        }

        public static void Save<T>(string key, T data)
        {
            SaveAsync<T>(key, data).GetAwaiter().GetResult();
        }

        public static async Task<T> LoadAsync<T>(string key)
        {
            var all = await LoadAll();
            var entry = all.entries.Find(e => e.key == key);
            if (entry == null || string.IsNullOrEmpty(entry.value))
                return default;

            try
            {
                var wrapper = JsonUtility.FromJson<Wrapper<T>>(entry.value);
                return wrapper != null ? wrapper.value : default;
            }
            catch
            {
                return default;
            }
        }

        public static T Load<T>(string key)
        {
            return LoadAsync<T>(key).GetAwaiter().GetResult();
        }

        public static void Delete(string key)
        {
            var all = LoadAll().GetAwaiter().GetResult();
            all.entries.RemoveAll(e => e.key == key);
            SaveAll().GetAwaiter().GetResult();
        }
    }
}
