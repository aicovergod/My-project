using System;
using System.Collections.Generic;
using System.IO;
using Core.Save;
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
    public class SoundManager : MonoBehaviour, ISaveable
    {
        private const string SoundFolderName = "Sound";
        private const string AssetRoot = "Assets";
        private const string SfxVolumeSaveKey = "audio_sfx_volume";
        private const float MinAudibleVolume = 0.001f;

#if UNITY_EDITOR
        private static readonly string[] SupportedExtensions = { ".ogg", ".wav", ".mp3" };
#endif

        private static SoundManager instance;

        /// <summary>
        /// Event fired whenever the sound effect volume multiplier is modified. UI layers hook
        /// into this to keep sliders/toggles synchronised with the persisted preference.
        /// </summary>
        public event Action<float> SfxVolumeChanged;

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
        /// Set of sound effects that represent level up chimes. Used so that only a single
        /// level up clip is played per frame when multiple skills level simultaneously.
        /// </summary>
        private static readonly HashSet<SoundEffect> LevelUpEffects = new()
        {
            SoundEffect.AttackLevelUp,
            SoundEffect.DefenceLevelUp,
            SoundEffect.MagicLevelUp,
            SoundEffect.MiningLevelUp,
            SoundEffect.WoodcuttingLevelUp,
            SoundEffect.FishingLevelUp,
            SoundEffect.CookingLevelUp,
            SoundEffect.BeastmasterLevelUp
        };

        /// <summary>
        /// Tracks which frame most recently triggered a level up sound so the manager can
        /// suppress additional requests in the same frame. This prevents multiple level up
        /// chimes firing simultaneously when the player earns several levels at once.
        /// </summary>
        private int lastLevelUpFrame = -1;

        /// <summary>
        /// Cache of loaded audio clips keyed by file name (without extension) so we only
        /// perform the IO/asset lookup once per clip.
        /// </summary>
        private readonly Dictionary<string, AudioClip> clipCache = new();

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Global volume applied to sound effects triggered through the manager.")]
        private float sfxVolume = 1f;

        /// <summary>
        /// Global sound effect volume multiplier. Values are clamped to the [0,1] range and
        /// automatically persisted via <see cref="SaveManager"/> whenever they change.
        /// </summary>
        public float SfxVolume
        {
            get => sfxVolume;
            set => ApplySfxVolume(value, true, true);
        }

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

        private void OnEnable()
        {
            SaveManager.Register(this);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
            SaveManager.Unregister(this);
        }

        private void OnDisable()
        {
            Save();
            SaveManager.Unregister(this);
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

            if (ShouldThrottleLevelUp(effect))
                return;

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

        private bool ShouldThrottleLevelUp(SoundEffect effect)
        {
            if (!LevelUpEffects.Contains(effect))
                return false;

            int currentFrame = Time.frameCount;
            if (currentFrame == lastLevelUpFrame)
                return true;

            lastLevelUpFrame = currentFrame;
            return false;
        }

        /// <summary>
        /// UnityEvent-friendly wrapper around <see cref="SfxVolume"/> so UI widgets can bind to
        /// the setter without using reflection.
        /// </summary>
        public void SetSfxVolumeFromUI(float volume)
        {
            SfxVolume = volume;
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

        /// <inheritdoc />
        public void Save()
        {
            var data = new SfxVolumeSaveData
            {
                hasValue = true,
                volume = sfxVolume
            };
            SaveManager.Save(SfxVolumeSaveKey, data);
        }

        /// <inheritdoc />
        public void Load()
        {
            EnsureAudioSource();
            var data = SaveManager.Load<SfxVolumeSaveData>(SfxVolumeSaveKey);
            if (data.hasValue)
                ApplySfxVolume(data.volume, false, true, true);
            else
                ApplySfxVolume(sfxVolume, false, true, true);
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
            oneShotSource.volume = 1f;
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

        private void ApplySfxVolume(float value, bool persist, bool notify)
        {
            ApplySfxVolume(value, persist, notify, false);
        }

        private void ApplySfxVolume(float value, bool persist, bool notify, bool forceNotify)
        {
            float clamped = Mathf.Clamp01(value);
            if (clamped < MinAudibleVolume)
                clamped = 0f;
            float previous = sfxVolume;
            sfxVolume = clamped;

            bool changed = !Mathf.Approximately(previous, clamped);

            if (notify && (changed || forceNotify))
                SfxVolumeChanged?.Invoke(sfxVolume);

            if (persist && changed)
                Save();
        }

        [Serializable]
        private struct SfxVolumeSaveData
        {
            public bool hasValue;
            public float volume;
        }
    }
}
