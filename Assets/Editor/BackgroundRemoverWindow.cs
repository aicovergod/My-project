// File: Editor/BackgroundRemoverWindow.cs
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class BackgroundRemoverWindow : EditorWindow
{
    Texture2D sourceTexture;
    Color keyColor = new Color(1, 1, 1, 1); // white default
    float tolerance = 0.08f;                // 0..1 color distance
    bool useFloodFillFromEdges = true;      // great for checkered/solid BG
    int edgeGrowPixels = 1;                 // dilate selection a bit
    bool premultiplyRGB = true;             // helps avoid halo
    string outputNameSuffix = "_bgRemoved";

    [MenuItem("Tools/Images/Background Remover…")]
    public static void Open() => GetWindow<BackgroundRemoverWindow>("Background Remover");

    void OnGUI()
    {
        GUILayout.Label("Input", EditorStyles.boldLabel);
        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Texture", sourceTexture, typeof(Texture2D), false);

        EditorGUILayout.Space();
        GUILayout.Label("Chroma Key", EditorStyles.boldLabel);
        keyColor = EditorGUILayout.ColorField("Key Color", keyColor);
        tolerance = EditorGUILayout.Slider("Tolerance", tolerance, 0f, 0.5f);

        EditorGUILayout.Space();
        GUILayout.Label("Edge Handling", EditorStyles.boldLabel);
        useFloodFillFromEdges = EditorGUILayout.Toggle(new GUIContent("Flood-Fill From Edges", "Removes any pixels within tolerance connected to the image border."), useFloodFillFromEdges);
        edgeGrowPixels = EditorGUILayout.IntSlider(new GUIContent("Grow (px)", "Expands removal region to avoid 1px halos."), edgeGrowPixels, 0, 3);
        premultiplyRGB = EditorGUILayout.Toggle(new GUIContent("Premultiply RGB", "Darkens RGB by alpha to reduce halos when composited."), premultiplyRGB);

        EditorGUILayout.Space();
        GUILayout.Label("Output", EditorStyles.boldLabel);
        outputNameSuffix = EditorGUILayout.TextField("Name Suffix", outputNameSuffix);

        EditorGUI.BeginDisabledGroup(sourceTexture == null);
        if (GUILayout.Button("Process & Save as PNG"))
        {
            ProcessAndSave();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Tip: Pick the gray of your checkerboard or the flat background color as the Key Color. Tolerance ~0.05–0.12 usually works well.", MessageType.Info);
    }

    void ProcessAndSave()
    {
        var readable = GetReadableCopy(sourceTexture);
        if (readable == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not read texture.", "OK");
            return;
        }

        RemoveBackground(readable, keyColor, tolerance, useFloodFillFromEdges, edgeGrowPixels, premultiplyRGB);

        string path = AssetDatabase.GetAssetPath(sourceTexture);
        string dir = string.IsNullOrEmpty(path) ? Application.dataPath : Path.GetDirectoryName(path);
        string baseName = string.IsNullOrEmpty(path) ? sourceTexture.name : Path.GetFileNameWithoutExtension(path);
        string savePath = EditorUtility.SaveFilePanel("Save PNG", dir, baseName + outputNameSuffix, "png");
        if (!string.IsNullOrEmpty(savePath))
        {
            var png = readable.EncodeToPNG();
            File.WriteAllBytes(savePath, png);
            AssetDatabase.Refresh();
        }
        DestroyImmediate(readable);
    }

    static Texture2D GetReadableCopy(Texture2D tex)
    {
        if (tex == null) return null;

        // Blit to a temporary RT, then ReadPixels
        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(tex, rt);

        var prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, true);
        copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        copy.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }

    static void RemoveBackground(Texture2D tex, Color key, float tol, bool floodFillEdges, int grow, bool premultiply)
    {
        int w = tex.width, h = tex.height;
        Color[] px = tex.GetPixels();
        bool[] remove = new bool[px.Length];

        // distance function in RGB
        float TolSq = tol * tol * 3f; // simple scale
        System.Func<Color, bool> isBg = c =>
        {
            float dr = c.r - key.r;
            float dg = c.g - key.g;
            float db = c.b - key.b;
            float d2 = dr * dr + dg * dg + db * db;
            return d2 <= TolSq;
        };

        if (floodFillEdges)
        {
            Queue<Vector2Int> q = new Queue<Vector2Int>();
            System.Action<int, int> tryEnq = (x, y) =>
            {
                if (x < 0 || x >= w || y < 0 || y >= h) return;
                int i = y * w + x;
                if (remove[i]) return;
                if (isBg(px[i]))
                {
                    remove[i] = true;
                    q.Enqueue(new Vector2Int(x, y));
                }
            };

            // seed from all edges
            for (int x = 0; x < w; x++) { tryEnq(x, 0); tryEnq(x, h - 1); }
            for (int y = 0; y < h; y++) { tryEnq(0, y); tryEnq(w - 1, y); }

            // BFS
            int[] NX = { 1, -1, 0, 0 };
            int[] NY = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                for (int k = 0; k < 4; k++)
                    tryEnq(p.x + NX[k], p.y + NY[k]);
            }

            // optional grow (dilate)
            for (int g = 0; g < grow; g++)
            {
                bool[] grown = (bool[])remove.Clone();
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (remove[i]) continue;
                    // if any neighbor is marked, grow into it if similar to key (or always? choose similar)
                    bool neighborMarked = false;
                    for (int k = -1; k <= 1; k++)
                    for (int m = -1; m <= 1; m++)
                    {
                        if (k == 0 && m == 0) continue;
                        int nx = x + k, ny = y + m;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        if (remove[ny * w + nx]) { neighborMarked = true; break; }
                    }
                    if (neighborMarked && isBg(px[i])) grown[i] = true;
                }
                remove = grown;
            }
        }
        else
        {
            // global chroma key
            for (int i = 0; i < px.Length; i++)
                if (isBg(px[i])) remove[i] = true;
        }

        // apply alpha & optional premultiply
        for (int i = 0; i < px.Length; i++)
        {
            if (remove[i])
            {
                // fully remove
                px[i].a = 0f;
            }
            else if (premultiply)
            {
                // slight premultiply to soften edges against any background
                float a = px[i].a;
                px[i].r *= a;
                px[i].g *= a;
                px[i].b *= a;
            }
        }

        tex.SetPixels(px);
        tex.Apply();
    }
}
