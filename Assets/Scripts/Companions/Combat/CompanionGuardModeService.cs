using System;
using Combat;
using UnityEngine;

namespace Companions.Combat
{
    /// <summary>
    /// Centralises guard-mode state management for companions so combat bridges and managers can share
    /// the same activation flags, cooldown locks, and attack routing without duplicating logic.
    /// </summary>
    public static class CompanionGuardModeService
    {
        /// <summary>Active controller bound to guard mode so attacks can be routed directly.</summary>
        private static CompanionController boundController;

        /// <summary>Cooldown tracker tied to the active controller for combat decline cooldown queries.</summary>
        private static CompanionSkillCooldownTracker boundCooldownTracker;

        /// <summary>Tracks whether guard mode is currently enabled for the bound controller.</summary>
        private static bool guardModeEnabled;

        /// <summary>True when guard mode is locked by an active combat-decline cooldown.</summary>
        private static bool guardModeLockedByCombatCooldown;

        /// <summary>
        /// Raised whenever guard mode toggles. The boolean flag mirrors the enabled state while the
        /// second flag indicates whether a flavour line should be published to chat.
        /// </summary>
        public static event Action<bool, bool> GuardModeStateChanged;

        /// <summary>Raised whenever the combat-decline cooldown lock toggles.</summary>
        public static event Action<bool> GuardModeLockStateChanged;

        /// <summary>Whether guard mode is currently active for the bound companion controller.</summary>
        public static bool GuardModeEnabled => guardModeEnabled;

        /// <summary>True when a combat cooldown is preventing guard mode from being enabled.</summary>
        public static bool IsGuardModeLockedByCombatCooldown => guardModeLockedByCombatCooldown;

        /// <summary>
        /// Registers the active companion controller and cooldown tracker with the service so commands
        /// can be routed and cooldown checks share the same data source.
        /// </summary>
        /// <param name="controller">Live companion controller instance.</param>
        /// <param name="cooldownTracker">Cooldown tracker bound to the controller.</param>
        public static void Bind(CompanionController controller, CompanionSkillCooldownTracker cooldownTracker)
        {
            boundController = controller;
            boundCooldownTracker = cooldownTracker;

            // Reset state when a new controller binds so UIs receive a clean slate.
            SetGuardModeState(false, false, forceNotify: true);
            SetLockState(false);
        }

        /// <summary>
        /// Clears the active controller binding so guard mode stops issuing commands to stale references.
        /// </summary>
        /// <param name="controller">Controller requesting the unbind. Ignored when it does not match the active binding.</param>
        public static void Unbind(CompanionController controller)
        {
            if (controller != null && controller != boundController)
                return;

            boundController = null;
            boundCooldownTracker = null;

            SetGuardModeState(false, false, forceNotify: true);
            SetLockState(false);
        }

        /// <summary>
        /// Called by the combat pipeline when guard mode should prompt the companion to attack.
        /// </summary>
        /// <param name="target">Combat target selected by the player.</param>
        public static void CommandGuardAttack(CombatTarget target)
        {
            if (IsCombatCooldownActiveAndUpdateLock(false))
            {
                DisableGuardModeForCooldown(false);
                return;
            }

            if (!guardModeEnabled)
                return;

            var controller = boundController;
            if (controller == null || target == null)
                return;

            var controllerObject = controller.gameObject;
            if (controllerObject == null || !controllerObject.activeSelf)
                return;

            if (!controller.CanFight)
                return;

            controller.CommandAttack(target);
        }

        /// <summary>
        /// Attempts to issue a direct attack command when guard mode is disabled, mirroring pet manual targeting.
        /// </summary>
        /// <param name="target">Combat target selected by the player.</param>
        /// <returns>True when the companion received the command.</returns>
        public static bool TryCommandAttack(CombatTarget target)
        {
            if (IsCombatCooldownActiveAndUpdateLock(true))
                return false;

            if (guardModeEnabled)
                return false;

            var controller = boundController;
            if (controller == null || target == null)
                return false;

            var controllerObject = controller.gameObject;
            if (controllerObject == null || !controllerObject.activeSelf)
                return false;

            if (!controller.CanFight)
                return false;

            controller.CommandAttack(target);
            return true;
        }

        /// <summary>
        /// Toggles guard mode and notifies listeners so menu labels stay in sync.
        /// </summary>
        public static void ToggleGuardMode()
        {
            if (!guardModeEnabled)
            {
                if (IsCombatCooldownActiveAndUpdateLock(true))
                    return;

                SetGuardModeState(true, true);
                SetLockState(false);
                return;
            }

            SetGuardModeState(false, true);
        }

        /// <summary>
        /// Called whenever the combat-decline cooldown is (re)started so guard mode can be locked and
        /// forcibly disabled until the timer elapses.
        /// </summary>
        public static void HandleCombatDeclineCooldownStarted()
        {
            SetLockState(true);
            DisableGuardModeForCooldown(true);
        }

        /// <summary>
        /// Called whenever the combat-decline cooldown ends or is cleared so guard mode toggles can
        /// resume functioning normally.
        /// </summary>
        public static void HandleCombatDeclineCooldownCleared()
        {
            if (!guardModeLockedByCombatCooldown)
                return;

            SetLockState(false);
        }

        /// <summary>
        /// Disables guard mode due to the combat cooldown and optionally publishes a deactivation line.
        /// </summary>
        /// <param name="publishGuardMessage">True when a flavour message should be raised.</param>
        private static void DisableGuardModeForCooldown(bool publishGuardMessage)
        {
            SetGuardModeState(false, publishGuardMessage);
        }

        /// <summary>
        /// Evaluates whether the combat-decline cooldown is still active and updates the guard lock flag.
        /// </summary>
        /// <param name="publishMessage">True when a cooldown reminder should be shown in chat.</param>
        private static bool IsCombatCooldownActiveAndUpdateLock(bool publishMessage)
        {
            bool active = CompanionSkillCooldownTimers.IsCombatDeclineCooldownActive(boundCooldownTracker, publishMessage);
            if (active)
            {
                SetLockState(true);
                return true;
            }

            if (guardModeLockedByCombatCooldown)
                SetLockState(false);

            return false;
        }

        /// <summary>
        /// Applies the new guard-mode state and notifies listeners when the value changes or a forced update is requested.
        /// </summary>
        /// <param name="enabled">Whether guard mode should be considered active.</param>
        /// <param name="publishChat">True when downstream listeners should publish flavour chat.</param>
        /// <param name="forceNotify">Forces the event to raise even when the state has not changed.</param>
        private static void SetGuardModeState(bool enabled, bool publishChat, bool forceNotify = false)
        {
            if (!forceNotify && guardModeEnabled == enabled)
                return;

            guardModeEnabled = enabled;
            GuardModeStateChanged?.Invoke(enabled, publishChat);
        }

        /// <summary>
        /// Updates the combat cooldown lock flag and raises events when the value changes.
        /// </summary>
        /// <param name="locked">New lock value.</param>
        private static void SetLockState(bool locked)
        {
            if (guardModeLockedByCombatCooldown == locked)
                return;

            guardModeLockedByCombatCooldown = locked;
            GuardModeLockStateChanged?.Invoke(locked);
        }
    }
}
