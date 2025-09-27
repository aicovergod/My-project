using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Core.Save;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Core.Save
{
    /// <summary>
    /// Exercises the global save recovery paths on <see cref="SaveManager"/> so we know
    /// that interrupted swaps or corrupted primary files are healed automatically.
    /// </summary>
    [TestFixture]
    public sealed class SaveManagerRecoveryTests
    {
        private const string TestKey = "login_flow_token";
        private const string ExpectedValue = "restored-token";

        private string testDirectory;
        private string globalFilePath;
        private string backupFilePath;
        private string tempFilePath;

        private string originalBaseDirectory;
        private string originalGlobalFilePath;

        [SetUp]
        public void SetUp()
        {
            testDirectory = Path.Combine(Path.GetTempPath(), "SaveManagerRecoveryTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);

            globalFilePath = Path.Combine(testDirectory, "global_state.json");
            backupFilePath = globalFilePath + ".bak";
            tempFilePath = globalFilePath + ".tmp";

            originalBaseDirectory = OverridePrivateStaticString(typeof(AccountManager), "BaseSavePath", testDirectory);
            originalGlobalFilePath = OverridePrivateStaticString(typeof(SaveManager), "GlobalFilePath", globalFilePath);

            ClearGlobalCache();
        }

        [TearDown]
        public void TearDown()
        {
            ClearGlobalCache();

            if (!string.IsNullOrEmpty(originalBaseDirectory))
                OverridePrivateStaticString(typeof(AccountManager), "BaseSavePath", originalBaseDirectory);
            if (!string.IsNullOrEmpty(originalGlobalFilePath))
                OverridePrivateStaticString(typeof(SaveManager), "GlobalFilePath", originalGlobalFilePath);

            TryDeleteDirectory(testDirectory);
        }

        /// <summary>
        /// When the autosave swap is interrupted the .tmp and .bak files are left in place.
        /// The loader should restore the backup, delete the temp artefact, and hydrate the cache.
        /// </summary>
        [Test]
        public void LoadGlobalStore_RecoversFromInterruptedSwap()
        {
            WriteFile(backupFilePath, CreateGlobalStoreJson(ExpectedValue));
            WriteFile(tempFilePath, "{\"incomplete\":true");

            string loaded = SaveManager.LoadGlobal<string>(TestKey);

            Assert.AreEqual(ExpectedValue, loaded, "The backup payload should be surfaced after recovery.");
            Assert.IsTrue(File.Exists(globalFilePath), "The live global_state.json file should be recreated.");
            Assert.IsFalse(File.Exists(tempFilePath), "The stale temp file must be cleaned up during recovery.");
            Assert.IsFalse(File.Exists(backupFilePath), "The backup is moved into place so no .bak file should remain.");
            AssertJsonMatchesExpected(globalFilePath, ExpectedValue);
        }

        /// <summary>
        /// Even when the temp artefact is absent the loader must still restore a missing live file
        /// if a valid backup exists.
        /// </summary>
        [Test]
        public void LoadGlobalStore_RecoversWhenLiveFileMissing()
        {
            WriteFile(backupFilePath, CreateGlobalStoreJson(ExpectedValue));

            string loaded = SaveManager.LoadGlobal<string>(TestKey);

            Assert.AreEqual(ExpectedValue, loaded, "Loading should succeed when only the backup survived.");
            Assert.IsTrue(File.Exists(globalFilePath), "The backup should have been promoted to the live file.");
            Assert.IsFalse(File.Exists(backupFilePath), "The backup must be consumed during promotion.");
            AssertJsonMatchesExpected(globalFilePath, ExpectedValue);
        }

        /// <summary>
        /// Corrupted live JSON should trigger a backup restore so that future loads can succeed.
        /// </summary>
        [Test]
        public void LoadGlobalStore_RecoversFromCorruptedLiveFile()
        {
            WriteFile(globalFilePath, "{\"entries\": [ { \"key\": \"" + TestKey + "\" }");
            WriteFile(backupFilePath, CreateGlobalStoreJson(ExpectedValue));

            string loaded = SaveManager.LoadGlobal<string>(TestKey);

            Assert.AreEqual(ExpectedValue, loaded, "The corrupted primary file should be replaced by the backup.");
            Assert.IsTrue(File.Exists(globalFilePath), "A valid global_state.json should exist after recovery.");
            Assert.IsFalse(File.Exists(backupFilePath), "The backup is moved to live so .bak should be removed.");
            AssertJsonMatchesExpected(globalFilePath, ExpectedValue);
        }

        private static void AssertJsonMatchesExpected(string path, string expected)
        {
            string json = File.ReadAllText(path);
            var store = JsonUtility.FromJson<TestGlobalData>(json);
            Assert.IsNotNull(store, "Global store JSON should deserialize after recovery.");
            Assert.IsNotNull(store.entries, "Global store should expose entries after recovery.");
            Assert.AreEqual(1, store.entries.Count, "Test data only seeds a single key.");

            var wrapper = JsonUtility.FromJson<TestWrapper<string>>(store.entries[0].value);
            Assert.IsNotNull(wrapper, "Wrapper should deserialize correctly.");
            Assert.AreEqual(expected, wrapper.value, "The wrapper payload should match the expected value.");
        }

        private static string CreateGlobalStoreJson(string value)
        {
            var data = new TestGlobalData
            {
                entries = new List<TestGlobalEntry>
                {
                    new TestGlobalEntry
                    {
                        key = TestKey,
                        value = JsonUtility.ToJson(new TestWrapper<string> { value = value }),
                    },
                },
            };

            return JsonUtility.ToJson(data);
        }

        private static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private static void ClearGlobalCache()
        {
            OverridePrivateStatic(typeof(SaveManager), "globalCache", null);
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (IOException)
            {
                // Ignored. On Windows the directory might still be locked briefly between tests.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignored. Tests best-effort clean-up.
            }
        }

        private static string OverridePrivateStaticString(Type type, string fieldName, string newValue)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field, $"Field '{fieldName}' was not found on type '{type}'.");

            string original = field.GetValue(null) as string;
            OverridePrivateStatic(type, fieldName, newValue);
            return original;
        }

        private static void OverridePrivateStatic(Type type, string fieldName, object newValue)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field, $"Field '{fieldName}' was not found on type '{type}'.");
            field.SetValue(null, newValue);
        }

        [Serializable]
        private sealed class TestGlobalData
        {
            public List<TestGlobalEntry> entries = new List<TestGlobalEntry>();
        }

        [Serializable]
        private sealed class TestGlobalEntry
        {
            public string key;
            public string value;
        }

        [Serializable]
        private sealed class TestWrapper<T>
        {
            public T value;
        }
    }
}
