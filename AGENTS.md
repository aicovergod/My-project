## Unity Development Principles for the RuneScape-Style Project
1. **Maintain steady progress.** Keep iterating on gameplay, skills, UI, and NPC systems without waiting for follow-up instructions whenever the goal is clear. If you must pause, be ready to explain the blocker (missing assets, unclear requirements, technical issues, etc.).
2. **Plan before debugging.** When behaviour in the Unity Editor or Play Mode looks off, review the relevant scripts and scene setup first. Use `Debug.Log`, the Unity Console, and targeted checks to validate assumptions about variables, object references, and execution order, and lean on the per-system `EnableDebugLogging` flags already in gathering skills—wire any new debug switches into the `AdminF2Menu` so tooling stays consistent.
3. **Validate changes in Unity.** After updating or creating scripts, attach them to the correct GameObjects, enter Play Mode, and confirm they function as intended. When altering gameplay, persistence, or scene transitions, run the Unity Test Runner edit/play mode suites under `Assets/Tests` and walk the login regression steps in `PlaytestNotes_LoginFlow.md` to cover regressions.
4. **Be careful with tooling and long-running tasks.** Think through any command-line or editor process—avoid starting builds or scripts that could run indefinitely without a plan. When long jobs are required (builds, analyses), run them in separate processes so regular development stays responsive, and ensure custom scripts can exit cleanly.
5. **Integrate before inventing globals.** Extend existing managers/services (`GameManager`, `SkillManager`, `SaveManager`, `ItemDatabase`, etc.) before creating new persistent systems so behaviour fits the shared lifecycle described below.
6. **Build around the 0.6 s tick.** Respect the tick-driven systems already in place by subscribing to `Util/Ticker`/`ITickable` instead of raw `Update` whenever you add feature logic that should align with the shared cadence.

# Agent Guidelines

## Project Snapshot
- **Engine**: Unity 6000.2.3f1 targeting a top-down, OSRS-inspired 2D RPG. The project lives in a URP setup with persistent world systems and should continue using the 64×64 pixel tile standard.
- **Core Pillars**: Tick-driven combat/skills, modular skill progression, persistent saving, OSRS-style UI/UX (LegacyRuntime.ttf as the default font), and a shared economy/inventory framework.
- **Input**: New Unity Input System via `Assets/InputSystem_Actions.inputactions`. Player-facing logic resolves actions through `Core/Input/InputActionResolver` and typically expects a `PlayerInput` component.

## Key Systems & Where They Live
- **Bootstrap & Persistence** (`Assets/Scripts/Core`, `Assets/Scripts/World`)
  - `Core/GameManager` is the scene-agnostic bootstrapper. It caches singletons spawned by `Resources/PersistentObjects.asset` and starts autosaves via `SaveManager`.
  - `World/ScenePersistentObject` and `World/PersistentObjectBootstrap` keep singleton prefabs alive across scenes while preventing duplicates.
  - `Util/Ticker` emits 0.6 s OSRS ticks and accepts `ITickable` subscribers. Skills and time-based systems should subscribe here rather than relying on `Update` for logic ticks.
  - `World/PersistentSceneGate` centralises which scenes are allowed to spawn persistent services. Subscribe to `SceneEvaluationChanged` or call `ShouldSpawnInScene` so long-lived systems stay dormant on menu/login scenes.
- **Time & Daily State** (`Assets/Scripts/Core/Time`)
  - `DailyGameTimeService` caches the current UTC calendar day, survives scene loads, and raises `DayChanged` whenever the day rolls over so dailies, rotating shops, or quests can refresh in sync.
  - Use `DailyGameTimeService.ComposeDailySeed(ReadOnlySpan<int> contextHashes)` to build deterministic per-day RNG seeds. Pass in hashes for player IDs, node positions, etc., to keep rolls unique while still resetting each day.
  - Systems that need the service early should either wait for `GameManager.ServicesReady` or inherit from `ScenePersistentObject` so lifecycle matches the rest of the persistent catalog.
- **Saving** (`Assets/Scripts/Core/Save`)
  - `SaveManager` exposes `Register/Unregister/Save/Load` helpers plus JSON-backed persistence (sample data in `PlayerSave/save_data.json`). Components implement `ISaveable` or use helper save bridges (`SkillManager`, quests, pets, outfits, etc.).
- **Skills & Progression** (`Assets/Scripts/Skills`)
  - `SkillManager` maintains XP/levels per `SkillType`. Individual skills (Woodcutting, Mining, Fishing, Cooking, Combat styles, Beastmaster, Outfits) live under dedicated folders and rely on the shared `XpTable`/tick math.
  - Woodcutting demonstrates tick integration (`Woodcutting/Core/WoodcuttingSkill`), outfit tracking, pet bonuses, and inventory interactions.
  - Fishing/Cooking use ScriptableObject databases in `Assets/Resources` (`FishingDatabase`, `CookingDatabase`).
  - Woodcutting, Mining, Fishing, and Cooking now expose `EnableDebugLogging` toggles that gate verbose log output and ticker subscription traces. Toggle them via the in-game `AdminF2Menu` (F2) or in the inspector when diagnosing tick cadence, loot rolls, or state transitions. The fishing `BycatchManager` also defaults `useDailySeed` to true so bycatch rolls key off the shared daily seed—leave that enabled unless a feature needs session-based randomness.
  - Firemaking joins the core gathering suite. `Firemaking/Core/FiremakingSkill` drives ignition, bonfire fueling, Phoenix pet XP bonuses, and outfit rolls while `Firemaking/UI/FiremakingHUD` mirrors progress bars from skill events. Log definitions live under `Assets/Resources/Firemaking/Logs` as `FiremakingLogDefinition` assets.
  - Shared floating-text behaviour for gathering skills flows through `Skills/Common/GatheringFloatingTextService`. Use the helper for OSRS-range validation, popup cooldowns, and delayed XP to keep Firemaking, Woodcutting, Mining, and Fishing feedback consistent.
- **Combat** (`Assets/Scripts/Combat`, `Assets/Scripts/NPC/Combat`, `Assets/NPCCombatProfile`)
  - `CombatController`, `CombatMath`, and `CombatEnums` coordinate player combat ticks, max hit calculations, spell elements, and hitsplat visuals via `Resources/HitSplatLibrary`.
  - NPC combat uses `NpcAttackController`, projectile logic, and drop resolution (`Drops` folder). Pet-assisted combat hooks exist in `Pets`.
  - Attach `Combat/OnHitPoisonApplier` to weapons or projectiles when you need configurable poison procs. It consumes `PoisonConfig` assets, honours `requiresDamage`, and should be invoked alongside the combat hit confirmation pipeline.
- **Player Systems** (`Assets/Scripts/Player`)
  - `PlayerMover` persists across scenes, auto-walks gathering nodes, saves position ticks, and respects freeze or respawn locks while keeping autosaves fresh.
  - `PlayerCombatLoadout`, `PlayerCombatTarget`, and `PlayerHitpoints` aggregate combat stats, weapon-driven poison hooks, and hitpoint changes for UI and respawn systems.
  - `PlayerRespawnSystem` cooperates with `World/RespawnPoint`, clears buffs via `BuffTimerService`, and triggers screen fades/death jingle through `SoundManager`. `PlayerEat` handles 0.6 s food ticks for OSRS-style healing windows.
- **Magic System** (`Assets/Scripts/Magic`, `Assets/Resources/Spells`, `Assets/Prefabs/Spells`)
  - `SpellDefinition` ScriptableObjects set spell range, projectile speed, hit FX, max hit, elemental type, and optional freeze settings. Respect `loadOrder` for UI sorting, keep `requiredMagicLevel` aligned with `SkillManager`, and pair freeze toggles with `Status/FrozenStatusController` listeners.
  - Projectile prefabs (`FireProjectile`) and hit effect prefabs (`HitEffect`) live under `Assets/Prefabs/Spells`. Sprite VFX are in `Assets/Sprites/GFX/Spells` and should stay 64×64 transparent assets matching the ScriptableObject name.
  - Spells load from `Resources/Spells`, so keep asset names unique and consistent with prefabs/icons. New strike-tier spells should call `MagicUI.UpdateStrikeMaxHits` logic by following the naming convention (`*Strike`).
- **Inventory, Equipment & Items** (`Assets/Scripts/Inventory`, `Assets/Scripts/Items`)
  - `Inventory`, `ItemUseResolver`, `StackSplitDialog`, and `InventoryDropMenu` underpin the OSRS-style bag. Equipment data flows through `Equipment`, `EquipmentAggregator`, and slot UI scripts.
  - `Items/Consumables` and `Items/ItemCombatStats` define stat blocks, while `PlayerEat` and `Inventory/ItemUseResolver` translate consumables into heals or buffs.
  - Scriptable item data resides in `Assets/Item` and `Assets/Resources/Items`. Combination recipes live in `Resources/ItemCombinationDatabase`.
- **Economy** (`Assets/Scripts/Shop`, `Assets/Scripts/Bank`)
  - Shop UIs share fonts/settings with inventory, reference `Shop`/`ShopUI` scripts, and rely on item databases. Bank UI reuses the same font default.
  - `BankUI`, `BankDepositMenu`, and `BankWithdrawMenu` process deposits/withdrawals with inventory hooks, while `BankOpener` gates world interactions.
- **Dialogue & Quests** (`Assets/Scripts/Dialogue`, `Assets/Scripts/Quests`)
  - Dialogue data/manager/UI implement OSRS-style panels. Quests use `QuestManager` (saveable) with ScriptableObject quest definitions in `Resources/Quests`.
- **NPCs & World** (`Assets/Scripts/NPC`, `Assets/Scripts/World`)
  - NPC combat/movement live under `NPC/Combat`, `NPC/Movement`, and `NPC/Navigation`; interaction/UI wrappers route right-click menus and HUDs. `NpcFaction` powers faction-aware tests.
  - `World/SceneTransitionManager` now owns additive scene swaps, persistent-object callbacks, spawn point routing, and fade timing. Use `SceneTransitionInteractable` to trigger transitions, populate `SceneTransitionManager.NextSpawnPoint`, and keep persistent services registered via `IScenePersistent` so they receive unload/load callbacks.
  - `World/Minimap` & `MinimapMarker` render the overworld HUD, `PopupText`/`PopupTextPool` feed floating world text, and `Environment/FenceColliderFoot` supplies nav blockers for fence kits.
  - Lighter NPC prefabs should include `NpcKnockbackReceiver` alongside `NpcWanderer` so damage-driven knockback eases displacement without breaking wander bounds. Heavy or boss NPCs can disable or omit the receiver to stay rooted.
- **UI Layer** (`Assets/Scripts/UI`, `Assets/Scripts/Player`, `Assets/Scripts/Status`)
  - HUDs such as `HealthHUD`, merge timers, tab buttons, and combat/skill overlays expect LegacyRuntime fonts and OSRS layout cues. `UI/PersistentEventSystem` maintains input modules across scenes.
  - `UI/MagicUI` is a `PersistentSceneSingleton` that builds the spellbook grid from `Resources/Spells`, caches strike spells for max-hit syncing, and drives the active spell/last selected spell state consumed by `CombatController` and `PlayerCombatLoadout`.
  - `UI/InterfaceTabButtons` spawns the bottom-right OSRS tab strip (Quest, Inventory, Skills, Equipment, Attack Style, Magic). Let it toggle windows instead of duplicating button logic, and rely on `UIManager`'s auto-close behaviour for AttackStyle.
  - Buff tracking now runs through `UI/HUD/BuffHudManager` and its `BuffInfoBox`/`BuffTooltipController` prefabs. Query the singleton to refresh icons, handle expiry audio, or respond to `BuffEvents` updates.
- **Audio & Screen Fades** (`Assets/Scripts/Audio`, `Assets/Scripts/World`)
  - `Audio/SoundManager` centralises SFX/music playback and exposes the `SoundEffect` enum (e.g., death jingle). Register clips there and let `SceneTransitionManager`'s `EnsureSingleAudioListener` prevent duplicate listeners after scene swaps.
  - `World/ScreenFader` provides black fade in/out routines used by `SceneTransitionManager` and `PlayerRespawnSystem`. Reuse it for any other screen transitions so fade timing stays consistent.
- **Status Effects** (`Assets/Scripts/Status`)
  - `BuffTimerService` owns timed buffs (poison, freeze, antifire, stamina, etc.) and relays updates via `BuffTimerInstance`. Always raise effects through the static `BuffEvents` hub so combat, inventory, pets, and scripted encounters stay decoupled.
  - `BuffStateSaveBridge` snapshots active timers for persistence. Attach it to entities that must keep buffs between loads; legacy `PoisonSaveBridge` is deprecated and should remain disabled unless debugging old data.
  - Effect-specific controllers live under subfolders (`Status/Poison`, `Status/Freeze`, `Status/Antifire`) and handle combat mitigation, HUD sync, and ticker subscriptions.
  - `Status/Freeze/FrozenStatusController` pauses locomotion for targets with freeze buffs and expects spells that set `SpellDefinition.appliesFreeze` to raise buffs via `BuffEvents`.
- **Pets & Drops** (`Assets/Scripts/Pets`, `Assets/Scripts/Drops`)
  - Pets include drop systems, storage, level bars, and context menus. Drop tables combine scriptable entries with RNG helpers and tie into `NpcDropper` and `GroundItemSpawner`.
  - The Phoenix familiar now synergises with Firemaking (`Skills/Firemaking/Core/FiremakingSkill`). When active it adds passive XP bonuses, rolls 1/20 double XP procs, and emits floating text/audio via `PetDropSystem` helpers, so ensure pet IDs match (`"Phoenix"`).
- **Books & Lore** (`Assets/Scripts/Books`, `Assets/Resources/Books`)
  - `BookData` ScriptableObjects (use `\f` delimiters within `content`) drive lore pages while `BookProgressManager` tracks per-book page progress via `PersistentSceneSingleton`. `BookItemData` links inventory entries to the underlying book asset.

## Data & Assets
- **Resources**: Centralized assets (persistent prefab list, item databases, hit splats, cooking/fishing databases, pet drop tables, quest data, sprite atlases).
- **Prefabs & Scenes**: Gameplay scenes under `Assets/Scenes` with associated navmeshes. Shared UI/combat/pet prefabs live in `Assets/Prefabs` and subfolders.
- **Sprites & Tiles**: Sprites under `Assets/Sprites`, `TileAssets`, and `WorldPalette`. Maintain 64×64 resolution with transparent backgrounds.
- **Spells**: Spell ScriptableObjects reside in `Assets/Resources/Spells`, with corresponding projectile and hit-effect prefabs under `Assets/Prefabs/Spells` and sprite sheets in `Assets/Sprites/GFX/Spells`. Keep naming aligned so `MagicUI` can auto-load visuals.
- **Buff Icons & Status Configs**: HUD sprites reside in `Assets/Resources/UI/Buffs` while status configs (poison defaults, etc.) live in `Assets/Resources/Status`. Align icon IDs with `BuffTimerDefinition.iconId` and keep `PoisonConfig.Id` stable for saves.
- **Firemaking Data**: Log ScriptableObjects live under `Assets/Resources/Firemaking/Logs`. Populate new entries there when introducing log tiers, bonfire lifetimes, ashes, or Phoenix XP hooks so `FiremakingSkill` can load them automatically.
- **Audio**: The central `Assets/Scripts/Audio/SoundManager` component exposes the `SoundEffect` enum for UI/gameplay cues (including death jingles). Register new clips there when wiring fresh feedback.
- **Sprite Depth**: `Assets/Scripts/Util/SpriteDepth` offsets render order using the object's world Y so sprites overlap consistently in the 2.5D stack.
- **Book Content**: Lore assets live in `Assets/Resources/Books`. Use `\f` within `BookData.content` to break pages and avoid relying on the obsolete `pages` list.

## Build & Editor Setup
- **Unity Version**: Open and build the project with Unity **6000.2.3f1** (Unity 6.2, DX12). Earlier editors risk serialization drift on URP assets and input bindings.
- **Initial Setup**:
  1. Clone the repository and open it through Unity Hub, targeting the `My-project` folder.
  2. Load a gameplay scene such as `Assets/Scenes/OverWorld.unity` to ensure persistent singletons spawn correctly.
  3. Use **File > Build Settings** to configure platform targets; URP assets are already configured for desktop. Keep the default color space and render pipeline settings unless a task explicitly requests changes.
- **Play Mode**: Enter Play Mode from the overworld or appropriate test scene. Persistent systems (`PersistentObjects.asset`) will bootstrap automatically via `GameManager`.
- **Standalone Builds**: Build via **File > Build Settings**. Verify that persistent bootstrap scenes are included and that the login/autosave flow continues to function in the player build.

## Input & Rebinding
- The project relies on the Unity Input System asset `Assets/InputSystem_Actions.inputactions`.
- The **Player** action map exposes `Move`, `Interact`, `Prospect`, `Cancel`, and `OpenMenu`. Share these bindings across gameplay, NPC interaction, and UI to keep inputs consistent.
- When introducing new bindings:
  1. Update the action asset via the Input Actions editor and apply changes.
  2. Ensure any `PlayerInput` components reference the updated asset.
  3. Resolve actions through `Core/Input/InputActionResolver.Resolve`, optionally exposing an `InputActionReference` in scripts for prefab overrides so the lifecycle stays centralised.
- Gathering systems, NPC interactables, and UI toggles should avoid hardcoded keycodes—always query the shared action map.

## Code Conventions
- Unity C# only. Scripts live under `Assets/Scripts/...` with folder-aligned namespaces (e.g., `namespace Skills.Woodcutting`).
- Use `[SerializeField]` to expose private inspector references, add `[DisallowMultipleComponent]` where duplicates would break behaviour, and wire events for decoupled communication.
- Tick-sensitive systems prefer `ITickable` + `Ticker` over raw `Update`. Use coroutines sparingly and clean up subscriptions in `OnDisable`/`OnDestroy`.
- Follow the `enableDebugLogging` pattern from the gathering skills when adding verbose logging so toggles can be driven from `AdminF2Menu` and hook into `TickedSkillBehaviour.LogTickerSubscription`.
- UI text defaults to `LegacyRuntime.ttf` via `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` unless a serialized font override is provided.
- Follow the existing commenting style (XML summaries and inline comments explaining intent) and keep logic modular for future skills/items.
- For assets, maintain 64×64 resolution, transparent backgrounds, and URP-compatible import settings.

## Working With Systems
- **Input**: Prefer serialized `InputActionReference` fields. Resolve actions through `InputActionResolver.Resolve` so they auto-enable/disable. New interactables should plug into the shared Player action map (`Move`, `Interact`, `Prospect`, `Cancel`, `OpenMenu`).
- **Skills**: Register new skills in `SkillType`, supply XP tables, hook into `SkillManager`, and consider pets/outfits/tick cadence. Leverage shared drop/pet helpers where relevant.
- **Daily Rolls & Calendar**: Query `DailyGameTimeService.CurrentUtcDay` for the cached day and subscribe to `DailyGameTimeService.DayChanged` when a system needs to refresh on rollover. Compose deterministic RNG with `DailyGameTimeService.ComposeDailySeed`, optionally passing context hashes (player ID, node hash, roll index, etc.) so per-entity rolls stay unique but reset each UTC day.
- **Saving**: Register/deregister save participants with `SaveManager`. Store unique keys (usually lowercase skill IDs) and ensure loads occur during `Awake/OnEnable` to populate runtime state.
- **Combat**: Route timing through `CombatController`/`Ticker`, reuse `HitSplatLibrary` for visuals, and respect `MagicUI` spell range queries. Pet and NPC hooks already exist—prefer extending those before introducing new combat entry points.
- **UI**: Add new OSRS-style panels under `Assets/Scripts/UI` or the appropriate feature folder. Use existing tab/button controllers to avoid duplicating input logic.
- **Data**: Place ScriptableObject databases in the relevant `Assets/Resources/...` subfolder so runtime lookups via `Resources.Load` continue to work.
- **Status Effects & Buff Timers**: Build a `BuffTimerDefinition` and `BuffEventContext` when applying effects from combat, inventory, or scripted hooks. Broadcast through `BuffEvents.RaiseBuffApplied`/`RaiseBuffRefreshed` (and `RaiseBuffRemoved` when clearing) so `BuffTimerService` can manage durations, HUD warnings, and recurrence. Query active buffs via `TryGetBuff`/`GetBuffsFor`, and persist long-lived timers with `BuffStateSaveBridge`.
- **Consumable & Equipment Buffs**: Configure `ItemData.buffEffects` to mirror OSRS potions/prayers. `Inventory/ItemUseResolver` automatically translates consume/equip/unequip events into the appropriate buff broadcasts and removes equipment buffs when items are unequipped.
- **Books**: Use `BookProgressManager.Instance` to track the current page per `BookData` ID. Load content with `Resources.Load<BookData>` and wire `BookItemData` references on inventory items so book UI panels resolve the correct lore entry and restore saved progress.

## Shared Gathering Utilities
- **Location**: `Assets/Scripts/Skills/Common` collects the cross-skill helpers used by Fishing, Mining, Woodcutting, and future gathering content.
- `GatheringController<TSkill, TNode>` drives interaction range checks, cancel conditions, and tick-aware start logic. Always subclass it for new gathering controllers so pointer/UI throttling, quick-action hotkeys, and movement cancellation match the rest of the project.
  - Controllers now auto-walk the player into interaction range when `autoMoveIntoRange` is enabled (default). Override `AllowAutoMoveToNodes` or tweak `autoMoveStopBuffer` if a future skill needs bespoke approach behaviour.
- `GatheringRewardProcessor` standardises how resource rewards, XP, outfit rolls, and floating text are resolved. Build a `GatheringRewardContext` and run it through the processor so outfit hooks, XP multipliers, and pet assistance are honoured automatically.
- `GatheringRewardContextBuilder` composes the shared `GatheringRewardContext` payload and the OSRS-style success roll. Supply the per-skill reward data plus lambdas for quest, pet, or outfit hooks so you don't duplicate boilerplate when wiring future gathering skills.
- `GatheringFloatingTextService` centralises distance checks, cooldowns, and delayed XP popups for all gathering popups. Request immediate/delayed feedback through the static helpers instead of calling `FloatingText` directly so skills stay consistent.
- `GatheringInventoryHelper` (new) owns the shared `Resources.Load` cache for `ItemData` lookups and the pet overflow capacity rules. When adding or updating gathering skills call `GatheringInventoryHelper.CanAcceptGatheredItem` instead of duplicating inventory checks. Pass the per-skill dictionary field by reference so the helper can bind it to the shared cache, and supply the double-drop pet ID ("Beaver", "Heron", "Rock Golem", etc.) to keep bonus rolls consistent.
- When a pet doubles resource output the helper will automatically probe the pet's `PetStorage` inventory. Ensure any new pets that offer a similar bonus have a matching `id` string and an attached `PetStorage` component so overflow routing continues to work.
- `Skills/Common/UI/GatheringSkillHudBase` now centralises the coroutine-based retry logic for gathering HUDs. Derive new progress UIs (or refactors of Fishing, Mining, Woodcutting) from this base instead of copying the FindObjectOfType polling code.
- `Skills/Outfits/SkillingOutfitRewarder` centralises the 1-in-2500 skilling outfit rolls. Pass the per-skill `SkillingOutfitProgress`, inventory, bank hook, toast strings, and RNG delegate so debug logging and sanity checks remain consistent across every gathering skill.
- `Skills/Common/SkillingPetRewarder` wraps `PetDropSystem.TryRollPet` for gathering skills. Supply the source ID, `SkillManager`, best available anchor, and optional 1-in-N override so pet rolls stay consistent.

## Testing & Validation
- Play mode and edit mode tests live in `Assets/Tests` (currently NUnit-based unit tests like `CookingSkillTests`, `NpcFactionTests`, `NpcElementalModifierTests`). Run them through the Unity Test Runner or an equivalent CLI invocation (`Unity -runTests`) whenever you touch gameplay logic.
- Validate scenes by loading `Assets/Scenes/OverWorld.unity` and ensuring persistent objects (`PersistentObjects.asset`) spawn correctly.
- Follow the login flow regression checklist in `PlaytestNotes_LoginFlow.md` after touching authentication, scene loading, autosave, or player placement logic. Cover both returning-account and new-account scenarios and confirm the autosave loop resumes post-login.
- Automated save recovery coverage exists in the playmode tests `LoadGlobalStore_RecoversFromInterruptedSwap`, `LoadGlobalStore_RecoversWhenLiveFileMissing`, and `LoadGlobalStore_RecoversFromCorruptedLiveFile`; keep them green when adjusting persistence.

## Workflow Notes
- Do **not** rename or delete existing assets/scenes unless explicitly requested. Extend systems via new components or ScriptableObjects.
- When adding scripts, keep them under `Assets/Scripts/...` within the most specific subsystem folder (e.g., `Assets/Scripts/Skills/Fishing`).
- Maintain compatibility with the existing autosave loop, pet systems, and tick timing. New features should clean up event subscriptions and coroutines to avoid lingering references across scene loads.
- Prefer integration with existing managers (GameManager, SkillManager, SaveManager, ItemDatabase) before introducing new global singletons.
- Use the in-game `AdminF2Menu` debug panel (F2) to toggle skill logging, pet roll debugging, bycatch debug spam, and skilling outfit odds while testing. Wire any new debug switches into this menu to keep QA tooling consistent.

- Added `PersistentSceneSingleton` helper under `Assets/Scripts/World` plus `PersistentSceneGate` for scene-gated lifecycle management of long-lived services.
