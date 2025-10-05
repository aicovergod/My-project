# Session Log
This file is auto-updated by CI on every push. Times shown are Europe/London.

<!-- commit:08615e5d0748696fd6dce6bccdcaeeb0e7359a91 -->
## 2025-10-05T15:26:44+01:00 — Merge pull request #1002 from aicovergod/codex/add-rangestrength-field-to-itemcombatstats

- Author: aicovergod <lewisshuffle136@gmail.com>
- Changed files (0): —
- Diff: 31 ++ / 6 --
- Notes:
  Add ranged strength stat and propagate through combat
---
<!-- commit:19f834834368fef1b0905b385c7ac61c3e409d6e -->
## 2025-10-05T15:26:37+01:00 — Update SESSION_LOG [ci]

- Author: session-log-bot <actions@github.com>
- Changed files (1): docs/SESSION_LOG.md
- Diff: 9 ++ / 0 --
- Notes:
  —
---
<!-- commit:fba63de6a89a0927830728336e1ee27570453ef6 -->
## 2025-10-05T15:26:26+01:00 — Add ranged strength stat and propagate through combat

- Author: aicovergod <lewisshuffle136@gmail.com>
- Changed files (8): Assets/Scripts/Combat/CombatController.cs, Assets/Scripts/Combat/CombatantStats.cs, Assets/Scripts/Combat/Ranged/RangedCombatController.cs, Assets/Scripts/Equipment/EquipmentAggregator.cs, Assets/Scripts/Items/ItemCombatStats.cs, Assets/Scripts/NPC/Combat/BaseNpcCombat.cs, Assets/Scripts/Pets/PetCombatController.cs, Assets/Scripts/Pets/PetServiceAdapter.cs
- Diff: 22 ++ / 6 --
- Notes:
  —
---
<!-- commit:42490cfc2bc3ce160a7b0e2a909764686d806202 -->
## 2025-10-05T14:59:30+01:00 — bows

- Author: aicovergod <lewisshuffle136@gmail.com>
- Changed files (100): Assets/Prefabs/MainScriptObjects/Player.prefab, Assets/Resources/Item/Dragon Axe.asset, Assets/Resources/Item/Magic Shortbow.asset, Assets/Resources/Item/Magic Shortbow.asset.meta, Assets/Resources/Item/Maple Shortbow.asset, Assets/Resources/Item/Maple Shortbow.asset.meta, Assets/Resources/Item/Oak Shortbow.asset, Assets/Resources/Item/Oak Shortbow.asset.meta, Assets/Resources/Item/Shortbow.asset, Assets/Resources/Item/Shortbow.asset.meta, Assets/Resources/Item/Willow Shortbow.asset, Assets/Resources/Item/Willow Shortbow.asset.meta, Assets/Resources/Item/Yew Shortbow.asset, Assets/Resources/Item/Yew Shortbow.asset.meta, Assets/Scripts/Combat/Ranged.meta, Assets/Scripts/Combat/Ranged/AmmunitionData.cs.meta, Assets/Scripts/Combat/Ranged/ChinchompaExplosionEffect.cs.meta, Assets/Scripts/Combat/Ranged/IRangedStatModifierProvider.cs.meta, Assets/Scripts/Combat/Ranged/RangedCombatController.cs.meta, Assets/Scripts/Combat/Ranged/RangedProjectile.cs.meta, Assets/Scripts/Combat/Ranged/RangedSpecialEffect.cs.meta, Assets/Scripts/Combat/Ranged/RangedWeaponData.cs.meta, Assets/Sprites/Fletching.meta, Assets/Sprites/Fletching/Shortbow.meta, Assets/Sprites/Fletching/Shortbow/Magic Shortbow (u).png, Assets/Sprites/Fletching/Shortbow/Magic Shortbow (u).png.meta, Assets/Sprites/Fletching/Shortbow/Maple Shortbow (u).png, Assets/Sprites/Fletching/Shortbow/Maple Shortbow (u).png.meta, Assets/Sprites/Fletching/Shortbow/Oak Shortbow (u).png, Assets/Sprites/Fletching/Shortbow/Oak Shortbow (u).png.meta, Assets/Sprites/Fletching/Shortbow/Shortbow (u).png, Assets/Sprites/Fletching/Shortbow/Shortbow (u).png.meta, Assets/Sprites/Fletching/Shortbow/Willow Shortbow (u).png, Assets/Sprites/Fletching/Shortbow/Willow Shortbow (u).png.meta, Assets/Sprites/Fletching/Shortbow/Yew Shortbow (u).png, Assets/Sprites/Fletching/Shortbow/Yew Shortbow (u).png.meta, Assets/Sprites/Ranged Weapons.meta, Assets/Sprites/Ranged Weapons/Ammunition.meta, Assets/Sprites/Ranged Weapons/Ammunition/Adamant_Arrow_Spritesheet.png, Assets/Sprites/Ranged Weapons/Ammunition/Adamant_Arrow_Spritesheet.png.meta, Assets/Sprites/Ranged Weapons/Ammunition/Black_Arrow_Spritesheet.png, Assets/Sprites/Ranged Weapons/Ammunition/Black_Arrow_Spritesheet.png.meta, Assets/Sprites/Ranged Weapons/Ammunition/Bronze_Arrows.png, Assets/Sprites/Ranged Weapons/Ammunition/Bronze_Arrows.png.meta, Assets/Sprites/Ranged Weapons/Ammunition/Iron_Arrow_Spritesheet.png, Assets/Sprites/Ranged Weapons/Ammunition/Iron_Arrow_Spritesheet.png.meta, Assets/Sprites/Ranged Weapons/Ammunition/Mithril_Arrow_Spritesheet.png, Assets/Sprites/Ranged Weapons/Ammunition/Mithril_Arrow_Spritesheet.png.meta, Assets/Sprites/Ranged Weapons/Ammunition/Orichalcum_Arrow_Spritesheet.png, Assets/Sprites/Ranged Weapons/Ammunition/Orichalcum_Arrow_Spritesheet.png.meta, Assets/Sprites/Ranged Weapons/Ammunition/Rune_Arrow_Spritesheet.png, Assets/Sprites/Ranged Weapons/Ammunition/Rune_Arrow_Spritesheet.png.meta, Assets/Sprites/Ranged Weapons/Ammunition/Steel_Arrow_Spritesheet.png, Assets/Sprites/Ranged Weapons/Ammunition/Steel_Arrow_Spritesheet.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Adamant_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Adamant_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Black_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Black_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Bronze_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Bronze_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Iron_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Iron_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Mithril_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Mithril_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Orichalcum_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Orichalcum_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Adamant_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Adamant_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Black_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Black_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Bronze_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Bronze_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Iron_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Iron_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Mithril_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Mithril_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Orichalcum_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Orichalcum_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Rune_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Rune_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Steel_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Poison_Steel_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Rune_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Rune_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Projectile Sprites/Steel_Arrow_Projectile.png, Assets/Sprites/Ranged Weapons/Projectile Sprites/Steel_Arrow_Projectile.png.meta, Assets/Sprites/Ranged Weapons/Shortbow.meta, Assets/Sprites/Ranged Weapons/Shortbow/Magic Shortbow.png, Assets/Sprites/Ranged Weapons/Shortbow/Magic Shortbow.png.meta, Assets/Sprites/Ranged Weapons/Shortbow/Maple Shortbow.png, Assets/Sprites/Ranged Weapons/Shortbow/Maple Shortbow.png.meta, Assets/Sprites/Ranged Weapons/Shortbow/Oak Shortbow.png, Assets/Sprites/Ranged Weapons/Shortbow/Oak Shortbow.png.meta, Assets/Sprites/Ranged Weapons/Shortbow/Shortbow.png, Assets/Sprites/Ranged Weapons/Shortbow/Shortbow.png.meta, Assets/Sprites/Ranged Weapons/Shortbow/Willow Shortbow.png, Assets/Sprites/Ranged Weapons/Shortbow/Willow Shortbow.png.meta, Assets/Sprites/Ranged Weapons/Shortbow/Yew Shortbow.png, Assets/Sprites/Ranged Weapons/Shortbow/Yew Shortbow.png.meta
- Diff: 12118 ++ / 1 --
- Notes:
  —
---
<!-- commit:6c53d967295c6d98473a3f6163d11173c2aebc96 -->
## 2025-10-05T14:58:13+01:00 — Merge pull request #1001 from aicovergod/codex/fix-codex-connector-bug

- Author: aicovergod <lewisshuffle136@gmail.com>
- Changed files (0): —
- Diff: 49 ++ / 11 --
- Notes:
  Prevent duplicate session log entries
---
<!-- commit:9c7e5178062c39a004f60074826e9a82d79597a1 -->
## 2025-10-05T14:58:05+01:00 — Update SESSION_LOG [ci]

- Author: session-log-bot <actions@github.com>
- Changed files (1): docs/SESSION_LOG.md
- Diff: 9 ++ / 0 --
- Notes:
  —
---
<!-- commit:933b2438fea3486db1f88705f1d8d1104fda30bc -->
## 2025-10-05T14:57:51+01:00 — Prevent duplicate session log entries

- Author: aicovergod <lewisshuffle136@gmail.com>
- Changed files (2): docs/SESSION_LOG.md, tools/ci_session_logger.py
- Diff: 40 ++ / 11 --
- Notes:
  —
---
<!-- commit:147f1af9f7ab88bb2f80c47c5ed715b2dfe4b890 -->
## 2025-10-05T13:45:43+01:00 — Update SESSION_LOG [ci]

- Author: session-log-bot <actions@github.com>
- Changed files (1): docs/SESSION_LOG.md
- Diff: 24 ++ / 0 --
- Notes:
  —
---
## 2025-10-05T13:45:32+01:00 — Merge pull request #1000 from aicovergod/codex/add-server-side-session-logger

- Author: aicovergod <lewisshuffle136@gmail.com>
- Changed files (0): —
- Diff: 174 ++ / 0 --
- Notes:
  Add automated session logging workflow
---
## 2025-10-05T13:45:21+01:00 — Update SESSION_LOG [ci]

- Author: session-log-bot <actions@github.com>
- Changed files (1): docs/SESSION_LOG.md
- Diff: 9 ++ / 0 --
- Notes:
  —
---
## 2025-10-05T13:45:06+01:00 — Add automated session logging workflow

- Author: aicovergod <lewisshuffle136@gmail.com>
- Changed files (3): .github/workflows/session-log.yml, docs/SESSION_LOG.md, tools/ci_session_logger.py
- Diff: 165 ++ / 0 --
- Notes:
  —
---
