#if UNITY_EDITOR
using System.Collections.Generic;
using Inventory;
using MyGame.Drops;
using UnityEditor;
using UnityEngine;

namespace Drops.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="DropTable"/> providing testing utilities.
    /// </summary>
    [CustomEditor(typeof(DropTable))]
    public class DropTableEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("tableName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stopOnUnique"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rollsPerKill"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mainAffectedByLuck"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rareDropTable"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rdtPlaceholder"));

            DrawSection(serializedObject.FindProperty("uniques"), "Uniques");
            DrawSection(serializedObject.FindProperty("mainTable"), "Main Table");
            DrawSection(serializedObject.FindProperty("tertiaries"), "Tertiaries");

            serializedObject.ApplyModifiedProperties();

            Validate((DropTable)target);

            if (GUILayout.Button("Test Roll"))
            {
                TestRoll((DropTable)target);
            }
        }

        private void DrawSection(SerializedProperty prop, string label)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(prop, true);
        }

        private void Validate(DropTable table)
        {
            foreach (var u in table.uniques)
            {
                if (u.denominator < 1)
                {
                    EditorGUILayout.HelpBox($"Unique {u.item?.name ?? "null"} has invalid denominator", MessageType.Warning);
                }
            }

            foreach (var e in table.mainTable)
            {
                if (e.weight <= 0 && !e.alwaysDrop)
                {
                    EditorGUILayout.HelpBox($"Main entry {e.item?.name ?? "RDT"} has non-positive weight", MessageType.Warning);
                }
            }

            foreach (var t in table.tertiaries)
            {
                if (t.denominator < 1)
                {
                    EditorGUILayout.HelpBox($"Tertiary {t.item?.name ?? "null"} has invalid denominator", MessageType.Warning);
                }
            }
        }

        private void TestRoll(DropTable table)
        {
            const int simulations = 10000;
            var totals = new Dictionary<ItemData, int>();
            for (int i = 0; i < simulations; i++)
            {
                var drops = DropResolver.Resolve(table, 1f);
                foreach (var drop in drops)
                {
                    if (totals.TryGetValue(drop.item, out int current))
                    {
                        totals[drop.item] = current + drop.quantity;
                    }
                    else
                    {
                        totals.Add(drop.item, drop.quantity);
                    }
                }
            }

            foreach (var kv in totals)
            {
                Debug.Log($"{kv.Key?.name ?? "null"}: {kv.Value} total over {simulations} kills");
            }
        }
    }
}
#endif
