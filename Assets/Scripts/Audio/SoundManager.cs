using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Audio
{
    /// <summary>
    /// Centralised sound service responsible for loading audio clips from <c>Assets/Sound</c>
    /// and replaying them on demand. The manager lazily loads clips, caches them for
    /// subsequent requests and exposes helper methods so gameplay code only needs to know
    /// about logical <see cref="SoundEffect"/> identifiers rather than file paths.
    /// </summary>
    [DisallowMultipleComponent]
    public class SoundManager : MonoBehaviour
    {
        private const string SoundFolderName = "Sound";
        private const string AssetRoot = "Assets";

#if UNITY_EDITOR
        private static readonly string[] SupportedExtensions = { ".ogg", ".wav", ".mp3" };
#endif

        private static SoundManager instance;

        /// <summary>
        /// Singleton style accessor that either returns an existing manager in the scene or
        /// spins up a new hidden GameObject when first called.
        /// </summary>
        public static SoundManager Instance
        {
            get
            {
                if (instance != null)
                    return instance;

                instance = FindExistingManager();
                if (instance != null)
                    return instance;

                var go = new GameObject(nameof(SoundManager));
                instance = go.AddComponent<SoundManager>();
                return instance;
            }
        }

        /// <summary>
        /// Lookup linking sound identifiers to on-disk file names (without extensions).
        /// Using the enum shields gameplay code from actual file names.
        /// </summary>
        private readonly Dictionary<SoundEffect, string> soundFileMap = new()
        {
            { SoundEffect.AttackLevelUp, "02_Attack_Level_Up" },
            { SoundEffect.DefenceLevelUp, "03_Defence_Level_Up" },
            { SoundEffect.MagicLevelUp, "09_Magic_Level_Up" },
            { SoundEffect.MiningLevelUp, "08_Mining_Level_Up" },
            { SoundEffect.WoodcuttingLevelUp, "09_Woodcutting_Level_Up" },
            { SoundEffect.FishingLevelUp, "11_Fishing_Level_Up" },
            { SoundEffect.CookingLevelUp, "10_Cooking_Level_Up" },
            { SoundEffect.BeastmasterLevelUp, "03_Defence_Level_Up" },
            { SoundEffect.PlayerDeath, "12_You_Are_Dead" },
            { SoundEffect.TreeChop, "01_Tree_Chop" }
        };

        /// <summary>
        /// Cache of loaded audio clips keyed by file name (without extension) so we only
        /// perform the IO/asset lookup once per clip.
        /// </summary>
        private readonly Dictionary<string, AudioClip> clipCache = new();

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Global volume applied to sound effects triggered through the manager.")]
        private float sfxVolume = 1f;

        private AudioSource oneShotSource;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureAudioSource();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>
        /// Play a sound effect using its logical identifier. The clip is loaded from disk on the
        /// first request, cached and played through a dedicated one-shot AudioSource.
        /// </summary>
        public void PlaySfx(SoundEffect effect)
        {
            if (!soundFileMap.TryGetValue(effect, out var fileName))
            {
                Debug.LogWarning($"SoundManager has no mapping for effect '{effect}'.", this);
                return;
            }

            PlaySfxByFileName(fileName);
        }

        /// <summary>
        /// Plays a sound effect by referencing the raw file name located inside
        /// <c>Assets/Sound</c>. Extensions are optional and multiple common formats are checked.
        /// </summary>
        public void PlaySfxByFileName(string clipName)
        {
            var clip = LoadClip(clipName);
            if (clip == null)
                return;

            EnsureAudioSource();
            oneShotSource.PlayOneShot(clip, sfxVolume);
        }

        /// <summary>
        /// Retrieve the audio clip linked to a logical sound effect identifier without playing
        /// it immediately. Returns <c>null</c> if the clip cannot be located.
        /// </summary>
        public AudioClip GetClip(SoundEffect effect)
        {
            return soundFileMap.TryGetValue(effect, out var fileName) ? LoadClip(fileName) : null;
        }

        /// <summary>
        /// Retrieve an audio clip by file name. This is exposed for UI previews or gameplay code
        /// that needs to manually control playback behaviour.
        /// </summary>
        public AudioClip GetClip(string clipName)
        {
            return LoadClip(clipName);
        }

        /// <summary>
        /// Allows runtime systems to register or override the file backing a logical sound
        /// effect. The provided name can include an extension which will be stripped so the
        /// lookup remains consistent.
        /// </summary>
        public void RegisterSound(SoundEffect effect, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Debug.LogWarning("Attempted to register a sound without a file name.", this);
                return;
            }

            string key = Path.GetFileNameWithoutExtension(fileName);
            if (soundFileMap.TryGetValue(effect, out var existingKey) && existingKey != key)
                clipCache.Remove(existingKey);

            soundFileMap[effect] = key;
            clipCache.Remove(key);
        }

        /// <summary>
        /// Clears the internal cache so clips will be reloaded next time they are requested.
        /// Handy while tweaking assets during play mode.
        /// </summary>
        public void ClearCache()
        {
            clipCache.Clear();
        }

        private static SoundManager FindExistingManager()
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<SoundManager>();
#else
            return FindObjectOfType<SoundManager>();
#endif
        }

        private void EnsureAudioSource()
        {
            if (oneShotSource != null)
                return;

            oneShotSource = gameObject.GetComponent<AudioSource>();
            if (oneShotSource == null)
                oneShotSource = gameObject.AddComponent<AudioSource>();

            oneShotSource.playOnAwake = false;
            oneShotSource.loop = false;
            oneShotSource.spatialBlend = 0f; // Treat as 2D UI-style audio.
        }

        private AudioClip LoadClip(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                Debug.LogWarning("Attempted to load a sound using an empty file name.", this);
                return null;
            }

            string key = Path.GetFileNameWithoutExtension(clipName);
            if (clipCache.TryGetValue(key, out var cachedClip))
                return cachedClip;

            // First attempt to load via Resources so builds continue to work when the folder
            // is mirrored under Assets/Resources/Sound.
            AudioClip clip = Resources.Load<AudioClip>($"{SoundFolderName}/{key}");

#if UNITY_EDITOR
            if (clip == null)
            {
                // When running inside the editor we can fall back to loading directly from the
                // Assets/Sound folder using the AssetDatabase so designers do not need to move
                // files around while iterating.
                foreach (var extension in SupportedExtensions)
                {
                    string assetPath = Path.Combine(AssetRoot, SoundFolderName, key + extension).Replace('\\', '/');
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                    if (clip != null)
                        break;
                }
            }
#endif

            if (clip == null)
            {
                Debug.LogWarning($"Failed to locate an audio clip named '{clipName}' in {AssetRoot}/{SoundFolderName}.", this);
                return null;
            }

            clipCache[key] = clip;
            return clip;
        }
    }
}
