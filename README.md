# My-project

## Overview
My-project is a Unity-based role-playing game focused on skill-based progression and an interactive economy. The project aims to deliver a persistent world where players train skills, interact with shops, and explore content reminiscent of classic MMORPGs.

## Unity Version
This project currently targets **Unity 6000.2.3f1**.

## Major Systems
- **Saving System** – Provides persistent player data using components like `SaveManager` and player-specific save bridges.
- **Skill System** – A modular framework for training skills such as woodcutting and mining through `SkillManager` and skill-specific modules.
- **Shop System** – Supports buying and selling items via `Shop` and `ShopUI` components.

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

