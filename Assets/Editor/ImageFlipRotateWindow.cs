// File: Editor/ImageFlipRotateWindow.cs
using UnityEngine;
using UnityEditor;
using System.IO;

public class ImageFlipRotateWindow : EditorWindow
{
    Texture2D sourceTexture;
    enum Op { FlipHorizontal, FlipVertical, Rotate90CW, Rotate90CCW }
    Op op = Op.FlipHorizontal;
    string outputNameSuffix = "_flipped";

    [MenuItem("Tools/Images/Flip & Rotate…")]
    public static void Open() => GetWindow<ImageFlipRotateWindow>("Flip & Rotate");

    void OnGUI()
    {
        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Texture", sourceTexture, typeof(Texture2D), false);
        op = (Op)EditorGUILayout.EnumPopup("Operation", op);
        outputNameSuffix = EditorGUILayout.TextField("Name Suffix", outputNameSuffix);

        EditorGUI.BeginDisabledGroup(sourceTexture == null);
        if (GUILayout.Button("Process & Save as PNG"))
            ProcessAndSave();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Works on textures and sprites. Keeps pixel-perfect results for 64×64, 32×64, etc.", MessageType.Info);
    }

    void ProcessAndSave()
    {
        var readable = GetReadableCopy(sourceTexture);
        if (readable == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not read texture.", "OK");
            return;
        }

        Texture2D result = ApplyOp(readable, op);

        string path = AssetDatabase.GetAssetPath(sourceTexture);
        string dir = string.IsNullOrEmpty(path) ? Application.dataPath : Path.GetDirectoryName(path);
        string baseName = string.IsNullOrEmpty(path) ? sourceTexture.name : Path.GetFileNameWithoutExtension(path);
        string savePath = EditorUtility.SaveFilePanel("Save PNG", dir, baseName + outputNameSuffix, "png");
        if (!string.IsNullOrEmpty(savePath))
        {
            var png = result.EncodeToPNG();
            File.WriteAllBytes(savePath, png);
            AssetDatabase.Refresh();
        }

        DestroyImmediate(readable);
        DestroyImmediate(result);
    }

    static Texture2D GetReadableCopy(Texture2D tex)
    {
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

    static Texture2D ApplyOp(Texture2D src, Op op)
    {
        int w = src.width, h = src.height;
        Color[] srcPx = src.GetPixels();
        Texture2D dst;

        if (op == Op.Rotate90CW || op == Op.Rotate90CCW)
            dst = new Texture2D(h, w, TextureFormat.RGBA32, false, true);
        else
            dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);

        Color[] dstPx = new Color[dst.width * dst.height];

        System.Func<int, int, int> IdxSrc = (x, y) => y * w + x;
        System.Func<int, int, int> IdxDst = (x, y) => y * dst.width + x;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Color c = srcPx[IdxSrc(x, y)];
            switch (op)
            {
                case Op.FlipHorizontal:
                    dstPx[IdxDst((w - 1 - x), y)] = c;
                    break;
                case Op.FlipVertical:
                    dstPx[IdxDst(x, (h - 1 - y))] = c;
                    break;
                case Op.Rotate90CW:
                    // (x,y) -> (h-1 - y, x)
                    dstPx[(x) + (h - 1 - y) * dst.width] = c;
                    break;
                case Op.Rotate90CCW:
                    // (x,y) -> (y, w-1 - x)
                    dstPx[(y) + (w - 1 - x) * dst.width] = c;
                    break;
            }
        }

        dst.SetPixels(dstPx);
        dst.Apply();
        return dst;
    }
}
