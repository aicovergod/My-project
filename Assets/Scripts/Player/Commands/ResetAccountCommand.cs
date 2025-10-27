using System;
using System.Collections.Generic;
using System.IO;
using Core.Save;
using Player.Ranks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Player.Commands
{
    /// <summary>
    /// Chat command that permanently deletes the active player's save file and returns them to the
    /// login screen. The routine stops the save system, removes the JSON profile alongside any
    /// temporary/backup artefacts, and then loads the Login scene so the player can re-authenticate
    /// or create a new account.
    /// </summary>
    public sealed class ResetAccountCommand : IPlayerCommand
    {
        private const string LoginSceneName = "Login";

        /// <inheritdoc />
        public string Name => "resetaccount";

        /// <inheritdoc />
        public string Description => "Deletes the active account save and returns to the login screen.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Developer;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            string activeSlug = SaveManager.ActiveProfileId;
            string activeUsername = SaveManager.ActiveAccountUsername;
            if (string.IsNullOrEmpty(activeSlug))
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "No account is currently logged in. Log in before using ::resetaccount.");
            }

            string accountPath = AccountManager.GetAccountPath(activeSlug);
            string displayName = string.IsNullOrEmpty(activeUsername) ? activeSlug : activeUsername;

            // Capture the current on-disk state so we can restore the session if deletion fails
            // midway through and leaves the player without an active profile.
            var preloadStatus = AccountManager.TryLoadAccount(activeSlug, out var restoreCandidate);

            try
            {
                // Unbind the active profile so background autosaves stop touching the file while we
                // delete it. The bind routine waits for any pending writes before detaching.
                SaveManager.BindAccount(null, false);
            }
            catch (Exception ex)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    $"Failed to detach the active account: {ex.Message}");
            }

            var errors = new List<string>();
            bool deletedAny = false;
            deletedAny |= TryDeleteFile(accountPath, errors);
            deletedAny |= TryDeleteFile(accountPath + ".tmp", errors);
            deletedAny |= TryDeleteFile(accountPath + ".bak", errors);
            deletedAny |= TryDeleteFile(accountPath + ".bak.tmp", errors);
            deletedAny |= TryDeleteFile(accountPath + ".bak.restore", errors);

            if (errors.Count > 0)
            {
                // Attempt to restore the session when deletion fails so the player is not stranded in
                // a half-detached state.
                if (restoreCandidate != null && preloadStatus == AccountManager.AccountLoadStatus.Success)
                {
                    try
                    {
                        SaveManager.BindAccount(restoreCandidate, true);
                    }
                    catch (Exception bindEx)
                    {
                        errors.Add($"Additionally failed to restore the active account: {bindEx.Message}");
                    }
                }
                else if (preloadStatus == AccountManager.AccountLoadStatus.FailedToDeserialize)
                {
                    errors.Add("Additionally failed to restore the active account because the save data is corrupted.");
                }

                string errorMessage = errors.Count == 1 ? errors[0] : string.Join(" ", errors);
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    $"Account reset failed: {errorMessage}");
            }

            string successMessage = deletedAny
                ? $"Account data for {displayName} has been deleted. Returning to the login screen."
                : $"No save files existed for {displayName}. Returning to the login screen.";

            try
            {
                // Force the client back to the authentication flow once the save data is gone so
                // the player can immediately create a fresh profile or log in with another account.
                SceneManager.LoadScene(LoginSceneName, LoadSceneMode.Single);
            }
            catch (Exception ex)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    $"Account deleted but failed to load the login screen: {ex.Message}");
            }

            return PlayerCommandResult.Success(successMessage);
        }

        /// <summary>
        /// Attempts to delete the supplied file path and records any errors so the caller can surface
        /// precise failure details to the player.
        /// </summary>
        /// <param name="path">Absolute file path that should be removed.</param>
        /// <param name="errors">Collection that receives any failure descriptions.</param>
        /// <returns><c>true</c> when the file existed and was deleted successfully.</returns>
        private static bool TryDeleteFile(string path, List<string> errors)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                if (!File.Exists(path))
                    return false;

                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                errors?.Add($"Failed to delete '{Path.GetFileName(path)}': {ex.Message}");
                Debug.LogError($"ResetAccountCommand: Failed to delete '{path}'. {ex}");
                return false;
            }
        }
    }
}
