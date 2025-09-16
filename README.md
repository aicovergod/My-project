# My-project

## Overview
My-project is a Unity-based role-playing game focused on skill-based progression and an interactive economy. The project aims to deliver a persistent world where players train skills, interact with shops, and explore content reminiscent of classic MMORPGs.

## Unity Version
This project currently targets **Unity 6000.2.3f1**.

## Major Systems
- **Saving System** – Provides persistent player data using components like `SaveManager` and player-specific save bridges.
- **Skill System** – A modular framework for training skills such as woodcutting and mining through `SkillManager` and skill-specific modules.
- **Shop System** – Supports buying and selling items via `Shop` and `ShopUI` components.

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

