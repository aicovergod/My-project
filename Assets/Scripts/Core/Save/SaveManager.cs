using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Core.Save
{
    /// <summary>
    /// Simple JSON based save manager with versioning and a file backend
    /// stored under a project-root folder for debugging.
    /// Consider Application.persistentDataPath for builds.
    /// </summary>
    public static class SaveManager
    {
        private const int Version = 1;
        // Editor/debug path; consider Application.persistentDataPath for builds.
        private static readonly string FilePath = Path.Combine(Application.dataPath, "../PlayerSave/save_data.json");

        // Registered saveable objects
        private static readonly List<ISaveable> saveables = new List<ISaveable>();

        /// <summary>
        /// Identifier for the profile whose data should be loaded and saved. When empty the
        /// manager behaves like the legacy single-profile implementation.
        /// </summary>
        public static string ActiveProfileId { get; private set; } = string.Empty;

        static SaveManager()
        {
            // Ensure all data is persisted when the application quits
            Application.quitting += SaveAll;
        }

        /// <summary>
        /// Register a saveable object with the manager. The object will immediately
        /// load its state and will be included in future SaveAll calls.
        /// </summary>
        public static void Register(ISaveable saveable)
        {
            if (saveable == null || saveables.Contains(saveable))
                return;
            saveables.Add(saveable);
            saveable.Load();
        }

        /// <summary>
        /// Remove a previously registered saveable object.
        /// </summary>
        public static void Unregister(ISaveable saveable)
        {
            if (saveable == null)
                return;
            saveables.Remove(saveable);
        }

        /// <summary>
        /// Invoke Save on all registered saveable objects.
        /// </summary>
        public static void SaveAll()
        {
            foreach (var s in saveables)
                s.Save();
        }

        /// <summary>
        /// Invoke Load on all registered saveable objects.
        /// </summary>
        public static void LoadAll()
        {
            foreach (var s in saveables)
                s.Load();
        }

        /// <summary>
        /// Normalises and activates a profile for subsequent save and load operations. The
        /// previous profile is saved before switching and the new profile can be reloaded so
        /// gameplay systems pick up their persisted state immediately.
        /// </summary>
        /// <param name="profileId">Raw profile identifier supplied by the caller.</param>
        /// <param name="reload">When true the manager invokes <see cref="LoadAll"/> after
        /// switching so registered systems refresh their state.</param>
        public static void SetActiveProfile(string profileId, bool reload = true)
        {
            string normalized = NormalizeProfileId(profileId);

            if (string.Equals(normalized, ActiveProfileId, StringComparison.Ordinal))
            {
                if (reload)
                    LoadAll();
                return;
            }

            if (!string.IsNullOrEmpty(ActiveProfileId))
                SaveAll();

            ActiveProfileId = normalized;

            if (reload)
                LoadAll();
        }

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

        private static SaveData LoadFile()
        {
            if (cache != null)
                return cache;

            if (File.Exists(FilePath))
            {
                try
                {
                    string json = File.ReadAllText(FilePath);
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

        private static void SaveFile()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string json = JsonUtility.ToJson(cache);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to write save file: {e}");
            }
        }

        public static void Save<T>(string key, T data)
        {
            SaveInternal(ComposeKey(key), data);
        }

        /// <summary>
        /// Persists data without applying a profile prefix. Intended for account-wide metadata
        /// such as credential hashes that must be available before a gameplay profile is active.
        /// </summary>
        public static void SaveGlobal<T>(string key, T data)
        {
            SaveInternal(key, data);
        }

        private static void SaveInternal<T>(string key, T data)
        {
            var all = LoadFile();
            string json = JsonUtility.ToJson(new Wrapper<T> { value = data });
            var entry = all.entries.Find(e => e.key == key);
            if (entry != null)
                entry.value = json;
            else
                all.entries.Add(new Entry { key = key, value = json });

            SaveFile();
        }

        public static T Load<T>(string key)
        {
            return LoadInternal<T>(ComposeKey(key));
        }

        /// <summary>
        /// Loads a value stored without a profile prefix. Use this for global data such as the
        /// account catalogue maintained by <see cref="AccountProfileService"/>.
        /// </summary>
        public static T LoadGlobal<T>(string key)
        {
            return LoadInternal<T>(key);
        }

        private static T LoadInternal<T>(string key)
        {
            var all = LoadFile();
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

        public static void Delete(string key)
        {
            DeleteInternal(ComposeKey(key));
        }

        /// <summary>
        /// Removes a value stored without a profile prefix.
        /// </summary>
        public static void DeleteGlobal(string key)
        {
            DeleteInternal(key);
        }

        private static void DeleteInternal(string key)
        {
            var all = LoadFile();
            all.entries.RemoveAll(e => e.key == key);
            SaveFile();
        }

        private static string ComposeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            return string.IsNullOrEmpty(ActiveProfileId) ? key : string.Concat(ActiveProfileId, ":", key);
        }

        private static string NormalizeProfileId(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return string.Empty;

            string trimmed = profileId.Trim();
            var builder = new StringBuilder(trimmed.Length);
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = char.ToLowerInvariant(trimmed[i]);
                if (char.IsWhiteSpace(c))
                    continue;
                if (c == ':')
                    c = '_';
                builder.Append(c);
            }

            return builder.Length > 0 ? builder.ToString() : string.Empty;
        }
    }
}
