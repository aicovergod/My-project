# Login Flow Playtest Notes

These scenarios help verify the revised login resume logic for both established accounts and brand-new profiles. Follow them whenever the login, save, or scene-loading code changes.

## Returning Account
1. Launch the project and authenticate with an existing account that has a saved location away from the overworld (e.g., log out inside an interior scene).
2. After entering credentials, confirm the status text reports the authentication and that the loading message changes to “Restoring last location…”.
3. Observe that the client loads the saved scene directly (the overworld should no longer flash first).
4. Once the scene finishes loading, ensure the player character appears at the exact saved coordinates and that pets or followers align beside the player.
5. Remain idle for at least 10 seconds so the autosave loop runs, then quit and relaunch. Verify the account still resumes in the same scene and position, confirming that the autosave loop resumed after placement.

## New Account
1. Launch the project and authenticate with a brand-new username/password combination so a fresh profile is created.
2. Confirm the status text switches to “Preparing the overworld…” while loading.
3. On a true cold start, let the client sit even if the scene takes longer than five seconds to finish bootstrapping. The login screen should stay on the loading message while the console periodically logs that it is waiting for the `PlayerInputManager`/`PlayerMover` instead of bouncing back to the credential form.
4. After the scene activates, verify that the player prefab spawns in the overworld at coordinates (0, 0, 0) before taking any movement input.
5. Wait for at least 10 seconds so an autosave occurs, then return to the login screen and log in again. The new account should now resume in the overworld at the last saved position, demonstrating that the initial placement was persisted correctly.

Document any deviations from the above behaviour so regressions can be triaged quickly.
