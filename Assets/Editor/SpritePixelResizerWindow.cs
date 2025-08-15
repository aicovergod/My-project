// Assets/Editor/SpritePixelResizerWindow.cs
// Unity Editor tool to resample textures/sprites and save resized PNG assets.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public class SpritePixelResizerWindow : EditorWindow
    {
        private const int DefaultSize = 64;

        private int outWidth = DefaultSize;
        private int outHeight = DefaultSize;

        private bool importAsSprite = true;
        private int pixelsPerUnit = 64;
        private FilterMode filterMode = FilterMode.Point;
        private TextureImporterCompression compression = TextureImporterCompression.Uncompressed;

        private Vector2 scroll;
        private readonly List<UnityEngine.Object> queuedObjects = new List<UnityEngine.Object>();

        [MenuItem("Tools/Images/Pixel Resizer")]
        public static void Open()
        {
            var w = GetWindow<SpritePixelResizerWindow>("Pixel Resizer");
            w.minSize = new Vector2(420, 420);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Sprite / Texture Pixel Resizer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag Texture2D or Sprite assets below. Choose a target size and click Resize & Export.\nThis creates NEW PNG files (does not modify originals).", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                outWidth = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Target Width"), outWidth));
                outHeight = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Target Height"), outHeight));
            }

            filterMode = (FilterMode)EditorGUILayout.EnumPopup(new GUIContent("Resample Filter"), filterMode);

            EditorGUILayout.Space(6);
            importAsSprite = EditorGUILayout.Toggle(new GUIContent("Import Output As Sprite"), importAsSprite);
            using (new EditorGUI.DisabledScope(!importAsSprite))
            {
                pixelsPerUnit = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Pixels Per Unit"), pixelsPerUnit));
                compression = (TextureImporterCompression)EditorGUILayout.EnumPopup(new GUIContent("Texture Compression"), compression);
            }

            EditorGUILayout.Space(8);
            DrawDropArea();

            EditorGUILayout.Space(8);
            DrawQueuedList();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear List"))
                {
                    queuedObjects.Clear();
                }

                using (new EditorGUI.DisabledScope(queuedObjects.Count == 0))
                {
                    if (GUILayout.Button($"Resize & Export ({queuedObjects.Count})", GUILayout.Height(32)))
                    {
                        ProcessAll();
                    }
                }
            }
        }

        private void DrawDropArea()
        {
            Rect dropArea = GUILayoutUtility.GetRect(0, 120, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag & Drop Textures or Sprites Here", EditorStyles.helpBox);

            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        return;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is Texture2D || obj is Sprite)
                                queuedObjects.Add(obj);
                        }
                    }
                    Event.current.Use();
                    break;
            }
        }

        private void DrawQueuedList()
        {
            EditorGUILayout.LabelField("Queued Assets", EditorStyles.boldLabel);
            if (queuedObjects.Count == 0)
            {
                EditorGUILayout.HelpBox("No assets queued yet.", MessageType.None);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(180));
            for (int i = 0; i < queuedObjects.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    queuedObjects[i] = EditorGUILayout.ObjectField(queuedObjects[i], typeof(UnityEngine.Object), false);
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        queuedObjects.RemoveAt(i);
                        i--;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void ProcessAll()
        {
            int success = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var obj in queuedObjects)
                {
                    try
                    {
                        if (TryProcessObject(obj, out string newAssetPath))
                            success++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Pixel Resizer: Failed processing {obj?.name}: {ex}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog("Pixel Resizer", $"Finished.\nCreated {success} file(s).", "OK");
        }

        private bool TryProcessObject(UnityEngine.Object obj, out string newAssetPath)
        {
            newAssetPath = null;

            if (obj is Sprite sprite)
            {
                // Handle sprite by cropping from its texture using sprite.rect
                var tex = sprite.texture;
                if (!EnsureReadable(tex, out var prevReadable, out var importer))
                    return false;

                try
                {
                    Rect r = sprite.rect;
                    Texture2D cropped = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                    var pixels = tex.GetPixels(
                        (int)r.x, (int)r.y,
                        (int)r.width, (int)r.height);
                    cropped.SetPixels(pixels);
                    cropped.Apply(false, false);

                    Texture2D resized = ResizeTexture(cropped, outWidth, outHeight, filterMode);

                    newAssetPath = SaveTextureBesideSource(tex, resized, outWidth, outHeight);

                    // cleanup
                    UnityEngine.Object.DestroyImmediate(cropped);
                    UnityEngine.Object.DestroyImmediate(resized);
                }
                finally
                {
                    RestoreReadable(importer, prevReadable);
                }
            }
            else if (obj is Texture2D tex)
            {
                if (!EnsureReadable(tex, out var prevReadable, out var importer))
                    return false;

                try
                {
                    Texture2D resized = ResizeTexture(tex, outWidth, outHeight, filterMode);
                    newAssetPath = SaveTextureBesideSource(tex, resized, outWidth, outHeight);
                    UnityEngine.Object.DestroyImmediate(resized);
                }
                finally
                {
                    RestoreReadable(importer, prevReadable);
                }
            }
            else
            {
                Debug.LogWarning($"Unsupported type: {obj?.GetType().Name}");
                return false;
            }

            if (!string.IsNullOrEmpty(newAssetPath) && importAsSprite)
            {
                ApplySpriteImportSettings(newAssetPath, pixelsPerUnit, filterMode, compression);
            }

            return !string.IsNullOrEmpty(newAssetPath);
        }

        // --- Core resizing using RenderTexture -> Texture2D readback ---
        private static Texture2D ResizeTexture(Texture2D source, int width, int height, FilterMode mode)
        {
            // Create temporary RT
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;

            // Choose sampling
            var prevFilter = source.filterMode;
            source.filterMode = mode;

            // Blit (GPU resample)
            Graphics.Blit(source, rt);

            // Read back
            RenderTexture.active = rt;
            Texture2D outTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            outTex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            outTex.Apply(false, false);

            // Restore
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            source.filterMode = prevFilter;

            return outTex;
        }

        private static bool EnsureReadable(Texture2D tex, out bool prevReadable, out TextureImporter importer)
        {
            prevReadable = true;
            importer = null;

            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("Texture is not an asset on disk.");
                return false;
            }

            importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("Failed to get TextureImporter.");
                return false;
            }

            prevReadable = importer.isReadable;
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            return true;
        }

        private static void RestoreReadable(TextureImporter importer, bool prevReadable)
        {
            if (importer != null && importer.isReadable != prevReadable)
            {
                importer.isReadable = prevReadable;
                importer.SaveAndReimport();
            }
        }

        private static string SaveTextureBesideSource(Texture2D source, Texture2D toSave, int w, int h)
        {
            string srcPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(srcPath))
            {
                Debug.LogError("Source texture is not a disk asset, cannot determine save path.");
                return null;
            }

            string dir = Path.GetDirectoryName(srcPath);
            string baseName = Path.GetFileNameWithoutExtension(srcPath);
            string outName = $"{baseName}_{w}x{h}.png";
            string outPath = Path.Combine(dir, outName).Replace("\\", "/");

            byte[] png = toSave.EncodeToPNG();
            File.WriteAllBytes(outPath, png);

            return outPath;
        }

        private static void ApplySpriteImportSettings(string assetPath, int ppu, FilterMode filter, TextureImporterCompression comp)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) return;

            ti.textureType = TextureImporterType.Sprite;
            ti.spritePixelsPerUnit = Mathf.Max(1, ppu);
            ti.filterMode = filter;
            ti.textureCompression = comp;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.npotScale = TextureImporterNPOTScale.None;

            ti.SaveAndReimport();
        }

        // --- Project window context menu for quick 64x64 ---
        [MenuItem("Assets/2DScape/Resize To 64x64 (Export)", true)]
        private static bool ValidateQuickResize()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is Texture2D || obj is Sprite) return true;
            }
            return false;
        }

        [MenuItem("Assets/2DScape/Resize To 64x64 (Export)")]
        private static void QuickResizeSelected()
        {
            var window = GetWindow<SpritePixelResizerWindow>();
            window.outWidth = DefaultSize;
            window.outHeight = DefaultSize;
            window.importAsSprite = true;
            window.pixelsPerUnit = 64;
            window.filterMode = FilterMode.Point;
            window.compression = TextureImporterCompression.Uncompressed;

            window.queuedObjects.Clear();
            foreach (var obj in Selection.objects)
            {
                if (obj is Texture2D || obj is Sprite)
                    window.queuedObjects.Add(obj);
            }

            window.ProcessAll();
        }
    }
}
#endif
