#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Skills.Mining;

/// <summary>
/// Editor utility that builds the mining progress prefab and assigns it to the MiningUI.
/// Run via Tools/Mining/Generate Progress Prefab.
/// </summary>
public static class MiningProgressPrefabGenerator
{
    private const string PrefabPath = "Assets/Prefabs/MiningProgress.prefab";

    [MenuItem("Tools/Mining/Generate Progress Prefab")]
    public static void Generate()
    {
        // Create hierarchy: Canvas (world-space) -> Image (filled)
        GameObject root = new GameObject("MiningProgress", typeof(Canvas));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        GameObject imageGO = new GameObject("Progress", typeof(Image));
        imageGO.transform.SetParent(root.transform, false);
        var image = imageGO.GetComponent<Image>();
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillOrigin = (int)Image.Origin360.Top;
        image.fillAmount = 0f;
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.sizeDelta = Vector2.one;

        // Save prefab
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        // Ensure MiningUI exists in scene
        var ui = Object.FindObjectOfType<MiningUI>();
        if (ui == null)
        {
            ui = new GameObject("Mining UI Manager").AddComponent<MiningUI>();
        }

        // Load prefab image component and assign
        var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var prefabImage = prefabRoot != null ? prefabRoot.GetComponentInChildren<Image>() : null;
        if (prefabImage != null)
        {
            SerializedObject so = new SerializedObject(ui);
            SerializedProperty prop = so.FindProperty("progressPrefab");
            prop.objectReferenceValue = prefabImage;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ui);
        }

        Debug.Log("Mining progress prefab generated at " + PrefabPath);
    }
}
#endif