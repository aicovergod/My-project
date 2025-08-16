// Assets/Editor/ImageScaler.cs
using UnityEngine;
using UnityEditor;
using System.IO;

public class ImageScaler : EditorWindow
{
    private Texture2D sourceTexture;
    private int scalePercent = 100; // default 100% (no scale)

    [MenuItem("Tools/Image Scaler")]
    public static void ShowWindow()
    {
        GetWindow<ImageScaler>("Image Scaler");
    }

    void OnGUI()
    {
        GUILayout.Label("Upscale / Downscale Image", EditorStyles.boldLabel);

        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source Texture", sourceTexture, typeof(Texture2D), false);
        scalePercent = EditorGUILayout.IntSlider("Scale %", scalePercent, 10, 400);

        if (GUILayout.Button("Scale & Save"))
        {
            if (sourceTexture == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a source texture.", "OK");
                return;
            }

            ScaleAndSave();
        }
    }

    void ScaleAndSave()
    {
        string path = AssetDatabase.GetAssetPath(sourceTexture);
        string directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileNameWithoutExtension(path);

        int newWidth = Mathf.RoundToInt(sourceTexture.width * (scalePercent / 100f));
        int newHeight = Mathf.RoundToInt(sourceTexture.height * (scalePercent / 100f));

        Texture2D newTex = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);

        // Scale pixels
        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                float u = x / (float)newWidth;
                float v = y / (float)newHeight;
                Color col = sourceTexture.GetPixelBilinear(u, v);
                newTex.SetPixel(x, y, col);
            }
        }

        newTex.Apply();

        byte[] pngData = newTex.EncodeToPNG();
        string savePath = EditorUtility.SaveFilePanel("Save Scaled Texture", directory, fileName + "_scaled.png", "png");

        if (!string.IsNullOrEmpty(savePath))
        {
            File.WriteAllBytes(savePath, pngData);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", "Scaled texture saved:\n" + savePath, "OK");
        }
    }
}
