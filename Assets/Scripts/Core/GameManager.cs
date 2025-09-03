using System;
using UnityEngine;
using Util;
using World;
using Inventory;
using ShopSystem;
using Player;
using Skills.Fishing;

namespace Core
{
    /// <summary>
    /// Central bootstrapper that persists across scenes and exposes global services.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        private Ticker ticker;
        private ScreenFader screenFader;
        private ItemDatabase itemDatabase;
        private ShopUI shopUI;
        private PlayerRespawnSystem respawnSystem;
        private BycatchManager bycatchManager;

        /// <summary>
        /// Event fired once all services are initialized.
        /// </summary>
        public static event Action ServicesReady;

        public static Ticker Ticker => Instance.ticker;
        public static ScreenFader ScreenFader => Instance.screenFader;
        public static ItemDatabase ItemDatabase => Instance.itemDatabase;
        public static BycatchManager BycatchManager => Instance.bycatchManager;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            ticker = FindOrCreate<Ticker>();
            screenFader = FindOrCreate<ScreenFader>();
            itemDatabase = FindOrCreate<ItemDatabase>();
            shopUI = FindOrCreate<ShopUI>();
            respawnSystem = FindOrCreate<PlayerRespawnSystem>();
            bycatchManager = FindOrCreate<BycatchManager>();

            ServicesReady?.Invoke();
        }

        /// <summary>
        /// Finds an existing service of type <typeparamref name="T"/> or creates a new one.
        /// </summary>
        private static T FindOrCreate<T>() where T : Component
        {
            var service = FindObjectOfType<T>();
            if (service == null)
            {
                var go = new GameObject(typeof(T).Name);
                service = go.AddComponent<T>();
                DontDestroyOnLoad(go);
            }
            return service;
        }
    }
}
