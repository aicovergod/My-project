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
- **Saving** (`Assets/Scripts/Core/Save`)
  - `SaveManager` exposes `Register/Unregister/Save/Load` helpers plus JSON-backed persistence (sample data in `PlayerSave/save_data.json`). Components implement `ISaveable` or use helper save bridges (`SkillManager`, quests, pets, outfits, etc.).
- **Skills & Progression** (`Assets/Scripts/Skills`)
  - `SkillManager` maintains XP/levels per `SkillType`. Individual skills (Woodcutting, Mining, Fishing, Cooking, Combat styles, Beastmaster, Outfits) live under dedicated folders and rely on the shared `XpTable`/tick math.
  - Woodcutting demonstrates tick integration (`Woodcutting/Core/WoodcuttingSkill`), outfit tracking, pet bonuses, and inventory interactions.
  - Fishing/Cooking use ScriptableObject databases in `Assets/Resources` (`FishingDatabase`, `CookingDatabase`).
- **Combat** (`Assets/Scripts/Combat`, `Assets/Scripts/NPC/Combat`, `Assets/NPCCombatProfile`)
  - `CombatController`, `CombatMath`, and `CombatEnums` coordinate player combat ticks, max hit calculations, spell elements, and hitsplat visuals via `Resources/HitSplatLibrary`.
  - NPC combat uses `NpcAttackController`, projectile logic, and drop resolution (`Drops` folder). Pet-assisted combat hooks exist in `Pets`.
- **Inventory, Equipment & Items** (`Assets/Scripts/Inventory`, `Assets/Scripts/Items`)
  - Inventory UI defaults to LegacyRuntime, supports stack splitting, drag/drop, ground loot via `Drops/GroundItemSpawner`, and equipment synergy through `EquipmentAggregator`.
  - Scriptable item data resides in `Assets/Item` and `Assets/Resources/Items`. Combination recipes live in `Resources/ItemCombinationDatabase`.
- **Economy** (`Assets/Scripts/Shop`, `Assets/Scripts/Bank`)
  - Shop UIs share fonts/settings with inventory, reference `Shop`/`ShopUI` scripts, and rely on item databases. Bank UI reuses the same font default.
- **Dialogue & Quests** (`Assets/Scripts/Dialogue`, `Assets/Scripts/Quests`)
  - Dialogue data/manager/UI implement OSRS-style panels. Quests use `QuestManager` (saveable) with ScriptableObject quest definitions in `Resources/Quests`.
- **NPCs & World** (`Assets/Scripts/NPC`, `Assets/Scripts/World`)
  - NPC interaction, navigation, and movement wrappers live here. Minimap, doors, scene transitions, and respawns sit under `World`.
- **UI Layer** (`Assets/Scripts/UI`, `Assets/Scripts/Player`, `Assets/Scripts/Status`)
  - HUDs such as `HealthHUD`, merge timers, tab buttons, and combat/skill overlays expect LegacyRuntime fonts and OSRS layout cues. `UI/PersistentEventSystem` maintains input modules across scenes.
- **Pets & Drops** (`Assets/Scripts/Pets`, `Assets/Scripts/Drops`)
  - Pets include drop systems, storage, level bars, and context menus. Drop tables combine scriptable entries with RNG helpers and tie into `NpcDropper` and `GroundItemSpawner`.

## Data & Assets
- **Resources**: Centralized assets (persistent prefab list, item databases, hit splats, cooking/fishing databases, pet drop tables, quest data, sprite atlases).
- **Prefabs & Scenes**: Gameplay scenes under `Assets/Scenes` with associated navmeshes. Shared UI/combat/pet prefabs live in `Assets/Prefabs` and subfolders.
- **Sprites & Tiles**: Sprites under `Assets/Sprites`, `TileAssets`, and `WorldPalette`. Maintain 64×64 resolution with transparent backgrounds.

## Code Conventions
- Unity C# only. Scripts live under `Assets/Scripts/...` with folder-aligned namespaces (e.g., `namespace Skills.Woodcutting`).
- Use `[SerializeField]` to expose private inspector references, add `[DisallowMultipleComponent]` where duplicates would break behaviour, and wire events for decoupled communication.
- Tick-sensitive systems prefer `ITickable` + `Ticker` over raw `Update`. Use coroutines sparingly and clean up subscriptions in `OnDisable`/`OnDestroy`.
- UI text defaults to `LegacyRuntime.ttf` via `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` unless a serialized font override is provided.
- Follow the existing commenting style (XML summaries and inline comments explaining intent) and keep logic modular for future skills/items.
- For assets, maintain 64×64 resolution, transparent backgrounds, and URP-compatible import settings.

## Working With Systems
- **Input**: Prefer serialized `InputActionReference` fields. Resolve actions through `InputActionResolver.Resolve` so they auto-enable/disable. New interactables should plug into the shared Player action map (`Move`, `Interact`, `Prospect`, `Cancel`, `OpenMenu`).
- **Skills**: Register new skills in `SkillType`, supply XP tables, hook into `SkillManager`, and consider pets/outfits/tick cadence. Leverage shared drop/pet helpers where relevant.
- **Saving**: Register/deregister save participants with `SaveManager`. Store unique keys (usually lowercase skill IDs) and ensure loads occur during `Awake/OnEnable` to populate runtime state.
- **Combat**: Route timing through `CombatController`/`Ticker`, reuse `HitSplatLibrary` for visuals, and respect `MagicUI` spell range queries. Pet and NPC hooks already exist—prefer extending those before introducing new combat entry points.
- **UI**: Add new OSRS-style panels under `Assets/Scripts/UI` or the appropriate feature folder. Use existing tab/button controllers to avoid duplicating input logic.
- **Data**: Place ScriptableObject databases in the relevant `Assets/Resources/...` subfolder so runtime lookups via `Resources.Load` continue to work.

## Shared Gathering Utilities
- **Location**: `Assets/Scripts/Skills/Common` collects the cross-skill helpers used by Fishing, Mining, Woodcutting, and future gathering content.
- `GatheringController<TSkill, TNode>` drives interaction range checks, cancel conditions, and tick-aware start logic. Always subclass it for new gathering controllers so pointer/UI throttling, quick-action hotkeys, and movement cancellation match the rest of the project.
  - Controllers now auto-walk the player into interaction range when `autoMoveIntoRange` is enabled (default). Override `AllowAutoMoveToNodes` or tweak `autoMoveStopBuffer` if a future skill needs bespoke approach behaviour.
- `GatheringRewardProcessor` standardises how resource rewards, XP, outfit rolls, and floating text are resolved. Build a `GatheringRewardContext` and run it through the processor so outfit hooks, XP multipliers, and pet assistance are honoured automatically.
- `GatheringRewardContextBuilder` composes the shared `GatheringRewardContext` payload and the OSRS-style success roll. Supply the per-skill reward data plus lambdas for quest, pet, or outfit hooks so you don't duplicate boilerplate when wiring future gathering skills.
- `GatheringInventoryHelper` (new) owns the shared `Resources.Load` cache for `ItemData` lookups and the pet overflow capacity rules. When adding or updating gathering skills call `GatheringInventoryHelper.CanAcceptGatheredItem` instead of duplicating inventory checks. Pass the per-skill dictionary field by reference so the helper can bind it to the shared cache, and supply the double-drop pet ID ("Beaver", "Heron", "Rock Golem", etc.) to keep bonus rolls consistent.
- When a pet doubles resource output the helper will automatically probe the pet's `PetStorage` inventory. Ensure any new pets that offer a similar bonus have a matching `id` string and an attached `PetStorage` component so overflow routing continues to work.
- `Skills/Outfits/SkillingOutfitRewarder` centralises the 1-in-2500 skilling outfit rolls. Pass the per-skill `SkillingOutfitProgress`, inventory, bank hook, toast strings, and RNG delegate so debug logging and sanity checks remain consistent across every gathering skill.
- `Skills/Common/SkillingPetRewarder` wraps `PetDropSystem.TryRollPet` for gathering skills. Supply the source ID, `SkillManager`, best available anchor, and optional 1-in-N override so pet rolls stay consistent.

## Testing & Validation
- Play mode and edit mode tests live in `Assets/Tests` (currently NUnit-based unit tests like `CookingSkillTests`, `NpcFactionTests`, `NpcElementalModifierTests`). Run them through the Unity Test Runner or an equivalent CLI invocation (`Unity -runTests`) whenever you touch gameplay logic.
- Validate scenes by loading `Assets/Scenes/OverWorld.unity` and ensuring persistent objects (`PersistentObjects.asset`) spawn correctly.

## Workflow Notes
- Do **not** rename or delete existing assets/scenes unless explicitly requested. Extend systems via new components or ScriptableObjects.
- When adding scripts, keep them under `Assets/Scripts/...` within the most specific subsystem folder (e.g., `Assets/Scripts/Skills/Fishing`).
- Maintain compatibility with the existing autosave loop, pet systems, and tick timing. New features should clean up event subscriptions and coroutines to avoid lingering references across scene loads.
- Prefer integration with existing managers (GameManager, SkillManager, SaveManager, ItemDatabase) before introducing new global singletons.
