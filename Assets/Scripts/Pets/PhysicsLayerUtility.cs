using UnityEngine;

namespace Pets
{
    /// <summary>
    /// Ensures a dedicated physics layer for pets that collides with nothing.
    /// </summary>
    public static class PhysicsLayerUtility
    {
        private const string LayerName = "Pets";
        private static bool initialized;

        /// <summary>
        /// Ensure the Pets layer exists and ignores all collisions.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Ensure()
        {
            if (initialized)
                return;
            initialized = true;

            int layer = LayerMask.NameToLayer(LayerName);
#if UNITY_EDITOR
            if (layer < 0)
            {
                var tagManager = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var layersProp = tagManager.FindProperty("layers");
                for (int i = 8; i < 32; i++)
                {
                    var sp = layersProp.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(sp.stringValue))
                    {
                        sp.stringValue = LayerName;
                        tagManager.ApplyModifiedProperties();
                        layer = i;
                        break;
                    }
                    if (sp.stringValue == LayerName)
                    {
                        layer = i;
                        break;
                    }
                }
            }
#endif
            if (layer < 0)
            {
                Debug.LogWarning("Pets layer missing. Please add a layer named 'Pets' in Project Settings > Tags and Layers.");
                return;
            }

            for (int i = 0; i < 32; i++)
                Physics2D.IgnoreLayerCollision(layer, i, true);
        }
    }
}