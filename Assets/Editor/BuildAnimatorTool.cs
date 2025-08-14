// Assets/Editor/BuildAnimatorTool.cs

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Editor
{
    public class BuildAnimatorTool : EditorWindow
    {
        [Header("Paths & Names")]
        private string spriteFolderPath = "Assets/Sprites/NPC";
        private string savePath = "Assets/Animations/NPC";
        private string animatorName = "NPCAnimator";

        [Header("Clip Settings")]
        private int fps = 12;
        private bool createFallbackSingleFrameIfMissing = true;

        private const string PARAM_DIR = "Dir";
        private const string PARAM_ISMOVING = "IsMoving";

        // Direction order MUST match your PlayerMover (0=Down,1=Left,2=Right,3=Up)
        private static readonly string[] DIR_NAMES = { "Down", "Left", "Right", "Up" };

        [MenuItem("Tools/Build Animator Tool")]
        public static void ShowWindow()
        {
            GetWindow<BuildAnimatorTool>("Build Animator");
        }

        void OnGUI()
        {
            GUILayout.Label("Animator Builder (Auto-Transitions)", EditorStyles.boldLabel);

            spriteFolderPath = EditorGUILayout.TextField("Sprite Folder Path", spriteFolderPath);
            savePath = EditorGUILayout.TextField("Save Path", savePath);
            animatorName = EditorGUILayout.TextField("Animator Name", animatorName);

            EditorGUILayout.Space(6);
            fps = EditorGUILayout.IntField("Clip FPS", Mathf.Max(1, fps));
            createFallbackSingleFrameIfMissing = EditorGUILayout.Toggle(
                new GUIContent("Fallback Single-Frame Clips",
                    "If no sprites match an Idle/Walk direction filter, create a 1-frame clip using the best available sprite."),
                createFallbackSingleFrameIfMissing
            );

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Build Animator"))
            {
                BuildAnimator();
            }
        }

        void BuildAnimator()
        {
            if (!Directory.Exists(spriteFolderPath))
            {
                Debug.LogError("Sprite folder not found: " + spriteFolderPath);
                return;
            }
            if (!AssetDatabase.IsValidFolder(savePath))
            {
                Debug.LogError("Save path not found: " + savePath);
                return;
            }

            // Load all sprites from the folder (and children)
            var spriteGUIDs = AssetDatabase.FindAssets("t:Sprite", new[] { spriteFolderPath });
            var sprites = spriteGUIDs
                .Select(g => AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null)
                .OrderBy(s => s.name)
                .ToArray();

            if (sprites.Length == 0)
            {
                Debug.LogError("No sprites found in folder: " + spriteFolderPath);
                return;
            }

            // Create AnimatorController
            string controllerPath = Path.Combine(savePath, animatorName + ".controller").Replace("\\", "/");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            // Add parameters
            EnsureParameter(controller, PARAM_DIR, AnimatorControllerParameterType.Int);
            EnsureParameter(controller, PARAM_ISMOVING, AnimatorControllerParameterType.Bool);

            // Build clips and states
            var stateByName = new Dictionary<string, AnimatorState>();
            for (int dirIdx = 0; dirIdx < DIR_NAMES.Length; dirIdx++)
            {
                string dir = DIR_NAMES[dirIdx];

                var idleClip = CreateClip(sprites, dir, "Idle", savePath, fps, createFallbackSingleFrameIfMissing);
                var walkClip = CreateClip(sprites, dir, "Walk", savePath, fps, createFallbackSingleFrameIfMissing);

                var idleState = controller.AddMotion(idleClip);
                idleState.name = "Idle" + dir;
                var walkState = controller.AddMotion(walkClip);
                walkState.name = "Walk" + dir;

                stateByName[idleState.name] = idleState;
                stateByName[walkState.name] = walkState;
            }

            // Default state: IdleDown
            var baseLayer = controller.layers[0];
            var sm = baseLayer.stateMachine;
            var defaultState = stateByName.ContainsKey("IdleDown") ? stateByName["IdleDown"] : sm.states.First().state;
            sm.defaultState = defaultState;

            // Create Any State transitions with conditions
            CreateAnyStateTransitions(controller, stateByName);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("✅ Animator built with transitions at: " + controllerPath);
        }

        static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            if (!controller.parameters.Any(p => p.name == name))
            {
                controller.AddParameter(name, type);
            }
        }

        static AnimationClip CreateClip(Sprite[] allSprites, string dir, string type, string saveFolder, int frameRate, bool fallback)
        {
            var clip = new AnimationClip { frameRate = frameRate };

            // Filter sprites by name, tolerant to case and separators (e.g., "walk_down_0")
            string dirL = dir.ToLower();
            string typeL = type.ToLower();

            var matched = allSprites
                .Where(s =>
                {
                    var n = s.name.ToLower();
                    return n.Contains(dirL) && n.Contains(typeL);
                })
                .OrderBy(s => ExtractNumericSuffix(s.name))
                .ToArray();

            if (matched.Length == 0 && fallback)
            {
                var dirOnly = allSprites.Where(s => s.name.ToLower().Contains(dirL))
                    .OrderBy(s => ExtractNumericSuffix(s.name))
                    .ToArray();
                if (dirOnly.Length > 0)
                    matched = new[] { dirOnly[0] };
                else
                    matched = new[] { allSprites[0] };
            }

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = "",
                propertyName = "m_Sprite"
            };

            if (matched.Length > 0)
            {
                var keys = new ObjectReferenceKeyframe[matched.Length];
                for (int i = 0; i < matched.Length; i++)
                {
                    keys[i] = new ObjectReferenceKeyframe
                    {
                        time = i / (float)frameRate,
                        value = matched[i]
                    };
                }
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            }

            string filePath = Path.Combine(saveFolder, $"{type}{dir}.anim").Replace("\\", "/");
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(filePath);
            if (existing == null)
                AssetDatabase.CreateAsset(clip, filePath);
            else
            {
                EditorUtility.CopySerialized(clip, existing);
                clip = existing;
            }

            return clip;
        }

        static int ExtractNumericSuffix(string name)
        {
            int num = 0;
            int i = name.Length - 1;
            int multiplier = 1;
            bool foundDigit = false;

            while (i >= 0 && char.IsDigit(name[i]))
            {
                foundDigit = true;
                num += (name[i] - '0') * multiplier;
                multiplier *= 10;
                i--;
            }

            return foundDigit ? num : int.MaxValue;
        }

        static void ConfigureTransition(AnimatorStateTransition t)
        {
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0.02f;
            t.exitTime = 0f;

            // In code, "Next State" is named "Destination"
            t.interruptionSource = TransitionInterruptionSource.Destination;

            // These exist in modern Unity; safe to keep.
            t.orderedInterruption = false;
            t.canTransitionToSelf = false;
        }

        static void CreateAnyStateTransitions(AnimatorController controller, Dictionary<string, AnimatorState> stateByName)
        {
            var sm = controller.layers[0].stateMachine;

            // Clear existing AnyState transitions targeting our states (avoid duplicates on rebuild)
            var toRemove = sm.anyStateTransitions
                .Where(t => t.destinationState != null && stateByName.ContainsValue(t.destinationState))
                .ToArray();
            foreach (var tr in toRemove)
                sm.RemoveAnyStateTransition(tr);

            // Dir map must match PlayerMover: 0=Down,1=Left,2=Right,3=Up
            var dirIndexByState = new Dictionary<string, int>
            {
                {"Down", 0}, {"Left", 1}, {"Right", 2}, {"Up", 3}
            };

            foreach (var kvp in dirIndexByState)
            {
                string dirName = kvp.Key;
                int dirVal = kvp.Value;

                // WalkDIR: IsMoving == true && Dir == dirVal
                if (stateByName.TryGetValue("Walk" + dirName, out var walkState))
                {
                    var t = sm.AddAnyStateTransition(walkState);
                    ConfigureTransition(t);
                    t.AddCondition(AnimatorConditionMode.If, 0, PARAM_ISMOVING);
                    t.AddCondition(AnimatorConditionMode.Equals, dirVal, PARAM_DIR);
                }

                // IdleDIR: IsMoving == false && Dir == dirVal
                if (stateByName.TryGetValue("Idle" + dirName, out var idleState))
                {
                    var t = sm.AddAnyStateTransition(idleState);
                    ConfigureTransition(t);
                    t.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_ISMOVING);
                    t.AddCondition(AnimatorConditionMode.Equals, dirVal, PARAM_DIR);
                }
            }
        }
    }
}
