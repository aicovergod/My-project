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
        private SerializedProperty isHalberdProperty;
        private SerializedProperty aoeRadiusTilesProperty;
        private SerializedProperty coneAngleDegreesProperty;
        private SerializedProperty aoeMaxTargetsProperty;
        private SerializedProperty aoeMultiplierProperty;

        /// <summary>
        /// Tracks whether the bonuses foldout should currently be expanded.
        /// Defaults to true so existing assets remain fully visible until the
        /// user chooses to collapse the section.
        /// </summary>
        private bool showBonuses = true;

        /// <summary>
        /// Tracks whether the halberd settings foldout should currently be
        /// expanded. Defaults to true to mirror the previous always-visible
        /// layout until the user decides to collapse the configuration block.
        /// </summary>
        private bool showHalberdSettings = true;

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

            // Cache the halberd AOE properties so the custom foldout can render
            // them without repeat lookups or boxing allocations each frame.
            isHalberdProperty = serializedObject.FindProperty("isHalberd");
            aoeRadiusTilesProperty = serializedObject.FindProperty("aoeRadiusTiles");
            coneAngleDegreesProperty = serializedObject.FindProperty("coneAngleDeg");
            aoeMaxTargetsProperty = serializedObject.FindProperty("aoeMaxTargets");
            aoeMultiplierProperty = serializedObject.FindProperty("aoeMultiplier");
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
            bool halberdSettingsDrawn = false;
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

                if (property.propertyPath == isHalberdProperty.propertyPath)
                {
                    // Group the halberd AOE configuration options beneath a
                    // collapsible foldout to keep the inspector tidy when items
                    // are not configured as halberds.
                    DrawHalberdSettingsFoldout();
                    halberdSettingsDrawn = true;
                    continue;
                }

                if (halberdSettingsDrawn && IsHalberdProperty(property))
                {
                    // Skip the halberd properties already drawn inside the
                    // foldout so the default inspector does not duplicate them.
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
        /// Renders the grouped halberd AOE foldout so the optional cone
        /// configuration only appears when needed while keeping it accessible
        /// for halberd-class weapons.
        /// </summary>
        private void DrawHalberdSettingsFoldout()
        {
            EditorGUILayout.Space();
            showHalberdSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showHalberdSettings, "Halberd AOE");
            if (showHalberdSettings)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(isHalberdProperty);
                EditorGUILayout.PropertyField(aoeRadiusTilesProperty);
                EditorGUILayout.PropertyField(coneAngleDegreesProperty);
                EditorGUILayout.PropertyField(aoeMaxTargetsProperty);
                EditorGUILayout.PropertyField(aoeMultiplierProperty);

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

        /// <summary>
        /// Helper used while iterating serialized properties to determine
        /// whether a property has already been rendered inside the halberd AOE
        /// foldout.
        /// </summary>
        private bool IsHalberdProperty(SerializedProperty property)
        {
            string path = property.propertyPath;
            return path == isHalberdProperty.propertyPath
                   || path == aoeRadiusTilesProperty.propertyPath
                   || path == coneAngleDegreesProperty.propertyPath
                   || path == aoeMaxTargetsProperty.propertyPath
                   || path == aoeMultiplierProperty.propertyPath;
        }
    }
}
