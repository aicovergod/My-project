// Refactor: OSRS-style login with per-account saves; removed Overworld hop; atomic file IO; PBKDF2.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Core.Save
{
    /// <summary>
    /// Handles account creation, authentication, and persistence using OSRS-style username and
    /// password credentials. Each account is stored as an individual JSON file so save data can
    /// be isolated per profile without a selection UI.
    /// </summary>
    public static class AccountManager
    {
        /// <summary>
        /// Base directory used for storing per-account save files. This resolves to Unity's
        /// persistent data path so the saves land in a writable location on every platform.
        /// </summary>
        private static readonly string BaseSavePath = Path.Combine(Application.persistentDataPath, "PlayerSave");
        private const int SaltSizeBytes = 16;
        private const int Pbkdf2Iterations = 100_000;
        private const int Pbkdf2KeySizeBytes = 32;
        private const string DefaultSceneName = "OverWorld";

        static AccountManager()
        {
            EnsureBaseDirectory();
        }

        /// <summary>
        /// Sanitises the supplied username into a slug suitable for filenames and profile IDs.
        /// Characters outside the [a-z0-9_-] range are discarded and whitespace becomes underscores.
        /// </summary>
        /// <param name="input">Raw username supplied by the player.</param>
        /// <returns>Lowercase slug that can be used safely for filenames.</returns>
        public static string SanitizeUsername(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var builder = new StringBuilder(input.Length);
            bool previousUnderscore = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = char.ToLowerInvariant(input[i]);

                if (char.IsWhiteSpace(c))
                {
                    if (!previousUnderscore && builder.Length > 0)
                    {
                        builder.Append('_');
                        previousUnderscore = true;
                    }
                    continue;
                }

                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_')
                {
                    builder.Append(c);
                    previousUnderscore = c == '_';
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Resolves the JSON path for an account file using either the raw username or an existing slug.
        /// </summary>
        /// <param name="usernameOrSlug">Raw username entered by the player or a pre-sanitised slug.</param>
        /// <returns>Absolute path to the JSON save file.</returns>
        public static string GetAccountPath(string usernameOrSlug)
        {
            string slug = SanitizeUsername(usernameOrSlug);
            return string.IsNullOrEmpty(slug)
                ? Path.Combine(BaseSavePath, "account.json")
                : Path.Combine(BaseSavePath, $"{slug}.json");
        }

        /// <summary>
        /// Attempts to load an account save file using the provided username or slug.
        /// </summary>
        /// <param name="username">Raw username supplied by the player.</param>
        /// <param name="save">Outputs the loaded account when successful.</param>
        /// <returns>True when a matching account file was found and deserialised.</returns>
        public static bool TryLoadAccount(string username, out AccountSave save)
        {
            save = null;

            string slug = SanitizeUsername(username);
            if (string.IsNullOrEmpty(slug))
                return false;

            string path = GetAccountPath(slug);
            if (!File.Exists(path))
                return false;

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var loaded = JsonUtility.FromJson<AccountSave>(json);
                if (loaded == null)
                    return false;

                EnsureDefaults(loaded, slug, username);
                save = loaded;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"AccountManager: Failed to load account '{slug}': {ex}");
                save = null;
                return false;
            }
        }

        /// <summary>
        /// Constructs a new account save with default data and hashed credentials.
        /// </summary>
        /// <param name="username">Username entered by the player.</param>
        /// <param name="rawPassword">Plain text password to hash.</param>
        /// <returns>Initialised account save ready for persistence.</returns>
        public static AccountSave CreateNewAccount(string username, string rawPassword)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username is required.", nameof(username));
            if (string.IsNullOrEmpty(rawPassword))
                throw new ArgumentException("Password is required.", nameof(rawPassword));

            string slug = SanitizeUsername(username);
            if (string.IsNullOrEmpty(slug))
                throw new ArgumentException("Username must include at least one valid character.", nameof(username));

            var salt = new byte[SaltSizeBytes];
            RandomNumberGenerator.Fill(salt);
            byte[] hash = HashPassword(rawPassword, salt);

            string now = DateTime.UtcNow.ToString("O");

            var save = new AccountSave
            {
                schemaVersion = 1,
                username = username.Trim(),
                usernameSlug = slug,
                passwordHash = Convert.ToBase64String(hash),
                passwordSalt = Convert.ToBase64String(salt),
                savedSceneName = DefaultSceneName,
                savedX = 0f,
                savedY = 0f,
                createdAtUtc = now,
                lastLoginUtc = now,
                data = new AccountSave.AccountData
                {
                    version = SaveManager.SaveDataVersion,
                    entries = new List<AccountSave.AccountDataEntry>(),
                },
            };

            return save;
        }

        /// <summary>
        /// Validates a password against the stored PBKDF2 hash using constant-time comparison.
        /// </summary>
        /// <param name="save">Account data loaded from disk.</param>
        /// <param name="rawPassword">Password entered by the player.</param>
        /// <returns>True when the password matches the stored hash.</returns>
        public static bool VerifyPassword(AccountSave save, string rawPassword)
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            if (rawPassword == null)
                throw new ArgumentNullException(nameof(rawPassword));

            if (string.IsNullOrEmpty(save.passwordSalt) || string.IsNullOrEmpty(save.passwordHash))
                return false;

            try
            {
                byte[] salt = Convert.FromBase64String(save.passwordSalt);
                byte[] storedHash = Convert.FromBase64String(save.passwordHash);
                byte[] computed = HashPassword(rawPassword, salt);
                return ConstantTimeEquals(storedHash, computed);
            }
            catch (FormatException)
            {
                Debug.LogError($"AccountManager: Stored credentials for '{save.usernameSlug}' are corrupted.");
                return false;
            }
        }

        /// <summary>
        /// Saves the supplied account to disk using atomic file replacement.
        /// </summary>
        /// <param name="save">Account that should be persisted.</param>
        public static async Task SaveAsync(AccountSave save)
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));

            EnsureBaseDirectory();
            EnsureDefaults(save, save.usernameSlug, save.username);

            save.lastLoginUtc = DateTime.UtcNow.ToString("O");

            string path = GetAccountPath(save.usernameSlug);
            string tempPath = path + ".tmp";
            string json = JsonUtility.ToJson(save, true);

            try
            {
                await Task.Run(() =>
                {
                    File.WriteAllText(tempPath, json, Encoding.UTF8);
                    if (File.Exists(path))
                    {
                        File.Replace(tempPath, path, null);
                    }
                    else
                    {
                        if (File.Exists(path))
                            File.Delete(path);

                        File.Move(tempPath, path);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"AccountManager: Failed to persist account '{save.usernameSlug}': {ex}");
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
                    // Ignored; temp cleanup best-effort.
                }
            }
        }

        /// <summary>
        /// Provides the filesystem directory used for per-account saves so other systems can
        /// reference it when needed (e.g., global data migration helpers).
        /// </summary>
        internal static string BaseDirectory => BaseSavePath;

        private static void EnsureDefaults(AccountSave save, string slug, string username)
        {
            if (save == null)
                return;

            if (save.schemaVersion <= 0)
                save.schemaVersion = 1;

            if (string.IsNullOrEmpty(save.usernameSlug))
                save.usernameSlug = SanitizeUsername(slug);

            if (string.IsNullOrEmpty(save.username) && !string.IsNullOrWhiteSpace(username))
                save.username = username.Trim();

            if (save.data == null)
            {
                save.data = new AccountSave.AccountData
                {
                    version = SaveManager.SaveDataVersion,
                    entries = new List<AccountSave.AccountDataEntry>(),
                };
            }
            else
            {
                if (save.data.entries == null)
                    save.data.entries = new List<AccountSave.AccountDataEntry>();
                if (save.data.version != SaveManager.SaveDataVersion)
                    save.data.version = SaveManager.SaveDataVersion;
            }

            if (string.IsNullOrEmpty(save.savedSceneName))
                save.savedSceneName = DefaultSceneName;
        }

        private static byte[] HashPassword(string password, byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(Pbkdf2KeySizeBytes);
        }

        private static bool ConstantTimeEquals(IReadOnlyList<byte> left, IReadOnlyList<byte> right)
        {
            if (left == null || right == null || left.Count != right.Count)
                return false;

            int diff = 0;
            for (int i = 0; i < left.Count; i++)
                diff |= left[i] ^ right[i];
            return diff == 0;
        }

        private static void EnsureBaseDirectory()
        {
            try
            {
                Directory.CreateDirectory(BaseSavePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"AccountManager: Failed to create save directory at '{BaseSavePath}': {ex}");
            }
        }
    }

    /// <summary>
    /// Serializable DTO that represents a single account, including credentials and gameplay state.
    /// </summary>
    [Serializable]
    public sealed class AccountSave
    {
        public int schemaVersion;
        public string username;
        public string usernameSlug;
        public string passwordHash;
        public string passwordSalt;
        public string savedSceneName;
        public float savedX;
        public float savedY;
        public string createdAtUtc;
        public string lastLoginUtc;
        public AccountData data = new AccountData();

        [Serializable]
        public sealed class AccountData
        {
            public int version = SaveManager.SaveDataVersion;
            public List<AccountDataEntry> entries = new List<AccountDataEntry>();
        }

        [Serializable]
        public sealed class AccountDataEntry
        {
            public string key;
            public string value;
        }
    }
}
