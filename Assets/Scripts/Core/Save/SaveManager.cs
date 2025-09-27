// Refactor: OSRS-style login with per-account saves; removed Overworld hop; atomic file IO; PBKDF2.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Core.Save
{
    /// <summary>
    /// Simple JSON based save manager that now persists per-account data managed by <see cref="AccountManager"/>.
    /// The manager still coordinates registered saveables but writes through to the active account file.
    /// </summary>
    public static class SaveManager
    {
        public const int SaveDataVersion = 1;

        private static readonly List<ISaveable> saveables = new List<ISaveable>();
        private static readonly string GlobalFilePath = Path.Combine(AccountManager.BaseDirectory, "global_state.json");

        public static string ActiveProfileId { get; private set; } = string.Empty;

        private static AccountSave boundAccount;
        private static AccountSave.AccountData cache;
        private static bool cacheDirty;

        private static GlobalData globalCache;

        static SaveManager()
        {
            Application.quitting += HandleApplicationQuitting;
        }

        private static void HandleApplicationQuitting()
        {
            SaveAll();
        }

        /// <summary>
        /// Register a saveable object with the manager. The object will immediately load its state
        /// and will be included in future SaveAll calls.
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
        /// Invoke Save on all registered saveable objects and flush the active account to disk.
        /// </summary>
        public static void SaveAll()
        {
            foreach (var s in saveables)
                s.Save();

            FlushActiveAccount();
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
        /// Binds the manager to a specific account so Save/Load operations map into that account's data store.
        /// </summary>
        /// <param name="account">Account save that should become active.</param>
        /// <param name="reload">When true, triggers <see cref="LoadAll"/> after switching accounts.</param>
        internal static void BindAccount(AccountSave account, bool reload = true)
        {
            boundAccount = account;
            cache = null;
            cacheDirty = false;
            ActiveProfileId = account != null ? account.usernameSlug : string.Empty;

            if (boundAccount != null)
            {
                if (string.IsNullOrEmpty(boundAccount.usernameSlug))
                    boundAccount.usernameSlug = AccountManager.SanitizeUsername(boundAccount.username);

                if (boundAccount.data == null)
                    boundAccount.data = new AccountSave.AccountData { version = SaveDataVersion, entries = new List<AccountSave.AccountDataEntry>() };
                else
                {
                    if (boundAccount.data.entries == null)
                        boundAccount.data.entries = new List<AccountSave.AccountDataEntry>();
                    if (boundAccount.data.version != SaveDataVersion)
                        boundAccount.data.version = SaveDataVersion;
                }
            }

            if (reload)
                LoadAll();
        }

        /// <summary>
        /// Persists data without applying a profile prefix. Intended for account-wide metadata
        /// such as credential hashes that must be available before a gameplay profile is active.
        /// </summary>
        public static void SaveGlobal<T>(string key, T data)
        {
            var store = LoadGlobalStore();
            string json = JsonUtility.ToJson(new Wrapper<T> { value = data });
            var entry = store.entries.Find(e => e.key == key);
            if (entry != null)
                entry.value = json;
            else
                store.entries.Add(new GlobalEntry { key = key, value = json });

            SaveGlobalStore();
        }

        /// <summary>
        /// Loads a value stored without a profile prefix.
        /// </summary>
        public static T LoadGlobal<T>(string key)
        {
            var store = LoadGlobalStore();
            var entry = store.entries.Find(e => e.key == key);
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

        /// <summary>
        /// Removes a value stored without a profile prefix.
        /// </summary>
        public static void DeleteGlobal(string key)
        {
            var store = LoadGlobalStore();
            if (store.entries.RemoveAll(e => e.key == key) > 0)
                SaveGlobalStore();
        }

        public static void Save<T>(string key, T data)
        {
            SaveInternal(ComposeKey(key), data);
        }

        public static T Load<T>(string key)
        {
            return LoadInternal<T>(ComposeKey(key));
        }

        public static void Delete(string key)
        {
            DeleteInternal(ComposeKey(key));
        }

        /// <summary>
        /// Updates the bound account with the latest scene and position metadata so the login flow
        /// can restore the player to the correct location on the next session.
        /// </summary>
        /// <param name="scene">Scene name that should be persisted for the active account.</param>
        /// <param name="position">World position that should be recorded.</param>
        internal static void UpdateLastKnownLocation(string scene, Vector3 position)
        {
            if (boundAccount == null)
                return;

            string resolvedScene = scene ?? string.Empty;
            bool changed = false;

            if (!string.Equals(boundAccount.savedSceneName, resolvedScene, StringComparison.Ordinal))
            {
                boundAccount.savedSceneName = resolvedScene;
                changed = true;
            }

            if (!Mathf.Approximately(boundAccount.savedX, position.x))
            {
                boundAccount.savedX = position.x;
                changed = true;
            }

            if (!Mathf.Approximately(boundAccount.savedY, position.y))
            {
                boundAccount.savedY = position.y;
                changed = true;
            }

            if (!changed)
                return;

            cacheDirty = true;
            FlushActiveAccount();
        }

        /// <summary>
        /// Normalises and activates a profile for subsequent save and load operations. Provided for
        /// backwards compatibility; prefer <see cref="BindAccount"/> when authenticating users.
        /// </summary>
        public static void SetActiveProfile(string profileId, bool reload = true)
        {
            ActiveProfileId = NormalizeProfileId(profileId);
            cache = null;
            cacheDirty = false;

            if (reload)
                LoadAll();
        }

        private static void SaveInternal<T>(string key, T data)
        {
            var store = EnsureDataStore();
            string json = JsonUtility.ToJson(new Wrapper<T> { value = data });
            var entry = store.entries.Find(e => e.key == key);
            if (entry != null)
                entry.value = json;
            else
                store.entries.Add(new AccountSave.AccountDataEntry { key = key, value = json });

            cacheDirty = true;
            FlushActiveAccount();
        }

        private static T LoadInternal<T>(string key)
        {
            var store = EnsureDataStore();
            var entry = store.entries.Find(e => e.key == key);
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

        private static void DeleteInternal(string key)
        {
            var store = EnsureDataStore();
            if (store.entries.RemoveAll(e => e.key == key) > 0)
            {
                cacheDirty = true;
                FlushActiveAccount();
            }
        }

        private static AccountSave.AccountData EnsureDataStore()
        {
            if (cache != null)
                return cache;

            if (boundAccount != null)
            {
                cache = boundAccount.data ?? new AccountSave.AccountData();
                if (cache.entries == null)
                    cache.entries = new List<AccountSave.AccountDataEntry>();
                if (cache.version != SaveDataVersion)
                    cache.version = SaveDataVersion;
                boundAccount.data = cache;
            }
            else
            {
                cache = new AccountSave.AccountData
                {
                    version = SaveDataVersion,
                    entries = new List<AccountSave.AccountDataEntry>(),
                };
            }

            return cache;
        }

        private static void FlushActiveAccount()
        {
            if (!cacheDirty || boundAccount == null)
                return;

            try
            {
                AccountManager.SaveAsync(boundAccount).GetAwaiter().GetResult();
                cacheDirty = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"SaveManager: Failed to persist account '{boundAccount.usernameSlug}': {ex}");
            }
        }

        private static string ComposeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            return string.IsNullOrEmpty(ActiveProfileId) ? key : string.Concat(ActiveProfileId, ":", key);
        }

        private static string NormalizeProfileId(string profileId)
        {
            return AccountManager.SanitizeUsername(profileId);
        }

        [Serializable]
        private sealed class Wrapper<T>
        {
            public T value;
        }

        [Serializable]
        private sealed class GlobalEntry
        {
            public string key;
            public string value;
        }

        [Serializable]
        private sealed class GlobalData
        {
            public List<GlobalEntry> entries = new List<GlobalEntry>();
        }

        private static GlobalData LoadGlobalStore()
        {
            if (globalCache != null)
                return globalCache;

            try
            {
                Directory.CreateDirectory(AccountManager.BaseDirectory);
                if (File.Exists(GlobalFilePath))
                {
                    string json = File.ReadAllText(GlobalFilePath, Encoding.UTF8);
                    globalCache = JsonUtility.FromJson<GlobalData>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"SaveManager: Failed to read global save file: {ex}");
            }

            if (globalCache == null || globalCache.entries == null)
                globalCache = new GlobalData { entries = new List<GlobalEntry>() };

            return globalCache;
        }

        private static void SaveGlobalStore()
        {
            if (globalCache == null)
                return;

            string tempPath = GlobalFilePath + ".tmp";

            try
            {
                Directory.CreateDirectory(AccountManager.BaseDirectory);
                string json = JsonUtility.ToJson(globalCache);

                using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(tempStream, Encoding.UTF8))
                {
                    writer.Write(json);
                    writer.Flush();
                    tempStream.Flush(true);
                }

                try
                {
                    using (var destinationLock = new FileStream(GlobalFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        destinationLock.Flush(true);
                    }
                }
                catch (Exception lockEx)
                {
                    throw new IOException($"SaveManager: Unable to access global save file '{GlobalFilePath}' for writing.", lockEx);
                }

                try
                {
                    if (File.Exists(GlobalFilePath))
                    {
                        File.Delete(GlobalFilePath);
                    }

                    File.Move(tempPath, GlobalFilePath);

                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception swapEx)
                {
                    throw new IOException($"SaveManager: Unable to swap temp global save '{tempPath}' into '{GlobalFilePath}'.", swapEx);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"SaveManager: Failed to write global save file: {ex}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Ignore temp cleanup failure.
                }
            }
        }
    }
}
