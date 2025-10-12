using UnityEditor;
using UnityEngine;

namespace Inventory.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="ItemData"/> assets that groups the various
    /// skilling bonus multipliers into a single foldout. Designers can collapse
    /// the section when working on unrelated data, keeping the inspector tidy.
    /// </summary>
    [CustomEditor(typeof(ItemData))]
    public sealed class ItemDataEditor : UnityEditor.Editor
    {
        private SerializedProperty bycatchChanceBonusProperty;
        private SerializedProperty fishingXpBonusMultiplierProperty;
        private SerializedProperty fishingXpBonusWaterTypesProperty;
        private SerializedProperty woodcuttingXpBonusMultiplierProperty;
        private SerializedProperty miningXpBonusMultiplierProperty;
        private SerializedProperty cookingXpBonusMultiplierProperty;
        private SerializedProperty firemakingXpBonusMultiplierProperty;

        /// <summary>
        /// Tracks whether the bonuses foldout should currently be expanded.
        /// Defaults to true so existing assets remain fully visible until the
        /// user chooses to collapse the section.
        /// </summary>
        private bool showBonuses = true;

        private void OnEnable()
        {
            // Cache the serialized properties we intend to render inside the
            // bonuses foldout so they can be drawn without repeated lookups.
            bycatchChanceBonusProperty = serializedObject.FindProperty("bycatchChanceBonus");
            fishingXpBonusMultiplierProperty = serializedObject.FindProperty("fishingXpBonusMultiplier");
            fishingXpBonusWaterTypesProperty = serializedObject.FindProperty("fishingXpBonusWaterTypes");
            woodcuttingXpBonusMultiplierProperty = serializedObject.FindProperty("woodcuttingXpBonusMultiplier");
            miningXpBonusMultiplierProperty = serializedObject.FindProperty("miningXpBonusMultiplier");
            cookingXpBonusMultiplierProperty = serializedObject.FindProperty("cookingXpBonusMultiplier");
            firemakingXpBonusMultiplierProperty = serializedObject.FindProperty("firemakingXpBonusMultiplier");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Unity still exposes the script reference at the top of default
            // inspectors, so mirror that behaviour for familiarity.
            using (new EditorGUI.DisabledScope(true))
            {
                MonoScript script = MonoScript.FromScriptableObject((ItemData)target);
                EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
            }

            bool bonusesDrawn = false;
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.propertyPath == "m_Script")
                {
                    // Already drawn manually above.
                    continue;
                }

                if (property.propertyPath == bycatchChanceBonusProperty.propertyPath)
                {
                    // Replace the default rendering of the bonus properties with
                    // a single foldout that reveals all relevant multipliers.
                    DrawBonusesFoldout();
                    bonusesDrawn = true;
                    continue;
                }

                if (bonusesDrawn && IsBonusProperty(property))
                {
                    // Skip the remaining bonus properties after the foldout has
                    // been rendered so they are not duplicated below.
                    continue;
                }

                EditorGUILayout.PropertyField(property, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Renders the grouped bonuses foldout so fishing, woodcutting, mining,
        /// cooking, and firemaking multipliers remain accessible without
        /// cluttering the inspector by default.
        /// </summary>
        private void DrawBonusesFoldout()
        {
            EditorGUILayout.Space();
            showBonuses = EditorGUILayout.BeginFoldoutHeaderGroup(showBonuses, "Bonuses");
            if (showBonuses)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField("Fishing Bonuses", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(bycatchChanceBonusProperty);
                EditorGUILayout.PropertyField(fishingXpBonusMultiplierProperty);
                EditorGUILayout.PropertyField(fishingXpBonusWaterTypesProperty);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Woodcutting Bonuses", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(woodcuttingXpBonusMultiplierProperty);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Mining Bonuses", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(miningXpBonusMultiplierProperty);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Cooking Bonuses", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(cookingXpBonusMultiplierProperty);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Firemaking Bonuses", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(firemakingXpBonusMultiplierProperty);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// Helper used while iterating serialized properties to determine
        /// whether a property represents one of the bonus multipliers that are
        /// already drawn inside the foldout.
        /// </summary>
        private bool IsBonusProperty(SerializedProperty property)
        {
            string path = property.propertyPath;
            return path == bycatchChanceBonusProperty.propertyPath
                   || path == fishingXpBonusMultiplierProperty.propertyPath
                   || path == fishingXpBonusWaterTypesProperty.propertyPath
                   || path == woodcuttingXpBonusMultiplierProperty.propertyPath
                   || path == miningXpBonusMultiplierProperty.propertyPath
                   || path == cookingXpBonusMultiplierProperty.propertyPath
                   || path == firemakingXpBonusMultiplierProperty.propertyPath;
        }
    }
}
