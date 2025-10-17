// Refactor: OSRS-style login with per-account saves; removed Overworld hop; atomic file IO; PBKDF2.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        /// <summary>
        /// Exposes the username of the currently bound account or an empty string when unauthenticated.
        /// </summary>
        public static string ActiveAccountUsername
        {
            get
            {
                var account = boundAccount;
                if (account == null)
                    return string.Empty;

                string username = account.username;
                return string.IsNullOrWhiteSpace(username) ? string.Empty : username.Trim();
            }
        }

        private static AccountSave boundAccount;
        private static AccountSave.AccountData cache;
        private static bool cacheDirty;

        private static GlobalData globalCache;

        private static readonly object flushLock = new object();
        private static bool flushLoopRunning;
        private static Task flushTask;
        private static CancellationTokenSource flushCancellation;
        private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

        static SaveManager()
        {
            Application.quitting += HandleApplicationQuitting;
        }

        private static void HandleApplicationQuitting()
        {
            SaveAll();
            WaitForPendingWrites();
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
            WaitForPendingWrites();

            lock (flushLock)
            {
                if (flushCancellation != null)
                {
                    flushCancellation.Dispose();
                    flushCancellation = null;
                }

                flushTask = null;
                flushLoopRunning = false;
                cache = null;
                cacheDirty = false;
                boundAccount = account;
                ActiveProfileId = account != null ? account.usernameSlug : string.Empty;
            }

            var activeAccount = boundAccount;

            if (activeAccount != null)
            {
                if (string.IsNullOrEmpty(activeAccount.usernameSlug))
                    activeAccount.usernameSlug = AccountManager.SanitizeUsername(activeAccount.username);

                if (activeAccount.data == null)
                    activeAccount.data = new AccountSave.AccountData { version = SaveDataVersion, entries = new List<AccountSave.AccountDataEntry>() };
                else
                {
                    if (activeAccount.data.entries == null)
                        activeAccount.data.entries = new List<AccountSave.AccountDataEntry>();
                    if (activeAccount.data.version != SaveDataVersion)
                        activeAccount.data.version = SaveDataVersion;
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
            lock (flushLock)
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

                if (!flushLoopRunning)
                    StartFlushLoopLocked();
            }
        }

        /// <summary>
        /// Normalises and activates a profile for subsequent save and load operations. Provided for
        /// backwards compatibility; prefer <see cref="BindAccount"/> when authenticating users.
        /// </summary>
        public static void SetActiveProfile(string profileId, bool reload = true)
        {
            WaitForPendingWrites();

            lock (flushLock)
            {
                ActiveProfileId = NormalizeProfileId(profileId);
                cache = null;
                cacheDirty = false;
                flushLoopRunning = false;

                if (flushCancellation != null)
                {
                    flushCancellation.Dispose();
                    flushCancellation = null;
                }

                flushTask = null;
            }

            if (reload)
                LoadAll();
        }

        private static void SaveInternal<T>(string key, T data)
        {
            string json = JsonUtility.ToJson(new Wrapper<T> { value = data });

            lock (flushLock)
            {
                var store = EnsureDataStore();
                var entry = store.entries.Find(e => e.key == key);
                if (entry != null)
                    entry.value = json;
                else
                    store.entries.Add(new AccountSave.AccountDataEntry { key = key, value = json });

                cacheDirty = true;

                if (boundAccount != null && !flushLoopRunning)
                    StartFlushLoopLocked();
            }
        }

        private static T LoadInternal<T>(string key)
        {
            string json = null;

            lock (flushLock)
            {
                var store = EnsureDataStore();
                var entry = store.entries.Find(e => e.key == key);
                if (entry == null || string.IsNullOrEmpty(entry.value))
                    return default;

                json = entry.value;
            }

            try
            {
                var wrapper = JsonUtility.FromJson<Wrapper<T>>(json);
                return wrapper != null ? wrapper.value : default;
            }
            catch
            {
                return default;
            }
        }

        private static void DeleteInternal(string key)
        {
            lock (flushLock)
            {
                var store = EnsureDataStore();
                if (store.entries.RemoveAll(e => e.key == key) > 0)
                {
                    cacheDirty = true;

                    if (boundAccount != null && !flushLoopRunning)
                        StartFlushLoopLocked();
                }
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
            lock (flushLock)
            {
                if (!cacheDirty || boundAccount == null || flushLoopRunning)
                    return;

                StartFlushLoopLocked();
            }
        }

        private static void StartFlushLoopLocked()
        {
            if (boundAccount == null)
                return;

            flushLoopRunning = true;

            if (flushCancellation != null)
            {
                flushCancellation.Cancel();
                flushCancellation.Dispose();
            }

            flushCancellation = new CancellationTokenSource();
            var source = flushCancellation;
            flushTask = Task.Run(() => FlushLoopAsync(source));
        }

        private static async Task FlushLoopAsync(CancellationTokenSource source)
        {
            var token = source.Token;

            try
            {
                while (true)
                {
                    AccountSave snapshot;

                    lock (flushLock)
                    {
                        if (token.IsCancellationRequested || boundAccount == null)
                            return;

                        if (!cacheDirty)
                            return;

                        snapshot = CreateAccountSnapshot(boundAccount);
                        cacheDirty = false;
                    }

                    try
                    {
                        await AccountManager.SaveAsync(snapshot).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        Debug.LogError($"SaveManager: Background flush failed for '{snapshot?.usernameSlug}': {ex}");

                        lock (flushLock)
                        {
                            cacheDirty = true;
                        }

                        try
                        {
                            await Task.Delay(FlushInterval, token).ConfigureAwait(false);
                        }
                        catch (TaskCanceledException)
                        {
                            return;
                        }

                        continue;
                    }

                    try
                    {
                        await Task.Delay(FlushInterval, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected when changing accounts or quitting the application.
            }
            finally
            {
                lock (flushLock)
                {
                    if (ReferenceEquals(flushCancellation, source))
                    {
                        flushCancellation.Dispose();
                        flushCancellation = null;
                    }

                    flushLoopRunning = false;
                    flushTask = null;

                    if (cacheDirty && boundAccount != null)
                        StartFlushLoopLocked();
                }
            }
        }

        private static AccountSave CreateAccountSnapshot(AccountSave source)
        {
            if (source == null)
                return null;

            var snapshot = new AccountSave
            {
                schemaVersion = source.schemaVersion,
                username = source.username,
                usernameSlug = source.usernameSlug,
                passwordHash = source.passwordHash,
                passwordSalt = source.passwordSalt,
                savedSceneName = source.savedSceneName,
                savedX = source.savedX,
                savedY = source.savedY,
                createdAtUtc = source.createdAtUtc,
                lastLoginUtc = source.lastLoginUtc,
                data = new AccountSave.AccountData
                {
                    version = source.data != null ? source.data.version : SaveDataVersion,
                    entries = new List<AccountSave.AccountDataEntry>()
                }
            };

            if (source.data?.entries != null)
            {
                foreach (var entry in source.data.entries)
                {
                    snapshot.data.entries.Add(new AccountSave.AccountDataEntry
                    {
                        key = entry.key,
                        value = entry.value,
                    });
                }
            }

            return snapshot;
        }

        private static void WaitForPendingWrites()
        {
            while (true)
            {
                Task pendingTask;

                lock (flushLock)
                {
                    pendingTask = flushTask;
                    if (pendingTask == null)
                        return;
                }

                try
                {
                    pendingTask.Wait();
                }
                catch (AggregateException ex)
                {
                    ex.Handle(e => e is TaskCanceledException || e is OperationCanceledException);
                }
                catch (OperationCanceledException)
                {
                    // Ignored; the flush loop was cancelled intentionally.
                }
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

            string backupPath = GlobalFilePath + ".bak";
            string tempPath = GlobalFilePath + ".tmp";

            bool TryRestoreFromBackup(string reason)
            {
                if (!File.Exists(backupPath))
                    return false;

                try
                {
                    Debug.LogWarning($"SaveManager: {reason}. Attempting to restore global save from backup.");

                    if (File.Exists(GlobalFilePath))
                    {
                        File.Delete(GlobalFilePath);
                    }

                    File.Move(backupPath, GlobalFilePath);

                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }

                    Debug.LogWarning("SaveManager: Successfully restored global save from backup. Retrying load.");
                    return true;
                }
                catch (Exception restoreEx)
                {
                    Debug.LogError($"SaveManager: Failed to restore global save backup: {restoreEx}");
                    return false;
                }
            }

            Directory.CreateDirectory(AccountManager.BaseDirectory);

            bool retry;
            do
            {
                retry = false;

                try
                {
                    if (File.Exists(GlobalFilePath))
                    {
                        string json = File.ReadAllText(GlobalFilePath, Encoding.UTF8);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            Debug.LogWarning("SaveManager: Global save file is empty.");
                            if (TryRestoreFromBackup("Global save file empty"))
                            {
                                globalCache = null;
                                retry = true;
                                continue;
                            }
                        }

                        globalCache = JsonUtility.FromJson<GlobalData>(json);

                        if (globalCache == null)
                        {
                            Debug.LogWarning("SaveManager: Failed to deserialize global save file (received null).");
                            if (TryRestoreFromBackup("Global save file failed to deserialize"))
                            {
                                retry = true;
                            }
                        }
                    }
                    else if (TryRestoreFromBackup("Global save file missing"))
                    {
                        retry = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"SaveManager: Failed to read global save file: {ex}");
                    if (TryRestoreFromBackup("Global save file unreadable"))
                        retry = true;
                }
            }
            while (retry);

            if (globalCache == null || globalCache.entries == null)
                globalCache = new GlobalData { entries = new List<GlobalEntry>() };

            // Regression test plan: simulate autosave interruption during the temp-to-live swap by killing the application
            // after the .tmp file is written but before the main file is replaced. On next launch verify that the backup is
            // restored, stale .tmp is cleaned, and that subsequent saves rewrite the store without errors.
            return globalCache;
        }

        private static void SaveGlobalStore()
        {
            if (globalCache == null)
                return;

            string tempPath = GlobalFilePath + ".tmp";
            string backupPath = GlobalFilePath + ".bak";

            try
            {
                Directory.CreateDirectory(AccountManager.BaseDirectory);
                string json = JsonUtility.ToJson(globalCache);

                bool backupCreated = false;
                bool swapSucceeded = false;

                using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(tempStream, Encoding.UTF8))
                {
                    writer.Write(json);
                    writer.Flush();
                    tempStream.Flush(true);
                }

                try
                {
                    if (File.Exists(GlobalFilePath))
                    {
                        try
                        {
                            using (var destinationLock = new FileStream(GlobalFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                                destinationLock.Flush(true);
                            }
                        }
                        catch (Exception lockEx)
                        {
                            throw new IOException($"SaveManager: Unable to access global save file '{GlobalFilePath}' for writing.", lockEx);
                        }

                        if (File.Exists(backupPath))
                        {
                            try
                            {
                                File.Delete(backupPath);
                            }
                            catch (Exception deleteEx)
                            {
                                throw new IOException($"SaveManager: Unable to clear stale global backup '{backupPath}' before saving.", deleteEx);
                            }
                        }

                        File.Move(GlobalFilePath, backupPath);
                        backupCreated = true;
                    }

                    try
                    {
                        File.Move(tempPath, GlobalFilePath);
                        swapSucceeded = true;
                    }
                    catch (Exception moveEx)
                    {
                        throw new IOException($"SaveManager: Unable to swap temp global save '{tempPath}' into '{GlobalFilePath}'.", moveEx);
                    }

                    if (backupCreated)
                    {
                        try
                        {
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                            }
                            backupCreated = false;
                        }
                        catch (Exception cleanupEx)
                        {
                            throw new IOException($"SaveManager: Failed to delete global backup '{backupPath}' after writing '{GlobalFilePath}'.", cleanupEx);
                        }
                    }
                }
                catch (Exception operationEx)
                {
                    if (backupCreated && !swapSucceeded)
                    {
                        try
                        {
                            if (!File.Exists(GlobalFilePath) && File.Exists(backupPath))
                            {
                                File.Move(backupPath, GlobalFilePath);
                            }
                            backupCreated = false;
                        }
                        catch (Exception restoreEx)
                        {
                            throw new IOException($"SaveManager: Failed to restore global backup '{backupPath}' after swap failure.", new AggregateException(operationEx, restoreEx));
                        }
                    }

                    throw;
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

                    if (backupCreated && swapSucceeded)
                    {
                        try
                        {
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                            }
                        }
                        catch
                        {
                            // Cleanup best effort when an earlier exception already surfaced.
                        }
                    }
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
