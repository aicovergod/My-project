#if UNITY_EDITOR
using System;
using System.IO;
using NPC;
using UnityEditor;
using UnityEngine;

namespace NPC.Combat.Editor
{
    /// <summary>
    /// Utility menu items that author the default NPC flash shader/material assets so designers can
    /// hook the <see cref="NpcFlashEffect"/> component up in a single click during content creation.
    /// </summary>
    public static class NpcFlashMaterialUtility
    {
        private const string ShaderAssetPath = "Assets/GeneratedAssets/NPC/Combat/NpcFlashSprite.shader";
        private const string MaterialAssetPath = "Assets/GeneratedAssets/NPC/Combat/NpcFlashSprite.mat";
        private const string CreateMaterialMenuPath = "Tools/NPC/Create Flash Material";
        private const string AssignMaterialMenuPath = "Tools/NPC/Assign Flash Material To Selected";
        private const string ContextAssignPath = "CONTEXT/NpcFlashEffect/Assign Default Flash Material";

        private static readonly Color DefaultFlashColor = new Color(1f, 0.35f, 0.35f, 1f);

        /// <summary>
        /// Generates the URP-compatible sprite flash shader, builds a material instance, saves it, and
        /// selects the new material so it can immediately be assigned inside the inspector.
        /// </summary>
        [MenuItem(CreateMaterialMenuPath, priority = 100)]
        public static void CreateFlashMaterial()
        {
            EnsureAssetDirectories();

            Shader shader = CreateOrUpdateShaderAsset();
            if (shader == null)
            {
                Debug.LogError("Failed to create the NPC flash shader asset.");
                return;
            }

            Material material = CreateOrUpdateMaterialAsset(shader);
            if (material == null)
            {
                Debug.LogError("Failed to create the NPC flash material asset.");
                return;
            }

            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);

            Debug.Log($"NPC flash material created at {MaterialAssetPath} and selected for assignment.", material);
        }

        /// <summary>
        /// Assigns the generated flash material to all selected <see cref="NpcFlashEffect"/> components.
        /// </summary>
        [MenuItem(AssignMaterialMenuPath, priority = 200)]
        public static void AssignFlashMaterialToSelection()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
            if (material == null)
            {
                if (!EditorUtility.DisplayDialog(
                        "NPC Flash Material Missing",
                        "The flash material asset has not been generated yet. Create it now?",
                        "Create",
                        "Cancel"))
                {
                    return;
                }

                CreateFlashMaterial();
                material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
                if (material == null)
                {
                    return;
                }
            }

            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                Debug.LogWarning("No game objects selected. Select NPCs that contain NpcFlashEffect components.");
                return;
            }

            int assignmentCount = 0;
            foreach (GameObject go in selectedObjects)
            {
                if (go == null)
                    continue;

                NpcFlashEffect flashEffect = go.GetComponent<NpcFlashEffect>();
                if (flashEffect == null)
                    continue;

                Undo.RecordObject(flashEffect, "Assign NPC Flash Material");
                SerializedObject so = new SerializedObject(flashEffect);
                SerializedProperty materialProperty = so.FindProperty("flashMaterial");
                if (materialProperty != null)
                {
                    materialProperty.objectReferenceValue = material;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(flashEffect);
                    assignmentCount++;
                }
            }

            if (assignmentCount > 0)
            {
                Debug.Log($"Assigned NPC flash material to {assignmentCount} NpcFlashEffect component(s).");
            }
            else
            {
                Debug.LogWarning("No NpcFlashEffect components found on the current selection. Nothing was assigned.");
            }
        }

        /// <summary>
        /// Context menu hook that allows designers to right-click an <see cref="NpcFlashEffect"/> component
        /// and automatically assign the generated flash material asset.
        /// </summary>
        [MenuItem(ContextAssignPath)]
        private static void AssignFlashMaterialFromContext(MenuCommand command)
        {
            if (command == null || command.context == null)
                return;

            if (command.context is not NpcFlashEffect flashEffect)
                return;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
            if (material == null)
            {
                CreateFlashMaterial();
                material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
                if (material == null)
                {
                    Debug.LogError("Unable to locate the NPC flash material asset after creation attempt.");
                    return;
                }
            }

            Undo.RecordObject(flashEffect, "Assign NPC Flash Material");
            SerializedObject so = new SerializedObject(flashEffect);
            SerializedProperty materialProperty = so.FindProperty("flashMaterial");
            if (materialProperty != null)
            {
                materialProperty.objectReferenceValue = material;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(flashEffect);
                Debug.Log("NPC flash material assigned via component context menu.", flashEffect);
            }
        }

        /// <summary>
        /// Ensures the folder hierarchy for generated shader and material assets exists.
        /// </summary>
        private static void EnsureAssetDirectories()
        {
            string shaderDirectory = Path.GetDirectoryName(ShaderAssetPath);
            string materialDirectory = Path.GetDirectoryName(MaterialAssetPath);

            if (!string.IsNullOrEmpty(shaderDirectory))
            {
                Directory.CreateDirectory(shaderDirectory);
            }

            if (!string.IsNullOrEmpty(materialDirectory) && !string.Equals(materialDirectory, shaderDirectory, StringComparison.Ordinal))
            {
                Directory.CreateDirectory(materialDirectory);
            }
        }

        /// <summary>
        /// Creates or overwrites the shader asset that drives the flash overlay behaviour.
        /// </summary>
        private static Shader CreateOrUpdateShaderAsset()
        {
            string shaderSource = BuildShaderSource();

            if (File.Exists(ShaderAssetPath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Overwrite NPC Flash Shader?",
                        "A flash shader already exists. Do you want to overwrite it with the latest template?",
                        "Overwrite",
                        "Keep Existing"))
                {
                    AssetDatabase.ImportAsset(ShaderAssetPath);
                    return AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
                }

                AssetDatabase.DeleteAsset(ShaderAssetPath);
            }

            File.WriteAllText(ShaderAssetPath, shaderSource);
            AssetDatabase.ImportAsset(ShaderAssetPath);
            return AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
        }

        /// <summary>
        /// Creates or overwrites the flash material asset so NPCs can reference it directly.
        /// </summary>
        private static Material CreateOrUpdateMaterialAsset(Shader shader)
        {
            if (shader == null)
                return null;

            if (File.Exists(MaterialAssetPath))
            {
                AssetDatabase.DeleteAsset(MaterialAssetPath);
            }

            Material material = new Material(shader)
            {
                name = Path.GetFileNameWithoutExtension(MaterialAssetPath)
            };

            material.SetFloat("_FlashAmount", 0f);
            material.SetColor("_FlashColor", DefaultFlashColor);

            AssetDatabase.CreateAsset(material, MaterialAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MaterialAssetPath);

            return AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
        }

        /// <summary>
        /// Builds the shader source used to author the URP-compatible sprite flash overlay shader.
        /// </summary>
        private static string BuildShaderSource()
        {
            return @"Shader \"NPC/Combat/NpcFlashSprite\"" +
                   @"\n{" +
                   @"\n    Properties" +
                   @"\n    {" +
                   @"\n        _MainTex (\"Sprite Texture\", 2D) = \"white\" {}" +
                   @"\n        _FlashColor (\"Flash Color\", Color) = (1, 0.35, 0.35, 1)" +
                   @"\n        _FlashAmount (\"Flash Amount\", Range(0, 1)) = 0" +
                   @"\n    }" +
                   @"\n    SubShader" +
                   @"\n    {" +
                   @"\n        Tags { \"RenderType\"=\"Transparent\" \"Queue\"=\"Transparent\" \"IgnoreProjector\"=\"True\" \"CanUseSpriteAtlas\"=\"True\" \"RenderPipeline\"=\"UniversalPipeline\" }" +
                   @"\n        Cull Off" +
                   @"\n        Blend SrcAlpha OneMinusSrcAlpha" +
                   @"\n        ZWrite Off" +
                   @"\n        Pass" +
                   @"\n        {" +
                   @"\n            Name \"SpriteForward\"" +
                   @"\n            Tags { \"LightMode\"=\"Universal2D\" }" +
                   @"\n" +
                   @"\n            HLSLPROGRAM" +
                   @"\n            #pragma vertex vert" +
                   @"\n            #pragma fragment frag" +
                   @"\n            #include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl\"" +
                   @"\n" +
                   @"\n            struct Attributes" +
                   @"\n            {" +
                   @"\n                float4 positionOS : POSITION;" +
                   @"\n                float2 uv : TEXCOORD0;" +
                   @"\n                float4 color : COLOR;" +
                   @"\n            };" +
                   @"\n" +
                   @"\n            struct Varyings" +
                   @"\n            {" +
                   @"\n                float4 positionHCS : SV_POSITION;" +
                   @"\n                float2 uv : TEXCOORD0;" +
                   @"\n                float4 color : COLOR;" +
                   @"\n            };" +
                   @"\n" +
                   @"\n            CBUFFER_START(UnityPerMaterial)" +
                   @"\n                float4 _MainTex_ST;" +
                   @"\n                float4 _FlashColor;" +
                   @"\n                float _FlashAmount;" +
                   @"\n            CBUFFER_END" +
                   @"\n" +
                   @"\n            TEXTURE2D(_MainTex);" +
                   @"\n            SAMPLER(sampler_MainTex);" +
                   @"\n" +
                   @"\n            Varyings vert(Attributes input)" +
                   @"\n            {" +
                   @"\n                Varyings output;" +
                   @"\n                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);" +
                   @"\n                output.uv = TRANSFORM_TEX(input.uv, _MainTex);" +
                   @"\n                output.color = input.color;" +
                   @"\n                return output;" +
                   @"\n            }" +
                   @"\n" +
                   @"\n            half4 frag(Varyings input) : SV_Target" +
                   @"\n            {" +
                   @"\n                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;" +
                   @"\n                half flashAmount = saturate(_FlashAmount);" +
                   @"\n                half3 flashedRgb = lerp(baseColor.rgb, _FlashColor.rgb * baseColor.a, flashAmount);" +
                   @"\n                return half4(flashedRgb, baseColor.a);" +
                   @"\n            }" +
                   @"\n            ENDHLSL" +
                   @"\n        }" +
                   @"\n    }" +
                   @"\n    FallBack \"Sprites/Default\"" +
                   @"\n}";
        }
    }
}
#endif
