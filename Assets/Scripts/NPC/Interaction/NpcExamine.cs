using System;
using UI.Chat;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NPC
{
    /// <summary>
    ///     Partial implementation of <see cref="NpcInteractionOptions"/> dedicated to the Examine option.
    ///     Keeps the examine configuration modular while allowing the primary script to remain focused
    ///     on the shared interaction toggles.
    /// </summary>
    public sealed partial class NpcInteractionOptions
    {
        [SerializeField]
        [Tooltip("When enabled the Examine option will appear in the right-click menu.")]
        private bool enableExamine = true;

        [SerializeField]
        [Tooltip("Text displayed in the Game chat channel when this NPC is examined.")]
        [TextArea(2, 4)]
        private string examineDescription = string.Empty;

        /// <summary>Gets whether the examine option is currently exposed to players.</summary>
        public bool IsExamineEnabled => enableExamine;

        /// <summary>
        ///     Returns the trimmed examine description when configured, otherwise falls back to
        ///     the NPC name so the chat message always contains useful information.
        /// </summary>
        public string ExamineDescription
        {
            get
            {
                if (string.IsNullOrWhiteSpace(examineDescription))
                    return gameObject != null ? gameObject.name : string.Empty;

                return examineDescription.Trim();
            }
        }

        private void Reset()
        {
            if (string.IsNullOrWhiteSpace(examineDescription))
                examineDescription = gameObject != null ? gameObject.name : string.Empty;
        }

        private void OnValidate()
        {
            if (!enableExamine)
                return;

            if (string.IsNullOrWhiteSpace(examineDescription))
                examineDescription = gameObject != null ? gameObject.name : string.Empty;
        }
    }

    /// <summary>
    ///     Partial implementation of <see cref="NpcInteractable"/> that routes Examine feedback through the
    ///     centralised Chatbox UI service.
    /// </summary>
    public partial class NpcInteractable
    {
        /// <summary>
        ///     Posts the configured examine message to the Game chat channel. Falls back to a generic
        ///     description when no custom text is provided or examine has been disabled on the options component.
        /// </summary>
        public void Examine()
        {
            string message = string.Empty;

            if (interactionOptions != null && interactionOptions.IsExamineEnabled)
                message = interactionOptions.ExamineDescription;

            if (string.IsNullOrWhiteSpace(message))
                message = $"You examine {name}.";

            ChatboxUI.PostSystemMessage(message);
        }
    }
}

#if UNITY_EDITOR
namespace NPC.Editor
{
    /// <summary>
    ///     Custom inspector that displays the examine description text area only when the option is enabled,
    ///     keeping the workflow intuitive for designers configuring NPCs.
    /// </summary>
    [CustomEditor(typeof(NpcInteractionOptions))]
    internal sealed class NpcInteractionOptionsEditor : UnityEditor.Editor
    {
        private SerializedProperty talkProperty;
        private SerializedProperty tradeProperty;
        private SerializedProperty pickpocketProperty;
        private SerializedProperty examineProperty;
        private SerializedProperty examineDescriptionProperty;

        private void OnEnable()
        {
            talkProperty = serializedObject.FindProperty("enableTalk");
            tradeProperty = serializedObject.FindProperty("enableTrade");
            pickpocketProperty = serializedObject.FindProperty("enablePickpocket");
            examineProperty = serializedObject.FindProperty("enableExamine");
            examineDescriptionProperty = serializedObject.FindProperty("examineDescription");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Right-Click Menu Options", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(talkProperty);
            EditorGUILayout.PropertyField(tradeProperty);
            EditorGUILayout.PropertyField(pickpocketProperty);
            EditorGUILayout.PropertyField(examineProperty);

            if (examineProperty.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(examineDescriptionProperty, new GUIContent("Examine Description"));

                if (string.IsNullOrWhiteSpace(examineDescriptionProperty.stringValue))
                {
                    EditorGUILayout.HelpBox("Examine text is empty. The NPC name will be used in the chatbox.", MessageType.Info);
                }

                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
