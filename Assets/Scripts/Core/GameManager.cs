using System;
using System.Collections;
using UnityEngine;
using Util;
using World;
using Inventory;
using ShopSystem;
using Player;
using Skills.Fishing;
using Core.Save;

namespace Core
{
    /// <summary>
    /// Central bootstrapper that persists across scenes and exposes global services.
    /// All of the referenced singletons are instantiated by <c>PersistentObjects.asset</c>,
    /// so adding a new cross-scene dependency only requires dropping its prefab into the
    /// asset's list.
    /// </summary>
    public class GameManager : ScenePersistentObject
    {
        public static GameManager Instance { get; private set; }

        private Ticker ticker;
        private ScreenFader screenFader;
        private ItemDatabase itemDatabase;
        private ShopUI shopUI;
        private PlayerRespawnSystem respawnSystem;
        private BycatchManager bycatchManager;
        private Coroutine autosaveRoutine;

        [Header("Window Configuration")]
        [SerializeField]
        [Tooltip("OSRS-style fixed window dimensions. Defaults to the 1024x768 client layout.")]
        private Vector2 osrsWindowedResolution = new Vector2(1024f, 768f);

        [SerializeField]
        [Tooltip("When enabled the editor will also enforce the fixed window size during Play Mode.")]
        private bool applyResolutionInEditorPlayMode = false;

        private const float AutosaveInterval = 10f;

        /// <summary>
        /// Event fired once all services are initialized.
        /// </summary>
        public static event Action ServicesReady;

        public static Ticker Ticker => Instance.ticker;
        public static ScreenFader ScreenFader => Instance.screenFader;
        public static ItemDatabase ItemDatabase => Instance.itemDatabase;
        public static BycatchManager BycatchManager => Instance.bycatchManager;

        protected override void Awake()
        {
            ConfigureWindowMode();

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            base.Awake();

            Instance = this;

            CacheServices(false);
        }

        /// <summary>
        /// Forces the game to start in a fixed-size window that mirrors OSRS' 4:3 aspect ratio.
        /// Ensures the runtime resolution is deterministic so UI layouts align perfectly.
        /// </summary>
        private void ConfigureWindowMode()
        {
            // Clamp to sane minimums to avoid invalid resolution requests.
            var width = Mathf.Max(1, Mathf.RoundToInt(osrsWindowedResolution.x));
            var height = Mathf.Max(1, Mathf.RoundToInt(osrsWindowedResolution.y));

            Screen.fullScreenMode = FullScreenMode.Windowed;

            if (!Application.isEditor || applyResolutionInEditorPlayMode)
            {
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
            }
        }

        private void Start()
        {
            CacheServices(true);

            ServicesReady?.Invoke();

            if (autosaveRoutine == null)
                autosaveRoutine = StartCoroutine(AutoSaveLoop());
        }

        private IEnumerator AutoSaveLoop()
        {
            // Use the realtime variant so autosaves still fire when the game is paused via Time.timeScale.
            var wait = new WaitForSecondsRealtime(AutosaveInterval);
            while (true)
            {
                yield return wait;
                SaveManager.SaveAll();
            }
        }

        private void OnDestroy()
        {
            if (autosaveRoutine != null)
                StopCoroutine(autosaveRoutine);
        }

        /// <summary>
        /// Locates required cross-scene services that should have been spawned by
        /// <see cref="World.PersistentObjectBootstrap"/>.
        /// </summary>
        /// <param name="logIfMissing">When <c>true</c>, logs guidance if a service could not be found.</param>
        private void CacheServices(bool logIfMissing)
        {
            ticker ??= FindService<Ticker>(logIfMissing);
            screenFader ??= FindService<ScreenFader>(logIfMissing);
            itemDatabase ??= FindService<ItemDatabase>(logIfMissing);
            shopUI ??= FindService<ShopUI>(logIfMissing);
            respawnSystem ??= FindService<PlayerRespawnSystem>(logIfMissing);
            bycatchManager ??= FindService<BycatchManager>(logIfMissing);
        }

        /// <summary>
        /// Searches the scene hierarchy (including inactive objects) for the requested service.
        /// When the service is missing, the log message explains that it should be added to
        /// <c>PersistentObjects.asset</c> so future scenes include it automatically.
        /// </summary>
        private T FindService<T>(bool logIfMissing) where T : Component
        {
            var service = FindObjectOfType<T>(true);
            if (service == null && logIfMissing)
            {
                Debug.LogError($"Required service of type {typeof(T).Name} was not found. Add its prefab to Resources/{World.PersistentObjectBootstrap.CatalogResourcePath}.asset to have it loaded automatically.", this);
            }
            return service;
        }
    }
}
