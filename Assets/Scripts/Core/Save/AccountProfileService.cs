using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Core.Save
{
    /// <summary>
    /// Maintains a collection of player accounts and coordinates authentication so each
    /// gameplay profile can be isolated inside <see cref="SaveManager"/>. Account data lives
    /// alongside save files via <see cref="SaveManager.SaveGlobal"/> calls so credential
    /// checks function before a profile is activated.
    /// </summary>
    public static class AccountProfileService
    {
        private const string AccountsKey = "accounts";
        private const int SaltSize = 32;

        private static AccountCollection cache;
        private static AccountEntry activeAccount;

        /// <summary>
        /// Display name for the authenticated account. Returns an empty string when no
        /// account has been activated yet.
        /// </summary>
        public static string ActiveDisplayName => activeAccount != null ? activeAccount.DisplayName : string.Empty;

        /// <summary>
        /// Normalised profile identifier for the active account. Falls back to the
        /// <see cref="SaveManager.ActiveProfileId"/> value so legacy callers can check the
        /// current profile even before <see cref="ActivateAccount"/> completes.
        /// </summary>
        public static string ActiveProfileId => activeAccount != null ? activeAccount.ProfileId : SaveManager.ActiveProfileId;

        /// <summary>
        /// Returns a read-only list of known accounts so login UIs can present existing
        /// characters.
        /// </summary>
        public static IReadOnlyList<AccountEntry> Accounts
        {
            get
            {
                var collection = EnsureCollection();
                return collection.Accounts;
            }
        }

        /// <summary>
        /// Attempts to authenticate or create an account with the provided credentials. Use
        /// this overload when the caller is not interested in the detailed status message.
        /// </summary>
        public static bool TryAuthenticate(string username, string password, out bool created)
        {
            return TryAuthenticate(username, password, out created, out _, out _);
        }

        /// <summary>
        /// Attempts to authenticate the supplied credentials. A new account is created when
        /// the username has not been seen before; otherwise the password must match the stored
        /// hash. The method always normalises the username before storing it so profile IDs can
        /// be used safely with <see cref="SaveManager"/>.
        /// </summary>
        /// <param name="username">Raw username entered by the player.</param>
        /// <param name="password">Plain text password supplied by the player.</param>
        /// <param name="created">Outputs true when an account was created during this call.</param>
        /// <param name="entry">Outputs the account entry that matches the credentials.</param>
        /// <param name="statusMessage">Describes the result in a player-friendly manner.</param>
        /// <returns>True when the authentication succeeded.</returns>
        public static bool TryAuthenticate(string username, string password, out bool created, out AccountEntry entry, out string statusMessage)
        {
            created = false;
            entry = null;
            statusMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(username))
            {
                statusMessage = "Enter a username to begin.";
                return false;
            }

            if (string.IsNullOrEmpty(password))
            {
                statusMessage = "Enter your password.";
                return false;
            }

            string displayName = username.Trim();
            string normalized = NormalizeUsername(displayName);

            if (string.IsNullOrEmpty(normalized))
            {
                statusMessage = "Usernames must include at least one letter or number.";
                return false;
            }

            var collection = EnsureCollection();
            entry = collection.FindByProfileId(normalized);

            if (entry == null)
            {
                entry = CreateAccount(displayName, normalized, password);
                collection.Add(entry);
                created = true;
                statusMessage = $"Created new account for {displayName}.";
                SaveCollection();
                return true;
            }

            byte[] saltBytes;
            byte[] storedHash;
            try
            {
                saltBytes = Convert.FromBase64String(entry.Salt);
                storedHash = Convert.FromBase64String(entry.PasswordHash);
            }
            catch (Exception)
            {
                statusMessage = "Account data is corrupted. Please recreate the profile.";
                return false;
            }

            var computedHash = HashPassword(saltBytes, password);

            if (!ConstantTimeEquals(storedHash, computedHash))
            {
                statusMessage = "Incorrect password.";
                return false;
            }

            entry.RefreshDisplayName(displayName);
            statusMessage = $"Welcome back, {entry.DisplayName}.";
            SaveCollection();
            return true;
        }

        /// <summary>
        /// Activates the supplied account by selecting its profile in the save manager and
        /// recording the last-used identifier for future sessions.
        /// </summary>
        /// <param name="entry">Account that should become active.</param>
        /// <returns>A user-facing status message describing the result.</returns>
        public static string ActivateAccount(AccountEntry entry)
        {
            if (entry == null)
                return "No account selected.";

            SaveManager.SetActiveProfile(entry.ProfileId);
            activeAccount = entry;

            var collection = EnsureCollection();
            collection.LastUsedProfileId = entry.ProfileId;
            SaveCollection();

            return $"Logged in as {entry.DisplayName}.";
        }

        /// <summary>
        /// Returns the display name for the most recently used account, allowing UI layers to
        /// pre-fill login forms.
        /// </summary>
        public static string GetLastUsedDisplayName()
        {
            var collection = EnsureCollection();
            var entry = collection.FindByProfileId(collection.LastUsedProfileId);
            return entry != null ? entry.DisplayName : string.Empty;
        }

        /// <summary>
        /// Normalises a username so it is suitable for use as a profile identifier. Whitespace
        /// is removed, characters are lowercased, and colon characters are replaced with
        /// underscores so the save key prefix remains valid.
        /// </summary>
        public static string NormalizeUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return string.Empty;

            string trimmed = username.Trim();
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

        private static AccountEntry CreateAccount(string displayName, string normalizedProfileId, string password)
        {
            var salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            var hash = HashPassword(salt, password);

            return new AccountEntry(displayName, normalizedProfileId, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
        }

        private static byte[] HashPassword(byte[] salt, string password)
        {
            if (salt == null)
                throw new ArgumentNullException(nameof(salt));
            if (password == null)
                throw new ArgumentNullException(nameof(password));

            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var buffer = new byte[salt.Length + passwordBytes.Length];
            Buffer.BlockCopy(salt, 0, buffer, 0, salt.Length);
            Buffer.BlockCopy(passwordBytes, 0, buffer, salt.Length, passwordBytes.Length);

            using var sha = SHA256.Create();
            return sha.ComputeHash(buffer);
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];

            return diff == 0;
        }

        private static AccountCollection EnsureCollection()
        {
            if (cache != null)
                return cache;

            cache = SaveManager.LoadGlobal<AccountCollection>(AccountsKey);
            if (cache == null)
                cache = new AccountCollection();

            return cache;
        }

        private static void SaveCollection()
        {
            if (cache == null)
                return;

            SaveManager.SaveGlobal(AccountsKey, cache);
        }
    }

    /// <summary>
    /// Serializable container stored in the global save file that tracks every known account
    /// as well as the last-used identifier.
    /// </summary>
    [Serializable]
    public sealed class AccountCollection
    {
        [SerializeField]
        private List<AccountEntry> accounts = new List<AccountEntry>();

        [SerializeField]
        private string lastUsedProfileId = string.Empty;

        /// <summary>
        /// Read-only view of the stored accounts.
        /// </summary>
        public IReadOnlyList<AccountEntry> Accounts => accounts;

        /// <summary>
        /// Identifier of the most recently authenticated account.
        /// </summary>
        public string LastUsedProfileId
        {
            get => lastUsedProfileId;
            set => lastUsedProfileId = value;
        }

        /// <summary>
        /// Adds a new account entry to the collection.
        /// </summary>
        public void Add(AccountEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            accounts.Add(entry);
        }

        /// <summary>
        /// Finds the stored account that matches the provided profile identifier.
        /// </summary>
        public AccountEntry FindByProfileId(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                return null;

            for (int i = 0; i < accounts.Count; i++)
            {
                var candidate = accounts[i];
                if (candidate == null)
                    continue;
                if (string.Equals(candidate.ProfileId, profileId, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }
    }

    /// <summary>
    /// Serialized representation of an account, including the display name shown to the player
    /// and the salted password hash used to validate credentials.
    /// </summary>
    [Serializable]
    public sealed class AccountEntry
    {
        [SerializeField]
        private string displayName;

        [SerializeField]
        private string profileId;

        [SerializeField]
        private string salt;

        [SerializeField]
        private string passwordHash;

        /// <summary>
        /// Parameterless constructor required for serialization.
        /// </summary>
        public AccountEntry()
        {
        }

        public AccountEntry(string displayName, string profileId, string salt, string passwordHash)
        {
            this.displayName = displayName;
            this.profileId = profileId;
            this.salt = salt;
            this.passwordHash = passwordHash;
        }

        /// <summary>
        /// Name presented to the player inside menus.
        /// </summary>
        public string DisplayName => displayName;

        /// <summary>
        /// Normalised identifier used to select save data.
        /// </summary>
        public string ProfileId => profileId;

        /// <summary>
        /// Base64 encoded salt used when hashing the password.
        /// </summary>
        public string Salt => salt;

        /// <summary>
        /// Base64 encoded salted password hash.
        /// </summary>
        public string PasswordHash => passwordHash;

        /// <summary>
        /// Updates the display name without altering the stored credentials.
        /// </summary>
        public void RefreshDisplayName(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                displayName = value.Trim();
        }
    }
}
