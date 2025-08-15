using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace World
{
    /// <summary>
    /// Pool for <see cref="PopupText"/> instances to reduce allocations.
    /// </summary>
    public class PopupTextPool : MonoBehaviour
    {
        public static PopupTextPool Instance { get; private set; }

        [SerializeField]
        private int _maxPoolSize = 20;

        [SerializeField]
        private int _warmUpCount = 0;

        private readonly Queue<PopupText> _pool = new Queue<PopupText>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            for (int i = 0; i < _warmUpCount; i++)
            {
                _pool.Enqueue(CreatePopup());
            }
        }

        private PopupText CreatePopup()
        {
            var go = new GameObject("PopupText");
            go.transform.SetParent(transform, false);
            var popup = go.AddComponent<PopupText>();
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 2f;
            go.SetActive(false);
            return popup;
        }

        /// <summary>
        /// Retrieves a popup from the pool or creates a new one if none are available.
        /// </summary>
        public PopupText Get()
        {
            return _pool.Count > 0 ? _pool.Dequeue() : CreatePopup();
        }

        /// <summary>
        /// Returns a popup to the pool. If the pool is full the popup is destroyed.
        /// </summary>
        public void Return(PopupText popup)
        {
            if (_pool.Count >= _maxPoolSize)
            {
                Destroy(popup.gameObject);
                return;
            }

            popup.gameObject.SetActive(false);
            popup.transform.SetParent(transform, false);
            _pool.Enqueue(popup);
        }
    }
}
