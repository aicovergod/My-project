#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Items;
using Inventory;

namespace Items.Editor
{
    /// <summary>
    /// Editor utility that migrates legacy combat stats on <see cref="ItemData"/> assets
    /// to the new <see cref="ItemCombatStats"/> structure.
    /// </summary>
    public class ItemCombatMigration : EditorWindow
    {
        private bool splitDefence;

        [MenuItem("Tools/Item Combat Migration")]
        public static void Open()
        {
            GetWindow<ItemCombatMigration>(true, "Item Combat Migration");
        }

        private void OnGUI()
        {
            GUILayout.Label("Migrate legacy combat fields into ItemCombatStats", EditorStyles.wordWrappedLabel);
            splitDefence = EditorGUILayout.Toggle("Split Defence Bonus Across Types", splitDefence);
            if (GUILayout.Button("Migrate All"))
                MigrateAll();
        }

        private void MigrateAll()
        {
            var guids = AssetDatabase.FindAssets("t:ItemData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (item == null)
                    continue;

                Undo.RecordObject(item, "Combat Migration");
                var stats = item.combat;
                if (item.attackBonus != 0)
                    stats.Attack = item.attackBonus;
                if (item.strengthBonus != 0)
                    stats.Strength = item.strengthBonus;
                if (item.rangeBonus != 0)
                    stats.Range = item.rangeBonus;
                if (item.magicBonus != 0)
                    stats.Magic = item.magicBonus;
                if (item.attackSpeed != 0)
                    stats.AttackSpeedTicks = item.attackSpeed;

                if (item.defenceBonus != 0)
                {
                    if (splitDefence)
                    {
                        int share = Mathf.RoundToInt(item.defenceBonus / 3f);
                        stats.MeleeDefence += share;
                        stats.RangeDefence += share;
                        stats.MagicDefence += item.defenceBonus - 2 * share;
                    }
                    else
                    {
                        stats.MeleeDefence += item.defenceBonus;
                    }
                }

                stats.MeleeDefence += item.meleeDefenceBonus;
                stats.RangeDefence += item.rangedDefenceBonus;
                stats.MagicDefence += item.magicDefenceBonus;

                item.combat = stats;
                EditorUtility.SetDirty(item);
                Debug.Log($"Migrated {item.name} ({path})");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Item combat migration completed.");
        }
    }
}
#endif
