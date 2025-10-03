# My-project

## Overview
My-project is a Unity-based role-playing game focused on skill-based progression and an interactive economy. The project aims to deliver a persistent world where players train skills, interact with shops, and explore content reminiscent of classic MMORPGs.

## Unity Version
This project currently targets **Unity 6000.2.3f1**.

## Major Systems
- **Saving System** – Provides persistent player data using components like `SaveManager` and player-specific save bridges.
- **Skill System** – `SkillManager` orchestrates XP/levels across gathering/combat skills (Woodcutting, Mining, Fishing, Cooking, Beastmaster, etc.) while the in-game `AdminF2Menu` handles debug level overrides.
- **Firemaking & Bonfires** – New Firemaking content lives in `Skills/Firemaking`. `FiremakingSkill` covers ignition, bonfire fueling, Phoenix pet bonuses, outfit rolls, and ashes, while `FiremakingHUD` mirrors tick progress. Expand the skill by authoring `FiremakingLogDefinition` assets under `Resources/Firemaking/Logs`.
- **Gathering Feedback Services** – `Skills/Common/GatheringFloatingTextService` centralises OSRS-style floating text, range checks, cooldowns, and delayed XP popups so Woodcutting, Mining, Fishing, and Firemaking share consistent messaging.
- **Shop System** – Supports buying and selling items via `Shop` and `ShopUI` components.
- **Inventory, Equipment & Bank** – `Inventory`, `ItemUseResolver`, `StackSplitDialog`, and `InventoryDropMenu` provide drag/drop, stack splitting, and consumable handling, while `EquipmentAggregator` aggregates bonuses. Bank interactions run through `BankUI`, `BankDepositMenu`, and `BankWithdrawMenu` with `BankOpener` world hooks.
- **Status & Buff System** – Centralises timed effects with `Status/BuffTimerService`, `BuffEvents`, and `BuffStateSaveBridge`. Buff icons and expiry audio now route through `UI/HUD/BuffHudManager`, `BuffInfoBox`, and `BuffTooltipController`.
- **Magic & Combat Hooks** – `MagicUI` builds the spellbook from `Resources/Spells`, while combat flow leverages `CombatController` and modular add-ons like `OnHitPoisonApplier` for weapon-based status effects.
- **Scene Transition & Respawn** – `World/SceneTransitionManager` performs additive scene swaps with fades via `ScreenFader`, keeps persistent singletons informed, and hands off spawn IDs. `Player/PlayerRespawnSystem` cooperates with `World/RespawnPoint` markers to clear buffs, play the death jingle, and restore the player, while `PlayerMover` persists across scenes, auto-walks gathering nodes, and saves recent positions for the autosave loop.
- **Interface Tabs & UI Shell** – `UI/InterfaceTabButtons` spawns the OSRS-style tab strip (Quest, Inventory, Skills, Equipment, Attack Style, Magic) and works alongside `UIManager` to auto-close conflicting panels.
- **Audio & Feedback** – `Audio/SoundManager` exposes the `SoundEffect` enum for music/SFX (including the death jingle) and keeps AudioListener duplication in check during transitions.
- **Lore & Books** – Scriptable `BookData` assets back in-world books while `BookProgressManager` tracks which page each player has reached when reading from items or world interactions.
- **Daily Time Service** – `Core/Time/DailyGameTimeService` caches the current UTC day, raises a `DayChanged` event for daily resets, and exposes `ComposeDailySeed` so features like fishing bycatch, rotating shops, or quests can share deterministic per-day RNG.

## Project Structure

- `Assets/Scripts/Core` – Bootstraps the project through `GameManager`, exposes save helpers via `SaveManager`, resolves input with `Core/Input/InputActionResolver`, and maintains the daily time service.
- `Assets/Scripts/Player` – Contains `PlayerMover`, `PlayerCombatLoadout`, `PlayerCombatTarget`, `PlayerHitpoints`, `PlayerRespawnSystem`, and supporting HUD scripts such as `HealthHUD`.
- `Assets/Scripts/Combat` – Houses `CombatController`, `CombatMath`, `CombatWeaponHUD`, `OnHitPoisonApplier`, and combat enums/targets used by players and NPCs.
- `Assets/Scripts/Skills` – Includes `SkillManager`, gathering skills (Woodcutting, Mining, Fishing, Cooking, Firemaking), Beastmaster content, outfits, and shared gathering utilities.
- `Assets/Scripts/Inventory` & `Assets/Scripts/Items` – Provide the bag/equipment UI (`Inventory`, `Equipment`, `StackSplitDialog`, `ItemUseResolver`) plus `Items/Consumables` and `ItemCombatStats` definitions.
- `Assets/Scripts/Bank` & `Assets/Scripts/Shop` – Implement OSRS-style bank panels (`BankUI`, deposit/withdraw menus) and merchant interfaces (`Shop`, `ShopUI`).
- `Assets/Scripts/Magic` – Defines spells via `SpellDefinition` and projectile/hit effect prefabs (`FireProjectile`, `HitEffect`).
- `Assets/Scripts/NPC` – Covers NPC combat (`NpcAttackController`, `NpcCombatant`), navigation, interactions, and HUD helpers.
- `Assets/Scripts/World` – Manages persistent objects, scene transitions, respawn markers, minimap/popup text systems, and screen fades.
- `Assets/Scripts/UI` – Centralises OSRS-style UI shells (`UIManager`, `InterfaceTabButtons`, `PersistentEventSystem`, `MagicUI`, HUD widgets).
- `Assets/Scripts/Status` – Contains `BuffTimerService`, `BuffEvents`, antifire/freeze/poison controllers, and save bridges.
- `Assets/Scripts/Pets` & `Assets/Scripts/Drops` – Run pet followers/storage, pet XP, drop tables (`DropResolver`, `GroundItemSpawner`), and pet drop UI. `PetDefinition` exposes eight-direction idle/walk/hit sprite arrays, per-state FPS overrides, and mirroring toggles so pet art can be sourced from single-sided sheets.
- `Assets/Scripts/Audio` – Provides `SoundManager` and the `SoundEffect` enum for SFX/music routing.
- `Assets/Scripts/Books`, `Assets/Scripts/Dialogue`, `Assets/Scripts/Quests` – Supply lore ScriptableObjects, dialogue UI/data, and quest management/definitions.
- `Assets/Scripts/Environment` & `Assets/Scripts/Util` – Offer environmental helpers like `FenceColliderFoot` and cross-cutting utilities (`Ticker`, `SpriteDepth`, `ITickable`).
- `Assets/Tests` – NUnit tests covering cooking, NPC factions, and elemental modifiers; execute them through the Unity Test Runner (`Unity -runTests`).

## Input & Rebinding
- The project relies on the Unity Input System with the `Assets/InputSystem_Actions.inputactions` asset. The **Player** map now
  exposes `Move`, `Interact`, `Prospect`, `Cancel`, and `OpenMenu` so gameplay, NPCs, and UI can share the same bindings.
- Player-facing scripts such as gathering controllers and `NpcInteractable` consume input through a `PlayerInput` component or
  serialized `InputActionReference` fields. The shared `Core/Input/InputActionResolver` helper resolves the action instance and
  ensures it is enabled when required.
- To rebind controls:
  1. Open **Input System Actions** in the Unity editor and edit the relevant binding under the **Player** action map. Left-click
     interactions, right-click context actions, and menu cancellation already have default bindings.
  2. Apply the changes to the `PlayerInput` component on the player prefab or scene object (it should reference the updated
     action asset).
  3. Any script that needs the new binding can request it via `InputActionResolver.Resolve`, optionally exposing an
     `InputActionReference` field for prefab-level overrides. This keeps new systems aligned with the shared action map and
     avoids duplicating bindings.

## Build and Run
1. Install Unity 6000.2.3f1 or newer.
2. Clone this repository.
   ```bash
   git clone <repo-url>
   ```
3. Open the project with Unity Hub or the Unity editor.
4. Load the desired scene (e.g., `Assets/Scenes/OverWorld.unity`).
5. Press the **Play** button in the editor to run.
6. For a standalone build, use **File > Build Settings** and select your target platform.

## Contribution Guidelines
1. Fork the repository and create a feature branch for your work.
2. Follow standard C# and Unity best practices.
3. Run existing tests through the Unity Test Runner before submitting.
4. Open a pull request with a clear description of your changes.

## NPC Setup Notes
- Add `NpcKnockbackReceiver` alongside `NpcWanderer` on light NPC prefabs to enable damage-driven knockback. The receiver defaults to easing the displacement over a short duration while clamping to wander bounds so goblins stay near their spawn tile.
- Heavy or raid-class NPCs can omit the receiver (or disable the `enableKnockback` toggle) to stay rooted. Leaving the component absent on `NPC_GOBLIN_WARCHIEF` keeps the boss-style goblin stationary.
- Designers can tune knockback by adjusting the base distance, duration, and damage scaling curve directly on the component. The wanderer now exposes `ApplyKnockback`/`CancelKnockback` helpers should scripted events need to fire custom impulses.
